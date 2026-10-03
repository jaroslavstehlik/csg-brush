using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Floor plan edit mode, the plan's counterpart of <see cref="BrushEditContext"/>: while it is active, Unity's Move, Rotate and
    /// Scale tools act on the selected points or walls of the selected plans, on their floor. Selection (click, Shift add,
    /// Ctrl remove, marquee) works as in brush edit mode, with Vertex, Edge (wall) and Room modes (1, 2, 3).
    /// </summary>
    [EditorToolContext("Edit Floor Plan", typeof(FloorPlan))]
    [Icon(BrushIcons.Folder + "FloorPlan.png")]
    public sealed class FloorPlanEditContext : EditorToolContext
    {
        public static bool IsActive => ToolManager.activeContextType == typeof(FloorPlanEditContext);

        /// <summary>Start editing the selected plans (nothing happens without one: Unity refuses the context).</summary>
        public static void Enter()
        {
            if (IsActive) return;
            foreach (var go in Selection.gameObjects) if (go.GetComponent<FloorPlan>() != null) { ToolManager.SetActiveContext<FloorPlanEditContext>(); return; }
        }
        public static void Exit() { if (IsActive) ToolManager.SetActiveContext<GameObjectToolContext>(); }

        protected override Type GetEditorToolType(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return typeof(FloorPlanMoveTool);
                case Tool.Rotate: return typeof(FloorPlanRotateTool);
                case Tool.Scale: return typeof(FloorPlanScaleTool);
                default: return base.GetEditorToolType(tool);
            }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView)) return;
            FloorPlanEditState.OnSceneGUI();
        }
    }

    /// <summary>Selection, drawing, picking and drags of floor plan edit mode. The selection rules are plain functions of screen points, for tests.</summary>
    public static class FloorPlanEditState
    {
        static BrushEditMode s_Mode = BrushEditMode.Vertex;
        public static event Action Changed;

        /// <summary>Vertex (points), Edge (walls, by index in <see cref="FloorPlan.walls"/>) or Face (rooms, by index in <see cref="FloorPlan.Rooms"/>).</summary>
        public static BrushEditMode Mode
        {
            get => s_Mode;
            set { if (s_Mode == value) return; s_Mode = value; Changed?.Invoke(); SceneView.RepaintAll(); }
        }

        public sealed class Selection
        {
            public HashSet<int> vertices = new HashSet<int>(), edges = new HashSet<int>(), rooms = new HashSet<int>();
            public void Clear() { vertices.Clear(); edges.Clear(); rooms.Clear(); }
        }
        static readonly Dictionary<FloorPlan, Selection> s_Selection = new Dictionary<FloorPlan, Selection>();

        /// <summary>A plan's selection, without indices the plan no longer has (after an undo, say).</summary>
        public static Selection Sel(FloorPlan plan)
        {
            if (!s_Selection.TryGetValue(plan, out var s)) { s = new Selection(); s_Selection[plan] = s; }
            int n = plan.points.Count, walls = plan.walls.Count, rooms = RoomsOf(plan).Count;
            s.vertices.RemoveWhere(i => i >= n); s.edges.RemoveWhere(i => i >= walls); s.rooms.RemoveWhere(i => i >= rooms);
            return s;
        }

        static readonly List<FloorPlan.Room> s_Rooms = new List<FloorPlan.Room>();

        /// <summary>The plan's rooms (shared list: copy it to keep it).</summary>
        public static List<FloorPlan.Room> RoomsOf(FloorPlan plan) { plan.Rooms(s_Rooms); return s_Rooms; }

        /// <summary>Each wall's two point indices, in the order of <see cref="FloorPlan.walls"/>.</summary>
        public static (int a, int b)[] WallEnds(FloorPlan plan)
        {
            plan.EnsureGraph();
            var ends = new (int, int)[plan.walls.Count];
            for (int w = 0; w < ends.Length; w++) { plan.WallPoints(w, out int a, out int b); ends[w] = (a, b); }
            return ends;
        }

        public static void ClearSelection() { s_Selection.Clear(); SceneView.RepaintAll(); }


        public static IEnumerable<FloorPlan> SelectedPlans()
        {
            foreach (var go in UnityEditor.Selection.gameObjects)
                if (go.TryGetComponent<FloorPlan>(out var plan)) yield return plan;
        }

        // ------------------------------------------------------------------ selection rules

        /// <summary>A click: the nearest point (within 12 px) or wall (within 10 px) to the mouse, added or removed.</summary>
        public static void SelectNearest(Selection sel, IList<Vector2> screen, IList<(int a, int b)> walls, Vector2 mouse, BrushEditMode mode, bool remove)
        {
            if (mode == BrushEditMode.Edge)
            {
                int best = -1; float bestD = 10f;
                for (int w = 0; w < walls.Count; w++) { float d = DistanceToSegment(mouse, screen[walls[w].a], screen[walls[w].b]); if (d < bestD) { bestD = d; best = w; } }
                if (best >= 0) Apply(sel.edges, best, remove);
            }
            else if (mode == BrushEditMode.Vertex)
            {
                int best = -1; float bestD = 12f;
                for (int v = 0; v < screen.Count; v++) { float d = (screen[v] - mouse).magnitude; if (d < bestD) { bestD = d; best = v; } }
                if (best >= 0) Apply(sel.vertices, best, remove);
            }
        }

        /// <summary>A marquee: points inside it; walls completely inside it, or touching it when <paramref name="complete"/> is off.</summary>
        public static void SelectInRect(Selection sel, IList<Vector2> screen, IList<(int a, int b)> walls, Rect rect, BrushEditMode mode, bool complete, bool remove)
        {
            if (mode == BrushEditMode.Edge)
            {
                for (int w = 0; w < walls.Count; w++)
                {
                    var a = screen[walls[w].a]; var b = screen[walls[w].b];
                    if (complete ? rect.Contains(a) && rect.Contains(b) : BrushEditState.SegmentTouchesRect(a, b, rect)) Apply(sel.edges, w, remove);
                }
            }
            else if (mode == BrushEditMode.Vertex) for (int v = 0; v < screen.Count; v++) if (rect.Contains(screen[v])) Apply(sel.vertices, v, remove);
        }

        /// <summary>A click on the floor: the room it lands in (a point on the plan's floor, in its space), added or removed.</summary>
        public static void SelectRoom(Selection sel, IList<FloorPlan.Room> rooms, Vector2 local, bool remove)
        {
            for (int r = 0; r < rooms.Count; r++) if (FloorPlan.Contains(rooms[r].polygon, local)) { Apply(sel.rooms, r, remove); return; }
        }

        /// <summary>A marquee in Room mode: rooms whose corners are all inside it, or any of them when <paramref name="complete"/> is off.</summary>
        public static void SelectRoomsInRect(Selection sel, IList<Vector2[]> roomScreens, Rect rect, bool complete, bool remove)
        {
            for (int r = 0; r < roomScreens.Count; r++)
            {
                int inside = 0; foreach (var p in roomScreens[r]) if (rect.Contains(p)) inside++;
                if (complete ? inside == roomScreens[r].Length : inside > 0) Apply(sel.rooms, r, remove);
            }
        }

        static void Apply(HashSet<int> set, int item, bool remove) { if (remove) set.Remove(item); else set.Add(item); }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = ab.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (a + ab * t - p).magnitude;
        }

        public static void SelectAll(FloorPlan plan)
        {
            var sel = Sel(plan);
            for (int v = 0; v < plan.points.Count; v++) sel.vertices.Add(v);
            for (int w = 0; w < plan.walls.Count; w++) sel.edges.Add(w);
            for (int r = 0; r < RoomsOf(plan).Count; r++) sel.rooms.Add(r);
        }

        public static void Invert(FloorPlan plan)
        {
            var sel = Sel(plan);
            var set = Mode == BrushEditMode.Edge ? sel.edges : Mode == BrushEditMode.Face ? sel.rooms : sel.vertices;
            int count = Mode == BrushEditMode.Edge ? plan.walls.Count : Mode == BrushEditMode.Face ? RoomsOf(plan).Count : plan.points.Count;
            for (int i = 0; i < count; i++) if (!set.Remove(i)) set.Add(i);
        }

        // ------------------------------------------------------------------ transforming the selection

        /// <summary>The points a transform moves: the selected points, both ends of every selected wall, or every point around a selected room.</summary>
        public static HashSet<int> MovingPoints(FloorPlan plan, Selection sel, BrushEditMode mode)
        {
            if (mode == BrushEditMode.Vertex) return new HashSet<int>(sel.vertices);
            var set = new HashSet<int>();
            if (mode == BrushEditMode.Edge)
            {
                foreach (var w in sel.edges) { if (w >= plan.walls.Count) continue; plan.WallPoints(w, out int a, out int b); set.Add(a); set.Add(b); }
                return set;
            }
            var rooms = RoomsOf(plan);
            foreach (var r in sel.rooms) if (r < rooms.Count) foreach (var id in rooms[r].boundary) { int i = plan.PointIndex(id); if (i >= 0) set.Add(i); }
            return set;
        }

        public static Vector3 Centre(IList<Vector3> points, HashSet<int> moving)
        {
            var c = Vector3.zero; foreach (var i in moving) c += points[i];
            return moving.Count > 0 ? c / moving.Count : Vector3.zero;
        }

        /// <summary>
        /// The moving points of the plan as they were at <paramref name="start"/>, transformed on the plan's floor (in its
        /// local space) and rounded to the grid when snapping is on. Undoable; the walls follow.
        /// </summary>
        public static void TransformPoints(FloorPlan plan, IList<Vector3> start, HashSet<int> moving, Func<Vector3, Vector3> local)
        {
            var s = BrushSettings.instance; float g = s.snapToGrid ? s.GridMeters : 0f;
            bool recorded = false;
            foreach (var i in moving)
            {
                if (i >= start.Count || i >= plan.points.Count) continue;
                var p = local(start[i]); p.y = 0f;
                if (g > 0f) { p.x = Mathf.Round(p.x / g) * g; p.z = Mathf.Round(p.z / g) * g; }
                if (p == plan.points[i]) continue;
                if (!recorded) { Undo.RecordObject(plan, "Edit floor plan"); recorded = true; }
                plan.points[i] = p;
            }
            if (recorded) BrushGenerators.MarkDirty(plan);
        }

        /// <summary>Gizmo yaw on the plan's floor, in degrees about its up axis: world X, the plan's X, or along the selected wall.</summary>
        public static float HandleYaw(FloorPlan plan, Selection sel, BrushHandleOrientation orientation, BrushEditMode mode)
        {
            Vector3 d;
            switch (orientation)
            {
                case BrushHandleOrientation.Local: return 0f;
                case BrushHandleOrientation.Element:
                {
                    int s = -1;
                    if (mode == BrushEditMode.Edge) { foreach (var e in sel.edges) if (s < 0 || e < s) s = e; }
                    else if (mode == BrushEditMode.Vertex)
                    {
                        int v = -1; foreach (var i in sel.vertices) if (v < 0 || i < v) v = i;
                        if (v >= 0) { int id = plan.pointIds[v]; for (int w = 0; w < plan.walls.Count && s < 0; w++) if (plan.walls[w].start == id || plan.walls[w].end == id) s = w; }
                    }
                    if (s < 0 || s >= plan.walls.Count) return 0f;
                    plan.WallPoints(s, out int ia, out int ib);
                    d = plan.points[ib] - plan.points[ia];
                    break;
                }
                default: d = plan.transform.InverseTransformDirection(Vector3.right); break;
            }
            d.y = 0f;
            return d.sqrMagnitude < 1e-8f ? 0f : -Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        }

        public static Quaternion HandleRotation(FloorPlan plan, Selection sel) => plan.transform.rotation * Quaternion.Euler(0f, HandleYaw(plan, sel, BrushEditState.Orientation, Mode), 0f);

        // drag state shared by the tools: the plan's points when the drag began
        public static FloorPlan dragPlan; public static List<Vector3> dragStart; public static HashSet<int> dragMoving; public static Vector3 dragCentre;
        public static bool Dragging(FloorPlan plan) => dragPlan == plan && dragStart != null;

        /// <summary>Start a drag of the plan's moving points; only while a handle holds the mouse, so a stray change never starts one.</summary>
        public static void BeginDrag(FloorPlan plan, HashSet<int> moving)
        {
            if (Dragging(plan) || GUIUtility.hotControl == 0) return;
            dragPlan = plan; dragStart = new List<Vector3>(plan.points); dragMoving = moving; dragCentre = Centre(plan.points, moving);
        }

        public static void EndDrag() { dragPlan = null; dragStart = null; dragMoving = null; }

        // ------------------------------------------------------------------ scene view

        static bool marquee; static Vector2 marqueeStart;

        public static void OnSceneGUI()
        {
            var e = Event.current;
            if (dragStart != null && GUIUtility.hotControl == 0) EndDrag(); // the handle let go, however the release went
            HandleKeys(e);
            bool viewTool = Tools.current == Tool.View || Tools.viewToolActive;
            int passive = GUIUtility.GetControlID(FocusType.Passive);
            if (!viewTool) HandleUtility.AddDefaultControl(passive);
            foreach (var plan in SelectedPlans())
            {
                var sel = Sel(plan);
                Draw(plan, sel);
                if (!viewTool) { PointControls(plan, sel, e); HandlePicking(plan, e, passive); }
            }
            DrawMarquee();
            if (e.rawType == EventType.MouseUp) EndDrag();
        }

        static Vector2[] Screen(FloorPlan plan)
        {
            var pts = new Vector2[plan.points.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = HandleUtility.WorldToGUIPoint(FloorPlanTools.World(plan, plan.points[i]));
            return pts;
        }

        static void HandleKeys(Event e)
        {
            // Delete removes the selected points or walls instead of the plan
            if ((e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) && (e.commandName == "Delete" || e.commandName == "SoftDelete") && CanDelete())
            {
                if (e.type == EventType.ExecuteCommand) DeleteSelection();
                e.Use(); return;
            }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && !e.control && !e.command && !e.alt && CanDelete())
            {
                DeleteSelection(); e.Use(); return;
            }
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Alpha1) { Mode = BrushEditMode.Vertex; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha2) { Mode = BrushEditMode.Edge; e.Use(); }
            else if (e.keyCode == KeyCode.Alpha3) { Mode = BrushEditMode.Face; e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { ClearSelection(); e.Use(); }
            else if (e.keyCode == KeyCode.A && (e.control || e.command)) { foreach (var p in SelectedPlans()) SelectAll(p); e.Use(); SceneView.RepaintAll(); }
            else if (e.keyCode == KeyCode.I && (e.control || e.command)) { foreach (var p in SelectedPlans()) Invert(p); e.Use(); SceneView.RepaintAll(); }
        }

        static bool CanDelete()
        {
            foreach (var p in SelectedPlans()) { var sel = Sel(p); if (Mode == BrushEditMode.Edge ? sel.edges.Count > 0 : Mode == BrushEditMode.Vertex && sel.vertices.Count > 0) return true; }
            return false;
        }

        /// <summary>Delete the selected points (Vertex mode) or walls (Edge mode).</summary>
        public static void DeleteSelection()
        {
            if (Mode == BrushEditMode.Vertex) { DeleteSelectedPoints(); return; }
            if (Mode != BrushEditMode.Edge) return;
            foreach (var plan in new List<FloorPlan>(SelectedPlans()))
            {
                var sel = Sel(plan);
                if (sel.edges.Count == 0) continue;
                if (DeleteWalls(plan, sel.edges)) sel.Clear();
            }
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Remove walls from a plan, and the points no other wall uses. A room opens where one of its walls goes; doors and
        /// windows on a removed wall stay where they are. Refused when no wall would be left. Undoable.
        /// </summary>
        public static bool DeleteWalls(FloorPlan plan, ICollection<int> walls)
        {
            plan.EnsureGraph();
            if (walls.Count >= plan.walls.Count) { bool all = true; for (int w = 0; w < plan.walls.Count; w++) if (!walls.Contains(w)) all = false; if (all) return false; }
            Undo.RecordObject(plan, "Delete floor plan walls");
            var keep = new List<FloorPlan.WallLink>();
            for (int w = 0; w < plan.walls.Count; w++) if (!walls.Contains(w)) keep.Add(plan.walls[w]);
            plan.walls = keep;
            plan.RemoveLonePoints();
            BrushGenerators.MarkDirty(plan);
            return true;
        }

        /// <summary>
        /// Remove points from a plan. A point between exactly two walls joins them into one; any other point takes its walls
        /// with it. Refused when no wall would be left. Undoable.
        /// </summary>
        public static bool DeletePoints(FloorPlan plan, ICollection<int> pointIndices)
        {
            plan.EnsureGraph();
            var ids = new List<int>(); foreach (var i in pointIndices) if (i < plan.points.Count) ids.Add(plan.pointIds[i]);
            var walls = new List<FloorPlan.WallLink>(plan.walls);
            foreach (var id in ids)
            {
                var touching = walls.FindAll(w => w.start == id || w.end == id);
                walls.RemoveAll(w => w.start == id || w.end == id);
                if (touching.Count != 2) continue;
                var w0 = touching[0]; var w1 = touching[1];
                int a = w0.end == id ? w0.start : w0.end, b = w1.start == id ? w1.end : w1.start; // a to b, the way the first wall ran
                if (w0.start == id) { int t = a; a = b; b = t; }
                if (a == b || walls.Exists(w => (w.start == a && w.end == b) || (w.start == b && w.end == a))) continue;
                walls.Add(new FloorPlan.WallLink(a, b, w0.thickness));
            }
            if (walls.Count == 0) return false;
            Undo.RecordObject(plan, "Remove floor plan points");
            plan.walls = walls;
            plan.RemoveLonePoints();
            BrushGenerators.MarkDirty(plan);
            return true;
        }

        /// <summary>Remove the selected points of the selected plans.</summary>
        public static void DeleteSelectedPoints()
        {
            foreach (var plan in SelectedPlans())
            {
                var sel = Sel(plan);
                if (sel.vertices.Count == 0) continue;
                if (DeletePoints(plan, sel.vertices)) sel.Clear();
            }
            SceneView.RepaintAll();
        }

        static readonly Color RoomColor = new Color(1f, 0.85f, 0.2f, 0.18f), RoomOutlineColor = new Color(1f, 1f, 1f, 0.35f);

        static void Draw(FloorPlan plan, Selection sel)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            if (Mode == BrushEditMode.Face)
            {
                var rooms = RoomsOf(plan);
                for (int r = 0; r < rooms.Count; r++)
                {
                    var poly = rooms[r].polygon; var world = new Vector3[poly.Length];
                    for (int i = 0; i < poly.Length; i++) world[i] = FloorPlanTools.World(plan, new Vector3(poly[i].x, 0f, poly[i].y));
                    if (sel.rooms.Contains(r)) { Handles.color = RoomColor; foreach (var tri in Triangles(poly)) Handles.DrawAAConvexPolygon(world[tri.x], world[tri.y], world[tri.z]); }
                    Handles.color = sel.rooms.Contains(r) ? BrushEditState.SelectedColor : RoomOutlineColor;
                    var loop = new Vector3[world.Length + 1]; world.CopyTo(loop, 0); loop[world.Length] = world[0];
                    Handles.DrawAAPolyLine(sel.rooms.Contains(r) ? 4f : 2f, loop);
                }
            }
            var ends = WallEnds(plan);
            for (int w = 0; w < ends.Length; w++)
            {
                bool selected = Mode == BrushEditMode.Edge && sel.edges.Contains(w);
                Handles.color = selected ? BrushEditState.SelectedColor : new Color(1f, 1f, 1f, 0.8f);
                Handles.DrawAAPolyLine(selected ? 5f : 3f, FloorPlanTools.World(plan, plan.points[ends[w].a]), FloorPlanTools.World(plan, plan.points[ends[w].b]));
            }
            if (Mode != BrushEditMode.Vertex) return;
            for (int v = 0; v < plan.points.Count; v++)
            {
                var w = FloorPlanTools.World(plan, plan.points[v]);
                Handles.color = sel.vertices.Contains(v) ? BrushEditState.SelectedColor : Color.white;
                Handles.DotHandleCap(0, w, Quaternion.identity, HandleUtility.GetHandleSize(w) * 0.045f, EventType.Repaint);
            }
        }

        /// <summary>A simple polygon (counter-clockwise) cut into triangles by ear clipping, for filling a room of any shape.</summary>
        public static List<Vector3Int> Triangles(Vector2[] poly)
        {
            var result = new List<Vector3Int>();
            var idx = new List<int>(); for (int i = 0; i < poly.Length; i++) idx.Add(i);
            float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float area = 0f; for (int i = 0; i < poly.Length; i++) area += Cross(Vector2.zero, poly[i], poly[(i + 1) % poly.Length]);
            float sign = area >= 0f ? 1f : -1f;
            for (int guard = 0; idx.Count > 3 && guard < poly.Length * poly.Length; guard++)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count && !clipped; k++)
                {
                    int i0 = idx[(k - 1 + idx.Count) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    if (Cross(poly[i0], poly[i1], poly[i2]) * sign <= 0f) continue; // reflex
                    bool empty = true;
                    foreach (var o in idx)
                    {
                        if (o == i0 || o == i1 || o == i2) continue;
                        var q = poly[o];
                        if (Cross(poly[i0], poly[i1], q) * sign > 0f && Cross(poly[i1], poly[i2], q) * sign > 0f && Cross(poly[i2], poly[i0], q) * sign > 0f) { empty = false; break; }
                    }
                    if (!empty) continue;
                    result.Add(new Vector3Int(i0, i1, i2)); idx.RemoveAt(k); clipped = true;
                }
                if (!clipped) break; // not simple: what is left stays unfilled
            }
            if (idx.Count == 3) result.Add(new Vector3Int(idx[0], idx[1], idx[2]));
            return result;
        }

        const float PointPickPixels = 8f;
        static readonly int s_PointHash = "FloorPlanPoint".GetHashCode(), s_AddHash = "FloorPlanAddPoint".GetHashCode();

        /// <summary>
        /// Vertex mode: a click on a point selects it (Shift adds, Ctrl removes), and a + in the middle of every wall adds a
        /// point there. Both come before the gizmo under the mouse, whose axes often lie along the walls; a selected point
        /// does not, the gizmo sits on it.
        /// </summary>
        static int s_PressedPoint = -1; static Vector3 s_Grab; static bool s_Moved;
        static readonly int s_DragHash = "FloorPlanPointDrag".GetHashCode();
        static readonly Color AddColor = new Color(0.55f, 0.85f, 1f, 1f); // light blue: not a point (white), not selected (yellow)

        /// <summary>
        /// Vertex mode: pressing a point selects it and drags it (a point of the selection drags the selection); pressing a +
        /// adds a point there and drags it. Both come before the gizmo under the mouse.
        /// </summary>
        static void PointControls(FloorPlan plan, Selection sel, Event e)
        {
            // one control holds the mouse for every point drag; made first, so a point added mid-press keeps its id
            int dragId = GUIUtility.GetControlID(s_DragHash, FocusType.Passive);
            DragControl(plan, sel, e, dragId);
            if (Mode != BrushEditMode.Vertex) return;
            int n = plan.points.Count;
            var ends = WallEnds(plan);
            for (int v = 0; v < n; v++)
            {
                int id = GUIUtility.GetControlID(s_PointHash, FocusType.Passive);
                var world = FloorPlanTools.World(plan, plan.points[v]);
                if (e.GetTypeForControl(id) == EventType.Layout && HandleUtility.DistanceToCircle(world, 0f) < PointPickPixels) HandleUtility.AddControl(id, -1f);
                if (e.GetTypeForControl(id) != EventType.MouseDown || e.button != 0 || e.alt || HandleUtility.nearestControl != id || Dragging(plan)) continue;
                e.Use(); SceneView.RepaintAll();
                if (e.control || e.command) { sel.vertices.Remove(v); continue; } // Ctrl: deselect only
                if (!sel.vertices.Contains(v))
                {
                    if (!e.shift) foreach (var p in SelectedPlans()) Sel(p).Clear();
                    sel.vertices.Add(v);
                }
                StartPointDrag(plan, sel, e, dragId, v);
            }
            if (Dragging(plan)) return; // no + while points move
            for (int w = 0; w < ends.Length; w++)
            {
                int id = GUIUtility.GetControlID(s_AddHash, FocusType.Passive);
                var mid = (FloorPlanTools.World(plan, plan.points[ends[w].a]) + FloorPlanTools.World(plan, plan.points[ends[w].b])) * 0.5f;
                switch (e.GetTypeForControl(id))
                {
                    case EventType.Layout:
                        if (HandleUtility.DistanceToCircle(mid, 0f) < PointPickPixels) HandleUtility.AddControl(id, -1f);
                        break;
                    case EventType.MouseDown:
                        if (e.button == 0 && !e.alt && HandleUtility.nearestControl == id)
                        {
                            Undo.RecordObject(plan, "Add floor plan point");
                            plan.SplitWall(w, FloorPlanTools.SnapLocal(plan, mid));
                            int added = plan.points.Count - 1;
                            sel.Clear(); sel.vertices.Add(added);
                            BrushGenerators.MarkDirty(plan);
                            e.Use(); SceneView.RepaintAll();
                            StartPointDrag(plan, sel, e, dragId, added); // keep holding: the new point follows the mouse
                            return; // the points changed: the rest waits for the next event
                        }
                        break;
                    case EventType.Repaint:
                    {
                        // a small plus, grey (white under the mouse): unlike the square points
                        Handles.color = HandleUtility.nearestControl == id && GUIUtility.hotControl == 0 ? Color.white : AddColor;
                        float r = HandleUtility.GetHandleSize(mid) * 0.05f;
                        var cam = Camera.current != null ? Camera.current.transform : null;
                        Vector3 right = cam != null ? cam.right : Vector3.right, up = cam != null ? cam.up : Vector3.forward;
                        Handles.DrawAAPolyLine(4f, mid - right * r, mid + right * r);
                        Handles.DrawAAPolyLine(4f, mid - up * r, mid + up * r);
                        break;
                    }
                }
            }
        }

        static void StartPointDrag(FloorPlan plan, Selection sel, Event e, int dragId, int pressed)
        {
            if (!FloorPlanTools.MouseOnFloor(plan, e.mousePosition, out s_Grab)) return;
            GUIUtility.hotControl = dragId;
            s_PressedPoint = pressed; s_Moved = false;
            BeginDrag(plan, new HashSet<int>(sel.vertices));
        }

        static void DragControl(FloorPlan plan, Selection sel, Event e, int dragId)
        {
            switch (e.GetTypeForControl(dragId))
            {
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == dragId && Dragging(plan) && FloorPlanTools.MouseOnFloor(plan, e.mousePosition, out var now))
                    {
                        var local = plan.transform.InverseTransformVector(now - s_Grab); local.y = 0f;
                        TransformPoints(plan, dragStart, dragMoving, q => q + local);
                        s_Moved = true;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != dragId) break;
                    GUIUtility.hotControl = 0; e.Use();
                    // a click (no drag) on a point of a larger selection selects just that point
                    if (!s_Moved && !e.shift && s_PressedPoint >= 0 && sel.vertices.Count > 1) { sel.Clear(); sel.vertices.Add(s_PressedPoint); }
                    s_PressedPoint = -1;
                    EndDrag();
                    break;
            }
        }

        static void HandlePicking(FloorPlan plan, Event e, int passive)
        {
            if (dragStart != null || e.alt) return;
            if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != passive) return; // a gizmo owns the mouse
            if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == passive)
            {
                marquee = true; marqueeStart = e.mousePosition;
                GUIUtility.hotControl = passive; e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 1 && GUIUtility.hotControl == 0)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Select All"), false, () => { foreach (var p in SelectedPlans()) SelectAll(p); SceneView.RepaintAll(); });
                menu.AddItem(new GUIContent("Invert Selection"), false, () => { foreach (var p in SelectedPlans()) Invert(p); SceneView.RepaintAll(); });
                menu.AddItem(new GUIContent("Clear Selection"), false, ClearSelection);
                if (CanDelete()) menu.AddItem(new GUIContent("Delete  ⌫"), false, DeleteSelection); else menu.AddDisabledItem(new GUIContent("Delete  ⌫"));
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Vertex mode  1"), Mode == BrushEditMode.Vertex, () => Mode = BrushEditMode.Vertex);
                menu.AddItem(new GUIContent("Wall mode  2"), Mode == BrushEditMode.Edge, () => Mode = BrushEditMode.Edge);
                menu.AddItem(new GUIContent("Room mode  3"), Mode == BrushEditMode.Face, () => Mode = BrushEditMode.Face);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Stop Editing"), false, FloorPlanEditContext.Exit);
                menu.ShowAsContext();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && marquee && GUIUtility.hotControl == passive) { e.Use(); SceneView.RepaintAll(); }
            else if (e.type == EventType.MouseUp && marquee && GUIUtility.hotControl == passive)
            {
                var rect = MarqueeRect(e.mousePosition);
                bool click = rect.width < 4f && rect.height < 4f;
                bool remove = e.control || e.command;
                foreach (var p in SelectedPlans())
                {
                    var sel = Sel(p);
                    if (!e.shift && !remove) sel.Clear();
                    if (Mode == BrushEditMode.Face)
                    {
                        var rooms = RoomsOf(p);
                        if (click) { if (FloorPlanTools.MouseOnFloor(p, e.mousePosition, out var floor)) { var l = p.transform.InverseTransformPoint(floor); SelectRoom(sel, rooms, new Vector2(l.x, l.z), remove); } }
                        else
                        {
                            var screens = new List<Vector2[]>();
                            foreach (var r in rooms) { var sp = new Vector2[r.polygon.Length]; for (int i = 0; i < sp.Length; i++) sp[i] = HandleUtility.WorldToGUIPoint(FloorPlanTools.World(p, new Vector3(r.polygon[i].x, 0f, r.polygon[i].y))); screens.Add(sp); }
                            SelectRoomsInRect(sel, screens, rect, BrushEditState.RectComplete, remove);
                        }
                    }
                    else if (click) SelectNearest(sel, Screen(p), WallEnds(p), e.mousePosition, Mode, remove);
                    else SelectInRect(sel, Screen(p), WallEnds(p), rect, Mode, BrushEditState.RectComplete, remove);
                }
                marquee = false; GUIUtility.hotControl = 0; e.Use(); SceneView.RepaintAll();
            }
        }

        static Rect MarqueeRect(Vector2 current) => Rect.MinMaxRect(Mathf.Min(marqueeStart.x, current.x), Mathf.Min(marqueeStart.y, current.y), Mathf.Max(marqueeStart.x, current.x), Mathf.Max(marqueeStart.y, current.y));

        static void DrawMarquee()
        {
            if (!marquee || Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            var r = MarqueeRect(Event.current.mousePosition);
            GUI.color = new Color(1f, 0.85f, 0.2f, 0.15f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.85f, 0.2f, 0.9f); GUI.Box(r, GUIContent.none, EditorStyles.selectionRect);
            GUI.color = Color.white;
            Handles.EndGUI();
        }
    }

    /// <summary>The three tools of floor plan edit mode share its Tool Settings toolbar.</summary>
    public abstract class FloorPlanSelectionTool : EditorTool
    {
        /// <summary>For each selected plan with something selected: the points it moves and the gizmo's place and axes.</summary>
        protected static IEnumerable<(FloorPlan plan, HashSet<int> moving, Vector3 centre, Quaternion frame)> Targets()
        {
            foreach (var plan in FloorPlanEditState.SelectedPlans())
            {
                var sel = FloorPlanEditState.Sel(plan);
                var moving = FloorPlanEditState.MovingPoints(plan, sel, FloorPlanEditState.Mode);
                if (moving.Count == 0) continue;
                var centre = FloorPlanEditState.Dragging(plan) ? FloorPlanEditState.dragCentre : FloorPlanEditState.Centre(plan.points, moving);
                yield return (plan, moving, FloorPlanTools.World(plan, centre), FloorPlanEditState.HandleRotation(plan, sel));
            }
        }
    }

    /// <summary>Move in floor plan edit mode: along the gizmo's two floor axes or freely on the floor; points land on the grid.</summary>
    [EditorTool("Move floor plan selection", typeof(FloorPlan), typeof(FloorPlanEditContext))]
    public sealed class FloorPlanMoveTool : FloorPlanSelectionTool
    {
        Vector3 offset; Quaternion frame = Quaternion.identity;

        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var (plan, moving, centre, rotation) in Targets())
            {
                bool dragging = FloorPlanEditState.Dragging(plan);
                if (!dragging) { frame = rotation; offset = Vector3.zero; }
                var at = centre + offset;
                float size = HandleUtility.GetHandleSize(at);
                Vector3 x = frame * Vector3.right, z = frame * Vector3.forward, up = plan.transform.up;
                EditorGUI.BeginChangeCheck();
                Handles.color = Handles.xAxisColor; var p = Handles.Slider(at, x, size, Handles.ArrowHandleCap, 0f);
                Handles.color = Handles.zAxisColor; p = Handles.Slider(p, z, size, Handles.ArrowHandleCap, 0f);
                Handles.color = Handles.yAxisColor; p = Handles.Slider2D(p, up, x, z, size * 0.12f, Handles.RectangleHandleCap, Vector2.zero);
                if (EditorGUI.EndChangeCheck())
                {
                    FloorPlanEditState.BeginDrag(plan, moving);
                    if (!FloorPlanEditState.Dragging(plan)) continue;
                    offset += p - at;
                    var local = plan.transform.InverseTransformVector(offset); local.y = 0f;
                    FloorPlanEditState.TransformPoints(plan, FloorPlanEditState.dragStart, FloorPlanEditState.dragMoving, q => q + local);
                }
            }
        }
    }

    /// <summary>Rotate in floor plan edit mode: drag the ring on the floor; the selection turns about its centre in rotation-snap steps.</summary>
    [EditorTool("Rotate floor plan selection", typeof(FloorPlan), typeof(FloorPlanEditContext))]
    public sealed class FloorPlanRotateTool : FloorPlanSelectionTool
    {
        Vector3 grab; // where the drag began on the floor, from the centre

        public override void OnToolGUI(EditorWindow window)
        {
            var e = Event.current;
            foreach (var (plan, moving, centre, _) in Targets())
            {
                var up = plan.transform.up;
                float radius = HandleUtility.GetHandleSize(centre);
                int id = GUIUtility.GetControlID(FocusType.Passive);
                switch (e.GetTypeForControl(id))
                {
                    case EventType.Layout:
                    {
                        var ring = new Vector3[49]; var side = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
                        for (int i = 0; i < ring.Length; i++) ring[i] = centre + Quaternion.AngleAxis(i * 7.5f, up) * side * radius;
                        HandleUtility.AddControl(id, HandleUtility.DistanceToPolyLine(ring));
                        break;
                    }
                    case EventType.MouseDown:
                        if (e.button == 0 && !e.alt && HandleUtility.nearestControl == id && OnFloor(plan, centre, e.mousePosition, out grab))
                        {
                            GUIUtility.hotControl = id;
                            FloorPlanEditState.BeginDrag(plan, moving);
                    if (!FloorPlanEditState.Dragging(plan)) continue;
                            e.Use();
                        }
                        break;
                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id && FloorPlanEditState.Dragging(plan) && OnFloor(plan, centre, e.mousePosition, out var now))
                        {
                            float angle = Vector3.SignedAngle(grab, now, up);
                            var s = BrushSettings.instance;
                            if (s.snapToGrid && s.rotationSnapDegrees > 0f) angle = Mathf.Round(angle / s.rotationSnapDegrees) * s.rotationSnapDegrees;
                            var turn = Quaternion.Euler(0f, angle, 0f); var c = FloorPlanEditState.dragCentre;
                            FloorPlanEditState.TransformPoints(plan, FloorPlanEditState.dragStart, FloorPlanEditState.dragMoving, q => c + turn * (q - c));
                            e.Use();
                        }
                        break;
                    case EventType.MouseUp:
                        if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                        break;
                    case EventType.Repaint:
                        Handles.color = GUIUtility.hotControl == id ? Handles.selectedColor : HandleUtility.nearestControl == id && GUIUtility.hotControl == 0 ? Handles.preselectionColor : Handles.yAxisColor;
                        Handles.DrawWireDisc(centre, up, radius, 2f);
                        break;
                }
            }
        }

        /// <summary>The mouse on the plan's floor through the centre, as a direction from the centre.</summary>
        static bool OnFloor(FloorPlan plan, Vector3 centre, Vector2 mouse, out Vector3 fromCentre)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var floor = new Plane(plan.transform.up, centre);
            fromCentre = default;
            if (!floor.Raycast(ray, out float t)) return false;
            fromCentre = ray.GetPoint(t) - centre;
            return fromCentre.sqrMagnitude > 1e-8f;
        }
    }

    /// <summary>Scale in floor plan edit mode: drag a cube along one of the gizmo's floor axes, or the middle cube for both, about the selection's centre.</summary>
    [EditorTool("Scale floor plan selection", typeof(FloorPlan), typeof(FloorPlanEditContext))]
    public sealed class FloorPlanScaleTool : FloorPlanSelectionTool
    {
        float sx = 1f, sz = 1f; Vector3 uniformOffset; Quaternion frame = Quaternion.identity;

        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var (plan, moving, centre, rotation) in Targets())
            {
                if (!FloorPlanEditState.Dragging(plan)) { sx = sz = 1f; uniformOffset = Vector3.zero; frame = rotation; }
                float size = HandleUtility.GetHandleSize(centre), cube = size * 0.1f;
                Vector3 x = frame * Vector3.right, z = frame * Vector3.forward;
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.color = Handles.xAxisColor; Handles.DrawLine(centre, centre + x * size * sx, 2f);
                    Handles.color = Handles.zAxisColor; Handles.DrawLine(centre, centre + z * size * sz, 2f);
                }
                EditorGUI.BeginChangeCheck();
                Handles.color = Handles.xAxisColor; var px = Handles.Slider(centre + x * size * sx, x, cube, Handles.CubeHandleCap, 0f);
                Handles.color = Handles.zAxisColor; var pz = Handles.Slider(centre + z * size * sz, z, cube, Handles.CubeHandleCap, 0f);
                Handles.color = Handles.centerColor; var pu = Handles.FreeMoveHandle(centre + uniformOffset, cube * 1.2f, Vector3.zero, Handles.CubeHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    FloorPlanEditState.BeginDrag(plan, moving);
                    if (!FloorPlanEditState.Dragging(plan)) continue;
                    sx = Vector3.Dot(px - centre, x) / size;
                    sz = Vector3.Dot(pz - centre, z) / size;
                    uniformOffset = pu - centre;
                    // the middle cube: dragging it right or up on screen grows the selection
                    var screen = HandleUtility.WorldToGUIPoint(pu) - HandleUtility.WorldToGUIPoint(centre);
                    float uniform = 1f + (screen.x - screen.y) / 100f;
                    // the gizmo's frame in the plan's space: a yaw on the floor
                    var r = Quaternion.Inverse(plan.transform.rotation) * frame; var ri = Quaternion.Inverse(r);
                    var k = new Vector3(sx * uniform, 1f, sz * uniform); var c = FloorPlanEditState.dragCentre;
                    FloorPlanEditState.TransformPoints(plan, FloorPlanEditState.dragStart, FloorPlanEditState.dragMoving, q => c + r * Vector3.Scale(ri * (q - c), k));
                }
            }
        }
    }

    /// <summary>Tool Settings toolbar of floor plan edit mode: handle orientation, Vertex / Edge, drag rectangle mode (shared with brush edit mode).</summary>
    [CustomEditor(typeof(FloorPlanSelectionTool), true)]
    sealed class FloorPlanSelectionToolEditor : UnityEditor.Editor, ICreateToolbar
    {
        public IEnumerable<string> toolbarElements
        {
            get
            {
                yield return "CSG Brush/Handle Orientation";
                yield return "CSG Brush/Floor Plan Select Mode";
                yield return "CSG Brush/Drag Rect Mode";
            }
        }
    }

    [EditorToolbarElement("CSG Brush/Floor Plan Select Mode")]
    sealed class FloorPlanSelectModeToolbar : VisualElement
    {
        public FloorPlanSelectModeToolbar()
        {
            Add(Toggle(BrushEditMode.Vertex, "Mode_Vertex", "Point Selection (1)"));
            Add(Toggle(BrushEditMode.Edge, "Mode_Edge", "Wall Selection (2)"));
            Add(Toggle(BrushEditMode.Face, "Mode_Face", "Room Selection (3)"));
            EditorToolbarUtility.SetupChildrenAsButtonStrip(this);
        }

        static EditorToolbarToggle Toggle(BrushEditMode mode, string icon, string tooltip)
        {
            var t = new EditorToolbarToggle { icon = BrushIcons.Get(icon), tooltip = tooltip };
            void Refresh() => t.SetValueWithoutNotify(FloorPlanEditState.Mode == mode);
            t.RegisterCallback<AttachToPanelEvent>(evt => { FloorPlanEditState.Changed += Refresh; Refresh(); });
            t.RegisterCallback<DetachFromPanelEvent>(evt => FloorPlanEditState.Changed -= Refresh);
            t.RegisterValueChangedCallback(evt => { if (evt.newValue) FloorPlanEditState.Mode = mode; else t.SetValueWithoutNotify(true); });
            Refresh();
            return t;
        }
    }
}

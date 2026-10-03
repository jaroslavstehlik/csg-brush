using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>Floor plan geometry for the tools: points on the plan's floor, snapped to the world grid.</summary>
    public static class FloorPlanTools
    {
        public static readonly Color Line = new Color(1f, 0.78f, 0.2f, 1f);
        public static readonly Color Preview = new Color(1f, 0.78f, 0.2f, 0.6f);

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        /// <summary>The plan's floor as a world plane.</summary>
        public static Plane Floor(FloorPlan plan) => new Plane(plan.transform.up, plan.transform.position);

        public static Vector3 World(FloorPlan plan, Vector3 local) => plan.transform.TransformPoint(new Vector3(local.x, 0f, local.z));

        /// <summary>A world point on the plan's floor, snapped to the grid in the plan's own axes, as a local point.</summary>
        public static Vector3 SnapLocal(FloorPlan plan, Vector3 world)
        {
            var local = plan.transform.InverseTransformPoint(world);
            local.y = 0f;
            float g = Grid;
            if (g > 0f) { local.x = Mathf.Round(local.x / g) * g; local.z = Mathf.Round(local.z / g) * g; }
            return local;
        }

        /// <summary>
        /// The next point from <paramref name="from"/> toward the mouse: along a multiple of 45 degrees with a length in grid
        /// steps (diagonal steps land on grid points too), unless <paramref name="free"/>, which takes the nearest grid point.
        /// </summary>
        public static Vector3 Constrain(Vector3 from, Vector3 toward, bool free)
        {
            if (free) return toward;
            var d = new Vector2(toward.x - from.x, toward.z - from.z);
            if (d.sqrMagnitude < 1e-8f) return from;
            float angle = Mathf.Round(Mathf.Atan2(d.y, d.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            dir.x = Mathf.Round(dir.x); dir.y = Mathf.Round(dir.y); // exact axes and diagonals
            float g = Grid > 0f ? Grid : 0.01f;
            float steps = Mathf.Max(1f, Mathf.Round(Vector2.Dot(d, dir.normalized) / (g * dir.magnitude))); // a diagonal step is g along both axes
            return new Vector3(from.x + dir.x * g * steps, 0f, from.z + dir.y * g * steps);
        }

        public static bool MouseOnFloor(FloorPlan plan, Vector2 mouse, out Vector3 world)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            if (Floor(plan).Raycast(ray, out float t) && t > 0f) { world = ray.GetPoint(t); return true; }
            world = default; return false;
        }

        public static void DrawOutline(FloorPlan plan)
        {
            plan.EnsureGraph();
            Handles.color = Line;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            for (int w = 0; w < plan.walls.Count; w++)
            {
                plan.WallPoints(w, out int a, out int b);
                Handles.DrawAAPolyLine(3f, World(plan, plan.points[a]), World(plan, plan.points[b]));
            }
        }

        /// <summary>The point nearest the mouse within 12 pixels, or -1.</summary>
        public static int PointUnderMouse(FloorPlan plan, Vector2 mouse)
        {
            int best = -1; float bestD = 12f;
            for (int i = 0; i < plan.points.Count; i++) { float d = (HandleUtility.WorldToGUIPoint(World(plan, plan.points[i])) - mouse).magnitude; if (d < bestD) { bestD = d; best = i; } }
            return best;
        }

        /// <summary>The wall nearest the mouse within 10 pixels, or -1, with the spot on it (local, on the grid along the wall when snapping).</summary>
        public static int WallUnderMouse(FloorPlan plan, Vector2 mouse, out Vector3 local)
        {
            local = default;
            int best = -1; float bestD = 10f;
            for (int w = 0; w < plan.walls.Count; w++)
            {
                plan.WallPoints(w, out int a, out int b);
                float d = HandleUtility.DistancePointLine(mouse, HandleUtility.WorldToGUIPoint(World(plan, plan.points[a])), HandleUtility.WorldToGUIPoint(World(plan, plan.points[b])));
                if (d < bestD) { bestD = d; best = w; }
            }
            if (best < 0 || !MouseOnFloor(plan, mouse, out var world)) return -1;
            plan.WallPoints(best, out int ia, out int ib);
            Vector3 pa = plan.points[ia], pb = plan.points[ib], ab = pb - pa;
            float len = ab.magnitude;
            var m = plan.transform.InverseTransformPoint(world); m.y = 0f;
            float t = Mathf.Clamp(Vector3.Dot(m - pa, ab) / len, 0f, len);
            float g = Grid;
            if (g > 0f) t = Mathf.Round(t / g) * g;
            t = Mathf.Clamp(t, 0f, len);
            local = pa + ab / len * t;
            return best;
        }
    }

    /// <summary>
    /// A selected floor plan shows its outline; Edit (the Edit Floor Plan context) selects and transforms its points and
    /// walls, Draw adds walls.
    /// </summary>
    [CustomEditor(typeof(FloorPlan))]
    public sealed class FloorPlanEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var plan = (FloorPlan)target;
            DrawDefaultInspector();
            plan.EnsureGraph();
            EditorGUILayout.LabelField("Plan", plan.points.Count + " points, " + plan.walls.Count + " walls, " + FloorPlanEditState.RoomsOf(plan).Count + " rooms");
            EditorGUILayout.BeginHorizontal();
            bool editing = FloorPlanEditContext.IsActive;
            if (GUILayout.Toggle(editing, new GUIContent("Edit", "Move, Rotate and Scale act on the selected points or walls (1, 2)"), EditorStyles.miniButton) != editing)
            {
                if (editing) FloorPlanEditContext.Exit(); else FloorPlanEditContext.Enter();
            }
            bool drawing = ToolManager.activeToolType == typeof(FloorPlanDrawTool);
            if (GUILayout.Toggle(drawing, new GUIContent("Draw", "Click on the floor to add walls; Enter or Escape finishes"), EditorStyles.miniButton) != drawing)
            {
                if (drawing) ToolManager.RestorePreviousPersistentTool();
                else { FloorPlanEditContext.Exit(); ToolManager.SetActiveTool<FloorPlanDrawTool>(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing && targets.Length == 1) DrawSelection(plan);
        }

        /// <summary>Edit mode: the thickness of the selected walls, or the floor and ceiling of the selected rooms.</summary>
        static void DrawSelection(FloorPlan plan)
        {
            var sel = FloorPlanEditState.Sel(plan);
            if (FloorPlanEditState.Mode == BrushEditMode.Edge && sel.edges.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Selected walls (" + sel.edges.Count + ")", EditorStyles.boldLabel);
                float first = -1f; bool mixed = false;
                foreach (var w in sel.edges) { float t = plan.walls[w].thickness; if (first < 0f) first = t; else if (!Mathf.Approximately(first, t)) mixed = true; }
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.FloatField(new GUIContent("Thickness", "Metres; 0 uses the plan's outside or interior thickness."), first);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(plan, "Wall thickness");
                    foreach (var w in sel.edges) { var link = plan.walls[w]; link.thickness = Mathf.Max(0f, value); plan.walls[w] = link; }
                    BrushGenerators.MarkDirty(plan);
                }
            }
            else if (FloorPlanEditState.Mode == BrushEditMode.Face && sel.rooms.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Selected rooms (" + sel.rooms.Count + ")", EditorStyles.boldLabel);
                var rooms = new List<FloorPlan.Room>(FloorPlanEditState.RoomsOf(plan));
                var chosen = new List<FloorPlan.Room>(); foreach (var r in sel.rooms) if (r < rooms.Count) chosen.Add(rooms[r]);
                if (chosen.Count == 0) return;
                var shown = chosen[0];
                bool Mixed<T>(System.Func<FloorPlan.Room, T> get) { foreach (var c in chosen) if (!Equals(get(c), get(shown))) return true; return false; }
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = Mixed(r => r.floor);
                bool floor = EditorGUILayout.Toggle(new GUIContent("Floor", "A floor slab under the room."), shown.floor);
                EditorGUI.showMixedValue = Mixed(r => r.floorMaterial);
                var floorMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Floor material", "Empty uses the project's default material."), shown.floorMaterial, typeof(Material), false);
                EditorGUI.showMixedValue = Mixed(r => r.ceiling);
                bool ceiling = EditorGUILayout.Toggle(new GUIContent("Ceiling", "A ceiling slab over the room."), shown.ceiling);
                EditorGUI.showMixedValue = Mixed(r => r.ceilingMaterial);
                var ceilingMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Ceiling material", "Empty uses the project's default material."), shown.ceilingMaterial, typeof(Material), false);
                EditorGUI.showMixedValue = false;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(plan, "Room settings");
                    foreach (var room in chosen)
                    {
                        var s = plan.SettingsOf(room);
                        if (floor != shown.floor) s.floor = floor;
                        if (floorMaterial != shown.floorMaterial) s.floorMaterial = floorMaterial;
                        if (ceiling != shown.ceiling) s.ceiling = ceiling;
                        if (ceilingMaterial != shown.ceilingMaterial) s.ceilingMaterial = ceilingMaterial;
                    }
                    BrushGenerators.MarkDirty(plan);
                }
                if (GUILayout.Button(new GUIContent("Use Plan Defaults", "The selected rooms take the plan's floor and ceiling settings.")))
                {
                    Undo.RecordObject(plan, "Room settings");
                    foreach (var room in chosen) if (room.settings >= 0 && room.settings < plan.rooms.Count) plan.rooms[room.settings] = null;
                    plan.rooms.RemoveAll(r => r == null);
                    BrushGenerators.MarkDirty(plan);
                }
            }
        }

        void OnSceneGUI()
        {
            // the draw tool and edit mode show their own
            if (ToolManager.activeToolType == typeof(FloorPlanDrawTool) || FloorPlanEditContext.IsActive) return;
            FloorPlanTools.DrawOutline((FloorPlan)target);
        }
    }

    /// <summary>
    /// Draw walls: click on the floor to add a point and a wall to it, snapped to the grid and to 45 degree steps (Shift: any
    /// grid point). A click on a point or a wall joins the new wall to it (splitting the wall there); a stroke that joins
    /// one ends, ready for the next. Backspace takes back the last wall; Escape or a double click ends the stroke, Enter or
    /// a second Escape leaves the tool.
    /// </summary>
    [EditorTool("Draw Floor Plan", typeof(FloorPlan))]
    public sealed class FloorPlanDrawTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), "Draw Floor Plan");

        enum Hover { None, Free, Point, Wall }
        Hover m_Kind; Vector3 m_Hover; int m_HoverPoint = -1, m_HoverWall = -1;
        /// <summary>The point the next wall starts from (id), or -1 between strokes.</summary>
        int m_Last = -1;
        readonly List<int> m_Stroke = new List<int>();

        public override void OnActivated()
        {
            m_Last = -1; m_Stroke.Clear();
            if (target is FloorPlan plan) { plan.EnsureGraph(); if (plan.walls.Count == 0 && plan.points.Count == 1) { m_Last = plan.pointIds[0]; m_Stroke.Add(m_Last); } }
        }

        public override void OnToolGUI(EditorWindow window)
        {
            var plan = target as FloorPlan;
            if (plan == null || !(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (m_Last >= 0 && plan.PointIndex(m_Last) < 0) { m_Last = -1; m_Stroke.Clear(); } // undone
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.Repaint) FindHover(plan, e);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && m_Kind != Hover.None)
            {
                e.Use();
                if (e.clickCount == 2) { EndStroke(); return; }
                Undo.RecordObject(plan, "Draw floor plan");
                int point; bool joined = m_Kind != Hover.Free;
                if (m_Kind == Hover.Point) point = plan.pointIds[m_HoverPoint];
                else if (m_Kind == Hover.Wall) point = plan.SplitWall(m_HoverWall, m_Hover);
                else point = plan.AddPoint(m_Hover);
                bool started = m_Last >= 0;
                if (started && point != m_Last) plan.AddWall(m_Last, point);
                BrushGenerators.MarkDirty(plan);
                if (joined && started && point != m_Last) EndStroke(); // joined the plan: this stroke is done
                else { m_Last = point; m_Stroke.Add(point); }
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && m_Stroke.Count >= 2)
                {
                    Undo.RecordObject(plan, "Remove floor plan wall");
                    int last = m_Stroke[m_Stroke.Count - 1], prev = m_Stroke[m_Stroke.Count - 2];
                    plan.walls.RemoveAll(w => (w.start == prev && w.end == last) || (w.start == last && w.end == prev));
                    plan.RemoveLonePoints();
                    m_Stroke.RemoveAt(m_Stroke.Count - 1); m_Last = prev;
                    BrushGenerators.MarkDirty(plan); e.Use();
                }
                else if (e.keyCode == KeyCode.Escape) { if (m_Last >= 0) EndStroke(); else Finish(); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Finish(); e.Use(); }
            }
            if (e.type == EventType.Repaint) DrawPreview(plan);
        }

        void EndStroke()
        {
            var plan = target as FloorPlan;
            // a lone start point (clicked, then ended) is not kept
            if (plan != null && m_Stroke.Count == 1) { int lone = m_Stroke[0]; if (!plan.walls.Exists(w => w.start == lone || w.end == lone)) { Undo.RecordObject(plan, "Draw floor plan"); plan.RemoveLonePoints(); } }
            m_Last = -1; m_Stroke.Clear(); SceneView.RepaintAll();
        }

        void FindHover(FloorPlan plan, Event e)
        {
            m_Kind = Hover.None; m_HoverPoint = m_HoverWall = -1;
            int point = FloorPlanTools.PointUnderMouse(plan, e.mousePosition);
            if (point >= 0) { m_Kind = Hover.Point; m_HoverPoint = point; m_Hover = plan.points[point]; return; }
            int wall = FloorPlanTools.WallUnderMouse(plan, e.mousePosition, out var onWall);
            if (wall >= 0)
            {
                plan.WallPoints(wall, out int a, out int b);
                if ((onWall - plan.points[a]).sqrMagnitude < 1e-8f) { m_Kind = Hover.Point; m_HoverPoint = a; m_Hover = plan.points[a]; return; }
                if ((onWall - plan.points[b]).sqrMagnitude < 1e-8f) { m_Kind = Hover.Point; m_HoverPoint = b; m_Hover = plan.points[b]; return; }
                m_Kind = Hover.Wall; m_HoverWall = wall; m_Hover = onWall; return;
            }
            if (!FloorPlanTools.MouseOnFloor(plan, e.mousePosition, out var floor)) return;
            m_Kind = Hover.Free;
            m_Hover = FloorPlanTools.SnapLocal(plan, floor);
            int last = m_Last >= 0 ? plan.PointIndex(m_Last) : -1;
            if (last >= 0) m_Hover = FloorPlanTools.Constrain(plan.points[last], m_Hover, e.shift);
        }

        void DrawPreview(FloorPlan plan)
        {
            FloorPlanTools.DrawOutline(plan);
            if (m_Kind == Hover.None) return;
            var hover = FloorPlanTools.World(plan, m_Hover);
            bool joining = m_Kind != Hover.Free;
            Handles.color = joining ? Color.white : FloorPlanTools.Line;
            Handles.DotHandleCap(-1, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * (joining ? 0.09f : 0.06f), EventType.Repaint);
            int last = m_Last >= 0 ? plan.PointIndex(m_Last) : -1;
            if (last < 0) return;
            var from = FloorPlanTools.World(plan, plan.points[last]);
            Handles.color = FloorPlanTools.Preview;
            Handles.DrawDottedLine(from, hover, 4f);
            float length = new Vector2(m_Hover.x - plan.points[last].x, m_Hover.z - plan.points[last].z).magnitude;
            Handles.Label((from + hover) * 0.5f, BrushSettings.instance.FormatUnits(length), EditorStyles.whiteMiniLabel);
        }

        void Finish() => ToolManager.RestorePreviousPersistentTool();
    }

    static class FloorPlanMenu
    {
        [MenuItem("GameObject/Brush/Floor Plan", false, 40)]
        static void Create()
        {
            var s = BrushSettings.instance;
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float g = s.GridMeters;
            if (g > 0f) pivot = new Vector3(Mathf.Round(pivot.x / g) * g, 0f, Mathf.Round(pivot.z / g) * g);
            var parent = Selection.activeTransform != null && Selection.activeTransform.GetComponent<Brush>() == null ? Selection.activeTransform : null;
            Create(pivot, parent, false);
        }

        /// <summary>A new plan at a position, selected, drawing; with <paramref name="firstPoint"/> its first point is there.</summary>
        public static FloorPlan Create(Vector3 position, Transform parent, bool firstPoint)
        {
            var go = new GameObject("Floor Plan");
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags); // its walls take the plan's flags
            Undo.RegisterCreatedObjectUndo(go, "Create floor plan");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Create floor plan");
            go.transform.position = position;
            var plan = Undo.AddComponent<FloorPlan>(go);
            if (firstPoint) plan.AddPoint(Vector3.zero); // the draw tool starts its first wall here
            Selection.activeGameObject = go;
            EditorApplication.delayCall += () => { if (Selection.activeGameObject == go) ToolManager.SetActiveTool<FloorPlanDrawTool>(); };
            return plan;
        }
    }

    /// <summary>The Create tool's Floor Plan: click on a floor to start a plan there, then click its corners.</summary>
    [EditorTool("Floor Plan", variantGroup = typeof(BrushCreateTool), variantPriority = 11)]
    public sealed class CreateFloorPlanTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), "Floor Plan");
        [MenuItem("Tools/CSG Brush/Create/Floor Plan", false, 12)] static void Menu() => ToolManager.SetActiveTool<CreateFloorPlanTool>();

        Vector3 m_Hover; bool m_HoverValid;

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);
            if (e.alt || Tools.viewToolActive) return;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                // a floor only: a plan's walls stand up from where it is started
                m_HoverValid = BrushCreateTool.FindPlane(e.mousePosition, out var p, out var n) && n.y > 0.7f;
                if (m_HoverValid) m_Hover = BrushDraw.SnapOnPlane(p, Vector3.up, Grid);
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && m_HoverValid && HandleUtility.nearestControl == id)
            {
                FloorPlanMenu.Create(m_Hover, null, true);
                m_HoverValid = false;
                e.Use();
            }
            if (e.type == EventType.Repaint && m_HoverValid)
            {
                Handles.color = FloorPlanTools.Line;
                Handles.DotHandleCap(-1, m_Hover, Quaternion.identity, HandleUtility.GetHandleSize(m_Hover) * 0.06f, EventType.Repaint);
            }
        }
    }
}

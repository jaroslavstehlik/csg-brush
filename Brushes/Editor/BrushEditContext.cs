using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    public enum BrushEditMode { Vertex = 0, Edge = 1, Face = 2 }

    /// <summary>How the Move/Rotate/Scale gizmo is aligned in brush edit mode (ProBuilder's World / Local / Element).</summary>
    public enum BrushHandleOrientation { World = 0, Local = 1, Element = 2 }

    /// <summary>
    /// Brush edit mode. While this context is active, Unity's Move, Rotate and Scale tools (toolbar and W/E/R)
    /// act on the selected vertices, edges or faces of the selected brushes instead of the GameObjects; leaving
    /// the mode gives the normal tools back. Selection (click, Shift add, Ctrl remove, marquee) is handled here
    /// so it works with any of the three tools.
    /// </summary>
    [EditorToolContext("Edit Brush", typeof(Brush))]
    public sealed class BrushEditContext : EditorToolContext
    {
        public static bool IsActive => ToolManager.activeContextType == typeof(BrushEditContext);

        public static void Enter() { if (!IsActive) ToolManager.SetActiveContext<BrushEditContext>(); }
        public static void Exit() { if (IsActive) ToolManager.SetActiveContext<GameObjectToolContext>(); }
        public static void Toggle() { if (IsActive) Exit(); else Enter(); }

        protected override Type GetEditorToolType(Tool tool)
        {
            switch (tool)
            {
                case Tool.Move: return typeof(BrushMoveTool);
                case Tool.Rotate: return typeof(BrushRotateTool);
                case Tool.Scale: return typeof(BrushScaleTool);
                default: return base.GetEditorToolType(tool);
            }
        }

        public override void OnActivated() { BrushExtrudeOverlay.Show(true); }
        public override void OnWillBeDeactivated() { BrushExtrudeOverlay.Show(false); }

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView)) return;
            BrushEditState.OnSceneGUI(this);
        }
    }

    /// <summary>Selection, drawing, picking and drag bookkeeping shared by the edit context and its three tools.</summary>
    public static class BrushEditState
    {
        static BrushEditMode s_Mode = BrushEditMode.Face;
        static bool s_SelectHidden, s_RectComplete = true;
        static BrushHandleOrientation s_Orientation = BrushHandleOrientation.Element;
        /// <summary>Raised when the mode or one of the selection settings changes (the Tool Settings toolbar listens).</summary>
        public static event Action Changed;
        static void Notify() { Changed?.Invoke(); SceneView.RepaintAll(); }

        public static BrushEditMode Mode { get => s_Mode; set { if (s_Mode == value) return; s_Mode = value; Notify(); } }
        /// <summary>Gizmo alignment: world axes, the brush's axes, or the selected element (blue axis along the face normal).</summary>
        public static BrushHandleOrientation Orientation { get => s_Orientation; set { if (s_Orientation == value) return; s_Orientation = value; Notify(); } }
        /// <summary>Select elements on faces that look away from the camera too (ProBuilder's "select hidden").</summary>
        public static bool SelectHidden { get => s_SelectHidden; set { if (s_SelectHidden == value) return; s_SelectHidden = value; Notify(); } }
        /// <summary>Drag rectangle: on selects only elements completely inside, off selects everything it touches.</summary>
        public static bool RectComplete { get => s_RectComplete; set { if (s_RectComplete == value) return; s_RectComplete = value; Notify(); } }
        public static readonly Color SelectedColor = new Color(1f, 0.85f, 0.2f, 1f);

        public sealed class Selection { public HashSet<int> faces = new HashSet<int>(); public HashSet<int> vertices = new HashSet<int>(); public HashSet<long> edges = new HashSet<long>(); public bool Any => faces.Count > 0 || vertices.Count > 0 || edges.Count > 0; }
        static readonly Dictionary<Brush, Selection> s_Selection = new Dictionary<Brush, Selection>();
        public static Selection Sel(Brush b) { if (!s_Selection.TryGetValue(b, out var s)) { s = new Selection(); s_Selection[b] = s; } return s; }
        public static long EdgeKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
        public static void ClearSelection() { s_Selection.Clear(); SceneView.RepaintAll(); }

        // drag state shared by the tools
        public static Brush dragBrush; public static BrushPolyhedron dragStart; public static HashSet<int> dragVertices; public static Vector3 dragOrigin; public static bool dragging;

        static bool marquee; static Vector2 marqueeStart;

        public static IEnumerable<Brush> SelectedBrushes()
        {
            foreach (var go in UnityEditor.Selection.gameObjects)
                if (go.TryGetComponent<Brush>(out var brush)) yield return brush;
        }

        public static BrushPolyhedron ShapeOf(Brush brush)
        {
            if (brush.shape == BrushShape.Custom) return brush.polyhedron;
            return BrushApi.CanConvertToCustom(brush.shape) ? BrushApi.PolyhedronFor(brush.shape, brush.ClampedSize, brush.sides) : null;
        }

        public static void OnSceneGUI(EditorToolContext context)
        {
            var e = Event.current;
            HandleKeys(e);
            bool viewTool = Tools.current == Tool.View || Tools.viewToolActive; // the hand tool pans; it never selects
            int passive = GUIUtility.GetControlID(FocusType.Passive);
            if (!viewTool) HandleUtility.AddDefaultControl(passive); // clicks on empty space stay in the edit mode
            foreach (var brush in SelectedBrushes())
            {
                var poly = ShapeOf(brush);
                if (poly == null || !poly.IsValid)
                {
                    Handles.Label(brush.transform.position, brush.shape + " brushes cannot be edited by hand yet", EditorStyles.helpBox);
                    continue;
                }
                var sel = Sel(brush);
                Draw(brush, poly, sel);
                if (!viewTool) HandlePicking(brush, poly, sel, e, passive);
            }
            DrawMarquee();
            if (e.type == EventType.MouseUp) { dragging = false; dragBrush = null; dragStart = null; }
        }

        // ------------------------------------------------------------------ keys

        static void HandleKeys(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Alpha1) { Mode = BrushEditMode.Vertex; e.Use(); SceneView.RepaintAll(); }
            else if (e.keyCode == KeyCode.Alpha2) { Mode = BrushEditMode.Edge; e.Use(); SceneView.RepaintAll(); }
            else if (e.keyCode == KeyCode.Alpha3) { Mode = BrushEditMode.Face; e.Use(); SceneView.RepaintAll(); }
            else if (e.keyCode == KeyCode.Escape) { ClearSelection(); e.Use(); }
            else if (e.keyCode == KeyCode.A && (e.control || e.command)) { SelectAll(); e.Use(); }
            else if (e.keyCode == KeyCode.I && (e.control || e.command)) { Invert(); e.Use(); }
        }

        public static void SelectAll()
        {
            foreach (var brush in SelectedBrushes())
            {
                var poly = ShapeOf(brush); if (poly == null) continue;
                var sel = Sel(brush);
                for (int f = 0; f < poly.faces.Length; f++) sel.faces.Add(f);
                for (int v = 0; v < poly.vertices.Length; v++) sel.vertices.Add(v);
                foreach (var ed in poly.Edges()) sel.edges.Add(EdgeKey(ed.a, ed.b));
            }
            SceneView.RepaintAll();
        }

        public static void Invert()
        {
            foreach (var brush in SelectedBrushes())
            {
                var poly = ShapeOf(brush); if (poly == null) continue;
                var sel = Sel(brush);
                switch (Mode)
                {
                    case BrushEditMode.Face: { var n = new HashSet<int>(); for (int f = 0; f < poly.faces.Length; f++) if (!sel.faces.Contains(f)) n.Add(f); sel.faces = n; break; }
                    case BrushEditMode.Vertex: { var n = new HashSet<int>(); for (int v = 0; v < poly.vertices.Length; v++) if (!sel.vertices.Contains(v)) n.Add(v); sel.vertices = n; break; }
                    case BrushEditMode.Edge: { var n = new HashSet<long>(); foreach (var ed in poly.Edges()) { var k = EdgeKey(ed.a, ed.b); if (!sel.edges.Contains(k)) n.Add(k); } sel.edges = n; break; }
                }
            }
            SceneView.RepaintAll();
        }

        // ------------------------------------------------------------------ drawing

        static void Draw(Brush brush, BrushPolyhedron poly, Selection sel)
        {
            if (Event.current.type != EventType.Repaint) return;
            var t = brush.transform;
            BrushShapeDrawing.Draw(brush, drawEdges: false);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
            if (Mode == BrushEditMode.Face)
            {
                foreach (var f in sel.faces)
                {
                    if (f >= poly.faces.Length) continue;
                    var idx = poly.faces[f].indices; var pts = new Vector3[idx.Length];
                    for (int i = 0; i < idx.Length; i++) pts[i] = t.TransformPoint(poly.vertices[idx[i]]);
                    Handles.color = new Color(1f, 0.85f, 0.2f, 0.25f);
                    Handles.DrawAAConvexPolygon(pts);
                }
            }
            foreach (var ed in poly.Edges())
            {
                bool selected = Mode == BrushEditMode.Edge && sel.edges.Contains(EdgeKey(ed.a, ed.b));
                if (Mode == BrushEditMode.Face) foreach (var f in sel.faces) if (f < poly.faces.Length && Array.IndexOf(poly.faces[f].indices, ed.a) >= 0 && Array.IndexOf(poly.faces[f].indices, ed.b) >= 0) { selected = true; break; }
                Handles.color = selected ? SelectedColor : BrushShapeDrawing.EdgeColor;
                Handles.DrawLine(t.TransformPoint(poly.vertices[ed.a]), t.TransformPoint(poly.vertices[ed.b]), selected ? 3f : 1.5f);
            }
            if (Mode == BrushEditMode.Vertex)
            {
                for (int v = 0; v < poly.vertices.Length; v++)
                {
                    var w = t.TransformPoint(poly.vertices[v]);
                    Handles.color = sel.vertices.Contains(v) ? SelectedColor : Color.white;
                    Handles.DotHandleCap(0, w, Quaternion.identity, HandleUtility.GetHandleSize(w) * 0.04f, EventType.Repaint);
                }
            }
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        }

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

        static Rect MarqueeRect(Vector2 current) => Rect.MinMaxRect(Mathf.Min(marqueeStart.x, current.x), Mathf.Min(marqueeStart.y, current.y), Mathf.Max(marqueeStart.x, current.x), Mathf.Max(marqueeStart.y, current.y));

        // ------------------------------------------------------------------ picking

        static void HandlePicking(Brush brush, BrushPolyhedron poly, Selection sel, Event e, int passive)
        {
            if (dragging || e.alt) return;
            if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != passive) return; // a gizmo owns the mouse
            if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == passive)
            {
                marquee = true; marqueeStart = e.mousePosition;
                GUIUtility.hotControl = passive; e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 1 && GUIUtility.hotControl == 0)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Select All"), false, SelectAll);
                menu.AddItem(new GUIContent("Invert Selection"), false, Invert);
                menu.AddItem(new GUIContent("Clear Selection"), false, ClearSelection);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Vertex mode  1"), Mode == BrushEditMode.Vertex, () => { Mode = BrushEditMode.Vertex; SceneView.RepaintAll(); });
                menu.AddItem(new GUIContent("Edge mode  2"), Mode == BrushEditMode.Edge, () => { Mode = BrushEditMode.Edge; SceneView.RepaintAll(); });
                menu.AddItem(new GUIContent("Face mode  3"), Mode == BrushEditMode.Face, () => { Mode = BrushEditMode.Face; SceneView.RepaintAll(); });
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Stop Editing"), false, BrushEditContext.Exit);
                menu.ShowAsContext();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && marquee && GUIUtility.hotControl == passive) { e.Use(); SceneView.RepaintAll(); }
            else if (e.type == EventType.MouseUp && marquee && GUIUtility.hotControl == passive)
            {
                var rect = MarqueeRect(e.mousePosition);
                bool click = rect.width < 4f && rect.height < 4f;
                bool remove = e.control || e.command;
                if (!e.shift && !remove) foreach (var b in SelectedBrushes()) { var s = Sel(b); s.faces.Clear(); s.vertices.Clear(); s.edges.Clear(); }
                foreach (var b in SelectedBrushes())
                {
                    var p = ShapeOf(b); if (p == null) continue;
                    if (click) PickClick(b, p, Sel(b), e.mousePosition, remove); else PickRect(b, p, Sel(b), rect, remove);
                }
                marquee = false; GUIUtility.hotControl = 0; e.Use(); SceneView.RepaintAll();
            }
        }

        static void Apply(HashSet<int> set, int item, bool remove) { if (remove) set.Remove(item); else set.Add(item); }
        static void Apply(HashSet<long> set, long item, bool remove) { if (remove) set.Remove(item); else set.Add(item); }

        /// <summary>Which faces look at the camera; vertices and edges are visible when one of their faces does.</summary>
        sealed class Visibility
        {
            public bool[] face, vertex;
            public Dictionary<long, bool> edge = new Dictionary<long, bool>();
        }

        static Visibility ComputeVisibility(Brush brush, BrushPolyhedron poly)
        {
            var vis = new Visibility { face = new bool[poly.faces.Length], vertex = new bool[poly.vertices.Length] };
            var cam = Camera.current != null ? Camera.current : (SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null);
            var t = brush.transform;
            for (int f = 0; f < poly.faces.Length; f++)
            {
                if (cam == null || SelectHidden) { vis.face[f] = true; }
                else
                {
                    var plane = poly.Plane(f);
                    var n = t.TransformDirection(new Vector3(plane.x, plane.y, plane.z));
                    var centre = t.TransformPoint(poly.FaceCentre(f));
                    var view = cam.orthographic ? cam.transform.forward : centre - cam.transform.position;
                    vis.face[f] = Vector3.Dot(n, view) < 0f;
                }
                if (!vis.face[f]) continue;
                var idx = poly.faces[f].indices;
                for (int i = 0; i < idx.Length; i++) { vis.vertex[idx[i]] = true; vis.edge[EdgeKey(idx[i], idx[(i + 1) % idx.Length])] = true; }
            }
            return vis;
        }

        /// <summary>Does the segment a-b touch the rectangle (either end inside, or the segment crosses an edge of it)?</summary>
        public static bool SegmentTouchesRect(Vector2 a, Vector2 b, Rect r)
        {
            if (r.Contains(a) || r.Contains(b)) return true;
            Vector2 p0 = new Vector2(r.xMin, r.yMin), p1 = new Vector2(r.xMax, r.yMin), p2 = new Vector2(r.xMax, r.yMax), p3 = new Vector2(r.xMin, r.yMax);
            return Crosses(a, b, p0, p1) || Crosses(a, b, p1, p2) || Crosses(a, b, p2, p3) || Crosses(a, b, p3, p0);
        }

        static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Side(Vector2 p, Vector2 q, Vector2 x) => (q.x - p.x) * (x.y - p.y) - (q.y - p.y) * (x.x - p.x);
            float s1 = Side(a, b, c), s2 = Side(a, b, d), s3 = Side(c, d, a), s4 = Side(c, d, b);
            return s1 * s2 < 0f && s3 * s4 < 0f;
        }

        static void PickClick(Brush brush, BrushPolyhedron poly, Selection sel, Vector2 mouse, bool remove)
        {
            var t = brush.transform;
            var vis = ComputeVisibility(brush, poly);
            switch (Mode)
            {
                case BrushEditMode.Vertex:
                {
                    int best = -1; float bestD = 12f;
                    for (int v = 0; v < poly.vertices.Length; v++) { if (!vis.vertex[v]) continue; float d = HandleUtility.DistanceToCircle(t.TransformPoint(poly.vertices[v]), 0f); if (d < bestD) { bestD = d; best = v; } }
                    if (best >= 0) Apply(sel.vertices, best, remove);
                    break;
                }
                case BrushEditMode.Edge:
                {
                    (int a, int b) best = (-1, -1); float bestD = 10f;
                    foreach (var ed in poly.Edges()) { if (!vis.edge.TryGetValue(EdgeKey(ed.a, ed.b), out var seen) || !seen) continue; float d = HandleUtility.DistanceToLine(t.TransformPoint(poly.vertices[ed.a]), t.TransformPoint(poly.vertices[ed.b])); if (d < bestD) { bestD = d; best = ed; } }
                    if (best.a >= 0) Apply(sel.edges, EdgeKey(best.a, best.b), remove);
                    break;
                }
                case BrushEditMode.Face:
                {
                    int face = PickFace(brush, poly, mouse);
                    if (face >= 0) Apply(sel.faces, face, remove);
                    break;
                }
            }
        }

        static int PickFace(Brush brush, BrushPolyhedron poly, Vector2 mouse)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var t = brush.transform;
            var lo = t.InverseTransformPoint(ray.origin); var ld = t.InverseTransformDirection(ray.direction);
            int best = -1; float bestT = float.MaxValue;
            for (int f = 0; f < poly.faces.Length; f++)
            {
                var plane = poly.Plane(f); var n = new Vector3(plane.x, plane.y, plane.z);
                float denom = Vector3.Dot(n, ld);
                if (denom >= -1e-6f) continue;
                float tt = -(Vector3.Dot(n, lo) + plane.w) / denom;
                if (tt < 0f || tt >= bestT) continue;
                if (PointInFace(poly, f, lo + ld * tt)) { bestT = tt; best = f; }
            }
            return best;
        }

        static bool PointInFace(BrushPolyhedron poly, int face, Vector3 p)
        {
            var plane = poly.Plane(face); var n = new Vector3(plane.x, plane.y, plane.z);
            var idx = poly.faces[face].indices;
            var u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(n, u);
            float px = Vector3.Dot(p, u), py = Vector3.Dot(p, w); int winding = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = poly.vertices[idx[i]]; var b = poly.vertices[idx[(i + 1) % idx.Length]];
                float ax = Vector3.Dot(a, u) - px, ay = Vector3.Dot(a, w) - py, bx = Vector3.Dot(b, u) - px, by = Vector3.Dot(b, w) - py;
                if (ay <= 0f) { if (by > 0f && ax * by - bx * ay > 0f) winding++; }
                else if (by <= 0f && ax * by - bx * ay < 0f) winding--;
            }
            return winding != 0;
        }

        static void PickRect(Brush brush, BrushPolyhedron poly, Selection sel, Rect rect, bool remove)
        {
            var t = brush.transform;
            var vis = ComputeVisibility(brush, poly);
            var gui = new Vector2[poly.vertices.Length];
            for (int v = 0; v < gui.Length; v++) gui[v] = HandleUtility.WorldToGUIPoint(t.TransformPoint(poly.vertices[v]));
            bool Inside(int v) => rect.Contains(gui[v]);
            bool EdgeIn(int a, int b) => RectComplete ? Inside(a) && Inside(b) : SegmentTouchesRect(gui[a], gui[b], rect);
            switch (Mode)
            {
                case BrushEditMode.Vertex: for (int v = 0; v < poly.vertices.Length; v++) if (vis.vertex[v] && Inside(v)) Apply(sel.vertices, v, remove); break;
                case BrushEditMode.Edge: foreach (var ed in poly.Edges()) if (vis.edge.TryGetValue(EdgeKey(ed.a, ed.b), out var seen) && seen && EdgeIn(ed.a, ed.b)) Apply(sel.edges, EdgeKey(ed.a, ed.b), remove); break;
                case BrushEditMode.Face:
                    for (int f = 0; f < poly.faces.Length; f++)
                    {
                        if (!vis.face[f]) continue;
                        var idx = poly.faces[f].indices;
                        bool hit;
                        if (RectComplete) { hit = true; foreach (var i in idx) if (!Inside(i)) { hit = false; break; } }
                        else { hit = false; for (int i = 0; i < idx.Length && !hit; i++) hit = EdgeIn(idx[i], idx[(i + 1) % idx.Length]); }
                        if (hit) Apply(sel.faces, f, remove);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ transforming the selection

        public static HashSet<int> SelectedVertices(BrushPolyhedron poly, Selection sel)
        {
            switch (Mode)
            {
                case BrushEditMode.Vertex: return new HashSet<int>(sel.vertices);
                case BrushEditMode.Edge: { var set = new HashSet<int>(); foreach (var k in sel.edges) { set.Add((int)(k >> 32)); set.Add((int)(k & 0xffffffffL)); } return set; }
                default: { var faces = new List<int>(); foreach (var f in sel.faces) if (f < poly.faces.Length) faces.Add(f); return poly.VerticesOfFaces(faces); }
            }
        }

        /// <summary>
        /// Gizmo rotation for a selection in the shape's local frame: the blue (z) axis along the normal of the selected
        /// faces (an edge or vertex uses the average of its faces), the green (y) axis along an edge of the element.
        /// Identity when nothing gives a direction.
        /// </summary>
        public static Quaternion ElementRotation(BrushPolyhedron poly, BrushEditMode mode, Selection sel)
        {
            var faces = new List<int>();
            Vector3 up = Vector3.zero;
            switch (mode)
            {
                case BrushEditMode.Face:
                    foreach (var f in sel.faces) if (f < poly.faces.Length) faces.Add(f);
                    if (faces.Count > 0) { var idx = poly.faces[faces[0]].indices; up = poly.vertices[idx[1]] - poly.vertices[idx[0]]; }
                    break;
                case BrushEditMode.Edge:
                    foreach (var k in sel.edges)
                    {
                        int a = (int)(k >> 32), b = (int)(k & 0xffffffffL);
                        if (a >= poly.vertices.Length || b >= poly.vertices.Length) continue;
                        if (up == Vector3.zero) up = poly.vertices[b] - poly.vertices[a];
                        for (int f = 0; f < poly.faces.Length; f++) { var idx = poly.faces[f].indices; bool ha = false, hb = false; foreach (var i in idx) { if (i == a) ha = true; if (i == b) hb = true; } if (ha && hb && !faces.Contains(f)) faces.Add(f); }
                    }
                    break;
                default:
                    foreach (var v in sel.vertices)
                    {
                        if (v >= poly.vertices.Length) continue;
                        for (int f = 0; f < poly.faces.Length; f++)
                        {
                            var idx = poly.faces[f].indices;
                            for (int i = 0; i < idx.Length; i++)
                                if (idx[i] == v) { if (!faces.Contains(f)) faces.Add(f); if (up == Vector3.zero) up = poly.vertices[idx[(i + 1) % idx.Length]] - poly.vertices[v]; }
                        }
                    }
                    break;
            }
            if (faces.Count == 0) return Quaternion.identity;
            Vector3 normal = Vector3.zero;
            foreach (var f in faces) { var pl = poly.Plane(f); normal += new Vector3(pl.x, pl.y, pl.z); }
            if (normal.sqrMagnitude < 1e-8f) { var pl = poly.Plane(faces[0]); normal = new Vector3(pl.x, pl.y, pl.z); } // opposite faces cancel: use the first
            normal.Normalize();
            up -= normal * Vector3.Dot(up, normal);
            if (up.sqrMagnitude < 1e-8f) up = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
            return Quaternion.LookRotation(normal, up.normalized);
        }

        /// <summary>World rotation of the gizmo for a brush's selection under the current orientation setting.</summary>
        public static Quaternion HandleRotation(Brush brush, BrushPolyhedron poly, Selection sel)
        {
            switch (Orientation)
            {
                case BrushHandleOrientation.Local: return brush.transform.rotation;
                case BrushHandleOrientation.Element: return brush.transform.rotation * ElementRotation(poly, Mode, sel);
                default: return Quaternion.identity;
            }
        }

        public static Vector3 SelectionCentre(Brush brush, BrushPolyhedron poly, HashSet<int> vertices)
        {
            var c = Vector3.zero; foreach (var v in vertices) c += brush.transform.TransformPoint(poly.vertices[v]);
            return vertices.Count > 0 ? c / vertices.Count : brush.transform.position;
        }

        public static void BeginDrag(Brush brush, BrushPolyhedron poly, HashSet<int> vertices, Vector3 origin)
        {
            if (dragging && dragBrush == brush) return;
            dragging = true; dragBrush = brush; dragStart = poly.Clone(); dragVertices = vertices; dragOrigin = origin;
        }

        /// <summary>
        /// Apply a world-space transform to the dragged vertices of the shape as it was at drag start: positions
        /// are rounded to the grid when snapping is on, bent faces split, coincident vertices weld, and an open
        /// or inverted result is refused.
        /// </summary>
        public static void ApplyDrag(Brush brush, Selection sel, Func<Vector3, Vector3> worldTransform)
        {
            var settings = BrushSettings.instance;
            var t = brush.transform;
            var result = dragStart.Clone();
            foreach (var v in dragVertices)
            {
                var world = worldTransform(t.TransformPoint(dragStart.vertices[v]));
                if (settings.snapToGrid) world = BrushSnap.Round(world, settings.GridMeters);
                result.vertices[v] = t.InverseTransformPoint(world);
            }
            foreach (var v in dragVertices) result.EnsurePlanar(v);
            var remap = result.WeldCoincident(settings.snapToGrid ? settings.GridMeters * 0.25f : 1e-3f);
            if (!result.IsSound(out _)) return; // the step would break the shape: keep the last valid one
            BrushApi.SetPolyhedron(brush, result);
            if (result.vertices.Length != dragStart.vertices.Length) Remap(sel, remap, result);
        }

        static void Remap(Selection sel, int[] remap, BrushPolyhedron result)
        {
            var v = new HashSet<int>(); foreach (var i in sel.vertices) if (i < remap.Length) v.Add(remap[i]); sel.vertices = v;
            var ed = new HashSet<long>(); foreach (var k in sel.edges) { int a = remap[(int)(k >> 32)], b = remap[(int)(k & 0xffffffffL)]; if (a != b) ed.Add(EdgeKey(a, b)); } sel.edges = ed;
            var f = new HashSet<int>(); foreach (var i in sel.faces) if (i < result.faces.Length) f.Add(i); sel.faces = f;
        }
    }

    /// <summary>Move tool inside brush edit mode: Unity's position gizmo on the selection, world-grid snapped.</summary>
    [EditorTool("Move brush selection", typeof(Brush), typeof(BrushEditContext))]
    /// <summary>The three tools of the edit context share the Tool Settings toolbar (see BrushEditToolbar).</summary>
    public abstract class BrushSelectionTool : EditorTool { }

    public sealed class BrushMoveTool : BrushSelectionTool
    {
        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var brush in BrushEditState.SelectedBrushes())
            {
                var poly = BrushEditState.ShapeOf(brush); if (poly == null) continue;
                var sel = BrushEditState.Sel(brush);
                var moving = BrushEditState.SelectedVertices(poly, sel);
                if (moving.Count == 0) continue;
                var centre = BrushEditState.dragging && BrushEditState.dragBrush == brush ? BrushEditState.dragOrigin + offset : BrushEditState.SelectionCentre(brush, poly, moving);
                if (!BrushEditState.dragging) frame = BrushEditState.HandleRotation(brush, poly, sel); // fixed for the whole drag
                EditorGUI.BeginChangeCheck();
                var moved = Handles.PositionHandle(centre, frame);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!BrushEditState.dragging || BrushEditState.dragBrush != brush) { BrushEditState.BeginDrag(brush, poly, moving, centre); offset = Vector3.zero; }
                    offset = moved - BrushEditState.dragOrigin;
                    var delta = offset;
                    BrushEditState.ApplyDrag(brush, sel, p => p + delta);
                }
            }
        }
        Vector3 offset; Quaternion frame = Quaternion.identity;
    }

    /// <summary>Rotate tool inside brush edit mode: the selection turns about its centre in rotation-snap steps; positions land on the grid.</summary>
    [EditorTool("Rotate brush selection", typeof(Brush), typeof(BrushEditContext))]
    public sealed class BrushRotateTool : BrushSelectionTool
    {
        Quaternion start = Quaternion.identity, current = Quaternion.identity;
        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var brush in BrushEditState.SelectedBrushes())
            {
                var poly = BrushEditState.ShapeOf(brush); if (poly == null) continue;
                var sel = BrushEditState.Sel(brush);
                var moving = BrushEditState.SelectedVertices(poly, sel);
                if (moving.Count == 0) continue;
                var centre = BrushEditState.dragging && BrushEditState.dragBrush == brush ? BrushEditState.dragOrigin : BrushEditState.SelectionCentre(brush, poly, moving);
                if (!BrushEditState.dragging) current = start = BrushEditState.HandleRotation(brush, poly, sel);
                EditorGUI.BeginChangeCheck();
                var rotated = Handles.RotationHandle(current, centre);
                if (EditorGUI.EndChangeCheck())
                {
                    BrushEditState.BeginDrag(brush, poly, moving, centre);
                    current = rotated;
                    var settings = BrushSettings.instance;
                    var delta = rotated * Quaternion.Inverse(start);
                    if (settings.snapToGrid) delta = BrushSnap.SnapRotation(delta, settings.rotationSnapDegrees);
                    var c = BrushEditState.dragOrigin;
                    BrushEditState.ApplyDrag(brush, sel, p => c + delta * (p - c));
                }
            }
        }
    }

    /// <summary>Scale tool inside brush edit mode: the selection scales about its centre; positions land on the grid.</summary>
    [EditorTool("Scale brush selection", typeof(Brush), typeof(BrushEditContext))]
    public sealed class BrushScaleTool : BrushSelectionTool
    {
        Vector3 current = Vector3.one; Quaternion frame = Quaternion.identity;
        public override void OnToolGUI(EditorWindow window)
        {
            foreach (var brush in BrushEditState.SelectedBrushes())
            {
                var poly = BrushEditState.ShapeOf(brush); if (poly == null) continue;
                var sel = BrushEditState.Sel(brush);
                var moving = BrushEditState.SelectedVertices(poly, sel);
                if (moving.Count == 0) continue;
                var centre = BrushEditState.dragging && BrushEditState.dragBrush == brush ? BrushEditState.dragOrigin : BrushEditState.SelectionCentre(brush, poly, moving);
                if (!BrushEditState.dragging) { current = Vector3.one; frame = BrushEditState.HandleRotation(brush, poly, sel); }
                EditorGUI.BeginChangeCheck();
                var scaled = Handles.ScaleHandle(current, centre, frame, HandleUtility.GetHandleSize(centre));
                if (EditorGUI.EndChangeCheck())
                {
                    BrushEditState.BeginDrag(brush, poly, moving, centre);
                    current = scaled;
                    var c = BrushEditState.dragOrigin; var s = scaled; var r = frame; var ri = Quaternion.Inverse(frame);
                    BrushEditState.ApplyDrag(brush, sel, p => c + r * Vector3.Scale(ri * (p - c), s));
                }
            }
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>A Spline's Inspector: the profile's settings that apply to it, the tolerance override, Edit and Draw.</summary>
    [CustomEditor(typeof(Spline))]
    public sealed class SplineEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var spline = (Spline)target;
            serializedObject.Update();
            var profile = serializedObject.FindProperty("profile");
            EditorGUILayout.PropertyField(profile);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("size"));
            var kind = (Spline.Profile)profile.enumValueIndex;
            if (kind == Spline.Profile.Circle) EditorGUILayout.PropertyField(serializedObject.FindProperty("sides"));
            if (kind != Spline.Profile.UChannel)
            {
                var hollow = serializedObject.FindProperty("hollow");
                EditorGUILayout.PropertyField(hollow);
                if (hollow.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("thickness"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("ends"));
                    EditorGUI.indentLevel--;
                }
            }
            else EditorGUILayout.PropertyField(serializedObject.FindProperty("thickness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("orientation"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("material"));
            serializedObject.ApplyModifiedProperties();
            PointGraphGUI.Tolerance(serializedObject);

            spline.EnsureGraph();
            EditorGUILayout.LabelField("Path", spline.points.Count + " points, " + spline.links.Count + " links");
            if (TighterThanProfile(spline)) EditorGUILayout.HelpBox("Curve is tighter than the profile: its inside folds over.", MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            bool editing = PointGraphEditContext.IsActive;
            if (GUILayout.Toggle(editing, new GUIContent("Edit", "Move, Rotate and Scale act on the selected points or links (1, 2)"), EditorStyles.miniButton) != editing)
            {
                if (editing) PointGraphEditContext.Exit(); else PointGraphEditContext.Enter();
            }
            bool drawing = ToolManager.activeToolType == typeof(SplineDrawTool);
            if (GUILayout.Toggle(drawing, new GUIContent("Draw", "Click to add points; Enter or Escape finishes"), EditorStyles.miniButton) != drawing)
            {
                if (drawing) ToolManager.RestorePreviousPersistentTool();
                else { PointGraphEditContext.Exit(); ToolManager.SetActiveTool<SplineDrawTool>(); }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Does the path bend anywhere more tightly than half the profile's width (its inside would fold)?</summary>
        public static bool TighterThanProfile(Spline spline)
        {
            float half = Mathf.Max(spline.size.x, spline.size.y) * 0.5f;
            foreach (var run in spline.Runs())
            {
                var p = run.points; int n = p.Count;
                for (int i = 1; i + 1 < n || (run.closed && i < n); i++)
                {
                    var a = p[i - 1]; var b = p[i]; var c = p[(i + 1) % n];
                    float angle = Vector3.Angle(b - a, c - b) * Mathf.Deg2Rad;
                    if (angle < 1e-3f) continue;
                    float radius = Mathf.Min((b - a).magnitude, (c - b).magnitude) * 0.5f / Mathf.Tan(angle * 0.5f); // the arc through the joint
                    if (radius < half && angle < Spline.SharpTurnDegrees * Mathf.Deg2Rad) return true;
                }
            }
            return false;
        }

        void OnSceneGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (ToolManager.activeToolType == typeof(SplineDrawTool) || PointGraphEditContext.IsActive) return;
            FloorPlanTools.DrawOutline((Spline)target);
        }
    }

    /// <summary>
    /// Draw a spline: click to add a point and a link to it, on the surface under the mouse (the profile resting on it) or
    /// level with the last point. A click on a point or a link joins the stroke to it (splitting the link there) and ends
    /// the stroke. Backspace takes back the last link; Escape or a double click ends the stroke, Enter or a second Escape
    /// leaves the tool.
    /// </summary>
    [EditorTool("Draw Spline", typeof(Spline))]
    public sealed class SplineDrawTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), "Draw Spline");

        enum Hover { None, Free, Point, Link }
        Hover m_Kind; Vector3 m_Hover; int m_HoverPoint = -1, m_HoverLink = -1;
        int m_Last = -1;
        readonly List<int> m_Stroke = new List<int>();

        public override void OnActivated() { m_Last = -1; m_Stroke.Clear(); }

        public override void OnToolGUI(EditorWindow window)
        {
            var spline = target as Spline;
            if (spline == null || !(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (m_Last >= 0 && spline.PointIndex(m_Last) < 0) { m_Last = -1; m_Stroke.Clear(); } // undone
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.Repaint) FindHover(spline, e);
            if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && m_Kind != Hover.None)
            {
                BrushHooks.TakeClick();
                e.Use();
                if (e.clickCount == 2) { EndStroke(); return; }
                Undo.RecordObject(spline, "Draw spline");
                bool joined = m_Kind != Hover.Free;
                int point = AddAt(spline, m_Kind == Hover.Point ? m_HoverPoint : -1, m_Kind == Hover.Link ? m_HoverLink : -1, m_Hover);
                bool started = m_Last >= 0;
                if (started && point != m_Last) spline.AddLink(m_Last, point);
                BrushGenerators.MarkDirty(spline);
                if (joined && started && point != m_Last) EndStroke();
                else { m_Last = point; m_Stroke.Add(point); }
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && m_Stroke.Count >= 2)
                {
                    Undo.RecordObject(spline, "Remove spline link");
                    int last = m_Stroke[m_Stroke.Count - 1], prev = m_Stroke[m_Stroke.Count - 2];
                    spline.links.RemoveAll(l => (l.start == prev && l.end == last) || (l.start == last && l.end == prev));
                    spline.RemoveLonePoints();
                    m_Stroke.RemoveAt(m_Stroke.Count - 1); m_Last = prev;
                    BrushGenerators.MarkDirty(spline); e.Use();
                }
                else if (e.keyCode == KeyCode.Escape) { if (m_Last >= 0) EndStroke(); else ToolManager.RestorePreviousPersistentTool(); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { ToolManager.RestorePreviousPersistentTool(); e.Use(); }
            }
            if (e.type == EventType.Repaint) DrawPreview(spline);
        }

        /// <summary>The point a click makes: an existing point (by index), a new one splitting a link, or a new one (local). Returns its id.</summary>
        public static int AddAt(Spline spline, int pointIndex, int link, Vector3 local)
        {
            if (pointIndex >= 0) return spline.pointIds[pointIndex];
            if (link >= 0) return spline.SplitLink(link, local);
            return spline.AddPoint(local);
        }

        void EndStroke() { m_Last = -1; m_Stroke.Clear(); SceneView.RepaintAll(); }

        void FindHover(Spline spline, Event e)
        {
            m_Kind = Hover.None;
            int point = FloorPlanTools.PointUnderMouse(spline, e.mousePosition);
            if (point >= 0) { m_Kind = Hover.Point; m_HoverPoint = point; m_Hover = spline.points[point]; return; }
            int link = FloorPlanTools.WallUnderMouse(spline, e.mousePosition, out var onLink);
            if (link >= 0) { m_Kind = Hover.Link; m_HoverLink = link; m_Hover = onLink; return; }
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            int last = m_Last >= 0 ? spline.PointIndex(m_Last) : -1;
            if (FreePoint(spline, ray, last >= 0 ? FloorPlanTools.World(spline, spline.points[last]) : (Vector3?)null, out var world))
            {
                m_Kind = Hover.Free;
                m_Hover = spline.transform.InverseTransformPoint(world);
            }
        }

        /// <summary>
        /// Where a click in the open lands, in world space: on the surface of a brush under the mouse, raised so the profile
        /// rests on it; else level with the last point (the spline's own height when there is none). On the grid across the
        /// surface when snapping is on.
        /// </summary>
        public static bool FreePoint(Spline spline, Ray ray, Vector3? last, out Vector3 world)
        {
            float g = BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;
            var hit = BrushHooks.PickBrushSurface(ray, out var point, out var normal);
            if (hit != null && hit.generatedBy != spline) // never on its own pieces
            {
                float rest = spline.profile == Spline.Profile.Circle ? spline.size.x * 0.5f : spline.size.y * 0.5f;
                world = SnapAcross(point, normal, g) + normal * rest;
                return true;
            }
            float height = last.HasValue ? last.Value.y : spline.transform.position.y;
            var level = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!level.Raycast(ray, out float t) || t <= 0f) { world = default; return false; }
            world = SnapAcross(ray.GetPoint(t), Vector3.up, g);
            return true;
        }

        /// <summary>A point on a surface moved to the grid along the surface (not off it).</summary>
        static Vector3 SnapAcross(Vector3 p, Vector3 normal, float g)
        {
            if (g <= 0f) return p;
            var snapped = new Vector3(Mathf.Round(p.x / g) * g, Mathf.Round(p.y / g) * g, Mathf.Round(p.z / g) * g);
            var n = normal.normalized;
            return snapped - n * Vector3.Dot(snapped - p, n); // back onto the surface's plane
        }

        void DrawPreview(Spline spline)
        {
            FloorPlanTools.DrawOutline(spline);
            if (m_Kind == Hover.None) return;
            var hover = FloorPlanTools.World(spline, m_Hover);
            bool joining = m_Kind == Hover.Point || m_Kind == Hover.Link;
            Handles.color = joining ? Color.white : FloorPlanTools.Line;
            Handles.DotHandleCap(0, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * 0.06f, EventType.Repaint);
            int last = m_Last >= 0 ? spline.PointIndex(m_Last) : -1;
            if (last < 0) return;
            Handles.color = FloorPlanTools.Line;
            Handles.DrawAAPolyLine(3f, FloorPlanTools.World(spline, spline.points[last]), hover);
        }
    }

    static class SplineMenu
    {
        [MenuItem("GameObject/Brush/Spline", false, 41)]
        static void Create()
        {
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            float g = BrushSettings.instance.GridMeters;
            if (g > 0f) pivot = new Vector3(Mathf.Round(pivot.x / g) * g, 0f, Mathf.Round(pivot.z / g) * g);
            var parent = Selection.activeTransform != null && Selection.activeTransform.GetComponent<Brush>() == null ? Selection.activeTransform : null;
            var go = new GameObject("Spline");
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags);
            Undo.RegisterCreatedObjectUndo(go, "Create spline");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Create spline");
            go.transform.position = pivot;
            Undo.AddComponent<Spline>(go);
            Selection.activeGameObject = go;
            EditorApplication.delayCall += () =>
            {
                if (Selection.activeGameObject != go) return;
                ActiveEditorTracker.sharedTracker.ForceRebuild(); // a new selection's component tools are made a little later
                ToolManager.SetActiveTool<SplineDrawTool>();
            };
        }
    }
}

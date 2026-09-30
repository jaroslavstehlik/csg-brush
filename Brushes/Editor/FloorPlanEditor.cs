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
            int n = plan.points.Count;
            if (n < 2) return;
            var pts = new Vector3[plan.closed && n > 2 ? n + 1 : n];
            for (int i = 0; i < n; i++) pts[i] = World(plan, plan.points[i]);
            if (pts.Length > n) pts[n] = pts[0];
            Handles.color = Line;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.DrawAAPolyLine(3f, pts);
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
            EditorGUILayout.LabelField("Points", plan.points.Count.ToString());
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
        }

        void OnSceneGUI()
        {
            // the draw tool and edit mode show their own
            if (ToolManager.activeToolType == typeof(FloorPlanDrawTool) || FloorPlanEditContext.IsActive) return;
            FloorPlanTools.DrawOutline((FloorPlan)target);
        }
    }

    /// <summary>
    /// Draw walls: click on the floor to add a point, snapped to the grid and to 45 degree steps (Shift: any grid point).
    /// Clicking the first point closes the room; Backspace removes the last point; Enter, Escape or a double click finishes.
    /// </summary>
    [EditorTool("Draw Floor Plan", typeof(FloorPlan))]
    public sealed class FloorPlanDrawTool : EditorTool
    {
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get("FloorPlan"), "Draw Floor Plan");

        Vector3 m_Hover; bool m_HoverValid;

        public override void OnToolGUI(EditorWindow window)
        {
            var plan = target as FloorPlan;
            if (plan == null || !(window is SceneView)) return;
            var e = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            var pts = plan.points;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown || e.type == EventType.Repaint)
            {
                m_HoverValid = FloorPlanTools.MouseOnFloor(plan, e.mousePosition, out var floor);
                if (m_HoverValid)
                {
                    m_Hover = FloorPlanTools.SnapLocal(plan, floor);
                    if (pts.Count > 0) m_Hover = FloorPlanTools.Constrain(pts[pts.Count - 1], m_Hover, e.shift);
                }
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            // the first point closes the room when the mouse is near it on screen, whatever the snapping would give
            bool nearFirst = pts.Count > 2 && !plan.closed && (HandleUtility.WorldToGUIPoint(FloorPlanTools.World(plan, pts[0])) - e.mousePosition).sqrMagnitude < 12f * 12f;
            if (nearFirst) { m_Hover = pts[0]; m_HoverValid = true; }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id && m_HoverValid)
            {
                if (e.clickCount == 2) { Finish(); e.Use(); return; }
                Undo.RecordObject(plan, "Draw floor plan");
                if (nearFirst) { plan.closed = true; BrushGenerators.MarkDirty(plan); e.Use(); Finish(); return; }
                if (pts.Count == 0 || (pts[pts.Count - 1] - m_Hover).sqrMagnitude > 1e-8f) plan.AddPoint(m_Hover);
                if (plan.closed) plan.closed = false; // drawing on continues the outline
                BrushGenerators.MarkDirty(plan);
                e.Use();
            }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace && pts.Count > 0) { Undo.RecordObject(plan, "Remove floor plan point"); plan.RemovePointAt(pts.Count - 1); BrushGenerators.MarkDirty(plan); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape) { Finish(); e.Use(); }
            }
            if (e.type == EventType.Repaint)
            {
                FloorPlanTools.DrawOutline(plan);
                if (m_HoverValid)
                {
                    var hover = FloorPlanTools.World(plan, m_Hover);
                    Handles.color = nearFirst ? Color.white : FloorPlanTools.Line;
                    Handles.DotHandleCap(-1, hover, Quaternion.identity, HandleUtility.GetHandleSize(hover) * (nearFirst ? 0.1f : 0.06f), EventType.Repaint);
                    if (pts.Count > 0)
                    {
                        var last = FloorPlanTools.World(plan, pts[pts.Count - 1]);
                        Handles.color = FloorPlanTools.Preview;
                        Handles.DrawDottedLine(last, hover, 4f);
                        float length = new Vector2(m_Hover.x - pts[pts.Count - 1].x, m_Hover.z - pts[pts.Count - 1].z).magnitude;
                        Handles.Label((last + hover) * 0.5f, BrushSettings.instance.FormatUnits(length), EditorStyles.whiteMiniLabel);
                    }
                }
            }
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
            if (firstPoint) plan.AddPoint(Vector3.zero);
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

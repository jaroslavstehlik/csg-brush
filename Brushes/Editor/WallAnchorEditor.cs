using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// A Wall Anchor's Inspector: Pick Wall, then click a wall in the Scene view; the wall it is on is outlined while it is
    /// selected. Distance along the wall, height and face below.
    /// </summary>
    [CustomEditor(typeof(WallAnchor)), CanEditMultipleObjects]
    sealed class WallAnchorEditor : UnityEditor.Editor
    {
        /// <summary>Picking a wall in the Scene view: other clicks there are ignored meanwhile.</summary>
        public static bool Picking { get; private set; }

        static readonly List<FloorPlan.Wall> s_Walls = new List<FloorPlan.Wall>();
        static readonly Color HoverColor = new Color(1f, 1f, 1f, 0.9f);
        FloorPlan m_HoverPlan; int m_HoverWall = -1;

        void OnDisable() { Picking = false; }

        public override void OnInspectorGUI()
        {
            var a = (WallAnchor)target;
            if (a.Plan == null) EditorGUILayout.HelpBox("Not on a floor plan: pick a wall to put it on one.", MessageType.Info);
            bool picking = GUILayout.Toggle(Picking, new GUIContent("Pick Wall", "Click a wall in the Scene view to put this on it; Escape cancels"), EditorStyles.miniButton);
            if (picking != Picking) { Picking = picking; m_HoverPlan = null; SceneView.RepaintAll(); }
            if (a.Plan == null) return;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WallAnchor.distance)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WallAnchor.height)));
            bool opening = a.TryGetComponent<Brush>(out var brush) && brush.IsOpening;
            if (!opening) EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WallAnchor.face))); // a door or window always goes through
            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI()
        {
            var e = Event.current;
            var a = (WallAnchor)target;
            if (Picking) PickGUI(e);
            if (e.type != EventType.Repaint) return;
            var plan = a.Plan;
            if (plan != null)
            {
                plan.Walls(s_Walls);
                int i = WallAnchors.WallIndex(a, s_Walls);
                if (i >= 0) DrawWall(plan, s_Walls[i], BrushEditState.SelectedColor);
            }
            if (Picking && m_HoverPlan != null && m_HoverWall >= 0)
            {
                m_HoverPlan.Walls(s_Walls);
                if (m_HoverWall < s_Walls.Count) DrawWall(m_HoverPlan, s_Walls[m_HoverWall], HoverColor);
            }
        }

        void PickGUI(Event e)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(id);
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { Picking = false; e.Use(); Repaint(); SceneView.RepaintAll(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                var hit = BrushHooks.PickBrushSurface(e.mousePosition, out var point, out _);
                m_HoverPlan = hit != null ? hit.generatedBy as FloorPlan : null;
                m_HoverWall = -1;
                if (m_HoverPlan != null)
                {
                    var local = m_HoverPlan.transform.InverseTransformPoint(point);
                    if (WallAnchors.NearestWall(m_HoverPlan, new Vector2(local.x, local.z), out var wall, out _, out _)) m_HoverWall = wall.index;
                }
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && HandleUtility.nearestControl == id)
            {
                if (m_HoverPlan != null && m_HoverWall >= 0)
                {
                    int group = Undo.GetCurrentGroup();
                    foreach (var go in Selection.gameObjects) if (go.TryGetComponent<WallAnchor>(out var picked)) WallAnchors.SetWall(picked, m_HoverPlan, m_HoverWall); // every selected one (targets is off limits here)
                    Undo.CollapseUndoOperations(group);
                    Picking = false;
                    Repaint();
                }
                GUIUtility.hotControl = id; e.Use(); SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
        }

        /// <summary>A wall's outline in its middle, floor to top, over everything.</summary>
        static void DrawWall(FloorPlan plan, FloorPlan.Wall w, Color color)
        {
            float middle = (w.inner + w.outer) * 0.5f;
            var p0 = w.a + w.outward * middle; var p1 = w.b + w.outward * middle;
            var t = plan.transform;
            Vector3 P(Vector2 p, float y) => t.TransformPoint(new Vector3(p.x, y, p.y));
            float h = plan.wallHeight;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = color;
            Handles.DrawAAPolyLine(4f, P(p0, 0f), P(p1, 0f), P(p1, h), P(p0, h), P(p0, 0f));
        }
    }
}

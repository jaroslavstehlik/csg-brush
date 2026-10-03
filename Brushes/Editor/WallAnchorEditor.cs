using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// A Wall Anchor's Inspector: Pick Wall, then click a wall in the Scene view; the wall it is on is outlined while it is
    /// selected. Distance along the wall, height, face, offset and rotation relative to it below.
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
            if (!opening) // a door or window always goes through, in the middle of the wall
            {
                FaceField(serializedObject.FindProperty(nameof(WallAnchor.face)));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(WallAnchor.offset)));
            }
            serializedObject.ApplyModifiedProperties();
            if (!opening) RotationField();
        }

        static readonly WallFace[] s_FaceOrder = { WallFace.Outside, WallFace.Centered, WallFace.Inside }; // as the floor plan's Side
        static GUIContent[] s_FaceNames;

        static void FaceField(SerializedProperty face)
        {
            s_FaceNames ??= System.Array.ConvertAll(s_FaceOrder, f => new GUIContent(f.ToString()));
            var rect = EditorGUILayout.GetControlRect();
            var label = EditorGUI.BeginProperty(rect, new GUIContent("Face", "On the wall's outside face, in its middle, or on its inside face."), face);
            EditorGUI.showMixedValue = face.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUI.Popup(rect, label, System.Array.IndexOf(s_FaceOrder, (WallFace)face.intValue), s_FaceNames);
            if (EditorGUI.EndChangeCheck() && picked >= 0) face.intValue = (int)s_FaceOrder[picked];
            EditorGUI.showMixedValue = false;
            EditorGUI.EndProperty();
        }

        Vector3 m_Euler; Quaternion m_EulerOf = new Quaternion(0f, 0f, 0f, 0f); // what was typed, kept while it still matches (as the Transform Inspector does)

        /// <summary>The rotation relative to the wall, as angles, and Turn around: 180 degrees about the wall's up.</summary>
        void RotationField()
        {
            var first = (WallAnchor)target; bool mixed = false;
            foreach (var t in targets) if (Quaternion.Angle(((WallAnchor)t).rotation, first.rotation) > 1e-3f) mixed = true;
            if (Quaternion.Angle(m_EulerOf, first.rotation) > 1e-3f || m_EulerOf == new Quaternion(0f, 0f, 0f, 0f)) { m_Euler = Tidy(first.rotation.eulerAngles); m_EulerOf = first.rotation; }
            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = mixed;
            var euler = EditorGUILayout.Vector3Field(new GUIContent("Rotation", "Relative to the wall: 0, 0, 0 faces out of it, up is up."), m_Euler);
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck()) { m_Euler = euler; m_EulerOf = Quaternion.Euler(euler); SetRotation(_ => Quaternion.Euler(euler), "Rotate on wall"); }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth + 2f);
            if (GUILayout.Button(new GUIContent("Turn around", "Turns the object 180 degrees about the wall's up axis."), EditorStyles.miniButton))
                SetRotation(r => Quaternion.Euler(0f, 180f, 0f) * r, "Turn around on wall");
            EditorGUILayout.EndHorizontal();
        }

        void SetRotation(System.Func<Quaternion, Quaternion> change, string undo)
        {
            foreach (var t in targets)
            {
                var a = (WallAnchor)t;
                Undo.RecordObject(a, undo);
                a.rotation = change(a.rotation).normalized;
                EditorUtility.SetDirty(a);
                WallAnchor.Changed?.Invoke(a);
            }
        }

        /// <summary>Angles near whole numbers as whole numbers, and 360 as 0.</summary>
        static Vector3 Tidy(Vector3 e)
        {
            float T(float v) { v = Mathf.Abs(v - Mathf.Round(v)) < 1e-3f ? Mathf.Round(v) : v; return v >= 360f ? v - 360f : v; }
            return new Vector3(T(e.x), T(e.y), T(e.z));
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
                var hit = BrushHooks.PickBrushSurface(e.mousePosition, out var point, out var hitNormal);
                m_HoverPlan = hit != null && Mathf.Abs(hitNormal.y) < 0.5f ? hit.generatedBy as FloorPlan : null; // a wall's side, not the floor or ceiling
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

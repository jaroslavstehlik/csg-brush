using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Extrude settings and confirmation, opened from the Extrude button in the Brushes overlay: distance, whole
    /// selection or individual faces, then Extrude. The faces stay selected, so a second press extrudes again.
    /// </summary>
    [Overlay(typeof(SceneView), PanelId, "Extrude", false)]
    public sealed class BrushExtrudeOverlay : Overlay
    {
        public const string PanelId = "CSG Brush/Extrude";

        public static void Toggle()
        {
            foreach (SceneView view in SceneView.sceneViews)
                if (view.TryGetOverlay(PanelId, out var overlay)) overlay.displayed = !overlay.displayed;
        }

        public static void Hide()
        {
            foreach (SceneView view in SceneView.sceneViews)
                if (view.TryGetOverlay(PanelId, out var overlay)) overlay.displayed = false;
        }

        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(Draw);
            container.style.minWidth = 200;
            return container;
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            EditorGUIUtility.labelWidth = 70;
            float placeholder = s.ToUnits(s.GridMeters);
            EditorGUI.BeginChangeCheck();
            float d = EditorGUILayout.FloatField(new GUIContent("Distance (" + s.unitLabel + ")", "Negative cuts a pocket; 0 uses one grid step"), s.extrudeDistance != 0f ? s.extrudeDistance : placeholder);
            if (EditorGUI.EndChangeCheck()) s.extrudeDistance = d;
            int mode = EditorGUILayout.Popup(new GUIContent("Faces"), s.extrudeIndividual ? 1 : 0, new[] { "Whole selection", "Individual" });
            s.extrudeIndividual = mode == 1;
            int faces = 0;
            foreach (var b in BrushEditState.SelectedBrushes()) faces += BrushEditState.Sel(b).faces.Count;
            using (new EditorGUI.DisabledScope(faces == 0 || BrushEditState.Mode != BrushEditMode.Face || !BrushEditContext.IsActive))
            {
                if (GUILayout.Button(faces > 0 ? "Extrude " + faces + (faces == 1 ? " face" : " faces") : "Extrude"))
                    Extrude(s.ToMeters(s.extrudeDistance != 0f ? s.extrudeDistance : placeholder), s.extrudeIndividual);
            }
        }

        public static void Extrude(float meters, bool individual)
        {
            int group = Undo.GetCurrentGroup();
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                var sel = BrushEditState.Sel(b);
                if (sel.faces.Count == 0) continue;
                var remap = BrushApi.ExtrudeFaces(b, new System.Collections.Generic.List<int>(sel.faces), meters, individual);
                if (remap == null) continue;
                var kept = new System.Collections.Generic.HashSet<int>();
                foreach (var f in sel.faces) if (f < remap.Length && remap[f] >= 0) kept.Add(remap[f]);
                sel.faces = kept; sel.vertices.Clear(); sel.edges.Clear();
            }
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            SceneView.RepaintAll();
        }
    }
}

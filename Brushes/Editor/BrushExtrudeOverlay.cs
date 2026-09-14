using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Shown with brush edit mode: extrude the selected faces by a distance, each face on its own or the whole
    /// selection as one, then confirm. The faces stay selected, so a second press extrudes again.
    /// </summary>
    [Overlay(typeof(SceneView), BrushEditContext.ExtrudePanelId, "Extrude", false)]
    public sealed class BrushExtrudeOverlay : Overlay
    {
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
            float shown = s.extrudeDistance != 0f ? s.extrudeDistance : placeholder;
            float d = EditorGUILayout.FloatField(new GUIContent("Distance (" + s.unitLabel + ")", "Negative cuts a pocket; 0 uses one grid step"), shown);
            if (EditorGUI.EndChangeCheck()) s.extrudeDistance = d;
            int mode = EditorGUILayout.Popup(new GUIContent("Faces"), s.extrudeIndividual ? 1 : 0, new[] { "Whole selection", "Individual" });
            s.extrudeIndividual = mode == 1;
            int faces = 0;
            foreach (var b in BrushEditState.SelectedBrushes()) faces += BrushEditState.Sel(b).faces.Count;
            using (new EditorGUI.DisabledScope(faces == 0 || BrushEditState.Mode != BrushEditMode.Face))
            {
                if (GUILayout.Button(faces > 0 ? "Extrude " + faces + (faces == 1 ? " face" : " faces") : "Extrude"))
                {
                    float meters = s.ToMeters(s.extrudeDistance != 0f ? s.extrudeDistance : placeholder);
                    int group = Undo.GetCurrentGroup();
                    foreach (var b in BrushEditState.SelectedBrushes())
                    {
                        var sel = BrushEditState.Sel(b);
                        if (sel.faces.Count == 0) continue;
                        if (BrushApi.ExtrudeFaces(b, new System.Collections.Generic.List<int>(sel.faces), meters, s.extrudeIndividual)) { sel.vertices.Clear(); sel.edges.Clear(); }
                    }
                    Undo.CollapseUndoOperations(group);
                    BrushApi.ForceUpdate();
                    SceneView.RepaintAll();
                }
            }
        }
    }
}

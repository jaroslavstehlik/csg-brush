using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Extrude settings, shown with brush edit mode: distance and whole selection or individual faces. The Extrude
    /// button in the Brushes overlay applies them. The faces stay selected, so a second press extrudes again.
    /// </summary>
    [Overlay(typeof(SceneView), PanelId, "Extrude", false)]
    public sealed class BrushExtrudeOverlay : Overlay
    {
        public const string PanelId = "CSG Brush/Extrude";

        public static void Show(bool show)
        {
            foreach (SceneView view in SceneView.sceneViews)
                if (view.TryGetOverlay(PanelId, out var overlay)) overlay.displayed = show;
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
            if (!string.IsNullOrEmpty(lastRefusal)) EditorGUILayout.HelpBox(lastRefusal, MessageType.Info);
        }

        /// <summary>Why the last extrude left a brush untouched, shown in the panel until the next extrude.</summary>
        static string lastRefusal;

        /// <summary>The one brush with exactly two faces selected, or null (the Bridge button's condition).</summary>
        public static Brush BridgeCandidate()
        {
            Brush found = null;
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                int count = BrushEditState.Sel(b).faces.Count;
                if (count == 0) continue;
                if (count != 2 || found != null) return null;
                found = b;
            }
            return found;
        }

        /// <summary>Bridge the two selected faces of the selected brush (the Bridge button in the Brushes overlay); the new walls end up selected.</summary>
        public static void BridgeSelection()
        {
            var b = BridgeCandidate();
            if (b == null) return;
            int group = Undo.GetCurrentGroup();
            lastRefusal = null;
            var sel = BrushEditState.Sel(b);
            var two = new System.Collections.Generic.List<int>(sel.faces);
            var remap = BrushApi.BridgeFaces(b, two[0], two[1]);
            if (remap == null) { lastRefusal = BrushBoolean.LastRefusal; SceneView.RepaintAll(); return; }
            var walls = new System.Collections.Generic.HashSet<int>();
            for (int f = 0; f < b.polyhedron.faces.Length; f++) if (b.polyhedron.faces[f].source == two[0] && (f >= remap.Length || remap[two[0]] != f)) walls.Add(f);
            sel.faces = walls; sel.vertices.Clear(); sel.edges.Clear();
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            SceneView.RepaintAll();
        }

        /// <summary>Extrude the selected faces with the panel's settings (the Extrude button in the Brushes overlay).</summary>
        public static void ExtrudeSelection()
        {
            var s = BrushSettings.instance;
            float meters = s.ToMeters(s.extrudeDistance != 0f ? s.extrudeDistance : s.ToUnits(s.GridMeters));
            Extrude(meters, s.extrudeIndividual);
        }

        public static void Extrude(float meters, bool individual)
        {
            int group = Undo.GetCurrentGroup();
            lastRefusal = null;
            foreach (var b in BrushEditState.SelectedBrushes())
            {
                var sel = BrushEditState.Sel(b);
                if (sel.faces.Count == 0) continue;
                var remap = BrushApi.ExtrudeFaces(b, new System.Collections.Generic.List<int>(sel.faces), meters, individual);
                if (remap == null) { lastRefusal = BrushBoolean.LastRefusal; continue; }
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

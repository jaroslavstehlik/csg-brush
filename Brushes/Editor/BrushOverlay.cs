using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>Scene view overlay: grid size and snap, the edit mode toggle, lint.</summary>
    [Overlay(typeof(SceneView), "Brushes", true)]
    public sealed class BrushOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var container = new IMGUIContainer(Draw);
            container.style.minWidth = 220;
            return container;
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            EditorGUILayout.BeginVertical();

            // grid row: icon, size dropdown, snap toggle
            EditorGUILayout.BeginHorizontal();
            var gridIcon = EditorGUIUtility.IconContent("GridAxisY");
            GUILayout.Label(gridIcon != null && gridIcon.image != null ? new GUIContent(gridIcon.image, "Grid size ([ and ] change it)") : new GUIContent("Grid"), GUILayout.Width(20), GUILayout.Height(18));
            var names = System.Array.ConvertAll(s.gridSizes, g => g.ToString("0.###") + " " + s.unitLabel);
            int idx = EditorGUILayout.Popup(Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names, GUILayout.Width(74));
            if (idx != s.gridIndex) s.SetGridIndex(idx);
            var snapIcon = EditorGUIUtility.IconContent("SceneViewSnap");
            var snapContent = snapIcon != null && snapIcon.image != null ? new GUIContent(snapIcon.image, "Snap to grid: position, size, rotation, and vertices when editing") : new GUIContent("Snap");
            bool snap = GUILayout.Toggle(s.snapToGrid, snapContent, EditorStyles.miniButton, GUILayout.Width(26), GUILayout.Height(18));
            if (snap != s.snapToGrid) { s.snapToGrid = snap; s.NotifyChanged(); }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // edit mode: one row, Edit brush toggle then Vertex / Edge / Face
            EditorGUILayout.BeginHorizontal();
            bool editing = BrushEditContext.IsActive;
            bool editable = false;
            foreach (var go in Selection.gameObjects)
                if (go.TryGetComponent<Brush>(out var b) && (b.shape == BrushShape.Custom || BrushApi.CanConvertToCustom(b.shape))) { editable = true; break; }
            if (editing && !editable) BrushEditContext.Exit();
            using (new EditorGUI.DisabledScope(!editable))
            {
                bool wantEditing = GUILayout.Toggle(editing, new GUIContent("Edit brush", editable ? "Move, Rotate and Scale then act on the selected vertices, edges or faces; the selection mode is in the Tool Settings toolbar (1, 2, 3)" : "Select a brush"), EditorStyles.miniButton);
                if (wantEditing != editing) { if (wantEditing) BrushEditContext.Enter(); else BrushEditContext.Exit(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing) DrawExtrude(s);

            // lint: what would make a level invalid for the controller
            var offGrid = BrushSnap.OffGridBrushes();
            var parents = BrushSnap.TransformedParents();
            if (offGrid.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(offGrid.Count + " brush" + (offGrid.Count == 1 ? "" : "es") + " off grid", EditorStyles.miniLabel);
                if (GUILayout.Button("Select", GUILayout.Width(52))) Selection.objects = offGrid.ConvertAll(b => (Object)b.gameObject).ToArray();
                if (GUILayout.Button("Snap all", GUILayout.Width(60))) { BrushSnap.SnapAll(true); BrushApi.ForceUpdate(); }
                EditorGUILayout.EndHorizontal();
            }
            if (parents.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(parents.Count + " rotated or scaled parent" + (parents.Count == 1 ? "" : "s"), EditorStyles.miniLabel);
                if (GUILayout.Button("Select", GUILayout.Width(52))) Selection.objects = parents.ConvertAll(t => (Object)t.gameObject).ToArray();
                if (GUILayout.Button("Reset", GUILayout.Width(60))) { BrushSnap.ResetTransformedParents(); BrushApi.ForceUpdate(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        /// <summary>The edit actions: Extrude applies the Extrude panel's distance and mode to the selected faces.</summary>
        static void DrawExtrude(BrushSettings s)
        {
            int faces = 0;
            foreach (var b in BrushEditState.SelectedBrushes()) faces += BrushEditState.Sel(b).faces.Count;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(faces == 0 || BrushEditState.Mode != BrushEditMode.Face))
            {
                if (GUILayout.Button(new GUIContent("Extrude", faces > 0 ? "Extrude " + faces + (faces == 1 ? " face" : " faces") + " with the settings in the Extrude panel" : "Select faces first"), EditorStyles.miniButton))
                    BrushExtrudeOverlay.ExtrudeSelection();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        [Shortcut("Brushes/Grid smaller", typeof(SceneView), KeyCode.LeftBracket)]
        static void GridSmaller() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex - 1); }

        [Shortcut("Brushes/Grid larger", typeof(SceneView), KeyCode.RightBracket)]
        static void GridLarger() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex + 1); }
    }
}

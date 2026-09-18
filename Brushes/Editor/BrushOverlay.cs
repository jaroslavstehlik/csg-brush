using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>Scene view overlay: grid size and snap, the new brush's parameters while a Create tool is active, the edit mode toggle, lint.</summary>
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

            if (BrushCreateTool.Active != null) DrawNewBrush(s, BrushCreateTool.Active);

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

        static float UnitsField(BrushSettings s, string label, string tooltip, float units, float placeholderMeters)
        {
            EditorGUI.BeginChangeCheck();
            float shown = units > 0f ? units : s.ToUnits(placeholderMeters);
            float v = EditorGUILayout.FloatField(new GUIContent(label + " (" + s.unitLabel + ")", tooltip), shown);
            return EditorGUI.EndChangeCheck() ? Mathf.Max(0f, v) : units;
        }

        /// <summary>What the next brush of the active Create tool gets: operation, surface and the shape's parameters (Project Settings > Brushes holds them).</summary>
        static void DrawNewBrush(BrushSettings s, BrushCreateTool tool)
        {
            var shape = tool.Shape;
            float labelWidth = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 96;
            EditorGUILayout.LabelField(tool.Title, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            s.newOperation = (BrushOperation)EditorGUILayout.EnumPopup(new GUIContent("Operation", "Add fills space, Subtract carves the brushes above it"), s.newOperation);
            s.newSurface = (CsgBrush.Colliders.ControllerSurface.Kind)EditorGUILayout.EnumPopup(new GUIContent("Surface", "What the volume means to the character controller"), s.newSurface);
            BrushCreateTool.NewBrushParameters(out float stepHeight, out float stepDepth, out float wall);
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Sides"), s.newSides));
                    break;
                case BrushShape.Sphere:
                    s.newTessellation = EditorGUILayout.IntSlider(new GUIContent("Tessellation"), s.newTessellation, 1, 5);
                    break;
                case BrushShape.Stairs:
                    s.newStepHeight = UnitsField(s, "Step height", "0 uses the grid (at most the max step)", s.newStepHeight, stepHeight);
                    s.newStepDepth = UnitsField(s, "Step length", "0 uses two grid steps", s.newStepDepth, stepDepth);
                    break;
                case BrushShape.CurvedStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newCurveAngle = EditorGUILayout.FloatField(new GUIContent("Angle of curve", "Degrees the steps cover"), s.newCurveAngle);
                    s.newNumSteps = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps"), s.newNumSteps));
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    break;
                case BrushShape.SpiralStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newStepThickness = UnitsField(s, "Step thickness", "0 uses half a grid step", s.newStepThickness, s.GridMeters * 0.5f);
                    s.newStepsPer360 = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps per 360"), s.newStepsPer360));
                    s.newNumSteps = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps"), s.newNumSteps));
                    s.newSlopedCeiling = EditorGUILayout.Toggle(new GUIContent("Sloped ceiling"), s.newSlopedCeiling);
                    s.newSlopedFloor = EditorGUILayout.Toggle(new GUIContent("Sloped floor"), s.newSlopedFloor);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    break;
                case BrushShape.Arch:
                    s.newWallThickness = UnitsField(s, "Thickness", "0 uses one grid step", s.newWallThickness, wall);
                    s.newArchAngle = EditorGUILayout.Slider(new GUIContent("Angle", "180 is a full arch; less keeps the top part of it"), s.newArchAngle, 1f, 180f);
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Segments"), s.newSides));
                    break;
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                s.newHollow = EditorGUILayout.Toggle(new GUIContent("Hollow", "Keep only the walls"), s.newHollow);
                using (new EditorGUI.DisabledScope(!s.newHollow))
                    s.newWallThickness = UnitsField(s, "Wall thickness", "0 uses one grid step", s.newWallThickness, wall);
            }
            if (EditorGUI.EndChangeCheck()) { s.NotifyChanged(); SceneView.RepaintAll(); }
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUILayout.Space(2);
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
            using (new EditorGUI.DisabledScope(BrushEditState.Mode != BrushEditMode.Face || BrushExtrudeOverlay.BridgeCandidate() == null))
            {
                if (GUILayout.Button(new GUIContent("Bridge", "Connect the two selected faces of one brush with a block between them"), EditorStyles.miniButton))
                    BrushExtrudeOverlay.BridgeSelection();
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

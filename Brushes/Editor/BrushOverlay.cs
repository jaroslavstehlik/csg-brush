using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>Scene view overlay: grid size and snap, the new brush's parameters while a Create tool is active, the edit mode toggle, lint.</summary>
    [Icon(BrushIcons.Folder + "Brush.png")]
    [Overlay(typeof(SceneView), "Brushes", true)]
    public sealed class BrushOverlay : Overlay
    {
        const float PreferredWidth = 270f, Gap = 4f;

        /// <summary>The panel's width: rows of buttons wrap to fit it.</summary>
        static float s_Width = PreferredWidth;

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer container = null;
            container = new IMGUIContainer(() =>
            {
                float width = container.contentRect.width;
                s_Width = float.IsNaN(width) || width < PreferredWidth ? PreferredWidth : width;
                s_Width -= 8f; // the panel's padding
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Clamp(s_Width * 0.45f, 100f, 160f); // from the panel, not the whole Scene view
                try { Draw(); }
                finally { EditorGUIUtility.labelWidth = labelWidth; }
            });
            container.style.minWidth = PreferredWidth;
            return container;
        }

        /// <summary>A row of buttons that wraps: before a button of this width, start a new row if it would not fit.</summary>
        static void Wrap(ref float x, float width, float indent = 0f)
        {
            if (x > indent && x + width > s_Width)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (indent > 0f) GUILayout.Space(indent);
                x = indent;
            }
            x += width + Gap;
        }

        static readonly string[] SelectionIcons = { "ShapeEdit_Single", "ShapeEdit_Offset", "ShapeEdit_Match" };
        static readonly string[] SelectionNames = { "Single", "Offset", "Match" };
        static readonly string[] SelectionTips =
        {
            "Single: only the brush you drag changes",
            "Offset: selected brushes of the same shape change by the same amount",
            "Match: selected brushes of the same shape get the same size",
        };

        /// <summary>An icon, or its name while the icon isn't imported yet.</summary>
        static GUIContent IconOrName(string icon, string name, string tooltip)
        {
            var image = BrushIcons.Get(icon);
            return image != null ? new GUIContent(image, tooltip) : new GUIContent(name, tooltip);
        }

        /// <summary>The Shape Edit toggle and, while it is on, what a drag does to the other selected brushes (Single / Offset / Match): part of the grid row.</summary>
        static void DrawShapeEdit(BrushSettings s, ref float x)
        {
            var icon = BrushIcons.Get("ShapeEdit");
            var content = new GUIContent(icon != null ? " Shape Edit" : "Shape Edit", icon, "Drag a selected brush's sides to change its shape (B)");
            float width = EditorStyles.miniButton.CalcSize(content).x + (s.shapeHandles ? Gap + SelectionIcons.Length * 24f : 0f);
            Wrap(ref x, width); // the toggle and its choices stay together
            bool shapes = GUILayout.Toggle(s.shapeHandles, content, EditorStyles.miniButton, GUILayout.Height(18));
            if (shapes != s.shapeHandles) { s.shapeHandles = shapes; s.NotifyChanged(); SceneView.RepaintAll(); }
            if (s.shapeHandles)
            {
                var options = new GUIContent[SelectionIcons.Length];
                for (int i = 0; i < options.Length; i++) options[i] = IconOrName(SelectionIcons[i], SelectionNames[i], SelectionTips[i]);
                int current = Mathf.Clamp((int)s.shapeEditSelection, 0, options.Length - 1);
                int chosen = GUILayout.Toolbar(current, options, EditorStyles.miniButton, GUILayout.Width(options.Length * 24), GUILayout.Height(18));
                if (chosen != current) { s.shapeEditSelection = (ShapeEditSelection)chosen; s.NotifyChanged(); }
            }
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            EditorGUILayout.BeginVertical();

            // grid row: icon, size dropdown, snap toggle, view toggles, Shape Edit
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
            bool cuts = GUILayout.Toggle(s.showCuts, new GUIContent("Cuts", "Show subtract brushes as translucent red volumes, so they can be seen and selected where they have carved everything away"), EditorStyles.miniButton, GUILayout.Width(40), GUILayout.Height(18));
            if (cuts != s.showCuts) { s.showCuts = cuts; s.NotifyChanged(); SceneView.RepaintAll(); }
            bool sizes = GUILayout.Toggle(s.showDimensions, new GUIContent("Sizes", "Show the dimensions of a brush or wall while it is drawn"), EditorStyles.miniButton, GUILayout.Width(44), GUILayout.Height(18));
            if (sizes != s.showDimensions) { s.showDimensions = sizes; s.NotifyChanged(); SceneView.RepaintAll(); }
            float x = 20f + 74f + 26f + 40f + 44f + 5 * Gap;
            DrawShapeEdit(s, ref x);
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
            bool plan = !editable && Selection.activeGameObject != null && Selection.activeGameObject.GetComponent<FloorPlan>() != null;
            if (plan)
            {
                bool planEditing = FloorPlanEditContext.IsActive;
                if (GUILayout.Toggle(planEditing, new GUIContent("Edit plan", "Move, Rotate and Scale act on the selected points or walls (1, 2)"), EditorStyles.miniButton) != planEditing)
                {
                    if (planEditing) FloorPlanEditContext.Exit(); else FloorPlanEditContext.Enter();
                }
            }
            else using (new EditorGUI.DisabledScope(!editable))
            {
                bool wantEditing = GUILayout.Toggle(editing, new GUIContent("Edit brush", editable ? "Move, Rotate and Scale then act on the selected vertices, edges or faces; the selection mode is in the Tool Settings toolbar (1, 2, 3)" : "Select a brush"), EditorStyles.miniButton);
                if (wantEditing != editing) { if (wantEditing) BrushEditContext.Enter(); else BrushEditContext.Exit(); }
            }
            EditorGUILayout.EndHorizontal();
            if (editing) BrushExtrude.DrawPanel();

            // lint: parents that rotate or scale brushes off the grid
            var parents = BrushSnap.TransformedParents();
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

        /// <summary>Linear and curved stairs: solid under the steps, or slabs of a thickness.</summary>
        static void SupportUnderSteps(BrushSettings s)
        {
            s.newSupportUnderSteps = EditorGUILayout.Toggle(new GUIContent("Support under steps", "The steps stand on solid support down to the floor"), s.newSupportUnderSteps);
            if (!s.newSupportUnderSteps) s.newStepThickness = UnitsField(s, "Step thickness", "0 uses 0.1 m", s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
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
            if (!BrushCreateTool.IsOpening(shape)) // a door or window is always a cut
            {
                s.newOperation = (BrushOperation)EditorGUILayout.EnumPopup(new GUIContent("Operation", "Add fills space, Subtract carves the brushes above it"), s.newOperation);
                s.newGridSnap = (GridSnap)EditorGUILayout.EnumPopup(new GUIContent("Grid snap", "What goes onto the grid: the outermost vertices, or the pivot"), s.newGridSnap);
            }
            var moduleTypes = BrushApi.ModuleTypes();
            if (moduleTypes.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Modules", "The game's data every new brush gets, as components on it"));
                float indent = EditorGUIUtility.labelWidth + 2f, x = indent;
                foreach (var t in moduleTypes)
                {
                    bool on = s.newModules.Contains(t.FullName);
                    var label = new GUIContent(ObjectNames.NicifyVariableName(t.Name));
                    Wrap(ref x, EditorStyles.miniButton.CalcSize(label).x, indent);
                    bool want = GUILayout.Toggle(on, label, EditorStyles.miniButton);
                    if (want != on) { if (want) s.newModules.Add(t.FullName); else s.newModules.Remove(t.FullName); }
                }
                EditorGUILayout.EndHorizontal();
            }
            BrushCreateTool.NewBrushParameters(out float stepHeight, out float wall);
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
                    s.newStepHeight = UnitsField(s, "Step height", "0 uses 0.25 m; the steps fill the drawn height", s.newStepHeight, stepHeight);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.CurvedStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newStepHeight = UnitsField(s, "Step height", "0 uses 0.25 m; the steps fill the drawn height", s.newStepHeight, stepHeight);
                    s.newCurveAngle = EditorGUILayout.FloatField(new GUIContent("Angle of curve", "Degrees the steps cover"), s.newCurveAngle);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    SupportUnderSteps(s);
                    break;
                case BrushShape.SpiralStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newStepThickness = UnitsField(s, "Step thickness", "0 uses 0.1 m", s.newStepThickness, BrushSettings.DefaultStepThicknessMeters);
                    s.newStepHeight = UnitsField(s, "Step height", "0 uses 0.25 m; the steps fill the drawn height", s.newStepHeight, stepHeight);
                    s.newStepsPer360 = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Steps per 360"), s.newStepsPer360));
                    s.newSlopedCeiling = EditorGUILayout.Toggle(new GUIContent("Sloped ceiling"), s.newSlopedCeiling);
                    s.newSlopedFloor = EditorGUILayout.Toggle(new GUIContent("Sloped floor"), s.newSlopedFloor);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    break;
                case BrushShape.Arch:
                    s.newWallThickness = UnitsField(s, "Thickness", "0 uses one grid step", s.newWallThickness, wall);
                    s.newArchAngle = EditorGUILayout.Slider(new GUIContent("Angle", "180 is a full arch; less keeps the top part of it"), s.newArchAngle, 1f, 180f);
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Segments"), s.newSides));
                    break;
                case BrushShape.Door:
                    s.newDoorWidth = UnitsField(s, "Width", "Width of the doorway; 0 uses 1 m", s.newDoorWidth, BrushSettings.DefaultDoorWidthMeters);
                    s.newDoorHeight = UnitsField(s, "Height", "Height of the doorway; 0 uses 2.2 m", s.newDoorHeight, BrushSettings.DefaultDoorHeightMeters);
                    break;
                case BrushShape.Window:
                    s.newWindowWidth = UnitsField(s, "Width", "Width of the window; 0 uses 1.2 m", s.newWindowWidth, BrushSettings.DefaultWindowWidthMeters);
                    s.newWindowHeight = UnitsField(s, "Height", "Height of the window; 0 uses 1.2 m", s.newWindowHeight, BrushSettings.DefaultWindowHeightMeters);
                    s.newWindowSill = UnitsField(s, "Sill", "Height of its bottom above the floor; 0 uses 0.9 m", s.newWindowSill, BrushSettings.DefaultWindowSillMeters);
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

        [Shortcut("Brushes/Grid smaller", typeof(SceneView), KeyCode.LeftBracket)]
        static void GridSmaller() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex - 1); }

        [Shortcut("Brushes/Grid larger", typeof(SceneView), KeyCode.RightBracket)]
        static void GridLarger() { BrushSettings.instance.SetGridIndex(BrushSettings.instance.gridIndex + 1); }
    }
}

using CsgBrush.Colliders;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(Brush))]
    [CanEditMultipleObjects]
    public sealed class BrushEditor : UnityEditor.Editor
    {
        SerializedProperty shapeProp, operationProp, collisionProp, physicsMaterialProp, provideContactsProp, sizeProp, hollowProp, wallProp, sidesProp, tessProp, stepHeightProp, materialProp;
        SerializedProperty innerRadiusProp, stepWidthProp, stepThicknessProp, curveAngleProp, numStepsProp, stepsPer360Prop, addToFirstStepProp, ccwProp, slopedFloorProp, slopedCeilingProp;

        void OnEnable()
        {
            shapeProp = serializedObject.FindProperty(nameof(Brush.shape));
            operationProp = serializedObject.FindProperty(nameof(Brush.operation));
            collisionProp = serializedObject.FindProperty(nameof(Brush.collision));
            physicsMaterialProp = serializedObject.FindProperty(nameof(Brush.physicsMaterial));
            provideContactsProp = serializedObject.FindProperty(nameof(Brush.provideContacts));
            sizeProp = serializedObject.FindProperty(nameof(Brush.size));
            hollowProp = serializedObject.FindProperty(nameof(Brush.hollow));
            wallProp = serializedObject.FindProperty(nameof(Brush.wallThickness));
            sidesProp = serializedObject.FindProperty(nameof(Brush.sides));
            tessProp = serializedObject.FindProperty(nameof(Brush.tessellation));
            stepHeightProp = serializedObject.FindProperty(nameof(Brush.stepHeight));
            materialProp = serializedObject.FindProperty(nameof(Brush.material));
            innerRadiusProp = serializedObject.FindProperty(nameof(Brush.innerRadius));
            stepWidthProp = serializedObject.FindProperty(nameof(Brush.stepWidth));
            stepThicknessProp = serializedObject.FindProperty(nameof(Brush.stepThickness));
            curveAngleProp = serializedObject.FindProperty(nameof(Brush.curveAngle));
            numStepsProp = serializedObject.FindProperty(nameof(Brush.numSteps));
            stepsPer360Prop = serializedObject.FindProperty(nameof(Brush.stepsPer360));
            addToFirstStepProp = serializedObject.FindProperty(nameof(Brush.addToFirstStep));
            ccwProp = serializedObject.FindProperty(nameof(Brush.counterClockwise));
            slopedFloorProp = serializedObject.FindProperty(nameof(Brush.slopedFloor));
            slopedCeilingProp = serializedObject.FindProperty(nameof(Brush.slopedCeiling));
        }

        static bool s_CollisionOpen = true, s_RenderingOpen = true;

        // the Shape popup: Custom first, a separator, then the parametric shapes by name (null marks the separator)
        static BrushShape?[] s_ShapeOrder;
        static string[] s_ShapeNames;

        static void BuildShapeOrder()
        {
            if (s_ShapeOrder != null) return;
            var shapes = new System.Collections.Generic.List<BrushShape>();
            foreach (BrushShape shape in System.Enum.GetValues(typeof(BrushShape))) if (shape != BrushShape.Custom) shapes.Add(shape);
            shapes.Sort((a, b) => string.CompareOrdinal(ShapeName(a), ShapeName(b)));
            var order = new System.Collections.Generic.List<BrushShape?> { BrushShape.Custom, null };
            foreach (var shape in shapes) order.Add(shape);
            s_ShapeOrder = order.ToArray();
            s_ShapeNames = System.Array.ConvertAll(s_ShapeOrder, shape => shape.HasValue ? ShapeName(shape.Value) : "");
        }

        static string ShapeName(BrushShape shape)
        {
            var field = typeof(BrushShape).GetField(shape.ToString());
            var inspectorName = (InspectorNameAttribute)System.Attribute.GetCustomAttribute(field, typeof(InspectorNameAttribute));
            return inspectorName != null ? inspectorName.displayName : ObjectNames.NicifyVariableName(shape.ToString());
        }

        void ShapeField()
        {
            BuildShapeOrder();
            var rect = EditorGUILayout.GetControlRect();
            var label = EditorGUI.BeginProperty(rect, new GUIContent("Shape"), shapeProp);
            EditorGUI.showMixedValue = shapeProp.hasMultipleDifferentValues;
            int current = System.Array.IndexOf(s_ShapeOrder, (BrushShape?)(BrushShape)shapeProp.intValue);
            int picked = EditorGUI.Popup(rect, label.text, current, s_ShapeNames);
            EditorGUI.showMixedValue = false;
            if (picked != current && picked >= 0 && s_ShapeOrder[picked].HasValue) shapeProp.intValue = (int)s_ShapeOrder[picked].Value;
            EditorGUI.EndProperty();
        }

        public override void OnInspectorGUI()
        {
            var settings = BrushSettings.instance;
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            ShapeField();
            EditorGUILayout.PropertyField(operationProp, new GUIContent("Operation", "Add fills space, Subtract carves it out of the brushes above it in the Hierarchy."));
            if (targets.Length == 1 && target is Brush owner)
            {
                string bakedBy = BrushCsg.BakedBy(owner, out var reference);
                var label = new GUIContent("Brush group", "Parent group that produces the mesh and collision geometry.");
                if (reference != null) using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(label, reference, reference.GetType(), true);
                else EditorGUILayout.LabelField(label, new GUIContent(bakedBy), EditorStyles.wordWrappedMiniLabel);
            }

            foreach (var t in targets) if (t is Brush pb && !string.IsNullOrEmpty(pb.problem)) { EditorGUILayout.HelpBox(pb.problem + " Undo the last edit or reset the shape.", MessageType.Error); break; }
            EditorGUILayout.Space(4);
            var shape = (BrushShape)shapeProp.enumValueIndex;
            if (shape == BrushShape.Custom)
            {
                var brush = (Brush)target;
                var poly = brush.polyhedron;
                int parts = 0;
                if (poly != null && poly.IsValid) { var pieces = new System.Collections.Generic.List<ConvexPolytope>(); ConvexDecomposition.Decompose(poly, pieces); parts = pieces.Count; }
                string info = poly != null && poly.IsValid ? poly.vertices.Length + " vertices, " + poly.faces.Length + " faces, " + (parts == 1 ? "convex" : parts + " convex parts") : "no shape";
                EditorGUILayout.LabelField("Custom shape", info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(BrushEditContext.IsActive ? "Stop editing" : "Edit shape")) BrushEditContext.Toggle();
                if (GUILayout.Button("Reset to " + brush.customFrom)) foreach (var t in targets) BrushApi.ResetShape((Brush)t);
                EditorGUILayout.EndHorizontal();
            }
            else if (shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                var sz = settings.ToUnits(sizeProp.vector3Value);
                EditorGUILayout.LabelField("Size (" + settings.unitLabel + ")", sz.x.ToString("0.#") + " x " + sz.y.ToString("0.#") + " x " + sz.z.ToString("0.#") + "  (from the parameters)");
            }
            else DrawSize(settings, shape);

            if (shape != BrushShape.Custom && BrushApi.CanConvertToCustom(shape))
            {
                if (GUILayout.Button(new GUIContent(BrushEditContext.IsActive ? "Stop editing" : "Edit shape", "Move, Rotate and Scale act on vertices, edges or faces in the Scene view. The first edit turns the brush into a Custom shape.")))
                    BrushEditContext.Toggle();
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(hollowProp, new GUIContent("Hollow", "Keep only the walls. Unreal's one-click room."), GUILayout.Width(EditorGUIUtility.labelWidth + 20f));
                using (new EditorGUI.DisabledScope(!hollowProp.boolValue))
                    DrawUnitsField(settings, wallProp, "Wall thickness");
                EditorGUILayout.EndHorizontal();
            }
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    EditorGUILayout.PropertyField(sidesProp, new GUIContent("Sides"));
                    break;
                case BrushShape.Sphere:
                    EditorGUILayout.PropertyField(tessProp, new GUIContent("Tessellation"));
                    break;
                case BrushShape.Arch:
                    DrawUnitsField(settings, wallProp, "Thickness");
                    EditorGUILayout.Slider(curveAngleProp, 1f, 180f, new GUIContent("Angle", "180 is a full arch; less keeps the top part of it"));
                    EditorGUILayout.PropertyField(sidesProp, new GUIContent("Segments"));
                    break;
                case BrushShape.CurvedStairs:
                    DrawUnitsField(settings, innerRadiusProp, "Inner radius");
                    DrawUnitsField(settings, stepWidthProp, "Step width");
                    DrawStepsByHeight(settings);
                    EditorGUILayout.PropertyField(curveAngleProp, new GUIContent("Angle of curve"));
                    DrawUnitsField(settings, addToFirstStepProp, "Add to first step", true);
                    EditorGUILayout.PropertyField(ccwProp, new GUIContent("Counter clockwise"));
                    break;
                case BrushShape.SpiralStairs:
                    DrawUnitsField(settings, innerRadiusProp, "Inner radius");
                    DrawUnitsField(settings, stepWidthProp, "Step width");
                    DrawStepsByHeight(settings);
                    DrawUnitsField(settings, stepThicknessProp, "Step thickness");
                    EditorGUILayout.PropertyField(stepsPer360Prop, new GUIContent("Steps per 360"));
                    DrawUnitsField(settings, addToFirstStepProp, "Add to first step", true);
                    EditorGUILayout.PropertyField(slopedCeilingProp, new GUIContent("Sloped ceiling"));
                    EditorGUILayout.PropertyField(slopedFloorProp, new GUIContent("Sloped floor"));
                    EditorGUILayout.PropertyField(ccwProp, new GUIContent("Counter clockwise"));
                    break;
                case BrushShape.Stairs:
                    DrawUnitsField(settings, stepHeightProp, "Step height");
                    var sz = sizeProp.vector3Value;
                    int steps = Mathf.Max(1, Mathf.RoundToInt(sz.y / Mathf.Max(0.001f, stepHeightProp.floatValue)));
                    EditorGUILayout.LabelField(" ", steps + " steps over " + settings.FormatUnits(sz.z) + " (" + settings.FormatUnits(sz.z / steps) + " each)", EditorStyles.miniLabel);
                    break;
            }

            EditorGUILayout.Space(4);
            s_CollisionOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_CollisionOpen, "Collision");
            if (s_CollisionOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(collisionProp, new GUIContent("Collision", "Kind of collider the brush produces: solid, trigger or none."));
                if (target is Brush cb && cb.EffectiveCollision() != cb.collision)
                    EditorGUILayout.LabelField(" ", "made a " + cb.EffectiveCollision().ToString().ToLower() + " by a module", EditorStyles.miniLabel);
                if (target is Brush pb2 && pb2.EffectiveCollision() != ColliderKind.None)
                {
                    EditorGUILayout.PropertyField(physicsMaterialProp, new GUIContent("Physics material", "On every collider piece of the brush, triggers included: friction and bounce, and a handle for sounds or other lookups."));
                    EditorGUILayout.PropertyField(provideContactsProp, new GUIContent("Provide contacts", "The pieces provide contact data to OnCollision callbacks."));
                    EditorGUILayout.LabelField(" ", "the pieces also take this object's tag, layer and static flags", EditorStyles.miniLabel);
                }
                if (target is Brush mb)
                {
                    var modules = mb.Modules();
                    if (modules.Length > 0)
                    {
                        var names = new System.Text.StringBuilder();
                        foreach (var m in modules) { if (names.Length > 0) names.Append(", "); names.Append(ObjectNames.NicifyVariableName(m.GetType().Name)); if (m.gameObject != mb.gameObject) names.Append(" (from " + m.gameObject.name + ")"); }
                        EditorGUILayout.LabelField("Modules", names.ToString(), EditorStyles.miniLabel);
                    }
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            s_RenderingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_RenderingOpen, "Rendering");
            if (s_RenderingOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(materialProp, new GUIContent("Material", "Applied to every face. Drop a material onto a single face in the Scene view for per-face materials."));
                EditorGUILayout.LabelField(" ", "the render mesh takes its model's tag and static flags; its layer is this object's", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed)
                foreach (var t in targets) BrushSync.Ensure((Brush)t);

            foreach (var t in targets)
            {
                var brush = (Brush)t;
                var s = brush.transform.localScale;
                if (s != Vector3.one)
                {
                    EditorGUILayout.HelpBox("Scale is " + s + ". With snapping on, the Scale tool is baked into the size on release; otherwise apply it here.", MessageType.Info);
                    if (GUILayout.Button("Apply scale to size")) BrushApi.ApplyScale(brush);
                    break;
                }
            }
        }

        void DrawSize(BrushSettings settings, BrushShape shape)
        {
            var size = sizeProp.vector3Value;
            var units = settings.ToUnits(size);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Size (" + settings.unitLabel + ")", shape == BrushShape.Wedge ? "X width, Y height, Z length. The ramp rises along Z." : shape == BrushShape.Stairs ? "X width, Y total rise, Z run. Stairs climb along Z." : "Centred on the transform."));
            float w = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.FloatField("X", units.x);
            float y = EditorGUILayout.FloatField("Y", units.y);
            float z = EditorGUILayout.FloatField("Z", units.z);
            if (EditorGUI.EndChangeCheck())
                sizeProp.vector3Value = settings.ToMeters(new Vector3(Mathf.Max(0f, x), Mathf.Max(0f, y), Mathf.Max(0f, z)));
            EditorGUIUtility.labelWidth = w;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(" ", size.x.ToString("0.00") + " x " + size.y.ToString("0.00") + " x " + size.z.ToString("0.00") + " m", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Curved and spiral stairs: the step height and the height they climb. The number of steps follows from the two;
        /// a new step height keeps the height, a new height keeps the step height.
        /// </summary>
        void DrawStepsByHeight(BrushSettings settings)
        {
            float stepHeight = Mathf.Max(0.001f, stepHeightProp.floatValue);
            float height = numStepsProp.intValue * stepHeight;
            DrawUnitsField(settings, stepHeightProp, "Step height");
            float newStep = Mathf.Max(0.001f, stepHeightProp.floatValue);
            if (!Mathf.Approximately(newStep, stepHeight)) numStepsProp.intValue = BrushPolyhedron.StepCount(height, newStep);
            EditorGUI.showMixedValue = numStepsProp.hasMultipleDifferentValues || stepHeightProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float shown = EditorGUILayout.FloatField(new GUIContent("Height (" + settings.unitLabel + ")", "The steps fill it: its height divided by the step height, rounded"), settings.ToUnits(numStepsProp.intValue * newStep));
            EditorGUI.showMixedValue = false;
            if (EditorGUI.EndChangeCheck()) numStepsProp.intValue = BrushPolyhedron.StepCount(settings.ToMeters(shown), newStep);
        }

        static void DrawUnitsField(BrushSettings settings, SerializedProperty prop, string label, bool allowNegative = false)
        {
            EditorGUI.BeginChangeCheck();
            float v = EditorGUILayout.FloatField(new GUIContent(label + " (" + settings.unitLabel + ")"), settings.ToUnits(prop.floatValue));
            if (EditorGUI.EndChangeCheck())
                prop.floatValue = settings.ToMeters(allowNegative ? v : Mathf.Max(0f, v));
        }
    }
}

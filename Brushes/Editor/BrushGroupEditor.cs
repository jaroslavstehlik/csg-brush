using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(BrushGroup))]
    [CanEditMultipleObjects]
    public sealed class BrushGroupEditor : UnityEditor.Editor
    {
        static bool s_RenderingOpen, s_LightingOpen = true, s_LightmappingOpen = true, s_ProbesOpen = true, s_AdditionalOpen = true;

        /// <summary>A section of the Rendering foldout, as a Mesh Renderer shows its Lighting, Probes and the rest.</summary>
        static bool Section(bool open, string title) => EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);

        /// <summary>The settings every render mesh of the group gets, laid out as on a Mesh Renderer.</summary>
        void DrawRendering()
        {
            serializedObject.Update();
            var r = serializedObject.FindProperty(nameof(BrushGroup.rendering));
            SerializedProperty P(string name) => r.FindPropertyRelative(name);
            s_RenderingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(s_RenderingOpen, new GUIContent("Rendering", "What every mesh of the group gets on its renderer; the materials come from the brushes"));
            if (s_RenderingOpen)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.indentLevel++;
                s_LightingOpen = Section(s_LightingOpen, "Lighting");
                if (s_LightingOpen)
                {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.castShadows)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.staticShadowCaster)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.receiveShadows)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.receiveGlobalIllumination)), new GUIContent("Receive Global Illumination", "Lightmaps or light probes, for static brushes that contribute to global illumination"));
                EditorGUI.indentLevel--;
                }
                s_LightmappingOpen = Section(s_LightmappingOpen, "Lightmapping");
                if (s_LightmappingOpen)
                {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.scaleInLightmap)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.stitchLightmapSeams)), new GUIContent("Stitch Seams", "Blend lightmap seams where faces meet"));
                var lp = P(nameof(RenderingSettings.lightmapParameters));
                EditorGUI.showMixedValue = lp.hasMultipleDifferentValues;
                var chosen = EditorGUILayout.ObjectField(new GUIContent("Lightmap Parameters", "Empty uses the scene's default"), lp.objectReferenceValue, typeof(LightmapParameters), false);
                EditorGUI.showMixedValue = false;
                if (chosen != lp.objectReferenceValue) lp.objectReferenceValue = chosen;
                EditorGUI.indentLevel--;
                }
                s_ProbesOpen = Section(s_ProbesOpen, "Probes");
                if (s_ProbesOpen)
                {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.lightProbes)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.reflectionProbes)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.anchorOverride)));
                EditorGUI.indentLevel--;
                }
                s_AdditionalOpen = Section(s_AdditionalOpen, "Additional Settings");
                if (s_AdditionalOpen)
                {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.motionVectors)));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.dynamicOcclusion)));
                var mask = P(nameof(RenderingSettings.renderingLayerMask));
                EditorGUI.showMixedValue = mask.hasMultipleDifferentValues;
                uint m = (uint)EditorGUILayout.MaskField(new GUIContent("Rendering Layer Mask", "Light and decal layers"), (int)mask.uintValue, RenderingLayerNames());
                EditorGUI.showMixedValue = false;
                if (m != mask.uintValue) mask.uintValue = m;
                EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    foreach (var t in targets) BrushCsg.ApplyRendererSettings((BrushGroup)t);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static string[] RenderingLayerNames()
        {
            var names = UnityEngine.RenderingLayerMask.GetDefinedRenderingLayerNames();
            return names != null && names.Length > 0 ? names : new[] { "Default" };
        }

        public override void OnInspectorGUI()
        {
            if (targets.Length == 1) EditorGUILayout.LabelField("Brushes", BrushPrefabBuild.BrushesOf((BrushGroup)target).Count.ToString());
            bool unsaved = false;
            foreach (var t in targets)
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(((BrushGroup)t).gameObject);
                if (stage != null && stage.scene.isDirty) unsaved = true;
            }
            using (new EditorGUI.DisabledScope(unsaved))
                if (GUILayout.Button(new GUIContent("Rebuild", unsaved ? "Save the prefab first: a prefab is rebuilt from its saved file" : "Build the mesh and colliders again from scratch (into the prefab, for a group in a prefab)")))
                    foreach (var t in targets) BrushCsg.Rebuild((BrushGroup)t);
            DrawRendering();
        }
    }
}

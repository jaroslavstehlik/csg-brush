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
                EditorGUILayout.LabelField("Lightmap UVs", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.lightmapHardAngle)), new GUIContent("Hard Angle", "Degrees: edges sharper than this split the lightmap UVs"));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.lightmapAngleError)), new GUIContent("Angle Error", "Percent: how much the lightmap UVs may distort angles"));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.lightmapAreaError)), new GUIContent("Area Error", "Percent: how much the lightmap UVs may distort areas"));
                EditorGUILayout.PropertyField(P(nameof(RenderingSettings.lightmapPackMargin)), new GUIContent("Pack Margin", "Pixels between UV islands, for a mesh filling a 1024 pixel lightmap"));
                LightmapUVStatus();
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

        /// <summary>A moving group's brushes are not static (static meshes cannot move); a still one's get the default flags back.</summary>
        /// <summary>Whether the selected groups' lit meshes have lightmap UVs, and a button to make them.</summary>
        void LightmapUVStatus()
        {
            int missing = 0;
            foreach (var t in targets) if (t is BrushGroup g) missing += BrushLightmapUVs.Missing(g).Count;
            if (missing == 0) return;
            EditorGUILayout.HelpBox(BrushSettings.instance.autoLightmapUVs ? "Lightmap UVs are made when the edit is finished." : "Lightmap UVs are missing. Generate them before baking.", MessageType.Info);
            if (GUILayout.Button("Generate Lightmap UVs"))
                foreach (var t in targets) if (t is BrushGroup g) BrushLightmapUVs.Generate(g);
        }

        static void SetMoving(BrushGroup group, bool moving)
        {
            var flags = moving ? 0 : BrushSettings.instance.defaultModelStaticFlags;
            Undo.RecordObject(group.gameObject, "Change moving group");
            GameObjectUtility.SetStaticEditorFlags(group.gameObject, flags);
            foreach (var brush in BrushPrefabBuild.BrushesOf(group))
            {
                Undo.RecordObject(brush.gameObject, "Change moving group");
                GameObjectUtility.SetStaticEditorFlags(brush.gameObject, flags);
                BrushSync.Ensure(brush);
            }
        }

        static string[] RenderingLayerNames()
        {
            var names = UnityEngine.RenderingLayerMask.GetDefinedRenderingLayerNames();
            return names != null && names.Length > 0 ? names : new[] { "Default" };
        }

        public override void OnInspectorGUI()
        {
            if (targets.Length == 1) EditorGUILayout.LabelField("Brushes", BrushPrefabBuild.BrushesOf((BrushGroup)target).Count.ToString());
            serializedObject.Update();
            var movingProp = serializedObject.FindProperty(nameof(BrushGroup.moving));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(movingProp, new GUIContent("Moving", "Built once and moved as a whole at runtime (a lift, a drawbridge): its brushes are not static."));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                foreach (var t in targets) SetMoving((BrushGroup)t, ((BrushGroup)t).moving);
            }
            bool unsaved = false;
            foreach (var t in targets)
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(((BrushGroup)t).gameObject);
                if (stage != null && stage.scene.isDirty) unsaved = true;
            }
            using (new EditorGUI.DisabledScope(unsaved))
                if (GUILayout.Button(new GUIContent("Rebuild", unsaved ? "Save the prefab first: a prefab is rebuilt from its saved file" : "Build the mesh and colliders again from scratch (into the prefab, for a group in a prefab)")))
                    foreach (var t in targets) BrushCsg.Rebuild((BrushGroup)t);
            if (targets.Length == 1 && GUILayout.Button(new GUIContent("Save to Blender…", "Save the group's meshes as a Blender file, using the Blender installed on this computer")))
                SaveToBlender((BrushGroup)target);
            DrawRendering();
        }

        /// <summary>
        /// Check that Blender runs, then save: to the file the group was saved to before (asking first: overwrite it, or
        /// save as a new file), or to a file chosen in a Save dialog. The group remembers the file.
        /// </summary>
        static void SaveToBlender(BrushGroup group)
        {
            if (BlenderExport.FindBlender(out _, out var missing) == null)
            {
                int choice = EditorUtility.DisplayDialogComplex("Blender not found", missing, "Download Blender", "Cancel", "Locate Blender…");
                if (choice == 0) Application.OpenURL("https://www.blender.org/download/");
                else if (choice == 2)
                {
                    var picked = EditorUtility.OpenFilePanel("Choose Blender", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(picked)) { BrushSettings.instance.blenderPath = picked; BrushSettings.instance.NotifyChanged(); BlenderExport.Forget(); SaveToBlender(group); }
                }
                return;
            }
            string path = null;
            string remembered = string.IsNullOrEmpty(group.blenderFile) ? null : System.IO.Path.GetFullPath(group.blenderFile);
            if (remembered != null && System.IO.File.Exists(remembered))
            {
                int choice = EditorUtility.DisplayDialogComplex("Save to Blender", "Overwrite " + remembered + "?", "Overwrite", "Cancel", "Save As…");
                if (choice == 1) return;
                if (choice == 0) path = remembered;
            }
            if (path == null)
            {
                string folder = remembered != null ? System.IO.Path.GetDirectoryName(remembered) : "";
                path = EditorUtility.SaveFilePanel("Save Brush Group to Blender", folder, group.name, "blend");
                if (string.IsNullOrEmpty(path)) return;
            }
            string error;
            try { EditorUtility.DisplayProgressBar("Save to Blender", "Blender is building the file…", 0.5f); error = BlenderExport.Save(group, path); }
            finally { EditorUtility.ClearProgressBar(); }
            if (error != null) { EditorUtility.DisplayDialog("Save to Blender", error, "OK"); return; }
            Undo.RecordObject(group, "Save to Blender");
            group.blenderFile = ProjectRelative(path);
            EditorUtility.SetDirty(group);
            // saved into the project: Unity imports it now (it reads .blend files through Blender too), and again when overwritten
            if (group.blenderFile.StartsWith("Assets/")) AssetDatabase.ImportAsset(group.blenderFile, ImportAssetOptions.ForceUpdate);
            Debug.Log("CSG Brush: saved " + group.name + " to " + path + ".");
        }

        /// <summary>A path relative to the project folder when inside it (so the project can move), else as it is.</summary>
        static string ProjectRelative(string path)
        {
            var full = System.IO.Path.GetFullPath(path).Replace('\\', '/');
            var project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(project) ? full.Substring(project.Length) : full;
        }
    }
}

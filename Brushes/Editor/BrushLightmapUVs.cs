using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Lightmap UVs for the brush meshes that contribute to global illumination, as ProBuilder makes them: unwrapping is slow
    /// on big meshes, so it is not done while you edit. With Auto lightmap UVs (Project Settings > Brushes, on by default)
    /// they are made once an edit is finished; otherwise the Brush Group's Generate Lightmap UVs button or
    /// Tools > CSG Brush > Generate Lightmap UVs makes them. Any change to a mesh drops its UVs. The meshes are saved in the
    /// scene with their UVs and kept as they are when it opens again, so a bake stays valid until the brushes are edited.
    /// After a bake, the lit brush meshes that had no UVs are listed in the Console.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushLightmapUVs
    {
        /// <summary>Seconds after the last mesh was written before the UVs are made (an edit of several steps finishes first).</summary>
        public static double SettleSeconds = 0.5;
        /// <summary>An unwrap slower than this suggests turning Auto lightmap UVs off.</summary>
        const double SlowSeconds = 3.0;

        static readonly HashSet<BrushGroup> s_Pending = new HashSet<BrushGroup>();
        static double s_LastWrite;
        static bool s_WarnedSlow;

        static BrushLightmapUVs()
        {
            BrushCsg.MeshesWritten += g => { s_Pending.Add(g); s_LastWrite = EditorApplication.timeSinceStartup; };
            EditorApplication.update += Update;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        static void Update()
        {
            if (s_Pending.Count == 0 || !BrushSettings.instance.autoLightmapUVs) return;
            if (Application.isPlaying || GUIUtility.hotControl != 0 || BrushCsg.Busy || BrushCsg.HasDirty) return; // still editing
            if (EditorApplication.timeSinceStartup - s_LastWrite < SettleSeconds) return;
            FlushPending();
        }

        /// <summary>Make now the lightmap UVs of the groups built since the last time (with Auto lightmap UVs on).</summary>
        public static void FlushPending()
        {
            if (s_Pending.Count == 0 || !BrushSettings.instance.autoLightmapUVs) return;
            var groups = new List<BrushGroup>(s_Pending); s_Pending.Clear();
            var sw = Stopwatch.StartNew();
            foreach (var g in groups) if (g != null) Generate(g);
            if (sw.Elapsed.TotalSeconds > SlowSeconds && !s_WarnedSlow)
            {
                s_WarnedSlow = true;
                Debug.Log($"CSG Brush: making lightmap UVs took {sw.Elapsed.TotalSeconds:0.0} s. For large groups, turn off Auto lightmap UVs in Project Settings > Brushes and generate them before baking.");
            }
        }

        /// <summary>The group's render meshes that contribute to global illumination.</summary>
        public static List<MeshFilter> LitMeshes(BrushGroup group)
        {
            var list = new List<MeshFilter>();
            if (group == null || EditorUtility.IsPersistent(group)) return list;
            foreach (var t in BrushCsg.MeshObjects(group))
            {
                if ((GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0 && !EditorUtility.IsPersistent(mf.sharedMesh)) list.Add(mf);
            }
            return list;
        }

        /// <summary>The brush meshes that contribute to global illumination, in the open scenes.</summary>
        public static List<MeshFilter> LitMeshes()
        {
            var list = new List<MeshFilter>();
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Exclude)) list.AddRange(LitMeshes(group));
            return list;
        }

        public static bool HasLightmapUVs(Mesh mesh) => mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1);

        /// <summary>The group's lit meshes whose lightmap UVs are missing or were made with other settings.</summary>
        public static List<MeshFilter> Missing(BrushGroup group)
        {
            var list = new List<MeshFilter>();
            int settings = SettingsKey(group);
            foreach (var mf in LitMeshes(group))
                if (!HasLightmapUVs(mf.sharedMesh) || !mf.TryGetComponent<BrushMeshSources>(out var saved) || saved.lightmapUVSettings != settings) list.Add(mf);
            return list;
        }

        /// <summary>Unwrap the group's lit meshes that need it, with the group's settings; returns how many were made.</summary>
        public static int Generate(BrushGroup group)
        {
            int made = 0;
            var r = group.rendering;
            var param = new UnwrapParam();
            UnwrapParam.SetDefaults(out param);
            param.hardAngle = r.lightmapHardAngle;
            param.angleError = r.lightmapAngleError / 100f;
            param.areaError = r.lightmapAreaError / 100f;
            param.packMargin = r.lightmapPackMargin / 1024f;
            int settings = SettingsKey(group);
            foreach (var mf in Missing(group))
            {
                var mesh = mf.sharedMesh;
                if (HasLightmapUVs(mesh)) mesh.uv2 = null; // made with other settings: from the mesh as built
                if (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 && mesh.vertexCount > 30000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // room for the vertices the unwrap may add
                if (!Unwrapping.GenerateSecondaryUVSet(mesh, param)) { Debug.LogWarning("CSG Brush: lightmap UVs could not be made for " + mf.transform.parent?.name + "/" + mf.name + ".", mf); continue; }
                if (!mf.TryGetComponent<BrushMeshSources>(out var saved)) saved = mf.gameObject.AddComponent<BrushMeshSources>();
                saved.lightmapUVSettings = settings;
                EditorSceneManager.MarkSceneDirty(mf.gameObject.scene);
                made++;
            }
            return made;
        }

        /// <summary>Unwrap every lit brush mesh in the open scenes that needs it; returns how many were made.</summary>
        public static int Generate()
        {
            int made = 0;
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Exclude)) if (!EditorUtility.IsPersistent(group)) made += Generate(group);
            return made;
        }

        /// <summary>The group's unwrap settings as a number (never 0): UVs made with other settings are made again.</summary>
        static int SettingsKey(BrushGroup group)
        {
            var r = group.rendering;
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(r.lightmapHardAngle * 100f); h = h * 31 + Mathf.RoundToInt(r.lightmapAngleError * 100f);
                h = h * 31 + Mathf.RoundToInt(r.lightmapAreaError * 100f); h = h * 31 + Mathf.RoundToInt(r.lightmapPackMargin * 100f);
                return h == 0 ? 1 : h;
            }
        }

        [MenuItem("Tools/CSG Brush/Generate Lightmap UVs", false, 200)]
        static void Menu()
        {
            int made = Generate();
            Debug.Log(made == 0 ? "CSG Brush: every lit brush mesh already has lightmap UVs." : "CSG Brush: lightmap UVs made for " + made + " brush mesh" + (made == 1 ? "." : "es."));
        }

        static void OnBakeCompleted()
        {
            var missing = new List<string>();
            foreach (var mf in LitMeshes()) if (!HasLightmapUVs(mf.sharedMesh)) missing.Add(mf.transform.parent?.name + "/" + mf.name);
            if (missing.Count == 0) return;
            Debug.LogWarning("CSG Brush: these lit brush meshes have no lightmap UVs, so the bake left them out: " + string.Join(", ", missing) + ". Choose Tools > CSG Brush > Generate Lightmap UVs and bake again.");
        }
    }
}

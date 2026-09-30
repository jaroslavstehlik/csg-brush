using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Lightmap UVs for the brush meshes that contribute to global illumination. Unwrapping is slow on big meshes, so it is
    /// not done while you build: Tools > CSG Brush > Generate Lightmap UVs does it, and a light bake does it first when a
    /// mesh needs them (the bake is stopped, the UVs made, the bake started again). Any change to a mesh drops its UVs.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushLightmapUVs
    {
        static BrushLightmapUVs() { Lightmapping.bakeStarted += OnBakeStarted; }

        /// <summary>The brush meshes that contribute to global illumination, in the open scenes.</summary>
        public static List<MeshFilter> LitMeshes()
        {
            var list = new List<MeshFilter>();
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Exclude))
            {
                if (EditorUtility.IsPersistent(group)) continue;
                foreach (var t in BrushCsg.MeshObjects(group))
                {
                    if ((GameObjectUtility.GetStaticEditorFlags(t.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
                    if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 0) list.Add(mf);
                }
            }
            return list;
        }

        public static bool HasLightmapUVs(Mesh mesh) => mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1);

        /// <summary>Unwrap every lit brush mesh without lightmap UVs; returns how many were made.</summary>
        public static int Generate()
        {
            int made = 0;
            foreach (var mf in LitMeshes())
            {
                var mesh = mf.sharedMesh;
                if (HasLightmapUVs(mesh) || EditorUtility.IsPersistent(mesh)) continue;
                Unwrapping.GenerateSecondaryUVSet(mesh);
                EditorSceneManager.MarkSceneDirty(mf.gameObject.scene);
                made++;
            }
            return made;
        }

        [MenuItem("Tools/CSG Brush/Generate Lightmap UVs", false, 200)]
        static void Menu()
        {
            int made = Generate();
            Debug.Log(made == 0 ? "CSG Brush: every lit brush mesh already has lightmap UVs." : "CSG Brush: lightmap UVs made for " + made + " brush mesh" + (made == 1 ? "." : "es."));
        }

        static void OnBakeStarted()
        {
            bool missing = false;
            foreach (var mf in LitMeshes()) if (!HasLightmapUVs(mf.sharedMesh) && !EditorUtility.IsPersistent(mf.sharedMesh)) { missing = true; break; }
            if (!missing) return;
            // the bake has read the scene already: stop it, make the UVs, start it again
            Lightmapping.Cancel();
            Generate();
            EditorApplication.delayCall += () => Lightmapping.BakeAsync();
        }
    }
}

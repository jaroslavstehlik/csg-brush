using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// A brush group inside a prefab builds into the prefab: the generated meshes (render meshes and mesh colliders) are
    /// saved as sub-assets of the prefab file, so the prefab carries its own geometry and can be instantiated at
    /// runtime. Whenever a prefab is imported (created from a scene object, saved in Prefab Mode, overrides applied)
    /// and one of its groups lacks saved meshes, the prefab is rebuilt. A prefab without a group is a
    /// stamp and has nothing to build. A prefab open in Prefab Mode builds when Prefab Mode closes: writing its file
    /// while it is open makes Unity reload the stage, replacing every object being edited.
    /// </summary>
    public sealed class BrushPrefabBuild : AssetPostprocessor
    {
        const string MeshPrefix = "csg ";
        static bool s_Building;
        static readonly HashSet<string> s_Pending = new HashSet<string>();

        [InitializeOnLoadMethod]
        static void Init()
        {
            PrefabStage.prefabStageClosing += stage =>
            {
                var path = stage.assetPath;
                EditorApplication.delayCall += () => BuildIfNeeded(path);
            };
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (s_Building) return;
            foreach (var path in imported)
                if (path.EndsWith(".prefab") && NeedsBuild(path) && s_Pending.Add(path))
                    EditorApplication.delayCall += () => { s_Pending.Remove(path); BuildIfNeeded(path); };
        }

        /// <summary>Build a prefab that lacks saved meshes, unless it is open in Prefab Mode (it builds when that closes).</summary>
        public static void BuildIfNeeded(string path)
        {
            if (IsOpenInPrefabMode(path) || !NeedsBuild(path)) return;
            Build(path);
        }

        static bool IsOpenInPrefabMode(string path)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null && stage.assetPath == path;
        }

        /// <summary>A group in the prefab builds brushes but has no saved mesh or collider mesh.</summary>
        public static bool NeedsBuild(string path) => WhyBuild(path) != null;

        /// <summary>Why a prefab needs building, or null when it does not.</summary>
        public static string WhyBuild(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) return null;
            foreach (var group in root.GetComponentsInChildren<BrushGroup>(true))
            {
                if (BrushesOf(group).Count == 0) continue;
                var meshes = MeshChildren(group);
                if (meshes.Count == 0) return group.name + ": no mesh child";
                foreach (var mf in meshes)
                {
                    if (mf.sharedMesh == null) return group.name + ": " + mf.name + " has no mesh";
                    if (!EditorUtility.IsPersistent(mf.sharedMesh)) return group.name + ": " + mf.name + " mesh is not saved";
                }
                foreach (var mc in group.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (mc.sharedMesh == null) return group.name + ": collider " + mc.name + " has no mesh";
                    if (!EditorUtility.IsPersistent(mc.sharedMesh)) return group.name + ": collider " + mc.name + " mesh is not saved";
                }
            }
            return null;
        }

        /// <summary>Prefabs used by the open scenes that need building (saved before building existed).</summary>
        public static void BuildUsedPrefabs()
        {
            var paths = new HashSet<string>();
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include))
            {
                if (!PrefabUtility.IsPartOfPrefabInstance(group)) continue;
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(group);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            foreach (var path in paths) if (NeedsBuild(path)) Build(path);
        }

        /// <summary>The prefab file a group builds into: the asset itself, the prefab of an instance, or the prefab open in Prefab Mode. Null for a scene group.</summary>
        public static string PrefabPathOf(BrushGroup group)
        {
            if (group == null) return null;
            if (PrefabUtility.IsPartOfPrefabAsset(group)) return AssetDatabase.GetAssetPath(group);
            var stage = PrefabStageUtility.GetPrefabStage(group.gameObject);
            if (stage != null) return stage.assetPath;
            if (PrefabUtility.IsPartOfPrefabInstance(group)) return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(group);
            return null;
        }

        static bool HasGroup(string path) => path != null && path.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponentInChildren<BrushGroup>(true) != null;

        [MenuItem("Assets/CSG Brush/Rebuild Prefab", false, 2000)]
        static void RebuildSelected()
        {
            foreach (var o in Selection.objects)
            {
                var path = AssetDatabase.GetAssetPath(o);
                if (HasGroup(path)) Build(path);
            }
        }

        [MenuItem("Assets/CSG Brush/Rebuild Prefab", true)]
        static bool CanRebuildSelected()
        {
            foreach (var o in Selection.objects) if (HasGroup(AssetDatabase.GetAssetPath(o))) return true;
            return false;
        }

        [MenuItem("Tools/CSG Brush/Rebuild All Prefabs")]
        static void BuildAll()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (NeedsBuild(path)) Build(path);
            }
        }

        /// <summary>Rebuild every group of a prefab and save the generated meshes into the prefab file.</summary>
        public static void Build(string path)
        {
            if (s_Building) return;
            s_Building = true;
            try
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var group in contents.GetComponentsInChildren<BrushGroup>(true)) BrushCsg.Rebuild(group, BrushesOf(group));
                    // the meshes the contents now reference, by the sibling-index path of the object that holds them
                    var built = new Dictionary<string, (Mesh render, Mesh collider)>();
                    foreach (var mf in contents.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) built[PathOf(mf.transform, contents.transform)] = (mf.sharedMesh, null);
                    foreach (var mc in contents.GetComponentsInChildren<MeshCollider>(true))
                        if (mc.sharedMesh != null) { var key = PathOf(mc.transform, contents.transform); built.TryGetValue(key, out var e); built[key] = (e.render, mc.sharedMesh); }
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    // the saved prefab has the same hierarchy: give its components the meshes, stored in its own file
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var used = new HashSet<Mesh>();
                    foreach (var kv in built)
                    {
                        var t = FindByPath(asset.transform, kv.Key);
                        if (t == null) continue;
                        if (kv.Value.render != null && t.TryGetComponent<MeshFilter>(out var mf)) mf.sharedMesh = Persist(kv.Value.render, path, used);
                        if (kv.Value.collider != null && t.TryGetComponent<MeshCollider>(out var mc)) mc.sharedMesh = Persist(kv.Value.collider, path, used);
                        EditorUtility.SetDirty(t.gameObject);
                    }
                    // meshes of earlier builds nothing uses any more
                    foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                        if (o is Mesh old && old.name.StartsWith(MeshPrefix) && !used.Contains(old)) AssetDatabase.RemoveObjectFromAsset(old);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            finally { s_Building = false; }
        }

        static Mesh Persist(Mesh mesh, string path, HashSet<Mesh> used)
        {
            if (!EditorUtility.IsPersistent(mesh))
            {
                if (!mesh.name.StartsWith(MeshPrefix)) mesh.name = MeshPrefix + mesh.name;
                AssetDatabase.AddObjectToAsset(mesh, path);
            }
            used.Add(mesh);
            return mesh;
        }

        /// <summary>The brushes a group builds: below it, up to the next group, in hierarchy order.</summary>
        public static List<Brush> BrushesOf(BrushGroup group)
        {
            var list = new List<Brush>();
            // activeSelf, not activeInHierarchy: objects of a prefab asset are in no scene and never "active in hierarchy"
            void Walk(Transform t)
            {
                if (t != group.transform && t.TryGetComponent<BrushGroup>(out _)) return;
                if (!t.gameObject.activeSelf) return;
                if (t.TryGetComponent<Brush>(out var b) && b.enabled) list.Add(b);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            Walk(group.transform);
            return list;
        }

        static List<MeshFilter> MeshChildren(BrushGroup group)
        {
            var list = new List<MeshFilter>();
            foreach (Transform child in group.transform)
                if (BrushGroup.IsMeshChildName(child.name) && child.TryGetComponent<MeshFilter>(out var mf)) list.Add(mf);
            return list;
        }

        static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var c = t; c != root && c != null; c = c.parent) parts.Add(c.GetSiblingIndex().ToString());
            parts.Reverse();
            return string.Join("/", parts);
        }

        static Transform FindByPath(Transform root, string path)
        {
            var t = root;
            if (path.Length == 0) return t;
            foreach (var part in path.Split('/'))
            {
                int i = int.Parse(part);
                if (i >= t.childCount) return null;
                t = t.GetChild(i);
            }
            return t;
        }
    }
}

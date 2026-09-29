using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class CsgGroupTests
    {
        const string ScenePathA = "Assets/CsgGroupTestA.unity", ScenePathB = "Assets/CsgGroupTestB.unity", PrefabPath = "Assets/CsgGroupTestPrefab.prefab", StampPath = "Assets/CsgGroupTestStamp.prefab";

        [TearDown]
        public void Clean()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var p in new[] { ScenePathA, ScenePathB, PrefabPath, StampPath }) AssetDatabase.DeleteAsset(p);
        }

        static int Verts(CsgGroup g)
        {
            var t = BrushCsg.MeshObject(g, false);
            return t != null && t.GetComponent<MeshFilter>().sharedMesh != null ? t.GetComponent<MeshFilter>().sharedMesh.vertexCount : 0;
        }

        [Test]
        public void EachSceneBakesItsOwnBrushesWhenScenesAreOpenTogether()
        {
            var a = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, null, "BrushA");
            BrushApi.ForceUpdate(); EditorSceneManager.SaveScene(a, ScenePathA);
            var b = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BrushApi.Create(BrushShape.Cylinder, new Vector3(10f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "BrushB");
            BrushApi.ForceUpdate(); EditorSceneManager.SaveScene(b, ScenePathB);
            a = EditorSceneManager.OpenScene(ScenePathA, OpenSceneMode.Single);
            b = EditorSceneManager.OpenScene(ScenePathB, OpenSceneMode.Additive);
            BrushApi.ForceUpdate();
            var ga = BrushCsg.DefaultModel(a, false); var gb = BrushCsg.DefaultModel(b, false);
            Assert.IsNotNull(ga); Assert.IsNotNull(gb); Assert.AreNotEqual(ga, gb, "one automatic group per scene");
            Assert.AreEqual(a, ga.gameObject.scene); Assert.AreEqual(b, gb.gameObject.scene);
            var byModel = BrushCsg.BrushesByModel();
            Assert.AreEqual(1, byModel[ga].Count); Assert.AreEqual("BrushA", byModel[ga][0].name);
            Assert.AreEqual(1, byModel[gb].Count); Assert.AreEqual("BrushB", byModel[gb][0].name);
            Assert.Greater(Verts(ga), 0); Assert.Greater(Verts(gb), 0);
            Assert.AreNotEqual(Verts(ga), Verts(gb), "a box and a cylinder: each scene holds its own mesh");
        }

        static GameObject BuildGroupWithBrushes(string name)
        {
            var root = new GameObject(name); root.AddComponent<CsgGroup>();
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, root.transform, "Box");
            BrushApi.Create(BrushShape.Cylinder, new Vector3(4f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, root.transform, "Cylinder");
            BrushApi.ForceUpdate();
            return root;
        }

        [Test]
        public void AGroupInAPrefabBakesItsMeshesIntoThePrefab()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Assert.IsTrue(BrushPrefabBaking.NeedsBake(PrefabPath), "saving from the scene leaves the scene's meshes behind");
            BrushPrefabBaking.Bake(PrefabPath);
            Assert.IsNull(BrushPrefabBaking.WhyBake(PrefabPath));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mf = asset.GetComponentInChildren<MeshFilter>(true);
            Assert.IsNotNull(mf.sharedMesh); Assert.IsTrue(EditorUtility.IsPersistent(mf.sharedMesh)); Assert.AreEqual(PrefabPath, AssetDatabase.GetAssetPath(mf.sharedMesh), "stored in the prefab file");
            var mc = asset.GetComponentInChildren<MeshCollider>(true);
            Assert.IsNotNull(mc, "the cylinder is a mesh collider"); Assert.IsNotNull(mc.sharedMesh); Assert.IsTrue(EditorUtility.IsPersistent(mc.sharedMesh));
            var spawned = Object.Instantiate(asset);
            Assert.IsNotNull(spawned.GetComponentInChildren<MeshFilter>(true).sharedMesh, "a runtime instance has its mesh");
            Object.DestroyImmediate(spawned);
            // baking again replaces the meshes instead of piling them up
            BrushPrefabBaking.Bake(PrefabPath);
            int meshes = 0; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(PrefabPath)) if (o is Mesh) meshes++;
            Assert.AreEqual(2, meshes, "one render mesh and one collider mesh");
        }

        [Test]
        public void ASceneInstanceOfABakedPrefabUsesThePrefabsMeshesUntilItsBrushesChange()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); BrushPrefabBaking.Bake(PrefabPath);
            Object.DestroyImmediate(root);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var assetMesh = asset.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            int assetVerts = assetMesh.vertexCount;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            BrushApi.ForceUpdate();
            Assert.AreEqual(assetMesh, inst.GetComponentInChildren<MeshFilter>(true).sharedMesh, "the instance renders the prefab's baked mesh");
            Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false), "and has no overrides");
            // change a brush in the instance: now the scene bakes it, the prefab's mesh is untouched
            var box = inst.transform.Find("Box").GetComponent<Brush>();
            BrushApi.SetSize(box, new Vector3(2f, 4f, 2f));
            BrushApi.ForceUpdate();
            var mesh = inst.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.AreNotEqual(assetMesh, mesh); Assert.IsFalse(EditorUtility.IsPersistent(mesh));
            Assert.AreEqual(assetVerts, assetMesh.vertexCount, "the baked prefab mesh was not rewritten");
        }

        [Test]
        public void TheInspectorNamesWhatBakesABrush()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var free = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, null, "Free");
            EditorSceneManager.SaveScene(scene, ScenePathA);
            StringAssert.StartsWith("scene", BrushCsg.BakedBy(free, out var sceneRef));
            Assert.IsInstanceOf<SceneAsset>(sceneRef, "the scene itself, clickable");
            var root = BuildGroupWithBrushes("Level");
            var grouped = root.transform.Find("Box").GetComponent<Brush>();
            BrushCsg.BakedBy(grouped, out var groupRef);
            Assert.AreEqual(root.GetComponent<CsgGroup>(), groupRef);
            // a prefab without a group: a stamp
            var stampRoot = new GameObject("Doorway");
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 2f, 1f), Quaternion.identity, stampRoot.transform, "Cut");
            PrefabUtility.SaveAsPrefabAsset(stampRoot, StampPath);
            var stampAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StampPath);
            StringAssert.Contains("stamp", BrushCsg.BakedBy(stampAsset.GetComponentInChildren<Brush>(true), out var none));
            Assert.IsNull(none);
            Assert.IsFalse(BrushPrefabBaking.NeedsBake(StampPath), "a stamp has nothing to bake");
        }

        [Test]
        public void GeneratedObjectsAreHiddenInScenesAndInPrefabInstancesUnlessDebugging()
        {
            var settings = BrushSettings.instance; bool saved = settings.showGenerated;
            try
            {
                settings.showGenerated = false;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = BuildGroupWithBrushes("Level");
                foreach (Transform child in root.transform)
                    if (BrushSync.IsGenerated(child)) Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.NotEditable, child.gameObject.hideFlags, child.name + " hidden right after the build");
                Assert.AreNotEqual(HideFlags.None, root.GetComponent<Colliders.ConvexColliderSettings>().hideFlags & HideFlags.HideInInspector);
                // an instance of a baked prefab: hidden too, without becoming an override
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); BrushPrefabBaking.Bake(PrefabPath);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                BrushSync.ApplyVisibility();
                foreach (Transform child in inst.transform)
                    if (BrushSync.IsGenerated(child)) Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.NotEditable, child.gameObject.hideFlags, "instance " + child.name);
                Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false), "hiding is not an override");
                // debugging shows them, not editable
                settings.showGenerated = true;
                BrushSync.ApplyVisibility();
                Assert.AreEqual(HideFlags.NotEditable, BrushCsg.MeshObject(root.GetComponent<CsgGroup>(), false).gameObject.hideFlags);
            }
            finally { settings.showGenerated = saved; BrushSync.ApplyVisibility(); }
        }

        [Test]
        public void ARebakeFindsThePrefabOfAGroupWhereverItIsSeen()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sceneGroup = BuildGroupWithBrushes("Level");
            Assert.IsNull(BrushPrefabBaking.PrefabPathOf(sceneGroup.GetComponent<CsgGroup>()), "a scene group has no prefab to rebake");
            PrefabUtility.SaveAsPrefabAsset(sceneGroup, PrefabPath);
            Object.DestroyImmediate(sceneGroup);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(PrefabPath, BrushPrefabBaking.PrefabPathOf(asset.GetComponent<CsgGroup>()));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Assert.AreEqual(PrefabPath, BrushPrefabBaking.PrefabPathOf(inst.GetComponent<CsgGroup>()));
            // a forced rebake of a prefab whose meshes were lost brings them back
            BrushPrefabBaking.Bake(BrushPrefabBaking.PrefabPathOf(inst.GetComponent<CsgGroup>()));
            Assert.IsNull(BrushPrefabBaking.WhyBake(PrefabPath));
        }
    }
}

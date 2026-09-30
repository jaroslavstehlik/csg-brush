using System.Collections;
using System.Reflection;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace CsgBrush.Tests
{
    public class BrushGroupTests
    {
        const string ScenePathA = "Assets/BrushGroupTestA.unity", ScenePathB = "Assets/BrushGroupTestB.unity", PrefabPath = "Assets/BrushGroupTestPrefab.prefab", StampPath = "Assets/BrushGroupTestStamp.prefab";

        [TearDown]
        public void Clean()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (var p in new[] { ScenePathA, ScenePathB, PrefabPath, StampPath }) AssetDatabase.DeleteAsset(p);
        }

        static int Verts(BrushGroup g)
        {
            var t = BrushCsg.MeshObject(g, false);
            return t != null && t.GetComponent<MeshFilter>().sharedMesh != null ? t.GetComponent<MeshFilter>().sharedMesh.vertexCount : 0;
        }

        [Test]
        public void EachSceneBuildsItsOwnBrushesWhenScenesAreOpenTogether()
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
            var root = new GameObject(name); root.AddComponent<BrushGroup>();
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, root.transform, "Box");
            BrushApi.Create(BrushShape.Cylinder, new Vector3(4f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, root.transform, "Cylinder");
            BrushApi.ForceUpdate();
            return root;
        }

        [Test]
        public void AGroupInAPrefabBuildsItsMeshesIntoThePrefab()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Assert.IsTrue(BrushPrefabBuild.NeedsBuild(PrefabPath), "saving from the scene leaves the scene's meshes behind");
            BrushPrefabBuild.Build(PrefabPath);
            Assert.IsNull(BrushPrefabBuild.WhyBuild(PrefabPath));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var mf = asset.GetComponentInChildren<MeshFilter>(true);
            Assert.IsNotNull(mf.sharedMesh); Assert.IsTrue(EditorUtility.IsPersistent(mf.sharedMesh)); Assert.AreEqual(PrefabPath, AssetDatabase.GetAssetPath(mf.sharedMesh), "stored in the prefab file");
            var mc = asset.GetComponentInChildren<MeshCollider>(true);
            Assert.IsNotNull(mc, "the cylinder is a mesh collider"); Assert.IsNotNull(mc.sharedMesh); Assert.IsTrue(EditorUtility.IsPersistent(mc.sharedMesh));
            var spawned = Object.Instantiate(asset);
            Assert.IsNotNull(spawned.GetComponentInChildren<MeshFilter>(true).sharedMesh, "a runtime instance has its mesh");
            Object.DestroyImmediate(spawned);
            // building again replaces the meshes instead of piling them up
            BrushPrefabBuild.Build(PrefabPath);
            int meshes = 0; foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(PrefabPath)) if (o is Mesh) meshes++;
            Assert.AreEqual(2, meshes, "one render mesh and one collider mesh");
        }

        [Test]
        public void ASceneInstanceOfABuiltPrefabUsesThePrefabsMeshesUntilItsBrushesChange()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); BrushPrefabBuild.Build(PrefabPath);
            Object.DestroyImmediate(root);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var assetMesh = asset.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            int assetVerts = assetMesh.vertexCount;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            BrushApi.ForceUpdate();
            Assert.AreEqual(assetMesh, inst.GetComponentInChildren<MeshFilter>(true).sharedMesh, "the instance renders the prefab's built mesh");
            Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false), "and has no overrides");
            // change a brush in the instance: now the scene builds it, the prefab's mesh is untouched
            var box = inst.transform.Find("Box").GetComponent<Brush>();
            BrushApi.SetSize(box, new Vector3(2f, 4f, 2f));
            BrushApi.ForceUpdate();
            var mesh = inst.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.AreNotEqual(assetMesh, mesh); Assert.IsFalse(EditorUtility.IsPersistent(mesh));
            Assert.AreEqual(assetVerts, assetMesh.vertexCount, "the built prefab mesh was not rewritten");
        }

        [Test]
        public void TheInspectorNamesWhatBuildsABrush()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var free = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, null, "Free");
            EditorSceneManager.SaveScene(scene, ScenePathA);
            StringAssert.StartsWith("scene", BrushCsg.BuiltBy(free, out var sceneRef));
            Assert.IsInstanceOf<SceneAsset>(sceneRef, "the scene itself, clickable");
            var root = BuildGroupWithBrushes("Level");
            var grouped = root.transform.Find("Box").GetComponent<Brush>();
            BrushCsg.BuiltBy(grouped, out var groupRef);
            Assert.AreEqual(root.GetComponent<BrushGroup>(), groupRef);
            // a prefab without a group: a stamp
            var stampRoot = new GameObject("Doorway");
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 2f, 1f), Quaternion.identity, stampRoot.transform, "Cut");
            PrefabUtility.SaveAsPrefabAsset(stampRoot, StampPath);
            var stampAsset = AssetDatabase.LoadAssetAtPath<GameObject>(StampPath);
            StringAssert.Contains("stamp", BrushCsg.BuiltBy(stampAsset.GetComponentInChildren<Brush>(true), out var none));
            Assert.IsNull(none);
            Assert.IsFalse(BrushPrefabBuild.NeedsBuild(StampPath), "a stamp has nothing to build");
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
                // an instance of a built prefab: hidden too, without becoming an override
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); BrushPrefabBuild.Build(PrefabPath);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                BrushSync.ApplyVisibility();
                foreach (Transform child in inst.transform)
                    if (BrushSync.IsGenerated(child)) Assert.AreEqual(HideFlags.HideInHierarchy | HideFlags.NotEditable, child.gameObject.hideFlags, "instance " + child.name);
                Assert.IsFalse(PrefabUtility.HasPrefabInstanceAnyOverrides(inst, false), "hiding is not an override");
                // debugging shows them, not editable
                settings.showGenerated = true;
                BrushSync.ApplyVisibility();
                Assert.AreEqual(HideFlags.NotEditable, BrushCsg.MeshObject(root.GetComponent<BrushGroup>(), false).gameObject.hideFlags);
            }
            finally { settings.showGenerated = saved; BrushSync.ApplyVisibility(); }
        }

        [Test]
        public void ARebuildFindsThePrefabOfAGroupWhereverItIsSeen()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sceneGroup = BuildGroupWithBrushes("Level");
            Assert.IsNull(BrushPrefabBuild.PrefabPathOf(sceneGroup.GetComponent<BrushGroup>()), "a scene group has no prefab to rebuild");
            PrefabUtility.SaveAsPrefabAsset(sceneGroup, PrefabPath);
            Object.DestroyImmediate(sceneGroup);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(PrefabPath, BrushPrefabBuild.PrefabPathOf(asset.GetComponent<BrushGroup>()));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Assert.AreEqual(PrefabPath, BrushPrefabBuild.PrefabPathOf(inst.GetComponent<BrushGroup>()));
            // a forced rebuild of a prefab whose meshes were lost brings them back
            BrushPrefabBuild.Build(BrushPrefabBuild.PrefabPathOf(inst.GetComponent<BrushGroup>()));
            Assert.IsNull(BrushPrefabBuild.WhyBuild(PrefabPath));
        }

        [Test]
        public void RebuildBuildsASceneGroupAgainFromScratch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Level");
            var group = root.GetComponent<BrushGroup>();
            var before = BrushCsg.MeshObject(group, false).GetComponent<MeshFilter>().sharedMesh.vertexCount;
            BrushCsg.Rebuild(group);
            Assert.AreEqual(2, BrushCsg.LastBuiltSolids, "every brush solid built again, none from the cache");
            Assert.Greater(Colliders.Editor.ConvexColliderBuilder.LastReusedPieces + Colliders.Editor.ConvexColliderBuilder.LastCreatedPieces, 0, "the colliders were processed, not skipped");
            Assert.AreEqual(before, BrushCsg.MeshObject(group, false).GetComponent<MeshFilter>().sharedMesh.vertexCount, "same result");
        }

        [UnityTest]
        public IEnumerator APrefabOpenInPrefabModeBuildsWhenPrefabModeCloses()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            BrushPrefabBuild.Build(PrefabPath);
            var stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            var brush = stage.prefabContentsRoot.GetComponentInChildren<Brush>();
            brush.transform.position += Vector3.up;
            BrushApi.ForceUpdate();
            // what Auto Save does after an edit; the stage's own meshes are not in the file
            var save = typeof(PrefabStage).GetMethod("SavePrefab", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, System.Type.EmptyTypes, null);
            if (save == null) Assert.Ignore("PrefabStage.SavePrefab not found in this Unity version");
            save.Invoke(stage, null);
            Assert.IsTrue(BrushPrefabBuild.NeedsBuild(PrefabPath));
            BrushPrefabBuild.BuildIfNeeded(PrefabPath);
            Assert.IsTrue(BrushPrefabBuild.NeedsBuild(PrefabPath), "not built while open: writing the file would make Unity reload the stage");
            Assert.AreEqual(brush, PrefabStageUtility.GetCurrentPrefabStage().prefabContentsRoot.GetComponentInChildren<Brush>(), "the stage keeps the objects being edited");
            StageUtility.GoToMainStage();
            for (int i = 0; i < 5 && BrushPrefabBuild.NeedsBuild(PrefabPath); i++) yield return null;
            Assert.IsNull(BrushPrefabBuild.WhyBuild(PrefabPath), "built once Prefab Mode closed");
        }

        [Test]
        public void InPrefabModeOnlyThePrefabsBrushesCanBeClicked()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Prop");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction); // the scene keeps an instance in the same place
            Assert.AreEqual(2, BrushHooks.BrushesInView().Count, "the scene's brushes");
            var stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            try
            {
                var inView = BrushHooks.BrushesInView();
                Assert.AreEqual(2, inView.Count, "the prefab's brushes, not also the hidden scene's");
                foreach (var b in inView) Assert.AreEqual(stage.scene, b.gameObject.scene);
            }
            finally { StageUtility.GoToMainStage(); }
            Assert.AreEqual(root.scene, BrushHooks.BrushesInView()[0].gameObject.scene, "back in the scene");
        }

        [UnityTest]
        public IEnumerator APrefabAssetsBrushesAreNeitherSnappedNorBuilt()
        {
            bool snap = BrushSettings.instance.snapToGrid; int gridIndex = BrushSettings.instance.gridIndex; // the settings object is reloaded by asset refreshes: never held
            try
            {
                for (int i = 0; i < 5; i++) yield return null; // builds queued by earlier tests for the same path land first
                BrushSettings.instance.snapToGrid = true; BrushSettings.instance.gridIndex = BrushSettings.instance.gridSizes.Length - 1; // the coarsest step: the brush below is surely off it
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("Prop"); root.AddComponent<BrushGroup>();
                root.transform.position = new Vector3(116.75f, 29.75f, 34f);
                var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4.5f, 2.5f, 4f), Quaternion.identity, root.transform, "Box");
                var offGrid = new Vector3(0.63f, 0.25f, -0.86f); // as prefabs made before snapping, or on another grid, can be
                brush.transform.localPosition = offGrid;
                // Unity validates the prefab's brushes on every import (each Auto Save in Prefab Mode)
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Object.DestroyImmediate(root);
                BrushPrefabBuild.Build(PrefabPath);
                for (int i = 0; i < 5; i++) yield return null;
                BrushApi.ForceUpdate();
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                Assert.Less(Vector3.Distance(offGrid, asset.GetComponentInChildren<Brush>(true).transform.localPosition), 1e-4f, "the asset is not edited behind the user's back");
                Assert.IsTrue(EditorUtility.IsPersistent(asset.GetComponentInChildren<MeshFilter>(true).sharedMesh), "the asset keeps its built mesh: its group is not built in place");
            }
            finally { BrushSettings.instance.snapToGrid = snap; BrushSettings.instance.gridIndex = gridIndex; }
        }

        [Test]
        public void ABrushStepsPastItsVisibleNeighbourAndOverHiddenOnes()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = BuildGroupWithBrushes("Level"); // Box, Cylinder, then the hidden mesh and collider objects
            var box = root.transform.Find("Box").GetComponent<Brush>();
            var cylinder = root.transform.Find("Cylinder").GetComponent<Brush>();
            Assert.AreEqual(0, box.transform.GetSiblingIndex());
            BrushApi.Step(box, 1);
            Assert.AreEqual(0, cylinder.transform.GetSiblingIndex(), "down one: past the cylinder");
            Assert.AreEqual(1, box.transform.GetSiblingIndex());
            BrushApi.Step(box, 1);
            Assert.AreEqual(1, box.transform.GetSiblingIndex(), "only hidden generated objects below: it stays");
            Undo.IncrementCurrentGroup();
            BrushApi.Step(box, -1);
            Assert.AreEqual(0, box.transform.GetSiblingIndex(), "up one");
            Undo.PerformUndo();
            Assert.AreEqual(1, box.transform.GetSiblingIndex(), "a step is undoable");
        }

        [Test]
        public void ASubtractiveBrushIsClickableOnlyWhileCutsAreShown()
        {
            bool cuts = BrushSettings.instance.showCuts;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cut = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "Cut");
                cut.operation = BrushOperation.Subtract;
                var ray = new Ray(new Vector3(0f, 0f, -10f), Vector3.forward);
                BrushSettings.instance.showCuts = false;
                Assert.IsNull(BrushHooks.PickBrushSurface(ray, out _, out _), "hidden cut: the click goes through");
                BrushSettings.instance.showCuts = true;
                Assert.AreEqual(cut, BrushHooks.PickBrushSurface(ray, out var point, out _), "shown cut: clickable");
                Assert.AreEqual(-1f, point.z, 1e-4f, "on its front face");
            }
            finally { BrushSettings.instance.showCuts = cuts; }
        }
    }
}

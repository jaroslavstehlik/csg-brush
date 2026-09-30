using System.Diagnostics;
using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>The registry of active brushes and the caches built on it: always in step with the scene, and fast in a large level.</summary>
    public class BrushRegistryTests
    {
        const string PrefabPath = "Assets/BrushRegistryTestPrefab.prefab";

        [TearDown]
        public void Clean()
        {
            StageUtility.GoToMainStage();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        static bool Registered(Brush b) => Brush.Active.Contains(b);

        [Test]
        public void TheRegistryHoldsExactlyTheEnabledBrushesOnActiveObjects()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, null, "A");
            var b = BrushApi.Create(BrushShape.Box, new Vector3(4f, 0f, 0f), Vector3.one, Quaternion.identity, null, "B");
            var c = BrushApi.Create(BrushShape.Box, new Vector3(8f, 0f, 0f), Vector3.one, Quaternion.identity, null, "C");
            Assert.AreEqual(3, Brush.Active.Count);
            a.enabled = false;
            Assert.IsFalse(Registered(a), "a disabled brush leaves");
            Assert.IsTrue(Registered(b) && Registered(c), "the others stay (swap-remove keeps them)");
            a.enabled = true;
            Assert.IsTrue(Registered(a));
            b.gameObject.SetActive(false);
            Assert.IsFalse(Registered(b), "an inactive object's brush leaves");
            b.gameObject.SetActive(true);
            Undo.IncrementCurrentGroup();
            BrushApi.Delete(c);
            Assert.AreEqual(2, Brush.Active.Count, "a deleted brush leaves");
            Undo.PerformUndo();
            Assert.AreEqual(3, Brush.Active.Count, "an undone delete comes back");
            Assert.AreEqual(Brush.Active.Count, Brush.Active.Distinct().Count(), "never twice");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.AreEqual(0, Brush.Active.Count, "a closed scene's brushes leave");
        }

        [Test]
        public void PrefabAssetsAreNeverRegisteredButPrefabModeIs()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Prop");
            BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, root.transform, "Box");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Assert.AreEqual(0, Brush.Active.Count, "the prefab asset's brush is not in the registry");
            var stage = PrefabStageUtility.OpenPrefab(PrefabPath);
            var inStage = stage.prefabContentsRoot.GetComponentInChildren<Brush>();
            Assert.IsTrue(Registered(inStage), "Prefab Mode's brush is");
            StageUtility.GoToMainStage();
            Assert.AreEqual(0, Brush.Active.Count, "and leaves when Prefab Mode closes");
        }

        [Test]
        public void GroupingFollowsReordersAndDeletedGroups()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Level"); var group = root.AddComponent<BrushGroup>();
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, root.transform, "A");
            var b = BrushApi.Create(BrushShape.Box, new Vector3(4f, 0f, 0f), Vector3.one, Quaternion.identity, root.transform, "B");
            CollectionAssert.AreEqual(new[] { a, b }, BrushCsg.BrushesByModel()[group]);
            BrushApi.ToFirst(b);
            CollectionAssert.AreEqual(new[] { b, a }, BrushCsg.BrushesByModel()[group], "a reorder is seen at once");
            Object.DestroyImmediate(group);
            Assert.IsFalse(BrushCsg.BrushesByModel().ContainsKey(group), "a destroyed group is not handed out");
        }

        [Test]
        public void PickingFollowsMovesMadeOutsideTheTools()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, null, "Box");
            var down = new Ray(new Vector3(0f, 10f, 0f), Vector3.down);
            Assert.AreEqual(box, BrushHooks.PickBrushSurface(down, out _, out _));
            box.transform.position = new Vector3(10f, 0f, 0f); // a script, no Undo
            BrushSnap.ProcessChanged(); // what the editor loop does every frame
            Assert.IsNull(BrushHooks.PickBrushSurface(down, out _, out _), "gone from where it was");
            Assert.AreEqual(box, BrushHooks.PickBrushSurface(new Ray(new Vector3(10f, 10f, 0f), Vector3.down), out var point, out _), "found where it is");
            Assert.AreEqual(1f, point.y, 1e-4f, "on its top face");
            BrushApi.SetSize(box, new Vector3(2f, 4f, 2f));
            BrushHooks.PickBrushSurface(new Ray(new Vector3(10f, 10f, 0f), Vector3.down), out point, out _);
            Assert.Greater(point.y, 1.5f, "a new size is seen at once");
        }

        [Test]
        public void RotatedParentsFollowTheParentsTransform()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var parent = new GameObject("Folder").transform;
            BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, parent, "Box");
            BrushSnap.ProcessChanged();
            Assert.AreEqual(0, BrushSnap.TransformedParents().Count);
            parent.rotation = Quaternion.Euler(0f, 30f, 0f); // no Undo: only the children's changed flags tell
            BrushSnap.ProcessChanged();
            Assert.AreEqual(1, BrushSnap.TransformedParents().Count, "a parent rotated after the last answer is reported");
        }

        static double Best(System.Action action, int runs = 10)
        {
            action();
            double best = double.MaxValue;
            for (int i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); action(); sw.Stop(); best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds); }
            return best;
        }

        [Test]
        public void EveryFrameBookkeepingStaysUnderAMillisecondWithFiveThousandBrushes()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int f = 0; f < 50; f++)
            {
                var folder = new GameObject("Folder " + f).transform;
                for (int i = 0; i < 100; i++)
                {
                    var go = new GameObject("Brush");
                    go.transform.SetParent(folder, false);
                    go.transform.position = new Vector3(f * 4f, 0f, i * 4f);
                    go.AddComponent<Brush>().size = new Vector3(2f, 2f, 2f);
                }
            }
            Assert.AreEqual(5000, Brush.Active.Count);
            BrushSnap.ProcessChanged();
            var miss = new Ray(new Vector3(100f, 50f, 202f), Vector3.down);
            var report = "snap check " + Best(() => BrushSnap.ProcessChanged()).ToString("F3")
                + " ms, rotated parents " + Best(() => BrushSnap.TransformedParents()).ToString("F3")
                + " ms, pick " + Best(() => BrushHooks.PickBrushSurface(miss, out _, out _)).ToString("F3")
                + " ms, grouping " + Best(() => BrushCsg.BrushesByModel()).ToString("F3") + " ms";
            UnityEngine.Debug.Log("MGMT budget: " + report);
            Assert.Less(Best(() => BrushSnap.ProcessChanged()), 1.0, report);
            Assert.Less(Best(() => BrushSnap.TransformedParents()), 1.0, report);
            Assert.Less(Best(() => BrushHooks.PickBrushSurface(miss, out _, out _)), 1.0, report);
            Assert.Less(Best(() => BrushCsg.BrushesByModel()), 1.0, report);
        }
    }
}

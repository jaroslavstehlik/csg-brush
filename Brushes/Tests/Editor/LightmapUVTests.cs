using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Lit brush meshes get lightmap UVs once an edit is finished, keep them in the saved scene, and keep both when the
    /// scene opens again: a bake stays valid until the brushes are edited.
    /// </summary>
    public class LightmapUVTests
    {
        const string ScenePath = "Assets/__lightmap_uv_test.unity";
        bool m_Auto;

        [SetUp]
        public void Open() { m_Auto = BrushSettings.instance.autoLightmapUVs; BrushSettings.instance.autoLightmapUVs = true; EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }

        [TearDown]
        public void Close()
        {
            BrushSettings.instance.autoLightmapUVs = m_Auto;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
        }

        static BrushGroup Group() => Object.FindAnyObjectByType<BrushGroup>(FindObjectsInactive.Include);

        static MeshFilter LitMesh() => BrushLightmapUVs.LitMeshes(Group())[0];

        static Brush Room(Transform group = null)
        {
            var floor = BrushApi.Create(BrushShape.Box, new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 8f), Quaternion.identity, group, "Floor");
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 3.8f), new Vector3(8f, 3f, 0.4f), Quaternion.identity, group, "Wall");
            var cut = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 3.8f), new Vector3(1f, 2f, 1f), Quaternion.identity, group, "Door");
            cut.operation = BrushOperation.Subtract;
            BrushApi.ForceUpdate();
            return floor;
        }

        [Test]
        public void AFinishedEditGetsLightmapUVsAndAnotherEditDropsThem()
        {
            var floor = Room();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "not while building");
            BrushLightmapUVs.FlushPending(); // what the editor does once the edit is finished
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "made once the edit is finished");
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
            BrushApi.Move(floor, new Vector3(0f, -0.5f, 1f));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "an edit drops them");
            BrushLightmapUVs.FlushPending();
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "and they are made again");
        }

        [Test]
        public void WithoutAutoTheyAreMadeOnlyWhenAsked()
        {
            BrushSettings.instance.autoLightmapUVs = false;
            Room();
            BrushLightmapUVs.FlushPending();
            Assert.AreEqual(1, BrushLightmapUVs.Missing(Group()).Count, "missing until asked");
            Assert.AreEqual(1, BrushLightmapUVs.Generate(Group()));
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
        }

        [Test]
        public void OtherUnwrapSettingsMakeTheUVsAgain()
        {
            Room();
            BrushLightmapUVs.FlushPending();
            Group().rendering.lightmapPackMargin = 8f;
            Assert.AreEqual(1, BrushLightmapUVs.Missing(Group()).Count, "made with other settings");
            Assert.AreEqual(1, BrushLightmapUVs.Generate(Group()));
            Assert.AreEqual(0, BrushLightmapUVs.Missing(Group()).Count);
        }

        [Test]
        public void AReopenedSceneKeepsItsMeshesTheirLightmapUVsAndWhichBrushMadeEachTriangle()
        {
            Room();
            BrushLightmapUVs.FlushPending();
            var uv2 = LitMesh().sharedMesh.uv2; var vertices = LitMesh().sharedMesh.vertices;
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); // the editor clears its caches and builds every group
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate();
            var mesh = LitMesh().sharedMesh;
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(mesh), "the lightmap UVs are still there");
            CollectionAssert.AreEqual(uv2, mesh.uv2, "the same UVs");
            CollectionAssert.AreEqual(vertices, mesh.vertices, "on the same mesh: it was kept, not built again");
            var owners = BrushCsg.TriangleBrushes(LitMesh().transform);
            Assert.IsNotNull(owners, "which brush made each triangle is known");
            Assert.AreEqual(mesh.triangles.Length / 3, owners.Length);
            Assert.IsTrue(System.Array.Exists(owners, b => b != null && b.name == "Wall"), "the wall's triangles are the wall's");

            var floor = GameObject.Find("Floor").GetComponent<Brush>();
            BrushApi.Move(floor, new Vector3(0f, -0.5f, 1f));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "an edit still builds it again");
        }

        [Test]
        public void RebuildBuildsAKeptMeshAgain()
        {
            Room(new GameObject("Level").AddComponent<BrushGroup>().transform);
            BrushLightmapUVs.FlushPending();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "kept");
            BrushCsg.Rebuild(Group());
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(LitMesh().sharedMesh), "Rebuild builds it from the brushes");
        }
    }
}

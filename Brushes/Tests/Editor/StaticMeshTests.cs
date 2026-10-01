using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>Static and moving brushes carve each other and render apart; lit meshes get lightmap UVs only when asked.</summary>
    public class StaticMeshTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static StaticEditorFlags Default => BrushSettings.instance.defaultModelStaticFlags;

        static BrushGroup Group() => Object.FindAnyObjectByType<BrushGroup>(FindObjectsInactive.Include);

        static HashSet<Brush> BrushesIn(Transform meshObject) => new HashSet<Brush>(BrushCsg.TriangleBrushes(meshObject));

        [Test]
        public void NewBrushesStartWithTheProjectsStaticFlags()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            Assert.AreEqual(Default, GameObjectUtility.GetStaticEditorFlags(brush.gameObject));
        }

        [Test]
        public void StaticAndMovingBrushesCarveTogetherButRenderInTheirOwnMeshes()
        {
            var wall = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 0.4f), Quaternion.identity, null, "Wall");
            var door = BrushApi.Create(BrushShape.Door, new Vector3(-1.5f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.6f), Quaternion.identity, null, "Door");
            BrushApi.SetStaticFlags(door, 0); // a moving cut still carves the static wall
            var platform = BrushApi.Create(BrushShape.Box, new Vector3(2f, 1.5f, 0.5f), new Vector3(1f, 1f, 1f), Quaternion.identity, null, "Platform");
            BrushApi.SetStaticFlags(platform, 0); // overlaps the wall: they are united
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();

            var group = Group();
            var still = BrushCsg.MeshObject(group, 0, Default, false);
            var moving = BrushCsg.MeshObject(group, 0, 0, false);
            Assert.IsNotNull(still, "the static mesh"); Assert.IsNotNull(moving, "and a moving one");
            Assert.AreEqual("<[mesh moving]>", moving.name);
            Assert.AreEqual(Default, GameObjectUtility.GetStaticEditorFlags(still.gameObject), "each mesh has its brushes' flags");
            Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(moving.gameObject));

            CollectionAssert.AreEquivalent(new[] { platform }, BrushesIn(moving), "only the platform renders as moving");
            var inStill = BrushesIn(still);
            Assert.IsTrue(inStill.Contains(wall));
            Assert.IsTrue(inStill.Contains(door), "the doorway's sides are the wall's: static, lit with it");
            Assert.IsFalse(inStill.Contains(platform));

            Assert.AreEqual(0, Physics.OverlapBox(new Vector3(-1.5f, 1f, 0f), new Vector3(0.3f, 0.3f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, "the moving cut carves the static wall");
            Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(PieceOf(platform)), "colliders follow their brush");
            Assert.AreEqual(Default, GameObjectUtility.GetStaticEditorFlags(PieceOf(wall)));

            BrushApi.SetStaticFlags(platform, Default); // made static: back in the static mesh
            BrushApi.ForceUpdate();
            Assert.IsNull(BrushCsg.MeshObject(group, 0, 0, false), "no moving brush left: its mesh is gone");
            Assert.IsTrue(BrushesIn(BrushCsg.MeshObject(group, 0, Default, false)).Contains(platform));
        }

        static GameObject PieceOf(Brush brush)
        {
            foreach (var p in Object.FindObjectsByType<CsgBrush.Colliders.ConvexPiece>(FindObjectsInactive.Include)) if (p.brushName == brush.name) return p.gameObject;
            Assert.Fail("no piece for " + brush.name); return null;
        }

        [Test]
        public void LightmapUVsAreMadeOnlyWhenAskedAndOnlyForLitMeshes()
        {
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f), Quaternion.identity, null, "Room");
            var crate = BrushApi.Create(BrushShape.Box, new Vector3(10f, 0.5f, 0f), Vector3.one, Quaternion.identity, null, "Crate");
            BrushApi.SetStaticFlags(crate, 0);
            BrushApi.ForceUpdate();
            var group = Group();
            var lit = BrushCsg.MeshObject(group, 0, Default, false).GetComponent<MeshFilter>().sharedMesh;
            var moving = BrushCsg.MeshObject(group, 0, 0, false).GetComponent<MeshFilter>().sharedMesh;
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(lit), "not while building");
            Assert.AreEqual(1, BrushLightmapUVs.Generate(), "the lit mesh only");
            Assert.IsTrue(BrushLightmapUVs.HasLightmapUVs(lit));
            Assert.IsFalse(BrushLightmapUVs.HasLightmapUVs(moving), "a moving mesh is not lightmapped");
            Assert.AreEqual(0, BrushLightmapUVs.Generate(), "already made");
        }

        static SerializedProperty RendererProperty(Transform meshObject, string name) => new SerializedObject(meshObject.GetComponent<MeshRenderer>()).FindProperty(name);

        [Test]
        public void AGroupsRenderingSettingsGoToEveryMeshOfTheGroup()
        {
            var level = new GameObject("Level"); var group = level.AddComponent<BrushGroup>();
            var wall = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 0.4f), Quaternion.identity, level.transform, "Wall");
            var crate = BrushApi.Create(BrushShape.Box, new Vector3(5f, 0.5f, 0f), Vector3.one, Quaternion.identity, level.transform, "Crate");
            BrushApi.SetStaticFlags(crate, 0);
            group.rendering.scaleInLightmap = 0.25f;
            group.rendering.castShadows = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            BrushApi.ForceUpdate();
            var still = BrushCsg.MeshObject(group, 0, Default, false); var moving = BrushCsg.MeshObject(group, 0, 0, false);
            foreach (var t in new[] { still, moving })
            {
                Assert.AreEqual(0.25f, RendererProperty(t, "m_ScaleInLightmap").floatValue, 1e-5f, t.name + ": the group's lightmap scale");
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.TwoSided, t.GetComponent<MeshRenderer>().shadowCastingMode, t.name + ": and its shadows");
                Assert.Greater(t.GetComponent<MeshRenderer>().sharedMaterials.Length, 0, "materials still come from the brushes");
            }
            // changed later: passed on without a rebuild
            group.rendering.scaleInLightmap = 2f;
            BrushCsg.ApplyRendererSettings(group);
            Assert.AreEqual(2f, RendererProperty(still, "m_ScaleInLightmap").floatValue, 1e-5f);
            // a group left alone: Unity's defaults
            var plain = BrushApi.Create(BrushShape.Box, new Vector3(30f, 0.5f, 0f), Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            var def = BrushCsg.DefaultModel(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), false);
            Assert.AreEqual(1f, RendererProperty(BrushCsg.MeshObject(def, false), "m_ScaleInLightmap").floatValue, 1e-5f);
            Assert.IsNotNull(wall);
        }
    }
}

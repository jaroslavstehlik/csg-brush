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

        [Test]
        public void ABrushsMaterialGoesOnlyOnItsOwnFaces()
        {
            var a = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity, null, "A");
            BrushApi.ForceUpdate();
            var b = BrushApi.Create(BrushShape.Box, new Vector3(2f, 0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity, null, "B"); // touching A: one island
            var red = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "red" };
            BrushApi.SetMaterial(b, red);
            BrushApi.ForceUpdate();
            var t = BrushCsg.MeshObject(Group(), 0, Default, false);
            var mesh = t.GetComponent<MeshFilter>().sharedMesh; var mats = t.GetComponent<MeshRenderer>().sharedMaterials;
            var owners = BrushCsg.TriangleBrushes(t);
            int tri = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
                for (int k = 0; k < mesh.GetTriangles(sm).Length / 3; k++, tri++)
                    Assert.AreEqual(owners[tri] == b, mats[sm] == red, owners[tri].name + "'s triangle has " + mats[sm].name);
            Object.DestroyImmediate(red);
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static Transform s_Parent;

        static void Make(string name, BrushOperation operation, Vector3 position, Quaternion rotation, Vector3 size, Vector3[] vertices = null, int[][] faces = null)
        {
            var brush = BrushApi.Create(BrushShape.Box, position, size, rotation, s_Parent, name);
            if (vertices != null)
                BrushApi.SetPolyhedron(brush, new BrushPolyhedron { vertices = vertices, faces = System.Array.ConvertAll(faces, f => new BrushPolyhedron.Face { indices = f }) });
            if (operation != BrushOperation.Add) BrushApi.SetOperation(brush, operation);
        }

        /// <summary>Every vertex has its triangle's face normal (unit length) and that face's planar UV.</summary>
        static void AssertFacesNormalsAndUVs(Transform meshObject)
        {
            var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices; var normals = mesh.normals; var uv = mesh.uv; var tris = mesh.triangles;
            int checkedTriangles = 0;
            for (int k = 0; k < tris.Length; k += 3)
            {
                var a = v[tris[k]]; var b = v[tris[k + 1]]; var c = v[tris[k + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.magnitude < 0.02f) continue; // a well-shaped triangle (area over 0.01 m2): its own normal is exact
                n.Normalize(); checkedTriangles++;
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                System.Func<Vector3, Vector2> project = p => ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                for (int j = 0; j < 3; j++)
                {
                    int i = tris[k + j];
                    Assert.That(Vector3.Dot(normals[i], n), Is.GreaterThan(0.999f), "vertex " + i + " has its face's normal: " + normals[i].ToString("F3") + " on a face of " + n.ToString("F3"));
                    Assert.That(Vector2.Distance(uv[i], project(v[i])), Is.LessThan(1e-3f), "vertex " + i + " has its face's planar UV");
                }
            }
            Assert.Greater(checkedTriangles, 20);
        }

        /// <summary>
        /// Every vertex of a combined mesh has its face's normal and the face's planar UVs: thin slivers left by the union
        /// must not lend their own (imprecise) normal or projection to the vertices they share with the rest of the face.
        /// </summary>
        [Test]
        public void CombinedBrushesKeepTheirFacesNormalsAndUVs()
        {
            BrushSettings.instance.snapToGrid = false;
            BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 0f), new Vector3(8f, 3f, 0.4f), Quaternion.identity, null, "Wall");
            var rng = new System.Random(7);
            for (int i = 0; i < 6; i++) // boxes half in the wall, at odd angles and places: unions full of slivers
            {
                var at = new Vector3((float)rng.NextDouble() * 6f - 3f, (float)rng.NextDouble() * 2f + 0.5f, (float)rng.NextDouble() * 0.6f - 0.3f);
                var size = new Vector3(0.5f + (float)rng.NextDouble() * 1.5f, 0.3f + (float)rng.NextDouble(), 0.5f + (float)rng.NextDouble());
                BrushApi.Create(BrushShape.Box, at, size, Quaternion.Euler(0f, (float)rng.NextDouble() * 60f - 30f, 0f), null, "Box " + i);
            }
            BrushApi.ForceUpdate();
            AssertFacesNormalsAndUVs(BrushCsg.MeshObject(Group(), 0, Default, false));
        }

        /// <summary>
        /// A corner of a students' level far from the origin: the cuts leave slivers too thin for Unity's Normalize (it
        /// returns zero under 1e-5), and a sliver must not give its zero normal and wrong projection to the big triangles
        /// of its face.
        /// </summary>
        [Test]
        public void SliversFarFromTheOriginDoNotSpoilTheirFace()
        {
            BrushSettings.instance.snapToGrid = false;
            var group = new GameObject("Group").AddComponent<BrushGroup>();
            group.transform.position = new Vector3(184.47f, 2f, -147.73f); // off the grid: the brushes' coordinates in it are odd
            s_Parent = group.transform;
            Make("A", BrushOperation.Add, V(184.5f, 2f, -147.5f), new Quaternion(0f, 0f, 0f, 1f), V(5.00000238f, 4f, 3.00000143f));
            Make("A (2)", BrushOperation.Add, V(182.5f, 2f, -141f), new Quaternion(0f, 0f, 0f, 1f), V(5f, 4f, 6f));
            Make("A (3)", BrushOperation.Add, V(186.5f, 4f, -141f), new Quaternion(0f, 0f, 0f, 1f), V(5f, 4f, 6f));
            Make("A (4)", BrushOperation.Subtract, V(187f, 3f, -141f), new Quaternion(0f, 0f, 0f, 1f), V(8f, 4f, 11f), new[] { V(-5f, 1f, -6f), V(3f, -1f, -6f), V(0f, -1f, 5f), V(-5f, -1f, 5f), V(-5f, 3f, -6f), V(-3f, 3f, -6f), V(-3f, 3f, 5f), V(-5f, 3f, 5f) }, new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 6, 7, 3, 2 }, new[] { 0, 3, 7 }, new[] { 0, 7, 4 }, new[] { 2, 1, 5 }, new[] { 2, 5, 6 } });
            Make("A (1)", BrushOperation.Add, V(184f, -0.5f, -147.5f), new Quaternion(0f, 0f, 0f, 1f), V(24f, 1f, 19f));
            Make("B", BrushOperation.Add, V(180f, 2f, -148f), new Quaternion(0f, 0f, 0f, 1f), V(6f, 6f, 2f), new[] { V(-2f, -2f, -1f), V(2f, -2f, -1f), V(2f, -2f, 1f), V(-4f, -2f, 1f), V(-2f, 2f, -1f), V(2f, 2f, -1f), V(2f, 4f, 1f), V(-4f, 4f, 1f) }, new[] { new[] { 1, 2, 3, 0 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 6, 7, 3, 2 }, new[] { 3, 7, 4, 0 }, new[] { 5, 6, 2, 1 } });
            Make("C", BrushOperation.Add, V(178f, 2f, -145f), new Quaternion(0f, -8.742278E-08f, 0f, -1f), V(4.000001f, 5f, 4.00000048f), new[] { V(-1.99999964f, -2f, -2.00000024f), V(2.00000024f, -2f, -1.99999964f), V(1.99999964f, -2f, 2.00000024f), V(-2.00000024f, -2f, 1.99999964f), V(-1.99999964f, 3f, -2.00000024f), V(2.00000024f, 3f, -1.99999964f), V(1.99999964f, 1f, 2.00000024f), V(-2.00000024f, 1f, 1.99999964f) }, new[] { new[] { 1, 2, 3, 0 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 6, 7, 3, 2 }, new[] { 3, 7, 4, 0 }, new[] { 5, 6, 2, 1 } });
            Make("D", BrushOperation.Subtract, V(182f, 2f, -146f), new Quaternion(0f, 0.258819133f, 0f, -0.9659258f), V(5.46410227f, 5f, 5.19615173f), new[] { V(-3.73205137f, -3f, -2.46410084f), V(-0.133975029f, -3f, -2.232051f), V(-0.36602515f, -2f, 1.36602557f), V(-3.96410155f, -2f, 1.13397539f), V(-1.50000048f, 1f, -2.59807587f), V(1.23205042f, 2f, -1.86602581f), V(1.50000048f, 2f, 2.59807587f), V(-1.23205042f, 2f, 1.86602581f) }, new[] { new[] { 2, 3, 0 }, new[] { 2, 0, 1 }, new[] { 4, 7, 6 }, new[] { 4, 6, 5 }, new[] { 5, 1, 0 }, new[] { 5, 0, 4 }, new[] { 7, 3, 2 }, new[] { 7, 2, 6 }, new[] { 7, 4, 0 }, new[] { 7, 0, 3 }, new[] { 6, 2, 1 }, new[] { 6, 1, 5 } });
            s_Parent = null;
            BrushApi.ForceUpdate();
            AssertFacesNormalsAndUVs(BrushCsg.MeshObject(group, 0, Default, false));
        }

        [Test]
        public void ACuttersNewMaterialGoesOnTheFacesItCarved()
        {
            var wall = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 0f), new Vector3(6f, 3f, 0.4f), Quaternion.identity, null, "Wall");
            var door = BrushApi.Create(BrushShape.Door, new Vector3(0f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.6f), Quaternion.identity, null, "Door");
            BrushApi.ForceUpdate();
            var red = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "red" };
            BrushApi.SetMaterial(door, red); // only the cutter changes
            BrushApi.ForceUpdate();
            var t = BrushCsg.MeshObject(Group(), 0, Default, false);
            var mesh = t.GetComponent<MeshFilter>().sharedMesh; var mats = t.GetComponent<MeshRenderer>().sharedMaterials;
            var owners = BrushCsg.TriangleBrushes(t);
            int tri = 0, carved = 0;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
                for (int k = 0; k < mesh.GetTriangles(sm).Length / 3; k++, tri++)
                {
                    if (owners[tri] == door) carved++;
                    Assert.AreEqual(owners[tri] == door, mats[sm] == red, owners[tri].name + "'s triangle has " + mats[sm].name);
                }
            Assert.Greater(carved, 0, "the door carved faces into the wall");
            Object.DestroyImmediate(red);
        }
    }
}

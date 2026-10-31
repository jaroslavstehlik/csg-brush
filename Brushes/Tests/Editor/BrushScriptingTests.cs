using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>Building levels from scripts: exact boxes, batches, reports, cut-face materials, regenerating, moving groups.</summary>
    public class BrushScriptingTests
    {
        static Material Colour(string name, Color colour)
        {
            var material = Object.Instantiate(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            material.name = name;
            material.color = colour;
            return material;
        }

        static Material[] MaterialsOf(BrushGroup group)
        {
            foreach (Transform child in group.transform)
                if (BrushGroup.IsMeshChildName(child.name) && child.TryGetComponent<MeshRenderer>(out var renderer))
                    return renderer.sharedMaterials;
            return new Material[0];
        }

        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void ABoxFromTwoCornersStaysExactlyWhereItIsPut()
        {
            bool snapping = BrushSettings.instance.snapToGrid;
            BrushSettings.instance.snapToGrid = true;
            try
            {
                Brush exact = BrushApi.CreateBox(new Vector3(0.13f, 0f, 0.27f), new Vector3(4.53f, 0.75f, 2.9f));
                Brush snapped = BrushApi.Create(BrushShape.Box, new Vector3(10.13f, 0.375f, 0.27f), new Vector3(1f, 0.75f, 1f), Quaternion.identity, snap: true);
                BrushApi.ForceUpdate();
                Assert.AreEqual(new Vector3(2.33f, 0.375f, 1.585f).ToString("F3"), exact.transform.position.ToString("F3"), "not snapped");
                Assert.AreEqual(new Vector3(4.4f, 0.75f, 2.63f).ToString("F3"), exact.size.ToString("F3"));
                Assert.Greater(Mathf.Abs(snapped.transform.position.x - 10.13f), 1e-3f, "snapped when asked");
            }
            finally
            {
                BrushSettings.instance.snapToGrid = snapping;
            }
        }

        [Test]
        public void ABatchIsOneUndoStepAndReportsWhatItBuilt()
        {
            BrushBatch batch;
            using (batch = BrushApi.Batch("Build test level"))
            {
                BrushApi.CreateBox(new Vector3(-5f, -1f, -5f), new Vector3(5f, 0f, 5f), name: "Floor");
                BrushApi.CreateBox(new Vector3(-5f, 0f, 4f), new Vector3(5f, 3f, 5f), name: "Wall");
                BrushApi.CreateBox(new Vector3(-1f, 0f, 4f), new Vector3(1f, 2f, 5f), name: "Doorway", operation: BrushOperation.Subtract);
            }
            Assert.AreEqual(3, batch.Report.brushes);
            Assert.Greater(batch.Report.boxColliders, 2, "floor, and the wall on either side of the doorway");
            Assert.AreEqual(0, batch.Report.meshColliders, "axis aligned pieces are boxes");
            Undo.PerformUndo();
            Assert.AreEqual(0, Object.FindObjectsByType<Brush>(FindObjectsSortMode.None).Length, "one undo removes the whole batch");
        }

        [Test]
        public void TheReportListsConvexMeshColliders()
        {
            BrushApi.CreateBox(new Vector3(0f, 0f, 0f), new Vector3(2f, 1f, 2f));
            BrushApi.Create(BrushShape.Wedge, new Vector3(5f, 0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity, snap: false);
            RebuildReport report = BrushApi.ForceUpdate();
            TestContext.WriteLine(report);
            Assert.AreEqual(1, report.boxColliders);
            Assert.AreEqual(1, report.meshColliders, "the wedge's slope is not axis aligned");
            Assert.AreEqual(1, report.meshColliderPieces.Count);
        }

        [Test]
        public void CutFacesTakeTheCuttersOrTheCutSolidsMaterial([Values(CutFaces.CutterMaterial, CutFaces.CutSolidMaterial)] CutFaces cutFaces)
        {
            Material stone = Colour("Stone", Color.grey), frame = Colour("Frame", Color.red);
            BrushGroup group = BrushApi.CreateGroup("Level");
            using (BrushApi.Batch())
            {
                BrushApi.CreateBox(new Vector3(-5f, 0f, 0f), new Vector3(5f, 3f, 1f), group.transform, "Wall", material: stone);
                Brush doorway = BrushApi.CreateBox(new Vector3(-1f, 0f, -1f), new Vector3(1f, 2f, 2f), group.transform, "Doorway", BrushOperation.Subtract, frame);
                BrushApi.SetCutFaces(doorway, cutFaces);
            }
            Material[] materials = MaterialsOf(group);
            Assert.That(materials, Has.Member(stone));
            if (cutFaces == CutFaces.CutterMaterial)
                Assert.That(materials, Has.Member(frame), "the doorway's sides take the cutter's material");
            else
                Assert.That(materials, Has.No.Member(frame), "the doorway's sides take the wall's material");
        }

        [Test]
        public void RegeneratingKeepsTheBrushesBesideIt()
        {
            BrushGroup group = BrushApi.CreateGroup("Level");
            void Build(Transform into) => BrushApi.CreateBox(new Vector3(-5f, 0f, 0f), new Vector3(5f, 3f, 1f), into, "Wall");
            BrushApi.Regenerate(group.transform, "Generated", Build);
            // A designer cuts a window into the generated wall and adds a pillar beside it.
            using (BrushApi.Batch())
            {
                BrushApi.CreateBox(new Vector3(2f, 1f, -1f), new Vector3(3f, 2f, 2f), group.transform, "Window", BrushOperation.Subtract);
                BrushApi.CreateBox(new Vector3(-8f, 0f, 0f), new Vector3(-7f, 3f, 1f), group.transform, "Pillar");
            }
            GeneratedBrushes generated = BrushApi.Regenerate(group.transform, "Generated", Build);
            Physics.SyncTransforms();
            Assert.AreEqual(0, generated.transform.GetSiblingIndex(), "generated brushes come first");
            Assert.AreEqual(1, generated.GetComponentsInChildren<Brush>().Length, "the old generated wall was replaced, not kept");
            Assert.IsNotNull(group.GetComponentsInChildren<Brush>().FirstOrDefault(b => b.name == "Window"), "the designer's window was kept");
            Assert.IsNotNull(group.GetComponentsInChildren<Brush>().FirstOrDefault(b => b.name == "Pillar"), "the designer's pillar was kept");
            Assert.IsFalse(Physics.CheckBox(new Vector3(2.5f, 1.5f, 0.5f), Vector3.one * 0.2f), "the window still cuts the regenerated wall");
            Assert.IsTrue(Physics.CheckBox(new Vector3(-7.5f, 1.5f, 0.5f), Vector3.one * 0.2f), "the pillar is still solid");
        }

        [Test]
        public void AMovingGroupCarriesItsCollidersWithoutARebuild()
        {
            BrushGroup lift = BrushApi.CreateGroup("Lift", moving: true);
            Brush platform;
            using (BrushApi.Batch())
                platform = BrushApi.CreateBox(new Vector3(-1f, -0.2f, -1f), new Vector3(1f, 0f, 1f), lift.transform, "Platform");
            Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(platform.gameObject), "a moving group's brushes are not static");
            lift.transform.position += new Vector3(0f, 5f, 0f);
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.CheckBox(new Vector3(0f, 4.9f, 0f), Vector3.one * 0.05f), "the collider moved with the group");
            Assert.IsFalse(Physics.CheckBox(new Vector3(0f, -0.1f, 0f), Vector3.one * 0.05f), "and left where it was");
        }
    }
}

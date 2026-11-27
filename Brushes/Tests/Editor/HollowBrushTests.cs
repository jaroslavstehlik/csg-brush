using System.Linq;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A hollow brush's walls, outside and in, are the brush's: its material, its flags, and picking find it.</summary>
    public class HollowBrushTests
    {
        const string Folder = "Assets/__hollow_test";
        Material m_Red;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__hollow_test");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m_Red = new Material(shader);
            AssetDatabase.CreateAsset(m_Red, Folder + "/Red.mat");
        }

        [TearDown]
        public void Close() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); AssetDatabase.DeleteAsset(Folder); }

        [Test]
        public void AHollowBrushRendersWithItsOwnMaterial([Values(false, true)] bool inALargeIsland)
        {
            if (inALargeIsland) // enough touching brushes to be built in cells
                for (int i = 0; i < 14; i++) BrushApi.Create(BrushShape.Box, new Vector3(i * 1.5f - 10f, -0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity);
            var box = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0.75f, 0f), new Vector3(3f, 1.5f, 1.5f), Quaternion.identity, null, "Hollow box");
            BrushApi.SetHollow(box, true, 0.25f);
            BrushApi.SetMaterial(box, m_Red);
            BrushApi.ForceUpdate();
            Assert.IsTrue(box.IsHollow);

            var meshObject = BrushCsg.MeshObject(BrushCsg.ModelOf(box), false);
            var renderer = meshObject.GetComponent<MeshRenderer>(); var mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
            int redSubmesh = System.Array.IndexOf(renderer.sharedMaterials, m_Red);
            Assert.GreaterOrEqual(redSubmesh, 0, "the mesh renders the brush's material");
            var owners = BrushCsg.TriangleBrushes(meshObject);
            int boxTriangles = owners.Count(b => b == box);
            Assert.AreEqual(mesh.GetTriangles(redSubmesh).Length / 3, boxTriangles, "every red triangle is the box's, and every box triangle is red");
            Assert.Greater(boxTriangles, 12, "its inside walls too, not only its outside");
        }
    }
}

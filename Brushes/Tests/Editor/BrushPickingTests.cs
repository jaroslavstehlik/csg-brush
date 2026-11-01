using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Clicking where brushes and ordinary Unity meshes overlap on screen: the one nearer along the ray is picked, measured
    /// on the CPU, so the result doesn't depend on the graphics API or on which was made first.
    /// </summary>
    public class BrushPickingTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static readonly Ray LookingAtTheWall = new Ray(new Vector3(0f, 1f, -10f), Vector3.forward);

        /// <summary>A brush wall 1 m thick whose front face is at z = 0, and a Unity cube at <paramref name="cubeZ"/>, made in either order.</summary>
        static (Brush wall, GameObject cube) WallAndCube(float cubeZ, bool brushFirst)
        {
            Brush wall = null;
            if (brushFirst) wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(0f, 1f, cubeZ);
            if (!brushFirst) wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            BrushApi.ForceUpdate();
            return (wall, cube);
        }

        static float BrushDistance(Ray ray, Brush expected)
        {
            var hit = BrushHooks.PickBrushSurface(ray, out var point, out _);
            Assert.AreEqual(expected, hit, "the ray hits the brush");
            return (point - ray.origin).magnitude;
        }

        [Test]
        public void UnitysOwnMeshRayTestIsThere()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(BrushHooks.IntersectRayMesh(new Ray(new Vector3(0f, 0f, -5f), Vector3.forward), mesh, Matrix4x4.identity, out float t),
                "HandleUtility.IntersectRayMesh is found and hits; without it picking falls back to bounds");
            Assert.AreEqual(4.5f, t, 1e-4f);
        }

        [Test]
        public void AMeshInFrontOfABrushIsPicked([Values(true, false)] bool brushFirst)
        {
            var (wall, cube) = WallAndCube(-3f, brushFirst);
            Assert.IsTrue(BrushHooks.InFrontOfBrush(cube, LookingAtTheWall, BrushDistance(LookingAtTheWall, wall)));
        }

        [Test]
        public void ABrushInFrontOfAMeshIsPicked([Values(true, false)] bool brushFirst)
        {
            var (wall, cube) = WallAndCube(4f, brushFirst);
            Assert.IsFalse(BrushHooks.InFrontOfBrush(cube, LookingAtTheWall, BrushDistance(LookingAtTheWall, wall)));
        }

        [Test]
        public void AMeshBesideTheRayDoesNotCount([Values(true, false)] bool brushFirst)
        {
            var (wall, cube) = WallAndCube(-3f, brushFirst);
            cube.transform.position = new Vector3(5f, 1f, -3f); // in front of the wall, but not under the pointer
            Assert.IsFalse(BrushHooks.InFrontOfBrush(cube, LookingAtTheWall, BrushDistance(LookingAtTheWall, wall)));
        }

        [Test]
        public void AMeshStandingOnABrushIsPickedFromAbove([Values(true, false)] bool brushFirst)
        {
            Brush ground = null;
            if (brushFirst) ground = BrushApi.CreateBox(new Vector3(-5f, -1f, -5f), new Vector3(5f, 0f, 5f), name: "Ground");
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.transform.position = new Vector3(0f, 0.5f, 0f); // its bottom on the ground's top
            if (!brushFirst) ground = BrushApi.CreateBox(new Vector3(-5f, -1f, -5f), new Vector3(5f, 0f, 5f), name: "Ground");
            BrushApi.ForceUpdate();
            var down = new Ray(new Vector3(0.2f, 10f, 0.1f), Vector3.down);
            Assert.IsTrue(BrushHooks.InFrontOfBrush(crate, down, BrushDistance(down, ground)), "the crate, on top");
            var beside = new Ray(new Vector3(3f, 10f, 3f), Vector3.down);
            Assert.IsFalse(BrushHooks.InFrontOfBrush(crate, beside, BrushDistance(beside, ground)), "the ground beside it");
        }

        [Test]
        public void AMeshLyingOnABrushFaceIsPicked()
        {
            // a decal-like quad exactly on the wall's face: it is what is seen there
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = new Vector3(0f, 1f, 0f);
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushHooks.InFrontOfBrush(quad, LookingAtTheWall, BrushDistance(LookingAtTheWall, wall)));
        }
    }
}

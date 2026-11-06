using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Clicking where brushes and ordinary Unity meshes meet. Unity's picking finds what is under the pointer; on a brush
    /// group's mesh the brush that made that triangle is selected, anything else is left to Unity. Unity's picking (a
    /// render pass, not available in batch mode) is stood in for by the nearest mesh along the ray.
    /// </summary>
    public class BrushPickingTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static readonly Ray LookingAtTheWall = new Ray(new Vector3(0f, 1f, -10f), Vector3.forward);

        /// <summary>
        /// What Unity's picking finds: the rendered mesh nearest along the ray. Unity's Scene view picking skips objects
        /// hidden in the Hierarchy, as a group's mesh is while Show generated objects is off: then it finds what lies
        /// behind the level, or nothing. <paramref name="seesHidden"/> stands in for Show generated objects on.
        /// </summary>
        static GameObject UnityPick(Ray ray, bool seesHidden = false)
        {
            GameObject best = null; float bestDistance = float.PositiveInfinity;
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!seesHidden && (renderer.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0) continue;
                float d = BrushHooks.DistanceAlong(renderer.gameObject, ray);
                if (d < bestDistance) { bestDistance = d; best = renderer.gameObject; }
            }
            return best;
        }

        static Brush Click(Ray ray) => BrushHooks.BrushUnder(ray, UnityPick(ray));

        static Material Colour(string name, Color colour)
        {
            var material = Object.Instantiate(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat"));
            material.name = name; material.color = colour;
            return material;
        }

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

        [Test]
        public void UnitysOwnMeshRayTestIsThere()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(BrushHooks.IntersectRayMesh(new Ray(new Vector3(0f, 0f, -5f), Vector3.forward), mesh, Matrix4x4.identity, out var hit),
                "HandleUtility.IntersectRayMesh is found and hits: picking a brush on a group's mesh needs it");
            Assert.AreEqual(4.5f, hit.distance, 1e-4f);
        }

        [Test]
        public void ABrushOnTheGroupsMeshIsSelected([Values(true, false)] bool brushFirst)
        {
            var (wall, _) = WallAndCube(4f, brushFirst); // the cube behind the wall
            Assert.IsTrue(BrushCsg.TriangleBrushes(UnityPick(LookingAtTheWall, seesHidden: true).transform) != null, "Unity finds the group's mesh while it is shown");
            Assert.AreEqual(wall, BrushHooks.BrushUnder(LookingAtTheWall, UnityPick(LookingAtTheWall, seesHidden: true)));
            Assert.AreEqual(wall, Click(LookingAtTheWall), "with the group's mesh hidden from Unity's picking");
        }

        [Test]
        public void AMeshBehindTheLevelDoesNotTakeTheClick([Values(true, false)] bool brushFirst)
        {
            // Unity can't pick the hidden group mesh, so it reports the cube behind the wall
            var (wall, cube) = WallAndCube(4f, brushFirst);
            Assert.AreEqual(wall, BrushHooks.BrushUnder(LookingAtTheWall, cube), "the wall is nearer than what Unity found");
            Assert.IsNull(BrushHooks.BrushUnder(new Ray(new Vector3(0f, 1f, 10f), Vector3.back), cube), "from behind, the cube is nearer: Unity's");
        }

        [Test]
        public void AWindowBehindTheLevelIsNotSelected()
        {
            // a window is drawn over the scene and always clickable, but not through the wall in front of it
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            var window = BrushApi.Create(BrushShape.Window, new Vector3(0f, 1.5f, 5f), new Vector3(1.2f, 1.2f, 0.3f), Quaternion.identity, snap: false);
            BrushApi.ForceUpdate();
            var ray = new Ray(new Vector3(0f, 2f, -10f), Vector3.forward); // through the window whether its position is its centre or its sill
            Assert.AreEqual(window, BrushHooks.BrushUnder(new Ray(new Vector3(0f, 2f, 10f), Vector3.back), null), "seen from behind, the window");
            Assert.AreEqual(wall, BrushHooks.BrushUnder(ray, null), "Unity found nothing: the wall in front of the window");
        }

        [Test]
        public void AMeshInFrontOfTheLevelIsLeftToUnity([Values(true, false)] bool brushFirst)
        {
            var (_, cube) = WallAndCube(-3f, brushFirst);
            Assert.AreEqual(cube, UnityPick(LookingAtTheWall));
            Assert.IsNull(Click(LookingAtTheWall), "Unity selects the cube itself");
        }

        [Test]
        public void EachTriangleSelectsTheBrushThatMadeIt()
        {
            // two brushes with different materials in one mesh: the triangles are in submesh order
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall", material: Colour("Stone", Color.grey));
            var pillar = BrushApi.CreateBox(new Vector3(4f, 0f, -0.5f), new Vector3(5f, 3f, 0.5f), name: "Pillar", material: Colour("Wood", new Color(0.6f, 0.4f, 0.2f)));
            var step = BrushApi.CreateBox(new Vector3(-6f, 0f, -1f), new Vector3(-4f, 0.5f, 1f), name: "Step", material: Colour("Stone 2", Color.white));
            BrushApi.ForceUpdate();
            Assert.AreEqual(wall, Click(LookingAtTheWall));
            Assert.AreEqual(pillar, Click(new Ray(new Vector3(4.5f, 1f, -10f), Vector3.forward)));
            Assert.AreEqual(step, Click(new Ray(new Vector3(-5f, 10f, 0f), Vector3.down)), "from above");
        }

        [Test]
        public void ABrushCarvedAwayCanNotBeClickedThroughWhatIsSeen()
        {
            // a block entirely cut away stands in front of a wall: its shape is nearest along the ray, but the wall is seen
            var block = BrushApi.CreateBox(new Vector3(-1f, 0f, -3f), new Vector3(1f, 2f, -2f), name: "Block");
            BrushApi.CreateBox(new Vector3(-2f, 0f, -4f), new Vector3(2f, 3f, -1f), name: "Cut", operation: BrushOperation.Subtract);
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            BrushApi.ForceUpdate();
            Assert.AreEqual(block, BrushHooks.PickBrushSurface(LookingAtTheWall, out _, out _), "by shapes alone, the hidden block");
            Assert.AreEqual(wall, Click(LookingAtTheWall), "by what is seen, the wall");
        }

        [Test]
        public void ADoorwaysSidesSelectTheDoorway()
        {
            // the faces a cut makes belong to the cutter
            var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
            var doorway = BrushApi.CreateBox(new Vector3(-0.5f, 0f, -1f), new Vector3(0.5f, 2f, 2f), name: "Doorway", operation: BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            var atTheJamb = new Ray(new Vector3(0f, 1f, 0.5f), Vector3.right); // from inside the opening, at its side
            Assert.AreEqual(doorway, Click(atTheJamb));
            Assert.AreEqual(wall, Click(new Ray(new Vector3(2f, 1f, -10f), Vector3.forward)), "the wall beside it");
        }

        [Test]
        public void ACutShownOverTheSceneIsSelectedInFrontOfAMesh()
        {
            bool cuts = BrushSettings.instance.showCuts;
            try
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.transform.position = new Vector3(0f, 1f, 3f);
                var cut = BrushApi.CreateBox(new Vector3(-1f, 0f, -1f), new Vector3(1f, 2f, 1f), name: "Cut", operation: BrushOperation.Subtract);
                BrushApi.ForceUpdate();
                BrushSettings.instance.showCuts = false;
                Assert.IsNull(Click(LookingAtTheWall), "a hidden cut is not there to click: the cube is Unity's");
                BrushSettings.instance.showCuts = true;
                Assert.AreEqual(cut, Click(LookingAtTheWall), "a shown cut in front of the cube");
            }
            finally { BrushSettings.instance.showCuts = cuts; }
        }

        [Test]
        public void ACutBehindWhatUnityFoundIsNotSelected()
        {
            bool cuts = BrushSettings.instance.showCuts;
            try
            {
                BrushSettings.instance.showCuts = true;
                var wall = BrushApi.CreateBox(new Vector3(-3f, 0f, 0f), new Vector3(3f, 3f, 1f), name: "Wall");
                BrushApi.CreateBox(new Vector3(-1f, 0f, 3f), new Vector3(1f, 2f, 4f), name: "Cut", operation: BrushOperation.Subtract);
                var light = new GameObject("Light", typeof(Light));
                light.transform.position = new Vector3(0f, 1f, -5f);
                BrushApi.ForceUpdate();
                Assert.AreEqual(wall, BrushHooks.BrushUnder(LookingAtTheWall, wall.gameObject), "the brush itself, in front of the cut");
                Assert.IsNull(BrushHooks.BrushUnder(LookingAtTheWall, light), "a light's icon has no surface to measure: it is in front");
            }
            finally { BrushSettings.instance.showCuts = cuts; }
        }

        [Test]
        public void AFloorPlansFloorOnTheGroundSelectsThePlan([Values(true, false)] bool groundFirst)
        {
            Brush ground = null;
            if (groundFirst) ground = BrushApi.CreateBox(new Vector3(-10f, -2f, -10f), new Vector3(10f, 0f, 10f), name: "Ground");
            var go = new GameObject("Floor Plan");
            var plan = go.AddComponent<FloorPlan>();
            plan.points = new System.Collections.Generic.List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 3f), new Vector3(0f, 0f, 3f) };
            plan.closed = true;
            BrushGenerators.Update(plan);
            if (!groundFirst) ground = BrushApi.CreateBox(new Vector3(-10f, -2f, -10f), new Vector3(10f, 0f, 10f), name: "Ground");
            BrushApi.ForceUpdate();
            var hit = Click(new Ray(new Vector3(2f, 10f, 1.5f), Vector3.down));
            Assert.IsNotNull(hit);
            Assert.AreEqual(plan, hit.generatedBy, "the plan's floor, in one surface with the ground's top");
            Assert.AreEqual(ground, Click(new Ray(new Vector3(8f, 10f, 8f), Vector3.down)), "the ground beside it");
        }

        [Test]
        public void WhereUnityFindsNothingABrushIsPickedByItsShape()
        {
            // a scene just loaded, or a platform where Unity can't pick the level's mesh: the brush is still selected
            var (wall, _) = WallAndCube(4f, true);
            Assert.AreEqual(wall, BrushHooks.BrushUnder(LookingAtTheWall, null));
        }
    }
}

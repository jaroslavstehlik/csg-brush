using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>Shape handles: each drag changes the shape it should and keeps the rest where it was.</summary>
    public class BrushShapeHandleTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        /// <summary>A brush placed exactly (no snapping), so drags are exact.</summary>
        static Brush Make(BrushShape shape, Vector3 size, Vector3 position, float yaw = 0f)
        {
            Brush b = BrushApi.Create(shape, position, size, Quaternion.Euler(0f, yaw, 0f), snap: false);
            return b;
        }

        static void Drag(Brush b, string handle, float distance, bool symmetric = false) =>
            BrushShapeHandles.Apply(b, BrushShapeHandles.State.Of(b), handle, distance, symmetric);

        /// <summary>The shape's box corners in world space.</summary>
        static (Vector3 min, Vector3 max) WorldBox(Brush b)
        {
            Vector3 shift = b.PivotShift, half = b.ClampedSize * 0.5f;
            Vector3 a = b.transform.TransformPoint(shift - half), c = b.transform.TransformPoint(shift + half);
            return (Vector3.Min(a, c), Vector3.Max(a, c));
        }

        static void Near(Vector3 expected, Vector3 actual, string what) =>
            Assert.Less((expected - actual).magnitude, 1e-3f, $"{what}: expected {expected:F3}, got {actual:F3}");

        [Test]
        public void DraggingABoxsSideExtendsItAndTheOtherSideStays([Values(0f, 0.5f)] float pivotY)
        {
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), new Vector3(1f, 1f, 1f));
            box.pivot = new Vector3(0.5f, pivotY, 0.5f);
            var before = WorldBox(box);
            Drag(box, "+x", 1.5f);
            var after = WorldBox(box);
            Assert.AreEqual(3.5f, box.size.x, 1e-4f);
            Near(before.min, after.min, "the -x side and the rest stay");
            Assert.AreEqual(before.max.x + 1.5f, after.max.x, 1e-4f, "the +x side moved out");
            Drag(box, "-y", 0.5f);
            Assert.AreEqual(before.min.y - 0.5f, WorldBox(box).min.y, 1e-4f, "the bottom moved down");
            Assert.AreEqual(before.max.y, WorldBox(box).max.y, 1e-4f, "the top stayed");
        }

        [Test]
        public void ShiftDragsBothSides()
        {
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), Vector3.zero);
            Vector3 centre = (WorldBox(box).min + WorldBox(box).max) * 0.5f;
            Drag(box, "+z", 1f, symmetric: true);
            Assert.AreEqual(4f, box.size.z, 1e-4f);
            Near(centre, (WorldBox(box).min + WorldBox(box).max) * 0.5f, "the centre stays");
        }

        [Test]
        public void ATurnedBoxExtendsAlongItsOwnSide()
        {
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 4f), Vector3.zero, yaw: 90f);
            Vector3 backFace = box.transform.TransformPoint(new Vector3(0f, 0f, -2f));
            Drag(box, "+z", 1f);
            Near(backFace, box.transform.TransformPoint(box.PivotShift + new Vector3(0f, 0f, -box.size.z * 0.5f)), "the back face stays");
            Assert.AreEqual(5f, box.size.z, 1e-4f);
        }

        [Test]
        public void SizesSnapToTheGridUnlessTheBrushIsPlacedExactly()
        {
            bool snapping = BrushSettings.instance.snapToGrid;
            BrushSettings.instance.snapToGrid = true;
            try
            {
                Brush snapped = BrushApi.Create(BrushShape.Box, new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f), Quaternion.identity);
                Brush exact = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), new Vector3(10f, 1f, 1f));
                float grid = BrushSettings.instance.GridMeters;
                Drag(snapped, "+x", grid * 0.7f);
                Drag(exact, "+x", grid * 0.7f);
                Assert.AreEqual(2f + grid, snapped.size.x, 1e-4f, "onto the next grid step");
                Assert.AreEqual(2f + grid * 0.7f, exact.size.x, 1e-4f, "exactly as dragged");
            }
            finally
            {
                BrushSettings.instance.snapToGrid = snapping;
            }
        }

        [Test]
        public void ACylindersRimChangesItsRadiusAndKeepsItRound()
        {
            Brush round = Make(BrushShape.Cylinder, new Vector3(2f, 3f, 2f), Vector3.zero);
            Vector3 axis = (WorldBox(round).min + WorldBox(round).max) * 0.5f;
            Drag(round, "+x", 0.5f);
            Assert.AreEqual(3f, round.size.x, 1e-4f, "diameter grew by twice the drag");
            Assert.AreEqual(3f, round.size.z, 1e-4f, "still round");
            Near(axis, (WorldBox(round).min + WorldBox(round).max) * 0.5f, "the axis stays");
            Brush oval = Make(BrushShape.Cylinder, new Vector3(2f, 3f, 4f), new Vector3(10f, 0f, 0f));
            Drag(oval, "+x", 0.5f);
            Assert.AreEqual(3f, oval.size.x, 1e-4f);
            Assert.AreEqual(4f, oval.size.z, 1e-4f, "an oval keeps its other diameter");
        }

        [Test]
        public void AConesApexRisesAndItsBaseStays()
        {
            Brush cone = Make(BrushShape.Cone, new Vector3(2f, 2f, 2f), Vector3.zero);
            float baseY = WorldBox(cone).min.y;
            Drag(cone, "+y", 1f);
            Assert.AreEqual(3f, cone.size.y, 1e-4f);
            Assert.AreEqual(baseY, WorldBox(cone).min.y, 1e-4f);
        }

        [Test]
        public void AWedgesLowEndExtendsAndItsHighEndStays()
        {
            Brush wedge = Make(BrushShape.Wedge, new Vector3(2f, 1f, 3f), Vector3.zero);
            float high = WorldBox(wedge).max.z;
            Drag(wedge, "-z", 2f);
            Assert.AreEqual(5f, wedge.size.z, 1e-4f, "a longer, gentler ramp");
            Assert.AreEqual(high, WorldBox(wedge).max.z, 1e-4f);
        }

        [Test]
        public void ASpheresPoleChangesItsDiameterAboutItsCentre()
        {
            Brush sphere = Make(BrushShape.Sphere, new Vector3(2f, 2f, 2f), Vector3.zero);
            Drag(sphere, "+y", 0.5f);
            Assert.AreEqual(new Vector3(3f, 3f, 3f).ToString("F3"), sphere.size.ToString("F3"), "a round sphere stays round");
        }

        [Test]
        public void TheFirstStepSetsTheStepHeight()
        {
            Brush stairs = Make(BrushShape.Stairs, new Vector3(2f, 2f, 4f), Vector3.zero);
            stairs.stepHeight = 0.25f;
            Assert.AreEqual(8, BrushPolyhedron.StepCount(stairs.size.y, stairs.stepHeight));
            Drag(stairs, "step", 0.25f);
            Assert.AreEqual(0.5f, stairs.stepHeight, 1e-4f);
            Assert.AreEqual(4, BrushPolyhedron.StepCount(stairs.size.y, stairs.stepHeight), "fewer, taller steps");
        }

        [Test]
        public void AnArchsCrownSetsItsThickness()
        {
            Brush arch = Make(BrushShape.Arch, new Vector3(4f, 3f, 1f), Vector3.zero);
            arch.wallThickness = 0.5f;
            Drag(arch, "thickness", 0.25f);
            Assert.AreEqual(0.75f, arch.wallThickness, 1e-4f);
        }

        [Test]
        public void CurvedStairsTurnFurtherAndSpiralStairsClimbHigher()
        {
            Brush curved = Make(BrushShape.CurvedStairs, Vector3.one, Vector3.zero);
            curved.innerRadius = 1f; curved.stepWidth = 1f; curved.curveAngle = 90f;
            // a quarter of the outer circle (radius 2 m) more along the end of the stairs
            Drag(curved, "angle", Mathf.PI * 2f * 2f / 4f);
            Assert.AreEqual(180f, curved.curveAngle, 0.5f);
            Drag(curved, "outer", 0.5f);
            Assert.AreEqual(1.5f, curved.stepWidth, 1e-4f);
            Brush spiral = Make(BrushShape.SpiralStairs, Vector3.one, new Vector3(10f, 0f, 0f));
            spiral.stepHeight = 0.25f; spiral.numSteps = 8;
            Drag(spiral, "height", 1f);
            Assert.AreEqual(12, spiral.numSteps, "a metre more is four more steps");
        }

        [Test]
        public void SidesAreFacesAndPointsArePoints()
        {
            bool Face(Brush b, string id) => BrushShapeHandles.HandlesOf(b).Find(h => h.id == id).IsFace;
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), Vector3.zero);
            foreach (var h in BrushShapeHandles.HandlesOf(box)) Assert.IsTrue(h.IsFace, "a box's handles are its sides: " + h.id);
            Brush cone = Make(BrushShape.Cone, new Vector3(2f, 2f, 2f), new Vector3(5f, 0f, 0f));
            Assert.IsFalse(Face(cone, "+y"), "a cone's apex is a point");
            Assert.IsTrue(Face(cone, "-y"), "its base is a side");
            Assert.IsFalse(Face(cone, "+x"), "its radius is a point on the rim");
            Brush wedge = Make(BrushShape.Wedge, new Vector3(2f, 2f, 2f), new Vector3(10f, 0f, 0f));
            Assert.IsFalse(Face(wedge, "-z"), "a wedge's low end is an edge");
            Assert.IsTrue(Face(wedge, "+z"), "its high end is a side");
            Brush stairs = Make(BrushShape.Stairs, new Vector3(2f, 2f, 4f), new Vector3(15f, 0f, 0f));
            Assert.IsFalse(Face(stairs, "step"), "the step height is a point");
            Assert.IsTrue(Face(stairs, "+x"), "a stair's side is its stepped side");
        }

        [Test]
        public void OnlySidesTurnedTowardsTheCameraAreShown()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(5f, 5f, 5f);
            camera.transform.LookAt(Vector3.zero);
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), Vector3.zero);
            var shown = new System.Collections.Generic.List<string>();
            foreach (var h in BrushShapeHandles.HandlesOf(box))
                if (BrushShapeHandles.FacesCamera(box.transform.TransformDirection(h.direction), box.transform.TransformPoint(h.local), camera)) shown.Add(h.id);
            shown.Sort();
            Assert.AreEqual("+x,+y,+z", string.Join(",", shown), "seen from above, right and front");
            camera.transform.position = new Vector3(10f, 0.1f, 0f);
            camera.transform.LookAt(Vector3.zero);
            Assert.IsFalse(BrushShapeHandles.FacesCamera(Vector3.up, new Vector3(0f, 1f, 0f), camera), "the top seen edge-on is hidden");
        }

        [Test]
        public void EveryShapeHasHandlesExceptCustom()
        {
            foreach (BrushShape shape in System.Enum.GetValues(typeof(BrushShape)))
            {
                Brush b = Make(shape, new Vector3(2f, 2f, 2f), Vector3.zero);
                int count = BrushShapeHandles.HandlesOf(b).Count;
                if (shape == BrushShape.Custom) Assert.AreEqual(0, count, "Custom shapes are edited in Edit Brush mode");
                else Assert.Greater(count, 2, shape.ToString());
            }
        }
    }
}

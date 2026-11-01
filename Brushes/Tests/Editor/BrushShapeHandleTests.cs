using System.Collections.Generic;
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

        /// <summary>Two cylinders of different heights and a box, all selected; the first cylinder's top is dragged up 1 m.</summary>
        static (Brush dragged, Brush other, Brush box) DragTopOfSelection(ShapeEditSelection mode)
        {
            Brush dragged = Make(BrushShape.Cylinder, new Vector3(2f, 2f, 2f), Vector3.zero);
            Brush other = Make(BrushShape.Cylinder, new Vector3(2f, 4f, 2f), new Vector3(5f, 0f, 0f));
            Brush box = Make(BrushShape.Box, new Vector3(2f, 2f, 2f), new Vector3(-5f, 0f, 0f));
            var brushes = new List<Brush> { dragged, other, box };
            var starts = new Dictionary<Brush, BrushShapeHandles.State>();
            foreach (var b in brushes) starts[b] = BrushShapeHandles.State.Of(b);
            BrushShapeHandles.ApplyToSelection(brushes, dragged, "+y", 1f, false, mode, starts);
            return (dragged, other, box);
        }

        [Test]
        public void OnlyTheDraggedBrushChanges()
        {
            var (dragged, other, box) = DragTopOfSelection(ShapeEditSelection.Single);
            Assert.AreEqual(3f, dragged.size.y, 1e-4f);
            Assert.AreEqual(4f, other.size.y, 1e-4f, "the other cylinder stays");
            Assert.AreEqual(2f, box.size.y, 1e-4f, "the box stays");
        }

        [Test]
        public void SameShapesChangeByTheSameAmount()
        {
            var (dragged, other, box) = DragTopOfSelection(ShapeEditSelection.Offset);
            Assert.AreEqual(3f, dragged.size.y, 1e-4f);
            Assert.AreEqual(5f, other.size.y, 1e-4f, "1 m taller too");
            Assert.AreEqual(2f, box.size.y, 1e-4f, "another shape stays");
        }

        [Test]
        public void SameShapesChangeToTheSameValue()
        {
            var (dragged, other, box) = DragTopOfSelection(ShapeEditSelection.Match);
            Assert.AreEqual(3f, dragged.size.y, 1e-4f);
            Assert.AreEqual(3f, other.size.y, 1e-4f, "as tall as the dragged one");
            Assert.AreEqual(2f, box.size.y, 1e-4f, "another shape stays");
        }

        [Test]
        public void TheSameValueWorksForRadiiAndSlopes()
        {
            Brush a = Make(BrushShape.Cylinder, new Vector3(2f, 2f, 2f), Vector3.zero);
            Brush b = Make(BrushShape.Cylinder, new Vector3(6f, 2f, 6f), new Vector3(10f, 0f, 0f));
            Brush c = Make(BrushShape.Wedge, new Vector3(2f, 1f, 2f), new Vector3(0f, 0f, 10f));
            Brush d = Make(BrushShape.Wedge, new Vector3(2f, 2f, 4f), new Vector3(10f, 0f, 10f));
            foreach (var (dragged, other, handle) in new[] { (a, b, "side"), (c, d, "slope") })
            {
                var brushes = new List<Brush> { dragged, other };
                var starts = new Dictionary<Brush, BrushShapeHandles.State>();
                foreach (var x in brushes) starts[x] = BrushShapeHandles.State.Of(x);
                BrushShapeHandles.ApplyToSelection(brushes, dragged, handle, 0.5f, false, ShapeEditSelection.Match, starts);
                Assert.AreNotEqual(starts[dragged].size.x + starts[dragged].size.z, dragged.size.x + dragged.size.z, handle + " changed the dragged brush");
                Assert.AreEqual(dragged.size.x, other.size.x, 1e-3f, handle + ": the same width");
                if (handle == "side") Assert.AreEqual(dragged.size.z, other.size.z, 1e-3f, "the same diameter");
                else Assert.AreEqual(dragged.size.z, other.size.z, 1e-3f, "the same ramp length");
            }
        }

        [Test]
        public void ShapeEditHasAnIconForEachChoice()
        {
            Assert.AreEqual(3, System.Enum.GetValues(typeof(ShapeEditSelection)).Length, "Single, Offset, Match");
            foreach (var name in new[] { "ShapeEdit", "ShapeEdit_Single", "ShapeEdit_Offset", "ShapeEdit_Match" })
                foreach (var file in new[] { name, "d_" + name })
                    Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/" + file + ".png"), file);
        }

        [Test]
        public void ShapeEditGoesWithTheTransformToolsOnly()
        {
            foreach (var tool in new[] { UnityEditor.Tool.Move, UnityEditor.Tool.Rotate, UnityEditor.Tool.Scale, UnityEditor.Tool.Transform })
                Assert.IsTrue(BrushShapeHandles.WorksWith(tool), tool.ToString());
            foreach (var tool in new[] { UnityEditor.Tool.View, UnityEditor.Tool.Rect, UnityEditor.Tool.Custom, UnityEditor.Tool.None })
                Assert.IsFalse(BrushShapeHandles.WorksWith(tool), tool + ": a create tool is a custom tool");
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
        public void ACylindersRimChangesItsRadiusAndKeepsItsShape()
        {
            Brush round = Make(BrushShape.Cylinder, new Vector3(2f, 3f, 2f), Vector3.zero);
            Vector3 axis = (WorldBox(round).min + WorldBox(round).max) * 0.5f;
            Drag(round, "side", 0.5f);
            Assert.AreEqual(3f, round.size.x, 1e-4f, "diameter grew by twice the drag");
            Assert.AreEqual(3f, round.size.z, 1e-4f, "still round");
            Near(axis, (WorldBox(round).min + WorldBox(round).max) * 0.5f, "the axis stays");
            Assert.IsTrue(BrushShapeHandles.HandlesOf(round).Exists(h => h.id == "side" && h.IsFace && h.radial), "the round side is the radius");
            Brush oval = Make(BrushShape.Cylinder, new Vector3(2f, 3f, 4f), new Vector3(10f, 0f, 0f));
            Drag(oval, "side", 0.5f);
            Assert.AreEqual(4f / 3f * 2f, oval.size.x, 1e-3f, "an oval grows evenly");
            Assert.AreEqual(4f / 3f * 4f, oval.size.z, 1e-3f, "keeping its proportions");
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
        public void PushingAWedgesDiagonalFaceGrowsItAtTheSameSteepness()
        {
            Brush wedge = Make(BrushShape.Wedge, new Vector3(2f, 1.5f, 3f), Vector3.zero);
            var before = WorldBox(wedge);
            Assert.IsTrue(BrushShapeHandles.HandlesOf(wedge).Find(h => h.id == "slope").IsFace, "the diagonal face is a side");
            // pushing the face out by d moves its foot along the floor by d times the face's length over the height
            float slope = Mathf.Sqrt(3f * 3f + 1.5f * 1.5f);
            Drag(wedge, "slope", 1.5f * 1.5f / slope);
            Assert.AreEqual(4.5f, wedge.size.z, 1e-3f, "longer");
            Assert.AreEqual(2.25f, wedge.size.y, 1e-3f, "and taller in proportion: still 1 in 2");
            var after = WorldBox(wedge);
            Assert.AreEqual(before.max.z, after.max.z, 1e-4f, "the high end stays");
            Assert.AreEqual(before.min.y, after.min.y, 1e-4f, "the bottom stays");
            Drag(wedge, "slope", -1f);
            Assert.AreEqual(0.5f, wedge.size.y / wedge.size.z, 1e-4f, "pulled in: smaller, the same steepness");
        }

        [Test]
        public void ASpheresSurfaceChangesItsDiameterAboutItsCentre()
        {
            Brush sphere = Make(BrushShape.Sphere, new Vector3(2f, 2f, 2f), Vector3.zero);
            Vector3 centre = (WorldBox(sphere).min + WorldBox(sphere).max) * 0.5f;
            Drag(sphere, "surface", 0.5f);
            Assert.AreEqual(new Vector3(3f, 3f, 3f).ToString("F3"), sphere.size.ToString("F3"), "a round sphere stays round");
            Near(centre, (WorldBox(sphere).min + WorldBox(sphere).max) * 0.5f, "the centre stays");
            Brush egg = Make(BrushShape.Sphere, new Vector3(2f, 4f, 2f), new Vector3(10f, 0f, 0f));
            Drag(egg, "surface", 0.5f);
            Assert.AreEqual(2f, egg.size.y / egg.size.x, 1e-3f, "a stretched one keeps its proportions");
        }

        [Test]
        public void TheFirstRiserMakesTheStairsLongerAndTheStepHeightStaysInTheInspector()
        {
            Brush stairs = Make(BrushShape.Stairs, new Vector3(2f, 2f, 4f), Vector3.zero);
            float back = WorldBox(stairs).max.z;
            Drag(stairs, "-z", 2f);
            Assert.AreEqual(6f, stairs.size.z, 1e-4f, "longer");
            Assert.AreEqual(back, WorldBox(stairs).max.z, 1e-4f, "the top stays");
            Assert.IsFalse(BrushShapeHandles.HandlesOf(stairs).Exists(h => h.id == "step"), "no step height handle next to the first riser");
        }

        [Test]
        public void StepsAreNeverLowerThanTenCentimetres()
        {
            // Steps of a few millimetres made thousands of blocks and froze or crashed the editor.
            Brush stairs = Make(BrushShape.Stairs, new Vector3(2f, 3f, 6f), Vector3.zero);
            stairs.stepHeight = 0.25f;
            stairs.stepHeight = 0.001f; // as a script or an old scene might leave it
            Assert.AreEqual(30, BrushPolyhedron.StepCount(stairs.size.y, stairs.stepHeight), "3 m of stairs is at most 30 steps");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BrushApi.ForceUpdate();
            Assert.Less(sw.ElapsedMilliseconds, 5000, "builds in moments");
            BrushApi.SetStairs(stairs, 0.02f);
            Assert.AreEqual(BrushPolyhedron.MinStepHeight, stairs.stepHeight, 1e-4f, "scripts are held to it too");
        }

        [Test]
        public void AnArchsOuterFaceSetsItsHeightAndItsInnerFaceItsThickness()
        {
            Brush arch = Make(BrushShape.Arch, new Vector3(4f, 3f, 1f), Vector3.zero);
            arch.wallThickness = 0.5f;
            float floor = WorldBox(arch).min.y;
            Drag(arch, "+y", 1f);
            Assert.AreEqual(4f, arch.size.y, 1e-4f, "taller");
            Assert.AreEqual(floor, WorldBox(arch).min.y, 1e-4f, "the feet stay on the floor");
            Drag(arch, "thickness", 0.25f);
            Assert.AreEqual(0.75f, arch.wallThickness, 1e-4f);
            foreach (var h in BrushShapeHandles.HandlesOf(arch))
                if (h.id == "+y" || h.id == "thickness")
                    for (int i = 0; i < h.faces.Length; i++)
                    {
                        Vector3 middle = Vector3.zero;
                        foreach (var p in h.faces[i]) middle += p / h.faces[i].Length;
                        float away = Vector3.Dot(h.faceNormals[i], middle - new Vector3(arch.PivotShift.x, arch.PivotShift.y - arch.ClampedSize.y * 0.5f, middle.z));
                        Assert.That(h.id == "+y" ? away > 0f : away < 0f, h.id + " face " + i + " looks the right way");
                    }
        }

        [Test]
        public void CurvedStairsTurnFurtherAndSpiralStairsClimbHigher()
        {
            Brush curved = Make(BrushShape.CurvedStairs, Vector3.one, Vector3.zero);
            curved.innerRadius = 1f; curved.stepWidth = 1f; curved.curveAngle = 90f;
            foreach (var h in BrushShapeHandles.HandlesOf(curved)) Assert.IsTrue(h.IsFace, "curved stairs are all faces: " + h.id);
            // a quarter of the circle through the middle of the steps (radius 1.5 m) more along the end of the stairs
            Drag(curved, "angle", Mathf.PI * 2f * 1.5f / 4f);
            Assert.AreEqual(180f, curved.curveAngle, 0.5f);
            Drag(curved, "outer", 0.5f);
            Assert.AreEqual(1.5f, curved.stepWidth, 1e-4f, "the outer face moved out");
            Assert.AreEqual(1f, curved.innerRadius, 1e-4f, "the inner edge stayed");
            Drag(curved, "inner", 0.5f);
            Assert.AreEqual(1.5f, curved.innerRadius, 1e-4f, "the inner face moved away from the column");
            Assert.AreEqual(1f, curved.stepWidth, 1e-4f, "so the steps got narrower");
            Assert.AreEqual(2.5f, curved.innerRadius + curved.stepWidth, 1e-4f, "and the outer edge stayed");
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
            bool Edge(Brush b, string id) => BrushShapeHandles.HandlesOf(b).Find(h => h.id == id).IsEdge;
            Assert.IsTrue(Face(cone, "side"), "its radius is its sloped side");
            Assert.IsFalse(Edge(cone, "+y"), "and the apex is not an edge");
            Brush wedge = Make(BrushShape.Wedge, new Vector3(2f, 2f, 2f), new Vector3(10f, 0f, 0f));
            Assert.IsTrue(Edge(wedge, "-z"), "a wedge's low end is an edge");
            Assert.IsTrue(Edge(wedge, "+y"), "so is its high top edge");
            Assert.IsTrue(Face(wedge, "+z"), "its high end is a side");
            Brush stairs = Make(BrushShape.Stairs, new Vector3(2f, 2f, 4f), new Vector3(15f, 0f, 0f));
            Assert.IsTrue(Face(stairs, "+x"), "a stair's side is its stepped side");
            Brush arch = Make(BrushShape.Arch, new Vector3(4f, 3f, 1f), new Vector3(20f, 0f, 0f));
            Assert.IsTrue(Face(arch, "+y") && Face(arch, "thickness"), "an arch's curved faces between front and back are sides");
            Assert.IsTrue(Edge(arch, "+x"), "its feet are edges");
            Brush sphere = Make(BrushShape.Sphere, new Vector3(2f, 2f, 2f), new Vector3(25f, 0f, 0f));
            Assert.IsTrue(Face(sphere, "surface"), "a sphere's radius is its surface");
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
            camera.transform.position = new Vector3(10f, 1.3f, 0f);
            camera.transform.LookAt(Vector3.zero);
            Assert.IsTrue(BrushShapeHandles.FacesCamera(Vector3.up, new Vector3(0f, 1f, 0f), camera), "the top seen at a steep angle still works");
            camera.transform.position = new Vector3(10f, 0.9f, 0f);
            Assert.IsFalse(BrushShapeHandles.FacesCamera(Vector3.up, new Vector3(0f, 1f, 0f), camera), "seen from below, it is hidden");
        }

        [Test]
        public void EveryShapeHasHandlesExceptCustom()
        {
            foreach (BrushShape shape in System.Enum.GetValues(typeof(BrushShape)))
            {
                Brush b = Make(shape, new Vector3(2f, 2f, 2f), Vector3.zero);
                int count = BrushShapeHandles.HandlesOf(b).Count;
                if (shape == BrushShape.Custom) Assert.AreEqual(0, count, "Custom shapes are edited in Edit Brush mode");
                else Assert.Greater(count, 0, shape.ToString());
            }
        }
    }
}

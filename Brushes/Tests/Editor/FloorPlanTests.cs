using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A floor plan makes walls from its outline, keeps them in step with it, and keeps them out of the way.</summary>
    public class FloorPlanTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static FloorPlan Room(params Vector3[] corners)
        {
            var go = new GameObject("Floor Plan");
            var plan = go.AddComponent<FloorPlan>();
            plan.points = new List<Vector3>(corners);
            plan.closed = true;
            BrushGenerators.Update(plan);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            return plan;
        }

        static readonly Vector3[] FourByThree = { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 3f), new Vector3(0f, 0f, 3f) };

        static bool Solid(Vector3 p) => Physics.OverlapBox(p, Vector3.one * 0.02f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0;

        [Test]
        public void AClosedOutlineBecomesWallsAroundTheDrawnRoom()
        {
            var plan = Room(FourByThree);
            Assert.AreEqual(4, plan.generated.FindAll(b => b.name.StartsWith("Wall")).Count, "one wall per side");
            Assert.AreEqual(5, plan.generated.Count, "and the floor");
            foreach (var wall in plan.generated)
            {
                Assert.IsTrue(wall.IsGenerated); Assert.AreEqual(plan, wall.generatedBy);
                Assert.IsTrue((wall.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0, "hidden: the plan is what you edit");
                Assert.IsTrue(wall.polyhedron.IsSound(out var why), why);
            }
            Assert.IsFalse(Solid(new Vector3(2f, 1.5f, 1.5f)), "the room's inside is clear");
            Assert.IsFalse(Solid(new Vector3(0.05f, 1.5f, 0.05f)), "right up to the drawn line: it is the inner face");
            Assert.IsTrue(Solid(new Vector3(2f, 1.5f, -0.1f)), "the wall stands outside the line");
            Assert.IsTrue(Solid(new Vector3(-0.1f, 1.5f, -0.1f)), "the corner is closed");
            Assert.IsTrue(Solid(new Vector3(4.1f, 2.9f, 3.1f)), "up to the wall height");
            Assert.IsFalse(Solid(new Vector3(2f, 1.5f, -0.25f)), "0.2 m thick");
        }

        [Test]
        public void MovingACornerMovesItsWallsWithTheSameBrushes()
        {
            var plan = Room(FourByThree);
            var before = new List<Brush>(plan.generated);
            var hashes = new List<int>(); foreach (var b in before) hashes.Add(b.polyhedron.ContentHash());
            Undo.RecordObject(plan, "Move corner");
            plan.points[2] = new Vector3(5f, 0f, 3f);
            BrushGenerators.Update(plan);
            CollectionAssert.AreEqual(before, plan.generated, "the same brush objects, reshaped");
            int changed = 0; for (int i = 0; i < 4; i++) if (plan.generated[i].polyhedron.ContentHash() != hashes[i]) changed++;
            Assert.AreEqual(3, changed, "the corner's two walls, and the one mitred against their new angle");
            Undo.PerformUndo();
            BrushGenerators.Flush();
            for (int i = 0; i < 4; i++) Assert.AreEqual(hashes[i], plan.generated[i].polyhedron.ContentHash(), "undo brings the walls back");
        }

        [Test]
        public void GeneratedBrushesAreNotSnappedAndSelectTheirPlan()
        {
            var plan = Room(FourByThree);
            var wall = plan.generated[0];
            Assert.IsFalse(BrushSnap.Snap(wall), "a 0.2 m wall is not pulled onto the grid");
            Assert.IsFalse(BrushSnap.IsOffGrid(wall));
            Assert.AreEqual(plan.gameObject, BrushGenerators.SelectionTarget(wall), "a click on a wall selects the plan");
            var hit = BrushHooks.PickBrushSurface(new Ray(new Vector3(2f, 1.5f, -5f), Vector3.forward), out _, out _);
            Assert.IsNotNull(hit); Assert.AreEqual(plan, hit.generatedBy, "picking finds the wall");
        }

        [Test]
        public void ThicknessHeightAndSideReshapeTheWalls()
        {
            var plan = Room(FourByThree);
            plan.side = FloorPlan.Side.Inside; plan.wallThickness = 0.5f;
            BrushGenerators.Update(plan); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.IsTrue(Solid(new Vector3(2f, 1.5f, 0.4f)), "inside: the wall stands in the room");
            Assert.IsFalse(Solid(new Vector3(2f, 1.5f, -0.05f)), "and nothing outside the line");
            plan.RemovePointAt(3); // the point and its two walls go: an open line of two walls
            BrushGenerators.Update(plan);
            Assert.AreEqual(2, plan.generated.Count, "an open outline: one wall per segment");
            Assert.AreEqual(2, BrushGenerators.Container(plan, false).childCount, "the extra wall is gone");
        }

        [Test]
        public void DrawnPointsFollowFortyFiveDegreeStepsOnTheGrid()
        {
            var s = BrushSettings.instance; int grid = s.gridIndex; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = true;
                for (int i = 0; i < s.gridSizes.Length; i++) if (Mathf.Abs(s.ToMeters(s.gridSizes[i]) - 1f) < 1e-4f) s.SetGridIndex(i);
                Assume.That(s.GridMeters, Is.EqualTo(1f).Within(1e-4f));
                var from = Vector3.zero;
                Assert.AreEqual(new Vector3(3f, 0f, 0f), FloorPlanTools.Constrain(from, new Vector3(3f, 0f, 1f), false), "nearly along x: along x");
                Assert.AreEqual(new Vector3(2f, 0f, 2f), FloorPlanTools.Constrain(from, new Vector3(2f, 0f, 3f), false), "nearly diagonal: a diagonal on grid points");
                Assert.AreEqual(new Vector3(0f, 0f, -2f), FloorPlanTools.Constrain(from, new Vector3(0f, 0f, -2f), false));
                Assert.AreEqual(new Vector3(2f, 0f, 3f), FloorPlanTools.Constrain(from, new Vector3(2f, 0f, 3f), true), "Shift: the grid point itself");
            }
            finally { s.snapToGrid = snap; s.SetGridIndex(grid); }
        }

        // edit mode: the selection rules work on screen points; here a point's screen position is its (x, z) times 100
        static Vector2[] Screen(FloorPlan plan) => plan.points.ConvertAll(p => new Vector2(p.x * 100f, p.z * 100f)).ToArray();

        [Test]
        public void EditModeSelectsPointsAndWallsByClickAndRectangle()
        {
            var plan = Room(FourByThree);
            FloorPlanEditState.ClearSelection();
            var sel = FloorPlanEditState.Sel(plan); var screen = Screen(plan); var walls = FloorPlanEditState.WallEnds(plan);
            var aroundFirstWall = Rect.MinMaxRect(-50f, -50f, 450f, 50f); // points 0 and 1
            FloorPlanEditState.SelectInRect(sel, screen, walls, aroundFirstWall, BrushEditMode.Vertex, true, false);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, sel.vertices);
            FloorPlanEditState.SelectInRect(sel, screen, walls, aroundFirstWall, BrushEditMode.Edge, true, false);
            CollectionAssert.AreEquivalent(new[] { 0 }, sel.edges, "complete: only the wall inside");
            sel.edges.Clear();
            FloorPlanEditState.SelectInRect(sel, screen, walls, aroundFirstWall, BrushEditMode.Edge, false, false);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 3 }, sel.edges, "touching: the walls meeting it too");
            FloorPlanEditState.SelectNearest(sel, screen, walls, new Vector2(405f, 150f), BrushEditMode.Edge, true);
            CollectionAssert.AreEquivalent(new[] { 0, 3 }, sel.edges, "Ctrl-click on a wall removes it");
            FloorPlanEditState.SelectNearest(sel, screen, walls, new Vector2(398f, 297f), BrushEditMode.Vertex, false);
            Assert.IsTrue(sel.vertices.Contains(2), "a click near a point adds it");
        }

        [Test]
        public void MovingASelectedWallMovesItsCornersAndTheWallsMeetingIt()
        {
            var plan = Room(FourByThree);
            FloorPlanEditState.ClearSelection();
            var sel = FloorPlanEditState.Sel(plan); sel.edges.Add(1); // the wall along x = 4
            var moving = FloorPlanEditState.MovingPoints(plan, sel, BrushEditMode.Edge);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, moving);
            FloorPlanEditState.TransformPoints(plan, new List<Vector3>(plan.points), moving, p => p + new Vector3(1f, 0.5f, 0f));
            Assert.AreEqual(new Vector3(5f, 0f, 0f), plan.points[1], "on the floor: no height");
            Assert.AreEqual(new Vector3(5f, 0f, 3f), plan.points[2]);
            BrushGenerators.Update(plan); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.IsTrue(Solid(new Vector3(5.1f, 1.5f, 1.5f)), "the wall moved");
            Assert.IsFalse(Solid(new Vector3(4.1f, 1.5f, 1.5f)), "and left its old place");
            Assert.IsTrue(Solid(new Vector3(4.5f, 1.5f, -0.1f)), "the wall meeting it got longer");
            Undo.PerformUndo();
            Assert.AreEqual(new Vector3(4f, 0f, 0f), plan.points[1], "undo");
        }

        [Test]
        public void RotateAndScaleTurnAndStretchTheSelectionOnTheFloor()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var all = new HashSet<int> { 0, 1, 2, 3 };
                var start = new List<Vector3>(plan.points);
                var c = FloorPlanEditState.Centre(start, all);
                Assert.AreEqual(new Vector3(2f, 0f, 1.5f), c);
                var turn = Quaternion.Euler(0f, 90f, 0f);
                FloorPlanEditState.TransformPoints(plan, start, all, p => c + turn * (p - c));
                Assert.That(Vector3.Distance(plan.points[0], new Vector3(0.5f, 0f, 3.5f)), Is.LessThan(1e-4f), "a quarter turn about the centre");
                FloorPlanEditState.TransformPoints(plan, start, all, p => c + Vector3.Scale(p - c, new Vector3(2f, 1f, 1f)));
                Assert.That(Vector3.Distance(plan.points[1], new Vector3(6f, 0f, 0f)), Is.LessThan(1e-4f), "twice as wide about the centre");
                Assert.That(Vector3.Distance(plan.points[0], new Vector3(-2f, 0f, 0f)), Is.LessThan(1e-4f));
            }
            finally { s.snapToGrid = snap; }
        }

        [Test]
        public void ElementOrientationLiesAlongTheSelectedWall()
        {
            var plan = Room(FourByThree);
            FloorPlanEditState.ClearSelection();
            var sel = FloorPlanEditState.Sel(plan); sel.edges.Add(1); // from (4, 0) to (4, 3)
            float yaw = FloorPlanEditState.HandleYaw(plan, sel, BrushHandleOrientation.Element, BrushEditMode.Edge);
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Assert.That(Vector3.Distance(along, Vector3.forward), Is.LessThan(1e-4f), "red along the wall");
            Assert.AreEqual(0f, FloorPlanEditState.HandleYaw(plan, sel, BrushHandleOrientation.Local, BrushEditMode.Edge));
        }

        [Test]
        public void DeletingAPointJoinsItsTwoWallsAndKeepsAtLeastOne()
        {
            var plan = Room(FourByThree);
            Selection.activeGameObject = plan.gameObject;
            FloorPlanEditState.ClearSelection();
            FloorPlanEditState.Sel(plan).vertices.Add(3);
            FloorPlanEditState.DeleteSelectedPoints();
            Assert.AreEqual(3, plan.points.Count); Assert.AreEqual(3, plan.walls.Count, "its two walls became one");
            Assert.AreEqual(1, FloorPlanEditState.RoomsOf(plan).Count, "a triangle is still a room");
            FloorPlanEditState.Sel(plan).vertices.UnionWith(new[] { 0, 1, 2 });
            FloorPlanEditState.DeleteSelectedPoints();
            Assert.AreEqual(3, plan.points.Count, "not every wall");
            FloorPlanEditState.Sel(plan).vertices.Clear(); FloorPlanEditState.Sel(plan).vertices.Add(2);
            FloorPlanEditState.DeleteSelectedPoints();
            Assert.AreEqual(2, plan.points.Count); Assert.AreEqual(1, plan.walls.Count, "the walls it joined already meet: one wall");
            Assert.AreEqual(0, FloorPlanEditState.RoomsOf(plan).Count);
        }

        [Test]
        public void DeletingAWallOpensTheRoomAndAMiddleWallLeavesTwoRuns()
        {
            var plan = Room(FourByThree);
            Assert.IsTrue(FloorPlanEditState.DeleteWalls(plan, new[] { 1 })); // the wall along x = 4
            Assert.AreEqual(0, FloorPlanEditState.RoomsOf(plan).Count, "open where the wall was");
            Assert.AreEqual(4, plan.points.Count, "every point still has a wall");
            BrushGenerators.Update(plan); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.AreEqual(3, plan.generated.Count, "three walls, no floor");
            Assert.IsFalse(Solid(new Vector3(4.1f, 1.5f, 1.5f)), "the wall is gone");

            Undo.IncrementCurrentGroup();
            Assert.IsTrue(FloorPlanEditState.DeleteWalls(plan, new[] { 2 })); // the middle of the chain p2-p3-p0-p1: two runs, one plan
            Assert.AreEqual(2, plan.walls.Count); Assert.AreEqual(4, plan.points.Count);
            BrushGenerators.Update(plan);
            Assert.AreEqual(2, plan.generated.Count, "both runs are walls of the same plan");
            Undo.PerformUndo();
            Assert.AreEqual(3, plan.walls.Count, "undo gives the wall back");
            Assert.IsFalse(FloorPlanEditState.DeleteWalls(plan, new[] { 0, 1, 2 }), "not every wall");
        }

        // ------------------------------------------------------------------ doors and windows

        static WallAnchor Anchor(Brush b) => b.GetComponent<WallAnchor>();
        static void Rebuild(FloorPlan plan) { BrushGenerators.Update(plan, true); BrushApi.ForceUpdate(); Physics.SyncTransforms(); }

        [Test]
        public void ADoorCutsItsPlanWallAndAWindowStandsOnItsSill()
        {
            var plan = Room(FourByThree);
            var door = WallAnchors.Place(plan, BrushShape.Door, 0, 2f); // the wall along z = 0, outside the room
            var window = WallAnchors.Place(plan, BrushShape.Window, 1, 1.5f); // the wall along x = 4
            Rebuild(plan);
            Assert.AreEqual(plan.transform, door.transform.parent); Assert.AreEqual(BrushOperation.Subtract, door.operation);
            Assert.IsTrue(Anchor(door).onWall && Anchor(window).onWall);
            Assert.That(door.size.z, Is.EqualTo(plan.wallThickness + 2f * BrushSettings.OpeningMarginMeters).Within(1e-4f), "through the wall");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, -0.1f)), "the doorway is open");
            Assert.IsFalse(Solid(new Vector3(2f, 0.05f, -0.1f)), "down to the floor");
            Assert.IsTrue(Solid(new Vector3(2f, 2.5f, -0.1f)), "the wall above it stays");
            Assert.IsTrue(Solid(new Vector3(3f, 1f, -0.1f)), "and beside it");
            Assert.IsTrue(Solid(new Vector3(4.1f, 0.5f, 1.5f)), "under the window's sill is wall");
            Assert.IsFalse(Solid(new Vector3(4.1f, 1.5f, 1.5f)), "the window is open");
            Assert.IsTrue(Solid(new Vector3(4.1f, 2.5f, 1.5f)), "above it is wall");
        }

        [Test]
        public void ADoorFollowsItsWallAndItsThickness()
        {
            var plan = Room(FourByThree);
            var door = WallAnchors.Place(plan, BrushShape.Door, 0, 1f);
            Rebuild(plan);
            Assert.That(Vector3.Distance(door.transform.position, new Vector3(1f, 1.1f, -0.1f)), Is.LessThan(1e-4f));
            plan.points[0] = new Vector3(0f, 0f, -1f); // the wall turns; the door keeps 1 m from its first corner
            plan.wallThickness = 0.5f;
            Rebuild(plan);
            var dir = new Vector3(4f, 0f, 1f).normalized;
            var outward = new Vector3(dir.z, 0f, -dir.x);
            var expected = new Vector3(0f, 0f, -1f) + dir * 1f + outward * 0.25f + Vector3.up * 1.1f;
            Assert.That(Vector3.Distance(door.transform.position, expected), Is.LessThan(1e-3f), "on the wall's middle, 1 m along it");
            Assert.That(Mathf.Abs(Vector3.Dot(door.transform.right, dir)), Is.EqualTo(1f).Within(1e-4f), "facing across the wall");
            Assert.That(door.size.z, Is.EqualTo(0.5f + 2f * BrushSettings.OpeningMarginMeters).Within(1e-4f), "as deep as the wall is thick");
        }

        [Test]
        public void SplittingAWallKeepsTheDoorAndDeletingTheWallFreesIt()
        {
            var plan = Room(FourByThree);
            var door = WallAnchors.Place(plan, BrushShape.Door, 0, 3f);
            Rebuild(plan);
            var ids = new List<int>(plan.pointIds);
            int added = plan.SplitWall(0, new Vector3(2f, 0f, 0f)); // a + on the door's wall
            Rebuild(plan);
            CollectionAssert.AreEqual(ids, plan.pointIds.GetRange(0, ids.Count), "the other points keep their ids");
            Assert.That(door.transform.position.x, Is.EqualTo(3f).Within(1e-4f), "the door stays where it was");
            var pieces = new List<FloorPlan.Wall>(); plan.Walls(pieces);
            Assert.IsTrue(WallAnchors.Resolve(Anchor(door), pieces, out var piece, out float along));
            Assert.AreEqual(added, piece.startId, "on the piece it is on"); Assert.That(along, Is.EqualTo(1f).Within(1e-4f));
            Assert.AreEqual(ids[0], Anchor(door).startId, "its saved wall is untouched, so undo needs nothing of it");

            Undo.IncrementCurrentGroup();
            FloorPlanEditState.DeleteWalls(plan, new[] { 1 }); // the door's wall
            Rebuild(plan);
            Assert.IsFalse(Anchor(door).onWall, "its wall is gone");
            Assert.That(door.transform.position.x, Is.EqualTo(3f).Within(1e-4f), "it stays, a free cut");
            Undo.PerformUndo();
            Rebuild(plan);
            Assert.IsTrue(Anchor(door).onWall, "undo gives the wall back, with its door");
        }

        [Test]
        public void MovingADoorPutsItOnTheNearestWallOrFreesIt()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var door = WallAnchors.Place(plan, BrushShape.Door, 0, 2f);
                Rebuild(plan);
                door.transform.position = new Vector3(1.3f, 1.1f, -0.3f); // along its wall, a little off it
                WallAnchors.Moved(Anchor(door));
                Rebuild(plan);
                Assert.That(Anchor(door).distance, Is.EqualTo(1.3f).Within(1e-4f));
                Assert.That(Vector3.Distance(door.transform.position, new Vector3(1.3f, 1.1f, -0.1f)), Is.LessThan(1e-4f), "back on the wall");
                door.transform.position = new Vector3(4.2f, 1.1f, 2f); // onto another wall
                WallAnchors.Moved(Anchor(door));
                Rebuild(plan);
                Assert.That(Vector3.Distance(door.transform.position, new Vector3(4.1f, 1.1f, 2f)), Is.LessThan(1e-4f), "on the wall along x = 4");
                door.transform.position = new Vector3(2f, 1.1f, -5f); // far from every wall
                WallAnchors.Moved(Anchor(door));
                Rebuild(plan);
                Assert.IsFalse(Anchor(door).onWall, "free");
                Assert.That(Vector3.Distance(door.transform.position, new Vector3(2f, 1.1f, -5f)), Is.LessThan(1e-4f), "where it was put");
            }
            finally { s.snapToGrid = snap; }
        }

        // ------------------------------------------------------------------ anything on a wall

        /// <summary>A picture: a 0.6 x 0.4 x 0.1 m box mesh, its pivot in the middle.</summary>
        static GameObject Picture()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Picture";
            go.transform.localScale = new Vector3(0.6f, 0.4f, 0.1f);
            var root = new GameObject("Picture root");
            go.transform.SetParent(root.transform, false);
            return root;
        }

        static void Near(Vector3 expected, Vector3 actual, string message = null) => Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-3f), (message ?? "") + " expected " + expected + " was " + actual);

        [Test]
        public void AnAttachedObjectFollowsItsWallAndCanBePutOnAnotherInTheInspector()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var picture = Picture();
                picture.transform.position = new Vector3(2f, 1.5f, 0.05f); // against the inside face of the wall along z = 0
                var a = WallAnchors.Attach(picture);
                Rebuild(plan);
                Assert.AreEqual(plan.transform, picture.transform.parent);
                Assert.AreEqual(WallFace.Inside, a.face); Assert.IsTrue(a.onWall);
                var walls = new List<FloorPlan.Wall>(); plan.Walls(walls);
                Assert.AreEqual(0, WallAnchors.WallIndex(a, walls), "the nearest wall");
                Near(new Vector3(2f, 1.5f, 0.05f), picture.transform.position, "where it was");

                plan.points[0] = new Vector3(0f, 0f, -1f); plan.points[1] = new Vector3(4f, 0f, -1f); // the wall moves back a metre
                Rebuild(plan);
                Near(new Vector3(2f, 1.5f, -0.95f), picture.transform.position, "it goes with the wall");

                WallAnchors.SetWall(a, 1); // the wall along x = 4, from (4, -1) to (4, 3): 2 m along, still on its inside face
                Rebuild(plan);
                plan.Walls(walls);
                Assert.AreEqual(1, WallAnchors.WallIndex(a, walls));
                Near(new Vector3(3.95f, 1.5f, 1f), picture.transform.position, "on the other wall, as far along and as high");
                Near(Vector3.left, picture.transform.forward, "facing into the room");
                WallAnchors.SetWall(a, -1);
                Rebuild(plan);
                Assert.IsFalse(a.onWall, "none: free");
                Near(new Vector3(3.95f, 1.5f, 1f), picture.transform.position, "and it stays");
            }
            finally { s.snapToGrid = snap; }
        }

        [Test]
        public void AnAttachedBrushKeepsItsPoseOnTheWallAndIsNotSnapped()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var shelf = BrushApi.Create(BrushShape.Box, new Vector3(1.3f, 1.2f, 0.2f), new Vector3(1f, 0.1f, 0.4f), Quaternion.Euler(0f, 0f, 5f));
                var a = WallAnchors.Attach(shelf.gameObject);
                Rebuild(plan);
                Assert.AreEqual(plan, a.Plan, "the nearest plan"); Assert.AreEqual(WallFace.Inside, a.face);
                Near(new Vector3(1.3f, 1.2f, 0.2f), shelf.transform.position, "attached where it was");
                Assert.That(Quaternion.Angle(Quaternion.identity, shelf.transform.rotation), Is.LessThan(0.01f), "lined up with the wall: its tilt is gone");
                Assert.IsTrue(shelf.IsPlaced); Assert.IsFalse(BrushSnap.Snap(shelf), "placed by its wall, not the grid");
                // the wall turns about its first corner: the shelf turns with it, 1.3 m along, 0.2 m off the face
                plan.points[1] = new Vector3(4f, 0f, 4f);
                Rebuild(plan);
                var dir = new Vector3(1f, 0f, 1f).normalized; var inward = new Vector3(-dir.z, 0f, dir.x);
                Near(dir * 1.3f + inward * 0.2f + Vector3.up * 1.2f, shelf.transform.position, "on the turned wall");
                Assert.That(Quaternion.Angle(Quaternion.LookRotation(inward), shelf.transform.rotation), Is.LessThan(0.01f), "turned with it");
            }
            finally { s.snapToGrid = snap; }
        }

        [Test]
        public void DetachingLeavesAnObjectWhereItIsAndPullingItAwayFreesIt()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var picture = Picture();
                picture.transform.position = new Vector3(2f, 1.5f, 0.05f);
                var a = WallAnchors.Attach(picture);
                Rebuild(plan);
                picture.transform.position = new Vector3(2.5f, 1.6f, 0.05f); // slid along the face
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsTrue(a.onWall); Assert.That(a.distance, Is.EqualTo(2.5f).Within(1e-4f)); Assert.That(a.height, Is.EqualTo(1.6f).Within(1e-4f));
                picture.transform.position = new Vector3(2f, 1.5f, 1.5f); // into the middle of the room
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsFalse(a.onWall, "free");
                Near(new Vector3(2f, 1.5f, 1.5f), picture.transform.position, "where it was put");

                picture.transform.position = new Vector3(2f, 1.5f, 0.05f);
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsTrue(a.onWall, "back against a wall, back on it");
                WallAnchors.Detach(picture);
                Assert.IsNull(picture.GetComponent<WallAnchor>()); Assert.AreNotEqual(plan.transform, picture.transform.parent);
                plan.points[0] = new Vector3(0f, 0f, -1f); plan.points[1] = new Vector3(4f, 0f, -1f);
                Rebuild(plan);
                Near(new Vector3(2f, 1.5f, 0.05f), picture.transform.position, "detached: it stays");
            }
            finally { s.snapToGrid = snap; }
        }

        static void Facing(Vector3 forward, Transform t, string message) =>
            Assert.That(Quaternion.Angle(Quaternion.LookRotation(forward), t.rotation), Is.LessThan(0.01f), message + ": forward " + (t.rotation * Vector3.forward).ToString("F2"));

        [Test]
        public void AnAttachedObjectLinesUpWithItsWallAndTurnsWithItsSide()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var chest = BrushApi.Create(BrushShape.Box, new Vector3(3.7f, 0.5f, 1.5f), new Vector3(1f, 1f, 0.5f), Quaternion.Euler(0f, 20f, 0f));
                var a = WallAnchors.Attach(chest.gameObject); // the wall along x = 4, its inside facing -x
                Rebuild(plan);
                Facing(Vector3.left, chest.transform, "out of the inside face, into the room");
                var so = new SerializedObject(a); so.FindProperty(nameof(WallAnchor.face)).intValue = (int)WallFace.Outside; so.ApplyModifiedProperties();
                Rebuild(plan);
                Facing(Vector3.right, chest.transform, "on the outside: out of the building");
                Undo.RecordObject(a, "turn"); a.rotation = Quaternion.Euler(0f, 180f, 0f) * a.rotation; WallAnchor.Changed?.Invoke(a); // Turn around
                Rebuild(plan);
                Facing(Vector3.left, chest.transform, "turned around: towards the wall");
                WallAnchors.NearestWall(plan, new Vector2(2f, 0f), out var south, out _, out _);
                WallAnchors.SetWall(a, south.index); // the wall along z = 0, still on its outside
                Rebuild(plan);
                Facing(Vector3.forward, chest.transform, "on another wall it keeps its turn: towards that wall");
                chest.transform.position = new Vector3(0.3f, 0.5f, 1.5f); // dragged against the wall along x = 0
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsTrue(a.onWall);
                Assert.AreEqual(WallFace.Inside, a.face, "dragged inside the room");
                Facing(Vector3.left, chest.transform, "dragged to another wall: lined up with it, still turned towards it");

                var drawn = BrushApi.Create(BrushShape.Box, new Vector3(2f, 1.5f, 2.9f), new Vector3(1f, 0.2f, 1f), Quaternion.Euler(90f, 0f, 0f));
                WallAnchors.Attach(drawn.gameObject, plan, true); // drawn on the wall: its height out of the wall
                Rebuild(plan);
                Assert.That(Quaternion.Angle(Quaternion.Euler(90f, 0f, 0f), drawn.transform.rotation), Is.LessThan(0.01f), "a drawn brush keeps its pose");
            }
            finally { s.snapToGrid = snap; }
        }

        [Test]
        public void AWardrobeStandsItsOffsetFromTheWallAndSlidesAlongItThere()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid;
            try
            {
                s.snapToGrid = false;
                var plan = Room(FourByThree);
                var wardrobe = BrushApi.Create(BrushShape.Box, new Vector3(2f, 1f, 0.3f), new Vector3(2f, 2f, 0.6f), Quaternion.identity);
                BrushApi.SetPivot(wardrobe, new Vector3(0.5f, 0f, 0f)); // its back, at the bottom
                var a = WallAnchors.Attach(wardrobe.gameObject);
                Rebuild(plan);
                Assert.IsTrue(a.onWall); Assert.That(a.offset, Is.EqualTo(0f).Within(1e-4f), "its back against the face");
                var so = new SerializedObject(a); so.FindProperty(nameof(WallAnchor.offset)).floatValue = 1f; so.ApplyModifiedProperties(); // typed in the Inspector
                Rebuild(plan);
                Assert.IsTrue(a.onWall, "a typed offset keeps it on the wall");
                Near(new Vector3(2f, 0f, 1f), wardrobe.transform.position, "1 m out from the face");
                wardrobe.transform.position = new Vector3(2.5f, 0f, 1.05f); // slid along the wall
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsTrue(a.onWall, "sliding at its offset keeps it");
                Assert.That(a.distance, Is.EqualTo(2.5f).Within(1e-4f)); Assert.That(a.offset, Is.EqualTo(1.05f).Within(1e-4f));
                plan.EnsureGraph(); plan.SplitWall(0, new Vector3(3f, 0f, 0f)); // a point splits its wall (z = 0, from the first corner)
                Rebuild(plan);
                wardrobe.transform.position = new Vector3(2.4f, 0f, 1.05f);
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsTrue(a.onWall, "its wall split: still the same wall");
                wardrobe.transform.position = new Vector3(2.5f, 0f, 2f); // pulled a metre further out
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsFalse(a.onWall, "pulled away: free");
                wardrobe.transform.position = new Vector3(2.5f, 0f, 1.1f); // near its old offset, but off the wall
                WallAnchors.Moved(a);
                Rebuild(plan);
                Assert.IsFalse(a.onWall, "a free object takes a wall only against it");
            }
            finally { s.snapToGrid = snap; }
        }

        // ------------------------------------------------------------------ floor and ceiling

        static Brush Generated(FloorPlan plan, string name) => plan.generated.Find(b => b != null && b.name.StartsWith(name));

        [Test]
        public void AClosedRoomGetsAFloorUnderItsWallsAndACeilingOnTop()
        {
            var plan = Room(FourByThree); // floor on by default, ceiling off
            Assert.IsNotNull(Generated(plan, "Floor")); Assert.IsNull(Generated(plan, "Ceiling"));
            Assert.IsTrue(Solid(new Vector3(2f, -0.1f, 1.5f)), "the floor under the room");
            Assert.IsTrue(Solid(new Vector3(-0.15f, -0.1f, -0.15f)), "and under the outer corner of the walls");
            Assert.IsFalse(Solid(new Vector3(2f, -0.25f, 1.5f)), "0.2 m thick");
            Assert.IsFalse(Solid(new Vector3(2f, 3.1f, 1.5f)), "no ceiling");

            plan.ceiling = true; plan.ceilingThickness = 0.3f;
            BrushGenerators.Update(plan); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.IsTrue(Solid(new Vector3(2f, 3.25f, 1.5f)), "the ceiling on the walls");
            Assert.IsTrue(Solid(new Vector3(4.15f, 3.1f, 3.15f)), "over the walls too");
            Assert.IsFalse(Solid(new Vector3(2f, 1.5f, 1.5f)), "the room stays empty");

            plan.points[2] = new Vector3(6f, 0f, 3f); // the floor follows the outline
            BrushGenerators.Update(plan); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.IsTrue(Solid(new Vector3(5.5f, -0.1f, 2.5f)));

            plan.walls.RemoveAt(3); // an open line has no inside
            BrushGenerators.Update(plan);
            Assert.IsNull(Generated(plan, "Floor")); Assert.IsNull(Generated(plan, "Ceiling"));
        }

        [Test]
        public void AnLShapedRoomGetsAnLShapedFloor()
        {
            var plan = Room(new Vector3(0, 0, 0), new Vector3(6, 0, 0), new Vector3(6, 0, 4), new Vector3(3, 0, 4), new Vector3(3, 0, 6), new Vector3(0, 0, 6));
            var floor = Generated(plan, "Floor");
            Assert.IsNotNull(floor); Assert.IsTrue(floor.polyhedron.IsSound(out var why), why);
            Assert.IsTrue(Solid(new Vector3(1.5f, -0.1f, 5f)), "in the arm");
            Assert.IsTrue(Solid(new Vector3(5f, -0.1f, 1f)));
            Assert.IsFalse(Solid(new Vector3(5f, -0.1f, 5.5f)), "not in the missing corner");
        }

        // ------------------------------------------------------------------ wall networks

        static List<Brush> GeneratedNamed(FloorPlan plan, string prefix) => plan.generated.FindAll(b => b != null && b.name.StartsWith(prefix));

        static void AssertSound(FloorPlan plan)
        {
            foreach (var b in plan.generated) { Assert.IsNotNull(b); Assert.IsTrue(b.polyhedron.IsSound(out var why), b.name + ": " + why); }
        }

        [Test]
        public void AnInteriorWallJoinsTheOutsideWallsWithoutGaps()
        {
            var plan = Room(FourByThree);
            // a wall across the room from x = 2 on the front wall to x = 2 on the back wall, drawn by splitting both
            int front = plan.SplitWall(0, new Vector3(2f, 0f, 0f));
            int back = plan.SplitWall(3, new Vector3(2f, 0f, 3f)); // wall 3 is the back wall (4,3) to (0,3) after the first split
            plan.AddWall(front, back);
            Rebuild(plan);
            AssertSound(plan);
            Assert.AreEqual(2, FloorPlanEditState.RoomsOf(plan).Count, "two rooms");
            Assert.AreEqual(2, GeneratedNamed(plan, "Floor").Count, "a floor each");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, 1.5f)), "the interior wall, centred on its line");
            Assert.IsFalse(Solid(new Vector3(2.08f, 1.5f, 1.5f)), "0.1 m thick by default");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, 0.02f)), "it reaches the outside wall");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, -0.1f)), "and the outside wall runs on behind it");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, 2.98f)), "the other end too");
            var walls = new List<FloorPlan.Wall>(); plan.Walls(walls);
            Assert.IsFalse(walls.Find(w => w.startId == front && w.endId == back).exterior, "a wall between rooms is an interior wall");
            Assert.IsTrue(walls.Find(w => w.endId == front).exterior, "the outside wall it splits is still an outside wall");
        }

        [Test]
        public void WallsEndingOnOrCrossingOtherWallsJoinByThemselves()
        {
            var plan = Room(FourByThree);
            // drawn loose: its ends lie on the outside walls but are points of their own
            int a = plan.AddPoint(new Vector3(2f, 0f, 0f)), b = plan.AddPoint(new Vector3(2f, 0f, 3f));
            plan.AddWall(a, b);
            // and one crossing it, from wall to wall, with no point where they cross
            int c = plan.AddPoint(new Vector3(0f, 0f, 1.5f)), d = plan.AddPoint(new Vector3(4f, 0f, 1.5f));
            plan.AddWall(c, d);
            Rebuild(plan);
            AssertSound(plan);
            Assert.AreEqual(4, FloorPlanEditState.RoomsOf(plan).Count, "four rooms");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, 1.54f)), "the crossing is solid");
            Assert.IsTrue(Solid(new Vector3(2.04f, 1.5f, 0.02f)), "the joints with the outside walls are closed");
            Assert.IsTrue(Solid(new Vector3(0.02f, 1.5f, 1.54f)));
            Assert.IsFalse(Solid(new Vector3(1f, 1.5f, 0.75f)), "the rooms stay empty");
        }

        [Test]
        public void AWallCanHaveItsOwnThickness()
        {
            var plan = Room(FourByThree);
            int front = plan.SplitWall(0, new Vector3(2f, 0f, 0f));
            int back = plan.SplitWall(3, new Vector3(2f, 0f, 3f));
            int inner = plan.AddWall(front, back);
            var link = plan.walls[inner]; link.thickness = 0.3f; plan.walls[inner] = link;
            plan.interiorWallThickness = 0.05f; plan.wallThickness = 0.25f;
            Rebuild(plan);
            Assert.IsTrue(Solid(new Vector3(2.13f, 1.5f, 1.5f)), "0.3 m: its own thickness");
            Assert.IsTrue(Solid(new Vector3(2f, 1.5f, -0.23f)), "outside walls take the plan's outside thickness");
        }

        [Test]
        public void RoomsKeepTheirMaterialsWhileWallsMoveAndPassThemOnWhenSplit()
        {
            var plan = Room(FourByThree);
            var wood = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "wood" };
            var rooms = new List<FloorPlan.Room>(); plan.Rooms(rooms);
            var settings = plan.SettingsOf(rooms[0]);
            settings.floorMaterial = wood; settings.ceiling = true;
            Rebuild(plan);
            Assert.AreEqual(wood, Generated(plan, "Floor").material);
            Assert.IsNotNull(Generated(plan, "Ceiling"), "this room has a ceiling, the plan's default has none");

            plan.points[2] = new Vector3(5f, 0f, 3.5f); // a wall moves: the room is still the room
            Rebuild(plan);
            Assert.AreEqual(wood, Generated(plan, "Floor").material);

            int front = plan.SplitWall(0, new Vector3(2f, 0f, 0f));
            int back = plan.SplitWall(3, new Vector3(2f, 0f, 3f));
            plan.AddWall(front, back);
            Rebuild(plan);
            var floors = GeneratedNamed(plan, "Floor");
            Assert.AreEqual(2, floors.Count);
            foreach (var f in floors) Assert.AreEqual(wood, f.material, "both halves keep the room's floor");
            Object.DestroyImmediate(wood);
        }

        [Test]
        public void AnOutlineFromBeforeWallNetworksBecomesWalls()
        {
            var go = new GameObject("Old plan"); var plan = go.AddComponent<FloorPlan>();
            plan.points = new List<Vector3>(FourByThree); plan.closed = true; plan.walls.Clear();
            plan.EnsureGraph();
            Assert.AreEqual(4, plan.walls.Count, "one wall per side, closing the loop");
            Assert.IsFalse(plan.closed, "converted once");
            Assert.AreEqual(plan.pointIds[3], plan.walls[3].start); Assert.AreEqual(plan.pointIds[0], plan.walls[3].end);
        }

        [Test]
        public void AnyWallNetworkBuildsSoundBrushes()
        {
            // students: points anywhere on a small grid, walls between any of them, repeated, crossing, overlapping, zero length
            var random = new System.Random(1234);
            for (int round = 0; round < 40; round++)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("Fuzz"); var plan = go.AddComponent<FloorPlan>();
                var ids = new List<int>();
                int count = 3 + random.Next(8);
                for (int i = 0; i < count; i++) ids.Add(plan.AddPoint(new Vector3(random.Next(6), 0f, random.Next(6))));
                int walls = 2 + random.Next(12);
                for (int i = 0; i < walls; i++)
                {
                    int w = plan.AddWall(ids[random.Next(ids.Count)], ids[random.Next(ids.Count)]);
                    if (w >= 0 && random.Next(4) == 0) { var l = plan.walls[w]; l.thickness = 0.05f + (float)random.NextDouble() * 0.5f; plan.walls[w] = l; }
                }
                plan.ceiling = random.Next(2) == 0;
                plan.side = (FloorPlan.Side)random.Next(3);
                Assert.DoesNotThrow(() => BrushGenerators.Update(plan, true), "round " + round);
                foreach (var b in plan.generated) Assert.IsTrue(b.polyhedron.IsSound(out var why), "round " + round + " " + b.name + ": " + why);
                Assert.DoesNotThrow(() => BrushApi.ForceUpdate(), "round " + round);
            }
        }

        [Test]
        public void ARoomsSettingsStayWithThatRoomOnly()
        {
            var plan = Room(FourByThree);
            int front = plan.SplitWall(0, new Vector3(2f, 0f, 0f));
            int back = plan.SplitWall(3, new Vector3(2f, 0f, 3f));
            plan.AddWall(front, back);
            Rebuild(plan);
            var rooms = new List<FloorPlan.Room>(); plan.Rooms(rooms);
            Assert.AreEqual(2, rooms.Count);
            plan.SettingsOf(rooms[0]).ceiling = true; // two rooms share the dividing wall's two points
            plan.Rooms(rooms);
            Assert.IsTrue(rooms[0].ceiling); Assert.IsFalse(rooms[1].ceiling, "the room next door keeps the plan's default");
            Rebuild(plan);
            Assert.AreEqual(1, GeneratedNamed(plan, "Ceiling").Count);
        }

        [Test]
        public void UndoingAWallsDeletionPutsItsDoorBack()
        {
            var plan = Room(FourByThree);
            int front = plan.SplitWall(0, new Vector3(2f, 0f, 0f));
            int back = plan.SplitWall(3, new Vector3(2f, 0f, 3f));
            int inner = plan.AddWall(front, back);
            Rebuild(plan);
            Undo.IncrementCurrentGroup();
            var door = WallAnchors.Place(plan, BrushShape.Door, inner, 1.5f);
            Rebuild(plan);
            var placed = door.transform.position;
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 1.5f)), "the doorway is open");
            Assert.IsTrue(Solid(new Vector3(2f, 2.6f, 1.5f)), "the wall above it stands");
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(FloorPlanEditState.DeleteWalls(plan, new[] { inner }));
            Rebuild(plan);
            Assert.IsTrue(door != null, "the door is still there");
            Assert.IsFalse(Anchor(door).onWall);
            Undo.PerformUndo();
            BrushGenerators.Flush(); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.IsTrue(door != null, "undo keeps the door");
            Assert.AreEqual(2, FloorPlanEditState.RoomsOf(plan).Count, "two rooms again");
            Assert.IsTrue(Anchor(door).onWall, "back on its wall");
            Assert.That(Vector3.Distance(placed, door.transform.position), Is.LessThan(1e-4f), "where it was");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 1.5f)), "the doorway is open again");
            Assert.IsTrue(Solid(new Vector3(2f, 2.6f, 1.5f)), "in the wall that came back");
        }

        [Test]
        public void UndoingTheDeletionOfALooselyJoinedWallPutsItsDoorBack()
        {
            var plan = Room(FourByThree);
            // drawn loose: its ends only touch the outside walls, as points of their own
            int a = plan.AddPoint(new Vector3(2f, 0f, 0f)), b = plan.AddPoint(new Vector3(2f, 0f, 3f));
            int inner = plan.AddWall(a, b);
            Rebuild(plan);
            Undo.IncrementCurrentGroup();
            var door = WallAnchors.Place(plan, BrushShape.Door, inner, 1.5f);
            Rebuild(plan);
            var placed = door.transform.position; var ids = (Anchor(door).startId, Anchor(door).endId);
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(FloorPlanEditState.DeleteWalls(plan, new[] { inner }));
            Rebuild(plan);
            Assert.IsTrue(door != null);
            Assert.IsFalse(Anchor(door).onWall, "its wall is gone: free, not moved onto another wall");
            Assert.That(Vector3.Distance(placed, door.transform.position), Is.LessThan(1e-4f), "and left where it was");
            Undo.PerformUndo();
            BrushGenerators.Flush(); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.AreEqual(ids, (Anchor(door).startId, Anchor(door).endId), "still anchored to the same wall");
            Assert.IsTrue(Anchor(door).onWall, "back on its wall");
            Assert.That(Vector3.Distance(placed, door.transform.position), Is.LessThan(1e-4f), "where it was");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 1.5f)), "the doorway is open again");
        }

        [Test]
        public void UndoingTheDeletionOfAHalfJoinedWallPutsItsDoorBack()
        {
            var plan = Room(FourByThree);
            // one end clicked onto the front wall (a shared point), the other only touching the back wall
            int a = plan.SplitWall(0, new Vector3(2f, 0f, 0f)), b = plan.AddPoint(new Vector3(2f, 0f, 3f));
            int inner = plan.AddWall(a, b);
            Rebuild(plan);
            Undo.IncrementCurrentGroup();
            var door = WallAnchors.Place(plan, BrushShape.Door, inner, 1.5f);
            Rebuild(plan);
            var placed = door.transform.position; var ids = (Anchor(door).startId, Anchor(door).endId);
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(FloorPlanEditState.DeleteWalls(plan, new[] { inner }));
            Rebuild(plan);
            Assert.IsFalse(Anchor(door).onWall, "its wall is gone: free, not moved onto a wall of its first point");
            Assert.That(Vector3.Distance(placed, door.transform.position), Is.LessThan(1e-4f), "and left where it was");
            Undo.PerformUndo();
            BrushGenerators.Flush(); BrushApi.ForceUpdate(); Physics.SyncTransforms();
            Assert.AreEqual(ids, (Anchor(door).startId, Anchor(door).endId), "still anchored to the same wall");
            Assert.IsTrue(Anchor(door).onWall, "back on its wall");
            Assert.That(Vector3.Distance(placed, door.transform.position), Is.LessThan(1e-4f), "where it was");
        }

        [Test]
        public void AMaterialDroppedOnAPlanGoesToItsWallsOrToThatRoom()
        {
            var plan = Room(FourByThree);
            var red = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "red" };
            var blue = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "blue" };
            BrushMaterialDrop.Drop(GeneratedNamed(plan, "Wall")[0], red);
            Assert.AreEqual(red, plan.wallMaterial, "a wall: every wall");
            BrushMaterialDrop.Drop(Generated(plan, "Floor"), blue);
            Rebuild(plan);
            Assert.AreEqual(blue, Generated(plan, "Floor").material, "a floor: that room's floor");
            foreach (var w in GeneratedNamed(plan, "Wall")) Assert.AreEqual(red, w.material);
            var box = BrushApi.Create(BrushShape.Box, new Vector3(10f, 0.5f, 0f), Vector3.one, Quaternion.identity);
            BrushMaterialDrop.Drop(box, blue);
            Assert.AreEqual(blue, box.material, "any other brush: its own material");
            Object.DestroyImmediate(red); Object.DestroyImmediate(blue);
        }

        [Test]
        public void AMeshInFrontOfABrushTakesADroppedMaterialUnitysWay()
        {
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            Transform mesh = null;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>()) if (BrushGroup.IsMeshChildName(r.name)) mesh = r.transform;
            Assert.IsNotNull(mesh, "the brush has a generated mesh");
            Assert.IsTrue(BrushMaterialDrop.OverBrushes(mesh.gameObject), "the generated mesh: the brushes take it");
            Assert.IsTrue(BrushMaterialDrop.OverBrushes(null), "nothing picked: the brushes may take it");
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Assert.IsFalse(BrushMaterialDrop.OverBrushes(prop), "a regular mesh: Unity's own drop");
            Object.DestroyImmediate(prop);
        }
    }
}

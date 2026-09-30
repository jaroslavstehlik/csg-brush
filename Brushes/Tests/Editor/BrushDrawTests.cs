using CsgBrush.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>The geometry behind the Create tools: base rectangle on a plane, height along its normal, always on the grid.</summary>
    public class BrushDrawTests
    {
        [Test]
        public void GroundPlaneBoxFromDragAndHeight()
        {
            var rot = BrushDraw.PlaneRotation(Vector3.up);
            Assert.AreEqual(Quaternion.identity.eulerAngles.ToString("F1"), rot.eulerAngles.ToString("F1"));
            var opposite = BrushDraw.SnapInPlane(new Vector3(3.2f, 0.4f, 1.7f), Vector3.zero, rot, 0.5f);
            Assert.AreEqual(new Vector3(3f, 0f, 1.5f), opposite);
            BrushDraw.Pose(Vector3.zero, opposite, 1f, rot, 0.5f, out var centre, out var size, out var rotation);
            Assert.AreEqual(new Vector3(1.5f, 0.5f, 0.75f).ToString("F3"), centre.ToString("F3"));
            Assert.AreEqual(new Vector3(3f, 1f, 1.5f).ToString("F3"), size.ToString("F3"));
            Assert.AreEqual(Quaternion.identity.eulerAngles.ToString("F1"), rotation.eulerAngles.ToString("F1"));
        }

        [Test]
        public void NegativeDragAndDownwardHeightGivePositiveSizes()
        {
            var rot = BrushDraw.PlaneRotation(Vector3.up);
            BrushDraw.Pose(new Vector3(2f, 1f, 2f), new Vector3(0f, 1f, -1f), -2f, rot, 0.5f, out var centre, out var size, out _);
            Assert.AreEqual(new Vector3(2f, 2f, 3f).ToString("F3"), size.ToString("F3"));
            Assert.AreEqual(new Vector3(1f, 0f, 0.5f).ToString("F3"), centre.ToString("F3"));
        }

        [Test]
        public void ZeroExtentsBecomeOneGridStep()
        {
            var rot = BrushDraw.PlaneRotation(Vector3.up);
            BrushDraw.Pose(Vector3.zero, Vector3.zero, 0f, rot, 0.5f, out var centre, out var size, out _);
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f).ToString("F3"), size.ToString("F3"));
            Assert.AreEqual(new Vector3(0.25f, 0.25f, 0.25f).ToString("F3"), centre.ToString("F3"));
        }

        [Test]
        public void WallPlaneBuildsOutOfTheWall()
        {
            // a plane facing +x: height runs along +x, the base lies in the wall
            var rot = BrushDraw.PlaneRotation(new Vector3(0.9f, 0.1f, 0.2f));
            Assert.AreEqual(Vector3.right.ToString("F3"), (rot * Vector3.up).ToString("F3"), "local up is the wall normal");
            var origin = new Vector3(4f, 0f, 0f);
            var opposite = BrushDraw.SnapInPlane(new Vector3(4.3f, 2.1f, 3.4f), origin, rot, 0.5f);
            Assert.AreEqual(4f, opposite.x, 1e-4f, "stays in the wall plane");
            BrushDraw.Pose(origin, opposite, 1f, rot, 0.5f, out var centre, out var size, out var rotation);
            Assert.AreEqual(4.5f, centre.x, 1e-4f, "half the height out of the wall");
            Assert.AreEqual(1f, size.y, 1e-4f, "height is the brush's local y");
            var local = Quaternion.Inverse(rotation) * (opposite - origin);
            Assert.AreEqual(0f, local.y, 1e-4f);
            Assert.IsTrue(Mathf.Abs(Quaternion.Angle(rotation, BrushSnap.SnapRotation(rotation, 15f))) < 1e-3f, "a cardinal frame is on the rotation grid");
        }

        [Test]
        public void RoundBrushesAreDrawnFromTheirBaseCentre()
        {
            var rot = BrushDraw.PlaneRotation(Vector3.up);
            BrushDraw.CentredPose(new Vector3(1f, 0f, 1f), new Vector3(3.2f, 0f, 1f), 1.5f, rot, 0.5f, false, out var centre, out var size, out _);
            Assert.AreEqual(new Vector3(4f, 1.5f, 4f).ToString("F3"), size.ToString("F3"), "radius 2 rounded from 2.2, diameter 4");
            Assert.AreEqual(new Vector3(1f, 0.75f, 1f).ToString("F3"), centre.ToString("F3"), "transform at mid height above the press point");
            BrushDraw.CentredPose(Vector3.zero, new Vector3(0f, 0f, 1.5f), 0f, rot, 0.5f, true, out centre, out size, out _);
            Assert.AreEqual(new Vector3(3f, 3f, 3f).ToString("F3"), size.ToString("F3"), "a sphere with no height is round");
            Assert.AreEqual(new Vector3(0f, 1.5f, 0f).ToString("F3"), centre.ToString("F3"), "resting on the surface");
            BrushDraw.CentredPose(Vector3.zero, new Vector3(1f, 0f, 0f), -2f, rot, 0.5f, false, out centre, out size, out _);
            Assert.AreEqual(new Vector3(2f, 2f, 2f).ToString("F3"), size.ToString("F3"));
            Assert.AreEqual(new Vector3(0f, -1f, 0f).ToString("F3"), centre.ToString("F3"), "downward height hangs below the surface");
        }

        [Test]
        public void RadialStairsTakeWidthAndRiseFromTheDrag()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid; int grid = s.gridIndex;
            s.snapToGrid = true;
            for (int i = 0; i < s.gridSizes.Length; i++) if (s.ToMeters(s.gridSizes[i]) <= 0.5f) s.SetGridIndex(i); // the largest step up to 0.5 m, whatever the preset
            float g = s.GridMeters;
            try
            {
                var p = BrushCreateTool.ParametersForRadial(BrushShape.CurvedStairs, 3f, 2f);
                Assert.AreEqual(g, p.stairs.innerRadius, 1e-4f, "inner radius defaults to one grid step");
                Assert.AreEqual(3f - g, p.stairs.stepWidth, 1e-4f, "outer radius minus the inner radius");
                BrushCreateTool.NewBrushParameters(out float stepHeight, out _);
                Assert.AreEqual(stepHeight, p.stairs.stepHeight, 1e-4f, "the step height is the panel's");
                Assert.AreEqual(BrushPolyhedron.StepCount(2f, stepHeight), p.stairs.numSteps, "the steps fill the drawn height");
                var tiny = BrushCreateTool.ParametersForRadial(BrushShape.SpiralStairs, 0.2f, 0.1f);
                Assert.AreEqual(g, tiny.stairs.stepWidth, 1e-4f, "never thinner than a grid step");
            }
            finally { s.snapToGrid = snap; s.SetGridIndex(grid); }
        }

        [Test]
        public void ElementOrientationFollowsTheSelection()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = -1, right = -1;
            for (int f = 0; f < box.faces.Length; f++) { var n = box.Plane(f); if (n.y > 0.9f) top = f; if (n.x > 0.9f) right = f; }
            var sel = new BrushEditState.Selection(); sel.faces.Add(top);
            var rot = BrushEditState.ElementRotation(box, BrushEditMode.Face, sel);
            Assert.AreEqual(Vector3.up.ToString("F3"), (rot * Vector3.forward).ToString("F3"), "blue axis along the top face normal");
            sel.faces.Clear(); sel.faces.Add(right);
            rot = BrushEditState.ElementRotation(box, BrushEditMode.Face, sel);
            Assert.AreEqual(Vector3.right.ToString("F3"), (rot * Vector3.forward).ToString("F3"), "blue axis along the right face normal");
            // an edge: the average of its two faces, green along the edge
            var idx = box.faces[top].indices;
            var edge = new BrushEditState.Selection(); edge.edges.Add(BrushEditState.EdgeKey(idx[0], idx[1]));
            rot = BrushEditState.ElementRotation(box, BrushEditMode.Edge, edge);
            var along = (box.vertices[idx[1]] - box.vertices[idx[0]]).normalized;
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(rot * Vector3.up, along)), 1e-3f, "green axis along the edge");
            Assert.Greater(Vector3.Dot(rot * Vector3.forward, Vector3.up), 0.5f, "blue axis leans out of the top face");
            // a vertex: the average of its three faces
            var vertex = new BrushEditState.Selection(); vertex.vertices.Add(idx[0]);
            rot = BrushEditState.ElementRotation(box, BrushEditMode.Vertex, vertex);
            var corner = box.vertices[idx[0]].normalized;
            Assert.AreEqual(1f, Vector3.Dot(rot * Vector3.forward, corner), 1e-3f, "blue axis out of the corner");
            Assert.AreEqual(Quaternion.identity.eulerAngles.ToString("F1"), BrushEditState.ElementRotation(box, BrushEditMode.Face, new BrushEditState.Selection()).eulerAngles.ToString("F1"), "nothing selected: world");
        }

        [Test]
        public void HeightFromRayIsTheNearestPointOnTheNormalLine()
        {
            var corner = new Vector3(1f, 0f, 1f);
            var ray = new Ray(new Vector3(1f, 2f, -5f), Vector3.forward); // passes 2 above the corner
            Assert.AreEqual(2f, BrushDraw.HeightFromRay(corner, Vector3.up, ray), 1e-4f);
            var parallel = new Ray(new Vector3(0f, 0f, 0f), Vector3.up);
            Assert.AreEqual(0f, BrushDraw.HeightFromRay(corner, Vector3.up, parallel), "parallel rays give no height");
        }

        [Test]
        public void TheStartPointSnapsWithinItsPlaneAndKeepsThePlanesHeight()
        {
            // a grid moved to a height off the grid steps (0.3), snapping by 0.5
            var p = BrushDraw.SnapOnPlane(new Vector3(1.2f, 0.3f, -0.7f), Vector3.up, 0.5f);
            Assert.That(Vector3.Distance(new Vector3(1f, 0.3f, -0.5f), p), Is.LessThan(1e-5f));
            var side = BrushDraw.SnapOnPlane(new Vector3(2.1f, 0.9f, 0.4f), Vector3.left, 0.5f);
            Assert.That(Vector3.Distance(new Vector3(2.1f, 1f, 0.5f), side), Is.LessThan(1e-5f), "on a side plane X is kept");
        }
    }
}

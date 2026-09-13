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
        public void HeightFromRayIsTheNearestPointOnTheNormalLine()
        {
            var corner = new Vector3(1f, 0f, 1f);
            var ray = new Ray(new Vector3(1f, 2f, -5f), Vector3.forward); // passes 2 above the corner
            Assert.AreEqual(2f, BrushDraw.HeightFromRay(corner, Vector3.up, ray), 1e-4f);
            var parallel = new Ray(new Vector3(0f, 0f, 0f), Vector3.up);
            Assert.AreEqual(0f, BrushDraw.HeightFromRay(corner, Vector3.up, parallel), "parallel rays give no height");
        }
    }
}

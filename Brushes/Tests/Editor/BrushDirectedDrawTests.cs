using CsgBrush.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>Wedges and linear stairs drawn by their direction: low end, high end, width, height.</summary>
    public class BrushDirectedDrawTests
    {
        static readonly Quaternion Floor = BrushDraw.PlaneRotation(Vector3.up);

        static string F(Vector3 v) => v.ToString("F3");

        [Test]
        public void ARampDrawnAlongZRisesTowardsWhereTheDragEnded()
        {
            BrushDraw.DirectedPose(Vector3.zero, new Vector3(0f, 0f, 4f), 2f, false, 1f, Floor, 0.5f, 15f, out var centre, out var size, out var rotation);
            Assert.AreEqual(F(new Vector3(2f, 1f, 4f)), F(size), "width, height, length");
            Assert.AreEqual(F(new Vector3(1f, 0.5f, 2f)), F(centre), "to the right of the line, above the floor");
            Assert.Less(Quaternion.Angle(Quaternion.identity, rotation), 0.01f, "rising along +z");
            // the wedge's low edge is where the press was, its high edge where the drag ended
            var wedge = BrushPolyhedron.Wedge(size);
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            Vector3 low = Vector3.positiveInfinity, high = Vector3.negativeInfinity;
            foreach (var v in wedge.vertices) { var w = m.MultiplyPoint3x4(v); if (w.y > high.y) high = w; if (w.z < low.z) low = w; }
            Assert.AreEqual(0f, low.z, 1e-4f, "the low end at the press");
            Assert.AreEqual(4f, high.z, 1e-4f, "the high end at the release");
        }

        [Test]
        public void ARampDrawnTowardsMinusXFacesThatWay()
        {
            BrushDraw.DirectedPose(new Vector3(5f, 0f, 0f), new Vector3(2f, 0f, 0f), -1f, false, 1f, Floor, 0.5f, 15f, out _, out var size, out var rotation);
            Assert.AreEqual(F(Vector3.left), F(rotation * Vector3.forward), "its +z points along the drawn line");
            Assert.AreEqual(3f, size.z, 1e-4f);
        }

        [Test]
        public void TheDirectionSnapsToTheRotationSnapAndTheLengthToTheGrid()
        {
            // drawn at about 40 degrees: snaps to 45 with a 15 degree snap
            var end = new Vector3(Mathf.Sin(40f * Mathf.Deg2Rad), 0f, Mathf.Cos(40f * Mathf.Deg2Rad)) * 3.1f;
            BrushDraw.DirectedPose(Vector3.zero, end, 1f, false, 1f, Floor, 0.5f, 15f, out _, out var size, out var rotation);
            Assert.AreEqual(45f, rotation.eulerAngles.y, 0.01f);
            Assert.AreEqual(3f, size.z, 1e-4f, "the length on the grid");
        }

        [Test]
        public void ShiftCentresTheWidthOnTheLine()
        {
            BrushDraw.DirectedPose(Vector3.zero, new Vector3(0f, 0f, 4f), 3f, true, 1f, Floor, 0.5f, 15f, out var centre, out var size, out _);
            Assert.AreEqual(3f, size.x, 1e-4f);
            Assert.AreEqual(0f, centre.x, 1e-4f, "about the line");
        }
    }
}

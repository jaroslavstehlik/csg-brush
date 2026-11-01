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
        public void StairsDrawnTowardsMinusXClimbFromThePressToTheRelease()
        {
            BrushDraw.DirectedPose(new Vector3(5f, 0f, 0f), new Vector3(1f, 0f, 0f), 1f, false, 2f, Floor, 0.5f, 15f, out var centre, out var size, out var rotation);
            var stairs = BrushPolyhedron.Stairs(size, 0.25f);
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            // the lowest top tread lies by the press, the highest by the release
            float lowTreadX = float.NaN, highTreadX = float.NaN, lowY = float.PositiveInfinity, highY = float.NegativeInfinity;
            foreach (var v in stairs.vertices)
            {
                var w = m.MultiplyPoint3x4(v);
                if (w.y < 1e-4f) continue;
                if (w.y < lowY - 1e-4f || (Mathf.Abs(w.y - lowY) < 1e-4f && w.x > lowTreadX)) { lowY = w.y; lowTreadX = w.x; }
                if (w.y > highY + 1e-4f || (Mathf.Abs(w.y - highY) < 1e-4f && w.x < highTreadX)) { highY = w.y; highTreadX = w.x; }
            }
            Assert.AreEqual(2f, highY, 1e-4f);
            Assert.AreEqual(5f, lowTreadX, 1e-4f, "the first step starts at the press");
            Assert.AreEqual(1f, highTreadX, 1e-4f, "the top step ends at the release");
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
        public void AnArchDrawnFromFootToFootSpansTheLine()
        {
            // drawn along -x from 6 to 2, 1 m deep to the right of the line, 3 m high
            BrushDraw.SpanPose(new Vector3(6f, 0f, 0f), new Vector3(2f, 0f, 0f), 1f, false, 3f, Floor, 0.5f, 15f, out var centre, out var size, out var rotation);
            Assert.AreEqual(F(new Vector3(4f, 3f, 1f)), F(size), "span, height, depth");
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(rotation * Vector3.right, Vector3.right)), 1e-4f, "its width runs along the line");
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (var v in BrushPolyhedron.Arch(size, 1f, 180f, 8).vertices) { var w = m.MultiplyPoint3x4(v); minX = Mathf.Min(minX, w.x); maxX = Mathf.Max(maxX, w.x); }
            Assert.AreEqual(2f, minX, 1e-3f, "one foot at the release");
            Assert.AreEqual(6f, maxX, 1e-3f, "the other at the press");
            Assert.AreEqual(0.5f, centre.z, 1e-4f, "its depth to the side the pointer was on");
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

using System.Collections.Generic;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>The editable polyhedron and its live convex decomposition.</summary>
    public class BrushPolyhedronTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            BrushSettings.instance.snapToGrid = false;
        }

        static float PiecesVolume(List<ConvexPolytope> pieces) { float v = 0f; foreach (var p in pieces) v += p.Volume(); return v; }

        static void AssertValidDecomposition(BrushPolyhedron poly, int expectedPieces, out List<ConvexPolytope> pieces)
        {
            pieces = new List<ConvexPolytope>();
            var cuts = new List<Vector3[]>();
            Assert.IsTrue(ConvexDecomposition.Decompose(poly, pieces, cuts), "decomposition succeeds");
            Assert.AreEqual(expectedPieces, pieces.Count, "number of convex parts");
            Assert.AreEqual(poly.Volume(), PiecesVolume(pieces), 1e-3f, "parts fill the solid exactly (no gaps, no overlap)");
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                {
                    var overlap = ConvexPolytope.Intersect(pieces[i], pieces[j]);
                    Assert.IsTrue(overlap == null || overlap.IsEmpty || overlap.Volume() < 1e-4f, "parts do not overlap");
                }
            if (expectedPieces == 1) Assert.AreEqual(0, cuts.Count, "a convex shape has no cut faces");
            else Assert.Greater(cuts.Count, 0, "a concave shape shows where it is cut");
        }

        [Test]
        public void BoxIsClosedConvexAndHasTheRightVolume()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 1f, 4f));
            Assert.IsTrue(box.IsClosed());
            Assert.IsTrue(box.IsConvex());
            Assert.AreEqual(8f, box.Volume(), 1e-4f);
            AssertValidDecomposition(box, 1, out var pieces);
            Assert.IsTrue(pieces[0].IsAxisAlignedBox(out var b) && (b.size - new Vector3(2f, 1f, 4f)).magnitude < 1e-3f, "the single piece is the box itself");
        }

        [Test]
        public void PrimitivesAreClosedAndConvex()
        {
            foreach (var poly in new[] { BrushPolyhedron.Wedge(new Vector3(2f, 1f, 3f)), BrushPolyhedron.Prism(new Vector3(2f, 2f, 2f), 8, 1f), BrushPolyhedron.Prism(new Vector3(2f, 2f, 2f), 6, 0f) })
            {
                Assert.IsTrue(poly.IsClosed(), "closed");
                Assert.IsTrue(poly.IsConvex(), "convex");
                Assert.Greater(poly.Volume(), 0f, "outward faces");
                AssertValidDecomposition(poly, 1, out _);
            }
            Assert.AreEqual(3f, BrushPolyhedron.Wedge(new Vector3(2f, 1f, 3f)).Volume(), 1e-4f, "half a 2x1x3 box");
        }

        static BrushPolyhedron LShape()
        {
            // L-shaped profile in x/z (a 2x2 square with the +x,+z quadrant removed), 1 high
            var p = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(-1, -0.5f, -1), new Vector3(1, -0.5f, -1), new Vector3(1, -0.5f, 0), new Vector3(0, -0.5f, 0), new Vector3(0, -0.5f, 1), new Vector3(-1, -0.5f, 1),
                    new Vector3(-1,  0.5f, -1), new Vector3(1,  0.5f, -1), new Vector3(1,  0.5f, 0), new Vector3(0,  0.5f, 0), new Vector3(0,  0.5f, 1), new Vector3(-1,  0.5f, 1),
                },
                faces = new[]
                {
                    new BrushPolyhedron.Face(new[] { 0, 5, 4, 3, 2, 1 }),
                    new BrushPolyhedron.Face(new[] { 6, 7, 8, 9, 10, 11 }),
                    new BrushPolyhedron.Face(new[] { 0, 1, 7, 6 }), new BrushPolyhedron.Face(new[] { 1, 2, 8, 7 }), new BrushPolyhedron.Face(new[] { 2, 3, 9, 8 }),
                    new BrushPolyhedron.Face(new[] { 3, 4, 10, 9 }), new BrushPolyhedron.Face(new[] { 4, 5, 11, 10 }), new BrushPolyhedron.Face(new[] { 5, 0, 6, 11 }),
                }
            };
            p.EnsureOutward();
            return p;
        }

        [Test]
        public void ConcaveLShapeSplitsIntoTwoConvexParts()
        {
            var l = LShape();
            Assert.IsTrue(l.IsClosed());
            Assert.AreEqual(3f, l.Volume(), 1e-4f, "three quarters of a 2x1x2 block");
            Assert.IsFalse(l.IsConvex());
            Assert.AreEqual(1, l.ReflexEdges().Count, "one concave edge");
            AssertValidDecomposition(l, 2, out _);
        }

        [Test]
        public void MovingAVertexInwardSplitsFacesAndStaysDecomposable()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = 6; // (+x, +y, +z)
            box.MoveVertex(top, new Vector3(1f, 0.2f, 1f));
            Assert.IsTrue(box.IsClosed(), "still closed after the split");
            for (int f = 0; f < box.faces.Length; f++) Assert.Less(box.PlanarityError(f), 1e-3f, "every face planar");
            Assert.IsFalse(box.IsConvex(), "the dent makes it concave");
            var pieces = new List<ConvexPolytope>();
            Assert.IsTrue(ConvexDecomposition.Decompose(box, pieces));
            Assert.Greater(pieces.Count, 1);
            Assert.AreEqual(box.Volume(), PiecesVolume(pieces), 1e-3f);
        }

        [Test]
        public void CollapsingAnEdgeOntoAnotherWeldsIntoAWedge()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            // move the top front edge (+y, +z: vertices 6 and 7) down onto the bottom front edge (2 and 3)
            box.MoveVertices(new[] { 6, 7 }, new Vector3(0f, -2f, 0f));
            var remap = box.WeldCoincident();
            Assert.AreEqual(6, box.vertices.Length, "two vertices welded away");
            Assert.AreEqual(5, box.faces.Length, "the front face collapsed");
            Assert.IsTrue(box.IsClosed(), "still a closed solid");
            Assert.IsTrue(box.IsConvex(), "a wedge is convex");
            Assert.AreEqual(4f, box.Volume(), 1e-3f, "half the box");
            Assert.AreEqual(remap[2], remap[6], "the selection can follow the welded vertex");
            AssertValidDecomposition(box, 1, out _);
        }

        [Test]
        public void MovingAWholeFaceKeepsItPlanar()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            box.MoveVertices(new[] { 4, 5, 6, 7 }, new Vector3(0.5f, 0.5f, 0f)); // the top face, sideways and up
            Assert.AreEqual(6, box.faces.Length, "no face needed splitting");
            for (int f = 0; f < box.faces.Length; f++) Assert.Less(box.PlanarityError(f), 1e-4f);
            Assert.IsTrue(box.IsClosed());
            Assert.IsTrue(box.IsConvex(), "a sheared box is still convex");
            Assert.AreEqual(2f * 2.5f * 2f, box.Volume(), 1e-3f, "volume of a sheared box: base x height");
        }

        [Test]
        public void RotatedAndScaledFacesNeverProduceInvalidParts()
        {
            // what the Rotate and Scale tools do to a face selection, swept over angles and factors; every accepted
            // result must decompose into closed convex meshes
            int accepted = 0, refused = 0;
            foreach (var axis in new[] { Vector3.up, Vector3.right, Vector3.forward })
            for (int angle = 15; angle <= 180; angle += 15)
            {
                var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
                var top = new[] { 4, 5, 6, 7 };
                var centre = Vector3.zero; foreach (var v in top) centre += box.vertices[v]; centre /= 4f;
                var rot = Quaternion.AngleAxis(angle, axis);
                foreach (var v in top) box.vertices[v] = BrushSnap.Round(centre + rot * (box.vertices[v] - centre), 0.25f);
                foreach (var v in top) box.EnsurePlanar(v);
                box.WeldCoincident(0.06f);
                if (!box.IsSound(out _)) { refused++; continue; }
                accepted++;
                var pieces = new List<ConvexPolytope>();
                Assert.IsTrue(ConvexDecomposition.Decompose(box, pieces), "decomposes at " + angle + " about " + axis);
                foreach (var piece in pieces)
                    AssertClosed(piece, "closed manifold piece at " + angle + " about " + axis);
            }
            Assert.Greater(accepted, 0, "some rotations are valid shapes");
            Assert.Greater(refused, 0, "rotating a face through the body is refused, not accepted as garbage");
        }

        [Test]
        public void PolytopeFromManyTangentPlanesIsBounded()
        {
            // planes tangent to a sphere, like a sphere brush: the clipped polytope must sit on the sphere's centre
            var centre = new Vector3(3f, 1.5f, -2f); float radius = 2f;
            var poly = new ConvexPolytope();
            int rings = 8, segments = 16;
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int sgm = 0; sgm < segments; sgm++)
                {
                    float theta = Mathf.PI * 2f * sgm / segments;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    poly.AddPlane(n, centre + n * radius);
                }
            }
            poly.AddPlane(Vector3.up, centre + Vector3.up * radius); poly.AddPlane(Vector3.down, centre + Vector3.down * radius);
            Assert.IsTrue(poly.Build(), "built");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 10; i++) { var again = poly.Clone(); again.Build(); }
            Debug.Log("PERF sphere polytope Build: " + (sw.Elapsed.TotalMilliseconds / 10).ToString("F2") + " ms (clip " + ConvexPolytope.LastClipMs.ToString("F2") + " ms, hull " + ConvexPolytope.LastHullMs.ToString("F2") + " ms, loop " + ConvexPolytope.LastLoopMs.ToString("F2") + " ms, polys " + ConvexPolytope.LastPolys + " points " + ConvexPolytope.LastPoints + " attempts " + ConvexPolytope.LastRetries + " edgeTests " + ConvexPolytope.LastEdgeTests + " maxCap " + ConvexPolytope.LastMaxCap + "), planes " + poly.planes.Count + " vertices " + poly.vertices.Count + " faces " + poly.faces.Count);
            var bounds = poly.GetBounds();
            Assert.AreEqual(centre.ToString("F2"), bounds.center.ToString("F2"), "centred on the sphere");
            Assert.AreEqual(radius * 2f, bounds.size.x, 0.1f, "diameter x");
            Assert.AreEqual(radius * 2f, bounds.size.z, 0.1f, "diameter z");
            foreach (var v in poly.vertices) foreach (var pl in poly.planes) Assert.LessOrEqual(ConvexPolytope.Distance(pl, v), 1e-3f, "every vertex inside every plane");
            Assert.Greater(poly.Volume(), 4f / 3f * Mathf.PI * radius * radius * radius * 0.9f, "volume close to the sphere");
        }

        static ConvexPolytope TangentSphere(Vector3 centre, float radius, int rings, int segments)
        {
            var poly = new ConvexPolytope();
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int sgm = 0; sgm < segments; sgm++)
                {
                    float theta = Mathf.PI * 2f * sgm / segments;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    poly.AddPlane(n, centre + n * radius);
                }
            }
            poly.AddPlane(Vector3.up, centre + Vector3.up * radius); poly.AddPlane(Vector3.down, centre + Vector3.down * radius);
            return poly;
        }

        /// <summary>Every edge is shared by exactly two faces, running in opposite directions.</summary>
        static void AssertClosed(ConvexPolytope poly, string what)
        {
            var directed = new HashSet<(int, int)>();
            foreach (var face in poly.faces)
                for (int k = 0; k < face.Length; k++)
                {
                    var e = (face[k], face[(k + 1) % face.Length]);
                    Assert.AreNotEqual(e.Item1, e.Item2, what + ": degenerate edge");
                    Assert.IsTrue(directed.Add(e), what + ": edge " + e + " used twice in the same direction");
                }
            foreach (var e in directed) Assert.IsTrue(directed.Contains((e.Item2, e.Item1)), what + ": edge " + e + " has no twin");
            foreach (var v in poly.vertices) foreach (var pl in poly.planes) Assert.LessOrEqual(ConvexPolytope.Distance(pl, v), 2e-3f, what + ": vertex outside a plane");
        }

        [Test]
        public void ClipOfBuiltPolytopeMatchesAFullBuild()
        {
            var sphere = TangentSphere(new Vector3(3f, 1.5f, -2f), 2f, 8, 16);
            Assert.IsTrue(sphere.Build());
            var cuts = new[]
            {
                new Vector4(1f, 0f, 0f, -3f),                        // through the centre
                new Vector4(0f, 1f, 0f, -2.2f),                      // off centre
                new Vector4(0.7071f, 0.7071f, 0f, -3.5f),            // slanted
                new Vector4(0f, 0f, 1f, 3.9f),                       // shaves a sliver off the +z side (centre z=-2, radius 2)
                new Vector4(0f, 1f, 0f, -10f),                       // misses: everything inside
                new Vector4(0f, -1f, 0f, -10f),                      // removes everything
            };
            double fastMs = 0, fullMs = 0;
            for (int i = 0; i < cuts.Length; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var fast = sphere.Clip(cuts[i]);
                fastMs += sw.Elapsed.TotalMilliseconds; sw.Restart();
                var full = sphere.Clone(); full.AddPlane(cuts[i]); full.Build();
                fullMs += sw.Elapsed.TotalMilliseconds;
                Assert.AreEqual(full.IsEmpty, fast.IsEmpty, "cut " + i + " emptiness");
                if (full.IsEmpty) continue;
                Assert.AreEqual(full.Volume(), fast.Volume(), full.Volume() * 1e-3f, "cut " + i + " volume");
                Assert.AreEqual(full.GetBounds().ToString("F3"), fast.GetBounds().ToString("F3"), "cut " + i + " bounds");
                AssertClosed(fast, "cut " + i);
                foreach (var v in fast.vertices) foreach (var pl in full.planes) Assert.LessOrEqual(ConvexPolytope.Distance(pl, v), 2e-3f, "cut " + i + ": vertex outside the full build's planes");
                // a second cut on the clipped result
                var twice = fast.Clip(cuts[(i + 1) % 4]);
                var twiceFull = full.Clone(); twiceFull.AddPlane(cuts[(i + 1) % 4]); twiceFull.Build();
                Assert.AreEqual(twiceFull.Volume(), twice.Volume(), twiceFull.Volume() * 1e-3f + 1e-6f, "cut " + i + " then another: volume");
                if (!twice.IsEmpty) AssertClosed(twice, "cut " + i + " then another");
            }
            Debug.Log("PERF clip of a built 114-plane sphere: " + (fastMs / cuts.Length).ToString("F2") + " ms, full build " + (fullMs / cuts.Length).ToString("F2") + " ms");

            // subtract: the pieces plus the overlap add up to the sphere, and a box cutter keeps its box pieces boxes
            var box = new ConvexPolytope();
            box.AddPlane(Vector3.right, new Vector3(4f, 0f, 0f)); box.AddPlane(Vector3.left, new Vector3(2f, 0f, 0f));
            box.AddPlane(Vector3.up, new Vector3(0f, 2.5f, 0f)); box.AddPlane(Vector3.down, new Vector3(0f, 0.5f, 0f));
            box.AddPlane(Vector3.forward, new Vector3(0f, 0f, -1f)); box.AddPlane(Vector3.back, new Vector3(0f, 0f, -3f));
            var pieces = new List<ConvexPolytope>();
            ConvexPolytope.Subtract(sphere, box, pieces);
            float sum = 0f; foreach (var piece in pieces) { AssertClosed(piece, "subtract piece"); sum += piece.Volume(); }
            var overlap = ConvexPolytope.Intersect(sphere, box);
            Assert.AreEqual(sphere.Volume(), sum + overlap.Volume(), sphere.Volume() * 1e-3f, "pieces + overlap = sphere");
            Assert.Greater(pieces.Count, 1);
            var cube = new ConvexPolytope();
            cube.AddPlane(Vector3.right, new Vector3(1f, 0f, 0f)); cube.AddPlane(Vector3.left, new Vector3(-1f, 0f, 0f));
            cube.AddPlane(Vector3.up, new Vector3(0f, 1f, 0f)); cube.AddPlane(Vector3.down, new Vector3(0f, -1f, 0f));
            cube.AddPlane(Vector3.forward, new Vector3(0f, 0f, 1f)); cube.AddPlane(Vector3.back, new Vector3(0f, 0f, -1f));
            var half = cube.Clip(new Vector4(0f, 1f, 0f, 0f));
            Assert.IsTrue(half.IsAxisAlignedBox(out var hb), "a box cut by an axial plane is still a box");
            Assert.AreEqual("(0.000, -0.500, 0.000)", hb.center.ToString("F3"));
            Assert.AreEqual(6, half.planes.Count, "the cut plane replaced the top plane");
        }

        [Test]
        public void StairsAreClosedAndDecomposeIntoSteps([Values(0.5f, 0.4f, 0.25f)] float stepHeight)
        {
            // 1.5 m high: 3, 4 (1.5 / 0.4 rounded) or 6 steps, each rising 1.5 / steps and running 3 / steps
            var stairs = BrushPolyhedron.Stairs(new Vector3(2f, 1.5f, 3f), stepHeight);
            Assert.IsTrue(stairs.IsValid);
            Assert.IsTrue(stairs.IsClosed(), "every edge shared by exactly two faces");
            Assert.IsTrue(stairs.IsSound(out var why), why);
            Assert.Greater(stairs.Volume(), 0f);
            int steps = Mathf.RoundToInt(1.5f / stepHeight);
            Assert.AreEqual(steps, BrushPolyhedron.StepCount(1.5f, stepHeight));
            Assert.AreEqual(1.5f, stairs.Bounds().size.y, 1e-4f, "the steps fill the height");
            // volume: each step column is a box from the floor to its tread
            float rise = 1.5f / steps, run = 3f / steps, expected = 0f;
            for (int k = 0; k < steps; k++) expected += 2f * (k + 1) * rise * run;
            Assert.AreEqual(expected, stairs.Volume(), 1e-3f, "volume of " + steps + " step columns");
            var pieces = new List<ConvexPolytope>();
            ConvexDecomposition.Decompose(stairs, pieces);
            float sum = 0f; foreach (var piece in pieces) { AssertClosed(piece, "stairs piece"); sum += piece.Volume(); }
            Assert.AreEqual(expected, sum, 1e-3f, "convex parts add up to the stairs");
        }

        [Test]
        public void CurvedAndSpiralStairsAreClosedBlocks([Values(false, true)] bool spiral, [Values(false, true)] bool sloped)
        {
            var p = new StairParams { innerRadius = 0.5f, stepWidth = 1.5f, stepHeight = 0.25f, stepThickness = 0.2f, curveAngle = 90f, numSteps = 6, stepsPer360 = 8, slopedFloor = sloped, slopedCeiling = sloped, counterClockwise = spiral };
            var stairs = spiral ? BrushPolyhedron.SpiralStairs(p) : BrushPolyhedron.CurvedStairs(p);
            Assert.IsTrue(stairs.IsValid);
            Assert.IsTrue(stairs.IsClosed(), "every block closed");
            Assert.Greater(stairs.Volume(), 0f);
            Assert.GreaterOrEqual(stairs.Bounds().min.y, -1e-4f, "nothing below the floor: the origin is the axis at floor level");
            Assert.LessOrEqual(stairs.Bounds().min.x, 1e-4f); Assert.GreaterOrEqual(stairs.Bounds().max.x, -1e-4f); // the axis is inside the footprint
            var groups = new HashSet<int>(); foreach (var f in stairs.faces) groups.Add(f.group);
            Assert.AreEqual(spiral ? 6 : 12, groups.Count, spiral ? "one block per step" : "15 degree steps: two blocks each, none wider than 11.25 degrees");
            // each block's collider is the convex hull of its corners, and they add up to the shape
            float sum = 0f;
            foreach (var g in groups)
            {
                var corners = new HashSet<int>();
                for (int f = 0; f < stairs.faces.Length; f++) if (stairs.faces[f].group == g) foreach (var i in stairs.faces[f].indices) corners.Add(i);
                var points = new List<Vector3>(); foreach (var i in corners) points.Add(stairs.vertices[i]);
                var pt = BrushGeometry.ConvexHull(points);
                Assert.IsNotNull(pt, "block " + g + " builds");
                AssertClosed(pt, "block " + g);
                sum += pt.Volume();
            }
            if (sloped) Assert.GreaterOrEqual(sum, stairs.Volume() * 0.999f, "hulls contain the twisted blocks");
            if (sloped) Assert.LessOrEqual(sum, stairs.Volume() * 1.35f, "hulls of twisted ramp blocks stay close at 8 steps per turn (finer spirals twist less)");
            else Assert.AreEqual(stairs.Volume(), sum, 1e-3f, "blocks add up");
        }

        static int FaceWithNormal(BrushPolyhedron p, Vector3 n) { for (int f = 0; f < p.faces.Length; f++) if (Vector3.Dot(p.Plane(f), n) > 0.9f) return f; return -1; }

        [Test]
        public void ExtrudingOneFaceAddsABlock()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = FaceWithNormal(box, Vector3.up);
            var result = BrushBoolean.ExtrudeFaces(box, new[] { top }, 1f, true, out var remap);
            Assert.IsNotNull(result);
            Assert.AreEqual(10, result.faces.Length, "the face plus four walls");
            Assert.IsTrue(result.IsClosed(), "closed"); Assert.IsTrue(result.IsSound(out var why), why);
            Assert.AreEqual(12f, result.Volume(), 1e-3f, "2x2x2 plus a 2x1x2 block");
            Assert.AreEqual(2f, result.Bounds().max.y, 1e-4f, "the top moved up by one");
            Assert.AreEqual(top, remap[top], "the extruded face keeps its index");
            for (int f = 0; f < box.faces.Length; f++) Assert.AreEqual(f, remap[f], "untouched faces keep their indices");
            Assert.IsTrue(Vector3.Dot(result.Plane(top), Vector3.up) > 0.99f, "and it is still the top");
            int walls = 0; foreach (var f in result.faces) if (f.source == top) { walls++; Assert.AreEqual(4, f.indices.Length, "walls are quads"); }
            Assert.AreEqual(4, walls, "walls carry the extruded face as source");
        }

        [Test]
        public void ExtrudingInwardCutsAPocket()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = FaceWithNormal(box, Vector3.up);
            var result = BrushBoolean.ExtrudeFaces(box, new[] { top }, -0.5f, true, out var remap);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsClosed()); Assert.IsTrue(result.IsSound(out var why), why);
            Assert.AreEqual(6f, result.Volume(), 1e-3f, "the whole top moved down by half: a 2x1.5x2 box");
            Assert.AreEqual(0.5f, result.Bounds().max.y, 1e-4f);
            Assert.AreEqual(0.5f, result.vertices[result.faces[remap[top]].indices[0]].y, 1e-4f, "the face keeps its identity at its new height");
        }

        [Test]
        public void GroupAndIndividualExtrusionsOfAdjacentFaces()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = FaceWithNormal(box, Vector3.up), right = FaceWithNormal(box, Vector3.right);
            var group = BrushBoolean.ExtrudeFaces(box, new[] { top, right }, 1f, false, out _);
            Assert.IsTrue(group.IsClosed()); Assert.IsTrue(group.IsSound(out var why), why);
            Assert.Greater(group.Volume(), 8f);
            var single = BrushBoolean.ExtrudeFaces(box, new[] { top, right }, 1f, true, out _);
            Assert.IsTrue(single.IsClosed()); Assert.IsTrue(single.IsSound(out why), why);
            Assert.AreEqual(16f, single.Volume(), 1e-3f, "two 2x1x2 blocks on a 2x2x2 box");
        }

        [Test]
        public void ExtrudingThroughAnotherPartOfTheShapeStaysValid()
        {
            // the L's notch has two inner faces (x = 0 facing +x, z = 0 facing +z); blocks from both run through each other
            var l = LShape();
            int innerX = -1, innerZ = -1;
            for (int f = 0; f < l.faces.Length; f++) { var c = l.FaceCentre(f); var n = l.Plane(f); if (n.x > 0.9f && c.x < 0.1f) innerX = f; if (n.z > 0.9f && c.z < 0.1f) innerZ = f; }
            Assert.GreaterOrEqual(innerX, 0); Assert.GreaterOrEqual(innerZ, 0);
            var result = BrushBoolean.ExtrudeFaces(l, new[] { innerX, innerZ }, 1.5f, true, out var remap);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsClosed()); Assert.IsTrue(result.IsSound(out var why), why);
            Assert.IsFalse(result.SelfIntersects());
            Assert.AreEqual(3f + 1.5f + 1.5f - 1f, result.Volume(), 1e-3f, "two 1x1x1.5 blocks overlapping in a 1x1x1 cube, counted once");
            Assert.GreaterOrEqual(remap[innerX], 0); Assert.GreaterOrEqual(remap[innerZ], 0);
            // and a cut that goes right through the other arm and out the far side
            var cut = BrushBoolean.ExtrudeFaces(l, new[] { innerX }, -3f, true, out _);
            Assert.IsNotNull(cut);
            Assert.IsTrue(cut.IsSound(out why), why);
            Assert.AreEqual(2f, cut.Volume(), 1e-3f, "a 1x1 tunnel through the 1-thick arm");
        }

        [Test]
        public void SelfIntersectionIsDetectedExactlyAndCheaply()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            Assert.IsFalse(box.SelfIntersects());
            var stairs = BrushPolyhedron.Stairs(new Vector3(2f, 4f, 8f), 0.125f); // 32 steps, concave, many faces
            Assert.IsFalse(stairs.SelfIntersects());
            var sw = System.Diagnostics.Stopwatch.StartNew(); Assert.IsTrue(stairs.IsSound(out _)); double stairsMs = sw.Elapsed.TotalMilliseconds;
            var sphere = BrushPolyhedron.Sphere(new Vector3(4f, 4f, 4f), 5);
            sw.Restart(); Assert.IsTrue(sphere.IsSound(out _)); double sphereMs = sw.Elapsed.TotalMilliseconds;
            // fold a corner of the L through the other arm: an edge of the moved faces pierces a face
            var l = LShape();
            var bad = l.Clone(); bad.vertices[3] = new Vector3(2f, -0.5f, -2f); bad.vertices[9] = new Vector3(2f, 0.5f, -2f); // the inner corner dragged across the far arm
            Assert.IsTrue(bad.SelfIntersects(), "crossing faces are found");
            var twisted = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f)); // the top rotated half a turn: bow-tie sides
            var top = new[] { 4, 5, 6, 7 }; var tv = new Vector3[4]; for (int i = 0; i < 4; i++) tv[i] = twisted.vertices[top[i]];
            for (int i = 0; i < 4; i++) twisted.vertices[top[i]] = tv[(i + 2) % 4];
            Assert.IsFalse(twisted.FaceIsSimple(2), "a bow-tie side is not a simple polygon");
            Assert.IsFalse(twisted.IsSound(out _));
            Assert.IsFalse(bad.IsSound(out var why)); Assert.AreEqual("self-intersecting", why);
            Debug.Log("PERF IsSound: stairs " + stairs.faces.Length + " faces " + stairsMs.ToString("F1") + " ms, sphere " + sphere.faces.Length + " faces " + sphereMs.ToString("F1") + " ms");
            Assert.Less(stairsMs, 50.0, "concave check stays cheap"); Assert.Less(sphereMs, 20.0, "convex shapes skip the check");
        }

        [Test]
        public void SliverPiecesAreClosedMeshes()
        {
            // a thin wedge-like prism: the old face extraction lost edges on shapes like this
            var poly = new ConvexPolytope();
            poly.AddPlane(Vector3.down, new Vector3(0, 0, 0));
            poly.AddPlane(Vector3.up, new Vector3(0, 0.01f, 0));
            poly.AddPlane(Vector3.left, new Vector3(0, 0, 0));
            poly.AddPlane(Vector3.right, new Vector3(1, 0, 0));
            poly.AddPlane(Vector3.back, new Vector3(0, 0, 0));
            poly.AddPlane(new Vector3(0.05f, 0, 1).normalized, new Vector3(0, 0, 1));
            Assert.IsTrue(poly.Build());
            AssertClosed(poly, "sliver");
            Assert.AreEqual(6, poly.faces.Count);
        }

        static float AreaOnBox(Brush box)
        {
            var bounds = new Bounds(box.transform.position, box.size); float area = 0f;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (mf.sharedMesh == null || mf.name.StartsWith("‹[debug")) continue;
                var v = mf.sharedMesh.vertices; var t = mf.sharedMesh.triangles; var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var p0 = m.MultiplyPoint3x4(v[t[i]]); var p1 = m.MultiplyPoint3x4(v[t[i + 1]]); var p2 = m.MultiplyPoint3x4(v[t[i + 2]]);
                    var d = (p0 + p1 + p2) / 3f - bounds.center; var e = bounds.extents;
                    bool on = (Mathf.Abs(Mathf.Abs(d.x) - e.x) < 1e-3f && Mathf.Abs(d.y) <= e.y + 1e-3f && Mathf.Abs(d.z) <= e.z + 1e-3f) || (Mathf.Abs(Mathf.Abs(d.y) - e.y) < 1e-3f && Mathf.Abs(d.x) <= e.x + 1e-3f && Mathf.Abs(d.z) <= e.z + 1e-3f) || (Mathf.Abs(Mathf.Abs(d.z) - e.z) < 1e-3f && Mathf.Abs(d.x) <= e.x + 1e-3f && Mathf.Abs(d.y) <= e.y + 1e-3f);
                    if (on) area += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5f;
                }
            }
            return area;
        }

        [Test]
        public void ConcaveSubtractCutsLikeTheEquivalentBoxes()
        {
            // an L-shaped cutter as one Custom subtract must leave the wall exactly like two box subtracts would
            var wall = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity);
            var b1 = BrushApi.Create(BrushShape.Box, new Vector3(1.5f, 0f, 0.5f), new Vector3(1f, 1f, 3f), Quaternion.identity);
            var b2 = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0f, -0.5f), new Vector3(2f, 1f, 1f), Quaternion.identity);
            BrushApi.SetOperation(b1, BrushOperation.Subtract); BrushApi.SetOperation(b2, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            float withBoxes = AreaOnBox(wall);
            BrushApi.Delete(b1); BrushApi.Delete(b2); BrushApi.ForceUpdate();
            float plain = AreaOnBox(wall);
            Assert.AreEqual(2f * (4f * 2f + 4f * 4f + 2f * 4f), plain, 1e-2f, "the wall alone");
            Assert.Less(withBoxes, plain, "the boxes cut the wall");

            var l = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(1f, -0.5f, -1f), new Vector3(2f, -0.5f, -1f), new Vector3(2f, -0.5f, 2f), new Vector3(1f, -0.5f, 2f), new Vector3(1f, -0.5f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(0f, -0.5f, -1f),
                    new Vector3(1f,  0.5f, -1f), new Vector3(2f,  0.5f, -1f), new Vector3(2f,  0.5f, 2f), new Vector3(1f,  0.5f, 2f), new Vector3(1f,  0.5f, 0f), new Vector3(0f,  0.5f, 0f), new Vector3(0f,  0.5f, -1f),
                },
                faces = new[]
                {
                    new BrushPolyhedron.Face(new[] { 0, 1, 2, 3, 4, 5, 6 }), new BrushPolyhedron.Face(new[] { 13, 12, 11, 10, 9, 8, 7 }),
                    new BrushPolyhedron.Face(new[] { 0, 7, 8, 1 }), new BrushPolyhedron.Face(new[] { 1, 8, 9, 2 }), new BrushPolyhedron.Face(new[] { 2, 9, 10, 3 }), new BrushPolyhedron.Face(new[] { 3, 10, 11, 4 }),
                    new BrushPolyhedron.Face(new[] { 4, 11, 12, 5 }), new BrushPolyhedron.Face(new[] { 5, 12, 13, 6 }), new BrushPolyhedron.Face(new[] { 6, 13, 7, 0 }),
                }
            };
            l.EnsureOutward();
            Assert.IsTrue(l.IsClosed()); Assert.AreEqual(1f * 1f * 3f + 1f * 1f * 1f, l.Volume(), 1e-3f, "same volume as the two boxes");
            var cutter = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.SetPolyhedron(cutter, l);
            BrushApi.ForceUpdate(); BrushApi.ForceUpdate();
            float withCustom = AreaOnBox(wall);
            Assert.AreEqual(withBoxes, withCustom, 1e-2f, "a Custom concave subtract cuts exactly like the equivalent boxes");
        }

        // ------------------------------------------------------------------ integration

        static Bounds ColliderBounds()
        {
            Physics.SyncTransforms();
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include);
            Assert.Greater(colliders.Length, 0, "colliders generated");
            var b = colliders[0].bounds;
            foreach (var c in colliders) b.Encapsulate(c.bounds);
            return b;
        }

        [Test]
        public void CustomBoxRendersLikeTheParametricBox()
        {
            var a = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0.5f, -2f), new Vector3(2f, 1f, 3f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var before = ColliderBounds();
            Assert.IsTrue(BrushApi.ConvertToCustom(a));
            BrushApi.ForceUpdate();
            Assert.AreEqual(BrushShape.Custom, a.shape);
            foreach (Transform c in a.transform) Assert.IsFalse(Brush.IsGeneratedChildName(c.name), "no generated children");
            var after = ColliderBounds();
            Assert.AreEqual(before.center.ToString("F3"), after.center.ToString("F3"));
            Assert.AreEqual(before.size.ToString("F3"), after.size.ToString("F3"));
        }

        [Test]
        public void PushingAFaceGrowsTheBrushAndUndoShrinksItBack()
        {
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Undo.IncrementCurrentGroup();
            int top = 1; // Box face order: bottom, top, ...
            BrushApi.PushFace(a, top, 0.5f);
            BrushApi.ForceUpdate();
            var b = ColliderBounds();
            Assert.AreEqual(1.5f, b.size.y, 1e-3f, "top face pushed up by 0.5");
            Assert.AreEqual(0.25f, b.center.y, 1e-3f);
            Assert.AreEqual(new Vector3(2f, 1.5f, 2f).ToString("F3"), a.size.ToString("F3"), "size follows the vertices");
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(BrushShape.Box, a.shape, "undo returns to the parametric box");
            Assert.AreEqual(1f, ColliderBounds().size.y, 1e-3f);
        }

        [Test]
        public void ConcaveCustomBrushGetsOneConvexColliderPerPart()
        {
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.ConvertToCustom(a);
            BrushApi.SetPolyhedron(a, LShape());
            BrushApi.ForceUpdate();
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include);
            Assert.AreEqual(2, colliders.Length, "two convex parts, two colliders");
            foreach (var c in colliders) Assert.IsTrue(c is BoxCollider || (c is MeshCollider m && m.convex), "every collider convex");
            Assert.Greater(BrushCsg.LastTriangles, 12, "the L shape renders as one concave mesh");
            BrushApi.ResetShape(a);
            BrushApi.ForceUpdate();
            Assert.AreEqual(BrushShape.Box, a.shape);
            Assert.AreEqual(1, Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length);
        }

        [Test]
        public void ArchIsAClosedRingOfConvexSegments()
        {
            var size = new Vector3(4f, 2f, 1f);
            var arch = BrushPolyhedron.Arch(size, 0.5f, 180f, 16);
            Assert.IsTrue(arch.IsClosed(), "closed"); Assert.IsTrue(arch.IsSound(out var why), why);
            Assert.AreEqual(16 * 6, arch.faces.Length, "six faces per segment block");
            var b = arch.Bounds();
            Assert.AreEqual(size.x, b.size.x, 1e-3f); Assert.AreEqual(size.y, b.size.y, 1e-3f); Assert.AreEqual(size.z, b.size.z, 1e-3f);
            Assert.AreEqual(-1f, b.min.y, 1e-4f, "stands on the floor of its box, centred on the transform");
            // half an elliptical ring: outer 2 x 2, inner 1.5 x 1.5, times the depth; the polygonal ring is a little under it
            float exact = Mathf.PI * (2f * 2f - 1.5f * 1.5f) * 0.5f * size.z;
            Assert.Less(arch.Volume(), exact); Assert.Greater(arch.Volume(), exact * 0.97f);
            for (int g = 0; g < 16; g++)
            {
                var block = new List<int>(); for (int f = 0; f < arch.faces.Length; f++) if (arch.faces[f].group == g) block.Add(f);
                Assert.AreEqual(6, block.Count, "segment " + g);
            }
            var quarter = BrushPolyhedron.Arch(size, 0.5f, 90f, 8);
            Assert.IsTrue(quarter.IsSound(out why), why);
            Assert.AreEqual(2f, quarter.Bounds().max.y + 1f, 1e-3f, "a partial arch keeps the top of the ellipse");
            Assert.Less(quarter.Bounds().size.x, size.x, "and does not reach the box's sides");
        }

        [Test]
        public void ACurvedStairsFootprintDoesNotDependOnTheNumberOfSteps()
        {
            var p = new StairParams { innerRadius = 0.5f, stepWidth = 1.5f, stepHeight = 0.25f, curveAngle = 90f };
            Bounds? first = null;
            foreach (int steps in new[] { 1, 2, 3, 8 })
            {
                p.numSteps = steps;
                var stairs = BrushPolyhedron.CurvedStairs(p);
                Assert.IsTrue(stairs.IsClosed() && stairs.IsSound(out var why), steps + " steps: sound");
                var b = stairs.Bounds();
                float outer = 0f; foreach (var v in stairs.vertices) outer = Mathf.Max(outer, new Vector2(v.x, v.z).magnitude);
                Assert.AreEqual(2f, outer, 1e-4f, steps + " steps: the outer edge stays on the circle");
                // footprint: the faces on the floor; a quarter ring from 0.5 to 2 m, less a little for the straight segments
                float area = 0f;
                foreach (var f in stairs.faces)
                {
                    bool floor = true; foreach (var i in f.indices) floor &= Mathf.Abs(stairs.vertices[i].y) < 1e-5f;
                    if (!floor) continue;
                    for (int i = 0; i < f.indices.Length; i++) { var u = stairs.vertices[f.indices[i]]; var w = stairs.vertices[f.indices[(i + 1) % f.indices.Length]]; area += u.x * w.z - w.x * u.z; }
                }
                area = Mathf.Abs(area) * 0.5f;
                float ring = Mathf.PI * (2f * 2f - 0.5f * 0.5f) / 4f;
                Assert.AreEqual(ring, area, ring * 0.01f, steps + " steps: the footprint covers the curve");
                if (first == null) { first = b; continue; }
                Assert.AreEqual(first.Value.min.x, b.min.x, 1e-3f, steps + " steps"); Assert.AreEqual(first.Value.max.x, b.max.x, 1e-3f, steps + " steps");
                Assert.AreEqual(first.Value.min.z, b.min.z, 1e-3f, steps + " steps"); Assert.AreEqual(first.Value.max.z, b.max.z, 1e-3f, steps + " steps");
            }
        }

        [Test]
        public void EveryStairClimbsTheSameStepHeight([Values(0.05f, 0.1f, 0.2f)] float thickness)
        {
            // treads at 0.25, 0.5, 0.75 ... whatever the kind of stair or the spiral's tread thickness
            float h = 0.25f;
            var p = new StairParams { innerRadius = 0.3f, stepWidth = 1f, stepHeight = h, stepThickness = thickness, curveAngle = 90f, numSteps = 4, stepsPer360 = 12 };
            var tops = new Dictionary<string, List<float>>();
            void Collect(string name, BrushPolyhedron shape, float floor)
            {
                var ys = new SortedSet<float>();
                foreach (var f in shape.faces)
                {
                    float y = shape.vertices[f.indices[0]].y; bool flat = true;
                    foreach (var i in f.indices) flat &= Mathf.Abs(shape.vertices[i].y - y) < 1e-5f;
                    if (flat && shape.Plane(System.Array.IndexOf(shape.faces, f)).y > 0.9f) ys.Add(Mathf.Round((y - floor) * 1e4f) / 1e4f);
                }
                tops[name] = new List<float>(ys);
            }
            Collect("curved", BrushPolyhedron.CurvedStairs(p), 0f);
            Collect("spiral", BrushPolyhedron.SpiralStairs(p), 0f);
            Collect("linear", BrushPolyhedron.Stairs(new Vector3(1f, 4 * h, 2f), h), -2 * h);
            foreach (var kv in tops)
            {
                Assert.AreEqual(4, kv.Value.Count, kv.Key + ": four treads " + string.Join(", ", kv.Value));
                for (int k = 0; k < 4; k++) Assert.AreEqual((k + 1) * h, kv.Value[k], 1e-4f, kv.Key + " tread " + k);
            }
            Assert.GreaterOrEqual(BrushPolyhedron.SpiralStairs(p).Bounds().min.y, 0f, "nothing below the floor");
        }
    }
}

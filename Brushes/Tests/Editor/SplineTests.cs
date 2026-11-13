using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A spline sweeps its profile along its path: pieces meet without gaps, hollow ones open into each other.</summary>
    public class SplineTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static Spline Make(Spline.Profile profile, Vector2 size, bool hollow, params Vector3[] path)
        {
            var spline = new GameObject("Spline").AddComponent<Spline>();
            spline.profile = profile; spline.size = size; spline.hollow = hollow; spline.thickness = 0.05f;
            int last = -1;
            foreach (var p in path) { int id = spline.AddPoint(p); if (last >= 0) spline.AddLink(last, id); last = id; }
            Rebuild(spline);
            return spline;
        }

        static void Rebuild(Spline spline) { BrushGenerators.Update(spline, true); BrushApi.ForceUpdate(); Physics.SyncTransforms(); }

        static bool Solid(Vector3 p) => Physics.OverlapBox(p, Vector3.one * 0.01f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length > 0;

        static List<BrushGenerator.BrushSpec> Specs(Spline s) { var list = new List<BrushGenerator.BrushSpec>(); s.Describe(list); return list; }

        [Test]
        public void AStraightTubeIsOnePieceAroundItsPath()
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.5f, 0.5f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            Assert.AreEqual(1, Specs(s).Count, "one piece");
            Assert.IsTrue(Solid(new Vector3(2f, 1f, 0f)), "on the path");
            Assert.IsTrue(Solid(new Vector3(2f, 1.2f, 0f)), "inside the radius");
            Assert.IsFalse(Solid(new Vector3(2f, 1.3f, 0f)), "outside it");
            Assert.IsFalse(Solid(new Vector3(4.1f, 1f, 0f)), "and not past the end");
        }

        [Test]
        public void AHollowTubeIsOpenOrCappedAtItsEnds([Values(Spline.Ends.Open, Spline.Ends.Closed)] Spline.Ends ends)
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.5f, 0.5f), true, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            s.ends = ends; Rebuild(s);
            Assert.IsTrue(Solid(new Vector3(2f, 1.225f, 0f)), "its wall");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 0f)), "hollow inside");
            Assert.AreEqual(ends == Spline.Ends.Closed, Solid(new Vector3(0.02f, 1f, 0f)), "the end: capped when closed");
        }

        [Test]
        public void ASmoothBendIsSolidAllAlongAndEveryPieceIsConvex([Values(Spline.Orientation.Follow, Spline.Orientation.Upright)] Spline.Orientation orientation)
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(0.6f, 0.3f), false, new Vector3(0f, 0.5f, 0f), new Vector3(3f, 0.5f, 0f), new Vector3(3f, 1.5f, 3f));
            s.orientation = orientation;
            s.SetSmooth(s.pointIds[1], true);
            Rebuild(s);
            var specs = Specs(s);
            Assert.Greater(specs.Count, 4, "the bend is cut into pieces");
            foreach (var spec in specs) Assert.IsTrue(spec.polyhedron.IsConvex(), spec.name + " is convex: one collider");
            for (int link = 0; link < s.links.Count; link++)
            {
                float len = s.CurveLength(link);
                for (float d = 0.05f; d < len - 0.05f; d += 0.1f)
                {
                    s.CurvePoint(link, d, out var p, out _);
                    Assert.IsTrue(Solid(s.transform.TransformPoint(p)), "solid on the path at " + d.ToString("F2") + " of link " + link);
                }
            }
        }

        [Test]
        public void HollowBranchesOpenIntoEachOther([Values(false, true)] bool smoothJunction)
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.6f, 0.6f), true, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            int mid = s.SplitLink(0, new Vector3(2f, 1f, 0f));
            int branch = s.AddPoint(new Vector3(2f, 1f, 3f));
            s.AddLink(mid, branch);
            s.SetSmooth(mid, smoothJunction);
            Rebuild(s);
            // smooth: the main pipe runs straight through and the branch is a run of its own; a corner breaks all three
            Assert.AreEqual(smoothJunction ? 2 : 3, s.Runs().Count);
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 0f)), "open where they meet");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 0.27f)), "the branch cuts through the main pipe's wall");
            Assert.IsTrue(Solid(new Vector3(2f, 1f, -0.27f)), "the far wall stays");
            Assert.IsFalse(Solid(new Vector3(2f, 1f, 1f)), "the branch is hollow");
            Assert.IsTrue(Solid(new Vector3(2f, 1.24f, 1f)), "with a wall"); // six sides at this tolerance: a flat on top at 0.26 m, the inside's at 0.22 m
            Assert.IsTrue(Solid(new Vector3(1f, 1.24f, 0f)) && Solid(new Vector3(3f, 1.24f, 0f)), "the main pipe's top on both sides");
        }

        [Test]
        public void AHollowCornerHasAPassageAndAClosedOutside()
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(0.6f, 0.6f), true, new Vector3(0f, 1f, 0f), new Vector3(3f, 1f, 0f), new Vector3(3f, 1f, 3f));
            Assert.IsFalse(Solid(new Vector3(3f, 1f, 0f)), "open at the corner");
            Assert.IsFalse(Solid(new Vector3(2.8f, 1f, 0f)) || Solid(new Vector3(3f, 1f, 0.2f)), "the passage goes round it");
            Assert.IsTrue(Solid(new Vector3(3.27f, 1f, -0.27f)), "the outer corner is filled");
            Assert.IsFalse(Solid(new Vector3(3.4f, 1f, -0.4f)), "and no more");
        }

        [Test]
        public void UprightKeepsTheProfilesTopUpOnARamp()
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(1f, 0.2f), false, new Vector3(0f, 0f, 0f), new Vector3(4f, 2f, 0f));
            s.orientation = Spline.Orientation.Upright; Rebuild(s);
            var mid = new Vector3(2f, 1f, 0f);
            Assert.IsTrue(Solid(mid + Vector3.forward * 0.45f) && Solid(mid + Vector3.back * 0.45f), "its width lies level, across the ramp");
            Assert.IsFalse(Solid(mid + Vector3.up * 0.3f), "its height is the thin side");
        }

        [Test]
        public void AClosedLoopIsOneRunWithoutEnds()
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.4f, 0.4f), true, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f), new Vector3(4f, 1f, 4f), new Vector3(0f, 1f, 4f));
            s.AddLink(s.pointIds[3], s.pointIds[0]);
            foreach (var id in s.pointIds) s.SetSmooth(id, true);
            Rebuild(s);
            var runs = s.Runs();
            Assert.AreEqual(1, runs.Count); Assert.IsTrue(runs[0].closed);
            foreach (var spec in Specs(s)) Assert.IsTrue(spec.polyhedron.IsConvex(), spec.name);
            for (int link = 0; link < s.links.Count; link++)
            {
                s.CurvePoint(link, s.CurveLength(link) * 0.5f, out var p, out var dir);
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                Assert.IsFalse(Solid(p), "hollow all round");
                Assert.IsTrue(Solid(p + Vector3.up * 0.175f), "its wall all round");
            }
        }

        [Test]
        public void ACirclesSidesFollowTheTolerance()
        {
            var s = new GameObject("Spline").AddComponent<Spline>();
            s.overrideTolerance = true; s.tolerance = 0.1f;
            s.size = new Vector2(0.5f, 0.5f); Assert.AreEqual(6, s.CircleSides, "a thin pipe: the fewest");
            s.size = new Vector2(6f, 6f); Assert.AreEqual(13, s.CircleSides, "a tunnel: within 0.1 m of round");
            s.tolerance = 0.3f; Assert.Less(s.CircleSides, 13, "rougher");
            s.sides = 20; Assert.AreEqual(20, s.CircleSides, "typed");
        }

        // ------------------------------------------------------------------ objects on splines

        static void Near(Vector3 expected, Vector3 actual, string what, float tol = 2e-3f) => Assert.That((expected - actual).magnitude, Is.LessThan(tol), what + ": expected " + expected.ToString("F3") + " was " + actual.ToString("F3"));

        static GameObject Prop(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.localScale = Vector3.one * 0.1f; go.transform.position = at;
            return go;
        }

        /// <summary>Where an anchor should be, from the spline's own pose at its place.</summary>
        static void Expected(Spline s, SplineAnchor a, out Vector3 position, out Quaternion rotation)
        {
            Assert.IsTrue(SplineAnchors.Resolve(s, a, out int link, out float along, out _), "its link is found");
            Assert.IsTrue(s.PoseAt(link, along, out var pose));
            SplineAnchors.Place(s, pose, a.angle, a.offset, a.rotation, a.upright, out position, out rotation);
        }

        [Test]
        public void AnObjectStandsOnThePipeAndFollowsItsPath()
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.6f, 0.6f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            s.sides = 16; Rebuild(s);
            var prop = Prop(new Vector3(1.5f, 1.6f, 0.02f));
            var a = SplineAnchors.Attach(prop);
            Rebuild(s);
            Assert.IsTrue(a.onPath); Assert.AreEqual(s.transform, prop.transform.parent);
            Assert.That(a.angle, Is.EqualTo(0f).Within(5f), "on top");
            Assert.That(a.distance, Is.EqualTo(1.5f).Within(1e-3f));
            Near(new Vector3(1.5f, 1f + s.SurfacePoint(a.angle).magnitude, 0f), prop.transform.position, "standing on the surface", 0.05f);
            Near(Vector3.up, prop.transform.up, "upright, out of the surface (put 2 cm off the top: 2 degrees round)", 0.05f); Near(Vector3.right, prop.transform.forward, "facing along the path");

            s.points[1] = new Vector3(4f, 3f, 0f); Rebuild(s); // the pipe becomes a ramp
            Expected(s, a, out var p, out var r);
            Near(p, prop.transform.position, "it rides up with the pipe");
            Assert.That(Quaternion.Angle(r, prop.transform.rotation), Is.LessThan(0.1f));
            Assert.Greater(Vector3.Dot(prop.transform.up, Vector3.up), 0.8f, "still on top");

            int mid = s.SplitLink(0, new Vector3(2f, 2f, 0f));
            s.points[s.PointIndex(mid)] = new Vector3(2f, 2f, 1.5f); s.SetSmooth(mid, true); Rebuild(s); // split and bent
            Assert.IsTrue(a.onPath, "still on its link, split and bent");
            Expected(s, a, out p, out _);
            Near(p, prop.transform.position, "on the bent pipe");
        }

        [Test]
        public void MovedByHandItTakesItsPlaceAroundThePipeOrComesOff()
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.6f, 0.6f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            s.sides = 16; Rebuild(s);
            var prop = Prop(new Vector3(2f, 1.3f, 0f));
            var a = SplineAnchors.Attach(prop); Rebuild(s);
            prop.transform.position = new Vector3(3f, 1f, -0.45f); prop.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            SplineAnchors.Moved(a); Rebuild(s);
            Assert.IsTrue(a.onPath);
            Assert.That(a.angle, Is.EqualTo(90f).Within(1f), "on the right side (looking along the path)");
            Assert.That(a.distance, Is.EqualTo(3f).Within(1e-3f));
            Assert.That(a.offset, Is.EqualTo(0.45f - s.SurfacePoint(90f).magnitude).Within(1e-3f), "out from the surface as far as it was put");
            Near(new Vector3(3f, 1f, -0.45f), prop.transform.position, "it stays where it was put");
            prop.transform.position = new Vector3(3f, 1f, -2f);
            SplineAnchors.Moved(a); Rebuild(s);
            Assert.IsFalse(a.onPath, "pulled away: free");
            Near(new Vector3(3f, 1f, -2f), prop.transform.position, "and it stays");
        }

        [Test]
        public void RepeatPlacesACopyEveryFewMetresAlongEveryRun()
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(0.6f, 0.2f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            var post = BrushApi.Create(BrushShape.Box, new Vector3(0f, -10f, 0f), new Vector3(0.1f, 0.5f, 0.1f), Quaternion.identity);
            post.gameObject.SetActive(false); // the source stays out of the level
            var rep = s.gameObject.AddComponent<SplineRepeat>();
            rep.source = post.gameObject; rep.spacing = 0.7f; rep.start = 0.3f; rep.offset = 0.25f;
            Rebuild(s);
            Assert.IsNotNull(rep.container);
            Assert.AreEqual(6, rep.container.childCount, "0.3, 1.0, 1.7, 2.4, 3.1, 3.8 along 4 m");
            for (int i = 0; i < rep.container.childCount; i++)
            {
                var c = rep.container.GetChild(i);
                Near(new Vector3(0.3f + 0.7f * i, 1.1f + 0.25f, 0f), c.position, "copy " + i + " stands on the top at its place");
                Assert.IsTrue(c.gameObject.activeSelf);
                Assert.IsTrue(c.GetComponent<Brush>().anchored, "placed by the spline, not snapped");
            }
            BrushApi.ForceUpdate();
            Near(new Vector3(1f, 1.35f, 0f), rep.container.GetChild(1).position, "not moved to the grid");

            rep.angle = 90f; rep.offset = 0f; rep.upright = true; Rebuild(s); // on the right edge, standing up
            Near(Vector3.up, rep.container.GetChild(0).up, "upright on the edge");
            Assert.That(Vector3.Dot(rep.container.GetChild(0).position - new Vector3(0.3f, 1f, 0f), Vector3.back), Is.EqualTo(0.3f).Within(1e-3f), "on the right edge (right is -z looking along +x)");
            rep.upright = false; Rebuild(s);
            Near(Vector3.back, rep.container.GetChild(0).up, "out of the edge's face");
            rep.angle = 0f; rep.offset = 0.25f;
            rep.spacing = 2f; Rebuild(s);
            Assert.AreEqual(2, rep.container.childCount, "0.3 and 2.3");
            int mid = s.SplitLink(0, new Vector3(2f, 1f, 0f)); int end = s.AddPoint(new Vector3(2f, 1f, 3f)); s.AddLink(mid, end);
            Rebuild(s);
            Assert.AreEqual(4, rep.container.childCount, "per run: the runs of 2, 2 and 3 m get a copy at 0.3, the 3 m one also at 2.3");
        }

        [Test]
        public void DrawingOnALinkSplitsItAndBranchesFromThere()
        {
            var s = Make(Spline.Profile.Circle, new Vector2(0.4f, 0.4f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            int at = SplineDrawTool.AddAt(s, -1, 0, new Vector3(2f, 1f, 0f));
            int end = s.AddPoint(new Vector3(2f, 3f, 0f)); s.AddLink(at, end);
            Rebuild(s);
            Assert.AreEqual(3, s.links.Count, "split in two, and the branch");
            Assert.IsTrue(Solid(new Vector3(2f, 2f, 0f)), "the branch goes up");
        }
    }
}

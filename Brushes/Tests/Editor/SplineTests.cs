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

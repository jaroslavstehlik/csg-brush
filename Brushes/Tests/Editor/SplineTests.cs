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

        /// <summary>The convex blocks a brush's faces are grouped into: the pieces it collides as.</summary>
        static int Blocks(BrushPolyhedron poly) { var set = new HashSet<int>(); foreach (var f in poly.faces) set.Add(f.group); return set.Count; }

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
        public void ASmoothBendIsOneBrushSolidAllAlongThatCollidesAsConvexPieces([Values(Spline.Orientation.Follow, Spline.Orientation.Upright)] Spline.Orientation orientation)
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(0.6f, 0.3f), false, new Vector3(0f, 0.5f, 0f), new Vector3(3f, 0.5f, 0f), new Vector3(3f, 1.5f, 3f));
            s.orientation = orientation;
            s.SetSmooth(s.pointIds[1], true);
            Rebuild(s);
            var specs = Specs(s);
            Assert.AreEqual(1, specs.Count, "the whole run is one brush");
            Assert.IsTrue(specs[0].polyhedron.IsSound(out var why), why);
            Assert.Greater(Blocks(specs[0].polyhedron), 4, "the bend collides as one convex piece per straight piece of it");
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
            // the runs' square ends: the passage square to the inner corner, a wall of the thickness round it
            Assert.IsFalse(Solid(new Vector3(3.2f, 1f, -0.2f)), "the passage fills the corner");
            Assert.IsTrue(Solid(new Vector3(3.28f, 1f, -0.28f)), "the outer corner is walled, square");
            Assert.IsFalse(Solid(new Vector3(3.35f, 1f, -0.35f)), "and nothing past it");
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
            var specs = Specs(s);
            Assert.AreEqual(2, specs.Count, "one brush round the loop, one inside cut");
            foreach (var spec in specs) Assert.IsTrue(spec.polyhedron.IsSound(out var why), spec.name + ": " + why);
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

        /// <summary>A short piece of a wide profile between corners that turn up or down as well as sideways: its mitres cross.</summary>
        [Test]
        public void ALoweredCornerKeepsEveryPieceAndStaysSolidAllAlong([Values(false, true)] bool channel, [Values(Spline.Orientation.Upright, Spline.Orientation.Follow)] Spline.Orientation orientation, [Values(1f, 3f)] float drop)
        {
            var s = Make(Spline.Profile.Rectangle, new Vector2(1.2f, channel ? 0.6f : 0.3f), false, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f - drop, 0.5f), new Vector3(6f, 1f, -2f), new Vector3(3f, 1f, -5f), new Vector3(-1f, 1f, -3f));
            s.AddLink(s.pointIds[4], s.pointIds[0]);
            s.half = channel; s.hollow = channel; s.orientation = orientation; s.thickness = 0.1f; Rebuild(s);
            Specs(s);
            Assert.AreEqual(0, s.droppedPieces, "no piece left out");
            // a channel (a hollow half rectangle 0.3 m deep) is solid at its floor, 0.2 to 0.3 below the path: every piece a true slice of the profile
            var band = channel ? new[] { -0.25f } : new[] { 0f };
            for (int link = 0; link < s.links.Count; link++)
            {
                float len = s.CurveLength(link);
                for (float d = 0.3f; d < len - 0.3f; d += 0.25f)
                {
                    Assert.IsTrue(s.PoseAt(link, d, out var pose));
                    bool solid = false;
                    foreach (var y in band) solid |= Solid(s.transform.TransformPoint(pose.position + pose.up * y));
                    Assert.IsTrue(solid, "solid on link " + link + " at " + d.ToString("F2"));
                }
            }
        }

        [Test]
        public void RollTurnsTheProfileAboutThePath()
        {
            // a channel 0.6 m wide and deep, its rim on the path, walls 0.05 m
            var s = Make(Spline.Profile.Rectangle, new Vector2(0.6f, 1.2f), true, new Vector3(0f, 1f, 0f), new Vector3(4f, 1f, 0f));
            s.half = true; s.thickness = 0.05f; Rebuild(s);
            var mid = new Vector3(2f, 1f, 0f);
            Assert.IsFalse(Solid(mid + Vector3.down * 0.3f), "a channel is open on top");
            Assert.IsTrue(Solid(mid + Vector3.down * 0.575f), "its bottom");
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(s, "Roll");
            s.SetRoll(s.pointIds[0], 90f); s.SetRoll(s.pointIds[1], 90f); Rebuild(s);
            Assert.IsFalse(Solid(mid + Vector3.down * 0.575f), "turned a quarter about the path: its bottom is not underneath");
            Assert.IsTrue(Solid(mid + Vector3.forward * 0.575f) != Solid(mid + Vector3.back * 0.575f), "but to one side");
            s.SetRoll(s.pointIds[1], 0f); Rebuild(s);
            Assert.That(s.Roll(s.pointIds[0]), Is.EqualTo(90f)); Assert.AreEqual(1, s.rolls.Count, "0 removes a point's roll");
            Assert.IsFalse(Solid(new Vector3(0.3f, 1f, 0f) + Vector3.down * 0.575f), "near the rolled point: turned");
            Assert.IsTrue(Solid(new Vector3(3.7f, 1f, 0f) + Vector3.down * 0.575f), "near the other: its bottom underneath again");
            Undo.PerformUndo(); Rebuild(s);
            Assert.AreEqual(0, s.rolls.Count, "undo takes the rolls back");
            s.RemovePointAt(1); Assert.AreEqual(0, s.rolls.FindAll(r => r.id == 2).Count);
        }

        // a channel 2 m wide and 0.6 m deep, walls 0.1 m, around the given path: its floor from y 0.7 to 0.8, its sides up
        // to 1.3 (a hollow half rectangle has its rim on its path: 0.3 above the given one)
        static Spline Channel(params Vector3[] path)
        {
            var rim = new Vector3[path.Length];
            for (int i = 0; i < path.Length; i++) rim[i] = path[i] + Vector3.up * 0.3f;
            var s = Make(Spline.Profile.Rectangle, new Vector2(2f, 1.2f), true, rim);
            s.half = true; s.thickness = 0.1f; s.orientation = Spline.Orientation.Upright; Rebuild(s);
            return s;
        }

        [Test]
        public void ChannelsCrossingAtATOpenIntoEachOther([Values(false, true)] bool smoothJunction)
        {
            var s = Channel(new Vector3(-5f, 1f, 0f), new Vector3(5f, 1f, 0f));
            int mid = s.SplitLink(0, new Vector3(0f, 1.3f, 0f));
            int branch = s.AddPoint(new Vector3(0f, 1.3f, -5f)); s.AddLink(mid, branch);
            s.SetSmooth(mid, smoothJunction); Rebuild(s);
            Assert.IsTrue(Solid(new Vector3(0f, 0.75f, 0f)) && Solid(new Vector3(0f, 0.75f, -0.8f)), "the floor runs on under the junction");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, 0f)), "open where they meet");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, -0.95f)), "the branch opens the main channel's near wall");
            Assert.IsTrue(Solid(new Vector3(0f, 1f, 0.95f)), "its far wall stays");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, 1.1f)), "and nothing of the branch sticks out beyond it");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, -2f)), "the branch is a channel");
            Assert.IsTrue(Solid(new Vector3(0.95f, 1f, -2f)) && Solid(new Vector3(-0.95f, 1f, -2f)), "with its walls");
            Specs(s); Assert.AreEqual(0, s.unmitredPieces, "every piece mitred");
        }

        [Test]
        public void AChannelTurnsACornerCleanly()
        {
            var s = Channel(new Vector3(-5f, 1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 5f));
            Assert.IsFalse(Solid(new Vector3(0f, 1f, 0f)) || Solid(new Vector3(-0.5f, 1f, 0.5f)) || Solid(new Vector3(0.5f, 1f, -0.5f)), "the channel goes round the corner");
            Assert.IsTrue(Solid(new Vector3(0.95f, 1f, -0.95f)), "the outer corner is square: the two runs' square ends");
            Assert.IsFalse(Solid(new Vector3(1.1f, 1f, 0f)) || Solid(new Vector3(0f, 1f, -1.1f)) || Solid(new Vector3(1.1f, 1f, -1.1f)), "with nothing past it");
            Assert.IsTrue(Solid(new Vector3(-0.95f, 1f, 0.95f)), "and the inner corner");
        }

        [Test]
        public void AChannelIsOpenOrWalledAtItsEnds([Values(Spline.Ends.Open, Spline.Ends.Closed)] Spline.Ends ends)
        {
            var s = Channel(new Vector3(0f, 1f, 0f), new Vector3(5f, 1f, 0f));
            s.ends = ends; Rebuild(s);
            Assert.IsTrue(Solid(new Vector3(0.05f, 0.75f, 0f)), "its floor reaches the end");
            Assert.AreEqual(ends == Spline.Ends.Closed, Solid(new Vector3(0.05f, 1f, 0f)), "walled across when closed");
            Assert.IsFalse(Solid(new Vector3(2.5f, 1.2f, 0f)), "open on top");
        }

        [Test]
        public void ChannelsCrossingAtUnevenAnglesMeetWithoutStubs()
        {
            var s = Channel(new Vector3(0f, 1f, 0f), new Vector3(8f, 1f, 1.5f));
            int hub = s.pointIds[0];
            var ends = new[] { new Vector3(-2f, 1.3f, 8f), new Vector3(-8f, 1.3f, -1f), new Vector3(2.5f, 1.3f, -8f) };
            foreach (var e in ends) s.AddLink(hub, s.AddPoint(e));
            Rebuild(s);
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            var node = new Vector3(0f, 1f, 0f);
            Assert.IsTrue(Solid(new Vector3(0f, 0.75f, 0f)), "a floor under the crossing");
            Assert.IsFalse(Solid(node), "open in the middle");
            // between each two neighbouring arms: open up to where their insides meet, then a wall (the square ends' corners may reach further)
            var dirs = new List<Vector3> { new Vector3(8f, 0f, 1.5f).normalized };
            foreach (var e in ends) dirs.Add(new Vector3(e.x, 0f, e.z).normalized);
            dirs.Sort((a, b) => Mathf.Atan2(a.z, a.x).CompareTo(Mathf.Atan2(b.z, b.x)));
            for (int i = 0; i < dirs.Count; i++)
            {
                Vector3 a = dirs[i], b = dirs[(i + 1) % dirs.Count];
                var between = (a + b).normalized; float half = Vector3.Angle(a, b) * 0.5f * Mathf.Deg2Rad;
                float inside = 0.9f / Mathf.Sin(half), outside = 1f / Mathf.Sin(half);
                Assert.IsFalse(Solid(node + between * (inside - 0.05f)), "open into the corner between arms " + i);
                Assert.IsTrue(Solid(node + between * (inside + outside) * 0.5f), "the wall at that corner");
            }
            for (float x = -4f; x <= 4f; x += 0.1f)
                for (float z = -4f; z <= 4f; z += 0.1f)
                    Assert.IsFalse(Solid(new Vector3(x, 1.35f, z)), "nothing stands above the rim, at " + new Vector3(x, 1.35f, z));
        }

        [Test]
        public void AJunctionOfArmsAtDifferentHeightsHasAFloorAllThrough()
        {
            var s = Channel(new Vector3(0f, 1f, 0f), new Vector3(-7f, 1.6f, -1f));
            int hub = s.pointIds[0];
            var ends = new[] { new Vector3(7f, 2.5f, 1f), new Vector3(1f, 0.7f, -7f) };
            foreach (var e in ends) s.AddLink(hub, s.AddPoint(e));
            Rebuild(s);
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            // walk from each arm through the point into each other one, down the middle: the floor rises and falls, and no wall
            // stands in the way; where one arm's square end stops and another's floor takes over there is a step (the arms
            // climb at different slopes: here up to about 16 cm), never a wall
            var arms = new[] { new Vector3(-7f, 0f, -1f).normalized, new Vector3(7f, 0f, 1f).normalized, new Vector3(1f, 0f, -7f).normalized };
            for (int i = 0; i < arms.Length; i++)
                for (int j = i + 1; j < arms.Length; j++)
                {
                    Vector3 from = arms[i] * 3f, to = arms[j] * 3f;
                    float last = float.NaN;
                    for (float t = 0f; t <= 1f; t += 0.01f)
                    {
                        var at = t < 0.5f ? Vector3.Lerp(from, Vector3.zero, t * 2f) : Vector3.Lerp(Vector3.zero, to, t * 2f - 1f); // in along one arm, out along the other
                        Assert.IsTrue(Physics.Raycast(at + Vector3.up * 10f, Vector3.down, out var hit, 20f), "a floor between arms " + i + " and " + j + " at " + at);
                        if (!float.IsNaN(last)) Assert.Less(Mathf.Abs(hit.point.y - last), 0.2f, "no wall, a step at most, between arms " + i + " and " + j + " at " + at);
                        last = hit.point.y;
                    }
                }
        }

        /// <summary>
        /// Random channel networks (points on a jittered grid at random heights, a random tree of links plus a few loops, a
        /// third of the points Smooth): along the middle of every link, the first surface under the path is its floor.
        /// </summary>
        [Test]
        public void RandomChannelNetworksHaveTheirFloorAllAlong([Values(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)] int seed)
        {
            BrushSettings.instance.snapToGrid = false;
            var rnd = new System.Random(seed);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var s = new GameObject("Spline").AddComponent<Spline>();
            s.profile = Spline.Profile.Rectangle; s.half = true; s.hollow = true; s.size = new Vector2(3f, 1.2f); s.thickness = 0.15f; // 0.6 m deep
            s.orientation = rnd.Next(4) == 0 ? Spline.Orientation.Follow : Spline.Orientation.Upright;
            const int Cols = 3, Rows = 3;
            var grid = new int[Cols * Rows];
            for (int gx = 0; gx < Cols; gx++)
                for (int gz = 0; gz < Rows; gz++)
                    grid[gx * Rows + gz] = s.AddPoint(new Vector3(gx * 9f + R(-2.5f, 2.5f), R(0f, 2f), gz * 9f + R(-2.5f, 2.5f)));
            var linked = new HashSet<long>();
            void Link(int a, int b) { long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; if (a != b && linked.Add(k)) s.AddLink(a, b); }
            var edges = new List<(int, int)>();
            for (int gx = 0; gx < Cols; gx++) for (int gz = 0; gz < Rows; gz++) { int i = gx * Rows + gz; if (gx + 1 < Cols) edges.Add((i, i + Rows)); if (gz + 1 < Rows) edges.Add((i, i + 1)); }
            var inTree = new HashSet<int> { 0 };
            while (inTree.Count < Cols * Rows)
            {
                var (a, b) = edges[rnd.Next(edges.Count)];
                if (inTree.Contains(a) != inTree.Contains(b)) { Link(grid[a], grid[b]); inTree.Add(a); inTree.Add(b); }
            }
            for (int k = 0; k < 2; k++) { var (a, b) = edges[rnd.Next(edges.Count)]; Link(grid[a], grid[b]); }
            foreach (var id in grid) if (rnd.Next(3) == 0) s.SetSmooth(id, true);
            Rebuild(s);
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            for (int link = 0; link < s.links.Count; link++)
            {
                float len = s.CurveLength(link);
                for (float d = 0.1f; d < len - 0.1f; d += 0.1f)
                {
                    s.CurvePoint(link, d, out var p, out _);
                    Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out var hit, 1.5f), "a floor on link " + link + " at " + d.ToString("F1"));
                    float below = p.y - hit.point.y; // the floor's top is 0.45 under the path (the rim), lower on a slope or bank
                    Assert.IsTrue(below > 0.35f && below < 0.75f, "the floor, not a wall or a hole, on link " + link + " at " + d.ToString("F1") + ": " + below.ToString("F2") + " m under the path, on " + hit.collider.name);
                }
            }
        }

        /// <summary>
        /// A network drawn in the editor that broke junctions: a wide, low channel following the path, with rolls, one point
        /// 6.75 m below the rest, junctions where two links meet at 40 degrees and a third runs almost straight on.
        /// </summary>
        [Test]
        public void ADrawnNetworkWithRollsAndADeepJunctionHasItsFloorAllAlong()
        {
            BrushSettings.instance.snapToGrid = false;
            var s = new GameObject("Spline").AddComponent<Spline>();
            s.profile = Spline.Profile.Rectangle; s.half = true; s.hollow = true; s.size = new Vector2(3.88f, 1f); s.thickness = 0.249f; s.orientation = Spline.Orientation.Follow; // 0.5 m deep
            var pts = new[] { new Vector3(0f, 0f, 0f), new Vector3(-5.25f, 0f, -6.5f), new Vector3(5.25f, -1f, -20.5f), new Vector3(28.25f, 0f, -18f), new Vector3(26.75f, 0f, -11f), new Vector3(12.5f, 0f, 3f), new Vector3(22f, 0f, 1.5f),
                new Vector3(-2.25f, 1.5f, -14.25f), new Vector3(6.5f, 0.25f, -12.75f), new Vector3(16.5f, -6.75f, -18.75f), new Vector3(11f, 0f, -8.25f), new Vector3(4.05f, 0f, 0.97f), new Vector3(2f, -1.5f, -18.75f) };
            var id = new int[14];
            for (int i = 0; i < pts.Length; i++) id[i + 1] = s.AddPoint(pts[i]);
            foreach (var (a, b) in new[] { (1, 2), (2, 8), (8, 13), (13, 3), (3, 10), (10, 4), (4, 5), (5, 7), (7, 6), (6, 12), (12, 1), (7, 11), (11, 9), (9, 8), (9, 10), (11, 12) }) s.AddLink(id[a], id[b]);
            s.SetRoll(id[10], 13.6f); s.SetRoll(id[13], 10.4f); s.SetRoll(id[3], -4.6f);
            Rebuild(s);
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            for (int link = 0; link < s.links.Count; link++)
            {
                float len = s.CurveLength(link);
                for (float d = 0.25f; d < len - 0.1f; d += 0.25f)
                {
                    s.CurvePoint(link, d, out var p, out _);
                    Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out var hit, 1.5f), "a floor on link " + link + " at " + d.ToString("F2"));
                    // the floor's top is 0.251 under the path (the rim); lower on a bank, or where a lower channel overlaps this
                    // one near a junction (the insides add up: the lower floor shows, here up to about 1 m down), never a hole
                    float below = p.y - hit.point.y;
                    Assert.IsTrue(below > 0.19f && below < 1.25f, "the floor, not a wall or a lump, on link " + link + " at " + d.ToString("F2") + ": " + below.ToString("F2") + " m under the path, on " + hit.collider.name);
                }
            }
        }

        static Spline ThreeWay(Spline.Profile profile, bool hollow, bool half, Vector2 size, float thickness, params Vector3[] ends)
        {
            var s = new GameObject("Spline").AddComponent<Spline>();
            s.profile = profile; s.hollow = hollow; s.half = half; s.size = size; s.thickness = thickness; s.orientation = Spline.Orientation.Upright;
            int c = s.AddPoint(new Vector3(0f, 1f, 0f));
            foreach (var e in ends) s.AddLink(c, s.AddPoint(e));
            Rebuild(s);
            return s;
        }

        [Test]
        public void HalfPipesMeetingAtAJunctionOpenIntoEachOther()
        {
            var s = ThreeWay(Spline.Profile.Circle, true, true, new Vector2(2f, 2f), 0.2f, new Vector3(4.75f, 1f, -12.5f), new Vector3(-9.5f, 1f, 1.5f), new Vector3(-11f, 1f, -9.75f));
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            Assert.IsTrue(Solid(new Vector3(0f, 0.1f, 0f)), "a floor under the junction");
            Assert.IsFalse(Solid(new Vector3(0f, 0.6f, 0f)) || Solid(new Vector3(0f, 1.2f, 0f)), "open in the middle and on top");
            foreach (var e in new[] { new Vector3(4.75f, 0f, -12.5f), new Vector3(-9.5f, 0f, 1.5f), new Vector3(-11f, 0f, -9.75f) })
            {
                var along = new Vector3(0f, 1f, 0f) + e.normalized * 1.5f;
                Assert.IsFalse(Solid(along + Vector3.down * 0.5f), "each half pipe open into the junction, toward " + e);
            }
        }

        [Test]
        public void HollowRectanglesMeetingAtAJunctionAreOpenInsideAndRoofed()
        {
            var s = ThreeWay(Spline.Profile.Rectangle, true, false, new Vector2(2f, 1f), 0.1f, new Vector3(6f, 1f, 0f), new Vector3(-6f, 1f, 0f), new Vector3(0f, 1f, -6f));
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, 0f)) || Solid(new Vector3(0f, 1f, -0.8f)) || Solid(new Vector3(0.8f, 1f, 0f)), "open inside where they meet");
            Assert.IsTrue(Solid(new Vector3(0f, 1.45f, 0f)) && Solid(new Vector3(0f, 1.45f, -0.6f)), "a roof over the junction");
            Assert.IsTrue(Solid(new Vector3(0f, 0.55f, 0f)), "a floor under it");
            Assert.IsTrue(Solid(new Vector3(0f, 1f, 0.95f)), "the far wall");
            Assert.IsFalse(Solid(new Vector3(0f, 1f, 1.1f)), "and nothing past it");
        }

        [Test]
        public void TubesMeetingAtAJunctionClimbingDifferentlyLeaveNoNotchOnTop()
        {
            var s = ThreeWay(Spline.Profile.Circle, false, false, new Vector2(2f, 2f), 0.2f, new Vector3(-6f, 4f, 2f), new Vector3(6f, 1f, 1f), new Vector3(0f, 0f, -6f));
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            Assert.IsTrue(Solid(new Vector3(0f, 1f, 0f)) && Solid(new Vector3(0f, 1.8f, 0f)), "solid through the middle, up to near the top");
        }

        [Test]
        public void ACircleCornerIsABallJoint([Values(false, true)] bool hollow)
        {
            var s = Make(Spline.Profile.Circle, new Vector2(2f, 2f), hollow, new Vector3(-5f, 1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 5f));
            s.thickness = 0.2f; s.sides = 32; Rebuild(s);
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            var outside = new Vector3(Mathf.Sqrt(0.5f), 0f, -Mathf.Sqrt(0.5f)); // the outside of the corner
            var at = new Vector3(0f, 1f, 0f);
            Assert.IsTrue(Solid(at + outside * 0.92f) && Solid(at + Vector3.up * 0.92f), "round the outside and over the top: the tube's own radius");
            Assert.IsFalse(Solid(at + outside * 1.06f), "and no further: no pointed elbow");
            Assert.AreEqual(!hollow, Solid(at), hollow ? "open through the joint" : "solid through the joint");
        }

        [Test]
        public void RoundedEndsAreHalfABallOrHalfADisc()
        {
            var tube = Make(Spline.Profile.Circle, new Vector2(2f, 2f), false, new Vector3(0f, 1f, 0f), new Vector3(6f, 1f, 0f));
            tube.sides = 32; tube.ends = Spline.Ends.Rounded; Rebuild(tube);
            Assert.IsTrue(Solid(new Vector3(-0.9f, 1f, 0f)) && Solid(new Vector3(-0.6f, 1.6f, 0f)), "a tube ends in half a ball");
            Assert.IsFalse(Solid(new Vector3(-1.08f, 1f, 0f)), "of its own radius");
            Object.DestroyImmediate(tube.gameObject); Physics.SyncTransforms();
            var road = Channel(new Vector3(0f, 1f, 0f), new Vector3(6f, 1f, 0f));
            road.ends = Spline.Ends.Rounded; Rebuild(road);
            Assert.IsTrue(Solid(new Vector3(-0.95f, 1f, 0f)) && Solid(new Vector3(-0.67f, 1f, 0.67f)), "a channel ends in a wall round half a disc");
            Assert.IsFalse(Solid(new Vector3(-0.7f, 1f, 0f)), "open inside it");
            Assert.IsTrue(Solid(new Vector3(-0.5f, 0.75f, 0f)), "on its floor");
            Assert.IsFalse(Solid(new Vector3(-1.1f, 1f, 0f)), "and nothing past the half width");
        }

        [Test]
        public void ASharpCornerEndsSquareJustPastItsPoint()
        {
            var s = Channel(new Vector3(0f, 1f, -3f), new Vector3(8f, 1f, 0f), new Vector3(0f, 1f, 3f));
            Specs(s); Assert.AreEqual(0, s.droppedPieces, "every piece made");
            Assert.IsFalse(Solid(new Vector3(8f, 1f, 0f)), "the channel reaches the point");
            Assert.IsTrue(Solid(new Vector3(8.5f, 0.75f, 0f)), "with a floor past it");
            // each run goes on square past the point by the half width along its own way: their ends' walls meet at the tip
            Assert.IsFalse(Solid(new Vector3(8.8f, 1f, 0f)), "inside the ends the channel");
            Assert.IsTrue(Solid(new Vector3(9f, 1f, 0f)), "an end wall past it");
            Assert.IsFalse(Solid(new Vector3(9.15f, 1f, 0f)), "and nothing beyond");
            // the two sides part where their outsides meet, 1 / sin(half the angle) from the point
            float meet = 1f / Mathf.Sin(Mathf.Atan2(3f, 8f));
            Assert.IsTrue(Solid(new Vector3(8f - meet + 0.15f, 1f, 0f)), "a wall between the sides");
            Assert.IsFalse(Solid(new Vector3(8f - meet + 0.4f, 1f, 0f)), "open on the turning side of it");
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

using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A profile swept along a network of paths: pipes, beams, rails, gutters, tunnels. Points are joined by links like a
    /// floor plan's, in 3D, and may branch; Smooth points curve the path (see <see cref="PointGraph"/>). Everything is
    /// generated (see <see cref="BrushGenerator"/>): each straight piece of the path is one brush, the hull of the profile
    /// at its two ends. A hollow profile also cuts its inside, after every solid piece, so branches open into each other.
    /// </summary>
    [AddComponentMenu("CSG Brush/Spline")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/Spline.png")]
    [DisallowMultipleComponent]
    public sealed class Spline : PointGraph
    {
        public enum Profile
        {
            [Tooltip("A round tube: pipes, columns, tunnels.")] Circle,
            [Tooltip("A box section: beams, rails, roads, walls; Half and Hollow, a channel open on top.")] Rectangle,
        }

        public enum Orientation
        {
            [Tooltip("The profile's top stays up: roads, rails, fences.")] Upright,
            [Tooltip("The profile turns with the path without twisting: pipes, coasters.")] Follow,
        }

        public enum Ends
        {
            [Tooltip("The inside is open at the ends.")] Open,
            [Tooltip("The ends are capped.")] Closed,
            [Tooltip("The ends are rounded: a half ball, or a half disc for a flat profile, closed.")] Rounded,
        }

        [Tooltip("The shape swept along the path.")] public Profile profile = Profile.Circle;
        [Tooltip("Metres: width and height of the profile (a circle's diameter is the width).")] public Vector2 size = DefaultSize;
        /// <summary>A new spline's profile size, metres.</summary>
        public static readonly Vector2 DefaultSize = new Vector2(0.5f, 0.5f);
        [Tooltip("Sides of a circle; 0 works them out from the curve tolerance.")] public int sides = 0;
        [Tooltip("Only the lower half of the profile, its top level with the path: open on top when hollow (a half pipe, a channel).")] public bool half;
        [Tooltip("Cut out the inside, leaving a wall of the thickness below.")] public bool hollow;
        [Tooltip("Metres: the wall of a hollow profile or a channel.")] public float thickness = 0.05f;
        [Tooltip("Whether the path's ends are open, capped or rounded.")] public Ends ends = Ends.Open;
        [Tooltip("How the profile turns along the path.")] public Orientation orientation = Orientation.Follow;
        [Tooltip("Empty uses the project's default material.")] public Material material;

        public override bool Flat => false;

        /// <summary>A point's roll: how far the profile is turned about the path there, degrees, by the point's id.</summary>
        [System.Serializable]
        public struct PointRoll
        {
            public int id;
            public float degrees;
            public PointRoll(int id, float degrees) { this.id = id; this.degrees = degrees; }
        }

        /// <summary>The points with a roll; every other point has none. Between points the roll changes evenly along the path.</summary>
        [HideInInspector] public List<PointRoll> rolls = new List<PointRoll>();

        /// <summary>A point's roll (by id), degrees: 0 unless set.</summary>
        public float Roll(int id) { foreach (var r in rolls) if (r.id == id) return r.degrees; return 0f; }

        /// <summary>Set a point's roll (by id), degrees; 0 removes it.</summary>
        public void SetRoll(int id, float degrees)
        {
            rolls.RemoveAll(r => r.id == id);
            degrees = Mathf.Repeat(degrees + 180f, 360f) - 180f;
            if (Mathf.Abs(degrees) > 1e-4f) rolls.Add(new PointRoll(id, degrees));
        }

        protected override void AfterEnsureGraph()
        {
            if (rolls == null) rolls = new List<PointRoll>();
            rolls.RemoveAll(r => PointIndex(r.id) < 0);
        }

        /// <summary>Does the profile have an inside to cut?</summary>
        public bool IsHollow => hollow;

        protected override void OnValidate()
        {
            EnsureProfile();
            size = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
            sides = Mathf.Clamp(sides, 0, 64);
            thickness = Mathf.Clamp(thickness, 0.001f, Mathf.Min(size.x, size.y) * 0.5f - 0.001f);
            base.OnValidate();
        }

        /// <summary>
        /// A spline saved with the U Channel profile (dropped: a hollow Half Rectangle is the same channel) becomes one, as
        /// deep as it was; its rim, the top of the half, is now level with the path.
        /// </summary>
        void EnsureProfile()
        {
            const int LegacyUChannel = 2;
            if ((int)profile != LegacyUChannel) return;
            profile = Profile.Rectangle; half = true; hollow = true; size.y *= 2f;
        }

        public override int Key()
        {
            EnsureProfile();
            EnsureGraph();
            unchecked
            {
                int h = GraphKey(23);
                h = h * 31 + (int)profile; h = h * 31 + Mathf.RoundToInt(size.x * 1e5f); h = h * 31 + Mathf.RoundToInt(size.y * 1e5f);
                h = h * 31 + sides; h = h * 31 + (hollow ? 1 : 0); h = h * 31 + (half ? 7 : 0); h = h * 31 + Mathf.RoundToInt(thickness * 1e5f);
                h = h * 31 + (int)ends; h = h * 31 + (int)orientation; h = h * 31 + Mathf.RoundToInt(Tolerance * 1e5f);
                h = h * 31 + (material != null ? material.GetHashCode() : 0);
                foreach (var r in rolls) { h = h * 31 + r.id; h = h * 31 + Mathf.RoundToInt(r.degrees * 1e3f); }
                return h;
            }
        }

        // ------------------------------------------------------------------ profile

        /// <summary>The circle's number of sides: typed, or the fewest that keep its edges within the tolerance (at least 6).</summary>
        public int CircleSides
        {
            get
            {
                if (sides >= 3) return sides;
                float r = size.x * 0.5f, tol = Tolerance;
                if (tol >= r) return 6;
                int n = Mathf.CeilToInt(Mathf.PI / Mathf.Acos(1f - tol / r));
                return Mathf.Clamp(n, 6, 64);
            }
        }

        /// <summary>The profile as convex parts (x right, y up, centred on the path), outer or inner.</summary>
        public List<Vector2[]> ProfileParts(bool inner)
        {
            var parts = new List<Vector2[]>();
            float w = size.x, hgt = size.y, t = thickness;
            if (inner) { w -= 2f * t; hgt -= 2f * t; if (w <= 1e-3f || hgt <= 1e-3f) return parts; }
            if (profile == Profile.Rectangle && half)
            {
                // the lower half of the box, its top level with the path; hollow, its inside reaches up past the top, open
                // like a half pipe's, so crossing channels open into each other
                float w0 = size.x, d0 = size.y * 0.5f;
                if (!inner) { parts.Add(Rect(-w0 * 0.5f, -d0, w0 * 0.5f, 0f)); return parts; }
                if (w0 - 2f * t <= 1e-3f || d0 - t <= 1e-3f) return parts;
                parts.Add(Rect(-w0 * 0.5f + t, -d0 + t, w0 * 0.5f - t, d0 * OpenTop + 0.01f));
                return parts;
            }
            switch (profile)
            {
                case Profile.Circle when half:
                {
                    // the lower half, its flat top level with the path; hollow, its inside reaches up past the top, open
                    // like a channel's, so crossing half pipes open into each other
                    int n = Mathf.Max(2, (CircleSides + 1) / 2); float r = w * 0.5f;
                    var c = new List<Vector2>(n + 3);
                    for (int i = 0; i <= n; i++) { float a = Mathf.PI + Mathf.PI * i / n; c.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r); }
                    if (inner) { float top = size.x * 0.5f * OpenTop + 0.01f; c.Add(new Vector2(r, top)); c.Add(new Vector2(-r, top)); }
                    parts.Add(c.ToArray());
                    break;
                }
                case Profile.Circle:
                {
                    int n = CircleSides; var c = new Vector2[n];
                    for (int i = 0; i < n; i++) { float a = 2f * Mathf.PI * i / n; c[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (w * 0.5f); }
                    parts.Add(c);
                    break;
                }
                case Profile.Rectangle:
                    parts.Add(Rect(-w * 0.5f, -hgt * 0.5f, w * 0.5f, hgt * 0.5f));
                    break;
            }
            return parts;
        }

        static Vector2[] Rect(float x0, float y0, float x1, float y1) => new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

        /// <summary>
        /// How far a channel's cut reaches above its sides, as a share of its height: a piece twisted by a turn that also
        /// climbs (Upright) bulges a little, and the cut must clear it, or a thin lid would cover the channel.
        /// </summary>
        const float OpenTop = 0.5f;

        /// <summary>
        /// Half the profile's width (outer or inner): how far a run goes on past a junction or a folded corner. A branch's end
        /// is then flush with the far side of what it joins, and an L corner is filled to its outer corner.
        /// </summary>
        float HalfWidth(bool inner)
        {
            float r = 0f;
            foreach (var part in ProfileParts(inner)) foreach (var p in part) r = Mathf.Max(r, Mathf.Abs(p.x));
            return r;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>A continuous stretch of the path, swept in one go: from an end or a break to the next.</summary>
        public sealed class Run
        {
            public readonly List<Vector3> points = new List<Vector3>();
            /// <summary>The roll at each of <see cref="points"/>, degrees: the points' own, changing evenly along the path between them.</summary>
            public readonly List<float> rolls = new List<float>();
            /// <summary>The links it runs along, in order: which way, and the indices of their first and last points in <see cref="points"/> (the last may be <c>points.Count</c> on a closed run: its first point again).</summary>
            public readonly List<(int link, bool forward, int from, int to)> spans = new List<(int, bool, int, int)>();
            public bool closed;
            /// <summary>What each end of an open run is: a free end of the path (true), or a junction or sharp corner.</summary>
            public bool startIsEnd, endIsEnd;
            /// <summary>The ids of the points an open run starts and ends at.</summary>
            public int startId = -1, endId = -1;
        }

        /// <summary>The most the roll changes along one piece, degrees: a link whose ends roll differently is cut finer.</summary>
        public const float MaxRollStep = 10f;

        /// <summary>
        /// The path cut into runs. A point with one link ends a run; a point with three or more links breaks every run
        /// there, except that at a Smooth point the most nearly opposite pair runs through it; a Corner that turns breaks it
        /// too (its joint is the profile turned about the point). A loop with no break is one closed run.
        /// </summary>
        public List<Run> Runs()
        {
            EnsureGraph();
            var runs = new List<Run>();
            int n = links.Count;
            if (n == 0) return runs;
            var linksAt = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                foreach (var id in new[] { links[i].start, links[i].end })
                {
                    if (!linksAt.TryGetValue(id, out var list)) linksAt[id] = list = new List<int>();
                    list.Add(i);
                }
            }
            // which link continues each link through a point (-1: the run breaks there)
            int Next(int link, int atId)
            {
                var here = linksAt[atId];
                if (here.Count == 2)
                {
                    int other = here[0] == link ? here[1] : here[0];
                    // every Corner that turns is a joint (a ball for a round profile, a deck for a flat one): the profile
                    // turned about the point, so the outside of the corner is as far from the path as the sides are
                    if (!IsSmooth(atId) && TurnDegrees(link, other, atId) > 1f) return -1;
                    return other;
                }
                if (here.Count < 3 || !IsSmooth(atId)) return -1;
                int bi = -1, bj = -1; float best = float.MaxValue;
                for (int i = 0; i < here.Count; i++)
                    for (int j = i + 1; j < here.Count; j++)
                    {
                        float dot = Vector3.Dot(Away(here[i], atId), Away(here[j], atId));
                        if (dot < best) { best = dot; bi = here[i]; bj = here[j]; }
                    }
                return bi == link ? bj : bj == link ? bi : -1;
            }
            int Far(int link, int fromId) => links[link].start == fromId ? links[link].end : links[link].start;
            var used = new bool[n];
            // start from every break first, so no open run is cut in the middle; what is left are loops
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                {
                    if (used[i]) continue;
                    int startId = -1;
                    if (pass == 0)
                    {
                        if (Next(i, links[i].start) < 0) startId = links[i].start;
                        else if (Next(i, links[i].end) < 0) startId = links[i].end;
                        else continue;
                    }
                    else startId = links[i].start;
                    var run = new Run { startIsEnd = linksAt[startId].Count == 1, startId = startId };
                    int link = i, at = startId;
                    while (link >= 0 && !used[link])
                    {
                        used[link] = true;
                        var path = Curve(link);
                        bool forward = links[link].start == at;
                        int from = Mathf.Max(0, run.points.Count - 1);
                        float r0 = Roll(at), r1 = Roll(Far(link, at)), total = PathLength(path), along = 0f;
                        // where the roll changes, pieces no longer than MaxRollStep of it each: the profile turns gradually
                        int steps = Mathf.CeilToInt(Mathf.Abs(Mathf.DeltaAngle(r0, r1)) / MaxRollStep);
                        for (int k = 0; k < path.Length; k++)
                        {
                            var q = path[forward ? k : path.Length - 1 - k];
                            int parts = 1;
                            Vector3 prev = q;
                            if (k > 0)
                            {
                                prev = path[forward ? k - 1 : path.Length - k];
                                float len = (q - prev).magnitude;
                                if (steps > 0 && total > 1e-6f) parts = Mathf.Max(1, Mathf.CeilToInt(steps * len / total));
                                for (int j = 1; j < parts; j++)
                                {
                                    var mid = Vector3.Lerp(prev, q, j / (float)parts);
                                    run.points.Add(mid);
                                    run.rolls.Add(Mathf.LerpAngle(r0, r1, (along + len * j / parts) / total));
                                }
                                along += len;
                            }
                            if (run.points.Count == 0 || (run.points[run.points.Count - 1] - q).sqrMagnitude > 1e-10f)
                            {
                                run.points.Add(q);
                                run.rolls.Add(Mathf.LerpAngle(r0, r1, total > 1e-6f ? along / total : 0f));
                            }
                        }
                        run.spans.Add((link, forward, from, run.points.Count - 1));
                        at = Far(link, at);
                        int next = Next(link, at);
                        if (next < 0) { run.endIsEnd = linksAt[at].Count == 1; run.endId = at; break; }
                        if (used[next]) { run.closed = pass == 1 && at == startId; run.endId = at; break; }
                        link = next;
                    }
                    if (run.closed && run.points.Count > 1 && (run.points[0] - run.points[run.points.Count - 1]).sqrMagnitude < 1e-10f) { run.points.RemoveAt(run.points.Count - 1); run.rolls.RemoveAt(run.rolls.Count - 1); } // the last span ends on points.Count: the first point
                    if (run.points.Count >= 2) runs.Add(run);
                }
            return runs;
        }

        /// <summary>The direction a link leaves a point (toward its first sample), for choosing how runs continue.</summary>
        Vector3 Away(int link, int fromId)
        {
            var path = Curve(link);
            bool forward = links[link].start == fromId;
            var a = forward ? path[0] : path[path.Length - 1]; var b = forward ? path[1] : path[path.Length - 2];
            return (b - a).normalized;
        }

        float TurnDegrees(int arriving, int leaving, int atId) => Vector3.Angle(-Away(arriving, atId), Away(leaving, atId));


        // ------------------------------------------------------------------ places on the path

        /// <summary>A place on the path and the profile's frame there (this object's space): along the path, the profile's right and up.</summary>
        public struct Pose { public Vector3 position, forward, right, up; }

        /// <summary>The place a distance along a link from its start (clamped), with the frame the sweep uses there. False for a link that isn't swept.</summary>
        public bool PoseAt(int link, float distance, out Pose pose)
        {
            pose = default;
            foreach (var run in Runs())
                foreach (var span in run.spans)
                {
                    if (span.link != link) continue;
                    int n = run.points.Count;
                    float len = 0f; for (int i = span.from; i < span.to; i++) len += (run.points[(i + 1) % n] - run.points[i % n]).magnitude;
                    float d = Mathf.Clamp(span.forward ? distance : len - distance, 0f, len);
                    pose = OnRun(run, Frames(run.points, run.closed, run.rolls), span.from, span.to, d);
                    if (!span.forward) { pose.forward = -pose.forward; pose.right = -pose.right; } // facing the link's own way
                    return true;
                }
            return false;
        }

        /// <summary>The direction the path runs through a point (by id), as its roll turns about it; false when no run passes it.</summary>
        public bool PointDirection(int id, out Vector3 forward)
        {
            forward = Vector3.forward;
            int index = PointIndex(id);
            if (index < 0) return false;
            var at = Point(index);
            foreach (var run in Runs())
                for (int i = 0; i < run.points.Count; i++)
                    if ((run.points[i] - at).sqrMagnitude < 1e-10f)
                    {
                        int n = run.points.Count;
                        var outgoing = run.closed || i + 1 < n ? run.points[(i + 1) % n] - run.points[i] : run.points[i] - run.points[i - 1];
                        var incoming = run.closed || i > 0 ? run.points[i] - run.points[(i - 1 + n) % n] : outgoing;
                        var halfway = incoming.normalized + outgoing.normalized;
                        forward = halfway.sqrMagnitude > 1e-8f ? halfway.normalized : outgoing.normalized;
                        return true;
                    }
            return false;
        }

        /// <summary>A run's length along its points (round the loop for a closed one).</summary>
        public static float RunLength(Run run)
        {
            int n = run.points.Count; float len = 0f;
            for (int i = 0; i + 1 < n; i++) len += (run.points[i + 1] - run.points[i]).magnitude;
            if (run.closed && n > 1) len += (run.points[0] - run.points[n - 1]).magnitude;
            return len;
        }

        /// <summary>The place a distance along a run from its first point (clamped), with the sweep's frame there.</summary>
        public Pose PoseAlong(Run run, float distance)
        {
            int n = run.points.Count;
            return OnRun(run, Frames(run.points, run.closed, run.rolls), 0, run.closed ? n : n - 1, Mathf.Clamp(distance, 0f, RunLength(run)));
        }

        /// <summary>The place <paramref name="d"/> along a run from point <paramref name="from"/>, going no further than point <paramref name="to"/>.</summary>
        static Pose OnRun(Run run, List<Frame> frames, int from, int to, float d)
        {
            var pose = new Pose();
            int n = run.points.Count;
            for (int i = from; i < to || i == from; i++)
            {
                Vector3 a = run.points[i % n], b = run.points[(i + 1) % n]; float seg = (b - a).magnitude;
                if (d > seg && i + 1 < to) { d -= seg; continue; }
                var f = frames[Mathf.Clamp(i, 0, frames.Count - 1) % frames.Count]; // the piece's own frame: as it was swept
                pose.position = Vector3.Lerp(a, b, seg > 1e-9f ? Mathf.Clamp01(d / seg) : 0f);
                pose.forward = f.forward; pose.right = f.right; pose.up = f.up;
                break;
            }
            return pose;
        }

        /// <summary>
        /// Where the profile's outside is at an angle around it, in the profile's plane (x right, y up): 0 is the top, 90 the
        /// right. The farthest crossing of its parts along that direction.
        /// </summary>
        public Vector2 SurfacePoint(float angleDegrees)
        {
            float rad = angleDegrees * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            float far = 0f;
            foreach (var part in ProfileParts(false))
                for (int i = 0; i < part.Length; i++)
                {
                    var p = part[i]; var e = part[(i + 1) % part.Length] - p;
                    float den = dir.x * e.y - dir.y * e.x;
                    if (Mathf.Abs(den) < 1e-9f) continue;
                    float t = (p.x * e.y - p.y * e.x) / den, u = (p.x * dir.y - p.y * dir.x) / den;
                    if (u >= -1e-6f && u <= 1f + 1e-6f && t > far) far = t;
                }
            return dir * far;
        }

        /// <summary>The place on the path nearest a point (this object's space): which link and how far along it.</summary>
        public bool Nearest(Vector3 point, out int link, out float along, out float distance)
        {
            link = -1; along = 0f; distance = float.MaxValue;
            for (int l = 0; l < links.Count; l++)
            {
                var path = Curve(l); float before = 0f;
                for (int i = 0; i + 1 < path.Length; i++)
                {
                    var ab = path[i + 1] - path[i]; float len = ab.magnitude;
                    float t = len < 1e-9f ? 0f : Mathf.Clamp(Vector3.Dot(point - path[i], ab / len), 0f, len);
                    float d = (path[i] + (len < 1e-9f ? Vector3.zero : ab / len * t) - point).magnitude;
                    if (d < distance) { distance = d; link = l; along = before + t; }
                    before += len;
                }
            }
            return link >= 0;
        }

        // ------------------------------------------------------------------ the sweep

        /// <summary>Pieces the last build could not make (their two ends gave no sound shape). Should be 0.</summary>
        [System.NonSerialized] public int droppedPieces;
        /// <summary>Pieces the last build made straight because their mitred ends crossed (they overlap their neighbours).</summary>
        [System.NonSerialized] public int unmitredPieces;

        public override void Describe(List<BrushSpec> into)
        {
            droppedPieces = 0; unmitredPieces = 0;
            var runs = Runs();
            var outer = ProfileParts(false); var inner = IsHollow ? ProfileParts(true) : new List<Vector2[]>();
            float reachOut = HalfWidth(false), reachIn = HalfWidth(true);
            const float Hair = 0.01f;
            var hubs = Hubs(runs);
            bool boxed = Boxed, roundHalf = half && !boxed;
            int piece = 0;
            var cuts = new List<BrushSpec>();
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var solid = Trimmed(run, 0f, 0f, out var solidRolls);
                var frames = Frames(solid, run.closed, solidRolls);
                bool freeStart = run.startIsEnd && ends != Ends.Rounded, freeEnd = run.endIsEnd && ends != Ends.Rounded; // (a rounded end is a joint)
                // a round profile's run is a capsule: domed where it meets a joint (or ends rounded), the domes made of its
                // own end rings, so runs overlapping at a joint add up to the joint: a sweep of the profile's ball
                bool capStart = !boxed && !run.closed && !freeStart, capEnd = !boxed && !run.closed && !freeEnd;
                foreach (var part in outer) SweepWhole(solid, frames, run.closed, part, reachOut, BrushOperation.Add, "Piece", ref piece, into, capStart && !half, capEnd && !half, reachOut); // (a half profile's domes are cut at the joint: below)
                // the profile where the run meets its hubs, or where it crosses one it passes through
                foreach (var hub in hubs)
                    foreach (var arm in hub.arms)
                    {
                        if (arm.run != r) continue;
                        if (arm.through < 0)
                        {
                            int i = arm.atStart ? 0 : solid.Count - 1;
                            arm.frame = Away(frames[arm.atStart ? 0 : frames.Count - 1], arm.direction);
                            arm.end = solid[i];
                            foreach (var part in outer) arm.outer.Add(Ring(solid, frames, false, part, i));
                            foreach (var part in inner) arm.inner.Add(Ring(solid, frames, false, part, i));
                            continue;
                        }
                        int from = solid.FindIndex(q => (q - hub.at).sqrMagnitude < 1e-10f);
                        if (from < 0) continue;
                        Frame frame = default;
                        foreach (var part in outer) arm.outer.Add(RingAlong(solid, frames, run.closed, from, arm.sign, 0f, part, out frame));
                        foreach (var part in inner) arm.inner.Add(RingAlong(solid, frames, run.closed, from, arm.sign, 0f, part, out frame));
                        arm.frame = Away(frame, arm.direction);
                        arm.end = RingAlong(solid, frames, run.closed, from, arm.sign, 0f, new[] { Vector2.zero }, out _)[0];
                    }
                if (inner.Count == 0) continue;
                // inside: a hair into its hubs; past an open end by a hair, short of a closed one by the wall
                float s = freeStart ? (ends == Ends.Open ? Hair : -thickness) : Hair;
                float e = freeEnd ? (ends == Ends.Open ? Hair : -thickness) : Hair;
                var hole = Trimmed(run, s, e, out var holeRolls);
                var holeFrames = Frames(hole, run.closed, holeRolls);
                foreach (var part in inner) SweepWhole(hole, holeFrames, run.closed, part, reachIn, BrushOperation.Subtract, "Inside", ref piece, cuts, capStart, capEnd, reachIn);
            }
            // each hub: solid pieces joining its arms' ends, and pieces cutting their insides through
            int hubCount = 0;
            foreach (var hub in hubs)
            {
                hubCount++;
                if (boxed)
                {
                    if (hub.arms.Count > 1) { SquareEnds(hub, "Junction " + hubCount, into, cuts); continue; }
                    // a rounded end of the path is half a disc, its inside the same at the inner size, so a wall of the
                    // thickness stays round it
                    if (hub.arms[0].outer.Count == 0) continue;
                    var end = hub.arms[0];
                    var disc = HalfDisc(end.outer[0], end.end, end.frame.right, -end.direction);
                    if (disc != null) into.Add(new BrushSpec { name = "End " + hubCount, operation = BrushOperation.Add, polyhedron = disc, material = material });
                    if (end.inner.Count == 0) continue;
                    var hole = HalfDisc(end.inner[0], end.end, end.frame.right, -end.direction);
                    if (hole != null) cuts.Add(new BrushSpec { name = "End inside " + hubCount, operation = BrushOperation.Subtract, polyhedron = hole, material = material });
                    continue;
                }
                if (!roundHalf) continue; // (a whole round profile's joints are its runs' domes)
                // a half profile's domes, each its own brush, cut by any other arm's top that passes above this arm's own
                // top on this arm's side: where the arms meet over a crest a dome's flat top would stand above the next
                // arm's; in a valley it lies below it, and the domes add up as they are
                foreach (var arm in hub.arms)
                {
                    if (arm.through >= 0 || arm.outer.Count == 0) continue;
                    var ring = arm.outer[0];
                    var pts = new List<Vector3>(ring);
                    foreach (var dome in Dome(ring, arm.end, -arm.direction, reachOut)) pts.AddRange(dome);
                    var poly = HullOf(pts);
                    var ownSide = hub.at + arm.direction * reachOut;
                    foreach (var other in hub.arms)
                    {
                        if (poly == null) break;
                        if (other == arm) continue;
                        var top = other.frame.up;
                        if (Vector3.Dot(ownSide - hub.at, top) > 1e-4f) continue; // the other's top passes below this arm's: a valley
                        poly = Clip(poly, hub.at, top, 0f);
                    }
                    if (poly != null) into.Add(new BrushSpec { name = "Junction " + hubCount, operation = BrushOperation.Add, polyhedron = poly, material = material });
                }
            }
            into.AddRange(cuts); // every inside after every solid piece: branches open into each other
        }


        /// <summary>A frame turned to run along <paramref name="direction"/> (the profile is the same either way: it is symmetric).</summary>
        static Frame Away(Frame f, Vector3 direction) => Vector3.Dot(f.forward, direction) >= 0f ? f : new Frame { forward = -f.forward, right = -f.right, up = f.up };

        // ------------------------------------------------------------------ junction decks

        /// <summary>
        /// Profiles with a flat floor and straight sides (a U Channel, a Rectangle, solid or hollow) join at hubs with a deck: a floor
        /// built over the junction's outline, following the arms' slopes, with walls along the outline between the arms.
        /// Nothing is carried in from the arms, so arms at different heights and slopes still meet without steps or stubs.
        /// </summary>
        /// <summary>
        /// A Rectangle joins like a sweep of its square: each run goes on past a joint by half its width and the runs'
        /// square ends overlap there, its inside the same at the inner size, cut after every solid piece.
        /// </summary>
        bool Boxed => profile == Profile.Rectangle;

        /// <summary>
        /// A box profile's joint: each arm that ends there goes on past the point by half its width, the profile carried
        /// straight on (a square end, its own brush), and the arms' square ends overlap and add up to the joint. Each end is
        /// cut to every other arm's top and bottom where it would stand above or below it (arms sloping or rolled
        /// differently): the joint never rises over a road that meets it, nor hangs below it. The insides are the same at
        /// the inner size, cut to the other arms' insides, so a floor or roof is never cut away.
        /// </summary>
        void SquareEnds(Hub hub, string name, List<BrushSpec> into, List<BrushSpec> cuts)
        {
            float reachOut = HalfWidth(false), reachIn = HalfWidth(true);
            Heights(false, out float outLow, out float outHigh); Heights(true, out float inLow, out float inHigh);
            foreach (var arm in hub.arms)
            {
                if (arm.through >= 0) continue; // a run passing through: the others' ends overlap it
                for (int k = 0; k < arm.outer.Count; k++) Add(arm, arm.outer[k], reachOut, outLow, outHigh, BrushOperation.Add, into);
                for (int k = 0; k < arm.inner.Count; k++) Add(arm, arm.inner[k], reachIn, inLow, inHigh, BrushOperation.Subtract, cuts);
            }

            void Add(Arm arm, Vector3[] ring, float reach, float low, float high, BrushOperation op, List<BrushSpec> list)
            {
                var pts = new List<Vector3>(ring.Length * 2);
                pts.AddRange(ring);
                foreach (var q in ring) pts.Add(q - arm.direction * reach);
                var poly = HullOf(pts);
                foreach (var other in hub.arms)
                {
                    if (poly == null) return;
                    if (other == arm) continue;
                    var up = other.frame.up;
                    poly = Clip(poly, hub.at, up, high);   // not above the other's top (its roof, for an inside)
                    if (poly != null) poly = Clip(poly, hub.at, -up, -low); // nor below its bottom (its floor)
                }
                if (poly != null) list.Add(new BrushSpec { name = name, operation = op, polyhedron = poly, material = material });
            }
        }

        /// <summary>The profile's lowest and highest point above the path (outer or inner).</summary>
        void Heights(bool inner, out float low, out float high)
        {
            low = float.MaxValue; high = float.MinValue;
            foreach (var part in ProfileParts(inner)) foreach (var q in part) { low = Mathf.Min(low, q.y); high = Mathf.Max(high, q.y); }
            if (low > high) low = high = 0f;
        }

        /// <summary>
        /// Half a disc on an end <paramref name="ring"/> (square to the path at <paramref name="centre"/>): each corner swung
        /// out round the centre along <paramref name="outward"/>, keeping its height, so the profile's sides close in a half
        /// circle of their own spacing. Convex, one brush.
        /// </summary>
        BrushPolyhedron HalfDisc(Vector3[] ring, Vector3 centre, Vector3 right, Vector3 outward)
        {
            int steps = Mathf.Max(2, (CircleSides + 3) / 4);
            var pts = new List<Vector3>(ring.Length * (steps + 1));
            foreach (var q in ring)
            {
                var d = q - centre; float x = Vector3.Dot(d, right); var rest = d - right * x;
                for (int k = 0; k <= steps; k++)
                {
                    float a = 0.5f * Mathf.PI * k / steps;
                    pts.Add(centre + rest + right * (x * Mathf.Cos(a)) + outward * (Mathf.Abs(x) * Mathf.Sin(a)));
                }
            }
            return HullOf(pts);
        }

        /// <summary>
        /// A place where runs meet: a junction, a Corner point, or a rounded end of the path. Every run there runs right to
        /// the point; a round profile's runs end in domes, a box profile's go on square (see <see cref="SquareEnds"/>).
        /// </summary>
        sealed class Hub
        {
            public Vector3 at;
            public readonly List<Arm> arms = new List<Arm>();
        }

        /// <summary>One way out of a hub: a run's end (<see cref="through"/> is -1), or one side of a run passing through it.</summary>
        sealed class Arm
        {
            public int run; public bool atStart;
            /// <summary>For a run passing through: the hub's index in its points, and which way along them (+1 or -1).</summary>
            public int through = -1, sign;
            public Vector3 direction;
            /// <summary>The profile's frame at the arm's end, running away from the hub.</summary>
            public Frame frame;
            /// <summary>The middle of the arm's end (on the path), where its <see cref="frame"/> is.</summary>
            public Vector3 end;
            public readonly List<Vector3[]> outer = new List<Vector3[]>(), inner = new List<Vector3[]>();
        }

        /// <summary>The hubs: every point where an open run ends without being a free end of the path (or ends rounded).</summary>
        List<Hub> Hubs(List<Run> runs)
        {
            var hubs = new List<Hub>(); var byId = new Dictionary<int, Hub>();
            Hub At(int id)
            {
                if (!byId.TryGetValue(id, out var hub)) { int index = PointIndex(id); if (index < 0) return null; byId[id] = hub = new Hub { at = Point(index) }; hubs.Add(hub); }
                return hub;
            }
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r]; int n = run.points.Count;
                if (run.closed || n < 2) continue;
                // (a rounded end is a joint of one arm)
                if (!run.startIsEnd || ends == Ends.Rounded) At(run.startId)?.arms.Add(new Arm { run = r, atStart = true, direction = (run.points[1] - run.points[0]).normalized });
                if (!run.endIsEnd || ends == Ends.Rounded) At(run.endId)?.arms.Add(new Arm { run = r, atStart = false, direction = (run.points[n - 2] - run.points[n - 1]).normalized });
            }
            // a run going straight through a Smooth junction: both ways out of it are arms
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r]; int n = run.points.Count;
                foreach (var hub in hubs)
                    for (int i = run.closed ? 0 : 1; i < (run.closed ? n : n - 1); i++)
                        if ((run.points[i] - hub.at).sqrMagnitude < 1e-10f)
                        {
                            hub.arms.Add(new Arm { run = r, through = i, sign = 1, direction = (run.points[(i + 1) % n] - run.points[i]).normalized });
                            hub.arms.Add(new Arm { run = r, through = i, sign = -1, direction = (run.points[(i - 1 + n) % n] - run.points[i]).normalized });
                        }
            }
            return hubs;
        }

        /// <summary>The length of an open run's points from index <paramref name="from"/> to its end one way (+1) or the other.</summary>
        static float Along(List<Vector3> p, int from, int sign)
        {
            float len = 0f;
            for (int i = from; i + sign >= 0 && i + sign < p.Count; i += sign) len += (p[i + sign] - p[i]).magnitude;
            return len;
        }

        /// <summary>The profile square to the path, <paramref name="distance"/> along it from point <paramref name="from"/> one way (+1) or the other.</summary>
        static Vector3[] RingAlong(List<Vector3> p, List<Frame> frames, bool closed, int from, int sign, float distance, Vector2[] part, out Frame frame)
        {
            int n = p.Count, count = frames.Count, i = from;
            for (int step = 0; step < n; step++)
            {
                int j = closed ? ((i + sign) % n + n) % n : i + sign;
                if (j < 0 || j >= n) break;
                int k = sign > 0 ? i : j; // piece k runs from point k to the next
                var f = frames[((k % count) + count) % count];
                float seg = (p[j] - p[i]).magnitude;
                bool last = !closed && (j + sign < 0 || j + sign >= n);
                frame = f;
                if (distance <= seg || last) return Square(Vector3.Lerp(p[i], p[j], seg > 1e-9f ? Mathf.Clamp01(distance / seg) : 0f), f, part);
                distance -= seg; i = j;
            }
            frame = frames[Mathf.Clamp(sign > 0 ? i : i - 1, 0, count - 1)];
            return Square(p[i], frame, part);
        }

        /// <summary>
        /// A run's points (and rolls) with its ends moved along the path: out past the end along its last piece (positive), or
        /// back in along the path (negative), never past nine tenths of the last piece left.
        /// </summary>
        static List<Vector3> Trimmed(Run run, float atStart, float atEnd, out List<float> rolls)
        {
            var p = new List<Vector3>(run.points); rolls = new List<float>(run.rolls);
            if (run.closed || p.Count < 2) return p;
            while (rolls.Count < p.Count) rolls.Add(0f);
            Shift(p, rolls, atStart);
            p.Reverse(); rolls.Reverse();
            Shift(p, rolls, atEnd);
            p.Reverse(); rolls.Reverse();
            return p;
        }

        static void Shift(List<Vector3> p, List<float> rolls, float by)
        {
            if (Mathf.Abs(by) < 1e-6f) return;
            if (by > 0f)
            {
                var d = p[0] - p[1]; float len = d.magnitude;
                if (len > 1e-6f) p[0] += d / len * by;
                return;
            }
            float cut = -by;
            while (p.Count > 2 && cut >= (p[1] - p[0]).magnitude - 1e-4f) { cut = Mathf.Max(0f, cut - (p[1] - p[0]).magnitude); p.RemoveAt(0); rolls.RemoveAt(0); }
            if (cut <= 1e-4f) return;
            float last = (p[1] - p[0]).magnitude;
            if (last < 1e-6f) return;
            float t = Mathf.Min(cut / last, 0.9f);
            p[0] = Vector3.Lerp(p[0], p[1], t);
            rolls[0] = Mathf.LerpAngle(rolls[0], rolls[1], t);
        }

        /// <summary>The profile's frame along one straight piece: its direction, and the profile's right and up.</summary>
        struct Frame { public Vector3 forward, right, up; }

        /// <summary>
        /// One frame per straight piece of a run (piece i runs from point i to the next). Upright: right is level. Follow: each
        /// piece's frame is the last one turned by the smallest rotation onto its direction (round a loop the leftover turn
        /// is spread evenly), which makes the mitres between pieces exact. Then each piece is rolled by its points' rolls.
        /// </summary>
        List<Frame> Frames(List<Vector3> p, bool closed, List<float> rolls = null)
        {
            int n = p.Count, count = closed ? n : n - 1;
            var frames = new List<Frame>(count);
            Vector3 lastRight = Vector3.right;
            for (int i = 0; i < count; i++)
            {
                var d = (p[(i + 1) % n] - p[i]).normalized;
                var f = new Frame { forward = d };
                if (i == 0 || orientation == Orientation.Upright)
                {
                    var right = Vector3.Cross(Vector3.up, d);
                    if (right.sqrMagnitude < 1e-6f) right = i == 0 ? Vector3.Cross(Vector3.forward, d) : lastRight; // straight up or down
                    f.right = right.normalized;
                    f.up = Vector3.Cross(d, f.right).normalized;
                }
                else
                {
                    var prev = frames[i - 1];
                    var turn = Quaternion.FromToRotation(prev.forward, d);
                    f.up = (turn * prev.up).normalized;
                    f.right = Vector3.Cross(f.up, d).normalized;
                    f.up = Vector3.Cross(d, f.right).normalized;
                }
                lastRight = f.right;
                frames.Add(f);
            }
            if (closed && orientation == Orientation.Follow && count > 2)
            {
                var back = Quaternion.FromToRotation(frames[count - 1].forward, frames[0].forward) * frames[count - 1].up;
                float twist = Vector3.SignedAngle(back, frames[0].up, frames[0].forward);
                for (int i = 1; i < count; i++)
                {
                    var f = frames[i]; var q = Quaternion.AngleAxis(twist * i / count, f.forward);
                    f.up = q * f.up; f.right = q * f.right; frames[i] = f;
                }
            }
            if (rolls != null && rolls.Count >= n)
                for (int i = 0; i < count; i++)
                {
                    float roll = Mathf.LerpAngle(rolls[i], rolls[(i + 1) % n], 0.5f); // a piece turns as the middle of its two points
                    if (Mathf.Abs(roll) < 1e-4f) continue;
                    var f = frames[i]; var q = Quaternion.AngleAxis(roll, f.forward); // after the frames are carried along, so rolls never add up
                    f.up = q * f.up; f.right = q * f.right; frames[i] = f;
                }
            return frames;
        }

        /// <summary>
        /// The profile where two pieces meet at point <paramref name="i"/>: the incoming piece's profile carried along it onto
        /// the plane halving the turn (a mitre), so the piece is exactly its profile cut by two planes. At an end of an open
        /// run the profile stands square to its piece.
        /// </summary>
        Vector3[] Ring(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, int i)
        {
            int n = p.Count, count = frames.Count;
            int incoming = closed ? (i - 1 + count) % count : i - 1, outgoing = closed ? i % count : i;
            var at = p[i];
            Frame f; Vector3 plane;
            if (incoming < 0) { f = frames[outgoing]; plane = f.forward; }           // the start of an open run
            else if (outgoing >= count) { f = frames[incoming]; plane = f.forward; } // its end
            else
            {
                f = frames[incoming];
                var halfway = frames[incoming].forward + frames[outgoing].forward;
                plane = halfway.sqrMagnitude > 1e-6f ? halfway.normalized : f.forward; // a U-turn: square
            }
            var ring = new Vector3[part.Length];
            float along = Vector3.Dot(f.forward, plane);
            for (int k = 0; k < part.Length; k++)
            {
                var q = at + f.right * part[k].x + f.up * part[k].y;
                float t = Mathf.Abs(along) > 1e-4f ? -Vector3.Dot(q - at, plane) / along : 0f;
                ring[k] = q + f.forward * t;
            }
            return ring;
        }

        /// <summary>
        /// A piece without mitres, for where its two mitred ends would cross (a short piece of a wide profile between sharp
        /// turns): the profile square to the piece, run on past each inner corner by half its width so it overlaps its
        /// neighbours there, as branches do at a junction. Ends of the path are not run on.
        /// </summary>
        BrushPolyhedron Unmitred(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, float reach, int k)
        {
            int n = p.Count, next = (k + 1) % n;
            var f = frames[k];
            bool startInside = closed || k > 0, endInside = closed || next < n - 1;
            return Hull(Square(p[k] - f.forward * (startInside ? reach : 0f), f, part), Square(p[next] + f.forward * (endInside ? reach : 0f), f, part));
        }

        static Vector3[] Square(Vector3 at, Frame f, Vector2[] part)
        {
            var ring = new Vector3[part.Length];
            for (int k = 0; k < part.Length; k++) ring[k] = at + f.right * part[k].x + f.up * part[k].y;
            return ring;
        }

        /// <summary>
        /// A run swept as one brush: its profile at every point (the mitres) joined by bands, closed by its two ends. The
        /// pieces share their rings, so there are no seams between them, and one brush is far cheaper to build than many.
        /// Where it comes out unsound (mitres crossing on a tight bend), the run is swept in convex pieces instead.
        /// </summary>
        void SweepWhole(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, float reach, BrushOperation op, string name, ref int count, List<BrushSpec> into, bool capStart = false, bool capEnd = false, float capRadius = 0f)
        {
            var poly = Swept(p, frames, closed, part, capStart, capEnd, capRadius);
            if (poly != null) { into.Add(new BrushSpec { name = name + " " + (++count), operation = op, polyhedron = poly, material = material }); return; }
            Sweep(p, frames, closed, part, reach, op, name, ref count, into);
            // (in pieces: each dome its own brush)
            if (closed || p.Count < 2) return;
            int last = p.Count - 1;
            foreach (var (on, at, outward) in new[] { (capStart, 0, (p[0] - p[1]).normalized), (capEnd, last, (p[last] - p[last - 1]).normalized) })
            {
                if (!on) continue;
                var ring = Ring(p, frames, false, part, at); var centre = p[at];
                var pts = new List<Vector3>(ring);
                foreach (var dome in Dome(ring, centre, outward, capRadius)) pts.AddRange(dome);
                var hull = HullOf(pts);
                if (hull != null) into.Add(new BrushSpec { name = name + " " + (++count), operation = op, polyhedron = hull, material = material });
            }
        }

        /// <summary>
        /// The rings of a dome on an end <paramref name="ring"/> (centred on <paramref name="centre"/>, square to the path):
        /// the ring itself scaled in and moved out along <paramref name="outward"/> a quarter turn at a time, then the pole.
        /// Its rings share the ring's own corners, scaled, so the dome meets the sweep exactly: a capsule's end, as fine as
        /// the profile's circle.
        /// </summary>
        List<Vector3[]> Dome(Vector3[] ring, Vector3 centre, Vector3 outward, float radius)
        {
            int steps = Mathf.Max(2, (CircleSides + 3) / 4);
            var rings = new List<Vector3[]>(steps);
            for (int k = 1; k < steps; k++)
            {
                float a = 0.5f * Mathf.PI * k / steps, c = Mathf.Cos(a), sn = Mathf.Sin(a);
                var r = new Vector3[ring.Length];
                for (int j = 0; j < ring.Length; j++) r[j] = centre + (ring[j] - centre) * c + outward * (radius * sn);
                rings.Add(r);
            }
            rings.Add(new[] { centre + outward * radius });
            return rings;
        }

        BrushPolyhedron Swept(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, bool capStart = false, bool capEnd = false, float capRadius = 0f)
        {
            int n = p.Count, m = part.Length;
            var at = new List<int>(n); // the points that start a piece of some length (and the last)
            for (int i = 0; i < n; i++)
            {
                bool lastOpen = !closed && i == n - 1;
                if (lastOpen || (p[(i + 1) % n] - p[i]).sqrMagnitude >= 1e-10f) at.Add(i);
                else if (!closed && i == n - 2) { at.Add(n - 1); break; } // the last piece has no length: end on the last point
            }
            if (at.Count < 2 || m < 3) return null;
            var verts = new List<Vector3>(at.Count * m);
            foreach (int i in at) verts.AddRange(Ring(p, frames, closed, part, i));
            var faces = new List<BrushPolyhedron.Face>();
            int rings = at.Count, bands = closed ? rings : rings - 1;
            // a sweep folds only where its mitres cross: each band's edges run forward along its piece (much cheaper than
            // testing every face against every other)
            for (int k = 0; k < bands; k++)
            {
                int a = k * m, b = ((k + 1) % rings) * m;
                var forward = p[at[(k + 1) % rings]] - p[at[k]];
                for (int j = 0; j < m; j++) if (Vector3.Dot(verts[b + j] - verts[a + j], forward) <= 1e-6f) { LastHullProblem = "whole sweep: folds"; return null; }
            }
            for (int k = 0; k < bands; k++)
            {
                int a = k * m, b = ((k + 1) % rings) * m;
                for (int j = 0; j < m; j++)
                {
                    int j1 = (j + 1) % m;
                    int[] quad = { a + j, a + j1, b + j1, b + j };
                    AddQuad(faces, verts, quad, k);
                }
            }
            if (!closed)
            {
                // each end flat, or a dome (a capsule's end) whose rings are the end ring's own corners scaled in
                void Domed(int baseRing, Vector3 centre, Vector3 outward, bool atStart, int group)
                {
                    int prev = baseRing * m;
                    foreach (var r in Dome(verts.GetRange(baseRing * m, m).ToArray(), centre, outward, capRadius))
                    {
                        if (r.Length == 1)
                        {
                            int pole = verts.Count; verts.Add(r[0]);
                            for (int j = 0; j < m; j++)
                            {
                                int j1 = (j + 1) % m;
                                faces.Add(new BrushPolyhedron.Face(atStart ? new[] { pole, prev + j1, prev + j } : new[] { prev + j, prev + j1, pole }) { group = group });
                            }
                            break;
                        }
                        int next = verts.Count; verts.AddRange(r);
                        for (int j = 0; j < m; j++)
                        {
                            int j1 = (j + 1) % m;
                            AddQuad(faces, verts, atStart ? new[] { next + j, next + j1, prev + j1, prev + j } : new[] { prev + j, prev + j1, next + j1, next + j }, group);
                        }
                        prev = next;
                    }
                }
                if (capStart) Domed(0, p[at[0]], (p[at[0]] - p[at[1]]).normalized, true, bands);
                else { var start = new int[m]; for (int j = 0; j < m; j++) start[j] = m - 1 - j; faces.Add(new BrushPolyhedron.Face(start) { group = 0 }); }
                if (capEnd) Domed(rings - 1, p[at[rings - 1]], (p[at[rings - 1]] - p[at[rings - 2]]).normalized, false, bands + 1);
                else { var end = new int[m]; for (int j = 0; j < m; j++) end[j] = (rings - 1) * m + j; faces.Add(new BrushPolyhedron.Face(end) { group = bands - 1 }); }
            }
            var poly = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            poly.EnsureOutward();
            if (poly.IsClosed() && poly.Volume() > 1e-5f) return poly;
            LastHullProblem = "whole sweep: open";
            return null;
        }

        /// <summary>
        /// A quad face, or two triangles where it isn't flat. <paramref name="group"/>: the convex block it bounds; the
        /// colliders take each block's hull as one piece, so a one-brush sweep collides as its convex pieces would.
        /// </summary>
        static void AddQuad(List<BrushPolyhedron.Face> faces, List<Vector3> verts, int[] quad, int group)
        {
            if (Planar(verts, quad)) faces.Add(new BrushPolyhedron.Face(quad) { group = group });
            else
            {
                faces.Add(new BrushPolyhedron.Face(new[] { quad[0], quad[1], quad[2] }) { group = group });
                faces.Add(new BrushPolyhedron.Face(new[] { quad[0], quad[2], quad[3] }) { group = group });
            }
        }

        static bool Planar(List<Vector3> v, int[] quad)
        {
            var n = Vector3.Cross(v[quad[1]] - v[quad[0]], v[quad[2]] - v[quad[0]]) + Vector3.Cross(v[quad[2]] - v[quad[0]], v[quad[3]] - v[quad[0]]);
            if (n.sqrMagnitude < 1e-12f) return false;
            n.Normalize();
            return Mathf.Abs(Vector3.Dot(v[quad[3]] - v[quad[0]], n)) < BrushPolyhedron.Epsilon && Mathf.Abs(Vector3.Dot(v[quad[1]] - v[quad[0]], n)) < BrushPolyhedron.Epsilon;
        }

        void Sweep(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, float reach, BrushOperation op, string name, ref int count, List<BrushSpec> into)
        {
            int n = p.Count, pieces = frames.Count;
            var rings = new Vector3[n][];
            for (int i = 0; i < n; i++) rings[i] = Ring(p, frames, closed, part, i);
            for (int k = 0; k < pieces; k++)
            {
                if ((p[(k + 1) % n] - p[k]).sqrMagnitude < 1e-10f) continue;
                var poly = Hull(rings[k], rings[(k + 1) % n]);
                if (poly == null) { poly = Unmitred(p, frames, closed, part, reach, k); unmitredPieces++; } // the mitres cross: a straight section that overlaps its neighbours
                if (poly == null) { droppedPieces++; continue; }
                into.Add(new BrushSpec { name = name + " " + (++count), operation = op, polyhedron = poly, material = material });
            }
        }

        /// <summary>
        /// The convex hull of two rings of a convex profile (at a piece's two ends). Neighbouring pieces share a ring, which is
        /// a face of both, so they meet exactly; the hull is right however the rings are turned against each other (a path
        /// that rises or falls while it turns). Null when it comes out unsound.
        /// </summary>
        public static BrushPolyhedron Hull(Vector3[] a, Vector3[] b)
        {
            var pts = new List<Vector3>(a.Length + b.Length); pts.AddRange(a); pts.AddRange(b);
            return HullOf(pts);
        }

        /// <summary>The convex hull of any points, as a sound brush; null when it comes out flat or unsound.</summary>
        public static BrushPolyhedron HullOf(List<Vector3> pts)
        {
            // points that coincide once (a clip finds each crossing twice, from both faces of an edge): one, or the hull
            // can come out open
            {
                float s0 = 1f; foreach (var q in pts) s0 = Mathf.Max(s0, Mathf.Abs(q.x), Mathf.Abs(q.y), Mathf.Abs(q.z));
                float weld = Mathf.Max(1e-3f, 1e-5f * s0), weld2 = weld * weld; // a millimetre at least: far below the grid
                var unique = new List<Vector3>(pts.Count);
                foreach (var q in pts)
                {
                    bool seen = false;
                    foreach (var w in unique) if ((w - q).sqrMagnitude <= weld2) { seen = true; break; }
                    if (!seen) unique.Add(q);
                }
                pts = unique;
            }
            float scale = 1f; foreach (var q in pts) scale = Mathf.Max(scale, Mathf.Abs(q.x), Mathf.Abs(q.y), Mathf.Abs(q.z));
            var faces = CsgBrush.Colliders.ConvexPolytope.ConvexHullFaces(pts, 1e-5f * scale);
            if (faces == null) { LastHullProblem = "flat"; return null; }
            // only the points on the hull, in their first order
            var map = new Dictionary<int, int>(); var verts = new List<Vector3>();
            var polys = new BrushPolyhedron.Face[faces.Count];
            for (int f = 0; f < faces.Count; f++)
            {
                var idx = new int[faces[f].Length];
                for (int k = 0; k < idx.Length; k++)
                {
                    if (!map.TryGetValue(faces[f][k], out int m)) { m = verts.Count; verts.Add(pts[faces[f][k]]); map[faces[f][k]] = m; }
                    idx[k] = m;
                }
                polys[f] = new BrushPolyhedron.Face(idx);
            }
            var poly = new BrushPolyhedron { vertices = verts.ToArray(), faces = polys };
            if (!poly.IsClosed()) MendEdges(poly, 1e-4f * scale);
            poly.EnsureOutward();
            if (poly.IsSound(out var why)) return poly;
            LastHullProblem = why;
            return null;
        }

        /// <summary>
        /// A hull's faces given every vertex that lies on one of their edges: where coplanar points merge into one face,
        /// a point on an edge is a corner of the face on one side but not of the face on the other, and the hull is open.
        /// </summary>
        /// <summary>
        /// A convex brush cut to the side of a plane where <c>dot(q - at, direction)</c> is at most <paramref name="limit"/>;
        /// null when nothing is left. Exact: each face cut by the plane, the cut closed by a face through the crossings.
        /// </summary>
        static BrushPolyhedron Clip(BrushPolyhedron poly, Vector3 at, Vector3 direction, float limit)
        {
            var v = poly.vertices; var d = new float[v.Length];
            bool any = false, all = true;
            for (int i = 0; i < v.Length; i++)
            {
                d[i] = Vector3.Dot(v[i] - at, direction) - limit;
                if (Mathf.Abs(d[i]) < 1e-6f) d[i] = 0f;
                if (d[i] > 0f) any = true; else all = false;
            }
            if (!any) return poly;
            if (all) return null;
            var verts = new List<Vector3>(); var faces = new List<BrushPolyhedron.Face>();
            int Weld(Vector3 p)
            {
                for (int i = 0; i < verts.Count; i++) if ((verts[i] - p).sqrMagnitude < 1e-10f) return i;
                verts.Add(p); return verts.Count - 1;
            }
            var cap = new List<int>();
            foreach (var f in poly.faces)
            {
                var kept = new List<int>(f.indices.Length + 2);
                for (int k = 0; k < f.indices.Length; k++)
                {
                    int i = f.indices[k], j = f.indices[(k + 1) % f.indices.Length];
                    if (d[i] <= 0f) kept.Add(Weld(v[i]));
                    if (d[i] == 0f) { cap.Add(Weld(v[i])); continue; }
                    if ((d[i] < 0f && d[j] > 0f) || (d[i] > 0f && d[j] < 0f))
                    {
                        int x = Weld(Vector3.Lerp(v[i], v[j], d[i] / (d[i] - d[j])));
                        kept.Add(x); cap.Add(x);
                    }
                }
                var loop = new List<int>(kept.Count);
                foreach (int i in kept) if (loop.Count == 0 || loop[loop.Count - 1] != i) loop.Add(i);
                if (loop.Count > 1 && loop[0] == loop[loop.Count - 1]) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3) faces.Add(new BrushPolyhedron.Face(loop.ToArray()) { group = f.group });
            }
            var unique = new List<int>(); foreach (int i in cap) if (!unique.Contains(i)) unique.Add(i);
            if (unique.Count >= 3)
            {
                var centre = Vector3.zero; foreach (int i in unique) centre += verts[i]; centre /= unique.Count;
                var u = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(direction, u);
                unique.Sort((i, j) => Mathf.Atan2(Vector3.Dot(verts[i] - centre, w), Vector3.Dot(verts[i] - centre, u)).CompareTo(Mathf.Atan2(Vector3.Dot(verts[j] - centre, w), Vector3.Dot(verts[j] - centre, u))));
                faces.Add(new BrushPolyhedron.Face(unique.ToArray()));
            }
            var result = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            if (!result.IsClosed()) MendEdges(result, 1e-4f);
            result.EnsureOutward();
            if (result.IsClosed() && result.IsSound(out _)) return result;
            return HullOf(new List<Vector3>(verts)); // (the cap ordered by angle only: wound wrong, a hull of the same corners)
        }

        static void MendEdges(BrushPolyhedron poly, float eps)
        {
            var v = poly.vertices;
            for (int f = 0; f < poly.faces.Length; f++)
            {
                var idx = poly.faces[f].indices; var mended = new List<int>(idx.Length + 2);
                for (int k = 0; k < idx.Length; k++)
                {
                    int a = idx[k], b = idx[(k + 1) % idx.Length];
                    mended.Add(a);
                    var ab = v[b] - v[a]; float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-12f) continue;
                    var between = new List<(float t, int i)>();
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (i == a || i == b) continue;
                        float t = Vector3.Dot(v[i] - v[a], ab) / len2;
                        if (t <= 1e-4f || t >= 1f - 1e-4f) continue;
                        if ((v[a] + ab * t - v[i]).sqrMagnitude <= eps * eps) between.Add((t, i));
                    }
                    between.Sort((x, y) => x.t.CompareTo(y.t));
                    foreach (var (_, i) in between) mended.Add(i);
                }
                poly.faces[f] = new BrushPolyhedron.Face(mended.ToArray()) { group = poly.faces[f].group };
            }
        }

        /// <summary>The hull of two rings by pairing their points in order: quads, or triangle pairs split to stay convex. Kept for reference.</summary>
        static BrushPolyhedron HullByPairs(Vector3[] a, Vector3[] b)
        {
            int m = a.Length;
            var v = new Vector3[m * 2];
            for (int i = 0; i < m; i++) { v[i] = a[i]; v[m + i] = b[i]; }
            var centre = Vector3.zero; foreach (var q in v) centre += q; centre /= v.Length;
            var faces = new List<BrushPolyhedron.Face>();
            var capA = new int[m]; var capB = new int[m];
            for (int i = 0; i < m; i++) { capA[i] = m - 1 - i; capB[i] = m + i; } // cap a backwards: every edge is walked both ways
            faces.Add(new BrushPolyhedron.Face(capA)); faces.Add(new BrushPolyhedron.Face(capB));
            for (int i = 0; i < m; i++)
            {
                int j = (i + 1) % m;
                int a0 = i, a1 = j, b1 = m + j, b0 = m + i;
                // the side a0 a1 b1 b0: flat enough is one face, else the diagonal whose other corner lies inside
                var nrm = Vector3.Cross(v[a1] - v[a0], v[b1] - v[a0]);
                if (Vector3.Dot(nrm, v[a0] - centre) < 0f) nrm = -nrm;
                float off = Vector3.Dot(nrm.normalized, v[b0] - v[a0]);
                float scale = Mathf.Max((v[a1] - v[a0]).magnitude, (v[b0] - v[a0]).magnitude, 1e-4f);
                if (Mathf.Abs(off) < 1e-5f * scale) faces.Add(new BrushPolyhedron.Face(new[] { a0, a1, b1, b0 }));
                else if (off <= 0f) { faces.Add(new BrushPolyhedron.Face(new[] { a0, a1, b1 })); faces.Add(new BrushPolyhedron.Face(new[] { a0, b1, b0 })); }
                else { faces.Add(new BrushPolyhedron.Face(new[] { a0, a1, b0 })); faces.Add(new BrushPolyhedron.Face(new[] { a1, b1, b0 })); }
            }
            var poly = new BrushPolyhedron { vertices = v, faces = faces.ToArray() };
            poly.EnsureOutward();
            if (poly.IsSound(out var why)) return poly;
            LastHullProblem = why;
            return null;
        }

        /// <summary>Why the last hull that came out unsound was refused (for diagnosis).</summary>
        public static string LastHullProblem;
    }
}

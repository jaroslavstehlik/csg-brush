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
            [Tooltip("A box section: beams, rails, roads, walls.")] Rectangle,
            [Tooltip("An open channel facing up: gutters, half-pipes, troughs.")] UChannel,
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
        }

        [Tooltip("The shape swept along the path.")] public Profile profile = Profile.Circle;
        [Tooltip("Metres: width and height of the profile (a circle's diameter is the width).")] public Vector2 size = DefaultSize;
        /// <summary>A new spline's profile size, metres.</summary>
        public static readonly Vector2 DefaultSize = new Vector2(0.5f, 0.5f);
        [Tooltip("Sides of a circle; 0 works them out from the curve tolerance.")] public int sides = 0;
        [Tooltip("Cut out the inside, leaving a wall of the thickness below.")] public bool hollow;
        [Tooltip("Metres: the wall of a hollow profile or a channel.")] public float thickness = 0.05f;
        [Tooltip("Whether a hollow profile or a channel is open or capped where the path ends.")] public Ends ends = Ends.Open;
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

        /// <summary>Does the profile have an inside to cut: a hollow one, or a channel (a block with its channel cut out)?</summary>
        public bool IsHollow => profile == Profile.UChannel || hollow;

        protected override void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y));
            sides = Mathf.Clamp(sides, 0, 64);
            thickness = Mathf.Clamp(thickness, 0.001f, Mathf.Min(size.x, size.y) * 0.5f - 0.001f);
            base.OnValidate();
        }

        public override int Key()
        {
            EnsureGraph();
            unchecked
            {
                int h = GraphKey(23);
                h = h * 31 + (int)profile; h = h * 31 + Mathf.RoundToInt(size.x * 1e5f); h = h * 31 + Mathf.RoundToInt(size.y * 1e5f);
                h = h * 31 + sides; h = h * 31 + (hollow ? 1 : 0); h = h * 31 + Mathf.RoundToInt(thickness * 1e5f);
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
            if (profile == Profile.UChannel)
            {
                // a channel is a block with the channel cut out, open at the top: crossing channels open into each other
                float w0 = size.x, h0 = size.y;
                if (!inner) { parts.Add(Rect(-w0 * 0.5f, -h0 * 0.5f, w0 * 0.5f, h0 * 0.5f)); return parts; }
                if (w0 - 2f * t <= 1e-3f || h0 - t <= 1e-3f) return parts;
                parts.Add(Rect(-w0 * 0.5f + t, -h0 * 0.5f + t, w0 * 0.5f - t, h0 * (0.5f + OpenTop) + 0.01f));
                return parts;
            }
            switch (profile)
            {
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
        }

        /// <summary>The most the roll changes along one piece, degrees: a link whose ends roll differently is cut finer.</summary>
        public const float MaxRollStep = 10f;

        /// <summary>A corner turning more than this breaks the path: the two sides overlap instead of a spiking mitre.</summary>
        public const float SharpTurnDegrees = 120f;

        /// <summary>
        /// The path cut into runs. A point with one link ends a run; a point with three or more links breaks every run
        /// there, except that at a Smooth point the most nearly opposite pair runs through it; a Corner sharper than
        /// <see cref="SharpTurnDegrees"/> breaks it too. A loop with no break is one closed run.
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
                    if (!IsSmooth(atId) && TurnDegrees(link, other, atId) > SharpTurnDegrees) return -1;
                    if (!IsSmooth(atId) && orientation == Orientation.Upright && UprightTwist(-Away(link, atId), Away(other, atId)) > MaxTwistDegrees) return -1;
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
                    var run = new Run { startIsEnd = linksAt[startId].Count == 1 };
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
                        if (next < 0) { run.endIsEnd = linksAt[at].Count == 1; break; }
                        if (used[next]) { run.closed = pass == 1 && at == startId; break; }
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

        /// <summary>
        /// A corner sharing one mitre between two Upright pieces twists one of them by this much (degrees): how far the level
        /// frames on its two sides differ from the corner's own bend. Large at a corner that climbs or falls while it turns.
        /// </summary>
        static float UprightTwist(Vector3 arriving, Vector3 leaving)
        {
            Vector3 Up(Vector3 d) { var r = Vector3.Cross(Vector3.up, d); if (r.sqrMagnitude < 1e-6f) return Vector3.up; return Vector3.Cross(d, r.normalized).normalized; }
            var carried = Quaternion.FromToRotation(arriving, leaving) * Up(arriving);
            return Mathf.Abs(Vector3.SignedAngle(carried, Up(leaving), leaving));
        }

        /// <summary>
        /// An Upright corner twisting a piece more than this breaks the path instead: each side keeps its own profile and runs
        /// on by half its width past the corner, so the two overlap there and every piece is a true slice of its profile.
        /// </summary>
        public const float MaxTwistDegrees = 5f;

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
            int piece = 0;
            var cuts = new List<BrushSpec>();
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                // solid: on past a junction or sharp corner, so the branches overlap
                var solid = Extended(run, run.startIsEnd ? 0f : reachOut, run.endIsEnd ? 0f : reachOut);
                var frames = Frames(solid, run.closed, run.rolls);
                foreach (var part in outer) Sweep(solid, frames, run.closed, part, reachOut, BrushOperation.Add, "Piece", ref piece, into);
                if (inner.Count == 0) continue;
                // inside: to the middle of junctions; past an open end by a hair, short of a closed one by the wall
                float s = run.startIsEnd ? (ends == Ends.Open ? Hair : -thickness) : reachIn;
                float e = run.endIsEnd ? (ends == Ends.Open ? Hair : -thickness) : reachIn;
                var hole = Extended(run, s, e);
                var holeFrames = Frames(hole, run.closed, run.rolls);
                foreach (var part in inner) Sweep(hole, holeFrames, run.closed, part, reachIn, BrushOperation.Subtract, "Inside", ref piece, cuts);
            }
            into.AddRange(cuts); // every inside after every solid piece: branches open into each other
        }

        /// <summary>A run's points with its ends moved along the path: out past the end (positive) or back in (negative).</summary>
        static List<Vector3> Extended(Run run, float atStart, float atEnd)
        {
            var p = new List<Vector3>(run.points);
            if (run.closed || p.Count < 2) return p;
            Move(p, 0, 1, atStart);
            Move(p, p.Count - 1, p.Count - 2, atEnd);
            return p;
        }

        static void Move(List<Vector3> p, int end, int neighbour, float by)
        {
            var d = p[end] - p[neighbour]; float len = d.magnitude;
            if (len < 1e-6f || Mathf.Abs(by) < 1e-6f) return;
            by = Mathf.Max(by, -len * 0.5f); // never past the middle of the last piece
            p[end] += d / len * by;
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
            poly.EnsureOutward();
            if (poly.IsSound(out var why)) return poly;
            LastHullProblem = why;
            return null;
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

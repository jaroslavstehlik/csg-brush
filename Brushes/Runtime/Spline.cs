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
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/FloorPlan.png")]
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
        [Tooltip("Metres: width and height of the profile (a circle's diameter is the width).")] public Vector2 size = new Vector2(0.5f, 0.5f);
        [Tooltip("Sides of a circle; 0 works them out from the curve tolerance.")] public int sides = 0;
        [Tooltip("Cut out the inside, leaving a wall of the thickness below.")] public bool hollow;
        [Tooltip("Metres: the wall of a hollow profile or a channel.")] public float thickness = 0.05f;
        [Tooltip("Whether a hollow profile is open or capped where the path ends.")] public Ends ends = Ends.Open;
        [Tooltip("How the profile turns along the path.")] public Orientation orientation = Orientation.Follow;
        [Tooltip("Empty uses the project's default material.")] public Material material;

        public override bool Flat => false;

        /// <summary>Does the profile have an inside to cut (a channel is open already)?</summary>
        public bool IsHollow => hollow && profile != Profile.UChannel;

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
                case Profile.UChannel:
                    if (inner) break;
                    float x0 = -w * 0.5f, x1 = w * 0.5f, y0 = -hgt * 0.5f, y1 = hgt * 0.5f;
                    parts.Add(Rect(x0, y0, x1, y0 + t));     // the bottom
                    parts.Add(Rect(x0, y0 + t, x0 + t, y1)); // the sides, standing on it
                    parts.Add(Rect(x1 - t, y0 + t, x1, y1));
                    break;
            }
            return parts;
        }

        static Vector2[] Rect(float x0, float y0, float x1, float y1) => new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };

        /// <summary>How far the profile reaches from the path: branches overlap by this much where they meet.</summary>
        float Reach(bool inner)
        {
            float r = 0f;
            foreach (var part in ProfileParts(inner)) foreach (var p in part) r = Mathf.Max(r, p.magnitude);
            return r;
        }

        // ------------------------------------------------------------------ runs

        /// <summary>A continuous stretch of the path, swept in one go: from an end or a break to the next.</summary>
        public sealed class Run
        {
            public readonly List<Vector3> points = new List<Vector3>();
            /// <summary>The links it runs along, in order: which way, and the indices of their first and last points in <see cref="points"/> (the last may be <c>points.Count</c> on a closed run: its first point again).</summary>
            public readonly List<(int link, bool forward, int from, int to)> spans = new List<(int, bool, int, int)>();
            public bool closed;
            /// <summary>What each end of an open run is: a free end of the path (true), or a junction or sharp corner.</summary>
            public bool startIsEnd, endIsEnd;
        }

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
                        for (int k = 0; k < path.Length; k++)
                        {
                            var q = path[forward ? k : path.Length - 1 - k];
                            if (run.points.Count == 0 || (run.points[run.points.Count - 1] - q).sqrMagnitude > 1e-10f) run.points.Add(q);
                        }
                        run.spans.Add((link, forward, from, run.points.Count - 1));
                        at = Far(link, at);
                        int next = Next(link, at);
                        if (next < 0) { run.endIsEnd = linksAt[at].Count == 1; break; }
                        if (used[next]) { run.closed = pass == 1 && at == startId; break; }
                        link = next;
                    }
                    if (run.closed && run.points.Count > 1 && (run.points[0] - run.points[run.points.Count - 1]).sqrMagnitude < 1e-10f) run.points.RemoveAt(run.points.Count - 1); // the last span ends on points.Count: the first point
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
                    pose = OnRun(run, Frames(run.points, run.closed), span.from, span.to, d);
                    if (!span.forward) { pose.forward = -pose.forward; pose.right = -pose.right; } // facing the link's own way
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
            return OnRun(run, Frames(run.points, run.closed), 0, run.closed ? n : n - 1, Mathf.Clamp(distance, 0f, RunLength(run)));
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
                float t = seg > 1e-9f ? Mathf.Clamp01(d / seg) : 0f;
                pose.position = Vector3.Lerp(a, b, t);
                pose.forward = seg > 1e-9f ? (b - a) / seg : frames[i % n].forward;
                var up = Vector3.Lerp(frames[i % n].up, frames[(i + 1) % n].up, t);
                up -= pose.forward * Vector3.Dot(up, pose.forward);
                pose.up = up.sqrMagnitude > 1e-8f ? up.normalized : frames[i % n].up;
                pose.right = Vector3.Cross(pose.up, pose.forward).normalized;
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

        public override void Describe(List<BrushSpec> into)
        {
            var runs = Runs();
            var outer = ProfileParts(false); var inner = IsHollow ? ProfileParts(true) : new List<Vector2[]>();
            float reachOut = Reach(false), reachIn = Reach(true);
            const float Hair = 0.01f;
            int piece = 0;
            var cuts = new List<BrushSpec>();
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                // solid: on past a junction or sharp corner, so the branches overlap
                var solid = Extended(run, run.startIsEnd ? 0f : reachOut, run.endIsEnd ? 0f : reachOut);
                var frames = Frames(solid, run.closed);
                foreach (var part in outer) Sweep(solid, frames, run.closed, part, BrushOperation.Add, "Piece", ref piece, into);
                if (inner.Count == 0) continue;
                // inside: to the middle of junctions; past an open end by a hair, short of a closed one by the wall
                float s = run.startIsEnd ? (ends == Ends.Open ? Hair : -thickness) : reachIn;
                float e = run.endIsEnd ? (ends == Ends.Open ? Hair : -thickness) : reachIn;
                var hole = Extended(run, s, e);
                var holeFrames = Frames(hole, run.closed);
                foreach (var part in inner) Sweep(hole, holeFrames, run.closed, part, BrushOperation.Subtract, "Inside", ref piece, cuts);
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

        /// <summary>Per point: the joint's direction, the profile's right and up there, and the mitre (stretch across the turn).</summary>
        struct Frame { public Vector3 forward, right, up, bend; public float stretch; }

        List<Frame> Frames(List<Vector3> p, bool closed)
        {
            int n = p.Count;
            var frames = new List<Frame>(n);
            var seg = new Vector3[closed ? n : n - 1];
            for (int i = 0; i < seg.Length; i++) seg[i] = (p[(i + 1) % n] - p[i]).normalized;
            Vector3 lastRight = Vector3.right, lastUp = Vector3.up;
            for (int i = 0; i < n; i++)
            {
                Vector3 din = closed ? seg[(i - 1 + seg.Length) % seg.Length] : i > 0 ? seg[i - 1] : seg[0];
                Vector3 dout = closed ? seg[i] : i < seg.Length ? seg[i] : seg[seg.Length - 1];
                var sum = din + dout;
                var f = new Frame { forward = sum.sqrMagnitude > 1e-8f ? sum.normalized : dout, stretch = 1f };
                float half = sum.magnitude * 0.5f; // cos of half the turn
                if (half > 1e-3f && half < 0.99999f) { f.bend = (dout - din).normalized; f.stretch = 1f / half; }
                if (i == 0 || orientation == Orientation.Upright)
                {
                    var right = Vector3.Cross(Vector3.up, f.forward);
                    if (right.sqrMagnitude < 1e-6f) right = i == 0 ? Vector3.Cross(Vector3.forward, f.forward) : lastRight; // straight up or down
                    f.right = right.normalized;
                }
                else
                {
                    // Follow: the last frame turned by the smallest rotation onto this direction
                    var up = Quaternion.FromToRotation(frames[i - 1].forward, f.forward) * lastUp;
                    f.right = Vector3.Cross(up, f.forward).normalized;
                }
                f.up = Vector3.Cross(f.forward, f.right).normalized;
                lastRight = f.right; lastUp = f.up;
                frames.Add(f);
            }
            if (closed && orientation == Orientation.Follow && n > 2)
            {
                // round the loop the transported frame comes back turned: spread that turn evenly along it
                var back = Quaternion.FromToRotation(frames[n - 1].forward, frames[0].forward) * frames[n - 1].up;
                float twist = Vector3.SignedAngle(back, frames[0].up, frames[0].forward);
                for (int i = 1; i < n; i++)
                {
                    var f = frames[i]; var q = Quaternion.AngleAxis(twist * i / n, f.forward);
                    f.up = q * f.up; f.right = q * f.right; frames[i] = f;
                }
            }
            return frames;
        }

        Vector3[] Ring(Vector3 at, Frame f, Vector2[] part)
        {
            var ring = new Vector3[part.Length];
            for (int i = 0; i < part.Length; i++)
            {
                var o = f.right * part[i].x + f.up * part[i].y;
                if (f.stretch > 1f) o += f.bend * (Vector3.Dot(o, f.bend) * (f.stretch - 1f)); // the mitre: wider across the turn
                ring[i] = at + o;
            }
            return ring;
        }

        void Sweep(List<Vector3> p, List<Frame> frames, bool closed, Vector2[] part, BrushOperation op, string name, ref int count, List<BrushSpec> into)
        {
            int n = p.Count, pieces = closed ? n : n - 1;
            var rings = new Vector3[n][];
            for (int i = 0; i < n; i++) rings[i] = Ring(p[i], frames[i], part);
            for (int k = 0; k < pieces; k++)
            {
                if ((p[(k + 1) % n] - p[k]).sqrMagnitude < 1e-10f) continue;
                var poly = Hull(rings[k], rings[(k + 1) % n]);
                if (poly == null) continue;
                into.Add(new BrushSpec { name = name + " " + (++count), operation = op, polyhedron = poly, material = material });
            }
        }

        /// <summary>
        /// The convex hull of two rings of the same convex profile: the rings as end caps, each side a quad, or two triangles
        /// split along the diagonal that keeps the hull convex. Null when it comes out unsound.
        /// </summary>
        public static BrushPolyhedron Hull(Vector3[] a, Vector3[] b)
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
            return poly.IsSound(out _) ? poly : null;
        }
    }
}

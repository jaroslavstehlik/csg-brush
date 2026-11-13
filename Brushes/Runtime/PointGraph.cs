using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CsgBrush
{
    /// <summary>
    /// A generator drawn as points joined by links (see <see cref="BrushGenerator"/>): a floor plan's walls, a spline's
    /// path. A point may join any number of links, so the network can branch. Every point has an id that stays with it
    /// while others are added or removed, so anything that refers to a link (an object anchored on a wall, say) finds it
    /// again by the ids of its two ends. The draw and edit tools work on any point graph.
    /// </summary>
    public abstract class PointGraph : BrushGenerator
    {
        /// <summary>A link between two points, by their ids.</summary>
        [Serializable]
        public struct Link
        {
            public int start, end;
            /// <summary>Metres; 0 uses the generator's own (a floor plan's wall thickness).</summary>
            public float thickness;
            public Link(int start, int end, float thickness = 0f) { this.start = start; this.end = end; this.thickness = thickness; }
        }

        /// <summary>The points, in this object's space. Edit through the point and link methods, which keep the ids in step.</summary>
        [HideInInspector] public List<Vector3> points = new List<Vector3>();
        /// <summary>One id per point that stays with the point while others are added or removed. Never reused.</summary>
        [HideInInspector] public List<int> pointIds = new List<int>();
        [HideInInspector] public int nextPointId = 1;
        /// <summary>The links, by the ids of their two points.</summary>
        [HideInInspector, FormerlySerializedAs("walls")] public List<Link> links = new List<Link>();

        /// <summary>Ids of the Smooth points: the path curves through them. Every other point is a Corner.</summary>
        [HideInInspector] public List<int> smoothPoints = new List<int>();
        /// <summary>Use <see cref="tolerance"/> for this object instead of the project's curve tolerance.</summary>
        [HideInInspector] public bool overrideTolerance;
        /// <summary>Metres: how far a curve's straight pieces may stray from the true curve, when overridden.</summary>
        [HideInInspector] public float tolerance = 0.1f;

        /// <summary>The project's curve tolerance in metres (set by the editor's settings).</summary>
        public static float ProjectTolerance = 0.1f;
        /// <summary>The finest tolerance allowed, metres.</summary>
        public const float MinTolerance = 0.01f;
        /// <summary>A curved link is never cut into more pieces than this.</summary>
        public const int MaxPiecesPerLink = 64;

        /// <summary>The tolerance curves are sampled with: the object's override, else the project's.</summary>
        public float Tolerance => Mathf.Max(MinTolerance, overrideTolerance ? tolerance : ProjectTolerance);

        /// <summary>Points lie on this object's floor (y is ignored), as a floor plan's do.</summary>
        public virtual bool Flat => false;

        /// <summary>Does the path curve through the point with this id?</summary>
        public bool IsSmooth(int id) => smoothPoints.Contains(id);

        /// <summary>Make a point (by id) Smooth or a Corner.</summary>
        public void SetSmooth(int id, bool smooth)
        {
            if (smooth) { if (!smoothPoints.Contains(id)) smoothPoints.Add(id); }
            else smoothPoints.Remove(id);
        }

        /// <summary>A point as the generator uses it: on the floor for a flat graph.</summary>
        public Vector3 Point(int index) { var p = points[index]; if (Flat) p.y = 0f; return p; }

        /// <summary>Data saved before the graph existed, turned into points and links once (see <see cref="EnsureGraph"/>).</summary>
        protected virtual void ConvertLegacy() { }

        /// <summary>Give every point an id, convert older data, and drop links that join nothing.</summary>
        public void EnsureGraph()
        {
            while (pointIds.Count > points.Count) pointIds.RemoveAt(pointIds.Count - 1);
            while (pointIds.Count < points.Count) pointIds.Add(nextPointId++);
            ConvertLegacy();
            var seen = new HashSet<long>();
            for (int i = links.Count - 1; i >= 0; i--)
            {
                var l = links[i];
                long key = ((long)Mathf.Min(l.start, l.end) << 32) | (uint)Mathf.Max(l.start, l.end);
                if (l.start == l.end || PointIndex(l.start) < 0 || PointIndex(l.end) < 0 || !seen.Add(key)) links.RemoveAt(i);
            }
            if (smoothPoints == null) smoothPoints = new List<int>();
            var known = new HashSet<int>();
            for (int i = smoothPoints.Count - 1; i >= 0; i--) if (PointIndex(smoothPoints[i]) < 0 || !known.Add(smoothPoints[i])) smoothPoints.RemoveAt(i);
        }

        /// <summary>The index of the point with an id, or -1.</summary>
        public int PointIndex(int id) { for (int i = 0; i < pointIds.Count; i++) if (pointIds[i] == id) return i; return -1; }

        /// <summary>A new point (on the floor for a flat graph); returns its id.</summary>
        public int AddPoint(Vector3 p) { EnsureGraph(); if (Flat) p.y = 0f; points.Add(p); pointIds.Add(nextPointId); return nextPointId++; }

        /// <summary>A link between two points; returns its index, or -1 when they are one point or already joined.</summary>
        public int AddLink(int startId, int endId, float thickness = 0f)
        {
            if (startId == endId || PointIndex(startId) < 0 || PointIndex(endId) < 0) return -1;
            foreach (var l in links) if ((l.start == startId && l.end == endId) || (l.start == endId && l.end == startId)) return -1;
            links.Add(new Link(startId, endId, thickness));
            return links.Count - 1;
        }

        /// <summary>
        /// Split a link with a new point; both halves keep its thickness. On a curved link (a Smooth end) the new point is
        /// Smooth, so the path keeps its shape. Returns the new point's id.
        /// </summary>
        public int SplitLink(int link, Vector3 p)
        {
            var l = links[link];
            int id = AddPoint(p);
            if (IsSmooth(l.start) || IsSmooth(l.end)) SetSmooth(id, true);
            links[link] = new Link(l.start, id, l.thickness);
            links.Insert(link + 1, new Link(id, l.end, l.thickness));
            return id;
        }

        /// <summary>Remove a point and its links.</summary>
        public void RemovePointAt(int index)
        {
            EnsureGraph();
            int id = pointIds[index];
            points.RemoveAt(index); pointIds.RemoveAt(index);
            links.RemoveAll(l => l.start == id || l.end == id);
        }

        /// <summary>Remove points no link uses.</summary>
        public void RemoveLonePoints()
        {
            var used = new HashSet<int>();
            foreach (var l in links) { used.Add(l.start); used.Add(l.end); }
            for (int i = points.Count - 1; i >= 0; i--) if (!used.Contains(pointIds[i])) { points.RemoveAt(i); pointIds.RemoveAt(i); }
        }

        /// <summary>Replace the points; points that carry over pass their ids (null ids: all new). Links to points that are gone are dropped.</summary>
        public void SetPoints(List<Vector3> newPoints, List<int> ids)
        {
            points = new List<Vector3>(newPoints);
            pointIds = ids != null ? new List<int>(ids) : new List<int>();
            foreach (var id in pointIds) if (id >= nextPointId) nextPointId = id + 1;
            EnsureGraph();
        }

        /// <summary>The indices of a link's two points.</summary>
        public void LinkPoints(int link, out int a, out int b) { a = PointIndex(links[link].start); b = PointIndex(links[link].end); }

        /// <summary>Hash of the points and links, for <see cref="BrushGenerator.Key"/>.</summary>
        protected int GraphKey(int h)
        {
            unchecked
            {
                for (int i = 0; i < points.Count; i++)
                {
                    h = h * 31 + pointIds[i]; h = h * 31 + Mathf.RoundToInt(points[i].x * 1e5f);
                    if (!Flat) h = h * 31 + Mathf.RoundToInt(points[i].y * 1e5f);
                    h = h * 31 + Mathf.RoundToInt(points[i].z * 1e5f);
                }
                foreach (var l in links) { h = h * 31 + l.start; h = h * 31 + l.end; h = h * 31 + Mathf.RoundToInt(l.thickness * 1e5f); }
                if (smoothPoints != null && smoothPoints.Count > 0) // nothing curves without them: older keys stay as they were
                {
                    var ids = new List<int>(smoothPoints); ids.Sort();
                    foreach (var id in ids) h = h * 31 + id;
                    h = h * 31 + Mathf.RoundToInt(Tolerance * 1e5f);
                }
                return h;
            }
        }

        // ------------------------------------------------------------------ finding a link again

        /// <summary>
        /// A link split by points added since (ids from <paramref name="newPointsFrom"/> on), straight or bent: the shortest
        /// run of links from <paramref name="startId"/> to <paramref name="endId"/> through such points, and the link a
        /// distance along that run reaches, with the distance on it (from that link's start). False when there is no run.
        /// </summary>
        public bool FollowSplit(int startId, int endId, float distance, int newPointsFrom, out int link, out float along)
        {
            link = -1; along = distance;
            if (newPointsFrom < 0) return false; // which points are new is not known
            var reachedBy = new Dictionary<int, int> { [startId] = -1 }; // point id -> the link it was reached along
            var queue = new Queue<int>(); queue.Enqueue(startId);
            while (queue.Count > 0 && !reachedBy.ContainsKey(endId))
            {
                int p = queue.Dequeue();
                for (int i = 0; i < links.Count; i++)
                {
                    var l = links[i];
                    int q = l.start == p ? l.end : l.end == p ? l.start : -1;
                    if (q < 0 || reachedBy.ContainsKey(q) || (q != endId && q < newPointsFrom)) continue;
                    reachedBy[q] = i; queue.Enqueue(q);
                }
            }
            if (!reachedBy.ContainsKey(endId)) return false;
            var run = new List<int>();
            for (int p = endId; reachedBy[p] >= 0;) { int i = reachedBy[p]; run.Add(i); p = links[i].start == p ? links[i].end : links[i].start; }
            run.Reverse();
            float before = 0f; int at = startId;
            for (int k = 0; k < run.Count; k++)
            {
                var l = links[run[k]]; bool forward = l.start == at; float len = CurveLength(run[k]);
                if (distance <= before + len || k == run.Count - 1)
                {
                    link = run[k]; float d = distance - before;
                    along = forward ? d : len - d;
                    return true;
                }
                before += len; at = forward ? l.end : l.start;
            }
            return false;
        }

        // ------------------------------------------------------------------ curves

        [NonSerialized] Vector3[][] m_Curves;
        [NonSerialized] int m_CurvesKey;

        /// <summary>
        /// A link's path from its start point to its end, in this object's space: its two ends for a straight link (both ends
        /// Corners), else the curve cut into straight pieces that stay within <see cref="Tolerance"/> of it. Shared: do not
        /// modify.
        /// </summary>
        public Vector3[] Curve(int link)
        {
            int key = GraphKey(17);
            if (m_Curves == null || m_CurvesKey != key || m_Curves.Length != links.Count)
            {
                m_Curves = new Vector3[links.Count][];
                m_CurvesKey = key;
            }
            return m_Curves[link] ??= SampleLink(link);
        }

        /// <summary>A link's length along its path.</summary>
        public float CurveLength(int link) => PathLength(Curve(link));

        /// <summary>A place on a link's path a distance from its start (clamped to the path), and the path's direction there.</summary>
        public void CurvePoint(int link, float distance, out Vector3 point, out Vector3 direction) => PathPoint(Curve(link), distance, out point, out direction);

        public static float PathLength(Vector3[] path) { float l = 0f; for (int i = 0; i + 1 < path.Length; i++) l += (path[i + 1] - path[i]).magnitude; return l; }

        /// <summary>A place on a polyline a distance from its start (clamped), and its direction there.</summary>
        public static void PathPoint(Vector3[] path, float distance, out Vector3 point, out Vector3 direction)
        {
            direction = path.Length > 1 ? (path[1] - path[0]).normalized : Vector3.forward;
            point = path[0];
            if (distance <= 0f || path.Length < 2) return;
            for (int i = 0; i + 1 < path.Length; i++)
            {
                var d = path[i + 1] - path[i]; float len = d.magnitude;
                if (len < 1e-9f) continue;
                direction = d / len;
                if (distance <= len || i + 2 == path.Length) { point = path[i] + direction * Mathf.Min(distance, len); return; }
                distance -= len;
            }
        }

        Vector3[] SampleLink(int link)
        {
            var l = links[link];
            int ia = PointIndex(l.start), ib = PointIndex(l.end);
            var a = Point(ia); var b = Point(ib);
            float length = (b - a).magnitude;
            bool sa = IsSmooth(l.start), sb = IsSmooth(l.end);
            if ((!sa && !sb) || length < 1e-6f) return new[] { a, b };
            var ta = Tangent(link, true, a, b);   // leaving a toward b
            var tb = -Tangent(link, false, b, a); // arriving at b
            var c1 = a + ta * (length / 3f); var c2 = b - tb * (length / 3f);
            float tol = Tolerance, minPiece = tol;
            var path = new List<Vector3> { a };
            void Split(float t0, Vector3 p0, float t1, Vector3 p1, int depth)
            {
                float chord = (p1 - p0).magnitude;
                bool flat = true;
                if (depth < 6 && chord >= 2f * minPiece) // 2^6 = MaxPiecesPerLink
                    for (int k = 1; k <= 3 && flat; k++)
                    {
                        var q = Bezier(a, c1, c2, b, Mathf.Lerp(t0, t1, k * 0.25f));
                        if (DistanceToSegment(q, p0, p1) > tol) flat = false;
                    }
                if (flat) { path.Add(p1); return; }
                float tm = (t0 + t1) * 0.5f; var pm = Bezier(a, c1, c2, b, tm);
                Split(t0, p0, tm, pm, depth + 1);
                Split(tm, pm, t1, p1, depth + 1);
            }
            Split(0f, a, 1f, b, 0);
            return path.ToArray();
        }

        /// <summary>
        /// The direction a link leaves one of its points (<paramref name="from"/>, toward <paramref name="to"/>): straight at a
        /// Corner or an end; at a Smooth point, along the line through it from the link most nearly opposite. Where three or
        /// more links meet, only the most nearly opposite pair runs through smoothly; the others meet it as at a corner.
        /// </summary>
        Vector3 Tangent(int link, bool atStart, Vector3 from, Vector3 to)
        {
            var l = links[link];
            int id = atStart ? l.start : l.end;
            var leaving = (to - from).normalized;
            if (!IsSmooth(id)) return leaving;
            var others = new List<(int link, Vector3 dir)>();
            var all = new List<(int link, Vector3 dir)>();
            for (int i = 0; i < links.Count; i++)
            {
                var o = links[i];
                int other = o.start == id ? o.end : o.end == id ? o.start : -1;
                if (other < 0) continue;
                int oi = PointIndex(other); if (oi < 0) continue;
                var d = (Point(oi) - from).normalized;
                all.Add((i, d));
                if (i != link) others.Add((i, d));
            }
            if (others.Count == 0) return leaving; // an end
            if (all.Count >= 3)
            {
                // the most nearly opposite pair among all the links here: this link must be one of them
                int bi = -1, bj = -1; float best = float.MaxValue;
                for (int i = 0; i < all.Count; i++) for (int j = i + 1; j < all.Count; j++) { float dot = Vector3.Dot(all[i].dir, all[j].dir); if (dot < best) { best = dot; bi = i; bj = j; } }
                int partner = all[bi].link == link ? all[bj].link : all[bj].link == link ? all[bi].link : -1;
                if (partner < 0) return leaving;
                others.Clear(); foreach (var x in all) if (x.link == partner) others.Add(x);
            }
            var arriving = -others[0].dir; // continuing through from the partner
            var through = arriving + leaving;
            return through.sqrMagnitude < 1e-8f ? leaving : through.normalized;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (a + ab * t - p).magnitude;
        }
    }
}

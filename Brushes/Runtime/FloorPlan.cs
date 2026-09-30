using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A floor plan: an outline drawn on the floor that becomes walls of one thickness and height, one brush per wall.
    /// The walls are generated (see <see cref="BrushGenerator"/>): change the outline and they follow. By default the
    /// drawn line is a closed room's inner face, so what you draw is the room's inside. Door and window brushes under it
    /// ride on its walls (see <see cref="WallAnchor"/>).
    /// </summary>
    [AddComponentMenu("CSG Brush/Floor Plan")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/FloorPlan.png")]
    [DisallowMultipleComponent]
    public sealed class FloorPlan : BrushGenerator
    {
        public enum Side
        {
            [Tooltip("A closed outline's walls stand outside it: the line is the room's inner face.")] Outside,
            [Tooltip("The walls are centred on the line.")] Centered,
            [Tooltip("The walls stand inside the outline.")] Inside,
        }

        /// <summary>The outline's corners, on the floor of this object (y is ignored). Edit through the point methods, which keep <see cref="pointIds"/> in step.</summary>
        [HideInInspector] public List<Vector3> points = new List<Vector3>();
        /// <summary>
        /// One id per point that stays with the point while others are added or removed: a wall is known by the ids of its
        /// two ends, so what rides on it (a door) finds it again. Never reused.
        /// </summary>
        [HideInInspector] public List<int> pointIds = new List<int>();
        [HideInInspector] public int nextPointId = 1;
        [Tooltip("The last point joins the first.")] public bool closed;
        [Tooltip("Metres; not snapped to the grid.")] public float wallThickness = 0.2f;
        [Tooltip("Metres.")] public float wallHeight = 3f;
        [Tooltip("Where the walls stand relative to the drawn line.")] public Side side = Side.Outside;

        // ------------------------------------------------------------------ points

        /// <summary>Give every point an id (points set directly, or data from before ids).</summary>
        public void EnsurePointIds()
        {
            while (pointIds.Count > points.Count) pointIds.RemoveAt(pointIds.Count - 1);
            while (pointIds.Count < points.Count) pointIds.Add(nextPointId++);
        }

        public void AddPoint(Vector3 p) { EnsurePointIds(); points.Add(p); pointIds.Add(nextPointId++); }
        public void InsertPoint(int index, Vector3 p) { EnsurePointIds(); points.Insert(index, p); pointIds.Insert(index, nextPointId++); }
        public void RemovePointAt(int index) { EnsurePointIds(); points.RemoveAt(index); pointIds.RemoveAt(index); }

        /// <summary>Replace the outline; points that carry over pass their ids (null ids: all new).</summary>
        public void SetPoints(List<Vector3> newPoints, List<int> ids)
        {
            points = new List<Vector3>(newPoints);
            pointIds = ids != null ? new List<int>(ids) : new List<int>();
            foreach (var id in pointIds) if (id >= nextPointId) nextPointId = id + 1;
            EnsurePointIds();
        }

        // ------------------------------------------------------------------ walls

        /// <summary>One wall of the plan, on its floor (x, z as a 2D point).</summary>
        public struct Wall
        {
            /// <summary>Index of the segment: from point <c>index</c> to the next.</summary>
            public int index;
            public int startId, endId;
            public Vector2 a, b;
            /// <summary>Unit direction from a to b, and the unit normal pointing out of the room (the wall stands on that side).</summary>
            public Vector2 direction, outward;
            /// <summary>Offsets of the wall's two faces from the line along <see cref="outward"/>.</summary>
            public float inner, outer;
            public float Length => (b - a).magnitude;
        }

        public override int Key()
        {
            unchecked
            {
                int h = 17;
                foreach (var p in points) { h = h * 31 + Mathf.RoundToInt(p.x * 1e5f); h = h * 31 + Mathf.RoundToInt(p.z * 1e5f); }
                h = h * 31 + (closed ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(wallThickness * 1e5f);
                h = h * 31 + Mathf.RoundToInt(wallHeight * 1e5f);
                h = h * 31 + (int)side;
                return h;
            }
        }

        protected override void OnValidate()
        {
            if (wallThickness < 0.001f) wallThickness = 0.001f;
            if (wallHeight < 0.001f) wallHeight = 0.001f;
            base.OnValidate();
        }

        /// <summary>The corners in use: on the floor, repeated points dropped (and a closing point equal to the first).</summary>
        public List<Vector2> Corners() { var c = new List<Vector2>(); CornersWithIds(c, null); return c; }

        void CornersWithIds(List<Vector2> corners, List<int> ids)
        {
            EnsurePointIds();
            for (int i = 0; i < points.Count; i++)
            {
                var q = new Vector2(points[i].x, points[i].z);
                if (corners.Count > 0 && (q - corners[corners.Count - 1]).sqrMagnitude <= 1e-8f) continue;
                corners.Add(q); ids?.Add(pointIds[i]);
            }
            if (closed && corners.Count > 2 && (corners[0] - corners[corners.Count - 1]).sqrMagnitude <= 1e-8f) { corners.RemoveAt(corners.Count - 1); ids?.RemoveAt(ids.Count - 1); }
        }

        /// <summary>The walls in drawing order.</summary>
        public void Walls(List<Wall> into)
        {
            into.Clear();
            var c = new List<Vector2>(); var ids = new List<int>();
            CornersWithIds(c, ids);
            bool loop = closed && c.Count > 2;
            int segments = loop ? c.Count : c.Count - 1;
            if (segments < 1) return;
            // outward: the right of the drawing direction for a counter-clockwise outline, the left for a clockwise one
            float area = 0f;
            for (int i = 0; i < c.Count; i++) { var p = c[i]; var q = c[(i + 1) % c.Count]; area += p.x * q.y - q.x * p.y; }
            float sign = loop && area < 0f ? -1f : 1f;
            float t = wallThickness;
            float inner = side == Side.Outside ? 0f : side == Side.Centered ? -0.5f * t : -t;
            for (int s = 0; s < segments; s++)
            {
                int j = (s + 1) % c.Count;
                var d = (c[j] - c[s]).normalized;
                into.Add(new Wall { index = s, startId = ids[s], endId = ids[j], a = c[s], b = c[j], direction = d, outward = new Vector2(d.y, -d.x) * sign, inner = inner, outer = inner + t });
            }
        }

        static readonly List<Wall> s_Walls = new List<Wall>();

        public override void Describe(List<BrushSpec> into)
        {
            Walls(s_Walls);
            int segments = s_Walls.Count;
            if (segments < 1) return;
            bool loop = closed && segments > 2 && s_Walls[segments - 1].endId == s_Walls[0].startId;
            // a corner's point on a line offset from the outline: mitred where two walls meet, square at an open end
            Vector2 Offset(int s, bool atEnd, float offset)
            {
                var w = s_Walls[s];
                var corner = atEnd ? w.b : w.a;
                int other = atEnd ? s + 1 : s - 1;
                if (loop) other = (other + segments) % segments;
                if (other < 0 || other >= segments) return corner + w.outward * offset;
                var n0 = w.outward; var n1 = s_Walls[other].outward;
                var m = n0 + n1;
                if (m.sqrMagnitude < 1e-8f) return corner + n0 * offset; // the outline doubles back
                m.Normalize();
                float cos = Mathf.Max(Vector2.Dot(m, n0), 0.25f); // very sharp corners: a bounded miter
                return corner + m * (offset / cos);
            }
            for (int s = 0; s < segments; s++)
            {
                var w = s_Walls[s];
                var quad = new[] { Offset(s, false, w.inner), Offset(s, true, w.inner), Offset(s, true, w.outer), Offset(s, false, w.outer) };
                var poly = Prism(quad, wallHeight);
                if (poly == null) continue;
                into.Add(new BrushSpec { name = "Wall " + (s + 1), operation = BrushOperation.Add, polyhedron = poly });
            }
        }

        /// <summary>A closed prism from a floor quad (x, z) up to a height; null when the quad folds over itself.</summary>
        public static BrushPolyhedron Prism(Vector2[] quad, float height)
        {
            // convex with a real area, or nothing
            float sign = 0f;
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i]; var b = quad[(i + 1) % 4]; var d = quad[(i + 2) % 4];
                float cross = (b.x - a.x) * (d.y - b.y) - (b.y - a.y) * (d.x - b.x);
                if (Mathf.Abs(cross) < 1e-9f) continue;
                if (sign == 0f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return null;
            }
            if (sign == 0f) return null;
            var v = new Vector3[8];
            for (int i = 0; i < 4; i++) { v[i] = new Vector3(quad[i].x, 0f, quad[i].y); v[i + 4] = new Vector3(quad[i].x, height, quad[i].y); }
            var faces = new[]
            {
                new BrushPolyhedron.Face(new[] { 0, 1, 2, 3 }), new BrushPolyhedron.Face(new[] { 7, 6, 5, 4 }),
                new BrushPolyhedron.Face(new[] { 0, 4, 5, 1 }), new BrushPolyhedron.Face(new[] { 1, 5, 6, 2 }),
                new BrushPolyhedron.Face(new[] { 2, 6, 7, 3 }), new BrushPolyhedron.Face(new[] { 3, 7, 4, 0 }),
            };
            var poly = new BrushPolyhedron { vertices = v, faces = faces };
            poly.EnsureOutward();
            return poly;
        }
    }
}

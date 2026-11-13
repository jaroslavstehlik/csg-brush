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

        /// <summary>Points lie on this object's floor (y is ignored), as a floor plan's do.</summary>
        public virtual bool Flat => false;

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

        /// <summary>Split a link with a new point; both halves keep its thickness. Returns the new point's id.</summary>
        public int SplitLink(int link, Vector3 p)
        {
            var l = links[link];
            int id = AddPoint(p);
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
                return h;
            }
        }
    }
}

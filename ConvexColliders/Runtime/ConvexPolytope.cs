using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// A convex solid defined as the intersection of half-spaces, the way a Quake brush is.
    /// Plane convention: (n.x, n.y, n.z, d) with n outward and a point p inside when dot(n, p) + d <= 0.
    ///
    /// Supports the operations brush-based CSG needs while staying convex: clipping by a plane,
    /// subtraction (A minus B becomes up to one convex piece per plane of B, the classic QuakeEd
    /// algorithm) and intersection. Vertices and faces are rebuilt from the planes on demand, so a
    /// polytope with redundant planes is still valid.
    /// </summary>
    public sealed class ConvexPolytope
    {
        public readonly List<Vector4> planes = new List<Vector4>();
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int[]> faces = new List<int[]>();
        /// <summary>Whatever the caller wants to carry along (source brush, surface kind).</summary>
        public object tag;

        public const float Epsilon = 1e-4f;
        /// <summary>
        /// Vertices closer than this are merged before the faces are built. Plane intersections of nearly parallel
        /// planes produce vertices a hair apart, and the hull then has faces of zero area, which CSG cannot
        /// categorise against (a wall next to such a cutter lost almost all its faces). Set it from the world grid.
        /// </summary>
        public float vertexWeld = Epsilon;
        /// <summary>Diagnostics: milliseconds spent in the last Build's clipping and hull steps.</summary>
        public static double LastClipMs, LastHullMs, LastLoopMs; public static int LastPolys, LastPoints, LastRetries, LastEdgeTests, LastMaxCap;
        bool built;

        public ConvexPolytope() { }

        public ConvexPolytope(IEnumerable<Vector4> planes, object tag = null, float vertexWeld = Epsilon)
        {
            foreach (var p in planes) AddPlane(p);
            this.tag = tag;
            this.vertexWeld = vertexWeld;
        }

        public ConvexPolytope Clone()
        {
            var c = new ConvexPolytope(planes, tag, vertexWeld);
            return c;
        }

        public void AddPlane(Vector4 plane)
        {
            Vector3 n = new Vector3(plane.x, plane.y, plane.z);
            float len = n.magnitude;
            if (len < 1e-8f) return;
            planes.Add(new Vector4(n.x / len, n.y / len, n.z / len, plane.w / len));
            built = false;
        }

        public void AddPlane(Vector3 normal, float d)
        {
            AddPlane(new Vector4(normal.x, normal.y, normal.z, d));
        }

        /// <summary>Plane through a point with an outward normal.</summary>
        public void AddPlane(Vector3 normal, Vector3 pointOnPlane)
        {
            normal.Normalize();
            AddPlane(new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, pointOnPlane)));
        }

        public static float Distance(Vector4 plane, Vector3 p)
        {
            return plane.x * p.x + plane.y * p.y + plane.z * p.z + plane.w;
        }

        public static Vector4 Flip(Vector4 plane)
        {
            return -plane;
        }

        /// <summary>Compute vertices and faces from the planes. Returns false if the solid is empty or degenerate.</summary>
        public bool Build()
        {
            vertices.Clear();
            faces.Clear();
            built = true;
            int n = planes.Count;
            if (n < 4) return false;

            // Vertices by clipping: start from a box far larger than any level and cut it with every plane. Each cut
            // walks the current polygons once, so a 300-plane sphere costs a few hundred polygon clips instead of
            // the four and a half million plane-triple intersections of the previous version.
            var swClip = System.Diagnostics.Stopwatch.StartNew();
            if (!ClipVertices()) return false;
            LastClipMs = swClip.Elapsed.TotalMilliseconds;
            var swHull = System.Diagnostics.Stopwatch.StartNew();
            if (vertices.Count < 4) return false;
            if (vertexWeld > Epsilon) WeldVertices(vertexWeld);
            if (vertices.Count < 4) return false;

            // Faces from the convex hull of the vertices, coplanar hull triangles merged into polygons. The hull is a
            // closed manifold by construction, so every edge is shared by exactly two faces even for slivers where the
            // old "which vertices lie on this plane" test disagreed between neighbouring faces.
            var hull = HullFaces(vertices, Epsilon * 4f);
            LastHullMs = swHull.Elapsed.TotalMilliseconds;
            if (hull == null) { vertices.Clear(); return false; }
            // keep only vertices the hull uses, renumbered
            var used = new int[vertices.Count]; for (int i = 0; i < used.Length; i++) used[i] = -1;
            var compact = new List<Vector3>();
            foreach (var face in hull) foreach (var v in face) if (used[v] < 0) { used[v] = compact.Count; compact.Add(vertices[v]); }
            vertices.Clear(); vertices.AddRange(compact);
            foreach (var face in hull)
            {
                var loop = new int[face.Length];
                for (int k = 0; k < face.Length; k++) loop[k] = used[face[k]];
                System.Array.Reverse(loop); // clockwise seen from outside (Unity front face), as before
                faces.Add(loop);
            }
            return faces.Count >= 4;
        }


        /// <summary>
        /// Cut a set of convex polygons (the boundary of a convex solid) with one plane, keeping the inside. The cut
        /// face is appended as a new polygon. Returns false when nothing is left.
        /// </summary>
        static bool ClipPolys(List<List<Vector3>> polys, Vector4 plane, float mergeTol, List<Vector3> cap, List<Vector4> polyPlanes = null)
        {
            cap.Clear();
            for (int q = polys.Count - 1; q >= 0; q--)
            {
                var poly = polys[q]; var kept = new List<Vector3>(poly.Count + 2);
                for (int k = 0; k < poly.Count; k++)
                {
                    LastEdgeTests++;
                    var a = poly[k]; var b = poly[(k + 1) % poly.Count];
                    float da = Distance(plane, a), db = Distance(plane, b);
                    if (da <= Epsilon) kept.Add(a);
                    if (Mathf.Abs(da) <= Epsilon) cap.Add(a); // a vertex on the plane is a corner of the cut face too
                    if ((da > Epsilon && db < -Epsilon) || (da < -Epsilon && db > Epsilon))
                    {
                        var x = Vector3.Lerp(a, b, da / (da - db));
                        kept.Add(x); cap.Add(x);
                    }
                }
                if (kept.Count >= 3) polys[q] = kept; else { polys.RemoveAt(q); polyPlanes?.RemoveAt(q); }
            }
            if (polys.Count == 0) return false;
            // every corner of the cut face is reached from several polygons; keep each once, otherwise the caps
            // grow with every cut (a sphere reached 16 000 points in one cap and millions of edge tests)
            var unique = new List<Vector3>(16);
            foreach (var v in cap)
            {
                bool dup = false;
                for (int i = 0; i < unique.Count && !dup; i++) if ((unique[i] - v).sqrMagnitude <= mergeTol * mergeTol) dup = true;
                if (!dup) unique.Add(v);
            }
            if (unique.Count > LastMaxCap) LastMaxCap = unique.Count;
            if (unique.Count >= 3)
            {
                // the cut face: order its points around their centre in the plane
                var centre = Vector3.zero; foreach (var v in unique) centre += v; centre /= unique.Count;
                Vector3 nrm = plane; var u = Vector3.Cross(nrm, Mathf.Abs(nrm.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(nrm, u);
                var ordered = new List<Vector3>(unique);
                ordered.Sort((x, y) => Mathf.Atan2(Vector3.Dot(x - centre, w), Vector3.Dot(x - centre, u)).CompareTo(Mathf.Atan2(Vector3.Dot(y - centre, w), Vector3.Dot(y - centre, u))));
                polys.Add(ordered); polyPlanes?.Add(plane);
            }
            return true;
        }

        /// <summary>
        /// Clip a built polytope with one plane by cutting its existing faces, instead of rebuilding it from all its
        /// planes. Same result as adding the plane and building, at the cost of one polygon pass (a 114-plane sphere:
        /// well under a millisecond instead of 7 ms per cut, and a subtract makes a dozen cuts). Planes that no
        /// longer bound a face are dropped.
        /// </summary>
        public ConvexPolytope Clip(Vector4 plane)
        {
            if (!built) Build();
            Vector3 n = plane; float len = n.magnitude;
            if (len < 1e-8f) return Clone();
            plane = new Vector4(n.x / len, n.y / len, n.z / len, plane.w / len);

            var result = new ConvexPolytope { tag = tag, vertexWeld = vertexWeld, built = true };
            if (faces.Count < 4) return result; // clipping nothing gives nothing

            float maxD = float.NegativeInfinity, minD = float.PositiveInfinity;
            foreach (var v in vertices) { float d = Distance(plane, v); if (d > maxD) maxD = d; if (d < minD) minD = d; }
            if (minD >= -Epsilon) return result; // everything is outside
            if (maxD <= Epsilon)
            {
                // the plane does not cut: same solid
                result.planes.AddRange(planes); result.vertices.AddRange(vertices);
                foreach (var f in faces) result.faces.Add((int[])f.Clone());
                return result;
            }

            // the faces as polygons, each remembering its plane; the cap gets the clip plane
            var polys = new List<List<Vector3>>(faces.Count + 1);
            var polyPlane = new List<Vector4>(faces.Count + 1);
            for (int f = 0; f < faces.Count; f++)
            {
                var loop = new List<Vector3>(faces[f].Length);
                foreach (var i in faces[f]) loop.Add(vertices[i]);
                polys.Add(loop);
                polyPlane.Add(PlaneOfFace(f));
            }
            float mergeTol = Mathf.Max(Epsilon * 4f, vertexWeld);
            var cap = new List<Vector3>();
            if (!ClipPolys(polys, plane, mergeTol, cap, polyPlane)) return result;

            // vertices: merge the corners reached through neighbouring polygons (a hair apart by rounding)
            float weld = Mathf.Max(vertexWeld, 5e-4f);
            var index = new List<int[]>(polys.Count);
            foreach (var poly in polys)
            {
                var loop = new List<int>(poly.Count);
                foreach (var v in poly)
                {
                    int found = -1;
                    for (int i = 0; i < result.vertices.Count && found < 0; i++) if ((result.vertices[i] - v).sqrMagnitude <= weld * weld) found = i;
                    if (found < 0) { found = result.vertices.Count; result.vertices.Add(v); }
                    if (loop.Count == 0 || loop[loop.Count - 1] != found) loop.Add(found);
                }
                if (loop.Count > 1 && loop[0] == loop[loop.Count - 1]) loop.RemoveAt(loop.Count - 1);
                index.Add(loop.ToArray());
            }

            // the cap is ordered by angle, which may be either winding; take the one the surviving faces use
            float sign = WindingSign();
            for (int q = 0; q < polys.Count; q++)
            {
                var loop = index[q];
                if (loop.Length < 3) continue;
                Vector4 facePlane = polyPlane[q];
                Vector3 newell = Newell(result.vertices, loop);
                if (Vector3.Dot(newell, (Vector3)facePlane) * sign < 0) System.Array.Reverse(loop);
                result.faces.Add(loop);
                result.planes.Add(facePlane);
            }
            // drop vertices no face uses (from polygons that collapsed)
            var used = new int[result.vertices.Count]; for (int i = 0; i < used.Length; i++) used[i] = -1;
            var compact = new List<Vector3>(result.vertices.Count);
            foreach (var face in result.faces) foreach (var v in face) if (used[v] < 0) { used[v] = compact.Count; compact.Add(result.vertices[v]); }
            foreach (var face in result.faces) for (int k = 0; k < face.Length; k++) face[k] = used[face[k]];
            result.vertices.Clear(); result.vertices.AddRange(compact);
            if (result.faces.Count < 4 || result.vertices.Count < 4) { result.faces.Clear(); result.vertices.Clear(); result.planes.Clear(); }
            return result;
        }

        /// <summary>+1 when the faces' Newell normals point the same way as their planes, -1 when reversed (clockwise from outside).</summary>
        float WindingSign()
        {
            for (int f = 0; f < faces.Count; f++)
            {
                if (faces[f].Length < 3) continue;
                var nw = Newell(vertices, faces[f]);
                if (nw.sqrMagnitude < 1e-12f) continue;
                Vector3 outward = PlaneOfFace(f);
                float d = Vector3.Dot(nw, outward);
                if (Mathf.Abs(d) > 1e-9f) return d > 0 ? 1f : -1f;
            }
            return -1f;
        }

        static Vector3 Newell(List<Vector3> pts, int[] loop)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < loop.Length; i++)
            {
                var a = pts[loop[i]]; var b = pts[loop[(i + 1) % loop.Length]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            return n;
        }

        /// <summary>The plane of a face: the stored plane all its vertices lie on, else one fitted to the polygon.</summary>
        Vector4 PlaneOfFace(int f)
        {
            var loop = faces[f];
            Vector3 nw = Newell(vertices, loop);
            for (int p = 0; p < planes.Count; p++)
            {
                Vector3 pn = planes[p];
                if (Mathf.Abs(Vector3.Dot(pn, nw)) < nw.magnitude * 0.999f) continue;
                bool on = true;
                for (int k = 0; k < loop.Length && on; k++) if (Mathf.Abs(Distance(planes[p], vertices[loop[k]])) > Epsilon * 10f) on = false;
                if (on) return planes[p];
            }
            // fitted: outward is away from the solid's centre
            var centre = Vector3.zero; foreach (var v in vertices) centre += v; centre /= Mathf.Max(1, vertices.Count);
            var fc = Vector3.zero; foreach (var i in loop) fc += vertices[i]; fc /= loop.Length;
            var nrm = nw.normalized; if (Vector3.Dot(nrm, fc - centre) < 0) nrm = -nrm;
            return new Vector4(nrm.x, nrm.y, nrm.z, -Vector3.Dot(nrm, fc));
        }

        // ------------------------------------------------------------------ convex hull (small point sets)

        struct Tri { public int a, b, c; public Vector3 n; public float d; }

        /// <summary>Incremental convex hull; returns polygons (counter-clockwise seen from outside) or null when degenerate.</summary>
        static List<int[]> HullFaces(List<Vector3> pts, float eps)
        {
            int n = pts.Count;
            if (n < 4) return null;
            // initial tetrahedron from extreme points
            int i0 = 0, i1 = 0;
            for (int i = 1; i < n; i++) { if (pts[i].x < pts[i0].x) i0 = i; if (pts[i].x > pts[i1].x) i1 = i; }
            if ((pts[i1] - pts[i0]).sqrMagnitude < eps * eps) return null;
            int i2 = -1; float best = eps;
            for (int i = 0; i < n; i++) { float d = Vector3.Cross(pts[i1] - pts[i0], pts[i] - pts[i0]).magnitude / (pts[i1] - pts[i0]).magnitude; if (d > best) { best = d; i2 = i; } }
            if (i2 < 0) return null;
            var n0 = Vector3.Cross(pts[i1] - pts[i0], pts[i2] - pts[i0]).normalized;
            int i3 = -1; best = eps;
            for (int i = 0; i < n; i++) { float d = Mathf.Abs(Vector3.Dot(n0, pts[i] - pts[i0])); if (d > best) { best = d; i3 = i; } }
            if (i3 < 0) return null;
            var centre = (pts[i0] + pts[i1] + pts[i2] + pts[i3]) * 0.25f;
            var tris = new List<Tri>();
            void Add(int a, int b, int c)
            {
                var nn = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                if (nn.sqrMagnitude < 1e-16f) return;
                nn.Normalize();
                if (Vector3.Dot(nn, centre - pts[a]) > 0f) { (b, c) = (c, b); nn = -nn; }
                tris.Add(new Tri { a = a, b = b, c = c, n = nn, d = -Vector3.Dot(nn, pts[a]) });
            }
            Add(i0, i1, i2); Add(i0, i1, i3); Add(i0, i2, i3); Add(i1, i2, i3);
            for (int p = 0; p < n; p++)
            {
                if (p == i0 || p == i1 || p == i2 || p == i3) continue;
                var visible = new List<int>();
                for (int t = 0; t < tris.Count; t++) if (Vector3.Dot(tris[t].n, pts[p]) + tris[t].d > eps) visible.Add(t);
                if (visible.Count == 0) continue;
                // horizon: edges of visible triangles not shared with another visible triangle
                var edgeCount = new Dictionary<long, int>(); var edgeDir = new List<(int, int)>();
                foreach (var t in visible)
                {
                    var tr = tris[t];
                    foreach (var (a, b) in new[] { (tr.a, tr.b), (tr.b, tr.c), (tr.c, tr.a) })
                    {
                        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                        edgeCount[key] = edgeCount.TryGetValue(key, out var cnt) ? cnt + 1 : 1;
                        edgeDir.Add((a, b));
                    }
                }
                visible.Sort(); for (int k = visible.Count - 1; k >= 0; k--) tris.RemoveAt(visible[k]);
                foreach (var (a, b) in edgeDir)
                {
                    long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    if (edgeCount[key] == 1) Add(a, b, p);
                }
            }
            if (tris.Count < 4) return null;
            // merge coplanar triangles into polygons: group by plane, take the boundary edges, chain them
            var faces = new List<int[]>();
            var assigned = new bool[tris.Count];
            for (int t = 0; t < tris.Count; t++)
            {
                if (assigned[t]) continue;
                var group = new List<int>();
                for (int u = t; u < tris.Count; u++)
                {
                    if (assigned[u]) continue;
                    if (Vector3.Dot(tris[t].n, tris[u].n) > 0.9999f && Mathf.Abs(tris[t].d - tris[u].d) <= eps * 4f) { group.Add(u); assigned[u] = true; }
                }
                var directed = new Dictionary<long, (int a, int b)>(); var seen = new HashSet<long>();
                foreach (var u in group)
                {
                    var tr = tris[u];
                    foreach (var (a, b) in new[] { (tr.a, tr.b), (tr.b, tr.c), (tr.c, tr.a) })
                    {
                        long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                        if (!seen.Add(key)) directed.Remove(key); else directed[key] = (a, b);
                    }
                }
                // merged polygon must be planar: welding moves points, and a bent polygon reads as concave downstream
                bool planar = true;
                {
                    var pn = tris[t].n; float pd = tris[t].d;
                    foreach (var e in directed.Values) if (Mathf.Abs(Vector3.Dot(pn, pts[e.a]) + pd) > eps) { planar = false; break; }
                }
                if (!planar)
                {
                    foreach (var u in group) { var tr = tris[u]; faces.Add(new[] { tr.a, tr.b, tr.c }); }
                    continue;
                }
                // chain boundary edges (directed a->b, counter-clockwise from outside)
                var next = new Dictionary<int, int>();
                foreach (var e in directed.Values) next[e.a] = e.b;
                if (next.Count < 3) continue;
                var loop = new List<int>(); int start = -1; foreach (var k in next.Keys) { start = k; break; }
                int cur = start;
                for (int guard = 0; guard <= next.Count; guard++) { loop.Add(cur); if (!next.TryGetValue(cur, out cur) || cur == start) break; }
                // a vertex where two merged triangles met on a straight edge adds nothing and makes zero-area triangles
                for (int k = loop.Count - 1; k >= 0 && loop.Count > 3; k--)
                {
                    var prev = pts[loop[(k + loop.Count - 1) % loop.Count]]; var here = pts[loop[k]]; var nxt = pts[loop[(k + 1) % loop.Count]];
                    if (Vector3.Cross(here - prev, nxt - here).sqrMagnitude <= eps * eps * (here - prev).sqrMagnitude) loop.RemoveAt(k);
                }
                if (loop.Count >= 3) faces.Add(loop.ToArray());
            }
            return faces.Count >= 4 ? faces : null;
        }

        /// <summary>Clip a huge box by every plane; the surviving polygons' corners are the vertices (deduplicated).</summary>
        bool ClipVertices()
        {
            // The start box must be as small as possible: float precision is relative, and clipping a 1e5 box gave
            // centimetre errors. Size it from the plane offsets and grow it when the solid turns out to reach it.
            float reach = 1f;
            for (int i = 0; i < planes.Count; i++) reach = Mathf.Max(reach, Mathf.Abs(planes[i].w));
            LastRetries = 0; LastEdgeTests = 0; LastMaxCap = 0;
            for (float half = reach * 4f + 8f; half <= 1e6f; half *= 16f)
            {
                int result = ClipVertices(half);
                LastRetries++;
                if (result == 1) return true;
                if (result == 0) return false;
                vertices.Clear(); // reached the box: retry larger
            }
            return false;
        }

        /// <summary>1 = closed solid found, 0 = empty, -1 = the solid touches the start box (needs a larger one).</summary>
        int ClipVertices(float kHuge)
        {
            var polys = new List<List<Vector3>>(8);
            var c = new Vector3[]
            {
                new Vector3(-kHuge, -kHuge, -kHuge), new Vector3(kHuge, -kHuge, -kHuge), new Vector3(kHuge, -kHuge, kHuge), new Vector3(-kHuge, -kHuge, kHuge),
                new Vector3(-kHuge, kHuge, -kHuge), new Vector3(kHuge, kHuge, -kHuge), new Vector3(kHuge, kHuge, kHuge), new Vector3(-kHuge, kHuge, kHuge),
            };
            polys.Add(new List<Vector3> { c[0], c[3], c[2], c[1] }); polys.Add(new List<Vector3> { c[4], c[5], c[6], c[7] });
            polys.Add(new List<Vector3> { c[0], c[1], c[5], c[4] }); polys.Add(new List<Vector3> { c[2], c[3], c[7], c[6] });
            polys.Add(new List<Vector3> { c[0], c[4], c[7], c[3] }); polys.Add(new List<Vector3> { c[1], c[2], c[6], c[5] });
            var cap = new List<Vector3>();
            var swLoop = System.Diagnostics.Stopwatch.StartNew();
            float mergeTol = Mathf.Max(Epsilon * 4f, vertexWeld);
            for (int p = 0; p < planes.Count; p++)
            {
                if (!ClipPolys(polys, planes[p], mergeTol, cap)) return 0;
            }
            LastLoopMs = swLoop.Elapsed.TotalMilliseconds; LastPolys = polys.Count; LastPoints = 0; foreach (var poly in polys) LastPoints += poly.Count;
            // the same corner is reached through several polygon edges with slightly different rounding; merge those
            // (a hair apart) so the hull does not see needle triangles
            float weld = Mathf.Max(vertexWeld, 5e-4f);
            foreach (var poly in polys)
                foreach (var v in poly)
                {
                    if (Mathf.Abs(v.x) >= kHuge * 0.5f || Mathf.Abs(v.y) >= kHuge * 0.5f || Mathf.Abs(v.z) >= kHuge * 0.5f) return -1; // reaches the box
                    bool dup = false;
                    for (int i = 0; i < vertices.Count && !dup; i++) if ((vertices[i] - v).sqrMagnitude <= weld * weld) dup = true;
                    if (!dup) vertices.Add(v);
                }
            return vertices.Count >= 4 ? 1 : 0;
        }

        void WeldVertices(float tolerance)
        {
            var kept = new List<Vector3>();
            foreach (var v in vertices)
            {
                bool dup = false;
                for (int k = 0; k < kept.Count && !dup; k++) if ((kept[k] - v).sqrMagnitude <= tolerance * tolerance) dup = true;
                if (!dup) kept.Add(v);
            }
            vertices.Clear(); vertices.AddRange(kept);
        }

        bool Inside(Vector3 p)
        {
            for (int i = 0; i < planes.Count; i++)
                if (Distance(planes[i], p) > Epsilon) return false;
            return true;
        }

        bool HasVertex(Vector3 p)
        {
            for (int i = 0; i < vertices.Count; i++)
                if ((vertices[i] - p).sqrMagnitude < Epsilon * Epsilon) return true;
            return false;
        }

        public bool IsEmpty
        {
            get
            {
                if (!built) Build();
                return vertices.Count < 4 || faces.Count < 4 || Volume() < 1e-9f;
            }
        }

        public float Volume()
        {
            if (!built) Build();
            double vol = 0;
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                Vector3 a = vertices[face[0]];
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    Vector3 b = vertices[face[i]], c = vertices[face[i + 1]];
                    vol += Vector3.Dot(a, Vector3.Cross(b, c));
                }
            }
            return Mathf.Abs((float)(vol / 6.0));
        }

        public Bounds GetBounds()
        {
            if (!built) Build();
            if (vertices.Count == 0) return new Bounds();
            var b = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Count; i++) b.Encapsulate(vertices[i]);
            return b;
        }

        /// <summary>True when the solid is an axis aligned box; then a BoxCollider is the better collider.</summary>
        public bool IsAxisAlignedBox(out Bounds bounds)
        {
            bounds = default;
            if (!built) Build();
            if (faces.Count != 6 || vertices.Count != 8) return false;
            for (int i = 0; i < planes.Count; i++)
            {
                Vector3 n = planes[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                bool axial = (ax > 0.9999f && ay < 1e-4f && az < 1e-4f) || (ay > 0.9999f && ax < 1e-4f && az < 1e-4f) || (az > 0.9999f && ax < 1e-4f && ay < 1e-4f);
                if (!axial) return false;
            }
            bounds = GetBounds();
            return true;
        }

        /// <summary>Keep the part on the inside of the plane.</summary>

        /// <summary>a minus b as convex pieces: for each plane of b, the part of a outside that plane is a piece; the rest continues.</summary>
        public static void Subtract(ConvexPolytope a, ConvexPolytope b, List<ConvexPolytope> result)
        {
            // quick reject: bounds, then a real volume test so a brush that only touches a solid does not fragment it
            if (!a.GetBounds().Intersects(b.GetBounds()) || Intersect(a, b).IsEmpty)
            {
                result.Add(a);
                return;
            }
            var remainder = a;
            bool touched = false;
            for (int i = 0; i < b.planes.Count; i++)
            {
                Vector4 p = b.planes[i];
                var outside = remainder.Clip(Flip(p));
                if (!outside.IsEmpty)
                {
                    outside.tag = a.tag;
                    result.Add(outside);
                    touched = true;
                }
                remainder = remainder.Clip(p);
                if (remainder.IsEmpty)
                    return; // nothing of a is inside b
            }
            // remainder is a intersected with b: removed
            if (!touched)
            {
                // a is completely inside b: nothing survives
            }
        }

        public static ConvexPolytope Intersect(ConvexPolytope a, ConvexPolytope b)
        {
            if (!a.built) a.Build();
            var c = a;
            for (int i = 0; i < b.planes.Count; i++)
            {
                c = c.Clip(b.planes[i]);
                if (c.faces.Count < 4) break;
            }
            if (c == a) { c = a.Clone(); c.Build(); } // no plane of b cut a: a copy, never the operand itself
            return c;
        }

        public Mesh ToMesh(string name)
        {
            if (!built) Build();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                int start = verts.Count;
                for (int i = 0; i < face.Length; i++) verts.Add(vertices[face[i]]);
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    tris.Add(start);
                    tris.Add(start + i);
                    tris.Add(start + i + 1);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

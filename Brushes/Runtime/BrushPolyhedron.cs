using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// The editable shape of a Custom brush: vertices (local metres, centred on the transform) and planar faces
    /// wound counter-clockwise seen from outside. It may be concave; the convex pieces the colliders need are derived
    /// from it (see ConvexDecomposition). Vertex and face indices are stable across edits so selection can
    /// refer to them.
    /// </summary>
    [Serializable]
    public sealed class BrushPolyhedron
    {
        [Serializable]
        public sealed class Face
        {
            public int[] indices;
            /// <summary>Index of the face this one was split from, or -1. Materials and surfaces follow it.</summary>
            public int source = -1;
            public Face() { }
            public Face(int[] indices, int source = -1) { this.indices = indices; this.source = source; }
        }

        public Vector3[] vertices = Array.Empty<Vector3>();
        public Face[] faces = Array.Empty<Face>();

        public const float Epsilon = 1e-4f;

        public bool IsValid => vertices != null && faces != null && vertices.Length >= 4 && faces.Length >= 4;

        public BrushPolyhedron Clone()
        {
            var c = new BrushPolyhedron { vertices = (Vector3[])vertices.Clone(), faces = new Face[faces.Length] };
            for (int i = 0; i < faces.Length; i++) c.faces[i] = new Face((int[])faces[i].indices.Clone(), faces[i].source);
            return c;
        }

        // ------------------------------------------------------------------ primitives

        public static BrushPolyhedron Box(Vector3 size)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f, z = size.z * 0.5f;
            var p = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(-x, -y, -z), new Vector3( x, -y, -z), new Vector3( x, -y,  z), new Vector3(-x, -y,  z),
                    new Vector3(-x,  y, -z), new Vector3( x,  y, -z), new Vector3( x,  y,  z), new Vector3(-x,  y,  z),
                },
                faces = new[]
                {
                    new Face(new[] { 0, 3, 2, 1 }), // bottom
                    new Face(new[] { 4, 5, 6, 7 }), // top
                    new Face(new[] { 0, 1, 5, 4 }), // back  (-z)
                    new Face(new[] { 2, 3, 7, 6 }), // front (+z)
                    new Face(new[] { 0, 4, 7, 3 }), // left  (-x)
                    new Face(new[] { 1, 2, 6, 5 }), // right (+x)
                }
            };
            p.OrientOutward();
            return p;
        }

        /// <summary>Ramp rising towards +z, extruded along x (same footprint as the parametric wedge).</summary>
        public static BrushPolyhedron Wedge(Vector3 size)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f, z = size.z * 0.5f;
            var p = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(-x, -y, -z), new Vector3(-x, -y, z), new Vector3(-x, y, z),
                    new Vector3( x, -y, -z), new Vector3( x, -y, z), new Vector3( x, y, z),
                },
                faces = new[]
                {
                    new Face(new[] { 0, 1, 2 }),       // -x cap
                    new Face(new[] { 3, 5, 4 }),       // +x cap
                    new Face(new[] { 0, 3, 4, 1 }),    // bottom
                    new Face(new[] { 1, 4, 5, 2 }),    // +z wall
                    new Face(new[] { 0, 2, 5, 3 }),    // slope
                }
            };
            p.OrientOutward();
            return p;
        }

        /// <summary>Prism around y with a regular polygon footprint; topScale 0 gives a cone (apex), 1 a cylinder.</summary>
        public static BrushPolyhedron Prism(Vector3 size, int sides, float topScale)
        {
            sides = Mathf.Max(3, sides);
            float y = size.y * 0.5f;
            // fit the polygon to the size box (its flat sides touch the box), like a collider fitted to the box
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                minX = Mathf.Min(minX, Mathf.Cos(a)); maxX = Mathf.Max(maxX, Mathf.Cos(a));
                minZ = Mathf.Min(minZ, Mathf.Sin(a)); maxZ = Mathf.Max(maxZ, Mathf.Sin(a));
            }
            float rx = size.x / (maxX - minX), rz = size.z / (maxZ - minZ);
            float cx = (maxX + minX) * 0.5f * rx, cz = (maxZ + minZ) * 0.5f * rz;
            var verts = new List<Vector3>();
            var faces = new List<Face>();
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                verts.Add(new Vector3(Mathf.Cos(a) * rx - cx, -y, Mathf.Sin(a) * rz - cz));
            }
            var bottom = new int[sides];
            for (int i = 0; i < sides; i++) bottom[i] = sides - 1 - i;
            faces.Add(new Face(bottom));
            if (topScale <= 0.001f)
            {
                verts.Add(new Vector3(0f, y, 0f));
                int apex = sides;
                for (int i = 0; i < sides; i++) faces.Add(new Face(new[] { i, (i + 1) % sides, apex }));
            }
            else
            {
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3((Mathf.Cos(a) * rx - cx) * topScale, y, (Mathf.Sin(a) * rz - cz) * topScale));
                }
                var top = new int[sides];
                for (int i = 0; i < sides; i++) top[i] = sides + i;
                faces.Add(new Face(top));
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    faces.Add(new Face(new[] { i, j, sides + j, sides + i }));
                }
            }
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.OrientOutward();
            return p;
        }

        /// <summary>Ellipsoid fitted to the size box: (4 + 4 t) segments around y, (2 + 2 t) rings from pole to pole. Convex.</summary>
        public static BrushPolyhedron Sphere(Vector3 size, int tessellation)
        {
            int t = Mathf.Clamp(tessellation, 1, 5);
            int segments = 4 + 4 * t, rings = 2 + 2 * t; // rings = number of latitude bands
            float rx = size.x * 0.5f, ry = size.y * 0.5f, rz = size.z * 0.5f;
            var verts = new List<Vector3> { new Vector3(0f, ry, 0f) };
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int sgm = 0; sgm < segments; sgm++)
                {
                    float theta = Mathf.PI * 2f * (sgm + 0.5f) / segments;
                    verts.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta) * rx, Mathf.Cos(phi) * ry, Mathf.Sin(phi) * Mathf.Sin(theta) * rz));
                }
            }
            verts.Add(new Vector3(0f, -ry, 0f));
            int top = 0, bottom = verts.Count - 1;
            int Ring(int r, int sgm) => 1 + (r - 1) * segments + (sgm % segments);
            var faces = new List<Face>();
            for (int sgm = 0; sgm < segments; sgm++) faces.Add(new Face(new[] { top, Ring(1, sgm + 1), Ring(1, sgm) }));
            for (int r = 1; r < rings - 1; r++)
                for (int sgm = 0; sgm < segments; sgm++)
                    faces.Add(new Face(new[] { Ring(r, sgm), Ring(r, sgm + 1), Ring(r + 1, sgm + 1), Ring(r + 1, sgm) }));
            for (int sgm = 0; sgm < segments; sgm++) faces.Add(new Face(new[] { bottom, Ring(rings - 1, sgm), Ring(rings - 1, sgm + 1) }));
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.OrientOutward();
            return p;
        }

        /// <summary>
        /// Linear stairs climbing towards +z inside the size box: risers face -z, the back is a solid wall. Concave;
        /// the side walls are one quad per step so every face stays convex.
        /// </summary>
        public static BrushPolyhedron Stairs(Vector3 size, float stepHeight, float stepDepth)
        {
            float w = size.x * 0.5f, h = size.y * 0.5f, d = size.z * 0.5f;
            stepHeight = Mathf.Max(0.001f, stepHeight); stepDepth = Mathf.Max(0.001f, stepDepth);
            int steps = Mathf.Max(1, Mathf.CeilToInt(size.z / stepDepth - 1e-4f));
            var verts = new List<Vector3>(); var lookup = new Dictionary<Vector3, int>();
            int V(float x, float y, float z)
            {
                var v = new Vector3(Mathf.Round(x * 1e5f) / 1e5f, Mathf.Round(y * 1e5f) / 1e5f, Mathf.Round(z * 1e5f) / 1e5f);
                if (!lookup.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(v); lookup[v] = i; }
                return i;
            }
            var faces = new List<Face>();
            void Add(Vector3 outward, params int[] idx)
            {
                // wind so the Newell normal points the intended way
                Vector3 n = Vector3.zero;
                for (int i = 0; i < idx.Length; i++) { var a = verts[idx[i]]; var b = verts[idx[(i + 1) % idx.Length]]; n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y); }
                if (Vector3.Dot(n, outward) < 0f) Array.Reverse(idx);
                faces.Add(new Face(idx));
            }
            // the top of every step (capped at the box top: once the stairs reach it the remaining steps are flat)
            var tops = new List<float>(); var zs = new List<float> { -d };
            for (int k = 0; k < steps; k++)
            {
                tops.Add(Mathf.Min(h, -h + (k + 1) * stepHeight));
                float z1 = Mathf.Min(d, -d + (k + 1) * stepDepth);
                zs.Add(z1);
                if (z1 >= d - 1e-6f) break;
            }
            int n = zs.Count - 1;
            // distinct wall levels: every side wall and the back wall are split at all of them, so shared edges match exactly
            var levels = new List<float> { -h };
            foreach (var t in tops) if (t > levels[levels.Count - 1] + 1e-6f) levels.Add(t);
            for (int k = 0; k < n; k++)
            {
                float z0 = zs[k], z1 = zs[k + 1], yPrev = k > 0 ? tops[k - 1] : -h, yTop = tops[k];
                if (yTop > yPrev + 1e-6f) Add(Vector3.back, V(-w, yPrev, z0), V(w, yPrev, z0), V(w, yTop, z0), V(-w, yTop, z0)); // riser
                Add(Vector3.up, V(-w, yTop, z0), V(w, yTop, z0), V(w, yTop, z1), V(-w, yTop, z1));       // tread
                Add(Vector3.down, V(-w, -h, z0), V(w, -h, z0), V(w, -h, z1), V(-w, -h, z1));             // bottom strip
                for (int l = 0; l + 1 < levels.Count && levels[l + 1] <= yTop + 1e-6f; l++)
                {
                    float ya = levels[l], yb = levels[l + 1];
                    Add(Vector3.left, V(-w, ya, z0), V(-w, ya, z1), V(-w, yb, z1), V(-w, yb, z0));
                    Add(Vector3.right, V(w, ya, z0), V(w, ya, z1), V(w, yb, z1), V(w, yb, z0));
                }
            }
            float zBack = zs[n], yBack = tops[n - 1];
            for (int l = 0; l + 1 < levels.Count && levels[l + 1] <= yBack + 1e-6f; l++)
                Add(Vector3.forward, V(-w, levels[l], zBack), V(w, levels[l], zBack), V(w, levels[l + 1], zBack), V(-w, levels[l + 1], zBack));
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.EnsureOutward();
            return p;
        }

        /// <summary>A copy with every vertex transformed.</summary>
        public BrushPolyhedron Transformed(Matrix4x4 m)
        {
            var c = Clone();
            for (int i = 0; i < c.vertices.Length; i++) c.vertices[i] = m.MultiplyPoint3x4(c.vertices[i]);
            if (m.determinant < 0f) c.InvertFaces(); // a mirroring transform turns the faces inside out
            return c;
        }

        /// <summary>Content hash of the shape (vertices rounded to a micron, faces).</summary>
        public int ContentHash()
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < vertices.Length; i++) { var v = vertices[i]; h = h * 31 + Mathf.RoundToInt(v.x * 1e6f); h = h * 31 + Mathf.RoundToInt(v.y * 1e6f); h = h * 31 + Mathf.RoundToInt(v.z * 1e6f); }
                for (int f = 0; f < faces.Length; f++) { h = h * 31 + faces[f].indices.Length; foreach (var i in faces[f].indices) h = h * 31 + i; }
                return h;
            }
        }

        // ------------------------------------------------------------------ geometry

        public Vector3 Centroid()
        {
            var c = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++) c += vertices[i];
            return vertices.Length > 0 ? c / vertices.Length : c;
        }

        /// <summary>Plane of a face (Newell normal, outward for counter-clockwise faces), as (n.xyz, d) with n·p + d = 0.</summary>
        public Vector4 Plane(int face)
        {
            var idx = faces[face].indices;
            Vector3 n = Vector3.zero;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = vertices[idx[i]]; var b = vertices[idx[(i + 1) % idx.Length]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
                c += a;
            }
            c /= idx.Length;
            n.Normalize();
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, c));
        }

        public static float Distance(Vector4 plane, Vector3 p) => plane.x * p.x + plane.y * p.y + plane.z * p.z + plane.w;

        public Vector3 FaceCentre(int face)
        {
            var idx = faces[face].indices; var c = Vector3.zero;
            for (int i = 0; i < idx.Length; i++) c += vertices[idx[i]];
            return c / idx.Length;
        }

        /// <summary>Flip every face whose normal points towards the centroid. Correct for primitives and star shapes.</summary>
        public void OrientOutward()
        {
            var c = Centroid();
            for (int f = 0; f < faces.Length; f++)
            {
                if (Distance(Plane(f), c) > Epsilon)
                    Array.Reverse(faces[f].indices);
            }
        }

        /// <summary>Reverse every face. Used to fix a consistently inside-out shape (negative volume).</summary>
        public void InvertFaces()
        {
            foreach (var f in faces) Array.Reverse(f.indices);
        }

        /// <summary>Outward faces have positive volume; a consistently inside-out shape is inverted.</summary>
        public void EnsureOutward()
        {
            if (Volume() < 0f) InvertFaces();
        }

        public Bounds Bounds()
        {
            if (vertices.Length == 0) return new Bounds();
            var b = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) b.Encapsulate(vertices[i]);
            return b;
        }

        /// <summary>Signed volume by the divergence theorem; positive for outward faces.</summary>
        public float Volume()
        {
            double v = 0;
            for (int f = 0; f < faces.Length; f++)
            {
                var idx = faces[f].indices;
                var a = vertices[idx[0]];
                for (int i = 1; i + 1 < idx.Length; i++)
                    v += Vector3.Dot(a, Vector3.Cross(vertices[idx[i]], vertices[idx[i + 1]]));
            }
            return (float)(v / 6.0);
        }

        /// <summary>Every edge must be shared by exactly two faces in opposite directions.</summary>
        public bool IsClosed()
        {
            var directed = new HashSet<long>();
            foreach (var face in faces)
            {
                var idx = face.indices;
                for (int i = 0; i < idx.Length; i++)
                {
                    long key = ((long)idx[i] << 32) | (uint)idx[(i + 1) % idx.Length];
                    if (!directed.Add(key)) return false; // an edge used twice in the same direction
                }
            }
            foreach (var key in directed)
            {
                long twin = ((key & 0xffffffffL) << 32) | (uint)(key >> 32);
                if (!directed.Contains(twin)) return false;
            }
            return true;
        }

        /// <summary>Max distance of any face vertex from its plane.</summary>
        public float PlanarityError(int face)
        {
            var plane = Plane(face); float worst = 0f;
            foreach (var i in faces[face].indices) worst = Mathf.Max(worst, Mathf.Abs(Distance(plane, vertices[i])));
            return worst;
        }

        /// <summary>Edges (as vertex pairs) where the solid bends outwards: the neighbouring face lies in front of the face plane.</summary>
        public List<(int a, int b, int faceA, int faceB)> ReflexEdges()
        {
            var result = new List<(int, int, int, int)>();
            var owner = new Dictionary<long, int>();
            for (int f = 0; f < faces.Length; f++)
            {
                var idx = faces[f].indices;
                for (int i = 0; i < idx.Length; i++)
                    owner[((long)idx[i] << 32) | (uint)idx[(i + 1) % idx.Length]] = f;
            }
            var planes = new Vector4[faces.Length];
            for (int f = 0; f < faces.Length; f++) planes[f] = Plane(f);
            var seen = new HashSet<long>();
            foreach (var kv in owner)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffffL);
                long undirected = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!seen.Add(undirected)) continue;
                if (!owner.TryGetValue(((long)b << 32) | (uint)a, out int g)) continue;
                int f = kv.Value;
                bool reflex = false;
                foreach (var v in faces[g].indices)
                {
                    if (v == a || v == b) continue;
                    if (Distance(planes[f], vertices[v]) > Epsilon * 10f) { reflex = true; break; }
                }
                if (reflex) result.Add((a, b, f, g));
            }
            return result;
        }

        public bool IsConvex() => ReflexEdges().Count == 0;

        /// <summary>
        /// A shape the decomposition can represent: closed, positive volume, no degenerate faces, and its convex
        /// parts fill exactly its volume (a self-intersecting or locally inverted shape fails that last test).
        /// </summary>
        public bool IsSound(out string reason)
        {
            reason = null;
            if (!IsValid) { reason = "not enough vertices or faces"; return false; }
            if (!IsClosed()) { reason = "open"; return false; }
            float volume = Volume();
            if (volume <= 1e-5f) { reason = "no volume"; return false; }
            for (int f = 0; f < faces.Length; f++)
            {
                if (faces[f].indices.Length < 3) { reason = "degenerate face"; return false; }
                if (PlanarityError(f) > Epsilon * 10f) { reason = "non-planar face"; return false; }
            }
            var pieces = new List<CsgBrush.Colliders.ConvexPolytope>();
            if (!ConvexDecomposition.Decompose(this, pieces)) { reason = "cannot be split into convex parts"; return false; }
            float sum = 0f; foreach (var piece in pieces) sum += piece.Volume();
            if (Mathf.Abs(sum - volume) > Mathf.Max(1e-3f + ConvexDecomposition.MinPieceVolume * 8f, volume * 0.01f)) { reason = "self-intersecting"; return false; }
            return true;
        }

        /// <summary>Undirected edges as vertex pairs (a &lt; b), each once.</summary>
        public List<(int a, int b)> Edges()
        {
            var set = new HashSet<long>(); var list = new List<(int, int)>();
            foreach (var face in faces)
            {
                var idx = face.indices;
                for (int i = 0; i < idx.Length; i++)
                {
                    int a = idx[i], b = idx[(i + 1) % idx.Length];
                    int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                    if (set.Add(((long)lo << 32) | (uint)hi)) list.Add((lo, hi));
                }
            }
            return list;
        }

        /// <summary>All vertices used by the given faces.</summary>
        public HashSet<int> VerticesOfFaces(IEnumerable<int> faceIndices)
        {
            var set = new HashSet<int>();
            foreach (var f in faceIndices) foreach (var i in faces[f].indices) set.Add(i);
            return set;
        }

        // ------------------------------------------------------------------ edits

        /// <summary>Move a set of vertices by one offset; faces that bend because only some of their vertices moved are split.</summary>
        public void MoveVertices(IEnumerable<int> vertexIndices, Vector3 delta)
        {
            var moved = new List<int>(vertexIndices);
            foreach (var v in moved) vertices[v] += delta;
            foreach (var v in moved) EnsurePlanar(v);
        }

        /// <summary>
        /// Vertices that coincide are merged and degenerate faces removed. Returns the old-to-new vertex index map
        /// (-1 for a removed duplicate maps to its survivor's index instead, so selections can follow).
        /// </summary>
        public int[] WeldCoincident(float tolerance = 1e-4f)
        {
            var remap = new int[vertices.Length];
            var kept = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int found = -1;
                for (int k = 0; k < kept.Count && found < 0; k++)
                    if ((kept[k] - vertices[i]).sqrMagnitude <= tolerance * tolerance) found = k;
                if (found < 0) { found = kept.Count; kept.Add(vertices[i]); }
                remap[i] = found;
            }
            if (kept.Count == vertices.Length) return remap;
            var newFaces = new List<Face>();
            foreach (var face in faces)
            {
                var loop = new List<int>();
                foreach (var i in face.indices)
                {
                    int n = remap[i];
                    if (loop.Count > 0 && loop[loop.Count - 1] == n) continue;
                    loop.Add(n);
                }
                while (loop.Count > 1 && loop[0] == loop[loop.Count - 1]) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3 && new HashSet<int>(loop).Count >= 3) newFaces.Add(new Face(loop.ToArray(), face.source));
            }
            vertices = kept.ToArray();
            faces = newFaces.ToArray();
            return remap;
        }

        /// <summary>Move every vertex of a face along the face normal.</summary>
        public void PushFace(int face, float distance)
        {
            var plane = Plane(face);
            var n = new Vector3(plane.x, plane.y, plane.z);
            foreach (var i in faces[face].indices) vertices[i] += n * distance;
        }

        /// <summary>Move one vertex and keep the faces around it planar by splitting the bent ones into triangles fanned from it.</summary>
        public void MoveVertex(int vertex, Vector3 position)
        {
            vertices[vertex] = position;
            EnsurePlanar(vertex);
        }

        /// <summary>Faces around a vertex that are no longer planar become triangle fans from that vertex (new faces keep the source id).</summary>
        public void EnsurePlanar(int vertex)
        {
            var list = new List<Face>(faces);
            for (int f = list.Count - 1; f >= 0; f--)
            {
                var face = list[f];
                if (Array.IndexOf(face.indices, vertex) < 0 || face.indices.Length <= 3) continue;
                if (PlanarityErrorOf(face) <= Epsilon * 10f) continue;
                // fan from the moved vertex
                int k = Array.IndexOf(face.indices, vertex); int n = face.indices.Length;
                int source = face.source >= 0 ? face.source : f;
                var fan = new List<Face>();
                for (int i = 1; i + 1 < n; i++)
                    fan.Add(new Face(new[] { face.indices[k], face.indices[(k + i) % n], face.indices[(k + i + 1) % n] }, source));
                list.RemoveAt(f);
                list.InsertRange(f, fan);
            }
            faces = list.ToArray();
        }

        float PlanarityErrorOf(Face face)
        {
            var saved = faces; faces = new[] { face };
            float e = PlanarityError(0);
            faces = saved;
            return e;
        }
    }
}

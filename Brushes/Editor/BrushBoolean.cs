using System.Collections.Generic;
using CsgBrush.Manifold;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Edits that are booleans by nature (extrude a face outward: union with a prism; inward: difference) run
    /// through Manifold and come back as a polyhedron, so the result is valid by construction whatever it passes
    /// through. Each face carries its id as a vertex property through the boolean, so the faces of the result know
    /// which original face they are, and the selection can follow.
    /// </summary>
    public static class BrushBoolean
    {
        /// <summary>
        /// Extrude faces through a boolean. Returns the new polyhedron and, per original face index, its index in
        /// the result (-1 when the face is gone). Null when Manifold rejects the input or the result is empty.
        /// </summary>
        public static BrushPolyhedron ExtrudeFaces(BrushPolyhedron poly, IEnumerable<int> faceIndices, float distance, bool individual, out int[] faceRemap)
        {
            faceRemap = null;
            var groups = new List<List<int>>();
            if (individual) { foreach (var f in faceIndices) if (f >= 0 && f < poly.faces.Length) groups.Add(new List<int> { f }); }
            else { var all = new List<int>(); foreach (var f in faceIndices) if (f >= 0 && f < poly.faces.Length && !all.Contains(f)) all.Add(f); if (all.Count > 0) groups.Add(all); }
            if (groups.Count == 0 || Mathf.Abs(distance) < 1e-6f) return null;

            var brushFaces = new List<int[]>(); foreach (var f in poly.faces) brushFaces.Add(f.indices);
            using var brush = ManifoldSolid.FromFaces(poly.vertices, brushFaces, out _);
            if (brush == null) return null;
            int nextId = poly.faces.Length;
            var sourceOf = new Dictionary<int, int>(); // new face id -> the face it was extruded from
            var result = brush;
            bool ownsResult = false;
            foreach (var group in groups)
            {
                // the prism: the region (reversed) as bottom, the moved region as top, walls on the region boundary
                Vector3 normal = Vector3.zero;
                foreach (var f in group) { var pl = poly.Plane(f); normal += new Vector3(pl.x, pl.y, pl.z); }
                if (normal.sqrMagnitude < 1e-10f) { var pl = poly.Plane(group[0]); normal = new Vector3(pl.x, pl.y, pl.z); }
                var offset = normal.normalized * distance;
                var verts = new List<Vector3>(); var faces = new List<int[]>(); var ids = new List<int>();
                var bottom = new Dictionary<int, int>(); var top = new Dictionary<int, int>();
                int B(int v) { if (!bottom.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(poly.vertices[v]); bottom[v] = i; } return i; }
                int T(int v) { if (!top.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(poly.vertices[v] + offset); top[v] = i; } return i; }
                var edgeCount = new Dictionary<long, int>();
                foreach (var f in group)
                {
                    var idx = poly.faces[f].indices;
                    for (int i = 0; i < idx.Length; i++) { long k = Key(idx[i], idx[(i + 1) % idx.Length]); edgeCount[k] = edgeCount.TryGetValue(k, out var c) ? c + 1 : 1; }
                }
                foreach (var f in group)
                {
                    var idx = poly.faces[f].indices;
                    var bot = new int[idx.Length]; var tp = new int[idx.Length];
                    for (int i = 0; i < idx.Length; i++) { bot[idx.Length - 1 - i] = B(idx[i]); tp[i] = T(idx[i]); }
                    faces.Add(bot); ids.Add(f);   // the pocket floor when cutting inward; interior when adding
                    faces.Add(tp); ids.Add(f);    // the moved face keeps its identity
                    for (int i = 0; i < idx.Length; i++)
                    {
                        int a = idx[i], b = idx[(i + 1) % idx.Length];
                        if (edgeCount[Key(a, b)] != 1) continue;
                        faces.Add(new[] { B(a), B(b), T(b), T(a) }); ids.Add(nextId); sourceOf[nextId] = f; nextId++;
                    }
                }
                using var prism = ManifoldSolid.FromFaces(verts, faces, out _, ids);
                if (prism == null) continue;
                var next = ManifoldSolid.Boolean(result, prism, distance > 0f ? ManifoldNative.OpType.Add : ManifoldNative.OpType.Subtract);
                if (ownsResult) result.Dispose();
                result = next; ownsResult = true;
            }
            if (!ownsResult) return null;
            var mesh = result.ToMesh();
            result.Dispose();
            if (mesh.triangles == null || mesh.triangles.Length == 0) return null;
            return FromMesh(mesh, poly, sourceOf, out faceRemap);
        }

        static long Key(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        /// <summary>
        /// A polyhedron from Manifold's triangles: triangles are grouped by face id and connectivity and merged back
        /// into polygons (a group whose outline has holes stays as triangles). Original faces keep their index when
        /// they survive; new faces are appended with their source set.
        /// </summary>
        public static BrushPolyhedron FromMesh(ManifoldSolid.MeshData mesh, BrushPolyhedron original, Dictionary<int, int> sourceOf, out int[] faceRemap)
        {
            // shared vertices by position (Manifold splits vertices where the face id property differs)
            var lookup = new Dictionary<Vector3, int>(); var verts = new List<Vector3>();
            var vmap = new int[mesh.vertices.Length];
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                var v = mesh.vertices[i];
                if (!lookup.TryGetValue(v, out int k)) { k = verts.Count; verts.Add(v); lookup[v] = k; }
                vmap[i] = k;
            }
            int triCount = mesh.triangles.Length / 3;
            var byId = new Dictionary<int, List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                var pa = verts[vmap[mesh.triangles[t * 3]]]; var pb = verts[vmap[mesh.triangles[t * 3 + 1]]]; var pc = verts[vmap[mesh.triangles[t * 3 + 2]]];
                if (Vector3.Cross(pb - pa, pc - pa).sqrMagnitude < 1e-16f) continue; // a coincident-face sliver: no area, no face
                int id = mesh.triangleFace[t];
                if (!byId.TryGetValue(id, out var list)) byId[id] = list = new List<int>();
                list.Add(t);
            }
            int Tri(int t, int c) => vmap[mesh.triangles[t * 3 + c]];
            // polygons per id: connected components of triangles, each turned into its outline
            var polygonsOf = new Dictionary<int, List<int[]>>();
            foreach (var kv in byId)
            {
                var polys = new List<int[]>();
                var remaining = new HashSet<int>(kv.Value);
                while (remaining.Count > 0)
                {
                    // flood one component over shared edges
                    var component = new List<int>(); var stack = new Stack<int>();
                    int first = -1; foreach (var t in remaining) { first = t; break; }
                    stack.Push(first); remaining.Remove(first);
                    var edgeOwner = new Dictionary<long, int>();
                    while (stack.Count > 0)
                    {
                        int t = stack.Pop(); component.Add(t);
                        for (int c = 0; c < 3; c++) edgeOwner[Key(Tri(t, c), Tri(t, (c + 1) % 3))] = t;
                        foreach (var o in new List<int>(remaining))
                            for (int c = 0; c < 3; c++)
                                if (edgeOwner.ContainsKey(Key(Tri(o, c), Tri(o, (c + 1) % 3)))) { stack.Push(o); remaining.Remove(o); break; }
                    }
                    // directed boundary edges: used once within the component
                    var directed = new Dictionary<long, (int a, int b)>(); var count = new Dictionary<long, int>();
                    foreach (var t in component)
                        for (int c = 0; c < 3; c++)
                        {
                            int a = Tri(t, c), b = Tri(t, (c + 1) % 3); long k = Key(a, b);
                            count[k] = count.TryGetValue(k, out var n) ? n + 1 : 1; directed[k] = (a, b);
                        }
                    var nextOf = new Dictionary<int, int>(); int boundary = 0;
                    foreach (var kv2 in count) if (kv2.Value == 1) { var e = directed[kv2.Key]; if (nextOf.ContainsKey(e.a)) { nextOf = null; break; } nextOf[e.a] = e.b; boundary++; }
                    int[] loop = null;
                    if (nextOf != null && nextOf.Count > 0)
                    {
                        int start = -1; foreach (var k in nextOf.Keys) { start = k; break; }
                        var chain = new List<int>(); int cur = start;
                        do { chain.Add(cur); if (!nextOf.TryGetValue(cur, out cur)) { chain = null; break; } } while (cur != start && chain.Count <= boundary);
                        if (chain != null && chain.Count == boundary) loop = chain.ToArray(); // vertices along straight stretches stay: neighbouring faces use them
                    }
                    if (loop != null && loop.Length >= 3) polys.Add(loop);
                    else foreach (var t in component) polys.Add(new[] { Tri(t, 0), Tri(t, 1), Tri(t, 2) }); // holes or a broken outline: keep the triangles
                }
                polygonsOf[kv.Key] = polys;
            }
            // faces: original indices first, in order, then the new ones
            var faces = new List<BrushPolyhedron.Face>();
            faceRemap = new int[original.faces.Length]; for (int i = 0; i < faceRemap.Length; i++) faceRemap[i] = -1;
            var extra = new List<BrushPolyhedron.Face>();
            for (int f = 0; f < original.faces.Length; f++)
            {
                if (!polygonsOf.TryGetValue(f, out var polys) || polys.Count == 0) continue;
                int main = 0; float best = -1f; // the largest piece keeps the face's index
                for (int i = 0; i < polys.Count; i++) { float area = Area(polys[i], verts); if (area > best) { best = area; main = i; } }
                faceRemap[f] = faces.Count;
                faces.Add(new BrushPolyhedron.Face(polys[main], original.faces[f].source));
                for (int i = 0; i < polys.Count; i++) if (i != main) extra.Add(new BrushPolyhedron.Face(polys[i], f));
            }
            faces.AddRange(extra);
            foreach (var kv in polygonsOf)
            {
                if (kv.Key < original.faces.Length) continue;
                int source = sourceOf != null && sourceOf.TryGetValue(kv.Key, out var s) ? s : -1;
                foreach (var poly in kv.Value) faces.Add(new BrushPolyhedron.Face(poly, source));
            }
            var result = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            result.WeldCoincident(1e-5f);
            result.EnsureOutward();
            return result;
        }

        static float Area(int[] loop, List<Vector3> verts)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < loop.Length; i++) { var a = verts[loop[i]]; var b = verts[loop[(i + 1) % loop.Length]]; n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y); }
            return n.magnitude * 0.5f;
        }
    }
}

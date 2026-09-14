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
        /// <summary>Diagnostics of the last rebuild (tests).</summary>
        public static readonly System.Text.StringBuilder DebugLog = new System.Text.StringBuilder();

        /// <summary>
        /// Extrude faces through a boolean. Returns the new polyhedron and, per original face index, its index in
        /// the result (-1 when the face is gone). Null when Manifold rejects the input or the result is empty.
        /// </summary>
        public static BrushPolyhedron ExtrudeFaces(BrushPolyhedron poly, IEnumerable<int> faceIndices, float distance, bool individual, out int[] faceRemap)
        {
            faceRemap = null; LastRefusal = null;
            var groups = new List<List<int>>();
            if (individual) { var seenFaces = new HashSet<int>(); foreach (var f in faceIndices) if (f >= 0 && f < poly.faces.Length && seenFaces.Add(f)) groups.Add(new List<int> { f }); }
            else { var all = new List<int>(); foreach (var f in faceIndices) if (f >= 0 && f < poly.faces.Length && !all.Contains(f)) all.Add(f); if (all.Count > 0) groups.Add(all); }
            if (groups.Count == 0 || Mathf.Abs(distance) < 1e-6f) return null;

            var brushFaces = new List<int[]>(); foreach (var f in poly.faces) brushFaces.Add(f.indices);
            using var brush = ManifoldSolid.FromFaces(poly.vertices, brushFaces, out _);
            if (brush == null) return null;
            int nextId = poly.faces.Length;
            var sourceOf = new Dictionary<int, int>(); // new face id -> the face it was extruded from
            var expected = new List<Vector3>(poly.vertices); // where the result's corners belong: the rebuild snaps to these
            var result = brush;
            bool ownsResult = false;
            // Manifold resolves faces that coincide exactly by symbolic perturbation, which can leave zero-thickness
            // sheets in the result. So no face of a prism ever coincides with anything: the prism is a hair longer at
            // both ends and a hair wider than the face it extrudes, and the rebuild welds that hair away again.
            const float hair = Hair;
            foreach (var group in groups)
            {
                Vector3 normal = Vector3.zero;
                foreach (var f in group) { var pl = poly.Plane(f); normal += new Vector3(pl.x, pl.y, pl.z); }
                if (normal.sqrMagnitude < 1e-10f) { var pl = poly.Plane(group[0]); normal = new Vector3(pl.x, pl.y, pl.z); }
                var offset = normal.normalized * distance;
                var dir = offset.normalized;
                // faces that move along their normal add material (union), faces that move against it remove it
                // (difference); each part is its own prism, so every shell is consistently oriented
                foreach (int sign in new[] { 1, -1 })
                {
                    var part = new List<int>();
                    foreach (var f in group) { var pl = poly.Plane(f); float d = Vector3.Dot(new Vector3(pl.x, pl.y, pl.z), offset); if (sign > 0 ? d > 1e-6f : d < -1e-6f) part.Add(f); }
                    if (part.Count == 0) continue;
                    // faces that share an edge move as one patch and make one prism; faces that only touch at a
                    // corner (or not at all) make separate prisms, so no shell ever shares a vertex with another
                    var byEdge = new Dictionary<long, List<int>>();
                    foreach (var f in part)
                    {
                        var idx = poly.faces[f].indices;
                        for (int i = 0; i < idx.Length; i++) { long k = Key(idx[i], idx[(i + 1) % idx.Length]); if (!byEdge.TryGetValue(k, out var l)) byEdge[k] = l = new List<int>(); l.Add(f); }
                    }
                    var patchOf = new Dictionary<int, int>(); var patches = new List<List<int>>();
                    foreach (var seed in part)
                    {
                        if (patchOf.ContainsKey(seed)) continue;
                        var patch = new List<int>(); var stack = new Stack<int>(); stack.Push(seed); patchOf[seed] = patches.Count;
                        while (stack.Count > 0)
                        {
                            int f = stack.Pop(); patch.Add(f);
                            var idx = poly.faces[f].indices;
                            for (int i = 0; i < idx.Length; i++)
                                foreach (var g in byEdge[Key(idx[i], idx[(i + 1) % idx.Length])])
                                    if (!patchOf.ContainsKey(g)) { patchOf[g] = patches.Count; stack.Push(g); }
                        }
                        patches.Add(patch);
                    }
                    foreach (var patch in patches)
                    {
                        var edgeCount = new Dictionary<long, int>();
                        foreach (var f in patch)
                        {
                            var idx = poly.faces[f].indices;
                            for (int i = 0; i < idx.Length; i++) { long k = Key(idx[i], idx[(i + 1) % idx.Length]); edgeCount[k] = edgeCount.TryGetValue(k, out var c) ? c + 1 : 1; }
                        }
                        // the outline of the patch moves outward by a hair (in the plane of the face each outline edge belongs to)
                        var outward = new Dictionary<int, List<Vector3>>();
                        foreach (var f in patch)
                        {
                            var idx = poly.faces[f].indices; var pl = poly.Plane(f); var n = new Vector3(pl.x, pl.y, pl.z);
                            for (int i = 0; i < idx.Length; i++)
                            {
                                int a = idx[i], b = idx[(i + 1) % idx.Length];
                                if (edgeCount[Key(a, b)] != 1) continue;
                                var m = Vector3.Cross(poly.vertices[b] - poly.vertices[a], n).normalized;
                                if (!outward.TryGetValue(a, out var la)) outward[a] = la = new List<Vector3>(); la.Add(m);
                                if (!outward.TryGetValue(b, out var lb)) outward[b] = lb = new List<Vector3>(); lb.Add(m);
                            }
                        }
                        Vector3 Widen(int v)
                        {
                            if (!outward.TryGetValue(v, out var ms) || ms.Count == 0) return Vector3.zero;
                            if (ms.Count == 1) return ms[0] * hair;
                            var sum = Vector3.zero; foreach (var m in ms) sum += m;
                            float k = ms.Count == 2 ? Mathf.Max(1f + Vector3.Dot(ms[0], ms[1]), 0.5f) : Mathf.Max(sum.magnitude, 0.5f); // mitre, capped at sharp corners so the move stays within welding distance
                            return sum * (hair / k);
                        }
                        var verts = new List<Vector3>(); var faces = new List<int[]>(); var ids = new List<int>();
                        var bottom = new Dictionary<int, int>(); var top = new Dictionary<int, int>();
                        int B(int v) { if (!bottom.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(poly.vertices[v] + Widen(v) - dir * hair); bottom[v] = i; } return i; }
                        int T(int v) { if (!top.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(poly.vertices[v] + Widen(v) + offset + dir * hair); top[v] = i; expected.Add(poly.vertices[v] + offset); } return i; }
                        foreach (var f in patch)
                        {
                            var idx = poly.faces[f].indices;
                            var bot = new int[idx.Length]; var tp = new int[idx.Length];
                            for (int i = 0; i < idx.Length; i++) { bot[idx.Length - 1 - i] = B(idx[i]); tp[i] = T(idx[i]); }
                            faces.Add(bot); ids.Add(f);   // vanishes inside the brush (union) or becomes the pocket floor (difference)
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
                        var next = ManifoldSolid.Boolean(result, prism, sign > 0 ? ManifoldNative.OpType.Add : ManifoldNative.OpType.Subtract);
                        if (ownsResult) result.Dispose();
                        result = next; ownsResult = true;
                    }
                }
            }
            if (!ownsResult) { LastRefusal = "The prism could not be built."; return null; }
            // the hair-sized steps and slivers the grown prisms leave are collapsed by Manifold itself, which keeps
            // the mesh manifold while doing so; what remains is welded below
            using var simplified = result.Simplify(1e-4);
            result.Dispose();
            var mesh = simplified.ToMesh();
            DebugLog.Clear();
            {
                // nothing left (a cut that removed everything) is a refused edit, not a shape
                double volume = 0;
                if (mesh.triangles != null) for (int t = 0; t + 2 < mesh.triangles.Length; t += 3) volume += Vector3.Dot(mesh.vertices[mesh.triangles[t]], Vector3.Cross(mesh.vertices[mesh.triangles[t + 1]], mesh.vertices[mesh.triangles[t + 2]])) / 6.0;
                if (volume < 1e-6) { LastRefusal = "Nothing would be left of the brush."; return null; }
            }
            DebugLog.Append("mesh: " + mesh.vertices.Length + " verts, " + mesh.triangles.Length / 3 + " tris\n");
            var rebuilt = FromMesh(mesh, poly, sourceOf, expected, out faceRemap);
            // a rebuild that is not a sound shape (the solid touches itself where a prism ends exactly on another
            // face, say) is refused rather than handed to the brush: the brush stays as it was
            string why = null;
            if (rebuilt == null || !rebuilt.IsValid || !rebuilt.IsClosed() || !rebuilt.IsSound(out why))
            {
                LastRefusal = "The result would be " + (why ?? "invalid") + ". Try another distance.";
                faceRemap = null;
                return null;
            }
            LastRefusal = null;
            return rebuilt;
        }

        static long Key(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        /// <summary>Why the last <see cref="ExtrudeFaces"/> returned null although the faces were valid (the result would not have been a sound shape), or null.</summary>
        public static string LastRefusal;

        /// <summary>How far a prism reaches past the face it extrudes, and how much wider it is.</summary>
        const float Hair = 1e-4f;

        /// <summary>
        /// A polyhedron from Manifold's triangles: triangles are grouped by face id and connectivity and merged back
        /// into polygons (a group whose outline has holes stays as triangles). Original faces keep their index when
        /// they survive; new faces are appended with their source set. <paramref name="expected"/> are positions
        /// the result's corners are known to belong at (the brush's own corners, the moved faces' corners): a
        /// vertex within welding distance of one lands exactly there.
        /// </summary>
        public static BrushPolyhedron FromMesh(ManifoldSolid.MeshData mesh, BrushPolyhedron original, Dictionary<int, int> sourceOf, IList<Vector3> expected, out int[] faceRemap)
        {
            // Manifold's topology is kept: the vertices it split between face ids are merged back (it says which),
            // and nothing else is merged by position, except around the corners the result is known to have. The
            // moved faces' corners come back a hair further than asked (the prism is a hair longer at both ends, so
            // its top never coincides with a face it lands on) and whatever lands within snapping distance of an
            // expected position (a corner of the brush, a corner of a moved face) is moved onto it and merged. The
            // hair-thin band that leaves collapses into triangles with repeated corners, which are dropped.
            var parent = new int[mesh.vertices.Length]; for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
            if (mesh.mergeFrom != null) for (int i = 0; i < mesh.mergeFrom.Length; i++) { int a = Find(mesh.mergeFrom[i]), b = Find(mesh.mergeTo[i]); if (a != b) parent[a] = b; }
            const float snap = 5e-4f;
            int snapped = 0;
            var expectedCells = new Dictionary<(int, int, int), List<int>>();
            if (expected != null)
                for (int k = 0; k < expected.Count; k++)
                {
                    var e = expected[k]; var key = (Mathf.FloorToInt(e.x / snap), Mathf.FloorToInt(e.y / snap), Mathf.FloorToInt(e.z / snap));
                    if (!expectedCells.TryGetValue(key, out var list)) expectedCells[key] = list = new List<int>();
                    list.Add(k);
                }
            int NearestExpected(Vector3 v)
            {
                int cx = Mathf.FloorToInt(v.x / snap), cy = Mathf.FloorToInt(v.y / snap), cz = Mathf.FloorToInt(v.z / snap);
                int nearest = -1; float nearestD = snap * snap;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (expectedCells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list))
                        foreach (var k in list) { float d = (expected[k] - v).sqrMagnitude; if (d <= nearestD) { nearestD = d; nearest = k; } }
                return nearest;
            }
            var vmap = new int[mesh.vertices.Length]; var verts = new List<Vector3>();
            {
                var ofRoot = new Dictionary<int, int>(); var ofExpected = new Dictionary<int, int>();
                for (int i = 0; i < vmap.Length; i++)
                {
                    int root = Find(i);
                    if (ofRoot.TryGetValue(root, out int k)) { vmap[i] = k; continue; }
                    int e = NearestExpected(mesh.vertices[i]);
                    if (e >= 0)
                    {
                        if (!ofExpected.TryGetValue(e, out k)) { k = verts.Count; verts.Add(expected[e]); ofExpected[e] = k; }
                        snapped++;
                    }
                    else { k = verts.Count; verts.Add(mesh.vertices[i]); }
                    ofRoot[root] = k; vmap[i] = k;
                }
            }
            DebugLog.Append("  vertices " + verts.Count + " (from " + mesh.vertices.Length + "), snapped " + snapped + "\n");
            var tris = new List<(int a, int b, int c, int id)>();
            int Tri(int t, int c) => c == 0 ? tris[t].a : c == 1 ? tris[t].b : tris[t].c;
            for (int t = 0; t * 3 + 2 < mesh.triangles.Length; t++)
            {
                int a = vmap[mesh.triangles[t * 3]], b = vmap[mesh.triangles[t * 3 + 1]], c = vmap[mesh.triangles[t * 3 + 2]];
                if (a == b || b == c || a == c) continue;
                tris.Add((a, b, c, mesh.triangleFace[t]));
            }
            // a zero-thickness sheet (the same triangle twice, facing both ways) is no surface: drop both
            // (only opposite pairs cancel: a third copy with the same facing stays, or the mesh would open)
            var facing = new Dictionary<(int, int, int), List<int>>(); var dropped = new HashSet<int>();
            for (int t = 0; t < tris.Count; t++)
            {
                var sorted = Sorted(tris[t].a, tris[t].b, tris[t].c);
                if (!facing.TryGetValue(sorted, out var list)) facing[sorted] = list = new List<int>();
                list.Add(t);
            }
            foreach (var list in facing.Values)
            {
                if (list.Count < 2) continue;
                var up = new List<int>(); var down = new List<int>();
                foreach (var t in list) (SameCycle(tris[t], tris[list[0]]) ? up : down).Add(t);
                for (int i = 0; i < Mathf.Min(up.Count, down.Count); i++) { dropped.Add(up[i]); dropped.Add(down[i]); }
            }
            if (dropped.Count > 0) { var kept = new List<(int a, int b, int c, int id)>(); for (int t = 0; t < tris.Count; t++) if (!dropped.Contains(t)) kept.Add(tris[t]); tris = kept; }
            // how many triangles use each edge: a polygon may only grow across an edge used by exactly two (a manifold
            // interior edge); an edge a third face stands on is a crease and stays a polygon boundary
            var edgeUse = new Dictionary<long, int>();
            foreach (var tri in tris)
                foreach (var (x, y) in new[] { (tri.a, tri.b), (tri.b, tri.c), (tri.c, tri.a) }) { long k = Key(x, y); edgeUse[k] = edgeUse.TryGetValue(k, out var n) ? n + 1 : 1; }
            {
                int bad = 0; foreach (var kv in edgeUse) if (kv.Value != 2) bad++;
                DebugLog.Append("  triangles " + tris.Count + ", sheets dropped " + dropped.Count + ", edges not used twice " + bad + "\n");
            }
            var byId = new Dictionary<int, List<int>>();
            for (int t = 0; t < tris.Count; t++)
            {
                if (!byId.TryGetValue(tris[t].id, out var list)) byId[tris[t].id] = list = new List<int>();
                list.Add(t);
            }
            Vector4 TrianglePlane(int t)
            {
                var pa = verts[Tri(t, 0)]; var pb = verts[Tri(t, 1)]; var pc = verts[Tri(t, 2)];
                var n = Vector3.Cross(pb - pa, pc - pa); if (n.sqrMagnitude < 1e-20f) return Vector4.zero; n.Normalize();
                return new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, pa));
            }
            bool Coplanar(Vector4 p, Vector4 q)
            {
                if (p == Vector4.zero || q == Vector4.zero) return false; // a degenerate triangle has no plane to share
                return Vector3.Dot(new Vector3(p.x, p.y, p.z), new Vector3(q.x, q.y, q.z)) > 0.99999f && Mathf.Abs(p.w - q.w) < 2e-4f;
            }
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
                    var plane0 = TrianglePlane(first);
                    while (stack.Count > 0)
                    {
                        int t = stack.Pop(); component.Add(t);
                        for (int c = 0; c < 3; c++) edgeOwner[Key(Tri(t, c), Tri(t, (c + 1) % 3))] = t;
                        foreach (var o in new List<int>(remaining))
                        {
                            if (!Coplanar(plane0, TrianglePlane(o))) continue; // same id but another plane: its own polygon
                            for (int c = 0; c < 3; c++)
                            {
                                long k = Key(Tri(o, c), Tri(o, (c + 1) % 3));
                                if (edgeOwner.ContainsKey(k) && edgeUse.TryGetValue(k, out var uses) && uses == 2) { stack.Push(o); remaining.Remove(o); break; }
                            }
                        }
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
                    DebugLog.Append("  id " + kv.Key + ": component of " + component.Count + " tris, boundary " + boundary + ", loop " + (loop != null ? loop.Length.ToString() : "none") + (nextOf == null ? " (pinched)" : "") + "\n");
                    // the snapped corners can leave a merged outline slightly bent; a face must stay planar, so such
                    // a component keeps its triangles (each of which is planar by nature)
                    if (loop != null && loop.Length >= 3 && PlanarityOf(loop, verts) <= 5e-4f) polys.Add(loop);
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
            // vertices no face uses any more (merged away) are dropped
            var used = new int[verts.Count]; for (int i = 0; i < used.Length; i++) used[i] = -1;
            var compact = new List<Vector3>();
            foreach (var face in faces) foreach (var i in face.indices) if (used[i] < 0) { used[i] = compact.Count; compact.Add(verts[i]); }
            foreach (var face in faces) for (int k = 0; k < face.indices.Length; k++) face.indices[k] = used[face.indices[k]];
            var result = new BrushPolyhedron { vertices = compact.ToArray(), faces = faces.ToArray() };
            result.EnsureOutward();
            return result;
        }

        static bool SameCycle((int a, int b, int c, int id) t, (int a, int b, int c, int id) u)
        {
            // same corners: the same cyclic order means the same facing
            return (t.a == u.a && t.b == u.b) || (t.a == u.b && t.b == u.c) || (t.a == u.c && t.b == u.a);
        }

        static (int, int, int) Sorted(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a); if (b > c) (b, c) = (c, b); if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }

        static float PlanarityOf(int[] loop, List<Vector3> verts)
        {
            Vector3 n = Vector3.zero; Vector3 centre = Vector3.zero;
            for (int i = 0; i < loop.Length; i++) { var a = verts[loop[i]]; var b = verts[loop[(i + 1) % loop.Length]]; n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y); centre += a; }
            if (n.sqrMagnitude < 1e-20f) return float.MaxValue;
            n.Normalize(); centre /= loop.Length;
            float worst = 0f; foreach (var i in loop) worst = Mathf.Max(worst, Mathf.Abs(Vector3.Dot(verts[i] - centre, n)));
            return worst;
        }

        static float Area(int[] loop, List<Vector3> verts)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < loop.Length; i++) { var a = verts[loop[i]]; var b = verts[loop[(i + 1) % loop.Length]]; n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y); }
            return n.magnitude * 0.5f;
        }
    }
}

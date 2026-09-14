using System;
using System.Collections.Generic;
using UnityEngine;
using static CsgBrush.Manifold.ManifoldNative;

namespace CsgBrush.Manifold
{
    /// <summary>
    /// A solid held by the Manifold library. Built from polygon faces, combined with booleans, read back as a
    /// triangle mesh that knows for every triangle which source solid and which of its faces it came from.
    /// Positions go in and out as doubles; the face index travels as a fourth vertex property.
    /// </summary>
    public sealed class ManifoldSolid : IDisposable
    {
        IntPtr handle;
        public IntPtr Handle => handle;
        public bool IsValid => handle != IntPtr.Zero;

        ManifoldSolid(IntPtr h) { handle = h; }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) { manifold_delete_manifold(handle); handle = IntPtr.Zero; }
        }

        public bool IsEmpty => manifold_is_empty(handle) != 0;
        public Error Status => manifold_status(handle);
        public double Volume => manifold_volume(handle);
        public int TriangleCount => (int)(ulong)manifold_num_tri(handle);
        /// <summary>The id output runs refer to (see <see cref="ToMesh"/>); assigned by <see cref="FromFaces"/>.</summary>
        public int OriginalId => manifold_original_id(handle);

        public static ManifoldSolid Empty()
        {
            var mem = manifold_alloc_manifold();
            return new ManifoldSolid(manifold_empty(mem));
        }

        /// <summary>
        /// A solid from planar polygon faces in Unity's front-face winding (outward normal = cross(b - a, c - a)). Every face gets
        /// its own vertices so the face index can ride along as a property; Manifold merges them by position.
        /// Returns null when Manifold rejects the input (not closed, degenerate).
        /// </summary>
        public static ManifoldSolid FromFaces(IList<Vector3> vertices, IList<int[]> faces, out Error error) => FromFaces(vertices, faces, out error, null);

        /// <summary>As above, with an explicit id per face carried as the vertex property (default: the face index).</summary>
        public static ManifoldSolid FromFaces(IList<Vector3> vertices, IList<int[]> faces, out Error error, IList<int> faceIds)
        {
            const int props = 4;
            // winding-agnostic: orient by the signed volume so the normals point outward whichever way the faces are wound
            double signed = 0;
            foreach (var f in faces)
                for (int k = 1; k + 1 < f.Length; k++)
                {
                    Vector3 a = vertices[f[0]], b = vertices[f[k]], c = vertices[f[k + 1]];
                    signed += Vector3.Dot(a, Vector3.Cross(b, c));
                }
            bool flip = signed < 0;
            int cornerCount = 0, triCount = 0;
            foreach (var f in faces) { cornerCount += f.Length; triCount += Math.Max(0, f.Length - 2); }
            var vp = new double[cornerCount * props];
            var tris = new ulong[triCount * 3];
            int vi = 0, ti = 0;
            var triangulated = new List<int>();
            for (int f = 0; f < faces.Count; f++)
            {
                var face = faces[f];
                int first = vi;
                for (int k = 0; k < face.Length; k++, vi++)
                {
                    var p = vertices[face[k]];
                    vp[vi * props] = p.x; vp[vi * props + 1] = p.y; vp[vi * props + 2] = p.z; vp[vi * props + 3] = faceIds != null ? faceIds[f] : f;
                }
                // Unity's front-face winding already gives outward normals for cross(b - a, c - a), which is what Manifold expects
                Triangulate(vertices, face, triangulated);
                for (int k = 0; k + 2 < triangulated.Count; k += 3)
                {
                    tris[ti++] = (ulong)(first + triangulated[k]);
                    tris[ti++] = (ulong)(first + (flip ? triangulated[k + 2] : triangulated[k + 1]));
                    tris[ti++] = (ulong)(first + (flip ? triangulated[k + 1] : triangulated[k + 2]));
                }
            }
            if (ti < tris.Length) Array.Resize(ref tris, ti);
            triCount = ti / 3;
            var meshMem = manifold_alloc_meshgl64();
            var mesh = manifold_meshgl64(meshMem, vp, (UIntPtr)cornerCount, (UIntPtr)props, tris, (UIntPtr)triCount);
            var mergedMem = manifold_alloc_meshgl64();
            var merged = manifold_meshgl64_merge(mergedMem, mesh); // returns `mesh` itself when nothing had to merge
            var solidMem = manifold_alloc_manifold();
            var raw = manifold_of_meshgl64(solidMem, merged);
            if (merged != mesh) manifold_delete_meshgl64(merged); else manifold_delete_meshgl64(mergedMem);
            manifold_delete_meshgl64(mesh);
            error = manifold_status(raw);
            if (error != Error.NoError) { manifold_delete_manifold(raw); return null; }
            var origMem = manifold_alloc_manifold();
            var orig = manifold_as_original(origMem, raw);
            manifold_delete_manifold(raw);
            return new ManifoldSolid(orig);
        }

        /// <summary>
        /// Triangles of a planar polygon as local corner indices: a fan when convex, ear clipping otherwise (a hand-edited
        /// face may have a notch). Output is wound like the input.
        /// </summary>
        static void Triangulate(IList<Vector3> vertices, int[] face, List<int> result)
        {
            result.Clear();
            int n = face.Length;
            if (n < 3) return;
            // project onto the dominant plane of the Newell normal
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++) { var a = vertices[face[i]]; var b = vertices[face[(i + 1) % n]]; normal.x += (a.y - b.y) * (a.z + b.z); normal.y += (a.z - b.z) * (a.x + b.x); normal.z += (a.x - b.x) * (a.y + b.y); }
            float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var p = vertices[face[i]];
                pts[i] = ax >= ay && ax >= az ? new Vector2(p.y, p.z) : ay >= az ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y);
            }
            // signed area gives the winding in the projection; cross products of consecutive edges tell convexity
            float area = 0f; for (int i = 0; i < n; i++) { var a = pts[i]; var b = pts[(i + 1) % n]; area += a.x * b.y - b.x * a.y; }
            float sign = area >= 0f ? 1f : -1f;
            bool convex = true;
            for (int i = 0; i < n && convex; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n]; var c = pts[(i + 2) % n];
                if (((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)) * sign < -1e-9f) convex = false;
            }
            if (convex) { for (int k = 1; k + 1 < n; k++) { result.Add(0); result.Add(k); result.Add(k + 1); } return; }
            var ring = new List<int>(n); for (int i = 0; i < n; i++) ring.Add(i);
            int guard = 0;
            while (ring.Count > 3 && guard++ < n * n)
            {
                bool clipped = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int ia = ring[(i + ring.Count - 1) % ring.Count], ib = ring[i], ic = ring[(i + 1) % ring.Count];
                    var a = pts[ia]; var b = pts[ib]; var c = pts[ic];
                    float cross = ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x)) * sign;
                    if (cross <= 1e-12f) continue; // reflex or degenerate corner
                    bool empty = true;
                    for (int j = 0; j < ring.Count && empty; j++)
                    {
                        int ip = ring[j]; if (ip == ia || ip == ib || ip == ic) continue;
                        var p = pts[ip];
                        float d1 = ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x)) * sign;
                        float d2 = ((c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x)) * sign;
                        float d3 = ((a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x)) * sign;
                        if (d1 >= 0f && d2 >= 0f && d3 >= 0f) empty = false;
                    }
                    if (!empty) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    ring.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break; // degenerate polygon: fan the rest
            }
            if (ring.Count >= 3) for (int k = 1; k + 1 < ring.Count; k++) { result.Add(ring[0]); result.Add(ring[k]); result.Add(ring[k + 1]); }
        }

        public static ManifoldSolid Boolean(ManifoldSolid a, ManifoldSolid b, OpType op)
        {
            var mem = manifold_alloc_manifold();
            return new ManifoldSolid(manifold_boolean(mem, a.handle, b.handle, op));
        }

        /// <summary>
        /// A copy with every feature smaller than the tolerance collapsed: surfaces move by less than the tolerance
        /// and the result stays manifold. Cleans the slivers a boolean between nearly coincident faces leaves.
        /// </summary>
        public ManifoldSolid Simplify(double tolerance)
        {
            var mem = manifold_alloc_manifold();
            return new ManifoldSolid(manifoldc_unity_simplify(mem, handle, tolerance));
        }

        /// <summary>Union (or intersection) of many solids at once; cheaper than a chain of pairwise operations.</summary>
        public static ManifoldSolid Batch(IList<ManifoldSolid> solids, OpType op)
        {
            var vecMem = manifold_alloc_manifold_vec();
            var vec = manifold_manifold_empty_vec(vecMem);
            foreach (var s in solids) manifold_manifold_vec_push_back(vec, s.handle);
            var mem = manifold_alloc_manifold();
            var result = manifold_batch_boolean(mem, vec, op);
            manifold_delete_manifold_vec(vec);
            return new ManifoldSolid(result);
        }

        public struct MeshData
        {
            public Vector3[] vertices;
            public int[] triangles;
            /// <summary>Per triangle: the <see cref="OriginalId"/> of the solid it came from.</summary>
            public int[] triangleSource;
            /// <summary>Per triangle: index of the source face within that solid (the property set by <see cref="FromFaces"/>).</summary>
            public int[] triangleFace;
            /// <summary>
            /// Vertices Manifold split because their face ids differ: mergeFrom[i] and mergeTo[i] are one vertex of the
            /// solid (same position). Merging exactly these, and nothing else, recovers the manifold topology.
            /// </summary>
            public int[] mergeFrom, mergeTo;
        }

        public MeshData ToMesh()
        {
            var meshMem = manifold_alloc_meshgl64();
            var mesh = manifold_get_meshgl64(meshMem, handle);
            int numProp = (int)(ulong)manifold_meshgl64_num_prop(mesh);
            int numVert = (int)(ulong)manifold_meshgl64_num_vert(mesh);
            int numTri = (int)(ulong)manifold_meshgl64_num_tri(mesh);
            var vp = new double[(int)(ulong)manifold_meshgl64_vert_properties_length(mesh)];
            if (vp.Length > 0) manifold_meshgl64_vert_properties(vp, mesh);
            var tv = new ulong[(int)(ulong)manifold_meshgl64_tri_length(mesh)];
            if (tv.Length > 0) manifold_meshgl64_tri_verts(tv, mesh);
            var runIndex = new ulong[(int)(ulong)manifold_meshgl64_run_index_length(mesh)];
            if (runIndex.Length > 0) manifold_meshgl64_run_index(runIndex, mesh);
            var runIds = new uint[(int)(ulong)manifold_meshgl64_run_original_id_length(mesh)];
            if (runIds.Length > 0) manifold_meshgl64_run_original_id(runIds, mesh);
            int mergeCount = (int)(ulong)manifold_meshgl64_merge_length(mesh);
            var mergeFrom = new ulong[mergeCount]; var mergeTo = new ulong[mergeCount];
            if (mergeCount > 0) { manifold_meshgl64_merge_from_vert(mergeFrom, mesh); manifold_meshgl64_merge_to_vert(mergeTo, mesh); }
            manifold_delete_meshgl64(mesh);

            var data = new MeshData
            {
                vertices = new Vector3[numVert],
                triangles = new int[numTri * 3],
                triangleSource = new int[numTri],
                triangleFace = new int[numTri],
                mergeFrom = new int[mergeCount],
                mergeTo = new int[mergeCount],
            };
            for (int i = 0; i < mergeCount; i++) { data.mergeFrom[i] = (int)mergeFrom[i]; data.mergeTo[i] = (int)mergeTo[i]; }
            for (int v = 0; v < numVert; v++)
                data.vertices[v] = new Vector3((float)vp[v * numProp], (float)vp[v * numProp + 1], (float)vp[v * numProp + 2]);
            for (int t = 0; t < numTri; t++)
            {
                int a = (int)tv[t * 3], b = (int)tv[t * 3 + 1], c = (int)tv[t * 3 + 2];
                data.triangles[t * 3] = a; data.triangles[t * 3 + 1] = b; data.triangles[t * 3 + 2] = c;
                data.triangleFace[t] = numProp > 3 ? (int)Math.Round(vp[a * numProp + 3]) : -1;
            }
            for (int r = 0; r + 1 < runIndex.Length && r < runIds.Length; r++)
            {
                int from = (int)(runIndex[r] / 3), to = (int)(runIndex[r + 1] / 3);
                for (int t = from; t < to && t < numTri; t++) data.triangleSource[t] = (int)runIds[r];
            }
            return data;
        }
    }
}

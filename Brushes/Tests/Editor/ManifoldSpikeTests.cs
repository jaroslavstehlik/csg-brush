using System.Collections.Generic;
using System.Diagnostics;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using CsgBrush.Colliders.Editor;
using CsgBrush.Manifold;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Tests
{
    /// <summary>Spike: can the Manifold library replace Chisel's CSG core? Correctness on a saved level, and timing.</summary>
    public class ManifoldSpikeTests
    {
        static ManifoldSolid Cube(Vector3 min, Vector3 max, out ManifoldNative.Error error)
        {
            var v = new List<Vector3>
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
            };
            // clockwise seen from outside
            var faces = new List<int[]>
            {
                new[] { 0, 1, 2, 3 }, // bottom (seen from below: 0,1,2,3 is clockwise)
                new[] { 7, 6, 5, 4 }, // top
                new[] { 4, 5, 1, 0 }, // back (-z)
                new[] { 6, 7, 3, 2 }, // front (+z)
                new[] { 7, 4, 0, 3 }, // left (-x)
                new[] { 5, 6, 2, 1 }, // right (+x)
            };
            return ManifoldSolid.FromFaces(v, faces, out error);
        }

        [Test]
        public void InvertedWindingIsOriented()
        {
            var v = new List<Vector3> { Vector3.zero, Vector3.right, Vector3.right + Vector3.forward, Vector3.forward, Vector3.up, Vector3.up + Vector3.right, Vector3.one, Vector3.up + Vector3.forward };
            var faces = new List<int[]> { new[] { 3, 2, 1, 0 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 }, new[] { 1, 2, 6, 5 } };
            using var a = ManifoldSolid.FromFaces(v, faces, out var err);
            Assert.AreEqual(ManifoldNative.Error.NoError, err);
            Assert.AreEqual(1.0, a.Volume, 1e-9, "inside-out input is oriented outward");
        }

        [Test]
        public void CubeMinusHalfCube()
        {
            using var a = Cube(Vector3.zero, Vector3.one, out var ea);
            Assert.AreEqual(ManifoldNative.Error.NoError, ea);
            Assert.IsNotNull(a);
            Assert.AreEqual(1.0, a.Volume, 1e-9);
            using var b = Cube(new Vector3(-1f, 0.5f, -1f), new Vector3(2f, 2f, 2f), out var eb);
            Assert.IsNotNull(b);
            using var c = ManifoldSolid.Boolean(a, b, ManifoldNative.OpType.Subtract);
            Assert.AreEqual(0.5, c.Volume, 1e-9);
            var mesh = c.ToMesh();
            Assert.AreEqual(12, mesh.triangles.Length / 3, "a box has 12 triangles");
            int fromA = 0, fromB = 0;
            for (int t = 0; t < mesh.triangleSource.Length; t++) { if (mesh.triangleSource[t] == a.OriginalId) fromA++; else if (mesh.triangleSource[t] == b.OriginalId) fromB++; }
            Assert.AreEqual(10, fromA, "five faces of the cube remain");
            Assert.AreEqual(2, fromB, "the cut face comes from the cutter");
            for (int t = 0; t < mesh.triangleFace.Length; t++) if (mesh.triangleSource[t] == b.OriginalId) Assert.AreEqual(0, mesh.triangleFace[t], "cut face is the cutter's bottom face");
            // Unity winding: every triangle's normal points away from the box centre
            var centre = new Vector3(0.5f, 0.25f, 0.5f);
            for (int t = 0; t < mesh.triangles.Length; t += 3)
            {
                var p0 = mesh.vertices[mesh.triangles[t]]; var p1 = mesh.vertices[mesh.triangles[t + 1]]; var p2 = mesh.vertices[mesh.triangles[t + 2]];
                var n = Vector3.Cross(p1 - p0, p2 - p0);
                Assert.Greater(Vector3.Dot(n, (p0 + p1 + p2) / 3f - centre), 0f, "clockwise from outside");
            }
        }

        struct Entry { public Brush brush; public List<ConvexPolytope> parts; public List<ManifoldSolid> solids; }

        static List<Entry> LevelEntries(out int failures, out string failureText)
        {
            var entries = new List<Entry>(); failures = 0; var sb = new System.Text.StringBuilder();
            var ordered = new List<Brush>();
            void Walk(Transform t) { var b = t.GetComponent<Brush>(); if (b != null) ordered.Add(b); for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i)); }
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects()) Walk(root.transform);
            ordered.RemoveAll(b => !b.enabled || !b.gameObject.activeInHierarchy);
            foreach (var b in ordered)
            {
                var e = new Entry { brush = b, parts = new List<ConvexPolytope>(), solids = new List<ManifoldSolid>() };
                var world = b.transform.localToWorldMatrix;
                string problem = null; var remove = new List<ConvexPolytope>();
                BrushGeometry.ConvexParts(b, world, e.parts, remove, ref problem);
                var poly = BrushGeometry.Polyhedron(b).Transformed(world);
                var faces = new List<int[]>(); foreach (var f in poly.faces) faces.Add(f.indices);
                var solid = ManifoldSolid.FromFaces(poly.vertices, faces, out var err);
                if (solid == null) { failures++; sb.Append("  " + b.name + ": " + err + " (verts " + poly.vertices.Length + " faces " + poly.faces.Length + ")\n"); }
                else e.solids.Add(solid);
                entries.Add(e);
            }
            failureText = sb.ToString();
            return entries;
        }

        /// <summary>Ordered CSG: consecutive brushes with the same operation are batched, then applied to the running result.</summary>
        static ManifoldSolid Fold(List<Entry> entries, out int operations)
        {
            operations = 0;
            var result = ManifoldSolid.Empty();
            int i = 0;
            while (i < entries.Count)
            {
                var op = entries[i].brush.operation;
                var group = new List<ManifoldSolid>();
                while (i < entries.Count && entries[i].brush.operation == op) { group.AddRange(entries[i].solids); i++; }
                if (group.Count == 0) continue;
                var combined = group.Count == 1 ? group[0] : ManifoldSolid.Batch(group, ManifoldNative.OpType.Add);
                var next = ManifoldSolid.Boolean(result, combined, op == BrushOperation.Subtract ? ManifoldNative.OpType.Subtract : ManifoldNative.OpType.Add);
                operations++;
                if (combined != group[0]) combined.Dispose();
                result.Dispose();
                result = next;
            }
            return result;
        }

        /// <summary>Scaling: the level tiled sideways several times, full rebuild time per size.</summary>
        [Test]
        public void LevelScaling()
        {
            string path = System.Environment.GetEnvironmentVariable("BRUSH_PROBE_SCENE");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("no BRUSH_PROBE_SCENE");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate(); BrushApi.ForceUpdate();
            var entries = LevelEntries(out _, out _);
            var bounds = new Bounds(); bool any = false;
            foreach (var e in entries) foreach (var p in e.parts) { if (!any) { bounds = p.GetBounds(); any = true; } else bounds.Encapsulate(p.GetBounds()); }
            float stride = bounds.size.x + 10f;
            var sb = new System.Text.StringBuilder("SCALING " + path + "\n");
            foreach (int copies in new[] { 1, 4, 8 })
            {
                var tiled = new List<Entry>();
                for (int c = 0; c < copies; c++)
                    foreach (var e in entries)
                    {
                        var t = new Entry { brush = e.brush, parts = e.parts, solids = new List<ManifoldSolid>() };
                        foreach (var part in e.parts)
                        {
                            var moved = new List<Vector3>(part.vertices.Count);
                            foreach (var v in part.vertices) moved.Add(v + new Vector3(c * stride, 0f, 0f));
                            var solid = ManifoldSolid.FromFaces(moved, part.faces, out _);
                            if (solid != null) t.solids.Add(solid);
                        }
                        tiled.Add(t);
                    }
                // order matters for the fold: keep each copy's brushes in level order, copies one after another
                var sw = Stopwatch.StartNew();
                var result = Fold(tiled, out int ops); int tris = result.TriangleCount;
                double ms = sw.Elapsed.TotalMilliseconds;
                sw.Restart(); var again = Fold(tiled, out _); int t2 = again.TriangleCount; double ms2 = sw.Elapsed.TotalMilliseconds; again.Dispose();
                sb.Append("  x" + copies + ": " + tiled.Count + " brushes, " + ops + " operations, full rebuild " + ms.ToString("F0") + " ms / " + ms2.ToString("F0") + " ms, " + tris + " triangles\n");
                result.Dispose();
                foreach (var t in tiled) foreach (var solid in t.solids) solid.Dispose();
            }
            Debug.Log(sb.ToString());
            foreach (var e in entries) foreach (var solid in e.solids) solid.Dispose();
        }

        [Test]
        public void LevelThroughManifold()
        {
            string path = System.Environment.GetEnvironmentVariable("BRUSH_PROBE_SCENE");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("no BRUSH_PROBE_SCENE");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate(); BrushApi.ForceUpdate();

            var sw = Stopwatch.StartNew();
            var entries = LevelEntries(out int failures, out string failureText);
            double buildMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var result = Fold(entries, out int ops);
            int resultTris = result.TriangleCount; // booleans are lazy: this forces the evaluation
            double foldMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var again = Fold(entries, out _);
            int againTris = again.TriangleCount;
            double foldAgainMs = sw.Elapsed.TotalMilliseconds;
            again.Dispose();
            sw.Restart();
            var mesh = result.ToMesh();
            double readMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); result.ToMesh(); double readAgainMs = sw.Elapsed.TotalMilliseconds;
            // worst case: one boolean per brush in order, no batching
            sw.Restart();
            {
                var seq = ManifoldSolid.Empty();
                foreach (var e in entries) foreach (var solid in e.solids)
                {
                    var next = ManifoldSolid.Boolean(seq, solid, e.brush.operation == BrushOperation.Subtract ? ManifoldNative.OpType.Subtract : ManifoldNative.OpType.Add);
                    seq.Dispose(); seq = next;
                }
                int seqTris = seq.TriangleCount;
                seq.Dispose();
            }
            double seqMs = sw.Elapsed.TotalMilliseconds;
            // where the time goes: union of all additive brushes only (no cutters)
            sw.Restart();
            double unionMs;
            {
                var adds = new List<ManifoldSolid>();
                foreach (var e in entries) if (e.brush.operation == BrushOperation.Add) adds.AddRange(e.solids);
                var u = ManifoldSolid.Batch(adds, ManifoldNative.OpType.Add);
                int n = u.TriangleCount; unionMs = sw.Elapsed.TotalMilliseconds; u.Dispose();
            }
            // an edit near the end with the prefix cached: fold everything but the last group, then apply only the last group
            double prefixMs, tailMs;
            {
                var withSolids = entries.FindAll(e => e.solids.Count > 0);
                int lastStart = withSolids.Count - 1; var lastOp = withSolids[lastStart].brush.operation;
                while (lastStart > 0 && withSolids[lastStart - 1].brush.operation == lastOp) lastStart--;
                sw.Restart();
                var prefix = Fold(withSolids.GetRange(0, lastStart), out _); int pn = prefix.TriangleCount; prefixMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var tail = new List<ManifoldSolid>(); for (int k = lastStart; k < withSolids.Count; k++) tail.AddRange(withSolids[k].solids);
                var tailSolid = tail.Count == 1 ? tail[0] : ManifoldSolid.Batch(tail, ManifoldNative.OpType.Add);
                var full = ManifoldSolid.Boolean(prefix, tailSolid, lastOp == BrushOperation.Subtract ? ManifoldNative.OpType.Subtract : ManifoldNative.OpType.Add);
                int fn = full.TriangleCount; tailMs = sw.Elapsed.TotalMilliseconds;
                if (tailSolid != tail[0]) tailSolid.Dispose(); full.Dispose(); prefix.Dispose();
            }
            // the scene's own render meshes, for comparison: signed volume
            double chiselVolume = 0; int chiselTris = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshCollider>() != null || mf.name.StartsWith("‹[debug")) continue;
                var mv = mf.sharedMesh.vertices; var mt = mf.sharedMesh.triangles; var m = mf.transform.localToWorldMatrix;
                for (int t = 0; t + 2 < mt.Length; t += 3)
                {
                    var p0 = m.MultiplyPoint3x4(mv[mt[t]]); var p1 = m.MultiplyPoint3x4(mv[mt[t + 1]]); var p2 = m.MultiplyPoint3x4(mv[mt[t + 2]]);
                    chiselVolume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6.0; chiselTris++;
                }
            }

            var sb = new System.Text.StringBuilder("MANIFOLD level " + path + "\n");
            sb.Append("  brushes " + entries.Count + ", solids built " + buildMs.ToString("F1") + " ms, failures " + failures + "\n" + failureText);
            sb.Append("  fold: " + ops + " boolean operations, " + foldMs.ToString("F1") + " ms first, " + foldAgainMs.ToString("F1") + " ms second; status " + result.Status + ", volume " + result.Volume.ToString("F1") + "\n");
            sb.Append("  output: " + mesh.vertices.Length + " vertices, " + mesh.triangles.Length / 3 + " triangles, read back " + readMs.ToString("F1") + " ms (again " + readAgainMs.ToString("F1") + " ms); sequential fold of every brush " + seqMs.ToString("F1") + " ms\n");
            sb.Append("  union of the additive brushes alone " + unionMs.ToString("F1") + " ms; prefix (all but the last group) " + prefixMs.ToString("F1") + " ms, then applying the last group to the cached prefix " + tailMs.ToString("F1") + " ms\n");
            sb.Append("  the scene's render meshes for the same level: " + chiselTris + " triangles, signed volume " + chiselVolume.ToString("F1") + " (Manifold " + result.Volume.ToString("F1") + ")\n");

            // coverage: no output vertex may lie strictly inside a subtract brush where a preceding additive brush overlaps it
            int notCut = 0, checkedPairs = 0;
            for (int si = 0; si < entries.Count; si++)
            {
                var s = entries[si]; if (s.brush.operation != BrushOperation.Subtract) continue;
                for (int ai = 0; ai < si; ai++)
                {
                    var a = entries[ai]; if (a.brush.operation != BrushOperation.Add || a.parts.Count == 0) continue;
                    var aBounds = a.parts[0].GetBounds(); foreach (var p in a.parts) aBounds.Encapsulate(p.GetBounds());
                    bool overlaps = false; foreach (var p in s.parts) if (p.GetBounds().Intersects(aBounds)) overlaps = true;
                    if (!overlaps) continue;
                    checkedPairs++;
                    // only A's own surface counts: geometry of brushes added after the cutter may legitimately sit inside it
                    var own = new HashSet<int>(); foreach (var solid in a.solids) own.Add(solid.OriginalId);
                    var ownVerts = new HashSet<int>();
                    for (int t = 0; t < mesh.triangleSource.Length; t++) if (own.Contains(mesh.triangleSource[t])) { ownVerts.Add(mesh.triangles[t * 3]); ownVerts.Add(mesh.triangles[t * 3 + 1]); ownVerts.Add(mesh.triangles[t * 3 + 2]); }
                    int stray = 0;
                    foreach (var vi in ownVerts)
                    {
                        var v = mesh.vertices[vi];
                        foreach (var vol in s.parts)
                        {
                            bool inside = true;
                            foreach (var pl in vol.planes) if (ConvexPolytope.Distance(pl, v) > -2e-3f) { inside = false; break; }
                            if (inside) { stray++; break; }
                        }
                    }
                    if (stray > 0) { notCut++; sb.Append("  NOT CUT: '" + a.brush.name + "' under '" + s.brush.name + "': " + stray + " vertices inside the cutter\n"); }
                }
            }
            sb.Append("  coverage: " + checkedPairs + " overlapping pairs checked, " + notCut + " not cut\n");
            Debug.Log(sb.ToString());
            foreach (var e in entries) foreach (var s in e.solids) s.Dispose();
            result.Dispose();
            Assert.AreEqual(0, failures, "brushes Manifold rejected:\n" + failureText);
            Assert.AreEqual(0, notCut, "cuts missing");
        }
    }
}

using System.Collections.Generic;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class SceneProbeTests
    {
        /// <summary>
        /// For every subtract brush in a saved scene: which additive brushes overlap it, which of those come later in
        /// CSG order (not cut by design), and whether the render mesh still has vertices inside the cutter's volume
        /// where a preceding brush overlaps it (a cut that did not happen). Run with BRUSH_PROBE_SCENE=path.
        /// </summary>
        [Test]
        public void ProbeSubtractCoverage()
        {
            string path = System.Environment.GetEnvironmentVariable("BRUSH_PROBE_SCENE");
            if (string.IsNullOrEmpty(path)) Assert.Ignore("no BRUSH_PROBE_SCENE");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            BrushHooks.EnsureAll();
            BrushApi.ForceUpdate(); BrushApi.ForceUpdate();

            var ordered = new List<Brush>();
            void Walk(Transform t) { var b = t.GetComponent<Brush>(); if (b != null) ordered.Add(b); for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i)); }
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects()) Walk(root.transform);

            ordered.RemoveAll(b => !b.enabled || !b.gameObject.activeInHierarchy); // the engine leaves inactive brushes out
            // every mesh vertex with the brush whose face it belongs to (a vertex is shared only within one face)
            var render = new List<Vector3>(); var renderBrush = new List<Brush>();
            foreach (var model in Object.FindObjectsByType<CsgGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = BrushCsg.MeshObject(model, false); if (t == null) continue;
                var mesh = t.GetComponent<MeshFilter>().sharedMesh; if (mesh == null) continue;
                var owners = BrushCsg.TriangleBrushes(model);
                var verts = mesh.vertices; var ownerOfVertex = new Brush[verts.Length]; int tri = 0;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var tris = mesh.GetTriangles(sm);
                    for (int i = 0; i < tris.Length; i += 3, tri++) { var o = owners != null && tri < owners.Length ? owners[tri] : null; ownerOfVertex[tris[i]] = o; ownerOfVertex[tris[i + 1]] = o; ownerOfVertex[tris[i + 2]] = o; }
                }
                for (int i = 0; i < verts.Length; i++) { render.Add(t.TransformPoint(verts[i])); renderBrush.Add(ownerOfVertex[i]); }
            }
            var parts = new Dictionary<Brush, List<ConvexPolytope>>();
            var bounds = new Dictionary<Brush, Bounds>();
            foreach (var b in ordered)
            {
                var add = new List<ConvexPolytope>(); var remove = new List<ConvexPolytope>(); string problem = null;
                BrushGeometry.ConvexParts(b, b.transform.localToWorldMatrix, add, remove, ref problem);
                parts[b] = add;
                var bb = BrushGeometry.Polyhedron(b).Transformed(b.transform.localToWorldMatrix).Bounds();
                bounds[b] = bb;
            }

            var sb = new System.Text.StringBuilder("SUBTRACT COVERAGE of " + path + " (" + ordered.Count + " brushes in CSG order, " + render.Count + " render vertices)\n");
            int notCut = 0;
            for (int si = 0; si < ordered.Count; si++)
            {
                var s = ordered[si];
                if (s.operation != BrushOperation.Subtract) continue;
                var volumes = parts[s];
                sb.Append("cutter '" + s.name + "' (#" + si + ", " + s.shape + ", bounds " + bounds[s].center.ToString("F1") + "/" + bounds[s].size.ToString("F1") + ", " + volumes.Count + " convex volumes)\n");
                for (int ai = 0; ai < ordered.Count; ai++)
                {
                    var a = ordered[ai];
                    if (a == s || a.operation != BrushOperation.Add) continue;
                    var aBounds = bounds[a];
                    if (!aBounds.Intersects(bounds[s])) continue;
                    var overlap = aBounds; overlap.SetMinMax(Vector3.Max(aBounds.min, bounds[s].min), Vector3.Min(aBounds.max, bounds[s].max));
                    if (overlap.size.x < 1e-3f || overlap.size.y < 1e-3f || overlap.size.z < 1e-3f) continue; // touching only
                    int stray = 0;
                    for (int vi = 0; vi < render.Count; vi++)
                    {
                        var v = render[vi];
                        if (renderBrush[vi] != a) continue; // only A's own surface counts: later brushes may sit inside the cutter
                        foreach (var vol in volumes)
                        {
                            bool inside = true;
                            foreach (var pl in vol.planes) if (ConvexPolytope.Distance(pl, v) > -2e-3f) { inside = false; break; }
                            if (inside) { stray++; break; }
                        }
                    }
                    string verdict = ai > si ? "AFTER the cutter in CSG order: not cut by design" : (stray > 0 ? "BEFORE the cutter but " + stray + " render vertices remain inside the cutter: NOT CUT" : "cut");
                    if (ai < si && stray > 0) notCut++;
                    sb.Append("   overlaps '" + a.name + "' (#" + ai + ", " + a.shape + ", overlap " + overlap.size.ToString("F2") + "): " + verdict + "\n");
                }
            }
            sb.Append("not cut: " + notCut + "\n");
            // the generated mesh alone: signed volume, and the cost of a drag frame on this level
            double volume = 0; int triangles = 0;
            foreach (var model in Object.FindObjectsByType<CsgGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = BrushCsg.MeshObject(model, false); if (t == null) continue;
                var mesh = t.GetComponent<MeshFilter>().sharedMesh; if (mesh == null) continue;
                var mv = mesh.vertices; var mt = mesh.triangles; var m = t.localToWorldMatrix;
                for (int i = 0; i + 2 < mt.Length; i += 3)
                {
                    var p0 = m.MultiplyPoint3x4(mv[mt[i]]); var p1 = m.MultiplyPoint3x4(mv[mt[i + 1]]); var p2 = m.MultiplyPoint3x4(mv[mt[i + 2]]);
                    volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6.0; triangles++;
                }
            }
            sb.Append("generated mesh: " + triangles + " triangles, signed volume " + volume.ToString("F1") + "\n");
            Brush last = null; foreach (var b in ordered) if (b.operation == BrushOperation.Add) last = b;
            if (last != null)
            {
                var start = last.transform.position; var sw = System.Diagnostics.Stopwatch.StartNew(); double total = 0;
                for (int i = 0; i < 4; i++)
                {
                    BrushApi.Move(last, start + new Vector3(i % 2 == 0 ? 1f : 0f, 0f, 0f)); sw.Restart(); BrushApi.ForceUpdate(); if (i > 0) total += sw.Elapsed.TotalMilliseconds;
                }
                sb.Append("drag frame (move '" + last.name + "' + rebuild): " + (total / 3).ToString("F1") + " ms; last build: solids " + BrushCsg.LastSolidsMs.ToString("F1") + " pieces " + BrushCsg.LastPiecesMs.ToString("F1") + " union " + BrushCsg.LastUnionMs.ToString("F1") + " mesh " + BrushCsg.LastMeshMs.ToString("F1") + " colliders " + BrushCsg.LastCollidersMs.ToString("F1") + " ms\n");
                BrushApi.Move(last, start); BrushApi.ForceUpdate();
            }
            Debug.Log(sb.ToString());
        }
    }
}

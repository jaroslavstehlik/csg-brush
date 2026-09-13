using System;
using System.Collections.Generic;
using System.Text;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// The undo and redo state oracle from research/05-brush-editor-ux.md: every operation records a hash of
    /// the brushes, the generated meshes and the convex colliders; then the whole history is walked back and
    /// forward and the hash must match at every step.
    /// </summary>
    public class BrushUndoTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            BrushSettings.instance.snapToGrid = false; // these tests use arbitrary sizes; snapping has its own tests
        }

        // ---------------------------------------------------------------- state hash

        /// <summary>Human readable version of the state, one line per element, used to explain a hash mismatch.</summary>
        public static string StateDescription()
        {
            var sb = new StringBuilder();
            var brushes = new List<Brush>(UnityEngine.Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            brushes.Sort((a, b) => string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));
            sb.AppendLine("brushes " + brushes.Count);
            foreach (var b in brushes)
            {
                var world = b.transform.localToWorldMatrix;
                var shapeBounds = BrushGeometry.Polyhedron(b).Transformed(world).Bounds();
                sb.AppendLine("  solid " + b.shape + " " + b.operation + " b=" + Q(shapeBounds.center) + "/" + Q(shapeBounds.size));
                var inner = BrushGeometry.HollowInner(b);
                if (inner != null) { var ib = inner.Transformed(world).Bounds(); sb.AppendLine("  hollow b=" + Q(ib.center) + "/" + Q(ib.size)); }
                sb.AppendLine(BrushLine(b));
            }
            foreach (var model in UnityEngine.Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var meshes = new List<string>();
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!IsLiveRenderMesh(mf)) continue;
                    meshes.Add("mesh " + mf.gameObject.name + " v=" + mf.sharedMesh.vertexCount + " t=" + mf.sharedMesh.triangles.Length + " " + MeshMetrics(mf.sharedMesh, mf.transform.localToWorldMatrix) + " bounds=" + WorldBounds(mf));
                }
                meshes.Sort();
                foreach (var m in meshes) sb.AppendLine(m);
                var pieces = new List<string>();
                var container = model.transform.Find(ConvexColliderSettings.ContainerName);
                if (container != null)
                {
                    foreach (var bc in container.GetComponentsInChildren<BoxCollider>(true)) pieces.Add("box " + Q(bc.center) + " " + Q(bc.size) + (bc.isTrigger ? " trigger" : ""));
                    foreach (var mc in container.GetComponentsInChildren<MeshCollider>(true)) pieces.Add("convex h=" + MeshHash(mc.sharedMesh, Matrix4x4.identity) + (mc.isTrigger ? " trigger" : "") + (mc.convex ? "" : " CONCAVE"));
                }
                pieces.Sort();
                if (pieces.Count > 0) sb.AppendLine("pieces " + pieces.Count);
                foreach (var p in pieces) sb.AppendLine(p);
                int containers = 0;
                foreach (Transform c in model.transform) if (c.name == ConvexColliderSettings.ContainerName) containers++;
                if (containers > 1 || pieces.Count > 0) sb.AppendLine("containers " + containers);
            }
            return sb.ToString();
        }

        /// <summary>Mesh lines compare numerically with a tolerance: area and centroid are float sums that can flip a rounding digit.</summary>
        static bool MeshLinesMatch(string x, string y)
        {
            if (!x.StartsWith("mesh ") || !y.StartsWith("mesh ")) return false;
            var rx = new System.Text.RegularExpressions.Regex(@"v=(\d+) t=(\d+) area=([-\d.]+) centroid=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\) bounds=\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)/\(([-\d.]+), ([-\d.]+), ([-\d.]+)\)");
            var mx = rx.Match(x); var my = rx.Match(y);
            if (!mx.Success || !my.Success) return x == y;
            if (mx.Groups[1].Value != my.Groups[1].Value || mx.Groups[2].Value != my.Groups[2].Value) return false;
            for (int g = 3; g <= 12; g++)
            {
                float fx = float.Parse(mx.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture), fy = float.Parse(my.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (Mathf.Abs(fx - fy) > (g == 3 ? 0.05f : 0.02f)) return false;
            }
            return true;
        }

        static string Diff(string expected, string actual)
        {
            // multiset difference, so two identical brushes versus three still shows up
            var e = new List<string>(expected.Split('\n')); var a = new List<string>(actual.Split('\n'));
            // mesh lines: tolerant numeric match first
            for (int i = e.Count - 1; i >= 0; i--)
            {
                if (!e[i].StartsWith("mesh ")) continue;
                int j = a.FindIndex(l => MeshLinesMatch(e[i], l));
                if (j >= 0) { e.RemoveAt(i); a.RemoveAt(j); }
            }
            var sb = new StringBuilder();
            var countE = new Dictionary<string, int>(); var countA = new Dictionary<string, int>();
            foreach (var line in e) countE[line] = countE.TryGetValue(line, out var n) ? n + 1 : 1;
            foreach (var line in a) countA[line] = countA.TryGetValue(line, out var n) ? n + 1 : 1;
            foreach (var kv in countE) { countA.TryGetValue(kv.Key, out var n); for (int i = n; i < kv.Value; i++) sb.AppendLine("  expected: " + kv.Key); }
            foreach (var kv in countA) { countE.TryGetValue(kv.Key, out var n); for (int i = n; i < kv.Value; i++) sb.AppendLine("  actual:   " + kv.Key); }
            return sb.ToString(); // empty when both states contain the same lines (order is not significant)
        }

        /// <summary>A build with every cache cleared: the reference for what the incremental path should produce.</summary>
        static string DescriptionAfterFullRebuild()
        {
            BrushCsg.ClearCaches();
            BrushApi.ForceUpdate();
            return StateDescription();
        }

        static string MeshLines(string desc)
        {
            var sb = new StringBuilder();
            foreach (var line in desc.Split('\n')) if (line.StartsWith("mesh ")) sb.AppendLine("    " + line);
            return sb.ToString();
        }

        public static int StateHash()
        {
            // Derived from the description so that the hash and the readable diff can never disagree.
            return StateDescription().GetHashCode();
        }

        static string BrushLine(Brush b)
        {
            int children = 0;
            string poly = "";
            if (b.shape == BrushShape.Custom && b.polyhedron != null && b.polyhedron.IsValid)
            {
                var ph = new StringBuilder(" poly=v" + b.polyhedron.vertices.Length + "f" + b.polyhedron.faces.Length + " vol=" + b.polyhedron.Volume().ToString("F3") + " verts=");
                foreach (var v in b.polyhedron.vertices) ph.Append(Q(v));
                poly = ph.ToString();
            }
            return "brush " + HierarchyPath(b.transform) + " #" + b.transform.GetSiblingIndex() + " " + b.shape + poly + " " + b.operation + " " + b.surface + " nfd=" + b.noFallDamage + " size=" + Q(b.size) + " hollow=" + b.hollow + " wall=" + Q(b.wallThickness) + " sides=" + b.sides + " tess=" + b.tessellation + " step=" + Q(b.stepHeight) + "/" + Q(b.stepDepth) + " mat=" + (b.material ? b.material.name : "-") + " pos=" + Q(b.transform.position) + " rot=" + Q(b.transform.rotation.eulerAngles) + " scl=" + Q(b.transform.localScale) + " children=" + children;
        }

        /// <summary>A generated render mesh that is actually shown.</summary>
        static bool IsLiveRenderMesh(MeshFilter mf)
        {
            if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) return false;
            if (mf.gameObject.name.StartsWith("‹[debug")) return false;
            var mr = mf.GetComponent<MeshRenderer>();
            return mr != null && mr.enabled;
        }

        static string WorldBounds(MeshFilter mf)
        {
            var verts = mf.sharedMesh.vertices; var m = mf.transform.localToWorldMatrix;
            var b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
            for (int i = 1; i < verts.Length; i++) b.Encapsulate(m.MultiplyPoint3x4(verts[i]));
            return Q(b.center) + "/" + Q(b.size);
        }

        static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }

        static Vector3 Q(Vector3 v) => new Vector3(Mathf.Round(v.x * 1000f) / 1000f, Mathf.Round(v.y * 1000f) / 1000f, Mathf.Round(v.z * 1000f) / 1000f);
        static float Q(float v) => Mathf.Round(v * 1000f) / 1000f;

        /// <summary>Triangulation independent: total area and area-weighted centroid of the surface.</summary>
        static string MeshMetrics(Mesh mesh, Matrix4x4 m)
        {
            var verts = mesh.vertices; var tris = mesh.triangles;
            double area = 0, cx = 0, cy = 0, cz = 0;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                var a = m.MultiplyPoint3x4(verts[tris[i]]); var b = m.MultiplyPoint3x4(verts[tris[i + 1]]); var c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                double ta = Vector3.Cross(b - a, c - a).magnitude * 0.5;
                var centre = (a + b + c) / 3f;
                area += ta; cx += centre.x * ta; cy += centre.y * ta; cz += centre.z * ta;
            }
            if (area > 0) { cx /= area; cy /= area; cz /= area; }
            return "area=" + area.ToString("F2") + " centroid=(" + cx.ToString("F2") + ", " + cy.ToString("F2") + ", " + cz.ToString("F2") + ")";
        }

        static int MeshHash(Mesh mesh, Matrix4x4 m)
        {
            if (mesh == null) return 0;
            unchecked
            {
                // Canonical for a given surface: total area and the area-weighted centroid do not depend on how the
                // surface was triangulated or in which order the vertices were emitted.
                var verts = mesh.vertices; var tris = mesh.triangles;
                double area = 0, cx = 0, cy = 0, cz = 0;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    var a = m.MultiplyPoint3x4(verts[tris[i]]); var b = m.MultiplyPoint3x4(verts[tris[i + 1]]); var c = m.MultiplyPoint3x4(verts[tris[i + 2]]);
                    double ta = Vector3.Cross(b - a, c - a).magnitude * 0.5;
                    var centre = (a + b + c) / 3f;
                    area += ta; cx += centre.x * ta; cy += centre.y * ta; cz += centre.z * ta;
                }
                if (area > 0) { cx /= area; cy /= area; cz /= area; }
                int h = mesh.vertexCount * 31 + tris.Length;
                h = h * 31 + Math.Round(area, 3).GetHashCode();
                h = h * 31 + Math.Round(cx, 2).GetHashCode();
                h = h * 31 + Math.Round(cy, 2).GetHashCode();
                h = h * 31 + Math.Round(cz, 2).GetHashCode();
                return h;
            }
        }

        // ---------------------------------------------------------------- harness

        static void Step(string name, Action op)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            op();
            BrushApi.ForceUpdate();
            Undo.CollapseUndoOperations(group);
            AssertConsistent("after '" + name + "'");
            // The incremental update must give the same geometry as Chisel's full rebuild.
            string incremental = StateDescription();
            string full = DescriptionAfterFullRebuild();
            if (Diff(full, incremental).Length > 0)
                Assert.Fail("incremental result differs from a full rebuild after '" + name + "':\n" + Diff(full, incremental).Replace("expected:", "full:    ").Replace("actual:  ", "incremental:"));
        }

        /// <summary>The derived structure must always match the brush fields: box generators span exactly the brush size.</summary>
        static void AssertConsistent(string when)
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // no generated children may remain on a brush; the polyhedron of a parametric shape matches its size
                foreach (Transform c in b.transform) Assert.IsFalse(Brush.IsGeneratedChildName(c.name), when + ": brush " + b.name + " still has a generated child " + c.name);
                if (b.shape == BrushShape.Box)
                {
                    var sz = BrushGeometry.Polyhedron(b).Bounds().size;
                    Assert.AreEqual(b.ClampedSize.ToString("F3"), sz.ToString("F3"), when + ": brush " + b.name + " shape size vs brush size");
                }
                bool hasHole = BrushGeometry.HollowInner(b) != null;
                Assert.AreEqual(b.IsHollow, hasHole, when + ": brush " + b.name + " hollow child");
            }
        }

        static string s_InitialDesc;
        static bool IsNoOp(List<(string name, int hash, string desc)> history, int i, int initialHash)
        {
            string previous = i > 0 ? history[i - 1].desc : s_InitialDesc;
            return Diff(previous, history[i].desc).Length == 0; // a no-op edit records nothing on Unity's undo stack
        }

        static void Walk(List<(string name, int hash, string desc)> history, int initialHash, string initialDesc)
        {
            s_InitialDesc = initialDesc;
            bool trace = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BRUSH_TEST_DUMP"));
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (IsNoOp(history, i, initialHash)) continue;
                if (trace) Debug.Log("WALK undo " + i + " '" + history[i].name + "'");
                Undo.PerformUndo();
                BrushApi.ForceUpdate();
                string expectedDesc = i > 0 ? history[i - 1].desc : initialDesc;
                string actualDesc = StateDescription();
                if (Diff(expectedDesc, actualDesc).Length > 0)
                {
                    Assert.Fail("state after undoing '" + history[i].name + "' (step " + i + ") differs (undo callbacks so far: " + BrushHooks.UndoRedoCount + "):\n" + Diff(expectedDesc, actualDesc)
                        + "  meshes after a full rebuild of the current (actual) scene:\n" + MeshLines(DescriptionAfterFullRebuild()));
                }
            }
            for (int i = 0; i < history.Count; i++)
            {
                if (IsNoOp(history, i, initialHash)) continue;
                if (trace) Debug.Log("WALK redo " + i + " '" + history[i].name + "'");
                Undo.PerformRedo();
                BrushApi.ForceUpdate();
                string actualDesc = StateDescription();
                if (Diff(history[i].desc, actualDesc).Length > 0)
                    Assert.Fail("state after redoing '" + history[i].name + "' (step " + i + ") differs:\n" + Diff(history[i].desc, actualDesc));
            }
        }

        static Bounds ColliderBounds()
        {
            bool any = false;
            var b = new Bounds();
            foreach (var model in UnityEngine.Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var container = model.transform.Find(ConvexColliderSettings.ContainerName);
                if (container == null) continue;
                foreach (var c in container.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger) continue;
                    if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds);
                }
            }
            return b;
        }

        // ---------------------------------------------------------------- tests

        [Test]
        public void ShapesAreCentredOnTheTransform([Values(BrushShape.Box, BrushShape.Wedge, BrushShape.Cylinder, BrushShape.Cone, BrushShape.Sphere, BrushShape.Stairs)] BrushShape shape)
        {
            var position = new Vector3(3f, 1.5f, -2f);
            var size = new Vector3(2f, 1f, 4f);
            var brush = BrushApi.Create(shape, position, size, Quaternion.identity);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            var bounds = ColliderBounds();
            Assert.Greater(bounds.size.magnitude, 0f, "no colliders generated for " + shape);
            Assert.AreEqual(position.x, bounds.center.x, 0.05f, shape + " centre x");
            Assert.AreEqual(position.z, bounds.center.z, 0.05f, shape + " centre z");
            Assert.AreEqual(position.y, bounds.center.y, 0.05f, shape + " centre y");
            float tol = shape == BrushShape.Sphere ? 0.16f : 0.05f; // sphere segments are inscribed (about 3.5% under)
            Assert.AreEqual(size.x, bounds.size.x, tol, shape + " size x");
            Assert.AreEqual(size.y, bounds.size.y, tol, shape + " size y");
            Assert.AreEqual(size.z, bounds.size.z, tol, shape + " size z");
        }

        [Test]
        public void DefaultMaterialMatchesTheRenderPipeline()
        {
            BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            var model = UnityEngine.Object.FindFirstObjectByType<BrushModel>();
            var mr = BrushCsg.MeshObject(model, false).GetComponent<MeshRenderer>();
            Assert.AreEqual(1, mr.sharedMaterials.Length);
            var mat = mr.sharedMaterials[0];
            Assert.IsNotNull(mat, "a default material is assigned");
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline ?? QualitySettings.renderPipeline;
            if (pipeline != null) Assert.AreEqual(pipeline.defaultMaterial, mat, "the render pipeline's default material (built-in Standard would render pink)");
        }

        [Test]
        public void ParametricStairsAreCentredAndCollide([Values(BrushShape.CurvedStairs, BrushShape.SpiralStairs)] BrushShape shape)
        {
            var position = new Vector3(3f, 1.5f, -2f);
            var brush = BrushApi.Create(shape, position, new Vector3(4f, 2f, 4f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            var bounds = ColliderBounds();
            Assert.Greater(bounds.size.magnitude, 0f, "no colliders generated for " + shape);
            Assert.AreEqual(position.ToString("F1"), bounds.center.ToString("F1"), shape + " centred");
            Assert.AreEqual(brush.size.ToString("F1"), bounds.size.ToString("F1"), "size follows the parameters");
            Assert.AreEqual(brush.numSteps, UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "one convex collider per step");
            Assert.Greater(RenderVertexCount(), 0, "renders");
        }

        [Test]
        public void HollowBoxIsARoom()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(8f, 4f, 8f), Quaternion.identity);
            BrushApi.SetHollow(brush, true, 0.5f);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.AreEqual(0, Physics.OverlapBox(Vector3.zero, Vector3.one * 0.5f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, "inside of the room must be empty");
            Assert.Greater(Physics.OverlapBox(new Vector3(3.9f, 0f, 0f), new Vector3(0.05f, 0.5f, 0.5f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, 0, "wall must be solid");
        }

        [Test]
        public void SubtractCarvesADoorway()
        {
            var wall = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 0f), new Vector3(8f, 3f, 0.5f), Quaternion.identity);
            var door = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(1.2f, 2f, 1f), Quaternion.identity);
            BrushApi.SetOperation(door, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            Physics.SyncTransforms();
            Assert.AreEqual(0, Physics.OverlapBox(new Vector3(0f, 1f, 0f), new Vector3(0.5f, 0.9f, 0.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, "doorway must be open");
            Assert.Greater(Physics.OverlapBox(new Vector3(2f, 1.5f, 0f), new Vector3(0.2f, 0.5f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, 0, "wall beside the door must be solid");
            Assert.Greater(Physics.OverlapBox(new Vector3(0f, 2.5f, 0f), new Vector3(0.2f, 0.2f, 0.1f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length, 0, "lintel must be solid");
            Assert.IsNotNull(wall);
        }

        [Test]
        public void UndoRedoOracle_Scripted()
        {
            BrushApi.ForceUpdate();
            int initial = StateHash();
            string initialDesc = StateDescription();
            var history = new List<(string, int, string)>();
            Brush a = null, b = null, c = null;
            void Record(string name, Action op)
            {
                Step(name, op);
                string d1 = StateDescription(), d2 = StateDescription();
                if (Diff(d1, d2).Length > 0) Assert.Fail("state description is not deterministic after '" + name + "':\n" + Diff(d1, d2));
                history.Add((name, d1.GetHashCode(), d1));
                var dump = Environment.GetEnvironmentVariable("BRUSH_TEST_DUMP");
                if (!string.IsNullOrEmpty(dump))
                    System.IO.File.AppendAllText(dump, "=== step " + (history.Count - 1) + " " + name + "\n" + history[history.Count - 1].Item3 + "\n");
            }

            Record("create box", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity));
            Record("create wedge", () => b = BrushApi.Create(BrushShape.Wedge, new Vector3(6f, 0f, 0f), new Vector3(2f, 1f, 3f), Quaternion.identity));
            Record("move box", () => BrushApi.Move(a, new Vector3(1f, 0.5f, 0f)));
            Record("rotate wedge", () => BrushApi.Rotate(b, Quaternion.Euler(0f, 90f, 0f)));
            Record("resize box", () => BrushApi.SetSize(a, new Vector3(6f, 3f, 6f)));
            Record("create door", () => c = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0.5f, 3f), new Vector3(1.2f, 2f, 1f), Quaternion.identity));
            Record("subtract door", () => BrushApi.SetOperation(c, BrushOperation.Subtract));
            Record("hollow box", () => BrushApi.SetHollow(a, true, 0.5f));
            Record("surface ice", () => BrushApi.SetSurface(a, ControllerSurface.Kind.Slick));
            Record("box to cylinder", () => BrushApi.SetShape(a, BrushShape.Cylinder));
            Record("cylinder to stairs", () => BrushApi.SetShape(a, BrushShape.Stairs));
            Record("stairs steps", () => BrushApi.SetStairs(a, 0.25f, 0.5f));
            Record("stairs to box", () => BrushApi.SetShape(a, BrushShape.Box));
            Record("door to first", () => BrushApi.ToFirst(c));
            Record("door to last", () => BrushApi.ToLast(c));
            Record("scale then apply", () => { Undo.RecordObject(a.transform, "scale"); a.transform.localScale = new Vector3(2f, 1f, 1f); BrushApi.ApplyScale(a); });
            Record("water volume", () => { var w = BrushApi.Create(BrushShape.Box, new Vector3(-4f, -0.5f, 0f), new Vector3(3f, 1f, 3f), Quaternion.identity); BrushApi.SetSurface(w, ControllerSurface.Kind.Water); });
            Record("delete wedge", () => BrushApi.Delete(b));
            Record("solid again", () => BrushApi.SetSurface(a, ControllerSurface.Kind.Solid));
            Record("unhollow", () => BrushApi.SetHollow(a, false, 0.5f));

            Walk(history, initial, initialDesc);
        }

        static float PieceWidthX(Brush brush)
        {
            // width along x of the largest collider piece produced for this brush's model
            var model = brush.GetComponentInParent<BrushModel>();
            if (model == null) model = UnityEngine.Object.FindFirstObjectByType<BrushModel>();
            var container = model.transform.Find(ConvexColliderSettings.ContainerName);
            float best = 0f;
            foreach (var bc in container.GetComponentsInChildren<BoxCollider>(true)) best = Mathf.Max(best, bc.size.x * bc.transform.lossyScale.x);
            return best;
        }

        static float RenderWidthX(Brush brush)
        {
            var model = brush.GetComponentInParent<BrushModel>();
            if (model == null) model = UnityEngine.Object.FindFirstObjectByType<BrushModel>();
            float best = 0f;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!IsLiveRenderMesh(mf)) continue;
                best = Mathf.Max(best, mf.sharedMesh.bounds.size.x);
            }
            return best;
        }

        [Test]
        public void ResizeRegeneratesGeometry()
        {
            var a = null as Brush;
            Step("create", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity));
            Assert.AreEqual(4f, PieceWidthX(a), 0.01f, "initial");
            Step("resize", () => BrushApi.SetSize(a, new Vector3(8f, 2f, 4f)));
            Assert.AreEqual(8f, BrushGeometry.Polyhedron(a).Bounds().size.x, 0.01f, "shape after SetSize");
            Assert.AreEqual(8f, RenderWidthX(a), 0.01f, "render mesh after SetSize");
            Assert.AreEqual(8f, PieceWidthX(a), 0.01f, "after SetSize");
            Step("scale", () => { Undo.RecordObject(a.transform, "scale"); a.transform.localScale = new Vector3(2f, 1f, 1f); });
            Assert.AreEqual(16f, PieceWidthX(a), 0.01f, "after scaling the transform");
            Step("apply", () => BrushApi.ApplyScale(a));
            Assert.AreEqual(new Vector3(16f, 2f, 4f), a.size, "size after ApplyScale");
            Assert.AreEqual(16f, PieceWidthX(a), 0.01f, "after ApplyScale");
            Step("scale+apply", () => { Undo.RecordObject(a.transform, "scale"); a.transform.localScale = new Vector3(0.5f, 1f, 1f); BrushApi.ApplyScale(a); });
            Assert.AreEqual(8f, PieceWidthX(a), 0.01f, "after scale+apply in one step");
        }

        static int RenderVertexCount()
        {
            int n = 0;
            foreach (var model in UnityEngine.Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!IsLiveRenderMesh(mf)) continue;
                    n += mf.sharedMesh.vertexCount;
                }
            return n;
        }

        [Test]
        public void DeleteRegeneratesRenderMesh()
        {
            Brush a = null, b = null;
            Step("create a", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity));
            Step("create b", () => b = BrushApi.Create(BrushShape.Box, new Vector3(8f, 0f, 0f), new Vector3(4f, 2f, 4f), Quaternion.identity));
            int two = RenderVertexCount();
            Assert.AreEqual(48, two, "two separate boxes");
            Step("delete b", () => BrushApi.Delete(b));
            Assert.AreEqual(24, RenderVertexCount(), "after delete + ForceUpdate");
        }

        [Test]
        public void CreateAfterDeletingLastBrush()
        {
            Brush a = null, b = null;
            Step("create a", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity));
            Assert.AreEqual(24, RenderVertexCount(), "one box");
            Step("delete a", () => BrushApi.Delete(a));
            Assert.AreEqual(0, RenderVertexCount(), "after deleting the only brush");
            b = BrushApi.Create(BrushShape.Wedge, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(18, RenderVertexCount(), "a wedge created after the model was emptied renders");
        }

        [Test]
        public void SiblingOrderUndo()
        {
            Brush a = null, b = null, c = null;
            Step("create a", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity));
            Step("create b", () => b = BrushApi.Create(BrushShape.Box, Vector3.right * 3f, Vector3.one, Quaternion.identity));
            Step("create c", () => c = BrushApi.Create(BrushShape.Box, Vector3.right * 6f, Vector3.one, Quaternion.identity));
            Assert.Greater(c.transform.GetSiblingIndex(), b.transform.GetSiblingIndex(), "c starts after b");
            Step("c to first", () => BrushApi.ToFirst(c));
            Assert.AreEqual(0, c.transform.GetSiblingIndex(), "c moved first");
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.Greater(c.transform.GetSiblingIndex(), b.transform.GetSiblingIndex(), "c after b again after undo");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(0, c.transform.GetSiblingIndex(), "c first again after redo");
        }

        static Bounds RenderBounds()
        {
            var b = new Bounds(); bool first = true;
            foreach (var model in UnityEngine.Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!IsLiveRenderMesh(mf)) continue;
                    foreach (var v in mf.sharedMesh.vertices) { var w = mf.transform.TransformPoint(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
                }
            return b;
        }

        [Test]
        public void FirstBrushInNewModelRenders()
        {
            BrushApi.Create(BrushShape.Wedge, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(18, RenderVertexCount(), "first wedge renders");
        }

        [Test]
        public void RotatedBrushFollowsTransform()
        {
            var a = BrushApi.Create(BrushShape.Wedge, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(18, RenderVertexCount());
            BrushApi.Rotate(a, Quaternion.Euler(0f, 90f, 0f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(3f, 1f, 2f).ToString("F2"), RenderBounds().size.ToString("F2"), "rotated wedge render bounds");
            var box = BrushApi.Create(BrushShape.Box, new Vector3(20f, 0f, 0f), Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(18 + 24, RenderVertexCount(), "a box added next to the rotated wedge");
        }

        [Test]
        public void MoveUndoRestoresTransform()
        {
            Brush a = null;
            Step("create", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity));
            Step("move", () => BrushApi.Move(a, new Vector3(3f, 0f, 1f)));
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(Vector3.zero, a.transform.position, "position after undoing the move");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(3f, 0f, 1f), a.transform.position, "position after redo");
        }

        static string TreeOrder(BrushModel model)
        {
            var sb = new StringBuilder();
            var byModel = BrushCsg.BrushesByModel();
            if (byModel.TryGetValue(model, out var list))
                foreach (var b in list) sb.Append(b.operation.ToString()[0]).Append("1 ");
            return sb.ToString().Trim();
        }

        static string SiblingProbe(Brush b) => b.name + ": goIndex=" + b.transform.GetSiblingIndex();

        [Test]
        public void ReorderChangesCsgOrder()
        {
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(4f, 2f, 4f), Quaternion.identity);
            var b = BrushApi.Create(BrushShape.Box, new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.SetOperation(b, BrushOperation.Subtract);
            BrushApi.ForceUpdate();
            var model = UnityEngine.Object.FindFirstObjectByType<BrushModel>();
            Assert.AreEqual("A1 S1", TreeOrder(model), "additive a then subtractive b");
            Assert.Greater(RenderVertexCount(), 24, "b carves a");
            string before = SiblingProbe(a) + " | " + SiblingProbe(b);
            BrushApi.ToFirst(b);
            string moved = SiblingProbe(a) + " | " + SiblingProbe(b);
            BrushApi.ForceUpdate();
            string after = SiblingProbe(a) + " | " + SiblingProbe(b);
            Assert.AreEqual(0, b.transform.GetSiblingIndex(), "b is first in the hierarchy");
            Assert.AreEqual("S1 A1", TreeOrder(model), "CSG order follows the hierarchy order\nbefore: " + before + "\nmoved: " + moved + "\nafter: " + after);
            Assert.AreEqual(24, RenderVertexCount(), "subtract before add has no effect");
        }

        [Test]
        public void UndoBeforeDeleteStillApplies([Values(false, true)] bool brush)
        {
            Transform t;
            if (brush)
            {
                Brush a = null;
                Step("create", () => a = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity));
                t = a.transform;
                Step("rotate", () => BrushApi.Rotate(a, Quaternion.Euler(0f, 90f, 0f)));
                Step("delete", () => BrushApi.Delete(a));
            }
            else
            {
                GameObject go = null;
                Step("create", () => { go = new GameObject("plain"); Undo.RegisterCreatedObjectUndo(go, "create"); });
                t = go.transform;
                Step("rotate", () => { Undo.RecordObject(t, "rotate"); t.rotation = Quaternion.Euler(0f, 90f, 0f); });
                Step("delete", () => Undo.DestroyObjectImmediate(go));
            }
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            t = brush ? UnityEngine.Object.FindFirstObjectByType<Brush>().transform : GameObject.Find("plain").transform;
            Assert.AreEqual("(0.0, 90.0, 0.0)", t.rotation.eulerAngles.ToString("F1"), "restored by undoing the delete");
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            t = brush ? UnityEngine.Object.FindFirstObjectByType<Brush>().transform : GameObject.Find("plain").transform;
            Assert.AreEqual("(0.0, 0.0, 0.0)", t.rotation.eulerAngles.ToString("F1"), "rotation undone after the object was restored");
        }

        [Test]
        public void MoveUpdatesRenderMesh([Values(BrushShape.Box, BrushShape.Wedge, BrushShape.Cylinder)] BrushShape shape)
        {
            var a = BrushApi.Create(shape, Vector3.zero, new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(Vector3.zero.ToString("F2"), RenderBounds().center.ToString("F2"), "initial centre");
            BrushApi.Move(a, new Vector3(3f, 1f, -2f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(3f, 1f, -2f).ToString("F2"), RenderBounds().center.ToString("F2"), "render mesh centre after move");
        }

        [Test]
        public void RotateUndoRestoresTransform()
        {
            Brush a = null;
            Step("create", () => a = BrushApi.Create(BrushShape.Wedge, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.identity));
            Step("rotate", () => BrushApi.Rotate(a, Quaternion.Euler(0f, 270f, 0f)));
            Assert.AreEqual("(0.0, 270.0, 0.0)", a.transform.rotation.eulerAngles.ToString("F1"), "rotated");
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 0.0, 0.0)", a.transform.rotation.eulerAngles.ToString("F1"), "rotation after undo");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 270.0, 0.0)", a.transform.rotation.eulerAngles.ToString("F1"), "rotation after redo");
        }

        [Test]
        public void RotatedWedgeFollowsTransform()
        {
            Brush a = null;
            Step("create", () => a = BrushApi.Create(BrushShape.Wedge, Vector3.zero, new Vector3(2f, 1f, 3f), Quaternion.identity));
            Assert.AreEqual(new Vector3(2f, 1f, 3f).ToString("F2"), RenderBounds().size.ToString("F2"), "unrotated wedge bounds");
            Step("rotate", () => BrushApi.Rotate(a, Quaternion.Euler(0f, 90f, 0f)));
            Assert.AreEqual(new Vector3(3f, 1f, 2f).ToString("F2"), RenderBounds().size.ToString("F2"), "rotated wedge bounds (incremental)");
        }

        [Test]
        public void UndoRedoOracle_Fuzz([Values(1, 2, 3, 4, 5, 6)] int seed)
        {
            var rng = new System.Random(seed);
            var geometryWarnings = new List<string>();
            void Trap(string message, string stack, LogType type) { if (type == LogType.Warning && (message.Contains("Triangulator") || message.Contains("BrushMesh"))) geometryWarnings.Add(message); }
            Application.logMessageReceived += Trap;
            try
            {
            BrushApi.ForceUpdate();
            int initial = StateHash();
            string initialDesc = StateDescription();
            var history = new List<(string, int, string)>();
            var brushes = new List<Brush>();
            Vector3 RandomPos() => new Vector3(rng.Next(-8, 9), rng.Next(0, 4), rng.Next(-8, 9)) * 0.5f;
            Vector3 RandomSize() => new Vector3(rng.Next(1, 8), rng.Next(1, 6), rng.Next(1, 8)) * 0.5f;
            for (int i = 0; i < 60; i++)
            {
                brushes.RemoveAll(x => x == null);
                int op = rng.Next(0, brushes.Count == 0 ? 1 : 13);
                string name = "op" + i;
                switch (op)
                {
                    case 0: Step(name = "create", () => brushes.Add(BrushApi.Create((BrushShape)rng.Next(0, 6), RandomPos(), RandomSize(), Quaternion.identity))); break;
                    case 1: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "delete", () => BrushApi.Delete(t)); break; }
                    case 2: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "move", () => BrushApi.Move(t, RandomPos())); break; }
                    case 3: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "resize", () => BrushApi.SetSize(t, RandomSize())); break; }
                    case 4: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "operation", () => BrushApi.SetOperation(t, (BrushOperation)rng.Next(0, 2))); break; }
                    case 5: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "surface", () => BrushApi.SetSurface(t, (ControllerSurface.Kind)rng.Next(0, 5))); break; }
                    case 6: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "hollow", () => BrushApi.SetHollow(t, rng.Next(0, 2) == 1, 0.25f)); break; }
                    case 7: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "shape", () => BrushApi.SetShape(t, (BrushShape)rng.Next(0, 6))); break; }
                    case 8: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "rotate", () => BrushApi.Rotate(t, Quaternion.Euler(0f, rng.Next(0, 4) * 90f, 0f))); break; }
                    case 9: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "to first", () => BrushApi.ToFirst(t)); break; }
                    case 10: { var t = brushes[rng.Next(brushes.Count)]; Step(name = "to last", () => BrushApi.ToLast(t)); break; }
                    case 11:
                    {
                        var t = brushes[rng.Next(brushes.Count)];
                        if (!BrushApi.CanConvertToCustom(t.shape) && t.shape != BrushShape.Custom) { Step(name = "create2", () => brushes.Add(BrushApi.Create(BrushShape.Box, RandomPos(), RandomSize(), Quaternion.identity))); break; }
                        var poly = t.shape == BrushShape.Custom ? t.polyhedron : BrushApi.PolyhedronFor(t.shape, t.ClampedSize, t.sides);
                        if (rng.Next(0, 2) == 0)
                        {
                            int face = rng.Next(poly.faces.Length); float d = (rng.Next(-2, 3)) * 0.5f;
                            Step(name = "push face", () => BrushApi.PushFace(t, face, d));
                        }
                        else
                        {
                            int v = rng.Next(poly.vertices.Length);
                            var world = t.transform.TransformPoint(poly.vertices[v]) + new Vector3(rng.Next(-2, 3), rng.Next(-2, 3), rng.Next(-2, 3)) * 0.25f;
                            var trial = poly.Clone(); trial.MoveVertex(v, t.transform.InverseTransformPoint(world));
                            if (trial.IsClosed() && trial.Volume() > 0.05f) Step(name = "move vertex", () => BrushApi.MoveVertex(t, v, world));
                            else Step(name = "reset shape", () => BrushApi.ResetShape(t));
                        }
                        break;
                    }
                    default: Step(name = "create2", () => brushes.Add(BrushApi.Create(BrushShape.Box, RandomPos(), RandomSize(), Quaternion.identity))); break;
                }
                string d1 = StateDescription(), d2 = StateDescription();
                if (Diff(d1, d2).Length > 0) Assert.Fail("state description is not deterministic after '" + name + " #" + i + "':\n" + Diff(d1, d2));
                history.Add((name + " #" + i, d1.GetHashCode(), d1));
                var dump = Environment.GetEnvironmentVariable("BRUSH_TEST_DUMP");
                if (!string.IsNullOrEmpty(dump))
                    System.IO.File.AppendAllText(dump, "=== fuzz " + seed + " step " + i + " " + name + "\n" + history[history.Count - 1].Item3 + "\n");
                if (geometryWarnings.Count > 0)
                    Assert.Fail("geometry warning after '" + name + " #" + i + "':\n  " + string.Join("\n  ", geometryWarnings) + "\nstate:\n" + d1);
            }
            Walk(history, initial, initialDesc);
            }
            finally { Application.logMessageReceived -= Trap; }
        }
    }
}

using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    public class ExtrudeFuzzTests
    {
        static BrushPolyhedron Shape(int kind)
        {
            switch (kind % 5)
            {
                case 0: return BrushPolyhedron.Box(new Vector3(2f, 1f, 3f));
                case 1: return BrushPolyhedron.Wedge(new Vector3(2f, 1f, 3f));
                case 2: return BrushPolyhedron.Prism(new Vector3(2f, 2f, 2f), 8, 1f);
                case 3: return BrushPolyhedron.Prism(new Vector3(2f, 2f, 2f), 6, 0f);
                default: return BrushPolyhedron.Stairs(new Vector3(2f, 1f, 2f), 0.5f, 1f);
            }
        }

        /// <summary>
        /// Random extrusions on random shapes: whatever comes out is a sound shape, or the extrusion is refused and
        /// the shape is left alone. Refusals (a prism that ends exactly where the solid touches itself) are rare.
        /// </summary>
        [Test]
        public void RandomExtrusionsAreSoundOrRefused([Values(1, 2, 3)] int seed)
        {
            var rng = new System.Random(seed);
            var unsound = new List<string>(); var refused = new List<string>();
            int runs = 0;
            for (int i = 0; i < 120; i++)
            {
                var poly = Shape(rng.Next(5));
                var history = new List<string>();
                for (int step = 0; step < 3; step++)
                {
                    int count = 1 + rng.Next(3);
                    var picked = new List<int>(); for (int k = 0; k < count; k++) picked.Add(rng.Next(poly.faces.Length));
                    float distance = (rng.Next(6) - 2) * 0.5f; if (distance == 0f) distance = 0.5f;
                    bool individual = rng.Next(2) == 0;
                    history.Add("faces [" + string.Join(",", picked) + "] by " + distance + (individual ? " individual" : " group"));
                    var result = BrushBoolean.ExtrudeFaces(poly, picked, distance, individual, out var remap);
                    runs++;
                    if (result == null)
                    {
                        Assert.IsNull(remap, "no remap without a result");
                        refused.Add("shape " + i + ": " + string.Join(" | ", history) + " -> " + BrushBoolean.LastRefusal);
                        break;
                    }
                    Assert.IsNotNull(remap); Assert.AreEqual(poly.faces.Length, remap.Length, "one remap entry per original face");
                    string why = null;
                    if (!(result.IsValid && result.IsClosed() && result.IsSound(out why)))
                        unsound.Add("shape " + i + ": " + string.Join(" | ", history) + " -> " + (result.IsClosed() ? "" : "OPEN ") + why);
                    poly = result;
                }
            }
            Assert.AreEqual(0, unsound.Count, runs + " extrusions, unsound results handed out:\n" + string.Join("\n", unsound));
            // about one in twenty of these random edits ends exactly where the solid touches itself, or cuts everything away
            Assert.LessOrEqual(refused.Count, runs / 12, runs + " extrusions, too many refused:\n" + string.Join("\n", refused));
            foreach (var r in refused) StringAssert.Contains(" -> ", r); foreach (var r in refused) Assert.IsFalse(r.EndsWith("-> "), "every refusal says why: " + r);
            Debug.Log("EXTRUDE FUZZ seed " + seed + ": " + runs + " extrusions, " + refused.Count + " refused");
        }

        [Test]
        public void ExtrudingIntoANearlyTouchingSpotIsRefusedNotBroken()
        {
            // a cut into the wedge's bevel that ends exactly on the front face: the solid would touch itself
            var poly = BrushPolyhedron.Wedge(new Vector3(2f, 1f, 3f));
            var first = BrushBoolean.ExtrudeFaces(poly, new[] { 4, 0, 3 }, 0.5f, false, out _);
            Assert.IsNotNull(first);
            var result = BrushBoolean.ExtrudeFaces(first, new[] { 6 }, -1f, true, out var remap);
            if (result == null) { Assert.IsNull(remap); StringAssert.Contains("Try another distance", BrushBoolean.LastRefusal); }
            else Assert.IsTrue(result.IsSound(out var why), why);
        }

        [Test]
        public void AnUnsoundShapeDoesNotBreakTheModelAndUndoRestoresIt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var other = BrushApi.Create(BrushShape.Box, new Vector3(5f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var model = Object.FindFirstObjectByType<BrushModel>();
            int before = BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount;
            // a bow-tie face, as a broken edit would leave it
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(BrushApi.ConvertToCustom(brush));
            Undo.RecordObject(brush, "Break");
            var broken = brush.polyhedron.Transformed(Matrix4x4.identity);
            var f = broken.faces[0].indices; (f[1], f[2]) = (f[2], f[1]);
            brush.polyhedron = broken;
            BrushSync.Ensure(brush);
            BrushApi.ForceUpdate();
            Assert.IsNotNull(brush.problem, "the broken brush is flagged");
            Assert.IsNull(other.problem);
            Assert.Greater(BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount, 0, "the other brush still builds");
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "restored brush is sound: " + brush.problem);
            Assert.AreEqual(before, BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount, "mesh back to the two boxes");
        }

        static int FaceAt(BrushPolyhedron p, Vector3 normal, int source, float along)
        {
            for (int f = 0; f < p.faces.Length; f++)
            {
                if (p.faces[f].source != source || Vector3.Dot(p.Plane(f), normal) < 0.99f) continue;
                if (Mathf.Abs(Vector3.Dot(p.vertices[p.faces[f].indices[0]], normal) - along) < 1e-3f) return f;
            }
            return -1;
        }

        /// <summary>
        /// The user's own faces, edges and vertices survive an extrusion: the boolean only adds the walls and the
        /// moved face. Coplanar neighbours stay separate faces (a belt of walls keeps its edges), a face split in
        /// two keeps its other half, and extruding a wall does not redraw the faces next to it.
        /// </summary>
        [Test]
        public void ExtrusionKeepsTheUsersTopology()
        {
            var box = BrushPolyhedron.Box(new Vector3(2f, 2f, 2f));
            int top = FaceAt(box, Vector3.up, -1, 1f);
            var once = BrushBoolean.ExtrudeFaces(box, new[] { top }, 1f, true, out var remap1);
            Assert.AreEqual(10, once.faces.Length, "box plus a belt of four walls");
            var twice = BrushBoolean.ExtrudeFaces(once, new[] { remap1[top] }, 1f, true, out _);
            Assert.AreEqual(14, twice.faces.Length, "the first belt keeps its edges under the second");
            Assert.AreEqual(16, twice.vertices.Length);
            foreach (var f in twice.faces) Assert.AreEqual(4, f.indices.Length, "every face is still a quad");
            // the top face split in two: extrude one half
            var split = new BrushPolyhedron
            {
                vertices = new[] { new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(0, 1, -1), new Vector3(0, 1, 1) },
                faces = new[] { new BrushPolyhedron.Face(new[] { 0, 3, 2, 1 }), new BrushPolyhedron.Face(new[] { 4, 8, 9, 7 }), new BrushPolyhedron.Face(new[] { 8, 5, 6, 9 }), new BrushPolyhedron.Face(new[] { 0, 1, 5, 8, 4 }), new BrushPolyhedron.Face(new[] { 3, 7, 9, 6, 2 }), new BrushPolyhedron.Face(new[] { 0, 4, 7, 3 }), new BrushPolyhedron.Face(new[] { 1, 2, 6, 5 }) }
            };
            split.EnsureOutward();
            var half = BrushBoolean.ExtrudeFaces(split, new[] { 1 }, 1f, true, out var remap3);
            Assert.AreEqual(11, half.faces.Length, "the other half, a raised half and four walls");
            Assert.AreEqual(2, remap3[2], "the other half keeps its index");
            Assert.AreEqual(4, half.faces[remap3[2]].indices.Length, "and stays a quad at its height");
            Assert.AreEqual(1f, half.vertices[half.faces[remap3[2]].indices[0]].y, 1e-4f);
            Assert.AreEqual(5, half.faces[remap3[3]].indices.Length, "the side keeps the vertex where the top was split");
            // extrude a belt wall: the faces next to it keep their shape
            int wall = FaceAt(once, Vector3.right, top, 1f);
            var side = BrushBoolean.ExtrudeFaces(once, new[] { wall }, 1f, true, out _);
            Assert.AreEqual(14, side.faces.Length);
            foreach (var f in side.faces) Assert.AreEqual(4, f.indices.Length, "every face is still a quad, none was redrawn with a diagonal");
        }

        [Test]
        public void OverlayExtrudeTwiceThenUndoTwiceRestoresTheBrush()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            BrushSettings.instance.snapToGrid = true;
            BrushSettings.instance.extrudeDistance = 0f; BrushSettings.instance.extrudeIndividual = true;
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var model = Object.FindFirstObjectByType<BrushModel>();
            var mesh = BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>();
            int before = mesh.sharedMesh.vertexCount;
            Selection.activeGameObject = brush.gameObject;
            BrushEditState.Mode = BrushEditMode.Face;
            var poly = BrushGeometry.Polyhedron(brush);
            int top = 0; for (int f = 0; f < poly.faces.Length; f++) if (Vector3.Dot(poly.Plane(f), Vector3.up) > 0.9f) top = f;
            BrushEditState.Sel(brush).faces = new HashSet<int> { top };
            Undo.IncrementCurrentGroup();
            BrushExtrudeOverlay.Extrude(1f, true);
            Assert.IsNull(brush.problem, "after first extrude: " + brush.problem);
            Assert.AreEqual(1, BrushEditState.Sel(brush).faces.Count, "the moved face stays selected");
            Undo.IncrementCurrentGroup();
            BrushExtrudeOverlay.Extrude(1f, true);
            BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "after second extrude: " + brush.problem);
            Assert.AreEqual(14, brush.polyhedron.faces.Length);
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "after first undo: " + brush.problem);
            Assert.AreEqual(10, brush.polyhedron.faces.Length, "back to one belt");
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "after second undo: " + brush.problem);
            Assert.AreEqual(before, mesh.sharedMesh.vertexCount, "mesh back to the box");
            Undo.PerformRedo(); BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "after redo: " + brush.problem);
            Assert.AreEqual(10, brush.polyhedron.faces.Length, "redo brings the belt back");
        }

        [Test]
        public void ExtrudeThenUndoRestoresTheBrush()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            BrushSettings.instance.snapToGrid = true;
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var model = Object.FindFirstObjectByType<BrushModel>();
            int before = BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount;
            Undo.IncrementCurrentGroup();
            var remap = BrushApi.ExtrudeFaces(brush, new[] { 1 }, 1f, true);
            Assert.IsNotNull(remap, "extrude happened");
            BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "extruded brush is sound: " + brush.problem);
            Assert.Greater(BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount, before);
            Undo.PerformUndo(); BrushApi.ForceUpdate();
            Assert.IsNull(brush.problem, "restored brush is sound: " + brush.problem);
            Assert.AreEqual(before, BrushCsg.MeshObject(model, false).GetComponent<MeshFilter>().sharedMesh.vertexCount, "mesh back to the box");
        }
    }
}

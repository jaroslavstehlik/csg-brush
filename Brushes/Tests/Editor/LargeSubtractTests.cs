using System.Collections.Generic;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A subtract brush must cut every brush it overlaps, however many that is.</summary>
    public class LargeSubtractTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            BrushSettings.instance.snapToGrid = true;
        }

        static List<Vector3> RenderVertices()
        {
            var list = new List<Vector3>();
            foreach (var model in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.GetComponent<MeshCollider>() != null) continue;
                    foreach (var v in mf.sharedMesh.vertices) list.Add(mf.transform.TransformPoint(v));
                }
            return list;
        }

        static List<Vector3> ColliderVertices()
        {
            var list = new List<Vector3>();
            foreach (var model in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var container = model.transform.Find(ConvexColliderSettings.ContainerName);
                if (container == null) continue;
                foreach (var bc in container.GetComponentsInChildren<BoxCollider>(true))
                    for (int i = 0; i < 8; i++) list.Add(bc.transform.TransformPoint(bc.center + Vector3.Scale(bc.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                foreach (var mc in container.GetComponentsInChildren<MeshCollider>(true))
                    foreach (var v in mc.sharedMesh.vertices) list.Add(mc.transform.TransformPoint(v));
            }
            return list;
        }

        static List<int> Uncut(List<Brush> boxes, List<Vector3> verts)
        {
            // a box that kept vertices above y = 0.5 inside its footprint was not cut (the cutter removes y > 0)
            var uncut = new List<int>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var c = boxes[i].transform.position; bool high = false;
                foreach (var v in verts)
                    if (Mathf.Abs(v.x - c.x) < 1.1f && Mathf.Abs(v.z - c.z) < 1.1f && v.y > 0.5f) { high = true; break; }
                if (high) uncut.Add(i);
            }
            return uncut;
        }

        static List<Brush> Grid(int count, int columns, float spacing, string prefix = "box")
        {
            var boxes = new List<Brush>();
            for (int i = 0; i < count; i++)
                boxes.Add(BrushApi.Create(BrushShape.Box, new Vector3((i % columns) * spacing, 0f, (i / columns) * spacing), new Vector3(2f, 2f, 2f), Quaternion.identity, null, prefix + i));
            return boxes;
        }

        static Brush Cutter(int count, int columns, float spacing, Vector3 offset)
        {
            int rows = (count + columns - 1) / columns;
            float w = columns * spacing + 4f, d = rows * spacing + 4f;
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3((columns - 1) * spacing * 0.5f, 1f, (rows - 1) * spacing * 0.5f) + offset, new Vector3(w, 2f, d), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            return cutter;
        }

        void Check(string what, List<Brush> boxes)
        {
            var uncutRender = Uncut(boxes, RenderVertices());
            var uncutCollider = Uncut(boxes, ColliderVertices());
            Debug.Log("LARGE " + what + ": render uncut " + uncutRender.Count + " [" + string.Join(",", uncutRender) + "], collider uncut " + uncutCollider.Count + " [" + string.Join(",", uncutCollider) + "]");
            Assert.AreEqual(0, uncutCollider.Count, what + " colliders: boxes not cut: " + string.Join(",", uncutCollider));
            Assert.AreEqual(0, uncutRender.Count, what + " render mesh: boxes not cut: " + string.Join(",", uncutRender));
        }

        [Test]
        public void CutterMovedOntoTheBoxesCutsAll()
        {
            var boxes = Grid(40, 10, 3f); BrushApi.ForceUpdate();
            var cutter = Cutter(40, 10, 3f, new Vector3(0f, 50f, 0f)); BrushApi.ForceUpdate();
            Check("before the move (cutter far away)", new List<Brush>()); // nothing to check, just runs the update
            BrushApi.Move(cutter, cutter.transform.position - new Vector3(0f, 50f, 0f)); BrushApi.ForceUpdate();
            Check("cutter moved onto 40 boxes", boxes);
        }

        [Test]
        public void CutterGrownOverTheBoxesCutsAll()
        {
            var boxes = Grid(40, 10, 3f); BrushApi.ForceUpdate();
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract); BrushApi.ForceUpdate();
            Check("small cutter on box 0", new List<Brush> { boxes[0] });
            // grow it over the whole grid, keeping the min corner (the way the Scale tool bakes size)
            int rows = 4; float w = 10 * 3f + 4f, d = rows * 3f + 4f;
            BrushApi.SetSize(cutter, new Vector3(w, 2f, d));
            BrushApi.Move(cutter, new Vector3(9 * 1.5f, 1f, 3 * 1.5f)); BrushApi.ForceUpdate();
            Check("cutter grown over 40 boxes", boxes);
        }

        [Test]
        public void TouchingBoxesAreAllCut()
        {
            // walls: boxes sharing faces (spacing equals size)
            var boxes = Grid(40, 10, 2f); BrushApi.ForceUpdate();
            Cutter(40, 10, 2f, Vector3.zero); BrushApi.ForceUpdate();
            Check("40 touching boxes", boxes);
        }

        [Test]
        public void BoxesCreatedAfterTheCutterAreNotCutByDesign()
        {
            // CSG order: a subtract only affects brushes above it. Documented behaviour, logged for reference.
            var cutter = Cutter(40, 10, 3f, Vector3.zero); BrushApi.ForceUpdate();
            var boxes = Grid(40, 10, 3f); BrushApi.ForceUpdate();
            var uncut = Uncut(boxes, RenderVertices());
            Debug.Log("LARGE boxes created after the cutter: render uncut " + uncut.Count + " of " + boxes.Count);
        }

        static List<int> Remaining(List<Brush> boxes, List<Vector3> verts)
        {
            // boxes entirely inside the cutter must vanish: any vertex left in the footprint means it survived
            var left = new List<int>();
            for (int i = 0; i < boxes.Count; i++)
            {
                var c = boxes[i].transform.position; bool any = false;
                foreach (var v in verts) if (Mathf.Abs(v.x - c.x) < 0.9f && Mathf.Abs(v.z - c.z) < 0.9f && Mathf.Abs(v.y - c.y) < 1.1f) { any = true; break; }
                if (any) left.Add(i);
            }
            return left;
        }

        [Test]
        public void EnclosedBoxesAllVanish([Values(20, 60, 100, 160)] int count, [Values(2f, 3f)] float spacing)
        {
            var boxes = Grid(count, 10, spacing); BrushApi.ForceUpdate();
            int rows = (count + 9) / 10;
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(9 * spacing * 0.5f, 0f, (rows - 1) * spacing * 0.5f), new Vector3(10 * spacing + 6f, 6f, rows * spacing + 6f), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract); BrushApi.ForceUpdate();
            var render = Remaining(boxes, RenderVertices()); var collider = Remaining(boxes, ColliderVertices());
            Debug.Log("ENCLOSED " + count + " boxes spacing " + spacing + ": render survivors " + render.Count + " [" + string.Join(",", render) + "], collider survivors " + collider.Count);
            Assert.AreEqual(0, collider.Count, "colliders left for enclosed boxes " + string.Join(",", collider));
            Assert.AreEqual(0, render.Count, "render mesh left for enclosed boxes " + string.Join(",", render));
        }

        [Test]
        public void BrushesBelowACutterAreReportedAndMovingItDownCutsThem()
        {
            var above = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, null, "above");
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3(1f, 1f, 0f), new Vector3(10f, 2f, 6f), Quaternion.identity, null, "cutter"); // covers both boxes fully in x
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            var below = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "below");
            var far = BrushApi.Create(BrushShape.Box, new Vector3(20f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "far");
            BrushApi.ForceUpdate();
            var reported = BrushCsg.UncutBelow(cutter);
            Assert.AreEqual(1, reported.Count, "one overlapping brush below the cutter");
            Assert.AreSame(below, reported[0]);
            Assert.AreEqual(1, Uncut(new List<Brush> { above, below }, RenderVertices()).Count, "the one below keeps its top");
            BrushApi.ToLast(cutter);
            BrushApi.ForceUpdate();
            Assert.AreEqual(0, BrushCsg.UncutBelow(cutter).Count, "nothing left below the cutter");
            Assert.AreEqual(0, Uncut(new List<Brush> { above, below }, RenderVertices()).Count, "both cut once the cutter is last");
        }

        [Test]
        public void LargeSubtractCutsEveryBoxItOverlaps([Values(10, 40, 100, 160)] int count)
        {
            int columns = 10;
            var boxes = new List<Brush>();
            for (int i = 0; i < count; i++)
                boxes.Add(BrushApi.Create(BrushShape.Box, new Vector3((i % columns) * 3f, 0f, (i / columns) * 3f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "box" + i));
            BrushApi.ForceUpdate();
            float w = columns * 3f + 4f, d = ((count + columns - 1) / columns) * 3f + 4f;
            var cutter = BrushApi.Create(BrushShape.Box, new Vector3((columns - 1) * 1.5f, 1f, ((count + columns - 1) / columns - 1) * 1.5f), new Vector3(w, 2f, d), Quaternion.identity, null, "cutter");
            BrushApi.SetOperation(cutter, BrushOperation.Subtract);
            BrushApi.ForceUpdate();

            var uncutRender = Uncut(boxes, RenderVertices());
            var uncutCollider = Uncut(boxes, ColliderVertices());
            Debug.Log("LARGE " + count + " boxes: render uncut " + uncutRender.Count + " [" + string.Join(",", uncutRender) + "], collider uncut " + uncutCollider.Count + " [" + string.Join(",", uncutCollider) + "]");
            Assert.AreEqual(0, uncutCollider.Count, "colliders: boxes not cut: " + string.Join(",", uncutCollider));
            Assert.AreEqual(0, uncutRender.Count, "render mesh: boxes not cut: " + string.Join(",", uncutRender));
        }
    }
}

using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>World-space grid enforcement: positions, sizes and rotations stay on the grid whatever the hierarchy does.</summary>
    public class BrushSnapTests
    {
        const float kGrid = 0.5f; // 16 Quake units

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Undo.ClearAll();
            var s = BrushSettings.instance;
            s.ApplyPreset(BrushSettings.Preset.Quake);
            s.gridIndex = System.Array.IndexOf(s.gridSizes, 16f);
            s.rotationSnapDegrees = 15f;
            s.snapToGrid = true;
            Assert.AreEqual(kGrid, s.GridMeters, 1e-5f);
        }

        static Vector3 MinCorner(Brush b) => b.transform.position - BrushSnap.WorldExtents(b.size, b.transform.rotation) * 0.5f;

        static void AssertOnGrid(Vector3 v, string what)
        {
            Assert.AreEqual(BrushSnap.Round(v, kGrid).ToString("F4"), v.ToString("F4"), what + " is on the grid");
        }

        [Test]
        public void SizeIsRoundedToGridMultiples()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.3f, 0.2f, 2.1f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0.5f, 2f), b.size, "sizes are grid multiples, at least one grid step");
        }

        [Test]
        public void MoveSnapsTheCornerNotThePivot()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.5f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            b.transform.position = new Vector3(0.37f, 0.1f, -0.9f); // like a script or an unsnapped drag
            BrushApi.ForceUpdate();
            AssertOnGrid(MinCorner(b), "minimum corner");
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f), b.transform.position, "pivot sits half a size away from the snapped corner");
        }

        [Test]
        public void RotationSnapsAndScaleIsRemoved()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 1f, 1f), Quaternion.identity);
            b.transform.rotation = Quaternion.Euler(0f, 47f, 0f);
            b.transform.localScale = new Vector3(2f, 1f, 1f);
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 45.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "rotation snapped to 15 degree steps");
            Assert.AreEqual(Vector3.one, b.transform.localScale, "scale is never used on brushes");
            AssertOnGrid(b.transform.position, "pivot of a non axis-aligned brush");
        }

        [Test]
        public void BrushUnderRotatedScaledParentStaysOnTheWorldGrid()
        {
            var parent = new GameObject("Group").transform;
            parent.position = new Vector3(0.3f, 0.2f, 0.1f);
            parent.rotation = Quaternion.Euler(0f, 30f, 0f);
            parent.localScale = new Vector3(2f, 2f, 2f);
            var b = BrushApi.Create(BrushShape.Box, new Vector3(1.1f, 0.4f, -0.7f), new Vector3(2f, 1f, 1f), Quaternion.identity, parent);
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushSnap.IsAxisAligned(b.transform.rotation), "world rotation snapped to a world axis in spite of the parent");
            Assert.AreEqual(Vector3.one.ToString("F3"), b.transform.lossyScale.ToString("F3"), "world scale is one in spite of the parent");
            AssertOnGrid(MinCorner(b), "world-space corner");
            Assert.AreEqual(parent, BrushSnap.TransformedParent(b), "lint reports the parent");
            Assert.IsFalse(BrushSnap.IsOffGrid(b));

            // moving the parent later moves the brush off the grid; the poll puts it back
            parent.position += new Vector3(0.13f, 0f, 0f);
            BrushApi.ForceUpdate();
            AssertOnGrid(MinCorner(b), "corner after the parent moved");
        }

        [Test]
        public void ResetParentsPutsEverythingBack()
        {
            var parent = new GameObject("Group").transform;
            parent.rotation = Quaternion.Euler(0f, 30f, 0f);
            parent.localScale = new Vector3(2f, 2f, 2f);
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity, parent);
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, BrushSnap.TransformedParents().Count);
            BrushSnap.ResetTransformedParents();
            BrushApi.ForceUpdate();
            Assert.AreEqual(0, BrushSnap.TransformedParents().Count);
            Assert.AreEqual(Vector3.one, parent.localScale);
            Assert.IsTrue(BrushSnap.IsAxisAligned(parent.rotation));
            Assert.AreEqual(0, BrushSnap.OffGridBrushes().Count);
        }

        [Test]
        public void ScaleToolResizesTheBrush()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            b.transform.localScale = new Vector3(2f, 1f, 1.3f);
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(2f, 1f, 2.5f), b.size, "scale baked into the size and rounded to the grid");
            Assert.AreEqual(Vector3.one, b.transform.localScale, "scale back to one");
        }

        [Test]
        public void SnappingWaitsForTheDragToEnd()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            GUIUtility.hotControl = 1234; // a handle is being dragged
            try
            {
                b.transform.rotation = Quaternion.Euler(0f, 7f, 0f);
                b.transform.localScale = new Vector3(1.4f, 1f, 1f);
                BrushApi.ForceUpdate();
                Assert.AreEqual("(0.0, 7.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "not snapped while dragging");
                Assert.AreEqual(new Vector3(1.4f, 1f, 1f), b.transform.localScale, "scale untouched while dragging");
                b.transform.rotation = Quaternion.Euler(0f, 22f, 0f); // the drag accumulates freely
            }
            finally { GUIUtility.hotControl = 0; }
            BrushApi.ForceUpdate();
            Assert.AreEqual("(0.0, 15.0, 0.0)", b.transform.rotation.eulerAngles.ToString("F1"), "snapped on release");
            Assert.AreEqual(new Vector3(1.5f, 1f, 1f), b.size, "scale baked on release");
            Assert.AreEqual(Vector3.one, b.transform.localScale);
        }

        static Bounds RenderBounds()
        {
            var b = new Bounds(); bool first = true;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0 || mf.name.StartsWith("‹[debug") || !mf.TryGetComponent<MeshRenderer>(out var mr) || !mr.enabled) continue;
                foreach (var v in mf.sharedMesh.vertices) { var w = mf.transform.TransformPoint(v); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w); }
            }
            return b;
        }

        static BoxCollider SingleBox()
        {
            var boxes = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.AreEqual(1, boxes.Length, "one axis-aligned collider piece");
            return boxes[0];
        }

        [Test]
        public void GeometryPreviewsTheSnapWhileTheGizmoMovesFreely()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(1.5f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            GUIUtility.hotControl = 1234;
            try
            {
                b.transform.position = new Vector3(0.37f, 0.1f, -0.9f);
                b.transform.rotation = Quaternion.Euler(0f, 7f, 0f);
                BrushApi.ForceUpdate();
                Assert.AreEqual(new Vector3(0.37f, 0.1f, -0.9f), b.transform.position, "the transform (gizmo) is free during the drag");
                Assert.IsTrue(BrushSnap.TryGetPreview(b, out _, out _), "the geometry is built at the snapped pose");
                // colliders are deferred until release; the render mesh is what previews the snap
                var mb = RenderBounds();
                Assert.AreEqual(new Vector3(0.25f, 0f, -1f).ToString("F3"), mb.center.ToString("F3"), "geometry shown at the snapped position");
                Assert.AreEqual(new Vector3(1.5f, 1f, 2f).ToString("F3"), mb.size.ToString("F3"), "geometry shown axis-aligned (snapped rotation)");
            }
            finally { GUIUtility.hotControl = 0; }
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f), b.transform.position, "transform snapped on release");
            Assert.IsFalse(BrushSnap.TryGetPreview(b, out _, out _), "no preview after the release");
            var after = SingleBox();
            Assert.AreEqual(new Vector3(0.25f, 0f, -1f).ToString("F3"), after.transform.TransformPoint(after.center).ToString("F3"), "geometry unchanged by the release");
        }

        [Test]
        public void CustomShapesAreJudgedAndSnappedByTheirVertices()
        {
            var b = BrushApi.Create(BrushShape.Box, new Vector3(1f, 0.5f, 0f), new Vector3(2f, 1f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            Assert.IsTrue(BrushApi.ConvertToCustom(b));
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushSnap.IsOffGrid(b), "a converted box is on the grid, whatever its size box looks like");
            BrushSettings.instance.snapToGrid = false;
            BrushApi.MoveVertex(b, 6, b.transform.TransformPoint(b.polyhedron.vertices[6]) + new Vector3(0.13f, 0.07f, 0f));
            BrushApi.ForceUpdate();
            BrushSettings.instance.snapToGrid = true;
            Assert.IsTrue(BrushSnap.IsOffGrid(b), "a vertex off the grid is reported");
            Assert.AreEqual(1, BrushSnap.SnapAll(true), "Snap all fixes it");
            BrushApi.ForceUpdate();
            Assert.IsFalse(BrushSnap.IsOffGrid(b));
            foreach (var v in b.polyhedron.vertices) AssertOnGrid(b.transform.TransformPoint(v), "vertex");
        }

        [Test]
        public void SnappingIsUndoable()
        {
            var b = BrushApi.Create(BrushShape.Box, Vector3.zero, Vector3.one, Quaternion.identity);
            BrushApi.ForceUpdate();
            Undo.IncrementCurrentGroup();
            BrushApi.Move(b, new Vector3(1.37f, 0f, 0f));
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0f, 0f), b.transform.position, "move landed on the grid");
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(Vector3.zero, b.transform.position, "undo restores the previous snapped pose");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(1.5f, 0f, 0f), b.transform.position, "redo lands on the grid again");
        }
    }
}

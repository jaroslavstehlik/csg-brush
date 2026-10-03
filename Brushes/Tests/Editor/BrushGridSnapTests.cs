using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Grid snap per brush: Shape puts the outermost vertices on the grid, Pivot the pivot. Either way a rotation never moves
    /// a brush, and neither does an undo or a new pivot; moving or resizing it snaps it again.
    /// </summary>
    public class BrushGridSnapTests
    {
        bool m_Snap; int m_Grid; float g;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var s = BrushSettings.instance; m_Snap = s.snapToGrid; m_Grid = s.gridIndex;
            s.snapToGrid = true;
            for (int i = 0; i < s.gridSizes.Length; i++) if (s.ToMeters(s.gridSizes[i]) <= 0.5f) s.SetGridIndex(i); // the largest step up to 0.5 m, whatever the preset
            g = s.GridMeters;
        }

        [TearDown]
        public void TearDown() { var s = BrushSettings.instance; s.snapToGrid = m_Snap; s.SetGridIndex(m_Grid); }

        static Bounds World(Brush b)
        {
            var poly = BrushGeometry.Polyhedron(b); var m = b.transform.localToWorldMatrix;
            var bounds = new Bounds(m.MultiplyPoint3x4(poly.vertices[0]), Vector3.zero);
            foreach (var v in poly.vertices) bounds.Encapsulate(m.MultiplyPoint3x4(v));
            return bounds;
        }

        void OnGrid(Vector3 p, string message)
        {
            foreach (float v in new[] { p.x, p.y, p.z }) Assert.AreEqual(Mathf.Round(v / g) * g, v, 1e-4f, message + ": " + p.ToString("F3"));
        }

        static void Near(Vector3 expected, Vector3 actual, string message) => Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), message + ": expected " + expected.ToString("F3") + " was " + actual.ToString("F3"));

        Brush OneCellBox(GridSnap mode)
        {
            BrushSettings.instance.newGridSnap = mode;
            try
            {
                var box = BrushApi.Create(BrushShape.Box, new Vector3(0.3f * g, 0.2f * g, 0.1f * g), Vector3.one * g, Quaternion.identity); // a cell's size, its pivot in the middle
                BrushApi.ForceUpdate();
                return box;
            }
            finally { BrushSettings.instance.newGridSnap = GridSnap.Shape; }
        }

        [Test]
        public void ANewBrushLandsOnTheGridByItsShape()
        {
            var box = OneCellBox(GridSnap.Shape);
            Assert.AreEqual(GridSnap.Shape, box.gridSnap, "new brushes take the setting");
            OnGrid(World(box).min, "its faces on grid lines");
            OnGrid(box.transform.position - Vector3.one * (0.5f * g), "so its centre pivot is half a cell off");
        }

        [Test]
        public void RotatingNeverMovesABrush()
        {
            var box = OneCellBox(GridSnap.Shape);
            var at = box.transform.position;
            box.transform.rotation = Quaternion.Euler(0f, 15f, 0f);
            BrushApi.ForceUpdate();
            Near(at, box.transform.position, "turned 15 degrees in place");
            box.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            BrushApi.ForceUpdate();
            Near(at, box.transform.position, "and to 90");
            box.transform.rotation = Quaternion.Euler(0f, 37f, 0f); // between rotation steps
            BrushApi.ForceUpdate();
            Assert.That(Quaternion.Angle(BrushSnap.SnapRotation(box.transform.rotation, BrushSettings.instance.rotationSnapDegrees), box.transform.rotation), Is.LessThan(1e-3f), "the angle still snaps");
            Near(at, box.transform.position, "and the brush still stays");
        }

        [Test]
        public void MovingARotatedShapeBrushPutsItsOutermostVerticesOnTheGrid()
        {
            var box = OneCellBox(GridSnap.Shape);
            box.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            BrushApi.ForceUpdate();
            box.transform.position += new Vector3(1.3f * g, 0f, 0.4f * g);
            BrushApi.ForceUpdate();
            OnGrid(World(box).min, "its bounds on the grid");
        }

        [Test]
        public void APivotBrushSnapsItsPivotAndTurnsAboutIt()
        {
            var box = OneCellBox(GridSnap.Pivot);
            OnGrid(box.transform.position, "the pivot on the grid");
            box.transform.position += new Vector3(1.3f * g, 0f, 0.4f * g);
            BrushApi.ForceUpdate();
            OnGrid(box.transform.position, "moved: the pivot on the grid again");
            var at = box.transform.position;
            box.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BrushApi.ForceUpdate();
            Near(at, box.transform.position, "turned about its pivot");
        }

        [Test]
        public void UndoingARotationLeavesTheBrushWhereItWas()
        {
            var box = OneCellBox(GridSnap.Shape);
            var at = box.transform.position;
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(box.transform, "Rotate");
            box.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            BrushApi.ForceUpdate();
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.That(Quaternion.Angle(Quaternion.identity, box.transform.rotation), Is.LessThan(1e-3f), "the rotation is undone");
            Near(at, box.transform.position, "and the brush did not move");
            Undo.PerformRedo();
            BrushApi.ForceUpdate();
            Near(at, box.transform.position, "nor on redo");
        }

        [Test]
        public void ACustomShapeTurnedOffTheAxesKeepsItsShape()
        {
            var box = OneCellBox(GridSnap.Shape);
            BrushApi.ConvertToCustom(box);
            var vertices = (Vector3[])box.polyhedron.vertices.Clone();
            box.transform.rotation = Quaternion.Euler(0f, 15f, 0f);
            BrushApi.ForceUpdate();
            box.transform.position += new Vector3(2.3f * g, 0f, 0f);
            BrushApi.ForceUpdate();
            for (int i = 0; i < vertices.Length; i++) Near(vertices[i], box.polyhedron.vertices[i], "vertex " + i + " is not bent onto the grid");
            OnGrid(World(box).min, "its bounds on the grid");
        }

        [Test]
        public void SettingThePivotOfAPivotBrushDoesNotMoveItsShape()
        {
            var box = OneCellBox(GridSnap.Pivot);
            var before = World(box);
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0f));
            BrushApi.ForceUpdate();
            Near(before.min, World(box).min, "the shape stays");
            Near(before.max, World(box).max, "all of it");
        }
    }
}

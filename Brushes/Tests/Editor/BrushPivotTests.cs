using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A brush's pivot moves its transform, never its shape; resizing and snapping keep the pivot where it is.</summary>
    public class BrushPivotTests
    {
        [SetUp]
        public void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static Bounds World(Brush b)
        {
            var poly = BrushGeometry.Polyhedron(b); var m = b.transform.localToWorldMatrix;
            var bounds = new Bounds(m.MultiplyPoint3x4(poly.vertices[0]), Vector3.zero);
            foreach (var v in poly.vertices) bounds.Encapsulate(m.MultiplyPoint3x4(v));
            return bounds;
        }

        static void Same(Bounds expected, Bounds actual, string message)
        {
            Assert.That(Vector3.Distance(expected.min, actual.min), Is.LessThan(1e-4f), message + ": min " + actual.min.ToString("F3"));
            Assert.That(Vector3.Distance(expected.max, actual.max), Is.LessThan(1e-4f), message + ": max " + actual.max.ToString("F3"));
        }

        static Bounds RenderBounds()
        {
            var r = Object.FindAnyObjectByType<MeshRenderer>(); Assert.IsNotNull(r, "the brush renders"); return r.bounds;
        }

        [Test]
        public void SettingThePivotMovesTheTransformAndLeavesTheShape()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), Quaternion.Euler(0f, 90f, 0f));
            BrushApi.ForceUpdate();
            var before = World(box); var rendered = RenderBounds();
            Undo.IncrementCurrentGroup(); // the pivot is its own step, apart from creating the brush
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0f));
            BrushApi.ForceUpdate();
            Same(before, World(box), "the shape stays");
            Same(rendered, RenderBounds(), "and so does the mesh");
            Assert.That(Vector3.Distance(new Vector3(-1f, 0f, 0f), box.transform.position), Is.LessThan(1e-4f), "the transform sits on the back of the bottom: " + box.transform.position.ToString("F3"));
            Undo.PerformUndo();
            BrushApi.ForceUpdate();
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), box.pivot, "undo takes the pivot back");
            Assert.That(Vector3.Distance(new Vector3(0f, 1f, 0f), box.transform.position), Is.LessThan(1e-4f), "and the transform");
            Same(before, World(box), "the shape never moved");
        }

        [Test]
        public void ABottomPivotKeepsTheBottomWhenResizedAndSnapsByItsFaces()
        {
            var s = BrushSettings.instance; bool snap = s.snapToGrid; int grid = s.gridIndex;
            try
            {
                s.snapToGrid = true;
                for (int i = 0; i < s.gridSizes.Length; i++) if (s.ToMeters(s.gridSizes[i]) <= 0.5f) s.SetGridIndex(i);
                float g = s.GridMeters;
                var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 2f) * (g / 0.5f), Quaternion.identity);
                BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0.5f));
                BrushApi.ForceUpdate();
                Assert.AreEqual(-0.5f * box.size.y, box.transform.position.y, 1e-4f, "the transform went to the bottom");
                BrushApi.SetSize(box, new Vector3(box.size.x, box.size.y * 3f, box.size.z));
                BrushApi.ForceUpdate();
                Assert.AreEqual(-0.5f * box.size.y / 3f, World(box).min.y, 1e-4f, "it grows up from its bottom");
                box.transform.position += new Vector3(0.3f * g, 0.3f * g, 0.3f * g);
                BrushApi.ForceUpdate();
                var b = World(box);
                foreach (float v in new[] { b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z })
                    Assert.AreEqual(Mathf.Round(v / g) * g, v, 1e-4f, "its faces snap to the grid: " + b.min.ToString("F3") + " " + b.max.ToString("F3"));
            }
            finally { s.snapToGrid = snap; s.SetGridIndex(grid); }
        }

        [Test]
        public void AnAbsolutePivotStaysThatFarFromTheCornerWhenResized()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var before = World(box);
            BrushApi.SetPivotMode(box, PivotMode.Absolute);
            Assert.AreEqual(new Vector3(1f, 1f, 1f), box.pivot, "the same pivot, in metres");
            Same(before, World(box), "switching the mode moves nothing");
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 1f));
            Assert.That(Vector3.Distance(new Vector3(-0.5f, -1f, 0f), box.transform.position), Is.LessThan(1e-4f), box.transform.position.ToString("F3"));
            Same(before, World(box), "the shape stays");
            BrushApi.SetSize(box, new Vector3(4f, 2f, 2f));
            Assert.AreEqual(-1f, World(box).min.x, 1e-4f, "the left face stays 0.5 m from the pivot");
            BrushApi.SetPivotMode(box, PivotMode.Normalized);
            Assert.That(Vector3.Distance(new Vector3(0.125f, 0f, 0.5f), box.pivot), Is.LessThan(1e-5f), "back to a fraction: " + box.pivot.ToString("F3"));
            Assert.AreEqual(-1f, World(box).min.x, 1e-4f, "still in place");
        }

        [Test]
        public void ACustomShapesPivotMovesItsVerticesAndSurvivesAReset()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, new Vector3(3f, 1f, 0f), new Vector3(2f, 2f, 4f), Quaternion.identity);
            BrushApi.ConvertToCustom(box);
            var before = World(box);
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0.5f), BrushApi.PivotOf(box), "a fresh custom shape is centred");
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0f));
            Same(before, World(box), "the shape stays");
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), BrushApi.PivotOf(box), "the vertices moved instead");
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, -2f), box.transform.position), Is.LessThan(1e-4f), box.transform.position.ToString("F3"));
            BrushApi.ResetShape(box);
            Assert.AreEqual(BrushShape.Box, box.shape);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0f), box.pivot, "the reset box keeps that pivot");
            Same(before, World(box), "and its place");
        }

        [Test]
        public void ChildrenStayWhereTheyAre()
        {
            BrushSettings.instance.snapToGrid = false;
            var box = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var child = new GameObject("Lamp").transform; child.SetParent(box.transform, false); child.localPosition = new Vector3(0f, 1f, 0f);
            BrushApi.SetPivot(box, new Vector3(0.5f, 0f, 0.5f));
            Assert.That(Vector3.Distance(new Vector3(0f, 1f, 0f), child.position), Is.LessThan(1e-4f), child.position.ToString("F3"));
        }

        [Test]
        public void DoorsWindowsAndRadialStairsKeepTheirOwnPivot()
        {
            BrushSettings.instance.snapToGrid = false;
            foreach (var shape in new[] { BrushShape.Door, BrushShape.Window, BrushShape.CurvedStairs, BrushShape.SpiralStairs })
            {
                var b = BrushApi.Create(shape, Vector3.zero, new Vector3(1f, 2f, 0.3f), Quaternion.identity);
                b.pivot = Vector3.zero;
                Assert.IsNull(BrushApi.PivotOf(b), shape + " has no pivot to set");
                Assert.AreEqual(Vector3.zero, b.PivotShift, shape + " ignores the field");
            }
        }
    }
}

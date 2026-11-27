using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;

namespace CsgBrush.Tests
{
    /// <summary>
    /// What the Brushes overlay sets for new brushes holds while one shape is drawn, and goes back to the project's
    /// defaults when another shape is picked or the Create tools are left: a Hollow picked once doesn't follow every brush.
    /// </summary>
    public class NewBrushOptionsTests
    {
        [SetUp]
        public void Open() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); BrushSettings.instance.ResetWorking(); }

        [TearDown]
        public void Close() { Tools.current = Tool.Move; BrushSettings.instance.ResetWorking(); }

        [Test]
        public void OverlayChoicesHoldForOneShapeThenGoBackToTheProjectDefaults()
        {
            var s = BrushSettings.instance;
            bool defaultHollow = s.newBrush.newHollow; var defaultOperation = s.newBrush.newOperation;
            ToolManager.SetActiveTool<CreateBoxBrushTool>();
            Assert.AreEqual(defaultHollow, s.Working.newHollow, "a shape starts from the project's defaults");
            s.Working.newHollow = !defaultHollow; s.Working.newOperation = BrushOperation.Subtract; // as the Brushes overlay does
            Assert.AreEqual(defaultHollow, s.newBrush.newHollow, "the project's defaults are left as they are");
            ToolManager.SetActiveTool<CreateBoxBrushTool>();
            Assert.AreEqual(!defaultHollow, s.Working.newHollow, "held while drawing the same shape");

            ToolManager.SetActiveTool<CreateCylinderBrushTool>();
            Assert.AreEqual(defaultHollow, s.Working.newHollow, "another shape: back to the defaults");
            Assert.AreEqual(defaultOperation, s.Working.newOperation);

            s.Working.newHollow = !defaultHollow;
            Tools.current = Tool.Move;
            Assert.AreEqual(defaultHollow, s.Working.newHollow, "the Create tools left: back to the defaults");
        }

        [Test]
        public void GridChangesInTheOverlayLastTheSessionAndLeaveTheProjectDefaults()
        {
            var s = BrushSettings.instance;
            int grid = s.gridIndex; bool snap = s.snapToGrid;
            int defaultGrid = s.defaultGridIndex; bool defaultSnap = s.defaultSnapToGrid;
            try
            {
                int other = grid > 0 ? grid - 1 : grid + 1;
                s.SetGridIndex(other); // the overlay's grid size, saved as the overlay does
                s.snapToGrid = !snap; s.NotifyChanged();
                Assert.AreEqual(other, s.gridIndex, "in use now");
                Assert.AreEqual(!snap, s.snapToGrid);
                Assert.AreEqual(defaultGrid, s.defaultGridIndex, "the project's default grid is left as it is");
                Assert.AreEqual(defaultSnap, s.defaultSnapToGrid);
                var saved = System.IO.File.ReadAllText("ProjectSettings/BrushSettings.asset");
                StringAssert.Contains("defaultGridIndex: " + defaultGrid, saved, "and so is the settings file");
                StringAssert.Contains("defaultSnapToGrid: " + (defaultSnap ? 1 : 0), saved);
            }
            finally { s.SetGridIndex(grid); s.snapToGrid = snap; }
        }
    }
}

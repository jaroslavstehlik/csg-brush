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
    }
}

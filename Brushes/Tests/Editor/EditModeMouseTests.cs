using CsgBrush.Editor;
using NUnit.Framework;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// A press taken by an edit mode is let go when the button comes up even if the edit mode's own handler never saw it
    /// (the View tool took over mid-press): a hold left behind made Unity start fly-throughs without their Scene view.
    /// </summary>
    public class EditModeMouseTests
    {
        [TearDown]
        public void Release() => GUIUtility.hotControl = 0;

        [Test]
        public void AHoldNoHandlerEndedIsLetGoWhenTheButtonComesUp()
        {
            EditModeMouse.Take(4242);
            Assert.IsFalse(EditModeMouse.ReleaseStale(new Event { type = EventType.MouseDrag }), "still dragging: kept");
            Assert.AreEqual(4242, GUIUtility.hotControl);
            Assert.IsTrue(EditModeMouse.ReleaseStale(new Event { type = EventType.MouseUp }), "the button came up, nobody let go: let go");
            Assert.AreEqual(0, GUIUtility.hotControl);
        }

        [Test]
        public void AHoldIsLetGoWhenTheMouseMovesWithNoButtonDown()
        {
            EditModeMouse.Take(4243);
            Assert.IsTrue(EditModeMouse.ReleaseStale(new Event { type = EventType.MouseMove }), "the release happened elsewhere");
            Assert.AreEqual(0, GUIUtility.hotControl);
        }

        [Test]
        public void AnotherControlsHoldIsLeftAlone()
        {
            EditModeMouse.Take(4244);
            GUIUtility.hotControl = 0; // its handler let go as usual
            GUIUtility.hotControl = 777; // then a control of Unity's took the mouse
            Assert.IsFalse(EditModeMouse.ReleaseStale(new Event { type = EventType.MouseUp }), "not ours");
            Assert.AreEqual(777, GUIUtility.hotControl);
        }
    }
}

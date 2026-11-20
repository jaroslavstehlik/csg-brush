using System;
using System.Collections;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Clicks in the Scene view, sent as real mouse events: a click on the level selects the brush under the pointer only
    /// where Unity's own selection would take the click, so every tool keeps the clicks it claims, over brushes too.
    /// </summary>
    public class SceneClickTests
    {
        SceneView m_View; Vector2 m_Offset, m_Seen, m_TargetGui; Func<Vector3> m_Target;
        Brush m_Floor, m_Tower;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            m_Floor = BrushApi.Create(BrushShape.Box, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), Quaternion.identity, null, "Floor");
            m_Tower = BrushApi.Create(BrushShape.Box, new Vector3(4f, 3f, 4f), new Vector3(4f, 6f, 4f), Quaternion.identity, null, "Tower");
            BrushApi.ForceUpdate();
            Selection.activeGameObject = null;
            m_View = EditorWindow.GetWindow<SceneView>();
            m_View.position = new Rect(0f, 0f, 1000f, 700f);
            m_View.LookAt(new Vector3(-1f, 1.5f, 0f), Quaternion.Euler(25f, 30f, 0f), 14f, false, true);
            SceneView.duringSceneGui += Observe;
            m_View.SendEvent(new Event { type = EventType.Repaint });
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = new Vector2(300f, 300f) });
            m_Offset = m_Seen - new Vector2(300f, 300f);
        }

        [TearDown]
        public void Close()
        {
            SceneView.duringSceneGui -= Observe;
            GUIUtility.hotControl = 0;
            Tools.current = Tool.Move;
            Selection.activeGameObject = null;
        }

        void Observe(SceneView view)
        {
            var e = Event.current;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown) m_Seen = e.mousePosition;
            if (m_Target != null && (e.type == EventType.Layout || e.type == EventType.Repaint)) m_TargetGui = HandleUtility.WorldToGUIPoint(m_Target());
        }

        Vector2 Gui(Vector3 world) { m_Target = () => world; m_View.SendEvent(new Event { type = EventType.Repaint }); return m_TargetGui - m_Offset; }

        void Send(EventType type, Vector2 at)
        {
            m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = at });
            m_View.SendEvent(new Event { type = type, button = 0, mousePosition = at, clickCount = 1 });
        }

        void MoveTo(Vector2 from, Vector2 to, EventType type = EventType.MouseMove)
        {
            for (int k = 1; k <= 5; k++)
            {
                var m = Vector2.Lerp(from, to, k / 5f);
                m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = m });
                m_View.SendEvent(new Event { type = type, button = 0, mousePosition = m, delta = (to - from) / 5f });
            }
        }

        void Click(Vector2 at) { Send(EventType.MouseDown, at); Send(EventType.MouseUp, at); MoveTo(at, at); }

        int Brushes => Object.FindObjectsByType<Brush>(FindObjectsInactive.Exclude).Length;

        /// <summary>A box drawn from the floor: its base dragged at (-6, -2) to (-4, 0), its last click at <paramref name="last"/>.</summary>
        void DrawBox(Vector3 last)
        {
            ToolManager.SetActiveTool<CreateBoxBrushTool>();
            m_View.SendEvent(new Event { type = EventType.Repaint });
            Vector2 a = Gui(new Vector3(-6f, 0f, -2f)), b = Gui(new Vector3(-4f, 0f, 0f));
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = a });
            Send(EventType.MouseDown, a); MoveTo(a, b, EventType.MouseDrag); Send(EventType.MouseUp, b);
            var c = Gui(last);
            MoveTo(b, c);
            Click(c);
        }

        [Test]
        public void ABoxFinishesWhereverItsLastClickLands([Values("sky", "tower", "floor")] string background)
        {
            var last = background == "sky" ? new Vector3(-5f, 9f, -1f) : background == "tower" ? new Vector3(4f, 4f, 2f) : new Vector3(-8f, 0f, -6f);
            int before = Brushes;
            DrawBox(last);
            Assert.AreEqual(before + 1, Brushes, "the box is made");
            Assert.AreNotEqual(m_Tower.gameObject, Selection.activeGameObject, "the brush behind the last click is not selected");
            Assert.AreNotEqual(m_Floor.gameObject, Selection.activeGameObject);
        }

        [Test]
        public void AClickOnTheLevelSelectsTheBrushUnderThePointer()
        {
            Tools.current = Tool.Move;
            Click(Gui(new Vector3(3f, 4f, 2f)));
            Assert.AreEqual(m_Tower.gameObject, Selection.activeGameObject, "the tower");
            Click(Gui(new Vector3(-8f, 0f, -6f)));
            Assert.AreEqual(m_Floor.gameObject, Selection.activeGameObject, "the floor");
        }

        [Test]
        public void TheMoveGizmoKeepsItsDragOverAnotherBrush()
        {
            Tools.current = Tool.Move;
            Selection.activeGameObject = m_Tower.gameObject;
            var at = m_Tower.transform.position;
            float size = HandleUtility.GetHandleSize(at);
            m_View.SendEvent(new Event { type = EventType.Repaint });
            var arrow = Gui(at + Vector3.right * size * 0.85f); var to = Gui(at + Vector3.right * size * 0.85f + Vector3.left * 6f); // dragged back over the floor
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = arrow });
            Send(EventType.MouseDown, arrow); MoveTo(arrow, to, EventType.MouseDrag); Send(EventType.MouseUp, to); MoveTo(to, to);
            Assert.AreEqual(m_Tower.gameObject, Selection.activeGameObject, "still the tower");
            Assert.Less(m_Tower.transform.position.x, at.x - 1f, "moved along the arrow");
        }

        [Test]
        public void DrawingAFloorPlanOrASplineOnABrushStartsItThere([Values(false, true)] bool spline)
        {
            if (spline) ToolManager.SetActiveTool<CreateSplineTool>(); else ToolManager.SetActiveTool<CreateFloorPlanTool>();
            m_View.SendEvent(new Event { type = EventType.Repaint });
            var at = Gui(new Vector3(-6f, 0f, -2f)); // on the floor brush
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = at });
            Click(at);
            var selected = Selection.activeGameObject;
            Assert.IsNotNull(selected, "something is selected");
            Assert.IsTrue(spline ? selected.GetComponent<Spline>() != null : selected.GetComponent<FloorPlan>() != null, "the new " + (spline ? "spline" : "floor plan") + ", not the floor under it (" + selected.name + ")");
        }

        [UnityTest]
        public IEnumerator AHoldIsLetGoOnlyAfterEveryToolHadTheRelease()
        {
            EditModeMouse.Take(4251);
            EditModeMouse.ReleaseStaleAfter(new Event { type = EventType.MouseUp });
            Assert.AreEqual(4251, GUIUtility.hotControl, "still held while the release goes round the handlers");
            yield return null; yield return null;
            Assert.AreEqual(0, GUIUtility.hotControl, "let go once nobody did");
            EditModeMouse.Take(4252);
            EditModeMouse.ReleaseStaleAfter(new Event { type = EventType.MouseUp });
            EditModeMouse.Take(4253); // a new press before the editor came round: it keeps its hold
            yield return null; yield return null;
            Assert.AreEqual(4253, GUIUtility.hotControl);
            GUIUtility.hotControl = 0;
        }
    }
}

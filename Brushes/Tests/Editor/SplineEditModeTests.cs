using System;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Spline edit mode driven by Scene view mouse events: after a click on a link's + adds a point, the Move tool's
    /// arrows still move the selected points (the edit mode once asked for fewer control ids mid-drag, so the arrow
    /// that took the press never saw the drag).
    /// </summary>
    public class SplineEditModeTests
    {
        SceneView m_View; SplineMoveTool m_Tool; Spline m_Spline;
        Func<Vector3> m_Target; Vector2 m_TargetGui, m_SeenMouse, m_Offset;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Spline = new GameObject("Spline").AddComponent<Spline>();
            m_Spline.profile = Spline.Profile.Rectangle; m_Spline.size = new Vector2(2f, 0.6f); m_Spline.thickness = 0.15f;
            int a = m_Spline.AddPoint(new Vector3(10f, 0f, 10f)), b = m_Spline.AddPoint(new Vector3(16f, 0f, 10f)), c = m_Spline.AddPoint(new Vector3(16f, 0f, 16f));
            m_Spline.AddLink(a, b); m_Spline.AddLink(b, c);
            m_Spline.transform.position = new Vector3(-5f, 0f, -5f); // points away from the pivot, whose own Move handle would catch the presses
            BrushGenerators.Update(m_Spline, true); BrushApi.ForceUpdate();
            Selection.activeGameObject = m_Spline.gameObject;
            FloorPlanEditState.Mode = BrushEditMode.Vertex;
            BrushEditState.Orientation = BrushHandleOrientation.World;
            m_Tool = ScriptableObject.CreateInstance<SplineMoveTool>();
            m_View = EditorWindow.GetWindow<SceneView>();
            m_View.position = new Rect(0f, 0f, 900f, 700f);
            SceneView.duringSceneGui += OnScene;
            // where a mouse position sent to the window lands in the Scene view's GUI
            m_View.SendEvent(new Event { type = EventType.Repaint });
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = new Vector2(300f, 300f) });
            m_Offset = m_SeenMouse - new Vector2(300f, 300f);
        }

        [TearDown]
        public void Close()
        {
            SceneView.duringSceneGui -= OnScene;
            FloorPlanEditState.EndDrag();
            GUIUtility.hotControl = 0;
            if (m_Tool != null) UnityEngine.Object.DestroyImmediate(m_Tool);
        }

        void OnScene(SceneView view)
        {
            var e = Event.current;
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown) m_SeenMouse = e.mousePosition;
            if (m_Target != null && (e.type == EventType.Layout || e.type == EventType.Repaint)) m_TargetGui = HandleUtility.WorldToGUIPoint(m_Target());
            FloorPlanEditState.OnSceneGUI();
            m_Tool.OnToolGUI(view);
        }

        Vector3 Point(int i) => m_Spline.transform.TransformPoint(m_Spline.points[i]);

        Vector2 Gui(Func<Vector3> world)
        {
            m_Target = world; m_View.SendEvent(new Event { type = EventType.Repaint });
            return m_TargetGui - m_Offset;
        }

        /// <summary>Press at <paramref name="from"/>, drag by <paramref name="by"/> in the world, and let go.</summary>
        void Drag(Func<Vector3> from, Vector3 by)
        {
            m_View.LookAt(from(), Quaternion.Euler(50f, -20f, 0f), 12f, false, true);
            m_View.SendEvent(new Event { type = EventType.Repaint });
            var grab = Gui(from);
            var start = from();
            var to = Gui(() => start + by);
            m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = grab });
            m_View.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = grab, clickCount = 1 });
            for (int k = 1; k <= 5; k++)
            {
                var m = Vector2.Lerp(grab, to, k / 5f);
                m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = m });
                if (to != grab) m_View.SendEvent(new Event { type = EventType.MouseDrag, button = 0, mousePosition = m, delta = (to - grab) / 5f });
            }
            m_View.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = to });
            m_View.SendEvent(new Event { type = EventType.Layout, mousePosition = to });
            m_View.SendEvent(new Event { type = EventType.MouseMove, mousePosition = to });
        }

        void MoveArrow(int point, Vector3 axis)
        {
            var sel = FloorPlanEditState.Sel(m_Spline);
            sel.Clear(); sel.vertices.Add(point);
            var before = m_Spline.points[point];
            Drag(() => Point(point) + axis * HandleUtility.GetHandleSize(Point(point)) * 0.85f, axis);
            Assert.AreEqual(1f, Vector3.Distance(before, m_Spline.points[point]), 0.05f, "the Move arrow moves point " + point + " a metre");
        }

        [Test]
        public void TheMoveArrowsStillMovePointsAfterAPlusAddsOne()
        {
            MoveArrow(0, Vector3.right);
            Vector3 Middle() { m_Spline.CurvePoint(0, m_Spline.CurveLength(0) * 0.5f, out var m, out _); return m_Spline.transform.TransformPoint(m); }
            FloorPlanEditState.Sel(m_Spline).Clear(); // no Move tool over the + (point 0's x arrow lies along the link)
            Drag(Middle, Vector3.zero); // click the + of the first link
            Assert.AreEqual(4, m_Spline.points.Count, "the + added a point");
            MoveArrow(0, Vector3.right);
            MoveArrow(1, Vector3.forward);
            MoveArrow(3, Vector3.forward);
        }
    }
}

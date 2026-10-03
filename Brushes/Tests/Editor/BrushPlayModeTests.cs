using System.Collections;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace CsgBrush.Tests
{
    /// <summary>Entering and leaving play mode (with its domain reload) must not stop the CSG updates.</summary>
    public class BrushPlayModeTests
    {
        static Bounds ColliderBounds()
        {
            Physics.SyncTransforms();
            var colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include);
            Assert.Greater(colliders.Length, 0, "colliders exist");
            var b = colliders[0].bounds; foreach (var c in colliders) b.Encapsulate(c.bounds); return b;
        }

        static int RenderVertices()
        {
            int n = 0;
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                if (mf.sharedMesh != null && !mf.name.StartsWith("‹[debug") && mf.TryGetComponent<MeshRenderer>(out var mr) && mr.enabled) n += mf.sharedMesh.vertexCount;
            return n;
        }

        [UnityTest]
        public IEnumerator BrushesKeepUpdatingAfterPlayMode()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BrushSettings.instance.snapToGrid = false;
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 1f, 2f), Quaternion.identity);
            a.name = "PlayModeBox";
            BrushApi.ForceUpdate();
            Assert.AreEqual(24, RenderVertices(), "box renders before play mode");
            string path = "Assets/__brush_playmode_test.unity";
            EditorSceneManager.SaveScene(scene, path);
            try
            {
                yield return new EnterPlayMode();
                yield return null;
                yield return new ExitPlayMode();
                for (int i = 0; i < 4; i++) yield return null; // the editor's deferred rebuild after play mode runs on the next ticks
                BrushApi.ForceUpdate();
                var box = GameObject.Find("PlayModeBox").GetComponent<Brush>();
                Assert.IsNotNull(box);
                Assert.AreEqual(24, RenderVertices(), "box still renders after play mode");
                BrushApi.Move(box, new Vector3(3f, 0f, 0f));
                BrushApi.ForceUpdate();
                Assert.AreEqual(3f, ColliderBounds().center.x, 1e-3f, "collider follows a move after play mode");
                BrushApi.SetSize(box, new Vector3(4f, 1f, 2f));
                BrushApi.ForceUpdate();
                Assert.AreEqual(4f, ColliderBounds().size.x, 1e-3f, "geometry follows a resize after play mode");
                var b = BrushApi.Create(BrushShape.Box, new Vector3(0f, 0f, 5f), Vector3.one, Quaternion.identity);
                BrushApi.ForceUpdate();
                Assert.AreEqual(2, Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length, "new brush has a collider");
                Assert.AreEqual(48, RenderVertices(), "a new brush after play mode renders");
                for (int i = 0; i < 4; i++) yield return null; // flush deferred calls before the next test starts
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>A plain trigger script, as a student writes it, on the brush object.</summary>
        public sealed class TriggerCounter : MonoBehaviour
        {
            public int enters, exits, stays;
            void OnTriggerEnter(Collider other) => enters++;
            void OnTriggerExit(Collider other) => exits++;
            void OnTriggerStay(Collider other) => stays++;
        }

        [UnityTest]
        public IEnumerator ATriggerBrushSendsUnitysTriggerMessagesToItsOwnObject()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var brush = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            brush.name = "TriggerBox";
            BrushApi.SetCollision(brush, Colliders.ColliderKind.Trigger);
            BrushApi.ForceUpdate();
            string path = "Assets/__brush_trigger_test.unity";
            EditorSceneManager.SaveScene(scene, path);
            try
            {
                yield return new EnterPlayMode();
                GameObject box = null; // the brush, not its collider piece of the same name
                for (int i = 0; i < 20 && box == null; i++) { yield return null; foreach (var b in Object.FindObjectsByType<Brush>()) if (b.name == "TriggerBox") box = b.gameObject; }
                Assert.IsNotNull(box, "the scene is loaded in play mode");
                var counter = box.AddComponent<TriggerCounter>(); // a test script cannot be saved in the scene
                int events = 0; box.GetComponent<Brush>().TriggerEntered += _ => events++;
                var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ball.transform.position = new Vector3(0f, 1f, -5f);
                var body = ball.AddComponent<Rigidbody>(); body.isKinematic = true;
                var mode = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script; // time does not run in a batch test: step by hand
                void Step() { for (int i = 0; i < 4; i++) { Physics.SyncTransforms(); Physics.Simulate(0.02f); } }
                Step();
                ball.transform.position = new Vector3(0f, 1f, 0f);
                Step();
                int afterEnter = events, enters = counter.enters, stays = counter.stays;
                ball.transform.position = new Vector3(0f, 1f, 5f);
                Step();
                int exits = counter.exits;
                Physics.simulationMode = mode;
                yield return new ExitPlayMode();
                for (int i = 0; i < 10; i++) yield return null; // let the editor settle before the next play mode test
                Assert.AreEqual(1, afterEnter, "the brush's own event");
                Assert.AreEqual(1, enters, "OnTriggerEnter on the brush object");
                Assert.Greater(stays, 0, "OnTriggerStay too");
                Assert.AreEqual(1, exits, "OnTriggerExit");
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}

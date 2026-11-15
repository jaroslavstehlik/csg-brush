using System.Collections.Generic;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// A generator in a prefab instance keeps the brushes it rebuilt when the scene is saved and opened again (exiting play
    /// mode does that too): its changes to brushes that came from the prefab are recorded as overrides, not lost to the
    /// prefab's values.
    /// </summary>
    public class GeneratorPrefabTests
    {
        const string PrefabPath = "Assets/__generator_prefab_test.prefab", ScenePath = "Assets/__generator_prefab_test.unity";

        [TearDown]
        public void Clean()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(PrefabPath);
        }

        static int Differing(Spline s)
        {
            var specs = new List<BrushGenerator.BrushSpec>(); s.Describe(specs);
            Assert.AreEqual(specs.Count, s.generated.Count, "one brush per piece");
            int differ = 0;
            for (int i = 0; i < specs.Count; i++)
                if (s.generated[i] == null || s.generated[i].polyhedron.ContentHash() != specs[i].polyhedron.ContentHash()) differ++;
            return differ;
        }

        [Test]
        public void ASplinePrefabInstanceKeepsItsRebuiltPiecesWhenTheSceneIsOpenedAgain()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var made = new GameObject("Spline").AddComponent<Spline>();
            made.profile = Spline.Profile.Rectangle; made.size = new Vector2(1f, 0.5f); made.thickness = 0.1f;
            int a = made.AddPoint(new Vector3(0f, 0f, 0f)), b = made.AddPoint(new Vector3(4f, 0f, 0f)), c = made.AddPoint(new Vector3(4f, 0f, 4f));
            made.AddLink(a, b); made.AddLink(b, c);
            BrushGenerators.Update(made, true);
            PrefabUtility.SaveAsPrefabAsset(made.gameObject, PrefabPath);
            Object.DestroyImmediate(made.gameObject);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            var s = go.GetComponent<Spline>();
            Undo.RecordObject(s, "Profile"); // as the Inspector changes it
            s.profile = Spline.Profile.Circle; s.size = new Vector2(1f, 1f); s.hollow = true;
            BrushGenerators.Update(s);
            Assert.AreEqual(0, Differing(s), "rebuilt");
            // what the copy of the scene taken for play mode keeps: the overrides (a scene save would find the changes anyway)
            foreach (var piece in s.generated)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(piece) == null) continue; // added by the instance: saved whole
                bool recorded = false;
                foreach (var m in PrefabUtility.GetPropertyModifications(go))
                    if (m.target == PrefabUtility.GetCorrespondingObjectFromSource(piece) && m.propertyPath.StartsWith("polyhedron")) { recorded = true; break; }
                Assert.IsTrue(recorded, piece.name + " came from the prefab: its new shape is an override");
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var opened = Object.FindAnyObjectByType<Spline>();
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(opened), "still a prefab instance");
            Assert.AreEqual(0, Differing(opened), "the pieces are the rebuilt ones, not the prefab's");
        }
    }
}

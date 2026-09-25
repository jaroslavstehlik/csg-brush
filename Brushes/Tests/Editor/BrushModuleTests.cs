using System.Collections.Generic;
using CsgBrush.Colliders;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>A game's module, as a test would write one: a value that rides onto the pieces as a marker component.</summary>
    public sealed class TestSurfaceModule : BrushModule
    {
        public int value;
        public bool trigger;
        public override int Fingerprint() => value * 2 + (trigger ? 1 : 0);
        public override bool OverrideCollision(out ColliderKind collision) { collision = ColliderKind.Trigger; return trigger; }
        public override void ApplyToPiece(GameObject piece, bool isTrigger)
        {
            if (!piece.TryGetComponent<TestPieceMarker>(out var marker)) marker = piece.AddComponent<TestPieceMarker>();
            marker.value = value; marker.applied++;
        }
    }

    public sealed class TestPieceMarker : MonoBehaviour { public int value; public int applied; }

    public class BrushModuleTests
    {
        [SetUp]
        public void NewScene() { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); Undo.ClearAll(); }

        static List<ConvexPiece> PiecesOf(Brush brush)
        {
            var list = new List<ConvexPiece>();
            foreach (var p in Object.FindObjectsByType<ConvexPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (p.brushName == brush.name) list.Add(p);
            return list;
        }

        [Test]
        public void AModuleRidesOntoThePiecesAndAChangeRebuildsThem()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var module = BrushApi.AddModule<TestSurfaceModule>(brush); module.value = 7;
            BrushApi.ForceUpdate();
            var pieces = PiecesOf(brush);
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(7, pieces[0].GetComponent<TestPieceMarker>().value, "the module's data is on the piece");
            Assert.AreEqual(BrushCsg.PieceFingerprint(brush), pieces[0].fingerprint);
            module.value = 8;
            BrushApi.ForceUpdate();
            pieces = PiecesOf(brush);
            Assert.AreEqual(8, pieces[0].GetComponent<TestPieceMarker>().value, "a changed value reaches the piece");
            Assert.AreEqual(1, Colliders.Editor.ConvexColliderBuilder.LastCreatedPieces, "the piece was rebuilt, its identity changed");
        }

        [Test]
        public void AParentsModuleTagsTheBrushesBelowIt()
        {
            var group = new GameObject("Icy group");
            group.AddComponent<TestSurfaceModule>().value = 3;
            var a = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, group.transform);
            var b = BrushApi.Create(BrushShape.Box, new Vector3(4f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity, group.transform);
            BrushApi.ForceUpdate();
            Assert.AreEqual(3, PiecesOf(a)[0].GetComponent<TestPieceMarker>().value);
            Assert.AreEqual(3, PiecesOf(b)[0].GetComponent<TestPieceMarker>().value);
        }

        [Test]
        public void AModuleCanMakeTheBrushATriggerAndItsPiecesRelayToTheBrush()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.AddModule<TestSurfaceModule>(brush).trigger = true;
            Assert.AreEqual(ColliderKind.Trigger, brush.EffectiveCollision()); Assert.AreEqual(ColliderKind.Solid, brush.collision, "the field itself is untouched");
            BrushApi.ForceUpdate();
            var pieces = PiecesOf(brush);
            Assert.AreEqual(1, pieces.Count);
            Assert.IsTrue(pieces[0].trigger); Assert.IsTrue(pieces[0].GetComponent<Collider>().isTrigger);
            var relay = pieces[0].GetComponent<BrushTriggerRelay>();
            Assert.IsNotNull(relay, "trigger pieces relay to their brush"); Assert.AreEqual(brush, relay.brush);
        }

        [Test]
        public void TriggerEventsAreRaisedOncePerBrushAcrossItsPieces()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var trigger = BrushApi.AddModule<BrushTrigger>(brush);
            int enters = 0, exits = 0, eventEnters = 0;
            trigger.onEnter.AddListener(_ => enters++); trigger.onExit.AddListener(_ => exits++);
            brush.TriggerEntered += _ => eventEnters++;
            var other = new GameObject("player").AddComponent<BoxCollider>();
            brush.PieceTriggerEnter(other); brush.PieceTriggerEnter(other); // two pieces of the same brush
            Assert.AreEqual(1, enters); Assert.AreEqual(1, eventEnters);
            brush.PieceTriggerExit(other);
            Assert.AreEqual(0, exits, "still inside the other piece");
            brush.PieceTriggerExit(other);
            Assert.AreEqual(1, exits);
            Assert.AreEqual(ColliderKind.Trigger, brush.EffectiveCollision(), "the trigger module makes the brush a trigger");
        }

        [Test]
        public void LegacySurfaceDataMigrates()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var so = new SerializedObject(brush); so.FindProperty("legacySurface").intValue = 3; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(brush.HasLegacySurface);
            BrushSync.Ensure(brush);
            Assert.AreEqual(ColliderKind.Trigger, brush.collision, "the old Trigger kind is the Collision field now");
            Assert.IsFalse(brush.HasLegacySurface);
            // a game's kind waits for the game's migration
            so.Update(); so.FindProperty("legacySurface").intValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
            var saved = Brush.LegacySurfaceMigration;
            try
            {
                Brush.LegacySurfaceMigration = null;
                BrushSync.Ensure(brush);
                Assert.IsTrue(brush.HasLegacySurface, "kept until a module package can take it");
                Brush.LegacySurfaceMigration = (b, surface, nfd) => { if (surface != 1) return false; b.gameObject.AddComponent<TestSurfaceModule>().value = 11; return true; };
                BrushSync.Ensure(brush);
                Assert.IsFalse(brush.HasLegacySurface);
                Assert.AreEqual(11, brush.GetComponent<TestSurfaceModule>().value);
            }
            finally { Brush.LegacySurfaceMigration = saved; }
        }

        [Test]
        public void NewBrushesGetTheProjectsModules()
        {
            var s = BrushSettings.instance; var saved = new List<string>(s.newModules);
            try
            {
                s.newModules.Clear(); s.newModules.Add(typeof(TestSurfaceModule).FullName);
                var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
                Assert.IsNotNull(brush.GetComponent<TestSurfaceModule>());
            }
            finally { s.newModules.Clear(); s.newModules.AddRange(saved); }
        }

        [Test]
        public void PiecesTakeTheirBrushesColliderProperties()
        {
            var brush = BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity);
            var mat = new PhysicsMaterial("bouncy") { bounciness = 0.9f };
            brush.physicsMaterial = mat; brush.provideContacts = true;
            brush.gameObject.tag = "Finish";
            GameObjectUtility.SetStaticEditorFlags(brush.gameObject, StaticEditorFlags.OccluderStatic | StaticEditorFlags.NavigationStatic);
            BrushApi.ForceUpdate();
            var piece = PiecesOf(brush)[0];
            var collider = piece.GetComponent<Collider>();
            Assert.AreEqual(mat, collider.sharedMaterial); Assert.IsTrue(collider.providesContacts);
            Assert.AreEqual("Finish", piece.tag);
            Assert.AreEqual(StaticEditorFlags.OccluderStatic | StaticEditorFlags.NavigationStatic, GameObjectUtility.GetStaticEditorFlags(piece.gameObject));
            brush.physicsMaterial = null;
            BrushApi.ForceUpdate();
            Assert.AreEqual(1, Colliders.Editor.ConvexColliderBuilder.LastCreatedPieces, "a changed material is a new piece identity");
            Assert.IsNull(PiecesOf(brush)[0].GetComponent<Collider>().sharedMaterial);
            // trigger brushes carry the material too
            brush.physicsMaterial = mat; BrushApi.SetCollision(brush, ColliderKind.Trigger);
            BrushApi.ForceUpdate();
            Assert.AreEqual(mat, PiecesOf(brush)[0].GetComponent<Collider>().sharedMaterial);
        }

        [Test]
        public void RenderMeshesTakeTheirModelsTagAndStaticFlags()
        {
            var modelGo = new GameObject("Level"); var model = modelGo.AddComponent<BrushModel>();
            modelGo.tag = "Respawn"; GameObjectUtility.SetStaticEditorFlags(modelGo, StaticEditorFlags.ContributeGI);
            BrushApi.Create(BrushShape.Box, Vector3.zero, new Vector3(2f, 2f, 2f), Quaternion.identity, modelGo.transform);
            BrushApi.ForceUpdate();
            var mesh = BrushCsg.MeshObject(model, false);
            Assert.IsNotNull(mesh);
            Assert.AreEqual("Respawn", mesh.tag);
            Assert.AreEqual(StaticEditorFlags.ContributeGI, GameObjectUtility.GetStaticEditorFlags(mesh.gameObject));
            // the hidden default model gets the project's default flags
            BrushApi.Create(BrushShape.Box, new Vector3(6f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            BrushApi.ForceUpdate();
            var def = BrushCsg.DefaultModel(false);
            Assert.AreEqual(BrushSettings.instance.defaultModelStaticFlags, GameObjectUtility.GetStaticEditorFlags(BrushCsg.MeshObject(def, false).gameObject));
        }
    }
}

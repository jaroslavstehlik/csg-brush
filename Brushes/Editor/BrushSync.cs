using System.Collections.Generic;
using CsgBrush.Colliders;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Keeps the derived state of brushes in step with their fields: validates the shape, marks the brush's model
    /// for a rebuild (see <see cref="BrushCsg"/>), and hides the generated objects.
    ///
    /// Everything derived (models, meshes, collider pieces) is never registered with Undo. The only undo state is
    /// the Brush component, its transform and the brush GameObject itself (see BrushApi). After an undo or redo
    /// the derived state is rebuilt from the restored fields, which is what keeps undo reliable.
    /// </summary>
    public static class BrushSync
    {
        const HideFlags kHidden = HideFlags.HideInHierarchy | HideFlags.NotEditable;

        public static void Ensure(Brush brush)
        {
            if (brush == null) return;
            if (PrefabUtility.IsPartOfPrefabAsset(brush)) return;
            BrushCache.Forget(brush);
            RemoveLegacyChildren(brush);
            if (brush.HasLegacySurface) MigrateLegacySurface(brush);
            brush.problem = null;
            if (brush.shape == BrushShape.Custom)
            {
                if (brush.polyhedron == null || !brush.polyhedron.IsValid)
                    brush.polyhedron = BrushGeometry.ShapePolyhedron(brush.customFrom, BrushGeometry.ShapeParams.From(brush)); // picked Custom in the dropdown
                brush.size = brush.polyhedron.Bounds().size;
                if (!brush.polyhedron.IsSound(out var why)) brush.problem = "Shape is " + why + ".";
            }
            else if (brush.HasParametricSize)
                brush.size = BrushGeometry.Polyhedron(brush).Bounds().size; // curved and spiral stairs: the size follows the parameters
            BrushCsg.MarkDirty(brush);
        }

        /// <summary>
        /// The surface kind of earlier versions (solid, slick, water, trigger, no collision) and its fall-damage flag
        /// become the Collision field and, through <see cref="Brush.LegacySurfaceMigration"/>, the game's module.
        /// </summary>
        static void MigrateLegacySurface(Brush brush)
        {
            var (surface, noFallDamage) = brush.LegacySurface;
            bool handled = Brush.LegacySurfaceMigration != null && Brush.LegacySurfaceMigration(brush, surface, noFallDamage);
            if (surface == 3) { brush.collision = ColliderKind.Trigger; handled = true; }
            else if (surface == 4) { brush.collision = ColliderKind.None; handled = true; }
            else if (surface == 2 && handled) brush.collision = ColliderKind.Trigger;
            if (!handled) return; // no module package registered yet: keep the old values for when one is
            brush.ClearLegacySurface();
            EditorUtility.SetDirty(brush);
        }

        /// <summary>Hidden children of the Chisel era ("<[shape]>", "<[hollow]>", pieces) are no longer used; drop them.</summary>
        static void RemoveLegacyChildren(Brush brush)
        {
            for (int i = brush.transform.childCount - 1; i >= 0; i--)
            {
                var child = brush.transform.GetChild(i);
                if (Brush.IsGeneratedChildName(child.name)) { child.SetParent(null, false); Object.DestroyImmediate(child.gameObject); }
            }
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(brush.gameObject) > 0)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(brush.gameObject);
        }

        /// <summary>Objects Chisel generated in a scene saved before the switch: its default model and generated containers.</summary>
        public static void RemoveLegacySceneObjects()
        {
            var doomed = new List<GameObject>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t == null) continue;
                if (t.name == "‹[default-model]›" || t.name.StartsWith("‹[generated")) doomed.Add(t.gameObject);
            }
            foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        /// <summary>After a structural change (brush added, removed, reordered, operation changed) the model is rebuilt on the next update.</summary>
        public static void RequestFullUpdate(Brush brush)
        {
            if (brush == null) return;
            BrushCsg.InvalidateGrouping(); // added, reordered or moved to another parent: the group lists change
            BrushCsg.MarkDirty(brush);
        }

        /// <summary>Snapping, scripts and parent moves change transforms directly: mark the model.</summary>
        public static void NotifyTransformChanged(Brush brush)
        {
            if (brush == null) return;
            BrushCache.Forget(brush);
            BrushCsg.MarkDirty(brush);
        }

        /// <summary>Set the visibility of the default model and the generated objects according to the settings.</summary>
        public static void ApplyVisibility()
        {
            // prefab instances included: Unity never stores hide flags in a prefab file, and hiding an instance's
            // objects is not an override, so they are hidden in memory like everything else
            foreach (var model in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include)) HideGenerated(model);
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                foreach (var model in stage.prefabContentsRoot.GetComponentsInChildren<BrushGroup>(true)) HideGenerated(model);
            var generators = BrushGenerator.Active;
            for (int i = 0; i < generators.Count; i++) BrushGenerators.ApplyVisibility(generators[i]);
            EditorApplication.RepaintHierarchyWindow();
        }

        /// <summary>
        /// Generated objects (a group's mesh children, its collider container and pieces, its Convex Colliders component
        /// and a scene's automatic group) are authored for the user, never by them: hidden and not editable, unless
        /// Show generated objects is on for debugging. Applied in memory wherever they appear (Unity keeps no hide flags
        /// in prefab files), right after every build and by the sweep over scenes and Prefab Mode.
        /// </summary>
        public static void HideGenerated(BrushGroup model) => HideGenerated(model, BrushSettings.instance.showGenerated);

        public static void HideGenerated(BrushGroup model, bool show)
        {
            var flags = show ? HideFlags.NotEditable : kHidden;
            if (model.isDefault && model.gameObject.hideFlags != flags) model.gameObject.hideFlags = flags;
            var componentFlags = show ? HideFlags.None : HideFlags.HideInInspector;
            if (model.TryGetComponent<ConvexColliderSettings>(out var cc))
            {
                if (cc.showInHierarchy != show) cc.showInHierarchy = show;
                if (cc.hideFlags != componentFlags) cc.hideFlags = componentFlags;
            }
            foreach (Transform child in model.transform)
            {
                if (!IsGenerated(child)) continue;
                if (child.gameObject.hideFlags != flags) child.gameObject.hideFlags = flags;
                foreach (Transform piece in child) if (piece.gameObject.hideFlags != flags) piece.gameObject.hideFlags = flags;
            }
        }

        public static bool IsGenerated(Transform t) => BrushGroup.IsMeshChildName(t.name) || t.name == ConvexColliderSettings.ContainerName;
    }
}

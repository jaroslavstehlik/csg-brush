using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Keeps every <see cref="BrushGenerator"/>'s brushes in step with its data. The brushes are derived state, like a group's
    /// mesh: never registered with Undo, built again after an undo from the restored data, hidden unless Show generated
    /// objects is on. A generator whose key still matches what it made (a prefab instance just loaded, say) is left alone.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushGenerators
    {
        static readonly HashSet<BrushGenerator> s_Dirty = new HashSet<BrushGenerator>();
        static bool s_Force; // after an undo the stored key was restored with the data: compare the brushes themselves

        static BrushGenerators()
        {
            BrushGenerator.Changed = MarkDirty;
            EditorApplication.update += Flush;
            Undo.undoRedoPerformed += () => { s_Force = true; MarkAllDirty(); };
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => MarkAllDirty();
            EditorApplication.delayCall += MarkAllDirty; // after a domain reload
        }

        public static void MarkDirty(BrushGenerator generator) { if (generator != null) s_Dirty.Add(generator); }

        /// <summary>After a generator's brushes are up to date, changed or not (a floor plan places its doors then).</summary>
        public static event System.Action<BrushGenerator> Updated;

        public static void MarkAllDirty()
        {
            var active = BrushGenerator.Active;
            for (int i = 0; i < active.Count; i++) s_Dirty.Add(active[i]);
        }

        /// <summary>Bring the dirty generators up to date (the editor loop does this every update).</summary>
        public static void Flush()
        {
            if (s_Dirty.Count == 0 || Application.isPlaying) return;
            var list = new List<BrushGenerator>(s_Dirty); s_Dirty.Clear();
            bool force = s_Force; s_Force = false;
            foreach (var g in list) if (g != null && g.isActiveAndEnabled) Update(g, force);
        }

        static readonly List<BrushGenerator.BrushSpec> s_Specs = new List<BrushGenerator.BrushSpec>();

        /// <summary>
        /// Make the generator's brushes match its data, unless its key says they already do. Forced, every brush is compared
        /// and only differing ones are written, so an unchanged generator (a prefab instance, say) is left untouched.
        /// </summary>
        public static void Update(BrushGenerator g, bool force = false)
        {
            if (g == null || EditorUtility.IsPersistent(g)) return; // a prefab asset: built when its contents are
            int key = g.Key();
            if (!force && key == g.generatedKey && Intact(g))
            {
                foreach (var b in g.generated) if (SyncObject(g, b)) BrushCsg.MarkDirty(b); // layer, tag or static flags of the generator changed
                ApplyVisibility(g);
                Updated?.Invoke(g);
                return;
            }
            s_Specs.Clear();
            g.Describe(s_Specs);
            var container = Container(g, true);
            var made = new List<Brush>(s_Specs.Count);
            for (int i = 0; i < s_Specs.Count; i++)
            {
                var spec = s_Specs[i];
                var brush = i < g.generated.Count ? g.generated[i] : null;
                if (brush == null || brush.transform.parent != container)
                {
                    var go = new GameObject(spec.name);
                    go.transform.SetParent(container, false);
                    brush = go.AddComponent<Brush>();
                }
                Apply(g, brush, spec, i);
                made.Add(brush);
            }
            // brushes no longer described, and strays under the container
            var keep = new HashSet<Brush>(made);
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (!child.TryGetComponent<Brush>(out var b) || !keep.Contains(b)) Object.DestroyImmediate(child.gameObject);
            }
            g.generated = made;
            g.generatedKey = key;
            EditorUtility.SetDirty(g);
            BrushCsg.InvalidateGrouping();
            ApplyVisibility(g);
            Updated?.Invoke(g);
        }

        static void Apply(BrushGenerator g, Brush brush, BrushGenerator.BrushSpec spec, int index)
        {
            var go = brush.gameObject;
            if (go.name != spec.name) go.name = spec.name;
            if (brush.transform.GetSiblingIndex() != index) brush.transform.SetSiblingIndex(index); // CSG order
            var t = brush.transform;
            if (t.localPosition != Vector3.zero) t.localPosition = Vector3.zero;
            if (t.localRotation != Quaternion.identity) t.localRotation = Quaternion.identity;
            if (t.localScale != Vector3.one) t.localScale = Vector3.one;
            bool changed = SyncObject(g, brush);
            if (brush.generatedBy != g) { brush.generatedBy = g; changed = true; }
            if (brush.shape != BrushShape.Custom) { brush.shape = BrushShape.Custom; changed = true; }
            if (brush.operation != spec.operation) { brush.operation = spec.operation; changed = true; }
            if (brush.material != spec.material) { brush.material = spec.material; changed = true; }
            if (brush.polyhedron == null || !brush.polyhedron.IsValid || brush.polyhedron.ContentHash() != spec.polyhedron.ContentHash()) { brush.polyhedron = spec.polyhedron; changed = true; }
            if (changed) BrushSync.Ensure(brush);
        }

        /// <summary>The generator object's layer, tag and static flags onto a brush (what its pieces and meshes take). True when any changed.</summary>
        static bool SyncObject(BrushGenerator g, Brush brush)
        {
            if (brush == null) return false;
            var go = brush.gameObject; bool changed = false;
            if (go.layer != g.gameObject.layer) { go.layer = g.gameObject.layer; changed = true; }
            if (!go.CompareTag(g.gameObject.tag)) { go.tag = g.gameObject.tag; changed = true; }
            var flags = GameObjectUtility.GetStaticEditorFlags(g.gameObject);
            if (GameObjectUtility.GetStaticEditorFlags(go) != flags) { GameObjectUtility.SetStaticEditorFlags(go, flags); changed = true; }
            if (changed) BrushCache.Forget(brush);
            return changed;
        }

        static bool Intact(BrushGenerator g)
        {
            var container = Container(g, false);
            if (container == null) return g.generated.Count == 0;
            foreach (var b in g.generated) if (b == null || b.transform.parent != container) return false;
            return true;
        }

        /// <summary>The hidden child the generator's brushes live under.</summary>
        public static Transform Container(BrushGenerator g, bool create)
        {
            var t = g.transform.Find(BrushGenerator.ContainerName);
            if (t != null || !create) return t;
            var go = new GameObject(BrushGenerator.ContainerName);
            go.transform.SetParent(g.transform, false);
            return go.transform;
        }

        /// <summary>Hide the generated brushes (Show generated objects reveals them, read-only).</summary>
        public static void ApplyVisibility(BrushGenerator g)
        {
            var container = Container(g, false);
            if (container == null) return;
            var flags = BrushSettings.instance.showGenerated ? HideFlags.NotEditable : HideFlags.HideInHierarchy | HideFlags.NotEditable;
            if (container.gameObject.hideFlags != flags) container.gameObject.hideFlags = flags;
            foreach (Transform child in container) if (child.gameObject.hideFlags != flags) child.gameObject.hideFlags = flags;
        }

        /// <summary>The object a click on a brush selects: its generator for a generated brush, the brush otherwise.</summary>
        public static GameObject SelectionTarget(Brush brush) => brush == null ? null : brush.IsGenerated ? brush.generatedBy.gameObject : brush.gameObject;
    }
}

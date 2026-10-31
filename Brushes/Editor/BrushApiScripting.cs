using System;
using System.Collections.Generic;
using System.Text;
using CsgBrush.Colliders;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CsgBrush.Editor
{
    /// <summary>What a rebuild produced, for scripts and tests: counts, and the pieces that got convex mesh colliders.</summary>
    public struct RebuildReport
    {
        public int brushes, groups, triangles, boxColliders, meshColliders;
        public double milliseconds;
        /// <summary>The collider pieces that are convex meshes rather than boxes (physics treats boxes more exactly).</summary>
        public List<GameObject> meshColliderPieces;

        public override string ToString() =>
            $"{Count(brushes, "brush", "brushes")} in {Count(groups, "group", "groups")}: {Count(triangles, "triangle", "triangles")}, " +
            $"{Count(boxColliders, "box collider", "box colliders")}, {Count(meshColliders, "convex mesh collider", "convex mesh colliders")}, {milliseconds:0} ms";

        static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

        /// <summary>Count what the loaded scenes' brush groups hold now.</summary>
        public static RebuildReport OfLoadedScenes(double milliseconds)
        {
            var report = new RebuildReport { milliseconds = milliseconds, meshColliderPieces = new List<GameObject>() };
            foreach (var brush in Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (brush.gameObject.scene.IsValid()) report.brushes++;
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!group.gameObject.scene.IsValid()) continue;
                report.groups++;
                foreach (Transform child in group.transform)
                    if (BrushGroup.IsMeshChildName(child.name) && child.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                        report.triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
            }
            foreach (var piece in Object.FindObjectsByType<ConvexPiece>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!piece.gameObject.scene.IsValid()) continue;
                if (piece.TryGetComponent<BoxCollider>(out _)) report.boxColliders++;
                else if (piece.TryGetComponent<MeshCollider>(out _)) { report.meshColliders++; report.meshColliderPieces.Add(piece.gameObject); }
            }
            return report;
        }
    }

    /// <summary>
    /// A scope for building many brushes from a script: one undo step for all of them, and one rebuild when it ends
    /// (its <see cref="Report"/>). Scopes nest; only the outermost rebuilds.
    /// </summary>
    public sealed class BrushBatch : IDisposable
    {
        readonly int undoGroup;
        bool disposed;

        /// <summary>What the rebuild at the end produced (after <see cref="Dispose"/>).</summary>
        public RebuildReport Report { get; private set; }

        internal BrushBatch(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            undoGroup = Undo.GetCurrentGroup();
            BrushApi.batchDepth++;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            BrushApi.batchDepth--;
            if (BrushApi.batchDepth == 0) Report = BrushApi.ForceUpdate();
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    public static partial class BrushApi
    {
        internal static int batchDepth;

        /// <summary>
        /// A box brush between two corners in world space, exactly there (not snapped to the grid unless asked): what a
        /// level built from measured numbers wants.
        /// </summary>
        public static Brush CreateBox(Vector3 min, Vector3 max, Transform parent = null, string name = null,
            BrushOperation operation = BrushOperation.Add, Material material = null, bool snap = false)
        {
            Vector3 lo = Vector3.Min(min, max), hi = Vector3.Max(min, max);
            var brush = Create(BrushShape.Box, (lo + hi) * 0.5f, hi - lo, Quaternion.identity, parent, name, snap);
            if (operation != BrushOperation.Add) SetOperation(brush, operation);
            if (material != null) SetMaterial(brush, material);
            return brush;
        }

        /// <summary>Build many brushes as one undo step with one rebuild at the end: <c>using (BrushApi.Batch()) { ... }</c>.</summary>
        public static BrushBatch Batch(string name = "Build brushes") => new BrushBatch(name);

        /// <summary>A new brush group; a moving one (a lift, a drawbridge) is built once and moved as a whole.</summary>
        public static BrushGroup CreateGroup(string name, Transform parent = null, bool moving = false)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create brush group");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Create brush group");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            GameObjectUtility.SetStaticEditorFlags(go, moving ? 0 : BrushSettings.instance.defaultModelStaticFlags);
            var group = Undo.AddComponent<BrushGroup>(go);
            group.moving = moving;
            return group;
        }

        /// <summary>
        /// Rebuild what a script made, keeping everything else: the child of <paramref name="parent"/> named
        /// <paramref name="name"/> (made if missing) is emptied, kept first among its siblings, and filled by
        /// <paramref name="build"/>; then the level is rebuilt once. Brushes a designer adds beside it survive, and a
        /// subtract brush added after it still cuts what was generated.
        /// </summary>
        public static GeneratedBrushes Regenerate(Transform parent, string name, Action<Transform> build, string generator = null)
        {
            Transform container = null;
            if (parent != null)
            {
                foreach (Transform child in parent)
                    if (child.name == name && child.GetComponent<GeneratedBrushes>() != null) { container = child; break; }
            }
            else
            {
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root.name == name && root.GetComponent<GeneratedBrushes>() != null) { container = root.transform; break; }
            }
            using (Batch("Regenerate " + name))
            {
                if (container == null)
                {
                    var go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, "Regenerate brushes");
                    if (parent != null) Undo.SetTransformParent(go.transform, parent, "Regenerate brushes");
                    container = go.transform;
                    Undo.AddComponent<GeneratedBrushes>(go);
                }
                var marker = container.GetComponent<GeneratedBrushes>();
                marker.generator = generator ?? name;
                Undo.SetSiblingIndex(container, 0, "Regenerate brushes");
                for (int i = container.childCount - 1; i >= 0; i--)
                    Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
                build(container);
            }
            return container.GetComponent<GeneratedBrushes>();
        }

        [MenuItem("Tools/CSG Brush/Select Convex Mesh Colliders", false, 200)]
        static void SelectMeshCollidersMenu()
        {
            var report = RebuildReport.OfLoadedScenes(0);
            Selection.objects = report.meshColliderPieces.ToArray();
            var text = new StringBuilder(report.ToString().Replace(", 0 ms", ""));
            if (report.meshColliders == 0) text.Append(". Every collider piece is a box.");
            else text.Append(". The convex mesh colliders are selected; physics treats them less exactly than boxes.");
            Debug.Log(text.ToString());
        }
    }

    [CustomEditor(typeof(GeneratedBrushes))]
    sealed class GeneratedBrushesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("These brushes are made by code and are replaced when it runs again. Put your own brushes beside this object, not inside it: they are kept, and your subtract brushes still cut what is generated here.", MessageType.Info);
            DrawDefaultInspector();
        }
    }
}

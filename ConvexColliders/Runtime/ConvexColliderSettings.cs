using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// Settings of a model's convex colliders, added to every model automatically. The builder makes one
    /// convex collider per brush piece, because character controllers and PhysX both handle convex shapes far better than concave mesh
    /// colliders (start-in-solid detection, sweeps, contact generation). Rendering uses the model's CSG mesh.
    ///
    /// Pieces live under a child named by <see cref="ContainerName"/> and are rebuilt with the model's mesh (editor only).
    /// </summary>
    [AddComponentMenu("CSG Brush/Convex Colliders")]
    [DisallowMultipleComponent]
    public sealed class ConvexColliderSettings : MonoBehaviour
    {
        public const string ContainerName = "<[convex colliders]>";

        [Tooltip("Rebuild after every model update in the editor.")]
        public bool autoRebuild = true;
        [Tooltip("Layer for the generated colliders.")]
        public int layer = 0;
        [Tooltip("Pieces with a smaller volume (cubic meters) are dropped as slivers.")]
        public float minPieceVolume = 1e-6f;
        [Tooltip("Show the generated pieces in the hierarchy.")]
        public bool showInHierarchy = true;

        [Header("Last build")]
        [HideInInspector] public int lastGeometryHash;
        public int brushCount;
        public int pieceCount;
        public int boxColliders;
        public int meshColliders;
        public int triggerVolumes;
        public float buildMilliseconds;
    }
}

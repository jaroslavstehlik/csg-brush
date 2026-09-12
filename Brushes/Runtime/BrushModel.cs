using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// The root under which brushes are combined into one mesh and one set of convex colliders. Brushes outside
    /// any model belong to a hidden default model that is created automatically. A model's transform moves its
    /// whole level part; brush geometry is built in the model's local space.
    /// </summary>
    [AddComponentMenu("Brush Model")]
    [DisallowMultipleComponent]
    public sealed class BrushModel : MonoBehaviour
    {
        public const string DefaultName = "<[default model]>";
        public const string MeshChildName = "<[mesh]>";

        /// <summary>The implicit model for brushes that are not under a user-made model.</summary>
        [HideInInspector] public bool isDefault;
    }
}

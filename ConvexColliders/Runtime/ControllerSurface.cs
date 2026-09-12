using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// What a brush's volume means to the character controllers (the Brush component carries the value).
    /// Without it a brush is plain solid. Looked up on the brush object and its parents, so a composite
    /// can tag all its children at once.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ControllerSurface : MonoBehaviour
    {
        public enum Kind
        {
            /// <summary>Solid, walkable.</summary>
            Solid = 0,
            /// <summary>Solid with SURF_SLICK: no friction, air acceleration on the ground.</summary>
            Slick = 1,
            /// <summary>Not solid; a convex trigger volume. Game adapters add their own water component (see ConvexColliderHooks).</summary>
            Water = 2,
            /// <summary>Not solid; a plain trigger volume.</summary>
            Trigger = 3,
            /// <summary>Render only, no collider at all.</summary>
            NoCollision = 4,
        }

        public Kind kind = Kind.Solid;
        [Tooltip("Never take fall damage on this surface (SURF_NODAMAGE).")]
        public bool noFallDamage = false;
    }
}

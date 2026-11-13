using System;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// Keeps an object on the spline above it: which link (by the ids of its two ends), how far along it, at what angle
    /// around the profile, and how far out of its surface. The spline places it whenever either changes; move it and it
    /// takes the nearest place on the spline. The object stands on the surface: its up points out of it, its forward
    /// along the path. When its link is gone the object stays where it was.
    /// </summary>
    [AddComponentMenu("CSG Brush/Spline Anchor")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class SplineAnchor : MonoBehaviour
    {
        [HideInInspector] public int startId = WallAnchor.Unplaced, endId = WallAnchor.Unplaced;
        [Tooltip("Metres along the link from its first point.")] public float distance;
        [Tooltip("Degrees around the profile: 0 on top, 90 on the right, 180 underneath.")] public float angle;
        [Tooltip("Metres from the profile's surface to the pivot, out of it.")] public float offset;
        [Tooltip("Keep the profile's up instead of standing out of the surface: posts and lamps on a road's edge.")] public bool upright;
        /// <summary>The object's rotation relative to the surface: identity stands on it, facing along the path.</summary>
        [HideInInspector] public Quaternion rotation = Quaternion.identity;
        /// <summary>The link's length when last placed: finds the place again from the far end if the first point is gone.</summary>
        [HideInInspector] public float linkLength;
        /// <summary>The spline's next point id when its link was last seen whole (see <see cref="WallAnchor.newPointsFrom"/>).</summary>
        [HideInInspector] public int newPointsFrom = -1;

        /// <summary>Set when the spline last placed it: false while its link is not in the spline.</summary>
        [NonSerialized] public bool onPath;

        /// <summary>Set by the editor layer: the anchor's values changed (Inspector, undo, added).</summary>
        public static Action<SplineAnchor> Changed;

        public Spline Spline => transform.parent != null ? transform.parent.GetComponent<Spline>() : null;

        /// <summary>The brushes it carries are placed by the spline, not snapped to the grid.</summary>
        public void MarkBrushes(bool anchored) { foreach (var b in GetComponentsInChildren<Brush>(true)) b.anchored = anchored; }

        void OnEnable() { MarkBrushes(true); Changed?.Invoke(this); }
        void OnDisable() => MarkBrushes(false);
        void OnValidate() => Changed?.Invoke(this);
    }
}

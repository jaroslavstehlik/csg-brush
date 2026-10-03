using System;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>Where on its wall an anchored object sits.</summary>
    public enum WallFace
    {
        // the names and the Inspector's order are the floor plan's (FloorPlan.Side); the values are the saved ones
        [Tooltip("In the middle of the wall: doors and windows cut through it.")] Centered = 0,
        [Tooltip("On the wall's inside face (the room's side).")] Inside = 1,
        [Tooltip("On the wall's outside face.")] Outside = 2,
    }

    /// <summary>
    /// Keeps an object on a wall of the floor plan above it: which wall (by the ids of its two ends), how far along it,
    /// how high, and its pose relative to the wall's face. The plan places it from these whenever either changes; move it
    /// and it takes the nearest wall. Doors and windows get one by themselves; anything else (a prefab, a brush) is
    /// attached by dropping it on a wall. When its wall is gone the object stays where it was.
    /// </summary>
    [AddComponentMenu("CSG Brush/Wall Anchor")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class WallAnchor : MonoBehaviour
    {
        /// <summary><see cref="startId"/> of an anchor not placed yet: it takes the wall nearest to where the object is.</summary>
        public const int Unplaced = -2;
        /// <summary><see cref="startId"/> of an anchor on no wall: the object stays where it is.</summary>
        public const int Free = -1;

        [HideInInspector] public int startId = Unplaced, endId = Unplaced;
        [Tooltip("Metres along the wall from its first corner.")] public float distance;
        [Tooltip("Metres above the floor: a door's or window's bottom, anything else's pivot.")] public float height;
        [Tooltip("On the wall's outside face, in its middle, or on its inside face.")] public WallFace face = WallFace.Inside;
        [Tooltip("Metres from the wall face to the pivot, out of the wall.")] public float offset;
        /// <summary>The object's rotation relative to the face: identity faces out of the wall (+Z out, up is up).</summary>
        [HideInInspector] public Quaternion rotation = Quaternion.identity;
        /// <summary>The wall's length when last placed: finds the place again from the far end if the first corner is gone.</summary>
        [HideInInspector] public float wallLength;

        /// <summary>Set when the plan last placed it: false while its wall is not in the plan.</summary>
        [NonSerialized] public bool onWall;

        /// <summary>Set by the editor layer: the anchor's values changed (Inspector, undo, added).</summary>
        public static Action<WallAnchor> Changed;

        public FloorPlan Plan => transform.parent != null ? transform.parent.GetComponent<FloorPlan>() : null;

        /// <summary>The brushes it carries are placed by their wall, not snapped to the grid.</summary>
        public void MarkBrushes(bool anchored)
        {
            foreach (var b in GetComponentsInChildren<Brush>(true)) b.anchored = anchored;
        }

        void OnEnable() { MarkBrushes(true); Changed?.Invoke(this); }
        void OnDisable() => MarkBrushes(false);

        void OnValidate()
        {
            if (height < 0f) height = 0f;
            Changed?.Invoke(this);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// Places a copy of an object every few metres along a spline: sleepers on a rail, posts along a fence, brackets on a
    /// pipe, lamps along a road. Each run of the path (from an end or a junction to the next) gets its own row. The copies
    /// are generated: hidden, made again whenever the spline or these settings change. Edit the source to change them.
    /// </summary>
    [AddComponentMenu("CSG Brush/Spline Repeat")]
    [RequireComponent(typeof(Spline))]
    [ExecuteAlways]
    public sealed class SplineRepeat : MonoBehaviour
    {
        [Tooltip("The object or prefab to copy.")] public GameObject source;
        [Tooltip("Metres between copies.")] public float spacing = 2f;
        [Tooltip("Metres from the start of each run to the first copy.")] public float start = 0f;
        [Tooltip("Degrees around the profile: 0 on top, 90 on the right, 180 underneath.")] public float angle;
        [Tooltip("Metres from the profile's surface to each copy's pivot, out of it.")] public float offset;
        [Tooltip("Keep the profile's up instead of standing out of the surface: posts and lamps on a road's edge.")] public bool upright;
        [Tooltip("Each copy's turn, in degrees, relative to standing on the surface facing along the path.")] public Vector3 rotation;

        /// <summary>The hidden object holding the copies. Derived: made again from the settings.</summary>
        [HideInInspector] public Transform container;
        /// <summary>A hash of what the copies were made from.</summary>
        [HideInInspector] public int builtKey;

        public const string ContainerName = "<[repeated objects]>";

        /// <summary>Set by the editor layer: the settings changed (Inspector, undo, added).</summary>
        public static Action<SplineRepeat> Changed;

        void OnEnable() => Changed?.Invoke(this);
        void OnValidate()
        {
            spacing = Mathf.Max(0.05f, spacing);
            start = Mathf.Max(0f, start);
            Changed?.Invoke(this);
        }
    }
}

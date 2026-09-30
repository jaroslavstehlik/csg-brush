using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A component that makes brushes from its own data (a floor plan's outline, say). You edit the generator; the brushes
    /// it makes are hidden children under <see cref="ContainerName"/>, built again whenever its data changes, and never
    /// snapped, picked or edited on their own. They are ordinary brushes otherwise: CSG order, groups, layers, colliders
    /// and modules inherited from a parent all apply. They take the generator object's layer, tag and static flags.
    /// </summary>
    [ExecuteAlways]
    public abstract class BrushGenerator : MonoBehaviour
    {
        public const string ContainerName = "<[generated brushes]>";

        /// <summary>One brush to make: its shape in the generator's space.</summary>
        public struct BrushSpec
        {
            public string name;
            public BrushOperation operation;
            public BrushPolyhedron polyhedron;
        }

        /// <summary>The brushes made last time, in order. Derived: rebuilt from the generator's data.</summary>
        [HideInInspector] public List<Brush> generated = new List<Brush>();
        /// <summary><see cref="Key"/> of the data the brushes were made from.</summary>
        [HideInInspector] public int generatedKey;

        /// <summary>A hash of everything the brushes depend on: equal keys, equal brushes.</summary>
        public abstract int Key();

        /// <summary>The brushes to make, in CSG order.</summary>
        public abstract void Describe(List<BrushSpec> into);

        /// <summary>Set by the editor layer: a generator's data changed (Inspector, undo, enable).</summary>
        public static Action<BrushGenerator> Changed;

        static readonly List<BrushGenerator> s_Active = new List<BrushGenerator>();

        /// <summary>Every enabled generator on an active object (as <see cref="Brush.Active"/>). Iterate by index.</summary>
        public static IReadOnlyList<BrushGenerator> Active => s_Active;

        protected virtual void OnEnable() { if (!s_Active.Contains(this)) s_Active.Add(this); Changed?.Invoke(this); }
        protected virtual void OnDisable() => s_Active.Remove(this);
        protected virtual void OnValidate() => Changed?.Invoke(this);
    }
}

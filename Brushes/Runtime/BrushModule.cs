using System;
using UnityEngine;
using UnityEngine.Events;
using CsgBrush.Colliders;

namespace CsgBrush
{
    /// <summary>
    /// A game's own data on a brush. The CSG package only knows whether a volume is solid, a trigger or nothing;
    /// everything a specific character controller cares about (ice, water, fall damage, footsteps) is a module
    /// component on the brush object or one of its parents (a parent tags all its children). A module's data rides
    /// along onto every collider piece generated from the brush, and is part of the piece's identity, so a change
    /// rebuilds exactly the pieces it affects.
    /// </summary>
    public abstract class BrushModule : MonoBehaviour
    {
        /// <summary>Hash of the module's values; when it changes, the pieces are rebuilt and <see cref="ApplyToPiece"/> runs again.</summary>
        public abstract int Fingerprint();

        /// <summary>A module may decide what the volume is to physics (a water module makes a trigger) without the user setting it twice.</summary>
        public virtual bool OverrideCollision(out ColliderKind collision) { collision = ColliderKind.Solid; return false; }

        /// <summary>
        /// Put the module's data on a collider piece of the brush. Called on every build for every piece, new or
        /// reused, so it must be idempotent (get-or-add the component, set its values).
        /// </summary>
        public abstract void ApplyToPiece(GameObject piece, bool isTrigger);
    }

    /// <summary>Components on a trigger brush's object that want its trigger events (one per brush, however many pieces it has).</summary>
    public interface IBrushTriggerListener
    {
        void OnBrushTriggerEnter(Brush brush, Collider other);
        void OnBrushTriggerExit(Brush brush, Collider other);
    }

    /// <summary>On every trigger piece: forwards Unity's trigger events to the brush, which counts them per collider across its pieces.</summary>
    [AddComponentMenu("")]
    public sealed class BrushTriggerRelay : MonoBehaviour
    {
        public Brush brush;
        void OnTriggerEnter(Collider other) { if (brush != null) brush.PieceTriggerEnter(other); }
        void OnTriggerExit(Collider other) { if (brush != null) brush.PieceTriggerExit(other); }
    }

    /// <summary>
    /// The plain trigger module: makes the brush a trigger volume and exposes its Enter and Exit as UnityEvents,
    /// so a door or a checkpoint can be wired in the Inspector without code. Code can implement
    /// <see cref="IBrushTriggerListener"/> on the same object, or subscribe to the brush's events, instead.
    /// </summary>
    [AddComponentMenu("CSG Brush/Brush Trigger")]
    public sealed class BrushTrigger : BrushModule, IBrushTriggerListener
    {
        public UnityEvent<Collider> onEnter = new UnityEvent<Collider>();
        public UnityEvent<Collider> onExit = new UnityEvent<Collider>();

        public override int Fingerprint() => 1;
        public override bool OverrideCollision(out ColliderKind collision) { collision = ColliderKind.Trigger; return true; }
        public override void ApplyToPiece(GameObject piece, bool isTrigger) { }
        public void OnBrushTriggerEnter(Brush brush, Collider other) => onEnter?.Invoke(other);
        public void OnBrushTriggerExit(Brush brush, Collider other) => onExit?.Invoke(other);
    }
}

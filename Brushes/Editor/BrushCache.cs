using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// What the editor derives from one brush and keeps on it (<see cref="Brush.editorCache"/>) until the brush changes: its
    /// pick shape and world box, and its build key and pose in its group. Every change to a brush's shape or pose goes through
    /// <see cref="BrushSync.Ensure"/>, <see cref="BrushSync.NotifyTransformChanged"/> or the snap functions, which call
    /// <see cref="Forget"/>; the next use measures the brush again. Lives and dies with the brush: no dictionary, no cleanup.
    /// </summary>
    sealed class BrushCache
    {
        // picking (BrushHooks)
        public bool pickReady;
        public BrushPolyhedron polyhedron;
        public Vector3 min, max;
        public Matrix4x4 toLocal, toWorld;
        public Quaternion rotation;

        // building (BrushCsg)
        public BrushGroup keyModel; // the group the key was taken in; null until then
        public Matrix4x4 toModel;
        public int key;

        // collider data (BrushCsg): what the brush puts on its pieces, taken again when the brush or its structure changes
        public int infoEpoch = -1;
        public int fingerprint, layer;
        public CsgBrush.Colliders.ColliderKind kind;
        public string name;
        public System.Action<GameObject, bool> onPiece;

        /// <summary>
        /// Raised when something outside the brush may change what it puts on its pieces: a module edited, added or removed,
        /// a parent changed (modules are inherited). The brush's own tag, layer, static flags and name drop its cache directly.
        /// </summary>
        public static int InfoEpoch;

        public static BrushCache Of(Brush brush) => brush.editorCache as BrushCache ?? (BrushCache)(brush.editorCache = new BrushCache());

        /// <summary>The brush's shape or pose changed: measure it again next time.</summary>
        public static void Forget(Brush brush) { if (brush != null) brush.editorCache = null; }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// World-space grid enforcement, the way Unreal and TrenchBroom do it: the grid is a world grid, snapping is
    /// absolute (values are rounded to the grid, not moved by increments), sizes are grid multiples, rotation
    /// snaps to fixed angles and scale is never used. The GameObject hierarchy is not allowed to matter: a brush
    /// under a rotated or scaled parent is still snapped in world space, and the parent is reported by the lint.
    /// </summary>
    public static class BrushSnap
    {
        const float kEpsilon = 1e-4f;

        public static float Round(float value, float step) => step > 0f ? Mathf.Round(value / step) * step : value;

        public static Vector3 Round(Vector3 v, float step) => new Vector3(Round(v.x, step), Round(v.y, step), Round(v.z, step));

        /// <summary>Sizes become multiples of the grid, never smaller than one grid step.</summary>
        public static Vector3 SnapSize(Vector3 sizeMeters, float grid)
        {
            if (grid <= 0f) return sizeMeters;
            return new Vector3(Mathf.Max(grid, Round(sizeMeters.x, grid)), Mathf.Max(grid, Round(sizeMeters.y, grid)), Mathf.Max(grid, Round(sizeMeters.z, grid)));
        }

        public static Quaternion SnapRotation(Quaternion worldRotation, float degrees)
        {
            if (degrees <= 0f) return worldRotation;
            var e = worldRotation.eulerAngles;
            return Quaternion.Euler(Round(e.x, degrees), Round(e.y, degrees), Round(e.z, degrees));
        }

        /// <summary>True when the rotation maps the local axes onto world axes (multiples of 90 degrees).</summary>
        public static bool IsAxisAligned(Quaternion worldRotation)
        {
            var x = worldRotation * Vector3.right; var y = worldRotation * Vector3.up; var z = worldRotation * Vector3.forward;
            return IsAxis(x) && IsAxis(y) && IsAxis(z);
        }

        static bool IsAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return (Mathf.Abs(v.x - 1f) < 1e-3f && v.y < 1e-3f && v.z < 1e-3f) || (Mathf.Abs(v.y - 1f) < 1e-3f && v.x < 1e-3f && v.z < 1e-3f) || (Mathf.Abs(v.z - 1f) < 1e-3f && v.x < 1e-3f && v.y < 1e-3f);
        }

        /// <summary>World-space extents of a brush of the given size under an axis-aligned rotation.</summary>
        public static Vector3 WorldExtents(Vector3 size, Quaternion worldRotation)
        {
            var x = worldRotation * new Vector3(size.x, 0f, 0f); var y = worldRotation * new Vector3(0f, size.y, 0f); var z = worldRotation * new Vector3(0f, 0f, size.z);
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }

        /// <summary>A brush's snapped size: grid multiples, except a door's or window's, which is its own.</summary>
        public static Vector3 SnapBrushSize(Brush brush, Vector3 size, float grid) => brush.IsOpening ? size : SnapSize(size, grid);

        /// <summary>A brush's snapped position (see <see cref="SnapPosition"/>): faces on grid lines, as in Hammer or TrenchBroom.</summary>
        public static Vector3 SnapBrushPosition(Brush brush, Vector3 worldPosition, Vector3 size, Quaternion worldRotation, float grid) => SnapPosition(worldPosition, size, worldRotation, grid);

        /// <summary>Snapped world position: the minimum corner lands on the grid for axis-aligned brushes, the pivot otherwise.</summary>
        public static Vector3 SnapPosition(Vector3 worldPosition, Vector3 size, Quaternion worldRotation, float grid)
        {
            if (grid <= 0f) return worldPosition;
            if (!IsAxisAligned(worldRotation)) return Round(worldPosition, grid);
            var half = WorldExtents(size, worldRotation) * 0.5f;
            var corner = Round(worldPosition - half, grid);
            return corner + half;
        }

        /// <summary>
        /// Clear a snapped brush's changed flag. The snap check reads that flag to notice moves, so a move it would have seen
        /// (by a script, or a parent) is passed on here: the pick shape and the rotated-parents answer are measured again.
        /// </summary>
        static void ClearChanged(Brush brush, Transform t)
        {
            if (!t.hasChanged) return;
            BrushCache.Forget(brush);
            s_ParentsVersion = -1;
            t.hasChanged = false;
        }

        /// <summary>Snap one brush in world space. Returns true when anything changed. Records nothing with Undo.</summary>
        public static bool Snap(Brush brush)
        {
            var s = BrushSettings.instance;
            if (brush == null || !s.snapToGrid || brush.IsPlaced) return false; // a generator places its own brushes; doors and windows are placed on walls
            float grid = s.GridMeters;
            var t = brush.transform;
            bool changed = false;

            if (brush.shape == BrushShape.Custom) return SnapCustom(brush, grid, s.rotationSnapDegrees);
            if (brush.HasParametricSize) return SnapPivot(brush, grid, s.rotationSnapDegrees);

            // The Scale tool resizes the brush: any scale the user applied on top of the parent counter-scale is
            // baked into the size, then the world scale goes back to one.
            var desiredScale = CounterScale(t);
            var size = brush.size;
            if ((t.localScale - desiredScale).sqrMagnitude > kEpsilon * kEpsilon)
            {
                var user = UserScale(t, desiredScale);
                size = new Vector3(size.x * user.x, size.y * user.y, size.z * user.z);
                t.localScale = desiredScale;
                changed = true;
            }
            size = SnapBrushSize(brush, size, grid);
            if ((size - brush.size).sqrMagnitude > kEpsilon * kEpsilon) { brush.size = size; changed = true; }

            var rotation = SnapRotation(t.rotation, s.rotationSnapDegrees);
            if (Quaternion.Angle(rotation, t.rotation) > 1e-3f) { t.rotation = rotation; changed = true; }

            var position = SnapBrushPosition(brush, t.position, size, rotation, grid);
            if ((position - t.position).sqrMagnitude > kEpsilon * kEpsilon) { t.position = position; changed = true; }

            if (changed) BrushSync.NotifyTransformChanged(brush);
            ClearChanged(brush, t);
            return changed;
        }

        /// <summary>The scale the user applied, relative to the counter-scale that keeps world scale at one.</summary>
        static Vector3 UserScale(Transform t, Vector3 counter)
        {
            var l = t.localScale;
            return new Vector3(Mathf.Abs(counter.x) > 1e-6f ? Mathf.Abs(l.x / counter.x) : 1f, Mathf.Abs(counter.y) > 1e-6f ? Mathf.Abs(l.y / counter.y) : 1f, Mathf.Abs(counter.z) > 1e-6f ? Mathf.Abs(l.z / counter.z) : 1f);
        }

        /// <summary>Local scale that gives a world scale of one under the current parent.</summary>
        public static Vector3 CounterScale(Transform t)
        {
            var p = t.parent;
            if (p == null) return Vector3.one;
            var ps = p.lossyScale;
            return new Vector3(Mathf.Abs(ps.x) > 1e-6f ? 1f / ps.x : 1f, Mathf.Abs(ps.y) > 1e-6f ? 1f / ps.y : 1f, Mathf.Abs(ps.z) > 1e-6f ? 1f / ps.z : 1f);
        }

        /// <summary>
        /// A hand-edited shape has no size box: its rule is that the pivot and every vertex lie on the world grid.
        /// Rotation snaps as usual; the Scale tool is baked into the vertices; moving the pivot onto the grid keeps
        /// the geometry where it is.
        /// </summary>
        /// <summary>Shapes built around an axis (curved and spiral stairs): the transform is the axis, so the pivot, rotation and scale snap; the parameters stay.</summary>
        static bool SnapPivot(Brush brush, float grid, float rotationStep)
        {
            var t = brush.transform;
            bool changed = false;
            var rotation = SnapRotation(t.rotation, rotationStep);
            if (Quaternion.Angle(rotation, t.rotation) > 1e-3f) { t.rotation = rotation; changed = true; }
            var desiredScale = CounterScale(t);
            if ((t.localScale - desiredScale).sqrMagnitude > kEpsilon * kEpsilon) { t.localScale = desiredScale; changed = true; }
            var position = Round(t.position, grid);
            if ((position - t.position).sqrMagnitude > kEpsilon * kEpsilon) { t.position = position; changed = true; }
            if (changed) BrushSync.NotifyTransformChanged(brush);
            ClearChanged(brush, t);
            return changed;
        }

        static bool SnapCustom(Brush brush, float grid, float rotationStep)
        {
            var t = brush.transform;
            var poly = brush.polyhedron;
            bool changed = false;
            var rotation = SnapRotation(t.rotation, rotationStep);
            if (Quaternion.Angle(rotation, t.rotation) > 1e-3f) { t.rotation = rotation; changed = true; }
            var desiredScale = CounterScale(t);
            if ((t.localScale - desiredScale).sqrMagnitude > kEpsilon * kEpsilon)
            {
                var user = UserScale(t, desiredScale);
                if (poly != null && poly.IsValid) for (int i = 0; i < poly.vertices.Length; i++) poly.vertices[i] = Vector3.Scale(poly.vertices[i], user);
                t.localScale = desiredScale; changed = true;
            }
            var position = Round(t.position, grid);
            if ((position - t.position).sqrMagnitude > kEpsilon * kEpsilon)
            {
                var shift = t.InverseTransformVector(t.position - position); // keep the vertices where they are
                if (poly != null && poly.IsValid) for (int i = 0; i < poly.vertices.Length; i++) poly.vertices[i] += shift;
                t.position = position; changed = true;
            }
            if (poly != null && poly.IsValid)
            {
                bool moved = false;
                for (int i = 0; i < poly.vertices.Length; i++)
                {
                    var world = t.TransformPoint(poly.vertices[i]);
                    var snapped = Round(world, grid);
                    if ((snapped - world).sqrMagnitude > kEpsilon * kEpsilon) { poly.vertices[i] = t.InverseTransformPoint(snapped); moved = true; }
                }
                if (moved)
                {
                    for (int i = 0; i < poly.vertices.Length; i++) poly.EnsurePlanar(i);
                    poly.WeldCoincident(grid * 0.25f);
                    changed = true;
                }
            }
            if (changed) BrushSync.NotifyTransformChanged(brush);
            ClearChanged(brush, t);
            return changed;
        }

        /// <summary>Snap every brush that is off the grid; used when rotated or scaled parents are reset.</summary>
        public static int SnapAll(bool recordUndo)
        {
            int count = 0;
            var active = Brush.Active;
            for (int i = active.Count - 1; i >= 0; i--) // backwards: Ensure may not add, but a snap never removes; stay safe either way
            {
                var brush = active[i];
                if (brush == null || !IsOffGrid(brush)) continue;
                if (recordUndo) Undo.RecordObjects(new Object[] { brush, brush.transform }, "Snap brushes to grid");
                if (Snap(brush)) { count++; BrushSync.Ensure(brush); }
            }
            return count;
        }

        /// <summary>
        /// True while a scene-view handle is being dragged. Unity's Rotate and Scale handles accumulate from the
        /// current value every frame, so snapping during the drag fights the hand; snapping waits for the release.
        /// </summary>
        public static bool DragInProgress => GUIUtility.hotControl != 0;

        static readonly HashSet<Brush> s_Deferred = new HashSet<Brush>();

        /// <summary>Snap now, or after the current drag ends.</summary>
        public static void SnapOrDefer(Brush brush)
        {
            if (brush == null) return;
            if (DragInProgress) { s_Deferred.Add(brush); return; }
            if (Snap(brush)) BrushSync.Ensure(brush);
        }

        static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(Mathf.Abs(b.x) > 1e-6f ? a.x / b.x : a.x, Mathf.Abs(b.y) > 1e-6f ? a.y / b.y : a.y, Mathf.Abs(b.z) > 1e-6f ? a.z / b.z : a.z);

        static readonly Dictionary<Brush, (Matrix4x4 localToWorld, Vector3 shapeScale)> s_Preview = new Dictionary<Brush, (Matrix4x4, Vector3)>();

        /// <summary>
        /// While a handle is dragged the transform (and the wire gizmo) follows the hand freely, but the geometry is
        /// shown where it will snap: the brush's geometry is built at the snapped pose and size instead of at the
        /// transform (see <see cref="TryGetPreview"/>). The release pass then snaps the transform itself.
        /// </summary>
        public static void ApplyPreview(Brush brush)
        {
            var s = BrushSettings.instance;
            float grid = s.GridMeters;
            var t = brush.transform;
            if (brush.IsPlaced) { BrushSync.NotifyTransformChanged(brush); t.hasChanged = false; return; } // no snapped preview: placed by its generator, or a door on its wall
            var R = t.rotation; var T = t.position; var lossy = t.lossyScale;
            var Rs = SnapRotation(R, s.rotationSnapDegrees);
            if (brush.HasParametricSize)
            {
                s_Preview[brush] = (Matrix4x4.TRS(Round(T, grid), Rs, Vector3.one), Vector3.one);
                BrushSync.NotifyTransformChanged(brush);
                t.hasChanged = false;
                return;
            }
            var scaledSize = Vector3.Scale(brush.size, new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
            var sizeS = SnapBrushSize(brush, scaledSize, grid);
            var Ts = SnapBrushPosition(brush, T, sizeS, Rs, grid);
            s_Preview[brush] = (Matrix4x4.TRS(Ts, Rs, Vector3.one), Divide(sizeS, brush.size));
            BrushSync.NotifyTransformChanged(brush);
            t.hasChanged = false;
        }

        /// <summary>The snapped pose and per-axis size factor the geometry is shown with during a drag.</summary>
        public static bool TryGetPreview(Brush brush, out Matrix4x4 localToWorld, out Vector3 shapeScale)
        {
            if (s_Preview.TryGetValue(brush, out var p)) { localToWorld = p.localToWorld; shapeScale = p.shapeScale; return true; }
            localToWorld = Matrix4x4.identity; shapeScale = Vector3.one; return false;
        }

        static void ClearPreview()
        {
            if (s_Preview.Count == 0) return;
            foreach (var b in s_Preview.Keys) if (b != null) BrushSync.NotifyTransformChanged(b);
            s_Preview.Clear();
        }

        /// <summary>Snap the brushes whose transform changed since the last check (edits made without Undo).</summary>
        public static void ProcessChanged()
        {
            bool snap = BrushSettings.instance.snapToGrid;
            if (DragInProgress)
            {
                if (!snap) return;
                // preview: geometry at the snapped pose, transform free
                var dragged = Brush.Active;
                for (int i = 0; i < dragged.Count; i++)
                {
                    var brush = dragged[i];
                    if (brush == null || (!brush.CachedTransform.hasChanged && !s_Deferred.Contains(brush))) continue;
                    s_ParentsVersion = -1;
                    s_Deferred.Add(brush);
                    ApplyPreview(brush);
                }
                return;
            }
            ClearPreview();
            if (s_Deferred.Count > 0)
            {
                // release: snap the transform and rebuild
                foreach (var brush in s_Deferred)
                {
                    if (brush == null) continue;
                    if (snap) Snap(brush);
                    BrushSync.Ensure(brush);
                }
                s_Deferred.Clear();
            }
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var brush = active[i]; // registered brushes are alive: OnDisable always runs before a brush is destroyed
                var t = brush.CachedTransform;
                if (!t.hasChanged) continue;
                s_ParentsVersion = -1; // a brush or one of its parents moved
                // Something moved this brush outside Undo (a script, a parent, the snap itself): rebuild its model.
                if (snap && Snap(brush)) BrushSync.Ensure(brush);
                else BrushSync.NotifyTransformChanged(brush);
                t.hasChanged = false;
            }
        }

        // ------------------------------------------------------------------ lint

        public static bool IsOffGrid(Brush brush)
        {
            var s = BrushSettings.instance;
            float grid = s.GridMeters;
            var t = brush.transform;
            if (brush.IsPlaced) return false;
            if (brush.shape == BrushShape.Custom || brush.HasParametricSize)
            {
                if (Quaternion.Angle(SnapRotation(t.rotation, s.rotationSnapDegrees), t.rotation) > 1e-3f) return true;
                if ((t.lossyScale - Vector3.one).sqrMagnitude > 1e-4f) return true;
                if ((Round(t.position, grid) - t.position).sqrMagnitude > kEpsilon * kEpsilon) return true;
                if (brush.HasParametricSize) return false;
                var poly = brush.polyhedron;
                if (poly == null || !poly.IsValid) return false;
                foreach (var v in poly.vertices) { var w = t.TransformPoint(v); if ((Round(w, grid) - w).sqrMagnitude > kEpsilon * kEpsilon) return true; }
                return false;
            }
            if ((SnapBrushSize(brush, brush.size, grid) - brush.size).sqrMagnitude > kEpsilon * kEpsilon) return true;
            if (Quaternion.Angle(SnapRotation(t.rotation, s.rotationSnapDegrees), t.rotation) > 1e-3f) return true;
            if ((t.lossyScale - Vector3.one).sqrMagnitude > 1e-4f) return true;
            return (SnapBrushPosition(brush, t.position, brush.size, t.rotation, grid) - t.position).sqrMagnitude > kEpsilon * kEpsilon;
        }

        /// <summary>An ancestor that rotates by other than 90 degrees or scales makes the grid meaningless for its brushes.</summary>
        public static Transform TransformedParent(Brush brush)
        {
            for (var p = brush.transform.parent; p != null; p = p.parent)
            {
                if ((p.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) return p;
                if (!IsAxisAligned(p.rotation)) return p;
            }
            return null;
        }

        public static List<Brush> OffGridBrushes()
        {
            var list = new List<Brush>();
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++) if (active[i] != null && IsOffGrid(active[i])) list.Add(active[i]);
            return list;
        }

        static readonly List<Transform> s_Parents = new List<Transform>();
        static int s_ParentsVersion = -1;

        /// <summary>Forget the rotated or scaled parents found last time; the hierarchy changed.</summary>
        public static void InvalidateParents() => s_ParentsVersion = -1;

        /// <summary>
        /// The ancestors that rotate or scale brushes, each once. Drawn by the overlay on every GUI event, so the answer is
        /// kept until a brush joins or leaves, a brush or parent moves, or the hierarchy changes. Do not modify the list.
        /// </summary>
        public static List<Transform> TransformedParents()
        {
            if (s_ParentsVersion == Brush.ActiveVersion && !HasDestroyed(s_Parents)) return s_Parents;
            s_Parents.Clear();
            var set = new HashSet<Transform>();
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var brush = active[i];
                if (brush == null || brush.IsPlaced) continue; // placed by its generator (or its wall), whatever the rotation
                var p = TransformedParent(brush);
                if (p != null && set.Add(p)) s_Parents.Add(p);
            }
            s_ParentsVersion = Brush.ActiveVersion;
            return s_Parents;
        }

        static bool HasDestroyed(List<Transform> list) { for (int i = 0; i < list.Count; i++) if (list[i] == null) return true; return false; }

        /// <summary>Reset rotation and scale of the offending parents (with Undo) and re-snap their brushes.</summary>
        public static int ResetTransformedParents()
        {
            var parents = new List<Transform>(TransformedParents());
            foreach (var p in parents)
            {
                Undo.RecordObject(p, "Reset parent transform");
                p.rotation = SnapRotation(p.rotation, 90f);
                p.localScale = Vector3.one;
            }
            if (parents.Count > 0) SnapAll(true);
            return parents.Count;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Every brush operation as an undoable step. The Inspector, the menus, the Scene view tool and the
    /// tests all go through here, so undo behaves the same everywhere.
    /// </summary>
    public static class BrushApi
    {
        public static Brush Create(BrushShape shape, Vector3 position, Vector3 sizeMeters, Quaternion rotation, Transform parent = null, string name = null)
        {
            var go = new GameObject(name ?? shape.ToString());
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags); // static unless made otherwise: its meshes and colliders follow it
            Undo.RegisterCreatedObjectUndo(go, "Create brush");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Create brush");
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = BrushSnap.CounterScale(go.transform); // world scale one, whatever the parent
            var brush = Undo.AddComponent<Brush>(go);
            brush.shape = shape;
            brush.size = sizeMeters;
            if (shape == BrushShape.Stairs || shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                var d = BrushGeometry.ShapeParams.Default(sizeMeters);
                brush.stepHeight = d.stepHeight;
                brush.innerRadius = d.stairs.innerRadius; brush.stepWidth = d.stairs.stepWidth; brush.stepThickness = d.stairs.stepThickness;
                brush.curveAngle = d.stairs.curveAngle; brush.numSteps = d.stairs.numSteps; brush.stepsPer360 = d.stairs.stepsPer360;
            }
            if (brush.IsOpening) brush.operation = BrushOperation.Subtract; // a door or window is a cut
            if (shape == BrushShape.Arch) { var p = BrushCreateTool.ParametersFor(shape, sizeMeters); brush.curveAngle = p.stairs.curveAngle; brush.wallThickness = p.wallThickness; brush.sides = p.sides; }
            foreach (var t in ModuleTypes()) if (BrushSettings.instance.newModules.Contains(t.FullName)) Undo.AddComponent(go, t); // the project's modules for new brushes
            BrushSync.Ensure(brush);
            BrushSync.RequestFullUpdate(brush);
            return brush;
        }

        public static void Delete(Brush brush)
        {
            if (brush == null) return;
            Undo.DestroyObjectImmediate(brush.gameObject);
        }

        public static void SetSize(Brush brush, Vector3 sizeMeters)
        {
            if (BrushSettings.instance.snapToGrid && !brush.IsOpening) sizeMeters = BrushSnap.SnapSize(sizeMeters, BrushSettings.instance.GridMeters);
            if (brush.size == sizeMeters) return; // no-op edits must not create undo entries
            Undo.RecordObject(brush, "Resize brush");
            brush.size = sizeMeters;
            BrushSync.Ensure(brush);
        }

        public static void SetShape(Brush brush, BrushShape shape)
        {
            if (brush.shape == shape) return;
            Undo.RecordObject(brush, "Change brush shape");
            brush.shape = shape;
            BrushSync.Ensure(brush);
        }

        public static void SetOperation(Brush brush, BrushOperation operation)
        {
            if (brush.operation == operation) return;
            Undo.RecordObject(brush, "Change brush operation");
            BrushSync.RequestFullUpdate(brush);
            brush.operation = operation;
            BrushSync.Ensure(brush);
        }

        /// <summary>A brush's static flags (lightmaps, occlusion, batching), which its render triangles and colliders take; undoable.</summary>
        public static void SetStaticFlags(Brush brush, StaticEditorFlags flags)
        {
            if (GameObjectUtility.GetStaticEditorFlags(brush.gameObject) == flags) return;
            Undo.RecordObject(brush.gameObject, "Change static flags");
            GameObjectUtility.SetStaticEditorFlags(brush.gameObject, flags);
            BrushCache.Forget(brush);
            BrushCsg.MarkDirty(brush);
        }

        public static void SetCollision(Brush brush, Colliders.ColliderKind kind)
        {
            if (brush.collision == kind) return;
            Undo.RecordObject(brush, "Change brush collision");
            brush.collision = kind;
            BrushSync.Ensure(brush);
        }

        /// <summary>Add a module (a game's data) to a brush, or return the one it has; undoable.</summary>
        public static BrushModule AddModule(Brush brush, System.Type type)
        {
            if (brush == null || type == null || !typeof(BrushModule).IsAssignableFrom(type) || type.IsAbstract) return null;
            if (brush.GetComponent(type) is BrushModule existing) return existing;
            var module = Undo.AddComponent(brush.gameObject, type) as BrushModule;
            BrushSync.Ensure(brush);
            return module;
        }

        public static T AddModule<T>(Brush brush) where T : BrushModule => (T)AddModule(brush, typeof(T));

        /// <summary>The module types the project offers (every concrete BrushModule in the loaded assemblies).</summary>
        public static List<System.Type> ModuleTypes()
        {
            var list = new List<System.Type>();
            foreach (var t in TypeCache.GetTypesDerivedFrom<BrushModule>()) if (!t.IsAbstract) list.Add(t);
            list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return list;
        }

        public static void SetHollow(Brush brush, bool hollow, float wallThicknessMeters)
        {
            if (brush.hollow == hollow && Mathf.Approximately(brush.wallThickness, wallThicknessMeters)) return;
            Undo.RecordObject(brush, "Change brush hollow");
            brush.hollow = hollow;
            brush.wallThickness = wallThicknessMeters;
            BrushSync.Ensure(brush);
        }

        public static void SetMaterial(Brush brush, Material material)
        {
            if (brush.material == material) return;
            Undo.RecordObject(brush, "Change brush material");
            brush.material = material;
            BrushSync.Ensure(brush);
        }

        public static void SetStairs(Brush brush, float stepHeightMeters)
        {
            Undo.RecordObject(brush, "Change stairs");
            brush.stepHeight = stepHeightMeters;
            BrushSync.Ensure(brush);
        }

        public static void Move(Brush brush, Vector3 position)
        {
            if (brush.transform.position == position) return;
            Undo.RecordObject(brush.transform, "Move brush");
            brush.transform.position = position;
            BrushSync.NotifyTransformChanged(brush);
        }

        public static void Rotate(Brush brush, Quaternion rotation)
        {
            if (brush.transform.rotation == rotation) return;
            Undo.RecordObject(brush.transform, "Rotate brush");
            brush.transform.rotation = rotation;
            BrushSync.NotifyTransformChanged(brush);
        }

        public static void ToFirst(Brush brush)
        {
            if (brush.transform.GetSiblingIndex() == 0) return;
            Undo.SetSiblingIndex(brush.transform, 0, "Brush to first");
            BrushSync.RequestFullUpdate(brush);
        }

        public static void ToLast(Brush brush)
        {
            var t = brush.transform;
            int last = t.parent != null ? t.parent.childCount - 1 : t.gameObject.scene.rootCount - 1;
            if (t.GetSiblingIndex() == last) return;
            Undo.SetSiblingIndex(t, last, "Brush to last");
            BrushSync.RequestFullUpdate(brush);
        }

        /// <summary>The next sibling up (-1) or down (1) that the Hierarchy shows, or null at the end; hidden generated objects are skipped.</summary>
        public static Transform VisibleNeighbour(Brush brush, int direction)
        {
            var t = brush.transform;
            var roots = t.parent == null ? t.gameObject.scene.GetRootGameObjects() : null;
            int count = t.parent != null ? t.parent.childCount : roots.Length;
            for (int i = t.GetSiblingIndex() + direction; i >= 0 && i < count; i += direction)
            {
                var sibling = t.parent != null ? t.parent.GetChild(i) : roots[i].transform;
                if ((sibling.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0) return sibling;
            }
            return null;
        }

        /// <summary>One step up (-1) or down (1), past the next sibling the Hierarchy shows.</summary>
        public static void Step(Brush brush, int direction)
        {
            var neighbour = VisibleNeighbour(brush, direction);
            if (neighbour == null) return;
            Undo.SetSiblingIndex(brush.transform, neighbour.GetSiblingIndex(), direction < 0 ? "Brush up" : "Brush down");
            BrushSync.RequestFullUpdate(brush);
        }

        // ------------------------------------------------------------------ custom shapes

        /// <summary>The polyhedron of a parametric shape.</summary>
        public static BrushPolyhedron PolyhedronFor(BrushShape shape, Vector3 size, int sides)
        {
            return BrushGeometry.ShapePolyhedron(shape, BrushGeometry.ShapeParams.Default(size, sides));
        }

        /// <summary>Every parametric shape has a polyhedron now, so every shape can be edited by hand.</summary>
        public static bool CanConvertToCustom(BrushShape shape) => shape != BrushShape.Custom;

        /// <summary>Turn a parametric brush into an editable Custom shape (the first face or vertex edit does this implicitly).</summary>
        public static bool ConvertToCustom(Brush brush)
        {
            if (brush.shape == BrushShape.Custom) return true;
            if (!CanConvertToCustom(brush.shape)) return false;
            Undo.RecordObject(brush, "Edit brush shape");
            brush.customFrom = brush.shape;
            brush.polyhedron = BrushGeometry.Polyhedron(brush).Clone();
            brush.hollow = false;
            brush.shape = BrushShape.Custom;
            BrushSync.Ensure(brush);
            return true;
        }

        /// <summary>Back to the parametric shape the Custom brush came from; the hand edits are discarded (undoable).</summary>
        public static void ResetShape(Brush brush)
        {
            if (brush.shape != BrushShape.Custom) return;
            Undo.RecordObject(brush, "Reset brush shape");
            var custom = PivotOf(brush);
            brush.size = brush.polyhedron != null && brush.polyhedron.IsValid ? brush.polyhedron.Bounds().size : brush.size;
            if (custom.HasValue) brush.pivot = custom.Value; // the shape stays where it was
            brush.shape = brush.customFrom;
            brush.polyhedron = new BrushPolyhedron();
            BrushSync.Ensure(brush);
        }

        /// <summary>
        /// Where the transform sits in the brush's box, in its <see cref="Brush.pivotMode"/> (a fraction of the size, or
        /// metres from the left, bottom, back corner); a Custom shape's comes from its vertices. Null for shapes that keep
        /// their own: curved and spiral stairs, doors and windows.
        /// </summary>
        public static Vector3? PivotOf(Brush brush)
        {
            if (!PivotBox(brush, out var size, out var distance)) return null;
            return brush.pivotMode == PivotMode.Normalized ? new Vector3(Fraction(distance.x, size.x), Fraction(distance.y, size.y), Fraction(distance.z, size.z)) : distance;
        }

        /// <summary>The brush's box size and its pivot in metres from the box's left, bottom, back corner.</summary>
        static bool PivotBox(Brush brush, out Vector3 size, out Vector3 distance)
        {
            if (brush.HasPivot) { size = brush.ClampedSize; distance = brush.PivotDistance(size); return true; }
            size = distance = default;
            if (brush.shape != BrushShape.Custom || brush.polyhedron == null || !brush.polyhedron.IsValid) return false;
            var b = brush.polyhedron.Bounds();
            size = b.size; distance = -b.min;
            return true;
        }

        static float Fraction(float distance, float size) => size > 1e-6f ? Mathf.Clamp01(distance / size) : 0.5f;

        /// <summary>
        /// Move the brush's pivot (in its <see cref="Brush.pivotMode"/>) and its transform with it, so the shape stays where
        /// it is; on a Custom shape the vertices move instead. Children stay where they are too. Undoable.
        /// </summary>
        public static void SetPivot(Brush brush, Vector3 pivot)
        {
            if (!PivotBox(brush, out var size, out var current)) return;
            bool relative = brush.pivotMode == PivotMode.Normalized;
            if (relative) pivot = new Vector3(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y), Mathf.Clamp01(pivot.z));
            else pivot = new Vector3(Mathf.Clamp(pivot.x, 0f, size.x), Mathf.Clamp(pivot.y, 0f, size.y), Mathf.Clamp(pivot.z, 0f, size.z));
            var target = relative ? Vector3.Scale(pivot, size) : pivot;
            var move = target - current; // the new pivot in the old local space, from the old one
            if (move.sqrMagnitude < 1e-12f && (!brush.HasPivot || (brush.pivot - pivot).sqrMagnitude < 1e-12f)) return; // no-op edits must not create undo entries
            var t = brush.transform;
            Undo.RecordObject(brush, "Set brush pivot");
            Undo.RecordObject(t, "Set brush pivot");
            var children = new List<(Transform child, Vector3 position, Quaternion rotation)>();
            foreach (Transform c in t)
                if (!Brush.IsGeneratedChildName(c.name)) { Undo.RecordObject(c, "Set brush pivot"); children.Add((c, c.position, c.rotation)); }
            var world = t.TransformPoint(move);
            if (brush.HasPivot) brush.pivot = pivot;
            else
            {
                var poly = brush.polyhedron.Clone();
                for (int i = 0; i < poly.vertices.Length; i++) poly.vertices[i] -= move;
                brush.polyhedron = poly;
            }
            t.position = world;
            foreach (var (c, position, rotation) in children) c.SetPositionAndRotation(position, rotation);
            BrushSync.NotifyTransformChanged(brush);
            BrushSync.Ensure(brush);
        }

        /// <summary>Measure the pivot as a fraction of the size or as a distance; the pivot itself stays where it is. Undoable.</summary>
        public static void SetPivotMode(Brush brush, PivotMode mode)
        {
            if (brush.pivotMode == mode) return;
            Undo.RecordObject(brush, "Set brush pivot mode");
            bool has = PivotBox(brush, out var size, out var distance) && brush.HasPivot;
            brush.pivotMode = mode;
            if (has) brush.pivot = mode == PivotMode.Normalized ? new Vector3(Fraction(distance.x, size.x), Fraction(distance.y, size.y), Fraction(distance.z, size.z)) : distance;
            BrushSync.Ensure(brush);
        }

        /// <summary>Replace the whole editable shape (tools commit their result through this).</summary>
        public static void SetPolyhedron(Brush brush, BrushPolyhedron polyhedron)
        {
            if (polyhedron == null || !polyhedron.IsValid) return;
            Undo.RecordObject(brush, "Edit brush shape");
            if (brush.shape != BrushShape.Custom) { brush.customFrom = brush.shape; brush.hollow = false; brush.shape = BrushShape.Custom; }
            brush.polyhedron = polyhedron;
            BrushSync.Ensure(brush);
        }

        /// <summary>Push (positive) or pull a face along its normal; the distance is rounded to the grid when snapping is on.</summary>
        public static void PushFace(Brush brush, int face, float distanceMeters)
        {
            if (!ConvertToCustom(brush)) return;
            if (BrushSettings.instance.snapToGrid) distanceMeters = BrushSnap.Round(distanceMeters, BrushSettings.instance.GridMeters);
            if (Mathf.Abs(distanceMeters) < 1e-6f) return;
            Undo.RecordObject(brush, "Push face");
            brush.polyhedron.PushFace(face, distanceMeters);
            BrushSync.Ensure(brush);
        }

        /// <summary>
        /// Extrude the selected faces by a distance (rounded to the grid when snapping is on), each on its own or the
        /// whole selection as one. Runs as a boolean, so it works whatever the block passes through. Returns per
        /// original face index its index afterwards (-1 when gone), or null when nothing was done.
        /// </summary>
        public static int[] ExtrudeFaces(Brush brush, IEnumerable<int> faces, float distanceMeters, bool individual)
        {
            if (!ConvertToCustom(brush)) return null;
            if (BrushSettings.instance.snapToGrid) distanceMeters = BrushSnap.Round(distanceMeters, BrushSettings.instance.GridMeters);
            if (Mathf.Abs(distanceMeters) < 1e-6f) return null;
            var result = BrushBoolean.ExtrudeFaces(brush.polyhedron, faces, distanceMeters, individual, out var remap);
            if (result == null || !result.IsValid) return null;
            Undo.RecordObject(brush, "Extrude faces");
            brush.polyhedron = result;
            BrushSync.Ensure(brush);
            return remap;
        }

        /// <summary>
        /// Bridge two faces of a brush: the volume between them is added, the faces vanish into the join and the
        /// walls become new faces. Returns per original face index its index afterwards (-1 when gone), or null
        /// when nothing was done (<see cref="BrushBoolean.LastRefusal"/> says why).
        /// </summary>
        public static int[] BridgeFaces(Brush brush, int faceA, int faceB)
        {
            if (!ConvertToCustom(brush)) return null;
            var result = BrushBoolean.BridgeFaces(brush.polyhedron, faceA, faceB, out var remap);
            if (result == null || !result.IsValid) return null;
            Undo.RecordObject(brush, "Bridge faces");
            brush.polyhedron = result;
            BrushSync.Ensure(brush);
            return remap;
        }

        /// <summary>Move a vertex to a world position (snapped to the world grid when snapping is on); bent faces split into triangles.</summary>
        public static void MoveVertex(Brush brush, int vertex, Vector3 worldPosition)
        {
            if (!ConvertToCustom(brush)) return;
            if (BrushSettings.instance.snapToGrid) worldPosition = BrushSnap.Round(worldPosition, BrushSettings.instance.GridMeters);
            var local = brush.transform.InverseTransformPoint(worldPosition);
            if ((brush.polyhedron.vertices[vertex] - local).sqrMagnitude < 1e-10f) return;
            Undo.RecordObject(brush, "Move vertex");
            brush.polyhedron.MoveVertex(vertex, local);
            BrushSync.Ensure(brush);
        }

        /// <summary>Apply a non-uniform transform scale to the size and reset the scale to one.</summary>
        public static void ApplyScale(Brush brush)
        {
            var t = brush.transform;
            Undo.RecordObject(brush, "Apply scale to size");
            Undo.RecordObject(t, "Apply scale to size");
            var s = t.localScale;
            brush.size = new Vector3(brush.size.x * Mathf.Abs(s.x), brush.size.y * Mathf.Abs(s.y), brush.size.z * Mathf.Abs(s.z));
            t.localScale = Vector3.one;
            BrushSync.NotifyTransformChanged(brush);
            BrushSync.Ensure(brush);
        }

        /// <summary>Rebuild every model and its convex colliders now, instead of waiting for the editor loop. Used by tests and menus.</summary>
        public static void ForceUpdate()
        {
            BrushSnap.ProcessChanged(); // what the editor loop does every frame
            BrushHooks.Process();
            BrushCsg.RebuildAllNow();
            Colliders.Editor.ConvexColliderBuilder.FlushDeferred();
            BrushSync.ApplyVisibility();
        }
    }
}

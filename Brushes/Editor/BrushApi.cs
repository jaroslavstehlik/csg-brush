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
                brush.stepHeight = d.stepHeight; brush.stepDepth = d.stepDepth;
                brush.innerRadius = d.stairs.innerRadius; brush.stepWidth = d.stairs.stepWidth; brush.stepThickness = d.stairs.stepThickness;
                brush.curveAngle = d.stairs.curveAngle; brush.numSteps = d.stairs.numSteps; brush.stepsPer360 = d.stairs.stepsPer360;
            }
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
            if (BrushSettings.instance.snapToGrid) sizeMeters = BrushSnap.SnapSize(sizeMeters, BrushSettings.instance.GridMeters);
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

        public static void SetSurface(Brush brush, Colliders.ControllerSurface.Kind kind, bool noFallDamage = false)
        {
            if (brush.surface == kind && brush.noFallDamage == noFallDamage) return;
            Undo.RecordObject(brush, "Change brush surface");
            brush.surface = kind;
            brush.noFallDamage = noFallDamage;
            BrushSync.Ensure(brush);
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

        public static void SetStairs(Brush brush, float stepHeightMeters, float stepDepthMeters)
        {
            Undo.RecordObject(brush, "Change stairs");
            brush.stepHeight = stepHeightMeters;
            brush.stepDepth = stepDepthMeters;
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
            brush.size = brush.polyhedron != null && brush.polyhedron.IsValid ? brush.polyhedron.Bounds().size : brush.size;
            brush.shape = brush.customFrom;
            brush.polyhedron = new BrushPolyhedron();
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

        /// <summary>Bake a non-uniform transform scale into the size and reset the scale to one.</summary>
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

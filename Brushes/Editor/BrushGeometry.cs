using System.Collections.Generic;
using CsgBrush.Colliders;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// The geometry of a brush, derived from its fields: the polyhedron of its shape in local space, the inner
    /// polyhedron of a hollow shape, and the convex parts in model space that the collider builder consumes.
    /// </summary>
    public static class BrushGeometry
    {
        /// <summary>The polyhedron of a parametric shape in local space (centred on the transform).</summary>
        public static BrushPolyhedron ShapePolyhedron(BrushShape shape, Vector3 size, int sides, int tessellation, float stepHeight, float stepDepth)
        {
            switch (shape)
            {
                case BrushShape.Wedge: return BrushPolyhedron.Wedge(size);
                case BrushShape.Cylinder: return BrushPolyhedron.Prism(size, sides, 1f);
                case BrushShape.Cone: return BrushPolyhedron.Prism(size, sides, 0f);
                case BrushShape.Sphere: return BrushPolyhedron.Sphere(size, tessellation);
                case BrushShape.Stairs: return BrushPolyhedron.Stairs(size, stepHeight, stepDepth);
                default: return BrushPolyhedron.Box(size);
            }
        }

        /// <summary>The brush's polyhedron in local space; a Custom brush without a valid shape falls back to the shape it came from.</summary>
        public static BrushPolyhedron Polyhedron(Brush b)
        {
            var size = b.ClampedSize;
            if (b.shape == BrushShape.Custom)
            {
                if (b.polyhedron != null && b.polyhedron.IsValid) return b.polyhedron;
                return ShapePolyhedron(b.customFrom, size, b.sides, b.tessellation, b.stepHeight, b.stepDepth);
            }
            return ShapePolyhedron(b.shape, size, b.sides, b.tessellation, b.stepHeight, b.stepDepth);
        }

        /// <summary>The volume removed from a hollow shape, in local space; null when the brush is not hollow.</summary>
        public static BrushPolyhedron HollowInner(Brush b)
        {
            if (!b.IsHollow) return null;
            var size = b.ClampedSize; float t = Mathf.Max(0f, b.wallThickness);
            if (b.shape == BrushShape.Cylinder)
            {
                var inner = new Vector3(Mathf.Max(size.x - 2f * t, 0.001f), size.y * 1.002f, Mathf.Max(size.z - 2f * t, 0.001f));
                return BrushPolyhedron.Prism(inner, b.sides, 1f);
            }
            var innerBox = Vector3.Max(size - Vector3.one * (2f * t), Vector3.one * 0.001f);
            return BrushPolyhedron.Box(innerBox);
        }

        /// <summary>Local-to-world matrix of a brush's geometry: during a drag, the snapped preview pose and size.</summary>
        public static Matrix4x4 LocalToWorld(Brush b)
        {
            if (BrushSnap.TryGetPreview(b, out var preview, out var scale)) return preview * Matrix4x4.Scale(scale);
            return b.transform.localToWorldMatrix;
        }

        /// <summary>Local-to-model matrix of a brush.</summary>
        public static Matrix4x4 ToModel(Brush b, BrushModel model)
        {
            return (model != null ? model.transform.worldToLocalMatrix : Matrix4x4.identity) * LocalToWorld(b);
        }

        /// <summary>Everything that changes the brush's solid (not its operation or surface), including its pose in the model.</summary>
        public static int SolidKey(Brush b, Matrix4x4 toModel)
        {
            unchecked
            {
                int h = Polyhedron(b).ContentHash();
                h = h * 31 + (int)b.shape;
                if (b.IsHollow) { h = h * 31 + 1; h = h * 31 + Mathf.RoundToInt(b.wallThickness * 1e6f); }
                for (int i = 0; i < 16; i++) h = h * 31 + Mathf.RoundToInt(toModel[i] * 1e6f);
                return h;
            }
        }

        /// <summary>A convex polytope (model space) from a convex polyhedron: planes from its faces, vertices rebuilt by clipping.</summary>
        public static ConvexPolytope ToPolytope(BrushPolyhedron poly, float vertexWeld)
        {
            var pt = new ConvexPolytope { vertexWeld = vertexWeld };
            for (int f = 0; f < poly.faces.Length; f++) pt.AddPlane(poly.Plane(f));
            return pt.Build() ? pt : null;
        }

        /// <summary>
        /// The convex parts of a brush in model space: solids that add and (for hollow shapes) the inner solid that is
        /// removed from them. Concave shapes are split into convex pieces. Problems are described, never logged.
        /// </summary>
        public static void ConvexParts(Brush b, Matrix4x4 toModel, List<ConvexPolytope> add, List<ConvexPolytope> remove, ref string problem)
        {
            float weld = Mathf.Max(1e-3f, BrushSettings.instance.GridMeters / 16f); // for decomposition pieces: slivers a hair apart merge
            var local = Polyhedron(b);
            if (local.IsConvex())
            {
                var pt = ToPolytope(local.Transformed(toModel), ConvexPolytope.Epsilon); // exact: a 2 cm ice sheet is still a solid
                if (pt == null) problem = "The shape could not be built.";
                else add.Add(pt);
            }
            else
            {
                var pieces = new List<ConvexPolytope>();
                ConvexDecomposition.VertexWeld = weld;
                ConvexDecomposition.Decompose(local, pieces);
                if (!local.IsSound(out var why)) problem = "Shape is " + why + ".";
                foreach (var piece in pieces)
                {
                    // the decomposition works in local space; move the piece by rebuilding it from its transformed faces
                    var moved = new ConvexPolytope { vertexWeld = weld };
                    var centre = Vector3.zero; foreach (var v in piece.vertices) centre += toModel.MultiplyPoint3x4(v); centre /= Mathf.Max(1, piece.vertices.Count);
                    for (int f = 0; f < piece.faces.Count; f++)
                    {
                        var loop = piece.faces[f];
                        Vector3 n = Vector3.zero, c = Vector3.zero;
                        for (int i = 0; i < loop.Length; i++)
                        {
                            var pa = toModel.MultiplyPoint3x4(piece.vertices[loop[i]]); var pb = toModel.MultiplyPoint3x4(piece.vertices[loop[(i + 1) % loop.Length]]);
                            n.x += (pa.y - pb.y) * (pa.z + pb.z); n.y += (pa.z - pb.z) * (pa.x + pb.x); n.z += (pa.x - pb.x) * (pa.y + pb.y); c += pa;
                        }
                        c /= loop.Length;
                        if (Vector3.Dot(n, c - centre) < 0f) n = -n; // outward, whichever way the piece's faces are wound
                        moved.AddPlane(n, c);
                    }
                    if (moved.Build() && !moved.IsEmpty) add.Add(moved);
                    else problem = "One convex part could not be built and is left out.";
                }
            }
            var inner = HollowInner(b);
            if (inner != null)
            {
                var pt = ToPolytope(inner.Transformed(toModel), ConvexPolytope.Epsilon);
                if (pt != null) remove.Add(pt);
            }
        }
    }
}

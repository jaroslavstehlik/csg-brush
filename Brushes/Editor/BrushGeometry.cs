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
        /// <summary>Everything a parametric shape is built from.</summary>
        public struct ShapeParams
        {
            public Vector3 size;
            public int sides, tessellation;
            public float stepHeight, wallThickness;
            public StairParams stairs;

            public static ShapeParams From(Brush b) => new ShapeParams { size = b.ClampedSize, sides = b.sides, tessellation = b.tessellation, stepHeight = b.stepHeight, wallThickness = b.wallThickness, stairs = b.Stairs };

            /// <summary>Defaults for a shape of a given size (menus, conversions).</summary>
            public static ShapeParams Default(Vector3 size, int sides = 16)
            {
                var s = BrushSettings.instance;
                float stepHeight = BrushSettings.DefaultStepHeightMeters;
                return new ShapeParams
                {
                    size = size, sides = sides, tessellation = 2, stepHeight = stepHeight, wallThickness = s.GridMeters,
                    stairs = new StairParams { innerRadius = s.GridMeters, stepWidth = Mathf.Max(s.GridMeters, size.x - s.GridMeters), stepHeight = stepHeight, stepThickness = BrushSettings.DefaultStepThicknessMeters, curveAngle = 90f, numSteps = BrushPolyhedron.StepCount(size.y, stepHeight), stepsPer360 = 16 },
                };
            }
        }

        /// <summary>The polyhedron of a parametric shape in local space (centred on the transform).</summary>
        public static BrushPolyhedron ShapePolyhedron(BrushShape shape, in ShapeParams p)
        {
            switch (shape)
            {
                case BrushShape.Wedge: return BrushPolyhedron.Wedge(p.size);
                case BrushShape.Cylinder: return BrushPolyhedron.Prism(p.size, p.sides, 1f);
                case BrushShape.Cone: return BrushPolyhedron.Prism(p.size, p.sides, 0f);
                case BrushShape.Sphere: return BrushPolyhedron.Sphere(p.size, p.tessellation);
                case BrushShape.Stairs: return BrushPolyhedron.Stairs(p.size, p.stepHeight);
                case BrushShape.CurvedStairs: return BrushPolyhedron.CurvedStairs(p.stairs);
                case BrushShape.SpiralStairs: return BrushPolyhedron.SpiralStairs(p.stairs);
                case BrushShape.Arch: return BrushPolyhedron.Arch(p.size, p.wallThickness, p.stairs.curveAngle, p.sides);
                default: return BrushPolyhedron.Box(p.size);
            }
        }

        /// <summary>The brush's polyhedron in local space; a Custom brush without a valid shape falls back to the shape it came from.</summary>
        public static BrushPolyhedron Polyhedron(Brush b)
        {
            if (b.shape == BrushShape.Custom)
            {
                if (b.polyhedron != null && b.polyhedron.IsValid) return b.polyhedron;
                return ShapePolyhedron(b.customFrom, ShapeParams.From(b));
            }
            return ShapePolyhedron(b.shape, ShapeParams.From(b));
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
        public static Matrix4x4 ToModel(Brush b, BrushGroup model)
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

        /// <summary>
        /// The convex hull of a few points as a polytope: every plane through three points that keeps all points on one
        /// side. Meant for small sets (a block's eight corners); a twisted quad's two triangle planes would otherwise
        /// cut into the block.
        /// </summary>
        public static ConvexPolytope ConvexHull(IList<Vector3> points)
        {
            var pt = new ConvexPolytope { vertexWeld = ConvexPolytope.Epsilon };
            float scale = 0f; foreach (var p in points) scale = Mathf.Max(scale, p.magnitude);
            float eps = 1e-5f * Mathf.Max(1f, scale);
            var planes = new List<Vector4>();
            int n = points.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    for (int k = j + 1; k < n; k++)
                    {
                        var nrm = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                        if (nrm.sqrMagnitude < 1e-12f) continue;
                        nrm.Normalize();
                        float d = -Vector3.Dot(nrm, points[i]);
                        int above = 0, below = 0;
                        for (int m = 0; m < n; m++) { float dist = Vector3.Dot(nrm, points[m]) + d; if (dist > eps) above++; else if (dist < -eps) below++; }
                        if (above > 0 && below > 0) continue;
                        if (above > 0) { nrm = -nrm; d = -d; } // outward: every point on the negative side
                        bool dup = false;
                        foreach (var q in planes) if (Vector3.Dot(new Vector3(q.x, q.y, q.z), nrm) > 0.99999f && Mathf.Abs(q.w - d) < eps * 10f) { dup = true; break; }
                        if (dup) continue;
                        planes.Add(new Vector4(nrm.x, nrm.y, nrm.z, d));
                    }
            foreach (var q in planes) pt.AddPlane(q);
            return pt.Build() ? pt : null;
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
            bool grouped = false; foreach (var f in local.faces) if (f.group >= 0) { grouped = true; break; }
            if (grouped)
            {
                // shapes made of closed convex blocks (stairs): one polytope per block, straight from its faces
                var moved = local.Transformed(toModel);
                var byGroup = new Dictionary<int, HashSet<int>>();
                for (int f = 0; f < moved.faces.Length; f++)
                {
                    int g = moved.faces[f].group;
                    if (!byGroup.TryGetValue(g, out var set)) byGroup[g] = set = new HashSet<int>();
                    foreach (var i in moved.faces[f].indices) set.Add(i);
                }
                foreach (var set in byGroup.Values)
                {
                    var corners = new List<Vector3>(set.Count); foreach (var i in set) corners.Add(moved.vertices[i]);
                    var pt = ConvexHull(corners); // the hull: a sloped tread is a twisted quad, its triangle planes must not cut into the block
                    if (pt != null && !pt.IsEmpty) add.Add(pt);
                    else problem = "One step could not be built and is left out.";
                }
            }
            else if (local.IsConvex())
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

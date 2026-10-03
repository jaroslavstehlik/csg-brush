using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// The editable shape of a Custom brush: vertices (local metres, centred on the transform) and planar faces
    /// wound counter-clockwise seen from outside. It may be concave; the convex pieces the colliders need are derived
    /// from it (see ConvexDecomposition). Vertex and face indices are stable across edits so selection can
    /// refer to them.
    /// </summary>
    /// <summary>Parameters of the curved and spiral stair generators (metres and degrees), named after Unreal's brush settings.</summary>
    [Serializable]
    public struct StairParams
    {
        public float innerRadius, stepWidth, stepHeight, stepThickness, curveAngle, addToFirstStep;
        public int numSteps, stepsPer360;
        public bool counterClockwise, slopedFloor, slopedCeiling;
        /// <summary>Linear and curved stairs: no support under the steps; each step is a slab of <see cref="stepThickness"/>.</summary>
        public bool open;
    }

    [Serializable]
    public sealed class BrushPolyhedron
    {
        [Serializable]
        public sealed class Face
        {
            public int[] indices;
            /// <summary>Index of the face this one was split from, or -1. Materials and surfaces follow it.</summary>
            public int source = -1;
            /// <summary>Convex block this face belongs to in a shape made of several closed convex blocks (stairs), or -1.</summary>
            public int group = -1;
            public Face() { }
            public Face(int[] indices, int source = -1) { this.indices = indices; this.source = source; }
        }

        public Vector3[] vertices = Array.Empty<Vector3>();
        public Face[] faces = Array.Empty<Face>();

        public const float Epsilon = 1e-4f;

        public bool IsValid => vertices != null && faces != null && vertices.Length >= 4 && faces.Length >= 4;

        public BrushPolyhedron Clone()
        {
            var c = new BrushPolyhedron { vertices = (Vector3[])vertices.Clone(), faces = new Face[faces.Length] };
            for (int i = 0; i < faces.Length; i++) c.faces[i] = new Face((int[])faces[i].indices.Clone(), faces[i].source) { group = faces[i].group };
            return c;
        }

        // ------------------------------------------------------------------ primitives

        public static BrushPolyhedron Box(Vector3 size)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f, z = size.z * 0.5f;
            var p = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(-x, -y, -z), new Vector3( x, -y, -z), new Vector3( x, -y,  z), new Vector3(-x, -y,  z),
                    new Vector3(-x,  y, -z), new Vector3( x,  y, -z), new Vector3( x,  y,  z), new Vector3(-x,  y,  z),
                },
                faces = new[]
                {
                    new Face(new[] { 0, 3, 2, 1 }), // bottom
                    new Face(new[] { 4, 5, 6, 7 }), // top
                    new Face(new[] { 0, 1, 5, 4 }), // back  (-z)
                    new Face(new[] { 2, 3, 7, 6 }), // front (+z)
                    new Face(new[] { 0, 4, 7, 3 }), // left  (-x)
                    new Face(new[] { 1, 2, 6, 5 }), // right (+x)
                }
            };
            p.OrientOutward();
            return p;
        }

        /// <summary>Ramp rising towards +z, extruded along x (same footprint as the parametric wedge).</summary>
        public static BrushPolyhedron Wedge(Vector3 size)
        {
            float x = size.x * 0.5f, y = size.y * 0.5f, z = size.z * 0.5f;
            var p = new BrushPolyhedron
            {
                vertices = new[]
                {
                    new Vector3(-x, -y, -z), new Vector3(-x, -y, z), new Vector3(-x, y, z),
                    new Vector3( x, -y, -z), new Vector3( x, -y, z), new Vector3( x, y, z),
                },
                faces = new[]
                {
                    new Face(new[] { 0, 1, 2 }),       // -x cap
                    new Face(new[] { 3, 5, 4 }),       // +x cap
                    new Face(new[] { 0, 3, 4, 1 }),    // bottom
                    new Face(new[] { 1, 4, 5, 2 }),    // +z wall
                    new Face(new[] { 0, 2, 5, 3 }),    // slope
                }
            };
            p.OrientOutward();
            return p;
        }

        /// <summary>Prism around y with a regular polygon footprint; topScale 0 gives a cone (apex), 1 a cylinder.</summary>
        public static BrushPolyhedron Prism(Vector3 size, int sides, float topScale)
        {
            sides = Mathf.Max(3, sides);
            float y = size.y * 0.5f;
            // fit the polygon to the size box (its flat sides touch the box), like a collider fitted to the box
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                minX = Mathf.Min(minX, Mathf.Cos(a)); maxX = Mathf.Max(maxX, Mathf.Cos(a));
                minZ = Mathf.Min(minZ, Mathf.Sin(a)); maxZ = Mathf.Max(maxZ, Mathf.Sin(a));
            }
            float rx = size.x / (maxX - minX), rz = size.z / (maxZ - minZ);
            float cx = (maxX + minX) * 0.5f * rx, cz = (maxZ + minZ) * 0.5f * rz;
            var verts = new List<Vector3>();
            var faces = new List<Face>();
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                verts.Add(new Vector3(Mathf.Cos(a) * rx - cx, -y, Mathf.Sin(a) * rz - cz));
            }
            var bottom = new int[sides];
            for (int i = 0; i < sides; i++) bottom[i] = sides - 1 - i;
            faces.Add(new Face(bottom));
            if (topScale <= 0.001f)
            {
                verts.Add(new Vector3(0f, y, 0f));
                int apex = sides;
                for (int i = 0; i < sides; i++) faces.Add(new Face(new[] { i, (i + 1) % sides, apex }));
            }
            else
            {
                for (int i = 0; i < sides; i++)
                {
                    float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3((Mathf.Cos(a) * rx - cx) * topScale, y, (Mathf.Sin(a) * rz - cz) * topScale));
                }
                var top = new int[sides];
                for (int i = 0; i < sides; i++) top[i] = sides + i;
                faces.Add(new Face(top));
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    faces.Add(new Face(new[] { i, j, sides + j, sides + i }));
                }
            }
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.OrientOutward();
            return p;
        }

        /// <summary>Ellipsoid fitted to the size box: (4 + 4 t) segments around y, (2 + 2 t) rings from pole to pole. Convex.</summary>
        public static BrushPolyhedron Sphere(Vector3 size, int tessellation)
        {
            int t = Mathf.Clamp(tessellation, 1, 5);
            int segments = 4 + 4 * t, rings = 2 + 2 * t; // rings = number of latitude bands
            float rx = size.x * 0.5f, ry = size.y * 0.5f, rz = size.z * 0.5f;
            var verts = new List<Vector3> { new Vector3(0f, ry, 0f) };
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int sgm = 0; sgm < segments; sgm++)
                {
                    float theta = Mathf.PI * 2f * (sgm + 0.5f) / segments;
                    verts.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta) * rx, Mathf.Cos(phi) * ry, Mathf.Sin(phi) * Mathf.Sin(theta) * rz));
                }
            }
            verts.Add(new Vector3(0f, -ry, 0f));
            int top = 0, bottom = verts.Count - 1;
            int Ring(int r, int sgm) => 1 + (r - 1) * segments + (sgm % segments);
            var faces = new List<Face>();
            for (int sgm = 0; sgm < segments; sgm++) faces.Add(new Face(new[] { top, Ring(1, sgm + 1), Ring(1, sgm) }));
            for (int r = 1; r < rings - 1; r++)
                for (int sgm = 0; sgm < segments; sgm++)
                    faces.Add(new Face(new[] { Ring(r, sgm), Ring(r, sgm + 1), Ring(r + 1, sgm + 1), Ring(r + 1, sgm) }));
            for (int sgm = 0; sgm < segments; sgm++) faces.Add(new Face(new[] { bottom, Ring(rings - 1, sgm), Ring(rings - 1, sgm + 1) }));
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.OrientOutward();
            return p;
        }

        /// <summary>
        /// Linear stairs climbing towards +z and filling the size box: as many steps as the step height fits into the
        /// height (rounded), so each step rises height / steps and runs length / steps. Risers face -z, the back is a
        /// solid wall. Concave; the side walls are one quad per step so every face stays convex.
        /// </summary>
        /// <summary>
        /// Linear stairs with no support under them: one slab per step, its tread where the solid stairs' tread is and
        /// <paramref name="thickness"/> deep (never below the box's floor). Each slab is its own closed block (Face.group).
        /// </summary>
        public static BrushPolyhedron OpenStairs(Vector3 size, float stepHeight, float thickness)
        {
            float w = size.x * 0.5f, h = size.y * 0.5f, d = size.z * 0.5f;
            int steps = StepCount(size.y, stepHeight);
            stepHeight = size.y / steps;
            float stepDepth = size.z / steps, th = Mathf.Max(0.001f, thickness);
            var verts = new List<Vector3>(); var faces = new List<Face>();
            for (int k = 0; k < steps; k++)
            {
                float top = Mathf.Min(h, -h + (k + 1) * stepHeight), bottom = Mathf.Max(-h, top - th);
                float z0 = -d + k * stepDepth, z1 = Mathf.Min(d, z0 + stepDepth);
                int b = verts.Count;
                verts.Add(new Vector3(-w, bottom, z0)); verts.Add(new Vector3(w, bottom, z0)); verts.Add(new Vector3(w, bottom, z1)); verts.Add(new Vector3(-w, bottom, z1));
                verts.Add(new Vector3(-w, top, z0)); verts.Add(new Vector3(w, top, z0)); verts.Add(new Vector3(w, top, z1)); verts.Add(new Vector3(-w, top, z1));
                AddBlock(verts, faces, k, new[] { b, b + 1, b + 2, b + 3 }, new[] { b + 4, b + 5, b + 6, b + 7 });
            }
            return new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
        }

        public static BrushPolyhedron Stairs(Vector3 size, float stepHeight)
        {
            float w = size.x * 0.5f, h = size.y * 0.5f, d = size.z * 0.5f;
            int steps = StepCount(size.y, stepHeight);
            stepHeight = size.y / steps;
            float stepDepth = size.z / steps;
            var verts = new List<Vector3>(); var lookup = new Dictionary<Vector3, int>();
            int V(float x, float y, float z)
            {
                var v = new Vector3(Mathf.Round(x * 1e5f) / 1e5f, Mathf.Round(y * 1e5f) / 1e5f, Mathf.Round(z * 1e5f) / 1e5f);
                if (!lookup.TryGetValue(v, out int i)) { i = verts.Count; verts.Add(v); lookup[v] = i; }
                return i;
            }
            var faces = new List<Face>();
            void Add(Vector3 outward, params int[] idx)
            {
                // wind so the Newell normal points the intended way
                Vector3 n = Vector3.zero;
                for (int i = 0; i < idx.Length; i++) { var a = verts[idx[i]]; var b = verts[idx[(i + 1) % idx.Length]]; n.x += (a.y - b.y) * (a.z + b.z); n.y += (a.z - b.z) * (a.x + b.x); n.z += (a.x - b.x) * (a.y + b.y); }
                if (Vector3.Dot(n, outward) < 0f) Array.Reverse(idx);
                faces.Add(new Face(idx));
            }
            // the top of every step (capped at the box top: once the stairs reach it the remaining steps are flat)
            var tops = new List<float>(); var zs = new List<float> { -d };
            for (int k = 0; k < steps; k++)
            {
                tops.Add(Mathf.Min(h, -h + (k + 1) * stepHeight));
                float z1 = Mathf.Min(d, -d + (k + 1) * stepDepth);
                zs.Add(z1);
                if (z1 >= d - 1e-6f) break;
            }
            int n = zs.Count - 1;
            // distinct wall levels: every side wall and the back wall are split at all of them, so shared edges match exactly
            var levels = new List<float> { -h };
            foreach (var t in tops) if (t > levels[levels.Count - 1] + 1e-6f) levels.Add(t);
            for (int k = 0; k < n; k++)
            {
                float z0 = zs[k], z1 = zs[k + 1], yPrev = k > 0 ? tops[k - 1] : -h, yTop = tops[k];
                if (yTop > yPrev + 1e-6f) Add(Vector3.back, V(-w, yPrev, z0), V(w, yPrev, z0), V(w, yTop, z0), V(-w, yTop, z0)); // riser
                Add(Vector3.up, V(-w, yTop, z0), V(w, yTop, z0), V(w, yTop, z1), V(-w, yTop, z1));       // tread
                Add(Vector3.down, V(-w, -h, z0), V(w, -h, z0), V(w, -h, z1), V(-w, -h, z1));             // bottom strip
                for (int l = 0; l + 1 < levels.Count && levels[l + 1] <= yTop + 1e-6f; l++)
                {
                    float ya = levels[l], yb = levels[l + 1];
                    Add(Vector3.left, V(-w, ya, z0), V(-w, ya, z1), V(-w, yb, z1), V(-w, yb, z0));
                    Add(Vector3.right, V(w, ya, z0), V(w, ya, z1), V(w, yb, z1), V(w, yb, z0));
                }
            }
            float zBack = zs[n], yBack = tops[n - 1];
            for (int l = 0; l + 1 < levels.Count && levels[l + 1] <= yBack + 1e-6f; l++)
                Add(Vector3.forward, V(-w, levels[l], zBack), V(w, levels[l], zBack), V(w, levels[l + 1], zBack), V(-w, levels[l + 1], zBack));
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            p.EnsureOutward();
            return p;
        }

        /// <summary>The widest angle one block of a curved stair covers: 32 per full turn.</summary>
        public const float MaxArcSegmentDegrees = 360f / 32f;

        /// <summary>How many steps of about <paramref name="stepHeight"/> fill <paramref name="height"/>: at least one.</summary>
        public static int StepCount(float height, float stepHeight) => Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(height) / Mathf.Max(0.001f, stepHeight)));

        /// <summary>
        /// Curved stairs (Unreal's Curved Stair): steps wrapping around an inner column over an angle, each step a solid
        /// block from the floor to its tread. A step wider than <see cref="MaxArcSegmentDegrees"/> is made of several
        /// blocks, so the footprint follows the curve the same way whatever the number of steps (a few tall steps would
        /// otherwise cut straight across it). Every block is closed and convex (its own Face.group) and touches its
        /// neighbours. The column axis is the local y axis and the floor is y = 0: the transform is the axis.
        /// </summary>
        public static BrushPolyhedron CurvedStairs(StairParams p)
        {
            int n = Mathf.Max(1, p.numSteps);
            float a = Mathf.Max(1f, p.curveAngle) * Mathf.Deg2Rad / n * (p.counterClockwise ? 1f : -1f);
            int sub = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(a) * Mathf.Rad2Deg / MaxArcSegmentDegrees - 1e-4f));
            float ri = Mathf.Max(0f, p.innerRadius), ro = ri + Mathf.Max(0.001f, p.stepWidth), h = Mathf.Max(0.001f, p.stepHeight);
            var verts = new List<Vector3>(); var faces = new List<Face>();
            int group = 0;
            for (int k = 0; k < n; k++)
            {
                float yTop = p.addToFirstStep + (k + 1) * h;
                if (yTop <= 0.001f) yTop = 0.001f;
                for (int j = 0; j < sub; j++)
                {
                    float t0 = (k + (float)j / sub) * a, t1 = (k + (float)(j + 1) / sub) * a;
                    int b0 = verts.Count;
                    verts.Add(new Vector3(ri * Mathf.Cos(t0), 0f, ri * Mathf.Sin(t0))); verts.Add(new Vector3(ro * Mathf.Cos(t0), 0f, ro * Mathf.Sin(t0)));
                    verts.Add(new Vector3(ro * Mathf.Cos(t1), 0f, ro * Mathf.Sin(t1))); verts.Add(new Vector3(ri * Mathf.Cos(t1), 0f, ri * Mathf.Sin(t1)));
                    for (int i = 0; i < 4; i++) verts.Add(new Vector3(verts[b0 + i].x, yTop, verts[b0 + i].z));
                    if (p.open) // a slab under the tread instead of a block from the floor
                    {
                        float yBottom = Mathf.Max(0f, yTop - Mathf.Max(0.001f, p.stepThickness));
                        for (int i = 0; i < 4; i++) verts[b0 + i] = new Vector3(verts[b0 + i].x, yBottom, verts[b0 + i].z);
                    }
                    AddBlock(verts, faces, group++, new[] { b0, b0 + 1, b0 + 2, b0 + 3 }, new[] { b0 + 4, b0 + 5, b0 + 6, b0 + 7 });
                }
            }
            if (p.open) return new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() }; // the floor stays at y = 0 below the first slab
            return OnFloor(verts, faces);
        }

        /// <summary>
        /// Spiral stairs (Unreal's Spiral Stair): separate step slabs wrapping around an inner column, any number of
        /// turns; sloped floor and ceiling turn the steps into a ramp. Each slab is its own closed block (Face.group).
        /// The column axis is the local y axis and the floor is y = 0, so the transform is the axis at floor level. Tread k
        /// is at (k + 1) step heights, as on every other stair: the thickness only reaches down from the tread (never
        /// below the floor), so it never changes the step height a character climbs.
        /// </summary>
        public static BrushPolyhedron SpiralStairs(StairParams p)
        {
            int n = Mathf.Max(1, p.numSteps);
            float a = Mathf.PI * 2f / Mathf.Max(1, p.stepsPer360) * (p.counterClockwise ? 1f : -1f);
            float ri = Mathf.Max(0f, p.innerRadius), ro = ri + Mathf.Max(0.001f, p.stepWidth), h = Mathf.Max(0.001f, p.stepHeight), th = Mathf.Max(0.001f, p.stepThickness);
            var verts = new List<Vector3>(); var faces = new List<Face>();
            for (int k = 0; k < n; k++)
            {
                float t0 = k * a, t1 = (k + 1) * a;
                float yTop1 = p.addToFirstStep + (k + 1) * h, yTop0 = p.slopedFloor ? yTop1 - h : yTop1;
                float yBot1 = yTop1 - th, yBot0 = p.slopedCeiling ? yTop0 - th : (p.slopedFloor ? yTop1 - th : yTop1 - th);
                if (!p.slopedFloor && p.slopedCeiling) yBot0 = yBot1 - h; // ceiling slopes under a flat tread
                int b = verts.Count;
                verts.Add(new Vector3(ri * Mathf.Cos(t0), yBot0, ri * Mathf.Sin(t0))); verts.Add(new Vector3(ro * Mathf.Cos(t0), yBot0, ro * Mathf.Sin(t0)));
                verts.Add(new Vector3(ro * Mathf.Cos(t1), yBot1, ro * Mathf.Sin(t1))); verts.Add(new Vector3(ri * Mathf.Cos(t1), yBot1, ri * Mathf.Sin(t1)));
                verts.Add(new Vector3(ri * Mathf.Cos(t0), yTop0, ri * Mathf.Sin(t0))); verts.Add(new Vector3(ro * Mathf.Cos(t0), yTop0, ro * Mathf.Sin(t0)));
                verts.Add(new Vector3(ro * Mathf.Cos(t1), yTop1, ro * Mathf.Sin(t1))); verts.Add(new Vector3(ri * Mathf.Cos(t1), yTop1, ri * Mathf.Sin(t1)));
                for (int i = b; i < b + 8; i++) if (verts[i].y < 0f) verts[i] = new Vector3(verts[i].x, 0f, verts[i].z); // a sloped first step stops at the floor
                AddBlock(verts, faces, k, new[] { b, b + 1, b + 2, b + 3 }, new[] { b + 4, b + 5, b + 6, b + 7 }, splitCaps: p.slopedFloor || p.slopedCeiling);
            }
            return new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
        }

        /// <summary>
        /// An arch filling its box (Unreal's Arch): the ring between an outer ellipse (half the width, the full height)
        /// and an inner one a thickness in, standing on the box's floor, over an angle (180 is a full arch; less
        /// keeps the top part) in a number of segments. Each segment is its own closed convex block (Face.group), so
        /// the mesh is closed by construction and the colliders are one hull per segment. Centred on the transform.
        /// </summary>
        public static BrushPolyhedron Arch(Vector3 size, float thickness, float angleDegrees, int segments)
        {
            int n = Mathf.Max(1, segments);
            float a = Mathf.Max(0.001f, size.x * 0.5f), b = Mathf.Max(0.001f, size.y), d = Mathf.Max(0.001f, size.z);
            float t = Mathf.Clamp(thickness, 0.001f, Mathf.Min(a, b) - 0.0005f);
            float angle = Mathf.Clamp(angleDegrees, 1f, 180f) * Mathf.Deg2Rad;
            float t0 = Mathf.PI * 0.5f + angle * 0.5f, step = -angle / n; // from the left end over the top to the right
            float y0 = -size.y * 0.5f;
            var verts = new List<Vector3>(); var faces = new List<Face>();
            for (int k = 0; k < n; k++)
            {
                float u0 = t0 + k * step, u1 = t0 + (k + 1) * step;
                int v = verts.Count;
                Vector2 o0 = new Vector2(a * Mathf.Cos(u0), b * Mathf.Sin(u0)), o1 = new Vector2(a * Mathf.Cos(u1), b * Mathf.Sin(u1));
                Vector2 i0 = new Vector2((a - t) * Mathf.Cos(u0), (b - t) * Mathf.Sin(u0)), i1 = new Vector2((a - t) * Mathf.Cos(u1), (b - t) * Mathf.Sin(u1));
                foreach (var z in new[] { -d * 0.5f, d * 0.5f })
                {
                    verts.Add(new Vector3(i0.x, y0 + i0.y, z)); verts.Add(new Vector3(o0.x, y0 + o0.y, z));
                    verts.Add(new Vector3(o1.x, y0 + o1.y, z)); verts.Add(new Vector3(i1.x, y0 + i1.y, z));
                }
                AddBlock(verts, faces, k, new[] { v, v + 1, v + 2, v + 3 }, new[] { v + 4, v + 5, v + 6, v + 7 });
            }
            return new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
        }


        /// <summary>The shape with its lowest point at y = 0: the transform is the axis at floor level whatever the first step does.</summary>
        static BrushPolyhedron OnFloor(List<Vector3> verts, List<Face> faces)
        {
            var p = new BrushPolyhedron { vertices = verts.ToArray(), faces = faces.ToArray() };
            float minY = p.Bounds().min.y;
            if (Mathf.Abs(minY) > 1e-6f) for (int i = 0; i < p.vertices.Length; i++) p.vertices[i].y -= minY;
            return p;
        }

        /// <summary>
        /// A closed block from a bottom and a top quad (same corner order). Faces are wound combinatorially, so every
        /// edge is shared by exactly two faces whatever the geometry; the whole block is flipped when its signed volume
        /// comes out negative. Caps are split into triangles when they are not planar (sloped treads).
        /// </summary>
        static void AddBlock(List<Vector3> verts, List<Face> faces, int group, int[] bottom, int[] top, bool splitCaps = false)
        {
            var block = new List<int[]>();
            if (splitCaps)
            {
                block.Add(new[] { bottom[0], bottom[3], bottom[2] }); block.Add(new[] { bottom[0], bottom[2], bottom[1] });
                block.Add(new[] { top[0], top[1], top[2] }); block.Add(new[] { top[0], top[2], top[3] });
            }
            else { block.Add(new[] { bottom[0], bottom[3], bottom[2], bottom[1] }); block.Add(new[] { top[0], top[1], top[2], top[3] }); }
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                block.Add(new[] { bottom[i], bottom[j], top[j], top[i] });
            }
            double volume = 0;
            foreach (var idx in block)
            {
                var a = verts[idx[0]];
                for (int i = 1; i + 1 < idx.Length; i++) volume += Vector3.Dot(a, Vector3.Cross(verts[idx[i]], verts[idx[i + 1]]));
            }
            foreach (var idx in block)
            {
                if (volume < 0) Array.Reverse(idx);
                faces.Add(new Face(idx) { group = group });
            }
        }

        /// <summary>A copy with every vertex transformed.</summary>
        public BrushPolyhedron Transformed(Matrix4x4 m)
        {
            var c = Clone();
            for (int i = 0; i < c.vertices.Length; i++) c.vertices[i] = m.MultiplyPoint3x4(c.vertices[i]);
            if (m.determinant < 0f) c.InvertFaces(); // a mirroring transform turns the faces inside out
            return c;
        }

        /// <summary>Content hash of the shape (vertices rounded to a micron, faces).</summary>
        public int ContentHash()
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < vertices.Length; i++) { var v = vertices[i]; h = h * 31 + Mathf.RoundToInt(v.x * 1e6f); h = h * 31 + Mathf.RoundToInt(v.y * 1e6f); h = h * 31 + Mathf.RoundToInt(v.z * 1e6f); }
                for (int f = 0; f < faces.Length; f++) { h = h * 31 + faces[f].indices.Length; foreach (var i in faces[f].indices) h = h * 31 + i; }
                return h;
            }
        }

        // ------------------------------------------------------------------ geometry

        public Vector3 Centroid()
        {
            var c = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++) c += vertices[i];
            return vertices.Length > 0 ? c / vertices.Length : c;
        }

        /// <summary>Plane of a face (Newell normal, outward for counter-clockwise faces), as (n.xyz, d) with n·p + d = 0.</summary>
        public Vector4 Plane(int face)
        {
            var idx = faces[face].indices;
            Vector3 n = Vector3.zero;
            Vector3 c = Vector3.zero;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = vertices[idx[i]]; var b = vertices[idx[(i + 1) % idx.Length]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
                c += a;
            }
            c /= idx.Length;
            n.Normalize();
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, c));
        }

        public static float Distance(Vector4 plane, Vector3 p) => plane.x * p.x + plane.y * p.y + plane.z * p.z + plane.w;

        public Vector3 FaceCentre(int face)
        {
            var idx = faces[face].indices; var c = Vector3.zero;
            for (int i = 0; i < idx.Length; i++) c += vertices[idx[i]];
            return c / idx.Length;
        }

        /// <summary>Flip every face whose normal points towards the centroid. Correct for primitives and star shapes.</summary>
        public void OrientOutward()
        {
            var c = Centroid();
            for (int f = 0; f < faces.Length; f++)
            {
                if (Distance(Plane(f), c) > Epsilon)
                    Array.Reverse(faces[f].indices);
            }
        }

        /// <summary>Reverse every face. Used to fix a consistently inside-out shape (negative volume).</summary>
        public void InvertFaces()
        {
            foreach (var f in faces) Array.Reverse(f.indices);
        }

        /// <summary>Outward faces have positive volume; a consistently inside-out shape is inverted.</summary>
        public void EnsureOutward()
        {
            if (Volume() < 0f) InvertFaces();
        }

        public Bounds Bounds()
        {
            if (vertices.Length == 0) return new Bounds();
            var b = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Length; i++) b.Encapsulate(vertices[i]);
            return b;
        }

        /// <summary>Signed volume by the divergence theorem; positive for outward faces.</summary>
        public float Volume()
        {
            double v = 0;
            for (int f = 0; f < faces.Length; f++)
            {
                var idx = faces[f].indices;
                var a = vertices[idx[0]];
                for (int i = 1; i + 1 < idx.Length; i++)
                    v += Vector3.Dot(a, Vector3.Cross(vertices[idx[i]], vertices[idx[i + 1]]));
            }
            return (float)(v / 6.0);
        }

        /// <summary>
        /// Closed: every directed edge a-b is matched by a b-a. Usually that is two faces per edge; two parts of one
        /// solid touching along an edge (two extruded blocks meeting at a corner) give four, still balanced.
        /// </summary>
        public bool IsClosed()
        {
            if (!IsValid) return false;
            var balance = new Dictionary<long, int>();
            foreach (var face in faces)
            {
                var idx = face.indices;
                if (idx.Length < 3) return false;
                for (int i = 0; i < idx.Length; i++)
                {
                    int a = idx[i], b = idx[(i + 1) % idx.Length];
                    if (a == b || a < 0 || b < 0 || a >= vertices.Length || b >= vertices.Length) return false;
                    long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                    balance[key] = balance.TryGetValue(key, out var n) ? n + (a < b ? 1 : -1) : (a < b ? 1 : -1);
                }
            }
            foreach (var kv in balance) if (kv.Value != 0) return false;
            return true;
        }

        /// <summary>Max distance of any face vertex from its plane.</summary>
        public float PlanarityError(int face)
        {
            var plane = Plane(face); float worst = 0f;
            foreach (var i in faces[face].indices) worst = Mathf.Max(worst, Mathf.Abs(Distance(plane, vertices[i])));
            return worst;
        }

        /// <summary>Edges (as vertex pairs) where the solid bends outwards: the neighbouring face lies in front of the face plane.</summary>
        public List<(int a, int b, int faceA, int faceB)> ReflexEdges()
        {
            var result = new List<(int, int, int, int)>();
            var owner = new Dictionary<long, int>();
            for (int f = 0; f < faces.Length; f++)
            {
                var idx = faces[f].indices;
                for (int i = 0; i < idx.Length; i++)
                    owner[((long)idx[i] << 32) | (uint)idx[(i + 1) % idx.Length]] = f;
            }
            var planes = new Vector4[faces.Length];
            for (int f = 0; f < faces.Length; f++) planes[f] = Plane(f);
            var seen = new HashSet<long>();
            foreach (var kv in owner)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffffL);
                long undirected = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!seen.Add(undirected)) continue;
                if (!owner.TryGetValue(((long)b << 32) | (uint)a, out int g)) continue;
                int f = kv.Value;
                bool reflex = false;
                foreach (var v in faces[g].indices)
                {
                    if (v == a || v == b) continue;
                    if (Distance(planes[f], vertices[v]) > Epsilon * 10f) { reflex = true; break; }
                }
                if (reflex) result.Add((a, b, f, g));
            }
            return result;
        }

        public bool IsConvex() => ReflexEdges().Count == 0;

        /// <summary>
        /// A shape the decomposition can represent: closed, positive volume, no degenerate faces, and its convex
        /// parts fill exactly its volume (a self-intersecting or locally inverted shape fails that last test).
        /// </summary>
        public bool IsSound(out string reason)
        {
            reason = null;
            if (!IsValid) { reason = "not enough vertices or faces"; return false; }
            if (!IsClosed()) { reason = "open"; return false; }
            float volume = Volume();
            if (volume <= 1e-5f) { reason = "no volume"; return false; }
            for (int f = 0; f < faces.Length; f++)
            {
                if (faces[f].indices.Length < 3) { reason = "degenerate face"; return false; }
                if (PlanarityError(f) > Epsilon * 10f) { reason = "non-planar face"; return false; }
            }
            for (int f = 0; f < faces.Length; f++) if (!FaceIsSimple(f)) { reason = "self-intersecting"; return false; } // a bow-tie face, whatever the rest looks like
            if (!IsConvex() && SelfIntersects()) { reason = "self-intersecting"; return false; } // a closed convex shape cannot self-intersect
            return true;
        }

        /// <summary>A face polygon is simple when no two of its non-adjacent edges cross (in its plane).</summary>
        public bool FaceIsSimple(int face)
        {
            var idx = faces[face].indices; int n = idx.Length;
            if (n < 4) return true;
            // a bow-tie's Newell normal cancels out, so take the plane from the three consecutive corners that span
            // the most (a nearly collinear triple would give a plane the polygon does not lie in)
            Vector3 nrm = Vector3.zero; float longest = 0f;
            for (int i = 0; i < n; i++)
            {
                var c = Vector3.Cross(vertices[idx[(i + 1) % n]] - vertices[idx[i]], vertices[idx[(i + 2) % n]] - vertices[idx[i]]);
                if (c.sqrMagnitude > nrm.sqrMagnitude) nrm = c;
                longest = Mathf.Max(longest, (vertices[idx[(i + 1) % n]] - vertices[idx[i]]).sqrMagnitude);
            }
            if (nrm.sqrMagnitude < 1e-12f * longest * longest) return false; // every corner collinear (relative to the polygon's size): no polygon at all
            nrm.Normalize();
            var u = Vector3.Cross(nrm, Mathf.Abs(nrm.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(nrm, u);
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++) pts[i] = new Vector2(Vector3.Dot(vertices[idx[i]], u), Vector3.Dot(vertices[idx[i]], w));
            for (int i = 0; i < n; i++)
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // adjacent around the loop
                    var a = pts[i]; var b = pts[(i + 1) % n]; var c = pts[j]; var d = pts[(j + 1) % n];
                    float s1 = Cross2(a, b, c), s2 = Cross2(a, b, d), s3 = Cross2(c, d, a), s4 = Cross2(c, d, b);
                    if (s1 * s2 < 0f && s3 * s4 < 0f) return false;
                }
            return true;
        }

        static float Cross2(Vector2 p, Vector2 q, Vector2 x) => (q.x - p.x) * (x.y - p.y) - (q.y - p.y) * (x.x - p.x);

        /// <summary>Which pair the last SelfIntersects found, for diagnostics.</summary>
        public static string LastIntersection;

        /// <summary>
        /// Exact test: do two non-coplanar faces cross? The faces' planes meet in a line; where that line runs inside
        /// both polygons for more than a point, and that stretch is inside at least one of them rather than along
        /// both boundaries (the case of two faces sharing an edge), the faces intersect. Pairs whose bounds do not
        /// overlap are skipped. Coplanar overlaps are not detected.
        /// </summary>
        public bool SelfIntersects()
        {
            LastIntersection = null;
            int n = faces.Length;
            var planes = new Vector4[n]; var mins = new Vector3[n]; var maxs = new Vector3[n];
            // tolerance above the noise a boolean rebuild leaves (a 0.5 mm weld): a crossing thinner than that is not one
            float scale = Bounds().size.magnitude; float eps = Mathf.Max(2e-3f, scale * 1e-5f);
            for (int f = 0; f < n; f++)
            {
                planes[f] = Plane(f);
                var idx = faces[f].indices; mins[f] = maxs[f] = vertices[idx[0]];
                foreach (var i in idx) { mins[f] = Vector3.Min(mins[f], vertices[i]); maxs[f] = Vector3.Max(maxs[f], vertices[i]); }
            }
            var spansF = new List<float>(); var spansG = new List<float>();
            for (int f = 0; f < n; f++)
                for (int g = f + 1; g < n; g++)
                {
                    if (mins[f].x > maxs[g].x + eps || maxs[f].x < mins[g].x - eps || mins[f].y > maxs[g].y + eps || maxs[f].y < mins[g].y - eps || mins[f].z > maxs[g].z + eps || maxs[f].z < mins[g].z - eps) continue;
                    var nf = new Vector3(planes[f].x, planes[f].y, planes[f].z); var ng = new Vector3(planes[g].x, planes[g].y, planes[g].z);
                    var dir = Vector3.Cross(nf, ng);
                    if (dir.sqrMagnitude < 1e-10f) continue; // parallel planes
                    if (WithinPlane(g, planes[f], eps * 2f) || WithinPlane(f, planes[g], eps * 2f)) continue; // as good as coplanar: the line of two such planes is noise
                    dir.Normalize();
                    // a point on both planes: solve in the plane spanned by the two normals
                    float d1 = -planes[f].w, d2 = -planes[g].w, dot = Vector3.Dot(nf, ng), det = 1f - dot * dot;
                    var origin = ((d1 - d2 * dot) * nf + (d2 - d1 * dot) * ng) / det;
                    if (!InsideSpans(f, origin, dir, spansF, eps) || !InsideSpans(g, origin, dir, spansG, eps)) continue;
                    for (int i = 0; i + 1 < spansF.Count; i += 2)
                        for (int j = 0; j + 1 < spansG.Count; j += 2)
                        {
                            float lo = Mathf.Max(spansF[i], spansG[j]), hi = Mathf.Min(spansF[i + 1], spansG[j + 1]);
                            if (hi - lo <= eps * 4f) continue;
                            var mid = origin + dir * ((lo + hi) * 0.5f);
                            bool inF = StrictlyInside(f, mid, planes[f], eps), inG = StrictlyInside(g, mid, planes[g], eps);
                            if (inF || inG)
                            {
                                var sbi = new System.Text.StringBuilder();
                                sbi.Append("faces " + f + " [" + string.Join(",", faces[f].indices) + "] and " + g + " [" + string.Join(",", faces[g].indices) + "] cross at " + mid.ToString("F3") + " span " + (hi - lo).ToString("F4") + (inF ? " inside " + f : "") + (inG ? " inside " + g : ""));
                                sbi.Append("; f verts:"); foreach (var vi in faces[f].indices) sbi.Append(" " + vertices[vi].ToString("F3"));
                                sbi.Append("; g verts:"); foreach (var vi in faces[g].indices) sbi.Append(" " + vertices[vi].ToString("F3"));
                                sbi.Append("; line origin " + origin.ToString("F3") + " dir " + dir.ToString("F3") + "; spansF"); foreach (var sp in spansF) sbi.Append(" " + sp.ToString("F3"));
                                sbi.Append("; spansG"); foreach (var sp in spansG) sbi.Append(" " + sp.ToString("F3"));
                                LastIntersection = sbi.ToString(); return true;
                            }
                        }
                }
            return false;
        }

        /// <summary>Every corner of a face lies within a distance of a plane.</summary>
        bool WithinPlane(int face, Vector4 plane, float distance)
        {
            foreach (var i in faces[face].indices) if (Mathf.Abs(Distance(plane, vertices[i])) > distance) return false;
            return true;
        }

        /// <summary>Parameter spans along a line (in the face's plane) where the line is inside the polygon: pairs of t values. False when the line misses it.</summary>
        bool InsideSpans(int face, Vector3 origin, Vector3 dir, List<float> spans, float eps)
        {
            spans.Clear();
            var plane = Plane(face); var nrm = new Vector3(plane.x, plane.y, plane.z);
            var side = Vector3.Cross(nrm, dir); // in-plane normal of the line
            var idx = faces[face].indices;
            var crossings = new List<float>();
            for (int i = 0; i < idx.Length; i++)
            {
                var a = vertices[idx[i]]; var b = vertices[idx[(i + 1) % idx.Length]];
                float sa = Vector3.Dot(a - origin, side), sb = Vector3.Dot(b - origin, side);
                if (Mathf.Abs(sa) < eps) sa = 0f; if (Mathf.Abs(sb) < eps) sb = 0f; // on the line is exactly on the line: no phantom crossing between two such endpoints
                if ((sa >= 0f) == (sb >= 0f)) continue; // half-open rule: an endpoint on the line counts once
                var p = Vector3.Lerp(a, b, sa / (sa - sb));
                crossings.Add(Vector3.Dot(p - origin, dir));
            }
            if (crossings.Count < 2) return false;
            crossings.Sort();
            for (int i = 0; i + 1 < crossings.Count; i += 2) { spans.Add(crossings[i]); spans.Add(crossings[i + 1]); }
            return spans.Count > 0;
        }

        /// <summary>Point on the plane of a face, strictly inside its polygon (not on the boundary).</summary>
        bool StrictlyInside(int face, Vector3 p, Vector4 plane, float eps)
        {
            var n = new Vector3(plane.x, plane.y, plane.z);
            var u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(n, u);
            var idx = faces[face].indices;
            float px = Vector3.Dot(p, u), py = Vector3.Dot(p, w); int winding = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = vertices[idx[i]]; var b = vertices[idx[(i + 1) % idx.Length]];
                float ax = Vector3.Dot(a, u) - px, ay = Vector3.Dot(a, w) - py, bx = Vector3.Dot(b, u) - px, by = Vector3.Dot(b, w) - py;
                // distance from the point to the edge: on the boundary is not inside
                float ex = bx - ax, ey = by - ay, len2 = ex * ex + ey * ey;
                float t = len2 > 0f ? Mathf.Clamp01(-(ax * ex + ay * ey) / len2) : 0f;
                float dx = ax + ex * t, dy = ay + ey * t;
                if (dx * dx + dy * dy < eps * eps) return false;
                if (ay <= 0f) { if (by > 0f && ax * by - bx * ay > 0f) winding++; }
                else if (by <= 0f && ax * by - bx * ay < 0f) winding--;
            }
            return winding != 0;
        }

        /// <summary>Undirected edges as vertex pairs (a &lt; b), each once.</summary>
        public List<(int a, int b)> Edges()
        {
            var set = new HashSet<long>(); var list = new List<(int, int)>();
            foreach (var face in faces)
            {
                var idx = face.indices;
                for (int i = 0; i < idx.Length; i++)
                {
                    int a = idx[i], b = idx[(i + 1) % idx.Length];
                    int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                    if (set.Add(((long)lo << 32) | (uint)hi)) list.Add((lo, hi));
                }
            }
            return list;
        }

        /// <summary>All vertices used by the given faces.</summary>
        public HashSet<int> VerticesOfFaces(IEnumerable<int> faceIndices)
        {
            var set = new HashSet<int>();
            foreach (var f in faceIndices) foreach (var i in faces[f].indices) set.Add(i);
            return set;
        }

        // ------------------------------------------------------------------ edits

        /// <summary>Move a set of vertices by one offset; faces that bend because only some of their vertices moved are split.</summary>
        public void MoveVertices(IEnumerable<int> vertexIndices, Vector3 delta)
        {
            var moved = new List<int>(vertexIndices);
            foreach (var v in moved) vertices[v] += delta;
            foreach (var v in moved) EnsurePlanar(v);
        }

        /// <summary>
        /// Vertices that coincide are merged and degenerate faces removed. Returns the old-to-new vertex index map
        /// (-1 for a removed duplicate maps to its survivor's index instead, so selections can follow).
        /// </summary>
        public int[] WeldCoincident(float tolerance = 1e-4f)
        {
            var remap = new int[vertices.Length];
            var kept = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int found = -1;
                for (int k = 0; k < kept.Count && found < 0; k++)
                    if ((kept[k] - vertices[i]).sqrMagnitude <= tolerance * tolerance) found = k;
                if (found < 0) { found = kept.Count; kept.Add(vertices[i]); }
                remap[i] = found;
            }
            if (kept.Count == vertices.Length) return remap;
            var newFaces = new List<Face>();
            foreach (var face in faces)
            {
                var loop = new List<int>();
                foreach (var i in face.indices)
                {
                    int n = remap[i];
                    if (loop.Count > 0 && loop[loop.Count - 1] == n) continue;
                    loop.Add(n);
                }
                while (loop.Count > 1 && loop[0] == loop[loop.Count - 1]) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3 && new HashSet<int>(loop).Count >= 3) newFaces.Add(new Face(loop.ToArray(), face.source));
            }
            vertices = kept.ToArray();
            faces = newFaces.ToArray();
            return remap;
        }

        /// <summary>Move every vertex of a face along the face normal.</summary>
        public void PushFace(int face, float distance)
        {
            var plane = Plane(face);
            var n = new Vector3(plane.x, plane.y, plane.z);
            foreach (var i in faces[face].indices) vertices[i] += n * distance;
        }

        /// <summary>
        /// Extrude faces: the faces move along a normal and side walls are added along the boundary of what moved.
        /// Individual: every face moves along its own normal and gets its own walls. Otherwise the whole selection
        /// moves as one along the average normal, and only the boundary of the selection gets walls (shared edges
        /// stay open, like ProBuilder's group extrude). Face indices are kept; the walls are appended with the
        /// extruded face as their source. A negative distance cuts a pocket. Returns the number of walls added.
        /// </summary>
        public int ExtrudeFaces(IEnumerable<int> faceIndices, float distance, bool individual)
        {
            var groups = new List<List<int>>();
            if (individual) { foreach (var f in faceIndices) if (f >= 0 && f < faces.Length) groups.Add(new List<int> { f }); }
            else { var all = new List<int>(); foreach (var f in faceIndices) if (f >= 0 && f < faces.Length && !all.Contains(f)) all.Add(f); if (all.Count > 0) groups.Add(all); }
            var verts = new List<Vector3>(vertices);
            var newFaces = new List<Face>(faces);
            int walls = 0;
            foreach (var group in groups)
            {
                Vector3 normal = Vector3.zero;
                foreach (var f in group) { var pl = Plane(f); normal += new Vector3(pl.x, pl.y, pl.z); }
                if (normal.sqrMagnitude < 1e-10f) { var pl = Plane(group[0]); normal = new Vector3(pl.x, pl.y, pl.z); }
                var offset = normal.normalized * distance;
                // boundary edges: used once within the group (as a directed edge a->b of a group face)
                var edgeCount = new Dictionary<long, int>();
                foreach (var f in group)
                {
                    var idx = newFaces[f].indices;
                    for (int i = 0; i < idx.Length; i++) { long k = EdgeKey(idx[i], idx[(i + 1) % idx.Length]); edgeCount[k] = edgeCount.TryGetValue(k, out var c) ? c + 1 : 1; }
                }
                var dup = new Dictionary<int, int>();
                int Dup(int v) { if (!dup.TryGetValue(v, out int d)) { d = verts.Count; verts.Add(verts[v] + offset); dup[v] = d; } return d; }
                foreach (var f in group)
                {
                    var idx = newFaces[f].indices;
                    for (int i = 0; i < idx.Length; i++)
                    {
                        int a = idx[i], b = idx[(i + 1) % idx.Length];
                        if (edgeCount[EdgeKey(a, b)] != 1) continue; // shared with another face of the group: stays open
                        newFaces.Add(new Face(new[] { a, b, Dup(b), Dup(a) }, f));
                        walls++;
                    }
                    var moved = new int[idx.Length];
                    for (int i = 0; i < idx.Length; i++) moved[i] = Dup(idx[i]);
                    newFaces[f] = new Face(moved, newFaces[f].source) { group = newFaces[f].group };
                }
            }
            vertices = verts.ToArray(); faces = newFaces.ToArray();
            return walls;
        }

        static long EdgeKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        /// <summary>Move one vertex and keep the faces around it planar by splitting the bent ones into triangles fanned from it.</summary>
        public void MoveVertex(int vertex, Vector3 position)
        {
            vertices[vertex] = position;
            EnsurePlanar(vertex);
        }

        /// <summary>Faces around a vertex that are no longer planar become triangle fans from that vertex (new faces keep the source id).</summary>
        public void EnsurePlanar(int vertex)
        {
            var list = new List<Face>(faces);
            for (int f = list.Count - 1; f >= 0; f--)
            {
                var face = list[f];
                if (Array.IndexOf(face.indices, vertex) < 0 || face.indices.Length <= 3) continue;
                if (PlanarityErrorOf(face) <= Epsilon * 10f) continue;
                // fan from the moved vertex
                int k = Array.IndexOf(face.indices, vertex); int n = face.indices.Length;
                int source = face.source >= 0 ? face.source : f;
                var fan = new List<Face>();
                for (int i = 1; i + 1 < n; i++)
                    fan.Add(new Face(new[] { face.indices[k], face.indices[(k + i) % n], face.indices[(k + i + 1) % n] }, source));
                list.RemoveAt(f);
                list.InsertRange(f, fan);
            }
            faces = list.ToArray();
        }

        float PlanarityErrorOf(Face face)
        {
            var saved = faces; faces = new[] { face };
            float e = PlanarityError(0);
            faces = saved;
            return e;
        }
    }
}

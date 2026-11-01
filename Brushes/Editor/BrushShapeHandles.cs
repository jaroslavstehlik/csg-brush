using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Shape handles: a selected procedural brush's flat sides and its points (an apex, a radius, a step) change its
    /// shape directly in the Scene view, alongside whichever tool is active (Move, Rotate, Scale), as Godot does for its
    /// CSG shapes and TrenchBroom for its brushes. Drag a box's side and it extends while the opposite side stays; drag a
    /// cylinder's rim and its radius changes; drag a stair's first riser and it gets longer. The brush stays the
    /// shape it is (unlike the Edit Brush mode, which makes it Custom).
    ///
    /// Only sides turned towards the camera are shown; a side lights up under the pointer and is grabbed anywhere on it.
    /// Unity's own tool handles always win: ours take a press only where none of theirs is under the pointer, and a side
    /// starts resizing only after a short drag, so a plain click still selects. Shift drags both sides at once. Values
    /// follow the brush's snapping. Each drag is one undo step; with several brushes selected, the same handle changes all
    /// of them.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushShapeHandles
    {
        /// <summary>
        /// One handle: where it sits (brush local space), the way it drags, and what a drag does. A side has
        /// <see cref="faces"/> (convex polygons, local space) and drags along its outward normal; a point has none.
        /// </summary>
        public struct Handle
        {
            public string id;
            public Vector3 local;
            public Vector3 direction;
            public Vector3[][] faces;
            public bool IsFace => faces != null && faces.Length > 0;
            /// <summary>An edge (or a closed loop, such as a rim): a polyline in local space; grabbed anywhere along it.</summary>
            public Vector3[] edge;
            public bool closed;
            public bool IsEdge => edge != null && edge.Length > 1;
            /// <summary>A rim drags away from its axis, wherever it is grabbed: <see cref="local"/> is then the axis.</summary>
            public bool radial;
            /// <summary>
            /// For a curved side (a cylinder's or cone's): each polygon's outward normal, local space. Only the polygons
            /// turned towards the camera are shown and grabbed. Null for a flat side, whose normal is <see cref="direction"/>.
            /// </summary>
            public Vector3[] faceNormals;
            /// <summary>A radial side drags away from <see cref="local"/> in every direction (a sphere), not only across the axis.</summary>
            public bool fromCentre;
            /// <summary>Apply a drag of <c>distance</c> metres along the direction to a brush, from its state at the drag's start.</summary>
            public System.Action<Brush, State, float, bool, bool> apply;
            /// <summary>What the handle shows while dragged.</summary>
            public System.Func<Brush, string> label;
        }

        /// <summary>A brush's shape and place at the start of a drag: every drag is applied to this, not added up.</summary>
        public struct State
        {
            public Vector3 size, position, pivot;
            public Quaternion rotation;
            public float stepHeight, innerRadius, stepWidth, curveAngle, wallThickness;
            public int numSteps;

            public static State Of(Brush b) => new State
            {
                size = b.size, position = b.transform.position, rotation = b.transform.rotation, pivot = b.pivot,
                stepHeight = b.stepHeight, innerRadius = b.innerRadius, stepWidth = b.stepWidth, curveAngle = b.curveAngle,
                wallThickness = b.wallThickness, numSteps = b.numSteps,
            };
        }

        static BrushShapeHandles()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [Shortcut("Brushes/Toggle Shape Handles", typeof(SceneView), KeyCode.B)]
        static void ToggleShortcut()
        {
            var s = BrushSettings.instance;
            s.shapeHandles = !s.shapeHandles;
            s.NotifyChanged();
            SceneView.RepaintAll();
        }

        // ---- the handles of each shape

        /// <summary>The handles of a brush's shape, in its local space; none for Custom and generated brushes.</summary>
        public static List<Handle> HandlesOf(Brush b)
        {
            var list = new List<Handle>();
            if (b == null || b.IsGenerated || b.shape == BrushShape.Custom) return list;
            Vector3 size = b.ClampedSize, shift = b.PivotShift;
            Vector3 min = shift - size * 0.5f, max = shift + size * 0.5f, c = shift;
            // a side of the box (or part of one): drags its plane along its outward normal
            void Side(string id, Vector3 local, int axis, int side, params Vector3[][] polygons) =>
                list.Add(new Handle { id = id, local = local, direction = Axis(axis) * side, faces = polygons, apply = (br, st, d, sym, snap) => ResizeBox(br, st, axis, side, d, sym, snap), label = br => Length(br.size[axis]) });
            // the whole side of the box on this axis and side
            void Face(string id, Vector3 local, int axis, int side) => Side(id, local, axis, side, BoxSide(min, max, axis, side));
            // an edge of the shape that resizes the box, grabbed anywhere along it
            void Edge(string id, int axis, int side, Vector3 a, Vector3 e) =>
                list.Add(new Handle { id = id, local = (a + e) * 0.5f, direction = Axis(axis) * side, edge = new[] { a, e }, apply = (br, st, d, sym, snap) => ResizeBox(br, st, axis, side, d, sym, snap), label = br => Length(br.size[axis]) });
            // a point of the shape that resizes the box
            void Point(string id, Vector3 local, int axis, int side) => Side(id, local, axis, side);
            switch (b.shape)
            {
                case BrushShape.Box:
                case BrushShape.Door:
                case BrushShape.Window:
                    Face("+x", new Vector3(max.x, c.y, c.z), 0, 1); Face("-x", new Vector3(min.x, c.y, c.z), 0, -1);
                    Face("+y", new Vector3(c.x, max.y, c.z), 1, 1); Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    if (!b.IsOpening) { Face("+z", new Vector3(c.x, c.y, max.z), 2, 1); Face("-z", new Vector3(c.x, c.y, min.z), 2, -1); } // an opening's depth is its wall's
                    break;
                case BrushShape.Wedge: // a ramp rising towards +z
                    Face("+z", new Vector3(c.x, c.y, max.z), 2, 1);                                   // the high end
                    Edge("-z", 2, -1, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z)); // the low end (start)
                    Edge("+y", 1, 1, new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z));  // the high top edge: the height
                    // the diagonal face: pushed out parallel to itself, the ramp grows longer and taller at the same steepness
                    list.Add(new Handle
                    {
                        id = "slope", local = new Vector3(c.x, c.y, c.z), direction = new Vector3(0f, size.z, -size.y).normalized,
                        faces = new[] { new[] { new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z) } },
                        apply = (br, st, d, sym, snap) => ResizeSlope(br, st, d, snap),
                        label = br => Length(br.size.z) + " \u00d7 " + Length(br.size.y),
                    });
                    Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    float wy = min.y + size.y / 3f, wz = max.z - size.z / 3f;                          // the side caps' middles
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        float x = sx > 0 ? max.x : min.x;
                        Side(sx > 0 ? "+x" : "-x", new Vector3(x, wy, wz), 0, sx, new[] { new Vector3(x, min.y, min.z), new Vector3(x, min.y, max.z), new Vector3(x, max.y, max.z) });
                    }
                    break;
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    bool cone = b.shape == BrushShape.Cone;
                    if (cone) Point("+y", new Vector3(c.x, max.y, c.z), 1, 1);                       // the apex
                    else Side("+y", new Vector3(c.x, max.y, c.z), 1, 1, Disc(c, size, max.y));       // the top cap
                    Side("-y", new Vector3(c.x, min.y, c.z), 1, -1, Disc(c, size, min.y));
                    // the round side: the radius, grabbed anywhere on the part facing the camera, dragged away from the axis
                    RoundSide(cone, c, size, min, max, out var sidePolygons, out var sideNormals);
                    list.Add(new Handle
                    {
                        id = "side", local = c, direction = Vector3.right, faces = sidePolygons, faceNormals = sideNormals, radial = true,
                        apply = (br, st, d, sym, snap) => ResizeRim(br, st, d, snap),
                        label = br => Mathf.Abs(br.size.x - br.size.z) < 1e-4f ? "\u2300 " + Length(br.size.x) : "\u2300 " + Length(br.size.x) + " \u00d7 " + Length(br.size.z),
                    });
                    break;
                case BrushShape.Sphere:
                    // the surface: the radius, grabbed anywhere on the part facing the camera, dragged away from the centre
                    SphereSurface(c, size, out var patches, out var patchNormals);
                    list.Add(new Handle
                    {
                        id = "surface", local = c, direction = Vector3.up, faces = patches, faceNormals = patchNormals, radial = true, fromCentre = true,
                        apply = (br, st, d, sym, snap) => ResizeSphere(br, st, d, snap),
                        label = br => Mathf.Abs(br.size.x - br.size.y) < 1e-4f && Mathf.Abs(br.size.x - br.size.z) < 1e-4f
                            ? "\u2300 " + Length(br.size.x) : Length(br.size.x) + " \u00d7 " + Length(br.size.y) + " \u00d7 " + Length(br.size.z),
                    });
                    break;
                case BrushShape.Stairs: // climbing towards +z
                    int steps = BrushPolyhedron.StepCount(size.y, b.stepHeight);
                    float rise = size.y / steps, run = size.z / steps;
                    Face("+z", new Vector3(c.x, c.y, max.z), 2, 1);                                   // the back wall
                    Side("-z", new Vector3(c.x, min.y + rise * 0.5f, min.z), 2, -1, Rect(min.x, max.x, min.y, min.y + rise, min.z, 2)); // the first riser
                    Side("+y", new Vector3(c.x, max.y, max.z - run * 0.5f), 1, 1, Rect(min.x, max.x, max.z - run, max.z, max.y, 1)); // the top tread
                    Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    for (int sx = -1; sx <= 1; sx += 2)                                               // the stepped sides, a column per step
                    {
                        float x = sx > 0 ? max.x : min.x;
                        var columns = new Vector3[steps][];
                        for (int k = 0; k < steps; k++)
                            columns[k] = Rect(min.z + run * k, min.z + run * (k + 1), min.y, min.y + rise * (k + 1), x, 0);
                        Side(sx > 0 ? "+x" : "-x", new Vector3(x, c.y, c.z), 0, sx, columns);
                    }
                    break;
                case BrushShape.Arch: // the ring between an outer ellipse and one a thickness in, on the box's floor
                    Edge("+x", 0, 1, new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z));   // the feet: the width
                    Edge("-x", 0, -1, new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z));
                    Side("+z", new Vector3(c.x, max.y - b.wallThickness * 0.5f, max.z), 2, 1, Ring(b, min, max, max.z));
                    Side("-z", new Vector3(c.x, max.y - b.wallThickness * 0.5f, min.z), 2, -1, Ring(b, min, max, min.z));
                    // the curved faces between the front and the back: the outer one sets the height, the inner one the thickness
                    ArchSurface(b, min, max, true, out var outerFaces, out var outerNormals);
                    list.Add(new Handle
                    {
                        id = "+y", local = new Vector3(c.x, max.y, c.z), direction = Vector3.up, faces = outerFaces, faceNormals = outerNormals,
                        apply = (br, st, d, sym, snap) => ResizeBox(br, st, 1, 1, d, sym, snap),
                        label = br => Length(br.size.y),
                    });
                    ArchSurface(b, min, max, false, out var innerFaces, out var innerNormals);
                    list.Add(new Handle
                    {
                        id = "thickness", local = new Vector3(c.x, max.y - b.wallThickness, c.z), direction = Vector3.down,
                        faces = innerFaces, faceNormals = innerNormals,
                        apply = (br, st, d, sym, snap) => { br.wallThickness = Mathf.Clamp(SnapLength(br, st.wallThickness + d, snap), 0.01f, Mathf.Min(br.ClampedSize.x * 0.5f, br.ClampedSize.y) - 0.01f); },
                        label = br => "thickness " + Length(br.wallThickness),
                    });
                    break;
                case BrushShape.CurvedStairs:
                case BrushShape.SpiralStairs:
                    AddRoundStairs(b, list);
                    break;
            }
            return list;
        }

        /// <summary>
        /// Curved and spiral stairs, all faces: the inner face (the side towards the column) and the outer face move their own
        /// edge with the other fixed; the top step's tread sets the height; the last step's end face turns the stairs
        /// further (curved) or adds steps round the turn (spiral). The start stays where the brush is.
        /// </summary>
        static void AddRoundStairs(Brush b, List<Handle> list)
        {
            bool spiral = b.shape == BrushShape.SpiralStairs;
            float sign = b.counterClockwise ? 1f : -1f;
            float ri = Mathf.Max(0f, b.innerRadius), ro = ri + Mathf.Max(0.001f, b.stepWidth), h = Mathf.Max(BrushPolyhedron.MinStepHeight, b.stepHeight);
            int n = Mathf.Max(1, b.numSteps);
            float total = spiral ? Mathf.PI * 2f / Mathf.Max(1, b.stepsPer360) * n : Mathf.Max(1f, b.curveAngle) * Mathf.Deg2Rad;
            float stepAngle = total / n;
            int sub = Mathf.Max(1, Mathf.CeilToInt(stepAngle * Mathf.Rad2Deg / 10f));   // pieces per step, so curved faces stay close to the steps
            bool slab = spiral || !b.supportUnderSteps;
            float thickness = Mathf.Max(0.001f, b.stepThickness);
            Vector3 At(float radius, float angle, float y) => new Vector3(radius * Mathf.Cos(angle * sign), y, radius * Mathf.Sin(angle * sign));
            float Top(int k) => b.addToFirstStep + (k + 1) * h;
            float Bottom(int k) => slab ? Mathf.Max(0f, Top(k) - thickness) : 0f;

            // the inner and outer faces: one strip per piece of each step, facing away from (outer) or towards (inner) the axis
            Vector3[][] Wall(float radius, bool inner, out Vector3[] normals)
            {
                var polygons = new List<Vector3[]>(); var faceNormals = new List<Vector3>();
                for (int k = 0; k < n; k++)
                    for (int j = 0; j < sub; j++)
                    {
                        float t0 = (k + (float)j / sub) * stepAngle, t1 = (k + (float)(j + 1) / sub) * stepAngle, tm = (t0 + t1) * 0.5f;
                        polygons.Add(new[] { At(radius, t0, Bottom(k)), At(radius, t1, Bottom(k)), At(radius, t1, Top(k)), At(radius, t0, Top(k)) });
                        faceNormals.Add(At(inner ? -1f : 1f, tm, 0f));
                    }
                normals = faceNormals.ToArray();
                return polygons.ToArray();
            }
            var innerFaces = Wall(ri, true, out var innerNormals);
            var outerFaces = Wall(ro, false, out var outerNormals);
            list.Add(new Handle
            {
                id = "inner", local = Vector3.zero, direction = At(1f, stepAngle * 0.5f, 0f), faces = innerFaces, faceNormals = innerNormals, radial = true,
                // the inner edge moves (away from the axis is positive); the outer edge stays
                apply = (br, st, d, sym, snap) =>
                {
                    float outer = st.innerRadius + st.stepWidth;
                    br.innerRadius = Mathf.Clamp(SnapLength(br, st.innerRadius + d, snap), 0f, outer - 0.05f);
                    br.stepWidth = outer - br.innerRadius;
                },
                label = br => "column " + Length(br.innerRadius) + ", steps " + Length(br.stepWidth) + " wide",
            });
            list.Add(new Handle
            {
                id = "outer", local = Vector3.zero, direction = At(1f, stepAngle * 0.5f, 0f), faces = outerFaces, faceNormals = outerNormals, radial = true,
                apply = (br, st, d, sym, snap) => { br.stepWidth = Mathf.Max(0.05f, SnapLength(br, st.stepWidth + d, snap)); },
                label = br => "steps " + Length(br.stepWidth) + " wide",
            });

            // the top step's tread: the height
            var tread = new Vector3[sub][];
            for (int j = 0; j < sub; j++)
            {
                float t0 = (n - 1 + (float)j / sub) * stepAngle, t1 = (n - 1 + (float)(j + 1) / sub) * stepAngle;
                tread[j] = new[] { At(ri, t0, Top(n - 1)), At(ro, t0, Top(n - 1)), At(ro, t1, Top(n - 1)), At(ri, t1, Top(n - 1)) };
            }
            list.Add(new Handle
            {
                id = "height", local = At((ri + ro) * 0.5f, total - stepAngle * 0.5f, Top(n - 1)), direction = Vector3.up, faces = tread,
                // curved: the height spreads over the same steps; spiral: more height is more steps, going round further
                apply = spiral
                    ? (System.Action<Brush, State, float, bool, bool>)((br, st, d, sym, snap) => { br.numSteps = Mathf.Max(1, Mathf.RoundToInt((st.numSteps * st.stepHeight + d) / Mathf.Max(BrushPolyhedron.MinStepHeight, st.stepHeight))); })
                    : (br, st, d, sym, snap) => { br.stepHeight = Mathf.Max(BrushPolyhedron.MinStepHeight, SnapStep(st.stepHeight + d / Mathf.Max(1, st.numSteps), snap)); },
                label = br => spiral ? br.numSteps + " steps, " + Length(br.numSteps * br.stepHeight) : "step " + Length(br.stepHeight) + ", " + Length(br.numSteps * br.stepHeight) + " high",
            });

            // the last step's end face: further round (curved), or more steps (spiral)
            Vector3 onward = (At(1f, total + 0.01f, 0f) - At(1f, total, 0f)).normalized;
            list.Add(new Handle
            {
                id = "angle", local = At((ri + ro) * 0.5f, total, (Bottom(n - 1) + Top(n - 1)) * 0.5f), direction = onward,
                faces = new[] { new[] { At(ri, total, Bottom(n - 1)), At(ro, total, Bottom(n - 1)), At(ro, total, Top(n - 1)), At(ri, total, Top(n - 1)) } },
                apply = (br, st, d, sym, snap) =>
                {
                    float radius = Mathf.Max(0.01f, st.innerRadius + st.stepWidth * 0.5f);   // the middle of the steps
                    if (spiral)
                    {
                        float perStep = Mathf.PI * 2f / Mathf.Max(1, br.stepsPer360) * radius;
                        br.numSteps = Mathf.Max(1, st.numSteps + Mathf.RoundToInt(d / perStep));
                        return;
                    }
                    float degrees = st.curveAngle + d / radius * Mathf.Rad2Deg;
                    float stepDegrees = BrushSettings.instance.rotationSnapDegrees;
                    if (snap && stepDegrees > 0f) degrees = Mathf.Round(degrees / stepDegrees) * stepDegrees;
                    br.curveAngle = Mathf.Clamp(degrees, 1f, 3600f);
                },
                label = br => spiral ? br.numSteps + " steps" : br.curveAngle.ToString("0.#") + "\u00b0",
            });
        }

        static Vector3 Axis(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        /// <summary>The side of the box [min, max] on <paramref name="axis"/> and <paramref name="side"/>, as one rectangle.</summary>
        static Vector3[] BoxSide(Vector3 min, Vector3 max, int axis, int side)
        {
            float at = side > 0 ? max[axis] : min[axis];
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            Vector3 P(float a, float b) { var p = Vector3.zero; p[axis] = at; p[u] = a; p[v] = b; return p; }
            return new[] { P(min[u], min[v]), P(max[u], min[v]), P(max[u], max[v]), P(min[u], max[v]) };
        }

        /// <summary>A rectangle in the plane where <paramref name="axis"/> is <paramref name="at"/>, spanning the other two axes in order.</summary>
        static Vector3[] Rect(float a0, float a1, float b0, float b1, float at, int axis)
        {
            Vector3 P(float a, float b)
            {
                if (axis == 0) return new Vector3(at, b, a);      // across z (a) and y (b)
                if (axis == 1) return new Vector3(a, at, b);      // across x (a) and z (b)
                return new Vector3(a, b, at);                     // across x (a) and y (b)
            }
            return new[] { P(a0, b0), P(a1, b0), P(a1, b1), P(a0, b1) };
        }

        /// <summary>
        /// A cylinder's or cone's round side as strips (cylinder) or slices (cone) around its axis, each with its outward
        /// normal, so the part facing the camera can be told from the rest.
        /// </summary>
        static void RoundSide(bool cone, Vector3 centre, Vector3 size, Vector3 min, Vector3 max, out Vector3[][] polygons, out Vector3[] normals)
        {
            const int n = 32;
            float a = size.x * 0.5f, b = size.z * 0.5f;
            Vector3 Rim(float t, float y) => new Vector3(centre.x + Mathf.Cos(t) * a, y, centre.z + Mathf.Sin(t) * b);
            var apex = new Vector3(centre.x, max.y, centre.z);
            polygons = new Vector3[n][];
            normals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t0 = i * Mathf.PI * 2f / n, t1 = (i + 1) * Mathf.PI * 2f / n, tm = (t0 + t1) * 0.5f;
                Vector3 b0 = Rim(t0, min.y), b1 = Rim(t1, min.y);
                Vector3 outward = new Vector3(Mathf.Cos(tm) / Mathf.Max(a, 1e-4f), 0f, Mathf.Sin(tm) / Mathf.Max(b, 1e-4f)).normalized; // an ellipse's normal
                if (cone)
                {
                    polygons[i] = new[] { b0, b1, apex };
                    Vector3 n0 = Vector3.Cross(b1 - b0, apex - b0).normalized;
                    normals[i] = Vector3.Dot(n0, outward) < 0f ? -n0 : n0;
                }
                else
                {
                    polygons[i] = new[] { b0, b1, Rim(t1, max.y), Rim(t0, max.y) };
                    normals[i] = outward;
                }
            }
        }

        /// <summary>A sphere's surface as patches between lines of latitude and longitude, each with its outward normal.</summary>
        static void SphereSurface(Vector3 centre, Vector3 size, out Vector3[][] patches, out Vector3[] normals)
        {
            const int bands = 12, slices = 24;
            Vector3 radii = Vector3.Max(size * 0.5f, Vector3.one * 1e-4f);
            Vector3 At(float latitude, float longitude) => centre + Vector3.Scale(radii,
                new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude)));
            var list = new List<Vector3[]>(); var listNormals = new List<Vector3>();
            for (int i = 0; i < bands; i++)
            {
                float la0 = -Mathf.PI * 0.5f + Mathf.PI * i / bands, la1 = -Mathf.PI * 0.5f + Mathf.PI * (i + 1) / bands;
                for (int j = 0; j < slices; j++)
                {
                    float lo0 = Mathf.PI * 2f * j / slices, lo1 = Mathf.PI * 2f * (j + 1) / slices;
                    Vector3 a = At(la0, lo0), b = At(la0, lo1), c = At(la1, lo1), d = At(la1, lo0);
                    // at the poles two corners meet: a triangle
                    list.Add(i == 0 ? new[] { a, c, d } : i == bands - 1 ? new[] { a, b, c } : new[] { a, b, c, d });
                    Vector3 middle = At((la0 + la1) * 0.5f, (lo0 + lo1) * 0.5f) - centre;
                    listNormals.Add(new Vector3(middle.x / (radii.x * radii.x), middle.y / (radii.y * radii.y), middle.z / (radii.z * radii.z)).normalized); // an ellipsoid's normal
                }
            }
            patches = list.ToArray();
            normals = listNormals.ToArray();
        }

        /// <summary>A sphere's surface dragged out by <paramref name="distance"/>: all three diameters grow alike about its centre.</summary>
        static void ResizeSphere(Brush b, State start, float distance, bool snap)
        {
            Vector3 startSize = Vector3.Max(start.size, Vector3.one * 0.001f);
            float radius = (startSize.x + startSize.y + startSize.z) / 6f;
            float scale = Mathf.Max(0.001f, radius + distance) / radius;
            if (snap) scale = SnapLength(b, startSize.x * scale, true) / startSize.x;
            Vector3 startShift = b.PivotShiftFor(startSize);
            Vector3 centre = startShift, half = startSize * scale * 0.5f;
            Place(b, start, centre - half, centre + half);
        }

        /// <summary>A cylinder's or cone's cap at height <paramref name="y"/>: an ellipse of the box's width and depth.</summary>
        static Vector3[] Disc(Vector3 centre, Vector3 size, float y)
        {
            const int n = 32;
            var points = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i * Mathf.PI * 2f / n;
                points[i] = new Vector3(centre.x + Mathf.Cos(t) * size.x * 0.5f, y, centre.z + Mathf.Sin(t) * size.z * 0.5f);
            }
            return points;
        }

        /// <summary>An arch's front or back at depth <paramref name="z"/>: one quad per segment of its ring.</summary>
        /// <summary>
        /// An arch's curved faces between its front and back, one quad per segment, with each one's outward normal: the outer
        /// face (the top of the arch), or the inner face (the underside of the opening), whose outside looks into the opening.
        /// </summary>
        static void ArchSurface(Brush b, Vector3 min, Vector3 max, bool outer, out Vector3[][] quads, out Vector3[] normals)
        {
            int n = Mathf.Max(1, b.sides);
            float a = Mathf.Max(0.001f, (max.x - min.x) * 0.5f), h = Mathf.Max(0.001f, max.y - min.y);
            float t = Mathf.Clamp(b.wallThickness, 0.001f, Mathf.Min(a, h) - 0.0005f);
            float ra = outer ? a : a - t, rh = outer ? h : h - t;
            float angle = Mathf.Clamp(b.curveAngle, 1f, 180f) * Mathf.Deg2Rad, start = Mathf.PI * 0.5f + angle * 0.5f, step = -angle / n;
            float cx = (min.x + max.x) * 0.5f;
            quads = new Vector3[n][];
            normals = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float u0 = start + k * step, u1 = start + (k + 1) * step, um = (u0 + u1) * 0.5f;
                Vector3 E(float u, float z) => new Vector3(cx + ra * Mathf.Cos(u), min.y + rh * Mathf.Sin(u), z);
                quads[k] = new[] { E(u0, min.z), E(u1, min.z), E(u1, max.z), E(u0, max.z) };
                var normal = new Vector3(Mathf.Cos(um) / ra, Mathf.Sin(um) / rh, 0f).normalized; // the ellipse's normal
                normals[k] = outer ? normal : -normal;
            }
        }

        static Vector3[][] Ring(Brush b, Vector3 min, Vector3 max, float z)
        {
            int n = Mathf.Max(1, b.sides);
            float a = Mathf.Max(0.001f, (max.x - min.x) * 0.5f), h = Mathf.Max(0.001f, max.y - min.y);
            float t = Mathf.Clamp(b.wallThickness, 0.001f, Mathf.Min(a, h) - 0.0005f);
            float angle = Mathf.Clamp(b.curveAngle, 1f, 180f) * Mathf.Deg2Rad, start = Mathf.PI * 0.5f + angle * 0.5f, step = -angle / n;
            float cx = (min.x + max.x) * 0.5f;
            var quads = new Vector3[n][];
            for (int k = 0; k < n; k++)
            {
                float u0 = start + k * step, u1 = start + (k + 1) * step;
                Vector3 O(float u) => new Vector3(cx + a * Mathf.Cos(u), min.y + h * Mathf.Sin(u), z);
                Vector3 I(float u) => new Vector3(cx + (a - t) * Mathf.Cos(u), min.y + (h - t) * Mathf.Sin(u), z);
                quads[k] = new[] { I(u0), O(u0), O(u1), I(u1) };
            }
            return quads;
        }

        // ---- what a drag does

        /// <summary>Whether this brush's lengths go onto the grid: snapping on, the brush snaps, and it is not a door or window.</summary>
        public static bool Snaps(Brush b) => BrushSettings.instance.snapToGrid && !b.IsPlaced;

        static float SnapLength(Brush b, float metres, bool snap)
        {
            if (!snap) return metres;
            float grid = BrushSettings.instance.GridMeters;
            return grid > 0f ? Mathf.Max(grid, Mathf.Round(metres / grid) * grid) : metres;
        }

        /// <summary>Step heights snap to centimetres: a grid step is far coarser than stairs need.</summary>
        static float SnapStep(float metres, bool snap) => snap ? Mathf.Round(metres * 100f) / 100f : metres;

        /// <summary>
        /// Move one side of the shape's box by <paramref name="distance"/> (outward positive), or both with
        /// <paramref name="symmetric"/>; the other side stays where it was, whatever the pivot.
        /// </summary>
        static void ResizeBox(Brush b, State start, int axis, int side, float distance, bool symmetric, bool snap)
        {
            Vector3 startSize = Vector3.Max(start.size, Vector3.one * 0.001f);
            Vector3 startShift = b.PivotShiftFor(startSize);
            Vector3 min = startShift - startSize * 0.5f, max = startShift + startSize * 0.5f;
            float length = startSize[axis] + distance * (symmetric ? 2f : 1f);
            length = Mathf.Max(0.001f, snap ? SnapLength(b, length, true) : length);
            float grow = length - startSize[axis];
            if (symmetric) { min[axis] -= grow * 0.5f; max[axis] += grow * 0.5f; }
            else if (side > 0) max[axis] += grow;
            else min[axis] -= grow;
            Place(b, start, min, max);
        }

        /// <summary>
        /// A wedge's diagonal face pushed out by <paramref name="distance"/> along its normal: the bottom and the high end stay,
        /// the ramp grows longer and taller in proportion, so its steepness stays. The length snaps; the height follows it.
        /// </summary>
        static void ResizeSlope(Brush b, State start, float distance, bool snap)
        {
            Vector3 startSize = Vector3.Max(start.size, Vector3.one * 0.001f);
            float length = startSize.z, height = startSize.y, slope = Mathf.Sqrt(length * length + height * height);
            float newLength = Mathf.Max(0.001f, length + distance * slope / height);
            if (snap) newLength = SnapLength(b, newLength, true);
            float newHeight = height * newLength / length;
            Vector3 startShift = b.PivotShiftFor(startSize);
            Vector3 min = startShift - startSize * 0.5f, max = startShift + startSize * 0.5f;
            min.z = max.z - newLength;   // the high end stays
            max.y = min.y + newHeight;   // the bottom stays
            Place(b, start, min, max);
        }

        /// <summary>A rim dragged out by <paramref name="distance"/>: both diameters grow alike about the axis, so round stays round and an oval keeps its proportions.</summary>
        static void ResizeRim(Brush b, State start, float distance, bool snap)
        {
            Vector3 startSize = Vector3.Max(start.size, Vector3.one * 0.001f);
            float radius = (startSize.x + startSize.z) * 0.25f;
            float scale = Mathf.Max(0.001f, radius + distance) / radius;
            float x = startSize.x * scale;
            if (snap) { x = SnapLength(b, x, true); scale = x / startSize.x; }
            Vector3 startShift = b.PivotShiftFor(startSize);
            Vector3 min = startShift - startSize * 0.5f, max = startShift + startSize * 0.5f;
            foreach (int a in new[] { 0, 2 })
            {
                float centre = (min[a] + max[a]) * 0.5f, half = startSize[a] * scale * 0.5f;
                min[a] = centre - half; max[a] = centre + half;
            }
            Place(b, start, min, max);
        }

        /// <summary>Give the brush the box [min, max] (local to its start pose), keeping its pivot setting: size and position.</summary>
        static void Place(Brush b, State start, Vector3 min, Vector3 max)
        {
            Vector3 size = max - min;
            Vector3 centre = (min + max) * 0.5f;
            Vector3 shift = b.PivotShiftFor(size);
            b.size = size;
            b.transform.position = start.position + start.rotation * (centre - shift);
        }

        /// <summary>Apply a drag of one handle to a brush: one call per change, from the brush's state when the drag began.</summary>
        public static void Apply(Brush b, State start, string handleId, float distance, bool symmetric)
        {
            foreach (var h in HandlesOf(b))
            {
                if (h.id != handleId) continue;
                Undo.RecordObjects(new Object[] { b, b.transform }, "Change brush shape");
                h.apply(b, start, distance, symmetric, Snaps(b));
                BrushSnap.Accept(b); // placed by the handle: the grid pass must not move it again
                BrushSync.NotifyTransformChanged(b);
                BrushSync.Ensure(b);
                return;
            }
        }

        /// <summary>
        /// Apply a drag of <paramref name="dragged"/>'s handle, and to the other brushes as <paramref name="mode"/> says:
        /// none, those of the same shape by the same distance, or those of the same shape to the dragged brush's new value.
        /// </summary>
        public static void ApplyToSelection(List<Brush> brushes, Brush dragged, string handleId, float distance, bool symmetric,
            ShapeEditSelection mode, Dictionary<Brush, State> starts)
        {
            if (!starts.TryGetValue(dragged, out var draggedStart)) return;
            Apply(dragged, draggedStart, handleId, distance, symmetric);
            if (mode == ShapeEditSelection.Single) return;
            int measure = mode == ShapeEditSelection.Match ? Measured(dragged, draggedStart, handleId, distance, symmetric) : -1;
            foreach (var other in brushes)
            {
                if (other == dragged || other.shape != dragged.shape || !starts.TryGetValue(other, out var start)) continue;
                if (mode == ShapeEditSelection.Offset) { Apply(other, start, handleId, distance, symmetric); continue; }
                if (measure < 0) { Apply(other, start, handleId, 0f, symmetric); continue; }
                Apply(other, start, handleId, DistanceTo(other, start, handleId, symmetric, measure, Values(State.Of(dragged))[measure]), symmetric);
            }
        }

        /// <summary>The values a handle can change, in a fixed order: the size, then the stair and arch settings.</summary>
        static float[] Values(State s) => new[] { s.size.x, s.size.y, s.size.z, s.innerRadius, s.stepWidth, s.curveAngle, s.numSteps, s.wallThickness, s.stepHeight };

        static Handle? Find(Brush b, string handleId)
        {
            foreach (var h in HandlesOf(b)) if (h.id == handleId) return h;
            return null;
        }

        /// <summary>Which of <see cref="Values"/> the handle changes: the first one that a drag of it (unsnapped) moves.</summary>
        static int Measured(Brush b, State start, string handleId, float distance, bool symmetric)
        {
            var h = Find(b, handleId);
            if (h == null) return -1;
            var now = State.Of(b);
            float probe = Mathf.Abs(distance) > 1e-3f ? distance : 1f;
            h.Value.apply(b, start, probe, symmetric, false);
            float[] before = Values(start), after = Values(State.Of(b));
            Restore(b, now);
            for (int i = 0; i < before.Length; i++)
                if (Mathf.Abs(after[i] - before[i]) > 1e-5f) return i;
            return -1;
        }

        /// <summary>The drag distance that gives the brush the value <paramref name="target"/>: found by trying distances, as handles are not all linear.</summary>
        static float DistanceTo(Brush b, State start, string handleId, bool symmetric, int measure, float target)
        {
            var h = Find(b, handleId);
            if (h == null) return 0f;
            Undo.RecordObjects(new Object[] { b, b.transform }, "Change brush shape"); // before the trials change it
            float Value(float d) { h.Value.apply(b, start, d, symmetric, false); return Values(State.Of(b))[measure]; }
            float d0 = 0f, m0 = Values(start)[measure], d1 = target - m0, m1 = Value(d1);
            for (int i = 0; i < 6 && Mathf.Abs(m1 - target) > 1e-4f && Mathf.Abs(m1 - m0) > 1e-6f; i++)
            {
                float d2 = d1 + (target - m1) * (d1 - d0) / (m1 - m0);
                d0 = d1; m0 = m1; d1 = d2; m1 = Value(d1);
            }
            return d1;
        }

        static void Restore(Brush b, State s)
        {
            b.size = s.size; b.transform.position = s.position; b.transform.rotation = s.rotation; b.pivot = s.pivot;
            b.stepHeight = s.stepHeight; b.innerRadius = s.innerRadius; b.stepWidth = s.stepWidth; b.curveAngle = s.curveAngle;
            b.wallThickness = s.wallThickness; b.numSteps = s.numSteps;
        }

        // ---- the Scene view

        /// <summary>
        /// A handle's control reports this distance (pixels) when the pointer is on it: inside Unity's pick range, but
        /// farther than any of Unity's own handles under the pointer, which report about zero, so theirs always win.
        /// </summary>
        const float YieldDistance = 4f;
        /// <summary>A side starts resizing only after the pointer moves this far (pixels): a plain click still selects.</summary>
        const float DragThreshold = 4f;
        /// <summary>
        /// A side works whenever its front can be seen; only one seen exactly edge-on (or from behind) is left out. For a flat
        /// side this is exact everywhere on it, in perspective too: its plane's distance to the camera is the same at every point.
        /// </summary>
        const float MinFacing = 1e-4f;

        static readonly int s_Hash = "BrushShapeHandle".GetHashCode();
        static readonly Dictionary<Brush, State> s_Start = new Dictionary<Brush, State>();
        static int s_HotControl, s_UndoGroup;
        static bool s_Dragging;
        static string s_HotId;
        static Brush s_HotBrush;
        static Vector2 s_DownMouse;
        static Vector3 s_StartWorld, s_StartDirection;

        static bool Showing()
        {
            var s = BrushSettings.instance;
            return s.shapeHandles && !BrushEditContext.IsActive && !FloorPlanEditContext.IsActive && WorksWith(Tools.current);
        }

        /// <summary>
        /// The handles go with Unity's Move, Rotate, Scale and Transform tools only: never while a brush or floor plan is
        /// drawn (a custom tool), so they can't take its clicks.
        /// </summary>
        public static bool WorksWith(Tool tool) => tool == Tool.Move || tool == Tool.Rotate || tool == Tool.Scale || tool == Tool.Transform;

        /// <summary>Is a side with this world normal and middle turned towards the camera: is its front seen at all?</summary>
        public static bool FacesCamera(Vector3 worldNormal, Vector3 worldMiddle, Camera camera)
        {
            Vector3 toCamera = camera.orthographic ? -camera.transform.forward : (camera.transform.position - worldMiddle).normalized;
            return Vector3.Dot(worldNormal.normalized, toCamera) > MinFacing;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Showing()) { if (s_HotControl != 0) Release(); return; }
            var brushes = new List<Brush>();
            foreach (var go in Selection.gameObjects)
                if (go != null && go.TryGetComponent<Brush>(out var b) && !b.IsGenerated && b.shape != BrushShape.Custom) brushes.Add(b);
            if (brushes.Count == 0 || brushes.Count > 32) return;
            var e = Event.current;
            foreach (var b in brushes)
            {
                var toWorld = b.transform.localToWorldMatrix;
                foreach (var h in HandlesOf(b))
                {
                    int id = GUIUtility.GetControlID(s_Hash, FocusType.Passive); // one per handle, shown or not, so ids stay stable
                    Vector3 world = toWorld.MultiplyPoint3x4(h.local);
                    Vector3 dir = toWorld.MultiplyVector(h.direction).normalized;
                    Vector3[] line = h.IsEdge ? System.Array.ConvertAll(h.edge, p => toWorld.MultiplyPoint3x4(p)) : null;
                    Vector3[][] polygons = h.IsFace ? Facing(h, toWorld, dir, world, view.camera, GUIUtility.hotControl == id) : null;
                    bool shown = GUIUtility.hotControl == id || !h.IsFace || polygons.Length > 0; // points and edges always; sides when they face the camera
                    if (!shown) continue;
                    float dot = HandleUtility.GetHandleSize(world) * 0.05f;
                    switch (e.GetTypeForControl(id))
                    {
                        case EventType.Layout:
                        case EventType.MouseMove:
                            // an edge on a face's border wins over the face (a little nearer); Unity's own handles win over both
                            if (line != null ? EdgeDistance(e.mousePosition, line, h.closed, out _) <= 6f : Under(e.mousePosition, world, dot, polygons))
                                HandleUtility.AddControl(id, h.IsFace ? YieldDistance : YieldDistance - 1f);
                            break;
                        case EventType.MouseDown:
                            if (HandleUtility.nearestControl == id && e.button == 0 && !e.alt)
                            {
                                GUIUtility.hotControl = id;
                                s_HotControl = id; s_HotId = h.id; s_HotBrush = b; s_Dragging = false;
                                s_DownMouse = e.mousePosition; s_StartWorld = world; s_StartDirection = dir;
                                if (line != null)
                                {
                                    // grabbed where the pointer is along it
                                    EdgeDistance(e.mousePosition, line, h.closed, out Vector3 grabbed);
                                    s_StartWorld = grabbed;
                                }
                                else if (polygons != null && Grabbed(e.mousePosition, polygons, out Vector3 point))
                                {
                                    s_StartWorld = point;
                                    if (h.radial)
                                    {
                                        // a round side drags away from the axis, from where it was grabbed
                                        Vector3 local = toWorld.inverse.MultiplyPoint3x4(point);
                                        Vector3 away = h.fromCentre ? local - h.local : new Vector3(local.x - h.local.x, 0f, local.z - h.local.z);
                                        if (away.sqrMagnitude > 1e-8f) s_StartDirection = toWorld.MultiplyVector(away).normalized;
                                    }
                                }
                                if (!h.IsFace) Begin(brushes); // a point or an edge drags at once
                                e.Use();
                            }
                            break;
                        case EventType.MouseDrag:
                            if (GUIUtility.hotControl == id)
                            {
                                if (!s_Dragging && h.IsFace && (e.mousePosition - s_DownMouse).sqrMagnitude < DragThreshold * DragThreshold) { e.Use(); break; }
                                if (!s_Dragging) Begin(brushes);
                                float distance = HandleUtility.CalcLineTranslation(s_DownMouse, e.mousePosition, s_StartWorld, s_StartDirection);
                                ApplyToSelection(brushes, s_HotBrush, s_HotId, distance, e.shift, BrushSettings.instance.shapeEditSelection, s_Start);
                                e.Use();
                            }
                            break;
                        case EventType.MouseUp:
                            if (GUIUtility.hotControl == id && e.button == 0)
                            {
                                bool click = !s_Dragging;
                                Release();
                                if (click) Click(e);
                                e.Use();
                            }
                            break;
                        case EventType.Repaint:
                            Draw(id, h, world, dir, dot, polygons, line, b);
                            break;
                    }
                }
            }
        }

        /// <summary>Is the pointer on this handle: inside one of its polygons, or on its dot?</summary>
        static bool Under(Vector2 mouse, Vector3 world, float dot, Vector3[][] polygons)
        {
            if (polygons == null)
                return (HandleUtility.WorldToGUIPoint(world) - mouse).magnitude <= Mathf.Max(6f, dot / Mathf.Max(1e-5f, HandleUtility.GetHandleSize(world)) * 80f);
            foreach (var polygon in polygons)
                if (Inside(mouse, polygon)) return true;
            return false;
        }

        /// <summary>A side's polygons in world space that face the camera: all or none for a flat side, the near part of a curved one.</summary>
        static Vector3[][] Facing(Handle h, Matrix4x4 toWorld, Vector3 dir, Vector3 world, Camera camera, bool held)
        {
            var result = new List<Vector3[]>(h.faces.Length);
            for (int i = 0; i < h.faces.Length; i++)
            {
                var polygon = System.Array.ConvertAll(h.faces[i], p => toWorld.MultiplyPoint3x4(p));
                if (h.faceNormals == null) { if (held || FacesCamera(dir, world, camera)) result.Add(polygon); continue; }
                Vector3 middle = Vector3.zero;
                foreach (var p in polygon) middle += p;
                middle /= polygon.Length;
                if (FacesCamera(toWorld.MultiplyVector(h.faceNormals[i]), middle, camera)) result.Add(polygon);
            }
            return result.ToArray();
        }

        /// <summary>Where the pointer meets the side: the point under it on the polygon it is over.</summary>
        static bool Grabbed(Vector2 mouse, Vector3[][] polygons, out Vector3 point)
        {
            point = default;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            foreach (var polygon in polygons)
            {
                if (polygon.Length < 3 || !Inside(mouse, polygon)) continue;
                var plane = new Plane(polygon[0], polygon[1], polygon[2]);
                if (plane.Raycast(ray, out float enter)) { point = ray.GetPoint(enter); return true; }
            }
            return false;
        }

        /// <summary>The pointer's distance in pixels to a polyline on the screen, and the nearest point on it in the world.</summary>
        static float EdgeDistance(Vector2 mouse, Vector3[] line, bool closed, out Vector3 nearest)
        {
            float best = float.MaxValue;
            nearest = line[0];
            int segments = closed ? line.Length : line.Length - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector3 a = line[i], b = line[(i + 1) % line.Length];
                Vector2 sa = HandleUtility.WorldToGUIPoint(a), sb = HandleUtility.WorldToGUIPoint(b);
                Vector2 ab = sb - sa;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(mouse - sa, ab) / ab.sqrMagnitude) : 0f;
                float d = (sa + ab * t - mouse).magnitude;
                if (d < best) { best = d; nearest = Vector3.Lerp(a, b, t); }
            }
            return best;
        }

        /// <summary>Point in a convex polygon projected to the screen: the same side of every edge.</summary>
        static bool Inside(Vector2 mouse, Vector3[] polygon)
        {
            int sign = 0;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = HandleUtility.WorldToGUIPoint(polygon[i]), b = HandleUtility.WorldToGUIPoint(polygon[(i + 1) % polygon.Length]);
                float cross = (b.x - a.x) * (mouse.y - a.y) - (b.y - a.y) * (mouse.x - a.x);
                if (Mathf.Abs(cross) < 1e-6f) continue;
                int s = cross > 0f ? 1 : -1;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return sign != 0;
        }

        static void Draw(int id, Handle h, Vector3 world, Vector3 dir, float dot, Vector3[][] polygons, Vector3[] line, Brush b)
        {
            bool hot = GUIUtility.hotControl == id, hover = !hot && GUIUtility.hotControl == 0 && HandleUtility.nearestControl == id;
            Color colour = h.radial ? Handles.xAxisColor : AxisColor(h.direction);
            if (line != null)
            {
                // an edge: a thin line, thick and lit under the pointer
                var points = line;
                if (h.closed) { points = new Vector3[line.Length + 1]; line.CopyTo(points, 0); points[line.Length] = line[0]; }
                var c = hot || hover ? Handles.selectedColor : colour;
                if (!hot && !hover) c.a = 0.6f;
                Handles.color = c;
                Handles.DrawAAPolyLine(hot || hover ? 5f : 2.5f, points);
            }
            else if (polygons != null)
            {
                // a side: a faint outline until the pointer is on it, then the whole side lights up
                bool lit = hover || hot;
                var fill = colour; fill.a = hot ? 0.35f : 0.22f;
                var outline = colour; outline.a = lit ? 0.9f : 0.25f;
                foreach (var polygon in polygons)
                {
                    if (lit)
                    {
                        Handles.color = fill;
                        Handles.DrawAAConvexPolygon(polygon);
                    }
                    Handles.color = outline;
                    var loop = new Vector3[polygon.Length + 1];
                    polygon.CopyTo(loop, 0); loop[polygon.Length] = polygon[0];
                    Handles.DrawAAPolyLine(lit ? 2f : 1f, loop);
                }
            }
            else
            {
                Handles.color = hot || hover ? Handles.selectedColor : colour;
                Handles.DotHandleCap(id, world, Quaternion.identity, dot, EventType.Repaint);
            }
            if (hot && s_Dragging && h.label != null && b == s_HotBrush)
                Handles.Label(world + dir * HandleUtility.GetHandleSize(world) * 0.2f, h.label(b), EditorStyles.boldLabel);
        }

        static void Begin(List<Brush> brushes)
        {
            s_Dragging = true;
            s_Start.Clear();
            foreach (var b in brushes) s_Start[b] = State.Of(b);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Change brush shape");
            s_UndoGroup = Undo.GetCurrentGroup();
        }

        static void Release()
        {
            if (s_Dragging) Undo.CollapseUndoOperations(s_UndoGroup);
            if (GUIUtility.hotControl == s_HotControl) GUIUtility.hotControl = 0;
            s_HotControl = 0; s_HotId = null; s_HotBrush = null; s_Dragging = false;
            s_Start.Clear();
        }

        /// <summary>A press on a side that never became a drag is a click: select what is under the pointer, as Unity would.</summary>
        static void Click(Event e)
        {
            Brush brush = BrushHooks.PickBrushSurface(e.mousePosition, out _, out _);
            GameObject picked = brush != null ? brush.gameObject : HandleUtility.PickGameObject(e.mousePosition, false);
            if (e.shift || EditorGUI.actionKey)
            {
                if (picked == null) return;
                var selection = new List<Object>(Selection.objects);
                if (!selection.Remove(picked)) selection.Add(picked);
                Selection.objects = selection.ToArray();
            }
            else Selection.activeGameObject = picked;
        }

        static Color AxisColor(Vector3 localDirection)
        {
            float x = Mathf.Abs(localDirection.x), y = Mathf.Abs(localDirection.y), z = Mathf.Abs(localDirection.z);
            return x >= y && x >= z ? Handles.xAxisColor : y >= z ? Handles.yAxisColor : Handles.zAxisColor;
        }

        static string Length(float metres)
        {
            var s = BrushSettings.instance;
            return (metres * s.unitsPerMeter).ToString("0.###") + " " + s.unitLabel;
        }
    }
}

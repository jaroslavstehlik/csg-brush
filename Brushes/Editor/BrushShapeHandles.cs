using System.Collections.Generic;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Shape handles: dots on a selected procedural brush that change its shape directly in the Scene view, alongside
    /// whichever tool is active (Move, Rotate, Scale), as Godot does for its CSG shapes. Drag a box's side and it
    /// extends while the opposite side stays; drag a cylinder's rim and its radius changes; drag a stair's first step and
    /// its step height changes. The brush stays the shape it is (unlike the Edit Brush mode, which makes it Custom).
    /// Shift drags both sides at once. Values follow the brush's snapping. Each drag is one undo step; with several
    /// brushes selected, the same handle changes all of them.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushShapeHandles
    {
        /// <summary>One handle: where it sits (brush local space), the way it drags, and what a drag does.</summary>
        public struct Handle
        {
            public string id;
            public Vector3 local;
            public Vector3 direction;
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
            void Face(string id, Vector3 local, int axis, int side) =>
                list.Add(new Handle { id = id, local = local, direction = Axis(axis) * side, apply = (br, st, d, sym, snap) => ResizeBox(br, st, axis, side, d, sym, snap), label = br => Length(br.size[axis]) });
            void Round(string id, Vector3 local, int axis, int side) =>
                list.Add(new Handle { id = id, local = local, direction = Axis(axis) * side, apply = (br, st, d, sym, snap) => ResizeRound(br, st, axis, side, d, snap), label = br => "⌀ " + Length(br.size[axis]) });
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
                    Face("-z", new Vector3(c.x, min.y, min.z), 2, -1);                                // the low end (start)
                    Face("+y", new Vector3(c.x, max.y, max.z), 1, 1);                                 // height, on the high edge
                    Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    float wy = min.y + size.y / 3f, wz = max.z - size.z / 3f;                          // the side caps' middles
                    Face("+x", new Vector3(max.x, wy, wz), 0, 1); Face("-x", new Vector3(min.x, wy, wz), 0, -1);
                    break;
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    bool cone = b.shape == BrushShape.Cone;
                    Face("+y", new Vector3(c.x, max.y, c.z), 1, 1);                                   // top cap, or the apex
                    Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    float ry = cone ? min.y : c.y;                                                     // a cone's radius is its base's
                    Round("+x", new Vector3(max.x, ry, c.z), 0, 1); Round("-x", new Vector3(min.x, ry, c.z), 0, -1);
                    Round("+z", new Vector3(c.x, ry, max.z), 2, 1); Round("-z", new Vector3(c.x, ry, min.z), 2, -1);
                    break;
                case BrushShape.Sphere:
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Vector3 plus = c, minus = c; plus[axis] = max[axis]; minus[axis] = min[axis];
                        Round("+" + "xyz"[axis], plus, axis, 1); Round("-" + "xyz"[axis], minus, axis, -1);
                    }
                    break;
                case BrushShape.Stairs: // climbing towards +z
                    int steps = BrushPolyhedron.StepCount(size.y, b.stepHeight);
                    float rise = size.y / steps, run = size.z / steps;
                    Face("+z", new Vector3(c.x, c.y, max.z), 2, 1);
                    Face("-z", new Vector3(c.x, min.y + rise * 0.5f, min.z), 2, -1);
                    Face("+y", new Vector3(c.x, max.y, max.z - run * 0.5f), 1, 1);
                    Face("-y", new Vector3(c.x, min.y, c.z), 1, -1);
                    Face("+x", new Vector3(max.x, c.y, c.z), 0, 1); Face("-x", new Vector3(min.x, c.y, c.z), 0, -1);
                    list.Add(new Handle
                    {
                        id = "step", local = new Vector3(c.x - size.x * 0.25f, min.y + rise, min.z + run * 0.5f), direction = Vector3.up,
                        apply = (br, st, d, sym, snap) => SetStepHeight(br, st, d, snap),
                        label = br => "step " + Length(br.ClampedSize.y / BrushPolyhedron.StepCount(br.ClampedSize.y, br.stepHeight)) + ", " + BrushPolyhedron.StepCount(br.ClampedSize.y, br.stepHeight) + " steps",
                    });
                    break;
                case BrushShape.Arch: // the ring between an outer ellipse and one a thickness in, on the box's floor
                    Face("+x", new Vector3(max.x, min.y, c.z), 0, 1); Face("-x", new Vector3(min.x, min.y, c.z), 0, -1);
                    Face("+y", new Vector3(c.x, max.y, c.z), 1, 1);
                    Face("+z", new Vector3(c.x, max.y - b.wallThickness * 0.5f, max.z), 2, 1); Face("-z", new Vector3(c.x, max.y - b.wallThickness * 0.5f, min.z), 2, -1);
                    list.Add(new Handle
                    {
                        id = "thickness", local = new Vector3(c.x, max.y - b.wallThickness, c.z), direction = Vector3.down,
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

        /// <summary>Curved and spiral stairs: the column, the steps' width, the height, and (curved) the angle they cover.</summary>
        static void AddRoundStairs(Brush b, List<Handle> list)
        {
            bool spiral = b.shape == BrushShape.SpiralStairs;
            float sign = b.counterClockwise ? 1f : -1f;
            float ri = Mathf.Max(0f, b.innerRadius), ro = ri + Mathf.Max(0.001f, b.stepWidth), h = Mathf.Max(0.001f, b.stepHeight);
            int n = Mathf.Max(1, b.numSteps);
            float total = spiral ? Mathf.PI * 2f / Mathf.Max(1, b.stepsPer360) * n : Mathf.Max(1f, b.curveAngle) * Mathf.Deg2Rad;
            float stepAngle = total / n;
            Vector3 At(float radius, float angle, float y) => new Vector3(radius * Mathf.Cos(angle * sign), y, radius * Mathf.Sin(angle * sign));
            float firstTop = b.addToFirstStep + h, lastTop = b.addToFirstStep + n * h;
            float mid = stepAngle * 0.5f;
            list.Add(new Handle
            {
                id = "inner", local = At(ri, mid, firstTop), direction = At(1f, mid, 0f),
                apply = (br, st, d, sym, snap) => { br.innerRadius = Mathf.Max(0f, SnapLength(br, st.innerRadius + d, snap)); },
                label = br => "column " + Length(br.innerRadius),
            });
            list.Add(new Handle
            {
                id = "outer", local = At(ro, mid, firstTop), direction = At(1f, mid, 0f),
                apply = (br, st, d, sym, snap) => { br.stepWidth = Mathf.Max(0.01f, SnapLength(br, st.stepWidth + d, snap)); },
                label = br => "steps " + Length(br.stepWidth) + " wide",
            });
            float lastMid = total - mid;
            list.Add(new Handle
            {
                id = "height", local = At((ri + ro) * 0.5f, lastMid, lastTop), direction = Vector3.up,
                // curved: the height spreads over the same steps; spiral: more height is more steps, going round further
                apply = spiral
                    ? (System.Action<Brush, State, float, bool, bool>)((br, st, d, sym, snap) => { br.numSteps = Mathf.Max(1, Mathf.RoundToInt((st.numSteps * st.stepHeight + d) / Mathf.Max(0.001f, st.stepHeight))); })
                    : (br, st, d, sym, snap) => { br.stepHeight = Mathf.Max(0.01f, SnapStep(st.stepHeight + d / Mathf.Max(1, st.numSteps), snap)); },
                label = br => spiral ? br.numSteps + " steps, " + Length(br.numSteps * br.stepHeight) : "step " + Length(br.stepHeight) + ", " + Length(br.numSteps * br.stepHeight) + " high",
            });
            if (!spiral)
            {
                float end = total;
                list.Add(new Handle
                {
                    id = "angle", local = At(ro, end, lastTop * 0.5f), direction = At(1f, end + Mathf.PI * 0.5f, 0f),
                    apply = (br, st, d, sym, snap) =>
                    {
                        float radius = Mathf.Max(0.01f, st.innerRadius + st.stepWidth);
                        float degrees = st.curveAngle + d / radius * Mathf.Rad2Deg;
                        float stepDegrees = BrushSettings.instance.rotationSnapDegrees;
                        if (snap && stepDegrees > 0f) degrees = Mathf.Round(degrees / stepDegrees) * stepDegrees;
                        br.curveAngle = Mathf.Clamp(degrees, 1f, 3600f);
                    },
                    label = br => br.curveAngle.ToString("0.#") + "°",
                });
            }
        }

        static Vector3 Axis(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

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

        /// <summary>A round shape's diameter on one axis about its centre; round stays round (both horizontal axes, or all three).</summary>
        static void ResizeRound(Brush b, State start, int axis, int side, float distance, bool snap)
        {
            Vector3 startSize = Vector3.Max(start.size, Vector3.one * 0.001f);
            float diameter = Mathf.Max(0.001f, startSize[axis] + 2f * distance);
            if (snap) diameter = SnapLength(b, diameter, true);
            Vector3 startShift = b.PivotShiftFor(startSize);
            Vector3 min = startShift - startSize * 0.5f, max = startShift + startSize * 0.5f;
            void Set(int a)
            {
                float centre = (min[a] + max[a]) * 0.5f;
                min[a] = centre - diameter * 0.5f; max[a] = centre + diameter * 0.5f;
            }
            Set(axis);
            if (b.shape == BrushShape.Sphere)
            {
                for (int a = 0; a < 3; a++) if (a != axis && Mathf.Abs(startSize[a] - startSize[axis]) < 1e-4f) Set(a);
            }
            else if (axis != 1)
            {
                int other = axis == 0 ? 2 : 0;
                if (Mathf.Abs(startSize[other] - startSize[axis]) < 1e-4f) Set(other);
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

        static void SetStepHeight(Brush b, State start, float distance, bool snap)
        {
            int steps = BrushPolyhedron.StepCount(start.size.y, start.stepHeight);
            float rise = start.size.y / steps;
            b.stepHeight = Mathf.Clamp(SnapStep(rise + distance, snap), 0.01f, Mathf.Max(0.01f, start.size.y));
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

        // ---- the Scene view

        static readonly int s_Hash = "BrushShapeHandle".GetHashCode();
        static readonly Dictionary<Brush, State> s_Start = new Dictionary<Brush, State>();
        static int s_ActiveControl, s_UndoGroup;
        static string s_ActiveId;
        static Vector3 s_StartWorld;

        static bool Showing()
        {
            var s = BrushSettings.instance;
            return s.shapeHandles && !BrushEditContext.IsActive && !FloorPlanEditContext.IsActive && BrushCreateTool.Active == null && Tools.current != Tool.View;
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Showing()) { s_Start.Clear(); return; }
            var brushes = new List<Brush>();
            foreach (var go in Selection.gameObjects)
                if (go != null && go.TryGetComponent<Brush>(out var b) && !b.IsGenerated && b.shape != BrushShape.Custom) brushes.Add(b);
            if (brushes.Count == 0 || brushes.Count > 32) return;
            Vector3 gizmo = Tools.handlePosition;
            bool gizmoShown = Tools.current == Tool.Move || Tools.current == Tool.Rotate || Tools.current == Tool.Scale || Tools.current == Tool.Transform;
            var e = Event.current;
            foreach (var b in brushes)
            {
                var toWorld = b.transform.localToWorldMatrix;
                foreach (var h in HandlesOf(b))
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(h.local);
                    Vector3 dir = toWorld.MultiplyVector(h.direction).normalized;
                    float size = HandleUtility.GetHandleSize(world) * 0.06f;
                    // where it would sit on the transform gizmo, move it aside along the face; it still drags the same way
                    if (gizmoShown && (HandleUtility.WorldToGUIPoint(world) - HandleUtility.WorldToGUIPoint(gizmo)).sqrMagnitude < 24f * 24f)
                        world += Aside(toWorld, h.direction) * HandleUtility.GetHandleSize(world) * 0.45f;
                    int id = GUIUtility.GetControlID(s_Hash, FocusType.Passive);
                    bool hot = GUIUtility.hotControl == id;
                    Handles.color = hot || HandleUtility.nearestControl == id ? Handles.selectedColor : AxisColor(h.direction);
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider(id, world, dir, size, Handles.DotHandleCap, 0f);
                    if (GUIUtility.hotControl == id && s_ActiveControl != id) Begin(id, h.id, world, brushes);
                    if (EditorGUI.EndChangeCheck() && s_ActiveControl == id)
                    {
                        float distance = Vector3.Dot(moved - s_StartWorld, dir);
                        foreach (var other in brushes)
                            if (s_Start.TryGetValue(other, out var start)) Apply(other, start, s_ActiveId, distance, e.shift);
                    }
                    if (s_ActiveControl == id && h.label != null)
                        Handles.Label(world + dir * size * 3f, h.label(b), EditorStyles.boldLabel);
                }
            }
            if (s_ActiveControl != 0 && GUIUtility.hotControl != s_ActiveControl) End();
        }

        static void Begin(int control, string handleId, Vector3 world, List<Brush> brushes)
        {
            s_ActiveControl = control;
            s_ActiveId = handleId;
            s_StartWorld = world;
            s_Start.Clear();
            foreach (var b in brushes) s_Start[b] = State.Of(b);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Change brush shape");
            s_UndoGroup = Undo.GetCurrentGroup();
        }

        static void End()
        {
            Undo.CollapseUndoOperations(s_UndoGroup);
            s_ActiveControl = 0;
            s_ActiveId = null;
            s_Start.Clear();
        }

        /// <summary>A direction across the handle's face, to move it aside from the transform gizmo.</summary>
        static Vector3 Aside(Matrix4x4 toWorld, Vector3 localDirection)
        {
            Vector3 across = Mathf.Abs(localDirection.x) < 0.5f ? Vector3.right : Vector3.forward;
            return toWorld.MultiplyVector(across).normalized;
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

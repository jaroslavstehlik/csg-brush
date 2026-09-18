using System;
using UnityEngine;
using CsgBrush.Colliders;

namespace CsgBrush
{
    public enum BrushShape
    {
        Box = 0,
        /// <summary>Ramp: rises along the local Z axis.</summary>
        Wedge = 1,
        Cylinder = 2,
        Cone = 3,
        Sphere = 4,
        /// <summary>Linear stairs climbing along the local Z axis.</summary>
        [InspectorName("Linear Stairs")] Stairs = 5,
        /// <summary>Edited by hand: the shape is the polyhedron on the brush, decomposed into convex pieces.</summary>
        Custom = 6,
        /// <summary>Steps wrapping around an inner column over an angle; each step a solid block from the floor.</summary>
        [InspectorName("Curved Stairs")] CurvedStairs = 7,
        /// <summary>Separate step slabs wrapping around an inner column, possibly several turns.</summary>
        [InspectorName("Spiral Stairs")] SpiralStairs = 8,
        /// <summary>An arch filling its box: a ring of segments between an outer and an inner ellipse, standing on the floor.</summary>
        Arch = 9,
    }

    public enum BrushOperation
    {
        Add = 0,
        Subtract = 1,
    }

    /// <summary>
    /// A convex level-building volume. Place and rotate it with the normal Unity tools; its size lives on
    /// this component and is centred on the transform, like a BoxCollider. Rendering and collision are
    /// generated automatically (CSG through the Manifold library, one convex collider per piece). Sizes are stored in
    /// metres; the Inspector shows them in the world preset's units.
    /// </summary>
    [AddComponentMenu("Brush")]
    [DisallowMultipleComponent]
    [SelectionBase]
    [ExecuteAlways]
    public sealed class Brush : MonoBehaviour
    {
        public const string ShapeChildName = "<[shape]>";
        public const string HollowChildName = "<[hollow]>";
        /// <summary>Custom shapes are decomposed into convex pieces, one hidden child each: "<[piece 0]>", "<[piece 1]>", ...</summary>
        public const string PieceChildPrefix = "<[piece ";

        public static bool IsGeneratedChildName(string name) => name == ShapeChildName || name == HollowChildName || name.StartsWith(PieceChildPrefix);

        /// <summary>The editable shape when <see cref="shape"/> is Custom. May be concave; convex pieces are derived from it.</summary>
        [HideInInspector] public BrushPolyhedron polyhedron = new BrushPolyhedron();
        /// <summary>The parametric shape a Custom brush was converted from, for "Reset to ...".</summary>
        [HideInInspector] public BrushShape customFrom = BrushShape.Box;
        /// <summary>Set by the editor layer when the shape (or one of its convex parts) cannot be built; shown in the Inspector and the Scene view, never in the console.</summary>
        [NonSerialized] public string problem;

        public BrushShape shape = BrushShape.Box;
        public BrushOperation operation = BrushOperation.Add;
        public ControllerSurface.Kind surface = ControllerSurface.Kind.Solid;
        public bool noFallDamage;

        [Tooltip("Metres, centred on the transform.")]
        public Vector3 size = new Vector3(2f, 2f, 2f);

        [Tooltip("Box and cylinder: keep only the walls.")]
        public bool hollow;
        [Tooltip("Hollow walls, and the thickness of an arch: metres.")]
        public float wallThickness = 0.5f;

        [Tooltip("Cylinder and cone; segments of an arch.")]
        [Min(3)] public int sides = 16;
        [Tooltip("Sphere: 1 is coarse, 5 is smooth.")]
        [Range(1, 5)] public int tessellation = 2;
        [Tooltip("Stairs: metres.")]
        public float stepHeight = 0.5f;
        [Tooltip("Linear stairs: length of each step along the run, metres.")]
        public float stepDepth = 1f;
        [Tooltip("Curved and spiral stairs: radius of the inner column the steps wrap around, metres.")]
        public float innerRadius = 0.5f;
        [Tooltip("Curved and spiral stairs: width of the steps out from the column, metres.")]
        public float stepWidth = 1.5f;
        [Tooltip("Spiral stairs: thickness of each step slab, metres.")]
        public float stepThickness = 0.25f;
        [Tooltip("Curved stairs: total angle the steps cover; arch: the angle it spans, up to 180. Degrees.")]
        public float curveAngle = 90f;
        [Tooltip("Curved and spiral stairs.")]
        [Min(1)] public int numSteps = 8;
        [Tooltip("Spiral stairs: steps in one full turn.")]
        [Min(1)] public int stepsPer360 = 16;
        [Tooltip("Curved and spiral stairs: extra height under the first step (negative lowers it), metres.")]
        public float addToFirstStep = 0f;
        public bool counterClockwise;
        [Tooltip("Spiral stairs: the treads slope instead of stepping (a spiral ramp).")]
        public bool slopedFloor;
        [Tooltip("Spiral stairs: the underside slopes instead of stepping.")]
        public bool slopedCeiling;

        [Tooltip("Applied to every face. Per-face materials can be dropped onto faces in the Scene view.")]
        public Material material;

        /// <summary>Set by the editor layer; called when the component needs to push its values into the generated structure.</summary>
        public static Action<Brush> SyncRequested;

        public Vector3 ClampedSize
        {
            get
            {
                const float min = 0.001f;
                return new Vector3(Mathf.Max(size.x, min), Mathf.Max(size.y, min), Mathf.Max(size.z, min));
            }
        }

        public bool SupportsHollow => shape == BrushShape.Box || shape == BrushShape.Cylinder;

        /// <summary>Shapes whose size follows from their parameters (like a Custom shape follows its vertices).</summary>
        public bool HasParametricSize => shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs;

        /// <summary>The stair parameters as the polyhedron generators take them.</summary>
        public StairParams Stairs => new StairParams
        {
            innerRadius = innerRadius, stepWidth = stepWidth, stepHeight = stepHeight, stepThickness = stepThickness, curveAngle = curveAngle,
            numSteps = numSteps, stepsPer360 = stepsPer360, addToFirstStep = addToFirstStep, counterClockwise = counterClockwise, slopedFloor = slopedFloor, slopedCeiling = slopedCeiling,
        };

        /// <summary>True when a hollow (subtractive) child should exist for this brush.</summary>
        public bool IsHollow => hollow && SupportsHollow;

        void OnValidate()
        {
            if (size.x < 0f) size.x = 0f;
            if (size.y < 0f) size.y = 0f;
            if (size.z < 0f) size.z = 0f;
            if (wallThickness < 0f) wallThickness = 0f;
            if (stepHeight < 0.001f) stepHeight = 0.001f;
            if (stepDepth < 0.001f) stepDepth = 0.001f;
            if (innerRadius < 0f) innerRadius = 0f;
            if (stepWidth < 0.001f) stepWidth = 0.001f;
            if (stepThickness < 0.001f) stepThickness = 0.001f;
            if (curveAngle < 1f) curveAngle = 1f;
            if (numSteps < 1) numSteps = 1;
            if (stepsPer360 < 1) stepsPer360 = 1;
            SyncRequested?.Invoke(this);
        }

        void OnEnable()
        {
            SyncRequested?.Invoke(this);
        }

        void OnDrawGizmosSelected()
        {
            // The wire box follows the hand during a drag, including the Scale tool: the transform scale is what
            // the size will become once the drag is released and the scale is baked into the size.
            var scale = transform.lossyScale;
            var shown = Vector3.Scale(ClampedSize, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = operation == BrushOperation.Subtract ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            if (shape == BrushShape.Custom && polyhedron != null && polyhedron.IsValid)
            {
                var sc = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                foreach (var face in polyhedron.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Gizmos.DrawLine(Vector3.Scale(polyhedron.vertices[face.indices[i]], sc), Vector3.Scale(polyhedron.vertices[face.indices[(i + 1) % face.indices.Length]], sc));
                return;
            }
            if (HasParametricSize)
            {
                // the axis is the transform: draw the steps and a mark on the axis
                var poly = shape == BrushShape.CurvedStairs ? BrushPolyhedron.CurvedStairs(Stairs) : BrushPolyhedron.SpiralStairs(Stairs);
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Gizmos.DrawLine(poly.vertices[face.indices[i]], poly.vertices[face.indices[(i + 1) % face.indices.Length]]);
                var b = poly.Bounds();
                Gizmos.DrawLine(new Vector3(0f, b.min.y, 0f), new Vector3(0f, b.max.y, 0f));
                return;
            }
            Gizmos.DrawWireCube(Vector3.zero, shown);
        }
    }
}

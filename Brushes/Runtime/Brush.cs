using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CsgBrush.Colliders;

namespace CsgBrush
{
    /// <summary>How a brush's pivot is measured.</summary>
    public enum PivotMode
    {
        [Tooltip("0 to 1 of the size: follows the brush when resized.")] Normalized,
        [Tooltip("Distance from the brush's left, bottom, back corner: stays put when resized.")] Absolute,
    }

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
        /// <summary>A doorway to cut into a wall: a box of the opening's size, subtract by new. On a floor plan it rides on a wall (see <see cref="WallAnchor"/>).</summary>
        Door = 10,
        /// <summary>A window to cut into a wall: as <see cref="Door"/>, standing on a sill.</summary>
        Window = 11,
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
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/Brush.png")]
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
        [Tooltip("Solid geometry, a trigger volume, or no collider at all. A module on the brush (water, say) may override it.")]
        public ColliderKind collision = ColliderKind.Solid;
        [Tooltip("On every collider piece of the brush, triggers included: friction and bounce, and a handle for sounds or other lookups.")]
        public PhysicsMaterial physicsMaterial;
        [Tooltip("The pieces provide contact data to OnCollision callbacks (Unity's Provide Contacts).")]
        public bool provideContacts;
        // the surface kind and fall-damage flag of earlier versions, migrated into modules on the next sync (see LegacySurfaceMigration)
        [SerializeField, HideInInspector, FormerlySerializedAs("surface")] int legacySurface;
        [SerializeField, HideInInspector, FormerlySerializedAs("noFallDamage")] bool legacyNoFallDamage;

        [Tooltip("Metres.")]
        public Vector3 size = new Vector3(2f, 2f, 2f);

        /// <summary>
        /// Where the transform sits in the shape's box, per axis, measured from its left, bottom, back corner (the back is
        /// the side away from where the brush faces): a fraction of the size, or metres (see <see cref="pivotMode"/>).
        /// Curved and spiral stairs, doors and windows keep theirs; a Custom shape's comes from its vertices.
        /// </summary>
        public Vector3 pivot = new Vector3(0.5f, 0.5f, 0.5f);
        /// <summary>How <see cref="pivot"/> is measured.</summary>
        public PivotMode pivotMode = PivotMode.Normalized;

        /// <summary>Box-like shapes whose geometry follows <see cref="pivot"/>.</summary>
        public bool HasPivot => shape != BrushShape.Custom && !HasParametricSize && !IsOpening;

        /// <summary>The pivot in metres from the box's left, bottom, back corner for a box of <paramref name="boxSize"/>, kept inside the box.</summary>
        public Vector3 PivotDistance(Vector3 boxSize)
        {
            var d = pivotMode == PivotMode.Normalized ? Vector3.Scale(pivot, boxSize) : pivot;
            return new Vector3(Mathf.Clamp(d.x, 0f, boxSize.x), Mathf.Clamp(d.y, 0f, boxSize.y), Mathf.Clamp(d.z, 0f, boxSize.z));
        }

        /// <summary>How far a shape of <paramref name="boxSize"/> sits from the transform in local space: none at the centre.</summary>
        public Vector3 PivotShiftFor(Vector3 boxSize) => HasPivot ? boxSize * 0.5f - PivotDistance(boxSize) : Vector3.zero;

        /// <summary>How far the shape sits from the transform in local space: none at the centre.</summary>
        public Vector3 PivotShift => PivotShiftFor(ClampedSize);

        [Tooltip("Box and cylinder: keep only the walls.")]
        public bool hollow;
        [Tooltip("Hollow walls, and the thickness of an arch: metres.")]
        public float wallThickness = 0.5f;

        [Tooltip("Cylinder and cone; segments of an arch.")]
        [Min(3)] public int sides = 16;
        [Tooltip("Sphere: 1 is coarse, 5 is smooth.")]
        [Range(1, 5)] public int tessellation = 2;
        [Tooltip("Stairs: height of a step, metres. The number of steps comes from the stairs' height.")]
        public float stepHeight = 0.25f;
        [Tooltip("Curved and spiral stairs: radius of the inner column the steps wrap around, metres.")]
        public float innerRadius = 0.5f;
        [Tooltip("Curved and spiral stairs: width of the steps out from the column, metres.")]
        public float stepWidth = 1.5f;
        [Tooltip("Spiral stairs, and stairs without support under the steps: thickness of each step slab, metres.")]
        public float stepThickness = 0.1f;
        [Tooltip("Linear and curved stairs: the steps stand on solid support down to the floor.")]
        public bool supportUnderSteps = true;
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

        /// <summary>The generator that made this brush (a floor plan's wall, say), or null for a brush you authored.</summary>
        [HideInInspector] public BrushGenerator generatedBy;

        /// <summary>Made by a <see cref="BrushGenerator"/>: never snapped, picked or edited on its own.</summary>
        public bool IsGenerated => generatedBy != null;

        /// <summary>A door or window: a cut of a fixed size, not snapped to the grid, always clickable.</summary>
        public bool IsOpening => shape == BrushShape.Door || shape == BrushShape.Window;

        /// <summary>Set by a <see cref="WallAnchor"/> on the brush or above it: the brush rides on a wall.</summary>
        [NonSerialized] public bool anchored;

        /// <summary>Placed by something else (its generator, its wall): never snapped to the grid.</summary>
        public bool IsPlaced => generatedBy != null || IsOpening || anchored;

        // ------------------------------------------------------------------ registry

        static readonly List<Brush> s_Active = new List<Brush>();
        [NonSerialized] int m_ActiveSlot; // index in s_Active plus one; 0 when not registered
        [NonSerialized] Transform m_Transform;

        /// <summary>Editor-side data derived from the brush (pick shape, build key); dropped whenever the brush changes. Lives and dies with the brush, so it needs no dictionary and no cleanup.</summary>
        [NonSerialized] internal object editorCache;

        /// <summary>The brush's transform without a call into Unity: for loops over every brush each frame.</summary>
        public Transform CachedTransform => m_Transform != null ? m_Transform : (m_Transform = transform);

        /// <summary>
        /// Every enabled brush on an active GameObject: in all loaded scenes, in Prefab Mode and in prefab contents
        /// loaded for building, never in prefab assets. Kept by OnEnable and OnDisable, so a domain reload rebuilds it and
        /// scene unloads, deletes and deactivation empty it. Unordered. Iterate by index: a foreach allocates.
        /// </summary>
        public static IReadOnlyList<Brush> Active => s_Active;

        /// <summary>Incremented whenever a brush joins or leaves <see cref="Active"/>; lets callers cache what they derive from it.</summary>
        public static int ActiveVersion { get; private set; }

        void Register()
        {
            if (m_ActiveSlot != 0) return;
            m_Transform = transform;
            s_Active.Add(this);
            m_ActiveSlot = s_Active.Count;
            ActiveVersion++;
        }

        void Unregister()
        {
            int i = m_ActiveSlot - 1;
            if (i < 0) return;
            m_ActiveSlot = 0;
            if (i >= s_Active.Count || !ReferenceEquals(s_Active[i], this)) { s_Active.Remove(this); ActiveVersion++; return; } // defensive: never out of step
            int last = s_Active.Count - 1;
            if (i != last) { var moved = s_Active[last]; s_Active[i] = moved; moved.m_ActiveSlot = i + 1; } // swap-remove: O(1)
            s_Active.RemoveAt(last);
            ActiveVersion++;
        }

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
            numSteps = numSteps, stepsPer360 = stepsPer360, addToFirstStep = addToFirstStep, counterClockwise = counterClockwise, slopedFloor = slopedFloor, slopedCeiling = slopedCeiling, open = !supportUnderSteps,
        };

        /// <summary>True when a hollow (subtractive) child should exist for this brush.</summary>
        public bool IsHollow => hollow && SupportsHollow;

        // ---- modules

        /// <summary>The modules on this brush's object and its parents (own first): the game's data on the brush.</summary>
        public BrushModule[] Modules() => GetComponentsInParent<BrushModule>(false);

        /// <summary>What the volume is to physics once the modules have had their say.</summary>
        public ColliderKind EffectiveCollision()
        {
            foreach (var m in Modules()) if (m != null && m.enabled && m.OverrideCollision(out var o)) return o;
            return collision;
        }

        /// <summary>Everything the modules put on the pieces, hashed: part of each piece's identity.</summary>
        public int ModuleFingerprint()
        {
            unchecked
            {
                int h = (int)EffectiveCollision();
                h = h * 31 + (physicsMaterial != null ? physicsMaterial.name.GetHashCode() : 0) + (provideContacts ? 7 : 0);
                h = h * 31 + gameObject.tag.GetHashCode();
                foreach (var m in Modules())
                {
                    if (m == null || !m.enabled) continue;
                    h = h * 31 + m.GetType().FullName.GetHashCode();
                    h = h * 31 + m.Fingerprint();
                }
                return h;
            }
        }

        // ---- legacy surface data (versions before modules)

        /// <summary>
        /// Registered by a game's module package: given the old surface kind (0 solid, 1 slick, 2 water, 3 trigger,
        /// 4 no collision) and fall-damage flag, add the matching module and return true. Trigger and no-collision
        /// are handled by the brush itself.
        /// </summary>
        public static Func<Brush, int, bool, bool> LegacySurfaceMigration;
        public bool HasLegacySurface => legacySurface != 0 || legacyNoFallDamage;
        public (int surface, bool noFallDamage) LegacySurface => (legacySurface, legacyNoFallDamage);
        public void ClearLegacySurface() { legacySurface = 0; legacyNoFallDamage = false; }

        // ---- trigger events, one per brush however many pieces it is made of

        /// <summary>A collider entered the brush's volume (raised once, when it enters the first piece).</summary>
        public event Action<Collider> TriggerEntered;
        /// <summary>A collider left the brush's volume (raised once, when it leaves the last piece).</summary>
        public event Action<Collider> TriggerExited;
        readonly Dictionary<Collider, int> insidePieces = new Dictionary<Collider, int>();

        /// <summary>Called by the relay on a trigger piece.</summary>
        public void PieceTriggerEnter(Collider other)
        {
            insidePieces.TryGetValue(other, out int n);
            insidePieces[other] = n + 1;
            if (n > 0) return;
            TriggerEntered?.Invoke(other);
            foreach (var l in GetComponents<IBrushTriggerListener>()) l.OnBrushTriggerEnter(this, other);
            gameObject.SendMessage("OnTriggerEnter", other, SendMessageOptions.DontRequireReceiver); // scripts on the brush, as on any trigger
        }

        readonly Dictionary<Collider, (int frame, float step)> lastStay = new Dictionary<Collider, (int, float)>();

        /// <summary>Called by the relay on a trigger piece: OnTriggerStay on the brush object, once per physics step whatever the pieces.</summary>
        public void PieceTriggerStay(Collider other)
        {
            var now = (Time.frameCount, Time.fixedTime);
            if (lastStay.TryGetValue(other, out var last) && last == now) return;
            lastStay[other] = now;
            gameObject.SendMessage("OnTriggerStay", other, SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>Called by the relay on a trigger piece.</summary>
        public void PieceTriggerExit(Collider other)
        {
            if (!insidePieces.TryGetValue(other, out int n)) return;
            if (n > 1) { insidePieces[other] = n - 1; return; }
            insidePieces.Remove(other);
            lastStay.Remove(other);
            TriggerExited?.Invoke(other);
            foreach (var l in GetComponents<IBrushTriggerListener>()) l.OnBrushTriggerExit(this, other);
            gameObject.SendMessage("OnTriggerExit", other, SendMessageOptions.DontRequireReceiver);
        }

        void OnValidate()
        {
            if (size.x < 0f) size.x = 0f;
            if (size.y < 0f) size.y = 0f;
            if (size.z < 0f) size.z = 0f;
            if (wallThickness < 0f) wallThickness = 0f;
            if (stepHeight < 0.001f) stepHeight = 0.001f;
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
            Register();
            SyncRequested?.Invoke(this);
        }

        void OnDisable() => Unregister();

        void OnDrawGizmosSelected()
        {
            // The wire box follows the hand during a drag, including the Scale tool: the transform scale is what
            // the size will become once the drag is released and the scale is applied to the size.
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
            Gizmos.DrawWireCube(Vector3.Scale(PivotShift, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))), shown);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>Pure geometry of drawing a brush: a base rectangle on a plane, then a height. Testable without a Scene view.</summary>
    public static class BrushDraw
    {
        /// <summary>Frame of a drawing plane from its (cardinal) normal: local Y is the normal, X and Z lie in the plane.</summary>
        public static Quaternion PlaneRotation(Vector3 normal)
        {
            normal = Cardinal(normal);
            Vector3 right = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 forward = Vector3.Cross(right, normal);
            return Quaternion.LookRotation(forward, normal);
        }

        /// <summary>The nearest world axis to a direction.</summary>
        public static Vector3 Cardinal(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(n.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(n.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(n.z));
        }

        /// <summary>A point snapped to the grid in the plane's frame (the origin itself is on the grid).</summary>
        public static Vector3 SnapInPlane(Vector3 point, Vector3 origin, Quaternion planeRotation, float grid)
        {
            var local = Quaternion.Inverse(planeRotation) * (point - origin);
            local = BrushSnap.Round(local, grid);
            local.y = 0f;
            return origin + planeRotation * local;
        }

        /// <summary>A point snapped to the grid within its plane: the coordinate along the (cardinal) normal is kept, so a grid moved off the grid steps still holds it.</summary>
        public static Vector3 SnapOnPlane(Vector3 point, Vector3 normal, float grid)
        {
            var snapped = BrushSnap.Round(point, grid);
            var axis = Cardinal(normal);
            axis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            return snapped + Vector3.Scale(axis, point - snapped);
        }

        /// <summary>
        /// Pose of the brush for a base from <paramref name="origin"/> to <paramref name="opposite"/> (both on the plane)
        /// and a signed height along the plane normal. Sizes are positive; a zero extent becomes one grid step.
        /// </summary>
        public static void Pose(Vector3 origin, Vector3 opposite, float height, Quaternion planeRotation, float grid, out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
            float dx = local.x, dz = local.z;
            float step = grid > 0f ? grid : 0.5f;
            if (Mathf.Abs(dx) < 1e-6f) dx = step;
            if (Mathf.Abs(dz) < 1e-6f) dz = step;
            if (Mathf.Abs(height) < 1e-6f) height = step;
            size = new Vector3(Mathf.Abs(dx), Mathf.Abs(height), Mathf.Abs(dz));
            centre = origin + planeRotation * new Vector3(dx * 0.5f, height * 0.5f, dz * 0.5f);
            rotation = planeRotation;
        }

        /// <summary>
        /// Pose of a round brush drawn from its base centre: the drag sets the radius, the height the size along the
        /// normal (a sphere with no height is round). The transform sits at the middle of the height.
        /// </summary>
        public static void CentredPose(Vector3 origin, Vector3 opposite, float height, Quaternion planeRotation, float grid, bool sphere, out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
            float step = grid > 0f ? grid : 0.5f;
            float radius = Mathf.Max(step, grid > 0f ? BrushSnap.Round(new Vector2(local.x, local.z).magnitude, grid) : new Vector2(local.x, local.z).magnitude);
            float h = height;
            if (Mathf.Abs(h) < 1e-6f) h = sphere ? radius * 2f : step;
            size = new Vector3(radius * 2f, Mathf.Abs(h), radius * 2f);
            centre = origin + planeRotation * new Vector3(0f, h * 0.5f, 0f);
            rotation = planeRotation;
        }

        /// <summary>Signed distance along the plane normal from <paramref name="corner"/> to the point on that line nearest the ray.</summary>
        public static float HeightFromRay(Vector3 corner, Vector3 normal, Ray ray)
        {
            // closest points between the line (corner, normal) and the ray
            Vector3 w = corner - ray.origin;
            float a = Vector3.Dot(normal, normal), b = Vector3.Dot(normal, ray.direction), c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(normal, w), e = Vector3.Dot(ray.direction, w);
            float denom = a * c - b * b;
            if (Mathf.Abs(denom) < 1e-8f) return 0f; // parallel: no height information
            return (b * e - c * d) / denom;
        }
    }

    /// <summary>
    /// Draw a brush the ProBuilder way: press on a surface (a brush face, else the Scene view grid), drag the base rectangle,
    /// release, move the mouse to set the height, click to create. Escape cancels the current shape or leaves the tool.
    /// One tool per shape; they share a toolbar button with a shape dropdown.
    /// </summary>
    public abstract class BrushCreateTool : EditorTool
    {
        /// <summary>Operation of the brushes drawn next (Project Settings > Brushes).</summary>
        public static BrushOperation Operation { get => BrushSettings.instance.newOperation; set => BrushSettings.instance.newOperation = value; }

        /// <summary>The Create tool that is active, if any.</summary>
        public static BrushCreateTool Active { get; private set; }

        public abstract BrushShape Shape { get; }
        /// <summary>Exactly what the toolbar shows: "Box Brush", "Curved Stairs Brush".</summary>
        public abstract string Title { get; }

        enum State { Idle, Base, Height }
        State state;
        Vector3 origin, opposite, normal; Quaternion planeRotation; float height;
        Vector3 hoverPoint; bool hoverValid;
        FloorPlan wallPlan;
        int controlId;
        // Not cached: Unity keeps tool instances across domain reloads, and a cached GUIContent would keep an old tooltip.
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get(Shape.ToString()), Title);

        public override void OnActivated() { state = State.Idle; Active = this; m_OpeningValid = false; }
        public override void OnWillBeDeactivated() { state = State.Idle; if (Active == this) Active = null; }

        /// <summary>Step sizes and wall thickness for new brushes: the settings, or grid-derived defaults when left at 0.</summary>
        public static void NewBrushParameters(out float stepHeight, out float wallThickness)
        {
            var s = BrushSettings.instance;
            stepHeight = s.NewStepHeightMeters;
            wallThickness = s.newWallThickness > 0f ? s.ToMeters(s.newWallThickness) : s.GridMeters;
        }

        /// <summary>Curved and spiral stairs are placed by their axis: press on the axis, drag the outer radius, then the height.</summary>
        public static bool IsRadial(BrushShape shape) => shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs;

        /// <summary>Round brushes are drawn from the centre of their base: press on the centre, drag the radius, then the height.</summary>
        public static bool IsCentred(BrushShape shape) => shape == BrushShape.Cylinder || shape == BrushShape.Cone || shape == BrushShape.Sphere;

        /// <summary>Parameters of a radial shape drawn with an outer radius and a total height: step width from the radius, the number of steps from the height and the step height, the rest from the panel.</summary>
        public static BrushGeometry.ShapeParams ParametersForRadial(BrushShape shape, float outerRadius, float height)
        {
            var s = BrushSettings.instance;
            float grid = s.GridMeters, step = grid > 0f ? grid : 0.01f;
            var p = ParametersFor(shape, new Vector3(outerRadius, height, outerRadius));
            var st = p.stairs;
            st.stepWidth = Mathf.Max(step, BrushSnap.Round(outerRadius - st.innerRadius, grid));
            st.numSteps = BrushPolyhedron.StepCount(height, st.stepHeight);
            p.stairs = st;
            return p;
        }

        /// <summary>
        /// The parameters a brush drawn with the given size gets. Stairs take their number of steps from the drawn height and
        /// the step height; curved and spiral stairs take their step width from the drawn footprint.
        /// </summary>
        public static BrushGeometry.ShapeParams ParametersFor(BrushShape shape, Vector3 size)
        {
            var s = BrushSettings.instance;
            NewBrushParameters(out float stepHeight, out _);
            float grid = s.GridMeters;
            var p = BrushGeometry.ShapeParams.Default(size, s.newSides);
            p.tessellation = s.newTessellation; p.stepHeight = stepHeight;
            var st = p.stairs;
            st.innerRadius = s.newInnerRadius > 0f ? s.ToMeters(s.newInnerRadius) : grid;
            st.stepThickness = s.newStepThickness > 0f ? s.ToMeters(s.newStepThickness) : BrushSettings.DefaultStepThicknessMeters;
            st.curveAngle = Mathf.Max(1f, s.newCurveAngle); st.stepsPer360 = Mathf.Max(1, s.newStepsPer360);
            st.counterClockwise = s.newCounterClockwise; st.slopedFloor = s.newSlopedFloor; st.slopedCeiling = s.newSlopedCeiling;
            if (shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                // steps from the drawn height; step width so the footprint's x extent matches the drawn one
                st.stepHeight = stepHeight;
                st.numSteps = BrushPolyhedron.StepCount(size.y, stepHeight);
                st.stepWidth = 1f;
                var trial = shape == BrushShape.CurvedStairs ? BrushPolyhedron.CurvedStairs(st) : BrushPolyhedron.SpiralStairs(st);
                float trialX = trial.Bounds().size.x, ro = st.innerRadius + 1f;
                float targetRo = trialX > 1e-4f ? ro * size.x / trialX : ro;
                st.stepWidth = Mathf.Max(grid > 0f ? grid : 0.01f, BrushSnap.Round(targetRo - st.innerRadius, grid));
            }
            if (shape == BrushShape.Arch) { st.curveAngle = Mathf.Clamp(s.newArchAngle, 1f, 180f); NewBrushParameters(out _, out p.wallThickness); }
            p.stairs = st;
            return p;
        }

        static float Grid => BrushSettings.instance.snapToGrid ? BrushSettings.instance.GridMeters : 0f;

        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView)) return;
            var e = Event.current;
            controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);
            if (e.alt || Tools.viewToolActive) return;
            if (IsOpening(Shape)) { OpeningGUI(e); return; }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (state == State.Idle) ToolManager.RestorePreviousPersistentTool();
                state = State.Idle; hoverValid = false; e.Use(); SceneView.RepaintAll(); return;
            }
            if (state == State.Height && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space))
            {
                Finish(); e.Use(); return;
            }

            switch (state)
            {
                case State.Idle:
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
                    {
                        hoverValid = FindPlane(e.mousePosition, out var p, out var n);
                        if (hoverValid) { hoverPoint = BrushDraw.SnapOnPlane(p, n, Grid); }
                        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseDown && e.button == 0 && hoverValid && HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        FindPlane(e.mousePosition, out _, out normal);
                        var pressed = BrushHooks.PickBrushSurface(e.mousePosition, out _, out _);
                        wallPlan = pressed != null && Mathf.Abs(normal.y) < 0.5f ? pressed.generatedBy as FloorPlan : null; // drawn on a plan's wall: rides on it
                        normal = BrushDraw.Cardinal(normal);
                        planeRotation = BrushDraw.PlaneRotation(normal);
                        origin = hoverPoint; opposite = origin; height = 0f;
                        state = State.Base; e.Use();
                    }
                    if (e.type == EventType.Repaint && hoverValid)
                    {
                        Handles.color = Color.white;
                        Handles.DotHandleCap(-1, hoverPoint, Quaternion.identity, HandleUtility.GetHandleSize(hoverPoint) * 0.04f, EventType.Repaint);
                    }
                    break;

                case State.Base:
                    if (e.type == EventType.MouseDrag && e.button == 0)
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var plane = new Plane(normal, origin);
                        if (plane.Raycast(ray, out float t)) opposite = BrushDraw.SnapInPlane(ray.GetPoint(t), origin, planeRotation, Grid);
                        e.Use(); SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseUp && e.button == 0)
                    {
                        GUIUtility.hotControl = 0; e.Use();
                        var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
                        float min = Grid > 0f ? Grid * 0.5f : 0.05f;
                        bool tooSmall = IsRadial(Shape) || IsCentred(Shape) ? new Vector2(local.x, local.z).magnitude < min : Mathf.Abs(local.x) < min && Mathf.Abs(local.z) < min;
                        if (tooSmall) { state = State.Idle; break; } // a click, not a drag
                        state = State.Height; height = 0f;
                    }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;

                case State.Height:
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        float h = BrushDraw.HeightFromRay(IsRadial(Shape) || IsCentred(Shape) ? origin : opposite, normal, ray);
                        height = Grid > 0f ? BrushSnap.Round(h, Grid) : h;
                        e.Use(); SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseDown && e.button == 0) { GUIUtility.hotControl = controlId; e.Use(); }
                    if (e.type == EventType.MouseUp && e.button == 0) { GUIUtility.hotControl = 0; Finish(); e.Use(); }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;
            }
        }

        /// <summary>Doors and windows are placed with one click, at a size of their own: on a floor plan's wall, into a brush's side, or on the floor.</summary>
        public static bool IsOpening(BrushShape shape) => shape == BrushShape.Door || shape == BrushShape.Window;

        // not kept across a domain reload (Unity keeps tool instances and would restore the flag without the placement)
        [NonSerialized] WallAnchors.Placement m_Opening; [NonSerialized] bool m_OpeningValid;

        void OpeningGUI(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { ToolManager.RestorePreviousPersistentTool(); e.Use(); return; }
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                m_OpeningValid = WallAnchors.Find(HandleUtility.GUIPointToWorldRay(e.mousePosition), Shape, out m_Opening);
                if (e.type == EventType.MouseMove) SceneView.RepaintAll();
            }
            if (e.type == EventType.MouseDown && e.button == 0 && m_OpeningValid && HandleUtility.nearestControl == controlId)
            {
                var brush = WallAnchors.Create(Shape, m_Opening);
                BrushApi.ForceUpdate();
                Selection.activeGameObject = brush.gameObject;
                e.Use();
            }
            var q = m_Opening.rotation;
            if (e.type == EventType.Repaint && m_OpeningValid && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > 0.5f)
            {
                var poly = BrushPolyhedron.Box(m_Opening.size);
                var m = Matrix4x4.TRS(m_Opening.position, m_Opening.rotation, Vector3.one);
                Handles.color = m_Opening.plan != null ? new Color(1f, 0.78f, 0.2f, 0.95f) : new Color(1f, 0.4f, 0.2f, 0.9f); // on a plan's wall: the plan's colour
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 2f);
            }
        }

        /// <summary>The surface under the mouse: a brush face, else the Scene view grid (see <see cref="GridPlane"/>). Nothing past the grid's horizon.</summary>
        internal static bool FindPlane(Vector2 mouse, out Vector3 point, out Vector3 normal)
        {
            if (BrushHooks.PickBrushSurface(mouse, out point, out normal) != null) return true;
            var view = SceneView.lastActiveSceneView;
            if (view == null) { point = Vector3.zero; normal = Vector3.up; return false; }
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = GridPlane(view);
            normal = plane.normal;
            if (plane.Raycast(ray, out float t) && t > 0f) { point = ray.GetPoint(t); return true; }
            point = Vector3.zero; return false;
        }

        /// <summary>
        /// The plane to draw on in empty space, as ProBuilder does: the grid the Scene view shows (its axis and position),
        /// facing the camera; with the grid hidden, a plane through the view pivot, horizontal unless the view looks level.
        /// </summary>
        static Plane GridPlane(SceneView view)
        {
            var camera = view.camera.transform;
            if (view.showGrid || EditorSnapSettings.gridSnapActive)
            {
                GridAxisAndPivot(view, out var normal, out var pivot);
                if (Vector3.Dot(camera.forward, normal) > 0f) normal = -normal;
                var grid = new Plane(normal, pivot);
                if (grid.GetSide(camera.position)) return grid;
            }
            var f = camera.forward;
            var n = Mathf.Abs(f.y) >= 0.02f ? Vector3.up : Mathf.Abs(f.x) > Mathf.Abs(f.z) ? Vector3.right : Vector3.forward;
            if (Vector3.Dot(f, n) > 0f) n = -n;
            return new Plane(n, Grid > 0f ? BrushSnap.Round(view.pivot, Grid) : view.pivot);
        }

        static PropertyInfo s_Grids, s_GridAxis;
        static MethodInfo s_GridPivot;

        /// <summary>The Scene view grid's axis and position. Unity keeps them internal; without them the grid is the floor through the origin.</summary>
        static void GridAxisAndPivot(SceneView view, out Vector3 normal, out Vector3 pivot)
        {
            normal = Vector3.up; pivot = Vector3.zero;
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                s_Grids ??= typeof(SceneView).GetProperty("sceneViewGrids", flags);
                var grids = s_Grids?.GetValue(view);
                if (grids == null) return;
                s_GridAxis ??= grids.GetType().GetProperty("gridAxis", flags);
                s_GridPivot ??= grids.GetType().GetMethod("GetPivot", flags);
                if (s_GridAxis == null || s_GridPivot == null) return;
                var axis = s_GridAxis.GetValue(grids);
                int a = System.Convert.ToInt32(axis); // X, Y, Z, All
                normal = a == 0 ? Vector3.right : a == 2 ? Vector3.forward : Vector3.up;
                pivot = (Vector3)s_GridPivot.Invoke(grids, new[] { axis });
            }
            catch (System.Exception) { normal = Vector3.up; pivot = Vector3.zero; }
        }

        void CurrentPose(out Vector3 centre, out Vector3 size, out Quaternion rotation)
        {
            if (IsRadial(Shape))
            {
                // the axis is the press point; the drag sets the outer radius and where the first step starts
                var local = Quaternion.Inverse(planeRotation) * (opposite - origin);
                float radius = new Vector2(local.x, local.z).magnitude;
                float yaw = -Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
                if (radius < 1e-4f) yaw = 0f;
                yaw = BrushSnap.Round(yaw, BrushSettings.instance.rotationSnapDegrees);
                centre = origin;
                size = new Vector3(Mathf.Max(Grid, BrushSnap.Round(radius, Grid)), Mathf.Abs(height), 0f);
                rotation = planeRotation * Quaternion.Euler(0f, yaw, 0f);
                return;
            }
            if (IsCentred(Shape)) { BrushDraw.CentredPose(origin, opposite, height, planeRotation, Grid, Shape == BrushShape.Sphere, out centre, out size, out rotation); return; }
            BrushDraw.Pose(origin, opposite, height, planeRotation, Grid, out centre, out size, out rotation);
        }

        BrushGeometry.ShapeParams CurrentParameters(Vector3 size) => IsRadial(Shape) ? ParametersForRadial(Shape, size.x, size.y) : ParametersFor(Shape, size);

        void DrawPreview()
        {
            CurrentPose(out var centre, out var size, out var rotation);
            var poly = BrushGeometry.ShapePolyhedron(Shape, CurrentParameters(size));
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            Handles.color = Operation == BrushOperation.Subtract ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var face in poly.faces)
                for (int i = 0; i < face.indices.Length; i++)
                    Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 2f);
        }

        void Finish()
        {
            CurrentPose(out var centre, out var size, out var rotation);
            var s = BrushSettings.instance;
            int group = Undo.GetCurrentGroup();
            var brush = BrushApi.Create(Shape, centre, size, rotation, null);
            NewBrushParameters(out _, out float wall);
            var p = CurrentParameters(size);
            Undo.RecordObject(brush, "Create brush");
            brush.sides = p.sides; brush.tessellation = p.tessellation;
            brush.stepHeight = Shape == BrushShape.Stairs ? p.stepHeight : p.stairs.stepHeight;
            brush.innerRadius = p.stairs.innerRadius; brush.stepWidth = p.stairs.stepWidth; brush.stepThickness = p.stairs.stepThickness;
            brush.curveAngle = p.stairs.curveAngle; brush.numSteps = p.stairs.numSteps; brush.stepsPer360 = p.stairs.stepsPer360;
            brush.counterClockwise = p.stairs.counterClockwise; brush.slopedFloor = p.stairs.slopedFloor; brush.slopedCeiling = p.stairs.slopedCeiling;
            if (brush.SupportsHollow && s.newHollow) { brush.hollow = true; brush.wallThickness = wall; }
            if (Operation != BrushOperation.Add) BrushApi.SetOperation(brush, Operation);
            BrushSync.Ensure(brush);
            if (wallPlan != null) WallAnchors.Attach(brush.gameObject, wallPlan);
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            Selection.activeGameObject = brush.gameObject;
            state = State.Idle; hoverValid = false; wallPlan = null;
            SceneView.RepaintAll();
        }
    }

    /// <summary>Toolbar icons drawn in code from small pixel-art strings ('#' opaque), so the package needs no image assets.</summary>
    /// <summary>
    /// The package's icons (Brushes/Editor/Icons, drawn by Tools~/icons/generate_icons.py). Unity picks the d_ variant on the
    /// dark theme and caches them.
    /// </summary>
    static class BrushIcons
    {
        public const string Folder = "Packages/digital.dream.csgbrush/Brushes/Editor/Icons/";
        public static Texture2D Get(string name) => EditorGUIUtility.IconContent(Folder + name + ".png").image as Texture2D;
    }

    [EditorTool("Box Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 0)]
    public sealed class CreateBoxBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Box;
        public override string Title => "Box Brush";
        [MenuItem("Tools/CSG Brush/Create/Box", false, 1)] static void Menu() => ToolManager.SetActiveTool<CreateBoxBrushTool>();
    }

    [EditorTool("Wedge Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 1)]
    public sealed class CreateWedgeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Wedge;
        public override string Title => "Wedge Brush";
        [MenuItem("Tools/CSG Brush/Create/Wedge", false, 2)] static void Menu() => ToolManager.SetActiveTool<CreateWedgeBrushTool>();
    }

    [EditorTool("Cylinder Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 2)]
    public sealed class CreateCylinderBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cylinder;
        public override string Title => "Cylinder Brush";
        [MenuItem("Tools/CSG Brush/Create/Cylinder", false, 3)] static void Menu() => ToolManager.SetActiveTool<CreateCylinderBrushTool>();
    }

    [EditorTool("Cone Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 3)]
    public sealed class CreateConeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cone;
        public override string Title => "Cone Brush";
        [MenuItem("Tools/CSG Brush/Create/Cone", false, 4)] static void Menu() => ToolManager.SetActiveTool<CreateConeBrushTool>();
    }

    [EditorTool("Sphere Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 4)]
    public sealed class CreateSphereBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Sphere;
        public override string Title => "Sphere Brush";
        [MenuItem("Tools/CSG Brush/Create/Sphere", false, 5)] static void Menu() => ToolManager.SetActiveTool<CreateSphereBrushTool>();
    }

    [EditorTool("Linear Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 5)]
    public sealed class CreateStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Stairs;
        public override string Title => "Linear Stairs Brush";
        [MenuItem("Tools/CSG Brush/Create/Linear Stairs", false, 6)] static void Menu() => ToolManager.SetActiveTool<CreateStairsBrushTool>();
    }

    [EditorTool("Curved Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 6)]
    public sealed class CreateCurvedStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.CurvedStairs;
        public override string Title => "Curved Stairs Brush";
        [MenuItem("Tools/CSG Brush/Create/Curved Stairs", false, 7)] static void Menu() => ToolManager.SetActiveTool<CreateCurvedStairsBrushTool>();
    }

    [EditorTool("Spiral Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 7)]
    public sealed class CreateSpiralStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.SpiralStairs;
        public override string Title => "Spiral Stairs Brush";
        [MenuItem("Tools/CSG Brush/Create/Spiral Stairs", false, 8)] static void Menu() => ToolManager.SetActiveTool<CreateSpiralStairsBrushTool>();
    }

    [EditorTool("Arch Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 8)]
    public sealed class CreateArchBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Arch;
        public override string Title => "Arch Brush";
        [MenuItem("Tools/CSG Brush/Create/Arch", false, 9)] static void Menu() => ToolManager.SetActiveTool<CreateArchBrushTool>();
    }

    [EditorTool("Door Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 9)]
    public sealed class CreateDoorBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Door;
        public override string Title => "Door Brush";
        [MenuItem("Tools/CSG Brush/Create/Door", false, 10)] static void Menu() => ToolManager.SetActiveTool<CreateDoorBrushTool>();
    }

    [EditorTool("Window Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 10)]
    public sealed class CreateWindowBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Window;
        public override string Title => "Window Brush";
        [MenuItem("Tools/CSG Brush/Create/Window", false, 11)] static void Menu() => ToolManager.SetActiveTool<CreateWindowBrushTool>();
    }
}

using System;
using System.Collections.Generic;
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
    /// Draw a brush the ProBuilder way: press on a surface (a brush face, else the ground), drag the base rectangle,
    /// release, move the mouse to set the height, click to create. Escape cancels the current shape or leaves the tool.
    /// One tool per shape; they share a toolbar button with a shape dropdown.
    /// </summary>
    public abstract class BrushCreateTool : EditorTool
    {
        /// <summary>Operation of the brushes drawn next (set in the New Brush panel).</summary>
        public static BrushOperation Operation { get => BrushSettings.instance.newOperation; set => BrushSettings.instance.newOperation = value; }

        /// <summary>The Create tool that is active, if any.</summary>
        public static BrushCreateTool Active { get; private set; }
        public const string PanelId = "CSG Brush/New Brush";

        public abstract BrushShape Shape { get; }
        /// <summary>Exactly what the toolbar shows: "Box Brush", "Curved Stairs Brush".</summary>
        public abstract string Title { get; }
        protected abstract string IconArt { get; }

        enum State { Idle, Base, Height }
        State state;
        Vector3 origin, opposite, normal; Quaternion planeRotation; float height;
        Vector3 hoverPoint; bool hoverValid;
        int controlId;
        // Not cached: Unity keeps tool instances across domain reloads, and a cached GUIContent would keep an old tooltip.
        public override GUIContent toolbarIcon => new GUIContent(BrushIcons.Get(Shape.ToString(), IconArt), Title);

        public override void OnActivated() { state = State.Idle; Active = this; ShowPanel(true); }
        public override void OnWillBeDeactivated() { state = State.Idle; if (Active == this) Active = null; ShowPanel(false); }

        static void ShowPanel(bool show)
        {
            foreach (SceneView view in SceneView.sceneViews)
                if (view.TryGetOverlay(PanelId, out var overlay)) overlay.displayed = show;
        }

        /// <summary>Step sizes and wall thickness for new brushes: the settings, or grid-derived defaults when left at 0.</summary>
        public static void NewBrushParameters(out float stepHeight, out float stepDepth, out float wallThickness)
        {
            var s = BrushSettings.instance;
            stepHeight = s.newStepHeight > 0f ? s.ToMeters(s.newStepHeight) : s.ToMeters(Mathf.Min(s.maxStep, s.GridUnits));
            stepDepth = s.newStepDepth > 0f ? s.ToMeters(s.newStepDepth) : s.ToMeters(s.GridUnits * 2f);
            wallThickness = s.newWallThickness > 0f ? s.ToMeters(s.newWallThickness) : s.GridMeters;
        }

        /// <summary>Curved and spiral stairs are placed by their axis: press on the axis, drag the outer radius, then the height.</summary>
        public static bool IsRadial(BrushShape shape) => shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs;

        /// <summary>Round brushes are drawn from the centre of their base: press on the centre, drag the radius, then the height.</summary>
        public static bool IsCentred(BrushShape shape) => shape == BrushShape.Cylinder || shape == BrushShape.Cone || shape == BrushShape.Sphere;

        /// <summary>Parameters of a radial shape drawn with an outer radius and a total height: step width and step height from those, the rest from the panel.</summary>
        public static BrushGeometry.ShapeParams ParametersForRadial(BrushShape shape, float outerRadius, float height)
        {
            var s = BrushSettings.instance;
            float grid = s.GridMeters, step = grid > 0f ? grid : 0.01f;
            var p = ParametersFor(shape, new Vector3(outerRadius, height, outerRadius));
            var st = p.stairs;
            st.stepWidth = Mathf.Max(step, BrushSnap.Round(outerRadius - st.innerRadius, grid));
            st.stepHeight = Mathf.Max(step, BrushSnap.Round(Mathf.Abs(height) / Mathf.Max(1, st.numSteps), grid));
            p.stairs = st;
            return p;
        }

        /// <summary>
        /// The parameters a brush drawn with the given size gets. Curved and spiral stairs take their step width from the
        /// drawn footprint and their step height from the drawn height divided by the number of steps.
        /// </summary>
        public static BrushGeometry.ShapeParams ParametersFor(BrushShape shape, Vector3 size)
        {
            var s = BrushSettings.instance;
            NewBrushParameters(out float stepHeight, out float stepDepth, out _);
            float grid = s.GridMeters;
            var p = BrushGeometry.ShapeParams.Default(size, s.newSides);
            p.tessellation = s.newTessellation; p.stepHeight = stepHeight; p.stepDepth = stepDepth;
            var st = p.stairs;
            st.innerRadius = s.newInnerRadius > 0f ? s.ToMeters(s.newInnerRadius) : grid;
            st.stepThickness = s.newStepThickness > 0f ? s.ToMeters(s.newStepThickness) : grid * 0.5f;
            st.curveAngle = Mathf.Max(1f, s.newCurveAngle); st.numSteps = Mathf.Max(1, s.newNumSteps); st.stepsPer360 = Mathf.Max(1, s.newStepsPer360);
            st.counterClockwise = s.newCounterClockwise; st.slopedFloor = s.newSlopedFloor; st.slopedCeiling = s.newSlopedCeiling;
            if (shape == BrushShape.CurvedStairs || shape == BrushShape.SpiralStairs)
            {
                // step height from the drawn height; step width so the footprint's x extent matches the drawn one
                st.stepHeight = Mathf.Max(grid > 0f ? grid : 0.01f, BrushSnap.Round(size.y / st.numSteps, grid));
                st.stepWidth = 1f;
                var trial = shape == BrushShape.CurvedStairs ? BrushPolyhedron.CurvedStairs(st) : BrushPolyhedron.SpiralStairs(st);
                float trialX = trial.Bounds().size.x, ro = st.innerRadius + 1f;
                float targetRo = trialX > 1e-4f ? ro * size.x / trialX : ro;
                st.stepWidth = Mathf.Max(grid > 0f ? grid : 0.01f, BrushSnap.Round(targetRo - st.innerRadius, grid));
            }
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
                        if (hoverValid) { hoverPoint = BrushSnap.Round(p, Grid); }
                        if (e.type == EventType.MouseMove) SceneView.RepaintAll();
                    }
                    if (e.type == EventType.MouseDown && e.button == 0 && hoverValid && HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        FindPlane(e.mousePosition, out _, out normal);
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

        /// <summary>The surface under the mouse: a brush face (any brush), else the ground plane, else a plane facing the camera through the pivot.</summary>
        static bool FindPlane(Vector2 mouse, out Vector3 point, out Vector3 normal)
        {
            var hit = BrushHooks.PickBrushSurface(mouse, out point, out normal);
            if (hit != null) return true;
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float t) && t > 0f) { point = ray.GetPoint(t); normal = Vector3.up; return true; }
            var view = SceneView.lastActiveSceneView;
            if (view == null) { point = Vector3.zero; normal = Vector3.up; return false; }
            normal = BrushDraw.Cardinal(-view.camera.transform.forward);
            var facing = new Plane(normal, view.pivot);
            if (facing.Raycast(ray, out t)) { point = ray.GetPoint(t); return true; }
            point = Vector3.zero; return false;
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
            NewBrushParameters(out _, out _, out float wall);
            var p = CurrentParameters(size);
            Undo.RecordObject(brush, "Create brush");
            brush.sides = p.sides; brush.tessellation = p.tessellation;
            brush.stepHeight = Shape == BrushShape.Stairs ? p.stepHeight : p.stairs.stepHeight; brush.stepDepth = p.stepDepth;
            brush.innerRadius = p.stairs.innerRadius; brush.stepWidth = p.stairs.stepWidth; brush.stepThickness = p.stairs.stepThickness;
            brush.curveAngle = p.stairs.curveAngle; brush.numSteps = p.stairs.numSteps; brush.stepsPer360 = p.stairs.stepsPer360;
            brush.counterClockwise = p.stairs.counterClockwise; brush.slopedFloor = p.stairs.slopedFloor; brush.slopedCeiling = p.stairs.slopedCeiling;
            if (brush.SupportsHollow && s.newHollow) { brush.hollow = true; brush.wallThickness = wall; }
            if (Operation != BrushOperation.Add) BrushApi.SetOperation(brush, Operation);
            if (s.newSurface != CsgBrush.Colliders.ControllerSurface.Kind.Solid) BrushApi.SetSurface(brush, s.newSurface);
            BrushSync.Ensure(brush);
            Undo.CollapseUndoOperations(group);
            BrushApi.ForceUpdate();
            Selection.activeGameObject = brush.gameObject;
            state = State.Idle; hoverValid = false;
            SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Shown with the Create tools: the values the next brush is created with (what ProBuilder's Shape Settings does).
    /// Only the fields that apply to the active shape are shown.
    /// </summary>
    [Overlay(typeof(SceneView), BrushCreateTool.PanelId, "New Brush", false)]
    public sealed class NewBrushOverlay : UnityEditor.Overlays.Overlay
    {
        public override UnityEngine.UIElements.VisualElement CreatePanelContent()
        {
            var container = new UnityEngine.UIElements.IMGUIContainer(Draw);
            container.style.minWidth = 200;
            return container;
        }

        static float UnitsField(BrushSettings s, string label, string tooltip, float units, float placeholderMeters)
        {
            EditorGUI.BeginChangeCheck();
            float shown = units > 0f ? units : s.ToUnits(placeholderMeters);
            float v = EditorGUILayout.FloatField(new GUIContent(label + " (" + s.unitLabel + ")", tooltip), shown);
            return EditorGUI.EndChangeCheck() ? Mathf.Max(0f, v) : units;
        }

        static void Draw()
        {
            var s = BrushSettings.instance;
            var tool = BrushCreateTool.Active;
            var shape = tool != null ? tool.Shape : BrushShape.Box;
            EditorGUIUtility.labelWidth = 96;
            EditorGUILayout.LabelField(tool != null ? tool.Title : "Box Brush", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            s.newOperation = (BrushOperation)EditorGUILayout.EnumPopup(new GUIContent("Operation", "Add fills space, Subtract carves the brushes above it"), s.newOperation);
            s.newSurface = (CsgBrush.Colliders.ControllerSurface.Kind)EditorGUILayout.EnumPopup(new GUIContent("Surface", "What the volume means to the character controller"), s.newSurface);
            BrushCreateTool.NewBrushParameters(out float stepHeight, out float stepDepth, out float wall);
            switch (shape)
            {
                case BrushShape.Cylinder:
                case BrushShape.Cone:
                    s.newSides = Mathf.Max(3, EditorGUILayout.IntField(new GUIContent("Sides"), s.newSides));
                    EditorGUILayout.LabelField(" ", "Press on the centre, drag the radius, then the height", EditorStyles.miniLabel);
                    break;
                case BrushShape.Sphere:
                    s.newTessellation = EditorGUILayout.IntSlider(new GUIContent("Tessellation"), s.newTessellation, 1, 5);
                    EditorGUILayout.LabelField(" ", "Press on the centre, drag the radius; lift for an ellipsoid", EditorStyles.miniLabel);
                    break;
                case BrushShape.Stairs:
                    s.newStepHeight = UnitsField(s, "Step height", "0 uses the grid (at most the max step)", s.newStepHeight, stepHeight);
                    s.newStepDepth = UnitsField(s, "Step length", "0 uses two grid steps", s.newStepDepth, stepDepth);
                    break;
                case BrushShape.CurvedStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newCurveAngle = EditorGUILayout.FloatField(new GUIContent("Angle of curve", "Degrees the steps cover"), s.newCurveAngle);
                    s.newNumSteps = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps"), s.newNumSteps));
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    EditorGUILayout.LabelField(" ", "Press on the axis, drag the radius, then the height", EditorStyles.miniLabel);
                    break;
                case BrushShape.SpiralStairs:
                    s.newInnerRadius = UnitsField(s, "Inner radius", "0 uses one grid step", s.newInnerRadius, s.GridMeters);
                    s.newStepThickness = UnitsField(s, "Step thickness", "0 uses half a grid step", s.newStepThickness, s.GridMeters * 0.5f);
                    s.newStepsPer360 = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps per 360"), s.newStepsPer360));
                    s.newNumSteps = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("Num steps"), s.newNumSteps));
                    s.newSlopedCeiling = EditorGUILayout.Toggle(new GUIContent("Sloped ceiling"), s.newSlopedCeiling);
                    s.newSlopedFloor = EditorGUILayout.Toggle(new GUIContent("Sloped floor"), s.newSlopedFloor);
                    s.newCounterClockwise = EditorGUILayout.Toggle(new GUIContent("Counter clockwise"), s.newCounterClockwise);
                    EditorGUILayout.LabelField(" ", "Press on the axis, drag the radius, then the height", EditorStyles.miniLabel);
                    break;
            }
            if (shape == BrushShape.Box || shape == BrushShape.Cylinder)
            {
                s.newHollow = EditorGUILayout.Toggle(new GUIContent("Hollow", "Keep only the walls"), s.newHollow);
                using (new EditorGUI.DisabledScope(!s.newHollow))
                    s.newWallThickness = UnitsField(s, "Wall thickness", "0 uses one grid step", s.newWallThickness, wall);
            }
            if (EditorGUI.EndChangeCheck()) { s.NotifyChanged(); SceneView.RepaintAll(); }
        }
    }

    /// <summary>Toolbar icons drawn in code from small pixel-art strings ('#' opaque), so the package needs no image assets.</summary>
    static class BrushIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name, string art)
        {
            if (cache.TryGetValue(name, out var tex) && tex != null) return tex;
            var rows = art.Split('\n');
            int h = rows.Length, w = 0; foreach (var r in rows) w = Mathf.Max(w, r.Length);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, name = "brush icon " + name };
            var pixels = new Color32[w * h];
            var ink = EditorGUIUtility.isProSkin ? new Color32(210, 210, 210, 255) : new Color32(60, 60, 60, 255);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool on = x < rows[y].Length && rows[y][x] == '#';
                    pixels[(h - 1 - y) * w + x] = on ? ink : new Color32(0, 0, 0, 0);
                }
            tex.SetPixels32(pixels); tex.Apply();
            cache[name] = tex;
            return tex;
        }
    }

    [EditorTool("Box Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 0)]
    public sealed class CreateBoxBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Box;
        public override string Title => "Box Brush";
        protected override string IconArt => "................\n.....########...\n....#.......##..\n...#.......#.#..\n..########...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#..#...\n..#......#.#....\n..#......##.....\n..########......\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Box", false, 1)] static void Menu() => ToolManager.SetActiveTool<CreateBoxBrushTool>();
    }

    [EditorTool("Wedge Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 1)]
    public sealed class CreateWedgeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Wedge;
        public override string Title => "Wedge Brush";
        protected override string IconArt => "................\n................\n..........#.....\n.........##.....\n........#.#.....\n.......#..#.....\n......#...#.....\n.....#....#.....\n....#.....#.....\n...#......#.....\n..#.......#.....\n.###########....\n................\n................\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Wedge", false, 2)] static void Menu() => ToolManager.SetActiveTool<CreateWedgeBrushTool>();
    }

    [EditorTool("Cylinder Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 2)]
    public sealed class CreateCylinderBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cylinder;
        public override string Title => "Cylinder Brush";
        protected override string IconArt => "................\n....########....\n...#........#...\n..#..........#..\n..#..........#..\n...#........#...\n..#.########.#..\n..#..........#..\n..#..........#..\n..#..........#..\n..#..........#..\n..#..........#..\n...#........#...\n....########....\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Cylinder", false, 3)] static void Menu() => ToolManager.SetActiveTool<CreateCylinderBrushTool>();
    }

    [EditorTool("Cone Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 3)]
    public sealed class CreateConeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cone;
        public override string Title => "Cone Brush";
        protected override string IconArt => "................\n.......##.......\n.......##.......\n......#..#......\n......#..#......\n.....#....#.....\n.....#....#.....\n....#......#....\n....#......#....\n...#........#...\n...#........#...\n..#..........#..\n..#..........#..\n...#........#...\n....########....\n................";
        [MenuItem("Tools/CSG Brush/Create/Cone", false, 4)] static void Menu() => ToolManager.SetActiveTool<CreateConeBrushTool>();
    }

    [EditorTool("Sphere Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 4)]
    public sealed class CreateSphereBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Sphere;
        public override string Title => "Sphere Brush";
        protected override string IconArt => "................\n.....######.....\n...##......##...\n..#..........#..\n.#............#.\n.#............#.\n#..............#\n#..............#\n#..............#\n#..............#\n.#............#.\n.#............#.\n..#..........#..\n...##......##...\n.....######.....\n................";
        [MenuItem("Tools/CSG Brush/Create/Sphere", false, 5)] static void Menu() => ToolManager.SetActiveTool<CreateSphereBrushTool>();
    }

    [EditorTool("Linear Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 5)]
    public sealed class CreateStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Stairs;
        public override string Title => "Linear Stairs Brush";
        protected override string IconArt => "................\n................\n..........#####.\n..........#...#.\n.......####...#.\n.......#......#.\n....####......#.\n....#.........#.\n.####.........#.\n.#............#.\n.#............#.\n.##############.\n................\n................\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Linear Stairs", false, 6)] static void Menu() => ToolManager.SetActiveTool<CreateStairsBrushTool>();
    }

    [EditorTool("Curved Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 6)]
    public sealed class CreateCurvedStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.CurvedStairs;
        public override string Title => "Curved Stairs Brush";
        protected override string IconArt => "................\n..............#.\n.............##.\n............#.#.\n..........###.#.\n..........#...#.\n.......####...#.\n.......#......#.\n....####......#.\n....#.........#.\n..###.........#.\n..#...........#.\n.##...........#.\n.##############.\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Curved Stairs", false, 7)] static void Menu() => ToolManager.SetActiveTool<CreateCurvedStairsBrushTool>();
    }

    [EditorTool("Spiral Stairs Brush", variantGroup = typeof(BrushCreateTool), variantPriority = 7)]
    public sealed class CreateSpiralStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.SpiralStairs;
        public override string Title => "Spiral Stairs Brush";
        protected override string IconArt => "................\n.......##.......\n.......##.......\n....#..##..#....\n...###.##.###...\n....#..##..#....\n.......##.......\n..####.##.####..\n.......##.......\n....#..##..#....\n...###.##.###...\n....#..##..#....\n.......##.......\n.......##.......\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Spiral Stairs", false, 8)] static void Menu() => ToolManager.SetActiveTool<CreateSpiralStairsBrushTool>();
    }
}

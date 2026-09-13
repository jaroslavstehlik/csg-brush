using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
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
        /// <summary>Operation of the brushes drawn next (set in the Brushes overlay).</summary>
        public static BrushOperation Operation = BrushOperation.Add;

        public abstract BrushShape Shape { get; }
        protected abstract string IconArt { get; }

        enum State { Idle, Base, Height }
        State state;
        Vector3 origin, opposite, normal; Quaternion planeRotation; float height;
        Vector3 hoverPoint; bool hoverValid;
        int controlId;
        GUIContent icon;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (icon == null) icon = new GUIContent(BrushIcons.Get(Shape.ToString(), IconArt), "Create " + Shape + " brush: drag the base on a surface, move for the height, click");
                return icon;
            }
        }

        public override void OnActivated() { state = State.Idle; }
        public override void OnWillBeDeactivated() { state = State.Idle; }

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
                        if (Mathf.Abs(local.x) < min && Mathf.Abs(local.z) < min) { state = State.Idle; break; } // a click, not a drag
                        state = State.Height; height = 0f;
                    }
                    if (e.type == EventType.Repaint) DrawPreview();
                    break;

                case State.Height:
                    if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                    {
                        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        float h = BrushDraw.HeightFromRay(opposite, normal, ray);
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
            BrushDraw.Pose(origin, opposite, height, planeRotation, Grid, out centre, out size, out rotation);
        }

        void DrawPreview()
        {
            CurrentPose(out var centre, out var size, out var rotation);
            var s = BrushSettings.instance;
            var poly = BrushGeometry.ShapePolyhedron(Shape, size, 16, 2, s.ToMeters(Mathf.Min(s.maxStep, s.GridUnits)), s.ToMeters(s.GridUnits * 2f));
            var m = Matrix4x4.TRS(centre, rotation, Vector3.one);
            Handles.color = Operation == BrushOperation.Subtract ? new Color(1f, 0.4f, 0.2f, 0.9f) : new Color(0.3f, 0.8f, 1f, 0.9f);
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            foreach (var face in poly.faces)
                for (int i = 0; i < face.indices.Length; i++)
                    Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 2f);
            var units = s.ToUnits(size);
            Handles.Label(m.MultiplyPoint3x4(poly.Bounds().max), units.x.ToString("0.#") + " x " + units.y.ToString("0.#") + " x " + units.z.ToString("0.#") + " " + s.unitLabel, EditorStyles.helpBox);
        }

        void Finish()
        {
            CurrentPose(out var centre, out var size, out var rotation);
            var brush = BrushApi.Create(Shape, centre, size, rotation, null);
            if (Operation != BrushOperation.Add) BrushApi.SetOperation(brush, Operation);
            BrushApi.ForceUpdate();
            Selection.activeGameObject = brush.gameObject;
            state = State.Idle; hoverValid = false;
            SceneView.RepaintAll();
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

    [EditorTool("Create Box", variantGroup = typeof(BrushCreateTool), variantPriority = 0)]
    public sealed class CreateBoxBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Box;
        protected override string IconArt => "................\n.....########...\n....#.......##..\n...#.......#.#..\n..########...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#...#..\n..#......#..#...\n..#......#.#....\n..#......##.....\n..########......\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Box", false, 1)] static void Menu() => ToolManager.SetActiveTool<CreateBoxBrushTool>();
    }

    [EditorTool("Create Wedge", variantGroup = typeof(BrushCreateTool), variantPriority = 1)]
    public sealed class CreateWedgeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Wedge;
        protected override string IconArt => "................\n................\n..........#.....\n.........##.....\n........#.#.....\n.......#..#.....\n......#...#.....\n.....#....#.....\n....#.....#.....\n...#......#.....\n..#.......#.....\n.###########....\n................\n................\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Wedge", false, 2)] static void Menu() => ToolManager.SetActiveTool<CreateWedgeBrushTool>();
    }

    [EditorTool("Create Cylinder", variantGroup = typeof(BrushCreateTool), variantPriority = 2)]
    public sealed class CreateCylinderBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cylinder;
        protected override string IconArt => "................\n....########....\n...#........#...\n..#..........#..\n..#..........#..\n...#........#...\n..#.########.#..\n..#..........#..\n..#..........#..\n..#..........#..\n..#..........#..\n..#..........#..\n...#........#...\n....########....\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Cylinder", false, 3)] static void Menu() => ToolManager.SetActiveTool<CreateCylinderBrushTool>();
    }

    [EditorTool("Create Cone", variantGroup = typeof(BrushCreateTool), variantPriority = 3)]
    public sealed class CreateConeBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Cone;
        protected override string IconArt => "................\n.......##.......\n.......##.......\n......#..#......\n......#..#......\n.....#....#.....\n.....#....#.....\n....#......#....\n....#......#....\n...#........#...\n...#........#...\n..#..........#..\n..#..........#..\n...#........#...\n....########....\n................";
        [MenuItem("Tools/CSG Brush/Create/Cone", false, 4)] static void Menu() => ToolManager.SetActiveTool<CreateConeBrushTool>();
    }

    [EditorTool("Create Sphere", variantGroup = typeof(BrushCreateTool), variantPriority = 4)]
    public sealed class CreateSphereBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Sphere;
        protected override string IconArt => "................\n.....######.....\n...##......##...\n..#..........#..\n.#............#.\n.#............#.\n#..............#\n#..............#\n#..............#\n#..............#\n.#............#.\n.#............#.\n..#..........#..\n...##......##...\n.....######.....\n................";
        [MenuItem("Tools/CSG Brush/Create/Sphere", false, 5)] static void Menu() => ToolManager.SetActiveTool<CreateSphereBrushTool>();
    }

    [EditorTool("Create Stairs", variantGroup = typeof(BrushCreateTool), variantPriority = 5)]
    public sealed class CreateStairsBrushTool : BrushCreateTool
    {
        public override BrushShape Shape => BrushShape.Stairs;
        protected override string IconArt => "................\n................\n..........#####.\n..........#...#.\n.......####...#.\n.......#......#.\n....####......#.\n....#.........#.\n.####.........#.\n.#............#.\n.#............#.\n.##############.\n................\n................\n................\n................";
        [MenuItem("Tools/CSG Brush/Create/Stairs", false, 6)] static void Menu() => ToolManager.SetActiveTool<CreateStairsBrushTool>();
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Editor-side glue: keeps brushes in sync after undo and after Inspector edits, rebuilds the models that
    /// changed, redirects selection from generated objects to the brush, hides the generated objects, and keeps
    /// Unity's grid on the world grid.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushHooks
    {
        static readonly HashSet<Brush> pending = new HashSet<Brush>();
        static bool processing;
        static bool redirecting;

        static BrushHooks()
        {
            Brush.SyncRequested = RequestSync;
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.postprocessModifications += OnPostprocessModifications;
            EditorApplication.update += OnEditorUpdate;
            Colliders.Editor.ConvexColliderBuilder.DeferWhile = () => BrushSnap.DragInProgress;
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            BrushSettings.Changed += OnSettingsChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => EnsureAll();
            UnityEditor.SceneManagement.EditorSceneManager.newSceneCreated += (scene, setup, mode) => BrushCsg.ClearCaches();
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += () => { EnsureAll(); BrushApi.ForceUpdate(); }; };
            EditorApplication.delayCall += () =>
            {
                ApplyGrid();
                EnsureAll(); // derived objects in a freshly loaded scene may predate the current version
                Process();
            };
        }

        /// <summary>
        /// Re-derive everything (scene open, domain reload, after play mode): the saved meshes and pieces may be stale,
        /// and the native solids do not survive a reload. Also clears leftovers of scenes saved with the Chisel core.
        /// </summary>
        public static void EnsureAll()
        {
            if (Application.isPlaying) return;
            BrushSync.RemoveLegacySceneObjects();
            BrushCsg.ClearCaches();
            foreach (var brush in Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                BrushSync.Ensure(brush);
            BrushCsg.MarkAllDirty();
        }

        static void RequestSync(Brush brush)
        {
            if (brush == null || Application.isPlaying) return;
            pending.Add(brush);
            EditorApplication.delayCall -= FlushPending;
            EditorApplication.delayCall += FlushPending;
        }

        static void FlushPending()
        {
            if (pending.Count == 0) return;
            var list = new List<Brush>(pending);
            pending.Clear();
            foreach (var b in list)
            {
                if (b == null) continue;
                BrushSnap.Snap(b); // Inspector edits: sizes and transforms land on the grid
                BrushSync.Ensure(b);
            }
        }

        /// <summary>
        /// Transform edits go through Undo (Move/Rotate tools, Inspector). Snap the brush in world space right away and
        /// rewrite the recorded values, so the undo history holds the snapped pose and redo lands on the grid too.
        /// </summary>
        static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (Application.isPlaying) return modifications;
            // a brush moved to another layer belongs to another CSG group from now on; a module's values ride on the pieces
            for (int i = 0; i < modifications.Length; i++)
            {
                var mtarget = modifications[i].currentValue.target;
                if (mtarget is GameObject lgo && lgo.TryGetComponent<Brush>(out var layered))
                {
                    var path = modifications[i].currentValue.propertyPath;
                    if (path == "m_Layer" || path == "m_TagString" || path == "m_StaticEditorFlags") BrushCsg.MarkDirty(layered); // the pieces take these from the brush
                }
                else if (mtarget is GameObject mgo && mgo.TryGetComponent<BrushModel>(out var modelObj)) BrushCsg.MarkDirty(modelObj); // the meshes take tag and flags from the model
                else if (mtarget is BrushModule) BrushCsg.MarkAllDirty(); // a parent's module tags every brush below it
            }
            if (!BrushSettings.instance.snapToGrid) return modifications;
            if (BrushSnap.DragInProgress)
            {
                // Rotate/Scale handles accumulate per frame; snap on release instead (see BrushSnap.DragInProgress).
                for (int i = 0; i < modifications.Length; i++)
                    if (modifications[i].currentValue.target is Transform dt && dt.TryGetComponent<Brush>(out var db))
                        BrushSnap.SnapOrDefer(db);
                return modifications;
            }
            HashSet<Brush> snapped = null;
            for (int i = 0; i < modifications.Length; i++)
            {
                var target = modifications[i].currentValue.target;
                Brush brush = null;
                if (target is Transform t) t.TryGetComponent(out brush);
                else if (target is Brush b && modifications[i].currentValue.propertyPath.StartsWith("size")) brush = b;
                if (brush == null) continue;
                snapped ??= new HashSet<Brush>();
                if (!snapped.Add(brush)) continue;
                BrushSnap.Snap(brush);
                RequestSync(brush);
            }
            if (snapped == null) return modifications;
            for (int i = 0; i < modifications.Length; i++)
            {
                var cv = modifications[i].currentValue;
                if (cv.target is Transform t && t.TryGetComponent<Brush>(out _))
                {
                    var value = ReadTransformProperty(t, cv.propertyPath);
                    if (value != null) { cv.value = value; modifications[i].currentValue = cv; }
                }
                else if (cv.target is Brush b && cv.propertyPath.StartsWith("size."))
                {
                    char axis = cv.propertyPath[cv.propertyPath.Length - 1];
                    float v = axis == 'x' ? b.size.x : axis == 'y' ? b.size.y : b.size.z;
                    cv.value = v.ToString("R", System.Globalization.CultureInfo.InvariantCulture); modifications[i].currentValue = cv;
                }
            }
            return modifications;
        }

        static string ReadTransformProperty(Transform t, string path)
        {
            float v;
            if (path.StartsWith("m_LocalPosition.")) { var p = t.localPosition; v = path.EndsWith("x") ? p.x : path.EndsWith("y") ? p.y : p.z; }
            else if (path.StartsWith("m_LocalRotation.")) { var q = t.localRotation; v = path.EndsWith("x") ? q.x : path.EndsWith("y") ? q.y : path.EndsWith("z") ? q.z : q.w; }
            else if (path.StartsWith("m_LocalScale.")) { var p = t.localScale; v = path.EndsWith("x") ? p.x : path.EndsWith("y") ? p.y : p.z; }
            else if (path.StartsWith("m_LocalEulerAnglesHint.")) { var p = t.localEulerAngles; v = path.EndsWith("x") ? p.x : path.EndsWith("y") ? p.y : p.z; }
            else return null;
            return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        static Vector2 s_MouseDown; static bool s_MouseDownValid;

        /// <summary>
        /// Outside edit mode: clicking a brush's surface selects the brush. Unity's own picking would hit the generated
        /// mesh, which belongs to the model and cannot tell which brush was meant, so brushes are picked here by
        /// their own geometry (additive brushes first, a subtractive one only when nothing solid is under the mouse).
        /// Also draws the concave feedback for selected Custom shapes with the normal tools.
        /// </summary>
        static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (BrushEditContext.IsActive || Application.isPlaying) return;
            if (e.type == EventType.Repaint)
            {
                if (BrushSettings.instance.showCuts) BrushShapeDrawing.DrawCuts();
                foreach (var go in Selection.gameObjects)
                    if (go.TryGetComponent<Brush>(out var brush) && brush.shape == BrushShape.Custom)
                        BrushShapeDrawing.Draw(brush, drawEdges: false);
                return;
            }
            if (e.alt || Tools.current == Tool.View || Tools.viewToolActive) return;
            if (e.type == EventType.MouseDown && e.button == 0) { s_MouseDown = e.mousePosition; s_MouseDownValid = GUIUtility.hotControl == 0; return; }
            if (e.type != EventType.MouseUp || e.button != 0 || !s_MouseDownValid) return;
            s_MouseDownValid = false;
            if (GUIUtility.hotControl != 0 || (e.mousePosition - s_MouseDown).sqrMagnitude > 16f) return; // a drag or a gizmo
            var hit = PickBrush(e.mousePosition);
            if (hit == null) return; // let Unity handle empty space and other objects
            var picked = hit.gameObject;
            if (e.shift) { var list = new List<Object>(Selection.objects); if (!list.Contains(picked)) list.Add(picked); Selection.objects = list.ToArray(); }
            else if (e.control || e.command) { var list = new List<Object>(Selection.objects); if (!list.Remove(picked)) list.Add(picked); Selection.objects = list.ToArray(); }
            else Selection.activeGameObject = picked;
            e.Use();
        }

        /// <summary>Nearest brush under the mouse by its own shape.</summary>
        public static Brush PickBrush(Vector2 mouse) => PickBrushSurface(mouse, out _, out _);

        /// <summary>
        /// Nearest brush under the mouse with the hit point and face normal (world space). Additive brushes win over
        /// subtractive ones, except while cuts are shown: then whatever surface is drawn nearest is what a click hits.
        /// </summary>
        public static Brush PickBrushSurface(Vector2 mouse, out Vector3 point, out Vector3 normal)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            Brush bestAdd = null, bestSub = null; float tAdd = float.MaxValue, tSub = float.MaxValue;
            Vector3 pAdd = Vector3.zero, nAdd = Vector3.up, pSub = Vector3.zero, nSub = Vector3.up;
            foreach (var brush in Object.FindObjectsByType<Brush>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var poly = BrushGeometry.Polyhedron(brush);
                if (poly == null || !poly.IsValid) continue;
                var t = brush.transform;
                var lo = t.InverseTransformPoint(ray.origin); var ld = t.InverseTransformDirection(ray.direction);
                for (int f = 0; f < poly.faces.Length; f++)
                {
                    var plane = poly.Plane(f); var n = new Vector3(plane.x, plane.y, plane.z);
                    float denom = Vector3.Dot(n, ld);
                    if (denom >= -1e-6f) continue;
                    float tt = -(Vector3.Dot(n, lo) + plane.w) / denom;
                    if (tt < 0f) continue;
                    if (!PointInFace(poly, f, lo + ld * tt)) continue;
                    var worldPoint = t.TransformPoint(lo + ld * tt);
                    float world = (worldPoint - ray.origin).magnitude;
                    var worldNormal = t.TransformDirection(n).normalized;
                    if (brush.operation == BrushOperation.Subtract) { if (world < tSub) { tSub = world; bestSub = brush; pSub = worldPoint; nSub = worldNormal; } }
                    else if (world < tAdd) { tAdd = world; bestAdd = brush; pAdd = worldPoint; nAdd = worldNormal; }
                }
            }
            if (bestAdd != null && (bestSub == null || !BrushSettings.instance.showCuts || tAdd <= tSub)) { point = pAdd; normal = nAdd; return bestAdd; }
            point = pSub; normal = nSub; return bestSub;
        }

        static bool PointInFace(BrushPolyhedron poly, int face, Vector3 p)
        {
            var plane = poly.Plane(face); var n = new Vector3(plane.x, plane.y, plane.z);
            var idx = poly.faces[face].indices;
            var u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(n, u);
            float px = Vector3.Dot(p, u), py = Vector3.Dot(p, w); int winding = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = poly.vertices[idx[i]]; var b = poly.vertices[idx[(i + 1) % idx.Length]];
                float ax = Vector3.Dot(a, u) - px, ay = Vector3.Dot(a, w) - py, bx = Vector3.Dot(b, u) - px, by = Vector3.Dot(b, w) - py;
                if (ay <= 0f) { if (by > 0f && ax * by - bx * ay > 0f) winding++; }
                else if (by <= 0f && ax * by - bx * ay < 0f) winding--;
            }
            return winding != 0;
        }

        /// <summary>Transforms changed by scripts or by parents are caught by polling; a few hundred brushes cost nothing.</summary>
        static void OnEditorUpdate()
        {
            if (Application.isPlaying) return;
            BrushSnap.ProcessChanged();
            bool wasBusy = BrushCsg.Busy; bool hadDirty = BrushCsg.HasDirty;
            BrushCsg.Pump(); // applies a finished background build and starts the next one
            if (wasBusy || hadDirty) SceneView.RepaintAll();
            if (!BrushSnap.DragInProgress) Colliders.Editor.ConvexColliderBuilder.FlushDeferred(); // colliders catch up on release
        }

        /// <summary>Number of undo/redo callbacks handled; used by tests to verify the callback fires.</summary>
        public static int UndoRedoCount { get; private set; }

        static void OnUndoRedo()
        {
            UndoRedoCount++;
            // Only the Brush fields, its transform and the brush GameObject are undo state. Everything else
            // (composite, hidden generator children, colliders) is derived and rebuilt here without Undo,
            // so this callback never adds undo entries or clears the redo stack.
            foreach (var brush in Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                BrushSnap.Snap(brush); // a redo re-applies the recorded pose; keep the grid rule
                BrushSync.Ensure(brush);
            }
        }

        /// <summary>A module added to or removed from an object: the pieces of the brushes it tags change.</summary>
        static void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (Application.isPlaying) return;
            for (int i = 0; i < stream.length; i++)
                if (stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectStructure || stream.GetEventType(i) == ObjectChangeKind.ChangeGameObjectStructureHierarchy) { BrushCsg.MarkAllDirty(); return; }
        }

        static void OnHierarchyChanged()
        {
            if (Application.isPlaying || processing) return;
            BrushCsg.MarkAllDirty(); // order, parents, deletions: the brush lists are re-derived on the next build
            EditorApplication.delayCall -= Process;
            EditorApplication.delayCall += Process;
        }

        static void OnSettingsChanged()
        {
            ApplyGrid();
            BrushSync.ApplyVisibility();
            SceneView.RepaintAll();
        }

        /// <summary>Hide generated objects. Safe to call often.</summary>
        public static void Process()
        {
            if (processing || Application.isPlaying) return;
            processing = true;
            try { BrushSync.ApplyVisibility(); }
            finally { processing = false; }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        static void OnSelectionChanged()
        {
            if (redirecting) return;
            var objects = Selection.objects;
            bool changed = false;
            var result = new List<Object>(objects.Length);
            foreach (var o in objects)
            {
                var go = o as GameObject;
                if (go != null && Brush.IsGeneratedChildName(go.name) && go.transform.parent != null && go.transform.parent.GetComponent<Brush>() != null)
                {
                    var parent = go.transform.parent.gameObject;
                    if (!result.Contains(parent)) result.Add(parent);
                    changed = true;
                    continue;
                }
                if (go != null && (BrushModel.IsMeshChildName(go.name) || go.name == Colliders.ConvexColliderSettings.ContainerName || go.name == BrushModel.DefaultName) && !BrushSettings.instance.showGenerated)
                {
                    changed = true; // generated mesh or collider objects: never what the student meant
                    continue;
                }
                result.Add(o);
            }
            if (!changed) return;
            redirecting = true;
            try { Selection.objects = result.ToArray(); }
            finally { redirecting = false; }
        }

        /// <summary>Unity's grid snapping follows the world preset's grid size.</summary>
        public static void ApplyGrid()
        {
            var s = BrushSettings.instance;
            float g = s.GridMeters;
            EditorSnapSettings.move = new Vector3(g, g, g);
            EditorSnapSettings.rotate = s.rotationSnapDegrees;
            // Unity's absolute grid snapping (Global handle) matches how Unreal and TrenchBroom snap: to the
            // world grid, not by increments from where the object happens to be.
            EditorSnapSettings.gridSnapEnabled = s.snapToGrid;
            if (s.snapToGrid) Tools.pivotRotation = PivotRotation.Global;
        }
    }
}

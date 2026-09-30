using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
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
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= DrawGroupIcon;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += DrawGroupIcon;
            BrushSettings.Changed += OnSettingsChanged;
            UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => { EnsureAll(); EditorApplication.delayCall += BrushPrefabBuild.BuildUsedPrefabs; };
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
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++) if (active[i] != null) BrushSync.Ensure(active[i]);
            BrushCsg.MarkAllDirty();
        }

        static void RequestSync(Brush brush)
        {
            // a prefab asset's brushes (validated on every import, e.g. each Auto Save) are edited in Prefab Mode and built when the prefab is, never here
            if (brush == null || Application.isPlaying || EditorUtility.IsPersistent(brush)) return;
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
            // a brush moved to another layer is combined with that layer's brushes from now on; a module's values ride on the pieces
            for (int i = 0; i < modifications.Length; i++)
            {
                var mtarget = modifications[i].currentValue.target;
                if (mtarget is GameObject lgo && lgo.TryGetComponent<Brush>(out var layered))
                {
                    var path = modifications[i].currentValue.propertyPath;
                    if (path == "m_Layer" || path == "m_TagString" || path == "m_StaticEditorFlags" || path == "m_Name") { BrushCache.Forget(layered); BrushCsg.MarkDirty(layered); } // the pieces take these from the brush
                }
                else if (mtarget is GameObject mgo && mgo.TryGetComponent<BrushGroup>(out var modelObj)) BrushCsg.MarkDirty(modelObj); // the meshes take tag and flags from the model
                if (mtarget is Transform otr && otr.TryGetComponent<WallAnchor>(out var movedAnchor)) WallAnchors.Moved(movedAnchor); // a door moved by hand takes the nearest wall
                else if (mtarget is Brush ob && ob.TryGetComponent<WallAnchor>(out var sizedAnchor) && sizedAnchor.Plan != null) BrushGenerators.MarkDirty(sizedAnchor.Plan); // resized: placed again on its wall
                if (mtarget is GameObject ggo && ggo.TryGetComponent<BrushGenerator>(out var generatorObj)) BrushGenerators.MarkDirty(generatorObj); // its brushes take its layer, tag and flags
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
        /// their own geometry (subtractive ones only while cuts are shown).
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
            if (e.alt || Tools.current == Tool.View || Tools.viewToolActive || FloorPlanEditContext.IsActive || WallAnchorEditor.Picking) return; // plan edit mode and wall picking pick their own
            if (e.type == EventType.MouseDown && e.button == 0) { s_MouseDown = e.mousePosition; s_MouseDownValid = GUIUtility.hotControl == 0; return; }
            if (e.type != EventType.MouseUp || e.button != 0 || !s_MouseDownValid) return;
            s_MouseDownValid = false;
            if (GUIUtility.hotControl != 0 || (e.mousePosition - s_MouseDown).sqrMagnitude > 16f) return; // a drag or a gizmo
            var hit = PickBrush(e.mousePosition);
            if (hit == null) return; // let Unity handle empty space and other objects
            var picked = BrushGenerators.SelectionTarget(hit);
            if (e.shift) { var list = new List<Object>(Selection.objects); if (!list.Contains(picked)) list.Add(picked); Selection.objects = list.ToArray(); }
            else if (e.control || e.command) { var list = new List<Object>(Selection.objects); if (!list.Remove(picked)) list.Add(picked); Selection.objects = list.ToArray(); }
            else Selection.activeGameObject = picked;
            e.Use();
        }

        /// <summary>
        /// The active brushes of the stage the Scene view shows: in Prefab Mode only the prefab's, since the scenes
        /// behind it are hidden and cannot be clicked or edited there; otherwise those of the open scenes.
        /// </summary>
        public static List<Brush> BrushesInView() { var list = new List<Brush>(); BrushesInView(list); return list; }

        /// <summary>The brushes in view (see <see cref="BrushesInView()"/>) into a list the caller reuses: nothing is allocated.</summary>
        public static void BrushesInView(List<Brush> into)
        {
            into.Clear();
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var stageScene = stage != null ? stage.scene : default;
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var brush = active[i];
                if (brush == null) continue;
                var scene = brush.gameObject.scene;
                if (stage != null ? scene == stageScene : !EditorSceneManager.IsPreviewScene(scene)) into.Add(brush);
            }
        }


        /// <summary>Nearest brush under the mouse by its own shape.</summary>
        public static Brush PickBrush(Vector2 mouse) => PickBrushSurface(mouse, out _, out _);

        /// <summary>
        /// Nearest brush under the mouse with the hit point and face normal (world space). Subtractive brushes are
        /// picked only while cuts are shown, and then whatever surface is drawn nearest is what a click hits.
        /// </summary>
        public static Brush PickBrushSurface(Vector2 mouse, out Vector3 point, out Vector3 normal) => PickBrushSurface(HandleUtility.GUIPointToWorldRay(mouse), out point, out normal);

        /// <summary>Nearest brush along a world ray (see the mouse overload).</summary>
        public static Brush PickBrushSurface(Ray ray, out Vector3 point, out Vector3 normal)
        {
            bool cuts = BrushSettings.instance.showCuts;
            Brush bestAdd = null, bestSub = null; float tAdd = float.MaxValue, tSub = float.MaxValue;
            Vector3 pAdd = Vector3.zero, nAdd = Vector3.up, pSub = Vector3.zero, nSub = Vector3.up;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var stageScene = stage != null ? stage.scene : default;
            var o = ray.origin; var d = ray.direction;
            var inv = new Vector3(Inverse(d.x), Inverse(d.y), Inverse(d.z));
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var brush = active[i]; // registered brushes are alive: OnDisable always runs before a brush is destroyed
                if (brush.operation == BrushOperation.Subtract && !cuts && !brush.IsOpening) continue; // an invisible cut is not clickable; a door or window always is
                var e = BrushCache.Of(brush);
                if (!e.pickReady) MeasureForPicking(brush, e);
                if (e.polyhedron == null || !RayHitsBox(o, inv, e.min, e.max)) continue; // most brushes end here, without a call into Unity
                var scene = brush.gameObject.scene;
                if (stage != null ? scene != stageScene : EditorSceneManager.IsPreviewScene(scene)) continue; // not in the Scene view
                var poly = e.polyhedron;
                var lo = e.toLocal.MultiplyPoint3x4(ray.origin); var ld = e.toLocal.MultiplyVector(ray.direction);
                for (int f = 0; f < poly.faces.Length; f++)
                {
                    var plane = poly.Plane(f); var n = new Vector3(plane.x, plane.y, plane.z);
                    float denom = Vector3.Dot(n, ld);
                    if (denom >= -1e-6f) continue;
                    float tt = -(Vector3.Dot(n, lo) + plane.w) / denom;
                    if (tt < 0f) continue;
                    if (!PointInFace(poly, f, lo + ld * tt)) continue;
                    var worldPoint = e.toWorld.MultiplyPoint3x4(lo + ld * tt);
                    float world = (worldPoint - ray.origin).magnitude;
                    var worldNormal = (e.rotation * n).normalized;
                    if (brush.operation == BrushOperation.Subtract) { if (world < tSub) { tSub = world; bestSub = brush; pSub = worldPoint; nSub = worldNormal; } }
                    else if (world < tAdd) { tAdd = world; bestAdd = brush; pAdd = worldPoint; nAdd = worldNormal; }
                }
            }
            if (bestAdd != null && (bestSub == null || tAdd <= tSub)) { point = pAdd; normal = nAdd; return bestAdd; }
            point = pSub; normal = nSub; return bestSub;
        }



        /// <summary>Keys compared by reference: UnityEngine.Object's own hashing and equality call into Unity, which costs more than the lookup.</summary>
        sealed class ByReference : IEqualityComparer<Brush>
        {
            public static readonly ByReference Instance = new ByReference();
            public bool Equals(Brush a, Brush b) => ReferenceEquals(a, b);
            public int GetHashCode(Brush b) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
        }

        static void MeasureForPicking(Brush brush, BrushCache e)
        {
            e.pickReady = true;
            e.polyhedron = LocalShape(brush, out var local);
            if (e.polyhedron != null)
            {
                var t = brush.CachedTransform;
                e.toWorld = t.localToWorldMatrix; e.toLocal = t.worldToLocalMatrix; e.rotation = t.rotation;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    var corner = e.toWorld.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? local.min.x : local.max.x, (c & 2) == 0 ? local.min.y : local.max.y, (c & 4) == 0 ? local.min.z : local.max.z));
                    min = Vector3.Min(min, corner); max = Vector3.Max(max, corner);
                }
                const float pad = 1e-4f; // a face lying on the box still counts
                e.min = min - new Vector3(pad, pad, pad); e.max = max + new Vector3(pad, pad, pad);
            }
        }

        static void Prune<T>(Dictionary<Brush, T> cache)
        {
            var dead = new List<Brush>();
            foreach (var b in cache.Keys) if (b == null || !b.isActiveAndEnabled) dead.Add(b);
            foreach (var b in dead) cache.Remove(b);
        }

        static float Inverse(float v) => v != 0f ? 1f / v : float.PositiveInfinity;

        /// <summary>Slab test: does the ray (from its origin on, with 1 / direction per axis) pass through the box?</summary>
        static bool RayHitsBox(Vector3 o, Vector3 inv, Vector3 min, Vector3 max)
        {
            float t0 = (min.x - o.x) * inv.x, t1 = (max.x - o.x) * inv.x;
            float enter = Mathf.Min(t0, t1), exit = Mathf.Max(t0, t1);
            t0 = (min.y - o.y) * inv.y; t1 = (max.y - o.y) * inv.y;
            enter = Mathf.Max(enter, Mathf.Min(t0, t1)); exit = Mathf.Min(exit, Mathf.Max(t0, t1));
            t0 = (min.z - o.z) * inv.z; t1 = (max.z - o.z) * inv.z;
            enter = Mathf.Max(enter, Mathf.Min(t0, t1)); exit = Mathf.Min(exit, Mathf.Max(t0, t1));
            return exit >= Mathf.Max(enter, 0f); // NaN (origin on a slab plane of a parallel axis) fails, which only skips a touching box
        }

        sealed class CachedShape { public BrushShape shape; public BrushGeometry.ShapeParams parameters; public BrushPolyhedron polyhedron; public Bounds bounds; }
        static readonly Dictionary<Brush, CachedShape> s_Shapes = new Dictionary<Brush, CachedShape>(ByReference.Instance);

        /// <summary>
        /// A brush's shape in local space and its bounds, for picking; read-only. A parametric shape is built once per set of
        /// parameters; a Custom shape is the brush's own polyhedron (edited in place, so its bounds are measured each time).
        /// </summary>
        static BrushPolyhedron LocalShape(Brush brush, out Bounds bounds)
        {
            bounds = default;
            if (brush.shape == BrushShape.Custom && brush.polyhedron != null && brush.polyhedron.IsValid)
            {
                var v = brush.polyhedron.vertices;
                Vector3 min = v[0], max = v[0];
                for (int i = 1; i < v.Length; i++) { min = Vector3.Min(min, v[i]); max = Vector3.Max(max, v[i]); }
                bounds.SetMinMax(min, max);
                return brush.polyhedron;
            }
            var shape = brush.shape == BrushShape.Custom ? brush.customFrom : brush.shape;
            var parameters = BrushGeometry.ShapeParams.From(brush);
            if (!s_Shapes.TryGetValue(brush, out var cached))
            {
                if (s_Shapes.Count > 2 * Brush.Active.Count + 64) Prune(s_Shapes);
                s_Shapes[brush] = cached = new CachedShape();
            }
            else if (cached.polyhedron != null && cached.shape == shape && Same(cached.parameters, parameters)) { bounds = cached.bounds; return cached.polyhedron; }
            var poly = BrushGeometry.ShapePolyhedron(shape, parameters);
            cached.shape = shape; cached.parameters = parameters;
            cached.polyhedron = poly != null && poly.IsValid ? poly : null;
            cached.bounds = cached.polyhedron != null ? cached.polyhedron.Bounds() : default;
            bounds = cached.bounds;
            return cached.polyhedron;
        }

        static bool Same(in BrushGeometry.ShapeParams a, in BrushGeometry.ShapeParams b)
        {
            if (a.size.x != b.size.x || a.size.y != b.size.y || a.size.z != b.size.z || a.sides != b.sides || a.tessellation != b.tessellation || a.stepHeight != b.stepHeight || a.wallThickness != b.wallThickness) return false;
            var x = a.stairs; var y = b.stairs;
            return x.innerRadius == y.innerRadius && x.stepWidth == y.stepWidth && x.stepHeight == y.stepHeight && x.stepThickness == y.stepThickness && x.curveAngle == y.curveAngle
                && x.addToFirstStep == y.addToFirstStep && x.numSteps == y.numSteps && x.stepsPer360 == y.stepsPer360 && x.counterClockwise == y.counterClockwise
                && x.slopedFloor == y.slopedFloor && x.slopedCeiling == y.slopedCeiling;
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
            BrushSnap.InvalidateParents();
            BrushCsg.InvalidateGrouping(); // an undone reorder or reparent
            foreach (var group in Object.FindObjectsByType<BrushGroup>(FindObjectsInactive.Exclude)) BrushCsg.ApplyRendererSettings(group); // undone rendering settings
            var active = Brush.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var brush = active[i];
                if (brush == null) continue;
                BrushSnap.Snap(brush); // a redo re-applies the recorded pose; keep the grid rule
                BrushSync.Ensure(brush);
            }
        }

        static Texture2D s_GroupIcon;

        /// <summary>Brush groups carry their icon at the right end of their Hierarchy row, so the groups that build a level stand out.</summary>
        static void DrawGroupIcon(EntityId entityId, Rect row)
        {
            var go = EditorUtility.EntityIdToObject(entityId) as GameObject;
            if (go == null || !go.TryGetComponent<BrushGroup>(out _)) return;
            if (s_GroupIcon == null) s_GroupIcon = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/BrushGroup.png");
            if (s_GroupIcon != null) GUI.DrawTexture(new Rect(row.xMax - 16f, row.y, 16f, 16f), s_GroupIcon, ScaleMode.ScaleToFit);
        }

        /// <summary>A module added to or removed from an object: the pieces of the brushes it tags change.</summary>
        static void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            if (Application.isPlaying) return;
            for (int i = 0; i < stream.length; i++)
            {
                var kind = stream.GetEventType(i);
                if (kind == ObjectChangeKind.ChangeGameObjectStructure || kind == ObjectChangeKind.ChangeGameObjectStructureHierarchy || kind == ObjectChangeKind.ChangeGameObjectParent) { BrushCsg.MarkAllDirty(); return; }
            }
        }

        static void OnHierarchyChanged()
        {
            if (Application.isPlaying || processing) return;
            BrushSnap.InvalidateParents();
            BrushCsg.MarkAllDirty(); // order, parents, deletions: the brush lists are re-derived on the next build
            BrushGenerators.MarkAllDirty(); // a door put under a floor plan (or taken out) is placed on its walls
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
                if (go != null && go.TryGetComponent<Brush>(out var generatedBrush) && generatedBrush.IsGenerated)
                {
                    var owner = generatedBrush.generatedBy.gameObject;
                    if (!result.Contains(owner)) result.Add(owner);
                    changed = true; // a generated brush: its generator is what to edit
                    continue;
                }
                if (go != null && (BrushGroup.IsMeshChildName(go.name) || go.name == Colliders.ConvexColliderSettings.ContainerName || go.name == BrushGroup.DefaultName) && !BrushSettings.instance.showGenerated)
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

        /// <summary>Unity's grid snapping and its visible Scene view grid follow the world preset's grid size.</summary>
        public static void ApplyGrid()
        {
            var s = BrushSettings.instance;
            float g = s.GridMeters;
            EditorSnapSettings.move = new Vector3(g, g, g);
            EditorSnapSettings.gridSize = new Vector3(g, g, g);
            EditorSnapSettings.rotate = s.rotationSnapDegrees;
            // Unity's absolute grid snapping (Global handle) matches how Unreal and TrenchBroom snap: to the
            // world grid, not by increments from where the object happens to be.
            EditorSnapSettings.gridSnapEnabled = s.snapToGrid;
            if (s.snapToGrid) Tools.pivotRotation = PivotRotation.Global;
        }
    }
}

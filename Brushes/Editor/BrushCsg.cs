using System.Collections.Generic;
using System.Diagnostics;
using CsgBrush.Colliders;
using CsgBrush.Colliders.Editor;
using CsgBrush.Manifold;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Builds the render mesh of every <see cref="BrushGroup"/> with the Manifold library, then hands the brushes'
    /// convex parts to the collider builder.
    ///
    /// Ordered CSG, as in Quake and Unreal editors: a subtract brush cuts every brush above it in the hierarchy.
    /// Because union distributes over subtraction, that equals "every additive brush minus every later cutter
    /// that overlaps it, all unioned", which is what is computed: each brush's solid and each brush's cut piece
    /// are cached by content, so an edit only recomputes the pieces it touches, plus the final union.
    ///
    /// Every build re-derives the brush list from the scene and compares every key, so nothing stale can survive
    /// an undo, a delete or a reorder; the caches only skip work.
    /// </summary>
    public static class BrushCsg
    {
        sealed class SolidEntry { public int key; public ManifoldSolid solid; public Bounds bounds; public BrushPolyhedron polyhedron; public List<ConvexPolytope> partsAdd, partsRemove; public string partsProblem; }
        sealed class PieceEntry { public string key; public ManifoldSolid piece; public bool ownsPiece; }

        static readonly Dictionary<Brush, SolidEntry> s_Solids = new Dictionary<Brush, SolidEntry>();
        static readonly Dictionary<Brush, PieceEntry> s_Pieces = new Dictionary<Brush, PieceEntry>();
        static readonly HashSet<BrushGroup> s_Dirty = new HashSet<BrushGroup>();
        static bool s_AllDirty;
        static readonly Dictionary<BrushGroup, int> s_LastMeshKey = new Dictionary<BrushGroup, int>();
        static readonly Dictionary<Transform, Brush[]> s_TriangleBrush = new Dictionary<Transform, Brush[]>();

        /// <summary>For every triangle of the model's mesh (in submesh order), the brush whose face it lies on.</summary>
        public static Brush[] TriangleBrushes(BrushGroup model) { var t = MeshObject(model, false); return t != null ? TriangleBrushes(t) : null; }
        /// <summary>Per triangle of one mesh child (in submesh order): the brush whose face it belongs to.</summary>
        public static Brush[] TriangleBrushes(Transform meshObject) => s_TriangleBrush.TryGetValue(meshObject, out var a) ? a : null;

        /// <summary>Counters and timings of the last model build, for tests and the performance probe.</summary>
        public static int LastCachedSolids, LastBuiltSolids, LastCachedPieces, LastBuiltPieces, LastBrushes, LastTriangles, LastCachedIslands, LastBuiltIslands;
        public static double LastSolidsMs, LastPiecesMs, LastUnionMs, LastMeshMs, LastCollidersMs;

        public static void MarkDirty(BrushGroup model) { if (model != null) s_Dirty.Add(model); }
        public static void MarkDirty(Brush brush) { if (brush != null) MarkDirty(ModelOf(brush)); }
        public static void MarkAllDirty() { s_AllDirty = true; InvalidateGrouping(); BrushCache.InfoEpoch++; }
        public static bool HasDirty => s_AllDirty || s_Dirty.Count > 0;

        /// <summary>Forget every cached solid and piece; the next build computes everything again.</summary>
        public static void ClearCaches()
        {
            WaitForJob();
            InvalidateGrouping();
            foreach (var e in s_Pieces.Values) if (e.ownsPiece) e.piece.Dispose();
            s_Pieces.Clear();
            foreach (var e in s_Solids.Values) e.solid?.Dispose();
            s_Solids.Clear();
            s_LastMeshKey.Clear();
            s_Islands.Clear();
        }

        // ------------------------------------------------------------------ models

        /// <summary>
        /// The group that builds a brush: the nearest brush group above it, else its scene's automatic group. Null for a
        /// brush in a prefab (asset or Prefab Mode) with no group: a stamp, built by whatever level it is placed in.
        /// </summary>
        public static BrushGroup ModelOf(Brush brush)
        {
            var model = brush.GetComponentInParent<BrushGroup>(true);
            if (model != null) return model;
            if (IsInPrefabContext(brush.gameObject)) return null;
            return DefaultModel(brush.gameObject.scene, true);
        }

        /// <summary>A prefab asset, or the contents open in Prefab Mode or loaded for building: brushes there with no group are a stamp.</summary>
        public static bool IsInPrefabContext(GameObject go)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(go)) return true;
            if (PrefabStageUtility.GetPrefabStage(go) != null) return true;
            var scene = go.scene;
            return scene.IsValid() && EditorSceneManager.IsPreviewScene(scene);
        }

        static readonly Dictionary<Scene, BrushGroup> s_DefaultModels = new Dictionary<Scene, BrushGroup>();

        /// <summary>
        /// A scene's automatic group, created when needed (one per scene, at its root, hidden). Never registered with
        /// Undo: it is derived state. Remembered per scene and checked on every use, so the scene's roots are searched
        /// only when it is first needed or has gone (deleted, moved, scene closed).
        /// </summary>
        public static BrushGroup DefaultModel(Scene scene, bool create)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) return null;
            if (s_DefaultModels.TryGetValue(scene, out var known))
            {
                if (known != null && known.isDefault && known.transform.parent == null && known.gameObject.scene == scene) return known;
                s_DefaultModels.Remove(scene);
            }
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<BrushGroup>(out var m) && m.isDefault) { s_DefaultModels[scene] = m; return m; }
            if (!create) return null;
            var go = new GameObject(BrushGroup.DefaultName);
            SceneManager.MoveGameObjectToScene(go, scene);
            var model = go.AddComponent<BrushGroup>();
            model.isDefault = true;
            GameObjectUtility.SetStaticEditorFlags(go, BrushSettings.instance.defaultModelStaticFlags); // what its render meshes inherit; a group you make has its own
            s_DefaultModels[scene] = model;
            return model;
        }

        /// <summary>
        /// What builds a brush, for the Inspector: its group, or its scene (the scene's automatic group), or nothing
        /// (a stamp in a prefab). <paramref name="reference"/> is what to show and ping: the group or the scene asset.
        /// </summary>
        public static string BuiltBy(Brush brush, out Object reference)
        {
            reference = null;
            var group = brush.GetComponentInParent<BrushGroup>(true);
            if (group != null && !group.isDefault) { reference = group; return group.name; }
            if (IsInPrefabContext(brush.gameObject)) return "none (stamp)";
            var scene = brush.gameObject.scene;
            if (!string.IsNullOrEmpty(scene.path)) reference = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path);
            return "scene " + (string.IsNullOrEmpty(scene.name) ? "(unsaved)" : scene.name);
        }

        /// <summary>
        /// A group inside a prefab instance whose prefab already carries its built mesh and colliders, with no changes of
        /// its own in the scene: it is not rebuilt, so the instance keeps using the prefab's meshes and stays free of overrides.
        /// </summary>
        public static bool IsBuiltPrefabInstance(BrushGroup model, List<Brush> brushes)
        {
            if (model == null || model.isDefault || !PrefabUtility.IsPartOfPrefabInstance(model)) return false;
            bool any = false;
            foreach (var t in MeshObjects(model))
            {
                if (!t.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null || !EditorUtility.IsPersistent(mf.sharedMesh)) return false;
                any = true;
            }
            // the brushes still make exactly what the prefab built
            return any && Prepare(model, brushes).meshKey == model.builtKey;
        }

        /// <summary>
        /// Build a group again from scratch, ignoring every cache: into its prefab file when it belongs to a prefab
        /// (the asset, an instance, or Prefab Mode), in its scene otherwise; a prefab instance whose brushes differ from
        /// its prefab is rebuilt in the scene as well.
        /// </summary>
        public static void Rebuild(BrushGroup model)
        {
            if (model == null) return;
            var prefabPath = BrushPrefabBuild.PrefabPathOf(model);
            if (prefabPath != null) BrushPrefabBuild.Build(prefabPath);
            if (PrefabUtility.IsPartOfPrefabAsset(model)) return;
            var brushes = BrushPrefabBuild.BrushesOf(model);
            if (prefabPath != null && IsBuiltPrefabInstance(model, brushes)) return; // it shows the prefab's fresh meshes
            WaitForJob();
            foreach (var b in brushes)
            {
                if (s_Solids.TryGetValue(b, out var solid)) { solid.solid?.Dispose(); s_Solids.Remove(b); }
                if (s_Pieces.TryGetValue(b, out var piece)) { if (piece.ownsPiece) piece.piece?.Dispose(); s_Pieces.Remove(b); }
            }
            s_LastMeshKey.Remove(model);
            s_Islands.Remove(model);
            if (model.TryGetComponent<ConvexColliderSettings>(out var settings)) settings.lastGeometryHash = 0;
            ConvexColliderBuilder.Forget(model.transform);
            Rebuild(model, brushes);
        }

        /// <summary>A material's identity that survives editor sessions (its asset GUID), for the content keys.</summary>
        static int MaterialKey(Material material)
        {
            if (material == null) return 0;
            unchecked
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId)) return guid.GetHashCode() * 31 + localId.GetHashCode();
                return material.name.GetHashCode();
            }
        }

        static Dictionary<BrushGroup, List<Brush>> s_ByModel;
        static int s_ByModelVersion = -1;

        /// <summary>The hierarchy or the groups changed: <see cref="BrushesByModel"/> walks the scenes again next time.</summary>
        public static void InvalidateGrouping() => s_ByModelVersion = -1;

        /// <summary>
        /// Brushes of every group in CSG order (hierarchy order, depth first), in the loaded scenes and the open Prefab Mode.
        /// Inactive brushes are left out; a brush with no group in a prefab is a stamp and is not built there. Walking the
        /// scenes costs milliseconds in a large level, so the answer is kept until a brush joins or leaves, the hierarchy
        /// or a group changes (<see cref="InvalidateGrouping"/>), or one of its groups is destroyed. Do not modify it.
        /// </summary>
        public static Dictionary<BrushGroup, List<Brush>> BrushesByModel()
        {
            if (s_ByModel != null && s_ByModelVersion == Brush.ActiveVersion)
            {
                bool intact = true;
                foreach (var group in s_ByModel.Keys) if (group == null) { intact = false; break; }
                if (intact) return s_ByModel;
            }
            var result = new Dictionary<BrushGroup, List<Brush>>();
            BrushGroup sceneDefault = null; // the walked scene's automatic group, looked up once it is needed
            void Walk(Transform t, BrushGroup current, Scene scene, bool prefab)
            {
                if (t.TryGetComponent<BrushGroup>(out var m)) { current = m; if (!result.ContainsKey(m)) result[m] = new List<Brush>(); }
                if (t.TryGetComponent<Brush>(out var b) && b.enabled && t.gameObject.activeInHierarchy)
                {
                    var target = current;
                    if (target == null && !prefab) target = sceneDefault ??= DefaultModel(scene, true); // each scene's own automatic group
                    if (target != null)
                    {
                        if (!result.TryGetValue(target, out var list)) result[target] = list = new List<Brush>();
                        list.Add(b);
                    }
                }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), current, scene, prefab);
            }
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                sceneDefault = DefaultModel(scene, false);
                if (sceneDefault != null && !result.ContainsKey(sceneDefault)) result[sceneDefault] = new List<Brush>();
                foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, null, scene, false);
            }
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null) Walk(stage.prefabContentsRoot.transform, null, stage.scene, true);
            s_ByModel = result; s_ByModelVersion = Brush.ActiveVersion;
            return result;
        }

        /// <summary>
        /// Additive brushes that overlap a subtract brush but sit below it in CSG order, so it does not cut them.
        /// Shown in the Inspector and the Scene view: the usual reason a cut "does not work".
        /// </summary>
        public static List<Brush> UncutBelow(Brush cutter)
        {
            var result = new List<Brush>();
            if (cutter == null || cutter.operation != BrushOperation.Subtract) return result;
            var byModel = BrushesByModel();
            var cutterModel = ModelOf(cutter);
            if (cutterModel == null || !byModel.TryGetValue(cutterModel, out var list)) return result;
            int layer = cutter.gameObject.layer;
            int index = list.IndexOf(cutter);
            if (index < 0) return result;
            var cutterBounds = BrushGeometry.Polyhedron(cutter).Transformed(BrushGeometry.LocalToWorld(cutter)).Bounds();
            for (int i = index + 1; i < list.Count; i++)
            {
                var b = list[i];
                if (b.operation != BrushOperation.Add || b.gameObject.layer != layer) continue;
                var bb = BrushGeometry.Polyhedron(b).Transformed(BrushGeometry.LocalToWorld(b)).Bounds();
                if (!bb.Intersects(cutterBounds)) continue;
                var overlap = Vector3.Min(bb.max, cutterBounds.max) - Vector3.Max(bb.min, cutterBounds.min);
                if (overlap.x > 1e-3f && overlap.y > 1e-3f && overlap.z > 1e-3f) result.Add(b);
            }
            return result;
        }

        // ------------------------------------------------------------------ building

        /// <summary>
        /// One model build in three steps: Prepare (main thread: brushes, transforms, keys, polyhedra of solids that
        /// are not cached), Compute (any thread: Manifold solids, pieces, union, triangle read-back), Apply (main
        /// thread: caches, Unity mesh, colliders). The sync path runs all three; the editor loop runs Compute on a
        /// worker so a drag never waits for the union.
        /// </summary>
        sealed class Job
        {
            public BrushGroup model;
            public List<Brush> brushes;
            public List<Record> records = new List<Record>();
            public List<int> newSolidIndices = new List<int>();     // records whose solid must be built
            public List<PolyData> newSolidData = new List<PolyData>();
            public List<ManifoldSolid> newSolids = new List<ManifoldSolid>();
            public List<int> pieceRecord = new List<int>();          // per additive piece: record index
            public List<string> pieceKeys = new List<string>();
            public List<List<int>> pieceCutters = new List<List<int>>(); // record indices of the cutters
            public List<PieceEntry> pieceEntries = new List<PieceEntry>(); // cached or built
            public List<bool> pieceBuilt = new List<bool>();
            public bool needMesh;
            public int meshKey;
            public Bounds[] bounds;                                    // per record, in the group's space
            public Dictionary<string, Island> islandsBefore;           // this group's islands from the last build (read only)
            public Dictionary<string, Island> islandsAfter = new Dictionary<string, Island>();
            public List<(int layer, List<Island> islands)> layers = new List<(int, List<Island>)>();
            public int cachedIslands, builtIslands;
            public double solidsMs, piecesMs, unionMs;
            public int cachedSolids, builtSolids, cachedPieces, builtPieces;
            public System.Exception error;
        }

        struct PolyData { public Vector3[] vertices; public List<int[]> faces; public Vector3[] innerVertices; public List<int[]> innerFaces; public Bounds bounds; public BrushPolyhedron polyhedron; }

        struct Record { public Brush brush; public SolidEntry solid; public Matrix4x4 toModel; public int key; public bool subtract; public int layer; }

        static System.Threading.Tasks.Task s_Task;
        static Job s_Running;
        static double s_LastBuildMs;
        /// <summary>Builds that took longer than this (ms) go to the worker thread from then on; 0 forces the worker.</summary>
        public static double AsyncThresholdMs = 8.0;

        /// <summary>A background build is in flight.</summary>
        public static bool Busy => s_Task != null && !s_Task.IsCompleted;

        /// <summary>Rebuild the dirty models now, synchronously (after any background build has landed).</summary>
        public static void RebuildDirtyNow()
        {
            WaitForJob();
            if (!HasDirty) return;
            var byModel = BrushesByModel();
            var targets = s_AllDirty ? new List<BrushGroup>(byModel.Keys) : new List<BrushGroup>(s_Dirty);
            s_Dirty.Clear(); s_AllDirty = false;
            foreach (var model in targets)
            {
                if (model == null) continue;
                var list = byModel.TryGetValue(model, out var l) ? l : new List<Brush>();
                if (IsBuiltPrefabInstance(model, list)) continue;
                Rebuild(model, list);
            }
            PruneCaches(byModel);
        }

        public static void RebuildAllNow() { MarkAllDirty(); RebuildDirtyNow(); }

        /// <summary>Build one model now (all three steps on the calling thread).</summary>
        public static void Rebuild(BrushGroup model, List<Brush> brushes)
        {
            WaitForJob();
            var sw = Stopwatch.StartNew();
            var job = Prepare(model, brushes);
            Compute(job);
            Apply(job);
            s_LastBuildMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Editor loop entry: apply a finished background build, then start the next one for the dirty models. Small
        /// builds run synchronously (they are faster than a frame of latency); large ones go to a worker thread.
        /// </summary>
        public static void Pump()
        {
            if (s_Task != null)
            {
                if (!s_Task.IsCompleted) return;
                var job = s_Running; s_Task = null; s_Running = null;
                if (job.error != null) UnityEngine.Debug.LogException(job.error);
                else if (job.model != null) Apply(job);
                else DisposeJob(job);
            }
            if (!HasDirty) return;
            if (s_LastBuildMs < AsyncThresholdMs) { RebuildDirtyNow(); return; }
            var byModel = BrushesByModel();
            var targets = s_AllDirty ? new List<BrushGroup>(byModel.Keys) : new List<BrushGroup>(s_Dirty);
            s_Dirty.Clear(); s_AllDirty = false;
            // one model per pump; the others stay dirty for the next frames
            BrushGroup first = null; foreach (var m in targets) { if (m == null || IsBuiltPrefabInstance(m, byModel.TryGetValue(m, out var ml) ? ml : new List<Brush>())) continue; if (first == null) first = m; else s_Dirty.Add(m); }
            if (first == null) return;
            var running = Prepare(first, byModel.TryGetValue(first, out var list) ? list : new List<Brush>());
            s_Running = running;
            s_Task = System.Threading.Tasks.Task.Run(() => { try { Compute(running); } catch (System.Exception e) { running.error = e; } });
        }

        static void WaitForJob()
        {
            if (s_Task == null) return;
            try { s_Task.Wait(); } catch { }
            var job = s_Running; s_Task = null; s_Running = null;
            if (job != null) { if (job.error == null && job.model != null) Apply(job); else DisposeJob(job); }
        }

        static void DisposeJob(Job job)
        {
            foreach (var solid in job.newSolids) solid?.Dispose();
            for (int i = 0; i < job.pieceEntries.Count; i++) if (job.pieceBuilt[i] && job.pieceEntries[i].ownsPiece) job.pieceEntries[i].piece.Dispose();
        }

        // ---- step 1: main thread
        static Job Prepare(BrushGroup model, List<Brush> brushes)
        {
            var job = new Job { model = model, brushes = brushes };
            foreach (var b in brushes)
            {
                // pose and key from the last build unless the brush changed since (BrushCache.Forget)
                var cache = BrushCache.Of(b);
                if (!ReferenceEquals(cache.keyModel, model)) { cache.toModel = BrushGeometry.ToModel(b, model); cache.key = BrushGeometry.SolidKey(b, cache.toModel); cache.keyModel = model; }
                var toModel = cache.toModel; int key = cache.key;
                s_Solids.TryGetValue(b, out var entry);
                var r = new Record { brush = b, toModel = toModel, key = key, subtract = b.operation == BrushOperation.Subtract, layer = Info(b).layer, solid = entry != null && entry.key == key ? entry : null };
                job.records.Add(r);
                if (r.solid == null)
                {
                    var poly = BrushGeometry.Polyhedron(b).Transformed(toModel);
                    var data = new PolyData { vertices = poly.vertices, faces = new List<int[]>(poly.faces.Length), bounds = poly.Bounds(), polyhedron = poly };
                    foreach (var f in poly.faces) data.faces.Add(f.indices);
                    var inner = BrushGeometry.HollowInner(b);
                    if (inner != null)
                    {
                        var ip = inner.Transformed(toModel);
                        data.innerVertices = ip.vertices; data.innerFaces = new List<int[]>(ip.faces.Length);
                        foreach (var f in ip.faces) data.innerFaces.Add(f.indices);
                    }
                    job.newSolidIndices.Add(job.records.Count - 1);
                    job.newSolidData.Add(data);
                    job.newSolids.Add(null);
                }
            }
            // bounds of every record (cached or to be built) for the overlap test
            var bounds = new Bounds[job.records.Count];
            for (int i = 0; i < job.records.Count; i++) bounds[i] = job.records[i].solid != null ? job.records[i].solid.bounds : default;
            for (int n = 0; n < job.newSolidIndices.Count; n++) bounds[job.newSolidIndices[n]] = job.newSolidData[n].bounds;
            job.bounds = bounds;
            s_Islands.TryGetValue(model, out job.islandsBefore);
            // pieces: which cutters apply, and which pieces are already cached
            var subtracts = new List<int>();
            for (int i = 0; i < job.records.Count; i++) if (job.records[i].subtract) subtracts.Add(i);
            var keyBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < job.records.Count; i++)
            {
                var r = job.records[i];
                if (r.subtract) continue;
                keyBuilder.Clear(); keyBuilder.Append(r.key);
                var cutters = new List<int>();
                foreach (int j in subtracts)
                {
                    if (j <= i) continue; // a subtract cuts only the brushes above it
                    var c = job.records[j];
                    if (c.layer != r.layer || !bounds[j].Intersects(bounds[i])) continue; // each layer is combined on its own
                    cutters.Add(j); keyBuilder.Append('-').Append(c.key);
                }
                string pieceKey = keyBuilder.ToString();
                s_Pieces.TryGetValue(r.brush, out var pe);
                bool cached = pe != null && pe.key == pieceKey;
                job.pieceRecord.Add(i); job.pieceKeys.Add(pieceKey); job.pieceCutters.Add(cutters);
                job.pieceEntries.Add(cached ? pe : new PieceEntry { key = pieceKey }); job.pieceBuilt.Add(!cached);
            }
            unchecked
            {
                int meshKey = 17;
                for (int i = 0; i < job.pieceKeys.Count; i++) { var pr = job.records[job.pieceRecord[i]]; meshKey = meshKey * 31 + job.pieceKeys[i].GetHashCode(); var mat = pr.brush.material; if (mat == null) mat = DefaultMaterial(); meshKey = meshKey * 31 + MaterialKey(mat); meshKey = meshKey * 31 + pr.layer; meshKey = meshKey * 31 + StaticFlags(pr.brush); } // static flags: which mesh its triangles go to
                job.meshKey = meshKey;
            }
            bool anyMesh = false; foreach (var t in MeshObjects(model)) if (t.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) { anyMesh = true; break; }
            job.needMesh = !(anyMesh && s_LastMeshKey.TryGetValue(model, out var last) && last == job.meshKey);
            return job;
        }

        // ---- step 2: any thread (Manifold and arrays only)
        static void Compute(Job job)
        {
            var sw = Stopwatch.StartNew();
            var solids = new ManifoldSolid[job.records.Count];
            for (int i = 0; i < job.records.Count; i++) solids[i] = job.records[i].solid?.solid;
            for (int n = 0; n < job.newSolidIndices.Count; n++)
            {
                var data = job.newSolidData[n];
                var solid = ManifoldSolid.FromFaces(data.vertices, data.faces, out _);
                if (solid != null && data.innerVertices != null)
                {
                    using var inner = ManifoldSolid.FromFaces(data.innerVertices, data.innerFaces, out _);
                    if (inner != null) { var hollow = ManifoldSolid.Boolean(solid, inner, ManifoldNative.OpType.Subtract); solid.Dispose(); solid = hollow; }
                }
                job.newSolids[n] = solid;
                solids[job.newSolidIndices[n]] = solid;
            }
            job.builtSolids = job.newSolidIndices.Count; job.cachedSolids = job.records.Count - job.builtSolids;
            job.solidsMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            var piecesByLayer = new Dictionary<int, List<int>>(); var layerOrder = new List<int>(); // piece indices
            for (int p = 0; p < job.pieceRecord.Count; p++)
            {
                var pe = job.pieceEntries[p];
                if (job.pieceBuilt[p])
                {
                    var own = solids[job.pieceRecord[p]];
                    if (own == null) { pe.piece = null; pe.ownsPiece = false; }
                    else
                    {
                        var cutters = new List<ManifoldSolid>();
                        foreach (var j in job.pieceCutters[p]) if (solids[j] != null) cutters.Add(solids[j]);
                        if (cutters.Count == 0) { pe.piece = own; pe.ownsPiece = false; }
                        else
                        {
                            var cut = cutters.Count == 1 ? cutters[0] : ManifoldSolid.Batch(cutters, ManifoldNative.OpType.Add);
                            pe.piece = ManifoldSolid.Boolean(own, cut, ManifoldNative.OpType.Subtract); pe.ownsPiece = true;
                            if (cutters.Count > 1) cut.Dispose();
                        }
                    }
                    job.builtPieces++;
                }
                else job.cachedPieces++;
                if (pe.piece != null)
                {
                    int layer = job.records[job.pieceRecord[p]].layer;
                    if (!piecesByLayer.TryGetValue(layer, out var list)) { piecesByLayer[layer] = list = new List<int>(); layerOrder.Add(layer); }
                    list.Add(p);
                }
            }
            job.piecesMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            if (job.needMesh)
            {
                // per layer (layers never interact), per island: only islands with a changed piece are united again
                var keyBuilder = new System.Text.StringBuilder();
                foreach (var layer in layerOrder)
                {
                    var islands = new List<Island>();
                    foreach (var members in Connected(piecesByLayer[layer], p => job.bounds[job.pieceRecord[p]]))
                    {
                        keyBuilder.Clear().Append(layer);
                        foreach (int p in members) keyBuilder.Append('/').Append(job.pieceKeys[p]);
                        string key = keyBuilder.ToString();
                        if (job.islandsBefore == null || !job.islandsBefore.TryGetValue(key, out var island))
                        {
                            var pieces = new List<ManifoldSolid>(members.Count);
                            var sources = new Dictionary<int, Brush>();
                            foreach (int p in members)
                            {
                                pieces.Add(job.pieceEntries[p].piece);
                                int r = job.pieceRecord[p];
                                if (solids[r] != null) sources[solids[r].OriginalId] = job.records[r].brush;
                                foreach (int c in job.pieceCutters[p]) if (solids[c] != null) sources[solids[c].OriginalId] = job.records[c].brush; // carved faces take the cutter's material
                            }
                            using var union = pieces.Count == 1 ? null : ManifoldSolid.Batch(pieces, ManifoldNative.OpType.Add);
                            island = new Island { key = key, data = (union ?? pieces[0]).ToMesh(), sources = sources };
                            job.builtIslands++;
                        }
                        else job.cachedIslands++;
                        job.islandsAfter[key] = island;
                        islands.Add(island);
                    }
                    job.layers.Add((layer, islands));
                }
            }
            job.unionMs = sw.Elapsed.TotalMilliseconds;
        }

        // ---- step 3: main thread
        static void Apply(Job job)
        {
            var sw = Stopwatch.StartNew();
            var model = job.model;
            LastBrushes = job.records.Count;
            LastCachedSolids = job.cachedSolids; LastBuiltSolids = job.builtSolids; LastCachedPieces = job.cachedPieces; LastBuiltPieces = job.builtPieces;
            LastSolidsMs = job.solidsMs; LastPiecesMs = job.piecesMs; LastUnionMs = job.unionMs;
            // solids into the cache
            for (int n = 0; n < job.newSolidIndices.Count; n++)
            {
                var r = job.records[job.newSolidIndices[n]];
                if (r.brush == null) { job.newSolids[n]?.Dispose(); continue; }
                if (s_Solids.TryGetValue(r.brush, out var old)) old.solid?.Dispose();
                var data = job.newSolidData[n];
                var entry = new SolidEntry { key = r.key, solid = job.newSolids[n], bounds = data.bounds, polyhedron = data.polyhedron };
                if (entry.solid == null) r.brush.problem = "The shape could not be built.";
                s_Solids[r.brush] = entry;
                r.solid = entry; job.records[job.newSolidIndices[n]] = r;
            }
            // pieces into the cache
            for (int p = 0; p < job.pieceRecord.Count; p++)
            {
                if (!job.pieceBuilt[p]) continue;
                var r = job.records[job.pieceRecord[p]];
                var pe = job.pieceEntries[p];
                if (r.brush == null) { if (pe.ownsPiece) pe.piece.Dispose(); continue; }
                if (s_Pieces.TryGetValue(r.brush, out var old) && old.ownsPiece) old.piece.Dispose();
                s_Pieces[r.brush] = pe;
            }
            if (model == null) return;
            if (job.needMesh)
            {
                LastTriangles = 0;
                var kept = new HashSet<Transform>();
                foreach (var (layer, islands) in job.layers)
                {
                    foreach (var island in islands) LastTriangles += island.data.triangles != null ? island.data.triangles.Length / 3 : 0;
                    foreach (var written in ApplyMesh(model, layer, islands)) kept.Add(written);
                }
                s_Islands[model] = job.islandsAfter; // islands no longer in the group are dropped
                LastCachedIslands = job.cachedIslands; LastBuiltIslands = job.builtIslands;
                foreach (var t in MeshObjects(model)) if (!kept.Contains(t)) { s_TriangleBrush.Remove(t); Object.DestroyImmediate(t.gameObject); } // a layer nothing is on any more
                s_LastMeshKey[model] = job.meshKey;
                if (model.builtKey != job.meshKey) model.builtKey = job.meshKey;
            }
            LastMeshMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            // colliders from the convex parts (deferred by the builder during drags)
            var inputs = new List<ConvexColliderBuilder.Input>(job.records.Count);
            foreach (var r in job.records)
            {
                if (r.brush == null || r.solid == null) continue;
                if (r.solid.partsAdd == null)
                {
                    r.solid.partsAdd = new List<ConvexPolytope>(); r.solid.partsRemove = new List<ConvexPolytope>();
                    string problem = null;
                    BrushGeometry.ConvexParts(r.brush, r.toModel, r.solid.partsAdd, r.solid.partsRemove, ref problem);
                    r.solid.partsProblem = problem;
                }
                if (r.solid.partsProblem != null && r.brush.problem == null) r.brush.problem = r.solid.partsProblem;
                var info = Info(r.brush);
                inputs.Add(new ConvexColliderBuilder.Input { name = info.name, subtract = r.subtract, kind = info.kind, fingerprint = info.fingerprint, layer = r.layer, add = r.solid.partsAdd, remove = r.solid.partsRemove, onPiece = info.onPiece, owner = r.brush, key = r.key });
            }
            var settings = model.GetComponent<ConvexColliderSettings>();
            if (settings == null) settings = model.gameObject.AddComponent<ConvexColliderSettings>();
            ConvexColliderBuilder.Rebuild(model.transform, settings, inputs);
            BrushSync.HideGenerated(model); // hidden from the moment they exist, in scenes, Prefab Mode and prefabs being built
            LastCollidersMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>A brush's collider data, taken once and again only after it changed (<see cref="BrushCache"/>): reading it means several calls into Unity per brush.</summary>
        static BrushCache Info(Brush brush)
        {
            var c = BrushCache.Of(brush);
            if (c.infoEpoch == BrushCache.InfoEpoch) return c;
            c.infoEpoch = BrushCache.InfoEpoch;
            c.fingerprint = PieceFingerprint(brush); c.kind = brush.EffectiveCollision(); c.name = brush.name; c.layer = brush.gameObject.layer;
            c.onPiece ??= (piece, trigger) =>
            {
                // a piece is its brush: tag, static flags, physics material and contacts come from it (runs only for new pieces)
                if (piece.tag != brush.gameObject.tag) piece.tag = brush.gameObject.tag;
                var staticFlags = GameObjectUtility.GetStaticEditorFlags(brush.gameObject);
                if (GameObjectUtility.GetStaticEditorFlags(piece) != staticFlags) GameObjectUtility.SetStaticEditorFlags(piece, staticFlags);
                if (piece.TryGetComponent<Collider>(out var collider)) { collider.sharedMaterial = brush.physicsMaterial; collider.providesContacts = brush.provideContacts; }
                if (trigger) { if (!piece.TryGetComponent<BrushTriggerRelay>(out var relay)) relay = piece.AddComponent<BrushTriggerRelay>(); relay.brush = brush; }
                foreach (var m in brush.Modules()) if (m != null && m.enabled) m.ApplyToPiece(piece, trigger);
            };
            return c;
        }

        /// <summary>Everything a brush puts on its pieces beyond their planes (collision, material, contacts, tag, static flags, modules), hashed: the piece identity.</summary>
        public static int PieceFingerprint(Brush brush)
        {
            unchecked
            {
                int h = brush.ModuleFingerprint() * 31 + (int)GameObjectUtility.GetStaticEditorFlags(brush.gameObject);
                return h * 31 + (brush.physicsMaterial != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(brush.physicsMaterial) : 0); // the material itself, not only its name
            }
        }

        static void PruneCaches(Dictionary<BrushGroup, List<Brush>> byModel)
        {
            if (Busy) return;
            var live = new HashSet<Brush>();
            foreach (var list in byModel.Values) foreach (var b in list) live.Add(b);
            var dead = new List<Brush>();
            foreach (var kv in s_Solids) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) { s_Solids[id].solid?.Dispose(); s_Solids.Remove(id); }
            dead.Clear();
            foreach (var kv in s_Pieces) if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) { if (s_Pieces[id].ownsPiece) s_Pieces[id].piece.Dispose(); s_Pieces.Remove(id); }
            if (s_Islands.Count > byModel.Count)
            {
                var gone = new List<BrushGroup>();
                foreach (var group in s_Islands.Keys) if (group == null || !byModel.ContainsKey(group)) gone.Add(group);
                foreach (var group in gone) s_Islands.Remove(group);
            }
        }

        // ------------------------------------------------------------------ mesh output

        static Material s_DefaultMaterial;

        /// <summary>The material for brushes without one: the project's default brush material (the generated grid texture unless set), else the render pipeline's default.</summary>
        public static Material DefaultMaterial()
        {
            var project = BrushGridMaterial.GetOrCreate();
            if (project != null) return project;
            if (s_DefaultMaterial != null) return s_DefaultMaterial;
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null) pipeline = QualitySettings.renderPipeline;
            if (pipeline != null) s_DefaultMaterial = pipeline.defaultMaterial;
            if (s_DefaultMaterial == null) s_DefaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (s_DefaultMaterial == null) s_DefaultMaterial = Resources.GetBuiltinResource<Material>("Default-Diffuse.mat");
            return s_DefaultMaterial;
        }

        /// <summary>The render mesh of the brushes on the Default layer with the project's static flags (see <see cref="MeshObject(BrushGroup, int, StaticEditorFlags, bool)"/>).</summary>
        public static Transform MeshObject(BrushGroup model, bool create) => MeshObject(model, 0, create);

        /// <summary>A layer's render mesh for brushes with the project's static flags, or (not creating) the layer's first one of any flags.</summary>
        public static Transform MeshObject(BrushGroup model, int layer, bool create)
        {
            var t = MeshObject(model, layer, BrushSettings.instance.defaultModelStaticFlags, create);
            if (t != null || create) return t;
            foreach (var child in MeshObjects(model)) if (child.gameObject.layer == layer) return child;
            return null;
        }

        /// <summary>
        /// The name of the render mesh child for a layer and a set of static flags: the layer's plain name for the project's
        /// flags (Project Settings > Brushes), "moving" for none, the flags' number otherwise.
        /// </summary>
        public static string MeshChildName(int layer, StaticEditorFlags flags)
        {
            string name = BrushGroup.MeshChildNameFor(layer);
            if (flags == BrushSettings.instance.defaultModelStaticFlags) return name;
            string suffix = flags == 0 ? "moving" : "static " + (int)flags;
            return name == BrushGroup.MeshChildName ? "<[mesh " + suffix + "]>" : name.Substring(0, name.Length - 2) + " " + suffix + "]>";
        }

        /// <summary>A group's rendering settings onto one of its render meshes (materials come from the brushes).</summary>
        public static void ApplyRendererSettings(BrushGroup model, MeshRenderer r)
        {
            if (model == null || r == null) return;
            var g = model.rendering ?? (model.rendering = new RenderingSettings());
            if (r.shadowCastingMode != g.castShadows) r.shadowCastingMode = g.castShadows;
            if (r.staticShadowCaster != g.staticShadowCaster) r.staticShadowCaster = g.staticShadowCaster;
            if (r.receiveShadows != g.receiveShadows) r.receiveShadows = g.receiveShadows;
            if (r.receiveGI != g.receiveGlobalIllumination) r.receiveGI = g.receiveGlobalIllumination;
            if (r.lightProbeUsage != g.lightProbes) r.lightProbeUsage = g.lightProbes;
            if (r.reflectionProbeUsage != g.reflectionProbes) r.reflectionProbeUsage = g.reflectionProbes;
            if (r.probeAnchor != g.anchorOverride) r.probeAnchor = g.anchorOverride;
            if (r.motionVectorGenerationMode != g.motionVectors) r.motionVectorGenerationMode = g.motionVectors;
            if (r.allowOcclusionWhenDynamic != g.dynamicOcclusion) r.allowOcclusionWhenDynamic = g.dynamicOcclusion;
            if (r.renderingLayerMask != g.renderingLayerMask) r.renderingLayerMask = g.renderingLayerMask;
            // lightmap settings have no scripting property
            var so = new SerializedObject(r);
            bool changed = false;
            var scale = so.FindProperty("m_ScaleInLightmap"); if (scale != null && scale.floatValue != g.scaleInLightmap) { scale.floatValue = g.scaleInLightmap; changed = true; }
            var stitch = so.FindProperty("m_StitchLightmapSeams"); if (stitch != null && stitch.boolValue != g.stitchLightmapSeams) { stitch.boolValue = g.stitchLightmapSeams; changed = true; }
            var parameters = so.FindProperty("m_LightmapParameters");
            var wanted = g.lightmapParameters as LightmapParameters;
            if (parameters != null && parameters.objectReferenceValue != wanted) { parameters.objectReferenceValue = wanted; changed = true; }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A group's rendering settings onto all its render meshes again (they changed).</summary>
        public static void ApplyRendererSettings(BrushGroup model)
        {
            if (model == null) return;
            foreach (var t in MeshObjects(model)) if (t.TryGetComponent<MeshRenderer>(out var mr)) ApplyRendererSettings(model, mr);
        }

        public static Transform MeshObject(BrushGroup model, int layer, StaticEditorFlags flags, bool create) => MeshObject(model, MeshChildName(layer, flags), layer, flags, create);

        static Transform MeshObject(BrushGroup model, string name, int layer, StaticEditorFlags flags, bool create)
        {
            var t = model.transform.Find(name);
            if (t == null && create)
            {
                var go = new GameObject(name) { layer = layer };
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                t = go.transform;
            }
            if (t != null)
            {
                // static flags (lightmaps, occlusion, batching) come from the brushes it holds; the tag from the model object
                if (t.gameObject.layer != layer) t.gameObject.layer = layer;
                if (t.gameObject.tag != model.gameObject.tag) t.gameObject.tag = model.gameObject.tag;
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != flags) GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }
            return t;
        }

        /// <summary>Every render mesh child of a model, one per layer in use.</summary>
        public static List<Transform> MeshObjects(BrushGroup model)
        {
            var list = new List<Transform>();
            foreach (Transform child in model.transform) if (BrushGroup.IsMeshChildName(child.name)) list.Add(child);
            return list;
        }

        struct Corner { public Vector3 p; public int face; public int source; }

        /// <summary>
        /// Brushes whose boxes overlap or touch, on one layer: their pieces are united with each other and never with another
        /// island's, since solids apart from each other do not interact. An island keeps its union and its Unity arrays until
        /// one of its pieces changes, so a brush that touches nothing costs only itself when it moves.
        /// </summary>
        sealed class Island
        {
            public string key;                         // its pieces' keys, in CSG order
            public ManifoldSolid.MeshData data;        // their union, read back
            public Dictionary<int, Brush> sources;     // solid id (triangleSource) to brush, cutters included: whose material a triangle takes
            // the Unity arrays, built on the main thread; valid while every source brush has the material and static flags it had then
            public Material[] convertedWith; public int[] convertedFlags; public Material convertedDefault; public int convertedDefaultFlags;
            public List<Part> parts;
        }

        /// <summary>An island's triangles of one set of static flags: they render in the mesh child of those flags.</summary>
        sealed class Part
        {
            public int flags;
            public List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>(); public List<Vector2> uvs = new List<Vector2>();
            public List<Material> materials = new List<Material>(); public List<List<int>> triangles = new List<List<int>>(); public List<List<Brush>> triangleBrush = new List<List<Brush>>();
            public readonly Dictionary<Material, int> materialIndex = new Dictionary<Material, int>();
            public readonly Dictionary<(Vector3, int, int), int> lookup = new Dictionary<(Vector3, int, int), int>();
        }

        static readonly Dictionary<BrushGroup, Dictionary<string, Island>> s_Islands = new Dictionary<BrushGroup, Dictionary<string, Island>>();

        /// <summary>Group items whose boxes overlap or touch (padded a hair, so faces meeting at float precision join): a sweep along the widest axis, then union-find.</summary>
        static List<List<int>> Connected(List<int> items, System.Func<int, Bounds> boundsOf)
        {
            int n = items.Count;
            var box = new Bounds[n];
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            for (int i = 0; i < n; i++) { box[i] = boundsOf(items[i]); box[i].Expand(1e-3f); lo = Vector3.Min(lo, box[i].center); hi = Vector3.Max(hi, box[i].center); }
            var spread = hi - lo;
            int axis = spread.x >= spread.y && spread.x >= spread.z ? 0 : spread.y >= spread.z ? 1 : 2;
            var order = new int[n]; for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => box[a].min[axis].CompareTo(box[b].min[axis]));
            var parent = new int[n]; for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            var open = new List<int>();
            foreach (int i in order)
            {
                float start = box[i].min[axis];
                open.RemoveAll(j => box[j].max[axis] < start); // closed before this one begins
                foreach (int j in open) if (box[i].Intersects(box[j])) parent[Find(i)] = Find(j);
                open.Add(i);
            }
            var byRoot = new Dictionary<int, List<int>>(); var groups = new List<List<int>>();
            for (int i = 0; i < n; i++) // in item order, so each island lists its pieces in CSG order
            {
                int r = Find(i);
                if (!byRoot.TryGetValue(r, out var g)) { byRoot[r] = g = new List<int>(); groups.Add(g); }
                g.Add(items[i]);
            }
            return groups;
        }

        /// <summary>
        /// Manifold shares vertices between faces; Unity wants a vertex per face corner for flat normals and planar
        /// UVs. Triangles are split by the static flags of their brush (one mesh child each), grouped by material (one
        /// submesh each), vertices by (position, source brush, face). A face made by a cut takes the flags of the brush it
        /// was cut into, so a doorway's sides light like the wall around them.
        /// </summary>
        static void Convert(Island island, Material fallback, int defaultFlags, Matrix4x4 worldToModel)
        {
            var data = island.data;
            island.parts = new List<Part>();
            var used = new List<Material>(); var usedFlags = new List<int>();
            foreach (var b in island.sources.Values) { used.Add(b != null ? b.material : null); usedFlags.Add(b != null ? StaticFlags(b) : 0); }
            island.convertedWith = used.ToArray(); island.convertedFlags = usedFlags.ToArray(); island.convertedDefault = fallback; island.convertedDefaultFlags = defaultFlags;
            // the solids a cut can have carved: added brushes, by their boxes in the model's space
            var carved = new List<(Brush brush, Bounds box)>();
            foreach (var b in island.sources.Values)
            {
                if (b == null || b.operation != BrushOperation.Add) continue;
                var poly = BrushGeometry.Polyhedron(b);
                if (poly == null || poly.vertices == null || poly.vertices.Length == 0) continue;
                var m = worldToModel * b.transform.localToWorldMatrix;
                var box = new Bounds(m.MultiplyPoint3x4(poly.vertices[0]), Vector3.zero);
                foreach (var v in poly.vertices) box.Encapsulate(m.MultiplyPoint3x4(v));
                box.Expand(1e-3f);
                carved.Add((b, box));
            }
            var partOf = new Dictionary<int, Part>();
            int triCount = data.triangles != null ? data.triangles.Length / 3 : 0;
            for (int tri = 0; tri < triCount; tri++)
            {
                int source = data.triangleSource[tri], face = data.triangleFace[tri];
                island.sources.TryGetValue(source, out var brush);
                var a = data.vertices[data.triangles[tri * 3]]; var b = data.vertices[data.triangles[tri * 3 + 1]]; var c = data.vertices[data.triangles[tri * 3 + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-16f) continue;
                n.Normalize();
                int flags = brush != null ? StaticFlags(brush) : defaultFlags;
                if (brush != null && brush.operation == BrushOperation.Subtract)
                {
                    var inside = (a + b + c) / 3f - n * 2e-3f; // the solid a cut face bounds lies behind it
                    foreach (var (addBrush, box) in carved) if (box.Contains(inside)) { flags = StaticFlags(addBrush); break; }
                }
                if (!partOf.TryGetValue(flags, out var part)) { part = new Part { flags = flags }; partOf[flags] = part; island.parts.Add(part); }
                Material mat = brush != null && brush.material != null ? brush.material : fallback;
                if (!part.materialIndex.TryGetValue(mat, out int mi)) { mi = part.materials.Count; part.materials.Add(mat); part.materialIndex[mat] = mi; part.triangles.Add(new List<int>()); part.triangleBrush.Add(new List<Brush>()); }
                part.triangleBrush[mi].Add(brush);
                // planar UVs on the dominant axis plane, one texture per metre
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                Vector2 UV(Vector3 p) => ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                int fkey = (source << 12) ^ face;
                foreach (var p in new[] { a, b, c })
                {
                    var key = (p, fkey, mi);
                    if (!part.lookup.TryGetValue(key, out int vi))
                    {
                        vi = part.vertices.Count; part.lookup[key] = vi;
                        part.vertices.Add(p); part.normals.Add(n); part.uvs.Add(UV(p));
                    }
                    part.triangles[mi].Add(vi);
                }
            }
        }

        /// <summary>A brush's static flags: what its render triangles and its collider pieces get.</summary>
        static int StaticFlags(Brush brush) => (int)GameObjectUtility.GetStaticEditorFlags(brush.gameObject);

        static bool StillConverted(Island island, Material fallback, int defaultFlags)
        {
            if (island.parts == null || !ReferenceEquals(island.convertedDefault, fallback) || island.convertedDefaultFlags != defaultFlags) return false;
            int i = 0;
            foreach (var b in island.sources.Values)
            {
                if (!ReferenceEquals(island.convertedWith[i], b != null ? b.material : null) || island.convertedFlags[i] != (b != null ? StaticFlags(b) : 0)) return false;
                i++;
            }
            return true;
        }

        static readonly List<Vector3> s_Vertices = new List<Vector3>(), s_Normals = new List<Vector3>();
        static readonly List<Vector2> s_Uvs = new List<Vector2>();

        /// <summary>
        /// One layer's render meshes, one per set of static flags in use: the islands' Unity arrays, converted again only
        /// where an island changed, put together. Returns the mesh children written.
        /// </summary>
        static List<Transform> ApplyMesh(BrushGroup model, int layer, List<Island> islands)
        {
            var fallback = DefaultMaterial();
            int defaultFlags = (int)BrushSettings.instance.defaultModelStaticFlags;
            var worldToModel = model.transform.worldToLocalMatrix;
            var byFlags = new SortedDictionary<int, List<Part>>();
            foreach (var island in islands)
            {
                if (!StillConverted(island, fallback, defaultFlags)) Convert(island, fallback, defaultFlags, worldToModel);
                foreach (var part in island.parts) { if (!byFlags.TryGetValue(part.flags, out var list)) byFlags[part.flags] = list = new List<Part>(); list.Add(part); }
            }
            var written = new List<Transform>();
            if (byFlags.Count == 0) byFlags[defaultFlags] = new List<Part>(); // an empty layer keeps an empty mesh until it is dropped
            foreach (var (flags, parts) in byFlags) written.Add(WriteMesh(model, layer, flags, parts));
            return written;
        }

        static Transform WriteMesh(BrushGroup model, int layer, int flags, List<Part> parts)
        {
            var t = MeshObject(model, layer, (StaticEditorFlags)flags, true);
            var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
            ApplyRendererSettings(model, mr);
            var mesh = mf.sharedMesh;
            // a mesh saved in an asset (a built prefab's) is never rewritten from here: the scene gets its own
            if (mesh == null || EditorUtility.IsPersistent(mesh)) { mesh = new Mesh { name = "brush mesh" }; mf.sharedMesh = mesh; }
            mesh.Clear();
            s_Vertices.Clear(); s_Normals.Clear(); s_Uvs.Clear();
            var materials = new List<Material>(); var materialIndex = new Dictionary<Material, int>();
            var submeshTris = new List<List<int>>(); var submeshBrush = new List<List<Brush>>();
            foreach (var part in parts)
            {
                int offset = s_Vertices.Count;
                s_Vertices.AddRange(part.vertices); s_Normals.AddRange(part.normals); s_Uvs.AddRange(part.uvs);
                for (int m = 0; m < part.materials.Count; m++)
                {
                    var mat = part.materials[m];
                    if (!materialIndex.TryGetValue(mat, out int mi)) { mi = materials.Count; materials.Add(mat); materialIndex[mat] = mi; submeshTris.Add(new List<int>()); submeshBrush.Add(new List<Brush>()); }
                    var dst = submeshTris[mi];
                    foreach (int v in part.triangles[m]) dst.Add(v + offset);
                    submeshBrush[mi].AddRange(part.triangleBrush[m]);
                }
            }
            if (s_Vertices.Count == 0) { mr.sharedMaterials = new Material[0]; s_TriangleBrush[t] = new Brush[0]; return t; }
            mesh.indexFormat = s_Vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(s_Vertices); mesh.SetNormals(s_Normals); mesh.SetUVs(0, s_Uvs);
            mesh.subMeshCount = submeshTris.Count;
            for (int i = 0; i < submeshTris.Count; i++) mesh.SetTriangles(submeshTris[i], i);
            mesh.RecalculateBounds();
            mr.sharedMaterials = materials.ToArray();
            var triangleBrush = new List<Brush>(); foreach (var list in submeshBrush) triangleBrush.AddRange(list);
            s_TriangleBrush[t] = triangleBrush.ToArray();
            return t;
        }
    }
}

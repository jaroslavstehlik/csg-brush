using System.Collections.Generic;
using System.Diagnostics;
using CsgBrush.Colliders;
using CsgBrush.Colliders.Editor;
using CsgBrush.Manifold;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Builds the render mesh of every <see cref="BrushModel"/> with the Manifold library, then hands the brushes'
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
        static readonly HashSet<BrushModel> s_Dirty = new HashSet<BrushModel>();
        static bool s_AllDirty;
        static readonly Dictionary<BrushModel, int> s_LastMeshKey = new Dictionary<BrushModel, int>();
        static readonly Dictionary<Transform, Brush[]> s_TriangleBrush = new Dictionary<Transform, Brush[]>();

        /// <summary>For every triangle of the model's mesh (in submesh order), the brush whose face it lies on.</summary>
        public static Brush[] TriangleBrushes(BrushModel model) { var t = MeshObject(model, false); return t != null ? TriangleBrushes(t) : null; }
        /// <summary>Per triangle of one mesh child (in submesh order): the brush whose face it belongs to.</summary>
        public static Brush[] TriangleBrushes(Transform meshObject) => s_TriangleBrush.TryGetValue(meshObject, out var a) ? a : null;

        /// <summary>Counters and timings of the last model build, for tests and the performance probe.</summary>
        public static int LastCachedSolids, LastBuiltSolids, LastCachedPieces, LastBuiltPieces, LastBrushes, LastTriangles;
        public static double LastSolidsMs, LastPiecesMs, LastUnionMs, LastMeshMs, LastCollidersMs;

        public static void MarkDirty(BrushModel model) { if (model != null) s_Dirty.Add(model); }
        public static void MarkDirty(Brush brush) { if (brush != null) MarkDirty(ModelOf(brush)); }
        public static void MarkAllDirty() { s_AllDirty = true; }
        public static bool HasDirty => s_AllDirty || s_Dirty.Count > 0;

        /// <summary>Forget every cached solid and piece; the next build computes everything again.</summary>
        public static void ClearCaches()
        {
            WaitForJob();
            foreach (var e in s_Pieces.Values) if (e.ownsPiece) e.piece.Dispose();
            s_Pieces.Clear();
            foreach (var e in s_Solids.Values) e.solid?.Dispose();
            s_Solids.Clear();
            s_LastMeshKey.Clear();
        }

        // ------------------------------------------------------------------ models

        /// <summary>The model a brush belongs to: the nearest BrushModel above it, else the default model.</summary>
        public static BrushModel ModelOf(Brush brush)
        {
            var model = brush.GetComponentInParent<BrushModel>(true);
            return model != null ? model : DefaultModel(true);
        }

        /// <summary>The hidden default model, created when needed. Never registered with Undo: it is derived state.</summary>
        public static BrushModel DefaultModel(bool create)
        {
            foreach (var m in Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (m.isDefault) return m;
            if (!create) return null;
            var go = new GameObject(BrushModel.DefaultName);
            var model = go.AddComponent<BrushModel>();
            model.isDefault = true;
            return model;
        }

        /// <summary>Brushes of every model in CSG order (hierarchy order, depth first). Inactive brushes are left out.</summary>
        public static Dictionary<BrushModel, List<Brush>> BrushesByModel()
        {
            var result = new Dictionary<BrushModel, List<Brush>>();
            foreach (var m in Object.FindObjectsByType<BrushModel>(FindObjectsInactive.Include, FindObjectsSortMode.None)) result[m] = new List<Brush>();
            BrushModel defaultModel = null;
            void Walk(Transform t, BrushModel current)
            {
                if (t.TryGetComponent<BrushModel>(out var m)) current = m;
                if (t.TryGetComponent<Brush>(out var b) && b.enabled && t.gameObject.activeInHierarchy)
                {
                    if (current == null) { defaultModel ??= DefaultModel(true); if (!result.ContainsKey(defaultModel)) result[defaultModel] = new List<Brush>(); current = defaultModel; }
                    result[current].Add(b);
                }
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), current);
            }
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) Walk(root.transform, null);
            }
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
            if (!byModel.TryGetValue(ModelOf(cutter), out var list)) return result;
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
            public BrushModel model;
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
            public List<(int layer, ManifoldSolid.MeshData mesh)> meshes = new List<(int, ManifoldSolid.MeshData)>();
            public Dictionary<int, Record> bySource = new Dictionary<int, Record>();
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
            var targets = s_AllDirty ? new List<BrushModel>(byModel.Keys) : new List<BrushModel>(s_Dirty);
            s_Dirty.Clear(); s_AllDirty = false;
            foreach (var model in targets)
            {
                if (model == null) continue;
                Rebuild(model, byModel.TryGetValue(model, out var list) ? list : new List<Brush>());
            }
            PruneCaches(byModel);
        }

        public static void RebuildAllNow() { MarkAllDirty(); RebuildDirtyNow(); }

        /// <summary>Build one model now (all three steps on the calling thread).</summary>
        public static void Rebuild(BrushModel model, List<Brush> brushes)
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
            var targets = s_AllDirty ? new List<BrushModel>(byModel.Keys) : new List<BrushModel>(s_Dirty);
            s_Dirty.Clear(); s_AllDirty = false;
            // one model per pump; the others stay dirty for the next frames
            BrushModel first = null; foreach (var m in targets) { if (m == null) continue; if (first == null) first = m; else s_Dirty.Add(m); }
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
        static Job Prepare(BrushModel model, List<Brush> brushes)
        {
            var job = new Job { model = model, brushes = brushes };
            foreach (var b in brushes)
            {
                var toModel = BrushGeometry.ToModel(b, model);
                int key = BrushGeometry.SolidKey(b, toModel);
                s_Solids.TryGetValue(b, out var entry);
                var r = new Record { brush = b, toModel = toModel, key = key, subtract = b.operation == BrushOperation.Subtract, layer = b.gameObject.layer, solid = entry != null && entry.key == key ? entry : null };
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
            // pieces: which cutters apply, and which pieces are already cached
            var keyBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < job.records.Count; i++)
            {
                var r = job.records[i];
                if (r.subtract) continue;
                keyBuilder.Clear(); keyBuilder.Append(r.key);
                var cutters = new List<int>();
                for (int j = i + 1; j < job.records.Count; j++)
                {
                    var c = job.records[j];
                    if (!c.subtract || c.layer != r.layer || !bounds[j].Intersects(bounds[i])) continue; // each layer is its own CSG group
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
                for (int i = 0; i < job.pieceKeys.Count; i++) { var pr = job.records[job.pieceRecord[i]]; meshKey = meshKey * 31 + job.pieceKeys[i].GetHashCode(); var mat = pr.brush.material; if (mat == null) mat = DefaultMaterial(); meshKey = meshKey * 31 + (mat != null ? mat.GetHashCode() : 0); meshKey = meshKey * 31 + pr.layer; }
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

            var piecesByLayer = new Dictionary<int, List<ManifoldSolid>>(); var layerOrder = new List<int>();
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
                    if (!piecesByLayer.TryGetValue(layer, out var list)) { piecesByLayer[layer] = list = new List<ManifoldSolid>(); layerOrder.Add(layer); }
                    list.Add(pe.piece);
                }
            }
            job.piecesMs = sw.Elapsed.TotalMilliseconds; sw.Restart();

            if (job.needMesh)
            {
                for (int i = 0; i < job.records.Count; i++) if (solids[i] != null) job.bySource[solids[i].OriginalId] = job.records[i];
                // one union per layer: layers never interact, so each is a smaller union and the others' meshes stay
                foreach (var layer in layerOrder)
                {
                    var pieces = piecesByLayer[layer];
                    using var union = pieces.Count == 1 ? null : ManifoldSolid.Batch(pieces, ManifoldNative.OpType.Add);
                    job.meshes.Add((layer, (union ?? pieces[0]).ToMesh()));
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
                foreach (var (layer, data) in job.meshes) { LastTriangles += data.triangles != null ? data.triangles.Length / 3 : 0; kept.Add(ApplyMesh(model, layer, data, job.bySource)); }
                foreach (var t in MeshObjects(model)) if (!kept.Contains(t)) { s_TriangleBrush.Remove(t); Object.DestroyImmediate(t.gameObject); } // a layer nothing is on any more
                s_LastMeshKey[model] = job.meshKey;
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
                inputs.Add(new ConvexColliderBuilder.Input { name = r.brush.name, subtract = r.subtract, kind = r.brush.surface, noFallDamage = r.brush.noFallDamage, layer = r.layer, add = r.solid.partsAdd, remove = r.solid.partsRemove });
            }
            var settings = model.GetComponent<ConvexColliderSettings>();
            if (settings == null) settings = model.gameObject.AddComponent<ConvexColliderSettings>();
            ConvexColliderBuilder.Rebuild(model.transform, settings, inputs);
            LastCollidersMs = sw.Elapsed.TotalMilliseconds;
        }

        static void PruneCaches(Dictionary<BrushModel, List<Brush>> byModel)
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
        }

        // ------------------------------------------------------------------ mesh output

        static Material s_DefaultMaterial;

        /// <summary>The material for brushes without one: the active render pipeline's default (URP or HDRP lit), else Unity's built-in default.</summary>
        public static Material DefaultMaterial()
        {
            if (s_DefaultMaterial != null) return s_DefaultMaterial;
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null) pipeline = QualitySettings.renderPipeline;
            if (pipeline != null) s_DefaultMaterial = pipeline.defaultMaterial;
            if (s_DefaultMaterial == null) s_DefaultMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (s_DefaultMaterial == null) s_DefaultMaterial = Resources.GetBuiltinResource<Material>("Default-Diffuse.mat");
            return s_DefaultMaterial;
        }

        /// <summary>The generated mesh object of a model (created when missing).</summary>
        /// <summary>The render mesh of the brushes on the Default layer (see <see cref="MeshObject(BrushModel, int, bool)"/>).</summary>
        public static Transform MeshObject(BrushModel model, bool create) => MeshObject(model, 0, create);

        /// <summary>
        /// The render mesh child for one layer. Brushes are combined per layer (each layer is its own CSG group), and
        /// each layer renders as its own child on that layer, so camera culling masks apply to it.
        /// </summary>
        public static Transform MeshObject(BrushModel model, int layer, bool create)
        {
            string name = BrushModel.MeshChildNameFor(layer);
            var t = model.transform.Find(name);
            if (t == null && create)
            {
                var go = new GameObject(name) { layer = layer };
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                t = go.transform;
            }
            if (t != null && t.gameObject.layer != layer) t.gameObject.layer = layer;
            return t;
        }

        /// <summary>Every render mesh child of a model, one per layer in use.</summary>
        public static List<Transform> MeshObjects(BrushModel model)
        {
            var list = new List<Transform>();
            foreach (Transform child in model.transform) if (BrushModel.IsMeshChildName(child.name)) list.Add(child);
            return list;
        }

        struct Corner { public Vector3 p; public int face; public int source; }

        /// <summary>
        /// Manifold shares vertices between faces; Unity wants a vertex per face corner for flat normals and planar
        /// UVs. Triangles are grouped by material (one submesh each), vertices by (position, source brush, face).
        /// </summary>
        static Transform ApplyMesh(BrushModel model, int layer, ManifoldSolid.MeshData data, Dictionary<int, Record> bySource)
        {
            var t = MeshObject(model, layer, true);
            var mf = t.GetComponent<MeshFilter>(); var mr = t.GetComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;
            if (mesh == null) { mesh = new Mesh { name = "brush mesh" }; mf.sharedMesh = mesh; }
            mesh.Clear();
            int triCount = data.triangles != null ? data.triangles.Length / 3 : 0;
            if (triCount == 0) { mr.sharedMaterials = new Material[0]; s_TriangleBrush[t] = new Brush[0]; return t; }

            var materials = new List<Material>();
            var materialIndex = new Dictionary<Material, int>();
            var submeshTris = new List<List<int>>();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uvs = new List<Vector2>();
            var lookup = new Dictionary<(Vector3, int, int), int>();
            var submeshBrush = new List<List<Brush>>();
            for (int tri = 0; tri < triCount; tri++)
            {
                int source = data.triangleSource[tri], face = data.triangleFace[tri];
                Material mat = bySource.TryGetValue(source, out var rec) && rec.brush.material != null ? rec.brush.material : DefaultMaterial();
                if (!materialIndex.TryGetValue(mat, out int mi)) { mi = materials.Count; materials.Add(mat); materialIndex[mat] = mi; submeshTris.Add(new List<int>()); submeshBrush.Add(new List<Brush>()); }
                var a = data.vertices[data.triangles[tri * 3]]; var b = data.vertices[data.triangles[tri * 3 + 1]]; var c = data.vertices[data.triangles[tri * 3 + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-16f) continue;
                submeshBrush[mi].Add(rec.brush);
                n.Normalize();
                // planar UVs on the dominant axis plane, one texture per metre
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                Vector2 UV(Vector3 p) => ax >= ay && ax >= az ? new Vector2(p.z, p.y) : ay >= az ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                int fkey = (source << 12) ^ face;
                foreach (var p in new[] { a, b, c })
                {
                    var key = (p, fkey, mi);
                    if (!lookup.TryGetValue(key, out int vi))
                    {
                        vi = vertices.Count; lookup[key] = vi;
                        vertices.Add(p); normals.Add(n); uvs.Add(UV(p));
                    }
                    submeshTris[mi].Add(vi);
                }
            }
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs);
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

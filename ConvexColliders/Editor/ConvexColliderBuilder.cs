using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Colliders.Editor
{
    /// <summary>
    /// Builds convex colliders for a model from its brushes' convex parts, evaluating the ordered CSG at the brush
    /// level: additive parts are convex solids, a subtractive brush splits every solid it touches into convex
    /// pieces along its planes. The result is a list of convex polytopes, one BoxCollider or convex MeshCollider
    /// each. Water and trigger brushes become convex trigger volumes instead and never carve.
    ///
    /// Incremental per brush: a brush's pieces depend only on its own parts and the subtract brushes that reach it, so
    /// every build goes through the whole brush list (nothing stale survives an undo, a delete or a reorder) but only
    /// brushes whose key changed are built again. Each generated piece carries the planes it was built from
    /// (<see cref="ConvexPiece"/>), so the first build of a session reuses the pieces a scene or prefab saved.
    /// </summary>
    public static class ConvexColliderBuilder
    {
        /// <summary>Diagnostic log of the last build; filled only while <see cref="Verbose"/> is set.</summary>
        public static readonly System.Text.StringBuilder Log = new System.Text.StringBuilder();
        public static bool Verbose;

        /// <summary>One brush, in CSG order: its convex parts in model space and what its volume means.</summary>
        public sealed class Input
        {
            public string name;
            public bool subtract;
            /// <summary>Solid geometry, a trigger volume, or nothing.</summary>
            public ColliderKind kind;
            /// <summary>Hash of whatever <see cref="onPiece"/> puts on the pieces; a change rebuilds them.</summary>
            public int fingerprint;
            /// <summary>Layer of the generated colliders; a subtract input only cuts inputs on its own layer.</summary>
            public int layer;
            /// <summary>Called for every piece of this input on every build, new or reused (so it must be idempotent): game data goes on here.</summary>
            public System.Action<GameObject, bool> onPiece;
            /// <summary>Convex solids that make up the brush (several for a concave shape).</summary>
            public List<ConvexPolytope> add;
            /// <summary>Convex solids removed from the brush's own parts first (the inside of a hollow shape).</summary>
            public List<ConvexPolytope> remove;
            /// <summary>What the pieces belong to (the brush): its pieces are kept between builds while its key holds.</summary>
            public object owner;
            /// <summary>Identity of <see cref="add"/> and <see cref="remove"/> (shape and pose): equal keys mean equal parts.</summary>
            public int key;
        }

        sealed class BrushTag
        {
            public int fingerprint;
            public int layer;
            public string name;
            public System.Action<GameObject, bool> onPiece;
        }

        /// <summary>Set by the editing layer while a handle is dragged: colliders are not needed until release.</summary>
        public static System.Func<bool> DeferWhile;
        sealed class Deferred { public Transform model; public ConvexColliderSettings settings; public List<Input> inputs; }
        static readonly Dictionary<Transform, Deferred> s_Deferred = new Dictionary<Transform, Deferred>();

        /// <summary>Rebuild the models whose update was skipped during a drag (called on release).</summary>
        public static void FlushDeferred()
        {
            if (s_Deferred.Count == 0) return;
            var list = new List<Deferred>(s_Deferred.Values); s_Deferred.Clear();
            foreach (var d in list)
                if (d.model != null && d.settings != null) Rebuild(d.model, d.settings, d.inputs);
        }

        /// <summary>Counters of the last build, for tests and the performance probe.</summary>
        public static int LastReusedPieces, LastCreatedPieces, LastDestroyedPieces;

        /// <summary>The pieces one input made last time, kept while its key holds.</summary>
        sealed class Built
        {
            public long key; public readonly List<GameObject> pieces = new List<GameObject>(); public int boxes, meshes, solids, triggers;
            /// <summary>The pieces of each of the input's convex parts, by the part's key: its planes and the cutter parts that reach it.</summary>
            public Dictionary<long, PartPieces> parts = new Dictionary<long, PartPieces>();
        }

        sealed class PartPieces { public readonly List<GameObject> pieces = new List<GameObject>(); public int boxes, meshes; }

        sealed class ByReference : IEqualityComparer<object>
        {
            public static readonly ByReference Instance = new ByReference();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        /// <summary>Per collider container, what each input built. In memory only: a new session (domain reload, scene open, build) starts from the pieces' planes.</summary>
        static readonly Dictionary<Transform, Dictionary<object, Built>> s_Built = new Dictionary<Transform, Dictionary<object, Built>>();

        /// <summary>Forget what a group's inputs built: the next build matches every piece again by its planes (Rebuild).</summary>
        public static void Forget(Transform model)
        {
            foreach (var container in FindContainers(model)) s_Built.Remove(container);
            var dead = new List<Transform>();
            foreach (var c in s_Built.Keys) if (c == null) dead.Add(c);
            foreach (var c in dead) s_Built.Remove(c);
        }

        /// <summary>
        /// Build the colliders of a model from its inputs, per input: a brush's pieces are its own convex parts minus those of
        /// the later subtract inputs on its layer that overlap it, so they depend on nothing else. An input whose key (its
        /// parts, what goes on its pieces, and its cutters' keys) is unchanged keeps its pieces untouched; others are built
        /// again; pieces of inputs that are gone are destroyed.
        /// </summary>
        public static void Rebuild(Transform model, ConvexColliderSettings settings, List<Input> inputs)
        {
            if (DeferWhile != null && DeferWhile()) { s_Deferred[model] = new Deferred { model = model, settings = settings, inputs = inputs }; return; }
            var sw = Stopwatch.StartNew();
            Log.Clear();
            LastReusedPieces = LastCreatedPieces = LastDestroyedPieces = 0;

            // The pieces are derived state and are never registered with Undo: after an undo or redo they are
            // simply rebuilt from the brushes. Any duplicate containers (from earlier versions) are removed.
            var existing = FindContainers(model);
            for (int i = 1; i < existing.Count; i++) { s_Built.Remove(existing[i]); Object.DestroyImmediate(existing[i].gameObject); }
            Transform container;
            if (existing.Count > 0) container = existing[0];
            else
            {
                var containerGo = new GameObject(ConvexColliderSettings.ContainerName);
                containerGo.transform.SetParent(model, false);
                container = containerGo.transform;
            }
            var hidden = settings.showInHierarchy ? HideFlags.NotEditable : HideFlags.HideInHierarchy | HideFlags.NotEditable;
            if (container.gameObject.hideFlags != hidden) container.gameObject.hideFlags = hidden;

            // a first build in this session (or after Rebuild) starts from the pieces there are, matched by their planes
            if (settings.lastGeometryHash == 0) s_Built.Remove(container);
            bool first = !s_Built.TryGetValue(container, out var built);
            if (first) s_Built[container] = built = new Dictionary<object, Built>(ByReference.Instance);
            Dictionary<int, List<ConvexPiece>> pool = null; List<GameObject> unknown = null;
            if (first)
            {
                pool = new Dictionary<int, List<ConvexPiece>>(); unknown = new List<GameObject>();
                foreach (Transform child in container)
                {
                    if (!child.TryGetComponent<ConvexPiece>(out var id) || id.planes == null) { unknown.Add(child.gameObject); continue; }
                    if (!pool.TryGetValue(id.hash, out var list)) pool[id.hash] = list = new List<ConvexPiece>();
                    list.Add(id);
                }
            }

            // each input's own parts (hollow inside removed) and their bounds; computed only when needed
            var own = new List<ConvexPolytope>[inputs.Count];
            var bounds = new Bounds[inputs.Count];
            var hasBounds = new bool[inputs.Count];
            List<ConvexPolytope> Own(int i)
            {
                if (own[i] != null) return own[i];
                var input = inputs[i];
                var tag = new BrushTag { fingerprint = input.fingerprint, layer = input.layer, name = input.name, onPiece = input.onPiece };
                var parts = new List<ConvexPolytope>();
                foreach (var part in input.add) { part.tag = tag; parts.Add(part); }
                if (input.remove != null)
                    foreach (var inner in input.remove)
                    {
                        var next = new List<ConvexPolytope>();
                        foreach (var p in parts) ConvexPolytope.Subtract(p, inner, next);
                        parts = next;
                        foreach (var p in parts) p.tag = tag;
                    }
                return own[i] = parts;
            }
            Bounds BoundsOf(int i)
            {
                if (hasBounds[i]) return bounds[i];
                var b = inputs[i].add[0].GetBounds();
                for (int k = 1; k < inputs[i].add.Count; k++) b.Encapsulate(inputs[i].add[k].GetBounds());
                hasBounds[i] = true;
                return bounds[i] = b;
            }

            // the subtract inputs that carve colliders, in order
            var cutters = new List<int>();
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (input.subtract && input.kind == ColliderKind.Solid && input.add != null && input.add.Count > 0) cutters.Add(i);
            }

            var seen = new HashSet<object>(ByReference.Instance);
            int brushCount = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                if (input.kind == ColliderKind.None || input.add == null || input.add.Count == 0) continue;
                brushCount++;
                if (input.subtract) continue; // a subtract input has no pieces of its own; it carves those before it
                bool trigger = input.kind == ColliderKind.Trigger;
                // key: the parts, what goes on the pieces, and (for solids) the cutters that reach them
                var mine = new List<int>();
                long key;
                unchecked
                {
                    key = input.key;
                    key = key * 31 + input.fingerprint; key = key * 31 + (int)input.kind; key = key * 31 + input.layer;
                    key = key * 31 + (input.name != null ? input.name.GetHashCode() : 0);
                    key = key * 31 + settings.minPieceVolume.GetHashCode();
                    if (!trigger)
                        foreach (int j in cutters)
                        {
                            if (j <= i || inputs[j].layer != input.layer || !BoundsOf(j).Intersects(BoundsOf(i))) continue; // a subtract carves only what is above it
                            mine.Add(j); key = key * 1000003 + inputs[j].key;
                        }
                }
                var owner = input.owner ?? input;
                seen.Add(owner);
                if (built.TryGetValue(owner, out var was) && was.key == key && Alive(was))
                {
                    LastReusedPieces += was.pieces.Count;
                    continue;
                }
                // this input's pieces, part by part: each of its own parts carved by the cutters' parts that reach it, in
                // order. A part whose planes and reaching cutter parts are as they were keeps its pieces: a long brush
                // changed at one end, or cut anew near one end, builds again only there
                var entry = new Built { key = key };
                bool reusable = was != null && Alive(was);
                long partKeyBase;
                unchecked { partKeyBase = (((long)input.fingerprint * 31 + (int)input.kind) * 31 + input.layer) * 31 + (input.name != null ? input.name.GetHashCode() : 0); partKeyBase = partKeyBase * 31 + settings.minPieceVolume.GetHashCode(); }
                foreach (var part in Own(i))
                {
                    var partBounds = PartBounds(part);
                    var reaching = new List<ConvexPolytope>();
                    long partKey = partKeyBase * 1000003 + PlanesHash(part);
                    if (!trigger)
                        foreach (int j in mine)
                            foreach (var cutter in Own(j))
                                if (PartBounds(cutter).Intersects(partBounds)) { reaching.Add(cutter); unchecked { partKey = partKey * 1000003 + PlanesHash(cutter); } }
                    if (entry.parts.ContainsKey(partKey)) unchecked { partKey = partKey * 31 + entry.parts.Count; } // the same part twice: built on its own
                    if (reusable && was.parts.TryGetValue(partKey, out var kept))
                    {
                        was.parts.Remove(partKey);
                        entry.parts[partKey] = kept;
                        foreach (var go in kept.pieces) { entry.pieces.Add(go); var tag = part.tag as BrushTag; tag?.onPiece?.Invoke(go, trigger); }
                        entry.boxes += kept.boxes; entry.meshes += kept.meshes;
                        if (trigger) entry.triggers += kept.pieces.Count; else entry.solids += kept.pieces.Count;
                        LastReusedPieces += kept.pieces.Count;
                        continue;
                    }
                    var pieces = new List<ConvexPolytope> { part };
                    foreach (var cutter in reaching)
                    {
                        var next = new List<ConvexPolytope>();
                        foreach (var p in pieces) ConvexPolytope.Subtract(p, cutter, next);
                        pieces = next;
                    }
                    var made = new PartPieces();
                    foreach (var piece in pieces)
                    {
                        if (piece.IsEmpty || (!trigger && piece.Volume() < settings.minPieceVolume)) continue;
                        var tag = piece.tag as BrushTag;
                        int boxes = 0, meshes = 0;
                        var go = ClaimOrMake(pool, container, piece, input.layer, (trigger ? "trigger " : "solid ") + (tag != null ? tag.name : "?"), trigger, tag, ref boxes, ref meshes);
                        made.pieces.Add(go); made.boxes += boxes; made.meshes += meshes;
                    }
                    entry.parts[partKey] = made;
                    entry.pieces.AddRange(made.pieces); entry.boxes += made.boxes; entry.meshes += made.meshes;
                    if (trigger) entry.triggers += made.pieces.Count; else entry.solids += made.pieces.Count;
                }
                // the parts that are no more: their pieces go (a dead input's all go)
                if (was != null)
                {
                    var keptPieces = new HashSet<GameObject>(entry.pieces);
                    foreach (var go in was.pieces) if (go != null && !keptPieces.Contains(go)) { Object.DestroyImmediate(go); LastDestroyedPieces++; }
                }
                built[owner] = entry;
            }

            // inputs that are gone (deleted, disabled, no collision) take their pieces with them
            var gone = new List<object>();
            foreach (var kv in built) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var owner in gone)
            {
                foreach (var go in built[owner].pieces) if (go != null) { Object.DestroyImmediate(go); LastDestroyedPieces++; }
                built.Remove(owner);
            }
            if (first)
            {
                foreach (var go in unknown) { Object.DestroyImmediate(go); LastDestroyedPieces++; }
                foreach (var list in pool.Values)
                    foreach (var stale in list) { Object.DestroyImmediate(stale.gameObject); LastDestroyedPieces++; }
            }

            int solidCount = 0, triggerCount = 0, boxCount = 0, meshCount = 0;
            foreach (var e in built.Values) { solidCount += e.solids; triggerCount += e.triggers; boxCount += e.boxes; meshCount += e.meshes; }
            sw.Stop();
            bool changed = settings.lastGeometryHash != 1 || settings.brushCount != brushCount || settings.pieceCount != solidCount || settings.triggerVolumes != triggerCount || settings.boxColliders != boxCount || settings.meshColliders != meshCount;
            settings.lastGeometryHash = 1; // non-zero: built this session (Rebuild sets 0)
            settings.brushCount = brushCount;
            settings.pieceCount = solidCount;
            settings.boxColliders = boxCount;
            settings.meshColliders = meshCount;
            settings.triggerVolumes = triggerCount;
            settings.buildMilliseconds = (float)sw.Elapsed.TotalMilliseconds;
            if (changed) EditorUtility.SetDirty(settings);
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ConvexPolytope, object> s_PartBounds = new System.Runtime.CompilerServices.ConditionalWeakTable<ConvexPolytope, object>();

        /// <summary>A part's box, measured once (parts are kept with their brush's solid between builds).</summary>
        static Bounds PartBounds(ConvexPolytope part)
        {
            if (s_PartBounds.TryGetValue(part, out var b)) return (Bounds)b;
            var bounds = part.GetBounds(); bounds.Expand(1e-3f);
            s_PartBounds.Add(part, bounds);
            return bounds;
        }

        /// <summary>A hash of a part's planes (to a tenth of a millimetre): equal parts, equal hashes.</summary>
        static long PlanesHash(ConvexPolytope part)
        {
            unchecked
            {
                long h = 17;
                foreach (var pl in part.planes) { h = h * 31 + Mathf.RoundToInt(pl.x * 1e4f); h = h * 31 + Mathf.RoundToInt(pl.y * 1e4f); h = h * 31 + Mathf.RoundToInt(pl.z * 1e4f); h = h * 31 + Mathf.RoundToInt(pl.w * 1e4f); }
                return h;
            }
        }

        static bool Alive(Built b)
        {
            foreach (var go in b.pieces) if (go == null) return false;
            return true;
        }

        /// <summary>Reuse an existing piece with exactly these planes and module data, or create one; either way the input's hook runs on it.</summary>
        static GameObject ClaimOrMake(Dictionary<int, List<ConvexPiece>> pool, Transform container, ConvexPolytope piece, int layer, string name, bool trigger, BrushTag tag, ref int boxes, ref int meshes)
        {
            int fingerprint = tag != null ? tag.fingerprint : 0;
            string brushName = tag != null ? tag.name : "";
            int hash = ConvexPiece.HashOf(piece.planes, fingerprint, trigger, layer, brushName);
            if (pool != null && pool.TryGetValue(hash, out var candidates))
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (!c.Matches(piece.planes, fingerprint, trigger, layer, brushName)) continue;
                    if (c.TryGetComponent<MeshCollider>(out var existing) && existing.sharedMesh == null) continue; // its mesh was lost (a prefab saved from a scene): make it again
                    candidates.RemoveAt(i);
                    LastReusedPieces++;
                    if (c.TryGetComponent<BoxCollider>(out _)) boxes++; else meshes++;
                    tag?.onPiece?.Invoke(c.gameObject, trigger);
                    return c.gameObject;
                }
            }

            var go = MakePiece(container, piece, layer, name, trigger, ref boxes, ref meshes);
            var id = go.AddComponent<ConvexPiece>();
            id.planes = piece.planes.ToArray();
            id.hash = hash;
            id.fingerprint = fingerprint; id.trigger = trigger; id.layer = layer; id.brushName = brushName;
            LastCreatedPieces++;
            tag?.onPiece?.Invoke(go, trigger);
            return go;
        }

        static List<Transform> FindContainers(Transform model)
        {
            var list = new List<Transform>();
            foreach (Transform child in model)
                if (child.name == ConvexColliderSettings.ContainerName) list.Add(child);
            return list;
        }

        static GameObject MakePiece(Transform container, ConvexPolytope piece, int layer, string name, bool trigger, ref int boxes, ref int meshes)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(container, false);
            if (piece.IsAxisAlignedBox(out Bounds b))
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.center;
                bc.size = b.size;
                bc.isTrigger = trigger;
                boxes++;
            }
            else
            {
                var mc = go.AddComponent<MeshCollider>();
                var mesh = piece.ToMesh(name);
                mesh.SetPreBakeCollisionMesh(true, true); // builds take the convex collision data pre-baked, as for an imported mesh
                mc.sharedMesh = mesh;
                mc.convex = true;
                mc.isTrigger = trigger;
                meshes++;
            }
            return go;
        }
    }
}

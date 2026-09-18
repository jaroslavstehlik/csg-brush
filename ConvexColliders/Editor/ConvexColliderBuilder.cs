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
    /// Incremental: every build evaluates the whole brush list and compares every piece, so nothing stale can
    /// survive an undo, a delete or a reorder; only the expensive steps are skipped. Each generated piece carries
    /// the planes it was built from (<see cref="ConvexPiece"/>); a new piece with identical planes and
    /// surface data keeps the existing object, everything unmatched is destroyed.
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
            public ControllerSurface.Kind kind;
            public bool noFallDamage;
            /// <summary>Layer of the generated colliders; a subtract input only cuts inputs on its own layer.</summary>
            public int layer;
            /// <summary>Convex solids that make up the brush (several for a concave shape).</summary>
            public List<ConvexPolytope> add;
            /// <summary>Convex solids removed from the brush's own parts first (the inside of a hollow shape).</summary>
            public List<ConvexPolytope> remove;
        }

        sealed class BrushTag
        {
            public ControllerSurface.Kind kind;
            public bool noFallDamage;
            public int layer;
            public string name;
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

        public static void Rebuild(Transform model, ConvexColliderSettings settings, List<Input> inputs)
        {
            if (DeferWhile != null && DeferWhile()) { s_Deferred[model] = new Deferred { model = model, settings = settings, inputs = inputs }; return; }
            var sw = Stopwatch.StartNew();
            Log.Clear();
            int prevReused = LastReusedPieces, prevCreated = LastCreatedPieces, prevDestroyed = LastDestroyedPieces;
            LastReusedPieces = LastCreatedPieces = LastDestroyedPieces = 0;

            var solids = new List<ConvexPolytope>();
            var volumes = new List<ConvexPolytope>();
            int brushCount = 0;
            Evaluate(inputs, solids, volumes, ref brushCount);

            // Skip when nothing changed: updates fire often.
            int geometryHash = GeometryHash(solids, volumes, settings);
            var existing = FindContainers(model);
            if (existing.Count == 1 && settings.lastGeometryHash == geometryHash && settings.pieceCount + settings.triggerVolumes == existing[0].childCount)
            {
                LastReusedPieces = prevReused; LastCreatedPieces = prevCreated; LastDestroyedPieces = prevDestroyed;
                return;
            }

            // The pieces are derived state and are never registered with Undo: after an undo or redo they are
            // simply rebuilt from the brushes. Any duplicate containers (from earlier versions) are removed.
            for (int i = 1; i < existing.Count; i++)
                Object.DestroyImmediate(existing[i].gameObject);
            Transform container;
            if (existing.Count > 0) container = existing[0];
            else
            {
                var containerGo = new GameObject(ConvexColliderSettings.ContainerName);
                containerGo.transform.SetParent(model, false);
                container = containerGo.transform;
            }
            container.gameObject.hideFlags = settings.showInHierarchy ? HideFlags.NotEditable : HideFlags.HideInHierarchy | HideFlags.NotEditable;

            // pool of existing pieces by identity hash; whatever is not claimed below is destroyed
            var pool = new Dictionary<int, List<ConvexPiece>>();
            var unknown = new List<GameObject>();
            foreach (Transform child in container)
            {
                if (!child.TryGetComponent<ConvexPiece>(out var id) || id.planes == null) { unknown.Add(child.gameObject); continue; }
                if (!pool.TryGetValue(id.hash, out var list)) pool[id.hash] = list = new List<ConvexPiece>();
                list.Add(id);
            }

            int boxes = 0, meshes = 0, triggers = 0, pieces = 0;
            for (int i = 0; i < solids.Count; i++)
            {
                var piece = solids[i];
                if (piece.IsEmpty || piece.Volume() < settings.minPieceVolume) continue;
                var tag = piece.tag as BrushTag;
                if (Verbose) Log.AppendLine("piece " + pieces + " from " + (tag != null ? tag.name : "?") + " bounds " + piece.GetBounds() + " verts " + piece.vertices.Count + " faces " + piece.faces.Count + " vol " + piece.Volume().ToString("0.000"));
                ClaimOrMake(pool, container, piece, tag != null ? tag.layer : 0, "solid " + (tag != null ? tag.name : "?"), false, tag, ref boxes, ref meshes);
                pieces++;
            }
            for (int i = 0; i < volumes.Count; i++)
            {
                var piece = volumes[i];
                if (piece.IsEmpty) continue;
                var tag = piece.tag as BrushTag;
                ClaimOrMake(pool, container, piece, tag != null ? tag.layer : 0, (tag != null ? tag.kind.ToString().ToLower() : "trigger") + " " + (tag != null ? tag.name : "?"), true, tag, ref boxes, ref meshes);
                triggers++;
            }

            foreach (var go in unknown) { Object.DestroyImmediate(go); LastDestroyedPieces++; }
            foreach (var list in pool.Values)
                foreach (var stale in list) { Object.DestroyImmediate(stale.gameObject); LastDestroyedPieces++; }

            sw.Stop();
            settings.lastGeometryHash = geometryHash;
            settings.brushCount = brushCount;
            settings.pieceCount = pieces;
            settings.boxColliders = boxes;
            settings.meshColliders = meshes;
            settings.triggerVolumes = triggers;
            settings.buildMilliseconds = (float)sw.Elapsed.TotalMilliseconds;
            EditorUtility.SetDirty(settings);
        }

        /// <summary>Reuse an existing piece with exactly these planes and surface data, or create one (and raise the hook for it).</summary>
        static GameObject ClaimOrMake(Dictionary<int, List<ConvexPiece>> pool, Transform container, ConvexPolytope piece, int layer, string name, bool trigger, BrushTag tag, ref int boxes, ref int meshes)
        {
            var kind = tag != null ? tag.kind : (trigger ? ControllerSurface.Kind.Trigger : ControllerSurface.Kind.Solid);
            bool noFallDamage = tag != null && tag.noFallDamage && !trigger;
            string brushName = tag != null ? tag.name : "";
            int hash = ConvexPiece.HashOf(piece.planes, kind, noFallDamage, trigger, layer, brushName);
            if (pool.TryGetValue(hash, out var candidates))
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if (!c.Matches(piece.planes, kind, noFallDamage, trigger, layer, brushName)) continue;
                    candidates.RemoveAt(i);
                    LastReusedPieces++;
                    if (c.TryGetComponent<BoxCollider>(out _)) boxes++; else meshes++;
                    return c.gameObject;
                }
            }

            var go = MakePiece(container, piece, layer, name, trigger, ref boxes, ref meshes);
            var id = go.AddComponent<ConvexPiece>();
            id.planes = piece.planes.ToArray();
            id.hash = hash;
            id.kind = kind; id.noFallDamage = noFallDamage; id.trigger = trigger; id.layer = layer; id.brushName = brushName;
            LastCreatedPieces++;
            ConvexColliderHooks.RaisePieceCreated(new ConvexColliderHooks.Piece
            {
                gameObject = go,
                kind = kind,
                noFallDamage = noFallDamage,
                isTrigger = trigger,
                brushName = brushName,
            });
            return go;
        }

        static List<Transform> FindContainers(Transform model)
        {
            var list = new List<Transform>();
            foreach (Transform child in model)
                if (child.name == ConvexColliderSettings.ContainerName) list.Add(child);
            return list;
        }

        static int GeometryHash(List<ConvexPolytope> solids, List<ConvexPolytope> volumes, ConvexColliderSettings settings)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + settings.minPieceVolume.GetHashCode();
                h = h * 31 + (settings.showInHierarchy ? 1 : 0);
                foreach (var list in new[] { solids, volumes })
                {
                    h = h * 31 + list.Count;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        for (int k = 0; k < p.planes.Count; k++) h = h * 31 + p.planes[k].GetHashCode();
                        var tag = p.tag as BrushTag;
                        if (tag != null) h = h * 31 + (int)tag.kind * 7 + (tag.noFallDamage ? 1 : 0) + tag.layer * 131;
                    }
                }
                return h;
            }
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
                mc.sharedMesh = piece.ToMesh(name);
                mc.convex = true;
                mc.isTrigger = trigger;
                meshes++;
            }
            return go;
        }

        // ------------------------------------------------------------------
        // ordered CSG on convex parts
        // ------------------------------------------------------------------

        static void Evaluate(List<Input> inputs, List<ConvexPolytope> solids, List<ConvexPolytope> volumes, ref int brushCount)
        {
            foreach (var input in inputs)
            {
                if (input.kind == ControllerSurface.Kind.NoCollision || input.add == null || input.add.Count == 0) continue;
                var tag = new BrushTag { kind = input.kind, noFallDamage = input.noFallDamage, layer = input.layer, name = input.name };
                // the brush's own parts, with the hollow inside removed
                var own = new List<ConvexPolytope>();
                foreach (var p in input.add) { p.tag = tag; own.Add(p); }
                if (input.remove != null)
                    foreach (var inner in input.remove)
                    {
                        var next = new List<ConvexPolytope>();
                        foreach (var s in own) ConvexPolytope.Subtract(s, inner, next);
                        own = next;
                        foreach (var s in own) s.tag = tag;
                    }
                brushCount++;
                if (input.kind == ControllerSurface.Kind.Water || input.kind == ControllerSurface.Kind.Trigger)
                {
                    if (!input.subtract) volumes.AddRange(own);
                    continue;
                }
                if (input.subtract)
                {
                    foreach (var cutter in own)
                    {
                        var next = new List<ConvexPolytope>();
                        for (int a = 0; a < solids.Count; a++)
                        {
                            if ((solids[a].tag as BrushTag)?.layer != input.layer) { next.Add(solids[a]); continue; } // another layer: another CSG group
                            ConvexPolytope.Subtract(solids[a], cutter, next);
                        }
                        solids.Clear(); solids.AddRange(next);
                    }
                }
                else solids.AddRange(own);
            }
        }
    }
}

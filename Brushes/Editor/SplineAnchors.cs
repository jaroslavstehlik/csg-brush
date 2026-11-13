using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Objects on splines (see <see cref="SplineAnchor"/>) and rows of copies along them (see <see cref="SplineRepeat"/>).
    /// The spline places them after every change of its path; a moved object takes the nearest place on the spline
    /// (undoable with the move); one whose link is gone, or pulled away from the spline, stays where it is.
    /// </summary>
    [InitializeOnLoad]
    public static class SplineAnchors
    {
        static SplineAnchors()
        {
            BrushGenerators.Updated += g => { if (g is Spline s) { Layout(s); BuildRepeats(s); } };
            SplineAnchor.Changed = a => { var s = a != null ? a.Spline : null; if (s != null) BrushGenerators.MarkDirty(s); };
            SplineRepeat.Changed = r => { if (r != null && r.TryGetComponent<Spline>(out var s)) BrushGenerators.MarkDirty(s); };
        }

        /// <summary>How far past its own offset an object may be pulled out of the surface and stay on the spline, metres.</summary>
        const float CatchMeters = 0.3f;

        static readonly HashSet<SplineAnchor> s_Moved = new HashSet<SplineAnchor>();

        /// <summary>An anchored object was moved by hand: it takes the nearest place at the spline's next update.</summary>
        public static void Moved(SplineAnchor a)
        {
            if (a == null) return;
            s_Moved.Add(a);
            var s = a.Spline;
            if (s != null) BrushGenerators.MarkDirty(s);
        }

        /// <summary>Put everything anchored to the spline in its place (the spline calls this after building its pieces).</summary>
        public static void Layout(Spline spline)
        {
            if (spline == null) return;
            foreach (Transform child in spline.transform)
            {
                if (!child.TryGetComponent<SplineAnchor>(out var a)) continue;
                if (a.startId == WallAnchor.Unplaced) { s_Moved.Remove(a); Reanchor(spline, a, true); }
                else if (s_Moved.Remove(a)) Reanchor(spline, a, false);
                // the saved values are only changed by the user (move, attach): an undo of a path edit finds the place again from them
                if (!Resolve(spline, a, out int link, out float along, out bool whole) || !spline.PoseAt(link, along, out var pose)) { a.onPath = false; continue; }
                a.onPath = true;
                if (whole && a.newPointsFrom != spline.nextPointId) a.newPointsFrom = spline.nextPointId; // derived from the spline, kept out of Undo
                Place(spline, pose, a.angle, a.offset, a.rotation, a.upright, out var position, out var rotation);
                if ((child.position - position).sqrMagnitude <= 1e-10f && Quaternion.Angle(child.rotation, rotation) <= 1e-3f) continue;
                child.SetPositionAndRotation(position, rotation);
                a.MarkBrushes(true);
                foreach (var carried in child.GetComponentsInChildren<Brush>()) BrushSync.NotifyTransformChanged(carried);
            }
        }

        /// <summary>
        /// The link an anchor is on and how far along: the link between its two ends (either way round); a link split by
        /// new points, straight or bent (the piece it is on); or, when one end is gone, a link that kept the other.
        /// </summary>
        public static bool Resolve(Spline spline, SplineAnchor a, out int link, out float along, out bool whole)
        {
            link = -1; along = a.distance; whole = false;
            if (a.startId < 0) return false;
            for (int i = 0; i < spline.links.Count; i++)
            {
                var l = spline.links[i];
                if (l.start == a.startId && l.end == a.endId) { link = i; whole = true; return true; }
                if (l.start == a.endId && l.end == a.startId) { link = i; along = spline.CurveLength(i) - a.distance; whole = true; return true; }
            }
            bool hasStart = spline.PointIndex(a.startId) >= 0, hasEnd = spline.PointIndex(a.endId) >= 0;
            if (hasStart && hasEnd) return spline.FollowSplit(a.startId, a.endId, a.distance, a.newPointsFrom, out link, out along);
            for (int i = 0; i < spline.links.Count; i++)
            {
                var l = spline.links[i]; float len = spline.CurveLength(i);
                if (hasStart && l.start == a.startId) { link = i; return true; }
                if (hasStart && l.end == a.startId) { link = i; along = len - a.distance; return true; }
                if (hasEnd && l.end == a.endId) { link = i; along = len - (a.linkLength - a.distance); return true; }
                if (hasEnd && l.start == a.endId) { link = i; along = a.linkLength - a.distance; return true; }
            }
            return false;
        }

        /// <summary>
        /// The world pose of an object on the profile's surface at an angle, an offset out of it, turned by a rotation
        /// relative to standing on the surface (up out of it) facing along the path.
        /// </summary>
        public static void Place(Spline spline, Spline.Pose pose, float angle, float offset, Quaternion turn, out Vector3 position, out Quaternion rotation)
            => Place(spline, pose, angle, offset, turn, false, out position, out rotation);

        /// <summary><see cref="Place(Spline, Spline.Pose, float, float, Quaternion, out Vector3, out Quaternion)"/>; <paramref name="upright"/> keeps the profile's up instead of the surface's.</summary>
        public static void Place(Spline spline, Spline.Pose pose, float angle, float offset, Quaternion turn, bool upright, out Vector3 position, out Quaternion rotation)
        {
            var sp = spline.SurfacePoint(angle);
            var outward = pose.right * sp.x + pose.up * sp.y;
            var n = outward.sqrMagnitude > 1e-10f ? outward.normalized : pose.up;
            var t = spline.transform;
            position = t.TransformPoint(pose.position + outward + n * offset);
            rotation = t.rotation * Quaternion.LookRotation(pose.forward, upright ? pose.up : n) * turn;
        }

        /// <summary>
        /// Take the nearest place on the spline (undoable with the move that caused it): its angle round the profile, its
        /// offset out of the surface and its turn relative to it. Pulled further out than its offset by more than a catch
        /// distance, it comes off. Forced (a new anchor), it goes onto the surface wherever it is, standing on it.
        /// </summary>
        static void Reanchor(Spline spline, SplineAnchor a, bool force)
        {
            var t = a.transform; var st = spline.transform;
            var local = st.InverseTransformPoint(t.position);
            if (!spline.Nearest(local, out int link, out float along, out _) || !spline.PoseAt(link, along, out var pose)) return;
            var rel = local - pose.position;
            float x = Vector3.Dot(rel, pose.right), y = Vector3.Dot(rel, pose.up);
            float angle = new Vector2(x, y).sqrMagnitude > 1e-10f ? Mathf.Atan2(x, y) * Mathf.Rad2Deg : a.angle;
            float offset = new Vector2(x, y).magnitude - spline.SurfacePoint(angle).magnitude;
            if (!force && offset - Mathf.Max(0f, a.offset) > CatchMeters) { SetFree(a); return; }
            Quaternion turn;
            if (force) { offset = 0f; turn = Quaternion.identity; }
            else
            {
                Place(spline, pose, angle, 0f, Quaternion.identity, a.upright, out _, out var frame);
                turn = Quaternion.Inverse(frame) * t.rotation; // keeps a turn given by hand
            }
            var l = spline.links[link];
            Set(a, l.start, l.end, along, angle, Mathf.Max(0f, offset), turn, spline.CurveLength(link));
        }

        static void Set(SplineAnchor a, int startId, int endId, float distance, float angle, float offset, Quaternion turn, float length)
        {
            if (a.startId == startId && a.endId == endId && Mathf.Approximately(a.distance, distance) && Mathf.Approximately(a.angle, angle)
                && Mathf.Approximately(a.offset, offset) && Quaternion.Angle(a.rotation, turn) < 1e-3f) return;
            Undo.RecordObject(a, "Move on spline");
            a.startId = startId; a.endId = endId; a.distance = distance; a.angle = angle; a.offset = offset; a.rotation = turn; a.linkLength = length;
        }

        static void SetFree(SplineAnchor a)
        {
            if (a.startId == WallAnchor.Free) return;
            Undo.RecordObject(a, "Move off spline");
            a.startId = a.endId = WallAnchor.Free;
        }

        // ------------------------------------------------------------------ attaching

        /// <summary>
        /// Put an object on the nearest spline (or the given one), standing on its surface where it is nearest: it becomes the
        /// spline's child and follows it from now on. Undoable. Null when there is no spline.
        /// </summary>
        public static SplineAnchor Attach(GameObject go, Spline spline = null)
        {
            if (go == null) return null;
            if (spline == null) spline = NearestSpline(go.transform.position);
            if (spline == null || go.GetComponent<PointGraph>() != null) return null;
            int group = Undo.GetCurrentGroup();
            if (go.transform.parent != spline.transform) Undo.SetTransformParent(go.transform, spline.transform, "Attach to spline");
            if (!go.TryGetComponent<SplineAnchor>(out var a)) a = Undo.AddComponent<SplineAnchor>(go);
            Undo.RecordObject(a, "Attach to spline");
            a.startId = a.endId = WallAnchor.Unplaced;
            a.MarkBrushes(true);
            Layout(spline);
            Undo.CollapseUndoOperations(group);
            return a;
        }

        /// <summary>Take an object off its spline: it leaves the spline where it is, and stays there. Undoable.</summary>
        public static void Detach(GameObject go)
        {
            if (go == null || !go.TryGetComponent<SplineAnchor>(out var a)) return;
            var spline = a.Spline;
            int group = Undo.GetCurrentGroup();
            if (spline != null) Undo.SetTransformParent(go.transform, spline.transform.parent, "Detach from spline");
            a.MarkBrushes(false);
            Undo.DestroyObjectImmediate(a);
            Undo.CollapseUndoOperations(group);
        }

        static Spline NearestSpline(Vector3 world)
        {
            Spline best = null; float bestD = float.MaxValue;
            var active = BrushGenerator.Active;
            for (int i = 0; i < active.Count; i++)
            {
                if (!(active[i] is Spline s)) continue;
                var local = s.transform.InverseTransformPoint(world);
                if (s.Nearest(local, out _, out _, out float d) && d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        [MenuItem("GameObject/Brush/Attach to Spline", false, 47)]
        static void AttachSelected()
        {
            int group = Undo.GetCurrentGroup();
            foreach (var go in Selection.gameObjects) if (CanAttach(go)) Attach(go);
            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/Brush/Attach to Spline", true)]
        static bool CanAttachSelected() { foreach (var go in Selection.gameObjects) if (CanAttach(go)) return true; return false; }

        static bool CanAttach(GameObject go) => go != null && !EditorUtility.IsPersistent(go) && go.GetComponent<PointGraph>() == null && go.GetComponent<SplineAnchor>() == null
            && go.GetComponent<WallAnchor>() == null && !(go.TryGetComponent<Brush>(out var b) && b.IsGenerated) && NearestSpline(go.transform.position) != null;

        [MenuItem("GameObject/Brush/Detach from Spline", false, 48)]
        static void DetachSelected()
        {
            int group = Undo.GetCurrentGroup();
            foreach (var go in Selection.gameObjects) Detach(go);
            Undo.CollapseUndoOperations(group);
        }

        [MenuItem("GameObject/Brush/Detach from Spline", true)]
        static bool CanDetachSelected() { foreach (var go in Selection.gameObjects) if (go.GetComponent<SplineAnchor>() != null) return true; return false; }

        // ------------------------------------------------------------------ repeating

        /// <summary>
        /// Make the copies of every Spline Repeat on the spline again when the spline or its settings changed: one row per run
        /// of the path, from <see cref="SplineRepeat.start"/> every <see cref="SplineRepeat.spacing"/>. Derived, never undone.
        /// </summary>
        public static void BuildRepeats(Spline spline)
        {
            foreach (var rep in spline.GetComponents<SplineRepeat>())
            {
                if (!rep.isActiveAndEnabled) continue;
                if (rep.container == null || rep.container.parent != spline.transform)
                {
                    var go = new GameObject(SplineRepeat.ContainerName);
                    go.transform.SetParent(spline.transform, false);
                    rep.container = go.transform; rep.builtKey = 0;
                }
                var flags = BrushSettings.instance.showGenerated ? HideFlags.NotEditable : HideFlags.HideInHierarchy | HideFlags.NotEditable;
                if (rep.container.gameObject.hideFlags != flags) rep.container.gameObject.hideFlags = flags; // only on a change: a change of flags is a hierarchy change
                foreach (var b in rep.container.GetComponentsInChildren<Brush>(true)) b.anchored = true; // placed by the spline, never snapped (not saved)
                int key = RepeatKey(spline, rep);
                if (key == rep.builtKey) continue;
                rep.builtKey = key;
                for (int i = rep.container.childCount - 1; i >= 0; i--) Object.DestroyImmediate(rep.container.GetChild(i).gameObject);
                if (rep.source == null || rep.source.transform.IsChildOf(spline.transform)) continue; // nothing to copy, or a copy of itself
                foreach (var run in spline.Runs())
                {
                    float length = Spline.RunLength(run);
                    for (float d = rep.start; d <= length + 1e-4f; d += rep.spacing)
                    {
                        if (run.closed && d >= length - 1e-4f && d > rep.start) break; // round a loop: not twice on its first place
                        var pose = spline.PoseAlong(run, d);
                        Place(spline, pose, rep.angle, rep.offset, Quaternion.Euler(rep.rotation), rep.upright, out var position, out var rotation);
                        var copy = PrefabUtility.IsPartOfPrefabAsset(rep.source) ? (GameObject)PrefabUtility.InstantiatePrefab(rep.source, rep.container) : Object.Instantiate(rep.source, rep.container);
                        copy.name = rep.source.name;
                        copy.SetActive(true);
                        copy.transform.SetPositionAndRotation(position, rotation);
                        copy.hideFlags = HideFlags.NotEditable;
                        foreach (var b in copy.GetComponentsInChildren<Brush>(true)) { b.anchored = true; BrushSync.NotifyTransformChanged(b); }
                    }
                }
            }
        }

        static int RepeatKey(Spline spline, SplineRepeat rep)
        {
            unchecked
            {
                int h = spline.Key();
                h = h * 31 + (rep.source != null ? rep.source.GetHashCode() : 0);
                h = h * 31 + Mathf.RoundToInt(rep.spacing * 1e4f); h = h * 31 + Mathf.RoundToInt(rep.start * 1e4f);
                h = h * 31 + Mathf.RoundToInt(rep.angle * 1e3f); h = h * 31 + Mathf.RoundToInt(rep.offset * 1e4f);
                h = h * 31 + (rep.upright ? 7 : 3);
                h = h * 31 + rep.rotation.GetHashCode(); // the copies are the spline's children: they move with it
                return h == 0 ? 1 : h;
            }
        }
    }
}

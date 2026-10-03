using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A floor plan: a network of walls drawn on the floor. Points are joined by walls, and a point may join any number of
    /// walls, so interior walls belong to the same plan as the outside ones. Walls that cross or end on another wall are
    /// joined there; every enclosed area is a room with its own floor and ceiling. Walls on the outside of the building
    /// stand on the side of the line set by <see cref="side"/>; every other wall is centred on its line. Everything is
    /// generated (see <see cref="BrushGenerator"/>): change the plan and the brushes follow. Door and window brushes under
    /// it ride on its walls (see <see cref="WallAnchor"/>).
    /// </summary>
    [AddComponentMenu("CSG Brush/Floor Plan")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/FloorPlan.png")]
    [DisallowMultipleComponent]
    public sealed class FloorPlan : BrushGenerator
    {
        public enum Side
        {
            [Tooltip("Outside walls stand outside the line: the line is the rooms' inner face.")] Outside,
            [Tooltip("Outside walls are centred on the line.")] Centered,
            [Tooltip("Outside walls stand inside the line.")] Inside,
        }

        /// <summary>A wall between two points, by their ids.</summary>
        [Serializable]
        public struct WallLink
        {
            public int start, end;
            /// <summary>Metres; 0 uses the plan's outside or interior thickness.</summary>
            public float thickness;
            public WallLink(int start, int end, float thickness = 0f) { this.start = start; this.end = end; this.thickness = thickness; }
        }

        /// <summary>
        /// The floor and ceiling of one room, for rooms that differ from the plan's defaults. A room is found again by the ids
        /// of the points around it, so it keeps its settings while walls move; a room split by a new wall passes them to both
        /// halves.
        /// </summary>
        [Serializable]
        public sealed class RoomSettings
        {
            public List<int> boundary = new List<int>();
            public bool floor = true;
            public Material floorMaterial;
            public bool ceiling;
            public Material ceilingMaterial;
        }

        /// <summary>The points, on the floor of this object (y is ignored). Edit through the point and wall methods, which keep the ids in step.</summary>
        [HideInInspector] public List<Vector3> points = new List<Vector3>();
        /// <summary>One id per point that stays with the point while others are added or removed. Never reused.</summary>
        [HideInInspector] public List<int> pointIds = new List<int>();
        [HideInInspector] public int nextPointId = 1;
        /// <summary>The walls, by the ids of their two points.</summary>
        [HideInInspector] public List<WallLink> walls = new List<WallLink>();
        [HideInInspector] public List<RoomSettings> rooms = new List<RoomSettings>();
        /// <summary>Plans from before wall networks were an outline through the points in order: closed joined the last to the first. Read once, when they are converted.</summary>
        [HideInInspector] public bool closed;

        [Header("Walls")]
        [Tooltip("Thickness of walls on the outside of the building, metres.")] public float wallThickness = 0.2f;
        [Tooltip("Thickness of walls between rooms and of free-standing walls, metres.")] public float interiorWallThickness = 0.1f;
        [Tooltip("Metres.")] public float wallHeight = 3f;
        [Tooltip("Where outside walls stand relative to the drawn line; other walls are centred on it.")] public Side side = Side.Outside;
        [Tooltip("Empty uses the project's default material.")] public Material wallMaterial;

        [Header("Floors and ceilings")]
        [Tooltip("Rooms get a floor slab unless set otherwise per room.")] public bool floor = true;
        [Tooltip("Metres, down from the bottom of the walls.")] public float floorThickness = 0.2f;
        [Tooltip("Empty uses the project's default material.")] public Material floorMaterial;
        [Tooltip("Rooms get a ceiling slab unless set otherwise per room.")] public bool ceiling;
        [Tooltip("Metres, up from the top of the walls.")] public float ceilingThickness = 0.2f;
        [Tooltip("Empty uses the project's default material.")] public Material ceilingMaterial;

        // ------------------------------------------------------------------ points and walls

        /// <summary>Give every point an id, turn an outline from before wall networks into walls, and drop walls that join nothing.</summary>
        public void EnsureGraph()
        {
            while (pointIds.Count > points.Count) pointIds.RemoveAt(pointIds.Count - 1);
            while (pointIds.Count < points.Count) pointIds.Add(nextPointId++);
            if (walls.Count == 0 && points.Count >= 2)
            {
                for (int i = 0; i + 1 < points.Count; i++) walls.Add(new WallLink(pointIds[i], pointIds[i + 1]));
                if (closed && points.Count > 2) walls.Add(new WallLink(pointIds[points.Count - 1], pointIds[0]));
                closed = false;
            }
            var seen = new HashSet<long>();
            for (int i = walls.Count - 1; i >= 0; i--)
            {
                var w = walls[i];
                long key = ((long)Mathf.Min(w.start, w.end) << 32) | (uint)Mathf.Max(w.start, w.end);
                if (w.start == w.end || PointIndex(w.start) < 0 || PointIndex(w.end) < 0 || !seen.Add(key)) walls.RemoveAt(i);
            }
        }

        /// <summary>The index of the point with an id, or -1.</summary>
        public int PointIndex(int id) { for (int i = 0; i < pointIds.Count; i++) if (pointIds[i] == id) return i; return -1; }

        /// <summary>A new point; returns its id.</summary>
        public int AddPoint(Vector3 p) { EnsureGraph(); points.Add(new Vector3(p.x, 0f, p.z)); pointIds.Add(nextPointId); return nextPointId++; }

        /// <summary>A wall between two points; returns its index, or -1 when they are one point or already joined.</summary>
        public int AddWall(int startId, int endId, float thickness = 0f)
        {
            if (startId == endId || PointIndex(startId) < 0 || PointIndex(endId) < 0) return -1;
            foreach (var w in walls) if ((w.start == startId && w.end == endId) || (w.start == endId && w.end == startId)) return -1;
            walls.Add(new WallLink(startId, endId, thickness));
            return walls.Count - 1;
        }

        /// <summary>Split a wall with a new point (kept on the floor); both halves keep its thickness. Returns the new point's id.</summary>
        public int SplitWall(int wall, Vector3 p)
        {
            var w = walls[wall];
            int id = AddPoint(p);
            walls[wall] = new WallLink(w.start, id, w.thickness);
            walls.Insert(wall + 1, new WallLink(id, w.end, w.thickness));
            return id;
        }

        /// <summary>Remove a point and its walls.</summary>
        public void RemovePointAt(int index)
        {
            EnsureGraph();
            int id = pointIds[index];
            points.RemoveAt(index); pointIds.RemoveAt(index);
            walls.RemoveAll(w => w.start == id || w.end == id);
        }

        /// <summary>Remove points no wall uses.</summary>
        public void RemoveLonePoints()
        {
            var used = new HashSet<int>();
            foreach (var w in walls) { used.Add(w.start); used.Add(w.end); }
            for (int i = points.Count - 1; i >= 0; i--) if (!used.Contains(pointIds[i])) { points.RemoveAt(i); pointIds.RemoveAt(i); }
        }

        /// <summary>Replace the points; points that carry over pass their ids (null ids: all new). Walls to points that are gone are dropped.</summary>
        public void SetPoints(List<Vector3> newPoints, List<int> ids)
        {
            points = new List<Vector3>(newPoints);
            pointIds = ids != null ? new List<int>(ids) : new List<int>();
            foreach (var id in pointIds) if (id >= nextPointId) nextPointId = id + 1;
            EnsureGraph();
        }

        /// <summary>The indices of a wall's two points.</summary>
        public void WallPoints(int wall, out int a, out int b) { a = PointIndex(walls[wall].start); b = PointIndex(walls[wall].end); }

        // ------------------------------------------------------------------ what the plan makes

        /// <summary>One wall of the plan, on its floor (x, z as a 2D point).</summary>
        public struct Wall
        {
            /// <summary>Index in <see cref="walls"/>.</summary>
            public int index;
            public int startId, endId;
            public Vector2 a, b;
            /// <summary>Unit direction from a to b, and the unit normal toward the wall's outside face (out of the building for an outside wall).</summary>
            public Vector2 direction, outward;
            /// <summary>Offsets of the wall's two faces from the line along <see cref="outward"/>.</summary>
            public float inner, outer;
            /// <summary>On the outside of the building: one side faces a room, the other does not.</summary>
            public bool exterior;
            public float Length => (b - a).magnitude;
            public float Thickness => outer - inner;
        }

        /// <summary>An enclosed area of the plan.</summary>
        public struct Room
        {
            /// <summary>The room's corners on the walls' lines, counter-clockwise seen from above.</summary>
            public Vector2[] polygon;
            /// <summary>Ids of the points around it.</summary>
            public int[] boundary;
            /// <summary>Index in <see cref="rooms"/> of its settings, or -1 for the plan's defaults.</summary>
            public int settings;
            public bool floor, ceiling;
            public Material floorMaterial, ceilingMaterial;
        }

        public override int Key()
        {
            EnsureGraph();
            unchecked
            {
                int h = 17;
                for (int i = 0; i < points.Count; i++) { h = h * 31 + pointIds[i]; h = h * 31 + Mathf.RoundToInt(points[i].x * 1e5f); h = h * 31 + Mathf.RoundToInt(points[i].z * 1e5f); }
                foreach (var w in walls) { h = h * 31 + w.start; h = h * 31 + w.end; h = h * 31 + Mathf.RoundToInt(w.thickness * 1e5f); }
                foreach (var r in rooms)
                {
                    foreach (var id in r.boundary) h = h * 31 + id;
                    h = h * 31 + (r.floor ? 1 : 2) + (r.ceiling ? 4 : 8);
                    h = h * 31 + (r.floorMaterial != null ? r.floorMaterial.GetHashCode() : 0);
                    h = h * 31 + (r.ceilingMaterial != null ? r.ceilingMaterial.GetHashCode() : 0);
                }
                h = h * 31 + Mathf.RoundToInt(wallThickness * 1e5f);
                h = h * 31 + Mathf.RoundToInt(interiorWallThickness * 1e5f);
                h = h * 31 + Mathf.RoundToInt(wallHeight * 1e5f);
                h = h * 31 + (int)side;
                h = h * 31 + (floor ? 1 : 0); h = h * 31 + Mathf.RoundToInt(floorThickness * 1e5f);
                h = h * 31 + (ceiling ? 1 : 0); h = h * 31 + Mathf.RoundToInt(ceilingThickness * 1e5f);
                h = h * 31 + (wallMaterial != null ? wallMaterial.GetHashCode() : 0);
                h = h * 31 + (floorMaterial != null ? floorMaterial.GetHashCode() : 0);
                h = h * 31 + (ceilingMaterial != null ? ceilingMaterial.GetHashCode() : 0);
                return h;
            }
        }

        protected override void OnValidate()
        {
            if (wallThickness < 0.001f) wallThickness = 0.001f;
            if (interiorWallThickness < 0.001f) interiorWallThickness = 0.001f;
            if (wallHeight < 0.001f) wallHeight = 0.001f;
            if (floorThickness < 0.001f) floorThickness = 0.001f;
            if (ceilingThickness < 0.001f) ceilingThickness = 0.001f;
            base.OnValidate();
        }

        /// <summary>The walls, in the order of <see cref="walls"/>.</summary>
        public void Walls(List<Wall> into) { into.Clear(); into.AddRange(Analyze().walls); }

        /// <summary>The rooms: every area the walls enclose.</summary>
        public void Rooms(List<Room> into) { into.Clear(); into.AddRange(Analyze().rooms); }

        /// <summary>The settings of a room, made from the plan's defaults when the room has none yet.</summary>
        public RoomSettings SettingsOf(Room room)
        {
            if (room.settings >= 0 && room.settings < rooms.Count)
            {
                var s = rooms[room.settings];
                // a room split off another one gets settings of its own
                if (new HashSet<int>(s.boundary).SetEquals(room.boundary)) return s;
                var copy = new RoomSettings { boundary = new List<int>(room.boundary), floor = s.floor, floorMaterial = s.floorMaterial, ceiling = s.ceiling, ceilingMaterial = s.ceilingMaterial };
                rooms.Add(copy);
                return copy;
            }
            var made = new RoomSettings { boundary = new List<int>(room.boundary), floor = floor, floorMaterial = floorMaterial, ceiling = ceiling, ceilingMaterial = ceilingMaterial };
            rooms.Add(made);
            return made;
        }

        public override void Describe(List<BrushSpec> into)
        {
            var an = Analyze();
            float h = wallHeight;
            // wall pieces: their ends meet at the points, cut where they meet another wall
            var corners = EndCorners(an, out var caps);
            for (int p = 0; p < an.pieces.Count; p++)
            {
                var pc = an.pieces[p];
                var ea = corners[2 * p]; var eb = corners[2 * p + 1];
                var poly = Slab(new[] { ea.left, eb.right, eb.left, ea.right }, 0f, h);
                if (poly == null) // a cut corner folded the wall: plain square ends
                {
                    var n = Normal(pc.b - pc.a);
                    poly = Slab(new[] { pc.a + n * pc.lo, pc.b + n * pc.lo, pc.b + n * pc.hi, pc.a + n * pc.hi }, 0f, h);
                }
                if (poly == null) continue;
                string name = "Wall " + (pc.wall + 1) + (an.piecesOfWall[pc.wall] > 1 ? "." + (pc.part + 1) : "");
                into.Add(new BrushSpec { name = name, operation = BrushOperation.Add, polyhedron = poly, material = wallMaterial });
            }
            int junction = 0;
            foreach (var cap in caps)
            {
                var poly = Slab(cap, 0f, h);
                if (poly != null) into.Add(new BrushSpec { name = "Junction " + (++junction), operation = BrushOperation.Add, polyhedron = poly, material = wallMaterial });
            }
            // floors and ceilings: per room, out to the outer face of outside walls and to the middle of the others
            for (int r = 0; r < an.rooms.Count; r++)
            {
                var room = an.rooms[r];
                if (!room.floor && !room.ceiling) continue;
                var outline = RoomOutline(an, r);
                if (room.floor)
                {
                    var slab = Slab(outline, -floorThickness, 0f) ?? Slab(room.polygon, -floorThickness, 0f);
                    if (slab != null) into.Add(new BrushSpec { name = "Floor " + (r + 1), operation = BrushOperation.Add, polyhedron = slab, material = room.floorMaterial });
                }
                if (room.ceiling)
                {
                    var slab = Slab(outline, h, h + ceilingThickness) ?? Slab(room.polygon, h, h + ceilingThickness);
                    if (slab != null) into.Add(new BrushSpec { name = "Ceiling " + (r + 1), operation = BrushOperation.Add, polyhedron = slab, material = room.ceilingMaterial });
                }
            }
        }

        // ------------------------------------------------------------------ analysis

        const float Eps = 1e-4f;

        /// <summary>A straight run of one wall between two nodes (a wall is cut where other walls cross or touch it).</summary>
        sealed class Piece
        {
            public int wall, part, na, nb;
            public Vector2 a, b;
            /// <summary>Offsets of its two faces from the line along its right normal (lo below hi).</summary>
            public float lo, hi;
            public bool exterior;
            /// <summary>For an outside wall: the outside of the building is on the right of a to b.</summary>
            public bool outsideRight;
        }

        sealed class Node { public Vector2 p; public int id = -1; public readonly List<(int piece, bool atStart, float angle)> ends = new List<(int, bool, float)>(); }

        sealed class Analysis
        {
            public int key;
            public readonly List<Node> nodes = new List<Node>();
            public readonly List<Piece> pieces = new List<Piece>();
            public int[] piecesOfWall = new int[0];
            public readonly List<Wall> walls = new List<Wall>();
            public readonly List<Room> rooms = new List<Room>();
            /// <summary>Per room: its boundary as (piece, along a to b) in order, the room on the left.</summary>
            public readonly List<List<(int piece, bool forward)>> roomEdges = new List<List<(int, bool)>>();
        }

        [NonSerialized] Analysis m_Analysis;

        static Vector2 Normal(Vector2 d) { d.Normalize(); return new Vector2(d.y, -d.x); } // right of the direction, seen from above
        static Vector2 P2(Vector3 p) => new Vector2(p.x, p.z);

        Analysis Analyze()
        {
            int key = Key();
            if (m_Analysis != null && m_Analysis.key == key) return m_Analysis;
            var an = new Analysis { key = key };
            m_Analysis = an;

            // nodes: points, merged where they lie on one spot, and the crossings of walls
            var byCell = new Dictionary<(long, long), int>();
            int NodeAt(Vector2 p, int id)
            {
                var cell = ((long)Mathf.Round(p.x / Eps), (long)Mathf.Round(p.y / Eps));
                if (byCell.TryGetValue(cell, out int n)) { if (an.nodes[n].id < 0) an.nodes[n].id = id; return n; }
                an.nodes.Add(new Node { p = p, id = id });
                byCell[cell] = an.nodes.Count - 1;
                return an.nodes.Count - 1;
            }
            var segs = new List<(int wall, Vector2 a, Vector2 b, int ida, int idb)>();
            for (int i = 0; i < walls.Count; i++)
            {
                int ia = PointIndex(walls[i].start), ib = PointIndex(walls[i].end);
                var a = P2(points[ia]); var b = P2(points[ib]);
                if ((b - a).sqrMagnitude < Eps * Eps) continue;
                segs.Add((i, a, b, walls[i].start, walls[i].end));
            }
            an.piecesOfWall = new int[walls.Count];
            var pieceAt = new Dictionary<(int, int), int>();
            for (int s = 0; s < segs.Count; s++)
            {
                var (wall, a, b, ida, idb) = segs[s];
                var ab = b - a; float len2 = ab.sqrMagnitude;
                var cuts = new List<float> { 0f, 1f };
                for (int o = 0; o < segs.Count; o++)
                {
                    if (o == s) continue;
                    var c = segs[o].a; var d = segs[o].b;
                    foreach (var q in new[] { c, d }) // an end of another wall on this one
                    {
                        float t = Vector2.Dot(q - a, ab) / len2;
                        if (t > Eps && t < 1f - Eps && (a + ab * t - q).sqrMagnitude < Eps * Eps * 4f) cuts.Add(t);
                    }
                    var cd = d - c; float den = ab.x * cd.y - ab.y * cd.x;
                    if (Mathf.Abs(den) < 1e-9f) continue; // parallel
                    var ac = c - a;
                    float ts = (ac.x * cd.y - ac.y * cd.x) / den, to = (ac.x * ab.y - ac.y * ab.x) / den;
                    if (ts > Eps && ts < 1f - Eps && to > -Eps && to < 1f + Eps) cuts.Add(ts); // crossing
                }
                cuts.Sort();
                var thickness = walls[wall].thickness;
                int part = 0;
                for (int k = 0; k + 1 < cuts.Count; k++)
                {
                    float t0 = cuts[k], t1 = cuts[k + 1];
                    if ((t1 - t0) * Mathf.Sqrt(len2) < Eps) continue;
                    var pa = a + ab * t0; var pb = a + ab * t1;
                    int na = NodeAt(pa, t0 == 0f ? ida : -1), nb = NodeAt(pb, t1 == 1f ? idb : -1);
                    if (na == nb) continue;
                    var pairKey = (Mathf.Min(na, nb), Mathf.Max(na, nb));
                    if (pieceAt.TryGetValue(pairKey, out int existing))
                    {
                        // two walls over one stretch: the thicker one stands there
                        if (thickness > walls[an.pieces[existing].wall].thickness) an.pieces[existing].wall = wall;
                        continue;
                    }
                    pieceAt[pairKey] = an.pieces.Count;
                    an.pieces.Add(new Piece { wall = wall, part = part++, na = na, nb = nb, a = an.nodes[na].p, b = an.nodes[nb].p });
                }
            }
            foreach (var pc in an.pieces) an.piecesOfWall[pc.wall]++;
            for (int p = 0; p < an.pieces.Count; p++)
            {
                var pc = an.pieces[p]; var d = pc.b - pc.a;
                an.nodes[pc.na].ends.Add((p, true, Mathf.Atan2(d.y, d.x)));
                an.nodes[pc.nb].ends.Add((p, false, Mathf.Atan2(-d.y, -d.x)));
            }
            foreach (var n in an.nodes) n.ends.Sort((x, y) => x.angle.CompareTo(y.angle)); // counter-clockwise

            FindRooms(an);

            // which walls are outside walls, and where their faces are
            for (int p = 0; p < an.pieces.Count; p++)
            {
                var pc = an.pieces[p];
                var n = Normal(pc.b - pc.a); var mid = (pc.a + pc.b) * 0.5f;
                bool right = InRoom(an, mid + n * 1e-3f), left = InRoom(an, mid - n * 1e-3f);
                pc.exterior = right != left;
                pc.outsideRight = !right;
                float t = walls[pc.wall].thickness > 0f ? walls[pc.wall].thickness : pc.exterior ? wallThickness : interiorWallThickness;
                if (!pc.exterior || side == Side.Centered) { pc.lo = -0.5f * t; pc.hi = 0.5f * t; }
                else
                {
                    bool towardOutside = side == Side.Outside;
                    if (pc.outsideRight == towardOutside) { pc.lo = 0f; pc.hi = t; } else { pc.lo = -t; pc.hi = 0f; }
                }
            }

            // the walls as the user drew them, each standing as its longest piece does
            for (int i = 0; i < walls.Count; i++)
            {
                int ia = PointIndex(walls[i].start), ib = PointIndex(walls[i].end);
                var a = P2(points[ia]); var b = P2(points[ib]);
                Piece longest = null;
                foreach (var pc in an.pieces) if (pc.wall == i && (longest == null || (pc.b - pc.a).sqrMagnitude > (longest.b - longest.a).sqrMagnitude)) longest = pc;
                var dir = (b - a).sqrMagnitude > 0f ? (b - a).normalized : Vector2.right;
                var n = new Vector2(dir.y, -dir.x);
                float t0 = walls[i].thickness > 0f ? walls[i].thickness : interiorWallThickness;
                float lo = -0.5f * t0, hi = 0.5f * t0; bool exterior = false, outsideAlongN = false;
                if (longest != null)
                {
                    bool same = Vector2.Dot(longest.b - longest.a, b - a) >= 0f; // the piece may run the other way
                    lo = same ? longest.lo : -longest.hi; hi = same ? longest.hi : -longest.lo;
                    exterior = longest.exterior; outsideAlongN = same ? longest.outsideRight : !longest.outsideRight;
                }
                // outward: toward the face away from the rooms for an outside wall, the right of a to b otherwise
                bool flip = exterior && !outsideAlongN;
                var outward = flip ? -n : n;
                float inner = flip ? -hi : lo, outer = flip ? -lo : hi;
                an.walls.Add(new Wall { index = i, startId = walls[i].start, endId = walls[i].end, a = a, b = b, direction = dir, outward = outward, inner = inner, outer = outer, exterior = exterior });
            }
            return an;
        }

        /// <summary>The faces the walls enclose: walk every wall side keeping the room on the left; counter-clockwise loops are rooms.</summary>
        void FindRooms(Analysis an)
        {
            int halfCount = an.pieces.Count * 2; // half-edge 2p runs a to b, 2p+1 runs b to a
            var visited = new bool[halfCount];
            int From(int h) => (h & 1) == 0 ? an.pieces[h >> 1].na : an.pieces[h >> 1].nb;
            int To(int h) => (h & 1) == 0 ? an.pieces[h >> 1].nb : an.pieces[h >> 1].na;
            int Next(int h)
            {
                var node = an.nodes[To(h)];
                int twin = h ^ 1, k = -1;
                for (int i = 0; i < node.ends.Count; i++)
                {
                    var e = node.ends[i];
                    if ((e.piece << 1 | (e.atStart ? 0 : 1)) == twin) { k = i; break; }
                }
                var prev = node.ends[(k - 1 + node.ends.Count) % node.ends.Count];
                return prev.piece << 1 | (prev.atStart ? 0 : 1);
            }
            for (int start = 0; start < halfCount; start++)
            {
                if (visited[start]) continue;
                var loop = new List<int>();
                int h = start;
                for (int guard = 0; guard <= halfCount && !visited[h]; guard++) { visited[h] = true; loop.Add(h); h = Next(h); }
                // walls walked both ways (a wall ending inside a room, a wall to an island) are not part of the room's outline
                bool removed = true;
                while (removed)
                {
                    removed = false;
                    for (int i = 0; i < loop.Count && !removed; i++)
                        for (int j = i + 1; j < loop.Count && !removed; j++)
                        {
                            if (loop[j] != (loop[i] ^ 1)) continue;
                            var inside = loop.GetRange(i + 1, j - i - 1);
                            var outside = new List<int>(loop.GetRange(0, i)); outside.AddRange(loop.GetRange(j + 1, loop.Count - j - 1));
                            loop = Area(an, outside, From) >= Area(an, inside, From) ? outside : inside;
                            removed = true;
                        }
                }
                if (loop.Count < 3 || Area(an, loop, From) < 1e-6f) continue;
                var poly = new Vector2[loop.Count]; var ids = new List<int>(); var edges = new List<(int, bool)>();
                for (int i = 0; i < loop.Count; i++)
                {
                    var node = an.nodes[From(loop[i])];
                    poly[i] = node.p;
                    if (node.id >= 0) ids.Add(node.id);
                    edges.Add((loop[i] >> 1, (loop[i] & 1) == 0));
                }
                an.rooms.Add(new Room { polygon = poly, boundary = ids.ToArray(), settings = -1 });
                an.roomEdges.Add(edges);
            }
            AssignSettings(an);
        }

        /// <summary>
        /// Which settings each room takes. A settings entry belongs to the room whose points match it best (the same points,
        /// or the room it was before walls moved or a point was added). A room split by a new wall: its pieces share the old
        /// room's points about equally, and each takes the settings. Every other room uses the plan's defaults.
        /// </summary>
        void AssignSettings(Analysis an)
        {
            var score = new float[an.rooms.Count, rooms.Count];
            for (int r = 0; r < an.rooms.Count; r++)
            {
                var set = new HashSet<int>(an.rooms[r].boundary);
                for (int s = 0; s < rooms.Count; s++)
                {
                    var b = rooms[s].boundary;
                    if (b == null || b.Count == 0) continue;
                    int shared = 0; foreach (var id in b) if (set.Contains(id)) shared++;
                    // Jaccard: 1 for the same points, low when only a wall's two ends are shared
                    score[r, s] = shared < 2 ? 0f : shared / (float)(set.Count + b.Count - shared);
                }
            }
            for (int s = 0; s < rooms.Count; s++)
            {
                float best = 0f; for (int r = 0; r < an.rooms.Count; r++) best = Mathf.Max(best, score[r, s]);
                if (best < 0.25f) continue;
                // the best room owns it; a room scoring nearly as well is the other half of a split
                for (int r = 0; r < an.rooms.Count; r++)
                    if (score[r, s] >= best * 0.8f && (an.rooms[r].settings < 0 || score[r, s] > score[r, an.rooms[r].settings]))
                    { var room = an.rooms[r]; room.settings = s; an.rooms[r] = room; }
            }
            for (int r = 0; r < an.rooms.Count; r++)
            {
                var room = an.rooms[r];
                if (room.settings >= 0)
                {
                    var st = rooms[room.settings];
                    room.floor = st.floor; room.floorMaterial = st.floorMaterial; room.ceiling = st.ceiling; room.ceilingMaterial = st.ceilingMaterial;
                }
                else { room.floor = floor; room.floorMaterial = floorMaterial; room.ceiling = ceiling; room.ceilingMaterial = ceilingMaterial; }
                an.rooms[r] = room;
            }
        }

        static float Area(Analysis an, List<int> loop, Func<int, int> from)
        {
            float a = 0f;
            for (int i = 0; i < loop.Count; i++) { var p = an.nodes[from(loop[i])].p; var q = an.nodes[from(loop[(i + 1) % loop.Count])].p; a += p.x * q.y - q.x * p.y; }
            return a * 0.5f;
        }

        static bool InRoom(Analysis an, Vector2 p)
        {
            foreach (var r in an.rooms) if (Contains(r.polygon, p)) return true;
            return false;
        }

        /// <summary>Is a point inside a polygon (even-odd rule)?</summary>
        public static bool Contains(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        // ------------------------------------------------------------------ geometry

        struct EndCorner { public Vector2 left, right; }

        /// <summary>
        /// Where each piece's faces end at its two nodes (index 2p: at a, 2p + 1: at b), left and right seen walking out of
        /// the node. Neighbouring walls around a node meet where their facing faces cross; where they do not (parallel, or too
        /// sharp), the faces end square. Every node joining three or more walls, or with a square end among two, gets a cap
        /// filling the space between the ends.
        /// </summary>
        EndCorner[] EndCorners(Analysis an, out List<Vector2[]> caps)
        {
            var result = new EndCorner[an.pieces.Count * 2];
            caps = new List<Vector2[]>();
            foreach (var node in an.nodes)
            {
                int k = node.ends.Count;
                if (k == 0) continue;
                var dir = new Vector2[k]; var offL = new float[k]; var offR = new float[k];
                for (int i = 0; i < k; i++)
                {
                    var (piece, atStart, _) = node.ends[i];
                    var pc = an.pieces[piece];
                    var d = (pc.b - pc.a).normalized;
                    if (atStart) { dir[i] = d; offR[i] = pc.hi; offL[i] = -pc.lo; } else { dir[i] = -d; offR[i] = -pc.lo; offL[i] = pc.hi; }
                }
                var left = new Vector2[k]; var right = new Vector2[k];
                for (int i = 0; i < k; i++) { var n = new Vector2(dir[i].y, -dir[i].x); right[i] = node.p + n * offR[i]; left[i] = node.p - n * offL[i]; }
                bool square = false;
                if (k >= 2)
                    for (int i = 0; i < k; i++)
                    {
                        int j = (i + 1) % k;
                        var c = Cross(left[i], dir[i], right[j], dir[j], out bool ok);
                        float limit = 4f * Mathf.Max(Mathf.Abs(offL[i]), Mathf.Abs(offR[j]), 1e-3f);
                        if (ok && (c - node.p).magnitude <= limit + Eps) { left[i] = c; right[j] = c; }
                        else square = true;
                    }
                for (int i = 0; i < k; i++)
                {
                    var (piece, atStart, _) = node.ends[i];
                    result[2 * piece + (atStart ? 0 : 1)] = new EndCorner { left = left[i], right = right[i] };
                }
                if (k >= 3 || (k == 2 && square))
                {
                    var cap = new List<Vector2>();
                    for (int i = 0; i < k; i++)
                        foreach (var q in new[] { right[i], left[i] })
                            if (cap.Count == 0 || (cap[cap.Count - 1] - q).sqrMagnitude > Eps * Eps) cap.Add(q);
                    if (cap.Count > 1 && (cap[0] - cap[cap.Count - 1]).sqrMagnitude <= Eps * Eps) cap.RemoveAt(cap.Count - 1);
                    if (cap.Count >= 3) caps.Add(cap.ToArray());
                }
            }
            return result;
        }

        /// <summary>Where two lines cross (each a point and a direction); not ok when they are parallel.</summary>
        static Vector2 Cross(Vector2 p, Vector2 d, Vector2 q, Vector2 e, out bool ok)
        {
            float den = d.x * e.y - d.y * e.x;
            ok = Mathf.Abs(den) > 1e-6f;
            if (!ok) return p;
            var w = q - p;
            float t = (w.x * e.y - w.y * e.x) / den;
            return p + d * t;
        }

        /// <summary>A room's floor outline: its corners moved out to the far face of outside walls; inside walls are split down their middle.</summary>
        Vector2[] RoomOutline(Analysis an, int room)
        {
            var edges = an.roomEdges[room];
            int n = edges.Count;
            var from = new Vector2[n]; var dir = new Vector2[n]; var off = new float[n];
            for (int i = 0; i < n; i++)
            {
                var (piece, forward) = edges[i];
                var pc = an.pieces[piece];
                from[i] = forward ? pc.a : pc.b;
                dir[i] = forward ? (pc.b - pc.a).normalized : (pc.a - pc.b).normalized;
                // the room is on the left; the far face of an outside wall is on the right
                off[i] = pc.exterior ? (forward ? pc.hi : -pc.lo) : 0f;
            }
            var outline = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                var np = new Vector2(dir[prev].y, -dir[prev].x); var ni = new Vector2(dir[i].y, -dir[i].x);
                var c = Cross(from[prev] + np * off[prev], dir[prev], from[i] + ni * off[i], dir[i], out bool ok);
                outline[i] = ok && (c - from[i]).magnitude <= 4f * Mathf.Max(Mathf.Abs(off[prev]), Mathf.Abs(off[i]), 1e-3f) + Eps ? c : from[i] + ni * off[i];
            }
            return outline;
        }

        /// <summary>A slab over a floor outline (x, z; convex or not) from one height to another; null when the outline crosses itself.</summary>
        public static BrushPolyhedron Slab(Vector2[] outline, float bottom, float top)
        {
            int n = outline.Length;
            if (n < 3 || top - bottom < 1e-5f) return null;
            var v = new Vector3[n * 2];
            for (int i = 0; i < n; i++) { v[i] = new Vector3(outline[i].x, bottom, outline[i].y); v[i + n] = new Vector3(outline[i].x, top, outline[i].y); }
            var faces = new List<BrushPolyhedron.Face>();
            var under = new int[n]; var over = new int[n];
            for (int i = 0; i < n; i++) { under[i] = n - 1 - i; over[i] = n + i; }
            faces.Add(new BrushPolyhedron.Face(under)); faces.Add(new BrushPolyhedron.Face(over));
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; faces.Add(new BrushPolyhedron.Face(new[] { i, j, n + j, n + i })); }
            var poly = new BrushPolyhedron { vertices = v, faces = faces.ToArray() };
            poly.EnsureOutward();
            return poly.IsSound(out _) ? poly : null;
        }
    }
}

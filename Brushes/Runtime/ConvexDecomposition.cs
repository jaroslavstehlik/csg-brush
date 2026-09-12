using System.Collections.Generic;
using CsgBrush.Colliders;
using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// Exact convex decomposition of a closed polyhedron by a solid-leaf BSP over its own face planes: every cut
    /// is the extension of an existing face, pieces share cut planes exactly (no gaps or slivers) and a convex
    /// input yields itself. This is what the convex collider builder receives for a Custom brush.
    /// </summary>
    public static class ConvexDecomposition
    {
        const float kEpsilon = 1e-4f;

        /// <summary>
        /// Pieces below this volume (cubic metres) are dropped: they are numerical slivers from nearly coplanar split
        /// faces, and CSG against a sliver is unstable (whole brushes
        /// vanished). About three Quake units cubed.
        /// </summary>
        public static float MinPieceVolume = 1e-4f;

        /// <summary>Piece vertices closer than this are merged (see ConvexPolytope.vertexWeld); the brush layer sets it to a sixteenth of the grid.</summary>
        public static float VertexWeld = 1e-3f;

        sealed class Polygon
        {
            public List<Vector3> points = new List<Vector3>();
            public Vector4 plane;
            public int source;
            /// <summary>The source face touches a concave edge: its plane is a cut that actually separates parts.</summary>
            public bool reflex;
        }

        /// <summary>Faces of the pieces that lie inside the solid, i.e. where the pieces meet; drawn as the cut preview.</summary>
        public static bool Decompose(BrushPolyhedron poly, List<ConvexPolytope> pieces, List<Vector3[]> cutFaces = null)
        {
            pieces.Clear(); cutFaces?.Clear();
            if (poly == null || !poly.IsValid) return false;
            poly.EnsureOutward();
            var input = new List<Polygon>();
            var reflexFaces = new HashSet<int>();
            foreach (var e in poly.ReflexEdges()) { reflexFaces.Add(e.faceA); reflexFaces.Add(e.faceB); }
            for (int f = 0; f < poly.faces.Length; f++)
            {
                var plane = poly.Plane(f);
                if (float.IsNaN(plane.x)) continue;
                foreach (var tri in Triangulate(poly, f))
                {
                    var p = new Polygon { plane = plane, source = f, reflex = reflexFaces.Contains(f) };
                    foreach (var i in tri) p.points.Add(poly.vertices[i]);
                    input.Add(p);
                }
            }
            if (input.Count < 4) return false;
            var path = new List<Vector4>();
            Build(input, path, pieces, 0);
            if (pieces.Count == 0) return false;
            if (cutFaces != null) FindCutFaces(poly, pieces, cutFaces);
            return true;
        }

        static IEnumerable<int[]> Triangulate(BrushPolyhedron poly, int face)
        {
            var idx = poly.faces[face].indices;
            if (idx.Length == 3) { yield return idx; yield break; }
            // convex faces: fan; concave faces: ear clipping in the face plane
            if (IsConvexPolygon(poly, face))
            {
                for (int i = 1; i + 1 < idx.Length; i++) yield return new[] { idx[0], idx[i], idx[i + 1] };
                yield break;
            }
            foreach (var t in EarClip(poly, face)) yield return t;
        }

        static bool IsConvexPolygon(BrushPolyhedron poly, int face)
        {
            var idx = poly.faces[face].indices; var plane = poly.Plane(face); var n = new Vector3(plane.x, plane.y, plane.z);
            for (int i = 0; i < idx.Length; i++)
            {
                var a = poly.vertices[idx[i]]; var b = poly.vertices[idx[(i + 1) % idx.Length]]; var c = poly.vertices[idx[(i + 2) % idx.Length]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - b), n) < -kEpsilon) return false;
            }
            return true;
        }

        static IEnumerable<int[]> EarClip(BrushPolyhedron poly, int face)
        {
            var plane = poly.Plane(face); var n = new Vector3(plane.x, plane.y, plane.z);
            var ring = new List<int>(poly.faces[face].indices);
            int guard = 0;
            while (ring.Count > 3 && guard++ < 1000)
            {
                bool clipped = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    int ia = ring[(i + ring.Count - 1) % ring.Count], ib = ring[i], ic = ring[(i + 1) % ring.Count];
                    var a = poly.vertices[ia]; var b = poly.vertices[ib]; var c = poly.vertices[ic];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - b), n) <= kEpsilon) continue; // reflex corner
                    bool empty = true;
                    for (int j = 0; j < ring.Count && empty; j++)
                    {
                        int v = ring[j]; if (v == ia || v == ib || v == ic) continue;
                        if (PointInTriangle(poly.vertices[v], a, b, c, n)) empty = false;
                    }
                    if (!empty) continue;
                    yield return new[] { ia, ib, ic };
                    ring.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break; // degenerate; fall through with what is left
            }
            if (ring.Count == 3) yield return new[] { ring[0], ring[1], ring[2] };
        }

        static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            return Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= -kEpsilon && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= -kEpsilon && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= -kEpsilon;
        }

        static void Build(List<Polygon> polygons, List<Vector4> path, List<ConvexPolytope> pieces, int depth)
        {
            if (depth > 64) return;
            // splitter: faces at concave edges first (those cuts separate parts; every other face lies on the hull of
            // what is left and cuts nothing), lowest source face first for predictability
            Polygon splitter = polygons[0];
            foreach (var p in polygons)
                if ((p.reflex && !splitter.reflex) || (p.reflex == splitter.reflex && p.source < splitter.source)) splitter = p;
            var plane = splitter.plane;
            var front = new List<Polygon>(); var back = new List<Polygon>();
            foreach (var p in polygons)
            {
                if (p == splitter) continue;
                Classify(p, plane, front, back);
            }
            // in front of the plane: outside unless more faces close a solid there
            if (front.Count > 0)
            {
                path.Add(-plane);
                Build(front, path, pieces, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
            // behind the plane: solid, possibly bounded further by more faces
            path.Add(plane);
            if (back.Count > 0) Build(back, path, pieces, depth + 1);
            else
            {
                var piece = new ConvexPolytope(path, null, VertexWeld);
                if (piece.Build() && piece.Volume() > Mathf.Max(kEpsilon, MinPieceVolume)) pieces.Add(piece);
            }
            path.RemoveAt(path.Count - 1);
        }

        static void Classify(Polygon p, Vector4 plane, List<Polygon> front, List<Polygon> back)
        {
            int nf = 0, nb = 0;
            foreach (var v in p.points)
            {
                float d = ConvexPolytope.Distance(plane, v);
                if (d > kEpsilon) nf++; else if (d < -kEpsilon) nb++;
            }
            if (nf == 0 && nb == 0)
            {
                // coplanar: same facing is consumed by the splitter, opposite facing bounds the solid from the other side
                if (Vector3.Dot((Vector3)p.plane, (Vector3)plane) > 0f) return;
                back.Add(p); return;
            }
            if (nf == 0) { back.Add(p); return; }
            if (nb == 0) { front.Add(p); return; }
            var f = new Polygon { plane = p.plane, source = p.source, reflex = p.reflex }; var b = new Polygon { plane = p.plane, source = p.source, reflex = p.reflex };
            for (int i = 0; i < p.points.Count; i++)
            {
                var a = p.points[i]; var c = p.points[(i + 1) % p.points.Count];
                float da = ConvexPolytope.Distance(plane, a), dc = ConvexPolytope.Distance(plane, c);
                if (da >= -kEpsilon) f.points.Add(a);
                if (da <= kEpsilon) b.points.Add(a);
                if ((da > kEpsilon && dc < -kEpsilon) || (da < -kEpsilon && dc > kEpsilon))
                {
                    var x = Vector3.Lerp(a, c, da / (da - dc));
                    f.points.Add(x); b.points.Add(x);
                }
            }
            if (f.points.Count >= 3) front.Add(f);
            if (b.points.Count >= 3) back.Add(b);
        }

        static void FindCutFaces(BrushPolyhedron poly, List<ConvexPolytope> pieces, List<Vector3[]> cutFaces)
        {
            var planes = new Vector4[poly.faces.Length];
            for (int f = 0; f < planes.Length; f++) planes[f] = poly.Plane(f);
            foreach (var piece in pieces)
            {
                foreach (var face in piece.faces)
                {
                    var pts = new Vector3[face.Length];
                    var centre = Vector3.zero;
                    for (int i = 0; i < face.Length; i++) { pts[i] = piece.vertices[face[i]]; centre += pts[i]; }
                    centre /= face.Length;
                    bool onSurface = false;
                    for (int f = 0; f < planes.Length && !onSurface; f++)
                    {
                        if (Mathf.Abs(BrushPolyhedron.Distance(planes[f], centre)) > kEpsilon * 10f) continue;
                        if (PointInFace(poly, f, centre)) onSurface = true;
                    }
                    if (!onSurface) cutFaces.Add(pts);
                }
            }
        }

        static bool PointInFace(BrushPolyhedron poly, int face, Vector3 p)
        {
            // winding number in the face plane (works for concave faces)
            var plane = poly.Plane(face); var n = new Vector3(plane.x, plane.y, plane.z);
            var u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized; var w = Vector3.Cross(n, u);
            var idx = poly.faces[face].indices;
            float px = Vector3.Dot(p, u), py = Vector3.Dot(p, w);
            int winding = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                var a = poly.vertices[idx[i]]; var b = poly.vertices[idx[(i + 1) % idx.Length]];
                float ax = Vector3.Dot(a, u) - px, ay = Vector3.Dot(a, w) - py, bx = Vector3.Dot(b, u) - px, by = Vector3.Dot(b, w) - py;
                if (ay <= 0f) { if (by > 0f && ax * by - bx * ay > 0f) winding++; }
                else if (by <= 0f && ax * by - bx * ay < 0f) winding--;
            }
            return winding != 0;
        }
    }
}

using System.Collections.Generic;
using CsgBrush.Colliders;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Scene-view feedback for Custom shapes: the edges, and when the shape is concave the concave edges in orange
    /// and the cut faces (where the convex parts meet) as dashed lines. Nothing is drawn for a convex shape beyond
    /// its edges, so the feedback appears exactly when a student needs it.
    /// </summary>
    public static class BrushShapeDrawing
    {
        public static readonly Color EdgeColor = new Color(0.3f, 0.8f, 1f, 0.9f);

        /// <summary>Convex parts and cut faces of a brush's shape (cached per polyhedron instance and edit).</summary>
        public static int Parts(Brush brush, List<Vector3[]> cutFaces)
        {
            var pieces = new List<ConvexPolytope>();
            if (brush.shape != BrushShape.Custom || brush.polyhedron == null || !brush.polyhedron.IsValid) { cutFaces?.Clear(); return 1; }
            ConvexDecomposition.Decompose(brush.polyhedron, pieces, cutFaces);
            return pieces.Count;
        }

        static readonly Color CutFill = new Color(1f, 0.3f, 0.25f, 0.16f), CutEdge = new Color(1f, 0.35f, 0.3f, 0.9f), CutSelectedFill = new Color(1f, 0.45f, 0.3f, 0.3f);

        /// <summary>
        /// Every active subtract brush as a translucent red volume with its edges (the Cuts toggle in the Brushes
        /// overlay): a cut that has carved everything away has no surface of its own to see or click otherwise.
        /// </summary>
        public static void DrawCuts()
        {
            var selected = new HashSet<GameObject>(Selection.gameObjects);
            foreach (var brush in Object.FindObjectsByType<Brush>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (brush.operation != BrushOperation.Subtract || !brush.enabled) continue;
                var poly = BrushGeometry.Polyhedron(brush);
                if (poly == null || !poly.IsValid) continue;
                bool isSelected = selected.Contains(brush.gameObject);
                using (new Handles.DrawingScope(BrushGeometry.LocalToWorld(brush)))
                {
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                    foreach (var face in poly.faces)
                    {
                        var pts = new Vector3[face.indices.Length];
                        for (int i = 0; i < pts.Length; i++) pts[i] = poly.vertices[face.indices[i]];
                        Handles.color = isSelected ? CutSelectedFill : CutFill;
                        Handles.DrawAAConvexPolygon(pts);
                        Handles.color = CutEdge;
                        for (int i = 0; i < pts.Length; i++) Handles.DrawLine(pts[i], pts[(i + 1) % pts.Length], isSelected ? 2.5f : 1f);
                    }
                    Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                }
            }
        }

        public static void Draw(Brush brush, bool drawEdges)
        {
            if (brush == null || brush.shape != BrushShape.Custom) return;
            var poly = brush.polyhedron;
            if (poly == null || !poly.IsValid) return;
            var m = Matrix4x4.TRS(brush.transform.position, brush.transform.rotation, Vector3.one);
            if (!string.IsNullOrEmpty(brush.problem))
            {
                Handles.color = Color.red;
                foreach (var face in poly.faces)
                    for (int i = 0; i < face.indices.Length; i++)
                        Handles.DrawLine(m.MultiplyPoint3x4(poly.vertices[face.indices[i]]), m.MultiplyPoint3x4(poly.vertices[face.indices[(i + 1) % face.indices.Length]]), 3f);
                Handles.Label(m.MultiplyPoint3x4(poly.Bounds().max), brush.problem, EditorStyles.helpBox);
            }
            using (new Handles.DrawingScope(m))
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
                if (drawEdges)
                {
                    Handles.color = EdgeColor;
                    foreach (var face in poly.faces)
                        for (int i = 0; i < face.indices.Length; i++)
                            Handles.DrawLine(poly.vertices[face.indices[i]], poly.vertices[face.indices[(i + 1) % face.indices.Length]], 1.5f);
                }
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            }
        }
    }
}

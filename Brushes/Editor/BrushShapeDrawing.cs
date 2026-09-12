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
        public static readonly Color ReflexColor = new Color(1f, 0.55f, 0.1f, 1f);
        public static readonly Color CutColor = new Color(1f, 0.8f, 0.3f, 0.9f);

        /// <summary>Convex parts and cut faces of a brush's shape (cached per polyhedron instance and edit).</summary>
        public static int Parts(Brush brush, List<Vector3[]> cutFaces)
        {
            var pieces = new List<ConvexPolytope>();
            if (brush.shape != BrushShape.Custom || brush.polyhedron == null || !brush.polyhedron.IsValid) { cutFaces?.Clear(); return 1; }
            ConvexDecomposition.Decompose(brush.polyhedron, pieces, cutFaces);
            return pieces.Count;
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
                var reflex = poly.ReflexEdges();
                if (reflex.Count == 0) return;
                Handles.color = ReflexColor;
                foreach (var e in reflex) Handles.DrawLine(poly.vertices[e.a], poly.vertices[e.b], 4f);
                var cuts = new List<Vector3[]>();
                Parts(brush, cuts);
                Handles.color = CutColor;
                foreach (var face in cuts)
                    for (int i = 0; i < face.Length; i++)
                        Handles.DrawDottedLine(face[i], face[(i + 1) % face.Length], 4f);
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// A material dragged from the Project window onto a brush in the Scene view becomes that brush's material. Without this,
    /// Unity's own drop would give it to the generated mesh under the mouse, which every brush of the group shares, until
    /// the next rebuild. On a floor plan it goes to what was hit: the plan's walls, or that room's floor or ceiling. While
    /// dragging, the brushes it will go to are outlined.
    /// </summary>
    [InitializeOnLoad]
    public static class BrushMaterialDrop
    {
        static BrushMaterialDrop()
        {
            // a drop handler runs before Unity's own, which would preview the material on the generated mesh (all brushes)
            DragAndDrop.RemoveDropHandlerV2(OnDrop);
            DragAndDrop.AddDropHandlerV2(OnDrop);
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static Brush s_Target;

        static DragAndDropVisualMode OnDrop(Object dropUpon, Vector3 worldPosition, Vector2 viewportPosition, Transform parent, bool perform)
        {
            var material = DraggedMaterial();
            // another object in front of the brushes (a prop, a ProBuilder mesh) takes the material Unity's way
            if (material == null || !OverBrushes(dropUpon)) { s_Target = null; return DragAndDropVisualMode.None; }
            var hit = BrushHooks.PickBrushSurface(Event.current != null ? Event.current.mousePosition : viewportPosition, out _, out _);
            if (hit == null)
            {
                s_Target = null;
                // over a generated mesh but no brush (a hair off an edge): still keep Unity's preview off it
                return dropUpon != null ? DragAndDropVisualMode.Rejected : DragAndDropVisualMode.None;
            }
            s_Target = hit;
            if (perform)
            {
                Drop(hit, material);
                BrushApi.ForceUpdate();
                s_Target = null;
            }
            SceneView.RepaintAll();
            return DragAndDropVisualMode.Link;
        }

        static void OnSceneGUI(SceneView view)
        {
            var e = Event.current;
            if (e.type == EventType.DragExited) { s_Target = null; view.Repaint(); return; }
            if (e.type != EventType.Repaint || s_Target == null || DraggedMaterial() == null) return;
            foreach (var b in Receivers(s_Target)) DrawOutline(b); // Unity's drag label already names the material
        }

        /// <summary>Whether what Unity picked under the mouse leaves the drop to the brushes: nothing, or a mesh generated from them.</summary>
        public static bool OverBrushes(Object dropUpon) => !(dropUpon is GameObject go) || BrushSync.IsGenerated(go.transform);

        static Material DraggedMaterial()
        {
            foreach (var o in DragAndDrop.objectReferences) if (o is Material m) return m;
            return null;
        }

        /// <summary>The brushes a material dropped on <paramref name="hit"/> ends up on: the brush, or on a floor plan every wall, or that one floor or ceiling.</summary>
        public static List<Brush> Receivers(Brush hit)
        {
            var list = new List<Brush>();
            if (hit.generatedBy is FloorPlan plan && !hit.name.StartsWith("Floor ") && !hit.name.StartsWith("Ceiling "))
            { foreach (var b in plan.generated) if (b != null && !b.name.StartsWith("Floor ") && !b.name.StartsWith("Ceiling ")) list.Add(b); }
            else list.Add(hit);
            return list;
        }

        static void DrawOutline(Brush brush)
        {
            var poly = BrushGeometry.Polyhedron(brush);
            if (poly == null) return;
            var m = brush.transform.localToWorldMatrix;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.color = BrushEditState.SelectedColor;
            foreach (var f in poly.faces)
            {
                var pts = new Vector3[f.indices.Length + 1];
                for (int i = 0; i < f.indices.Length; i++) pts[i] = m.MultiplyPoint3x4(poly.vertices[f.indices[i]]);
                pts[f.indices.Length] = pts[0];
                Handles.DrawAAPolyLine(3f, pts);
            }
        }

        /// <summary>Give a material to a brush, or for a floor plan's brush to the wall, floor or ceiling setting it comes from. Undoable.</summary>
        public static void Drop(Brush hit, Material material)
        {
            if (!(hit.generatedBy is FloorPlan plan)) { BrushApi.SetMaterial(hit, material); return; }
            Undo.RecordObject(plan, "Set floor plan material");
            string name = hit.name;
            if (name.StartsWith("Floor ") || name.StartsWith("Ceiling "))
            {
                bool floor = name.StartsWith("Floor ");
                int index = int.TryParse(name.Substring(name.IndexOf(' ') + 1), out int n) ? n - 1 : -1;
                var rooms = new List<FloorPlan.Room>(); plan.Rooms(rooms);
                if (index >= 0 && index < rooms.Count)
                {
                    var settings = plan.SettingsOf(rooms[index]);
                    if (floor) settings.floorMaterial = material; else settings.ceilingMaterial = material;
                }
            }
            else plan.wallMaterial = material;
            BrushGenerators.MarkDirty(plan);
            BrushGenerators.Flush();
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    public static class BrushMenu
    {
        const string kMenu = "GameObject/Brush/";

        static Vector3 Snap(Vector3 p, float g)
        {
            if (g <= 0f) return p;
            return new Vector3(Mathf.Round(p.x / g) * g, Mathf.Round(p.y / g) * g, Mathf.Round(p.z / g) * g);
        }

        static Brush CreateAtPivot(BrushShape shape)
        {
            var s = BrushSettings.instance;
            var size = s.ToMeters(s.defaultBoxSize);
            Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            pivot = Snap(pivot, s.GridMeters);
            Transform parent = Selection.activeTransform != null && Selection.activeTransform.GetComponent<Brush>() == null ? Selection.activeTransform : null;
            var brush = BrushApi.Create(shape, pivot, size, Quaternion.identity, parent);
            Selection.activeGameObject = brush.gameObject;
            return brush;
        }

        [MenuItem(kMenu + "Box", false, 10)] static void Box() => CreateAtPivot(BrushShape.Box);
        [MenuItem(kMenu + "Wedge (ramp)", false, 11)] static void Wedge() => CreateAtPivot(BrushShape.Wedge);
        [MenuItem(kMenu + "Cylinder", false, 12)] static void Cylinder() => CreateAtPivot(BrushShape.Cylinder);
        [MenuItem(kMenu + "Cone", false, 13)] static void Cone() => CreateAtPivot(BrushShape.Cone);
        [MenuItem(kMenu + "Sphere", false, 14)] static void Sphere() => CreateAtPivot(BrushShape.Sphere);
        [MenuItem(kMenu + "Linear Stairs", false, 15)] static void Stairs() => CreateAtPivot(BrushShape.Stairs);
        [MenuItem(kMenu + "Curved Stairs", false, 16)] static void CurvedStairs() => CreateAtPivot(BrushShape.CurvedStairs);
        [MenuItem(kMenu + "Spiral Stairs", false, 17)] static void SpiralStairs() => CreateAtPivot(BrushShape.SpiralStairs);
        [MenuItem(kMenu + "Arch", false, 18)] static void Arch() => CreateAtPivot(BrushShape.Arch);
        [MenuItem(kMenu + "Door", false, 19)] static void Door() => CreateAtPivot(BrushShape.Door);

        [MenuItem(kMenu + "Hollow room", false, 30)]
        static void Room()
        {
            var brush = CreateAtPivot(BrushShape.Box);
            var s = BrushSettings.instance;
            BrushApi.SetSize(brush, s.ToMeters(new Vector3(s.defaultBoxSize.x * 4f, s.defaultBoxSize.y * 2f, s.defaultBoxSize.z * 4f)));
            BrushApi.SetHollow(brush, true, s.ToMeters(s.GridUnits));
            brush.name = "Room";
        }

        // order: right-click a brush in the Hierarchy (GameObject menu) or in the Scene view / Inspector (component context)
        static Brush[] SelectedBrushes() => Selection.GetFiltered<Brush>(SelectionMode.Editable | SelectionMode.ExcludePrefab);

        [MenuItem(kMenu + "To First", false, 50)] static void ToFirst() { foreach (var b in SelectedBrushes()) BrushApi.ToFirst(b); }
        [MenuItem(kMenu + "To First", true)] static bool CanToFirst() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "To Last", false, 51)] static void ToLast() { foreach (var b in SelectedBrushes()) BrushApi.ToLast(b); }
        [MenuItem(kMenu + "To Last", true)] static bool CanToLast() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "Move Up", false, 52)] static void Up() => StepSelected(-1);
        [MenuItem(kMenu + "Move Up", true)] static bool CanUp() => SelectedBrushes().Length > 0;
        [MenuItem(kMenu + "Move Down", false, 53)] static void Down() => StepSelected(1);
        [MenuItem(kMenu + "Move Down", true)] static bool CanDown() => SelectedBrushes().Length > 0;

        /// <summary>Step every selected brush, the one furthest in the direction first; one that cannot move holds back those behind it, so the selection keeps its order.</summary>
        static void StepSelected(int direction)
        {
            var brushes = new System.Collections.Generic.List<Brush>(SelectedBrushes());
            brushes.Sort((a, b) => direction * (b.transform.GetSiblingIndex() - a.transform.GetSiblingIndex()));
            var blocked = new System.Collections.Generic.HashSet<Transform>();
            foreach (var b in brushes)
            {
                var neighbour = BrushApi.VisibleNeighbour(b, direction);
                if (neighbour == null || blocked.Contains(neighbour)) { blocked.Add(b.transform); continue; }
                BrushApi.Step(b, direction);
            }
        }

        [MenuItem("CONTEXT/Brush/To First")] static void ContextToFirst(MenuCommand c) => BrushApi.ToFirst((Brush)c.context);
        [MenuItem("CONTEXT/Brush/To Last")] static void ContextToLast(MenuCommand c) => BrushApi.ToLast((Brush)c.context);
        [MenuItem("CONTEXT/Brush/Move Up")] static void ContextUp(MenuCommand c) => BrushApi.Step((Brush)c.context, -1);
        [MenuItem("CONTEXT/Brush/Move Down")] static void ContextDown(MenuCommand c) => BrushApi.Step((Brush)c.context, 1);

        [MenuItem("Brushes/Rebuild Now")]
        static void Rebuild() => BrushApi.ForceUpdate();
    }
}

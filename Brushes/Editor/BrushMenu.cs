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
        [MenuItem(kMenu + "Stairs", false, 15)] static void Stairs() => CreateAtPivot(BrushShape.Stairs);

        [MenuItem(kMenu + "Hollow room", false, 30)]
        static void Room()
        {
            var brush = CreateAtPivot(BrushShape.Box);
            var s = BrushSettings.instance;
            BrushApi.SetSize(brush, s.ToMeters(new Vector3(s.defaultBoxSize.x * 4f, s.defaultBoxSize.y * 2f, s.defaultBoxSize.z * 4f)));
            BrushApi.SetHollow(brush, true, s.ToMeters(s.GridUnits));
            brush.name = "Room";
        }

        [MenuItem("Brushes/Rebuild Now")]
        static void Rebuild() => BrushApi.ForceUpdate();
    }
}

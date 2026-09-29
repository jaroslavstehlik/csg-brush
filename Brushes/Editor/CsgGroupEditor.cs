using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    [CustomEditor(typeof(CsgGroup))]
    [CanEditMultipleObjects]
    public sealed class CsgGroupEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var group = (CsgGroup)target;
            int count = BrushPrefabBaking.BrushesOf(group).Count;
            EditorGUILayout.LabelField("Bakes", count + (count == 1 ? " brush" : " brushes") + " (below it, up to the next group)");
            if (PrefabUtility.IsPartOfAnyPrefab(group) || BrushCsg.IsInPrefabContext(group.gameObject))
                EditorGUILayout.HelpBox("Part of a prefab: the mesh and colliders are baked into the prefab, so it can be instantiated at runtime. It never carves anything outside itself.", MessageType.None);
            else
                EditorGUILayout.HelpBox("The mesh and colliders are generated children of this object, saved with the scene. Its transform moves everything it bakes.", MessageType.None);
        }
    }
}

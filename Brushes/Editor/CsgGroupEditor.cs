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
            var prefabPath = BrushPrefabBaking.PrefabPathOf(group);
            if (prefabPath != null)
            {
                EditorGUILayout.HelpBox("Part of a prefab: the mesh and colliders are baked into the prefab, so it can be instantiated at runtime. It never carves anything outside itself.", MessageType.None);
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(group.gameObject);
                bool unsaved = stage != null && stage.scene.isDirty;
                using (new EditorGUI.DisabledScope(unsaved))
                    if (GUILayout.Button(new GUIContent("Rebake prefab", unsaved ? "Save the prefab first: rebaking works on the saved file" : "Build this prefab's mesh and colliders again and save them into " + prefabPath)))
                        BrushPrefabBaking.Bake(prefabPath);
            }
            else
                EditorGUILayout.HelpBox("The mesh and colliders are generated children of this object, saved with the scene. Its transform moves everything it bakes.", MessageType.None);
        }
    }
}

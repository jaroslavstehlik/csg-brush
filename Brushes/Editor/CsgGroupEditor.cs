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
            if (targets.Length == 1) EditorGUILayout.LabelField("Brushes", BrushPrefabBaking.BrushesOf((CsgGroup)target).Count.ToString());
            bool unsaved = false;
            foreach (var t in targets)
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(((CsgGroup)t).gameObject);
                if (stage != null && stage.scene.isDirty) unsaved = true;
            }
            using (new EditorGUI.DisabledScope(unsaved))
                if (GUILayout.Button(new GUIContent("Rebake", unsaved ? "Save the prefab first: a prefab is rebaked from its saved file" : "Build the mesh and colliders again from scratch (into the prefab, for a group in a prefab)")))
                    foreach (var t in targets) BrushCsg.Rebake((CsgGroup)t);
        }
    }
}

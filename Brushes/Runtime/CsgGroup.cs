using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A CSG group: the brushes below it (up to the next group) are combined into one mesh per layer and one set of
    /// convex colliders, which the group holds as its generated children. A brush is baked by the nearest group above
    /// it; brushes with none are baked by their scene's automatic group (hidden, one per scene). A group inside a
    /// prefab bakes into the prefab, which then carries its own mesh and colliders; a prefab without a group is a
    /// stamp that carves the level it is placed in (in the editor). The group's transform moves everything it bakes.
    /// </summary>
    [AddComponentMenu("CSG Brush/CSG Group")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/CsgGroup.png")]
    [DisallowMultipleComponent]
    public sealed class CsgGroup : MonoBehaviour
    {
        public const string DefaultName = "<[default model]>";
        public const string MeshChildName = "<[mesh]>";
        /// <summary>Brushes on the Default layer render as <see cref="MeshChildName"/>; every other layer gets its own child, named after the layer.</summary>
        public static string MeshChildNameFor(int layer) => layer == 0 ? MeshChildName : "<[mesh " + (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) ? layer.ToString() : LayerMask.LayerToName(layer)) + "]>";
        public static bool IsMeshChildName(string name) => name == MeshChildName || (name.StartsWith("<[mesh ") && name.EndsWith("]>"));

        /// <summary>The scene's automatic group, for the brushes of that scene that are under no group of their own.</summary>
        [HideInInspector] public bool isDefault;

        /// <summary>Key of the content the generated children were last built from; a prefab instance whose brushes still match its prefab's key keeps the prefab's baked meshes.</summary>
        [HideInInspector] public int bakedKey;
    }
}

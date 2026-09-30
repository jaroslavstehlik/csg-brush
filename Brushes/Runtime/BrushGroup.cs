using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// A brush group: the brushes below it (up to the next group) are combined into one mesh per layer and one set of
    /// convex colliders, which the group holds as its generated children. A brush is built by the nearest group above
    /// it; brushes with none are built by their scene's automatic group (hidden, one per scene). A group inside a
    /// prefab builds into the prefab, which then carries its own mesh and colliders; a prefab without a group is a
    /// stamp that carves the level it is placed in (in the editor). The group's transform moves everything it builds.
    /// </summary>
    [AddComponentMenu("CSG Brush/Brush Group")]
    [Icon("Packages/digital.dream.csgbrush/Brushes/Editor/Icons/BrushGroup.png")]
    [DisallowMultipleComponent]
    public sealed class BrushGroup : MonoBehaviour
    {
        public const string DefaultName = "<[default model]>";
        public const string MeshChildName = "<[mesh]>";
        /// <summary>Brushes on the Default layer render as <see cref="MeshChildName"/>; every other layer gets its own child, named after the layer.</summary>
        public static string MeshChildNameFor(int layer) => layer == 0 ? MeshChildName : "<[mesh " + (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) ? layer.ToString() : LayerMask.LayerToName(layer)) + "]>";
        public static bool IsMeshChildName(string name) => name == MeshChildName || (name.StartsWith("<[mesh ") && name.EndsWith("]>"));

        /// <summary>The scene's automatic group, for the brushes of that scene that are under no group of their own.</summary>
        [HideInInspector] public bool isDefault;

        /// <summary>Key of the content the generated children were last built from; a prefab instance whose brushes still match its prefab's key keeps the prefab's built meshes.</summary>
        [HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("bakedKey")] public int builtKey;

        /// <summary>What every render mesh of the group gets on its renderer (the materials come from the brushes).</summary>
        public RenderingSettings rendering = new RenderingSettings();
    }

    /// <summary>A brush group's renderer settings, as on a Mesh Renderer; the defaults are Unity's.</summary>
    [System.Serializable]
    public sealed class RenderingSettings
    {
        [Tooltip("Whether the meshes cast shadows, and how.")] public UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On;
        [Tooltip("Cast shadows into cached shadow maps only once (static lights).")] public bool staticShadowCaster;
        [Tooltip("Receive shadows from other objects.")] public bool receiveShadows = true;
        [Tooltip("Lightmaps or light probes, for static meshes that contribute to global illumination.")] public ReceiveGI receiveGlobalIllumination = ReceiveGI.Lightmaps;
        [Tooltip("Lightmap texel density relative to the rest: 2 is twice as sharp, 0.5 half.")] [Min(0f)] public float scaleInLightmap = 1f;
        [Tooltip("Blend lightmap seams where faces meet.")] public bool stitchLightmapSeams = true;
        [Tooltip("Lightmap Parameters asset; empty uses the scene's default.")] public Object lightmapParameters;
        [Tooltip("How the meshes use light probes when they are not lightmapped.")] public UnityEngine.Rendering.LightProbeUsage lightProbes = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        [Tooltip("How the meshes use reflection probes.")] public UnityEngine.Rendering.ReflectionProbeUsage reflectionProbes = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        [Tooltip("The point probes are sampled at; empty uses each mesh's bounds.")] public Transform anchorOverride;
        [Tooltip("Motion vectors for motion blur and temporal effects.")] public MotionVectorGenerationMode motionVectors = MotionVectorGenerationMode.Object;
        [Tooltip("Hidden when occluded, even while not static.")] public bool dynamicOcclusion = true;
        [Tooltip("Rendering layers the meshes are on (light and decal layers).")] public uint renderingLayerMask = 1;
    }
}

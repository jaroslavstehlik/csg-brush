using UnityEngine;

namespace CsgBrush
{
    /// <summary>
    /// On a group's generated render mesh: which brush each of its triangles came from, as runs of triangles (in submesh
    /// order). Saved with the scene, so a mesh kept from the last session can still tell which brush a click hit.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class BrushMeshSources : MonoBehaviour
    {
        /// <summary>The first triangle of each run.</summary>
        public int[] runStarts = new int[0];
        /// <summary>The brush of each run's triangles (null where none).</summary>
        public Brush[] runBrushes = new Brush[0];
        /// <summary>Triangles in all.</summary>
        public int triangles;
        /// <summary>The settings the mesh's lightmap UVs were made with (0: none made).</summary>
        public int lightmapUVSettings;
    }
}

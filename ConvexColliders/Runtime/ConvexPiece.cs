using UnityEngine;

namespace CsgBrush.Colliders
{
    /// <summary>
    /// Identity of a generated collider piece: the planes it was built from and the surface data that went into it.
    /// The builder keeps a piece whose identity is unchanged instead of recreating it, so editing one brush only
    /// touches the pieces that brush contributes to. The identity is serialized, so a saved scene reopens
    /// without rebuilding anything.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ConvexPiece : MonoBehaviour
    {
        public Vector4[] planes;
        public int hash;
        /// <summary>Hash of everything the brush's modules put on the piece; a change rebuilds the piece.</summary>
        public int fingerprint;
        public bool trigger;
        public int layer;
        public string brushName;

        public static int HashOf(System.Collections.Generic.List<Vector4> planes, int fingerprint, bool trigger, int layer, string brushName)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < planes.Count; i++)
                {
                    var p = planes[i];
                    h = h * 31 + System.BitConverter.SingleToInt32Bits(p.x);
                    h = h * 31 + System.BitConverter.SingleToInt32Bits(p.y);
                    h = h * 31 + System.BitConverter.SingleToInt32Bits(p.z);
                    h = h * 31 + System.BitConverter.SingleToInt32Bits(p.w);
                }
                h = h * 31 + fingerprint;
                h = h * 31 + (trigger ? 2 : 0);
                h = h * 31 + layer;
                h = h * 31 + (brushName != null ? brushName.GetHashCode() : 0);
                return h;
            }
        }

        /// <summary>Exact comparison; the hash only narrows the candidates.</summary>
        public bool Matches(System.Collections.Generic.List<Vector4> otherPlanes, int fingerprint, bool trigger, int layer, string brushName)
        {
            if (planes == null || planes.Length != otherPlanes.Count) return false;
            if (this.fingerprint != fingerprint || this.trigger != trigger || this.layer != layer || this.brushName != brushName) return false;
            for (int i = 0; i < planes.Length; i++)
            {
                var a = planes[i]; var b = otherPlanes[i];
                if (a.x != b.x || a.y != b.y || a.z != b.z || a.w != b.w) return false;
            }
            return true;
        }
    }
}

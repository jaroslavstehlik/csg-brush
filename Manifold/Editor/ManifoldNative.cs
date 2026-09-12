using System;
using System.Runtime.InteropServices;

namespace CsgBrush.Manifold
{
    /// <summary>
    /// P/Invoke surface of Manifold's C API (bindings/c/include/manifold/manifoldc.h), the part this package uses.
    /// Objects are allocated by the library (manifold_alloc_*), constructed in place by the functions that take a
    /// `mem` argument, and released with manifold_delete_*. Array getters copy into caller-provided arrays.
    /// </summary>
    public static class ManifoldNative
    {
        const string Lib = "manifoldc";

        public enum OpType { Add = 0, Subtract = 1, Intersect = 2 }

        public enum Error
        {
            NoError, NonFiniteVertex, NotManifold, VertexIndexOutOfBounds, PropertiesWrongLength, MissingPositionProperties,
            MergeVectorsDifferentLengths, MergeIndexOutOfBounds, TransformWrongLength, RunIndexWrongLength, FaceIdWrongLength,
            InvalidConstruction, ResultTooLarge,
        }

        // allocation
        [DllImport(Lib)] public static extern IntPtr manifold_alloc_manifold();
        [DllImport(Lib)] public static extern IntPtr manifold_alloc_manifold_vec();
        [DllImport(Lib)] public static extern IntPtr manifold_alloc_meshgl64();
        [DllImport(Lib)] public static extern void manifold_delete_manifold(IntPtr m);
        [DllImport(Lib)] public static extern void manifold_delete_manifold_vec(IntPtr ms);
        [DllImport(Lib)] public static extern void manifold_delete_meshgl64(IntPtr m);

        // mesh construction (double precision)
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64(IntPtr mem, [In] double[] vertProps, UIntPtr nVerts, UIntPtr nProps, [In] ulong[] triVerts, UIntPtr nTris);
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64_merge(IntPtr mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_of_meshgl64(IntPtr mem, IntPtr mesh);
        [DllImport(Lib)] public static extern IntPtr manifold_get_meshgl64(IntPtr mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_as_original(IntPtr mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_empty(IntPtr mem);
        [DllImport(Lib)] public static extern IntPtr manifold_copy(IntPtr mem, IntPtr m);

        // vectors and booleans
        [DllImport(Lib)] public static extern IntPtr manifold_manifold_empty_vec(IntPtr mem);
        [DllImport(Lib)] public static extern void manifold_manifold_vec_push_back(IntPtr ms, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_boolean(IntPtr mem, IntPtr a, IntPtr b, OpType op);
        [DllImport(Lib)] public static extern IntPtr manifold_batch_boolean(IntPtr mem, IntPtr ms, OpType op);

        // info
        [DllImport(Lib)] public static extern int manifold_is_empty(IntPtr m);
        [DllImport(Lib)] public static extern Error manifold_status(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_num_vert(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_num_tri(IntPtr m);
        [DllImport(Lib)] public static extern double manifold_volume(IntPtr m);
        [DllImport(Lib)] public static extern int manifold_original_id(IntPtr m);

        // mesh extraction (double precision)
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_num_prop(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_num_vert(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_num_tri(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_vert_properties_length(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_tri_length(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_run_index_length(IntPtr m);
        [DllImport(Lib)] public static extern UIntPtr manifold_meshgl64_run_original_id_length(IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64_vert_properties([Out] double[] mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64_tri_verts([Out] ulong[] mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64_run_index([Out] ulong[] mem, IntPtr m);
        [DllImport(Lib)] public static extern IntPtr manifold_meshgl64_run_original_id([Out] uint[] mem, IntPtr m);
    }
}

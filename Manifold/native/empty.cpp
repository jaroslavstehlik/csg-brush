// The shared library's own sources: everything else comes from the static Manifold libraries linked in.
// One extra entry point the C binding of this Manifold version lacks: Manifold::Simplify.
#include <new>
#include <manifold/manifold.h>
#include <manifold/manifoldc.h>

#if defined(_WIN32)
#define MANIFOLDC_UNITY_EXPORT extern "C" __declspec(dllexport)
#else
#define MANIFOLDC_UNITY_EXPORT extern "C" __attribute__((visibility("default")))
#endif

/// A copy of the manifold with every feature smaller than the tolerance collapsed (surfaces move by less than
/// the tolerance, the result stays manifold). Same memory convention as the C binding: `mem` comes from
/// manifold_alloc_manifold and is released with manifold_delete_manifold.
MANIFOLDC_UNITY_EXPORT ManifoldManifold* manifoldc_unity_simplify(void* mem, ManifoldManifold* m, double tolerance)
{
    const auto* source = reinterpret_cast<const manifold::Manifold*>(m);
    return reinterpret_cast<ManifoldManifold*>(new (mem) manifold::Manifold(source->Simplify(tolerance)));
}

/// Sets the per-triangle face ids of a mesh about to become a manifold (the C binding cannot). Manifold keeps
/// every edge between triangles of different ids through booleans and simplification, and hands the ids back
/// per output triangle (manifold_meshgl64_face_id).
MANIFOLDC_UNITY_EXPORT void manifoldc_unity_meshgl64_set_face_id(ManifoldMeshGL64* m, const uint64_t* ids, size_t count)
{
    auto* mesh = reinterpret_cast<manifold::MeshGL64*>(m);
    mesh->faceID.assign(ids, ids + count);
}

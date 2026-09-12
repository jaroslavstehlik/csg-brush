# Convex colliders per brush

Rule: every collider a character controller can touch is convex. Chisel renders the CSG result, which
is concave, and its own `MeshCollider` follows that mesh. This module replaces it with one convex
collider per brush piece, built by evaluating the CSG tree at the brush level:

- an additive brush is a convex solid;
- a subtractive brush splits every solid it penetrates along its planes: the part of the solid outside
  each plane becomes a convex piece and the remainder (inside the cutter) is discarded. This is the
  QuakeEd and Radiant brush subtraction, exact and always convex;
- an intersecting brush clips the solids by its planes;
- composites and branches evaluate their children first, then combine with their own operation;
- a brush that only touches a solid does not split it.

Axis aligned pieces become `BoxCollider`, everything else a convex `MeshCollider`. Every Chisel model
gets the `Convex Colliders` component automatically; while it is present Chisel skips its own collider
and hides its collider settings. Rebuilds are undo-aware and skipped when the geometry did not change.

Tag brushes with `Controller Surface` when they are not plain solid: Slick, Water (a convex trigger,
never carves), Trigger, NoCollision. Games attach their own components to pieces by subscribing to
`ConvexColliderHooks.PieceCreated`.

Chisel's box brush spans 0..1 in its local space with the pivot at the minimum corner: a box at position
P with scale S occupies P..P+S. (The brush-first layer changes this to a centred pivot.)

Files: `Runtime/ConvexPolytope.cs` (plane-list convex solid: build, clip, subtract, intersect, mesh),
`Runtime/ControllerSurface.cs`, `Runtime/ConvexColliderSettings.cs`, `Runtime/ConvexColliderHooks.cs`,
`Runtime/ChiselConvexColliderRegistration.cs`, `Editor/ConvexColliderBuilder.cs`, diagnostics in `Editor/`.

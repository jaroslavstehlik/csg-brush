# CSG Brush

A Quake/Unreal-style brush level editor for Unity 6000.6, made for teaching: a handful of convex shapes, a world
grid that is always respected, additive and subtractive brushes, hand-editable shapes, and one convex collider per
brush piece so character controllers get clean contacts. Rendering goes through the
[Manifold](https://github.com/elalish/manifold) library (Apache 2.0), a robust mesh boolean engine loaded as an
editor-only native plugin; the game only ships the generated meshes and colliders.

The package grew out of a fork of [Chisel](https://github.com/RadicalCSG/com.scholastika.csgbrush) (MIT, see LICENSE). Chisel's
CSG core, generators and tools have all been replaced; the brush layer, the snapping rules, the edit tools and the
collider builder are original. History: `CHANGELOG-FORK.md`. Install: Package Manager, "Install package from git
URL", the URL of this repository.

## Brushes (the student-facing layer)

- **Create**: the Create tool in the Scene view toolbar (one button with a shape dropdown: Box, Wedge, Cylinder,
  Cone, Sphere, Linear Stairs, Curved Stairs, Spiral Stairs; also Tools > CSG Brush > Create). Press on a surface, a brush face or the ground, drag the
  base rectangle, release, move the mouse to set the height, click to create; Escape cancels or leaves the tool.
  Everything snaps to the grid, and a wall makes the brush grow out of the wall. Cylinder, Cone and Sphere are drawn from the centre of
  their base: press on the centre, drag the radius, then the height (a sphere with no height is round; lift for an
  ellipsoid). Curved and Spiral Stairs are drawn by their axis: press where the column axis goes, drag the outer radius (the drag direction is
  where the first step starts), then the height; the transform of those brushes is the axis at floor level. The "New Brush" panel that opens
  with the tool holds the values the next brush is made with: operation, surface, sides, tessellation, step
  sizes, hollow and wall thickness. GameObject > Brush > ... still creates a
  default-sized brush at the view pivot, and the `Brush` component can be added to any empty object. A brush is one GameObject with nothing hidden underneath it; the level's mesh and colliders are
  generated under a hidden model object.
- **Place and size**: use the normal Move and Rotate tools with Unity snapping. Size is edited in the Inspector
  in world units (the metre value is shown next to it) and is centred on the transform. The Scale tool resizes
  the brush: on release the scale is baked into the size and the transform scale returns to one.
- **Operation and surface**: Add or Subtract; Solid, Slick, Water, Trigger, No collision (the convex collider
  builder tags each piece for the character controller). Hollow turns a box or cylinder into a room with a wall
  thickness.
- **Edit shape**: the Edit Brush context (Inspector or overlay button, or the tool context dropdown in the Scene
  view Tools overlay) puts Vertex, Edge and Face selection in the Tool Settings toolbar, laid out like ProBuilder:
  the three mode toggles (keys 1, 2, 3), Select Hidden (also pick elements facing away from the camera), Drag
  Rectangle Mode (only what is completely inside, or everything the rectangle touches) and the handle
  orientation: Global, Local, or Element, which aligns the gizmo with the selection so the blue axis runs along
  the face normal, the natural way to push a face in or out. Click selects, Shift adds, Ctrl removes, drag a rectangle to select several, Esc clears. The Extrude panel that
  opens with edit mode extrudes the selected faces by a distance in world units (negative cuts a pocket), either
  the whole selection as one block or each face on its own; the faces stay selected for the next extrusion.
  The Move gizmo moves the selection on the grid; vertices dropped onto each other weld. The first edit turns the brush into a Custom shape; "Reset to Box" (or the
  shape it came from) discards the edits. Concave shapes are allowed; they are split into convex parts for the colliders
  automatically. Every shape can be edited by hand.
- **Stairs**: Linear Stairs fill their size box with steps of a given height and length. Curved Stairs and Spiral
  Stairs follow Unreal's parameters (inner radius, step width, step height, angle of curve or steps per 360, num
  steps, add to first step, counter clockwise, and for spirals step thickness, sloped floor, sloped ceiling);
  their size follows the parameters. Each step is a closed block and gets its own convex collider; a sloped
  spiral's collider is the hull of each twisted block, so use more steps per turn for a smoother ramp.
- **Order**: the Hierarchy order is the CSG order. A subtract carves only the brushes above it, so a brush
  created after the cutter stays whole until it is moved above it. "To first" and "To last" change the sibling
  order.
- **World preset**: Project Settings > Brushes selects Quake (32 units per metre), Source, Unreal, metric or
  custom, the grid steps and the default sizes. The Scene view "Brushes" overlay shows the preset and the grid;
  `[` and `]` change the grid size.
- **Grid**: with "Snap to grid" on (default), every brush is kept on the world grid after each edit: the minimum
  corner on the grid, sizes as grid multiples, rotation in steps, no scale. Parents may only organise brushes; a
  rotated or scaled parent is listed in the overlay with a Reset button, off-grid brushes with Snap all.
- **Models**: brushes belong to the nearest `Brush Model` component above them (a folder whose transform moves its
  part of the level) or to a hidden default model. Each model gets one mesh, split per material, and one set of
  convex colliders.
- **Rebuild**: Brushes > Rebuild Now forces a full regeneration (meshes and convex colliders).
- **Tests**: Window > General > Test Runner, EditMode, `CsgBrush.Tests` (also runs headlessly with
  `-runTests -testPlatform EditMode -testFilter CsgBrush.Tests`).

## Native plugin

`Manifold/Plugins` holds one self-contained shared library per editor platform (macOS arm64, Windows x64, Linux
x64), built from Manifold at a pinned tag by `Manifold/native/CMakeLists.txt`. The GitHub workflow
`.github/workflows/build-manifold.yml` builds all three and opens a pull request with the binaries; run it after
bumping the tag. Nothing native is needed at runtime.

## Layout

- `Brushes/` the Brush and Brush Model components, the editor layer (sync, snapping, edit tools, overlay,
  settings, menus), the Manifold model builder (`BrushCsg`), and the tests.
- `ConvexColliders/` the convex collider builder and the surface tags used by character controllers.
- `Manifold/` the native plugin, its P/Invoke layer and the `ManifoldSolid` wrapper.

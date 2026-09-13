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
  Cone, Sphere, Stairs; also Tools > CSG Brush > Create). Press on a surface, a brush face or the ground, drag the
  base rectangle, release, move the mouse to set the height, click to create; Escape cancels or leaves the tool.
  Everything snaps to the grid, and a wall makes the brush grow out of the wall. The "New" switch in the Brushes
  overlay picks Add or Subtract for the brushes drawn next. GameObject > Brush > ... still creates a
  default-sized brush at the view pivot, and the `Brush` component can be added to any empty object. A brush is one GameObject with nothing hidden underneath it; the level's mesh and colliders are
  generated under a hidden model object.
- **Place and size**: use the normal Move and Rotate tools with Unity snapping. Size is edited in the Inspector
  in world units (the metre value is shown next to it) and is centred on the transform. The Scale tool resizes
  the brush: on release the scale is baked into the size and the transform scale returns to one.
- **Operation and surface**: Add or Subtract; Solid, Slick, Water, Trigger, No collision (the convex collider
  builder tags each piece for the character controller). Hollow turns a box or cylinder into a room with a wall
  thickness.
- **Edit shape**: the Edit Shape tool (Inspector or overlay button, or the toolbar) has Face, Edge and Vertex
  modes (keys 1, 2, 3). Click selects, Shift adds, Ctrl removes, drag a rectangle to select several, Esc clears.
  The Move gizmo moves the selection on the grid; vertices dropped onto each other weld. The first edit turns the brush into a Custom shape; "Reset to Box" (or the
  shape it came from) discards the edits. Concave shapes are allowed: the concave edges turn orange and dashed lines
  show where the shape is split into convex parts for the colliders. Every shape can be edited by hand.
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

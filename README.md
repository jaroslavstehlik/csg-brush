# CSG Brush

A Quake/Unreal-style brush level editor for Unity 6000.6, made for teaching: a handful of convex shapes, a world
grid that is always respected, additive and subtractive brushes, hand-editable shapes, and one convex collider per
brush piece so character controllers get clean contacts. Rendering goes through the
[Manifold](https://github.com/elalish/manifold) library (Apache 2.0), a robust mesh boolean engine loaded as an
editor-only native plugin; the game only ships the generated meshes and colliders.

The package grew out of a fork of [Chisel](https://github.com/RadicalCSG/digital.dream.csgbrush) (MIT, see LICENSE). Chisel's
CSG core, generators and tools have all been replaced; the brush layer, the snapping rules, the edit tools and the
collider builder are original. History: `CHANGELOG-FORK.md`. Install: Package Manager, "Install package from git
URL", the URL of this repository.

## Brushes (the student-facing layer)

- **Cuts**: the Cuts toggle in the Brushes overlay draws subtract brushes as translucent red volumes so you can see and select them
  where they have carved everything away; with it off, clicks always prefer the solid brushes.
- **Layers**: a brush's Unity layer picks its CSG group. Brushes on one layer are combined with each other only: a subtract brush
  carves its own layer, each layer renders as its own mesh child on that layer, and the colliders sit on that layer. Use it for a
  collision layer per kind of thing, or to keep parts of a level from carving each other.
- **Create**: the Create tool in the Scene view toolbar (one button with a shape dropdown: Box, Wedge, Cylinder,
  Cone, Sphere, Linear Stairs, Curved Stairs, Spiral Stairs, Arch; also Tools > CSG Brush > Create). Press on a surface, a brush face or the ground, drag the
  base rectangle, release, move the mouse to set the height, click to create; Escape cancels or leaves the tool. While a Create tool is active the
  Brushes overlay shows what the next brush gets (operation, modules, sides, steps, arch thickness and angle, hollow).
  Everything snaps to the grid, and a wall makes the brush grow out of the wall. Cylinder, Cone and Sphere are drawn from the centre of
  their base: press on the centre, drag the radius, then the height (a sphere with no height is round; lift for an
  ellipsoid). Curved and Spiral Stairs are drawn by their axis: press where the column axis goes, drag the outer radius (the drag direction is
  where the first step starts), then the height; the transform of those brushes is the axis at floor level. The values a new brush is made with
  (operation, modules, sides, tessellation, step sizes, hollow) come from Project Settings > Brushes and are
  edited on the brush afterwards. GameObject > Brush > ... still creates a
  default-sized brush at the view pivot, and the `Brush` component can be added to any empty object. A brush is one GameObject with nothing hidden underneath it; the level's mesh and colliders are
  generated under a hidden model object.
- **Place and size**: use the normal Move and Rotate tools with Unity snapping. Size is edited in the Inspector
  in world units (the metre value is shown next to it) and is centred on the transform. The Scale tool resizes
  the brush: on release the scale is baked into the size and the transform scale returns to one.
- **Default material**: faces without a material of their own show a generated ruler texture (lines at every grid size, strong at the
  metre, on a metre checker) that lines up with the world grid across brushes and cuts; created under `Assets/CSG Brush` on first
  use, swappable in Project Settings > Brushes.
- **Operation and collision**: Add or Subtract; Collision is Solid, Trigger or None (no collider), with a physics material and
  Provide Contacts for the colliders. A piece is its brush: it also takes the brush's tag, layer and static flags. A render mesh is its
  model: it takes the model object's tag and static flags (the hidden default model's come from Project Settings). Everything a specific
  character controller cares about (ice, water, fall damage) is a *module*: a `BrushModule` component on the brush object or on a
  parent, which tags all its children. A module's values ride onto every collider piece of the brush and a change rebuilds exactly
  those pieces; a module may make the brush a trigger (water). Trigger brushes raise one Enter and one Exit per collider however
  many pieces they are made of: subscribe to `Brush.TriggerEntered` / `TriggerExited`, implement `IBrushTriggerListener` on the
  same object, or add the `BrushTrigger` module and wire its UnityEvents. Project Settings > Brushes lists the modules every new
  brush gets. The Quake controller's module is `QuakeBrushSurface` in the project, not in this package. Hollow turns a box or cylinder into a room
  with a wall thickness.
- **Edit shape**: the Edit Brush context (Inspector or overlay button, or the tool context dropdown in the Scene
  view Tools overlay) puts Vertex, Edge and Face selection in the Tool Settings toolbar, laid out like ProBuilder:
  the three mode toggles (keys 1, 2, 3), Select Hidden (also pick elements facing away from the camera), Drag
  Rectangle Mode (only what is completely inside, or everything the rectangle touches) and the handle
  orientation: Global, Local, or Element, which aligns the gizmo with the selection so the blue axis runs along
  the face normal, the natural way to push a face in or out. Click selects, Shift adds, Ctrl removes, drag a rectangle to select several, Esc clears. The Extrude panel that opens
  with edit mode holds the distance in world units (negative cuts a pocket) and whether the whole selection
  extrudes as one block or each face on its own; the Extrude button in the Brushes overlay applies it. It is a boolean, so the block may run through
  other parts of the brush, and the faces stay selected for the next extrusion. Your own faces, edges and vertices survive it: the boolean only adds the walls
  and moves the face, so coplanar faces you keep apart stay apart (a belt of walls keeps its edges, a split face keeps its other half) and per-face materials hold.
  Holding Shift when you start dragging the Move gizmo on selected faces extrudes them by the dragged distance along their normal instead of moving them
  (drag into the brush to cut). Bridge (next to Extrude) connects two selected faces of one brush with a block between them, the way a corridor joins two rooms; the faces need the same
  number of corners, and the new walls stay selected. When the result would not be a sound shape (a block ending exactly where the
  brush would touch itself, or a cut that removes everything) the brush is left as it was and the panel says so; pick another distance.
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
- `ConvexColliders/` the convex collider builder; game data reaches the pieces through brush modules.
- `Manifold/` the native plugin, its P/Invoke layer and the `ManifoldSolid` wrapper.

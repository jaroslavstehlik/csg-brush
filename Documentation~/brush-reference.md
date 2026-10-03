# Brush reference

Detailed notes on the brush tools and components. For a task-by-task guide to floor plans, doors and windows, wall anchors, and rendering, see the other pages of this manual.

- **Cuts**: the Cuts toggle in the Brushes overlay draws subtract brushes as translucent red volumes so you can see and select them
  where they have carved everything away; with it off, subtract brushes cannot be clicked in the Scene view.
- **Layers**: brushes are combined per Unity layer. Brushes on one layer are combined with each other only: a subtract brush
  carves its own layer, each layer renders as its own mesh child on that layer, and the colliders sit on that layer. Use it for a
  collision layer per kind of thing, or to keep parts of a level from carving each other.
- **Create**: the Create tool in the Scene view toolbar (one button with a shape dropdown: Box, Wedge, Cylinder,
  Cone, Sphere, Linear Stairs, Curved Stairs, Spiral Stairs, Arch, Door, Window, Floor Plan; also Tools > CSG Brush > Create). Press on a surface, a brush face or the ground, drag the
  base rectangle, release, move the mouse to set the height, click to create; Escape cancels or leaves the tool. While a Create tool is active the
  Brushes overlay shows what the next brush gets (operation, modules, sides, steps, arch thickness and angle, door side width and top height, hollow).
  Everything snaps to the grid, and a wall makes the brush grow out of the wall. Cylinder, Cone and Sphere are drawn from the centre of
  their base: press on the centre, drag the radius, then the height (a sphere with no height is round; lift for an
  ellipsoid). Curved and Spiral Stairs are drawn by their axis: press where the column axis goes, drag the outer radius (the drag direction is
  where the first step starts), then the height; the transform of those brushes is the axis at floor level. The values a new brush is made with
  (operation, modules, sides, tessellation, step sizes, hollow) come from Project Settings > Brushes and are
  edited on the brush afterwards. GameObject > Brush > ... still creates a
  default-sized brush at the view pivot, and the `Brush` component can be added to any empty object. A brush is one GameObject with nothing hidden underneath it; the level's mesh and colliders are
  generated under the brush group that builds it (see below).
- **Floor plan**: pick Floor Plan in the Create tool's dropdown and click a floor to start a plan there (GameObject > Brush >
  Floor Plan starts one at the view pivot), then click the corners (snapped to the grid and
  to 45 degree steps, Shift for any grid point) and click the first one to close the room. The plan makes the walls (one
  hidden brush per wall, rebuilt whenever the plan changes); set their thickness, height and side of the line in its
  Inspector. Its Edit button (or Edit plan in the Brushes overlay) works like Edit Brush for points and walls: Vertex
  or Edge selection (1, 2), click, Shift, Ctrl, drag rectangle, then Move, Rotate and Scale on the floor; + adds a point.
  Delete or Backspace removes the selected points, or the selected walls in Edge mode (a room opens there; a line cut
  in the middle becomes two plans). Clicking a wall selects its plan.
- **Doors and windows**: Door and Window in the Create tool place a cut of a set size with one click: on a floor plan's
  wall it rides on that wall (it follows it, and stays where it was if the wall is deleted); on any other brush it cuts
  through the side it was put on. Move one along its wall with the Move tool; its size is on the brush.
- **Anything on a wall**: put an object under a floor plan and use GameObject > Brush > Attach to Wall (or add a Wall
  Anchor), or draw a brush on a wall's face. Pick Wall on the Wall Anchor, then click a wall, moves it there; its wall
  is outlined while it is selected. Distance along it, height and face are in the Inspector. It follows its wall; move it along the wall, or away from every wall
  to free it; Detach from Wall takes it off.
- **Place and size**: use the normal Move and Rotate tools with Unity snapping. Size is edited in the Inspector
  in world units (the metre value is shown next to it). The Scale tool resizes the brush: on release the scale is
  applied to the size and the transform scale returns to one.
- **Pivot**: where the transform sits in the brush, measured from its left, bottom, back corner. Pivot mode
  Normalized gives it as 0 to 1 of the size (0.5 is the centre, the default), and it follows the brush when it is
  resized; Absolute gives it in grid units, and it stays that far from the corner. A pivot with Y 0 stands the brush
  on what it is placed on; Y 0 and Z 0 stand it against a wall, the way a wardrobe goes on a
  [wall anchor](wall-anchor.md). Setting the pivot moves the transform, never the shape; switching the mode moves
  nothing. On a Custom shape the pivot moves the vertices. Curved and spiral stairs, doors and windows keep their own pivot.
- **Default material**: faces without a material of their own show a generated ruler texture (lines at every grid size, strong at the
  metre, on a metre checker) that lines up with the world grid across brushes and cuts; created under `Assets/CSG Brush` on first
  use, swappable in Project Settings > Brushes.
- **Brush groups and building**: a `Brush Group` component (Add Component > CSG Brush > Brush Group, amber block icon in the
  Hierarchy and Scene view) builds the brushes below it, up to the next group, into one mesh per layer and one set of
  colliders, held as its generated children. A brush is built by the nearest group above it; brushes with none are
  built by their scene's automatic group (hidden, one per scene), so every scene holds its own geometry and scenes can
  be loaded together at runtime. The brush Inspector's *Brush group* field shows what builds it (click to find it; the
  scene asset for a scene's automatic group). A group inside a prefab builds into the prefab (meshes saved as sub-assets
  whenever the prefab is saved), so it can be instantiated at runtime and never carves anything outside itself; scene
  instances use the prefab's meshes until their brushes are changed. A prefab without a group is a stamp: placed in a
  level in the editor, its brushes (a doorway cut, say) join that level's CSG; it builds nothing of its own.
- **Operation and collision**: Add or Subtract; Collision is Solid, Trigger or None (no collider), with a physics material and
  Provide Contacts for the colliders. A piece is its brush: it also takes the brush's tag, layer and static flags. Static flags are per brush:
  its colliders take them, and its triangles render in a mesh of those flags (static and moving brushes still carve each
  other; a doorway's sides take the flags of the wall they are cut into). New brushes start static (Project Settings >
  Brushes). A render mesh takes its group object's tag. Lightmap UVs: Tools > CSG Brush > Generate Lightmap UVs, or a
  light bake makes them first. Other renderer settings (shadows, Scale In Lightmap,
  probes, rendering layers...) are per Brush Group, in its Rendering foldout; every mesh of the group takes them. Everything a specific
  character controller cares about (ice, water, fall damage) is a *module*: a `BrushModule` component on the brush object or on a
  parent, which tags all its children. A module's values ride onto every collider piece of the brush and a change rebuilds exactly
  those pieces; a module may make the brush a trigger (water). Trigger brushes raise one Enter and one Exit per collider however
  many pieces they are made of: put a script with `OnTriggerEnter` / `OnTriggerStay` / `OnTriggerExit` on the brush, subscribe to
  `Brush.TriggerEntered` / `TriggerExited`, implement `IBrushTriggerListener` on the same object, or add the
  `BrushTrigger` module and wire its UnityEvents. Project Settings > Brushes lists the modules every new
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
- **Stairs**: you set the step height and draw the height; the number of steps is the height divided by the step
  height, rounded, so the steps fill it exactly. Linear Stairs fill their size box (each step runs the length divided
  by the steps). Curved Stairs and Spiral Stairs follow Unreal's parameters (inner radius, step width, step height,
  angle of curve or steps per 360, add to first step, counter clockwise, and for spirals step thickness, sloped floor,
  sloped ceiling) with a Height instead of a number of steps; their size follows the parameters. Each step is a closed block and gets its own convex collider; a sloped
  spiral's collider is the hull of each twisted block, so use more steps per turn for a smoother ramp.
- **Order**: the Hierarchy order is the CSG order. A subtract carves only the brushes above it, so a brush
  created after the cutter stays whole until it is moved above it. "To first" and "To last" change the sibling
  order.
- **World preset**: Project Settings > Brushes selects Unity (metres, the default), Quake (32 units per metre),
  Source, Unreal or custom, the grid steps and the default sizes. The Scene view "Brushes" overlay shows the preset and the grid;
  `[` and `]` change the grid size.
- **Grid**: with "Snap to grid" on (default), every brush is kept on the world grid after each edit: the minimum
  corner on the grid, sizes as grid multiples, rotation in steps, no scale. Parents may only organise brushes; a
  rotated or scaled parent is listed in the overlay with a Reset button.
- **Rebuild**: Brushes > Rebuild Now forces a full regeneration (meshes and convex colliders).
- **Tests**: Window > General > Test Runner, EditMode, `CsgBrush.Tests` (also runs headlessly with
  `-runTests -testPlatform EditMode -testFilter CsgBrush.Tests`).

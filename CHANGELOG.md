# Changelog

## Unreleased

- CSG Group is now **Brush Group** (component, Add Component > CSG Brush > Brush Group, the brush Inspector's *Brush group*
  field). The script keeps its GUID, so existing scenes and prefabs keep their groups.
- Brush order from the right-click menu (Hierarchy, Scene view, the Brush component's menu): To First, To Last, Move Up,
  Move Down; a step skips the hidden generated objects. The Inspector's Order row is gone.
- Drawing a brush in empty space starts on the Scene view grid (its axis and position), as ProBuilder does; past the
  grid's horizon there is nothing to draw on.
- Subtract brushes can be clicked in the Scene view only while Cuts are shown.
- Prefab Mode: brushes are picked from the prefab being edited (not the scene behind it); a prefab open in Prefab Mode
  is baked when Prefab Mode closes; a prefab asset's brushes are no longer snapped or built when Unity imports it.
- Removed the off-grid notice and its Snap all button from the Brushes overlay; shorter Inspector texts.
- Large levels: brushes keep a registry of themselves (`Brush.Active`, maintained by OnEnable/OnDisable; inactive brushes
  are not processed), replacing every per-frame scene search. Each scene's automatic group is remembered (finding it
  per brush made grouping quadratic: 7.9 s for 5000 brushes, now cached). The grouping, the rotated-parents check and
  each brush's pick shape are cached and dropped on change. With 5000 brushes: snap check 1.9 to 0.3 ms per editor
  update, parents check 5.3 to 0.0 ms per overlay event, picking 30 to 0.35 ms, grouping for a rebuild 7.9 s to 0.0 ms
  (10 ms once after a hierarchy change). A test holds the per-frame paths under 1 ms.
- Islands: a group is built per island of brushes whose boxes overlap or touch, on each layer. An island keeps its
  union and its Unity arrays until one of its pieces (or a source brush's material) changes, so a brush that touches
  nothing costs only itself when it moves. Each brush keeps its build key, pose and collider data until it changes.
- Colliders per brush: a brush's pieces depend only on its own parts and the subtract brushes that reach it, so a brush
  whose key (parts, fingerprint, kind, layer, name, cutters) is unchanged keeps its pieces untouched and its hook is
  not run again; only changed brushes are built. The fingerprint now tells physics materials apart by identity, not
  only by name. Build with 1001 separate boxes in one group, one moved: 90 to ~9 ms (preparation 35 to 3, union 17
  to 3, Unity mesh 22 to 2, colliders 13 to 2).
- Stairs: set the step height, draw the height; the number of steps is the height divided by the step height, rounded,
  and the steps fill it. Linear stairs lost Step length (each step runs the length divided by the steps) and now fill
  their box: existing linear stairs whose height was not a whole number of steps, or whose steps did not reach the top,
  change shape. Curved and spiral stairs show Height instead of Num steps (a new step height keeps the height); their
  geometry is unchanged. `BrushApi.SetStairs(brush, stepHeight)`. New stairs default to 0.25 m steps (a comfortable
  step for a 1.5 to 2 m character, under Unity's default CharacterController step offset), not one grid step.
  Spiral stairs default to 0.1 m treads, not half a grid step.
- Spiral stairs climb the same step height as every other stair: tread k is at (k + 1) step heights above the floor
  (the transform); the thickness only reaches down from the tread. Before, the shape was lowered onto its first tread's
  underside, so every step sat one thickness lower (a 0.25 m step with 0.1 m treads started at 0.1 m).
- Curved stairs keep their footprint whatever the number of steps: a step wider than 11.25 degrees is made of several
  convex blocks along the curve (one collider each), so a low stair with few steps no longer cuts straight across it.
- Door and Window brushes: cuts of their own size (door 1 x 2.2 m on the floor, window 1.2 x 1.2 m on a 0.9 m sill; set
  in the Brushes overlay while creating, or on the brush), subtract, never snapped, always clickable. One click places
  one: on a floor plan's wall, into the side of any brush (through its thickness), or standing on the floor. Create
  tool, GameObject > Brush > Door / Window (on the selected floor plan's first wall) and Tools > CSG Brush > Create.
  Under a floor plan a door or window rides on its wall (a Wall Anchor: which wall, how far along, sill): it follows
  the wall when the plan changes, stays on its piece when a + splits the wall, cuts through whatever the wall's
  thickness, takes the nearest wall when moved, and is left where it was, a free cut, when its wall is deleted. Plan
  points keep an id for this. The Door frame shape is gone.
- Anything on a wall: GameObject > Brush > Attach to Wall (or a Wall Anchor added to a child of a floor plan) puts any
  object, a prefab or a brush, on the nearest wall where it is; a brush drawn on a wall's face with the Create tool
  stays on that wall. The Wall Anchor's Pick Wall button, then a click on a wall, puts it on that wall (of any floor
  plan); the wall it is on is outlined while it is selected. Distance along it, height and face (inside or outside) are
  set in the Inspector. Anchored objects keep their pose relative
  to the wall's face: they follow the wall, slide along it when moved, take the nearest wall when moved onto another,
  and are left free when dragged away from every wall or when their wall is deleted. Detach from Wall takes one off.
  Brushes they carry are not snapped to the grid.
- Floor Plan (the Create tool's dropdown, Tools > CSG Brush > Create > Floor Plan, GameObject > Brush > Floor Plan, Add
  Component > CSG Brush > Floor Plan): from the Create tool, click a floor to start the plan there. Draw an outline on the floor
  and it becomes walls of one thickness and height, one brush per wall with mitred corners. Points snap to the grid
  and to 45 degree steps (Shift: any grid point); clicking the first point closes the room; Backspace removes the last
  point; Enter, Escape or a double click finishes. Edit (Inspector, or Edit plan in the Brushes overlay) is the
  plan's edit mode, like Edit Brush: Vertex and Edge (wall) selection (1, 2), click, Shift, Ctrl and drag rectangle,
  Ctrl+A, Ctrl+I, and Unity's Move, Rotate and Scale on the selection, on the floor (World / Local / Element
  orientation; Element lies along the wall). In Vertex mode a + (light blue) adds a point, and dragging
  from it moves the new point at once; pressing a point and dragging moves it at once (a point of the selection drags the whole selection), even under the gizmo. Delete or Backspace removes the selected points, or the selected walls in Edge
  mode: a room opens where a wall is removed, and a line split in the middle becomes two plans. Wall thickness (0.2 m, off the grid), height (3 m) and Side (walls outside the
  line, centred on it or inside it). The walls are generated: hidden, rebuilt when the plan changes (the same brush
  objects, reshaped), never snapped, and a click on one selects the plan. `BrushGenerator` is the base for components
  that make brushes from their own data.
- Icons: a new set in the style of Unity's editor icons (create tools, Vertex / Edge / Face, Select Hidden, Drag
  Rect, Element orientation, the Edit Brush context, the Brushes and Extrude overlays, the Brush component), light and
  dark theme variants at 2x, drawn by `Tools~/icons/generate_icons.py` (Pillow). The overlays no longer show "Br" and
  "Ex" when docked in a toolbar; brushes no longer show the generic script icon in the Hierarchy.
- World presets: Metric is now **Unity** and the default for new projects (listed first); saved settings keep their
  preset. The player reference sizes (player box, crouch height, max step, slope, doorway) are gone from the settings:
  they belong to the character controller. Stairs step by one grid step unless a step height is set.

## 0.2.0 (2026-09-14)

- Rebake: the CSG Group Inspector shows its brush count and a Rebake button that builds the group again from scratch,
  ignoring every cache: into its prefab file for a group in a prefab (asset, instance, or Prefab Mode once saved), in
  its scene otherwise. Assets > CSG Brush > Rebake Prefab does the same for selected prefab assets. Synchronous.
- Generated objects (a group's mesh children, its collider container and pieces, its Convex Colliders component, a
  scene's automatic group) are hidden and not editable from the moment they are built, in scenes, in prefab
  instances and in Prefab Mode, and only Show generated objects reveals them for debugging. Unity keeps no hide flags
  in prefab files, so they are applied in memory wherever the objects appear (hiding an instance's objects is not an
  override). Prefabs used by an opened scene that were saved before baking existed are baked on open; Tools > CSG
  Brush > Bake All Prefabs does it for the whole project.
- CSG groups: `BrushModel` is now `CsgGroup` ("CSG Group", with an icon in the Hierarchy and the Scene view). Every
  scene has its own automatic group for brushes with no group (before, one default model served all loaded scenes, so
  opening scenes together merged their free brushes into one of them and emptied the others). A group inside a prefab
  bakes into the prefab: its meshes are saved as sub-assets whenever the prefab is imported without them, so prefabs
  carry their geometry and can be instantiated at runtime; scene instances keep the prefab's meshes until their
  brushes change, and a baked prefab's mesh is never rewritten from a scene. A prefab without a group is a stamp that
  joins the level it is placed in. The brush Inspector shows the group that bakes it (or the scene) as a clickable
  reference; the group Inspector says how many brushes it bakes. Groups in the open Prefab Mode are built live.
- Default grid material: brushes without a material of their own render with a generated dev texture that is a
  ruler in metres (lines at every grid size of the project, faint for the fine ones, strong at the metre, over a
  checker with a metre period), created once under Assets/CSG Brush with the active render pipeline's lit shader.
  Project Settings > Brushes > Default material swaps it for your own or regenerates it from the grid sizes.
- A piece is its brush, a mesh is its model: every collider piece takes its brush's physics material (new field,
  triggers included, also a handle for sounds), Provide Contacts (new field), tag and static flags, all part of the
  piece identity so a change rebuilds only that brush's pieces; every render mesh child takes its model's tag and
  static flags, the hidden default model's from Project Settings > Brushes (Default model static flags).
- Brush modules: the game's data leaves the package. A brush now only says what its volume is to physics
  (Collision: Solid, Trigger, None); everything a specific character controller cares about is a `BrushModule`
  component on the brush object or a parent (a parent tags all its children). A module's values ride onto every
  collider piece of the brush (`ApplyToPiece`, idempotent, run on every build) and are part of the piece's identity
  (`Fingerprint`), so a change rebuilds exactly the pieces it affects; a module may make the brush a trigger
  (`OverrideCollision`). Trigger pieces carry a relay, and the brush raises one Enter and one Exit per collider
  however many pieces it is made of: `Brush.TriggerEntered` / `TriggerExited`, `IBrushTriggerListener` on the same
  object, or the `BrushTrigger` module with UnityEvents. Project Settings > Brushes and the Brushes overlay list
  the modules every new brush gets. Brushes saved with the old Surface field migrate on the next sync (Trigger and
  No collision into Collision; the game's kinds through `Brush.LegacySurfaceMigration`). `ControllerSurface` and
  `ConvexColliderHooks` are gone; the Quake data lives in `QuakeBrushSurface` in the project.
- Cuts toggle in the Brushes overlay: subtract brushes are drawn as translucent red volumes with their edges, so a
  cut that has carved everything away can be seen and clicked; while it is on, a click picks whatever surface is
  drawn nearest, cut volumes included.
- Layers are CSG groups: brushes are combined per Unity layer inside a model. A subtract brush carves only
  brushes on its own layer, each layer renders as its own mesh child on that layer (camera culling masks apply)
  and its colliders sit on that layer. Set the layer in the Inspector's Layer dropdown like any object; the
  Convex Colliders component no longer has a layer of its own. Smaller unions per layer, and a change on one
  layer leaves the other layers' meshes as they are.
- The Brushes overlay shows the next brush's parameters (operation, surface, sides, steps, arch thickness and
  angle, hollow) while a Create tool is active; they are the Project Settings > Brushes values.
- Arch brush (Unreal's Arch): fills its box like a Box, drawn the same way, standing on the floor; Thickness,
  Angle (180 is a full arch, less keeps the top part) and Segments in the Inspector. Each segment is its own
  convex block, so the mesh is closed by construction and the colliders are one hull per segment.
- Shift-drag the Move gizmo on selected faces to extrude them instead of moving them: the distance along the
  selection's normal is the extrusion (grid-rounded, negative cuts), recomputed from the shape at drag start each
  step, using the Extrude panel's Whole selection / Individual setting; a step that would be refused keeps the last
  valid one. Same boolean as the Extrude button.
- Bridge: select two faces of one brush (same number of corners) and press Bridge in the Brushes overlay; the
  block between them is added as a boolean, the two faces vanish into the join, the walls become new faces and
  stay selected. Corners are paired the way that stretches least. Refused, with the reason in the Extrude panel,
  when the corner counts differ or the result would not be a sound shape. Undoable like an extrusion.
- Extrusion never hands the brush a broken shape. The prism a face is extruded by is grown by a hair at both
  ends and around its outline, so none of its faces ever coincides with a face of the brush (coincident faces
  are where a boolean can leave zero-thickness sheets), and the rebuilt corners snap back to where they belong.
  Faces that only touch at a corner are extruded as separate prisms (one prism sharing that vertex was not a
  manifold and corrupted the boolean). The result is checked before it is applied: if it would not be a sound
  shape (a prism ending exactly where the solid would touch itself, or a cut that removes everything), the
  brush is left as it was and the Extrude panel says why. The user's topology survives: every face carries its
  id through the boolean as Manifold's per-triangle face id (a new native export sets it), solids are no longer
  passed through `AsOriginal` (which re-derives faces by coplanarity and forgets the ids), and no simplification
  pass runs afterwards, so coplanar faces the user keeps apart stay apart, a belt of walls keeps its edges under
  the next extrusion and a split face keeps its other half. A fuzz test over random extrusions on random shapes
  checks that every result is sound and refusals stay rare; a topology test covers the cases above; a test breaks
  a brush on purpose and checks the model still builds and undo restores it.
- Extrusion runs as a boolean through Manifold (union outward, difference inward) and the polyhedron is rebuilt
  from the result with face identities preserved, so it works whatever the block passes through; the Extrude panel
  (settings only) opens with edit mode and the Extrude button in the Brushes overlay applies it. The New Brush panel is gone; a new
  brush's values come from Project Settings > Brushes and are edited on the brush. The soundness check that guards vertex drags uses an exact
  face-piercing test with bounds culling (skipped for convex shapes) instead of the decomposition-volume
  heuristic; a 32-step staircase checks in a few milliseconds.
- Extrude faces in edit mode: an Extrude panel opens with the edit context with Distance (world units,
  negative for a pocket), Whole selection / Individual, and an Extrude button. Group extrusion moves the
  selection along its average normal and walls only its boundary; individual extrusion walls every face. Faces
  keep their indices and stay selected. `BrushPolyhedron.ExtrudeFaces` is covered by tests.
- Handle orientation like ProBuilder's: Global, Local or Element in the Tool Settings toolbar. Element aligns
  the Move, Rotate and Scale gizmos with the selection (blue axis along the face normal; edges and vertices
  use their faces), fixed for the duration of a drag. Push/Pull and its Shift latch are gone: moving along the
  Element gizmo's blue axis is the same operation.
- The concave-edge and convex-cut overlay is gone: the edges are visible in edit mode anyway and the modeller
  handles concave shapes, so there is nothing to warn about.
- Brush edit mode uses the Tool Settings toolbar the way ProBuilder does: Vertex / Edge / Face toggles, Select
  Hidden (elements on faces looking away from the camera are otherwise left alone by clicks and rectangles),
  Drag Rectangle Mode (complete or touching) and Push/Pull in face mode. The Brushes overlay keeps only the Edit
  brush toggle.
- Cylinder, Cone and Sphere are drawn from the centre of their base: press on the centre, drag the radius, then
  the height; a sphere with no height is round, lifting makes an ellipsoid.
- Curved and Spiral Stairs are placed by their column axis: the transform is the axis at floor level (it snaps to
  the grid like a pivot), the gizmo draws the steps and the axis, and the Create tool works radially: press on
  the axis, drag the outer radius (the direction sets where the first step starts), then the height.
- Curved Stairs and Spiral Stairs, with Unreal's parameters (inner radius, step width, step height, angle of
  curve / steps per 360, num steps, add to first step, counter clockwise; spirals also step thickness, sloped
  floor, sloped ceiling). Every step is its own closed block (`Face.group`), so the mesh is closed by construction
  and the colliders are one convex hull per step, no decomposition. Their size follows the parameters; the Create
  tool takes the step width from the drawn footprint and the step height from the drawn height. The existing stairs
  are "Linear Stairs" and their second field is "Step length". New Brush panel and Inspector show the fields.
- Stairs are a closed mesh again: the side and back walls are built as one quad per step level, so no edge has
  a T-junction with its neighbour; steps that reach the box top early are flat. Covered by a test.
- Create tools, the ProBuilder way: one toolbar button with Box, Wedge, Cylinder, Cone, Sphere and Stairs
  variants (also Tools > CSG Brush > Create). Press on a brush face or the ground, drag the base, release, move
  for the height, click; Escape cancels or leaves the tool. Grid-snapped throughout; drawing on a wall builds
  out of the wall with the brush's up along the wall normal. A "New Brush" panel opens with the tool and holds
  the values the next brush is made with (operation, surface, sides, tessellation, step sizes, hollow, wall
  thickness), persisted in the settings; only the fields of the active shape are shown. Toolbar icons are drawn
  in code. Geometry helpers in `BrushDraw` are covered
  by tests.

## 0.1.0 (2026-09-12) — CSG Brush

- Renamed from the Chisel fork to CSG Brush (`digital.dream.csgbrush`, namespace `CsgBrush`); fresh repository.
  The entries below record the fork phase, in which every part of Chisel was replaced.

# Fork changelog

## 0.8.0 (2026-09-12) — Chisel's CSG core replaced by Manifold

- The CSG engine is now the Manifold library (Apache 2.0), loaded as an editor-only native plugin
  (`Manifold/Plugins`, one self-contained shared library per platform; macOS arm64 checked in, Windows x64 and
  Linux x64 built by the CI workflow). Chisel's `Core`, `Components` and `Editor` folders, its vendored
  dependencies, samples and docs are deleted; the package no longer depends on Burst, Entities or Collections.
  What remains of the fork is original work: the brush layer, snapping, the edit tools, the collider builder.
- `BrushCsg` builds every `BrushModel` (new component; a hidden default model is created automatically, a
  user-made one is a folder whose transform moves its part of the level): each brush's solid is cached by
  shape and pose, each additive brush is cut by the later subtract brushes whose bounds overlap it (cached per
  brush), the pieces are unioned, and the mesh is written as one `<[mesh]>` child with a submesh per material,
  flat normals and planar UVs. The union is skipped when the same pieces and materials already produced the
  current mesh. Every build re-derives the brush list, so undo, delete and reorder need no special handling.
- `BrushCsg.UncutBelow` lists the overlapping brushes below a subtract brush, which it does not cut by the
  ordered-CSG rule (what looked like a missed cut on the user's level). An Inspector warning and Scene view
  outlines built on it were tried and removed as distracting.
- Brushes without a material use the active render pipeline's default material (URP or HDRP lit); Unity's
  built-in Default-Material renders pink under URP.
- Large builds run on a worker thread: the editor loop prepares a build on the main thread (brushes, poses,
  keys), computes solids, pieces and the union on a task, and applies mesh and colliders when it lands; a
  drag never waits for the union, and an edit that arrives mid-build is picked up by the next one. Builds
  under 8 ms stay synchronous. Tests and menus use the synchronous path.
- `BrushGeometry` derives a brush's polyhedron for every shape (Sphere and Stairs have their own generators
  now, so every shape can be edited by hand) and its convex parts for the collider builder, which takes
  brush parts instead of Chisel's tree. The drag preview builds the geometry at the snapped pose instead of
  moving hidden children. Brushes have no hidden children any more.
- Scenes saved with the Chisel core are cleaned on open: the old hidden children, generated containers and
  components with missing scripts are removed; the brushes themselves are untouched.
- On the user's 204-brush level every one of 157 overlapping subtract pairs is cut (the Chisel core skipped
  about two thirds of them under one large cutter). Performance probe, drag frame: 10 boxes plus a subtract
  2.6 ms, sphere plus a subtract 4.8 ms, 40 boxes plus a subtract 12 ms; a rebuild with nothing changed 0.5
  to 2 ms.
- Tests: 78 EditMode tests green (undo oracle, snapping, polyhedron, incremental colliders, large subtract,
  Manifold engine), the Quake bridge test and movement validation pass.

## Manifold spike (2026-09-12)

- A large subtract brush in the user's level (185 x 23 x 39, overlapping 140 brushes) left about two thirds of
  the brushes above it uncut. Chisel's tree order matched the hierarchy, and synthetic scenes of up to 160 boxes,
  half-cut, enclosed, touching, moved onto, grown over, could not reproduce it; the failure is inside Chisel's
  per-brush categorisation. New probes: `LargeSubtractTests`, `SceneProbeTests.ProbeSubtractCoverage`.
- Decision: replace Chisel's CSG core with the Manifold library (Apache 2.0, https://github.com/elalish/manifold),
  which guarantees manifold output and is the engine behind OpenSCAD and Godot 4.4. Spike in `Manifold/`:
  Manifold's C API built as one self-contained shared library (`Manifold/native/CMakeLists.txt`, macOS arm64
  binary checked in, CI workflow for macOS arm64, Windows x64 and Linux x64 in `.github/workflows`), a P/Invoke
  layer (`ManifoldNative`), a wrapper that builds solids from polygon faces with the face index as a vertex
  property and reads back triangles with their source solid and face (`ManifoldSolid`), and
  `ManifoldSpikeTests`. On the same level: every one of 146 overlapping pairs is cut; full rebuild of 204 brushes
  61 ms single-threaded (46 ms with the TBB backend), of which the union of the additive brushes is 43 ms; an
  edit near the end with the earlier result cached costs 17 ms; reading the mesh back 1 ms. Chisel's render mesh
  for the same level has 2500 m3 more volume, the material it failed to cut.
- `ConvexColliderBuilder.BrushPolytope` exposes a CSG brush's convex volume for diagnostics and tests.

## 0.7.0-fork (2026-09-06) — hand-edited shapes (phase 2, first slice)

- `BrushShape.Custom`: the brush stores an editable polyhedron (`BrushPolyhedron`: vertices and planar faces
  with stable ids). It may be concave. Convex pieces are derived from it live by a solid-leaf BSP over its own
  face planes (`ConvexDecomposition`): every cut is the extension of an existing face, pieces share cut planes
  exactly, a convex shape yields itself. Each piece is a hidden `<[piece N]>` child holding a Chisel brush mesh,
  so Chisel and the convex colliders only ever see convex brushes.
- Brush edit mode (`BrushEditContext`, an EditorToolContext; "Edit brush" toggle in the overlay and the Inspector),
  the brush-editor bar rather than a mesh editor. While the mode is on, Unity's own Move, Rotate and Scale tools
  (toolbar and W/E/R) act on the selection; leaving the mode gives the GameObject tools back. Vertex, Edge and
  Face modes (1/2/3) sit in the same overlay row, click selects, Shift adds, Ctrl removes, drag a rectangle,
  Esc clears, Ctrl+A selects all, Ctrl+I inverts; Unity's Move gizmo moves the selection snapped to the world grid,
  faces move as a whole, and vertices that land on each other weld (drag a box edge onto another to get a wedge).
  A single selected face also shows an arrow for push and pull along its normal. The first edit turns a Box, Wedge,
  Cylinder or Cone into a Custom shape; "Reset to <shape>" goes back. Drags apply the snapped total offset from the
  shape at drag start. A bent face splits into triangles from the moved vertex so faces stay planar; a drag that
  would open or invert the shape is refused. Rotate snaps to the rotation step and Scale is free; in both the
  resulting vertices are rounded to the grid, so only 90 degree turns of grid shapes are exact and bent faces
  split. No bevel, bridge or loops: those belong to mesh editors, not brush editors.
- Every edit step is validated before it reaches Chisel (`BrushPolyhedron.IsSound`): closed, positive volume,
  planar faces, and the convex parts must fill exactly the shape's volume, which rejects self-intersecting or locally
  inverted results such as a face rotated through the body. A step that fails keeps the last valid shape.
- Convex pieces (and every convex collider) now get their faces from a convex hull of the polytope's vertices with
  coplanar triangles merged, instead of "which vertices lie on this plane": the hull is a closed manifold by
  construction, so slivers no longer produce half-edges without twins (Chisel refused those meshes, faces vanished
  and the collider builder hit invalid brush meshes). Pieces that still fail to close are skipped with a warning
  instead of being handed to Chisel; the collider builder ignores brushes whose mesh Chisel rejected.
- "Edit brush" is disabled unless a Box, Wedge, Cylinder, Cone or Custom brush is selected. The View (hand) tool
  never selects. In Face mode a "Push/Pull" toggle swaps the Move gizmo for one arrow per selected face along its
  normal, so the two no longer compete for the mouse; holding Shift does the same without the toggle (latched for
  the duration of a drag).
- Play mode round trip left the CSG dead: after `[RuntimeInitializeOnLoadMethod]` reset the generator job pools
  they were disposed (which unregisters them) and reallocated but never registered again, so no brush mesh was
  generated until the next domain reload; and the model manager's registered set could miss a live model, so
  mesh updates were dropped silently while colliders kept working. Pools re-register on reallocation, a model is
  found by its tree when missing, and the editor rebuilds the core and the brushes when it returns from play mode.
  Test: `BrushPlayModeTests` enters and leaves play mode and checks moves, resizes and new brushes afterwards.
- Node slot allocator guard: Chisel's section allocator sometimes returns a range that still holds live nodes
  (bookkeeping drifts after moves between hierarchies; it asserted in `CompactHierarchy.CreateNode`, intermittently
  after scene changes). The hierarchy now checks every allocation and takes a fresh range past the end when the
  returned one is occupied, warning once per session. The underlying drift is not fixed, the corruption is.
- Performance (measured headlessly by `BrushPerformanceProbe`): the convex polytope construction intersected every
  triple of planes (cubic in the plane count), so a tessellation-2 sphere cost about 500 ms per collider rebuild
  and the editor froze while editing near it. Vertices now come from clipping a start box with each plane (linear
  in planes times faces), with the start box sized from the planes for precision, on-plane corners kept in the cut
  face, and cut-face points deduplicated (they otherwise multiplied to thousands). Sphere polytope: 480 ms to 7 ms;
  sphere plus subtract collider rebuild: 500 ms to 31 ms. Convex colliders are no longer rebuilt on every drag
  frame: while a handle is held the rebuild is deferred and runs once on release. Remaining drag-frame cost is
  Chisel's incremental CSG (about 10 ms for 10 boxes, 20 ms for 40) plus a full collider rebuild on release
  (5 ms and 17 ms); both grow with the brush count because every touching brush is re-evaluated.
- Incremental convex collider builder. Every build still evaluates the whole CSG tree and compares every piece,
  so nothing stale can survive an undo, a delete or a reorder; only the expensive steps are skipped. The polytope
  of a brush is cached by its mesh content hash, exact tree-space transform and surface data. Each generated
  piece carries the planes it was built from (`ConvexPiece`), and a piece with identical planes and surface
  data is kept instead of recreated, so game adapters see the piece-created hook only for pieces that are new.
  The identity is serialized, so a saved scene reopens without rebuilding. Pieces are named after the brush,
  not its hidden generator child.
- `ConvexPolytope.Clip` cuts the existing faces with the one new plane instead of rebuilding from every plane
  (0.7 ms instead of 8 ms on a 114-plane sphere); subtract and intersect use it. Planes that no longer bound a
  face are dropped, so a box cut by an axial plane stays a BoxCollider. Collider rebuild on drag release,
  measured by `BrushPerformanceProbe`: 10 boxes plus a subtract 5 ms to 1.3 ms, sphere (tessellation 2) plus a
  subtract 31 ms to 3 ms, 40 boxes plus a subtract 17 ms to 5 ms. Drag frames are now Chisel's own CSG update
  (4 to 12 ms in those scenes). Covered by `ConvexColliderIncrementalTests` (incremental result equals a build
  from scratch after move, subtract, undo, redo, delete, undo of delete and a surface change; untouched pieces
  are the same objects) and `ClipOfBuiltPolytopeMatchesAFullBuild`.
- The Cone brush is a real cone with a single apex, built from the fork's own polyhedron through a plain Chisel
  brush (the cylinder generator with a tiny top still rendered a cylinder). Prism footprints fit the size box.
- The surface triangulation job no longer writes to the console for loops it cannot triangulate (zero-area
  slivers and self-touching loops produced by CSG); they are skipped silently, as a geometric condition of the
  model rather than a program error.
- Geometry problems are state, not console errors: Chisel no longer logs "Brush is concave" and friends; the
  brush definition keeps the message (`ErrorMessage`), and the brush layer shows it in the Inspector (red box)
  and in the Scene view (red outline with a label) while leaving the offending convex part out. Every part is
  checked for convexity before it reaches Chisel.
- Convex pieces weld vertices closer than a sixteenth of the grid before the hull is built: nearly parallel
  planes produce vertices a hair apart, and the resulting zero-area faces made Chisel's CSG throw away a
  neighbouring wall (the saved scene where an additive brush vanished next to a dented subtract). Merged hull
  polygons are only merged when truly planar. The decomposition cuts along concave edges first and drops
  numerical slivers below a few cubic units.
- Custom brushes use a pass-through composite with the operation on each piece; the flip happens with no
  children attached. Brushes are rebuilt on scene open. The model no longer runs a synchronous CSG update from
  OnValidate (it destroyed generated objects inside OnValidate, which Unity forbids on scene load).
- The CSG core is reset on scene open and on new scene: its node slot allocator kept stale entries across scene
  loads and the next scene's nodes collided with them (assertion in CompactHierarchy.CreateNode during a
  deep move). Found by the fuzz oracle running several seeds in one editor session.
- Chisel's incremental update can leave a stale surface when a brush is added next to existing geometry (found by
  the fuzz oracle, seed 4: the incremental mesh had more area than a full rebuild). The brush layer now asks for a
  full rebuild of the model after structural changes (create, operation change, reorder;
  `ChiselModelManager.RequestFullUpdate`); drags and shape edits stay incremental. The fuzz oracle runs six seeds
  and fails on Chisel geometry warnings.
- Clicking a brush's surface in the Scene view selects the brush (Shift adds, Ctrl toggles). Unity's own picking hit
  the generated mesh, which belongs to the model, so nothing was selectable by clicking; brushes are now picked by
  their own geometry, additive first. Generated Chisel objects picked by Unity are dropped from the selection.
- Grid rule for Custom shapes: the pivot and every vertex must lie on the world grid (the size-box corner rule made
  no sense for hand-edited shapes and reported them as off grid with no way to fix it). Snapping a Custom brush
  keeps its geometry in place when the pivot moves, bakes the Scale tool into the vertices, and rounds the
  vertices. Merged hull faces drop collinear vertices, which produced zero-area triangles ("Triangulator returned
  zero triangles") after a push.
- Chisel's loop overlap job used fixed-capacity edge lists (base edges plus four per intersecting brush) that
  overflowed inside Burst when many planes cut one polygon, as hand-edited brushes split into parts do; the lists
  now grow.
- Feedback: nothing while convex; when concave the concave edges are orange, the faces where the parts meet are
  dashed, the Inspector and the overlay say "concave, N convex parts". Shown with any tool while the brush is selected.
- Inside-out polyhedra (negative volume) are corrected automatically.
- Tests: box, wedge, prism and cone polyhedra closed and convex; L-shape splits into two parts with exact volume and no
  overlap; a dented box stays closed, planar and decomposable; Custom box renders like the parametric box; push face
  grows the brush and undo returns to the parametric shape; a concave brush gets one convex collider per part; the
  fuzz undo oracle now includes push-face and move-vertex edits (each step also checked against a full rebuild).

## 0.6.0-fork (2026-09-06) — grid enforcement, Chisel UI removed

- World-space grid enforcement (`Brushes/Editor/BrushSnap.cs`), the way Unreal and TrenchBroom do it: snapping
  is absolute, the minimum corner of an axis-aligned brush lands on the grid (the pivot for other rotations),
  sizes are grid multiples, rotation snaps to the rotation step, world scale is forced to one. Applied after every
  transform edit (Undo post-process, with the recorded values rewritten so undo and redo stay on the grid), after
  Inspector edits and by a per-frame poll for changes made by scripts or parents. Parents may organise brushes but
  a rotated or scaled parent is reported by the overlay lint ("Reset" puts it back); "Snap all" fixes off-grid
  brushes. Applying a preset also enables Unity's absolute grid snapping with the Global handle. Project setting
  and overlay toggle "Snap to grid".
- Rotate and Scale handles accumulate from the current value every frame, so snapping during a drag fought the
  hand; snapping now waits for the release (`BrushSnap.DragInProgress`). The Scale tool resizes the brush: on
  release the scale is baked into the size (rounded to the grid) and the world scale returns to one.
- Drag feedback: while a handle is dragged the transform and its wire gizmo follow the hand freely, and the geometry
  is drawn where it will snap (position, rotation and size), so the gap between hand and grid is visible. On release
  the transform snaps onto the geometry.
- Chisel only learned about transform changes made through Undo, so a snapped brush kept its unsnapped geometry.
  The brush layer now tells the CSG nodes about every direct transform change (`BrushSync.NotifyTransformChanged`:
  snapping, scripts, parent moves, the API), and the forced update no longer papers over it with a blanket
  transform refresh, so the tests exercise the same path as the editor loop.
- Generated objects are hidden under every model, not only the default model; a user-created model stays visible as
  a folder while its Chisel components and generated children are hidden unless "Show generated" is on.
- Chisel's own editor UI is removed: scene-view generator tools and overlays, the Chisel windows, node inspectors
  and their menus, UV tools, drag-and-drop materials, hierarchy icons, keyboard defaults, Chisel's grid and snapping
  settings. `Editor/` keeps the editor loop (`ChiselUnityEventsManager`, trimmed), the render-mode hook and the
  project settings inspector. Per-face materials will return through the brush layer.

## 0.5.0-fork (2026-09-06) — brush layer, phase 1

New `Brushes/` (assemblies `CsgBrush`, `CsgBrush.Editor`, tests in
`CsgBrush.Tests`): a `Brush` component (box, wedge, cylinder, cone, sphere, stairs; add or
subtract; controller surface; hollow with wall thickness; sizes in metres centred on the transform) that owns
hidden Chisel generator children and a hidden composite. Placement uses the normal Move/Rotate tools and Unity
snapping; a non-uniform scale shows a warning with an "Apply" button that bakes it into the size. World presets
(Quake 32 u/m, Source, Unreal, metric, custom) live in Project Settings > Brushes; the Scene view overlay shows
the preset and grid, `[` and `]` change the grid and keep Unity's snap and Chisel's grid in sync. GameObject >
Brush menu; Chisel components hidden from Add Component; generated objects hidden unless "Show generated".
Boxes drawn with Chisel's own tools are adopted into brushes.

Undo rule: only the Brush fields, its transform and the brush GameObject are undo state. The composite, the
hidden generators, the generated meshes, the default model and the convex colliders are derived and are rebuilt
without touching the undo stack (the convex collider builder and its auto-add no longer use Undo either).

EditMode tests (27, all passing headlessly): shape placement, hollow rooms, subtract, resize/move/rotate/delete/
reorder regeneration, undo/redo of moves, rotations, sibling order and of edits made before a delete, a scripted
20-step undo/redo oracle and a 60-step fuzz oracle (three seeds) where every step is also checked against a full
Chisel rebuild.

Core fixes found by those tests (all marked "Fork fix" in the source):

- Deleting a brush that touched nothing left it in the generated mesh: the update skipped mesh assembly when no
  remaining brush was flagged. A changed brush set now schedules a full rebuild of that tree on the next update.
- Brushes inside generated branches (extruded shapes, stairs, ...) stopped following their parents' transform
  after the first update (`FlagTransformationChangedDeep` skipped already-flagged subtrees and branch flags were
  never cleared).
- Generated brushes assigned by a job after the update list was made now flag their tree (`SetState`); the same
  method compared transformations with `=` instead of `!=`.
- A generator whose model tree does not exist yet no longer creates its node in the internal default hierarchy
  (it would never render); a tree updated before its model registered is re-flagged once.
- Reordering brushes at the scene root (or under plain GameObjects) never reached the CSG order: root nodes were
  not watched (`nonNodeChildren`), `UpdateSiblingIndices` returned early for them, the sibling index queue was
  cleared before it was read, and the child sort skipped every list with more than one item.
- Undo history hygiene: the default model is created and destroyed without Undo, generated containers are
  destroyed without Undo, and the empty default model is kept alive (`KeepEmptyDefaultModel`) because recreating a
  scene root shifts sibling indices and breaks Undo of reordering.
- Unity 6: writing `GridSettings.size` through reflection threw on every grid change (no setter any more).
- Robustness: a cached intersection with a removed brush no longer throws in `FixupBrushCacheIndicesJob`; a
  zero-triangle loop in `GenerateSurfaceTrianglesJob` is a warning, not an error.
- `ChiselBoundsUtility` is public. Diagnostics flags `CompactHierarchyManager.LogTreeUpdates` and
  `ChiselModelManager.LogFinishMeshUpdates` (off by default).

## 0.4.0-fork (2026-09-06)

- Compiles on Unity 6000.6 (EntityId, SceneHandle, Collections 6, hierarchy and selection APIs).
- Dependencies BurstTriangulator and CustomAssetMetadata vendored (git URL installs do not fetch submodules).
- Runtime errors fixed: RuntimeInitializeOnLoadMethod in generic generators; degenerate (zero-size) brushes no longer enter the CSG update.
- Rendering fixed under URP assigned per quality level; full meshes are no longer force-hidden in the game view, play mode and builds.
- New `ConvexColliders/`: one convex collider per brush piece from brush-level CSG (subtract splits along the cutter's planes), brush surface tags (solid, slick, water, trigger, no collision), auto-added to every model, Chisel's concave collider and its settings are disabled for those models. Hook `ConvexColliderHooks.PieceCreated` for game-specific components. New `ModelSettings.GenerateColliders`: the core generates no collision mesh at all for such models (upstream always used `MeshQuery.DefaultQueries` and ignored the Collidable toggle).
- Diagnostics menu: Smoke Test, Diagnose Rendering.

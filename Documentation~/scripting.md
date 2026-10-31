# Build levels from scripts

Everything you can do to a brush in the Scene view, you can also do from an editor script with the `CsgBrush.Editor.BrushApi` class. Every call is a step you can undo. This page covers building a level from measured numbers, rebuilding it, and keeping your own changes when a script builds the level again.

## Create brushes

To make a box between two corners, use `BrushApi.CreateBox`:

```csharp
using CsgBrush;
using CsgBrush.Editor;

var floor = BrushApi.CreateBox(new Vector3(-10f, -1f, -10f), new Vector3(10f, 0f, 10f), name: "Floor");
var doorway = BrushApi.CreateBox(new Vector3(-1f, 0f, 9f), new Vector3(1f, 2.2f, 10f),
    name: "Doorway", operation: BrushOperation.Subtract, material: frameMaterial);
```

A box made with `CreateBox` stays exactly where you put it, with its **Grid snap** set to **Off**, even when **Snap brushes to grid** is on in **Project Settings** > **Brushes**. A 4.4 m gap stays 4.4 m. To land a brush on the grid instead, pass `snap: true`.

For other shapes, use `BrushApi.Create` with a shape, a centre, a size, and a rotation. It snaps to the grid by default, like a brush drawn in the Scene view; pass `snap: false` to place it exactly.

## Subtract order

A subtract brush cuts only the brushes before it in the Hierarchy, and only on its own layer. Within a Brush Group, the Hierarchy order is the order of the brushes, depth first, so the order you create brushes in is the order they combine in. Create the solids first, then the brushes that cut them.

To decide what material the faces of a cut take, use `BrushApi.SetCutFaces`: `CutFaces.CutterMaterial` (the default) gives them the subtract brush's material, and `CutFaces.CutSolidMaterial` gives them the material of the solid they're cut into.

## Build many brushes at once

To build a level, wrap the calls in a batch:

```csharp
BrushBatch batch;
using (batch = BrushApi.Batch("Build the castle"))
{
    // create brushes here
}
Debug.Log(batch.Report);
```

A batch makes all its brushes one step to undo, and rebuilds the meshes and colliders once when it ends. Batches can be nested; only the outermost one rebuilds.

To rebuild without a batch, call `BrushApi.ForceUpdate()`. Both give a `RebuildReport`: the number of brushes, groups, triangles, box colliders, and convex mesh colliders, how long the rebuild took, and the collider pieces that are convex meshes.

## Check the colliders

Each collider piece is a **Box Collider** when it's an axis-aligned box, and a convex **Mesh Collider** otherwise, such as the slope of a wedge. Physics treats boxes more exactly: a character walking along the edge of a convex mesh can be pushed off it. To see which pieces are convex meshes, go to **Tools** > **CSG Brush** > **Select Convex Mesh Colliders**. The Console shows the counts, and the pieces are selected.

## Rebuild generated brushes and keep your own

When a script builds part of a level, run it again with `BrushApi.Regenerate`. It replaces only what the script made:

```csharp
BrushApi.Regenerate(levelRoot, "Generated", container =>
{
    BrushApi.CreateBox(new Vector3(-5f, 0f, 0f), new Vector3(5f, 3f, 1f), container, "Wall");
});
```

`Regenerate` finds the child named `Generated` of `levelRoot`, or creates it with a **Generated Brushes** component, empties it, and calls your code to fill it. Then it rebuilds the level once.

Brushes you add beside the **Generated Brushes** object are kept when the script runs again. The generated container always stays first among its siblings, so a subtract brush you add after it, such as a window, still cuts what the script generated. Don't put your own brushes inside the container: the next run replaces everything there.

## Build something that moves

To build a lift or a drawbridge from brushes, create its own moving group and put the brushes under it:

```csharp
var lift = BrushApi.CreateGroup("Lift", parent: levelRoot, moving: true);
BrushApi.CreateBox(new Vector3(-1.5f, -0.3f, -1.5f), new Vector3(1.5f, 0f, 1.5f), lift.transform, "Platform");
```

The group's brushes aren't static, and moving the group's transform moves its meshes and colliders. CSG Brush builds in the editor only, so move the group, not its brushes. For more, see [Moving groups](brush-group-rendering.md#moving-groups).

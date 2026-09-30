# About CSG Brush

CSG Brush is a level editor for building game levels from convex brushes. You add and subtract brushes on a world grid, and the package combines them into render meshes and convex colliders.

This documentation covers the following features:

| **Feature** | **Description** |
| :--- | :--- |
| [Floor plans](floor-plans.md) | Draw an outline on the floor and generate walls of one thickness and height. |
| [Floor plan edit mode](floor-plan-edit-mode.md) | Select, move, rotate, scale, add, and delete the points and walls of a floor plan. |
| [Doors and windows](doors-and-windows.md) | Cut openings of a set size into walls with one click. |
| [Wall Anchor](wall-anchor.md) | Keep doors, windows, props, and brushes on a wall when the wall changes. |
| [Brush Group rendering and lightmapping](brush-group-rendering.md) | Control static flags, renderer settings, and lightmap UVs for brush geometry. |

## Terminology

| **Term** | **Meaning** |
| :--- | :--- |
| **Build** | Combine brushes into render meshes and colliders. CSG Brush builds automatically when you change a brush. The **Rebuild** button builds a Brush Group again from scratch. |
| **Bake** | Unity's precomputed data, such as lightmaps, navigation meshes, and occlusion culling. CSG Brush doesn't use this word for its own output. |
| **Generated brush** | A brush that a component creates from its own data, such as the walls of a floor plan. Generated brushes are hidden, and you edit the component instead. |

## Requirements

This version of CSG Brush is compatible with Unity 6000.6 and later.

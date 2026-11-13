# Splines

A **Spline** sweeps a profile along a path: pipes, beams, rails, gutters, and tunnels. Points are joined by links like the walls of a [floor plan](floor-plans.md), but in 3D, and a point can join any number of links, so a spline can branch. Smooth points curve the path.

The pieces are generated brushes. They're hidden in the Hierarchy, and when you click one in the Scene view, Unity selects the Spline. When you change the path or the Spline properties, the pieces update.

## Draw a spline

To draw a spline:

1. Select **GameObject** > **Brush** > **Spline**. This creates a Spline at the Scene view pivot and starts drawing.
2. Click to place each point. Each click adds a link from the previous point.
    * On a brush, the point lands on its surface, raised so that the profile rests on it.
    * In the open, the point lands level with the previous point.
3. Click an existing point or link to join the stroke to it. Clicking a link splits it there. Joining ends the stroke.
4. Press **Esc** or double-click to end a stroke, and **Enter** or **Esc** again to stop drawing.

While you draw:

* Points snap to the grid across the surface they land on.
* Press **Backspace** to remove the last link of the stroke.

To continue drawing a Spline, select it and click **Draw** in the Inspector. To branch, click a point or the middle of a link, then click where the branch goes.

## Edit a spline

Click **Edit** in the Spline Inspector. Spline edit mode works like [floor plan edit mode](floor-plan-edit-mode.md): select points (**1**) or links (**2**), and use **Move**, **Rotate**, and **Scale** on the selection. **Move** also has an up arrow, because spline points can be at any height. Points snap to the grid.

To curve the path, select points and press **S**, or click **Smooth**. Smooth points are round marks, corners are square. See [Curve walls](floor-plan-edit-mode.md#curve-walls) for how smooth points and the curve tolerance work.

## Branches, corners, and ends

| **Where** | **Result** |
| :--- | :--- |
| A corner | The pieces meet at a mitre, like the corner of a picture frame. A corner sharper than 120 degrees is filled by overlapping the two sides instead. |
| A smooth point where three or more links meet | The two links that are most nearly opposite run through it in one piece. The other links end there. |
| A corner where three or more links meet | Every link ends there. |
| A junction of hollow links | The insides are cut after all the solid pieces, so the branches open into each other. |
| An end of a hollow spline | Open, or capped by the wall thickness, as set by **Ends**. |

## Spline component reference

| **Property** | **Description** |
| :--- | :--- |
| **Profile** | The shape swept along the path.<br/>&#8226; **Circle**: A round tube.<br/>&#8226; **Rectangle**: A box section.<br/>&#8226; **U Channel**: An open channel facing up. |
| **Size** | The width and height of the profile, in metres. A circle's diameter is the width. |
| **Sides** | The number of sides of a circle. **0** works them out from the curve tolerance, with at least 6. Visible for **Circle**. |
| **Hollow** | Cuts out the inside, leaving a wall of the thickness below. Not available for **U Channel**, which is open already. |
| **Thickness** | The wall of a hollow profile or a channel, in metres. |
| **Ends** | Whether a hollow spline is open or capped where the path ends. |
| **Orientation** | How the profile turns along the path.<br/>&#8226; **Follow**: The profile turns with the path without twisting. This is the default.<br/>&#8226; **Upright**: The top of the profile stays up. |
| **Material** | The material of every piece. When empty, pieces use the project's default material. |
| **Override tolerance** | Uses its own curve tolerance instead of the project's **Curve tolerance** (**Project Settings** > **Brushes**). |
| **Tolerance (m)** | How far the straight pieces of its curves may stray from the true curve. Visible when **Override tolerance** is enabled. |
| **Path** | The number of points and links. This value is read-only. |
| **Edit** | Enters spline edit mode. |
| **Draw** | Draws more links. |

If the path bends more tightly than half the profile's width, the Inspector warns that the curve is tighter than the profile: the inside of the bend folds over itself.

The pieces take the layer, tag, and static flags of the Spline GameObject.

## Additional resources

* [Floor plans](floor-plans.md)
* [Edit a floor plan](floor-plan-edit-mode.md)

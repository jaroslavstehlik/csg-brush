# Splines

A **Spline** sweeps a profile along a path: pipes, beams, rails, gutters, and tunnels. Points are joined by links like the walls of a [floor plan](floor-plans.md), but in 3D, and a point can join any number of links, so a spline can branch. Smooth points curve the path.

The pieces are generated brushes. They're hidden in the Hierarchy, and when you click one in the Scene view, Unity selects the Spline. When you change the path or the Spline properties, the pieces update.

## Draw a spline

To draw a spline:

1. In the Scene view, open the **Create** tool dropdown and select **Spline**, then click where the spline starts. On a brush, it starts on the surface; elsewhere on the ground. This creates a Spline GameObject and starts drawing. If you click a point or link of an existing Spline instead, drawing continues that spline from there.

    You can also select **GameObject** > **Brush** > **Spline**, which creates a Spline at the Scene view pivot and starts drawing.

2. Click to place each following point. Each click adds a link from the previous point.
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

To turn the profile about the path, select points and use the **Rotate** tool: drag the ring around the path's direction. Rotation snaps to the rotation snap angle. You can also type the angle in **Roll** in the Inspector, under **Selected points**. Between two points, the roll changes evenly along the path. Use it to open a U Channel to one side, or to bank a road.

To curve the path, select points and press **S**, or click **Smooth**. Smooth points are round marks, corners are square. See [Curve walls](floor-plan-edit-mode.md#curve-walls) for how smooth points and the curve tolerance work.

## Branches, corners, and ends

| **Where** | **Result** |
| :--- | :--- |
| A corner | The pieces meet at a mitre, like the corner of a picture frame: each piece is its profile cut by the planes halving the turns at its ends. A corner sharper than 120 degrees is filled by overlapping the two sides instead, as is an **Upright** corner that climbs or falls while it turns (keeping both sides level would twist one of them). Make such a corner Smooth for a clean curve. |
| A smooth point where three or more links meet | The two links that are most nearly opposite run through it in one piece. The other links end there. |
| A corner where three or more links meet | Every link ends there. |
| A junction of hollow links or channels | The insides are cut after all the solid pieces, so the branches open into each other. A branch ends flush with the far side of what it joins. |
| An end of a hollow spline or a channel | Open, or capped by the wall thickness, as set by **Ends**. |

## Spline component reference

| **Property** | **Description** |
| :--- | :--- |
| **Profile** | The shape swept along the path.<br/>&#8226; **Circle**: A round tube.<br/>&#8226; **Rectangle**: A box section.<br/>&#8226; **U Channel**: An open channel facing up. |
| **Size** | The width and height of the profile, in metres. A circle's diameter is the width. |
| **Sides** | The number of sides of a circle. **0** works them out from the curve tolerance, with at least 6. Visible for **Circle**. |
| **Hollow** | Cuts out the inside, leaving a wall of the thickness below. Not available for **U Channel**, which always has its channel cut out. |
| **Thickness** | The wall of a hollow profile or a channel, in metres. |
| **Ends** | Whether a hollow spline or a channel is open or capped where the path ends. |
| **Orientation** | How the profile turns along the path.<br/>&#8226; **Follow**: The profile turns with the path without twisting. This is the default.<br/>&#8226; **Upright**: The top of the profile stays up. |
| **Material** | The material of every piece. When empty, pieces use the project's default material. |
| **Override tolerance** | Uses its own curve tolerance instead of the project's **Curve tolerance** (**Project Settings** > **Brushes**). |
| **Tolerance (m)** | How far the straight pieces of its curves may stray from the true curve. Visible when **Override tolerance** is enabled. |
| **Path** | The number of points and links. This value is read-only. |
| **Edit** | Enters spline edit mode. |
| **Draw** | Draws more links. |

If the path bends more tightly than half the profile's width, the Inspector warns that the curve is tighter than the profile: the inside of the bend folds over itself.

The pieces take the layer, tag, and static flags of the Spline GameObject.

## Attach objects to a spline

An object attached to a spline stays on it when the path changes: at its distance along its link, at its angle around the profile, and at its offset from the surface. It stands on the surface: its up points out of the surface and its forward runs along the path.

To attach objects, select them and select **GameObject** > **Brush** > **Attach to Spline**. Each object goes onto the surface of the nearest spline where it's nearest, and becomes the spline's child. To take an object off, select **GameObject** > **Brush** > **Detach from Spline**. It stays where it is.

When you move an attached object, it takes the nearest place on the spline, keeping how far out from the surface you put it and how you turned it. Pull it more than 0.3 m further out and it comes off the spline. It stays where you put it and no longer follows the path. If its link is deleted, it stays where it was.

### Spline Anchor component reference

| **Property** | **Description** |
| :--- | :--- |
| **Distance** | Metres along the link from its first point. |
| **Angle** | Degrees around the profile: **0** is the top, **90** the right side looking along the path, **180** underneath. |
| **Offset** | Metres from the profile's surface to the object's pivot, out of the surface. |
| **Upright** | Keeps the profile's up instead of standing out of the surface. Use it for posts and lamps on the edge of a road. |

## Repeat objects along a spline

A **Spline Repeat** places a copy of an object every few metres along a spline: sleepers on a rail, posts along a fence, brackets under a pipe, lamps along a road. Each run of the path, from an end or a junction to the next, gets its own row.

To repeat an object:

1. Select the Spline and add **Spline Repeat** (**Add Component** > **CSG Brush** > **Spline Repeat**). A Spline can have several, one per kind of object.
2. Set **Source** to a prefab, or to an object in the scene. To keep a scene object out of the level, disable it: the copies are enabled.
3. Set **Spacing**, and the place on the profile with **Angle**, **Offset**, and **Upright**.

The copies are generated objects. They're hidden in the Hierarchy, and clicking one selects the Spline. They're made again when the path or the Spline Repeat changes. To change the copies, edit the source.

### Spline Repeat component reference

| **Property** | **Description** |
| :--- | :--- |
| **Source** | The object or prefab to copy. |
| **Spacing** | Metres between copies. |
| **Start** | Metres from the start of each run to the first copy. |
| **Angle** | Degrees around the profile: **0** is the top, **90** the right side looking along the path, **180** underneath. |
| **Offset** | Metres from the profile's surface to each copy's pivot, out of the surface. |
| **Upright** | Keeps the profile's up instead of standing out of the surface. |
| **Rotation** | Each copy's turn in degrees, relative to standing on the surface facing along the path. |

## Additional resources

* [Floor plans](floor-plans.md)
* [Edit a floor plan](floor-plan-edit-mode.md)

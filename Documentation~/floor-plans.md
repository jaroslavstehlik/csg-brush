# Floor plans

A **Floor Plan** is an outline on the floor that generates walls. Each segment of the outline becomes one wall brush. All walls share a thickness and height, and they meet at mitred corners.

![A closed floor plan with six walls. The outline is drawn in amber on the floor.](images/floor-plan-room.png)

The walls are generated brushes. They're hidden in the Hierarchy, and when you click a wall in the Scene view, Unity selects the Floor Plan. When you change the outline or the Floor Plan properties, the walls update.

## Draw a floor plan

To draw a floor plan:

1. In the Scene view, open the **Create** tool dropdown and select **Floor Plan**.

    ![The Create tool dropdown with Door Brush, Window Brush, and Floor Plan at the end of the list.](images/create-tool-dropdown.png)

2. Click on the floor where the first corner goes. This creates a Floor Plan GameObject and starts drawing.
3. Click to place each following corner. Each click adds a wall from the previous corner.

    ![Drawing a floor plan. A dotted line and its length show the next wall.](images/floor-plan-draw.png)

4. To close the room, click the first corner. To leave the outline open, press **Enter**, press **Esc**, or double-click.

You can also create a Floor Plan from the menu: **GameObject** > **Brush** > **Floor Plan** creates one at the Scene view pivot.

While you draw:

* Corners snap to the grid and to 45-degree angles from the previous corner.
* Hold **Shift** to place a corner on any grid point.
* Press **Backspace** to remove the last corner.

To continue drawing an existing Floor Plan, select it and click **Draw** in the Inspector.

## Floor Plan component reference

![The Floor Plan component in the Inspector.](images/floor-plan-inspector.png)

| **Property** | **Description** |
| :--- | :--- |
| **Closed** | Joins the last corner to the first, which makes a room. |
| **Wall Thickness** | The thickness of every wall, in metres. This value doesn't snap to the grid. |
| **Wall Height** | The height of every wall, in metres. |
| **Side** | Where the walls stand relative to the drawn line:<br/>&#8226; **Outside**: The walls stand outside the outline, so the line is the inner face of the room. This is the default.<br/>&#8226; **Centered**: The walls are centred on the line.<br/>&#8226; **Inside**: The walls stand inside the outline. |
| **Floor** | Generates a floor slab under a closed room. Enabled by default. |
| **Floor Thickness** | The thickness of the floor, in metres, measured down from the bottom of the walls. |
| **Ceiling** | Generates a ceiling slab on top of a closed room. Disabled by default. |
| **Ceiling Thickness** | The thickness of the ceiling, in metres, measured up from the top of the walls. |
| **Points** | The number of corners. This value is read-only. |
| **Edit** | Enters [floor plan edit mode](floor-plan-edit-mode.md). |
| **Draw** | Continues drawing the outline from its last corner. |

The walls, floor, and ceiling take the layer, tag, and static flags of the Floor Plan GameObject.

## Floor and ceiling

A closed Floor Plan can generate a floor and a ceiling. Each one is a slab that covers the room and the walls around it, so the room is sealed whatever the **Side** setting is. The slabs follow the outline, including concave shapes such as an L-shaped room, and update when you edit the Floor Plan.

![An L-shaped floor plan with a ceiling, a door, and a window.](images/floor-plan-floor-ceiling.png)

An open Floor Plan has no inside, so it doesn't generate a floor or a ceiling.

> [!NOTE]
> A ceiling hides the room from above in the Scene view. To work inside the room, disable **Ceiling** while you edit, or look in from the side.

## Additional resources

* [Edit a floor plan](floor-plan-edit-mode.md)
* [Doors and windows](doors-and-windows.md)

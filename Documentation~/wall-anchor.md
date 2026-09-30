# Attach objects to walls

A **Wall Anchor** keeps an object on a wall of a Floor Plan. When the wall moves, turns, gets thicker, or is split, the object follows. Doors and windows get a Wall Anchor automatically. You can also attach any other GameObject, such as a prefab, a light, or a brush.

![A door on a floor plan wall. The yellow outline shows the wall the door is attached to.](images/wall-anchor-outline.png)

An anchored object is a child of its Floor Plan. When you select it, the Scene view outlines its wall in yellow.

![A Floor Plan with a door and two windows as children in the Hierarchy.](images/floor-plan-hierarchy.png)

## Attach an object

To attach an object to a wall, do one of the following:

* Select the object and choose **GameObject** > **Brush** > **Attach to Wall**. The object moves under the nearest Floor Plan and attaches to its nearest wall. It keeps its current position and rotation.
* Draw a brush on the face of a Floor Plan wall with the **Create** tool.
* Add a **Wall Anchor** component to a child of a Floor Plan.

To detach an object, select it and choose **GameObject** > **Brush** > **Detach from Wall**. The object leaves the Floor Plan and stays where it is.

## Move an object to another wall

To move an anchored object to another wall:

1. In the Wall Anchor Inspector, click **Pick Wall**.
2. In the Scene view, move the pointer over walls. The wall under the pointer is outlined in white.
3. Click a wall. The object moves onto it and keeps its distance along the wall, height, and rotation.

Press **Esc** to cancel. The wall can belong to another Floor Plan. The object then moves under that Floor Plan.

You can also move an anchored object with the **Move** tool. It slides along its wall, or attaches to another wall when you drop it near one. If you drag it far from every wall, it's freed and stays where you put it.

## What happens when a wall changes

| **Change** | **Result** |
| :--- | :--- |
| The wall moves or turns | The object moves with the wall and keeps its pose relative to the wall face. |
| The wall gets thicker, or the Floor Plan **Side** changes | The object stays against the wall face. A door or window stays cut all the way through. |
| A corner is added in the wall | The object stays where it was, on the part of the wall it's on. |
| The wall is deleted | The object stays where it was and is no longer attached. |

## Wall Anchor component reference

![The Wall Anchor component in the Inspector.](images/wall-anchor-inspector.png)

| **Property** | **Description** |
| :--- | :--- |
| **Pick Wall** | Click, then click a wall in the Scene view to attach the object to it. |
| **Distance** | The distance along the wall from its first corner, in metres. |
| **Height** | The height above the floor, in metres. For a door or window, this is the height of its bottom edge. For other objects, it's the height of the pivot. |
| **Face** | The side of the wall the object is on: **Inside** or **Outside**. Doors and windows don't have this property, because they always cut through the wall. |

Brushes that an anchored object carries don't snap to the grid, so they stay aligned with walls at any angle.

## Additional resources

* [Doors and windows](doors-and-windows.md)
* [Floor plans](floor-plans.md)

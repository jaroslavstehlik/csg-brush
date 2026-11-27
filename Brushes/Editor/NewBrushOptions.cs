using System;
using System.Collections.Generic;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// What a new brush starts with: its operation, modules, hollow, sides, stairs and opening sizes. The project keeps one
    /// set as its defaults (Project Settings > Brushes); the Create tools work on a copy the Brushes overlay changes.
    /// </summary>
    [Serializable]
    public sealed class NewBrushOptions
    {
        public BrushOperation newOperation = BrushOperation.Add;
        [Tooltip("Full type names of the modules (a game's data) every new brush gets.")] public List<string> newModules = new List<string>();
        [Min(3)] public int newSides = 16;
        [Range(1, 5)] public int newTessellation = 2;
        [Tooltip("Units; 0 uses 0.25 m, a comfortable step for a 1.5 to 2 m character.")] public float newStepHeight = 0f;
        public bool newHollow = false;
        [Tooltip("Units; 0 uses one grid step.")] public float newInnerRadius = 0f;
        [Tooltip("Units; 0 uses 0.1 m, a thin tread.")] public float newStepThickness = 0f;
        public float newCurveAngle = 90f;
        [Min(1)] public int newStepsPer360 = 16;
        public bool newCounterClockwise = false;
        public bool newSlopedFloor = false;
        public bool newSlopedCeiling = false;
        [Tooltip("Linear and curved stairs: the steps stand on solid support down to the floor.")] public bool newSupportUnderSteps = true;
        [Tooltip("What of a new brush goes onto the grid: its outermost vertices (Shape), or its pivot.")] public GridSnap newGridSnap = GridSnap.Shape;
        [Tooltip("Units; 0 uses one grid step.")] public float newWallThickness = 0f;
        [Range(1f, 180f)] public float newArchAngle = 180f;
        [Tooltip("Units; 0 uses 1 m.")] public float newDoorWidth = 0f;
        [Tooltip("Units; 0 uses 2.2 m.")] public float newDoorHeight = 0f;
        [Tooltip("Units; 0 uses 1.2 m.")] public float newWindowWidth = 0f;
        [Tooltip("Units; 0 uses 1.2 m.")] public float newWindowHeight = 0f;
        [Tooltip("Units above the floor; 0 uses 0.9 m.")] public float newWindowSill = 0f;

        /// <summary>A copy to change without touching this one.</summary>
        public NewBrushOptions Clone() => JsonUtility.FromJson<NewBrushOptions>(JsonUtility.ToJson(this));
    }
}

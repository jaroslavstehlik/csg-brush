using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Project-wide world preset: units per metre, grid, defaults and the reference sizes used by the lint.
    /// Geometry is always stored in metres; the preset only changes how sizes are displayed and snapped.
    /// </summary>
    [FilePath("ProjectSettings/BrushSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class BrushSettings : ScriptableSingleton<BrushSettings>
    {
        public enum Preset { Quake, Source, Unreal, Metric, Custom }

        public Preset preset = Preset.Quake;
        public float unitsPerMeter = 32f;
        public string unitLabel = "u";
        public float[] gridSizes = { 1, 2, 4, 8, 16, 32, 64, 128, 256 };
        public int gridIndex = 4;
        public Vector3 defaultBoxSize = new Vector3(64f, 64f, 64f);
        public float rotationSnapDegrees = 15f;
        [Header("Reference sizes (units), used by the lint")]
        public Vector3 playerBox = new Vector3(30f, 56f, 30f);
        public float crouchHeight = 40f;
        public float maxStep = 18f;
        public float maxSlopeDegrees = 45.6f;
        public Vector2 doorwayMinimum = new Vector2(32f, 56f);
        [Header("New brushes (the Create tools)")]
        public BrushOperation newOperation = BrushOperation.Add;
        [Tooltip("Full type names of the modules (a game's data) every new brush gets.")] public List<string> newModules = new List<string>();
        [Min(3)] public int newSides = 16;
        [Range(1, 5)] public int newTessellation = 2;
        [Tooltip("Units; 0 uses the grid-sized default.")] public float newStepHeight = 0f;
        [Tooltip("Units; 0 uses the grid-sized default.")] public float newStepDepth = 0f;
        public bool newHollow = false;
        [Tooltip("Units; 0 uses one grid step.")] public float newInnerRadius = 0f;
        [Tooltip("Units; 0 uses half a grid step.")] public float newStepThickness = 0f;
        public float newCurveAngle = 90f;
        [Min(1)] public int newNumSteps = 8;
        [Min(1)] public int newStepsPer360 = 16;
        public bool newCounterClockwise = false;
        public bool newSlopedFloor = false;
        public bool newSlopedCeiling = false;
        [Tooltip("Units; 0 uses one grid step.")] public float newWallThickness = 0f;
        [Range(1f, 180f)] public float newArchAngle = 180f;
        [Header("Extrude (edit mode)")]
        [Tooltip("Units; 0 uses one grid step.")] public float extrudeDistance = 0f;
        public bool extrudeIndividual = false;
        [Tooltip("Static flags of the hidden default model; its render meshes inherit them. A model you make has its own.")]
        public StaticEditorFlags defaultModelStaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic | StaticEditorFlags.ReflectionProbeStatic;
        [Header("Editor")]
        public bool showGenerated = false;
        [Tooltip("Draw subtract brushes as translucent red volumes in the Scene view, so they can be seen and selected where they have carved everything away.")]
        public bool showCuts = false;
        [Tooltip("Keep every brush on the grid: position, size and rotation are snapped in world space after each edit, whatever the parent does.")]
        public bool snapToGrid = true;

        public static event Action Changed;

        public float GridUnits => gridSizes != null && gridSizes.Length > 0 ? gridSizes[Mathf.Clamp(gridIndex, 0, gridSizes.Length - 1)] : 1f;
        public float GridMeters => ToMeters(GridUnits);

        public float ToUnits(float meters) => meters * unitsPerMeter;
        public float ToMeters(float units) => units / unitsPerMeter;
        public Vector3 ToUnits(Vector3 meters) => meters * unitsPerMeter;
        public Vector3 ToMeters(Vector3 units) => units / unitsPerMeter;

        public string FormatUnits(float meters) => ToUnits(meters).ToString("0.##") + " " + unitLabel;

        public void ApplyPreset(Preset p)
        {
            preset = p;
            switch (p)
            {
                case Preset.Quake:
                    unitsPerMeter = 32f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    playerBox = new Vector3(30f, 56f, 30f); crouchHeight = 40f; maxStep = 18f; maxSlopeDegrees = 45.6f; doorwayMinimum = new Vector2(32f, 56f);
                    break;
                case Preset.Source:
                    unitsPerMeter = 39.37f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    playerBox = new Vector3(32f, 72f, 32f); crouchHeight = 36f; maxStep = 18f; maxSlopeDegrees = 45.6f; doorwayMinimum = new Vector2(33f, 73f);
                    break;
                case Preset.Unreal:
                    unitsPerMeter = 100f; unitLabel = "cm";
                    gridSizes = new float[] { 1, 5, 10, 25, 50, 100, 200, 500 }; gridIndex = 3;
                    defaultBoxSize = new Vector3(200f, 200f, 200f); rotationSnapDegrees = 15f;
                    playerBox = new Vector3(68f, 176f, 68f); crouchHeight = 88f; maxStep = 45f; maxSlopeDegrees = 44f; doorwayMinimum = new Vector2(70f, 180f);
                    break;
                case Preset.Metric:
                    unitsPerMeter = 1f; unitLabel = "m";
                    gridSizes = new float[] { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f }; gridIndex = 2;
                    defaultBoxSize = new Vector3(2f, 2f, 2f); rotationSnapDegrees = 15f;
                    playerBox = new Vector3(0.9f, 1.8f, 0.9f); crouchHeight = 1.2f; maxStep = 0.5f; maxSlopeDegrees = 45f; doorwayMinimum = new Vector2(1f, 2f);
                    break;
                case Preset.Custom:
                    break;
            }
            NotifyChanged();
        }

        public void SetGridIndex(int index)
        {
            gridIndex = Mathf.Clamp(index, 0, gridSizes.Length - 1);
            NotifyChanged();
        }

        public void NotifyChanged()
        {
            Save(true);
            Changed?.Invoke();
        }
    }

    static class BrushSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            var provider = new SettingsProvider("Project/Brushes", SettingsScope.Project)
            {
                label = "Brushes",
                keywords = new HashSet<string>(new[] { "brush", "grid", "units", "quake", "preset", "level" }),
                guiHandler = _ =>
                {
                    var s = BrushSettings.instance;
                    EditorGUI.BeginChangeCheck();
                    var preset = (BrushSettings.Preset)EditorGUILayout.EnumPopup("World preset", s.preset);
                    if (preset != s.preset)
                    {
                        s.ApplyPreset(preset);
                        GUI.changed = false;
                        return;
                    }
                    using (new EditorGUI.DisabledScope(s.preset != BrushSettings.Preset.Custom))
                    {
                        s.unitsPerMeter = EditorGUILayout.FloatField("Units per metre", s.unitsPerMeter);
                        s.unitLabel = EditorGUILayout.TextField("Unit label", s.unitLabel);
                        var gridText = string.Join(" ", Array.ConvertAll(s.gridSizes, g => g.ToString("0.###")));
                        var newGridText = EditorGUILayout.TextField("Grid sizes", gridText);
                        if (newGridText != gridText)
                        {
                            var list = new List<float>();
                            foreach (var part in newGridText.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                if (float.TryParse(part, out var v) && v > 0f) list.Add(v);
                            if (list.Count > 0) s.gridSizes = list.ToArray();
                        }
                        s.defaultBoxSize = EditorGUILayout.Vector3Field("Default box size", s.defaultBoxSize);
                        s.rotationSnapDegrees = EditorGUILayout.FloatField("Rotation snap (degrees)", s.rotationSnapDegrees);
                        EditorGUILayout.LabelField("Reference sizes (used by the lint)", EditorStyles.boldLabel);
                        s.playerBox = EditorGUILayout.Vector3Field("Player box", s.playerBox);
                        s.crouchHeight = EditorGUILayout.FloatField("Crouched height", s.crouchHeight);
                        s.maxStep = EditorGUILayout.FloatField("Max step", s.maxStep);
                        s.maxSlopeDegrees = EditorGUILayout.FloatField("Max walkable slope", s.maxSlopeDegrees);
                        s.doorwayMinimum = EditorGUILayout.Vector2Field("Doorway minimum", s.doorwayMinimum);
                    }
                    var names = Array.ConvertAll(s.gridSizes, g => g.ToString("0.###") + " " + s.unitLabel);
                    s.gridIndex = EditorGUILayout.Popup("Default grid", Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names);
                    s.snapToGrid = EditorGUILayout.Toggle(new GUIContent("Snap brushes to grid", "Positions, sizes and rotations are kept on the world grid after every edit, whatever the parent does."), s.snapToGrid);
                    s.showGenerated = EditorGUILayout.Toggle("Show generated objects", s.showGenerated);
                    s.defaultModelStaticFlags = (StaticEditorFlags)EditorGUILayout.EnumFlagsField(new GUIContent("Default model static flags", "Inherited by the render meshes of brushes that are not under a model of your own"), s.defaultModelStaticFlags);
                    EditorGUILayout.HelpBox("1 metre = " + s.unitsPerMeter + " " + s.unitLabel + ". Geometry is stored in metres; changing the preset only changes how sizes are shown and snapped.", MessageType.None);
                    if (EditorGUI.EndChangeCheck())
                        s.NotifyChanged();
                }
            };
            return provider;
        }
    }
}

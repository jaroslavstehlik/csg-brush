using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Project-wide world preset: units per metre, grid and defaults. Geometry is always stored in metres; the preset
    /// only changes how sizes are displayed and snapped.
    /// </summary>
    [FilePath("ProjectSettings/BrushSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class BrushSettings : ScriptableSingleton<BrushSettings>
    {
        public enum Preset { Quake = 0, Source = 1, Unreal = 2, Unity = 3, Custom = 4 } // stored by value: Unity was Metric

        /// <summary>The presets in the order the settings list them.</summary>
        public static readonly Preset[] PresetOrder = { Preset.Unity, Preset.Quake, Preset.Source, Preset.Unreal, Preset.Custom };

        public Preset preset = Preset.Unity;
        public float unitsPerMeter = 1f;
        public string unitLabel = "m";
        public float[] gridSizes = { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f };
        public int gridIndex = 2;
        public Vector3 defaultBoxSize = new Vector3(2f, 2f, 2f);
        public float rotationSnapDegrees = 15f;
        [Header("New brushes (the Create tools)")]
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
        [Tooltip("Units; 0 uses one grid step.")] public float newWallThickness = 0f;
        [Range(1f, 180f)] public float newArchAngle = 180f;
        [Tooltip("Units; 0 uses 1 m.")] public float newDoorWidth = 0f;
        [Tooltip("Units; 0 uses 2.2 m.")] public float newDoorHeight = 0f;
        [Tooltip("Units; 0 uses 1.2 m.")] public float newWindowWidth = 0f;
        [Tooltip("Units; 0 uses 1.2 m.")] public float newWindowHeight = 0f;
        [Tooltip("Units above the floor; 0 uses 0.9 m.")] public float newWindowSill = 0f;
        [Header("Extrude (edit mode)")]
        [Tooltip("Units; 0 uses one grid step.")] public float extrudeDistance = 0f;
        public bool extrudeIndividual = false;
        [Tooltip("Static flags of the hidden default model; its render meshes inherit them. A model you make has its own.")]
        public StaticEditorFlags defaultModelStaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic | StaticEditorFlags.ReflectionProbeStatic;
        [Tooltip("Material of every brush face without one of its own. Empty: a generated grid texture (a ruler in metres) is created under Assets/CSG Brush on first use.")]
        public Material defaultMaterial;
        [Header("Editor")]
        public bool showGenerated = false;
        [Tooltip("Draw subtract brushes as translucent red volumes in the Scene view, so they can be seen and selected where they have carved everything away.")]
        public bool showCuts = false;
        [Tooltip("Keep every brush on the grid: position, size and rotation are snapped in world space after each edit, whatever the parent does.")]
        public bool snapToGrid = true;

        public static event Action Changed;

        /// <summary>The step height new stairs get unless one is set: a comfortable step for a 1.5 to 2 m character, and under Unity's default CharacterController step offset (0.3 m).</summary>
        public const float DefaultStepHeightMeters = 0.25f;

        /// <summary>The tread thickness spiral stairs get unless one is set: thin, well under the default step height.</summary>
        public const float DefaultStepThicknessMeters = 0.1f;

        /// <summary>A door's opening unless set: room for a 0.5 m wide, 1.5 to 2 m tall character.</summary>
        public const float DefaultDoorWidthMeters = 1f, DefaultDoorHeightMeters = 2.2f;
        /// <summary>A window unless set: 1.2 m square, its bottom at 0.9 m (a common sill height).</summary>
        public const float DefaultWindowWidthMeters = 1.2f, DefaultWindowHeightMeters = 1.2f, DefaultWindowSillMeters = 0.9f;
        /// <summary>How far a door or window reaches past each face of a wall, so it cuts cleanly through.</summary>
        public const float OpeningMarginMeters = 0.05f;
        /// <summary>The depth of a door or window not placed on a wall.</summary>
        public const float DefaultOpeningDepthMeters = 0.5f;

        /// <summary>Width and height of the next door or window, in metres.</summary>
        public Vector2 NewOpeningSize(BrushShape shape) => shape == BrushShape.Window
            ? new Vector2(newWindowWidth > 0f ? ToMeters(newWindowWidth) : DefaultWindowWidthMeters, newWindowHeight > 0f ? ToMeters(newWindowHeight) : DefaultWindowHeightMeters)
            : new Vector2(newDoorWidth > 0f ? ToMeters(newDoorWidth) : DefaultDoorWidthMeters, newDoorHeight > 0f ? ToMeters(newDoorHeight) : DefaultDoorHeightMeters);

        /// <summary>Height of the next door's or window's bottom above the floor, in metres.</summary>
        public float NewOpeningSill(BrushShape shape) => shape == BrushShape.Window ? (newWindowSill > 0f ? ToMeters(newWindowSill) : DefaultWindowSillMeters) : 0f;


        /// <summary>The step height of the next stairs, in metres.</summary>
        public float NewStepHeightMeters => newStepHeight > 0f ? ToMeters(newStepHeight) : DefaultStepHeightMeters;

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
                    break;
                case Preset.Source:
                    unitsPerMeter = 39.37f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unreal:
                    unitsPerMeter = 100f; unitLabel = "cm";
                    gridSizes = new float[] { 1, 5, 10, 25, 50, 100, 200, 500 }; gridIndex = 3;
                    defaultBoxSize = new Vector3(200f, 200f, 200f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unity:
                    unitsPerMeter = 1f; unitLabel = "m";
                    gridSizes = new float[] { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f }; gridIndex = 2;
                    defaultBoxSize = new Vector3(2f, 2f, 2f); rotationSnapDegrees = 15f;
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
                keywords = new HashSet<string>(new[] { "brush", "grid", "units", "preset", "level" }),
                guiHandler = _ =>
                {
                    var s = BrushSettings.instance;
                    EditorGUI.BeginChangeCheck();
                    var presetNames = Array.ConvertAll(BrushSettings.PresetOrder, p => p.ToString());
                    var preset = BrushSettings.PresetOrder[Mathf.Max(0, EditorGUILayout.Popup("World preset", Array.IndexOf(BrushSettings.PresetOrder, s.preset), presetNames))];
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
                    }
                    var names = Array.ConvertAll(s.gridSizes, g => g.ToString("0.###") + " " + s.unitLabel);
                    s.gridIndex = EditorGUILayout.Popup("Default grid", Mathf.Clamp(s.gridIndex, 0, names.Length - 1), names);
                    s.snapToGrid = EditorGUILayout.Toggle(new GUIContent("Snap brushes to grid", "Positions, sizes and rotations are kept on the world grid after every edit, whatever the parent does."), s.snapToGrid);
                    EditorGUILayout.BeginHorizontal();
                    s.defaultMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Default material", "Every brush face without a material of its own. Empty: the generated grid texture, a ruler in metres, created under Assets/CSG Brush on first use."), s.defaultMaterial, typeof(Material), false);
                    if (GUILayout.Button(new GUIContent("Grid", "Use the generated grid material (drawn again from the grid sizes)"), GUILayout.Width(44))) { s.defaultMaterial = null; BrushGridMaterial.Regenerate(); BrushGridMaterial.GetOrCreate(); BrushApi.ForceUpdate(); }
                    EditorGUILayout.EndHorizontal();
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

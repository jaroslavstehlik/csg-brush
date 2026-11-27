using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>What dragging a shape handle does to the other selected brushes.</summary>
    public enum ShapeEditSelection
    {
        /// <summary>Only the brush whose handle is dragged changes.</summary>
        [InspectorName("Single")] Single = 0,
        /// <summary>Selected brushes of the same shape change by the same amount.</summary>
        [InspectorName("Offset")] Offset = 1,
        /// <summary>Selected brushes of the same shape take the dragged brush's new value.</summary>
        [InspectorName("Match")] Match = 2,
    }

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
        /// <summary>The grid size a project opens with (an index into <see cref="gridSizes"/>); the overlay changes <see cref="gridIndex"/> for the session.</summary>
        [UnityEngine.Serialization.FormerlySerializedAs("gridIndex")] public int defaultGridIndex = 2;
        public Vector3 defaultBoxSize = new Vector3(2f, 2f, 2f);
        public float rotationSnapDegrees = 15f;
        [Header("New brushes (the Create tools)")]
        [Tooltip("What a new brush starts with: the project's defaults. The Brushes overlay changes them for the shape you are drawing, until you pick another shape or leave the Create tools.")]
        public NewBrushOptions newBrush = new NewBrushOptions();
        [NonSerialized] NewBrushOptions m_Working;

        /// <summary>
        /// The options the Create tools use now: the project's defaults (<see cref="newBrush"/>) as changed in the Brushes overlay
        /// for the shape being drawn. They go back to the defaults when the shape changes or the Create tools are left (see
        /// <see cref="ResetWorking"/>), so a Hollow or Subtract picked once does not follow every later brush.
        /// </summary>
        public NewBrushOptions Working => m_Working ??= newBrush.Clone();

        /// <summary>Back to the project's defaults for the next brush.</summary>
        public void ResetWorking() => m_Working = null;

        [Header("Extrude (edit mode)")]
        [Tooltip("Units; 0 uses one grid step. The project's default; the Extrude panel changes it for the session.")] [UnityEngine.Serialization.FormerlySerializedAs("extrudeDistance")] public float defaultExtrudeDistance = 0f;
        [Tooltip("The project's default; the Extrude panel changes it for the session.")] [UnityEngine.Serialization.FormerlySerializedAs("extrudeIndividual")] public bool defaultExtrudeIndividual = false;
        [Tooltip("The static flags new brushes and floor plans start with. A brush's own flags go to its meshes and colliders.")]
        public StaticEditorFlags defaultModelStaticFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
        [Tooltip("Make lightmap UVs for lit brush meshes when an edit is finished.")]
        public bool autoLightmapUVs = true;
        [Tooltip("The Blender program Save to Blender uses. Empty: found where Blender is usually installed.")]
        public string blenderPath = "";
        [Tooltip("Put the textures inside the Blender file, so it shows them on any computer. The file gets larger.")]
        public bool blenderPackTextures = false;
        [Tooltip("Material of every brush face without one of its own. Empty: a generated grid texture (a ruler in metres) is created under Assets/CSG Brush on first use.")]
        public Material defaultMaterial;
        [Header("Editor")]
        public bool showGenerated = false;
        [Tooltip("Draw subtract brushes as translucent red volumes in the Scene view, so they can be seen and selected where they have carved everything away.")]
        [UnityEngine.Serialization.FormerlySerializedAs("showCuts")] public bool defaultShowCuts = false;
        [Tooltip("Show the width, depth and height of a brush or wall while it is drawn.")]
        [UnityEngine.Serialization.FormerlySerializedAs("showDimensions")] public bool defaultShowDimensions = true;
        [Tooltip("Drag a selected brush's sides to change its shape.")]
        [UnityEngine.Serialization.FormerlySerializedAs("shapeHandles")] public bool defaultShapeHandles = true;
        [Tooltip("What a shape edit does to the other selected brushes.")]
        [UnityEngine.Serialization.FormerlySerializedAs("shapeEditSelection")] public ShapeEditSelection defaultShapeEditSelection = ShapeEditSelection.Single;
        [Tooltip("Whether the project opens with grid snapping on: position, size and rotation are snapped in world space after each edit. The Brushes overlay changes it for the session only.")]
        [UnityEngine.Serialization.FormerlySerializedAs("snapToGrid")] public bool defaultSnapToGrid = true;
        [Tooltip("Metres: how far the straight pieces of a curved wall or spline may stray from the true curve. Larger is rougher and lighter.")]
        public float curveTolerance = 0.1f;

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
            ? new Vector2(Working.newWindowWidth > 0f ? ToMeters(Working.newWindowWidth) : DefaultWindowWidthMeters, Working.newWindowHeight > 0f ? ToMeters(Working.newWindowHeight) : DefaultWindowHeightMeters)
            : new Vector2(Working.newDoorWidth > 0f ? ToMeters(Working.newDoorWidth) : DefaultDoorWidthMeters, Working.newDoorHeight > 0f ? ToMeters(Working.newDoorHeight) : DefaultDoorHeightMeters);

        /// <summary>Height of the next door's or window's bottom above the floor, in metres.</summary>
        public float NewOpeningSill(BrushShape shape) => shape == BrushShape.Window ? (Working.newWindowSill > 0f ? ToMeters(Working.newWindowSill) : DefaultWindowSillMeters) : 0f;


        /// <summary>The step height of the next stairs, in metres.</summary>
        public float NewStepHeightMeters => Mathf.Max(BrushPolyhedron.MinStepHeight, Working.newStepHeight > 0f ? ToMeters(Working.newStepHeight) : DefaultStepHeightMeters);

        // what the tools use now: the project's defaults until changed in the overlay, kept for the editor session (script
        // reloads and play mode included, not a restart); never written into the project settings
        const string GridKey = "CsgBrush.gridIndex", SnapKey = "CsgBrush.snapToGrid";
        [NonSerialized] int m_GridIndex = -1; [NonSerialized] int m_Snap = -1;

        /// <summary>The grid size in use (an index into <see cref="gridSizes"/>): the project's default, or what the overlay set this session.</summary>
        public int gridIndex
        {
            get { if (m_GridIndex < 0) m_GridIndex = SessionState.GetInt(GridKey, defaultGridIndex); return m_GridIndex; }
            set { m_GridIndex = value; SessionState.SetInt(GridKey, value); }
        }

        /// <summary>Whether brushes snap to the grid now: the project's default, or what the overlay set this session.</summary>
        public bool snapToGrid
        {
            get { if (m_Snap < 0) m_Snap = SessionState.GetBool(SnapKey, defaultSnapToGrid) ? 1 : 0; return m_Snap == 1; }
            set { m_Snap = value ? 1 : 0; SessionState.SetBool(SnapKey, value); }
        }

        /// <summary>Shown cuts: the project's default, or what the overlay set this session.</summary>
        public bool showCuts { get => m_ShowCuts.Get(defaultShowCuts); set => m_ShowCuts.Set(value); }
        /// <summary>Sizes shown while drawing: the project's default, or what the overlay set this session.</summary>
        public bool showDimensions { get => m_ShowDimensions.Get(defaultShowDimensions); set => m_ShowDimensions.Set(value); }
        /// <summary>The shape handles: the project's default, or what the overlay (or B) set this session.</summary>
        public bool shapeHandles { get => m_ShapeHandles.Get(defaultShapeHandles); set => m_ShapeHandles.Set(value); }
        /// <summary>What a shape edit does to the other selected brushes: the project's default, or the overlay's this session.</summary>
        public ShapeEditSelection shapeEditSelection { get => (ShapeEditSelection)m_ShapeEditSelection.Get((int)defaultShapeEditSelection); set => m_ShapeEditSelection.Set((int)value); }
        /// <summary>Extrude distance (units): the project's default, or the Extrude panel's this session.</summary>
        public float extrudeDistance { get => m_ExtrudeDistance.Get(defaultExtrudeDistance); set => m_ExtrudeDistance.Set(value); }
        /// <summary>Extrude each face on its own: the project's default, or the Extrude panel's this session.</summary>
        public bool extrudeIndividual { get => m_ExtrudeIndividual.Get(defaultExtrudeIndividual); set => m_ExtrudeIndividual.Set(value); }

        [NonSerialized] readonly SessionValue m_ShowCuts = new SessionValue("showCuts"), m_ShowDimensions = new SessionValue("showDimensions"), m_ShapeHandles = new SessionValue("shapeHandles"),
            m_ShapeEditSelection = new SessionValue("shapeEditSelection"), m_ExtrudeDistance = new SessionValue("extrudeDistance"), m_ExtrudeIndividual = new SessionValue("extrudeIndividual");

        /// <summary>A value kept for the editor session (SessionState), read once and then from memory: the tools read some every frame.</summary>
        sealed class SessionValue
        {
            readonly string m_Key; bool m_Read; float m_Value;
            public SessionValue(string name) { m_Key = "CsgBrush." + name; }
            public bool Get(bool fallback) { if (!m_Read) { m_Value = SessionState.GetBool(m_Key, fallback) ? 1f : 0f; m_Read = true; } return m_Value != 0f; }
            public int Get(int fallback) { if (!m_Read) { m_Value = SessionState.GetInt(m_Key, fallback); m_Read = true; } return (int)m_Value; }
            public float Get(float fallback) { if (!m_Read) { m_Value = SessionState.GetFloat(m_Key, fallback); m_Read = true; } return m_Value; }
            public void Set(bool value) { m_Value = value ? 1f : 0f; m_Read = true; SessionState.SetBool(m_Key, value); }
            public void Set(int value) { m_Value = value; m_Read = true; SessionState.SetInt(m_Key, value); }
            public void Set(float value) { m_Value = value; m_Read = true; SessionState.SetFloat(m_Key, value); }
        }

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
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; defaultGridIndex = gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Source:
                    unitsPerMeter = 39.37f; unitLabel = "u";
                    gridSizes = new float[] { 1, 2, 4, 8, 16, 32, 64, 128, 256 }; defaultGridIndex = gridIndex = 4;
                    defaultBoxSize = new Vector3(64f, 64f, 64f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unreal:
                    unitsPerMeter = 100f; unitLabel = "cm";
                    gridSizes = new float[] { 1, 5, 10, 25, 50, 100, 200, 500 }; defaultGridIndex = gridIndex = 3;
                    defaultBoxSize = new Vector3(200f, 200f, 200f); rotationSnapDegrees = 15f;
                    break;
                case Preset.Unity:
                    unitsPerMeter = 1f; unitLabel = "m";
                    gridSizes = new float[] { 0.125f, 0.25f, 0.5f, 1f, 2f, 4f }; defaultGridIndex = gridIndex = 2;
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
        static bool s_DefaultsOpen; static bool s_ToolDefaultsOpen;
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
                    int defaultGrid = EditorGUILayout.Popup(new GUIContent("Default grid", "The grid size the project opens with. The Brushes overlay changes it for the session only."), Mathf.Clamp(s.defaultGridIndex, 0, names.Length - 1), names);
                    if (defaultGrid != s.defaultGridIndex) s.defaultGridIndex = s.gridIndex = defaultGrid; // in use now too
                    bool defaultSnap = EditorGUILayout.Toggle(new GUIContent("Snap brushes to grid", "Whether the project opens with grid snapping on. Positions, sizes and rotations are kept on the world grid after every edit. The Brushes overlay changes it for the session only."), s.defaultSnapToGrid);
                    if (defaultSnap != s.defaultSnapToGrid) s.defaultSnapToGrid = s.snapToGrid = defaultSnap;
                    s.curveTolerance = Mathf.Max(PointGraph.MinTolerance, EditorGUILayout.FloatField(new GUIContent("Curve tolerance (m)", "How far the straight pieces of curved walls may stray from the true curve. Larger is rougher and lighter."), s.curveTolerance));
                    EditorGUILayout.BeginHorizontal();
                    s.defaultMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Default material", "Every brush face without a material of its own. Empty: the generated grid texture, a ruler in metres, created under Assets/CSG Brush on first use."), s.defaultMaterial, typeof(Material), false);
                    if (GUILayout.Button(new GUIContent("Grid", "Use the generated grid material (drawn again from the grid sizes)"), GUILayout.Width(44))) { s.defaultMaterial = null; BrushGridMaterial.Regenerate(); BrushGridMaterial.GetOrCreate(); BrushApi.ForceUpdate(); }
                    EditorGUILayout.EndHorizontal();
                    s.blenderPackTextures = EditorGUILayout.Toggle(new GUIContent("Pack textures in Blender files", "Put the textures inside the Blender file, so it shows them on any computer. The file gets larger"), s.blenderPackTextures);
                    s.showGenerated = EditorGUILayout.Toggle("Show generated objects", s.showGenerated);
                    s_ToolDefaultsOpen = EditorGUILayout.Foldout(s_ToolDefaultsOpen, new GUIContent("Tool defaults", "What the Brushes overlay and the Extrude panel start with when the project opens. Changing them there lasts for the session only."), true);
                    if (s_ToolDefaultsOpen)
                    {
                        EditorGUI.indentLevel++;
                        // a default changed here is also what is used now
                        bool cuts = EditorGUILayout.Toggle(new GUIContent("Show cuts", "Draw subtract brushes as translucent red volumes"), s.defaultShowCuts);
                        if (cuts != s.defaultShowCuts) s.defaultShowCuts = s.showCuts = cuts;
                        bool sizes = EditorGUILayout.Toggle(new GUIContent("Show sizes", "Show a brush's width, depth and height while it is drawn"), s.defaultShowDimensions);
                        if (sizes != s.defaultShowDimensions) s.defaultShowDimensions = s.showDimensions = sizes;
                        bool handles = EditorGUILayout.Toggle(new GUIContent("Shape handles", "Drag a selected brush's sides to change its shape"), s.defaultShapeHandles);
                        if (handles != s.defaultShapeHandles) s.defaultShapeHandles = s.shapeHandles = handles;
                        var selection = (ShapeEditSelection)EditorGUILayout.EnumPopup(new GUIContent("Shape edit selection", "What a shape edit does to the other selected brushes"), s.defaultShapeEditSelection);
                        if (selection != s.defaultShapeEditSelection) s.defaultShapeEditSelection = s.shapeEditSelection = selection;
                        float distance = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Extrude distance (" + s.unitLabel + ")", "0 uses one grid step"), s.defaultExtrudeDistance));
                        if (distance != s.defaultExtrudeDistance) s.defaultExtrudeDistance = s.extrudeDistance = distance;
                        bool individual = EditorGUILayout.Toggle(new GUIContent("Extrude individually", "Extrude each selected face on its own"), s.defaultExtrudeIndividual);
                        if (individual != s.defaultExtrudeIndividual) s.defaultExtrudeIndividual = s.extrudeIndividual = individual;
                        EditorGUI.indentLevel--;
                    }
                    s.defaultModelStaticFlags = (StaticEditorFlags)EditorGUILayout.EnumFlagsField(new GUIContent("New brush static flags", "The static flags new brushes and floor plans start with"), s.defaultModelStaticFlags);
                    s_DefaultsOpen = EditorGUILayout.Foldout(s_DefaultsOpen, new GUIContent("New brush defaults", "What a new brush starts with. The Brushes overlay changes them for the shape you are drawing; they come back here when you pick another shape or leave the Create tools."), true);
                    if (s_DefaultsOpen)
                    {
                        var so = new SerializedObject(s);
                        var defaults = so.FindProperty(nameof(BrushSettings.newBrush));
                        EditorGUI.indentLevel++;
                        var it = defaults.Copy(); var end = defaults.GetEndProperty();
                        if (it.NextVisible(true))
                            do { if (SerializedProperty.EqualContents(it, end)) break; EditorGUILayout.PropertyField(it, new GUIContent(ObjectNames.NicifyVariableName(it.name.Substring(3)), it.tooltip), true); } while (it.NextVisible(false));
                        EditorGUI.indentLevel--;
                        if (so.ApplyModifiedPropertiesWithoutUndo()) s.ResetWorking();
                    }
                    s.autoLightmapUVs = EditorGUILayout.Toggle(new GUIContent("Auto lightmap UVs", "Make lightmap UVs for lit brush meshes when an edit is finished"), s.autoLightmapUVs);
                    EditorGUILayout.BeginHorizontal();
                    s.blenderPath = EditorGUILayout.TextField(new GUIContent("Blender", "The Blender program Save to Blender uses. Empty: found where Blender is usually installed"), s.blenderPath);
                    if (GUILayout.Button(new GUIContent("Browse", "Choose the Blender program"), GUILayout.Width(60)))
                    {
                        var picked = EditorUtility.OpenFilePanel("Choose Blender", "", Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                        if (!string.IsNullOrEmpty(picked)) { s.blenderPath = picked; GUI.changed = true; }
                    }
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.HelpBox("1 metre = " + s.unitsPerMeter + " " + s.unitLabel + ". Geometry is stored in metres; changing the preset only changes how sizes are shown and snapped.", MessageType.None);
                    if (EditorGUI.EndChangeCheck())
                    { BlenderExport.Forget(); s.NotifyChanged(); }
                }
            };
            return provider;
        }
    }
}

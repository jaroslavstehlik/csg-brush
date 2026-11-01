using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// The Shape Edit toolbar: the toggle for shape handles and, while it is on, what a drag does to the other selected
    /// brushes (Single / Offset / Match), shown like Unity's Pivot / Center button: the current choice's icon, a menu of all.
    /// </summary>
    [Icon(BrushIcons.Folder + "ShapeEdit.png")]
    [Overlay(typeof(SceneView), "Shape Edit", true)]
    public sealed class ShapeEditToolbar : ToolbarOverlay
    {
        ShapeEditToolbar() : base(ShapeEditToggle.Id, ShapeEditSelectionDropdown.Id) { }

        /// <summary>Set a button's icon now, and again every half second until it is there (it may not be imported yet).</summary>
        internal static void LoadIcon(VisualElement button, System.Action load, System.Func<bool> loaded)
        {
            load();
            if (!loaded()) button.schedule.Execute(load).Every(500).Until(loaded);
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    sealed class ShapeEditToggle : EditorToolbarToggle
    {
        public const string Id = "CSG Brush/Shape Edit";

        public ShapeEditToggle()
        {
            ShapeEditToolbar.LoadIcon(this, () => icon = BrushIcons.Get("ShapeEdit"), () => icon != null);
            tooltip = "Shape Edit: drag a selected brush's sides to change its shape (B)";
            RegisterCallback<AttachToPanelEvent>(evt => { BrushSettings.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushSettings.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt =>
            {
                var s = BrushSettings.instance;
                if (s.shapeHandles == evt.newValue) return;
                s.shapeHandles = evt.newValue;
                s.NotifyChanged();
                SceneView.RepaintAll();
            });
            Refresh();
        }

        void Refresh() => SetValueWithoutNotify(BrushSettings.instance.shapeHandles);
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    sealed class ShapeEditSelectionDropdown : EditorToolbarDropdown
    {
        public const string Id = "CSG Brush/Shape Edit Selection";

        /// <summary>The choices in <see cref="ShapeEditSelection"/> order.</summary>
        public static readonly GUIContent[] Options =
        {
            new GUIContent("Single", "Only the brush you drag changes"),
            new GUIContent("Offset", "Selected brushes of the same shape change by the same amount"),
            new GUIContent("Match", "Selected brushes of the same shape get the same size"),
        };
        static readonly string[] Icons = { "ShapeEdit_Single", "ShapeEdit_Offset", "ShapeEdit_Match" };

        public ShapeEditSelectionDropdown()
        {
            clicked += () =>
            {
                var menu = new GenericMenu();
                for (int i = 0; i < Options.Length; i++)
                {
                    var mode = (ShapeEditSelection)i;
                    menu.AddItem(new GUIContent(Options[i].text, BrushIcons.Get(Icons[i]), Options[i].tooltip), BrushSettings.instance.shapeEditSelection == mode, () =>
                    {
                        BrushSettings.instance.shapeEditSelection = mode;
                        BrushSettings.instance.NotifyChanged();
                    });
                }
                menu.DropDown(worldBound);
            };
            RegisterCallback<AttachToPanelEvent>(evt => { BrushSettings.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushSettings.Changed -= Refresh);
            ShapeEditToolbar.LoadIcon(this, Refresh, () => icon != null);
        }

        void Refresh()
        {
            var s = BrushSettings.instance;
            int i = Mathf.Clamp((int)s.shapeEditSelection, 0, Options.Length - 1);
            icon = BrushIcons.Get(Icons[i]);
            tooltip = "Other selected brushes: " + Options[i].text + ". " + Options[i].tooltip + ".";
            style.display = s.shapeHandles ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

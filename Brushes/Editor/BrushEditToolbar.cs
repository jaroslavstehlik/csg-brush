using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace CsgBrush.Editor
{
    /// <summary>
    /// The Tool Settings toolbar of brush edit mode, laid out like ProBuilder's: Vertex / Edge / Face selection,
    /// select hidden elements, drag rectangle mode, and the handle orientation (Global / Local / Element). Unity shows these elements in the
    /// Tool Settings overlay while one of the edit context's tools is active.
    /// </summary>
    [CustomEditor(typeof(BrushSelectionTool), true)]
    sealed class BrushSelectionToolEditor : UnityEditor.Editor, ICreateToolbar
    {
        public IEnumerable<string> toolbarElements
        {
            get
            {
                yield return "Tool Settings/Pivot Mode";
                yield return "CSG Brush/Handle Orientation";
                yield return "CSG Brush/Select Mode";
                yield return "CSG Brush/Select Hidden";
                yield return "CSG Brush/Drag Rect Mode";
            }
        }
    }

    sealed class BrushSelectModeToggle : EditorToolbarToggle
    {
        readonly BrushEditMode mode;

        public BrushSelectModeToggle(BrushEditMode mode)
        {
            this.mode = mode;
            switch (mode)
            {
                case BrushEditMode.Vertex: icon = BrushIcons.Get("Mode_Vertex"); tooltip = "Vertex Selection (1)"; break;
                case BrushEditMode.Edge: icon = BrushIcons.Get("Mode_Edge"); tooltip = "Edge Selection (2)"; break;
                default: icon = BrushIcons.Get("Mode_Face"); tooltip = "Face Selection (3)"; break;
            }
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt => { if (evt.newValue) BrushEditState.Mode = mode; else SetValueWithoutNotify(true); });
            Refresh();
        }

        void Refresh() => SetValueWithoutNotify(BrushEditState.Mode == mode);
    }

    [EditorToolbarElement("CSG Brush/Select Mode")]
    sealed class BrushSelectModeToolbar : VisualElement
    {
        public BrushSelectModeToolbar()
        {
            Add(new BrushSelectModeToggle(BrushEditMode.Vertex));
            Add(new BrushSelectModeToggle(BrushEditMode.Edge));
            Add(new BrushSelectModeToggle(BrushEditMode.Face));
            EditorToolbarUtility.SetupChildrenAsButtonStrip(this);
        }
    }

    [EditorToolbarElement("CSG Brush/Select Hidden")]
    sealed class BrushSelectHiddenToggle : EditorToolbarToggle
    {
        public BrushSelectHiddenToggle()
        {
            icon = BrushIcons.Get("SelectHidden");
            tooltip = "Select Hidden: also pick vertices, edges and faces that look away from the camera";
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt => BrushEditState.SelectHidden = evt.newValue);
            Refresh();
        }

        void Refresh() => SetValueWithoutNotify(BrushEditState.SelectHidden);
    }

    [EditorToolbarElement("CSG Brush/Drag Rect Mode")]
    sealed class BrushDragRectModeToggle : EditorToolbarToggle
    {
        public BrushDragRectModeToggle()
        {
            icon = BrushIcons.Get("DragRect");
            tooltip = "Drag Rectangle Mode: on selects only what is completely inside the rectangle, off everything it touches";
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt => BrushEditState.RectComplete = evt.newValue);
            Refresh();
        }

        void Refresh() => SetValueWithoutNotify(BrushEditState.RectComplete);
    }

    /// <summary>World / Local / Element, like ProBuilder's handle orientation dropdown; Element aligns the gizmo with the selection.</summary>
    [EditorToolbarElement("CSG Brush/Handle Orientation")]
    sealed class BrushHandleOrientationDropdown : EditorToolbarDropdown
    {
        readonly GUIContent[] options = new GUIContent[3];

        public BrushHandleOrientationDropdown()
        {
            name = "Handle Rotation";
            options[0] = new GUIContent("Global", EditorGUIUtility.IconContent("ToolHandleGlobal").image, "The gizmo is aligned with the world axes");
            options[1] = new GUIContent("Local", EditorGUIUtility.IconContent("ToolHandleLocal").image, "The gizmo is aligned with the brush's axes");
            options[2] = new GUIContent("Element", BrushIcons.Get("ToolHandleElement"), "The gizmo is aligned with the selected face, edge or vertex: blue along the normal");
            clicked += () =>
            {
                var menu = new GenericMenu();
                for (int i = 0; i < 3; i++) { int o = i; menu.AddItem(options[i], (int)BrushEditState.Orientation == i, () => BrushEditState.Orientation = (BrushHandleOrientation)o); }
                menu.DropDown(worldBound);
            };
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            Refresh();
        }

        void Refresh()
        {
            var c = options[(int)BrushEditState.Orientation];
            text = c.text; tooltip = c.tooltip; icon = c.image as Texture2D;
        }
    }
}

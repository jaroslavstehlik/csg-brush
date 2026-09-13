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
    /// select hidden elements, drag rectangle mode, and Push/Pull in face mode. Unity shows these elements in the
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
                yield return "CSG Brush/Select Mode";
                yield return "CSG Brush/Select Hidden";
                yield return "CSG Brush/Drag Rect Mode";
                yield return "CSG Brush/Push Pull";
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
                case BrushEditMode.Vertex: icon = BrushIcons.Get("Mode_Vertex", BrushEditIcons.Vertex); tooltip = "Vertex Selection (1)"; break;
                case BrushEditMode.Edge: icon = BrushIcons.Get("Mode_Edge", BrushEditIcons.Edge); tooltip = "Edge Selection (2)"; break;
                default: icon = BrushIcons.Get("Mode_Face", BrushEditIcons.Face); tooltip = "Face Selection (3)"; break;
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
            icon = BrushIcons.Get("SelectHidden", BrushEditIcons.Hidden);
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
            icon = BrushIcons.Get("DragRect", BrushEditIcons.DragRect);
            tooltip = "Drag Rectangle Mode: on selects only what is completely inside the rectangle, off everything it touches";
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt => BrushEditState.RectComplete = evt.newValue);
            Refresh();
        }

        void Refresh() => SetValueWithoutNotify(BrushEditState.RectComplete);
    }

    [EditorToolbarElement("CSG Brush/Push Pull")]
    sealed class BrushPushPullToggle : EditorToolbarToggle
    {
        public BrushPushPullToggle()
        {
            icon = BrushIcons.Get("PushPull", BrushEditIcons.PushPull);
            tooltip = "Push/Pull: move the selected faces along their normals with an arrow instead of the Move gizmo (or hold Shift)";
            RegisterCallback<AttachToPanelEvent>(evt => { BrushEditState.Changed += Refresh; Refresh(); });
            RegisterCallback<DetachFromPanelEvent>(evt => BrushEditState.Changed -= Refresh);
            this.RegisterValueChangedCallback(evt => BrushEditState.PushPull = evt.newValue);
            Refresh();
        }

        void Refresh()
        {
            SetValueWithoutNotify(BrushEditState.PushPull);
            style.display = BrushEditState.Mode == BrushEditMode.Face ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    static class BrushEditIcons
    {
        public const string Vertex = "................\n................\n...##......##...\n..####....####..\n..####....####..\n...##......##...\n................\n................\n................\n................\n...##......##...\n..####....####..\n..####....####..\n...##......##...\n................\n................";
        public const string Edge = "................\n..............#.\n.............##.\n............##..\n...........##...\n..........##....\n.........##.....\n........##......\n.......##.......\n......##........\n.....##.........\n....##..........\n...##...........\n..##............\n.#..............\n................";
        public const string Face = "................\n................\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n..############..\n................\n................";
        public const string Hidden = "................\n................\n................\n.....######.....\n...##......##...\n..#...####...#..\n.#...######...#.\n#....######....#\n.#...######...#.\n..#...####...#..\n...##......##...\n.....######.....\n................\n................\n................\n................";
        public const string DragRect = "................\n.##.##.##.##.##.\n................\n#..............#\n#..............#\n................\n#..............#\n#..............#\n................\n#..............#\n#..............#\n................\n#..............#\n#..............#\n................\n.##.##.##.##.##.";
        public const string PushPull = "................\n.......##.......\n......####......\n.....######.....\n....########....\n.......##.......\n.......##.......\n.......##.......\n.......##.......\n.......##.......\n.......##.......\n....########....\n.....######.....\n......####......\n.......##.......\n................";
    }
}

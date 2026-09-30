# Icons

The editor icons in `Brushes/Editor/Icons` are drawn by `generate_icons.py`: flat single-colour glyphs in the style of
Unity's own, 3D shapes from one shared view. Each icon is written as `Name.png` (light theme) and `d_Name.png` (dark
theme), 32 x 32 for 16 points on high-density screens, with a `.meta` whose GUID comes from the file name.

    python3 -m venv .venv
    .venv/bin/pip install -r requirements.txt
    .venv/bin/python generate_icons.py                  # writes the icons
    .venv/bin/python generate_icons.py --sheet out.png  # plus a contact sheet of all icons on both themes

On Windows use `.venv\Scripts\python`. Unity ignores this folder (its name ends in `~`); `.venv` is not committed.
To add an icon, write a drawing function and list it in `ICONS`; the code loads it with `BrushIcons.Get("Name")` or
`[Icon(BrushIcons.Folder + "Name.png")]`, and Unity picks the `d_` variant on the dark theme.

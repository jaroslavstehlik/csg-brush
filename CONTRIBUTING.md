# Contributing

Contributions are accepted under the [MIT license](LICENSE), the same license as the package.

## Run the tests

Open **Window** > **General** > **Test Runner**, select **EditMode**, and run `CsgBrush.Tests`. To run them headlessly:

```
Unity -batchmode -projectPath <project> -runTests -testPlatform EditMode -testFilter CsgBrush.Tests
```

## Native plugin

`Manifold/Plugins` holds the self-contained Manifold shared library for each editor platform, built from Manifold at a
pinned tag by `Manifold/native/CMakeLists.txt`. Only the macOS arm64 build is in the repository so far. The GitHub
workflow `.github/workflows/build-manifold.yml` builds macOS arm64, Windows x64 and Linux x64 and opens a pull request
with the binaries; run it after bumping the tag. Nothing native is needed at runtime.

## Layout

- `Brushes/` the Brush and Brush Model components, the editor layer (sync, snapping, edit tools, overlay,
  settings, menus), the Manifold model builder (`BrushCsg`), and the tests.
- `ConvexColliders/` the convex collider builder; game data reaches the pieces through brush modules.
- `Manifold/` the native plugin, its P/Invoke layer and the `ManifoldSolid` wrapper.

## Icons

The editor icons are generated: see `Tools~/icons/README.md` (Python 3 with Pillow).

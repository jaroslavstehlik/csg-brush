using System.IO;
using UnityEditor;
using UnityEngine;

namespace CsgBrush.Editor
{
    /// <summary>
    /// The default material of brushes: a generated dev texture that is a ruler in metres. Lines at every grid size
    /// of the project, faint for the fine ones and strong at the metre, over a light/dark checker with a metre
    /// period, so a level reads its own measurements. The render mesh's UVs are one tile per metre from the
    /// position, so the lines fall on the world grid and run seamlessly across brushes and cuts. The material is
    /// created once as an asset under Assets/CSG Brush with the active render pipeline's lit shader (the mesh
    /// objects are saved with the scene, so their material has to be an asset).
    /// </summary>
    public static class BrushGridMaterial
    {
        public const string Folder = "Assets/CSG Brush";
        public const string TexturePath = Folder + "/BrushGrid.png";
        public const string MaterialPath = Folder + "/BrushGrid.mat";
        /// <summary>The texture spans this many metres (a checker needs two cells per period).</summary>
        public const float Metres = 2f;
        public const int PixelsPerMetre = 512;

        /// <summary>The project's default brush material: the one in Project Settings > Brushes, else the grid material (created on first use).</summary>
        public static Material GetOrCreate()
        {
            var s = BrushSettings.instance;
            if (s.defaultMaterial != null) return s.defaultMaterial;
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
                if (texture == null) texture = WriteTexture();
                var shader = PipelineLitShader();
                if (shader == null) return null;
                material = new Material(shader) { name = "BrushGrid", mainTexture = texture, mainTextureScale = Vector2.one / Metres };
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);
                Directory.CreateDirectory(Folder);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            s.defaultMaterial = material; s.NotifyChanged();
            return material;
        }

        /// <summary>Draw the texture again from the current grid sizes (the material keeps referencing it).</summary>
        public static Texture2D Regenerate() => WriteTexture();

        static Shader PipelineLitShader()
        {
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (pipeline == null) pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null) pipeline = QualitySettings.renderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null) return pipeline.defaultMaterial.shader;
            return Shader.Find("Standard");
        }

        static Texture2D WriteTexture()
        {
            var s = BrushSettings.instance;
            var pixels = Generate(Mathf.RoundToInt(Metres * PixelsPerMetre), s.unitsPerMeter, s.gridSizes);
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(TexturePath, pixels.EncodeToPNG());
            Object.DestroyImmediate(pixels);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(TexturePath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
                importer.maxTextureSize = 2048; importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        /// <summary>
        /// The ruler: a square texture of <see cref="Metres"/> metres. Grid sizes are in units; those at or below a
        /// metre draw lines, the finest faint and the metre strong; the checker alternates every metre.
        /// </summary>
        public static Texture2D Generate(int size, float unitsPerMeter, float[] gridSizesUnits)
        {
            float pxPerMetre = size / Metres;
            var steps = new System.Collections.Generic.List<float>();
            if (gridSizesUnits != null) foreach (var g in gridSizesUnits) { float m = g / Mathf.Max(1e-6f, unitsPerMeter); if (m > 0f && m <= 1f + 1e-6f && !steps.Contains(m)) steps.Add(m); }
            if (!steps.Contains(1f)) steps.Add(1f);
            steps.Sort();
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "BrushGrid", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[size * size];
            const float light = 0.64f, dark = 0.58f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int cx = Mathf.FloorToInt(x / pxPerMetre), cy = Mathf.FloorToInt(y / pxPerMetre);
                    float v = (cx + cy) % 2 == 0 ? light : dark;
                    float ink = 0f;
                    for (int i = 0; i < steps.Count; i++)
                    {
                        float stepPx = steps[i] * pxPerMetre;
                        float strength = steps.Count == 1 ? 0.5f : Mathf.Lerp(0.12f, 0.5f, i / (float)(steps.Count - 1));
                        int half = i == steps.Count - 1 ? 2 : 1; // the metre line is thicker
                        if (OnLine(x, stepPx, half) || OnLine(y, stepPx, half)) ink = Mathf.Max(ink, strength);
                    }
                    v *= 1f - ink;
                    byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            tex.SetPixels32(pixels); tex.Apply();
            return tex;
        }

        static bool OnLine(int p, float stepPx, int half)
        {
            float d = p % stepPx; if (d > stepPx * 0.5f) d = stepPx - d;
            return d < half;
        }
    }
}

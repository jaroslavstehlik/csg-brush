using System.Diagnostics;
using System.IO;
using CsgBrush.Editor;
using UnityEditor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CsgBrush.Tests
{
    /// <summary>
    /// Save to Blender, read back by Blender itself: the meshes arrive at the same size and place (Blender's Z up), facing
    /// the same way, with both UV sets and the materials. Skipped where Blender isn't installed.
    /// </summary>
    public class BlenderExportTests
    {
        string m_Folder;

        [SetUp]
        public void Open()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            m_Folder = Path.Combine(Path.GetTempPath(), "csgbrush-blender-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_Folder);
        }

        [TearDown]
        public void Close() { try { Directory.Delete(m_Folder, true); } catch { } }

        static string Blender()
        {
            var blender = BlenderExport.FindBlender(out _, out var error);
            if (blender == null) Assert.Ignore("Blender is not installed: " + error);
            return blender;
        }

        const string Inspect = @"import bpy, sys
for ob in bpy.data.objects:
    me = ob.data
    if hasattr(me, 'calc_normals_split'):
        me.calc_normals_split()
    lo = [min(v.co[i] for v in me.vertices) for i in range(3)]
    hi = [max(v.co[i] for v in me.vertices) for i in range(3)]
    facing = 0
    for p in me.polygons:
        n = [sum(me.loops[l].normal[i] for l in p.loop_indices) for i in range(3)]
        if sum(n[i] * p.normal[i] for i in range(3)) > 0: facing += 1
    print('INSPECT', ob.name, len(me.polygons), facing, ' '.join('%.4f' % x for x in lo + hi), ','.join(l.name for l in me.uv_layers), ','.join(m.name for m in me.materials if m), bpy.context.scene.unit_settings.system, bpy.context.scene.unit_settings.scale_length)
";

        [Test]
        public void AGroupSavedToBlenderArrivesTheSameSizePlaceAndWayRound()
        {
            var blender = Blender();
            var group = new GameObject("Level").AddComponent<BrushGroup>();
            group.transform.position = new Vector3(10f, 0f, -5f); // the file is in the group's own space
            BrushApi.Create(BrushShape.Box, new Vector3(10f, -0.5f, -5f), new Vector3(8f, 1f, 6f), Quaternion.identity, group.transform, "Floor");
            BrushApi.Create(BrushShape.Box, new Vector3(10f, 1.5f, -2.2f), new Vector3(8f, 3f, 0.4f), Quaternion.identity, group.transform, "Wall");
            var door = BrushApi.Create(BrushShape.Box, new Vector3(9f, 1f, -2.2f), new Vector3(1f, 2f, 1f), Quaternion.identity, group.transform, "Door");
            door.operation = BrushOperation.Subtract;
            BrushApi.ForceUpdate();
            BrushLightmapUVs.Generate(group);
            var mesh = BrushCsg.MeshObject(group, false).GetComponent<MeshFilter>().sharedMesh;
            int triangles = mesh.triangles.Length / 3;
            var b = mesh.bounds; // the mesh child sits at the group's origin

            var path = Path.Combine(m_Folder, "level.blend");
            Assert.IsNull(BlenderExport.Save(group, path), "saved");
            Assert.IsTrue(File.Exists(path));

            var script = Path.Combine(m_Folder, "inspect.py");
            File.WriteAllText(script, Inspect);
            var info = new ProcessStartInfo(blender, "--background --factory-startup \"" + path + "\" --python \"" + script + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            string output;
            using (var p = Process.Start(info)) { output = p.StandardOutput.ReadToEnd(); p.WaitForExit(120000); }
            string line = null;
            foreach (var l in output.Split('\n')) if (l.StartsWith("INSPECT ")) line = l;
            Assert.IsNotNull(line, "Blender read the file:\n" + output);
            var f = line.Split(' ');
            // INSPECT name polygons facing minx miny minz maxx maxy maxz uvlayers materials units scale
            Assert.AreEqual("Level", f[1]);
            Assert.AreEqual(triangles, int.Parse(f[2]), "every triangle");
            Assert.AreEqual(triangles, int.Parse(f[3]), "every face faces the way its Unity normal does");
            var lo = new Vector3(float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[6], System.Globalization.CultureInfo.InvariantCulture));
            var hi = new Vector3(float.Parse(f[7], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[8], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[9], System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(new Vector3(b.min.x, b.min.z, b.min.y).ToString("F3"), lo.ToString("F3"), "Unity's x, y, z are Blender's x, z, y: the same size, at the group's origin");
            Assert.AreEqual(new Vector3(b.max.x, b.max.z, b.max.y).ToString("F3"), hi.ToString("F3"));
            Assert.AreEqual("UVMap,Lightmap", f[10], "both UV sets");
            Assert.IsFalse(string.IsNullOrEmpty(f[11]), "the material");
            Assert.AreEqual("METRIC", f[12]); Assert.AreEqual(1f, float.Parse(f[13], System.Globalization.CultureInfo.InvariantCulture), 1e-6f, "metres");
        }

        const string InspectMaterials = @"import bpy
def src(sock):
    return sock.links[0].from_node if sock.is_linked else None
for mat in bpy.data.materials:
    nt = mat.node_tree
    b = nt.nodes.get('Principled BSDF')
    def get(*names):
        for n in names:
            if n in b.inputs:
                return b.inputs[n]
    base, met, rough, nrm, emi = get('Base Color'), get('Metallic'), get('Roughness'), get('Normal'), get('Emission Color', 'Emission')
    mapping = next((n for n in nt.nodes if n.type == 'MAPPING'), None)
    normal_image = ''
    if nrm.is_linked:
        nm = nrm.links[0].from_node
        normal_image = nm.inputs['Color'].links[0].from_node.image.colorspace_settings.name if nm.inputs['Color'].is_linked else 'none'
    faces = sum(1 for o in bpy.data.objects for p in o.data.polygons if o.data.materials[p.material_index] == mat)
    print('MAT', mat.name.replace(' ', '_'), faces,
          'base=' + ('texture' if base.is_linked else '%.3f,%.3f,%.3f' % tuple(base.default_value[:3])),
          'metallic=' + ('map' if met.is_linked else '%.2f' % met.default_value),
          'roughness=' + ('map' if rough.is_linked else '%.2f' % rough.default_value),
          'normal=' + (normal_image if nrm.is_linked else 'none'),
          'emission=' + ('texture' if emi.is_linked else '%.3f,%.3f,%.3f' % tuple(emi.default_value[:3])),
          'tiling=%.1f,%.1f' % (mapping.inputs['Scale'].default_value[0], mapping.inputs['Scale'].default_value[1]))
";

        static Texture2D TextureAsset(string path, Color color, bool normalMap)
        {
            var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = color; t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            if (normalMap) { var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.NormalMap; importer.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [Test]
        public void MaterialsArriveWithTheirColourTexturesMetalRoughnessNormalsAndEmission()
        {
            var blender = Blender();
            const string Folder = "Assets/__blender_export_test";
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "__blender_export_test");
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                bool urp = shader.name.StartsWith("Universal");
                var red = new Material(shader) { name = "Red Paint" };
                red.SetColor(urp ? "_BaseColor" : "_Color", new Color(1f, 0f, 0f, 1f));
                red.SetFloat("_Metallic", 0f); red.SetFloat(urp ? "_Smoothness" : "_Glossiness", 0.25f);
                AssetDatabase.CreateAsset(red, Folder + "/Red Paint.mat");
                var metal = new Material(shader) { name = "Plated Metal" };
                metal.SetTexture(urp ? "_BaseMap" : "_MainTex", TextureAsset(Folder + "/base.png", Color.gray, false));
                metal.SetTextureScale(urp ? "_BaseMap" : "_MainTex", new Vector2(2f, 3f));
                metal.SetFloat("_Metallic", 0.8f); metal.SetFloat(urp ? "_Smoothness" : "_Glossiness", 0.6f);
                metal.SetTexture("_BumpMap", TextureAsset(Folder + "/normal.png", new Color(0.5f, 0.5f, 1f), true)); metal.EnableKeyword("_NORMALMAP");
                metal.SetColor("_EmissionColor", new Color(0f, 0.5f, 0f)); metal.EnableKeyword("_EMISSION"); metal.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; // as the Inspector's Emission tick leaves it
                AssetDatabase.CreateAsset(metal, Folder + "/Plated Metal.mat");

                var group = new GameObject("Level").AddComponent<BrushGroup>();
                var floor = BrushApi.Create(BrushShape.Box, new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 6f), Quaternion.identity, group.transform, "Floor");
                var wall = BrushApi.Create(BrushShape.Box, new Vector3(0f, 1.5f, 2.8f), new Vector3(8f, 3f, 0.4f), Quaternion.identity, group.transform, "Wall");
                floor.material = red; wall.material = metal;
                BrushApi.ForceUpdate();

                var path = Path.Combine(m_Folder, "materials.blend");
                Assert.IsNull(BlenderExport.Save(group, path), "saved");
                var script = Path.Combine(m_Folder, "materials.py");
                File.WriteAllText(script, InspectMaterials);
                var info = new ProcessStartInfo(blender, "--background --factory-startup \"" + path + "\" --python \"" + script + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                string output;
                using (var p = Process.Start(info)) { output = p.StandardOutput.ReadToEnd(); p.WaitForExit(120000); }
                string redLine = null, metalLine = null;
                foreach (var l in output.Split('\n')) { if (l.StartsWith("MAT Red_Paint ")) redLine = l.Trim(); if (l.StartsWith("MAT Plated_Metal ")) metalLine = l.Trim(); }
                Assert.IsNotNull(redLine, "the red material:\n" + output); Assert.IsNotNull(metalLine, "the metal material:\n" + output);
                StringAssert.Contains("base=1.000,0.000,0.000", redLine, "red, linear");
                StringAssert.Contains("metallic=0.00", redLine); StringAssert.Contains("roughness=0.75", redLine, "one minus smoothness");
                StringAssert.Contains("normal=none", redLine); StringAssert.Contains("emission=0.000,0.000,0.000", redLine);
                StringAssert.Contains("base=texture", metalLine); StringAssert.Contains("tiling=2.0,3.0", metalLine, "the base map's tiling");
                StringAssert.Contains("metallic=0.80", metalLine); StringAssert.Contains("roughness=0.40", metalLine);
                StringAssert.Contains("normal=Non-Color", metalLine, "the normal map read as data, not colour");
                StringAssert.Contains("emission=0.000,0.214,0.000", metalLine, "the emission colour, linear");
                int redFaces = int.Parse(redLine.Split(' ')[2]), metalFaces = int.Parse(metalLine.Split(' ')[2]);
                Assert.Greater(redFaces, 0, "faces of the floor"); Assert.Greater(metalFaces, 0, "faces of the wall");
            }
            finally { AssetDatabase.DeleteAsset(Folder); }
        }

        [Test]
        public void AMissingBlenderSaysSo()
        {
            var keep = BrushSettings.instance.blenderPath;
            try
            {
                BrushSettings.instance.blenderPath = Path.Combine(m_Folder, "no-blender-here");
                BlenderExport.Forget();
                Assert.IsNull(BlenderExport.FindBlender(out _, out var error));
                StringAssert.Contains("not found", error);
                var group = new GameObject("Level").AddComponent<BrushGroup>();
                StringAssert.Contains("not found", BlenderExport.Save(group, Path.Combine(m_Folder, "x.blend")));
            }
            finally { BrushSettings.instance.blenderPath = keep; BlenderExport.Forget(); }
        }
    }
}

using System.Diagnostics;
using System.IO;
using CsgBrush.Editor;
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

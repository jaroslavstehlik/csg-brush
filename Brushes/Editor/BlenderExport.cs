using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Editor
{
    /// <summary>
    /// Saves a Brush Group's meshes as a Blender file, using the Blender installed on this computer: the meshes are
    /// written in Blender's own coordinates (Unity's x, y, z become Blender's x, z, y: right-handed, Z up, metres, each
    /// triangle's corners reversed since that swap mirrors them) and a
    /// short script run by Blender in the background builds them with Blender's own API and saves the file. Nothing is
    /// left for an importer to interpret, and no package is needed.
    /// </summary>
    public static class BlenderExport
    {
        /// <summary>The oldest Blender the export script runs on.</summary>
        public static readonly Version MinimumVersion = new Version(3, 6);
        /// <summary>Seconds Blender may take to save a file before it is stopped.</summary>
        public static int TimeoutSeconds = 300;
        const string OkMarker = "CSGBRUSH_EXPORT_OK";

        // ------------------------------------------------------------------ finding Blender

        static string s_CheckedPath, s_CheckedError; static Version s_CheckedVersion;

        /// <summary>
        /// The Blender to use: the path in Project Settings > Brushes, else one found where it is usually installed. It counts
        /// only when it runs and reports a version of at least <see cref="MinimumVersion"/>. Null with <paramref name="error"/>
        /// saying why otherwise. The answer is kept until the path changes.
        /// </summary>
        public static string FindBlender(out Version version, out string error)
        {
            var candidates = new List<string>();
            string set = BrushSettings.instance.blenderPath;
            if (!string.IsNullOrEmpty(set)) candidates.Add(Executable(set));
            else candidates.AddRange(UsualPlaces());
            string firstError = null;
            foreach (var path in candidates)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (path == s_CheckedPath && s_CheckedError == null) { version = s_CheckedVersion; error = null; return path; }
                var found = Check(path, out version, out error);
                if (found)
                {
                    s_CheckedPath = path; s_CheckedVersion = version; s_CheckedError = null;
                    return path;
                }
                firstError ??= error;
            }
            version = null;
            error = !string.IsNullOrEmpty(set) ? firstError ?? "The Blender set in Project Settings > Brushes was not found."
                : "Blender was not found. Install it from blender.org (free), or set where it is in Project Settings > Brushes.";
            return null;
        }

        /// <summary>Forget the Blender found, so the next export looks again (after an install, or a new path).</summary>
        public static void Forget() { s_CheckedPath = null; s_CheckedVersion = null; s_CheckedError = null; }

        /// <summary>The program inside a path given as an application bundle on macOS (Blender.app).</summary>
        static string Executable(string path)
        {
            if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".app/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(path.TrimEnd('/'), "Contents", "MacOS", "Blender");
            return path;
        }

        /// <summary>Where Blender is installed by its installers, the stores and Steam, per platform.</summary>
        static IEnumerable<string> UsualPlaces()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                {
                    // Spotlight knows the app by its bundle id, wherever it is and whatever it is called
                    foreach (var app in Run("/usr/bin/mdfind", new[] { "kMDItemCFBundleIdentifier == 'org.blenderfoundation.blender'" }, 5, out _).Split('\n'))
                        if (app.Trim().Length > 0) yield return Executable(app.Trim());
                    yield return "/Applications/Blender.app/Contents/MacOS/Blender";
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Applications/Blender.app/Contents/MacOS/Blender");
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender");
                    break;
                }
                case RuntimePlatform.WindowsEditor:
                {
                    // the program that opens .blend files: set by the installer and the Microsoft Store
                    var command = Run("reg", new[] { "query", @"HKCR\blendfile\shell\open\command", "/ve" }, 5, out _);
                    int q = command.IndexOf('"');
                    if (q >= 0) { int e = command.IndexOf('"', q + 1); if (e > q) yield return command.Substring(q + 1, e - q - 1).Replace("blender-launcher.exe", "blender.exe"); }
                    foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                    {
                        var foundation = Path.Combine(root, "Blender Foundation");
                        if (!Directory.Exists(foundation)) continue;
                        var dirs = Directory.GetDirectories(foundation);
                        Array.Sort(dirs); Array.Reverse(dirs); // newest first
                        foreach (var d in dirs) yield return Path.Combine(d, "blender.exe");
                    }
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Blender\blender.exe");
                    break;
                }
                default:
                {
                    var which = Run("/usr/bin/which", new[] { "blender" }, 5, out _).Trim();
                    if (which.Length > 0) yield return which;
                    yield return "/usr/bin/blender";
                    yield return "/snap/bin/blender";
                    yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".steam/steam/steamapps/common/Blender/blender");
                    break;
                }
            }
        }

        /// <summary>Does the program run as Blender, and which version is it.</summary>
        static bool Check(string path, out Version version, out string error)
        {
            version = null;
            if (!File.Exists(path)) { error = "Blender was not found at " + path + "."; return false; }
            var output = Run(path, new[] { "--factory-startup", "--version" }, 30, out bool ran);
            if (!ran) { error = "Blender at " + path + " did not start."; return false; }
            var match = System.Text.RegularExpressions.Regex.Match(output, @"Blender\s+(\d+)\.(\d+)");
            if (!match.Success) { error = path + " is not Blender (it did not report a Blender version)."; return false; }
            version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            if (version < MinimumVersion) { error = $"Blender {version} is too old: {MinimumVersion} or newer is needed. Install a newer one from blender.org."; return false; }
            error = null;
            return true;
        }

        /// <summary>Run a program and return what it printed (output and errors); <paramref name="ran"/> is false when it didn't start or finish in time.</summary>
        static string Run(string file, string[] args, int timeoutSeconds, out bool ran)
        {
            ran = false;
            try
            {
                var info = new ProcessStartInfo(file, string.Join(" ", Array.ConvertAll(args, Quote))) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using var p = Process.Start(info);
                if (p == null) return "";
                var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutSeconds * 1000)) { try { p.Kill(); } catch { } return ""; }
                ran = p.ExitCode == 0;
                return stdout.Result + "\n" + stderr.Result;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>An argument quoted for the command line (spaces in paths; quotes and backslashes before them escaped).</summary>
        static string Quote(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in a)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ saving

        [Serializable] class Data { public List<Obj> objects = new List<Obj>(); public List<Mat> materials = new List<Mat>(); }
        [Serializable] class Obj { public string name; public float[] vertices, normals, uv0, uv1; public int[] triangles, faceMaterial, materials; }
        [Serializable] class Mat { public string name, texture; public float[] color; }

        /// <summary>
        /// Save the group's meshes (built first if they are out of date) to a Blender file at <paramref name="path"/>,
        /// replacing it. In the group's own space: its origin is Blender's origin. Null when saved, else what went wrong.
        /// </summary>
        public static string Save(BrushGroup group, string path)
        {
            var blender = FindBlender(out _, out var error);
            if (blender == null) return error;
            BrushApi.ForceUpdate();
            var data = Collect(group);
            if (data.objects.Count == 0) return "The Brush Group has no mesh to save: it builds no brushes.";
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Directory.Exists(folder)) return "The folder " + folder + " does not exist.";
            var temp = Path.Combine(Path.GetTempPath(), "csgbrush-blender-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string dataPath = Path.Combine(temp, "group.json"), scriptPath = Path.Combine(temp, "build.py");
                File.WriteAllText(dataPath, JsonUtility.ToJson(data));
                File.WriteAllText(scriptPath, Script);
                var output = Run(blender, new[] { "--background", "--factory-startup", "--python-exit-code", "1", "--python", scriptPath, "--", dataPath, Path.GetFullPath(path) }, TimeoutSeconds, out bool ran);
                if (ran && output.Contains(OkMarker) && File.Exists(path)) return null;
                if (!ran && output.Length == 0) return $"Blender did not finish saving within {TimeoutSeconds} s, or did not start.";
                Debug.LogWarning("CSG Brush: Blender's output while saving " + path + ":\n" + output);
                return "Blender could not save the file. Its output is in the Console.";
            }
            finally { try { Directory.Delete(temp, true); } catch { } }
        }

        /// <summary>The group's render meshes in Blender's coordinates, relative to the group: positions, normals, both UV sets, triangles and materials.</summary>
        static Data Collect(BrushGroup group)
        {
            var data = new Data();
            var materialIndex = new Dictionary<Material, int>();
            var toGroup = group.transform.worldToLocalMatrix;
            foreach (var t in BrushCsg.MeshObjects(group))
            {
                if (!t.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null || mf.sharedMesh.vertexCount == 0) continue;
                var mesh = mf.sharedMesh; var mats = t.TryGetComponent<MeshRenderer>(out var mr) ? mr.sharedMaterials : new Material[0];
                var m = toGroup * t.localToWorldMatrix;
                var v = mesh.vertices; var n = mesh.normals;
                var obj = new Obj { name = group.name + (t.name == BrushGroup.MeshChildName ? "" : " " + t.name.Trim('<', '[', ']', '>')), vertices = new float[v.Length * 3], normals = new float[v.Length * 3] };
                for (int i = 0; i < v.Length; i++)
                {
                    var p = m.MultiplyPoint3x4(v[i]); var q = n.Length == v.Length ? m.MultiplyVector(n[i]).normalized : Vector3.up;
                    obj.vertices[i * 3] = p.x; obj.vertices[i * 3 + 1] = p.z; obj.vertices[i * 3 + 2] = p.y; // Unity (x, y, z) is Blender (x, z, y)
                    obj.normals[i * 3] = q.x; obj.normals[i * 3 + 1] = q.z; obj.normals[i * 3 + 2] = q.y;
                }
                obj.uv0 = Flatten(mesh, 0); obj.uv1 = Flatten(mesh, 1);
                var tris = new List<int>(); var faceMaterial = new List<int>(); var slots = new List<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var mat = s < mats.Length ? mats[s] : null;
                    int mi = -1;
                    if (mat != null && !materialIndex.TryGetValue(mat, out mi)) { mi = data.materials.Count; materialIndex[mat] = mi; data.materials.Add(Describe(mat)); }
                    int slot = slots.IndexOf(mi); if (slot < 0) { slot = slots.Count; slots.Add(mi); }
                    var st = mesh.GetTriangles(s);
                    // each triangle reversed: Unity and Blender both take a face's front from cross(b - a, c - a), and swapping
                    // two axes mirrors that, so the order that faced out in Unity faces in after the swap
                    for (int k = 0; k + 2 < st.Length; k += 3) { tris.Add(st[k]); tris.Add(st[k + 2]); tris.Add(st[k + 1]); }
                    for (int k = 0; k < st.Length / 3; k++) faceMaterial.Add(slot);
                }
                obj.triangles = tris.ToArray(); obj.faceMaterial = faceMaterial.ToArray(); obj.materials = slots.ToArray();
                data.objects.Add(obj);
            }
            return data;
        }

        static float[] Flatten(Mesh mesh, int channel)
        {
            if (!mesh.HasVertexAttribute(channel == 0 ? UnityEngine.Rendering.VertexAttribute.TexCoord0 : UnityEngine.Rendering.VertexAttribute.TexCoord1)) return new float[0];
            var uv = new List<Vector2>(); mesh.GetUVs(channel, uv);
            var f = new float[uv.Count * 2];
            for (int i = 0; i < uv.Count; i++) { f[i * 2] = uv[i].x; f[i * 2 + 1] = uv[i].y; }
            return f;
        }

        static Mat Describe(Material material)
        {
            var c = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : material.HasProperty("_Color") ? material.color : Color.white;
            string texture = "";
            var tex = material.mainTexture;
            if (tex != null) { var asset = AssetDatabase.GetAssetPath(tex); if (!string.IsNullOrEmpty(asset)) texture = Path.GetFullPath(asset); }
            return new Mat { name = material.name, texture = texture, color = new[] { c.r, c.g, c.b, c.a } };
        }

        /// <summary>Run by Blender: builds the meshes from the data file and saves the .blend. Works on Blender 3.6 to 4.x.</summary>
        const string Script = @"import bpy, json, sys
args = sys.argv[sys.argv.index('--') + 1:]
data_path, out_path = args[0], args[1]
with open(data_path) as f:
    d = json.load(f)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
mats = []
for m in d['materials']:
    mat = bpy.data.materials.new(m['name'])
    c = m['color']
    mat.diffuse_color = (c[0], c[1], c[2], c[3])
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = (c[0], c[1], c[2], c[3])
        if m['texture']:
            try:
                tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
                tex.image = bpy.data.images.load(m['texture'], check_existing=True)
                mat.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
            except Exception as e:
                print('texture not loaded:', m['texture'], e)
    mats.append(mat)
for o in d['objects']:
    v, t = o['vertices'], o['triangles']
    me = bpy.data.meshes.new(o['name'])
    me.from_pydata([tuple(v[i:i + 3]) for i in range(0, len(v), 3)], [], [tuple(t[i:i + 3]) for i in range(0, len(t), 3)])
    for mi in o['materials']:
        me.materials.append(mats[mi] if mi >= 0 else None)
    me.polygons.foreach_set('material_index', o['faceMaterial'])
    loop_vertex = [0] * len(me.loops)
    me.loops.foreach_get('vertex_index', loop_vertex)
    for name, key in (('UVMap', 'uv0'), ('Lightmap', 'uv1')):
        uv = o[key]
        if uv:
            layer = me.uv_layers.new(name=name)
            layer.data.foreach_set('uv', [c for i in loop_vertex for c in (uv[i * 2], uv[i * 2 + 1])])
    if hasattr(me, 'use_auto_smooth'):
        me.use_auto_smooth = True
    n = o['normals']
    me.normals_split_custom_set_from_vertices([tuple(n[i:i + 3]) for i in range(0, len(n), 3)])
    me.update()
    ob = bpy.data.objects.new(o['name'], me)
    scene.collection.objects.link(ob)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=out_path)
print('" + OkMarker + @"')
";
    }
}

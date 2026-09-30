using System.Diagnostics;
using CsgBrush.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Tests
{
    /// <summary>
    /// What keeping track of brushes costs in a large level, per editor frame or GUI event, with nothing changed:
    /// the bookkeeping only, no CSG build. Run explicitly; the numbers go to the log as MGMT lines.
    /// </summary>
    public class BrushManagementProbe
    {
        const int Folders = 50, BrushesPerFolder = 100, OtherObjects = 5000;

        static string Time(string label, System.Action action, int repeats = 20)
        {
            action(); // warm up
            double best = double.MaxValue, total = 0;
            for (int i = 0; i < repeats; i++)
            {
                var sw = Stopwatch.StartNew(); action(); sw.Stop();
                best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds); total += sw.Elapsed.TotalMilliseconds;
            }
            return "MGMT " + label + ": mean " + (total / repeats).ToString("F3") + " ms, best " + best.ToString("F3") + " ms";
        }

        [Test, Explicit("measurement")]
        public void MeasureBookkeepingInALargeLevel()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int f = 0; f < Folders; f++)
            {
                var folder = new GameObject("Folder " + f).transform;
                for (int i = 0; i < BrushesPerFolder; i++)
                {
                    var go = new GameObject("Brush");
                    go.transform.SetParent(folder, false);
                    go.transform.position = new Vector3(f * 4f, 0f, i * 4f);
                    go.AddComponent<Brush>().size = new Vector3(2f, 2f, 2f);
                }
            }
            for (int i = 0; i < OtherObjects; i++) new GameObject("Prop " + i);
            BrushSnap.ProcessChanged(); // settle: new transforms count as changed once

            var log = new System.Text.StringBuilder();
            log.AppendLine("MGMT level: " + Folders * BrushesPerFolder + " brushes in " + Folders + " folders, " + OtherObjects + " other objects");
            log.AppendLine(Time("FindObjectsByType<Brush>", () => Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None)));
            log.AppendLine(Time("snap check (every editor update)", () => BrushSnap.ProcessChanged()));
            log.AppendLine(Time("rotated/scaled parents (every overlay GUI event)", () => BrushSnap.TransformedParents()));
            var ray = new Ray(new Vector3(-10f, 1f, 200f), Vector3.right); // across the whole level
            log.AppendLine(Time("pick along a ray through 50 brushes (worst case)", () => BrushHooks.PickBrushSurface(ray, out _, out _)));
            var down = new Ray(new Vector3(100f, 50f, 202f), Vector3.down); // between the brushes: a click on empty floor
            log.AppendLine(Time("pick where nothing is hit (each mouse move with a Create tool)", () => BrushHooks.PickBrushSurface(down, out _, out _)));
            var onto = new Ray(new Vector3(100f, 50f, 200f), Vector3.down); // onto one brush from above
            log.AppendLine(Time("pick onto one brush (a click)", () => BrushHooks.PickBrushSurface(onto, out _, out _)));
            var one = Object.FindFirstObjectByType<Brush>();
            log.AppendLine(Time("mark one brush's group dirty (every drag frame)", () => BrushCsg.MarkDirty(one)));
            log.AppendLine(Time("brushes by group (every rebuild)", () => BrushCsg.BrushesByModel()));
            log.AppendLine(Time("brushes by group after a hierarchy change (a walk)", () => { BrushCsg.InvalidateGrouping(); BrushCsg.BrushesByModel(); }, 5));
            Debug.Log(log.ToString());
        }

        [Test, Explicit("measurement")]
        public void MeasureADragFrameInALargeGroup([Values(250, 1000)] int count)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++) BrushApi.Create(BrushShape.Box, new Vector3((i % side) * 4f, 0f, (i / side) * 4f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            var loner = BrushApi.Create(BrushShape.Box, new Vector3(-50f, 0f, -50f), new Vector3(2f, 2f, 2f), Quaternion.identity, null, "Loner"); // touches nothing
            var full = Stopwatch.StartNew(); BrushApi.ForceUpdate(); full.Stop();
            var log = new System.Text.StringBuilder("MGMT drag frame, " + (count + 1) + " separate boxes in one group (first full build " + full.Elapsed.TotalMilliseconds.ToString("F0") + " ms)\n");
            var start = loner.transform.position;
            for (int frame = 0; frame < 3; frame++)
            {
                loner.transform.position = start + new Vector3(frame % 2 == 0 ? 1f : 0f, 0f, 0f);
                BrushSync.NotifyTransformChanged(loner);
                var group = BrushCsg.ModelOf(loner);
                var list = BrushCsg.BrushesByModel()[group];
                var sw = Stopwatch.StartNew(); BrushCsg.Rebuild(group, list); sw.Stop();
                log.Append("  frame " + frame + ": " + sw.Elapsed.TotalMilliseconds.ToString("F1") + " ms = solids " + BrushCsg.LastSolidsMs.ToString("F1") + " (built " + BrushCsg.LastBuiltSolids + "), pieces " + BrushCsg.LastPiecesMs.ToString("F1") + " (built " + BrushCsg.LastBuiltPieces + "), islands built " + BrushCsg.LastBuiltIslands + " of " + (BrushCsg.LastBuiltIslands + BrushCsg.LastCachedIslands) + ", union+mesh read " + BrushCsg.LastUnionMs.ToString("F1") + ", Unity mesh " + BrushCsg.LastMeshMs.ToString("F1") + ", colliders " + BrushCsg.LastCollidersMs.ToString("F1") + "; prepare = the rest\n");
            }
            Debug.Log(log.ToString());
        }
    }
}

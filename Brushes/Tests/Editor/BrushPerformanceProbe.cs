using System.Collections.Generic;
using System.Diagnostics;
using CsgBrush.Editor;
using CsgBrush.Colliders;
using CsgBrush.Colliders.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CsgBrush.Tests
{
    /// <summary>Where the time goes when editing: solids, pieces, the union, the Unity mesh and the convex colliders.</summary>
    public class BrushPerformanceProbe
    {
        static string Time(string label, System.Action action, int repeats = 1)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < repeats; i++) action();
            sw.Stop();
            return label + ": " + (sw.Elapsed.TotalMilliseconds / repeats).ToString("F1") + " ms";
        }

        static string Breakdown() => "solids " + BrushCsg.LastSolidsMs.ToString("F1") + " (cached " + BrushCsg.LastCachedSolids + ", built " + BrushCsg.LastBuiltSolids + "), pieces " + BrushCsg.LastPiecesMs.ToString("F1") + " (cached " + BrushCsg.LastCachedPieces + ", built " + BrushCsg.LastBuiltPieces + "), union " + BrushCsg.LastUnionMs.ToString("F1") + ", mesh " + BrushCsg.LastMeshMs.ToString("F1") + ", colliders " + BrushCsg.LastCollidersMs.ToString("F1") + " (pieces reused " + ConvexColliderBuilder.LastReusedPieces + ", created " + ConvexColliderBuilder.LastCreatedPieces + ", destroyed " + ConvexColliderBuilder.LastDestroyedPieces + ")";

        static string Scenario(string name, System.Func<Brush> makeCutter)
        {
            var sb = new System.Text.StringBuilder("PERF " + name + "\n");
            var cutter = makeCutter();
            BrushApi.ForceUpdate();
            int brushes = Object.FindObjectsByType<Brush>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            sb.Append("  brushes " + brushes + ", triangles " + BrushCsg.LastTriangles + ", colliders " + Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length + "\n");
            var start = cutter.transform.position; var pos = start;
            sb.Append("  " + Time("BrushSync.Ensure(cutter)", () => BrushSync.Ensure(cutter), 3) + "\n");
            // a drag frame: the cutter alternates between two overlapping positions, everything else is cached
            sb.Append("  " + Time("move + rebuild (a drag frame)", () => { pos = pos == start ? start + new Vector3(1f, 0f, 0f) : start; BrushApi.Move(cutter, pos); BrushApi.ForceUpdate(); }, 4) + "\n");
            sb.Append("    last: " + Breakdown() + "\n");
            sb.Append("  " + Time("rebuild with nothing changed", () => BrushApi.ForceUpdate(), 3) + "\n");
            sb.Append("    last: " + Breakdown() + "\n");
            sb.Append("  " + Time("full rebuild (caches cleared)", () => { BrushCsg.ClearCaches(); BrushApi.ForceUpdate(); }, 2) + "\n");
            sb.Append("    last: " + Breakdown() + "\n");
            return sb.ToString();
        }

        [Test]
        public void MeasureEditingCost()
        {
            BrushSettings.instance.snapToGrid = true;
            var log = new System.Text.StringBuilder();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < 10; i++) BrushApi.Create(BrushShape.Box, new Vector3(i * 2f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            log.Append(Scenario("10 boxes + 1 box subtract", () => { var c = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0.5f, 0f), new Vector3(3f, 1f, 3f), Quaternion.identity); BrushApi.SetOperation(c, BrushOperation.Subtract); return c; }));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sphere = BrushApi.Create(BrushShape.Sphere, Vector3.zero, new Vector3(6f, 6f, 6f), Quaternion.identity);
            sphere.tessellation = 2; BrushSync.Ensure(sphere);
            log.Append(Scenario("sphere (tessellation 2) + 1 box subtract", () => { var c = BrushApi.Create(BrushShape.Box, new Vector3(2f, 0f, 0f), new Vector3(2f, 2f, 2f), Quaternion.identity); BrushApi.SetOperation(c, BrushOperation.Subtract); return c; }));

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            for (int i = 0; i < 40; i++) BrushApi.Create(BrushShape.Box, new Vector3((i % 8) * 2f, 0f, (i / 8) * 2f), new Vector3(2f, 2f, 2f), Quaternion.identity);
            log.Append(Scenario("40 boxes + 1 box subtract", () => { var c = BrushApi.Create(BrushShape.Box, new Vector3(3f, 0.5f, 3f), new Vector3(3f, 1f, 3f), Quaternion.identity); BrushApi.SetOperation(c, BrushOperation.Subtract); return c; }));

            Debug.Log(log.ToString());
        }
    }
}

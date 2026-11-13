using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GloomhavenVR.Quest;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    static int assertions;
    public static int Assertions => assertions;
    static void Check(bool value, string message)
    { assertions++; if (!value) throw new Exception("FAIL " + message); }

    public static IEnumerator Run()
    {
        assertions = 0;
        const string prefabPath = "Assets/DiscoveryPopulation.prefab";
        var authored = new GameObject("Imported prefab population");
        Check(authored.AddComponent<DiscoveryWidget>() != null, "runtime fixture MonoBehaviour can attach");
        for (int index = 0; index < 4000; index++)
        {
            var child = new GameObject("Imported component " + index);
            child.AddComponent<DiscoveryWidget>(); child.transform.SetParent(authored.transform, false);
        }
        GameObject imported = PrefabUtility.SaveAsPrefabAsset(authored, prefabPath);
        Object.DestroyImmediate(authored);
        Check(imported != null && !imported.scene.IsValid(), "actual imported prefab has no live scene");
        var anchor = new GameObject("Persistent adapter owner");
        var owner = anchor.AddComponent<DiscoveryWidget>();
        Object.DontDestroyOnLoad(anchor);
        var root = new GameObject("Native menu root");
        var rootWidget = root.AddComponent<DiscoveryWidget>();
        var ordered = new List<MonoBehaviour> { rootWidget };
        for (int index = 0; index < 120; index++)
        {
            var child = new GameObject("Native widget " + index); ordered.Add(child.AddComponent<DiscoveryWidget>());
            child.transform.SetParent(root.transform, false);
        }
        var inactive = new GameObject("Inactive native widget"); var inactiveWidget = inactive.AddComponent<DiscoveryWidget>();
        ordered.Add(inactiveWidget); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
        var discovery = new QuestSceneObjects(owner);
        var result = new List<MonoBehaviour>(); var scratch = new List<MonoBehaviour>();
        discovery.Collect(result, scratch);
        Check(result.Contains(rootWidget) && result.Contains(inactiveWidget), "actual inactive native widget discovered");
        Check(result.Contains(owner), "actual persistent owner discovered");
        Check(!result.Contains(imported!.GetComponent<DiscoveryWidget>()) && result.Count < 1000, "live-scene census excludes imported prefab assets");
        int first = result.IndexOf(rootWidget);
        for (int index = 0; index < ordered.Count; index++)
            Check(result[first + index] == ordered[index], "actual deterministic native hierarchy order preserved");
        var delayed = new GameObject("Delayed native widget"); delayed.transform.SetParent(root.transform, false);
        var delayedWidget = delayed.AddComponent<DiscoveryWidget>();
        discovery.Collect(result, scratch);
        Check(result.Contains(delayedWidget), "actual delayed native widget discovered");
        Scene additional = SceneManager.CreateScene("Additive native menu " + typeof(InteractionProgram).Assembly.GetName().Name);
        var additive = new GameObject("Additive native widget"); var additiveWidget = additive.AddComponent<DiscoveryWidget>();
        SceneManager.MoveGameObjectToScene(additive, additional);
        discovery.Collect(result, scratch);
        Check(result.Contains(additiveWidget), "actual additive scene widget discovered");
        // Move the still-alive object out before unloading. This prices the
        // collector's scene boundary separately from Unity's fake-null behavior.
        SceneManager.MoveGameObjectToScene(additive, SceneManager.GetActiveScene());
        AsyncOperation unloading = SceneManager.UnloadSceneAsync(additional);
        while (!unloading.isDone) yield return null;
        Check(!additional.isLoaded, "actual additive scene unload completed before discovery");
        discovery.Collect(result, scratch);
        Check(result.Contains(additiveWidget), "native object moved out of unloading scene remains discoverable");
        Object.DestroyImmediate(additive);
        discovery.Collect(result, scratch);
        Check(!result.Contains(additiveWidget), "destroyed native widget removed from discovery");
        var reparented = new GameObject("Moved native widget");
        var movedWidget = reparented.AddComponent<DiscoveryWidget>();
        reparented.transform.SetParent(inactive.transform, false);
        discovery.Collect(result, scratch);
        Check(result.Contains(movedWidget), "late widget under inactive parent discovered");
        var future = new GameObject("Unrecognized future native option"); var futureWidget = future.AddComponent<DiscoveryWidget>();
        future.transform.SetParent(root.transform, false);
        discovery.Collect(result, scratch);
        Check(result.Contains(futureWidget), "future original hierarchy remains discoverable");
        for (int warm = 0; warm < 10; warm++) discovery.Collect(result, scratch);
        int population = result.Count;
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < 200; index++) discovery.Collect(result, scratch);
        double scopedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 200;
        int allPopulation = 0;
        started = Stopwatch.GetTimestamp();
        for (int index = 0; index < 100; index++)
            allPopulation = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Length;
        double allMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 100;
        Check(allPopulation > population + 3900, "CPU comparison includes actual non-scene imported asset population");
        Check(result.Count == population, "repeated discovery preserves complete live population");
        Console.WriteLine("DISCOVERY CPU: scoped " + scopedMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
            + " ms / " + population + " live components; B620 all-assets route "
            + allMs.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " ms / " + allPopulation
            + " loaded components; Unity=" + Application.unityVersion + "; no Android or headset timing claim.");
        // Keep a compact measurement in the shared runner's output, without
        // claiming the desktop CPU ratio establishes a Quest frame-time gain.
        string? report = Environment.GetEnvironmentVariable("QUEST_DISCOVERY_CPU_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report,
            "scopedMs=" + scopedMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "\nallAssetsMs=" + allMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "\nliveComponents=" + population + "\nallComponents=" + allPopulation + "\n");
        Object.DestroyImmediate(root); Object.DestroyImmediate(anchor);
        AssetDatabase.DeleteAsset(prefabPath);
        yield break;
    }
}

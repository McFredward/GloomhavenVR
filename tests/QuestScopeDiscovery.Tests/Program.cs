using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
    static readonly Type[] Types = { typeof(ScopeOwnerA), typeof(ScopeOwnerB), typeof(ScopeOwnerC), typeof(ScopeOwnerD), typeof(ScopeOwnerE), typeof(ScopeOwnerF), typeof(ScopeOwnerG) };
    static T Owner<T>(string name, Transform? parent = null) where T : Component
    {
        var gameObject = new GameObject(name); if (parent != null) gameObject.transform.SetParent(parent, false);
        return gameObject.AddComponent<T>();
    }
    static void Equivalent(QuestSceneObjects previous, QuestScopeObjects targeted, List<MonoBehaviour> old, List<MonoBehaviour> scratch, List<MonoBehaviour> actual)
    {
        previous.Collect(old, scratch); targeted.Collect(actual);
        var expected = new HashSet<int>(old.Where(value => value != null && Types.Contains(value.GetType())).Select(value => value.GetInstanceID()));
        Check(expected.SetEquals(actual.Select(value => value.GetInstanceID())), "actual B621 and targeted scope return identical exact live owners");
        Check(actual.Count == expected.Count, "targeted scope does not duplicate native owners");
    }
    public static IEnumerator Run()
    {
        assertions = 0;
        const string prefabPath = "Assets/TargetedScopePrefab.prefab";
        var authored = new GameObject("Imported purchase-owner prefab"); authored.AddComponent<ScopeOwnerA>();
        for (int index = 0; index < 256; index++) Owner<UnrelatedScopeOwner>("Imported unrelated " + index, authored.transform);
        var imported = PrefabUtility.SaveAsPrefabAsset(authored, prefabPath);
        Object.DestroyImmediate(authored);
        Check(imported != null && !imported.scene.IsValid(), "actual targeted prefab asset has no loaded scene");
        var root = new GameObject("Actual large menu hierarchy");
        for (int index = 0; index < 14338; index++) Owner<UnrelatedScopeOwner>("Unrelated runtime component " + index, root.transform);
        var active = Owner<ScopeOwnerA>("Original active owner", root.transform);
        var inactive = Owner<ScopeOwnerB>("Original inactive owner", root.transform); inactive.gameObject.SetActive(false);
        var persistent = Owner<ScopeOwnerC>("Original persistent owner"); Object.DontDestroyOnLoad(persistent.gameObject);
        var hidden = Owner<ScopeOwnerF>("Original DontSave owner", root.transform); hidden.hideFlags = HideFlags.HideAndDontSave;
        var future = Owner<FutureScopeOwner>("Future subclass stays native", root.transform);
        var last = Owner<ScopeOwnerG>("Original final owner", root.transform);
        var targeted = new QuestScopeObjects(Types); var previous = new QuestSceneObjects(persistent);
        var actual = new List<MonoBehaviour>(); var old = new List<MonoBehaviour>(); var scratch = new List<MonoBehaviour>();
        var beforeScope = persistent.gameObject.AddComponent<QuestGameScopeB621>(); beforeScope.enabled = false;
        var afterScope = persistent.gameObject.AddComponent<QuestGameScope>(); afterScope.enabled = false;
        typeof(QuestGameScope).GetField("scopeObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(afterScope, targeted);
        typeof(QuestGameScope).GetField("discoveryAttempted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(afterScope, true);
        Action beforeScan = (Action)Delegate.CreateDelegate(typeof(Action), beforeScope,
            typeof(QuestGameScopeB621).GetMethod("Scan", BindingFlags.Instance | BindingFlags.NonPublic)!);
        Action<double> afterScan = (Action<double>)Delegate.CreateDelegate(typeof(Action<double>), afterScope,
            typeof(QuestGameScope).GetMethod("Scan", BindingFlags.Instance | BindingFlags.NonPublic)!);

        long coldStarted = Stopwatch.GetTimestamp();
        targeted.Collect(actual);
        double coldTargetedMs = (Stopwatch.GetTimestamp() - coldStarted) * 1000d / Stopwatch.Frequency;
        Check(actual.Contains(inactive), "actual inactive scope owner retained");
        Check(actual.Contains(hidden), "actual DontSave scope owner retained");
        Check(actual.Contains(persistent), "actual DDOL scope owner retained");
        Check(!actual.Contains(imported!.GetComponent<ScopeOwnerA>()), "actual imported scope prefab remains untouched");
        Check(!actual.Contains(future), "actual future native subclass remains outside exact scope");
        Check(targeted.LastNativeObjectCount < 32, "native exact-type queries transfer only rare candidates among 14k behaviours");
        Equivalent(previous, targeted, old, scratch, actual);
        var delayed = Owner<ScopeOwnerD>("Late owner under inactive parent", inactive.transform);
        targeted.Collect(actual);
        Check(actual.Contains(delayed), "actual delayed inactive scope owner retained");
        Equivalent(previous, targeted, old, scratch, actual);
        Scene additive = SceneManager.CreateScene("Actual additive scope " + typeof(InteractionProgram).Assembly.GetName().Name);
        var added = Owner<ScopeOwnerE>("Original additive owner"); SceneManager.MoveGameObjectToScene(added.gameObject, additive);
        targeted.Collect(actual); Check(actual.Contains(added), "actual additive scope owner retained");
        Equivalent(previous, targeted, old, scratch, actual);
        added.gameObject.SetActive(false); targeted.Collect(actual);
        Check(actual.Contains(added), "actual pooled inactive owner remains registered");
        Object.DestroyImmediate(delayed.gameObject); targeted.Collect(actual);
        Check(!actual.Contains(delayed), "actual destroyed scope owner retired");
        AsyncOperation unloading = SceneManager.UnloadSceneAsync(additive);
        while (!unloading.isDone) yield return null;
        Check(!additive.isLoaded, "actual additive unload completed");
        targeted.Collect(actual); Check(!actual.Contains(added), "actual unloaded scope owner retired");
        Equivalent(previous, targeted, old, scratch, actual);
        for (int index = 0; index < 8; index++) { previous.Collect(old, scratch); targeted.Collect(actual); beforeScan(); }
        int population = old.Count, matched = actual.Count, returned = targeted.LastNativeObjectCount;
        Check(population >= 14338, "CPU fixture contains genuine 14k live MonoBehaviours");
        long started = Stopwatch.GetTimestamp();
        for (int index = 0; index < 100; index++) previous.Collect(old, scratch);
        double oldMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 100;
        started = Stopwatch.GetTimestamp();
        for (int index = 0; index < 100; index++) targeted.Collect(actual);
        double targetedMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 100;
        started = Stopwatch.GetTimestamp();
        long probeAllocated = GC.GetAllocatedBytesForCurrentThread();
        var allocationProbe = new byte[8192]; GC.KeepAlive(allocationProbe);
        probeAllocated = GC.GetAllocatedBytesForCurrentThread() - probeAllocated;
        bool allocationCounterReliable = probeAllocated >= 8192;
        long oldHeapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        long oldAllocated = GC.GetAllocatedBytesForCurrentThread();
        int oldCollections = GC.CollectionCount(0);
        for (int index = 0; index < 100; index++) beforeScan();
        oldAllocated = GC.GetAllocatedBytesForCurrentThread() - oldAllocated;
        oldCollections = GC.CollectionCount(0) - oldCollections;
        long oldHeapDelta = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - oldHeapBefore;
        double oldScopeMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 100;
        started = Stopwatch.GetTimestamp();
        long newHeapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        long newAllocated = GC.GetAllocatedBytesForCurrentThread();
        int newCollections = GC.CollectionCount(0);
        for (int index = 0; index < 100; index++) afterScan(10d * index);
        newAllocated = GC.GetAllocatedBytesForCurrentThread() - newAllocated;
        newCollections = GC.CollectionCount(0) - newCollections;
        long newHeapDelta = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - newHeapBefore;
        double newScopeMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency / 100;
        double steadyWorst = 0, steadyTotal = 0, initialMs;
        var perTypeWorst = new double[Types.Length];
        var previousQuery = new double[Types.Length];
        var familyCounts = new int[Types.Length];
        var phased = new QuestScopeObjects(Types);
        typeof(QuestGameScope).GetField("scopeObjects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(afterScope, phased);
        started = Stopwatch.GetTimestamp(); afterScan(0d);
        initialMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        Check(phased.LastQueryTypeCount == 7, "initial production binding retains one measured seven-family batch");
        long steadyHeapBefore = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        long steadyAllocated = GC.GetAllocatedBytesForCurrentThread();
        for (int tick = 1; tick <= 140; tick++)
        {
            double now = tick / 7d + .000001d;
            started = Stopwatch.GetTimestamp(); afterScan(now);
            double tickMs = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            Check(phased.LastQueryTypeCount == 1, "normal production discovery tick has one atomic native type query");
            int family = phased.LastQueryFamilyIndex;
            if (familyCounts[family] != 0)
                Check(now - previousQuery[family] <= 1.000001d, "each known family retains its absolute one-second discovery cadence");
            previousQuery[family] = now; familyCounts[family]++;
            perTypeWorst[family] = Math.Max(perTypeWorst[family], tickMs);
            steadyWorst = Math.Max(steadyWorst, tickMs); steadyTotal += tickMs;
        }
        steadyAllocated = GC.GetAllocatedBytesForCurrentThread() - steadyAllocated;
        long steadyHeapDelta = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - steadyHeapBefore;
        Check(familyCounts.All(count => count == 20), "all seven known native families receive scheduled production queries");
        Check(!phased.NeedsScan(20.01d), "normal intermediate frames do not run a native query");
        // Adding a component to an existing GameObject has no hierarchy event.
        // The named-family query must find it by its next ordinary deadline.
        var existing = new GameObject("Existing inactive object receives a late scope component");
        existing.transform.SetParent(root.transform, false); existing.SetActive(false);
        var attached = existing.AddComponent<ScopeOwnerD>();
        bool foundAttached = false;
        for (int tick = 141; tick <= 147; tick++)
        {
            phased.CollectDue(tick / 7d + .000001d, actual);
            if (actual.Contains(attached)) foundAttached = true;
        }
        Check(foundAttached, "scheduled discovery finds late AddComponent on an existing inactive GameObject");
        phased.CollectDue(100d, actual);
        Check(phased.LastQueryTypeCount == 7 && phased.MatchedOwnerCount >= 6, "forward clock jump catches up all seven families once");
        phased.CollectDue(100d, actual);
        Check(phased.LastQueryTypeCount == 0, "same-frame stalled-clock catch-up never replays old cycles");
        for (int tick = 1; tick <= 7; tick++)
        {
            phased.CollectDue(100d + tick / 7d + .000001d, actual);
            Check(phased.LastQueryTypeCount == 1, "forward-jump recovery retains staggered normal native queries");
        }
        phased.CollectDue(0d, actual);
        Check(phased.LastQueryTypeCount == 1, "backward clock rephases known families without a new bootstrap batch");
        var lowFpsFamilies = new HashSet<int>();
        for (int frame = 1; frame <= 5; frame++)
        {
            phased.CollectDue(frame * .2d, actual);
            Check(phased.LastQueryTypeCount <= 2, "low-FPS tick catches up only the known due families");
            // All seven original deadlines have advanced after the one-second span.
            lowFpsFamilies.Add(phased.LastQueryFamilyIndex);
        }
        Check(phased.NeedsScan(1.01d) == false, "low-FPS scheduling misses no deadline after a complete known-family cycle");
        Object.DestroyImmediate(existing);
        string facts = "oldSceneMs=" + oldMs.ToString("R", CultureInfo.InvariantCulture)
            + "\ntargetedMs=" + targetedMs.ToString("R", CultureInfo.InvariantCulture)
            + "\ncoldTargetedMs=" + coldTargetedMs.ToString("R", CultureInfo.InvariantCulture)
            + "\nallocationCounterReliable=" + allocationCounterReliable + "\nknown8192ByteAllocationCounterDelta=" + probeAllocated
            + "\noldScopeAllocatedBytesPerScan=" + (allocationCounterReliable ? (oldAllocated / 100d).ToString(CultureInfo.InvariantCulture) : "unsupported")
            + "\nnewScopeAllocatedBytesPerScan=" + (allocationCounterReliable ? (newAllocated / 100d).ToString(CultureInfo.InvariantCulture) : "unsupported")
            + "\nold100ScanMonoUsedHeapDelta=" + oldHeapDelta + "\nnew100CatchupCycleMonoUsedHeapDelta=" + newHeapDelta
            + "\nsteady140TickMonoUsedHeapDelta=" + steadyHeapDelta
            + "\nheapCounterLimit=Global retained/rounded Mono heap delta, not exact per-call allocation; no zero-allocation claim from unsupported thread counter."
            + "\noldScopeGen0Collections=" + oldCollections + "\nnewScopeGen0Collections=" + newCollections
            + "\ninitialBatchMs=" + initialMs.ToString("R", CultureInfo.InvariantCulture)
            + "\nsteadyWorstTickMs=" + steadyWorst.ToString("R", CultureInfo.InvariantCulture)
            + "\nsteadyMeanTickMs=" + (steadyTotal / 140).ToString("R", CultureInfo.InvariantCulture)
            + "\nsteadyWholeCycleMeanMs=" + (steadyTotal / 20).ToString("R", CultureInfo.InvariantCulture)
            + "\nsteadyAllocatedBytesPerTick=" + (allocationCounterReliable ? (steadyAllocated / 140d).ToString(CultureInfo.InvariantCulture) : "unsupported")
            + "\nperTypeWorstMs=" + string.Join(",", perTypeWorst.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))
            + "\noldScopeScanMs=" + oldScopeMs.ToString("R", CultureInfo.InvariantCulture)
            + "\nnewCatchupCycleMs=" + newScopeMs.ToString("R", CultureInfo.InvariantCulture)
            + "\nliveMonoBehaviours=" + population + "\nmatchedOwners=" + matched
            + "\nnativeQueryCandidates=" + returned + "\nUnity=" + Application.unityVersion
            + "\nboundary=Real production native typed-query versus B621 scene-root collector; no Android or headset timing claim.\n";
        string? report = Environment.GetEnvironmentVariable("QUEST_DISCOVERY_CPU_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, facts);
        Console.WriteLine("TARGETED SCOPE CPU: " + targetedMs.ToString("F4", CultureInfo.InvariantCulture)
            + " ms collector / " + returned + " native candidates / " + matched + " owners; production Scope.Scan "
            + newScopeMs.ToString("F4", CultureInfo.InvariantCulture) + " ms vs B621 "
            + oldScopeMs.ToString("F4", CultureInfo.InvariantCulture) + " ms / " + population + " live MonoBehaviours; no Android timing claim.");
        // Unity Editor/Mono natively prices typed resource searches differently
        // from the Quest Debug IL2CPP per-component path. Record actual costs;
        // neither a forced desktop ratio nor a fabricated headset gain is proof.
        Check(targeted.MatchedOwnerCount == matched && targeted.LastNativeObjectCount == returned,
            "priced repeated targeted discovery retains its exact bounded candidate population");
        Object.DestroyImmediate(root); Object.DestroyImmediate(persistent.gameObject); AssetDatabase.DeleteAsset(prefabPath);
        yield break;
    }
}

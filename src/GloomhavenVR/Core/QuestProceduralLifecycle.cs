using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// B679's first Tutorial has active, populated entities but zero native handles,
/// empty generated roots and a non-running ApparanceEngine component. The engine
/// and reviewed game owners never disable that component: its ordinary Update
/// creates/checks entities and drains the original native task stream. Initializing
/// the worker alone cannot replace that Update. The capture does not establish
/// whether the component is disabled or an ancestor/duplicate owns the problem.
///
/// Recover only the former, after a stable, fully requested initial scenario and
/// only on the unique original scene owner. Respect pause/background, scene teardown,
/// frozen/hidden entities, completed/generated content and existing native handles.
/// Re-enable that one component once; let Unity invoke its original Start/Update.
/// Never invoke generation manually, activate a root, reveal a renderer, fabricate
/// placement readiness, reset the worker or repeatedly fight another disable owner.
/// The next headset capture must establish whether this conditional repair applies.
/// </summary>
internal static class QuestProceduralLifecycle
{
    private const float GraceSeconds = 3f;
    private const int RepairLimit = 8;
    private static Driver? _driver;
    private static readonly HashSet<int> Attempted = new();
    private static ApparanceEngine? _candidate;
    private static float _due;
    private static bool _ignoredFreeformSuppression;

    internal static void Install(bool ignoredFreeformSuppression)
    {
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || _driver != null) return;
        _ignoredFreeformSuppression = ignoredFreeformSuppression;
        var owner = new GameObject("GloomhavenVR.QuestProceduralLifecycle");
        UnityEngine.Object.DontDestroyOnLoad(owner);
        _driver = owner.AddComponent<Driver>();
    }

    internal static void Uninstall()
    {
        if (_driver != null) UnityEngine.Object.Destroy(_driver.gameObject);
        _driver = null;
        _candidate = null;
        _due = 0f;
        _ignoredFreeformSuppression = false;
        Attempted.Clear();
        // Successful recovery restored the native engine's required lifecycle,
        // like MaterialLoaderHeal. Do not disable it again on VR shutdown.
    }

    private sealed class Driver : MonoBehaviour
    {
        private float _next;
        private bool _paused;
        private bool _failureLogged;
        private void OnApplicationPause(bool paused) => _paused = paused;
        private void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + .5f;
            try { Tick(now, Application.isFocused, _paused); }
            catch (Exception error)
            {
                _candidate = null;
                if (_failureLogged) return;
                _failureLogged = true;
                VRLog.Warn("QuestLifecycle", "Native procedural lifecycle observation failed once; original engine ownership retained: " + error);
            }
        }
    }

    // Foreground/pause are external application boundaries. All native scene,
    // entity and component decisions remain inside this real path.
    internal static void Tick(float now, bool focused, bool paused)
    {
        ApparanceEngine engine = ApparanceEngine.Instance;
        SceneController loading = SceneController.Instance;
        bool game = SceneManager.GetSceneByName("Game").isLoaded || SceneManager.GetSceneByName("Game_gamepad").isLoaded;
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !focused || paused || !game
            || !SceneManager.GetSceneByName("ProcGen").isLoaded || loading == null || loading.IsLoading || loading.ScenarioIsLoading
            || engine == null || engine.enabled || !engine.gameObject.activeInHierarchy
            || engine.gameObject.scene.name != "Gloomhaven_unified" || engine.Resources == null
            || Attempted.Contains(engine.GetInstanceID()) || Attempted.Count >= RepairLimit)
        { _candidate = null; return; }

        ApparanceEngine[] engines = UnityEngine.Object.FindObjectsOfType<ApparanceEngine>(true);
        if (engines.Length != 1 || engines[0] != engine || !InitialGenerationRequested(engine))
        { _candidate = null; return; }
        if (_candidate != engine) { _candidate = engine; _due = now + GraceSeconds; return; }
        if (now < _due) return;

        // Mark before the one write. A throwing setter or a second owner cannot
        // cause periodic retries. No worker/asset/scene ownership is transferred.
        Attempted.Add(engine.GetInstanceID());
        _candidate = null;
        engine.enabled = true;
        VRLog.Warn("QuestLifecycle", "Restored the unique disabled native procedural engine component for initial scenario generation after a stable 3s request; original Start/Update now own continuation. No renderer, entity parameter or worker was changed. instance="
            + engine.GetInstanceID() + " active=" + engine.isActiveAndEnabled);
    }

    private static bool InitialGenerationRequested(ApparanceEngine engine)
    {
        if (engine.EntityComponents == null || engine.EntityComponents.Count == 0) return false;
        foreach (ApparanceEntity registered in engine.EntityComponents)
            if (registered != null && registered.m_EntityHandle != 0) return false;
        bool requested = false;
        foreach (ProceduralMapTile tile in UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>(true))
        {
            if (tile == null) continue;
            Transform generated = tile.transform.Find("Generated Content");
            if (tile.IsPlacementCompleted || (generated != null && generated.childCount != 0)) return false;
            ApparanceEntity entity = tile.GetComponent<ApparanceEntity>();
            if (tile.visibility == ProceduralMapTile.Visibility.All && tile.isActiveAndEnabled && generated != null
                && generated.gameObject.activeInHierarchy && entity != null && entity.isActiveAndEnabled
                && entity.IsPopulated && !entity.Frozen && entity.ProcedureID != 0
                && entity.m_EntityHandle == 0 && !entity.IsBusy && engine.EntityComponents.Contains(entity)) requested = true;
        }
        return requested;
    }

    internal static string Describe(ApparanceEngine? engine)
    {
        if (engine == null) return "instance=none";
        ApparanceEngine[] engines = UnityEngine.Object.FindObjectsOfType<ApparanceEngine>(true);
        int active = 0;
        string others = "";
        foreach (ApparanceEngine item in engines)
        {
            if (item.isActiveAndEnabled) active++;
            if (others.Length < 220) others += item.GetInstanceID() + ":" + Clean(item.name, 32) + ":" + item.enabled + ":" + item.gameObject.activeInHierarchy + ";";
        }
        return "instance=" + engine.GetInstanceID() + " name=" + Clean(engine.name, 48) + " scene=" + Clean(engine.gameObject.scene.name, 48)
            + " component=" + engine.enabled + " self=" + engine.gameObject.activeSelf + " hierarchy=" + engine.gameObject.activeInHierarchy
            + " path=" + OwnerPath(engine.transform) + " inactiveOwner=" + InactiveOwner(engine.transform) + " loaded=" + engines.Length + " active=" + active
            + " owners=" + others + " resources=" + (engine.Resources != null) + " codeStarted=" + CodeStarted(engine)
            + " legacySuppressionIgnored=" + _ignoredFreeformSuppression
            + " recovery=" + (Attempted.Contains(engine.GetInstanceID()) ? "attempted" : _candidate == engine ? "stable-request-wait" : "not-admitted");
    }

    private static string CodeStarted(ApparanceEngine engine)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Type type = typeof(ApparanceEngine);
        object? current = type.GetField("s_LastRunTime", flags)?.GetValue(null);
        object? seen = type.GetField("m_LastRunTime", flags)?.GetValue(engine);
        return current is long now && seen is long previous ? (now == previous).ToString() : "unknown";
    }

    private static string InactiveOwner(Transform source)
    {
        for (Transform current = source; current != null; current = current.parent)
            if (!current.gameObject.activeSelf) return Clean(current.name, 48);
        return "none";
    }
    private static string OwnerPath(Transform source)
    {
        string result = Clean(source.name, 32);
        for (Transform current = source.parent; current != null && result.Length < 96; current = current.parent)
            result = Clean(current.name, 24) + "/" + result;
        return Clean(result, 120);
    }
    private static string Clean(string? text, int bound) => string.IsNullOrEmpty(text) ? "none" : text!.Length > bound ? text.Substring(0, bound) : text;
}

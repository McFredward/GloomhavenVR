using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace GloomhavenVR.Core;

/// <summary>
/// ROUND-4 fix for the missing revealed-room geometry (fehlender_boden3.png): the room's
/// full-detail content now BUILDS at reveal (round 2/3, <see cref="ApparanceDetailFocus"/>)
/// — but 130 of its 133 renderers sit ACTIVE with <c>enabled=false</c> (hardware census,
/// LogOutput.log 1124-1129) and null <c>sharedMaterial</c> slots. That disabled-but-active,
/// materials-missing state is written by exactly ONE piece of code in the game:
/// <c>MaterialLoaderData.LoadMaterials()</c> disables the renderer, async-loads each
/// material through Addressables (<c>AssetReferenceT&lt;Material&gt;.LoadAssetAsync</c>),
/// and only <c>CheckAllMaterialLoaded()</c> — every handle completed with a non-null
/// Result — assigns the materials and re-enables it (decompiled MaterialLoaderData.cs).
///
/// WHY IT STRANDS (all read from the decompiled game/Addressables code; which branch fires
/// on hardware is what the census's new <c>ml=</c> tag proves):
/// <list type="bullet">
/// <item>NO RETRY, EVER: the only caller is <c>MaterialLoader.Start()</c> — once per
///   component lifetime, <c>async void</c>, deferred one frame behind <c>Task.Yield()</c>
///   when a <c>DetailsDisabler</c> sibling exists, and filtering entries by
///   <c>Renderer.gameObject.activeInHierarchy || ForceLoad</c> AT THAT MOMENT. A reveal-time
///   instance whose subtree is toggled by <c>ProceduralMapTile.ApplyVisibility</c> between
///   instantiation and the deferred frame has its entries skipped FOREVER (never-started).</item>
/// <item>NO FAILURE PATH: a handle that completes with a null <c>Result</c> makes
///   <c>CheckAllMaterialLoaded</c> early-return on every later completion — the renderer
///   stays disabled for the rest of the scenario (null-result).</item>
/// <item>SIZING DEADLOCK: with <c>IsSaveExistedMaterials</c> and ≥1 authored non-null
///   sharedMaterial, <c>_loadedMaterials</c> is sized count+existing but only [0..count)
///   is ever filled BEFORE the all-non-null check — the check can never pass (done-stuck).</item>
/// <item>DOUBLE-LOAD POISON (why this healer must NOT simply call the component-level
///   <c>MaterialLoader.LoadMaterials()</c>): the shipped Addressables'
///   <c>AssetReference.LoadAssetAsync</c> returns a DEFAULT (invalid) handle and logs an
///   error when its <c>m_Operation</c> is still valid, and subscribing <c>.Completed</c> on
///   an invalid handle throws — re-running a loader with healthy already-loaded entries
///   would poison them. Healing is therefore per-<c>MaterialLoaderData</c>, releasing each
///   still-valid per-reference operation (<c>AssetReference.ReleaseAsset()</c> clears
///   <c>m_Operation</c>, making a reload legal) before re-triggering.</item>
/// </list>
///
/// The start room never shows this because it loads during the loading screen with its
/// hierarchy stably active; 3 of the revealed tile's renderers DID complete mid-play, so
/// the Addressables pipeline itself works — the strands are per-entry, which is why a
/// per-entry watchdog is the right shape (and why four rounds of single-cause fixes each
/// left a residue: belt and braces this time).
///
/// WHAT THE WATCHDOG DOES: on a 1 s cadence, for every <see cref="MaterialLoader"/> under
/// an active <see cref="ProceduralMapTile"/>, find entries whose renderer is
/// <c>activeInHierarchy &amp;&amp; !enabled</c> with at least one null sharedMaterial slot
/// (the loader's mid-state signature — a renderer some OTHER system disabled has its
/// materials and is left alone), classify the load via the publicized
/// <c>_handles</c>/<c>_loadedMaterials</c>, and heal per state: never-started/null-result/
/// pending-forever → release + re-trigger <c>data.LoadMaterials()</c>; done-stuck → assign
/// the already-loaded materials and re-enable directly.
///
/// ROUND 5 (hardware log 2026-08-02, every stuck floor renderer <c>ml=done-stuck</c>): the
/// null-slot signature is only valid for the RE-TRIGGER states. In the done-stuck deadlock
/// the assignment never ran, so the renderer still carries its authored prefab placeholder
/// materials and looked "fully materialed" — the old global pre-gate therefore blocked the
/// exact heal it was built for (0 done-stuck heals on hardware). The gate is now per-state;
/// foreign disables are instead recognized by <see cref="MaterialsAlreadyAssigned"/>
/// (loader finished ⇒ its results are ON the renderer) plus a hard skip of anything under
/// a <c>UnityGameEditorDoorProp</c> (the game's own door hide unit). Genuinely in-flight loads are
/// NEVER touched (30 s grace), no entry is touched until it has been observed stuck for
/// 3 s (lets the game's own deferred Start land first), retries are throttled to one per
/// 5 s and capped at 5 per entry (then ONE error naming the assets). Zero behavior when
/// nothing is stuck.
///
/// MP-SAFE: local rendering only — materials/renderer state are never synced; peers run
/// their own loaders. REVERSIBLE: the healer only pushes the game's OWN load path to its
/// intended terminal state; there is nothing to restore on uninstall beyond dropping the
/// driver. TickGuard-safe: the tick is routed through <see cref="TickGuard.Run"/>.
/// </summary>
internal static class MaterialLoaderHeal
{
    /// <summary>Log scope — lines read <c>[Compat] MaterialLoaderHeal: …</c> so the heal
    /// activity greps together with the other compat fixups it ships beside.</summary>
    private const string Name = "Compat";

    private const string DriverName = "GloomhavenVR.MaterialLoaderHeal";

    private static HealDriver? _driver;

    /// <summary>Install the watchdog (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<HealDriver>();
        VRLog.Info(Name,
            "MaterialLoaderHeal installed — 1 s watchdog re-triggers map-tile MaterialLoaders "
            + "whose renderers are stuck active-but-disabled with unloaded materials "
            + "(reveal-time Addressables loads have no retry path in the game).");
    }

    /// <summary>Drop the watchdog. Nothing to restore: it only ever completed the game's
    /// own load path; healed renderers are exactly what the game intended to produce.</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    // ---------------------------------------------------------------------------------
    // Shared classification — used by the watchdog AND by WallSegmentFade's MAPTILE
    // census (the `ml=` tag on sampled disabled renderers), so the diagnosis on the next
    // hardware log and the heal decision are provably the same logic.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// One-token loader state for a renderer, for the census line: <c>no-loader</c> (no
    /// MaterialLoader entry references it), <c>never-started</c> (entry exists, load never
    /// ran — the Start-time activeInHierarchy filter skipped it), <c>loading a/b</c>
    /// (handles in flight), <c>null-result a/b</c> (a handle completed without a material
    /// — no retry exists in the game), <c>done-stuck</c> (every handle loaded but the
    /// renderer was never re-enabled — the CheckAllMaterialLoaded sizing deadlock),
    /// <c>done</c>, or <c>empty-refs</c> (an entry with no material references at all —
    /// the healer refuses these: LoadMaterials would disable the renderer with zero
    /// handles to ever re-enable it).
    /// </summary>
    internal static string DescribeForRenderer(Renderer renderer)
    {
        MaterialLoaderData? data = FindLoaderData(renderer, out _);
        if (data == null)
            return "no-loader";
        string described = Describe(data, renderer.enabled);
        // Census nuance (round 5): a disabled renderer whose loaded materials are already
        // assigned is NOT the healer's done-stuck target — another system disabled it
        // (door wings). Name it distinctly so the log matches the heal decision.
        if (described == "done-stuck" && MaterialsAlreadyAssigned(data, renderer))
            return "done-disabled(foreign)";
        return described;
    }

    /// <summary>The MaterialLoaderData referencing <paramref name="renderer"/>, found by
    /// walking its ancestors' <see cref="MaterialLoader"/> components (the loader sits on
    /// the instantiated asset root, the renderers on its children).</summary>
    private static MaterialLoaderData? FindLoaderData(Renderer renderer, out MaterialLoader? owner)
    {
        owner = null;
        MaterialLoader[] loaders = renderer.GetComponentsInParent<MaterialLoader>(includeInactive: true);
        foreach (MaterialLoader loader in loaders)
        {
            if (loader == null || loader.LoadersData == null)
                continue;
            foreach (MaterialLoaderData data in loader.LoadersData)
            {
                if (data != null && data.Renderer == renderer)
                {
                    owner = loader;
                    return data;
                }
            }
        }
        return null;
    }

    private enum LoaderState
    {
        /// <summary>No material references — untouchable (see DescribeForRenderer doc).</summary>
        EmptyRefs,
        /// <summary>LoadMaterials never enlisted this entry (or never ran) — _handles null.</summary>
        NeverStarted,
        /// <summary>Handles exist and at least one is genuinely still in flight.</summary>
        Loading,
        /// <summary>At least one handle finished dead: invalid handle or null Result.</summary>
        NullResult,
        /// <summary>Every handle holds a loaded material, renderer still disabled.</summary>
        DoneStuck,
        /// <summary>Every handle loaded and the renderer is enabled — healthy terminal state.</summary>
        Done,
    }

    private static LoaderState Classify(
        MaterialLoaderData data, bool rendererEnabled, out int done, out int pending, out int failed)
    {
        done = pending = failed = 0;
        var refs = data.MaterialReferences;
        if (refs == null || refs.Count == 0)
            return LoaderState.EmptyRefs;
        AsyncOperationHandle<Material>[]? handles = data._handles;
        if (handles == null)
            return LoaderState.NeverStarted;
        foreach (AsyncOperationHandle<Material> h in handles)
        {
            // Invalid = the double-load poison or a foreign release — either way this load
            // can never deliver a material; count it with the dead ones.
            if (!h.IsValid()) { failed++; continue; }
            if (!h.IsDone) { pending++; continue; }
            Material? m = null;
            try { m = h.Result; }
            catch { /* op destroyed between IsValid and Result — treat as dead */ }
            if (m != null) done++;
            else failed++;
        }
        if (pending > 0)
            return LoaderState.Loading;
        if (failed > 0)
            return LoaderState.NullResult;
        return rendererEnabled ? LoaderState.Done : LoaderState.DoneStuck;
    }

    private static string Describe(MaterialLoaderData data, bool rendererEnabled)
    {
        LoaderState state = Classify(data, rendererEnabled, out int done, out int pending, out int failed);
        int total = done + pending + failed;
        return state switch
        {
            LoaderState.EmptyRefs => "empty-refs",
            LoaderState.NeverStarted => "never-started",
            LoaderState.Loading => $"loading {done}/{total}",
            LoaderState.NullResult => $"null-result {failed}/{total}",
            LoaderState.DoneStuck => "done-stuck",
            _ => "done",
        };
    }

    /// <summary>Every loaded handle's material is already reference-present on the
    /// renderer — i.e. <c>CheckAllMaterialLoaded</c> DID run its assignment, so the loader
    /// reached its terminal state and the current disable belongs to another system (door
    /// wings hidden by MakeDoor/ApparanceLayer are the known case). Distinguishes a
    /// genuinely stuck loader (assignment never happened — renderer still carries prefab
    /// placeholders) from a finished one, which handle-state alone cannot.</summary>
    private static bool MaterialsAlreadyAssigned(MaterialLoaderData data, Renderer renderer)
    {
        AsyncOperationHandle<Material>[]? handles = data._handles;
        if (handles == null || handles.Length == 0)
            return false;
        Material[] shared = renderer.sharedMaterials;
        foreach (AsyncOperationHandle<Material> h in handles)
        {
            if (!h.IsValid() || !h.IsDone)
                return false;
            Material? m;
            try { m = h.Result; }
            catch { return false; }
            if (m == null)
                return false;
            bool present = false;
            foreach (Material s in shared)
            {
                if (ReferenceEquals(s, m)) { present = true; break; }
            }
            if (!present)
                return false;
        }
        return true;
    }

    /// <summary>A renderer still waiting on its loader's RE-TRIGGER states has ≥1 null
    /// sharedMaterial slot (the loader exists precisely because the materials are stripped
    /// for Addressables). NOTE (round 5): this is deliberately NOT a global pre-gate — a
    /// done-stuck renderer keeps its authored prefab placeholders because the assignment
    /// never ran, so it looks fully materialed while being exactly the state to heal.</summary>
    private static bool HasNullMaterialSlot(Renderer renderer)
    {
        Material[] shared = renderer.sharedMaterials;
        if (shared.Length == 0)
            return true;
        foreach (Material m in shared)
        {
            if (m == null)
                return true;
        }
        return false;
    }

    // ---------------------------------------------------------------------------------

    private sealed class HealDriver : MonoBehaviour
    {
        /// <summary>Slow cadence — a stuck loader has been stuck for seconds already;
        /// per-frame scanning would only buy hitches.</summary>
        private const float ScanInterval = 1f;

        /// <summary>An entry must be OBSERVED stuck for this long before it is touched:
        /// the game's own Start defers LoadMaterials one frame behind Task.Yield when a
        /// DetailsDisabler sibling exists, and a mid-build tile streams instances in over
        /// several frames — healing a renderer whose loader is about to run would race the
        /// game's LoadAssetAsync into the double-load error.</summary>
        private const float MinStuckSeconds = 3f;

        /// <summary>Per-entry retry throttle + cap: the failure this heals is a one-shot
        /// race, so one retry normally suffices; a load that fails five spaced retries is
        /// broken for a reason retrying can't fix (missing catalog entry, dead bundle) and
        /// gets ONE error naming the assets instead of a log flood.</summary>
        private const float RetryCooldown = 5f;
        private const int MaxRetries = 5;

        /// <summary>Handles genuinely in flight are the game working — never touched. Only
        /// when an entry has sat in 'loading' this long is it treated as wedged (an
        /// Addressables op whose completion was lost does not finish on its own).</summary>
        private const float PendingForeverSeconds = 30f;

        /// <summary>Track entries not seen for this long (tile despawned, scene change)
        /// are dropped so the dictionary tracks live content only.</summary>
        private const float TrackExpirySeconds = 120f;

        private sealed class Track
        {
            public float FirstSeen;
            public float LastSeen;
            public float NextRetry;
            public int Retries;
            public bool GaveUp;
        }

        private readonly Dictionary<MaterialLoaderData, Track> _tracks = new();
        private readonly List<MaterialLoader> _loaderScratch = new();
        private readonly List<MaterialLoaderData> _pruneScratch = new();
        private readonly HashSet<MaterialLoader> _touchedLoaders = new();
        private float _nextScan;
        private float _nextPrune;
        private System.Action? _tick;

        private void Awake() => _tick = Tick; // cached delegate — TickGuard hot-path contract

        private void Update() => TickGuard.Run("Compat.LoaderHeal", _tick!, Name);

        private void Tick()
        {
            if (!VRSession.IsRunning)
                return;
            float now = Time.unscaledTime;
            if (now < _nextScan)
                return;
            _nextScan = now + ScanInterval;

            // Active tiles only: hidden tiles have no live content, and the census proved
            // the stuck renderers live under the revealed tile's active 'Full' child.
            ProceduralMapTile[] tiles = FindObjectsOfType<ProceduralMapTile>();
            for (int i = 0; i < tiles.Length; i++)
            {
                ProceduralMapTile tile = tiles[i];
                if (tile == null)
                    continue;
                HealTile(tile, now);
            }

            if (now >= _nextPrune)
            {
                _nextPrune = now + TrackExpirySeconds;
                PruneTracks(now);
            }
        }

        private void HealTile(ProceduralMapTile tile, float now)
        {
            _loaderScratch.Clear();
            tile.GetComponentsInChildren(includeInactive: false, _loaderScratch);
            if (_loaderScratch.Count == 0)
                return;

            _touchedLoaders.Clear();
            int nNever = 0, nNull = 0, nPending = 0, nDone = 0;

            foreach (MaterialLoader loader in _loaderScratch)
            {
                if (loader == null || loader.LoadersData == null)
                    continue;
                foreach (MaterialLoaderData data in loader.LoadersData)
                {
                    Renderer? r = data?.Renderer;
                    if (data == null || r == null)
                        continue;
                    if (!r.gameObject.activeInHierarchy || r.enabled)
                    {
                        _tracks.Remove(data); // healthy/hidden — restart observation if it re-sticks
                        continue;
                    }
                    LoaderState state = Classify(data, rendererEnabled: false, out _, out _, out _);
                    if (state == LoaderState.EmptyRefs || state == LoaderState.Done)
                        continue;

                    // ROUND 5 (hardware log 2026-08-02, ml=done-stuck on every stuck floor
                    // renderer): the old "fully materialed ⇒ someone else's deliberate
                    // disable" pre-gate silently blocked the DONE-STUCK heal path — in the
                    // sizing deadlock the loader never ASSIGNS its loaded materials, so the
                    // renderer still carries its authored (non-null) prefab placeholders and
                    // looked "fully materialed". The gate now applies only to the re-trigger
                    // states: a fully-materialed renderer is healed EXCLUSIVELY via the
                    // provable done-stuck path below, never by re-running its loader.
                    bool fullyMaterialed = !HasNullMaterialSlot(r);
                    if (fullyMaterialed && state != LoaderState.DoneStuck)
                    {
                        _tracks.Remove(data); // someone else's deliberate disable
                        continue;
                    }
                    // A done-stuck-looking entry whose loaded materials are ALREADY on the
                    // renderer is a loader that finished its job — the disable came from a
                    // game system afterwards (MakeDoor/ApparanceLayer hide door wings this
                    // way). Never re-enable those.
                    if (state == LoaderState.DoneStuck && MaterialsAlreadyAssigned(data, r))
                    {
                        _tracks.Remove(data);
                        continue;
                    }
                    // Defense in depth for the same class: the door prop subtree is the
                    // game's own hide unit — the healer never touches anything inside it.
                    if (r.GetComponentInParent<UnityGameEditorDoorProp>() != null)
                        continue;

                    if (!_tracks.TryGetValue(data, out Track track))
                    {
                        track = new Track { FirstSeen = now };
                        _tracks[data] = track;
                    }
                    track.LastSeen = now;

                    float stuckFor = now - track.FirstSeen;
                    switch (state)
                    {
                        case LoaderState.Loading:
                            // The game is (apparently) working — leave it alone unless the
                            // op has been in flight implausibly long.
                            if (stuckFor >= PendingForeverSeconds
                                && TryRetrigger(data, r, track, now, "pending-forever"))
                            {
                                nPending++;
                                _touchedLoaders.Add(loader);
                            }
                            break;

                        case LoaderState.NeverStarted:
                            if (stuckFor >= MinStuckSeconds
                                && TryRetrigger(data, r, track, now, "never-started"))
                            {
                                nNever++;
                                _touchedLoaders.Add(loader);
                            }
                            break;

                        case LoaderState.NullResult:
                            if (stuckFor >= MinStuckSeconds
                                && TryRetrigger(data, r, track, now, "null-result"))
                            {
                                nNull++;
                                _touchedLoaders.Add(loader);
                            }
                            break;

                        case LoaderState.DoneStuck:
                            // Everything is already loaded — re-running the loader would hit
                            // the Addressables double-load error; finish its job directly.
                            if (stuckFor >= MinStuckSeconds && TryFinishDirect(data, r))
                            {
                                nDone++;
                                _touchedLoaders.Add(loader);
                                _tracks.Remove(data);
                            }
                            break;
                    }
                }
            }

            int retriggered = nNever + nNull + nPending;
            if (retriggered > 0)
            {
                var reasons = new List<string>(3);
                if (nNever > 0) reasons.Add($"never-started x{nNever}");
                if (nNull > 0) reasons.Add($"null-result x{nNull}");
                if (nPending > 0) reasons.Add($"pending-forever x{nPending}");
                VRLog.Info(Name,
                    $"MaterialLoaderHeal: re-triggered {_touchedLoaders.Count} loader(s) "
                    + $"({retriggered} renderer(s)) under tile '{tile.name}' — reason: "
                    + string.Join(", ", reasons));
            }
            if (nDone > 0)
            {
                VRLog.Info(Name,
                    $"MaterialLoaderHeal: completed {nDone} stalled renderer(s) directly under tile "
                    + $"'{tile.name}' — reason: done-stuck (all handles loaded, CheckAllMaterialLoaded "
                    + "never re-enabled — its sizing deadlock)");
            }
        }

        /// <summary>
        /// Restart one entry's load the only legal way: release every material reference
        /// whose Addressables operation is still valid (<c>ReleaseAsset()</c> clears the
        /// reference's <c>m_Operation</c>; without this, <c>LoadAssetAsync</c> logs the
        /// "already been loaded" error and hands back an invalid handle that
        /// LoadMaterials' <c>.Completed</c> subscription would throw on), then re-run
        /// <c>MaterialLoaderData.LoadMaterials()</c> — which rebuilds its handle arrays,
        /// disables the renderer (it already is) and re-enables it on completion.
        /// </summary>
        private bool TryRetrigger(
            MaterialLoaderData data, Renderer r, Track track, float now, string reason)
        {
            if (track.GaveUp || now < track.NextRetry)
                return false;
            if (track.Retries >= MaxRetries)
            {
                track.GaveUp = true;
                var keys = new List<string>();
                if (data.MaterialReferences != null)
                {
                    foreach (var mref in data.MaterialReferences)
                        keys.Add(mref?.RuntimeKey?.ToString() ?? "<null-ref>");
                }
                VRLog.Error(Name,
                    $"MaterialLoaderHeal: giving up on renderer '{r.name}' after {MaxRetries} "
                    + $"retries (state {reason}) — it stays hidden. Material asset(s): "
                    + string.Join(", ", keys));
                return false;
            }
            track.Retries++;
            track.NextRetry = now + RetryCooldown;

            try
            {
                if (data.MaterialReferences != null)
                {
                    foreach (var mref in data.MaterialReferences)
                    {
                        if (mref != null && mref.OperationHandle.IsValid())
                            mref.ReleaseAsset();
                    }
                }
                data.LoadMaterials();
                return true;
            }
            catch (System.Exception e)
            {
                VRLog.Warn(Name,
                    $"MaterialLoaderHeal: re-trigger for renderer '{r.name}' threw ({reason}): {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Terminal assignment for an entry whose every handle already holds a material but
        /// whose <c>CheckAllMaterialLoaded</c> can never pass (its <c>_loadedMaterials</c>
        /// is sized count+existing yet only [0..count) is ever filled before the all-non-null
        /// check). Mirrors the game's intended semantics: plain entries get exactly the
        /// loaded array (what <c>CheckAllMaterialLoaded</c> assigns when it works);
        /// <c>IsSaveExistedMaterials</c> entries keep their authored non-null slots and have
        /// the loaded materials fill the null ones in order.
        /// </summary>
        private static bool TryFinishDirect(MaterialLoaderData data, Renderer r)
        {
            var refs = data.MaterialReferences;
            AsyncOperationHandle<Material>[]? handles = data._handles;
            if (refs == null || handles == null || handles.Length == 0)
                return false;

            var loaded = new Material[handles.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                AsyncOperationHandle<Material> h = handles[i];
                if (!h.IsValid() || !h.IsDone)
                    return false;
                Material? m;
                try { m = h.Result; }
                catch { return false; }
                if (m == null)
                    return false;
                loaded[i] = m;
            }

            Material[] shared = r.sharedMaterials;
            Material[] final;
            if (!data.IsSaveExistedMaterials || shared.Length == 0)
            {
                final = loaded;
            }
            else
            {
                final = (Material[])shared.Clone();
                int j = 0;
                for (int i = 0; i < final.Length && j < loaded.Length; i++)
                {
                    if (final[i] == null)
                        final[i] = loaded[j++];
                }
                if (j < loaded.Length)
                {
                    // More loaded materials than null slots — append (defensive; matches the
                    // grown array CheckAllMaterialLoaded builds for this configuration).
                    var grown = new Material[final.Length + (loaded.Length - j)];
                    final.CopyTo(grown, 0);
                    for (int k = final.Length; j < loaded.Length; k++, j++)
                        grown[k] = loaded[j];
                    final = grown;
                }
            }

            r.sharedMaterials = final;
            r.enabled = true;
            return true;
        }

        private void PruneTracks(float now)
        {
            if (_tracks.Count == 0)
                return;
            _pruneScratch.Clear();
            foreach (KeyValuePair<MaterialLoaderData, Track> kv in _tracks)
            {
                if (now - kv.Value.LastSeen > TrackExpirySeconds)
                    _pruneScratch.Add(kv.Key);
            }
            foreach (MaterialLoaderData key in _pruneScratch)
                _tracks.Remove(key);
            _pruneScratch.Clear();
        }
    }
}

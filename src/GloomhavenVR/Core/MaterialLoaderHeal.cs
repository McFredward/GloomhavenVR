using System.Collections.Generic;
using HarmonyLib;
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

    // ---------------------------------------------------------------------------------
    // ROUND 7 — REGISTRY AT THE SOURCE (discovery is no longer searched, it is told):
    // two hardware rounds proved that SCANNING for the stuck loaders is a minefield —
    // the per-tile downward GetComponentsInChildren(activeOnly) never classified the
    // floor entries, and the scene-wide FindObjectsOfType (round 6) found NOTHING at
    // all, coins included, because Apparance flags its generated containers
    // HideAndDontSave (ApparanceEntity, decompiled) and FindObjectsOfType skips
    // DontSave-flagged objects. So a Harmony postfix on the game's own
    // MaterialLoader.LoadMaterials() (its only trigger, called from Start) now REGISTERS
    // every loader the moment it begins loading — no hierarchy, active-state or hideFlag
    // assumption can ever hide a loader from the healer again. The per-tile deep scan
    // (includeInactive: true — transform traversal ignores hideFlags) stays as a seed
    // for loaders that ran before the patch landed.
    // ---------------------------------------------------------------------------------

    private static readonly List<MaterialLoader> RegisteredLoaders = new();
    private static readonly HashSet<MaterialLoader> RegisteredSet = new();

    /// <summary>Called by the Harmony postfix — every loader that starts loading enrolls
    /// itself for supervision. Idempotent; dead entries are pruned by the driver.</summary>
    internal static void Register(MaterialLoader? loader)
    {
        if (loader == null)
            return;
        if (RegisteredSet.Add(loader))
            RegisteredLoaders.Add(loader);
    }

    /// <summary>Census forensics: is this loader under the healer's supervision?</summary>
    private static bool IsRegistered(MaterialLoader loader) => RegisteredSet.Contains(loader);

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
        MaterialLoaderData? data = FindLoaderData(renderer, out MaterialLoader? owner);
        if (data == null)
            return "no-loader";
        string described = Describe(data, renderer.enabled);
        // Census nuance (round 5): a disabled renderer whose loaded materials are already
        // assigned is NOT the healer's done-stuck target — another system disabled it
        // (door wings). Name it distinctly so the log matches the heal decision.
        // ROUND 8 ORDER FIX: this check MUST run before the forensic suffixes are
        // appended — round 7 appended first, so the literal comparison never matched and
        // the log could no longer distinguish "loader stuck" from "loader finished,
        // someone else disabled" — the exact question the round hinged on.
        if (described == "done-stuck" && MaterialsAlreadyAssigned(data, renderer))
            described = "done-disabled(foreign)";
        // Round 7/8 forensics: WHERE the loader lives (name/active/hideFlags/registered),
        // plus the two fields that convict or acquit the remaining suspects in one run —
        // isPartOfStaticBatch (did static batching touch this renderer or the template it
        // was cloned from?) and the first material slot's actual content.
        if (described != "done" && owner != null)
        {
            Material[] shared = renderer.sharedMaterials;
            string mat0 = shared.Length == 0 ? "<no-slots>"
                : shared[0] == null ? "<null>" : shared[0].name;
            described += $" loader='{owner.gameObject.name}' "
                + $"active={owner.gameObject.activeInHierarchy} "
                + $"flags={owner.gameObject.hideFlags} "
                + $"registered={IsRegistered(owner)} "
                + $"staticBatch={renderer.isPartOfStaticBatch} "
                + $"mat0='{mat0}'";
        }
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
        private readonly List<MaterialLoader> _tileLoaderScratch = new();
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

            // ROUND 6: scene-wide, includeInactive — the per-tile downward
            // GetComponentsInChildren scan provably missed the stuck floor loaders (two
            // hardware runs: the renderer-upward census said done-stuck while the heal
            // loop never even classified those entries), so NO hierarchy assumption
            // survives: every MaterialLoader in the scene is visited, wherever Apparance
            // parented it and whatever its own GameObject's active state — the per-entry
            // renderer filter (activeInHierarchy && !enabled) already scopes the work to
            // live content on its own.
            HealAllLoaders(now);

            if (now >= _nextPrune)
            {
                _nextPrune = now + TrackExpirySeconds;
                PruneTracks(now);
            }
        }

        private void HealAllLoaders(float now)
        {
            _loaderScratch.Clear();
            // Primary: the Harmony-fed registry (see the ROUND 7 header) — complete for
            // every loader whose LoadMaterials ever ran, wherever Apparance parented it.
            for (int i = RegisteredLoaders.Count - 1; i >= 0; i--)
            {
                MaterialLoader reg = RegisteredLoaders[i];
                if (reg == null)
                {
                    // Destroyed with its scene — prune both stores (the set's stale key
                    // compares equal to null via Unity's overload but keeps the slot).
                    RegisteredSet.Remove(RegisteredLoaders[i]);
                    RegisteredLoaders.RemoveAt(i);
                    continue;
                }
                _loaderScratch.Add(reg);
            }
            // Seed/fallback: deep per-tile transform walk, includeInactive — immune to the
            // HideAndDontSave flags that blind FindObjectsOfType (the round-6 failure) and
            // covers loaders that ran before the Harmony patch landed (hot reload).
            ProceduralMapTile[] tiles = FindObjectsOfType<ProceduralMapTile>();
            foreach (ProceduralMapTile tile in tiles)
            {
                if (tile == null)
                    continue;
                _tileLoaderScratch.Clear();
                tile.GetComponentsInChildren(includeInactive: true, _tileLoaderScratch);
                foreach (MaterialLoader tl in _tileLoaderScratch)
                {
                    if (tl != null && RegisteredSet.Add(tl))
                    {
                        RegisteredLoaders.Add(tl);
                        _loaderScratch.Add(tl);
                    }
                }
            }
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
                    // The door prop subtree is the game's own hide unit (MakeDoor/
                    // ApparanceLayer disable the wings of unrevealed doors) — the healer
                    // never touches anything inside it, in any state.
                    if (r.GetComponentInParent<UnityGameEditorDoorProp>() != null)
                        continue;
                    // ROUND 8: a done-stuck-looking entry whose loaded materials are
                    // ALREADY on the renderer means the loader finished — the disable came
                    // from elsewhere. Outside door props nothing in the game legitimately
                    // leaves map-tile content disabled, so this is now HEALED rather than
                    // skipped (rounds 4-7 skipped it silently — the last silent branch):
                    // switch the renderer back on.
                    if (state == LoaderState.DoneStuck && MaterialsAlreadyAssigned(data, r))
                    {
                        r.enabled = true;
                        nDone++;
                        _touchedLoaders.Add(loader);
                        _tracks.Remove(data);
                        if (nDone <= 3)
                            VRLog.Info(Name,
                                $"MaterialLoaderHeal: re-enabled foreign-disabled renderer '{r.name}' "
                                + "(materials were loaded AND assigned) — outside a door prop "
                                + "nothing legitimately leaves tile content disabled.");
                        continue;
                    }

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
                            // ROUND 6: IMMEDIATELY (user ruling: the room must appear at
                            // once, like every other room) — no observation delay: all
                            // handles complete + materials not assigned is already a
                            // terminal, provable state the game can never leave on its own.
                            if (TryFinishDirect(data, r))
                            {
                                nDone++;
                                _touchedLoaders.Add(loader);
                                _tracks.Remove(data);
                                if (nDone <= 3) // forensic sample: WHICH strand was it?
                                    LogDoneStuckForensics(data, r);
                            }
                            else
                            {
                                // Round-5 blind spot: this false path was silent, so a
                                // classification/finish disagreement was invisible. Once.
                                if (!track.GaveUp)
                                {
                                    track.GaveUp = true;
                                    VRLog.Warn(Name,
                                        $"MaterialLoaderHeal: done-stuck entry for renderer '{r.name}' "
                                        + "could NOT be finished directly (handle state changed between "
                                        + "classify and finish?) — will retry next scan.");
                                }
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
                    + $"({retriggered} renderer(s)) — reason: "
                    + string.Join(", ", reasons));
            }
            if (nDone > 0)
            {
                VRLog.Info(Name,
                    $"MaterialLoaderHeal: completed {nDone} stalled renderer(s) directly — "
                    + "reason: done-stuck (all handles loaded, the game's CheckAllMaterialLoaded "
                    + "never re-enabled them)");
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

        /// <summary>
        /// One-shot forensic line for a healed done-stuck entry (first 3 per scan): names
        /// WHICH stranding mechanism produced it — <c>_released=true</c> ⇒ the game called
        /// <c>Release()</c> and every later completion callback no-opped;
        /// <c>saveExisted=true</c> with ≥1 authored non-null material ⇒ the
        /// CheckAllMaterialLoaded sizing deadlock; neither ⇒ the completion callbacks never
        /// ran at all (subscription raced the synchronous completion). Ends the five-round
        /// "which strand is it" guesswork with data instead of inference.
        /// </summary>
        private static void LogDoneStuckForensics(MaterialLoaderData data, Renderer r)
        {
            bool released = false;
            try { released = data._released; } catch { /* publicized access — defensive */ }
            int authoredNonNull = 0, slots = 0;
            Material[] shared = r.sharedMaterials;
            slots = shared.Length;
            foreach (Material m in shared)
            {
                if (m != null) authoredNonNull++;
            }
            var tile = r.GetComponentInParent<ProceduralMapTile>();
            VRLog.Info(Name,
                $"MaterialLoaderHeal forensics '{r.name}' (tile '{(tile != null ? tile.name : "<none>")}'): "
                + $"_released={released}, saveExisted={data.IsSaveExistedMaterials}, "
                + $"authoredMaterials={authoredNonNull}/{slots}, handles={data._handles?.Length ?? -1}, "
                + $"refs={data.MaterialReferences?.Count ?? -1} — mechanism: "
                + (released ? "Release() no-opped the completion callbacks"
                    : data.IsSaveExistedMaterials && authoredNonNull > 0
                        ? "CheckAllMaterialLoaded sizing deadlock"
                        : "completion callbacks never ran"));
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

/// <summary>
/// The ROUND-7 discovery fix (see the registry header in <see cref="MaterialLoaderHeal"/>):
/// a postfix on the game's <c>MaterialLoader.LoadMaterials()</c> — the single entry point
/// through which every material load starts (called from its <c>Start</c>; the healer's
/// own re-triggers go through the DATA-level <c>MaterialLoaderData.LoadMaterials</c>, so
/// they can never recurse into this patch) — enrolls the loader for supervision the moment
/// it begins loading. Pure bookkeeping: the original method is untouched, vanilla behavior
/// is bit-identical, and a throw inside Register can never reach the game (guarded).
/// Registered by <c>CompatModule.Init</c> only while VR runs; removed with the mod's
/// <c>UnpatchSelf</c> on hot reload.
/// </summary>
[HarmonyPatch(typeof(MaterialLoader), nameof(MaterialLoader.LoadMaterials))]
internal static class MaterialLoader_LoadMaterials_RegisterPatch
{
    private static void Postfix(MaterialLoader __instance)
    {
        try { MaterialLoaderHeal.Register(__instance); }
        catch { /* supervision is best-effort — never disturb the game's load path */ }
    }
}

using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// Builds the visible hand and its <see cref="HandRig"/> transform contract.
///
/// Source priority:
/// 1. Glove prefab from <c>BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle</c>
///    (built from the companion Unity project — unity/HANDS.md, scripts/build-bundles.sh).
///    Expected asset paths (documented contract, docs/INTERFACES-P2.md §Hand assets):
///      <c>Assets/Bundle/Hands/VRHand_L.prefab</c> / <c>VRHand_R.prefab</c>
///    (legacy alias also probed: <c>HandLeft.prefab</c> / <c>HandRight.prefab</c>;
///    the Plate/Arcane pairs resolve through <see cref="HandStyles.BaseName"/>).
///    Rig mapping inside the prefab, by child name (first match wins):
///      anchors  <c>Anchor_Wrist</c>, <c>Anchor_Palm</c>, <c>Anchor_IndexTip</c>, <c>Anchor_Grab</c>,
///      fingers  <c>Anchor_{Thumb|Index|Middle|Ring|Pinky}_{Root|Mid|Tip}</c>,
///      fallback SteamVR skeleton bone names (<c>finger_index_0_r</c> …).
///    Any missing transform is synthesized at the procedural default position, so the
///    HandRig contract is ALWAYS complete regardless of asset quality.
/// 2. Procedural primitive hand (palm box + 5 capsule-segment fingers with correct
///    joint transforms) — zero-asset fallback so Phase 2+ is testable NOW.
///
/// All dimensions are meters at scale 1; the hand inherits the diorama scale from the
/// rig root above it.
/// </summary>
internal static class HandVisuals
{
    private const string BundleFileName = "gloomhavenvr.bundle";

    private static AssetBundle? _bundle;
    private static bool _bundleProbed;

    /// <summary>
    /// False when <see cref="_bundle"/> was ADOPTED from another module (Cards/WorldUI load the
    /// same file) — an adopted bundle must never be unloaded here. See <see cref="GetBundle"/>.
    /// </summary>
    private static bool _bundleOwned;

    /// <summary>In-flight prewarm (see <see cref="Prewarm"/>); null once realized.</summary>
    private static AssetBundleCreateRequest? _bundleRequest;

    // ---- procedural hand dimensions (meters, right hand; X mirrored for left) --------

    private static readonly Vector3 PalmCenterPos = new(0f, -0.008f, 0.05f);
    private static readonly Vector3 PalmBoxSize = new(0.078f, 0.026f, 0.082f);
    private const float KnuckleZ = 0.088f;

    /// <summary>
    /// Per-finger base radius (test #13 upgrade): thumb/middle thicker, pinky
    /// thinner — the old constant 0.0075 for all five read as sausage fingers.
    /// Segments taper toward the tip (see <see cref="SegmentTaper"/>).
    /// </summary>
    private static readonly float[] FingerRadii = { 0.0090f, 0.0078f, 0.0080f, 0.0073f, 0.0063f };

    /// <summary>Radius multiplier per segment (root, mid, tip) — real fingers taper.</summary>
    private static readonly float[] SegmentTaper = { 1f, 0.88f, 0.78f };

    // Per finger: knuckle X offset (right hand, thumb side = -X), segment lengths root/mid/tip.
    private static readonly float[] FingerX = { -0.038f, -0.026f, -0.008f, 0.010f, 0.028f };
    private static readonly Vector3[] SegmentLengths =
    {
        new(0.042f, 0.030f, 0.025f), // thumb (root sits at the palm edge, see BuildProceduralHand)
        new(0.036f, 0.024f, 0.020f), // index
        new(0.040f, 0.026f, 0.022f), // middle
        new(0.036f, 0.024f, 0.020f), // ring
        new(0.028f, 0.019f, 0.017f), // pinky
    };

    /// <summary>
    /// Build the hand visual + rig under <paramref name="handRoot"/> (the wrist-space
    /// child of the tracked pose) using the LOCAL player's chosen style
    /// (<c>[Hands] HandStyle</c>). Returns a complete <see cref="HandRig"/> always.
    /// </summary>
    internal static HandRig Build(Transform handRoot, HandSide side) =>
        Build(handRoot, side, LocalStyle());

    /// <summary>The locally-configured hand style; Glove when the config is not bound
    /// (early init / hot reload) so callers never throw.</summary>
    internal static HandStyle LocalStyle()
    {
        try
        {
            return Plugin.HandStyle != null ? HandStyles.Clamp((int)Plugin.HandStyle.Value) : HandStyle.Glove;
        }
        catch
        {
            return HandStyle.Glove;
        }
    }

    /// <summary>
    /// Build the hand visual + rig for an EXPLICIT style — used by <see cref="Net.RemoteAvatar"/>
    /// to render a remote player's transmitted choice. Missing styled prefab degrades to the
    /// Glove pair, then to the procedural hand (the contract always completes).
    /// </summary>
    internal static HandRig Build(Transform handRoot, HandSide side, HandStyle style)
    {
        var rig = new HandRig { Root = handRoot };

        GameObject? prefab = TryLoadPrefab(side, style, out HandStyle effectiveStyle);
        rig.VisualStyle = prefab != null ? effectiveStyle : HandStyle.Glove;
        if (prefab != null)
        {
            // MEASURED SCOPE (c) of the boot-stall split — see the block comment on Prewarm.
            // Instantiate is where the deserialized prefab becomes a live object tree (and where
            // its meshes/textures are first touched by the render pipeline); MapPrefabRig is a
            // name-keyed recursive walk over that tree. Both are pure main-thread work, so if the
            // split lands HERE the async prewarm cannot help and the answer is a smaller prefab.
            GameObject instance;
            using (PerfMonitor.Scope("Hands.GloveSpawn"))
            {
                instance = Object.Instantiate(prefab, handRoot, worldPositionStays: false);
                instance.name = $"Glove_{side}";
                MapPrefabRig(instance.transform, rig, side);
            }
            // The glove is instantiated AFTER HandsDriver's tree-wide VRLayers.Apply, so
            // it would stay on layer 0 and get CULLED by the menu head camera (which
            // renders the mod layer only) — the hands vanished in front of the menu.
            // Re-layer the whole glove subtree onto the mod layer here.
            Core.VRLayers.Apply(instance);
            // Pin best per-renderer skin quality (4 bones/vertex) + updateWhenOffscreen
            // (avoids stale-bounds frustum culling). NOTE: this pin alone is NOT enough —
            // the global QualitySettings.skinWeights is a hard CAP that per-renderer
            // quality can only lower (the intro boots on the 'Fastest' level = ONE bone,
            // which is why the original 5b119e0 pin "didn't hold" there). The global
            // 4-bone floor is enforced per frame by HandsDriver.EnforceGlobalSkinWeights.
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = true;
            }
            VRLog.Info("Hands", $"{side}: glove prefab loaded from bundle (style {effectiveStyle}, " +
                                $"scale {StyleScale(effectiveStyle):0.00}).");
        }
        else
        {
            BuildProceduralHand(handRoot, rig, side);
        }

        // Synthesize whatever the asset did not provide so the contract always holds.
        FillMissingAnchors(handRoot, rig, side);

        // Externally-parented anchors become scale-compensated SOCKETS, then the
        // per-style visual scale is applied (see ApplyStyleScale). Order matters:
        // sockets first, so the compensation below sees the final hierarchy.
        rig.Wrist = CreateSocket(rig.Wrist, "Socket_Wrist");
        rig.PalmCenter = CreateSocket(rig.PalmCenter, "Socket_Palm");
        rig.GrabAnchor = CreateSocket(rig.GrabAnchor, "Socket_Grab");
        ApplyStyleScale(handRoot, rig, StyleScale(rig.VisualStyle));
        return rig;
    }

    /// <summary>
    /// The configured uniform visual scale of a style ([Hands] GloveScale/PlateScale/
    /// ArcaneScale), clamped to a sane range; 1 when the config is not bound yet.
    /// </summary>
    internal static float StyleScale(HandStyle style)
    {
        try
        {
            var entries = Plugin.HandStyleScale;
            if (entries == null)
                return 1f;
            return Mathf.Clamp(entries[(int)HandStyles.Clamp((int)style)].Value, 0.2f, 3f);
        }
        catch
        {
            return 1f;
        }
    }

    /// <summary>
    /// Apply a per-style uniform visual scale to the whole hand subtree, then
    /// counter-scale the attachment SOCKETS so everything the rest of the mod parents
    /// INTO the hand (card fan under PalmCenter, grabbed objects under GrabAnchor, the
    /// wrist HUD under Wrist) keeps its own world size. The compensation is numeric
    /// (reference lossyScale over the socket parent's lossyScale) so it is exact for
    /// any hierarchy the anchors ended up in (FBX bone, procedural child of PalmCenter,
    /// or handRoot itself). Positions/rotations of the anchors are untouched — only
    /// scale is normalized. Called at build time and live by VRHand.SyncVisualOffset
    /// whenever the scale entry changes.
    /// </summary>
    internal static void ApplyStyleScale(Transform handRoot, HandRig rig, float scale)
    {
        handRoot.localScale = Vector3.one * scale;
        float reference = handRoot.parent != null ? handRoot.parent.lossyScale.x : 1f;
        NormalizeSocket(rig.Wrist, reference);
        NormalizeSocket(rig.PalmCenter, reference);
        NormalizeSocket(rig.GrabAnchor, reference);
    }

    /// <summary>Zero-offset child used as a scale-compensated attachment point. No-op
    /// (returns the anchor) when the anchor already IS a socket (live re-apply).</summary>
    private static Transform CreateSocket(Transform anchor, string name)
    {
        if (anchor == null || anchor.name == name)
            return anchor!;
        var socket = new GameObject(name).transform;
        socket.SetParent(anchor, worldPositionStays: false);
        socket.localPosition = Vector3.zero;
        socket.localRotation = Quaternion.identity;
        return socket;
    }

    private static void NormalizeSocket(Transform socket, float referenceScale)
    {
        if (socket == null || socket.parent == null)
            return;
        float parentLossy = socket.parent.lossyScale.x;
        socket.localScale = parentLossy > 1e-6f
            ? Vector3.one * (referenceScale / parentLossy)
            : Vector3.one;
    }

    /// <summary>Release the cached bundle (module shutdown / hot reload).</summary>
    internal static void UnloadBundle()
    {
        // An in-flight prewarm must be COMPLETED, not dropped: an abandoned
        // AssetBundleCreateRequest still finishes on the loading thread and leaves the bundle
        // registered with nobody holding a reference — the next LoadFromFile would then be
        // refused as a duplicate and every module would silently fall back to procedural.
        if (_bundleRequest != null)
        {
            AssetBundle? pending = _bundleRequest.assetBundle;
            _bundleRequest = null;
            if (_bundle == null)
            {
                _bundle = pending;
                _bundleOwned = pending != null;
            }
        }

        // Only unload what we own. Since the adoption probe below, _bundle can be another
        // module's instance; unloading it there would pull the tray/card/table assets out from
        // under Cards and WorldUI on a hot reload.
        if (_bundle != null && _bundleOwned)
            _bundle.Unload(unloadAllLoadedObjects: false);
        _bundle = null;
        _bundleOwned = false;
        _bundleProbed = false;
    }

    // ---- bundle loading ---------------------------------------------------------------

    private static GameObject? TryLoadPrefab(HandSide side, HandStyle style, out HandStyle effectiveStyle)
    {
        effectiveStyle = HandStyle.Glove;
        AssetBundle? bundle = GetBundle();
        if (bundle == null)
            return null;

        // MEASURED SCOPE (b) of the boot-stall split — see the block comment on Prewarm.
        // Deserialization of the prefab and its dependency closure (meshes, textures, materials,
        // the bundled GloomhavenVR/BoardLit shader) plus the GPU upload happen inside these
        // LoadAsset calls. TypeTrees are deliberately ON in the bundle
        // (unity/GloomhavenVR.Assets/Assets/Editor/BuildBundles.cs), which makes this path more
        // expensive than a stripped bundle would be — if the split lands here, that is the dial.
        using (PerfMonitor.Scope("Hands.PrefabLoad"))
            return LoadPrefabFrom(bundle, side, style, ref effectiveStyle);
    }

    private static GameObject? LoadPrefabFrom(AssetBundle bundle, HandSide side, HandStyle style,
        ref HandStyle effectiveStyle)
    {
        string suffix = side == HandSide.Left ? "L" : "R";

        // Styled pair first (Plate/Arcane resolve to their own prefabs; Glove to the
        // original VRHand pair). An OLD bundle without the styled prefab falls back to
        // the Glove pair — graceful degradation, mirrors the head-mask placeholder rule.
        if (style != HandStyle.Glove)
        {
            var styled = bundle.LoadAsset<GameObject>(
                $"Assets/Bundle/Hands/{HandStyles.BaseName(style)}_{suffix}.prefab");
            if (styled != null)
            {
                effectiveStyle = style;
                return styled;
            }
            // ALERT: the player picked a hand style and did not get it. The fix is theirs
            // (the bundle is older than the DLL — reinstall both halves), and this line is
            // the only place they would find that out.
            VRLog.Alert("Hands", $"{side}: style {style} prefab not in bundle (old bundle?) — falling back to Glove.");
        }

        string[] candidates = side == HandSide.Left
            ? new[] { "Assets/Bundle/Hands/VRHand_L.prefab", "Assets/Bundle/Hands/HandLeft.prefab" }
            : new[] { "Assets/Bundle/Hands/VRHand_R.prefab", "Assets/Bundle/Hands/HandRight.prefab" };

        foreach (string path in candidates)
        {
            var prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab != null)
                return prefab;
        }

        VRLog.Info("Hands", $"{side}: no glove prefab in bundle (looked for {string.Join(", ", candidates)}) — using procedural hand.");
        return null;
    }

    /// <summary>Full path of the shipped bundle, or null when it is not deployed.</summary>
    private static string? BundlePath()
    {
        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string bundlePath = Path.Combine(pluginDir, BundleFileName);
        return File.Exists(bundlePath) ? bundlePath : null;
    }

    /// <summary>
    /// ADOPTION PROBE — the bundle may already be loaded by another module (Cards' VRCardFactory,
    /// WorldUI's WorldUIAssets: same file). Unity REFUSES a second <c>LoadFromFile</c> on a loaded
    /// bundle and returns null, so the loaded-bundle registry is asked first. The other two
    /// modules have always done this; HandVisuals did not, and only got away with it because it
    /// happens to be first today. The moment anything touches the bundle earlier the hands would
    /// have fallen back to procedural — a silent failure that reads as a rendering bug.
    /// The three now read as one pattern.
    /// </summary>
    private static AssetBundle? FindLoadedBundle()
    {
        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded != null && loaded.name.Contains("gloomhavenvr"))
                return loaded;
        }
        return null;
    }

    /// <summary>
    /// BOOT-STALL PREWARM — start the bundle load as early as the mod exists, on Unity's LOADING
    /// THREAD, so the main thread does not have to sit through it later.
    ///
    /// <para>MEASUREMENT (hardware log ModBuild 107 / b765a5b6e, during the Unity splash, before
    /// the intro): frame 4 cost 1145.21 ms of which the mod was 1007.15 ms, and 981.48 ms of that
    /// was the single step <c>Hands.Rig</c> — the mod's first touch of the bundle. <c>[Perf]
    /// STEPS</c> confirms it is a one-off (<c>Hands.Rig</c> averages 1.340 ms over 733 frames,
    /// worst 981.48). It hides under the black splash, so the player perceives only the game's own
    /// boot freeze, but a full second of that boot is ours.</para>
    ///
    /// <para>ROOT CAUSE, read out of the shipped file itself — not inferred. The UnityFS archive
    /// (29,416,312 bytes) contains exactly ONE data block: 29,416,152 compressed →
    /// <b>57,998,411 uncompressed, compression type 1 = LZMA</b> (nodes:
    /// <c>CAB-…</c> 12.7 MB + <c>CAB-….resS</c> 45.3 MB). That is whole-stream LZMA, which is what
    /// <c>BuildAssetBundleOptions.None</c> produces — the archive is NOT chunk-compressed. For an
    /// LZMA bundle <c>AssetBundle.LoadFromFile</c> cannot read on demand: it must inflate the
    /// ENTIRE 58 MB stream into memory before it returns, single-threaded, on the calling thread.
    /// At the 50–70 MB/s a single LZMA decoder manages that is ~0.8–1.2 s — which is the measured
    /// 981 ms, and it explains why the stall is a boot ONE-OFF that no amount of asset-side
    /// trimming would have touched.</para>
    ///
    /// <para>WHAT THIS CHANGES. <see cref="AssetBundle.LoadFromFileAsync(string)"/> performs that
    /// same decompression on Unity's loading thread. Issued from <c>HandsDriver.Awake</c> (BepInEx
    /// chainloader time, before frame 0) it overlaps the game's own boot — frames 0–3 already burn
    /// hundreds of ms of wall time on the game's side (frame 2 alone was 571.85 ms, and the log's
    /// own verdict there is "NOT the mod"). <see cref="GetBundle"/> then blocks on
    /// <c>request.assetBundle</c> for the REMAINDER only. Nothing about WHEN the hands appear
    /// changes: <see cref="Build(Transform, HandSide, HandStyle)"/> still returns a complete
    /// <see cref="HandRig"/> synchronously on
    /// the same frame it does today, so there is no procedural→glove pop and no deferred hand.</para>
    ///
    /// <para>REJECTED ALTERNATIVES.
    /// (1) <b>Rebuild the bundle with <c>ChunkBasedCompression</c> (LZ4HC).</b> This is the actual
    /// fix — LZ4 bundles are memory-mapped and decompressed per block on demand, so
    /// <c>LoadFromFile</c> becomes a header read (single-digit ms) and only the hand prefab's own
    /// blocks inflate, at ~1–2 GB/s. It is a one-word change in
    /// <c>unity/GloomhavenVR.Assets/Assets/Editor/BuildBundles.cs</c>, but it changes the SHIPPED
    /// BUNDLE'S IDENTITY (refactor-guard pins it) and may only be rebuilt with
    /// <c>/home/claw/unity-2021.3.5</c>. That is a deliberate decision for the integrator, not a
    /// side effect of a perf pass — so it is written down here and NOT done.
    /// (2) <b>Make <see cref="Build(Transform, HandSide, HandStyle)"/> itself async</b> (return a
    /// procedural hand, swap in the
    /// glove when the load lands). Rejected: it would pop, which the project forbids, and
    /// <c>Net.RemoteAvatar</c> depends on the synchronous contract.
    /// (3) <b>Decompress off the main thread ourselves.</b> Rejected outright: every AssetBundle
    /// API is main-thread-only; calling one from a worker is a crash, not an optimisation.</para>
    ///
    /// <para>KNOWN NARROW HAZARD. While the prewarm is IN FLIGHT the bundle is in no registry, so
    /// the adoption probes in Cards/WorldUI cannot see it and a <c>LoadFromFile</c> from those
    /// modules would be refused. Unity exposes no way to observe an in-flight load, so the window
    /// is bounded instead: it opens at chainloader time and closes at the first
    /// <see cref="PumpPrewarm"/> that sees <c>isDone</c> (or at the first
    /// <see cref="Build(Transform, HandSide, HandStyle)"/>, frame ~4).
    /// Both other consumers only reach the bundle when a table/tray/card exists — i.e.
    /// inside a scenario, hundreds of frames later. The <c>[Hands] PREWARM</c> log lines below make
    /// the exact window visible in the next log.</para>
    /// </summary>
    internal static void Prewarm()
    {
        if (_bundleProbed || _bundleRequest != null)
            return;

        // Never start an async load for a bundle that is already resident — that is precisely the
        // duplicate load Unity refuses, and an in-flight request cannot be adopted by anyone.
        AssetBundle? adopted = FindLoadedBundle();
        if (adopted != null)
        {
            _bundle = adopted;
            _bundleOwned = false;
            _bundleProbed = true;
            return;
        }

        string? bundlePath = BundlePath();
        if (bundlePath == null)
            return; // GetBundle logs the miss once, with the path it looked at.

        _bundleRequest = AssetBundle.LoadFromFileAsync(bundlePath);
        VRLog.Info("Hands", "PREWARM: asset bundle load started ASYNC (loading thread). The shipped " +
                            "bundle is a single 58 MB LZMA block, so a synchronous LoadFromFile has to " +
                            "inflate all of it on the main thread — that was the 981 ms one-off in " +
                            "'Hands.Rig' at boot. Whatever the loading thread finishes before the hands " +
                            "are built is free; the rest is still paid, once, in 'Hands.BundleLoad'.");
    }

    /// <summary>
    /// Realize a COMPLETED prewarm without blocking (one <c>isDone</c> read per frame). Called from
    /// <c>HandsDriver.TickRig</c>. Its only job is to close the in-flight window described on
    /// <see cref="Prewarm"/> as early as possible, so the bundle is in Unity's loaded-bundle
    /// registry — and therefore adoptable by Cards/WorldUI — the frame it finishes rather than the
    /// frame the hands happen to be built.
    /// </summary>
    internal static void PumpPrewarm()
    {
        if (_bundleRequest == null || !_bundleRequest.isDone)
            return;
        ConsumePrewarm();
    }

    /// <summary>Take the finished (or force-completed) prewarm request as our bundle.</summary>
    private static void ConsumePrewarm()
    {
        AssetBundleCreateRequest request = _bundleRequest!;
        _bundleRequest = null;
        // THE LINE THAT SETTLES THE NEXT HARDWARE RUN: 'done=True' means the loading thread had
        // already finished the 58 MB inflate when the hands were built and the prewarm converted
        // the whole 981 ms one-off into zero main-thread time; 'done=False' means we still paid a
        // remainder, and 'Hands.BundleLoad' on the [Perf] SPIKE line prices exactly how much.
        bool wasDone = request.isDone;
        float progress = request.progress;
        // Reading .assetBundle on a request that is NOT done stalls the main thread until it is —
        // which is exactly the intended behaviour on the GetBundle path: pay the remainder, once.
        _bundle = request.assetBundle;
        _bundleOwned = _bundle != null;
        _bundleProbed = true;
        VRLog.Info("Hands", $"PREWARM realized: done={wasDone} progress={progress:0.00} at frame {Time.frameCount} " +
                            $"(bundle {(_bundle != null ? "loaded" : "NULL")}). done=True ⇒ the async load beat the " +
                            "hand build and the boot stall is gone; done=False ⇒ 'Hands.BundleLoad' on the next " +
                            "[Perf] SPIKE line is the residual that was still paid on the main thread.");
        if (_bundle == null)
        {
            string? path = BundlePath();
            // ALERT: every 3D asset in the mod has just degraded to its procedural fallback.
            // Reinstalling is the fix, and BundleDiagnostics already says which one it is.
            VRLog.Alert("Hands", $"AssetBundle.LoadFromFileAsync failed for {path} — procedural hands active. " +
                                $"Cause: {(path != null ? BundleDiagnostics.Explain(path) : "file missing")}");
        }
    }

    private static AssetBundle? GetBundle()
    {
        if (_bundleProbed && _bundleRequest == null)
            return _bundle;

        // MEASURED SCOPE (a) of the boot-stall split — see the block comment on Prewarm. With the
        // prewarm in place this is the RESIDUAL of the 58 MB LZMA inflate that the loading thread
        // had not finished yet; without it (bundle adopted, or prewarm never ran) it is the whole
        // synchronous LoadFromFile.
        using (PerfMonitor.Scope("Hands.BundleLoad"))
        {
            if (_bundleRequest != null)
            {
                ConsumePrewarm();
                return _bundle;
            }

            _bundleProbed = true;

            AssetBundle? adopted = FindLoadedBundle();
            if (adopted != null)
            {
                _bundle = adopted;
                _bundleOwned = false;
                return _bundle;
            }

            string? bundlePath = BundlePath();
            if (bundlePath == null)
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                VRLog.Info("Hands", $"Asset bundle not found ({Path.Combine(pluginDir, BundleFileName)}) — procedural hands active.");
                return null;
            }

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            _bundleOwned = _bundle != null;
            if (_bundle == null)
                VRLog.Alert("Hands", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural hands active. " +
                                    $"Cause: {BundleDiagnostics.Explain(bundlePath)}");
            return _bundle;
        }
    }

    // ---- prefab rig mapping -------------------------------------------------------------

    private static readonly string[][] FingerNameParts =
    {
        new[] { "Thumb", "thumb" },
        new[] { "Index", "index" },
        new[] { "Middle", "middle" },
        new[] { "Ring", "ring" },
        new[] { "Pinky", "pinky" },
    };

    private static void MapPrefabRig(Transform instance, HandRig rig, HandSide side)
    {
        string suffix = side == HandSide.Left ? "_l" : "_r";

        rig.Wrist = FindDeep(instance, "Anchor_Wrist") ?? FindDeep(instance, "wrist" + suffix) ?? instance;
        rig.PalmCenter = FindDeep(instance, "Anchor_Palm")!;
        rig.IndexTip = FindDeep(instance, "Anchor_IndexTip")!;
        rig.GrabAnchor = FindDeep(instance, "Anchor_Grab")!;

        for (int f = 0; f < 5; f++)
        {
            string finger = FingerNameParts[f][0];
            string steamVr = FingerNameParts[f][1];
            Transform? root = FindDeep(instance, $"Anchor_{finger}_Root") ?? FindDeep(instance, $"finger_{steamVr}_0{suffix}");
            Transform? mid = FindDeep(instance, $"Anchor_{finger}_Mid") ?? FindDeep(instance, $"finger_{steamVr}_1{suffix}");
            Transform? tip = FindDeep(instance, $"Anchor_{finger}_Tip") ?? FindDeep(instance, $"finger_{steamVr}_2{suffix}");
            rig.SetFinger((Finger)f, new FingerJoints(root!, mid!, tip!));
            if (f == (int)Finger.Index)
                rig.IndexKnuckle = root; // curl-independent beam origin
        }
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    // ---- procedural fallback hand -----------------------------------------------------

    private static void BuildProceduralHand(Transform handRoot, HandRig rig, HandSide side)
    {
        float mirror = side == HandSide.Right ? 1f : -1f;
        Material material = CreateHandMaterial(side);
        Material nailMaterial = CreateNailMaterial(material);

        var visualRoot = new GameObject("ProceduralHand");
        visualRoot.transform.SetParent(handRoot, worldPositionStays: false);

        BuildPalm(visualRoot.transform, material, mirror);

        rig.Wrist = handRoot;

        // Fingers: three-joint chains; visual capsule per segment (tapered radii) +
        // a sphere at every joint so bends stay continuous when the curler rotates
        // them. Capsule primitives are Y-aligned — rotated 90° around X so the
        // segment runs along local +Z. Joint POSITIONS/rotations are unchanged from
        // Phase 2 (frozen rig contract) — only the visuals got better (test #13).
        for (int f = 0; f < 5; f++)
        {
            Vector3 lengths = SegmentLengths[f];
            float radius = FingerRadii[f];
            bool isThumb = f == (int)Finger.Thumb;

            var rootJoint = new GameObject($"{(Finger)f}_Root").transform;
            rootJoint.SetParent(visualRoot.transform, worldPositionStays: false);
            if (isThumb)
            {
                // Thumb: starts at the palm edge, splayed outward and pre-rolled so its
                // local X curl axis closes it across the palm.
                rootJoint.localPosition = new Vector3(mirror * -0.032f, -0.012f, 0.03f);
                rootJoint.localRotation = Quaternion.Euler(20f, mirror * -40f, mirror * 35f);
            }
            else
            {
                rootJoint.localPosition = new Vector3(mirror * FingerX[f], 0f, KnuckleZ);
                rootJoint.localRotation = Quaternion.identity;
            }

            Transform midJoint = CreateSegment(rootJoint, lengths.x, radius * SegmentTaper[0], material, $"{(Finger)f}_Mid");
            Transform tipJoint = CreateSegment(midJoint, lengths.y, radius * SegmentTaper[1], material, $"{(Finger)f}_Tip");
            float tipRadius = radius * SegmentTaper[2];
            CreateSegmentVisual(tipJoint, lengths.z, tipRadius, material);
            CreateFingertip(tipJoint, lengths.z, tipRadius, material, nailMaterial);

            rig.SetFinger((Finger)f, new FingerJoints(rootJoint, midJoint, tipJoint));

            if (f == (int)Finger.Index)
            {
                var tipAnchor = new GameObject("Anchor_IndexTip").transform;
                tipAnchor.SetParent(tipJoint, worldPositionStays: false);
                tipAnchor.localPosition = new Vector3(0f, 0f, lengths.z);
                rig.IndexTip = tipAnchor;
                rig.IndexKnuckle = rootJoint; // curl-independent beam origin
            }
        }
    }

    /// <summary>
    /// Rounded palm (test #13): the single hard-edged slab read as a brick. Bevel
    /// approximation by stacking — two interpenetrating boxes (each smaller than
    /// the other on one axis) chamfer the edges, a capsule ridge fills the knuckle
    /// line, a mound rounds the thumb base and a capsule heel rounds the wrist end.
    /// Static visuals only: built once, zero per-frame cost.
    /// </summary>
    private static void BuildPalm(Transform parent, Material material, float mirror)
    {
        Vector3 center = new(0f, -0.008f, 0.045f);

        GameObject slabA = CreatePrimitivePart(PrimitiveType.Cube, parent, material);
        slabA.name = "Palm";
        slabA.transform.localPosition = center;
        slabA.transform.localScale = new Vector3(PalmBoxSize.x, PalmBoxSize.y * 0.72f, PalmBoxSize.z);

        GameObject slabB = CreatePrimitivePart(PrimitiveType.Cube, parent, material);
        slabB.name = "PalmBevel";
        slabB.transform.localPosition = center;
        slabB.transform.localScale = new Vector3(PalmBoxSize.x * 0.88f, PalmBoxSize.y, PalmBoxSize.z * 0.90f);

        // Knuckle ridge: capsule across the palm just behind the finger roots.
        GameObject knuckles = CreatePrimitivePart(PrimitiveType.Capsule, parent, material);
        knuckles.name = "KnuckleRidge";
        knuckles.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // Y-capsule → X-aligned
        knuckles.transform.localPosition = new Vector3(mirror * -0.005f, -0.006f, KnuckleZ - 0.006f);
        knuckles.transform.localScale = new Vector3(0.022f, 0.033f, 0.022f); // r 0.011, len 0.066

        // Thumb-base mound (thenar): the palm visibly thickens toward the thumb.
        GameObject thenar = CreatePrimitivePart(PrimitiveType.Sphere, parent, material);
        thenar.name = "ThumbMound";
        thenar.transform.localPosition = new Vector3(mirror * -0.026f, -0.012f, 0.032f);
        thenar.transform.localScale = new Vector3(0.030f, 0.020f, 0.042f);

        // Heel: rounded wrist end instead of a raw box edge.
        GameObject heel = CreatePrimitivePart(PrimitiveType.Capsule, parent, material);
        heel.name = "PalmHeel";
        heel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        heel.transform.localPosition = new Vector3(0f, -0.008f, 0.008f);
        heel.transform.localScale = new Vector3(0.024f, 0.026f, 0.024f);
    }

    /// <summary>Creates the next joint at the end of a segment and the segment's capsule visual.</summary>
    private static Transform CreateSegment(Transform parentJoint, float length, float radius,
        Material material, string nextJointName)
    {
        CreateSegmentVisual(parentJoint, length, radius, material);
        var next = new GameObject(nextJointName).transform;
        next.SetParent(parentJoint, worldPositionStays: false);
        next.localPosition = new Vector3(0f, 0f, length);
        // Joint sphere ON the new joint: when the curler bends it, the sphere keeps
        // the knuckle continuous instead of showing a gap between two capsules.
        GameObject joint = CreatePrimitivePart(PrimitiveType.Sphere, next, material);
        joint.name = "Joint";
        joint.transform.localScale = Vector3.one * (radius * 2.05f);
        return next;
    }

    private static void CreateSegmentVisual(Transform joint, float length, float radius, Material material)
    {
        GameObject capsule = CreatePrimitivePart(PrimitiveType.Capsule, joint, material);
        capsule.name = "Segment";
        capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        capsule.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        // Capsule primitive: height 2 along Y, radius 0.5 ⇒ scale to (2r, len/2, 2r).
        capsule.transform.localScale = new Vector3(radius * 2f, length * 0.5f + radius * 0.5f, radius * 2f);
    }

    /// <summary>
    /// Fingertip cap + fingernail hint (test #13): a slightly squashed sphere caps
    /// the distal segment; a small flattened, lighter-tinted box on the BACK of the
    /// segment (+Y = back of hand) reads as a nail at a glance.
    /// </summary>
    private static void CreateFingertip(Transform tipJoint, float length, float radius,
        Material material, Material nailMaterial)
    {
        GameObject cap = CreatePrimitivePart(PrimitiveType.Sphere, tipJoint, material);
        cap.name = "TipCap";
        cap.transform.localPosition = new Vector3(0f, 0f, length);
        cap.transform.localScale = new Vector3(radius * 1.9f, radius * 1.7f, radius * 2.0f);

        GameObject nail = CreatePrimitivePart(PrimitiveType.Cube, tipJoint, material);
        nail.name = "Nail";
        nail.GetComponent<Renderer>().sharedMaterial = nailMaterial;
        nail.transform.localPosition = new Vector3(0f, radius * 0.72f, length * 0.72f);
        nail.transform.localScale = new Vector3(radius * 1.2f, radius * 0.30f, length * 0.5f);
    }

    private static GameObject CreatePrimitivePart(PrimitiveType type, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        // Interactors are registry-driven — hand visuals must not collide with anything.
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static Material CreateHandMaterial(HandSide side)
    {
        // UNLIT ONLY (hardware test #7): the VR void and menu scenes have NO lights,
        // so a lit shader (Standard / Legacy Diffuse) renders pitch black — hands were
        // visible as dark silhouettes on the old grey void and vanished completely on
        // the black one. Sprites/Default is unlit (vertex-color tinted) and verified
        // shipped (decompiled ThirdParty GraphProgress/VertexView Shader.Find's it).
        Shader? shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default")
                         ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader);
        Color baseColor = Plugin.ParseHandColor();
        // Slight per-side tint so L/R stay distinguishable at a glance.
        material.color = side == HandSide.Left
            ? baseColor * new Color(0.92f, 0.96f, 1.05f, 1f)
            : baseColor;
        material.color = new Color(
            Mathf.Clamp01(material.color.r), Mathf.Clamp01(material.color.g),
            Mathf.Clamp01(material.color.b), 1f);
        return material;
    }

    /// <summary>Lighter tint of the hand material — the fingernail hint (unlit, like the hand).</summary>
    private static Material CreateNailMaterial(Material handMaterial)
    {
        var material = new Material(handMaterial);
        Color c = handMaterial.color;
        material.color = new Color(
            Mathf.Clamp01(c.r * 1.10f + 0.12f),
            Mathf.Clamp01(c.g * 1.10f + 0.12f),
            Mathf.Clamp01(c.b * 1.08f + 0.10f),
            1f);
        return material;
    }

    // ---- contract completion -------------------------------------------------------------

    private static void FillMissingAnchors(Transform handRoot, HandRig rig, HandSide side)
    {
        float mirror = side == HandSide.Right ? 1f : -1f;

        if (rig.Wrist == null)
            rig.Wrist = handRoot;

        if (rig.PalmCenter == null)
        {
            var palm = new GameObject("Anchor_Palm").transform;
            palm.SetParent(handRoot, worldPositionStays: false);
            palm.localPosition = PalmCenterPos;
            // +Y of the hand frame is the BACK of the hand ⇒ palm normal is -Y:
            // rotate 180° around Z so the anchor's +Y points out of the palm.
            palm.localRotation = Quaternion.Euler(0f, 0f, 180f);
            rig.PalmCenter = palm;
        }

        if (rig.GrabAnchor == null)
        {
            var grab = new GameObject("Anchor_Grab").transform;
            grab.SetParent(rig.PalmCenter, worldPositionStays: false);
            grab.localPosition = new Vector3(0f, 0.02f, 0f); // slightly off the palm surface
            rig.GrabAnchor = grab;
        }

        for (int f = 0; f < 5; f++)
        {
            FingerJoints joints = rig.GetFinger((Finger)f);
            if (joints.IsValid)
                continue;

            // Invisible joint chain at the procedural default positions — keeps the
            // curler and interactors functional even with an unmapped art asset.
            Vector3 lengths = SegmentLengths[f];
            var root = new GameObject($"Anchor_{(Finger)f}_Root").transform;
            root.SetParent(handRoot, worldPositionStays: false);
            root.localPosition = new Vector3(mirror * FingerX[f], 0f, f == 0 ? 0.03f : KnuckleZ);
            var mid = new GameObject($"Anchor_{(Finger)f}_Mid").transform;
            mid.SetParent(root, worldPositionStays: false);
            mid.localPosition = new Vector3(0f, 0f, lengths.x);
            var tip = new GameObject($"Anchor_{(Finger)f}_Tip").transform;
            tip.SetParent(mid, worldPositionStays: false);
            tip.localPosition = new Vector3(0f, 0f, lengths.y);
            rig.SetFinger((Finger)f, new FingerJoints(root, mid, tip));
        }

        if (rig.IndexTip == null)
        {
            FingerJoints index = rig.GetFinger(Finger.Index);
            var tipAnchor = new GameObject("Anchor_IndexTip").transform;
            tipAnchor.SetParent(index.Tip, worldPositionStays: false);
            tipAnchor.localPosition = new Vector3(0f, 0f, SegmentLengths[(int)Finger.Index].z);
            rig.IndexTip = tipAnchor;
        }

        rig.IndexKnuckle ??= rig.GetFinger(Finger.Index).Root;
    }
}

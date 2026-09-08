using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Creates and tracks <see cref="VRCard"/> objects; maps game widgets
/// (<c>AbilityCardUI</c>) 1:1 to VR cards. Loads the <c>CardBacking</c> mesh from
/// <c>gloomhavenvr.bundle</c> (asset contract:
/// unity/GloomhavenVR.Assets/Assets/Bundle/Table/README.md) with a procedural
/// fallback. All faces are restored to the game on <see cref="Clear"/> — wired to
/// scenario teardown, mode exits, and module shutdown (hot-reload clean).
/// </summary>
internal sealed class VRCardFactory
{
    private const string BundleFileName = "gloomhavenvr.bundle";
    private static readonly string[] BackingAssetPaths =
    {
        "Assets/Bundle/Table/CardBacking.prefab",
    };
    // Enum → bundle prefab path map for the switchable control board. Oak is the
    // original bundled board (default); Steel/Bronze are the two new boards added to
    // the bundle in parallel. GetTrayPrefab picks per CardsConfig.Board and falls back
    // to Oak (then the procedural board) when a selected prefab isn't in the bundle yet.
    private const string OakTrayPath = "Assets/Bundle/Table/PlayTray.prefab";
    private const string SteelTrayPath = "Assets/Bundle/Table/PlayTray_9capjqp6.prefab";
    private const string BronzeTrayPath = "Assets/Bundle/Table/PlayTray_16vm268h.prefab";

    private readonly Dictionary<AbilityCardUI, VRCard> _byWidget = new(16);
    private readonly List<VRCard> _all = new(16);

    private AssetBundle? _bundle;
    private bool _bundleOwned;
    private bool _bundleProbed;
    private GameObject? _backingPrefab;

    private Transform? _poolRoot;

    internal IReadOnlyList<VRCard> All => _all;

    internal Transform PoolRoot
    {
        get
        {
            if (_poolRoot == null)
            {
                var go = new GameObject("GloomhavenVR.Cards.Pool");
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.SetActive(false); // parked cards are invisible/inactive
                _poolRoot = go.transform;
            }
            return _poolRoot;
        }
    }

    // ------------------------------------------------------------------ cards --

    /// <summary>Get (or create) the VR card mirroring a game widget.</summary>
    internal VRCard GetOrCreate(AbilityCardUI widget)
    {
        if (_byWidget.TryGetValue(widget, out VRCard existing) && existing != null)
            return existing;

        VRCard card = CreateBlank();
        if (!card.AttachGameCard(widget))
            VRLog.Warn("Cards", $"Face adoption failed for {CardsGameApi.CardName(widget)} — backing only.");
        _byWidget[widget] = card;
        return card;
    }

    internal VRCard? Find(AbilityCardUI widget) =>
        _byWidget.TryGetValue(widget, out VRCard card) && card != null ? card : null;

    /// <summary>Create an unbound card (dev fake hand).</summary>
    internal VRCard CreateBlank()
    {
        var go = new GameObject("VRCard");
        go.transform.SetParent(PoolRoot, worldPositionStays: false);
        var card = go.AddComponent<VRCard>();
        card.Build(GetBackingPrefab());
        _all.Add(card);
        return card;
    }

    /// <summary>Restore + destroy the VR card for one widget (pool recycle guard).</summary>
    internal void ReleaseWidget(AbilityCardUI widget)
    {
        if (!_byWidget.TryGetValue(widget, out VRCard card))
            return;
        _byWidget.Remove(widget);
        if (card == null)
            return;
        card.DetachGameCard();
        _all.Remove(card);
        Object.Destroy(card.gameObject);
    }

    /// <summary>Restore faces of every card belonging to a dying hand.</summary>
    internal void ReleaseHand(CardsHandUI hand)
    {
        if (hand == null)
            return;
        List<AbilityCardUI> cards = hand.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                ReleaseWidget(cards[i]);
        }
    }

    /// <summary>Park a card invisibly without destroying it (mode exits).</summary>
    internal void Park(VRCard card)
    {
        card.ForgetActionHighlight();
        card.Grabbable = false;
        card.SetHome(PoolRoot, Vector3.zero, Quaternion.identity, 1f, instant: true);
    }

    /// <summary>Restore all faces and destroy all VR cards (scenario end / shutdown).</summary>
    internal void Clear()
    {
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            VRCard card = _all[i];
            if (card != null)
            {
                card.DetachGameCard();
                Object.Destroy(card.gameObject);
            }
        }
        _all.Clear();
        _byWidget.Clear();
    }

    // ------------------------------------------------------------------ assets --

    internal GameObject? GetBackingPrefab()
    {
        if (_backingPrefab == null)
            _backingPrefab = LoadPrefab(BackingAssetPaths);
        return _backingPrefab;
    }

    /// <summary>
    /// Load the control-board prefab for the selected <see cref="ControlBoard"/>. Tries the
    /// selected board's bundle path first; if it isn't in the bundle yet, falls back to the
    /// Oak (original) path; if that also fails, returns null so <c>PlayTray.EnsureBuilt</c>'s
    /// procedural fallback board kicks in. NOTE (per-board tuning seam): <c>[Cards]
    /// SlotCardInset</c> is still a single GLOBAL value; a future per-board descriptor keyed by
    /// <see cref="ControlBoard"/> may override it once the new boards' recess dimensions land.
    /// Two dials have already left this list: the card's fill factor became the per-board
    /// <c>[Cards] SlotOverlayScale_{board}</c> (2026-08-11, because the recess it fills is board
    /// geometry and it now sizes the slot overlays with it), and the round-button diameter became
    /// <c>[Cards] RestButtonDiameter_{board}</c>.
    /// </summary>
    internal GameObject? GetTrayPrefab()
    {
        ControlBoard board = CardsConfig.Board.Value;
        string selectedPath = TrayPrefabPath(board);

        GameObject? prefab = LoadPrefab(new[] { selectedPath });
        if (prefab != null)
        {
            VRLog.Info("Cards", $"Control board '{board}' → '{selectedPath}' loaded from bundle.");
            EnforceSingleSided(prefab, selectedPath);
            return prefab;
        }

        if (selectedPath != OakTrayPath)
        {
            prefab = LoadPrefab(new[] { OakTrayPath });
            if (prefab != null)
            {
                VRLog.Info("Cards", $"Control board '{board}' ('{selectedPath}') not in bundle — " +
                                    $"fell back to Oak ('{OakTrayPath}').");
                EnforceSingleSided(prefab, OakTrayPath);
                return prefab;
            }
        }

        VRLog.Info("Cards", $"Control board '{board}' prefab unavailable in bundle — " +
                            "using the procedural fallback board.");
        return null;
    }

    /// <summary>Bundle path of the control-board prefab for <paramref name="board"/> — the ONE
    /// enum → asset map, shared by the local board build and the remote board mirror so the two
    /// can never load different assets for the same style id.</summary>
    internal static string TrayPrefabPath(ControlBoard board) => board switch
    {
        ControlBoard.Steel => SteelTrayPath,
        ControlBoard.Bronze => BronzeTrayPath,
        _ => OakTrayPath,
    };

    /// <summary>
    /// The control-board prefab for <paramref name="board"/> from an ALREADY-LOADED bundle — the
    /// remote-board path (<c>Net.RemoteTrayVisual</c>): a peer's board mirror must never LOAD the
    /// bundle itself (Unity forbids loading the same bundle twice, and the instance
    /// <see cref="GetBundle"/> owns the load/unload lifecycle). By the time any remote board
    /// builds, the Hands or Cards module has the bundle resident in every normal run; when it is
    /// not (yet), this returns null and the caller keeps its procedural fallback and may probe
    /// again later. Falls back to the Oak prefab when the selected style's asset is not in the
    /// bundle — the same degradation <see cref="GetTrayPrefab"/> applies for the local board.
    /// </summary>
    internal static GameObject? PeekTrayPrefab(ControlBoard board)
    {
        AssetBundle? bundle = null;
        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded != null && loaded.name.Contains("gloomhavenvr"))
            {
                bundle = loaded;
                break;
            }
        }
        if (bundle == null)
            return null;
        GameObject? prefab = bundle.LoadAsset<GameObject>(TrayPrefabPath(board));
        if (prefab == null && TrayPrefabPath(board) != OakTrayPath)
            prefab = bundle.LoadAsset<GameObject>(OakTrayPath);
        // The peer's board is the SAME asset drawn by the SAME material, and the 1:1 rule says what
        // a peer sees of another player's board must match what that player sees of their own. The
        // rule is applied on every path that reaches the prefab, not only the local one.
        EnforceSingleSided(prefab, TrayPrefabPath(board));
        return prefab;
    }

    /// <summary>Board materials this process has already corrected — keyed by material instance id,
    /// because the three styles share nothing and a session may load two of them.</summary>
    private static readonly HashSet<int> CulledBoardMaterials = new();
    /// <summary>Tray prefab paths already reported. ONE line per STYLE, not one per session and
    /// not one per load: the prefab is fetched again on every board rebuild and on every remote
    /// board that appears, so a per-load line would be spam; but a per-session latch would hide a
    /// second style that arrives with a different cull mode, which is exactly the reading this line
    /// exists to give. Three paths exist, so this is bounded at three lines.</summary>
    private static readonly HashSet<string> LoggedBoardCullPaths = new();

    /// <summary>
    /// A CONTROL BOARD HAS NO BACK FACES (user, 2026-09-05: <i>"alle Controll boards sollen gar
    /// keine back-faces haben"</i>). <c>GloomhavenVR/BoardLit</c> defaults to <c>Cull Back</c>;
    /// <c>BuildBoard.cs</c> overrode it to <c>Cull Off</c> for a photogrammetry mesh with 1288–1639
    /// hole loops, and that mesh was replaced by authored, watertight geometry in the ModBuild 271
    /// rebuild. <c>BuildBoard.cs</c> now bakes <c>Cull Back</c> — but that is a BUNDLE input and a
    /// DLL-only drop does not carry it, so the same rule is applied here to whatever bundle is
    /// actually loaded. Against a re-baked bundle this finds nothing to do and says so.
    ///
    /// <para>MEASURED BEFORE IT WAS WRITTEN, on the shipped FBXes and the shipped bundle:
    /// <c>gen_winding.py</c> reports 0 inward-wound faces and positive, recalc-invariant signed
    /// volume on all three styles, and <c>PreviewBoard.CullBackCheck</c> puts the whole Cull Back /
    /// Cull Off difference at 86 / 6 / 10 px of 840 000, sitting as two strokes on an edge-on rim
    /// wall rather than anywhere a hole could be. So this removes back-face overdraw and reveals
    /// nothing.</para>
    ///
    /// <para>IT IS A WRITE TO A BUNDLE MATERIAL, AND THAT IS DELIBERATE AND SAFE. The material is
    /// ours, it is loaded once per process, and every board instance shares it — which is exactly
    /// the "ALL control boards" the user asked for, local and peer alike, in one write. It is NOT a
    /// <c>sharedMaterials</c> write on a renderer: <c>PeerBoardFade</c>'s private clones are
    /// untouched by it, and because the clone's depth settle hands back the ORIGINAL's cull mode,
    /// correcting the original also removes cull from the set of things a fade can change.
    /// Idempotent, and only <c>Off</c> is corrected — a material deliberately culling Front stays
    /// inside out.</para>
    /// </summary>
    private static void EnforceSingleSided(GameObject? prefab, string path)
    {
        if (prefab == null)
            return;
        int corrected = 0, already = 0;
        foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            foreach (Material? m in r.sharedMaterials)
            {
                if (m == null || !m.HasProperty(CullPropertyId))
                    continue;
                if (!CulledBoardMaterials.Add(m.GetInstanceID()))
                    continue;
                if (Mathf.Approximately(m.GetFloat(CullPropertyId), (float)UnityEngine.Rendering.CullMode.Off))
                {
                    m.SetFloat(CullPropertyId, (float)UnityEngine.Rendering.CullMode.Back);
                    corrected++;
                }
                else
                {
                    already++;
                }
            }
        }
        if (corrected == 0 && already == 0)
            return;
        if (!LoggedBoardCullPaths.Add(path))
            return;
        // HW-VERIFY
        VRLog.Note("Cards", $"BOARD CULL: '{path}' — {corrected} board material(s) were Cull Off " +
            $"and are now Cull Back, {already} already single-sided. A control board draws NO back " +
            "faces from here on, local and peer alike, in every state including mid-fade. The " +
            "Cull Off it used to ship was a workaround for the pre-rebuild photogrammetry shells " +
            "(1288-1639 hole loops), and the mesh it was written for no longer exists: gen_winding " +
            "reports 0 inward-wound faces and positive signed volume on all three authored boards, " +
            "and PreviewBoard.CullBackCheck puts the entire Cull Back / Cull Off difference at " +
            "86 / 6 / 10 px of 840 000, on an edge-on rim wall. A 'corrected' count above zero " +
            "means the loaded BUNDLE still bakes Cull Off and this DLL is doing the work; zero " +
            "corrected with a non-zero 'already' means the re-baked bundle arrived and this call " +
            "is now a no-op. THE FALSIFIER: if this line is absent from a session in which a " +
            "control board was on screen, the enforcement never ran on the path that built it — " +
            "look for a fourth loader of the tray prefab, not for a board that is somehow exempt.");
    }

    private static readonly int CullPropertyId = Shader.PropertyToID("_Cull");

    private GameObject? LoadPrefab(string[] candidates)
    {
        AssetBundle? bundle = GetBundle();
        if (bundle == null)
            return null;
        foreach (string path in candidates)
        {
            var prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab != null)
                return prefab;
        }
        return null;
    }

    private float _nextBundleRecoveryAt;
    private bool _bundleFailureLogged;

    /// <summary>Recover required bundled art through the existing single-owner loader. Remote
    /// views may request availability, but never create a second bundle ownership lifecycle.</summary>
    internal bool EnsureBoardAssets()
    {
        if (_bundle != null) return true;
        if (Time.unscaledTime < _nextBundleRecoveryAt) return false;
        _nextBundleRecoveryAt = Time.unscaledTime + 1f;
        _bundleProbed = false; // a previous missing-file/load refusal is retryable
        return GetBundle() != null;
    }

    /// <summary>
    /// The bundle may already be loaded by the Hands module (same file; Unity forbids
    /// loading a bundle twice) — reuse a loaded instance first, own it otherwise.
    /// </summary>
    private AssetBundle? GetBundle()
    {
        if (_bundleProbed)
            return _bundle;
        _bundleProbed = true;

        foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (loaded != null && loaded.name.Contains("gloomhavenvr"))
            {
                _bundle = loaded;
                _bundleOwned = false;
                _bundleFailureLogged = false;
                return _bundle;
            }
        }

        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string bundlePath = Path.Combine(pluginDir, BundleFileName);
        if (!File.Exists(bundlePath))
        {
            // ALERT, not Info: the bundle is a REQUIRED part of the install, and HandVisuals
            // says the same at the same tier — a player's log must name a missing 75 MB file.
            if (!_bundleFailureLogged) VRLog.Alert("Cards", $"gloomhavenvr.bundle NOT FOUND at {bundlePath} — procedural card/tray visuals " +
                                "active. The bundle is a required part of the release zip: unpack the zip again.");
            _bundleFailureLogged = true;
            return null;
        }

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        _bundleOwned = _bundle != null;
        if (_bundle == null && !_bundleFailureLogged)
            VRLog.Alert("Cards", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural visuals active. " +
                                $"Cause: {BundleDiagnostics.Explain(bundlePath)}");
        _bundleFailureLogged = _bundle == null;
        return _bundle;
    }

    /// <summary>Shutdown: destroy pool root, release the bundle if we own it.</summary>
    internal void Dispose()
    {
        Clear();
        if (_poolRoot != null)
        {
            Object.Destroy(_poolRoot.gameObject);
            _poolRoot = null;
        }
        if (_bundle != null && _bundleOwned)
            _bundle.Unload(unloadAllLoadedObjects: false);
        _bundle = null;
        _backingPrefab = null;
        _bundleProbed = false;
        _bundleOwned = false;
    }
}

using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cloned FRONT-art overlay for ONE card slot of a remote surface — a <see cref="RemoteHandFan"/>
/// slab, a <see cref="RemoteBoardCard"/> recess, or (since the user ruling of 2026-08-08) a slab of
/// the pile-browse and item fans via <see cref="RemotePileFronts"/>.
///
/// When (and ONLY when) <see cref="RevealGate.ShowRoundCardFronts"/> permits, the owning surface asks
/// each slot to show the REAL card face — art + enhancement stickers — of the corresponding card. We
/// render it by CLONING the game's own widget (<c>Object.Instantiate</c>) onto a world-space canvas
/// that floats a hair in front of the slab's owner-facing (−Z) side, matching the fan convention
/// "fronts look toward the owner". We NEVER adopt/reparent the LIVE widget (that would rip the face
/// out of the remote actor's own hidden hand UI and corrupt the game's bookkeeping) — always a
/// throwaway clone we fully own and destroy.
///
/// TWO KINDS OF SOURCE, ONE MECHANISM. An ABILITY card arrives as a <c>FullAbilityCard</c> (the
/// dedicated overload below, which also re-hands the runtime skin). Anything else arrives through the
/// generic <c>ShowFront(GameObject, key, …)</c> overload with a caller-supplied dedup key and an
/// optional "configure the clone before it activates" hook — which is what an ITEM card needs, because
/// its widget has to be manufactured from the pool per call and its model reference re-planted before
/// <c>OnEnable</c> dereferences it (see <see cref="RemoteItemCardSource"/>).
///
/// ANTI-CHEAT: this class holds NO gate logic of its own — it renders a face only when its owner
/// hands it a source widget, and every owner gates that strictly on
/// <see cref="RevealGate.ShowRoundCardFronts"/>. Every game deref here is null-guarded and the whole
/// clone is wrapped so that ANY failure leaves the slot showing NO front (the slab's card BACK
/// remains) — fail-safe = no leak. The clone is made fully NON-INTERACTIVE (raycasters removed +
/// a blocking <see cref="CanvasGroup"/>) so a local player can never poke a remote player's card
/// action buttons through it.
///
/// ENHANCEMENTS: the enhancement stickers ("Verbesserungen") live as child objects of the source
/// <c>fullAbilityCard</c> (populated in <c>MakeFullCard</c> onto the top/bottom action buttons), so
/// the deep <c>Instantiate</c> copy carries them along with whatever sprites the owner's widget had
/// loaded — the clone therefore shows the same enhancements the owner sees.
///
/// ART LOAD: the header illustration is loaded via an <c>ImageAddressableLoader</c> keyed by the
/// widget's runtime <c>_skin</c> (a NON-serialized field, so <c>Instantiate</c> does NOT copy it).
/// We best-effort re-hand the source skin to the clone so its own <c>OnEnable → ShowCard</c> reloads
/// the real header art even if the (hidden) source hand never had it loaded; if the source skin is
/// itself null the clone still shows the copied frame/actions/enhancements minus the header art.
///
/// Cheap to drive per frame: the clone is (re)built only when the shown card identity changes (or a
/// front first appears), and torn down on hide/rebuild/teardown — no per-frame allocations.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. It renders a face from a game-owned widget
/// resolved by <see cref="RemoteAbilityCardSource"/> off the host-replicated model. No card
/// identity, art reference or enhancement state ever crosses the wire; all of it is
/// DELIBERATELY-NOT. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteCardArt
{
    // Physical card size the clone is fit to (matches the slab it overlays).
    private readonly float _cardWidth;
    private readonly float _cardHeight;

    // Standoff toward −Z (owner side), a hair in front of the slab's front face so the art wins the
    // depth test over the card-back slab. The slab front face sits at z = −Thickness/2.
    private static readonly float FrontStandoff = CardMesh.Thickness * 0.5f + 0.0006f;

    // Fallback face pixel size when the cloned rect is degenerate.
    private static readonly Vector2 DefaultFaceSize = new(270f, 400f);

    // Small inset so the art sits just inside the slab silhouette (mirrors CardFace.BorderFraction).
    private const float BorderFraction = 0.06f;

    /// <summary>Re-run the shared mip-bake sprite swap over the shown clone this often (seconds) —
    /// the exact cadence the LOCAL card faces use (<c>CardFace.MipRescanInterval</c>). Needed
    /// because the clone's art arrives ASYNC (OnEnable → ShowCard → addressable header load) and
    /// the game can hand sub-widgets fresh mipless sprites after the build-time pass ran.</summary>
    private const float MipRescanInterval = 1f;

    private readonly Transform _slab;
    private GameObject? _host;      // world-space canvas host (child of the slab), inactive when hidden
    private Canvas? _canvas;
    private GameObject? _clone;     // the instantiated fullAbilityCard clone (child of _host)
    private int _shownSourceId = int.MinValue; // GetInstanceID of the source fullAbilityCard shown
    private float _nextMipRescan;   // unscaled time of the next cadenced mip-bake rescan

    /// <summary>
    /// The peer half of the local cards' zero-aliased-frame fix (see <see cref="Cards.CardArtWatch"/>).
    /// The clone runs the game's OWN widget, so its header art arrives through the same async
    /// addressable loader with the same two-continuation delay — and until this existed, a remote
    /// card showed the mipless original for up to <see cref="MipRescanInterval"/> exactly like a
    /// local one did. The 1:1 rule cuts both ways: a peer's card must not look worse on our screen
    /// than our own does.
    /// </summary>
    private readonly Cards.CardArtWatch _artWatch = new();

    public RemoteCardArt(Transform slab, float cardWidth, float cardHeight)
    {
        _slab = slab;
        _cardWidth = cardWidth;
        _cardHeight = cardHeight;
    }

    /// <summary>
    /// Is a front currently up that was built for <paramref name="key"/>? The dedup question asked
    /// from OUTSIDE, so a caller whose source widget does not exist yet (an ITEM card, which the game
    /// only ever manufactures from the pool — see <see cref="RemoteItemCardSource"/>) can skip the
    /// whole borrow when the right face is already showing. Without it every such caller would have
    /// to spawn a widget just to learn its instance id, which is precisely the per-frame cost the
    /// dedup exists to avoid.
    /// </summary>
    public bool ShowsKey(int key) => _clone != null && _host != null && _shownSourceId == key;

    /// <summary>
    /// Show the cloned face of <paramref name="source"/> (a remote hand card's live
    /// <c>fullAbilityCard</c>). Dedups by instance id — a no-op if the same source is already shown.
    /// Any failure clears the front (fail-safe to the slab back). Returns true iff a front is shown.
    /// </summary>
    public bool ShowFront(FullAbilityCard source)
    {
        if (source == null)
        {
            HideFront();
            return false;
        }
        return ShowFront(source.gameObject, source.GetInstanceID(), source);
    }

    /// <summary>
    /// The GENERIC clone path: show a throwaway copy of <paramref name="sourceGo"/>, dedup'd on a
    /// CALLER-supplied <paramref name="key"/>.
    ///
    /// <para>WHY THE KEY IS A PARAMETER RATHER THAN <c>sourceGo.GetInstanceID()</c>: an ITEM card has
    /// no long-lived widget anywhere on this client (the game manufactures one from the pool on
    /// demand), so its source object is a different instance on every call and would defeat instance-id
    /// dedup entirely — a clone rebuilt every frame, for every chip, for every peer. The item source
    /// keys on the ITEM instead, which is stable for as long as the peer keeps that item equipped, and
    /// so never rebuilds a settled fan. Keys from different KINDS of source must never meet on the same
    /// overlay: the pile-browse fan, which is the one surface that can switch between ability and item
    /// content on the same slabs, calls <see cref="HideFront"/> on every overlay when its content kind
    /// changes (see <see cref="RemotePileFronts.Tick"/>), so the key space is cleared with it.</para>
    ///
    /// <paramref name="skinSource"/> is the ability-card widget whose runtime <c>_skin</c> the clone
    /// needs re-handed (see <see cref="TryReapplySkin"/>); null for non-ability sources.
    /// <paramref name="beforeActivate"/> runs on the clone while it is still INACTIVE — the seam where
    /// a caller re-plants the non-serialized model reference its widget dereferences in
    /// <c>OnEnable</c> (an <c>ItemCardUI</c> reads <c>item.ID</c> the instant it activates).
    /// </summary>
    public bool ShowFront(GameObject sourceGo, int key, FullAbilityCard? skinSource = null,
        System.Action<GameObject>? beforeActivate = null)
    {
        if (sourceGo == null)
        {
            HideFront();
            return false;
        }

        if (_clone != null && key == _shownSourceId && _host != null)
        {
            if (!_host.activeSelf)
                _host.SetActive(true);
            // The hand fan calls ShowFront every frame while a front is up, so the steady-state
            // cadence rescan lives right here on the dedup path — the remote twin of
            // CardFace.Maintain's 1 s loop (the board slots, whose Set() is change-gated instead,
            // reach the same loop through MaintainMipBake below).
            MaintainMipBake();
            return true;
        }

        // Identity changed (or first show): rebuild the clone.
        DestroyClone();
        try
        {
            EnsureHost();
            if (_host == null)
                return false;

            // Build the clone UNDER the inactive host so the cloned widget's Awake/OnEnable does not
            // run until we have neutralized interaction and re-applied the skin.
            var clone = Object.Instantiate(sourceGo, _host.transform, worldPositionStays: false);
            clone.name = "FrontArtClone";
            _clone = clone;

            Neutralize(clone);
            StripFragileEffects(clone);
            if (skinSource != null)
                TryReapplySkin(skinSource, clone);
            beforeActivate?.Invoke(clone);

            // A clone copies the source's own activeSelf, and a widget BORROWED from the pool is
            // handed out deactivated (activate:false) so it can never render before the gate. Flip the
            // clone on here — it is still under the INACTIVE host, so nothing runs yet — rather than
            // leaving it to FitClone below: that way the host activation is what runs the widget's
            // OnEnable, and FitClone's pose write stays the FINAL one on every path, not just on the
            // paths whose source happened to be active.
            if (!clone.activeSelf)
                clone.SetActive(true);

            // Owned head camera renders the mod layer only; put the whole clone subtree on it (the
            // game face was on a game UI layer). It's a throwaway clone we own, so re-layering is safe.
            VRLayers.Apply(_host);

            _host.SetActive(true);   // clone activates → OnEnable → ShowCard reloads the real art
            FitClone(clone);         // final pose write, so OnEnable's own reposition can't offset it

            // MIP BAKE (user report: "the aliasing on the remote cards is extreme — the fix for my
            // own local cards should apply here too"). The clone's Image sprites are verbatim
            // copies of the source's, i.e. they sample the game's MIPLESS UI atlases — the exact
            // data defect CardFaceMipBake exists for, and the remote faces bypassed it entirely.
            // One immediate pass swaps everything already copied; the cadenced rescan (see
            // MaintainMipBake) catches the async header art and any sprite the widget re-assigns.
            // Shared cache: an atlas the local faces already baked costs nothing here, and vice
            // versa. The clone is a throwaway we own, so no restore pass is ever needed.
            RescanMips();

            _shownSourceId = key;
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"RemoteCardArt clone failed ({ex.Message}) — slot stays a card back.");
            DestroyClone();
            HideFront();
            return false;
        }
    }

    /// <summary>
    /// Cadenced mip-bake upkeep while a front is shown — the remote equivalent of the 1 s rescan
    /// loop in <c>CardFace.Maintain</c> (and <c>ItemsPile</c>'s hosted card): the clone's header
    /// illustration arrives ASYNC after activation, so a single build-time pass would leave
    /// exactly the biggest image on the card mipless. Idempotent-cheap once warm (dictionary hits
    /// inside <see cref="CardFaceMipBake.Rescan"/>); a hidden/absent front early-returns. Called
    /// per frame by the hand fan (via ShowFront's dedup path) and on the 4 Hz content cadence by
    /// the board-card owners, whose Set() is change-gated and would otherwise never rescan.
    /// </summary>
    public void MaintainMipBake()
    {
        if (_clone == null || _host == null || !_host.activeSelf)
            return;
        // ARRIVAL FIRST (zero-alloc, one reference compare per Image): the clone's header art
        // lands in a loader continuation, and this catches it in that very frame instead of
        // whenever the cadence below next happens to fire. The cadenced pass stays as the
        // backstop for Images the clone's widget created after the watch was captured.
        if (_artWatch.Poll("remote card") > 0)
            _nextMipRescan = Time.unscaledTime + MipRescanInterval;
        if (Time.unscaledTime < _nextMipRescan)
            return;
        RescanMips();
    }

    /// <summary>One shared-cache sprite-swap pass over the clone, cadence re-armed. Guarded inside
    /// <see cref="CardFaceMipBake.Rescan"/> itself (a bake surprise never breaks the face) and
    /// config-gated there on [Cards] FaceMipBake — the same switch the local cards obey.</summary>
    private void RescanMips()
    {
        if (_canvas != null)
        {
            CardFaceMipBake.Rescan(_canvas);
            _artWatch.Capture(_canvas); // (re)arm the per-frame arrival watch on the current clone
        }
        _nextMipRescan = Time.unscaledTime + MipRescanInterval;
    }

    /// <summary>Hide any front (the slab's card BACK shows through). Keeps the host for reuse.</summary>
    public void HideFront()
    {
        if (_clone != null || _shownSourceId != int.MinValue)
            DestroyClone();
        if (_host != null && _host.activeSelf)
            _host.SetActive(false);
    }

    public void Destroy()
    {
        DestroyClone();
        if (_host != null)
        {
            Object.Destroy(_host);
            _host = null;
            _canvas = null;
        }
    }

    // ------------------------------------------------------------------ internals --

    private void EnsureHost()
    {
        if (_host != null)
            return;

        _host = new GameObject("FrontArt");
        _host.transform.SetParent(_slab, worldPositionStays: false);
        // Float a hair in front of the slab's owner-facing (−Z) face, identity rotation so the uGUI
        // content reads from the −Z side — the exact convention VRCard's FaceCanvas uses.
        _host.transform.localPosition = new Vector3(0f, 0f, -FrontStandoff);
        _host.transform.localRotation = Quaternion.identity;
        _host.transform.localScale = Vector3.one;

        _canvas = _host.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
            _canvas.worldCamera = head;

        _host.SetActive(false); // stays inactive until a clone is built + neutralized
    }

    /// <summary>Make the clone purely cosmetic: remove uGUI raycasters and add a blocking
    /// <see cref="CanvasGroup"/> so a local poke/laser can never drive a remote card's buttons.
    /// The clone is still inactive here (no Awake has run), so component removal is side-effect free.</summary>
    private static void Neutralize(GameObject clone)
    {
        var raycasters = clone.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(includeInactive: true);
        for (int i = 0; i < raycasters.Length; i++)
        {
            if (raycasters[i] != null)
                Object.Destroy(raycasters[i]);
        }

        CanvasGroup cg = clone.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = clone.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false; // immediate + robust: blocks all raycasts to the subtree this frame
    }

    /// <summary>
    /// Strip the <c>CardEffects</c> / <c>ItemCardEffects</c> FX drivers from the clone BEFORE it
    /// activates. CardEffects'
    /// <c>Initialize</c> (Awake) swaps the card's image materials to a CUSTOM screen-space shader keyed
    /// by a <c>_PosAndBounds</c> that is only valid for the card's ORIGINAL hand-canvas position — on a
    /// detached world-space clone that shader is the known "card renders DEEP BLACK" hazard
    /// (CardFace.Maintain documents the identical burn/lose-popup failure). Removing the component while
    /// the clone is still INACTIVE means Initialize never runs, so every card Image keeps its plain
    /// copied material and renders the REAL art/frame/text/enhancements via the standard UI shader. We
    /// lose only the cosmetic holo/foil sheen — an acceptable, deterministic trade for a correct render.
    /// Uses <c>DestroyImmediate</c> because a deferred Destroy would still let Awake run this frame; safe
    /// here since the clone has never been active (no Awake yet) and all game refs to cardEffects are
    /// null-guarded in the widget code we let run.
    ///
    /// <para><c>ItemCardEffects</c> is the ITEM card's twin of the same hazard and is stripped for the
    /// same reason: it drives the identical <c>_PosAndBounds</c> screen-space material (ItemCardEffects.cs:15)
    /// plus a particle emitter whose size is only valid at the card's original canvas scale —
    /// <c>Cards.ItemsPile</c> keeps it alive only because it hosts the widget at a measured scale and
    /// clamps that emitter (<c>ClampCardEffectSmoke</c>). A detached clone has neither. The cost is the
    /// "spent"/"consumed" tint, which is a STATE decoration, not the card's identity — the art, title,
    /// symbol and condition icon all survive, so the card stays fully readable, which is what the
    /// ruling asks for. The strip also removes the one component <c>ItemCardUI.OnReturnedToPool</c>
    /// dereferences without a null check — harmless here because a clone is never recycled, and the
    /// BORROWED source is never touched by this method.</para>
    /// </summary>
    private static void StripFragileEffects(GameObject clone)
    {
        try
        {
            var effects = clone.GetComponentsInChildren<CardEffects>(includeInactive: true);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] != null)
                    Object.DestroyImmediate(effects[i]);
            }
            var itemEffects = clone.GetComponentsInChildren<ItemCardEffects>(includeInactive: true);
            for (int i = 0; i < itemEffects.Length; i++)
            {
                if (itemEffects[i] != null)
                    Object.DestroyImmediate(itemEffects[i]);
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Net", $"RemoteCardArt CardEffects strip skipped: {ex.Message}");
        }
    }

    /// <summary>Best-effort: re-hand the source widget's runtime <c>_skin</c> to the clone so its
    /// activation reloads the real header art (the skin is non-serialized → not copied by
    /// Instantiate). Non-fatal: on any failure the clone keeps its copied visuals minus the header.</summary>
    private static void TryReapplySkin(FullAbilityCard source, GameObject clone)
    {
        try
        {
            var cloneFull = clone.GetComponent<FullAbilityCard>();
            AbilityCardUISkin? skin = source._skin;   // publicized runtime field
            if (cloneFull != null && skin != null)
                cloneFull._skin = skin;               // OnEnable → ShowCard() will LoadAsync the art
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Net", $"RemoteCardArt skin reapply skipped: {ex.Message}");
        }
    }

    /// <summary>Centre + fit the clone rect to the physical card size, mirroring CardFace's host pose.
    /// Runs AFTER activation so it is the final write and the widget's own OnEnable reposition can't
    /// offset the art.</summary>
    private void FitClone(GameObject clone)
    {
        if (_canvas == null)
            return;

        var canvasRect = (RectTransform)_canvas.transform;
        RectTransform? cloneRect = clone.transform as RectTransform;
        if (cloneRect == null)
            return;

        Vector2 faceSize = cloneRect.rect.size;
        if (faceSize.x <= 1f || faceSize.y <= 1f)
            faceSize = DefaultFaceSize;

        // Host canvas sized to face pixels, scaled down to the physical card size (inset a touch).
        canvasRect.sizeDelta = faceSize;
        float fit = Mathf.Min(_cardWidth / faceSize.x, _cardHeight / faceSize.y) * (1f - BorderFraction);
        canvasRect.localScale = new Vector3(fit, fit, fit);

        // Centre the clone in the host canvas.
        var center = new Vector2(0.5f, 0.5f);
        cloneRect.anchorMin = center;
        cloneRect.anchorMax = center;
        cloneRect.pivot = center;
        cloneRect.anchoredPosition3D = Vector3.zero;
        cloneRect.localRotation = Quaternion.identity;
        cloneRect.localScale = Vector3.one;
        if (!cloneRect.gameObject.activeSelf)
            cloneRect.gameObject.SetActive(true);
    }

    private void DestroyClone()
    {
        if (_clone != null)
        {
            Object.Destroy(_clone);
            _clone = null;
        }
        _artWatch.Clear(); // the watched Images belong to the clone that just died
        _shownSourceId = int.MinValue;
    }
}

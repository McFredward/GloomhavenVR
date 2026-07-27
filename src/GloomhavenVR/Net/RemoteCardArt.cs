using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cloned FRONT-art overlay for ONE slot of a <see cref="RemoteHandFan"/> slab.
///
/// When (and ONLY when) <see cref="RevealGate.ShowRoundCardFronts"/> permits, the remote hand fan
/// asks each slot to show the REAL card face — art + enhancement stickers — of the corresponding
/// remote-actor hand card. We render it by CLONING the game's own
/// <c>AbilityCardUI.fullAbilityCard</c> widget (<c>Object.Instantiate</c>) onto a world-space canvas
/// that floats a hair in front of the slab's owner-facing (−Z) side, matching the fan convention
/// "fronts look toward the owner". We NEVER adopt/reparent the LIVE widget (that would rip the face
/// out of the remote actor's own hidden hand UI and corrupt the game's bookkeeping) — always a
/// throwaway clone we fully own and destroy.
///
/// ANTI-CHEAT: this class holds NO gate logic of its own — it renders a face only when
/// <see cref="RemoteHandFan"/> hands it a source widget, and the fan gates that strictly on
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

    private readonly Transform _slab;
    private GameObject? _host;      // world-space canvas host (child of the slab), inactive when hidden
    private Canvas? _canvas;
    private GameObject? _clone;     // the instantiated fullAbilityCard clone (child of _host)
    private int _shownSourceId = int.MinValue; // GetInstanceID of the source fullAbilityCard shown

    public RemoteCardArt(Transform slab, float cardWidth, float cardHeight)
    {
        _slab = slab;
        _cardWidth = cardWidth;
        _cardHeight = cardHeight;
    }

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

        int id = source.GetInstanceID();
        if (_clone != null && id == _shownSourceId && _host != null)
        {
            if (!_host.activeSelf)
                _host.SetActive(true);
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
            var clone = Object.Instantiate(source.gameObject, _host.transform, worldPositionStays: false);
            clone.name = "FrontArtClone";
            _clone = clone;

            Neutralize(clone);
            StripFragileEffects(clone);
            TryReapplySkin(source, clone);

            // Owned head camera renders the mod layer only; put the whole clone subtree on it (the
            // game face was on a game UI layer). It's a throwaway clone we own, so re-layering is safe.
            VRLayers.Apply(_host);

            _host.SetActive(true);   // clone activates → OnEnable → ShowCard reloads the real art
            FitClone(clone);         // final pose write, so OnEnable's own reposition can't offset it

            _shownSourceId = id;
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
    /// Strip the <c>CardEffects</c> FX driver from the clone BEFORE it activates. CardEffects'
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
        _shownSourceId = int.MinValue;
    }
}

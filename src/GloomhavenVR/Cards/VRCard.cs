using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// One physical card: 3D backing + world-space canvas hosting the game's live card
/// face (<see cref="CardFace"/>), proximity-grabbable (P2 <see cref="GrabbableBehaviour"/>,
/// snap-to-hand). Owned/pooled by <see cref="VRCardFactory"/>; laid out by
/// <see cref="CardFan"/>, <see cref="PlayTray"/> or <see cref="HalfSelection"/> via
/// the home-pose API. No per-frame allocations in <see cref="Update"/>.
/// </summary>
internal sealed class VRCard : GrabbableBehaviour, IGrabHighlight, IPokeable, IGrabbableHandFilter
{
    /// <summary>
    /// P7 (hardware test #10): hand excluded from ALL card interaction — the fan-
    /// owning (non-dominant) hand. Its palm sits inside the fan, so its own proximity
    /// hover kept flip-flopping highlights between two cards without any user input.
    /// Set every frame by CardsDriver; only the free (dominant) hand touches cards.
    /// </summary>
    internal static VRHand? InteractionBlockedHand;

    /// <summary>
    /// Single-winner hand-contact arbitration (user issue A/B): the hand whose fan/dock
    /// contact CardsDriver arbitrates every tick. Suppressed cards refuse THIS hand in
    /// <see cref="AllowsHand"/>, so the ProximityGrabber's highlight (and therefore the
    /// grab) always lands on the one arbitration winner — never on a card the hand merely
    /// brushes while sweeping.
    /// </summary>
    internal static VRHand? HandArbitrationHand;

    /// <summary>
    /// Issue A (fan sweep multi-lift): true while CardsDriver's per-tick single-winner
    /// arbitration elected a DIFFERENT card as the hand-contact winner. While set, this
    /// card's hand-driven pop sources (<see cref="_popped"/> proximity highlight,
    /// <see cref="_pokeHover"/> fingertip touch) are ignored — only ONE card lifts at a
    /// time — and <see cref="AllowsHand"/> refuses the arbitration hand so grab priority
    /// follows the same winner. The laser pop (<see cref="_laserPopped"/>) is never
    /// gated: the laser path already works single-winner on its own.
    /// </summary>
    private bool _handPopSuppressed;

    /// <summary>Set by CardsDriver's hand-contact arbitration (see <see cref="_handPopSuppressed"/>).</summary>
    internal void SetHandPopSuppressed(bool suppressed) => _handPopSuppressed = suppressed;

    /// <summary>
    /// Per-hand grab/hover gate (see <see cref="InteractionBlockedHand"/>). While the
    /// slot-dock apron is active, a docked card also YIELDS to the tray's grab bar:
    /// the apron-extended collider may overlap the bar's grab zone, and ProximityGrabber
    /// is single-winner by distance — without this gate the card could steal the
    /// highlight from a palm clearly placed at the bar (accidental card grabs, bar
    /// unreachable). The card only wins there when the palm is inside its CORE box.
    /// Issue A: a card suppressed by the driver's hand-contact arbitration additionally
    /// refuses the arbitration hand, so the proximity highlight/grab can only land on
    /// the single elected winner (the driver never suppresses the laser-hovered card,
    /// so laser plucks keep working unchanged).
    /// </summary>
    public bool AllowsHand(VRHand hand) =>
        !ReferenceEquals(hand, InteractionBlockedHand)
        && !(_handPopSuppressed && ReferenceEquals(hand, HandArbitrationHand))
        && !(_dockGrabPad && PalmClearlyAtTrayBar(hand));

    /// <summary>
    /// True when <paramref name="hand"/>'s palm is INSIDE the tray handle's grab zone
    /// (clearly at the bar) while NOT inside this card's core box (the original,
    /// un-padded collider volume plus the dock face thickness) — i.e. only the apron
    /// extension overlaps the palm. In that case the bar candidate must win.
    /// </summary>
    private bool PalmClearlyAtTrayBar(VRHand hand)
    {
        Collider? bar = PlayTray.Current?.HandleZone;
        if (bar == null || !bar.enabled || !bar.gameObject.activeInHierarchy)
            return false;
        Vector3 palm = hand.Rig.PalmCenter.position;

        // Palm inside the card's CORE box → the card legitimately wins even at the bar.
        // Issue B hysteresis (highlight "glitches away while the hand is at the card"):
        // while THIS card is the live proximity highlight its card-wins core is judged
        // ×DockPadStickyScale larger, so a palm resting right at the core boundary can
        // no longer flip-flop between card and bar on tracking jitter — the card only
        // yields once the palm has moved decisively toward the bar.
        Vector3 local = transform.InverseTransformPoint(palm);
        float sticky = _popped ? DockPadStickyScale : 1f;
        Vector3 half = _fullColliderSize * (0.5f * sticky);
        if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y
            && Mathf.Abs(local.z) <= Mathf.Max(half.z, DockPadDepth * 0.5f * sticky))
            return false;

        // "Clearly at the bar" = palm inside the bar's own grab zone volume.
        return (bar.ClosestPoint(palm) - palm).sqrMagnitude < 1e-10f;
    }

    private CardFace _face = new();
    private readonly BurnCardFx _burnFx = new();
    private RectTransform? _canvasRect;
    private Canvas? _canvas;
    private Transform? _visualRoot;

    // Backing/collider fit (P6): the backing and grab collider follow the LIVE face
    // canvas size exactly — no dead black margin around the card art.
    private Transform? _backing;
    private Vector3 _backingBaseScale;
    private Vector2 _backingBaseSize; // design size the base scale was authored for
    private BoxCollider? _box;
    private Vector3 _fullColliderSize;

    // Render-on-top (Bug #2): a card shown in FRONT of the opaque control board was painted
    // over by the board's action-button TMP label (ButtonCluster, queue 4003, ZTest Always +
    // ZWrite Off) — its text bled through the card. While a VRCard is visible we push BOTH the
    // opaque backing slab AND the world-space face-art graphics past those widgets
    // (CardMesh.HeldCardRenderQueue), using PER-INSTANCE materials so the shared card / live
    // game-UI materials (and the remote opponent hand backs) are never mutated. ZTest/ZWrite
    // are left at the shader defaults (LEqual + on), so the card still self-occludes and stays
    // hidden behind real geometry — only the draw ORDER changes. Mirrors FigureGrabbable's
    // ApplyRenderOnTop/RestoreRenderers. Instances are destroyed on restore (no leak).
    private bool _renderOnTop;
    private bool _renderOnTopLogged;
    private Renderer[]? _backingRenderers;
    private Material[][]? _backingOrigShared;
    private UnityEngine.UI.Graphic[]? _faceGraphics;
    private Material?[]? _faceOrigMats;
    private bool _canvasOrigOverrideSorting;
    private int _canvasOrigSortingOrder;
    private readonly System.Collections.Generic.List<Material> _renderOnTopInstances = new();
    private const int HeldCardSortingOrder = 200;

    // Home pose (local space of the current parent).
    private Vector3 _homePos;
    private Quaternion _homeRot = Quaternion.identity;
    private float _homeScale = 1f;
    private bool _instantNext;

    private bool _popped;
    private float _pop; // smoothed 0..1

    /// <summary>The game widget this card mirrors (null for dev placeholder cards).</summary>
    internal AbilityCardUI? GameCard { get; private set; }

    /// <summary>Shortcut: the adopted full card (null for placeholders).</summary>
    internal FullAbilityCard? FullCard => GameCard != null ? GameCard.fullAbilityCard : null;

    /// <summary>
    /// True when the face was yielded to a game dialog (see CardFace.Maintain) and
    /// should be re-adopted on the next rebuild.
    /// </summary>
    internal bool NeedsFace => GameCard != null && !_face.IsAdopted;

    /// <summary>Layout owners toggle this; combined with base "not already held".</summary>
    internal bool Grabbable { get; set; } = true;

    /// <summary>
    /// USER BUG B — the explicit ROOTED predicate: this card may not be grabbed in the
    /// current phase (committed/played cards docked during action execution, locked
    /// selection, modal block), so it must lie rooted — ZERO pop, ZERO scale change,
    /// ZERO hover haptic from hand and laser alike. Exactly the not-held complement of
    /// <see cref="CanGrab"/>: every hover-pop is a grab-affordance promise, and a card
    /// that refuses the grab must never make it. Action taps on the card face (top/
    /// bottom halves, consume buttons) route through the registered face canvas and the
    /// poke zones — independent of the pop system, so they keep working.
    /// </summary>
    internal bool IsRooted => !IsHeld && !CanGrab;

    // Rooted-flip diagnostic: log the transitions once (throttled, shared across cards)
    // so hardware logs show WHEN a card became rooted / grabbable again.
    private bool _wasRooted;
    private static float s_nextRootedLogAt;

    /// <summary>Raised on grip-release with palm velocity — CardsDriver routes fan/tray drops.</summary>
    internal event Action<VRCard, VRHand, Vector3>? Released;

    /// <summary>Raised when a hand closes on this card.</summary>
    internal event Action<VRCard, VRHand>? Grabbed;

    /// <summary>
    /// Poke-to-select (LoseCard/Recover* modes where the 2D UI is click-to-select).
    /// The card registers as a pokeable ONLY while enabled — the P2 PokeInteractor
    /// targets the single nearest pokeable, and a permanently registered card
    /// collider would swallow the half-selection zone pokes.
    /// </summary>
    internal bool PokeSelectEnabled
    {
        get => _pokeSelectEnabled;
        set
        {
            if (_pokeSelectEnabled == value)
                return;
            _pokeSelectEnabled = value;
            if (!isActiveAndEnabled)
                return;
            if (value)
            {
                Collider? collider = GetComponent<Collider>();
                if (collider != null)
                    VRInteractables.RegisterPokeable(this, collider);
            }
            else
            {
                VRInteractables.UnregisterPokeable(this);
            }
        }
    }

    private bool _pokeSelectEnabled;

    /// <summary>
    /// Test #9 (fingertip highlight): true while the free (dominant) hand's index tip is
    /// touching / just reaching this card's front face — driven by <see cref="CardFan"/>'s
    /// per-frame fingertip scan (NOT the global poke registry, which has no per-hand
    /// filter and would buzz the fan-owning hand). Pops the card exactly like the laser
    /// hover. Before this, a card only popped via <see cref="ProximityGrabber"/>, which
    /// measures from the PALM CENTER (0.13 m reach) — so the whole hand had to be pushed
    /// INTO the card before it lit.
    /// </summary>
    private bool _pokeHover;

    /// <summary>Set by <see cref="CardFan"/> when the dominant fingertip enters/leaves this
    /// card's front-face hover range (test #9). Returns whether the flag actually changed.</summary>
    internal bool SetFingertipHover(bool on)
    {
        if (_pokeHover == on)
            return false;
        _pokeHover = on;
        return true;
    }

    /// <summary>Raised on fingertip poke while <see cref="PokeSelectEnabled"/>.</summary>
    internal event Action<VRCard, VRHand>? Poked;

    internal bool IsHeld => Holder != null;

    /// <summary>
    /// Test #15: cards refuse NEW grabs while a dialog is open (ModalUI) — the Grab
    /// interactor stays enabled there so the tray handle keeps working, but the
    /// fan/slots must not be manipulable mid-dialog. A card already held when the
    /// dialog opens stays held (this only gates grab starts, incl. laser pluck).
    /// </summary>
    public override bool CanGrab => base.CanGrab && Grabbable
        && Core.Events.VRModeStateMachine.CurrentMode != Core.Events.VRMode.ModalUI;

    // ------------------------------------------------------------------ build --

    /// <summary>Create geometry (called once by the factory right after AddComponent).</summary>
    internal void Build(GameObject? backingPrefab)
    {
        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        _visualRoot = new GameObject("Visual").transform;
        _visualRoot.SetParent(transform, worldPositionStays: false);

        // Grab collider (trigger: never interacts with game physics; the P2 grabber
        // only uses Collider.ClosestPoint on registered colliders).
        _box = gameObject.GetComponent<BoxCollider>();
        if (_box == null)
            _box = gameObject.AddComponent<BoxCollider>();
        _box.size = new Vector3(w, h, 0.02f);
        _box.isTrigger = true;
        _fullColliderSize = _box.size;

        if (backingPrefab != null)
        {
            GameObject backing = Instantiate(backingPrefab, _visualRoot, false);
            backing.name = "Backing";
            _backing = backing.transform;
        }
        else
        {
            _backing = BuildProceduralBacking(_visualRoot, w, h);
        }
        // Asset contract (unity/.../Table/README.md): the backing is authored at the
        // configured card size — remember it so the backing can be re-fit to the
        // face canvas once the real face pixels are known (no visible dead margin).
        _backingBaseScale = _backing.localScale;
        _backingBaseSize = new Vector2(w, h);

        // World-space canvas hosting the live face. Sized in "face pixels", scaled
        // down to the physical card width. Registered with UguiPokeSurfaces so the
        // fingertip can poke the REAL uGUI buttons on the face (consume buttons,
        // default-action buttons) via the P2 synthesized-pointer path.
        var canvasGo = new GameObject("FaceCanvas");
        canvasGo.transform.SetParent(transform, worldPositionStays: false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        // Task #5: mod-pointer-only raycaster — the game's InControl input module
        // raycasts every canvas each frame at the PARKED mouse pixel through the
        // MOVING head camera, which made the card's half-hover FX wander with head
        // motion. CardFaceRaycaster answers only the laser/poke pointers.
        canvasGo.AddComponent<CardFaceRaycaster>();
        _canvasRect = (RectTransform)canvasGo.transform;
        SetCanvasSize(new Vector2(270f, 400f), w, h);
        // Face plane sits a hair in front of the backing. Convention everywhere in
        // this module: layouts orient roots with +Z pointing AWAY from the HMD, so
        // the viewer is on the -Z side — exactly the side uGUI/TMP/Quad render to
        // with identity rotation.
        _canvasRect.localPosition = new Vector3(0f, 0f, -0.0012f);

        // Mod-owned card shell (backing, canvas, colliders) on the mod layer. The live
        // game face (FullAbilityCard) re-parented in LATER keeps its own game layer —
        // game objects are never re-layered (CAMERA-POLICY §2 reversibility rule).
        Core.VRLayers.Apply(gameObject);

        UpdateCanvasCamera();
    }

    /// <summary>
    /// Fraction of the fitted face rect the VISIBLE card art actually fills — the backing
    /// and grab collider are fit to this, not the full rect, so CardFace's border inset no
    /// longer leaves a dark rectangular ring (test #25). Mirror of CardFace.BorderFraction
    /// (0.06 → art at 94 %); keep the two in sync.
    /// </summary>
    private const float VisibleFaceFraction = 0.94f;

    private void SetCanvasSize(Vector2 facePixels, float w, float h)
    {
        if (_canvasRect == null)
            return;
        _canvasRect.sizeDelta = facePixels;
        float fit = Mathf.Min(w / facePixels.x, h / facePixels.y);
        _canvasRect.localScale = new Vector3(fit, fit, fit);

        // P6 (hardware test #8): the face rarely matches the card's 63.5:88 aspect —
        // the letterboxed backing showed as a fat black border. Fit backing AND grab
        // collider exactly to the visible face rect instead.
        //
        // Test #25 ("NO black rectangular border"): CardFace insets the live art by its
        // BorderFraction (a small centered margin) inside this canvas, so a backing fit
        // to the FULL face rect still showed the dark mesh front as a rectangular ring in
        // that margin. Fit the backing + grab collider to the VISIBLE (inset) art instead,
        // so the dark front sits entirely BEHIND the art and only the thin physical rim
        // reads at the card edge. VisibleFaceFraction mirrors CardFace.BorderFraction
        // (kept in sync manually — both files describe the same ~6 % art inset).
        float faceW = facePixels.x * fit * VisibleFaceFraction;
        float faceH = facePixels.y * fit * VisibleFaceFraction;
        if (_backing != null && _backingBaseSize.x > 1e-5f && _backingBaseSize.y > 1e-5f)
        {
            _backing.localScale = new Vector3(
                _backingBaseScale.x * (faceW / _backingBaseSize.x),
                _backingBaseScale.y * (faceH / _backingBaseSize.y),
                _backingBaseScale.z);
        }
        if (_box != null)
        {
            _fullColliderSize = new Vector3(faceW, faceH, 0.02f);
            ResetColliderRegion(); // re-applies the dock grab pad when set
        }
    }

    private static Transform BuildProceduralBacking(Transform parent, float w, float h)
    {
        // P7 (test #10): a REAL 3D card body — rounded slab with ~1.5 mm thickness,
        // dark rim, opaque decorative back (see CardMesh). The live face canvas at
        // z = -0.0012 covers the front almost edge-to-edge; only the thin rim shows.
        var backing = new GameObject("Backing");
        backing.transform.SetParent(parent, worldPositionStays: false);
        backing.AddComponent<MeshFilter>().sharedMesh = CardMesh.Get(w, h);
        var renderer = backing.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { CardMesh.CreateEdgeMaterial(), CardMesh.CreateBackMaterial() };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return backing.transform;
    }

    private void UpdateCanvasCamera()
    {
        if (_canvas == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null && _canvas.worldCamera != head)
            _canvas.worldCamera = head;
    }

    // ------------------------------------------------------------------ face --

    /// <summary>Adopt the live face of a game card widget.</summary>
    internal bool AttachGameCard(AbilityCardUI card)
    {
        if (_canvasRect == null)
            return false;
        DetachGameCard();
        if (!_face.Adopt(card, _canvasRect))
            return false;
        GameCard = card;
        SetCanvasSize(_face.FaceSize, CardsConfig.CardWidth.Value, CardsConfig.CardHeight);
        name = $"VRCard_{CardsGameApi.CardName(card)}";
        // Bug #2: the live face art is now hosted under our canvas — push it (and the slab)
        // over the control-board button widgets so the button text can't bleed through.
        SetRenderOnTop(true);
        NeutralizeFaceHoverFx();
        return true;
    }

    /// <summary>Give the face back to the game (pool-safe). Idempotent.</summary>
    internal void DetachGameCard()
    {
        // Restore the game's own materials/sorting BEFORE handing the face back to the game.
        RestoreFaceHoverFx();
        SetRenderOnTop(false);
        if (_face.IsAdopted)
            _face.Restore();
        GameCard = null;
    }

    // ------------------------------------------------------------ face hover FX --

    // USER BUG B (docked card "pulses — rapidly grows, shrinks, grows — and vibrates
    // constantly" during the action phase): the adopted face keeps the game's own
    // ExtendedButton components ('Top button'/'Bottom button', consume buttons), whose
    // pointer-enter runs ToggleHighlight → a LeanTween SCALE of the button rect
    // (highlightScaleFactor) plus an anchoredPosition hoverMovement shift
    // (ExtendedButton.cs:407-478, verified ilspycmd). Under our world-space canvas the
    // synthesized pointers (finger poke via UguiPokeSurfaces, dominant laser via
    // RayUguiDriver) hover those rects — and the scale/move animation shifts the very
    // rect under the stationary pointer, so enter→grow→raycast-target-changes→exit→
    // shrink→re-enter loops forever: the visible pulse, with a HoverTick haptic on every
    // hovered-element change (RayUguiDriver.Tick). CardFace.Maintain additionally snaps
    // the face root scale back each frame, fighting the tween. Neutralize the SOURCE on
    // the adopted subtree: scale factor → 1 (tween becomes a no-op) and hoverMovement →
    // zero (no shift). The game's tint/selection highlight and all click paths are
    // untouched — action taps keep working. Originals restored on detach (pool-safe).
    private readonly System.Collections.Generic.List<(ExtendedButton button, float scale, Vector3 move)>
        _neutralizedHoverFx = new();

    private static bool s_loggedHoverFxNeutralized;

    private void NeutralizeFaceHoverFx()
    {
        if (_canvasRect == null)
            return;
        ExtendedButton[] buttons = _canvasRect.GetComponentsInChildren<ExtendedButton>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            ExtendedButton button = buttons[i];
            if (button == null)
                continue;
            _neutralizedHoverFx.Add((button, button.highlightScaleFactor, button.hoverMovement));
            button.highlightScaleFactor = 1f;
            button.hoverMovement = Vector3.zero;
        }
        if (buttons.Length > 0 && !s_loggedHoverFxNeutralized)
        {
            s_loggedHoverFxNeutralized = true;
            Core.VRLog.Info("Cards", $"Face hover FX neutralized on adopted cards ({buttons.Length} ExtendedButton(s) " +
                                     "on the first face): highlightScaleFactor→1, hoverMovement→0 — the game's " +
                                     "hover scale/shift no longer oscillates under the VR pointers (user bug B); " +
                                     "tint highlight + clicks unchanged, originals restored on detach.");
        }
    }

    private void RestoreFaceHoverFx()
    {
        for (int i = 0; i < _neutralizedHoverFx.Count; i++)
        {
            (ExtendedButton button, float scale, Vector3 move) = _neutralizedHoverFx[i];
            if (button != null)
            {
                button.highlightScaleFactor = scale;
                button.hoverMovement = move;
            }
        }
        _neutralizedHoverFx.Clear();
    }

    /// <summary>Dev placeholder face (no game card): colored quad + big index label.</summary>
    internal void BuildPlaceholderFace(int index)
    {
        if (_canvasRect == null)
            return;
        var img = new GameObject("Placeholder");
        img.transform.SetParent(_canvasRect, worldPositionStays: false);
        var rect = img.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = img.AddComponent<Image>();
        image.color = Color.HSVToRGB((index * 0.13f) % 1f, 0.45f, 0.85f);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(rect, worldPositionStays: false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        tmp.text = (index + 1).ToString();
        tmp.fontSize = 160f;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = new Color(0.1f, 0.1f, 0.1f);
        name = $"VRCard_Fake{index + 1}";
        // Bug #2: keep the dev placeholder on top of the board widgets too.
        SetRenderOnTop(true);
    }

    // ------------------------------------------------------------ render-on-top --

    /// <summary>
    /// Bug #2: draw this visible card OVER the control board's on-top HUD widgets (chiefly the
    /// ButtonCluster action-button label at queue 4003, ZTest Always + ZWrite Off, which
    /// otherwise bled its text THROUGH the card). <paramref name="on"/> = true pushes the
    /// backing slab AND the face-art canvas graphics to <see cref="CardMesh.HeldCardRenderQueue"/>
    /// via per-instance materials (never the shared card / game materials) and raises the face
    /// canvas' sorting so the art draws over the slab; false restores everything and frees the
    /// instances. Idempotent. Mirrors <see cref="Board.FigureGrab.FigureGrabbable"/>.
    /// </summary>
    internal void SetRenderOnTop(bool on)
    {
        // REVERTED (perspective fix): the render-on-top queue/sorting bump made the card face-art
        // canvas draw over the button widgets but ALSO swallowed all card TEXT (per-instance TMP
        // material copies broke the font rendering) and broke depth. The user wants perspective
        // respected everywhere except the sky, so cards now keep their native depth-correct render:
        // never apply the bump; always restore so any previously-bumped card reverts + frees its
        // instances. The button-text bleed-through is instead fixed on the widget side (proud-seat
        // + LEqual) as part of the mod-wide occlusion pass. Kept as a no-op-forward for call sites.
        _ = on;
        RestoreRenderOnTop();
    }

    private void ApplyRenderOnTop()
    {
        bool wasOn = _renderOnTop;
        RestoreRenderOnTop(); // drop any prior instances first (face graphics may have changed)
        int queue = CardMesh.HeldCardRenderQueue;

        // (1) Backing slab — per-renderer INSTANCE materials, queue bumped past the button
        // widgets. ZTest/ZWrite are left at the shader defaults (LEqual + on) so the slab still
        // self-occludes and hides behind real geometry; only the draw order changes.
        if (_backing != null)
        {
            _backingRenderers = _backing.GetComponentsInChildren<Renderer>(true);
            _backingOrigShared = new Material[_backingRenderers.Length][];
            for (int i = 0; i < _backingRenderers.Length; i++)
            {
                Renderer r = _backingRenderers[i];
                if (r == null)
                    continue;
                _backingOrigShared[i] = r.sharedMaterials;   // snapshot the ORIGINAL shared assets
                Material[] instances = r.materials;          // per-renderer INSTANCES (no shared mutation)
                for (int m = 0; m < instances.Length; m++)
                {
                    if (instances[m] == null)
                        continue;
                    instances[m].renderQueue = queue;
                    _renderOnTopInstances.Add(instances[m]);
                }
            }
        }

        // (2) Face-art world-space canvas — override sorting + high order so the art draws over
        // the slab, AND bump the graphics' MATERIAL renderQueue (queue dominates across renderer
        // types, so this is what actually beats the queue-4003 label MeshRenderer). Per-instance
        // materials so the live FullAbilityCard / game-UI materials are never mutated.
        if (_canvas != null)
        {
            _canvasOrigOverrideSorting = _canvas.overrideSorting;
            _canvasOrigSortingOrder = _canvas.sortingOrder;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = HeldCardSortingOrder;
        }
        if (_canvasRect != null)
        {
            _faceGraphics = _canvasRect.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            _faceOrigMats = new Material?[_faceGraphics.Length];
            for (int i = 0; i < _faceGraphics.Length; i++)
            {
                UnityEngine.UI.Graphic g = _faceGraphics[i];
                if (g == null)
                    continue;
                Material orig = g.material;
                _faceOrigMats[i] = orig;
                if (orig == null)
                    continue;
                var instance = new Material(orig) { renderQueue = queue };
                g.material = instance;
                _renderOnTopInstances.Add(instance);
            }
        }

        _renderOnTop = true;
        if (!wasOn && !_renderOnTopLogged)
        {
            _renderOnTopLogged = true;
            Core.VRLog.Info("Cards",
                $"VRCard render-on-top ON for '{name}' (queue {queue}, canvas sortingOrder " +
                $"{HeldCardSortingOrder}) — slab + face art now draw over the control-board " +
                "button widgets; ZTest LEqual kept so the card still hides behind real geometry.");
        }
    }

    private void RestoreRenderOnTop()
    {
        if (!_renderOnTop && _backingRenderers == null && _faceGraphics == null)
            return;

        // Face graphics back to their captured materials (may be null → default material).
        if (_faceGraphics != null && _faceOrigMats != null)
        {
            for (int i = 0; i < _faceGraphics.Length; i++)
            {
                UnityEngine.UI.Graphic g = _faceGraphics[i];
                if (g != null)
                    g.material = _faceOrigMats[i];
            }
        }
        if (_canvas != null)
        {
            _canvas.overrideSorting = _canvasOrigOverrideSorting;
            _canvas.sortingOrder = _canvasOrigSortingOrder;
        }

        // Backing renderers back to their ORIGINAL shared assets.
        if (_backingRenderers != null && _backingOrigShared != null)
        {
            for (int i = 0; i < _backingRenderers.Length; i++)
            {
                Renderer r = _backingRenderers[i];
                if (r != null && _backingOrigShared[i] != null)
                    r.sharedMaterials = _backingOrigShared[i];
            }
        }

        // Free the orphaned per-instance materials.
        for (int i = 0; i < _renderOnTopInstances.Count; i++)
        {
            if (_renderOnTopInstances[i] != null)
                Destroy(_renderOnTopInstances[i]);
        }
        _renderOnTopInstances.Clear();

        _backingRenderers = null;
        _backingOrigShared = null;
        _faceGraphics = null;
        _faceOrigMats = null;
        _renderOnTop = false;
    }

    // ------------------------------------------------------------------ layout --

    /// <summary>
    /// Set the animated home pose in the local space of <paramref name="parent"/>.
    /// Re-parents without moving the world pose, then Update() flies the card home.
    /// </summary>
    internal void SetHome(Transform parent, Vector3 localPos, Quaternion localRot, float scale, bool instant = false)
    {
        if (transform.parent != parent)
            transform.SetParent(parent, worldPositionStays: !instant);
        _homePos = localPos;
        _homeRot = localRot;
        _homeScale = scale;
        _instantNext = instant;
    }

    /// <summary>Extra forward pop + scale for the hovered card (set by layouts each frame is fine — plain field).</summary>
    internal void SetPopped(bool popped) => _popped = popped;

    /// <summary>
    /// Laser hover (P6, Demeo pluck): separate flag so the dominant hand's ray and the
    /// proximity highlight never stomp each other — the card pops while EITHER is set.
    /// </summary>
    internal void SetLaserHover(bool hovered) => _laserPopped = hovered;

    private bool _laserPopped;

    /// <summary>
    /// T2 (fan grab misses): extra grab-accept depth a FANNED card's collider grows
    /// TOWARD THE VIEWER (-Z, the side the reaching hand approaches from), on top of the
    /// 0.02 exact-fit thickness. The exact-fit box demanded the palm essentially touch
    /// the card plane before ProximityGrabber would highlight it; a modest 3 cm apron on
    /// the approach side accepts the hand just BEFORE contact — mirroring the slot-dock
    /// apron pattern (<see cref="DockPadDepth"/>) that fixed the same miss on the tray.
    /// Card-local meters, so it scales with the fan/diorama exactly like the card.
    /// Neighbor arbitration unchanged: the strip width still bounds X, and the pad is
    /// identical on every fanned card, so nearest-collider + the 2.5 cm switch margin
    /// resolve overlaps exactly as before.
    /// </summary>
    private const float FanGrabDepthPad = 0.03f;

    /// <summary>
    /// P6 fan-collider strip: while fanned, each card's grab collider shrinks to its
    /// VISIBLE (un-overlapped) strip so neighboring colliders never fight for the
    /// hover — the source of the constant haptic buzz in test #8. The full collider
    /// comes back via <see cref="ResetColliderRegion"/> (grab, tray, half layout).
    /// T2: the fan strip additionally grows a viewer-side accept apron
    /// (<see cref="FanGrabDepthPad"/>) so the reaching hand highlights the card a
    /// touch earlier — the highlighted card then wins the trigger via the fan
    /// lift-priority rescue (CardsDriver.UpdateFanLaser).
    /// </summary>
    internal void SetColliderRegion(float width, float offsetX)
    {
        if (_box == null)
            return;
        _dockGrabPad = false; // fan strips own the collider shape — a fanned card is never slot-docked
        _box.size = new Vector3(Mathf.Min(width, _fullColliderSize.x), _fullColliderSize.y,
            _fullColliderSize.z + FanGrabDepthPad);
        // Bias the extra depth entirely onto the viewer side (-Z): the hand approaches
        // from there; the far side keeps the exact fit so the pad never pokes through
        // the palm the fan hovers over.
        _box.center = new Vector3(offsetX, 0f, -FanGrabDepthPad * 0.5f);
    }

    // Task #2 follow-up (grab UNDER the docked card): the exact-fit collider is thin
    // (0.02) and the tray shows docked cards at ~half scale, so a palm slightly BELOW/
    // beside a slot-docked card fell outside the ProximityGrabber highlight — no
    // hover-lift, and the trigger fell through to board actions. While slot-docked the
    // grab collider grows a modest accept apron, biased DOWN (the reported miss).
    // Card-local units, so the pad scales with the tray exactly like the card does.
    // Single-winner semantics unchanged: ProximityGrabber still picks the NEAREST
    // collider with its 2.5 cm switch margin, and the two slot cards sit far enough
    // apart that a padded neighbor can only win when it really is closer.
    // HARDWARE ROUND 2 (apron too big): the first cut (below +1.0×H, sides/above
    // +0.35×H, depth 0.06) swallowed the tray's GRAB BAR under the board and caused
    // accidental card grabs. Shrunk back toward the original — enough to keep the
    // "slightly under the card" accept, no longer reaching the bar — and, belt and
    // braces, AllowsHand yields to the bar whenever the palm is INSIDE the bar's grab
    // zone but outside the card's core box (see PalmClearlyAtTrayBar).
    private const float DockPadSideFrac = 0.10f;  // × card width, each side
    private const float DockPadUpFrac = 0.15f;    // × card height, above
    private const float DockPadDownFrac = 0.35f;  // × card height, below — the under-grab
    private const float DockPadDepth = 0.04f;     // face-normal thickness while docked

    /// <summary>
    /// Issue B hysteresis: while a docked card is the live proximity highlight its accept
    /// apron (and the bar-yield core test) grows by this factor — ENTER zone = the base
    /// pads, EXIT zone = ×1.3. A palm resting right at the apron edge (or at the bar-yield
    /// core boundary) used to flicker the highlight on tracking jitter, and a trigger in a
    /// flicker-off frame fell through to board actions ("grabbing slightly off does
    /// nothing"). All pads stay CARD-LOCAL units, so both zones scale with the board.
    /// </summary>
    private const float DockPadStickyScale = 1.3f;

    private bool _dockGrabPad;

    /// <summary>
    /// Issue B (neighbor separation): the runtime slot pitch of the control board in
    /// CARD-LOCAL units (this collider's space) — computed from the live slot transforms
    /// so it tracks every board scale/config. -1 when no board/slots exist. Used to clamp
    /// the apron side pads so two docked cards' grab zones can never cross the midline
    /// between the recesses, no matter how the pad fractions or SlotCardFill are tuned.
    /// </summary>
    private float DockSlotPitchLocal()
    {
        PlayTray? tray = PlayTray.Current;
        Transform? s0 = tray != null ? tray.SlotTransform(0) : null;
        Transform? s1 = tray != null ? tray.SlotTransform(1) : null;
        if (s0 == null || s1 == null)
            return -1f;
        float lossy = transform.lossyScale.x;
        return lossy > 1e-5f ? Vector3.Distance(s0.position, s1.position) / lossy : -1f;
    }

    /// <summary>
    /// Enable/disable the slot-dock grab apron (PlayTray sets it on dock, clears it on
    /// undock; grabbing or fan-stripping the card clears it implicitly). Idempotent.
    /// </summary>
    internal void SetDockGrabPad(bool on)
    {
        if (_dockGrabPad == on)
            return;
        _dockGrabPad = on;
        ResetColliderRegion();
    }

    /// <summary>
    /// Restore the full-card grab collider (see <see cref="SetColliderRegion"/>) —
    /// including the dock apron while the card is slot-docked (<see cref="SetDockGrabPad"/>).
    /// </summary>
    internal void ResetColliderRegion()
    {
        if (_box == null)
            return;
        if (_dockGrabPad)
        {
            // Issue B hysteresis: the apron a hand must ENTER is the base pads; once this
            // card holds the highlight the EXIT apron grows ×DockPadStickyScale so jitter
            // at the zone edge cannot flicker the highlight (and eat the grab). Everything
            // is card-local, so the whole zone scales with the board.
            float sticky = _popped ? DockPadStickyScale : 1f;
            float padX = _fullColliderSize.x * DockPadSideFrac * sticky;
            float padUp = _fullColliderSize.y * DockPadUpFrac * sticky;
            float padDown = _fullColliderSize.y * DockPadDownFrac * sticky;
            // Neighbor separation: the side pads may never push this card's grab zone past
            // the midline to the neighboring slot recess — clamp against the RUNTIME slot
            // pitch (95 % of the half-gap leaves a small dead seam between the two aprons).
            float pitch = DockSlotPitchLocal();
            if (pitch > 0f)
                padX = Mathf.Min(padX, Mathf.Max(0f, (pitch - _fullColliderSize.x) * 0.5f * 0.95f));
            _box.size = new Vector3(
                _fullColliderSize.x + padX * 2f,
                _fullColliderSize.y + padUp + padDown,
                Mathf.Max(_fullColliderSize.z, DockPadDepth * sticky));
            _box.center = new Vector3(0f, -(padDown - padUp) * 0.5f, 0f);
            return;
        }
        _box.size = _fullColliderSize;
        _box.center = Vector3.zero;
    }

    // Feature 6 (ACTIVE CARDS) highlight: the active region is now drawn by the game's
    // OWN action-region highlight (FullAbilityCard.ToggleHighlightHover /
    // CardActionHighlight), driven by CardsDriver on the live re-parented card — the exact
    // mouse-over visual. The old translucent-gold overlay quads are retired.

    // -------------------------------------------------------------- interaction --

    // Held-pose target in GrabAnchor local space (captured once per grab).
    private Vector3 _heldPos;
    private Quaternion _heldRot = Quaternion.identity;
    private float _heldScale = 1f;

    /// <summary>
    /// Fraction of the card height between the bottom edge and the pinch anchor:
    /// the fingers grip the card's lower-edge area ~12 % up from the bottom, like a
    /// real card pinched at its corner.
    /// </summary>
    private const float PinchGripFraction = 0.12f;

    /// <summary>
    /// P8 (hardware test #12, "pinch grip"): the held card is pinched BETWEEN THUMB
    /// AND INDEX — its lower-edge area sits exactly at the pinch point, the midpoint
    /// between the thumb tip and the index tip of the holding hand, sampled at grab
    /// time (the fingers are curled onto the grip right then, i.e. the physical
    /// pinch pose). The card is parented to the GrabAnchor, so it follows the wrist
    /// 1:1 (P7 behavior kept); NO per-frame auto-facing.
    ///
    /// Frames: GrabAnchor is a child of PalmCenter with identity rotation
    /// (HandVisuals.FillMissingAnchors), so its axes are the palm frame of the
    /// HandRig contract — +Y = palm normal (out of the palm), +Z = along the
    /// fingers; identical for the bundle gloves (Hands README: 'Anchor_Palm, +Y
    /// must point OUT of the palm', 'Anchor_Grab' child of it; missing joints are
    /// synthesized, so Anchor_{Thumb|Index}_Tip — or the SteamVR bone fallback
    /// finger_{thumb|index}_2_{l|r} — always exist). Card frame: +Z away from the
    /// viewer, +Y = card top.
    ///
    /// Rotation (test #13, readable-at-rest): the old Euler(90°,0,0) laid the card
    /// flat on the palm plane — readable only after a hard supination. Now the FACE
    /// NORMAL is the palm normal (+Y) leaned [Cards] HeldFaceBias° back toward the
    /// wrist (−Z): in a relaxed grip (grip pose ~60° pitched, [Hands]
    /// GripPitchOffsetDegrees frame) the fingers point forward/slightly down, so
    /// −Z runs back and UP toward the head — at the ~65° default the face points
    /// at your eyes without any wrist twist. The card TOP points to the thumb side
    /// (±X, mirrored per hand), which is world-up in a relaxed grip — the card
    /// stands up out of the pinch exactly like a really held playing card. Still a
    /// FIXED local rotation: 1:1 wrist-follow, NO per-frame auto-facing.
    ///
    /// Position: pinch = midpoint(thumb tip, index tip) in GrabAnchor local space
    /// (InverseTransformPoint divides the diorama scale back out, so the result is
    /// in the same real-meter units as the card constants) + [Cards]
    /// HeldPinchOffset; the card CENTER then sits (0.5 − PinchGripFraction) · h
    /// along the card's own up axis above that pinch. Fallback (rig without valid
    /// thumb/index joints — never the case for procedural or bundle hands, but the
    /// contract allows partial rigs): the pre-P8 palm-offset approximation
    /// ([Cards] HeldOffPalm/HeldForward).
    /// </summary>
    protected override HeldPose GetHeldPose(VRHand hand)
    {
        float scale = CardsConfig.InspectScale.Value;
        float cardH = CardsConfig.CardHeight * scale;

        // GrabAnchor frame: +Y out of the palm, +Z along the fingers, ±X thumb side.
        float bias = CardsConfig.HeldFaceBias.Value * Mathf.Deg2Rad;
        var faceNormal = new Vector3(0f, Mathf.Cos(bias), -Mathf.Sin(bias));
        float thumbSide = hand.Side == HandSide.Right ? 1f : -1f;
        // Card +Z (away from the viewer) = −faceNormal; card top (+Y) = thumb side.
        var rot = Quaternion.LookRotation(-faceNormal, new Vector3(thumbSide, 0f, 0f));

        Vector3 pinchLocal;
        FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb);
        FingerJoints index = hand.Rig.GetFinger(Finger.Index);
        if (thumb.IsValid && index.IsValid)
        {
            Vector3 pinchWorld = (thumb.Tip.position + index.Tip.position) * 0.5f;
            pinchLocal = hand.Rig.GrabAnchor.InverseTransformPoint(pinchWorld);
        }
        else
        {
            pinchLocal = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
        }
        pinchLocal += CardsConfig.HeldPinchOffset.Value;

        Vector3 pos = pinchLocal + rot * new Vector3(0f, cardH * (0.5f - PinchGripFraction), 0f);
        return new HeldPose(pos, rot, scale);
    }

    public override void OnGrab(VRHand hand)
    {
        // Keep the world pose across the re-parent — TickHeldPose flies the card from
        // its fan slot into the hand instead of teleporting it.
        Vector3 worldPos = transform.position;
        Quaternion worldRot = transform.rotation;
        Vector3 worldScale = transform.localScale;
        base.OnGrab(hand); // snap to GrabAnchor at GetHeldPose (P2/P5 GrabbableBehaviour)
        // Capture the local target the base snap applied, then fly in from the old pose.
        _heldPos = transform.localPosition;
        _heldRot = transform.localRotation;
        _heldScale = transform.localScale.x;
        transform.position = worldPos;
        transform.rotation = worldRot;
        transform.localScale = worldScale;
        _dockGrabPad = false;  // in-hand: exact-card collider again (the apron is a docked-only affordance)
        ResetColliderRegion(); // full card again (fan strips, see SetColliderRegion)
        _laserPopped = false;
        _handPopSuppressed = false; // a held card is out of the contact arbitration pool
        _releaseGlide = 0f; // re-grab mid-glide: the held pose takes over cleanly
        try
        {
            Grabbed?.Invoke(this, hand);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"VRCard.Grabbed subscriber threw: {ex}");
        }
    }

    /// <summary>
    /// Fly-in to the in-hand pose: the card sits at the pinch point (position lerped
    /// in GrabAnchor-local space, so it tracks the wrist 1:1) but per-frame BILLBOARDS
    /// its face to the head — same convention as CardFan.Tick — so a grabbed card is
    /// readable without twisting the wrist. The initial grab snap (_heldRot) still
    /// orients the card the instant it is picked up; this override then eases the face
    /// toward the viewer. Scale lerps as before.
    /// </summary>
    private void TickHeldPose()
    {
        float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, _heldPos, t);
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = transform.position - head.transform.position; // cards' +Z away from viewer (uGUI reads from -Z)
            if (away.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(away.normalized, head.transform.up), t);
        }
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _heldScale, t);
    }

    /// <summary>How long (unscaled seconds) the post-release home flight runs on unscaled
    /// time. The flight itself is the existing exponential home-lerp (CardLerpSpeed 14/s
    /// ≈ 95 % converged in ~0.21 s — a fast but visible ease-out glide); this window only
    /// keeps it moving while the game pauses simulation time (card phases pause timeScale,
    /// same reason the fan reveal animation runs unscaled).</summary>
    private const float ReleaseGlideSeconds = 0.35f;

    /// <summary>Unscaled seconds of release glide remaining (0 = none). See <see cref="OnRelease"/>.</summary>
    private float _releaseGlide;

    // ---------------------------------------------------------------- fly-to-pile --

    // Issue 5 (user): when the control board is cleared, a just-played round card must NOT simply
    // vanish — it flies into its destination pile (burned → burnt stack, discarded → discard
    // stack). CardsDriver launches this in place of the instant park; the card animates in WORLD
    // space (position + scale, ease-out) on UNSCALED time (card phases pause timeScale), then the
    // completion callback parks/hides it as before. Purely VR presentation — the game's own 2D pile
    // state is untouched. A flying card is out of every zone/hover/grab path.
    private bool _flying;
    private Vector3 _flyFromPos;
    private Quaternion _flyFromRot;
    private Vector3 _flyFromScale;
    private Vector3 _flyToPos;
    private Vector3 _flyToScale;
    private float _flyElapsed;
    private float _flyDuration;
    private Action? _flyDone;

    /// <summary>True while this card is animating into a pile (see <see cref="FlyToPile"/>).</summary>
    internal bool IsFlying => _flying;

    /// <summary>
    /// Issue 5: fly this card from its current world pose into a pile stack over
    /// <paramref name="duration"/> unscaled seconds, shrinking to <paramref name="targetWorldWidth"/>
    /// (the pile slab's on-screen width), then invoke <paramref name="onComplete"/> (CardsDriver
    /// parks/hides it there). No-op-safe to call while active/visible; a held card is never flown
    /// (CardsDriver only launches this on cleared, un-held round cards). Rotation is held constant —
    /// the card slides in flat, as it sat on the board.
    /// </summary>
    internal void FlyToPile(Vector3 targetWorldPos, float targetWorldWidth, float duration, Action onComplete)
    {
        _flying = true;
        _flyElapsed = 0f;
        _flyDuration = Mathf.Max(0.05f, duration);
        _flyFromPos = transform.position;
        _flyFromRot = transform.rotation;
        _flyFromScale = transform.localScale;
        _flyToPos = targetWorldPos;
        // Convert the wanted on-screen width into a LOCAL scale under the current parent, so the
        // card ends roughly the size of a pile slab regardless of the board's live diorama scale.
        float parentLossy = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        float w = CardsConfig.CardWidth.Value;
        float targetLocal = (parentLossy > 1e-5f && w > 1e-5f)
            ? targetWorldWidth / (parentLossy * w)
            : transform.localScale.x;
        _flyToScale = Vector3.one * Mathf.Max(1e-4f, targetLocal);
        _flyDone = onComplete;
        // Drop every hover/grab affordance — a flying card makes no promises.
        Grabbable = false;
        _popped = false;
        _laserPopped = false;
        _pokeHover = false;
        _handPopSuppressed = false;
        _pop = 0f;
        _releaseGlide = 0f;
        _instantNext = false;
    }

    /// <summary>Cancel any in-flight fly-to-pile (teardown / re-adoption). Does NOT run the callback.</summary>
    private void CancelFly()
    {
        _flying = false;
        _flyDone = null;
    }

    public override void OnRelease(VRHand hand, Vector3 velocity)
    {
        // Glide-back, not teleport: base.OnRelease → DetachFromHand restores the PRE-GRAB
        // parent AND the pre-grab LOCAL pose (worldPositionStays: false) — the card used to
        // SNAP to its origin the instant the fingers opened. Mirror OnGrab's fly-in: keep
        // the world pose across the re-parent so the card stays at the release point, then
        // let Update's home-lerp fly it back to whatever home the layout asserts next
        // (fan slot on a void release, tray slot on a play, …). Layout-owned instant seeds
        // (fan open/close animation, pool return) still win via _instantNext — the glide
        // never fights them. Side benefit: CardsDriver's drop routing now samples the card
        // at its REAL drop position instead of the already-snapped-home pose.
        Vector3 worldPos = transform.position;
        Quaternion worldRot = transform.rotation;
        Vector3 worldScale = transform.localScale;
        base.OnRelease(hand, velocity); // restore pre-grab parent
        transform.position = worldPos;
        transform.rotation = worldRot;
        transform.localScale = worldScale; // scale glides home with the pose (was an instant snap)
        _releaseGlide = ReleaseGlideSeconds;
        try
        {
            Released?.Invoke(this, hand, velocity);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"VRCard.Released subscriber threw: {ex}");
        }
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        bool changed = _popped != highlighted;
        _popped = highlighted;
        // Issue B hysteresis: while slot-docked the accept apron (and the bar-yield core)
        // grows on highlight and shrinks back on un-highlight — enter zone < exit zone.
        // Fan strips own the collider shape, so this only re-applies for docked cards.
        if (changed && _dockGrabPad)
            ResetColliderRegion();
    }

    protected override void OnEnable()
    {
        base.OnEnable(); // grab registration (P2 GrabbableBehaviour)
        if (_pokeSelectEnabled)
        {
            Collider? collider = GetComponent<Collider>();
            if (collider != null)
                VRInteractables.RegisterPokeable(this, collider);
        }
    }

    public void OnPokeEnter(VRHand hand)
    {
        // Rooted gate (user bug B): zero pop, zero haptic while the card refuses grabs.
        if (PokeSelectEnabled && AllowsHand(hand) && !IsRooted)
        {
            _popped = true;
            hand.SendHaptic(HapticPreset.HoverTick);
        }
    }

    public void OnPokeExit(VRHand hand)
    {
        if (PokeSelectEnabled)
            _popped = false;
    }

    /// <summary>
    /// Test #9: distance from a world-space fingertip to this card's grab collider (0 when
    /// inside). Used by <see cref="CardFan"/> to pop the card the free hand's index tip is
    /// touching. False when the collider is missing/disabled. Allocation-free.
    /// </summary>
    internal bool TryFingertipDistance(Vector3 worldTip, out float distance)
    {
        distance = float.MaxValue;
        if (_box == null || !_box.enabled || !_box.gameObject.activeInHierarchy)
            return false;
        distance = Vector3.Distance(worldTip, _box.ClosestPoint(worldTip));
        return true;
    }

    public void OnPoke(VRHand hand)
    {
        if (!PokeSelectEnabled || !AllowsHand(hand))
            return;
        hand.SendHaptic(HapticPreset.ClickPulse);
        try
        {
            Poked?.Invoke(this, hand);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"VRCard.Poked subscriber threw: {ex}");
        }
    }

    // ------------------------------------------------------------------ update --

    private void Update()
    {
        UpdateCanvasCamera();
        _face.Maintain();
        // Bug #2: if Maintain YIELDED the face to a game dialog (it now belongs to the game
        // again), drop our per-instance material overrides so the game's face renders normally.
        // Re-applied on the next AttachGameCard when the face is re-adopted.
        if (_renderOnTop && NeedsFace)
            SetRenderOnTop(false);
        // Keep the game's world-space CardSmoke plume bounded to the card while a burn/
        // ghost effect runs (test #22, symptom 4c-i) and log the on-card burn lifecycle
        // (symptom 4c-ii). No-op when nothing is burning.
        _burnFx.Tick(FullCard, transform);

        if (IsHeld)
        {
            _flying = false; // a re-grab mid-flight wins — the hand owns the pose now
            TickHeldPose();
            return;
        }

        // Issue 5: fly-to-pile animation owns the transform while it runs (unscaled, ease-out on
        // world position + scale). On arrival the completion callback parks/hides the card.
        if (_flying)
        {
            float fdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap, like the fan anim
            _flyElapsed += fdt;
            float ft = _flyDuration > 0f ? Mathf.Clamp01(_flyElapsed / _flyDuration) : 1f;
            float e = 1f - (1f - ft) * (1f - ft); // ease-out
            transform.position = Vector3.Lerp(_flyFromPos, _flyToPos, e);
            transform.rotation = _flyFromRot;
            transform.localScale = Vector3.Lerp(_flyFromScale, _flyToScale, e);
            if (ft >= 1f)
            {
                _flying = false;
                Action? done = _flyDone;
                _flyDone = null;
                done?.Invoke();
            }
            return;
        }

        float dt = Time.deltaTime;
        // Release glide window: run the home-lerp on UNSCALED time for a beat after a
        // release so the glide-back plays even while the game pauses simulation time.
        // The exponential form below is frame-rate independent either way; take the
        // larger of the two dts so a running timeScale never slows the flight down.
        if (_releaseGlide > 0f)
        {
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap, like the fan anim
            _releaseGlide -= udt;
            dt = Mathf.Max(dt, udt);
        }
        float speed = CardsConfig.CardLerpSpeed.Value;
        // Pop from ANY hover source: grabber/poke-select (_popped), laser (_laserPopped),
        // or a light fingertip touch (_pokeHover, test #9). Issue A: the hand-driven
        // sources are gated by the driver's single-winner arbitration — while another
        // card is the elected hand-contact winner this card may not lift, so a sweeping
        // hand can never raise more than one card. The laser pop is never gated.
        // USER BUG B: ALL sources are additionally gated by the rooted predicate — a card
        // that cannot be grabbed in this phase makes no grab promise: zero pop, zero
        // scale change, no matter which hover flag a driver left set. Single choke point,
        // so a stale hover flag can never pulse a rooted card.
        bool rooted = IsRooted;
        if (rooted != _wasRooted)
        {
            _wasRooted = rooted;
            if (Time.unscaledTime >= s_nextRootedLogAt)
            {
                s_nextRootedLogAt = Time.unscaledTime + 1f;
                Core.VRLog.Info("Cards", $"Card '{name}' rooted={rooted} (Grabbable={Grabbable}, " +
                                          $"mode={Core.Events.VRModeStateMachine.CurrentMode}) — " +
                                          (rooted ? "hover pop/haptics OFF (grab refused this phase)."
                                                  : "hover pop/haptics back ON."));
            }
        }
        float popTarget = !rooted && (_laserPopped || (!_handPopSuppressed && (_popped || _pokeHover))) ? 1f : 0f;
        _pop = Mathf.MoveTowards(_pop, popTarget, dt * 8f);

        // Pop: toward the viewer (-Z of the card) and slightly up, plus scale-up. The
        // forward magnitude is [Cards] FanSelectedPopForward (G2) so it matches the fan
        // split; falls back to the 0.035 m default if the config is not yet bound.
        float popForward = CardsConfig.FanSelectedPopForward != null
            ? CardsConfig.FanSelectedPopForward.Value : 0.035f;
        Vector3 target = _homePos + _homeRot * new Vector3(0f, 0.012f * _pop, -popForward * _pop);
        float scale = _homeScale * (1f + 0.18f * _pop);

        if (_instantNext)
        {
            _instantNext = false;
            _releaseGlide = 0f; // an instant seed (fan open/close anim, pool) owns the pose now
            transform.localPosition = target;
            transform.localRotation = _homeRot;
            transform.localScale = Vector3.one * scale;
            return;
        }

        float t = 1f - Mathf.Exp(-speed * dt);
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, _homeRot, t);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scale, t);
    }

    protected override void OnDisable()
    {
        base.OnDisable(); // unregister grabbable + detach from hand if held
        VRInteractables.UnregisterPokeable(this);
        _burnFx.Detach(); // restore any bounded CardSmoke module state
        _popped = false;
        _laserPopped = false;
        _pokeHover = false;
        _handPopSuppressed = false; // arbitration flags never outlive a pooled/parked card
        _pop = 0f;
        _releaseGlide = 0f;
        CancelFly(); // a parked/pooled card is never mid-flight
    }

    private void OnDestroy()
    {
        _burnFx.Detach();
        DetachGameCard();
    }
}

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

    /// <summary>Per-hand grab/hover gate (see <see cref="InteractionBlockedHand"/>).</summary>
    public bool AllowsHand(VRHand hand) => !ReferenceEquals(hand, InteractionBlockedHand);

    private CardFace _face = new();
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
        canvasGo.AddComponent<GraphicRaycaster>();
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
        float faceW = facePixels.x * fit;
        float faceH = facePixels.y * fit;
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
            _box.size = _fullColliderSize;
            _box.center = Vector3.zero;
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
        return true;
    }

    /// <summary>Give the face back to the game (pool-safe). Idempotent.</summary>
    internal void DetachGameCard()
    {
        if (_face.IsAdopted)
            _face.Restore();
        GameCard = null;
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
    /// P6 fan-collider strip: while fanned, each card's grab collider shrinks to its
    /// VISIBLE (un-overlapped) strip so neighboring colliders never fight for the
    /// hover — the source of the constant haptic buzz in test #8. The full collider
    /// comes back via <see cref="ResetColliderRegion"/> (grab, tray, half layout).
    /// </summary>
    internal void SetColliderRegion(float width, float offsetX)
    {
        if (_box == null)
            return;
        _box.size = new Vector3(Mathf.Min(width, _fullColliderSize.x), _fullColliderSize.y, _fullColliderSize.z);
        _box.center = new Vector3(offsetX, 0f, 0f);
    }

    /// <summary>Restore the full-card grab collider (see <see cref="SetColliderRegion"/>).</summary>
    internal void ResetColliderRegion()
    {
        if (_box == null)
            return;
        _box.size = _fullColliderSize;
        _box.center = Vector3.zero;
    }

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
        ResetColliderRegion(); // full card again (fan strips, see SetColliderRegion)
        _laserPopped = false;
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
    /// Fly-in to the in-hand pose (P7): a short LOCAL-space lerp toward the captured
    /// held pose. Local space means the card follows the wrist 1:1 even while still
    /// converging; once converged it is simply parented — zero per-frame head math,
    /// no auto-facing, no allocations.
    /// </summary>
    private void TickHeldPose()
    {
        float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, _heldPos, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, _heldRot, t);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _heldScale, t);
    }

    public override void OnRelease(VRHand hand, Vector3 velocity)
    {
        base.OnRelease(hand, velocity); // restore pre-grab parent
        transform.localScale = Vector3.one * _homeScale;
        try
        {
            Released?.Invoke(this, hand, velocity);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"VRCard.Released subscriber threw: {ex}");
        }
    }

    public void OnGrabHighlight(VRHand hand, bool highlighted) => _popped = highlighted;

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
        if (PokeSelectEnabled && AllowsHand(hand))
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

        if (IsHeld)
        {
            TickHeldPose();
            return;
        }

        float dt = Time.deltaTime;
        float speed = CardsConfig.CardLerpSpeed.Value;
        float popTarget = _popped || _laserPopped ? 1f : 0f;
        _pop = Mathf.MoveTowards(_pop, popTarget, dt * 8f);

        // Pop: toward the viewer (-Z of the card) and slightly up, plus scale-up.
        Vector3 target = _homePos + _homeRot * new Vector3(0f, 0.012f * _pop, -0.035f * _pop);
        float scale = _homeScale * (1f + 0.18f * _pop);

        if (_instantNext)
        {
            _instantNext = false;
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
        _popped = false;
        _laserPopped = false;
        _pop = 0f;
    }

    private void OnDestroy() => DetachGameCard();
}

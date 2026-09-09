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
internal sealed class VRCard : GrabbableBehaviour, IGrabHighlight, IPokeable, IGrabbableHandFilter,
    IFanSweepTarget
{
    /// <summary>
    /// P7 (hardware test #10): the fan-owning (non-dominant, "gate") hand — set every frame by
    /// CardsDriver. Its palm carries the ability hand fan, so its own proximity hover kept
    /// flip-flopping highlights between two fan cards without any user input.
    ///
    /// SCOPE (user ruling 2026-08-04, the GENERAL rule): this is no longer a hand-wide veto.
    /// It refuses the gate hand ONLY on cards that do NOT <see cref="AllowsGateHand"/> — which
    /// after the 2026-08-04 ruling is exactly the ability fan's own cards (see that property).
    /// Every other card is hover/grab/highlightable with BOTH hands.
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

    /// <summary>
    /// WHICH hand the suppression above refuses in <see cref="AllowsHand"/> (null = the flag is
    /// clear). Used to be the single global <see cref="HandArbitrationHand"/>, which only worked
    /// while exactly one fan swept with exactly one hand. Both browse fans now sweep with BOTH
    /// hands (user ruling 2026-08-03), so two fans can hold contact winners for two different
    /// hands in the same frame and a global "the arbitration hand" cannot describe that: whichever
    /// fan wrote the static last decided the OTHER fan's grab gate. Per card + per hand is the
    /// same shape <see cref="ItemsPile.ItemChip"/> already uses.
    /// </summary>
    private VRHand? _handPopSuppressedFor;

    /// <summary>
    /// Second refused hand — filled when a SECOND suppressor names a different hand while the
    /// suppression is already up. Needed since the 2026-08-04 both-hands ruling: the dock/pick-field
    /// pool is now swept by TWO per-hand elections in the same tick (dominant + gate hand, see
    /// <c>CardsDriver.UpdateHandContactArbitration</c>), so one card can lose BOTH elections and
    /// must refuse BOTH hands — with a single slot the second write silently re-opened the first
    /// hand's grab gate on a card that hand had NOT elected.
    /// </summary>
    private VRHand? _handPopSuppressedFor2;

    /// <summary>Set by CardsDriver's hand-contact arbitration (see <see cref="_handPopSuppressed"/>);
    /// the refused hand defaults to that arbitration's <see cref="HandArbitrationHand"/>.</summary>
    internal void SetHandPopSuppressed(bool suppressed)
        => SetHandPopSuppressed(suppressed, HandArbitrationHand);

    /// <summary>
    /// Suppress this card's hand-driven pop and refuse <paramref name="forHand"/> in
    /// <see cref="AllowsHand"/> (see <see cref="_handPopSuppressedFor"/>). ACCUMULATIVE while
    /// suppressed: a second call naming a DIFFERENT hand adds it as a second refused hand
    /// (<see cref="_handPopSuppressedFor2"/>) instead of replacing the first — every suppressor
    /// re-derives its set from scratch each tick, so the pair can never go stale. Clearing
    /// (<paramref name="suppressed"/> = false) drops both.
    /// </summary>
    internal void SetHandPopSuppressed(bool suppressed, VRHand? forHand)
    {
        if (!suppressed)
        {
            _handPopSuppressed = false;
            _handPopSuppressedFor = null;
            _handPopSuppressedFor2 = null;
            return;
        }
        if (_handPopSuppressed && _handPopSuppressedFor != null
            && !ReferenceEquals(_handPopSuppressedFor, forHand))
            _handPopSuppressedFor2 = forHand;
        else
            _handPopSuppressedFor = forHand;
        _handPopSuppressed = true;
    }

    /// <summary>
    /// TRUE while BOTH hands may hover/grab this card — the exemption from the fan-owning-hand
    /// block (<see cref="InteractionBlockedHand"/>).
    ///
    /// GENERAL RULE (user ruling 2026-08-04: "Alle Karten sollen allgemein auch mit der
    /// nicht-dominanten Hand aufgenommen werden können … Das soll für alle Karten gelten — außer
    /// den Fächerkarten selber auf der linken Hand"): TRUE for EVERY card EXCEPT the ability hand
    /// fan's own cards. This replaces the per-zone whittling of the last builds (browse arc
    /// 2026-08-03, pick-field cards 2026-08-04), which kept re-discovering the same defect one
    /// zone at a time — the 2026-08-03 hardware log carries the refusal verbatim: <c>Left grab
    /// refused — nearest in-reach grabbable 'VRCard_ABILITY_CARD_ProvokingRoar' refuses this hand
    /// (AllowsHand)</c>, three times, on a card the off hand should have taken.
    ///
    /// WHY the fan's own cards stay dominant-hand-only: the block's single root cause
    /// (<see cref="InteractionBlockedHand"/>) is that the fan hangs off the gate hand's own palm —
    /// that hand's proximity hover sits permanently inside the fan and flip-flops highlights with
    /// no user input, and grabbing a card out of the fan with the very hand that carries the fan
    /// is geometric nonsense. Stamped at three seams so it can never go stale: per Rebuild
    /// (<c>!inFan</c>, the authoritative re-assert), on fan entry (<see cref="CardFan.Add"/> /
    /// <see cref="CardFan.SetCards"/> — a void-release lands a card in the fan BETWEEN rebuilds),
    /// and on EVERY grab (CardsDriver.OnCardGrabbed, the single point where any card becomes
    /// held — a held card is by definition not a fan card and must be hand-to-hand transferable
    /// to the gate hand at once; a stale FALSE here would also make
    /// ProximityGrabber.HealDeadHeld force-drop it out of the gate hand mid-hold).
    /// </summary>
    internal bool AllowsGateHand { get; set; }

    /// <summary>
    /// Which pile stack this card visual is currently ON LOAN from (null = not a pile loan).
    ///
    /// ROOT CAUSE (user report 2026-08-04, "abgeworfene Karte im Handfaecher"): the release
    /// routing's only notion of "this is a discard/burnt card" used to be LIVE MEMBERSHIP in the
    /// open browse arc (<c>CardsDriver.OnCardReleased</c>: <c>_browser.IsOpen &amp;&amp;
    /// _browser.Contains(card)</c>). That membership is destroyed while the card is still in the
    /// player's hand: <c>PileBrowser.Close()</c> clears its card list, and the browser closes for
    /// a dozen reasons that do not end the hold (foreign interaction / click-away, grip release,
    /// context change, board rebuild, modal dialog). Hardware log (Player.log 3948-4028): a
    /// DISCARD card is plucked from the browse arc, <c>Pile browse CLOSE (foreign interaction:
    /// click-away ...)</c> fires mid-hold with ledger <c>borrowed 1 ... returned 0</c> (the
    /// collapse skips held cards), the card survives four hand-to-hand transfers, and its final
    /// <c>Drop (Right): ... rule=none -&gt; return to fan</c> routes it into the HAND fan
    /// (fan n=4 -&gt; n=5) - a discarded card mixed into the hand, which also inflated the
    /// hand-card count peers receive (NetAvatarDriver reads CardFan.Count).
    ///
    /// THE MARKER: stamped at the single point where the browse arc borrows a pile card
    /// (<c>CardsDriver.UpdateBrowser</c>), it lives ON the card so it survives every path a hold
    /// can take - grab, T2 rescue, hand-to-hand transfer, transfer abort/re-adopt, and a browser
    /// that closes underneath the hold. Retired at exactly two game-truth points: the Rebuild
    /// zone loop (a non-held card the browser no longer lists has been re-homed or parked by
    /// authoritative game state - including the pick-mode exception flows whose fan legitimately
    /// IS the discard set) and <see cref="OnDisable"/> (pool hygiene: a parked/pooled card never
    /// carries a stale loan into its next life). While set, <c>CardsDriver.OnCardReleased</c>
    /// routes the card back to its pile (browse arc if open, else a fly into the stack) and
    /// NEVER into the hand fan.
    /// </summary>
    internal PileKind? PileOrigin { get; set; }

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
    /// The fan-owning-hand veto is skipped entirely for a card that
    /// <see cref="AllowsGateHand"/> — since the 2026-08-04 ruling that is every card except
    /// the ability fan's own (see that property for the general rule and its one exception).
    /// </summary>
    public bool AllowsHand(VRHand hand) =>
        (AllowsGateHand || !ReferenceEquals(hand, InteractionBlockedHand))
        && !(_handPopSuppressed && (ReferenceEquals(hand, _handPopSuppressedFor)
                                    || ReferenceEquals(hand, _handPopSuppressedFor2)))
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

    // Render-on-top (Bug #2) — RETAINED BUT INACTIVE. Read SetRenderOnTop's revert note before
    // touching any of this: the bump below is NEVER applied. SetRenderOnTop's whole body is
    // `_ = on; RestoreRenderOnTop();`, so _renderOnTop can never become true, ApplyRenderOnTop has
    // no caller, RestoreRenderOnTop's guard always returns, and every field in this block stays
    // null. It is kept deliberately, exactly like Board/FigureGrab/FigureGrabbable's analogous
    // block: it is the record of a tested-and-REJECTED approach, and the queue ordering it
    // documents (4200 > 4100 > 4003) is still the design rationale for PlayTray's and
    // ButtonCluster's widget queues. Do not delete it, and do not re-enable it.
    //
    // What it did, and why the description below is written in the present tense: a card shown in
    // FRONT of the opaque control board was painted
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
    /// INSPECTION GRAB (user ruling 2026-08-08: "Ich möchte das man jederzeit auch eine Karte aus
    /// der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die Karte nirgendwo
    /// ablegen kann. Das soll also niemals blockiert sein"). True while this card may be PICKED UP
    /// AND READ but may NOT be COMMITTED — played into a board slot, selected, discarded, or any
    /// other call that writes game state.
    ///
    /// <para>THE SPLIT THIS ENCODES. Before this flag the mod had exactly one verdict per card,
    /// <see cref="Grabbable"/>, and it meant BOTH "you may hold this" and "you may place this".
    /// Every phase in which a hand card cannot be PLAYED therefore also refused to let the player
    /// LOOK at it (<c>CardsDriver.Rebuild</c>'s <c>grabbable</c> is <c>CardsGameApi.IsSelectionPhase</c>
    /// in <c>CardsSelection</c> and plain <c>false</c> in every other mode). Inspection and play are
    /// now two different questions: <see cref="Grabbable"/> answers "may I hold it", this answers
    /// "and is holding it ALL I may do".</para>
    ///
    /// <para>WHO WRITES IT: the zone stamp in <c>CardsDriver.Rebuild</c> (the single per-card
    /// funnel) plus the two between-rebuild fan seams (<c>CardFan.SetCards</c> / <c>CardFan.Add</c>),
    /// exactly like <see cref="Grabbable"/> and <see cref="AllowsGateHand"/> — so a card that
    /// changes zone between frames can never carry a stale verdict.</para>
    ///
    /// <para>WHO READS IT: <c>CardsDriver.OnCardReleased</c>, which returns an inspect-only card
    /// HOME to the fan before any game seam is reachable. Deliberately a property of the CARD and
    /// not of the fan: the release routing runs on the card long after the fan may have changed,
    /// and a mid-hold rebuild never re-stamps a HELD card (the zone loop skips them), so the verdict
    /// the player saw when they grabbed it is the verdict that decides their release.</para>
    /// </summary>
    internal bool InspectOnly { get; set; }

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
        LogBackingSource(backingPrefab != null);
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
        // The BEST KNOWN face size, not a frozen guess. This used to be a hard-coded 270x400 while
        // the real ability face is 294x450, so a card built before its face was adopted wore a body
        // 1.8 mm too wide; AttachGameCard then re-fit it and the width visibly stepped.
        // CardFace.ObservedFacePixels IS 270x400 until this client has hosted an ability face, so
        // nothing changes before the first card and everything agrees after it.
        // (This note used to carry a second case — the size a BORROWED remote copy was stuck with,
        // since that copy never adopted a game widget. Cards/CardBorrow.cs was deleted on
        // 2026-09-07 when reaching into a teammate's fan stopped handing anything out, so there is
        // no such copy any more and the sentence above stands on the adopted case alone.)
        SetCanvasSize(CardFace.ObservedFacePixels, w, h);
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
    /// longer leaves a dark rectangular ring (test #25).
    ///
    /// <para>NO LONGER A HAND-COPIED 0.94. It used to be a local const with a comment asking the
    /// next reader to keep it in sync with CardFace.BorderFraction by hand. That instruction was
    /// followed here and missed one module over — Net/RemoteHandFan sized its ghost slabs to the
    /// FULL nominal card and let the cloned face letterbox inside, which is user report 12 of
    /// 2026-08-15 (the peer's card back showing as a rim around the print). It now reads the one
    /// definition, and the fit itself is <see cref="CardFace.VisibleFaceRect"/>.</para>
    /// </summary>
    private static float VisibleFaceFraction => CardFace.VisibleFaceFraction;

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

    /// <summary>
    /// Which body an ability card is actually wearing, once per session.
    ///
    /// WHY THIS LINE EXISTS (2026-08-11 round 2, "Der schwarze Rand soll im gesamten Spiel entfernt
    /// werden egal wo die Karte ist"). The card-art alpha clip lives on the SHARED
    /// <c>CardBodyKind.Ability</c> material pair, which only <see cref="BuildProceduralBacking"/>
    /// hands out. When the bundle ships <c>Assets/Bundle/Table/CardBacking.prefab</c>, the branch
    /// above takes an authored prefab with its OWN materials instead, and the clip — plus every
    /// silhouette line in the log claiming success — reaches nothing the player can see. That
    /// branch was chosen SILENTLY, and <c>VRCardFactory.LoadPrefab</c> logs nothing either way, so a
    /// hardware log could not distinguish "the clip works" from "the clip is applied to a material
    /// nobody draws". <c>ItemsPile</c> has printed its own equivalent ("ITEM CARD backing source")
    /// since the item chips were built; the ability cards never did.
    ///
    /// Today's evidence says the procedural branch is the live one — the user watched the border go
    /// TRANSPARENT for a moment during a character switch on the ModBuild-108 build, which only a
    /// clipped body can do — but that is an inference from a symptom, and this makes it a fact in
    /// the next log.
    /// </summary>
    private static bool s_backingSourceLogged;

    private static void LogBackingSource(bool fromBundle)
    {
        if (s_backingSourceLogged)
            return;
        s_backingSourceLogged = true;
        Core.VRLog.Info("Cards", "ABILITY CARD backing source: " + (fromBundle
            ? "bundle 'Assets/Bundle/Table/CardBacking.prefab' — NOTE: an authored prefab carries its " +
              "OWN mesh and materials, so neither the shared CardBodyKind.Ability pair nor the round-17 " +
              "punched-out contour mesh (CardMesh.AttachBody) reaches this body. Every 'CARD " +
              "SILHOUETTE (Ability): APPLIED' line above is then true and invisible."
            : "procedural CardMesh body on the SHARED CardBodyKind.Ability material pair — this is the " +
              "body the round-17 punched-out contour mesh replaces (CardMesh.AttachBody), so an APPLIED " +
              "line above is a change the player can see."));
    }

    private static Transform BuildProceduralBacking(Transform parent, float w, float h)
    {
        // P7 (test #10): a REAL 3D card body with ~1.5 mm thickness, dark rim, opaque decorative
        // back (see CardMesh). ROUND 17 (2026-08-11 ruling: "das mesh der Karte auf das outline
        // der Kartenoberfläche 'auszustanzen'"): AttachBody serves the PUNCHED-OUT contour mesh
        // once the Ability footprint is known (warm cache: before this ever runs) and the rounded
        // slab until then, upgrading in place when a footprint lands. The live face canvas at
        // z = -0.0012 covers the front almost edge-to-edge; only the thin rim shows.
        var backing = new GameObject("Backing");
        backing.transform.SetParent(parent, worldPositionStays: false);
        CardMesh.AttachBody(backing.AddComponent<MeshFilter>(), CardBodyKind.Ability, w, h);
        var renderer = backing.AddComponent<MeshRenderer>();
        // ABILITY-kind materials (2026-08-11 "keinen schwarzen Rand"): the shared pair that
        // CardFace's silhouette capture alpha-clips to the ability card's own outline. Item
        // chips take the Item pair; everyone else keeps the never-clipped Neutral pair — see
        // CardBodyKind. Assigning the SHARED material (never an instance) is what lets a
        // silhouette captured seconds later re-shape this card without rebuilding it.
        renderer.sharedMaterials = new[]
        {
            CardMesh.CreateEdgeMaterial(CardBodyKind.Ability),
            CardMesh.CreateBackMaterial(CardBodyKind.Ability),
        };
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // THE DEPTH STAMP, MEASURED RATHER THAN ASSERTED. This is the first moment a real hand card's
        // body has both its mesh and its materials, so it is the first moment the claim two other
        // features rest on — "a card's backing slab is depth-writing AlphaTest geometry in the opaque
        // tier", which is how the ghost hand (Hands/HandGhost) and the wrist HUD stay hidden behind a
        // card — can be read off the object instead of restated. Change-gated inside, so this costs
        // one string compare per card after the first.
        CardMesh.ReportBodyDepthStamp(backing.GetComponent<MeshFilter>(), "local card backing built");
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
        ForgetActionHighlight();
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
        _dustTone = image.color; // crumble/materialize dust takes this placeholder's tone

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

    /// <summary>
    /// RETAINED BUT INACTIVE — no callers, and no way to acquire one without reverting the revert
    /// documented at <see cref="SetRenderOnTop"/>. Kept as the record of the rejected approach.
    /// </summary>
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

    /// <summary>
    /// The only half of the render-on-top machinery that still RUNS (from <see cref="SetRenderOnTop"/>),
    /// and it returns immediately in practice: nothing ever applies the bump, so there is never
    /// anything to restore. Kept so the no-op-forward at the five call sites stays honest.
    /// </summary>
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

    /// <summary>
    /// Laser hover (P6, Demeo pluck): separate flag so the dominant hand's ray and the
    /// proximity highlight never stomp each other — the card pops while EITHER is set.
    /// </summary>
    internal void SetLaserHover(bool hovered)
    {
        if (hovered && !_laserPopped)
            LogLaserRectDecoupled(); // one throttled diagnostic on hover-start (see below)
        _laserPopped = hovered;
    }

    private bool _laserPopped;

    /// <summary>
    /// True while this card is visually SINGLED OUT — lifted toward the viewer, enlarged, and (in a
    /// hand fan) pushing its neighbours apart. This is the EXACT predicate the pop animation runs
    /// on (see the <c>popTarget</c> line in the home-pose update, which reads it), promoted to a
    /// property for one reason: the multiplayer layer has to broadcast WHICH card a player is
    /// singling out, and a second, separately-derived "is it highlighted" test would be free to
    /// drift from the one the local pop actually obeys.
    ///
    /// It deliberately covers EVERY hover source — laser ray, grabber/poke select, and the light
    /// fingertip touch — and inherits both of the local gates: a card the hand-contact arbitration
    /// suppressed does not count (only one card lifts per sweeping hand), and neither does a
    /// ROOTED card (one that cannot be grabbed this phase makes no grab promise and never pops).
    /// </summary>
    internal bool IsHighlighted =>
        !IsRooted && (_laserPopped || (!_handPopSuppressed && (_popped || _pokeHover)));

    /// <summary>
    /// BURN ANIM: the card's SEATED (home) pose in world space — where the layout put it, which is
    /// where it will be sitting once any fly-in/settle finishes. Needed because a card can be asked
    /// for its "true position" during the very frame a <see cref="FlyFromPile"/> is seeding the
    /// flight start at the PILE: the live transform then reads the pile, while the pose that matters
    /// for a later burn animation is the seat it is heading to. Read-only; false for a parentless
    /// card (nothing meaningful to report — the live transform is returned instead).
    /// </summary>
    internal bool TryGetHomeWorldPose(out Vector3 pos, out Quaternion rot)
    {
        Transform? parent = transform.parent;
        if (parent == null)
        {
            pos = transform.position;
            rot = transform.rotation;
            return false;
        }
        pos = parent.TransformPoint(_homePos);
        rot = parent.rotation * _homeRot;
        return true;
    }

    /// <summary>
    /// LASER hit geometry that DELIBERATELY EXCLUDES the hover RAISE (pop): the card's
    /// RESTING world-space rectangle — center, plane normal, unit right/up axes and
    /// world-meter half-extents — computed from the HOME pose
    /// (<see cref="_homePos"/>/<see cref="_homeRot"/>/<see cref="_homeScale"/> under the
    /// current parent), NOT the live transform.
    ///
    /// ROOT-CAUSE FIX (hardware: the laser "sticks" ABOVE a raised fan card — the board
    /// button beside it becomes unreachable and the card is grabbed by mistake). The fan
    /// laser pick (<see cref="CardFan.TryRaycast"/>) built its per-card plane from the LIVE
    /// transform. A laser-hovered card immediately lifts toward the viewer (−Z) and up (+Y)
    /// — <see cref="Update"/>'s pop — so the hit PLANE moved INTO the beam the instant the
    /// card popped, feeding the raise straight back into the hit test: sweeping the beam up
    /// the card raised its plane up to meet the beam, and sweeping off sideways left the
    /// hover latched at the raised height (a stale hit floating above the resting card,
    /// which is exactly the "invisible collider above the card" the tester felt). Because
    /// the stuck fan hover gates the board laser off (CardsDriver.UpdateBoardLaser returns
    /// while <c>_laserHover != null</c>) and arms the laser pluck, the adjacent button dies
    /// and the trigger grabs the card. Testing the RESTING rect breaks the loop — the pop is
    /// now purely visual; the beam hits the card where it sits at rest, so leaving the rect
    /// drops the hover at once. Returns false only for a parentless/degenerate card (the
    /// caller then simply skips it — a fanned card always has the fan root as parent).
    /// </summary>
    internal bool TryGetRestingLaserRect(out Vector3 center, out Vector3 normal,
        out Vector3 right, out Vector3 up, out float halfWidth, out float halfHeight)
    {
        center = default;
        normal = default;
        right = default;
        up = default;
        halfWidth = 0f;
        halfHeight = 0f;
        Transform? parent = transform.parent;
        if (parent == null)
            return false;
        center = parent.TransformPoint(_homePos);
        Quaternion rot = parent.rotation * _homeRot;
        right = rot * Vector3.right;
        up = rot * Vector3.up;
        normal = rot * Vector3.forward; // card +Z, away from the viewer (uGUI reads from −Z)
        float lossy = parent.lossyScale.x * _homeScale; // resting world scale (pop scale excluded)
        halfWidth = CardsConfig.CardWidth.Value * 0.5f * lossy;
        halfHeight = CardsConfig.CardHeight * 0.5f * lossy;
        return true;
    }

    /// <summary>
    /// The card's rect WHERE IT IS RIGHT NOW (live transform, pop included), world meters —
    /// the twin of <see cref="TryGetRestingLaserRect"/>, for callers whose raise is NOT
    /// laser-driven.
    ///
    /// The resting rect exists to break ONE feedback loop: laser hover raises the card, the
    /// raise moves the plane into the beam, the beam re-hits it. That loop only exists where
    /// the BEAM is what pops the card. Where the pop comes from somewhere else — the palm
    /// proximity highlight lifting a docked card — there is no loop, and testing the resting
    /// rect is simply wrong: the card is visibly lifted, so the player aims at the LIFTED card
    /// and the resting rect sits behind/below it. At a flat angle that offset projects far
    /// along the view direction, so the ray misses the rect entirely while the reticle looks
    /// dead centre on the card — the "laser goes straight through the card at a flat angle"
    /// report. Callers pop-driven by the hand use this; laser-driven picks keep the resting rect.
    /// </summary>
    internal bool TryGetLiveLaserRect(out Vector3 center, out Vector3 normal,
        out Vector3 right, out Vector3 up, out float halfWidth, out float halfHeight)
    {
        Transform t = transform;
        center = t.position;
        right = t.right;
        up = t.up;
        normal = t.forward; // card +Z, away from the viewer (uGUI reads from −Z)
        float lossy = t.lossyScale.x; // live world scale — the pop's grow is part of the target
        halfWidth = CardsConfig.CardWidth.Value * 0.5f * lossy;
        halfHeight = CardsConfig.CardHeight * 0.5f * lossy;
        return halfWidth > 1e-5f && halfHeight > 1e-5f;
    }

    /// <summary>Throttled diagnostic (shared across all cards): on laser hover-START, log the
    /// RESTING hit center the pick now uses vs the LIVE (about-to-pop) transform, so a
    /// hardware log verifies the laser rect no longer tracks the raise.</summary>
    private static float s_nextLaserRectLogAt;

    private void LogLaserRectDecoupled()
    {
        if (Time.unscaledTime < s_nextLaserRectLogAt)
            return;
        s_nextLaserRectLogAt = Time.unscaledTime + 1f;
        if (!TryGetRestingLaserRect(out Vector3 center, out _, out _, out _, out float hw, out float hh))
            return;
        Vector3 live = transform.position;
        Core.VRLog.Debug("Cards", $"Laser hover '{name}': hit rect = RESTING center " +
            $"({center.x:F3},{center.y:F3},{center.z:F3}) half({hw:F3},{hh:F3}) m; live transform " +
            $"({live.x:F3},{live.y:F3},{live.z:F3}) pop={_pop:F2} — laser rect decoupled from the " +
            "raise (hits the card at rest, never the popped pose).");
    }

    /// <summary>Throttle clock (shared across cards) for the fan collider-fit diagnostic.</summary>
    private static float s_nextFanColliderLogAt;

    /// <summary>
    /// P6 fan-collider strip: while fanned, each card's grab collider shrinks to its
    /// VISIBLE (un-overlapped) strip so neighboring colliders never fight for the
    /// hover — the source of the constant haptic buzz in test #8. The full collider
    /// comes back via <see cref="ResetColliderRegion"/> (grab, tray, half layout).
    ///
    /// STEEP-ANGLE LASER FIX (build on 306e8ea — the laser STILL "sticks" above a raised
    /// fan card from a top-down angle, board button beside it unreachable, card grabbed by
    /// mistake). The earlier fix corrected only the GEOMETRIC fan pick (resting rect); the
    /// PHYSICS BoxCollider still carried a 3 cm accept apron biased entirely onto the VIEWER
    /// side (-Z). Fan cards billboard toward the head, so -Z points at the viewer's EYE — and
    /// from a steep look-down that eye is ABOVE the card, so the apron protruded 3 cm (≈ half a
    /// card width) of INVISIBLE collider volume ABOVE/IN FRONT of the card's upper half. That
    /// volume is what caught the beam (physics-ray hit / ClosestPoint proximity highlight →
    /// the fan lift-priority rescue in CardsDriver.UpdateFanLaser clamps the beam and grabs),
    /// floating the reticle over the card and eating the trigger meant for the adjacent board
    /// button — exactly the reported "something invisible hovering above the upper half".
    ///
    /// Fix: the fan strip now fits the collider EXACTLY to the visible card face (thickness =
    /// <see cref="_fullColliderSize"/>.z, centred, NO viewer-side apron). The collider tracks
    /// the visible rect with no upper/front overhang, so a hit can only land on the card face
    /// and it clears the instant the beam leaves that face. Hand proximity grab is unaffected:
    /// <see cref="ProximityGrabber"/> reaches 0.13 m off the collider (≈ 2× the 6.35 cm card
    /// width, same fan/diorama scale as this box), so dropping a 3 cm accept pad leaves ample
    /// reach, and the fan lift-priority rescue still catches a near-miss grab. Docked-card
    /// tray-edge grab (<see cref="DockPadDepth"/> / <see cref="ResetColliderRegion"/>), poke,
    /// and held-card behaviour are untouched — this apron was fan-only.
    /// </summary>
    internal void SetColliderRegion(float width, float offsetX)
    {
        if (_box == null)
            return;
        var size = new Vector3(Mathf.Min(width, _fullColliderSize.x), _fullColliderSize.y,
            _fullColliderSize.z);                   // exact-fit depth — no viewer-side apron (steep-angle laser fix)
        var center = new Vector3(offsetX, 0f, 0f);  // centred on the card plane, no -Z overhang
        // IDEMPOTENCE GUARD: the fan re-lays out as the player's gaze moves (CardFan's card
        // presentation), so this is now a per-frame caller. Assigning an unchanged BoxCollider
        // size/centre still dirties the physics shape every frame for every fanned card, so no-op
        // when nothing actually changed. The dock-pad flag still gets cleared on the first fan
        // call after a dock (that path always differs, and the !_dockGrabPad term guarantees it).
        if (!_dockGrabPad && _box.size == size && _box.center == center)
            return;
        _dockGrabPad = false; // fan strips own the collider shape — a fanned card is never slot-docked
        _box.size = size;
        _box.center = center;
        LogFanColliderFit();
    }

    /// <summary>
    /// Diagnostic (throttled, shared): prove the fan-seated collider now matches the VISIBLE
    /// card face — logs the BoxCollider world centre/size against the resting laser rect
    /// (world meters). A hardware log line with the box centred on the rect centre and its
    /// depth ≈ the card thickness (no −Z overhang) verifies the invisible above-card volume
    /// is gone. No-op for a card without a valid resting rect (parentless/degenerate).
    /// </summary>
    private void LogFanColliderFit()
    {
        if (_box == null || Time.unscaledTime < s_nextFanColliderLogAt)
            return;
        if (!TryGetRestingLaserRect(out Vector3 rectCenter, out _, out _, out _,
                out float halfW, out float halfH))
            return;
        s_nextFanColliderLogAt = Time.unscaledTime + 2f;
        Vector3 boxCenter = transform.TransformPoint(_box.center);
        Vector3 lossy = transform.lossyScale;
        Vector3 boxWorldSize = new(_box.size.x * lossy.x, _box.size.y * lossy.y, _box.size.z * lossy.z);
        Core.VRLog.Debug("Cards", $"Fan collider fit '{name}': box world center " +
            $"({boxCenter.x:F3},{boxCenter.y:F3},{boxCenter.z:F3}) size " +
            $"({boxWorldSize.x:F3},{boxWorldSize.y:F3},{boxWorldSize.z:F3}) m vs visible rect center " +
            $"({rectCenter.x:F3},{rectCenter.y:F3},{rectCenter.z:F3}) half({halfW:F3},{halfH:F3}) — " +
            "collider fits the card face, no viewer-side (-Z) apron above the upper half.");
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
    /// between the recesses, no matter how the pad fractions or SlotOverlayScale are tuned.
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

    /// <summary>Last highlight state pushed per half (index 0 = bottom, 1 = top) and which REGION
    /// it lit — the memory <see cref="ActionHighlightDriver"/> gates on. Held on the card because a
    /// VR card outlives every rebuild that re-asserts it, which is precisely what the gate needs to
    /// know.</summary>
    private readonly int[] _actionHighlight = { ActionHighlightDriver.Off, ActionHighlightDriver.Off };

    /// <summary>Companion to <see cref="_actionHighlight"/>: which of the half's two regions was
    /// lit. Always false on this path — the active column lights the BIG action region, never the
    /// standard-action chip — but the gate takes both terms and must not be given a half of one.</summary>
    private readonly bool[] _actionHighlightRegion = { false, false };
    private bool _activeHighlightTop;
    private bool _activeHighlightBottom;

    /// <summary>Pool/rebind ownership ends the old active-column request; a temporary
    /// parent disable alone does not, because that is precisely the pulse-recovery case.</summary>
    internal void ForgetActionHighlight()
    {
        _activeHighlightTop = _activeHighlightBottom = false;
        _actionHighlight[0] = _actionHighlight[1] = ActionHighlightDriver.Off;
        _actionHighlightRegion[0] = _actionHighlightRegion[1] = false;
    }

    /// <summary>
    /// Light this card's ACTIVE half/halves with the game's own action-region highlight, WITHOUT
    /// restarting a pulse that is already running (user item 7, 2026-09-07 — "Die Frequenz … war
    /// plötzlich viel höher als zuvor. Es soll mit der dauerhaft selben ruhigen Frequenz blinken").
    ///
    /// <para>The whole of the fix is that this is gated. <c>CardActionHighlight.ShowHover</c> is not
    /// idempotent — it cancels the running LeanTween chain and re-seeds the alpha to 1 — so the
    /// caller's cadence became the blink's frequency. See <see cref="ActionHighlightDriver"/> for
    /// the mechanism, the authored period, and the census that now prints it in seconds.</para>
    /// </summary>
    internal void SetActionHighlight(bool top, bool bottom)
    {
        _activeHighlightTop = top;
        _activeHighlightBottom = bottom;
        FullAbilityCard? full = FullCard;
        if (full == null)
            return;
        ActionHighlightDriver.Assert(full.bottomActionButton,
            bottom ? ActionHighlightDriver.Hover : ActionHighlightDriver.Off, wantDefault: false,
            ref _actionHighlight[0], ref _actionHighlightRegion[0],
            ActionHighlightDriver.Site.ActiveColumn);
        ActionHighlightDriver.Assert(full.topActionButton,
            top ? ActionHighlightDriver.Hover : ActionHighlightDriver.Off, wantDefault: false,
            ref _actionHighlight[1], ref _actionHighlightRegion[1],
            ActionHighlightDriver.Site.ActiveColumn);
    }

    // -------------------------------------------------------------- interaction --

    // Held-pose target in GrabAnchor local space (captured once per grab).
    private Vector3 _heldPos;
    private float _heldScale = 1f;

    /// <summary>
    /// Fraction of the card height between the bottom edge and the pinch anchor:
    /// the fingers grip the card's lower-edge area ~12 % up from the bottom, like a
    /// real card pinched at its corner.
    /// </summary>
    internal const float PinchGripFraction = 0.12f;

    // ---- THE HOVER POP — ONE DEFINITION, FIVE DRAWERS -----------------------------------------
    //
    // THE LIFT A HIGHLIGHTED CARD TAKES is the ability card's, and four other places draw it: the
    // owner's item chip (ItemsPile.ItemChip) and the three mirrors that reproduce a peer's fans
    // (Net.RemoteHandFan, Net.RemoteBrowserFan, Net.RemoteItemFan). Until 2026-09-05 the ORIGINAL
    // was three INLINE literals in the method below while all four copies were named constants
    // citing it in a comment — the arrangement scripts/check-mirrors.sh cannot see at all, because
    // its extractor reads named constants and an inline literal is not one. Retuning the card the
    // player is actually looking at was therefore a change no checker could pair with the four
    // fans that were supposed to follow it.
    //
    // THE FORWARD COMPONENT IS DELIBERATELY NOT HERE: it is the authored dial
    // [Cards] FanSelectedPopForward, which already rides the wire (record 28, id 75), so a peer's
    // mirrored lift comes toward the viewer by the OWNER's own number. These three have never had
    // a dial on either side and minting one for them is the parallel set this removes, not adds.

    /// <summary>The pop's UPWARD component in card-local metres — the small rise that goes with the
    /// forward lift.</summary>
    internal const float PopUp = 0.012f;

    /// <summary>The pop's EXTRA SIZE as a fraction (+18 %). Sites that want the MULTIPLIER write
    /// <c>1f + PopScale</c> rather than a second literal.</summary>
    internal const float PopScale = 0.18f;

    /// <summary>The pop RAMP rate, units per second on a <c>MoveTowards</c> — how fast the lift
    /// grows and relaxes. The mirrors run it on their own clock, so the wire never carries an
    /// animation, only the highlighted index.</summary>
    internal const float PopRate = 8f;

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
    /// wrist (−Z): in a relaxed grip (grip pose ~60° pitched, the per-style [Hands]
    /// seat-pitch frame) the fingers point forward/slightly down, so
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

        bool left = hand.Side == HandSide.Left;
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
        // MIRRORED fine-tune (user report 2026-08-04: "Die Kartenposition der linken Hand ist
        // falsch ... exakt wie die rechte ... nur eben gespiegelt"). ROOT CAUSE: the tuned
        // [Cards] HeldPinchOffset (authored on the RIGHT hand, default X = -5.5 cm) was added
        // RAW on both hands — but the GrabAnchor frames are anatomical mirrors: +Y out of the
        // palm and +Z along the fingers on BOTH hands, so the lateral ±X axis necessarily
        // points to the THUMB side on the right hand and the PINKY side on the left. A raw X
        // therefore shifted the card toward the thumb on one hand and toward the pinky on the
        // other — the left card missed the thumb/index pinch spot by twice the tuned lateral
        // offset. Flip ONLY the X term for the left hand (Y/Z are anatomically symmetric).
        //
        // THE SIGN IS THE PROJECT'S ONE DEFINITION OF IT — Board.FigureGrab.HeldPoseMirror.
        // OffsetSign, which the figure and prop grabs already read, rather than a fourth
        // hand-spelled `if (left) x = -x`. That rule has been broken three times in this
        // codebase (CardGripPose's header records two of them and the 2026-08-09 item-card
        // report is the third), and every one of them was a copy of the ternary rather than a
        // call into it.
        float thumbSide = Board.FigureGrab.HeldPoseMirror.OffsetSign(left);
        Vector3 pinchOffset = CardsConfig.HeldPinchOffset.Value;
        pinchOffset.x *= thumbSide;
        pinchLocal += pinchOffset;

        // ONE SOLVER FOR BOTH CARD KINDS — see CardGripPose.ReadingPose. The item card's copy of
        // these terms is what made the left-hand mirror a five-day defect on one card kind and not
        // the other; only the card HEIGHT legitimately differs, and that is the parameter.
        CardGripPose.ReadingPose(CardsConfig.HeldFaceBias.Value, thumbSide, pinchLocal,
                                 cardH, PinchGripFraction, out Vector3 pos, out Quaternion rot);
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
        _heldScale = transform.localScale.x;
        transform.position = worldPos;
        transform.rotation = worldRot;
        transform.localScale = worldScale;
        _dockGrabPad = false;  // in-hand: exact-card collider again (the apron is a docked-only affordance)
        ResetColliderRegion(); // full card again (fan strips, see SetColliderRegion)
        _laserPopped = false;
        SetHandPopSuppressed(false); // a held card is out of the contact arbitration pool
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
    /// Fly-in to the in-hand pose. TWO MODES, and which one is live is
    /// <see cref="HeldCardGrip"/>'s answer, not this method's:
    ///
    /// <para>READING (the default, unchanged): the card sits at the pinch point (position lerped
    /// in GrabAnchor-local space, so it tracks the wrist 1:1) but per-frame BILLBOARDS
    /// its face to the head — same convention as CardFan.Tick — so a grabbed card is
    /// readable without twisting the wrist. The initial grab snap (_heldRot) still
    /// orients the card the instant it is picked up; this override then eases the face
    /// toward the viewer.</para>
    ///
    /// <para>IN-HAND (grip held, user 2026-08-29): the card is RIGID in the fist. Both position
    /// and rotation ease toward <see cref="HeldCardGrip.TryPose"/>'s answer in grab-anchor-local
    /// space, so the card turns with the wrist and can be aimed at another player. The pose is
    /// re-solved every frame rather than reused from the grab edge — the modelled grip curls are
    /// still easing in, and the pinch point is sampled off the fingertips they move.</para>
    ///
    /// <para>The mode can flip mid-hold (that is the whole gesture), so nothing here latches: the
    /// same exponential lerp carries the card between the two poses, which is why a switch reads
    /// as the card turning in the hand instead of snapping.</para>
    ///
    /// Scale lerps identically in both.
    /// </summary>
    private void TickHeldPose()
    {
        float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
        float cardW = CardsConfig.CardWidth.Value * _heldScale;
        float cardH = CardsConfig.CardHeight * _heldScale;
        // THE GRASP, blended — the card travels between the two poses on the SAME eased progress
        // the fingers travel on, so it arrives in the hand exactly as the hand closes on it. At
        // blend 0 this branch does not run at all and the original billboard path below is
        // byte-for-byte what it always was.
        //
        // WORKED IN THE PARENT'S FRAME (the hand's grab socket), which is why the billboard target
        // is converted INTO it rather than the grip target out of it: the billboard is a world
        // rotation and the grip pose is hand-local, and interpolating a hand-local pair while the
        // wrist moves is the thing that tracks the wrist. Converting the other way would blend two
        // world poses and the card would lag the hand for the length of the animation.
        float grasp = HeldCardGrip.Blend(Holder);
        Transform? socket = transform.parent;
        if (grasp > 0f && socket != null
            && HeldCardGrip.TryPose(Holder, cardW, cardH, out Vector3 gripPos, out Quaternion gripRot))
        {
            Vector3 wantPos = gripPos;
            Quaternion wantRot = gripRot;
            if (grasp < 1f)
            {
                Quaternion readRot = LocalBillboard(socket);
                wantPos = Vector3.Lerp(_heldPos, gripPos, grasp);
                wantRot = Quaternion.Slerp(readRot, gripRot, grasp);
            }
            transform.localPosition = Vector3.Lerp(transform.localPosition, wantPos, t);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, wantRot, t);
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * _heldScale, t);
            return;
        }
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


    /// <summary>The READING pose's rotation expressed in the hand socket's frame — the billboard
    /// this card would be at if the grasp were not running, converted so it can be interpolated
    /// against the hand-local grip pose. Falls back to the card's current local rotation when
    /// there is no head this frame, which makes the blend a no-op rather than a lurch.</summary>
    private Quaternion LocalBillboard(Transform socket)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return transform.localRotation;
        Vector3 away = transform.position - head.transform.position;
        if (away.sqrMagnitude <= 1e-6f)
            return transform.localRotation;
        return Quaternion.Inverse(socket.rotation)
               * Quaternion.LookRotation(away.normalized, head.transform.up);
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
    // Issue 2 (short-rest choreography): a fly-IN (the burn-offered card flying OUT of the discard
    // pile INTO its display slot) settles at its HOME pose on arrival and hands back to the normal
    // home-lerp — it must NOT run the park/hide completion a fly-OUT (to a pile) does. This flag
    // distinguishes the two while they share the one <see cref="_flying"/> tween machinery.
    private bool _flyIntro;
    private Vector3 _flyFromPos;
    // Issue 3 (user): the LOCKED orientation held for the ENTIRE flight — captured once at launch
    // and never changed (no billboarding, no reorient at launch or on arrival). The card keeps
    // facing exactly as it did (face-up on the board) so it never rotates or tumbles in the air.
    private Quaternion _flyRot;
    private Vector3 _flyFromScale;
    private Vector3 _flyToPos;
    private Vector3 _flyToScale;
    private Vector3 _flyArcUp;
    private float _flyArcHeight;
    private float _flyElapsed;
    private float _flyDuration;
    private Action? _flyDone;

    /// <summary>
    /// Issue A/3 (user): the straight-line fly-to-pile passed THROUGH the control board, and even
    /// the first arc was a flat skim the user could not follow. The flight now bows UP along the
    /// board's up axis so the card arcs clearly OVER the board top, visibly readable in flight. The
    /// lift is this fraction of the travel distance (world meters) so it scales with the board's
    /// diorama scale automatically; callers ALSO pass an absolute <c>minArcHeight</c> (board-scaled)
    /// so even a SHORT hop still peaks above the board's top edge. Zero at both ends (parabola
    /// peaking at the temporal midpoint). Raised from 0.35 → 0.55 for issue 3 (a taller, clearly
    /// followable arch rather than a flat pass).
    /// </summary>
    internal const float FlyArcHeightFraction = 0.55f;

    /// <summary>
    /// SMOOTHNESS FLOOR (user issue 1 "choppy flights"): the flight never runs shorter than this,
    /// so on a low frame-rate frame (the tick is unscaled-dt capped at 0.05 s) the eased motion
    /// still has enough sub-steps to read as a smooth glide rather than a couple of visible jumps.
    /// Callers may pass longer; the flight takes the larger of the two.
    /// </summary>
    internal const float MinFlySeconds = 0.35f;

    /// <summary>
    /// Smootherstep (Ken Perlin's C² ease-in-out: <c>6t⁵ − 15t⁴ + 10t³</c>) — symmetric about
    /// t = 0.5 with zero first AND second derivative at both ends, so the eased motion has no
    /// velocity/acceleration kink (the source of the "choppy" look). At t = 0.5 it returns exactly
    /// 0.5, so a flight driven by this is at the spatial midpoint at the temporal midpoint — which
    /// is exactly where the symmetric arc peaks, keeping the bow centered instead of lopsided.
    /// </summary>
    internal static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    /// <summary>
    /// Parabolic lift offset along <paramref name="up"/> for a fly-to-pile at eased progress
    /// <paramref name="t"/> (0..1): <c>height · 4 · t · (1−t)</c> — zero at both ends, peaking at
    /// <paramref name="height"/> when t = 0.5. The card flights feed the SMOOTHERSTEP-eased
    /// parameter here (peak stays at the temporal midpoint because smootherstep is symmetric), so
    /// the lift ramps up and settles as gently as the horizontal slide — no stepped/linear bow.
    /// Shared by <see cref="FlyToPile"/> and the transient burn-slab fallback (same arc shape).
    /// </summary>
    internal static Vector3 FlyArcOffset(float t, Vector3 up, float height) =>
        up * (height * 4f * t * (1f - t));

    /// <summary>True while this card is animating into a pile (see <see cref="FlyToPile"/>).</summary>
    internal bool IsFlying => _flying;

    /// <summary>
    /// Issue 5: fly this card from its current world pose into a pile stack over
    /// <paramref name="duration"/> unscaled seconds, shrinking to <paramref name="targetWorldWidth"/>
    /// (the pile slab's on-screen width), then invoke <paramref name="onComplete"/> (CardsDriver
    /// parks/hides it there). No-op-safe to call while active/visible; a held card is never flown
    /// (CardsDriver only launches this on cleared, un-held round cards). Rotation is held constant —
    /// the card slides in flat, as it sat on the board. The flight bows UP along
    /// <paramref name="arcUp"/> (the board's up axis, so it works when the board is tilted) so the
    /// card arcs OVER the board instead of passing through it — see <see cref="FlyArcOffset"/>.
    /// </summary>
    internal void FlyToPile(Vector3 targetWorldPos, float targetWorldWidth, float duration, Vector3 arcUp,
        Action onComplete, float minArcHeight = 0f)
    {
        CancelAppear(); // a fly wins over a running materialize — never leave it half-faded/bodiless
        _flying = true;
        _flyIntro = false; // fly-OUT: run the park/hide completion on arrival
        _flyElapsed = 0f;
        _flyDuration = Mathf.Max(MinFlySeconds, duration);
        _flyFromPos = transform.position;
        // Issue 3: LOCK the orientation to whatever the card had (face-up on the board) for the
        // whole flight — captured once, never changed, so the card never rotates or billboards.
        _flyRot = transform.rotation;
        _flyFromScale = transform.localScale;
        _flyToPos = targetWorldPos;
        // Issue 1 (arc toward the CEILING): the bow always lifts along WORLD up — toward the
        // player's head / the ceiling — regardless of how the board is tilted. The caller's
        // board-up `arcUp` is intentionally ignored so a tilted board can never lean the arch
        // sideways or into the table.
        _flyArcUp = Vector3.up;
        // Issue 3: peak the arch at max(distance-fraction, an absolute board-scaled floor) so even a
        // short hop still clears the board top instead of skimming across it.
        _flyArcHeight = Mathf.Max(minArcHeight, Vector3.Distance(_flyFromPos, _flyToPos) * FlyArcHeightFraction);
        // Convert the wanted on-screen width into a LOCAL scale under the current parent, so the
        // card ends roughly the size of a pile slab regardless of the board's live diorama scale.
        float parentLossy = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        float w = CardsConfig.CardWidth.Value;
        float targetLocal = (parentLossy > 1e-5f && w > 1e-5f)
            ? targetWorldWidth / (parentLossy * w)
            : transform.localScale.x;
        _flyToScale = Vector3.one * Mathf.Max(1e-4f, targetLocal);
        _flyDone = onComplete;
        LogAnim("fly-out", _flyFromPos, _flyToPos, "to pile (world-up arch, orientation locked)");
        // Drop every hover/grab affordance — a flying card makes no promises.
        Grabbable = false;
        _popped = false;
        _laserPopped = false;
        _pokeHover = false;
        SetHandPopSuppressed(false);
        _pop = 0f;
        _releaseGlide = 0f;
        _instantNext = false;
    }

    /// <summary>
    /// Issue 2 (short-rest choreography): fly this card IN from a pile — the reverse of
    /// <see cref="FlyToPile"/>. The burn-offered short-rest card ORIGINATES in the discard pile, so
    /// it flies OUT of <paramref name="fromWorldPos"/> (growing from the pile-slab size) and ARCHES
    /// over the board into the HOME pose the layout already asserted (<see cref="SetHome"/> /
    /// PlacePickCard must run first), then settles there and hands back to the normal home-lerp — it
    /// never parks/hides (that is the fly-OUT completion). Orientation is LOCKED to the home
    /// (face-up) rotation for the whole flight — no rotation, no billboard. Runs on unscaled time,
    /// bowing along <paramref name="arcUp"/> (the board up axis) so it works on a tilted board.
    /// No-op on a held card (the hand owns the pose).
    /// </summary>
    internal void FlyFromPile(Vector3 fromWorldPos, float fromWorldWidth, float duration, Vector3 arcUp, float minArcHeight = 0f, Action? onComplete = null)
    {
        if (IsHeld)
            return;
        Transform? parent = transform.parent;
        // The destination is the home pose the layout just set (in parent-local space).
        Vector3 toWorld = parent != null ? parent.TransformPoint(_homePos) : _homePos;
        _flyRot = parent != null ? parent.rotation * _homeRot : _homeRot; // LOCKED upright/home orientation

        CancelAppear(); // a fly wins over a running materialize — never leave it half-faded/bodiless
        _flying = true;
        _flyIntro = true; // fly-IN: settle at home on arrival, do NOT park
        _flyElapsed = 0f;
        _flyDuration = Mathf.Max(MinFlySeconds, duration);
        _flyFromPos = fromWorldPos;
        _flyToPos = toWorld;
        // Issue 1: arch along WORLD up (toward the ceiling / the player's head), never the tilted
        // board up — the caller's `arcUp` is intentionally ignored.
        _flyArcUp = Vector3.up;
        _flyArcHeight = Mathf.Max(minArcHeight, Vector3.Distance(_flyFromPos, _flyToPos) * FlyArcHeightFraction);
        LogAnim("fly-in", _flyFromPos, _flyToPos, "from pile to home (world-up arch, orientation locked)");

        // Start pile-slab sized, grow to the home scale (mirror of FlyToPile's shrink).
        float parentLossy = parent != null ? parent.lossyScale.x : 1f;
        float w = CardsConfig.CardWidth.Value;
        float fromLocal = (parentLossy > 1e-5f && w > 1e-5f) ? fromWorldWidth / (parentLossy * w) : _homeScale;
        _flyFromScale = Vector3.one * Mathf.Max(1e-4f, fromLocal);
        _flyToScale = Vector3.one * Mathf.Max(1e-4f, _homeScale);
        _flyDone = onComplete; // home arrivals may restore their interaction ownership

        // Drop every hover/grab affordance — a flying card makes no promises.
        Grabbable = false;
        _popped = false;
        _laserPopped = false;
        _pokeHover = false;
        SetHandPopSuppressed(false);
        _pop = 0f;
        _releaseGlide = 0f;
        _instantNext = false;
        SetVisualAlpha(1f); // the offered card is fully visible for the whole flight (no fade)

        // Seed the start pose (at the pile) NOW so no home-pose frame renders before Update's first
        // fly tick — the card visibly departs FROM the pile, never flashing at its slot first.
        transform.position = _flyFromPos;
        transform.rotation = _flyRot;
        transform.localScale = _flyFromScale;
    }

    /// <summary>Cancel any in-flight fly-to-pile (teardown / re-adoption). Does NOT run the callback.</summary>
    private void CancelFly()
    {
        _flying = false;
        _flyIntro = false;
        _flyDone = null;
    }

    // ---------------------------------------------------------- appear / disappear (issue 2) --

    // Issue 2 (user): "nothing should ever just appear or disappear without a corresponding
    // animation." When the control board's docked action cards clear/re-populate on a character/
    // turn switch they used to POP in/out instantly. A cleared card that is NOT flying to a pile
    // (issue 1 owns those) plays a quick scale-down + face fade before it is parked; a freshly
    // docked card scales+fades IN instead of popping from nothing. Both run on UNSCALED time (card
    // phases pause timeScale) and hold position/rotation constant (no slide, no reorient). Purely
    // VR presentation. A held/flying card is never appear/disappear-animated (the hand / fly owns
    // the pose); the two are mutually exclusive with the fly (CardsDriver only Vanishes cards the
    // fly-to-pile path declined), so a card is never double-animated.
    internal const float DockVanishSeconds = 0.30f;
    internal const float DockAppearSeconds = 0.28f;

    /// <summary>
    /// Issue 2 DUST: the card no longer shrinks to nothing / grows from nothing — the DUST is the
    /// star. The card only settles to / rises from this scale as a SECONDARY cue while it crumbles
    /// (vanish) or coalesces (appear); the primary read is the drifting dust puff + the quick fade.
    /// </summary>
    // INTERNAL SINCE 2026-08-28 so the mirrored slot card can REFERENCE it rather than copy it.
    // Every other number in RemoteBoardCard's materialise ramp — the curve, both durations, the
    // hitch cap — is already a reference to this class; this was the only one that had to be a
    // duplicated literal, and it was a duplicate solely because of an access modifier.
    internal const float DustSettleScale = 0.82f;

    /// <summary>
    /// The card's colour/tone the crumble/materialize dust motes take (user: "in the card's colour/
    /// tone"). A warm parchment default reads as card-paper dust for real game faces; placeholder
    /// cards seed their own tint (<see cref="BuildPlaceholderFace"/>).
    /// </summary>
    private Color _dustTone = CardDustFx.DefaultTone;

    private bool _vanishing;
    private float _vanishElapsed;
    private Vector3 _vanishFromScale;
    private Action? _vanishDone;

    private bool _appearing;
    private float _appearElapsed;

    private CanvasGroup? _faceGroup;

    /// <summary>True while this card is playing the disappear (scale-down + fade) before it parks.</summary>
    internal bool IsVanishing => _vanishing;

    /// <summary>
    /// Fade the adopted face art (a lazy <see cref="CanvasGroup"/> on our world-space face canvas).
    /// This fades the ART ONLY — the 3D body (backing slab) is opaque and does NOT follow, so every
    /// caller that fades below alpha 1 must ALSO suppress the body via <see cref="SetBodyVisible"/>
    /// (see the "black slab over the slot overlay" root cause there). No shared game/card material
    /// is ever touched. Reset to 1 whenever an animation ends or the card is disabled (pool-safe).
    /// </summary>
    private void SetVisualAlpha(float alpha)
    {
        if (_canvas == null)
            return;
        if (_faceGroup == null)
        {
            _faceGroup = _canvas.GetComponent<CanvasGroup>();
            if (_faceGroup == null)
                _faceGroup = _canvas.gameObject.AddComponent<CanvasGroup>();
        }
        _faceGroup.alpha = Mathf.Clamp01(alpha);
    }

    // ------------------------------------------- body suppression during appear/disappear --
    //
    // USER BUG ("wenn man zu einem Charakter wechselt, der die Karten noch nicht gelegt hat und dort
    // noch die Overlays sind, werden die Verschwindenden kurz so schwarz in dem neu auftauchenden
    // Overlay — sieht aus wie ein Glitch"): switching to a character who has NOT played yet turns the
    // still-empty play slots' TEAL "wanted slot" overlay ON (PlayTray.BuildWantedHighlights) in the
    // very same frame the OUTGOING character's slot cards start their crumble-to-dust vanish. For
    // ~0.3 s the two share one slot — and that exposed TWO defects of the vanish, both rooted in the
    // SAME thing: the vanish only fades the FACE ART (a CanvasGroup on our world-space face canvas)
    // while the card's 3D BODY stays 100 % opaque, because the settle-shrink is only to
    // DustSettleScale (0.82) — it never shrinks the slab away the way the original scale-to-zero
    // disappear did (the stale claim this comment block replaces).
    //
    //   1. BLACK SLAB. CardMesh's front/rim material is EdgeColor — near-black (0.10, 0.09, 0.08)
    //      when this was diagnosed, warm umber since round 14 — because it normally sits entirely
    //      BEHIND the card art (the backing is fitted to
    //      VisibleFaceFraction, inside CardFace's border inset). As the art fades toward 0 that
    //      near-black front face is progressively UNCOVERED, so the last ~two thirds of the vanish is
    //      a black card-shaped slab sitting in the freshly lit teal overlay. That is the "schwarz"
    //      the user sees. Identical defect on the APPEAR side: PlayAppear seeds alpha 0 with the slab
    //      still opaque, so an incoming card starts as a black slab and fades its art in over itself.
    //
    //   2. Z-FIGHT WEDGES. The slab is opaque and ZWRITES (Standard opaque; the punched-out
    //      contour body writes depth the same way). The overlay quad is the bundled
    //      GloomhavenVR/Overlay shader — additive, ZWrite Off, ZTest LEqual, Queue Transparent — so
    //      it depth-tests against whatever the slab wrote. Card and overlay are effectively coplanar
    //      in the slot (both are slot children a few millimetres off the recess floor), so the test
    //      resolves inconsistently across the slab's CENTRE-FAN triangulation: the hardware capture
    //      shows exactly that — jagged teal wedges radiating from the card's fan centre. Normally the
    //      two are never co-present (the wanted-overlay shows only for an EMPTY slot), which is why
    //      this only ever appeared during the switch-over animation.
    //
    // FIX (one change, both symptoms): while an appear/disappear runs, HIDE the body renderers. The
    // slab is fully covered by the art at alpha 1 anyway (only its ~1.5 mm rim reads, and only at a
    // grazing angle), so dropping it is imperceptible at the start of the animation — but it means
    // the fade now carries the WHOLE card (nothing opaque left to be uncovered → no black), and the
    // card stops writing depth for the animation's duration, so the additive overlay draws uniformly
    // underneath it (→ no z-fight wedges). Renderer.enabled only: no material is created, mutated or
    // cloned (the front/rim + back materials are SHARED with every other card and with
    // Net.RemoteHandFan's opponent hand backs — fading them would tint every card in the scene), no
    // game object is touched, and the flag is restored on every exit path incl. OnDisable, so a
    // pooled card can never come back invisible. Purely local presentation — never networked.

    private Renderer[]? _bodyRenderers;
    private bool _bodyHidden;
    private static bool s_loggedBodySuppressed;

    /// <summary>
    /// Show/hide the card's 3D body (the backing slab: front, rim and decorative back) WITHOUT
    /// touching any material — used to keep the opaque near-black slab from being uncovered (and
    /// from ZWRITING into the slot overlay) while <see cref="SetVisualAlpha"/> fades the art during
    /// <see cref="Vanish"/> / <see cref="PlayAppear"/>. Idempotent, allocation-free after the first
    /// call, and safe on a prefab backing with several renderers.
    /// </summary>
    private void SetBodyVisible(bool visible)
    {
        if (_bodyHidden == !visible)
            return;
        if (_bodyRenderers == null)
        {
            if (_backing == null)
                return;
            _bodyRenderers = _backing.GetComponentsInChildren<Renderer>(true);
        }
        for (int i = 0; i < _bodyRenderers.Length; i++)
        {
            Renderer r = _bodyRenderers[i];
            if (r != null)
                r.enabled = visible;
        }
        _bodyHidden = !visible;
        if (!visible && !s_loggedBodySuppressed)
        {
            s_loggedBodySuppressed = true;
            Core.VRLog.Info("Cards", $"Card body suppressed during appear/disappear ('{name}', " +
                                     $"{_bodyRenderers.Length} backing renderer(s)): the opaque near-black slab " +
                                     "no longer surfaces as the face art fades, and it stops ZWRITING — so a card " +
                                     "crumbling out over a freshly lit teal slot overlay shows neither a black " +
                                     "slab nor z-fight wedges. Renderers re-enabled on every exit path.");
        }
    }

    // Throttle so a relayout that animates several cards in one frame logs a couple of lines, not a
    // storm — shared across every card (mirrors the rooted-flip / ButtonTuning.LogAnim throttles).
    private static float s_nextAnimLogAt;

    /// <summary>
    /// Log one card animation (user: "log each animation — type, from/to, reason"). Types: fly-out,
    /// fly-in, vanish, appear. Throttled to the [Cards] debug channel so a multi-card sweep does not
    /// spam the hardware log.
    /// </summary>
    private void LogAnim(string type, Vector3 from, Vector3 to, string reason)
    {
        float now = Time.unscaledTime;
        if (now < s_nextAnimLogAt)
            return;
        s_nextAnimLogAt = now + 0.2f;
        Core.VRLog.Debug("Cards", $"Card anim '{name}': {type} from ({from.x:F2},{from.y:F2},{from.z:F2}) " +
                                  $"to ({to.x:F2},{to.y:F2},{to.z:F2}) — {reason}.");
    }

    /// <summary>
    /// Emit the crumble (<paramref name="appear"/> = false) or materialize (<paramref name="appear"/>
    /// = true) dust for this card at its CURRENT world pose, spread over the card's face rectangle in
    /// its own colour/tone. Allocation-free (a shared pooled system). Degrades to no dust in a shader-
    /// less environment — the fade + settle-scale still play.
    /// </summary>
    private void EmitCardDust(bool appear)
    {
        float lossy = transform.lossyScale.x;
        float halfW = _fullColliderSize.x * 0.5f * lossy;
        float halfH = _fullColliderSize.y * 0.5f * lossy;
        if (halfW < 1e-4f || halfH < 1e-4f)
            return;
        Vector3 center = transform.position;
        Vector3 right = transform.right;
        Vector3 up = transform.up;
        Vector3 outNormal = -transform.forward; // card +Z points AWAY from the viewer; dust puffs toward them
        if (appear)
            CardDustFx.EmitAppear(center, right, up, outNormal, halfW, halfH, _dustTone);
        else
            CardDustFx.EmitVanish(center, right, up, outNormal, halfW, halfH, _dustTone);
    }

    /// <summary>
    /// Issue 2 DISAPPEAR ("crumble to dust"): the card bursts into a puff of drifting dust motes (in
    /// its own colour/tone) and quickly fades over <see cref="DockVanishSeconds"/> — a tiny settle-
    /// shrink is only a secondary cue — then invokes <paramref name="onComplete"/> (CardsDriver parks/
    /// hides it). No-op-restart while already vanishing; a held/flying card cannot vanish (the callback
    /// still runs so the caller's park bookkeeping is not stranded). Rotation and position are held
    /// constant — the card crumbles exactly where it sat, it never slides or reorients. The dust is
    /// purely visual; interactivity is dropped THIS instant below. Suppressed on the initial build by
    /// the caller (CardsDriver only Vanishes an already-present card, never a first-built one).
    /// </summary>
    internal void Vanish(Action onComplete)
    {
        if (_vanishing)
            return;
        if (IsHeld || _flying)
        {
            onComplete?.Invoke();
            return;
        }
        _vanishing = true;
        _appearing = false;
        _vanishElapsed = 0f;
        _vanishFromScale = transform.localScale;
        _vanishDone = onComplete;
        // The art fade below is the WHOLE fade only if nothing opaque is left underneath it — drop
        // the body for the duration (see SetBodyVisible: black-slab + overlay z-fight root cause).
        SetBodyVisible(false);
        EmitCardDust(appear: false); // crumble puff at the card's current pose
        LogAnim("vanish", transform.position, transform.position, "dust crumble (in place)");
        // A vanishing card makes no promises — drop every hover/grab affordance.
        Grabbable = false;
        _popped = false;
        _laserPopped = false;
        _pokeHover = false;
        SetHandPopSuppressed(false);
        _pop = 0f;
        _releaseGlide = 0f;
        _instantNext = false;
    }

    /// <summary>
    /// Issue 2 APPEAR ("emerge from dust"): the card MATERIALIZES from converging/settling dust motes
    /// (in its own colour/tone) plus a quick fade-in over <see cref="DockAppearSeconds"/>, instead of
    /// scaling up from nothing — a tiny grow (from <see cref="DustSettleScale"/> to full) is only a
    /// secondary cue. Snaps position/rotation to the current home immediately (no slide from the pool
    /// origin) and starts near-full scale but fully transparent THIS instant so there is never a full-
    /// opacity flash before the first animated frame. Called by CardsDriver right after the layout
    /// asserts the card's home. No-op for a held/flying card. Suppressed on the initial build by the
    /// caller (initial cards seed their home instantly and never call this — no materialize storm).
    /// </summary>
    internal void PlayAppear()
    {
        if (IsHeld || _flying)
            return;
        _appearing = true;
        _vanishing = false;
        _vanishDone = null;
        _appearElapsed = 0f;
        _instantNext = false;
        // Seed the start pose NOW so no full-opacity frame renders before Update's first appear tick.
        transform.localPosition = _homePos;
        transform.localRotation = _homeRot;
        transform.localScale = Vector3.one * (_homeScale * DustSettleScale);
        // Same reason as Vanish: at alpha 0 the opaque near-black slab would BE the card, so an
        // incoming card would materialize as a black slab over the slot overlay before its art
        // faded in. Body off for the duration; restored when the appear completes.
        SetBodyVisible(false);
        SetVisualAlpha(0f);
        EmitCardDust(appear: true); // converging motes coalesce into the card at its home pose
        LogAnim("appear", transform.position, transform.position, "dust materialize (in place)");
    }

    /// <summary>
    /// End a running materialize IMMEDIATELY at its finished state (full alpha, body back). Anything
    /// that takes the transform away from the appear tick — a grab, a fly — must call this, or the
    /// card is stranded half-faded AND bodiless (its opaque slab is suppressed for the animation's
    /// duration, see <see cref="SetBodyVisible"/>). No-op when no appear is running. Deliberately
    /// does NOT touch a running <see cref="Vanish"/>: a vanishing card is un-grabbable and never also
    /// flies (the driver picks exactly one), and silently dropping it would strand the park callback.
    /// </summary>
    private void CancelAppear()
    {
        if (!_appearing)
            return;
        _appearing = false;
        SetVisualAlpha(1f);
        SetBodyVisible(true);
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

    // ---- IFanSweepTarget (explicit: the shared sweep election, no public surface widened) ----

    /// <summary>A card that cannot be grabbed (rooted, bug B) or is already in a hand must never
    /// win the sweep — a dead winner would suppress the lift of a live card right beside it.</summary>
    bool IFanSweepTarget.SweepEligible => !IsHeld && CanGrab;

    /// <summary>
    /// The card's width in WORLD units — the size the shared reach is derived from
    /// (<see cref="FanSweep.ResolveReach"/>).
    ///
    /// Deliberately the AUTHORED width (<c>CardsConfig.CardWidth</c> through the live transform
    /// chain), NOT <see cref="_fullColliderSize"/>. The collider is fit to the VISIBLE ART, which
    /// is inset by <see cref="VisibleFaceFraction"/> and letterboxed to the face canvas' aspect —
    /// roughly 0.88 of the authored width, a rendering detail. Feeding that in would have quietly
    /// retuned the ABILITY hand fan by ~12 %, and the hand fan is the one thing that must not move:
    /// with the authored width it resolves to relative size exactly 1 and gets its proven constants
    /// back untouched. The SAME card in the board-anchored browse arc reports the board's scale and
    /// the arc's enlargement through <c>lossyScale</c>, which is the whole point.
    /// </summary>
    float IFanSweepTarget.SweepFaceWidthWorld
        => CardsConfig.CardWidth.Value * transform.lossyScale.x;

    bool IFanSweepTarget.TrySweepDistance(Vector3 worldPoint, out float distance)
        => TryFingertipDistance(worldPoint, out distance);

    string IFanSweepTarget.SweepName => name;

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

    /// <summary>
    /// Perf attribution (2026-07 perf pass): this Update runs PER CARD — a full hand plus a pile
    /// browser can be twenty-odd instances, so its cost is the one that scales with what the
    /// player is holding. The step aggregates all instances under one name and reports the call
    /// count next to the frame count, so the [Perf] STEPS line reads as "n cards x m µs".
    /// </summary>
    private void Update()
    {
        using (Core.PerfMonitor.Scope("Cards.VRCard"))
            UpdateBody();
    }

    /// <summary>
    /// CARD-ART ARRIVAL (user: "ich will die Karte ohne Aliasing direkt sehen ohne dass es erst
    /// nachgeladen werden muss"). LateUpdate on purpose, and it is the whole point of the fix:
    /// the game's addressable loader assigns the new sprite from a sync-context continuation
    /// somewhere inside the Update phase, and uGUI rebuilds/draws the canvas AFTER LateUpdate —
    /// so a swap issued here is guaranteed to be in place before that art's first rendered
    /// frame, no matter where in Update the continuation happened to run. Doing it from
    /// <see cref="Update"/> instead would race the loader and lose roughly half the time.
    /// Allocation-free unless a sprite actually changed (see <c>CardFace.MaintainArtArrival</c>).
    /// </summary>
    private void LateUpdate()
    {
        using (Core.PerfMonitor.Scope("Cards.VRCardArt"))
        {
            _face.MaintainArtArrival();
            PreserveSpentBurnAppearance();
        }
    }

    // Native card effects can start after Update. Keep the spent base before both the
    // camera draw and the owner appearance capture, regardless of LateUpdate ordering.
    internal void PreserveSpentBurnAppearance() => _burnFx.PreserveSpentBurnAppearance(FullCard);

    private void UpdateBody()
    {
        UpdateCanvasCamera();
        _face.Maintain();
        // Parent enable/disable can cancel the native pulse between rebuilds. Reassert
        // only the active-column look here; the shared driver leaves a live tween alone.
        if (_face.IsAdopted && (_activeHighlightTop || _activeHighlightBottom))
            SetActionHighlight(_activeHighlightTop, _activeHighlightBottom);
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
            CancelFly(); // a re-grab mid-flight wins — the hand owns the pose and clears the old callback
            // A grab mid-APPEAR wins the same way — and because the held branch returns before the
            // appear tick below, the animation would otherwise be frozen half-faded with its body
            // suppressed (an invisible-ish card stuck in the hand). Finish it here instead: full
            // alpha, body back. (A VANISHING card cannot get here — Vanish drops Grabbable and
            // refuses to start on a held card, so its completion callback is never stranded.)
            CancelAppear();
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
            // Issue 1 (SMOOTH): one SMOOTHERSTEP-eased parameter drives BOTH the horizontal slide
            // AND the arc, so they ramp up and settle together (no ease-out slide fighting a linear
            // bow — that mismatch was the "choppy"/lopsided look). Smootherstep is symmetric, so at
            // the temporal midpoint the card is at the spatial midpoint AND the arc is at its peak.
            float s = SmootherStep(ft);
            transform.position = Vector3.Lerp(_flyFromPos, _flyToPos, s) + FlyArcOffset(s, _flyArcUp, _flyArcHeight);
            transform.rotation = _flyRot; // issue 3: fixed for the whole flight — never rotates/billboards
            transform.localScale = Vector3.Lerp(_flyFromScale, _flyToScale, s); // eased scale too
            if (ft >= 1f)
            {
                _flying = false;
                if (_flyIntro)
                {
                    // Issue 2 fly-IN: settle exactly at the home pose and hand back to the normal
                    // home-lerp (no park). The card is now sitting at its display slot.
                    _flyIntro = false;
                    transform.localPosition = _homePos;
                    transform.localRotation = _homeRot;
                    transform.localScale = Vector3.one * _homeScale;
                    SetVisualAlpha(1f);
                    Action? arrived = _flyDone;
                    _flyDone = null;
                    arrived?.Invoke();
                    return;
                }
                Action? done = _flyDone;
                _flyDone = null;
                done?.Invoke();
            }
            return;
        }

        // Issue 2 DISAPPEAR (crumble to dust): the dust burst was emitted at Vanish(); here the card
        // only quickly FADES while it settles a hair smaller (secondary cue), position/rotation held,
        // then parks via the completion callback. Unscaled time; owns the transform while it runs.
        if (_vanishing)
        {
            float vdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap, like the fly anim
            _vanishElapsed += vdt;
            float vt = DockVanishSeconds > 0f ? Mathf.Clamp01(_vanishElapsed / DockVanishSeconds) : 1f;
            float s = SmootherStep(vt);
            transform.localScale = _vanishFromScale * Mathf.Lerp(1f, DustSettleScale, s); // slight settle only
            SetVisualAlpha(1f - s);                                                       // dust carries the vanish
            if (vt >= 1f)
            {
                _vanishing = false;
                Action? done = _vanishDone;
                _vanishDone = null;
                SetVisualAlpha(1f); // reset for the pooled card's next life (it is about to hide)
                SetBodyVisible(true); // ditto for the body — a pooled card never comes back bodiless
                done?.Invoke();
            }
            return;
        }

        // Issue 2 APPEAR (emerge from dust): the converging motes were emitted at PlayAppear(); here
        // the card FADES in while it grows the last little bit into place (secondary cue), position/
        // rotation snapped to home. Unscaled time; ends fully opaque at full scale.
        if (_appearing)
        {
            float adt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _appearElapsed += adt;
            float at = DockAppearSeconds > 0f ? Mathf.Clamp01(_appearElapsed / DockAppearSeconds) : 1f;
            float s = SmootherStep(at);
            transform.localPosition = _homePos;
            transform.localRotation = _homeRot;
            transform.localScale = Vector3.one * (_homeScale * Mathf.Lerp(DustSettleScale, 1f, s));
            SetVisualAlpha(s);
            if (at >= 1f)
            {
                _appearing = false;
                SetVisualAlpha(1f);
                SetBodyVisible(true); // fully opaque art again — the slab is covered, bring it back
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
        // ONE predicate, read here and broadcast by the Net layer (see IsHighlighted) so a peer's
        // copy of this fan lifts the same card this one does.
        float popTarget = IsHighlighted ? 1f : 0f;
        _pop = Mathf.MoveTowards(_pop, popTarget, dt * PopRate);

        // Pop: toward the viewer (-Z of the card) and slightly up, plus scale-up. The
        // forward magnitude is [Cards] FanSelectedPopForward (G2) so it matches the fan
        // split; falls back to the 0.035 m default if the config is not yet bound.
        float popForward = CardsConfig.FanSelectedPopForward != null
            ? CardsConfig.FanSelectedPopForward.Value : 0.035f;
        Vector3 target = _homePos + _homeRot * new Vector3(0f, PopUp * _pop, -popForward * _pop);
        float scale = _homeScale * (1f + PopScale * _pop);

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
        SetHandPopSuppressed(false); // arbitration flags never outlive a pooled/parked card
        AllowsGateHand = false;      // parked default = most restrictive; the next role re-stamps
                                     // it (Rebuild: !inFan — general rule 2026-08-04)
        PileOrigin = null;           // a parked/pooled card is on loan from nowhere; the browse
                                     // arc re-stamps it on the next borrow (UpdateBrowser)
        _pop = 0f;
        _releaseGlide = 0f;
        CancelFly(); // a parked/pooled card is never mid-flight
        // Issue 2: a parked/pooled card carries no half-finished appear/disappear into its next life.
        _vanishing = false;
        _vanishDone = null;
        _appearing = false;
        SetVisualAlpha(1f);
        SetBodyVisible(true); // a parked/pooled card never carries a suppressed body into its next life
    }

    /// <summary>
    /// BOTH STEPS ARE RESTORE CONTRACTS, so neither may be skipped by the other throwing.
    /// <c>DetachGameCard</c> hands the game's own card widget back; <c>_burnFx.Detach</c> puts a
    /// bounded smoke module back. A card is destroyed in the scene-unload wave of a scenario
    /// restart, where the game widget it borrowed may already be gone — and an unguarded throw
    /// there printed one anonymous, stackless "NullReferenceException" (the game disables stack
    /// traces process-wide; see Core/ExceptionTraces) and left the OTHER restore undone.
    /// </summary>
    private void OnDestroy()
    {
        Core.TickGuard.Run("Cards.Teardown.BurnFx", _burnFx.Detach, "Cards");
        Core.TickGuard.Run("Cards.Teardown.DetachGameCard", DetachGameCard, "Cards");
    }
}

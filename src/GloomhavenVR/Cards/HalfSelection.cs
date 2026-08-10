using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// In-turn action selection: the round's played cards sit docked in the control
/// board's card slots (test #19; head-floating layout only as the no-tray fallback);
/// poking the top or bottom half commits that half via
/// <c>FullAbilityCard.OnAbilityClick(ActionType, isProxyAction: false, checkValid: true)</c>
/// — the identical call the 2D buttons make, so all validity/phase guards apply
/// (see CardsGameApi.PlayHalf). The CARD HALVES are the whole affordance (test #19):
/// no mod-side "ATK 2"-style buttons — the game's card art already shows the values,
/// and the game's own face widgets (default-action buttons, consume/infusion
/// buttons) stay reachable because each card's world canvas is registered with the
/// P2 <c>UguiPokeSurfaces</c> while in this layout (finger poke AND dominant laser
/// via <c>RayUguiDriver</c>). Laser commits therefore run through the REAL
/// 'Top button'/'Bottom button' uGUI click path; the mod zones additionally handle
/// fingertip pokes but stay INVISIBLE (test #20 + ITEM 9: the game's own on-card
/// highlight is the only hover/selection feedback — the mod paints no backing quad;
/// the earlier per-half "invalid" dim read as a black semi-transparent sheet over the
/// cards). Half validity is still mirrored from the game's own query
/// (<c>FullAbilityCard.IsInteractable + isValid</c>) so an invalid half's poke is a
/// no-op, but it is never visualised. The
/// <c>CardsActionControlller</c> phase machine (Select1st → Pick1st → Select2nd →
/// Pick2nd) drives which halves report playable — we only mirror it.
///
/// LASER HALF HIGHLIGHT IS GEOMETRIC (user report 2026-08: "das Highlighting wird durch
/// die Tooltipps ... unterbrochen"). ROOT CAUSE, proven from the decompiled sources and
/// the hardware log: the game's half highlight is driven purely by uGUI pointer
/// enter/exit on per-graphic <c>FullCardEventPusher</c> components
/// (<c>AddFullCardEventPusher</c> stamps one on every MaskableGraphic that exists at
/// build time - FullCardEventPusherExtension.cs; exit -> <c>FullAbilityCard.OnPointerExit
/// -> OnCardHighlight(false)</c> clears the highlight, FullAbilityCard.cs:542-545). The
/// card face is a mosaic of graphics, and several of them carry
/// <c>UITextTooltipTarget</c>s ('Text Container' ability rows, 'Header' bands -
/// CreateLayout.cs:1115/1150), so the raycast leaf under a drifting beam flaps between
/// graphics; whenever the winning leaf's ancestor chain does NOT contain the half's
/// pusher (hardware log Player.log:14423: dispatched "hover EXIT: 'Bottom button'"
/// followed by re-ENTER of 'Text Container' on the SAME card), the exit walk fires the
/// pusher's OnPointerExit and the half highlight drops - exactly while the tooltip that
/// same leaf just raised is showing, which is why the user reads it as "the tooltip
/// interrupts the highlight". The tooltip CANVAS is innocent: it is never registered
/// with UguiPokeSurfaces, has no colliders, and so can never steal the mod pointer.
///
/// FIX: while a card is docked here, the LASER's half highlight is computed from the
/// BEAM'S POSITION on the card (ray x card plane -> top/bottom zone rect - the same
/// rects the poke zones use), and the game's own hover calls (<c>Highlight</c>,
/// <c>OnPointerEnter/Exit</c>, <c>ToggleHover</c> - the exact <c>FullCardEventPusher</c>
/// dispatch) are edge-driven from that state. The per-graphic pushers are suppressed for
/// mod LASER pointer events on docked cards (<c>Patches.HalfHoverPatches</c>) so the two
/// writers can never fight; poke/mouse events keep the vanilla pusher path, and
/// <c>UITooltipTarget</c>s are untouched - tooltips keep showing, they just cannot take
/// the highlight with them.
/// </summary>
internal sealed class HalfSelection
{
    private sealed class ZoneSet
    {
        public GameObject Root = null!;
        public HalfZone Top = null!;
        public HalfZone Bottom = null!;
        public Canvas? RegisteredCanvas;
    }

    // Half-zone geometry in CARD-LOCAL units (fractions of card width/height). Shared by
    // the poke zone colliders (ArmCard) AND the laser's geometric half resolve
    // (UpdateLaserHighlight) so the two affordances can never disagree about where a
    // half begins. INTERNAL (not private) since the half-hover MP sync: the remote board's
    // hover glow (Net.RemoteBoardCard) draws the peer's lit half with these same fractions,
    // so the glowed region on a mirrored card is the region the owner's pointer is in.
    internal const float ZoneCenterYFrac = 0.27f;
    internal const float ZoneWidthFrac = 0.96f;
    internal const float ZoneHeightFrac = 0.42f;

    private readonly List<VRCard> _cards = new(4);
    private readonly Dictionary<VRCard, ZoneSet> _zones = new(4);
    private Transform? _root;
    private PlayTray? _tray;
    private bool _placed;

    /// <summary>The live instance (CardsDriver owns exactly one). Static so the
    /// <c>FullCardEventPusher</c> laser-suppression patch can ask "is this pusher's card
    /// docked here" without threading driver references through Harmony.</summary>
    private static HalfSelection? s_active;

    // ---- geometric laser half-hover state (see class doc: LASER HALF HIGHLIGHT) --------
    /// <summary>Card whose half the beam currently highlights (null = none).</summary>
    private VRCard? _laserCard;

    /// <summary>The game card the enter calls were sent to - kept so the exit is sent to
    /// the SAME object even if the VRCard re-adopts a new face mid-hover.</summary>
    private FullAbilityCard? _laserFull;

    /// <summary>True = top half currently highlighted (valid while <see cref="_laserCard"/> set).</summary>
    private bool _laserTop;

    /// <summary>Poke commit request: (card, half). CardsDriver executes it.</summary>
    internal Action<VRCard, CBaseCard.ActionType>? PlayRequested;

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    /// <summary>
    /// Test #19: the action-selection display lives ON the control board — the round
    /// cards dock straight into the tray's two card slots (same position, frame and
    /// SlotScale density as during selection; stable under tray follow/pin/move/
    /// resize because the cards parent under the slot transforms, exactly like
    /// <see cref="PlayTray.PlaceCard"/>). The head-floating layout survives only as
    /// the no-tray fallback.
    /// </summary>
    internal void DockTo(PlayTray? tray) => _tray = tray;

    /// <summary>Dock target for card <paramref name="index"/>; null → floating fallback.</summary>
    private Transform? DockSlot(int index) =>
        _tray != null && _tray.Root != null ? _tray.SlotTransform(index) : null;

    // ------------------------------------------------------------------ lifecycle --

    internal void EnsureBuilt(Transform anchorParent)
    {
        s_active = this;
        if (_root != null)
        {
            if (_root.parent != anchorParent)
            {
                _root.SetParent(anchorParent, worldPositionStays: false);
                _placed = false;
            }
            return;
        }
        _root = new GameObject("GloomhavenVR.HalfSelection").transform;
        _root.SetParent(anchorParent, worldPositionStays: false);
        _placed = false;
    }

    internal void SetVisible(bool visible)
    {
        if (_root == null)
            return;
        if (_root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
        if (visible && !_placed && DockSlot(0) == null)
            PlaceAtHead(); // floating fallback only — docked cards live on the tray
        if (!visible)
            ClearCards();
    }

    internal void InvalidatePlacement() => _placed = false;

    internal void Destroy()
    {
        ClearCards();
        if (_root != null)
        {
            UnityEngine.Object.DestroyImmediate(_root.gameObject);
            _root = null;
        }
        if (ReferenceEquals(s_active, this))
            s_active = null;
    }

    private void PlaceAtHead()
    {
        if (_root == null)
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Transform headT = head.transform;
        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float scale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
        // Slightly above/behind the tray pose: comfortable reading + poke distance.
        Vector3 pos = headT.position
                      + flatForward * (CardsConfig.TrayForward.Value * 0.95f * scale)
                      + Vector3.up * (-(CardsConfig.TrayDown.Value - 0.14f) * scale);
        _root.position = pos;
        _root.rotation = Quaternion.LookRotation(flatForward, Vector3.up)
                         * Quaternion.Euler(-18f, 0f, 0f);
        _placed = true;
    }

    // ------------------------------------------------------------------ content --

    /// <summary>
    /// READ-ONLY DOCK (feature "free character focus", user 2026-08-08: a focused character's
    /// chosen cards must LIE IN THE SLOTS even when that character is not at turn). The dock is the
    /// one card zone that arms REAL input on what it shows — the two <see cref="HalfZone"/> poke
    /// volumes, and the card's own uGUI canvas registered with <c>UguiPokeSurfaces</c>, which is
    /// what lets a laser click a card half at all. Showing a watched character's cards must not
    /// open either path, so the whole affordance is switched off structurally rather than guarded:
    /// <list type="bullet">
    /// <item><see cref="SetCards"/> DISARMS instead of arming — no zone object is active and the
    ///   canvas is never registered, so neither fingertip nor beam can find the card;</item>
    /// <item><see cref="Tick"/> marks every half unplayable and drives no hover, so the geometric
    ///   laser highlight cannot write on a foreign card either;</item>
    /// <item><see cref="RequestPlay"/> refuses outright — the fourth funnel, in case a zone ever
    ///   survives a frame it should not;</item>
    /// <item><see cref="TrySampleLocalHover"/> / <see cref="SampleLocalSelection"/> report NOTHING,
    ///   so a peer's mirror of OUR board never picks up the hover/selected half of a character we
    ///   are only watching. This is a local view change; peers see nothing new.</item>
    /// </list>
    /// </summary>
    internal bool ReadOnly { get; private set; }

    /// <summary>Set the read-only mode. Called by the driver BEFORE <see cref="SetVisible"/> /
    /// <see cref="SetCards"/> so the first frame of a focus view is already inert.</summary>
    internal void SetReadOnly(bool readOnly)
    {
        if (ReadOnly == readOnly)
            return;
        ReadOnly = readOnly;
        // Re-stamp whatever is already docked: a dock that learned it was read-only one frame late
        // would be pokeable/clickable for exactly that frame.
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard card = _cards[i];
            if (card == null)
                continue;
            if (readOnly)
                DisarmCard(card);
            else
                ArmCard(card);
        }
        if (readOnly)
            ClearLaserHighlight();
    }

    /// <summary>Lay out the played cards (1 or 2) and arm their poke zones (never while
    /// <see cref="ReadOnly"/> — see that property).</summary>
    internal void SetCards(List<VRCard> cards)
    {
        // Disarm zones of cards leaving the layout.
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (!cards.Contains(_cards[i]))
                DisarmCard(_cards[i]);
        }

        _cards.Clear();
        _cards.AddRange(cards);
        if (_root == null)
            return;

        float w = CardsConfig.CardWidth.Value;
        const float layoutScale = 1.45f;
        int n = _cards.Count;
        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null)
                continue;
            card.gameObject.SetActive(true);
            card.Grabbable = false; // pokes only in this layout
            Transform? slot = DockSlot(i);
            if (slot != null)
            {
                // Docked (test #19): the round cards stay in the SAME tray slots
                // they were played into — home scale 1 under the slot root, so the
                // slot's own SlotScale is the card density (test #18 pattern). Seated
                // into the physical recess with the shared inset (test #28) and scaled up
                // to fill the recess (ITEM 3, [Cards] SlotOverlayScale_{board}).
                //
                // THE SEAT IS PER-SLOT (2026-08-11). User report, verbatim: "Die Karten auf dem
                // controllboard liegen leicht unterschiedlich auf der x-achse als andere. Das fällt
                // mir auch wenn ich die charactere wechsle, haben manche Karten einen leicht höhren
                // abstand auf der x-achse zwischen sich, nicht viel - aber visibel."
                //
                // ROOT CAUSE: this line used to read PlayTray.SlotHomeOffset — which is
                // SlotHomeOffsetFor(0, applySpread: FALSE), i.e. the pair-spread term dropped, and
                // dropped for BOTH cards. Every other slot-home path (PlayTray.PlaceCard,
                // PlacePickCard, the live-tune re-home in PlayTray.3.Pose) takes
                // SlotHomeOffsetFor(slot) WITH the spread, and so do the teal wanted-pulse and the
                // gold snap glow (BuildWantedHighlights / BuildSlotHighlights) and the peer's copies
                // of them (Net.RemoteBoardFurniture.SlotOverlayLocal). So the two writers of the
                // very same two recesses disagreed on exactly ONE axis — X — which is the axis the
                // report names.
                //
                // THE ARITHMETIC (Oak, shipped defaults: SlotSpacing 0.155, SlotOverlayOffset_Oak
                // x = +0.002, SlotOverlaySpacing_Oak = −0.008, SlotScale 1.3, board world scale
                // TrayScale 0.57247 × BoardScale_Oak 0.92378 = 0.5288, card world width
                // CardWidth 0.0635 × SlotCardScale 1.45 × 1.3 × 0.5288 = 63.3 mm):
                //   placed  (spread)   → slot-local x = 0.002 ∓ 0.004, centre distance
                //                        (0.155 − 0.008·1.3)·0.5288 = 76.5 mm, visible edge gap
                //                        76.5 − 63.3 = 13.2 mm
                //   docked  (no spread)→ slot-local x = 0.002 for both, centre distance
                //                        0.155·0.5288 = 82.0 mm, visible edge gap 18.7 mm
                // Each card sat 2.75 mm off its own overlay and the pair sat 5.5 mm too wide — an
                // absolute error small enough to read as "nicht viel" while the GAP between the two
                // cards, which is what the eye actually measures, changed by 42 %. It flips on every
                // seam that swaps a placed pair for a docked one: the selection→action phase change,
                // and a character switch, where a watched character's played cards arrive through
                // this dock while our own lie in the slots from PlaceCard.
                //
                // REJECTED: adding a second dial for the dock. The whole point of the "Overlays"
                // element (ModBuild 106) is that ONE per-board number moves the glow and the card
                // that lands in it together; a second number is how the two drifted apart in the
                // first place. The spread-free PlayTray.SlotHomeOffset had no other caller and is
                // retired with this change so the trap cannot be re-armed.
                card.SetHome(slot, PlayTray.SlotHomeOffsetFor(i), Quaternion.identity,
                    PlayTray.SlotCardScale);
            }
            else
            {
                float x = n > 1 ? (i == 0 ? -0.75f : 0.75f) * w * layoutScale : 0f;
                card.SetHome(_root, new Vector3(x, 0f, 0f), Quaternion.identity, layoutScale);
            }
            // READ-ONLY DOCK: a watched character's cards are laid out but never armed — see the
            // ReadOnly property. DisarmCard is idempotent for a card that was never armed.
            if (ReadOnly)
                DisarmCard(card);
            else
                ArmCard(card);
        }

        LogDockSeats(n);
    }

    /// <summary>
    /// Change-gated seat dump for the docked pair — the measurement the 2026-08-11 X-axis report
    /// had to be argued without. Prints, in REAL millimetres at the board's live world scale, each
    /// docked card's slot-local X seat, the resulting CENTRE distance and the VISIBLE edge gap
    /// between the two cards; the last two are the numbers the eye actually judges, and the gap is
    /// the one that moved 42 % while the seats moved 2.75 mm.
    ///
    /// <para>Not per frame and not per dock: gated on a key built from the rounded seats and the
    /// card count, so it prints once per genuinely different layout (a dial edit, a board switch, a
    /// one-card round) and stays silent through the rebuild storm of a character switch. Developer-
    /// facing; it names the dials so the next log can be read without re-deriving anything.</para>
    /// </summary>
    private void LogDockSeats(int n)
    {
        Transform? s0 = DockSlot(0);
        Transform? s1 = DockSlot(1);
        if (n <= 0 || s0 == null || s1 == null)
            return;

        Vector3 o0 = PlayTray.SlotHomeOffsetFor(0);
        Vector3 o1 = PlayTray.SlotHomeOffsetFor(1);
        // Rounded to a tenth of a millimetre in slot-local metres: a stepper press changes this,
        // controller jitter cannot (the slot transforms are not read for the key).
        int key = n * 1_000_000
                  + (Mathf.RoundToInt(o0.x * 10_000f) & 0x7FF) * 2048
                  + (Mathf.RoundToInt(o1.x * 10_000f) & 0x7FF);
        if (_loggedSeatKey == key)
            return;
        _loggedSeatKey = key;

        float slotScale = s0.lossyScale.x;                       // board world scale × PlayTray.SlotScale
        float cardW = CardsConfig.CardWidth.Value * PlayTray.SlotCardScale * slotScale;
        float centres = Vector3.Distance(s0.TransformPoint(o0), s1.TransformPoint(o1));
        ControlBoard b = CardsConfig.CurrentBoard;
        Core.VRLog.Info("Cards",
            $"Dock seats [{b}]: {n} card(s) docked in the recesses at slot-local x " +
            $"{o0.x * 1000f:F1} / {o1.x * 1000f:F1} mm — the SAME [Cards] SlotOverlayOffset_{b} + " +
            $"SlotOverlaySpacing_{b} seat PlayTray.PlaceCard and both slot glows take " +
            $"(SlotHomeOffsetFor, spread applied per slot). Card {cardW * 1000f:F1} mm wide, " +
            $"centres {centres * 1000f:F1} mm apart, visible gap between them " +
            $"{(centres - cardW) * 1000f:F1} mm. If the two cards ever read unevenly spaced against " +
            "the pair that was PLACED there during selection, these three numbers differ between the " +
            "two phases and one of the writers dropped the spread again.");
    }

    /// <summary>Change gate for <see cref="LogDockSeats"/>; int.MinValue = never printed.</summary>
    private int _loggedSeatKey = int.MinValue;

    internal void ClearCards()
    {
        ClearLaserHighlight();
        for (int i = _cards.Count - 1; i >= 0; i--)
            DisarmCard(_cards[i]);
        _cards.Clear();
    }

    // ------------------------------------------------------------------ zones --

    private void ArmCard(VRCard card)
    {
        if (_zones.TryGetValue(card, out ZoneSet existing))
        {
            existing.Root.SetActive(true);
            RegisterCanvas(card, existing);
            return;
        }

        float w = CardsConfig.CardWidth.Value;
        float h = CardsConfig.CardHeight;

        var set = new ZoneSet { Root = new GameObject("HalfZones") };
        set.Root.transform.SetParent(card.transform, worldPositionStays: false);

        // No default-action chips (test #19): the game's own face widgets cover the
        // default "Attack 2"/"Move 2" options via the registered canvas below.
        set.Top = HalfZone.Create(set.Root.transform, card, CBaseCard.ActionType.TopAction,
            new Vector3(0f, h * ZoneCenterYFrac, 0f),
            new Vector2(w * ZoneWidthFrac, h * ZoneHeightFrac), this);
        set.Bottom = HalfZone.Create(set.Root.transform, card, CBaseCard.ActionType.BottomAction,
            new Vector3(0f, -h * ZoneCenterYFrac, 0f),
            new Vector2(w * ZoneWidthFrac, h * ZoneHeightFrac), this);
        Core.VRLayers.Apply(set.Root); // mod layer (zones poke via registries; no renderer)

        _zones[card] = set;
        RegisterCanvas(card, set);
    }

    private void RegisterCanvas(VRCard card, ZoneSet set)
    {
        if (set.RegisteredCanvas != null)
            return;
        Canvas? canvas = card.GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            UguiPokeSurfaces.Register(canvas);
            set.RegisteredCanvas = canvas;
        }
    }

    private void DisarmCard(VRCard card)
    {
        if (ReferenceEquals(card, _laserCard))
            ClearLaserHighlight(); // a card leaving the layout must not stay lit
        if (card == null || !_zones.TryGetValue(card, out ZoneSet set))
            return;
        if (set.RegisteredCanvas != null)
        {
            UguiPokeSurfaces.Unregister(set.RegisteredCanvas);
            set.RegisteredCanvas = null;
        }
        if (set.Root != null)
            set.Root.SetActive(false);
    }

    internal void DestroyZonesFor(VRCard card)
    {
        if (!_zones.TryGetValue(card, out ZoneSet set))
            return;
        if (set.RegisteredCanvas != null)
            UguiPokeSurfaces.Unregister(set.RegisteredCanvas);
        if (set.Root != null)
            UnityEngine.Object.Destroy(set.Root);
        _zones.Remove(card);
    }

    // ------------------------------------------------------------------ per frame --

    /// <summary>Mirror each half's playability from the game's own interactability query
    /// (drives whether a poke commits; ITEM 9 — no longer any visual dim).</summary>
    internal void Tick()
    {
        if (!IsVisible)
        {
            ClearLaserHighlight(); // layout hidden mid-hover (turn ended) - never stay lit
            return;
        }
        if (ReadOnly)
        {
            // Watched character: no half is playable, and the geometric laser resolve does not run
            // (it could not find the card anyway — the canvas is unregistered — but a hidden zone
            // left marked playable would be a latent commit path).
            for (int i = 0; i < _cards.Count; i++)
            {
                VRCard card = _cards[i];
                if (card == null || !_zones.TryGetValue(card, out ZoneSet roSet))
                    continue;
                roSet.Top.SetPlayable(false);
                roSet.Bottom.SetPlayable(false);
            }
            ClearLaserHighlight();
            return;
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard card = _cards[i];
            if (card == null || !_zones.TryGetValue(card, out ZoneSet set))
                continue;
            FullAbilityCard? full = card.FullCard;
            set.Top.SetPlayable(full != null && CardsGameApi.IsHalfPlayable(full, CBaseCard.ActionType.TopAction));
            set.Bottom.SetPlayable(full != null && CardsGameApi.IsHalfPlayable(full, CBaseCard.ActionType.BottomAction));
        }
        UpdateLaserHighlight();
    }

    // ------------------------------------------------- geometric laser half highlight --
    // See the class doc (LASER HALF HIGHLIGHT IS GEOMETRIC) for the root cause and the
    // hardware-log evidence. The state machine is edge-driven: enter/exit calls fire only
    // on a genuine change of (card, half, game-card identity), so the game's own hover
    // tweens/sounds run once per change - exactly like a vanilla mouse crossing a half.

    /// <summary>
    /// Should <paramref name="pusher"/> IGNORE this pointer event? True for mod LASER
    /// pointer events on a card that is docked in the visible half-selection layout -
    /// there the beam's geometry (this class) is the single writer of the half-hover
    /// state, and letting the per-graphic pusher also fire would re-introduce the exact
    /// raycast-leaf flapping this fix removes. Poke/mouse/touch events, cards outside
    /// the dock, and DEFAULT-action pushers (the "Attack 2"/"Move 2" chips are real
    /// sub-widgets with their own hover FX, which the geometric resolve cannot and
    /// should not replicate) all keep the vanilla pusher path.
    /// </summary>
    internal static bool SuppressPusherEvent(FullCardEventPusher pusher, UnityEngine.EventSystems.PointerEventData? eventData)
    {
        if (pusher == null || eventData == null
            || !Hands.Interact.UguiPointer.IsLaserPointerId(eventData.pointerId))
            return false;
        HalfSelection? self = s_active;
        if (self == null || !self.IsVisible)
            return false;
        if (pusher._eventType.HasFlag(FullCardEventPusher.EventType.Default))
            return false; // default-action chips keep their own hover FX
        FullAbilityCard? full = pusher.GetComponentInParent<FullAbilityCard>();
        if (full == null)
            return false;
        for (int i = 0; i < self._cards.Count; i++)
        {
            VRCard card = self._cards[i];
            if (card != null && ReferenceEquals(card.FullCard, full))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolve which docked card + half the beam is on this frame and drive the game's
    /// hover calls on the edges. "The beam is on this card" comes from
    /// <see cref="Hands.Interact.RayUguiDriver"/>'s arbitration (its winning canvas is
    /// the card's registered face canvas) - the SAME decision that delivers clicks, so
    /// occlusion by physics, the raised fan or a nearer panel is honoured for free. The
    /// half then comes from ray x card plane in CARD-LOCAL space against the same zone
    /// rects the poke volumes use.
    /// </summary>
    private void UpdateLaserHighlight()
    {
        VRCard? hitCard = null;
        bool hitTop = false;

        VRHand? dom = VRHands.Primary;
        if (dom != null && dom.RayUgui.HasHit)
        {
            Canvas? beamCanvas = dom.RayUgui.HoveredCanvas;
            if (beamCanvas != null)
            {
                for (int i = 0; i < _cards.Count; i++)
                {
                    VRCard card = _cards[i];
                    if (card == null || !_zones.TryGetValue(card, out ZoneSet set)
                        || !ReferenceEquals(set.RegisteredCanvas, beamCanvas))
                        continue;
                    if (TryResolveHalf(dom, card, out hitTop))
                        hitCard = card;
                    break; // canvas matched - half or no half, no other card can match
                }
            }
        }

        FullAbilityCard? hitFull = hitCard != null ? hitCard.FullCard : null;
        if (hitFull == null)
            hitCard = null; // face not adopted (rebuild frame) - nothing to highlight

        bool unchanged = ReferenceEquals(hitCard, _laserCard)
                         && (hitCard == null
                             || (hitTop == _laserTop && ReferenceEquals(hitFull, _laserFull)));
        if (unchanged)
            return;

        ClearLaserHighlight();
        if (hitCard == null || hitFull == null)
            return;
        try
        {
            // The exact FullCardEventPusher.OnPointerEnter dispatch for a Top/Bottom (non-
            // default) pusher, in its order: card highlight, half enter (the game's own
            // interactability guards apply inside), then the action button's hover state.
            hitFull.Highlight(active: true);
            hitFull.OnPointerEnter(hitTop);
            (hitTop ? hitFull.topActionButton : hitFull.bottomActionButton)
                ?.ToggleHover(active: true, isDefaultAbility: false);
            _laserCard = hitCard;
            _laserFull = hitFull;
            _laserTop = hitTop;
        }
        catch (Exception ex)
        {
            // Never let a game-side throw starve CardsDriver's tick (WorldUI gotcha:
            // an unguarded per-frame NRE kills every interaction after it).
            Core.VRLog.Error("Cards", $"Laser half-hover ENTER threw: {ex}");
            _laserCard = null;
            _laserFull = null;
        }
    }

    /// <summary>Send the exit half of the pusher dispatch for the currently lit half, if any.</summary>
    private void ClearLaserHighlight()
    {
        FullAbilityCard? full = _laserFull;
        bool top = _laserTop;
        _laserCard = null;
        _laserFull = null;
        if (full == null)
            return;
        try
        {
            full.Highlight(active: false);
            full.OnPointerExit(top);
            (top ? full.topActionButton : full.bottomActionButton)
                ?.ToggleHover(active: false, isDefaultAbility: false);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"Laser half-hover EXIT threw: {ex}");
        }
    }

    /// <summary>
    /// Which half rect does the dominant beam cross on <paramref name="card"/>?
    /// Ray x card plane, expressed in CARD-LOCAL units and tested against the SAME
    /// center/size fractions the poke zones are built from. The title band between the
    /// two rects highlights nothing - matching both the poke zones and the vanilla card,
    /// whose top/bottom buttons do not cover the middle band either.
    /// </summary>
    private static bool TryResolveHalf(VRHand hand, VRCard card, out bool top)
    {
        top = false;
        Transform t = card.transform;
        PickPose pick = hand.Ray.Current;
        float denom = Vector3.Dot(pick.Direction, t.forward);
        if (Mathf.Abs(denom) < 1e-6f)
            return false; // beam parallel to the card plane
        float dist = Vector3.Dot(t.position - pick.Origin, t.forward) / denom;
        if (dist <= 0f)
            return false; // card behind the hand
        Vector3 local = t.InverseTransformPoint(pick.Origin + pick.Direction * dist);

        float halfW = CardsConfig.CardWidth.Value * ZoneWidthFrac * 0.5f;
        if (Mathf.Abs(local.x) > halfW)
            return false;
        float h = CardsConfig.CardHeight;
        float centerY = h * ZoneCenterYFrac;
        float halfH = h * ZoneHeightFrac * 0.5f;
        if (Mathf.Abs(local.y - centerY) <= halfH)
        {
            top = true;
            return true;
        }
        return Mathf.Abs(local.y + centerY) <= halfH; // bottom zone (top stays false)
    }

    // ------------------------------------------------- multiplayer half-hover sample --
    // The LIT HALF, published for NetAvatarDriver's extras sender (extension record 14 — user
    // defect 2026-08-04: "Die Overlay-Auswahl auf Karten beim Hovern ist nicht synchronisiert").
    //
    // FED FROM THE GAME'S OWN HOVER CALLS, not from this class's laser state alone: BOTH pointer
    // paths end in FullAbilityCard.OnPointerEnter/OnPointerExit — the geometric laser resolve
    // above calls them directly, and the fingertip's uGUI pusher chain
    // (FullCardEventPusher.OnPointerEnter -> _target.OnPointerEnter) does too — so one postfix
    // pair (Patches.HalfHoverPatches) covers laser, fingertip and even a desktop mouse. The
    // registry stores the raw game card; the SLOT mapping happens at sample time against the
    // live docked list, so a stale entry (card left the layout mid-hover, missed exit) simply
    // stops matching and reads as "no hover".

    /// <summary>The game card whose half the pointer is on (per the game's own enter/exit calls);
    /// null = none. Unity-object field, so destruction reads as null at the sample site.</summary>
    private static FullAbilityCard? s_gameHoverFull;

    /// <summary>True = the lit half is the TOP action (valid while <see cref="s_gameHoverFull"/>).</summary>
    private static bool s_gameHoverTop;

    /// <summary>Record a <c>FullAbilityCard.OnPointerEnter/Exit</c> edge (Harmony postfix —
    /// <c>Patches.HalfHoverPatches</c>). Exits only clear a matching entry, so an out-of-order
    /// exit from a previous card cannot wipe a fresh hover.</summary>
    internal static void NoteGameHover(FullAbilityCard? full, bool top, bool active)
    {
        if (full == null)
            return;
        if (active)
        {
            s_gameHoverFull = full;
            s_gameHoverTop = top;
        }
        else if (ReferenceEquals(s_gameHoverFull, full) && s_gameHoverTop == top)
        {
            s_gameHoverFull = null;
        }
    }

    /// <summary>
    /// Which docked round card's half the local pointer is lighting RIGHT NOW: the board SLOT
    /// index (0 = left recess, 1 = right — <see cref="SetCards"/> docks card i into slot i, the
    /// same indices the wire's occupancy nibble uses) and the half. False while the
    /// action-selection layout is hidden or nothing is lit. A POSITION only — the card itself
    /// never leaves this method.
    /// </summary>
    internal static bool TrySampleLocalHover(out int slot, out bool top)
    {
        slot = 0;
        top = false;
        HalfSelection? self = s_active;
        FullAbilityCard? full = s_gameHoverFull;
        if (self == null || !self.IsVisible || self.ReadOnly || full == null)
            return false; // read-only dock: the slots hold a WATCHED character's cards, which are
                          // nothing to do with our own board that peers mirror (see ReadOnly)
        for (int i = 0; i < self._cards.Count; i++)
        {
            VRCard card = self._cards[i];
            if (card == null || !ReferenceEquals(card.FullCard, full))
                continue;
            slot = i;
            top = s_gameHoverTop;
            return true;
        }
        return false; // stale registry entry (card left the layout) — reads as "no hover"
    }

    /// <summary>
    /// The persistently SELECTED half of each docked round card (follow-up defect 2026-08-04:
    /// "Ich will auch sehen, welche Hälfte der Mitspieler GEKLICKT hat — die wird dauerhaft
    /// hervorgehoben, und auch das Abwählen muss sichtbar sein"): per board slot,
    /// <see cref="Net.NetProtocol.HalfSelectNone"/> / <c>…Top</c> / <c>…Bottom</c>.
    ///
    /// READ FROM THE GAME'S OWN LATCH, per half: <c>FullAbilityCardAction.isSelected</c> (set by
    /// <c>ToggleSelect</c>, which is what <c>CardsActionControlller</c> drives on click and on
    /// undo — the exact latch <c>RefreshHighlight</c> turns into the STEADY
    /// <c>CardActionHighlight.ShowSelected</c> overlay) plus its <c>isSelectedDefaultAction</c>
    /// twin (clicking the default "Attack 2"/"Move 2" chip selects the half the same way and
    /// lights the same region). Polling the widget latch rather than the controller's phase
    /// fields means click, undo, extra-turn re-init and every other path the game itself uses
    /// to change the highlight are covered by construction. Zeros while the action-selection
    /// layout is hidden — a selection nobody's board displays must not ride the wire.
    /// </summary>
    internal static void SampleLocalSelection(out int sel0, out int sel1)
    {
        sel0 = Net.NetProtocol.HalfSelectNone;
        sel1 = Net.NetProtocol.HalfSelectNone;
        HalfSelection? self = s_active;
        if (self == null || !self.IsVisible || self.ReadOnly)
            return; // read-only dock: publishing a WATCHED character's selected halves would light
                    // the wrong half on our OWN mirrored board on every peer (see ReadOnly)
        for (int i = 0; i < self._cards.Count && i < 2; i++)
        {
            int sel = SelectedHalfOf(self._cards[i]);
            if (i == 0) sel0 = sel;
            else sel1 = sel;
        }
    }

    /// <summary>One card's selected half off the game's own per-half latches (guarded — a card
    /// mid-teardown reads as none, never throws inside the extras sender).</summary>
    private static int SelectedHalfOf(VRCard? card)
    {
        try
        {
            FullAbilityCard? full = card != null ? card.FullCard : null;
            if (full == null)
                return Net.NetProtocol.HalfSelectNone;
            FullAbilityCardAction? top = full.topActionButton;
            if (top != null && (top.isSelected || top.isSelectedDefaultAction))
                return Net.NetProtocol.HalfSelectTop;
            FullAbilityCardAction? bottom = full.bottomActionButton;
            if (bottom != null && (bottom.isSelected || bottom.isSelectedDefaultAction))
                return Net.NetProtocol.HalfSelectBottom;
        }
        catch
        {
            // fall through — none
        }
        return Net.NetProtocol.HalfSelectNone;
    }

    internal void RequestPlay(VRCard card, CBaseCard.ActionType type)
    {
        if (ReadOnly)
        {
            // FOURTH FUNNEL (see the ReadOnly property). Nothing should be able to get here — the
            // zones are inactive and unplayable, and the canvas is unregistered — so a hit is a
            // regression worth a line, never a silent drop.
            Core.VRLog.Warn("Cards", "[Focus] half-play REFUSED on a read-only dock: the board is " +
                                     "showing a character the player does not control. No game call " +
                                     "was made (CardsGameApi.PlayHalf was never reached).");
            return;
        }
        try
        {
            PlayRequested?.Invoke(card, type);
        }
        catch (Exception ex)
        {
            Core.VRLog.Error("Cards", $"PlayRequested subscriber threw: {ex}");
        }
    }

    /// <summary>
    /// One pokeable half zone: a fingertip trigger volume only, INVISIBLE (ITEM 9 — the
    /// game already draws its own mouse-over/selection highlight on the card, so the mod
    /// no longer paints any backing quad; the earlier per-half "invalid" dim read as a
    /// black semi-transparent sheet over the cards). Validity is still tracked so an
    /// invalid half's poke is a no-op, but it is never visualised.
    /// </summary>
    private sealed class HalfZone : PokeableBehaviour
    {
        private VRCard _card = null!;
        private CBaseCard.ActionType _type;
        private HalfSelection _owner = null!;
        private bool _playable;

        internal static HalfZone Create(Transform parent, VRCard card, CBaseCard.ActionType type,
            Vector3 localPos, Vector2 size, HalfSelection owner)
        {
            var go = new GameObject($"Zone_{type}");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;

            // Poke selection volume only — no renderer (ITEM 9): the game's own on-card
            // highlight is the sole hover/selection feedback.
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 0.012f);
            box.isTrigger = true;

            var zone = go.AddComponent<HalfZone>();
            zone._card = card;
            zone._type = type;
            zone._owner = owner;
            return zone;
        }

        /// <summary>
        /// Track whether this half may be committed. No visual (ITEM 9) — an invalid
        /// half simply refuses the poke below.
        /// </summary>
        internal void SetPlayable(bool playable) => _playable = playable;

        /// <summary>
        /// Poke hover feedback (test #20): a haptic tick only — no zone tint. The
        /// card canvas is registered with <c>UguiPokeSurfaces</c> in this layout, so
        /// the fingertip already drives the game's own uGUI hover highlight on the
        /// card face; the zone adds nothing visual.
        /// </summary>
        public override void OnPokeEnter(VRHand hand)
        {
            if (_playable)
                hand.SendHaptic(HapticPreset.HoverTick);
        }

        public override void OnPoke(VRHand hand)
        {
            if (!_playable)
                return;
            hand.SendHaptic(HapticPreset.ClickPulse);
            _owner.RequestPlay(_card, _type);
        }
    }
}

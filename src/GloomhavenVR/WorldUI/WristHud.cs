using System.Text;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Compact character status on the non-dominant wrist (ROADMAP P3c #5): HP, XP,
/// gold, level and condition counts, look-at activated. Since 2026-08-09 it is a PALM
/// plate — you read it by turning your palm up, not by glancing at your knuckles (user:
/// "ich will nun, dass der Arm-HUD zu sehen ist wenn man die Handflächen anschaut"); see
/// the measured wrist frame in <see cref="Build"/>.
/// Test #13: topped by a PROMINENT character identity row — class portrait + name
/// of the character the player is currently acting for ("whose cards am I picking").
///
/// Data sources (verified via ilspycmd, ScenarioRuleLibrary.dll — the same values
/// NewPartyDisplayUI/ActorStatPanel render): <c>CActor.Health / MaxHealth
/// (public int)</c>, <c>public int XP =&gt; m_XP;</c>, <c>public int Gold =&gt; m_Gold;</c>,
/// <c>CActor.Level</c>, <c>public CTokens Tokens</c> with
/// <c>GetAllPositiveConditions()/GetAllNegativeConditions()</c>.
///
/// Actor resolution (test #13): during <see cref="VRMode.CardSelection"/> the hand
/// the game presents wins — <c>CardsHandManager.Instance.CurrentHand/GetActiveHand()</c>
/// (CardsHandManager.cs:127/137/595, via the Cards module's read-only
/// <c>CardsGameApi.ActiveHand()</c>) → <c>CardsHandUI.PlayerActor</c>
/// (CardsHandUI.cs:208) — that is the tab-switchable multi-merc hand whose cards are
/// being selected. Otherwise <c>Choreographer.CurrentPlayerActor</c> (own turn,
/// Choreographer.cs:474) falling back to
/// <c>InitiativeTrack.Instance.SelectedActor().Actor</c> (the pattern UndoButton
/// itself uses). Class portrait: <c>UIInfoTools.Instance (UIInfoTools.cs:465)
/// .GetNewAdventureCharacterPortrait(ECharacter, …)</c> (UIInfoTools.cs:773) with
/// <c>CCharacterClass.CharacterModel =&gt; m_CharacterYML.Model</c>
/// (CCharacterClass.cs:234) — the exact call the game's own card-selection preview
/// uses (CardsHandManager.ShowPreview, CardsHandManager.cs:776); when the sprite is
/// unavailable the name stays as gold text without an icon. The 2D party HUD
/// canvases are deeply embedded in <c>NewPartyDisplayUI</c> layout groups, so a
/// minimal TMP panel with live values is built instead of converting them
/// (documented decision — reuse was evaluated).
///
/// Refresh cadence: the panel rebuilds at 4 Hz (StringBuilder, only assigned on
/// change) and IMMEDIATELY on turn/selection changes
/// (<see cref="VREvents.ChoreographerMessage"/>,
/// <see cref="VREvents.CardSelectionChanged"/>, mode changes); the per-frame path
/// only does the look-at test and alpha fade — allocation-free.
/// </summary>
internal sealed class WristHud
{
    // Look-at hysteresis on the panel's OWN normal (see the gate in Tick). Lowered in an
    // earlier round because VISIBILITY is the #1 requirement: the readable face only needs
    // to be roughly toward the HMD for the glance to register.
    private const float ShowDot = 0.35f;
    private const float HideDot = 0.2f;
    private const float RefreshInterval = 0.25f;

    /// <summary>
    /// Sub-step lift above the farthest-still-behind panel's ladder slot (Test 3, user
    /// 2026-08-09: "Auch das Arm-HUD soll sich mit allen anderen Dingen im Spiel an
    /// Perspektive halten"). Same value and the same reasoning as
    /// <c>Cards.CardCueOrder.CuePanelLift</c> and <c>Net.BoardVisual.TagPanelLift</c>: it must
    /// stay under <c>CanvasConversion.PanelOrderStep</c> (16) so the HUD can never climb into
    /// the NEXT panel's slot, and 12 clears that window's own decorations too (close X +2,
    /// grab bar +4, menu-laid tooltip +10) — a wrist HUD genuinely in front of a window covers
    /// the whole window, its furniture included.
    /// </summary>
    private const int PanelLift = 12;

    private readonly StringBuilder _sb = new(256);

    private GameObject? _root;
    private Canvas? _canvas;
    private CanvasGroup? _group;
    private TextMeshProUGUI? _text;
    private VRHand? _hand;
    private bool _shown;
    private float _nextRefresh;
    private string _lastText = string.Empty;

    // Test #13 identity row.
    private GameObject? _portraitGo;
    private UnityEngine.UI.Image? _portrait;
    private TextMeshProUGUI? _nameText;
    private CPlayerActor? _identityActor;
    private string _lastIdentity = string.Empty;
    private bool _eventsAttached;

    /// <summary>Last ladder order written to <see cref="_canvas"/> (change-gate).
    /// <c>int.MinValue</c> = never written, so the first shown tick always seats the plate —
    /// a one-frame gap at order 0 is the defect band itself.</summary>
    private int _appliedOrder = int.MinValue;

    // ---- Item 10 + per-style rework: live-tunable pose (the "Wrist" debug category) --------
    // The watch-face pose — its OFFSET from the wrist anchor and its TILT (pitch/yaw/roll on
    // top of the palm base) — is re-read and re-applied every Tick (ApplyPose), so nudging a
    // stepper in the VR settings menu moves the HUD immediately.
    //
    // PER HAND STYLE (2026-07 request B): the HUD rests over the hand MESH, whose shape differs
    // per style (Glove/Plate/Arcane), so the persistent home of the pose is the PER-STYLE
    // [WristHud] section of dev.gloomhavenvr.hands.cfg (HandsConfig.StyleWrist*). The accessors
    // below read/write the ACTIVE style ([Hands] HandStyle), so the steppers edit the style
    // currently worn and a style switch re-poses the HUD on the very next Tick. The HUD's
    // on/off toggle ([WorldUI] WristHud) is global.
    //
    // ONE DIAL, ONE OWNER (user 2026-08-09: "Der X-Offset beim Arm-HUD hat keinen Einfluss,
    // alle anderen Werte und Offsets funktionieren"). This class used to accept its pose from
    // TWO places: the per-style [WristHud] entries above AND a second, older set of six global
    // [WorldUI] WristHud* entries assigned into static ConfigEntry fields here. The per-style
    // array always wins (it is bound during HandsConfig.Bind, which runs long before the first
    // Tick), so those six were unreachable — six dials in the settings menu, each carrying the
    // SAME localized caption as the live one ("Arm-HUD: X (m)"), every one of them dead. That
    // is the exact disease the reparented cluster dial and the consumer-less wire field were:
    // a control the player can move with nothing to show for it. They are gone from here and
    // marked LEGACY at their bind site (WorldUIConfig), which drops them from the menu.
    //
    // (The X dial's OWN failure was a STEP, not an owner, and the two are worth telling apart —
    // see the block above the arrays in HandsConfig: the key ended in "X", so ConfigSteps' unit
    // rule never saw the word "Offset" and the step fell to a fiftieth of the shipped default's
    // magnitude. X had the smallest default of the three, so it moved the HUD by a twentieth of
    // a millimetre per press while Y moved it by one.)
    //
    // The statics below are the last-resort in-session fallback for the window before
    // HandsConfig.Bind has run (a hot reload, a config file that failed to open). They carry
    // the shipped palm pose so that window looks like the shipped one rather than like origin.
    private static float _pitch, _yaw, _roll;
    private static float _offX = Defaults.GlovePalmSideOffset,
                         _offY = Defaults.GlovePalmFingerOffset,
                         _offZ = Defaults.GlovePalmLiftOffset;

    /// <summary>The ACTIVE style's element of a per-style pose array, else <paramref name="fallback"/>.</summary>
    private static float StyleGet(ConfigEntry<float>[]? styled, float fallback)
    {
        try
        {
            return styled != null ? styled[HandsConfig.ActiveStyleIndex].Value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Write the ACTIVE style's element of a per-style pose array. False = not bound yet.</summary>
    private static bool StyleSet(ConfigEntry<float>[]? styled, float value)
    {
        try
        {
            if (styled != null)
            {
                styled[HandsConfig.ActiveStyleIndex].Value = value; // BepInEx persists on set
                return true;
            }
        }
        catch
        {
            // fall through to the legacy path
        }
        return false;
    }

    internal static float PitchDeg
    {
        get => StyleGet(HandsConfig.StyleWristPitch, _pitch);
        set { if (!StyleSet(HandsConfig.StyleWristPitch, value)) _pitch = value; }
    }
    internal static float YawDeg
    {
        get => StyleGet(HandsConfig.StyleWristYaw, _yaw);
        set { if (!StyleSet(HandsConfig.StyleWristYaw, value)) _yaw = value; }
    }
    internal static float RollDeg
    {
        get => StyleGet(HandsConfig.StyleWristRoll, _roll);
        set { if (!StyleSet(HandsConfig.StyleWristRoll, value)) _roll = value; }
    }
    internal static float OffsetX
    {
        get => StyleGet(HandsConfig.StyleWristOffsetX, _offX);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetX, value)) _offX = value; }
    }
    internal static float OffsetY
    {
        get => StyleGet(HandsConfig.StyleWristOffsetY, _offY);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetY, value)) _offY = value; }
    }
    internal static float OffsetZ
    {
        get => StyleGet(HandsConfig.StyleWristOffsetZ, _offZ);
        set { if (!StyleSet(HandsConfig.StyleWristOffsetZ, value)) _offZ = value; }
    }

    /// <summary>
    /// THE PALM BASE — a half turn about the wrist's own +Y (the finger axis). See Build's
    /// rotation block for the measured derivation; the live pitch/yaw/roll compose in the WRIST
    /// frame BEFORE it (see <see cref="ApplyPose"/>), so a trim of 0/0/0 IS the shipped orientation
    /// and a trim tuned against the previous base still means what it meant.
    ///
    /// <para>Half a turn and not the identity because a uGUI canvas is READ FROM ITS -Z SIDE: a
    /// canvas with identity rotation is the one Unity's default camera — parked at negative z,
    /// looking along +z — renders right way round, so <c>transform.forward</c> points AWAY from
    /// the reader. Identity here therefore aimed the readable face out of the BACK of the hand
    /// while the look-at gate revealed the plate from the PALM side: the player was shown the
    /// canvas's back face, which uGUI's Cull-Off shader draws mirror-reversed (user 2026-08-09:
    /// "Weiterhin sehe ich das HUD jetzt spiegelverkehrt!"). Rotating 180° about +Y turns the
    /// readable -Z face out of the palm and keeps the text top on the fingers.</para>
    /// </summary>
    private static readonly Quaternion PalmFlat = Quaternion.Euler(0f, 180f, 0f);

    /// <summary>
    /// The plate's READABLE face in world space — the side the text can be read from, which is the
    /// canvas's -Z (see <see cref="PalmFlat"/>). Single source for the look-at gate so the gate and
    /// the base rotation can never drift apart again, whatever trims are dialled in on top.
    /// </summary>
    private Vector3 ReadableFace => _root != null ? -_root.transform.forward : Vector3.forward;

    /// <summary>
    /// Item 10: re-apply the wrist HUD pose from the live-tunable offset + tilt. Called once in
    /// Build and every Tick, so the "Wrist" steppers move the watch face immediately.
    /// </summary>
    /// <remarks>
    /// TRIM FIRST, THEN THE HALF TURN — the order is load-bearing, not style.
    ///
    /// <para>Composed the other way round (base × trim, as the un-mirrored round shipped it) the
    /// trims would rotate in the PLATE's own frame, on top of a base that has just turned 180°.
    /// Every angle a player had already dialled in would then mean its own opposite: a pitch that
    /// tipped the plate toward the forearm now tips it toward the fingers, i.e. it moves by TWICE
    /// the trim. The user's tuned 32° would have swung the face by 64°.</para>
    ///
    /// <para>Trim × base instead applies the trims in the WRIST frame and turns the plate over
    /// afterwards. Algebraically it is R_old·Ry(180), which keeps the plate's PLANE and its text-up
    /// exactly where they were and flips only which face points at the reader — precisely "mach es
    /// richtig rum", with nothing else moving. That is why the palm flip needs no key rename and no
    /// migration marker, unlike the turn-around before it: every saved value survives it meaning
    /// the same thing. It also reads better on the dials — pitch tips about the across-hand axis,
    /// yaw spins about the finger axis, roll rolls about the palm normal, none of them dependent on
    /// what the other two are set to.</para>
    /// </remarks>
    private void ApplyPose()
    {
        if (_root == null)
            return;
        _root.transform.localPosition = new Vector3(OffsetX, OffsetY, OffsetZ);
        _root.transform.localRotation = Quaternion.Euler(PitchDeg, YawDeg, RollDeg) * PalmFlat;
    }

    public void Tick()
    {
        EnsureEvents();
        bool want = WorldUIConfig.WristHud.Value && WorldUIConfig.ConversionActive
                    && Choreographer.s_Choreographer != null;

        VRHand? hand = NonDominantHand();
        if (!want || hand == null || !hand.HasPose)
        {
            if (_root != null && _root.activeSelf)
                _root.SetActive(false);
            return;
        }

        if (_root == null || !ReferenceEquals(hand, _hand))
            Build(hand);
        if (_root == null)
            return;

        if (!_root.activeSelf)
            _root.SetActive(true);

        // Item 10: re-read + re-apply the tunable pose every tick so the "Wrist" debug
        // steppers move the HUD live (allocation-free — a Vector3 + two quaternions).
        ApplyPose();

        // Look-at gate: the HUD is a flat plate lying in the PALM plane, readable from the
        // palm side (user 2026-08-09: "ich will nun, dass der Arm-HUD zu sehen ist wenn man
        // die Handflächen anschaut"). It is visible while its readable face turns toward the
        // HMD — i.e. when you turn your palm up to read it.
        //
        // GATE AXIS IS THE PANEL'S OWN READABLE FACE, not a rig axis. Every previous round wrote
        // the gate as a hand-picked rig axis that had to be kept in agreement with the base
        // rotation BY HAND, and the file's own history is three rounds of that agreement
        // breaking (normal on +Y with the gate on +Z, then the reverse). ReadableFace IS the
        // side the text can be read from by construction: it follows the base AND the per-style
        // yaw/pitch/roll trims, so no future re-aim can desynchronize the two again. Costs the
        // same one matrix read the old `hand.Rig.Root.up` did.
        //
        // It is -forward, not +forward: the last round gated on +forward, which is the canvas's
        // BACK, so the plate faded in exactly when the player was positioned to see it mirrored.
        // A gate on the readable face cannot express that state at all.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null && _group != null)
        {
            Vector3 toHead = (head.transform.position - _root.transform.position).normalized;
            float dot = Vector3.Dot(ReadableFace, toHead);
            if (!_shown && dot > ShowDot) _shown = true;
            else if (_shown && dot < HideDot) _shown = false;

            float target = _shown ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * 6f);

            // ---- PERSPECTIVE (user 2026-08-09) ----------------------------------------------
            // "Auch das Arm-HUD soll sich mit allen anderen Dingen im Spiel an Perspektive
            // halten, aktuell kann ich die Healthbars hindurch sehen die dahinter sind und ich
            // kann die Menü-Fenster hindurch sehen die auch dahinter sind."
            //
            // ROOT CAUSE, and it is the mod's standing one for depth-less transparents: this
            // canvas shipped at the default sortingOrder 0 while every converted panel — the
            // health bars (ActorBars converts each bar host, ActorBars.cs BarHostSortingOrder)
            // and every menu window — rides the distance ladder at >= PanelOrderBase (100).
            // Unity sorts transparents by sortingLayer -> sortingOrder -> renderQueue ->
            // distance, so order beat distance and a panel SEVEN METRES AWAY painted last, over
            // a HUD an arm's length from the eye. Nothing here writes depth (a ZWrite on an
            // alpha-blended plate stamps its bounding RECTANGLE — the hard-edged hole
            // CanvasConversion.8.Order.cs exists to have removed), so depth cannot arbitrate.
            //
            // THE FIX IS THE LADDER, NOT A BIGGER NUMBER. OrderAboveDistance ranks this plate by
            // its MEASURED eye distance among the panels, exactly as the board tooltip
            // (WorldTooltips), the avatar identity tags (Net.BoardVisual) and the card cue art
            // (Cards.CardCueOrder, ModBuild 94) already do. A menu genuinely IN FRONT of the
            // wrist still covers it; one behind it no longer shows through. That is the user's
            // sentence — consistency — rather than "the HUD wins".
            //
            // Reads the PREVIOUS frame's ladder (TickPanelOrder runs last in the WorldUI
            // LateUpdate chain): a one-frame lag on a hysteresis-damped ladder is not
            // observable, the same trade every other OrderAboveDistance caller accepts.
            //
            // COST (there is a live performance budget): one Vector3.Distance, one walk over the
            // ~30 listed panels and a CHANGE-GATED int write — and only while the plate is
            // actually on screen. A hidden HUD does none of it.
            if (_canvas != null && (_shown || _group.alpha > 0.001f))
            {
                float eyeDistance = Vector3.Distance(head.transform.position, _root.transform.position);
                int order = CanvasConversion.OrderAboveDistance(eyeDistance, PanelLift);
                if (order != _appliedOrder)
                {
                    _appliedOrder = order;
                    _canvas.sortingOrder = order;
                }
            }
        }

        if (_shown && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshText();
        }
    }

    public void Shutdown()
    {
        DetachEvents();
        DestroyPanel();
    }

    private void DestroyPanel()
    {
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _canvas = null;
        _appliedOrder = int.MinValue;
        _hand = null;
        _portraitGo = null;
        _portrait = null;
        _nameText = null;
        _identityActor = null;
        _lastIdentity = string.Empty;
    }

    // ---- live update events (test #13) ---------------------------------------------------

    private void EnsureEvents()
    {
        if (_eventsAttached)
            return;
        _eventsAttached = true;
        VREvents.ChoreographerMessage += OnGameFlowChanged;
        VREvents.CardSelectionChanged += OnCardSelectionChanged;
        VRModeStateMachine.ModeChanged += OnModeChanged;
        Core.Loc.OnChanged += OnLanguageChanged; // live language following
    }

    private void DetachEvents()
    {
        if (!_eventsAttached)
            return;
        _eventsAttached = false;
        VREvents.ChoreographerMessage -= OnGameFlowChanged;
        VREvents.CardSelectionChanged -= OnCardSelectionChanged;
        VRModeStateMachine.ModeChanged -= OnModeChanged;
        Core.Loc.OnChanged -= OnLanguageChanged;
    }

    // Turn/round messages, card (de)selection and mode flips all potentially change
    // whose character the HUD shows — force the next shown tick to refresh at once.
    private void OnGameFlowChanged(ChoreoMessageEvent e) => _nextRefresh = 0f;
    private void OnCardSelectionChanged(CardSelectionEvent e) => _nextRefresh = 0f;
    private void OnModeChanged(VRModeChange e) => _nextRefresh = 0f;
    // Language change: the change-gated strings differ once localized, so a forced refresh re-reads them.
    private void OnLanguageChanged() => _nextRefresh = 0f;

    // ---- construction ------------------------------------------------------------------

    private static VRHand? NonDominantHand()
    {
        VRHand? primary = VRHands.Primary;
        if (primary == null)
            return VRHands.Left ?? VRHands.Right;
        return VRHands.Get(primary.Side == HandSide.Left ? HandSide.Right : HandSide.Left);
    }

    private void Build(VRHand hand)
    {
        DestroyPanel();
        _hand = hand;

        _root = new GameObject("GloomhavenVR.WristHud");
        _root.layer = 5; // UI
        Transform wrist = hand.Rig.Wrist;
        _root.transform.SetParent(wrist, worldPositionStays: false);
        // ============================ THE PALM PLATE (2026-08-09) ============================
        // USER REQUEST, verbatim: "Bitte drehe einmal den Arm-HUD um 180 Grad - ich will nun,
        // dass der Arm-HUD zu sehen ist wenn man die Handflächen anschaut, nicht die Oberseite
        // der Hand wie es aktuell der Fall ist."
        //
        // THE FRAME THIS PARENTS INTO — MEASURED, NOT ASSUMED. Every round before the previous
        // one reasoned in the frame documented for HandRig.ROOT ("+Z along the fingers, +Y out
        // of the BACK of the hand") and silently applied it to HandRig.WRIST, which is a
        // DIFFERENT transform: the parent here is the prefabs' `Anchor_Wrist` itself
        // (HandVisuals.BindRig), and its chain to the prefab root nets out to a +90° rotation
        // about X (Model +90 · rig bone -90 · anchor +90). Re-read straight out of the prefab
        // YAML for this round, for all six shipped hands (VRHand_L/R, VRHandPlate_L/R,
        // VRHandArcane_L/R — every one identical, and identical between LEFT and RIGHT: only the
        // anchor's POSITION mirrors, its axes do not, and no hand carries a negative scale), the
        // wrist frame is
        //     wrist +X -> root +X   lateral across the hand   (shared by both hands)
        //     wrist +Y -> root +Z   ALONG THE FINGERS
        //     wrist +Z -> root -Y   OUT OF THE PALM
        // The prefabs corroborate it three times in their own data: Anchor_Middle_Root sits at
        // wrist-local (0.010, 0.096, -0.017), i.e. nearly 10 cm straight up +Y to the knuckle;
        // Anchor_Palm at (0.008, 0.049, 0.003), the palm centre half way up that line; and
        // Anchor_Palm's OWN rotation (0, .7071, .7071, 0) — a half turn about (0,1,1) — maps its
        // local +Y, which the Hands README requires to point OUT OF THE PALM, onto wrist +Z.
        //
        // A uGUI CANVAS IS READ FROM ITS -Z SIDE, which is where the previous round went wrong.
        // It took the "readable +Z" note of commit 3cc7ac8 at face value; that commit measured a
        // plate whose +Z pointed along the FINGERS (it reasoned in the root frame too), so its
        // screenshot could not tell the two sides apart. The rest of this repo says -Z in two
        // independent places (PanelPlacement.Facing "+Z away from viewer", VRCard "viewer on the
        // -Z side"), Unity's own default setup says it (identity canvas, camera at -z), and so do
        // the user's hand-tuned trims from the back-of-hand era: LookRotation(up, forward) with
        // pitch -102°/yaw -180° composes to within 12° of the IDENTITY, i.e. that plate's +Z
        // pointed out of the palm — away from a player reading it off the back of their hand.
        //
        // SO THE BASE IS A HALF TURN ABOUT +Y (PalmFlat), which lands
        //   canvas -Z (READABLE FACE)  -> wrist +Z = out of the PALM, toward the player looking
        //                                 at their own palm.
        //   canvas +Y (text top)       -> wrist +Y = toward the FINGERS, so the stats read
        //                                 upright when you raise your palm — the same
        //                                 "12-o'clock points up your hand" convention the
        //                                 back-of-hand version had.
        //   canvas +X (text right)     -> wrist -X
        // The plate spans wrist X/Y = the PALM plane, its normal is the palm normal. A half turn
        // is a proper rotation (det +1), so THE TEXT IS NEVER MIRRORED, and because the wrist
        // frame is anatomically identical on both hands this needs NO per-hand sign flip — unlike
        // the seat roll/yaw and the pinky counter-abduction, which mirror because they are stated
        // in the CONTROLLER's frame. Left wrist and right wrist get the same, correct plate.
        //
        // The per-style pitch/yaw/roll keep meaning exactly what they meant, because they are
        // applied in the WRIST frame BEFORE this half turn (ApplyPose's remarks give the algebra):
        // a pose tuned against the identity base keeps its plane and its text-up and only turns
        // its readable face around. The pre-turn-around trims could not be carried
        // over: they were the correction for a base that no longer exists, so their KEYS were
        // renamed ({Style}PalmPitch etc., HandsConfig) — a saved -180° yaw silently surviving
        // into the new base would have put the plate straight back on the knuckles, which is
        // precisely the "two rotations that cancel" trap.
        //
        // POSITION, same frame: the shipped offsets put the plate over the inner wrist, a couple
        // of centimetres clear of the mesh on the palm side (Defaults.GlovePalmOffset*). It is
        // driven live by ApplyPose, so the "Wrist" rows nudge it in real time.
        ApplyPose();

        var canvas = _root.AddComponent<Canvas>();
        _canvas = canvas;
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        // Seated on the distance ladder from the first shown tick (see Tick's PERSPECTIVE
        // block). PanelOrderBase is where the panels start, so a plate left at the Unity
        // default 0 is under every one of them; this is only the pre-measurement seat.
        canvas.sortingOrder = 0;
        _appliedOrder = int.MinValue;
        var rect = (RectTransform)_root.transform;
        rect.sizeDelta = new Vector2(240f, 196f); // +46 px identity row (test #13)
        rect.localScale = Vector3.one * 0.0004f; // 0.4 mm/px → 9.6 × 7.8 cm watch face

        _group = _root.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var bg = new GameObject("Background");
        bg.layer = 5;
        bg.transform.SetParent(_root.transform, worldPositionStays: false);
        var bgImage = bg.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.08f, 0.82f);
        MrBacking.Opacify(bgImage); // 0.82 backdrop lets the room shimmer through in MR
        var bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        // Identity row (test #13): class portrait + prominent name at the top.
        _portraitGo = new GameObject("Portrait");
        _portraitGo.layer = 5;
        _portraitGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _portrait = _portraitGo.AddComponent<UnityEngine.UI.Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget = false;
        var portraitRect = (RectTransform)_portraitGo.transform;
        portraitRect.anchorMin = new Vector2(0f, 1f);
        portraitRect.anchorMax = new Vector2(0f, 1f);
        portraitRect.pivot = new Vector2(0f, 1f);
        portraitRect.anchoredPosition = new Vector2(8f, -8f);
        portraitRect.sizeDelta = new Vector2(44f, 44f);
        _portraitGo.SetActive(false); // enabled once a sprite resolves

        var nameGo = new GameObject("Name");
        nameGo.layer = 5;
        nameGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _nameText = nameGo.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 21f;
        _nameText.alignment = TextAlignmentOptions.MidlineLeft;
        _nameText.richText = true;
        _nameText.enableWordWrapping = false;
        _nameText.overflowMode = TextOverflowModes.Ellipsis;
        var nameRect = (RectTransform)nameGo.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.offsetMin = new Vector2(60f, -52f);
        nameRect.offsetMax = new Vector2(-8f, -8f);
        WorldUIAssets.TryAssignGameFont(_nameText);

        var textGo = new GameObject("Text");
        textGo.layer = 5;
        textGo.transform.SetParent(_root.transform, worldPositionStays: false);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        _text.fontSize = 24f;
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.richText = true;
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 6f);
        textRect.offsetMax = new Vector2(-8f, -58f);
        WorldUIAssets.TryAssignGameFont(_text);

        _lastText = string.Empty;
        _lastIdentity = string.Empty;
        _identityActor = null;
        _nextRefresh = 0f;
        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        VRLayers.Apply(_root);
        // The resolved pose goes in the log so a "it sits wrong" report can be read against the
        // numbers that produced it — which style's row was live, and what it held.
        // The resolved pose AND the direction the text can actually be read from go in the log: a
        // "it sits wrong / it is mirrored" report is then answerable from the numbers that
        // produced it. readable·palmOut ≈ +1 is the correct plate; ≈ -1 is the mirrored one.
        Vector3 palmOut = wrist.TransformDirection(Vector3.forward); // wrist +Z = out of the palm
        VRLog.Info("WorldUI", $"WristHud built on {hand.Side} wrist (palm plate, style " +
                              $"{(HandStyle)HandsConfig.ActiveStyleIndex}): offset " +
                              $"{OffsetX * 1000f:0}/{OffsetY * 1000f:0}/{OffsetZ * 1000f:0} mm " +
                              $"in the wrist frame (+X across, +Y to the fingers, +Z out of the " +
                              $"palm), trim {PitchDeg:0.#}/{YawDeg:0.#}/{RollDeg:0.#}°, " +
                              $"readable·palmOut {Vector3.Dot(ReadableFace, palmOut):0.00} " +
                              $"(+1 = readable from the palm side, -1 = mirrored).");
    }

    // ---- data --------------------------------------------------------------------------

    // ---- stale-value diagnostics (user bug: "wrist info doesn't update on gold/XP") ------
    private CPlayerActor? _lastValueActor;
    private int _lastHp, _lastMaxHp, _lastXp, _lastGold, _lastLevel;
    private float _lastValueLog;

    private void RefreshText()
    {
        if (_text == null)
            return;

        // STALE-VALUE FIX: the flow-facing resolution (choreographer message actor / hand
        // actor / initiative-track actor) can hand back a SNAPSHOT clone (CActor.Clone is
        // MemberwiseClone; messages and UI caches carry actor objects). A clone's
        // m_Gold/m_XP are frozen at capture time, so the 4 Hz poll re-read the same numbers
        // forever. The RULES mutate the live instances in ScenarioManager.Scenario
        // .PlayerActors (LootTile → AddGold, GainXP — CActor.cs:1813/2011), so the resolved
        // IDENTITY is re-mapped onto the live scenario actor by ActorGuid before any stat
        // is read. liveRemap=True in the log line below is the hardware proof that a stale
        // instance was actually being displayed.
        CPlayerActor? resolved = ResolveActor();
        CPlayerActor? actor = ToLiveActor(resolved);
        bool liveRemap = !ReferenceEquals(resolved, actor);
        RefreshIdentity(actor);

        if (actor != null)
        {
            int hp = actor.Health, maxHp = actor.MaxHealth, xp = actor.XP,
                gold = actor.Gold, level = actor.Level;
            bool actorChanged = !ReferenceEquals(actor, _lastValueActor);
            if (actorChanged || hp != _lastHp || maxHp != _lastMaxHp || xp != _lastXp
                || gold != _lastGold || level != _lastLevel)
            {
                // Throttled hardware-proof line: displayed values changed (old → new).
                float now = Time.unscaledTime;
                if (now - _lastValueLog >= 0.5f)
                {
                    _lastValueLog = now;
                    string oldVals = actorChanged
                        ? "(new actor)"
                        : $"HP {_lastHp}/{_lastMaxHp}, XP {_lastXp}, Gold {_lastGold}, L{_lastLevel}";
                    VRLog.Info("WorldUI", $"WristHud values: '{actor.CharacterName}' {oldVals} → " +
                                          $"HP {hp}/{maxHp}, XP {xp}, Gold {gold}, L{level} " +
                                          $"(liveRemap={liveRemap}).");
                }
                _lastValueActor = actor;
                _lastHp = hp; _lastMaxHp = maxHp; _lastXp = xp;
                _lastGold = gold; _lastLevel = level;
            }
        }
        else
        {
            _lastValueActor = null;
        }

        _sb.Length = 0;
        if (actor == null)
        {
            _sb.Append("<alpha=#88>").Append(Core.Loc.Mod("no_character"));
        }
        else
        {
            _sb.Append("<alpha=#AA>").Append(Core.Loc.Game("GUI_LEVEL", "Level")).Append(' ')
               .Append(actor.Level).Append("<alpha=#FF>\n");
            _sb.Append("<color=#ff6a5e>").Append(Core.Loc.Mod("hp")).Append(' ')
               .Append(actor.Health).Append('/').Append(actor.MaxHealth).Append("</color>   ");
            _sb.Append("<color=#7fd4ff>").Append(Core.Loc.Mod("xp")).Append(' ')
               .Append(actor.XP).Append("</color>\n");
            _sb.Append("<color=#ffd45e>").Append(Core.Loc.Mod("gold")).Append(' ')
               .Append(actor.Gold).Append("</color>\n");

            CTokens tokens = actor.Tokens;
            if (tokens != null)
            {
                // 4 Hz refresh — the two list allocations here are acceptable
                // (never in the per-frame path).
                var positives = tokens.GetAllPositiveConditions();
                var negatives = tokens.GetAllNegativeConditions();
                if (positives.Count > 0 || negatives.Count > 0)
                {
                    for (int i = 0; i < positives.Count; i++)
                        _sb.Append("<color=#9fe08a>+").Append(positives[i]).Append("</color> ");
                    for (int i = 0; i < negatives.Count; i++)
                        _sb.Append("<color=#e08a8a>-").Append(negatives[i]).Append("</color> ");
                }
                else
                {
                    _sb.Append("<alpha=#88>").Append(Core.Loc.Mod("no_conditions"));
                }
            }
        }

        string text = _sb.ToString();
        if (text != _lastText)
        {
            _lastText = text;
            _text.text = text;
            if (_text.font == null)
                WorldUIAssets.TryAssignGameFont(_text);
        }
    }

    /// <summary>
    /// Identity row (test #13): portrait sprite is fetched only when the resolved
    /// actor changes; the name/context string rebuilds at refresh cadence and is
    /// only assigned on change (same pattern as the stats text).
    /// </summary>
    private void RefreshIdentity(CPlayerActor? actor)
    {
        if (_nameText == null)
            return;

        if (!ReferenceEquals(actor, _identityActor))
        {
            _identityActor = actor;
            Sprite? sprite = null;
            if (actor != null)
            {
                // Verified: UIInfoTools.Instance (UIInfoTools.cs:465),
                // GetNewAdventureCharacterPortrait (UIInfoTools.cs:773),
                // CCharacterClass.CharacterModel (CCharacterClass.cs:234) — the call
                // the game's card-selection preview makes (CardsHandManager.cs:776).
                // Guarded: modded/custom classes may lack a config → text fallback.
                try
                {
                    UIInfoTools tools = UIInfoTools.Instance;
                    if (tools != null)
                        sprite = tools.GetNewAdventureCharacterPortrait(actor.CharacterClass.CharacterModel);
                }
                catch (System.Exception ex)
                {
                    VRLog.Debug("WorldUI", $"WristHud: no class portrait ({ex.GetType().Name}) — name-only identity.");
                }
            }
            if (_portrait != null && _portraitGo != null)
            {
                _portrait.sprite = sprite;
                _portraitGo.SetActive(sprite != null);
            }
        }

        Choreographer choreographer = Choreographer.s_Choreographer;
        // SameActor (guid), not ReferenceEquals: the displayed actor is live-remapped
        // (ToLiveActor) and may be a different INSTANCE than the choreographer's message
        // actor while still being the same character.
        string context =
            actor == null ? string.Empty :
            VRModeStateMachine.CurrentMode == VRMode.CardSelection ? Core.Loc.Mod("selecting_cards") :
            choreographer != null && SameActor(actor, choreographer.CurrentPlayerActor) ? Core.Loc.Mod("current_turn") :
            Core.Loc.Mod("selected");
        _sb.Length = 0;
        if (actor == null)
            _sb.Append("<alpha=#88>—");
        else
            _sb.Append("<size=13><alpha=#AA>").Append(context).Append("</size>\n")
               .Append("<b><color=#ffd45e>").Append(actor.CharacterName).Append("</color></b>");

        string identity = _sb.ToString();
        if (identity != _lastIdentity)
        {
            _lastIdentity = identity;
            _nameText.text = identity;
            if (_nameText.font == null)
                WorldUIAssets.TryAssignGameFont(_nameText);
        }
    }

    private static CPlayerActor? ResolveActor()
    {
        // FREE CHARACTER FOCUS (user feature 2026-08-08): the watch face names the character the
        // player is LOOKING at. No focus falls straight through to the unchanged resolution
        // below, so a player who never uses the feature sees exactly what they saw before.
        CPlayerActor? focus = Board.CharacterFocus.Focused;
        if (focus != null)
            return focus;

        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

        // Multiplayer compat (audit R1): this HUD is the LOCAL player's own watch face. When
        // ONLINE it must ALWAYS show the character THIS client controls, never whoever's turn it
        // currently is — during a REMOTE player's turn Choreographer.CurrentPlayerActor and the
        // active card hand are the remote character, so the offline resolution below would leak
        // their Level/HP/XP/Gold/conditions onto the local wrist. Ownership test is
        // CPlayerActor.IsUnderMyControl, the SAME guard CardsGameApi.IsLocalHand and
        // Net/RevealGate use. Offline (single-player) there is exactly one local player who owns
        // every merc, so this whole block is skipped and the original behaviour below runs
        // byte-for-byte.
        if (FFSNetwork.IsOnline)
        {
            // During CardSelection the tab-switchable hand identifies WHICH of the local
            // player's own mercs is currently being viewed — honour it, but only when it is a
            // hand we actually control (IsLocalHand is IsUnderMyControl-gated while online, and
            // guarantees a non-null PlayerActor), so a remote's fan can never win.
            if (VRModeStateMachine.CurrentMode == VRMode.CardSelection)
            {
                CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
                if (hand != null && Cards.CardsGameApi.IsLocalHand(hand))
                    return hand.PlayerActor;
            }
            // Otherwise resolve straight to the local player's own character, independent of the
            // active turn. A null result (spectator / no assigned actor) makes RefreshText hide
            // the HUD rather than fall through to a remote actor's stats.
            return LocalPlayerActor();
        }

        // Test #13: while cards are being selected, the character the fan belongs to
        // wins — the tab-switchable hand the game presents. Read-only access through
        // the Cards module's verified API surface (CardsGameApi.ActiveHand():
        // CardsHandManager.Instance/CurrentHand/GetActiveHand, CardsHandManager.cs:
        // 127/137/595; CardsHandUI.PlayerActor, CardsHandUI.cs:208).
        if (VRModeStateMachine.CurrentMode == VRMode.CardSelection)
        {
            CardsHandUI? hand = Cards.CardsGameApi.ActiveHand();
            if (hand != null && hand.PlayerActor != null)
                return hand.PlayerActor;
        }

        CPlayerActor? actor = choreographer.CurrentPlayerActor;
        if (actor != null)
            return actor;

        InitiativeTrack track = InitiativeTrack.Instance;
        if (track != null)
        {
            InitiativeTrackActorBehaviour selected = track.SelectedActor();
            if (selected != null && selected.Actor is CPlayerActor selectedPlayer)
                return selectedPlayer;
        }
        return null;
    }

    /// <summary>Same character? Guid compare with reference fallback — instance-safe across
    /// the message/hand snapshot clones the game passes around.</summary>
    private static bool SameActor(CPlayerActor? a, CPlayerActor? b)
    {
        if (a == null || b == null)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        string ga = a.ActorGuid, gb = b.ActorGuid;
        return !string.IsNullOrEmpty(ga) && string.Equals(ga, gb, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Stale-value fix: re-map a resolved actor onto the LIVE rules instance in
    /// <c>ScenarioManager.Scenario.PlayerActors</c> (matched by ActorGuid) — the object
    /// <c>LootTile → AddGold</c> and <c>GainXP</c> actually mutate (CActor.cs:1813/2011).
    /// Falls back to the resolved object when the scenario/list is unavailable or the guid
    /// is not found (e.g. exhausted actor moved to ExhaustedPlayers), so the HUD never goes
    /// blank because of the remap.
    /// </summary>
    private static CPlayerActor? ToLiveActor(CPlayerActor? actor)
    {
        if (actor == null)
            return null;
        CScenario scenario = ScenarioManager.Scenario;
        var players = scenario?.PlayerActors;
        if (players == null)
            return actor;
        for (int i = 0; i < players.Count; i++)
        {
            CPlayerActor player = players[i];
            if (player != null && SameActor(player, actor))
                return player;
        }
        return actor;
    }

    /// <summary>
    /// Multiplayer compat (audit R1): the LOCAL player's own <see cref="CPlayerActor"/> — the
    /// character THIS client controls — independent of whose turn it is. Ownership is
    /// <c>CPlayerActor.IsUnderMyControl</c>, the same test <c>CardsGameApi.IsLocalHand</c> and
    /// <c>Net/RevealGate</c> use; the actor list is the scenario's own
    /// <c>CScenario.PlayerActors</c>. Returns null when no local-controlled actor exists
    /// (spectator / not yet assigned), so the HUD hides rather than showing a remote's stats.
    /// Only meaningful while <c>FFSNetwork.IsOnline</c> (offline every merc is under my control).
    /// </summary>
    private static CPlayerActor? LocalPlayerActor()
    {
        CScenario scenario = ScenarioManager.Scenario;
        if (scenario == null)
            return null;
        var players = scenario.PlayerActors;
        if (players != null)
        {
            for (int i = 0; i < players.Count; i++)
            {
                CPlayerActor player = players[i];
                if (player != null && player.IsUnderMyControl)
                    return player;
            }
        }
        return null;
    }
}

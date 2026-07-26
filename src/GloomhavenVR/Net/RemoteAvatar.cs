using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Visual proxy for one remote VR player: a floating head "mask" + two floating hands
/// (Demeo style — no humanoid IK), anchored in the SHARED world frame and eased toward the
/// latest received pose with Lerp/Slerp (never snapped). Purely cosmetic; touches no game
/// state. Built lazily by <see cref="NetAvatarDriver"/> on the first rig packet from a peer
/// and destroyed on player-left or staleness.
///
/// Hands reuse the mod's own <see cref="HandVisuals"/> (bundle glove or procedural fallback)
/// so remote hands look exactly like the local ones. The head mask is the one the SENDER
/// picked in their VR settings (<see cref="AvatarState.MaskId"/>): it loads
/// <c>Assets/Bundle/Head/Mask_&lt;id&gt;.prefab</c> from the ALREADY-LOADED mod bundle via
/// <see cref="HeadMaskLibrary"/> (see PLAN.md "mask asset contract"); until the masks ship, a
/// low-poly placeholder head stands in so the feature is testable now. A peer changing their
/// mask at runtime rebuilds only the head (cheap; only on change).
///
/// Sender scale: each part holder is scaled by the sender's rig <c>WorldScale</c> so a remote
/// player's 15 cm hand reads the same physical size above the shared board regardless of the
/// sender's diorama zoom. Positions are absolute world (game units).
/// </summary>
internal sealed class RemoteAvatar
{
    private readonly GameObject _root;
    private readonly Transform _headHolder;
    private readonly Transform _leftHolder;
    private readonly Transform _rightHolder;
    private readonly Color _tint;

    // Not readonly: rebuilt in place when the sender switches their hand style.
    private HandRig? _leftRig;
    private HandRig? _rightRig;
    private FingerCurler? _leftCurler;
    private FingerCurler? _rightCurler;

    private readonly RemoteHandFan _handFan;
    private readonly RemoteControlBoard _controlBoard;
    private readonly RemoteItemFan _itemFan;   // report 5: the peer's equipped-item fan
    private readonly RemoteCardFx _cardFx;     // report 6: replayed card animations
    private readonly RemoteBrowserFan _browserFan; // the peer's discard/burnt pile-browse reading fan

    // Card-FX de-duplication. The event byte is re-sent for redundancy on the unreliable extras
    // stream, so the flight plays only when the SEQUENCE changes. _fxSeqInit exists because the
    // very first extras packet from a peer may already carry an old event (they were mid-flight
    // when we joined) — playing that would fire a stray card across the table on join.
    private byte _lastFxSeq;
    private bool _fxSeqInit;

    // Ghost hand (extras FlagExtrasGhostHand): when the sender fades the hand carrying their open
    // fan, OUR copy of that hand must fade too — otherwise the sender's view of themselves and
    // everyone else's view of them disagree. ONE ghost is enough: the fan is always on the
    // sender's non-dominant hand, and HandGhost restores the old rig by itself when that side
    // flips (dominant-hand switch) or when BuildHands hands it a new rig instance.
    private readonly HandGhost _ghost;

    private AvatarState _target;
    private bool _hasTarget;
    private float _appliedScale = -1f;
    private float _appliedStyleScale = -1f; // per-style visual scale currently on the hand visual roots
    private int _appliedMaskId = -1; // which HeadMaskLibrary mask the head currently shows
    private int _appliedHandStyle = -1; // which HandStyle the hand holders currently wear

    // Held-card slab (additive FlagHeldCard wire field): one both-faces-back card slab eased
    // toward the sender's held-card pose — a card in a peer's HAND, distinct from their fan.
    // Backs only, mirroring the fan's anti-cheat stance (no card identity is ever on the wire).
    private Transform? _heldCardHolder;
    private Mesh? _heldCardMesh;
    private bool _heldCardBillboardLogged; // one-line confirm the receiver-side billboard fired

    /// <summary>Seconds since the last accepted packet (staleness bookkeeping).</summary>
    public float TimeSinceUpdate { get; private set; }

    public int PlayerId { get; }

    // ---- PUBLIC SEAM (read-only) — the feature workers (ghost fan / control board) build off
    //      these holders + received data without reaching into RemoteAvatar's privates. -------

    /// <summary>The avatar's root transform (world origin of the whole proxy).</summary>
    public Transform Root => _root.transform;

    /// <summary>Head-mask holder transform.</summary>
    public Transform HeadHolder => _headHolder;

    /// <summary>Left-hand holder transform.</summary>
    public Transform LeftHandHolder => _leftHolder;

    /// <summary>Right-hand holder transform.</summary>
    public Transform RightHandHolder => _rightHolder;

    /// <summary>The holder for the sender's NON-dominant hand (where the card fan sits). When the
    /// sender is right-dominant the non-dominant hand is the Left hand, and vice versa.</summary>
    public Transform NonDominantHandHolder => DominantRight ? LeftHandHolder : RightHandHolder;

    /// <summary>Stable per-player tint (matches the head/hand tint).</summary>
    public Color Tint => _tint;

    /// <summary>The sender-scale currently applied to the part holders (1 until the first packet).</summary>
    public float AppliedScale => _appliedScale > 0f ? _appliedScale : 1f;

    // ---- received extras / rig data (world frame) --------------------------------------------

    /// <summary>True when the sender broadcast a control-board world pose.</summary>
    public bool HasBoard { get; private set; }

    /// <summary>Control-board world position (valid when <see cref="HasBoard"/>).</summary>
    public Vector3 BoardPosition { get; private set; }

    /// <summary>Control-board world rotation (valid when <see cref="HasBoard"/>).</summary>
    public Quaternion BoardRotation { get; private set; } = Quaternion.identity;

    /// <summary>Control-board uniform scale (valid when <see cref="HasBoard"/>).</summary>
    public float BoardScale { get; private set; } = 1f;

    /// <summary>How many cards are in the sender's hand fan (rendered as backs only).</summary>
    public int HandCardCount { get; private set; }

    /// <summary>How many cards are in the sender's open ITEM fan (0 = closed / sender predates the
    /// field). Rendered as backs only by <see cref="RemoteItemFan"/>.</summary>
    public int ItemCardCount { get; private set; }

    /// <summary>True when that item fan is hand-held rather than anchored above their board.</summary>
    public bool ItemFanHeld { get; private set; }

    /// <summary>True when the hand-held item fan rides the sender's LEFT hand.</summary>
    public bool ItemFanLeftHand { get; private set; }

    /// <summary>True while the sender has a control-board PILE BROWSER open (the "Abgelegt" /
    /// "Verbrannt" reading fan). False for peers that predate the field — they simply show no fan.
    /// Rendered by <see cref="RemoteBrowserFan"/> as backs only.</summary>
    public bool PileBrowseOpen { get; private set; }

    /// <summary>Which pile that browser reads (<see cref="NetProtocol.PileBrowseKindDiscard"/> /
    /// <c>…Burnt</c> / <c>…Items</c>); meaningful only when <see cref="PileBrowseOpen"/>.</summary>
    public byte PileBrowseKind { get; private set; }

    /// <summary>How many cards are in that open browse fan (0 when closed).</summary>
    public int PileBrowseCardCount { get; private set; }

    /// <summary>True when that browse fan is a hand-held reading fan rather than board-anchored.</summary>
    public bool PileBrowseHeld { get; private set; }

    /// <summary>True when the hand-held browse fan rides the sender's LEFT hand.</summary>
    public bool PileBrowseLeftHand { get; private set; }

    /// <summary>True when the sender's dominant hand is the RIGHT hand (default true).</summary>
    public bool DominantRight { get; private set; } = true;

    /// <summary>True while the sender's fan-carrying hand is faded ("ghost hand"). False for peers
    /// that predate the field — their hands simply stay solid.</summary>
    public bool GhostHand { get; private set; }

    /// <summary>Material alpha the sender's ghost hand should be drawn at (1 = opaque), decoded
    /// from the transmitted strength byte. Meaningful only when <see cref="GhostHand"/>.</summary>
    public float GhostAlpha { get; private set; } = 1f;

    /// <summary>True when the sender is physically holding a figure this frame.</summary>
    public bool HasHeldFigure { get; private set; }

    /// <summary>Stable id of the held figure (valid when <see cref="HasHeldFigure"/>).</summary>
    public int HeldFigureActorId { get; private set; }

    /// <summary>Held-figure world position (valid when <see cref="HasHeldFigure"/>).</summary>
    public Vector3 HeldFigurePosition { get; private set; }

    /// <summary>Held-figure world rotation (valid when <see cref="HasHeldFigure"/>).</summary>
    public Quaternion HeldFigureRotation { get; private set; } = Quaternion.identity;

    public RemoteAvatar(int playerId)
    {
        PlayerId = playerId;

        _root = new GameObject($"GloomhavenVR.RemoteAvatar[{playerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.position = Vector3.zero;
        _root.transform.rotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;

        _tint = TintFor(playerId);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHeadMask(0); // mask 0 until the first packet reports the sender's real choice
        _headHolder.gameObject.SetActive(false);

        _leftHolder = new GameObject("Hand_Left").transform;
        _leftHolder.SetParent(_root.transform, worldPositionStays: false);
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightHolder.gameObject.SetActive(false);

        // Default Glove until the first packet reports the sender's real style —
        // mirrors the mask-0 default above.
        BuildHands(0);

        // Whole subtree onto the mod layer so the owned head camera renders it (no-op when
        // VR is not running, exactly like the local hands).
        VRLayers.Apply(_root);

        // Cosmetic add-ons built off the public seam (ghost card fan + read-only control board).
        // Foundation stubs today; the feature workers fill their bodies. Owned here: ticked from
        // Tick and torn down from Destroy.
        _handFan = new RemoteHandFan(this);
        _controlBoard = new RemoteControlBoard(this);
        _ghost = new HandGhost($"remote[{playerId}]");
        _itemFan = new RemoteItemFan(this);
        _cardFx = new RemoteCardFx(this);
        _browserFan = new RemoteBrowserFan(this);

        VRLog.Info("Net", $"Remote avatar created for player {playerId}.");
    }

    /// <summary>Accept a freshly-decoded state as the new interpolation target.</summary>
    public void SetTarget(in AvatarState state)
    {
        _target = state;
        _hasTarget = true;
        TimeSinceUpdate = 0f;

        // Carry the rig packet's held-figure + dominant-hand data onto the seam (world frame:
        // the driver has already converted the poses). Consumed by the feature workers.
        DominantRight = state.DominantRight;
        HasHeldFigure = state.HasHeldFigure;
        if (state.HasHeldFigure)
        {
            HeldFigureActorId = state.HeldFigureActorId;
            HeldFigurePosition = state.HeldFigurePose.Position;
            HeldFigureRotation = state.HeldFigurePose.Rotation;
        }
        else
        {
            HeldFigureActorId = 0;
        }

        // Swap the head mask when the sender's choice changes (cheap; only on change).
        if (state.MaskId != _appliedMaskId)
            BuildHeadMask(state.MaskId);

        // Swap the hand pair when the sender's HAND STYLE changes (same pattern; peers
        // without the styled prefab in their bundle degrade to Glove inside HandVisuals).
        if (state.HandStyle != _appliedHandStyle)
            BuildHands(state.HandStyle);

        // Apply sender scale to the part holders when it changes (cosmetic sizing only).
        // The per-STYLE visual scale lives on the "HandVisual" child roots (BuildHands),
        // so this write can no longer stomp it.
        float scale = state.WorldScale > 0f ? state.WorldScale : 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
            if (_heldCardHolder != null)
                _heldCardHolder.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Accept a freshly-decoded EXTRAS packet (board pose + hand count + dominant hand).
    /// Poses are already in world frame (converted by the driver).</summary>
    public void SetExtras(in PresenceState p)
    {
        HasBoard = p.HasBoard;
        if (p.HasBoard)
        {
            BoardPosition = p.Board.Position;
            BoardRotation = p.Board.Rotation;
            BoardScale = p.BoardScale > 0f ? p.BoardScale : 1f;
        }
        HandCardCount = p.HandCardCount;
        DominantRight = p.DominantRight;

        // Ghost hand: the sender's own strength rides the wire, so their faded hand reads the
        // same on every client. Decoded through the same AlphaFor curve the local hands use.
        GhostHand = p.GhostHand;
        GhostAlpha = p.GhostHand
            ? Hands.HandGhosts.AlphaFor(p.GhostStrength / 255f)
            : 1f;

        // Item fan (additive field): absent flag = the sender predates it OR their fan is closed —
        // both mean "show nothing", so a plain reset is correct in either case.
        ItemCardCount = p.HasItemFan ? p.ItemCardCount : 0;
        ItemFanHeld = p.HasItemFan && p.ItemFanHeld;
        ItemFanLeftHand = ItemFanHeld && p.ItemFanLeftHand;

        // Pile browse (additive field, same reasoning as the item fan): an absent flag means the
        // sender closed the fan OR predates the field — both mean "no fan", and the receiver's
        // RemoteBrowserFan turns the open→closed transition into the collapse-into-the-stack
        // animation. Because a DROPPED packet produces no SetExtras call at all, a lost packet can
        // never be mistaken for a close; only a packet that really arrived without the flag can.
        PileBrowseOpen = p.HasPileBrowse && p.PileBrowseCardCount > 0;
        PileBrowseKind = PileBrowseOpen ? p.PileBrowseKind : (byte)0;
        PileBrowseCardCount = PileBrowseOpen ? p.PileBrowseCardCount : 0;
        PileBrowseHeld = PileBrowseOpen && p.PileBrowseHeld;
        PileBrowseLeftHand = PileBrowseHeld && p.PileBrowseLeftHand;

        // Card FX (additive field): play ONLY on a sequence change (the same event is deliberately
        // re-sent for redundancy), and never on the first packet we ever see from this peer.
        if (p.HasCardFx)
        {
            if (!_fxSeqInit)
            {
                _fxSeqInit = true;
                _lastFxSeq = p.FxSeq; // adopt without playing — this event predates our joining
            }
            else if (p.FxSeq != _lastFxSeq)
            {
                _lastFxSeq = p.FxSeq;
                _cardFx.Play(p.FxEndpoints);
            }
        }
    }

    /// <summary>Per-frame interpolation toward the latest target. Call from the driver's Update.</summary>
    public void Tick(float deltaTime)
    {
        TimeSinceUpdate += deltaTime;
        // Defensive: if our root was destroyed out from under us (should not happen — it is
        // DontDestroyOnLoad + HideAndDontSave and owned solely by us) skip rather than throw.
        if (_root == null)
            return;

        float dt = Mathf.Max(deltaTime, 0f);

        // Live re-apply of the receiver-local per-style visual scale (config stepper edit
        // while a remote avatar is up) — same live check the local hands/mirror run.
        if (_leftRig != null)
        {
            float styleScale = HandVisuals.StyleScale(_leftRig.VisualStyle);
            if (!Mathf.Approximately(styleScale, _appliedStyleScale))
            {
                _appliedStyleScale = styleScale;
                if (_leftRig.Root != null)
                    HandVisuals.ApplyStyleScale(_leftRig.Root, _leftRig, styleScale);
                if (_rightRig != null && _rightRig.Root != null)
                    HandVisuals.ApplyStyleScale(_rightRig.Root, _rightRig, styleScale);
            }
        }

        if (_hasTarget)
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);
            UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
            UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
            UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
            UpdateHeldCard(k);
        }

        // Ghost hand: fade the sender's fan-carrying (= non-dominant) hand by the strength they
        // broadcast. The rig objects are ours (built by BuildHands), so the fade runs on private
        // material copies of THIS avatar only — no other player's hands and no bundle asset is
        // ever touched. Released automatically when the flag clears or the avatar is destroyed.
        HandRig? ghostRig = GhostHand ? (DominantRight ? _leftRig : _rightRig) : null;
        _ghost.Apply(ghostRig, GhostAlpha);

        // Cosmetic add-ons (own their own guards; stubs today).
        _handFan.Tick(dt);
        _controlBoard.Tick(dt);
        _itemFan.Tick(dt);
        _cardFx.Tick(dt);
        _browserFan.Tick(dt);
    }

    private static void UpdatePart(Transform holder, bool valid, in RigPose pose, float k)
    {
        if (!valid)
        {
            if (holder.gameObject.activeSelf) holder.gameObject.SetActive(false);
            return;
        }
        if (!holder.gameObject.activeSelf)
        {
            // First activation: snap to avoid a lerp streak from the origin.
            holder.SetPositionAndRotation(pose.Position, pose.Rotation);
            holder.gameObject.SetActive(true);
            return;
        }
        holder.SetPositionAndRotation(
            Vector3.Lerp(holder.position, pose.Position, k),
            Quaternion.Slerp(holder.rotation, pose.Rotation, k));
    }

    private static void UpdateHand(Transform holder, FingerCurler? curler, in HandStateSample hand, bool fingers, float k, float dt)
    {
        UpdatePart(holder, hand.Tracked, in hand.Pose, k);
        if (!hand.Tracked || curler == null)
            return;
        if (fingers)
        {
            curler.SetTarget(Finger.Thumb, hand.Curl0);
            curler.SetTarget(Finger.Index, hand.Curl1);
            curler.SetTarget(Finger.Middle, hand.Curl2);
            curler.SetTarget(Finger.Ring, hand.Curl3);
            curler.SetTarget(Finger.Pinky, hand.Curl4);
        }
        curler.Tick(dt);
    }

    /// <summary>
    /// One both-faces-back card slab at the sender's held-card pose (additive
    /// <see cref="NetProtocol.FlagHeldCard"/> field — a single card physically held in a peer's
    /// hand, e.g. plucked from their fan or a pile viewer). Built lazily on first use, eased
    /// exactly like the other parts, hidden while the sender holds nothing. Shows a BACK only:
    /// no card identity rides the wire (same anti-cheat stance as <see cref="RemoteHandFan"/>).
    ///
    /// ORIENTATION (multiplayer half of user report 2): a held card is not rigid in the owner's
    /// hand — <see cref="Cards.VRCard"/>.TickHeldPose re-billboards it to the OWNER's head every
    /// frame, so the owner always reads it face-on however their wrist is turned. The POSITION for
    /// that rule rides the wire already; the ROTATION does not need to. This slab used to simply
    /// slerp toward the transmitted rotation, which breaks the rule on the receiver in two ways:
    /// (a) the rotation is a 16-bit-quantized snapshot taken at the SEND rate and then eased with
    /// <see cref="NetProtocol.InterpolationSharpness"/> INDEPENDENTLY of the head and of the card's
    /// own position, so during head/hand motion the slab visibly lags out of the "facing its owner"
    /// relationship instead of holding it; and (b) after packet loss the last rotation keeps
    /// pointing at where the peer's head WAS. Both vanish if the receiver simply re-derives the
    /// billboard each frame from data it already has: the peer's head is a mandatory part of the
    /// SAME rig packet (<see cref="AvatarState.Head"/>, eased onto <see cref="HeadHolder"/> a few
    /// lines earlier in <see cref="Tick"/>), so <c>LookRotation(slabPos − headPos, head.up)</c>
    /// reproduces exactly what the owner sees — NO new wire field, no flag bit, no version bump.
    /// This is the same receiver-side billboard <see cref="RemoteItemFan"/> and
    /// <see cref="RemoteBrowserFan"/> already run for the peer's fans; the held-card slab was the
    /// one card proxy still trusting the transmitted rotation. The transmitted rotation is still
    /// read and still used as the fallback for a peer whose head is not tracked.
    ///
    /// No extra slerp on top: the billboard is derived from an ALREADY-eased slab position and an
    /// already-eased head, so it inherits their smoothing — easing it again would only re-introduce
    /// the lag this removes.
    /// </summary>
    private void UpdateHeldCard(float k)
    {
        if (_heldCardHolder == null)
        {
            if (!_target.HasHeldCard)
                return; // never held anything yet — build nothing
            BuildHeldCardSlab();
            if (_heldCardHolder == null)
                return;
        }
        UpdatePart(_heldCardHolder, _target.HasHeldCard, in _target.HeldCardPose, k);
        if (!_target.HasHeldCard || !_heldCardHolder.gameObject.activeSelf)
            return;
        if (!_target.HeadValid || !_headHolder.gameObject.activeSelf)
            return; // no synced head this frame — keep the transmitted rotation

        // Card +Z points AWAY from its reader (CardMesh / BuildBackSlab convention), so the look
        // direction is head → card: the owner sees the face, everyone else sees the back.
        Vector3 away = _heldCardHolder.position - _headHolder.position;
        if (away.sqrMagnitude < 1e-6f)
            return;
        away.Normalize();
        Vector3 up = _headHolder.up; // the OWNER's head-up: their head roll is on the wire too
        if (Mathf.Abs(Vector3.Dot(away, up)) > 0.9995f)
            return; // forward ∥ up — LookRotation undefined; keep the previous rotation
        _heldCardHolder.rotation = Quaternion.LookRotation(away, up);

        if (!_heldCardBillboardLogged)
        {
            _heldCardBillboardLogged = true;
            VRLog.Info("Net", $"Remote held card: billboarding player {PlayerId}'s slab to their SYNCED head "
                + $"(rig-packet head, no new wire field); wire rotation kept only as the untracked-head fallback. "
                + $"delta vs wire rot {Quaternion.Angle(_target.HeldCardPose.Rotation, _heldCardHolder.rotation):F1} deg.");
        }
    }

    private void BuildHeldCardSlab()
    {
        _heldCardHolder = new GameObject("HeldCard").transform;
        _heldCardHolder.SetParent(_root.transform, worldPositionStays: false);
        _heldCardHolder.localScale = Vector3.one * AppliedScale;
        _heldCardHolder.gameObject.SetActive(false);

        // Own mesh (freed in Destroy); SHARED back material (CardMesh caches it — never ours
        // to destroy). Sized to the same defaults the remote fan slabs use.
        _heldCardMesh = RemoteHandFan.BuildBackSlab(
            RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
        var mf = _heldCardHolder.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = _heldCardMesh;
        var mr = _heldCardHolder.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Cards.CardMesh.CreateBackMaterial();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        VRLayers.Apply(_heldCardHolder.gameObject);
    }

    public void Destroy()
    {
        // Cloned ghost materials are ASSETS — free them before the hand objects go away.
        _ghost.Release();
        _handFan.Destroy();
        _controlBoard.Destroy();
        _itemFan.Destroy();
        _cardFx.Destroy();
        _browserFan.Destroy();
        if (_heldCardMesh != null)
            Object.Destroy(_heldCardMesh); // asset — not freed with the GameObject tree
        _heldCardMesh = null;
        _heldCardHolder = null;
        if (_root != null)
            Object.Destroy(_root);
        VRLog.Info("Net", $"Remote avatar destroyed for player {PlayerId}.");
    }

    // ---- hands -------------------------------------------------------------------------

    /// <summary>(Re)build both hand visuals for the given wire hand-style: clears the hand
    /// holders' children, rebuilds the rigs + curlers with the sender's chosen prefab pair
    /// (Glove fallback inside <see cref="HandVisuals"/>), and re-applies the mod layer.
    /// Cheap and only on change — mirrors <see cref="BuildHeadMask"/>.
    ///
    /// Each hand is built under a "HandVisual" CHILD of the holder so the per-style visual
    /// scale ([Hands] GloveScale/PlateScale/ArcaneScale ≈0.62 for the styled pairs, applied
    /// by <see cref="HandVisuals.ApplyStyleScale"/> onto the transform passed to Build) lands
    /// on that child. The HOLDER carries only the sender scale from <see cref="SetTarget"/> —
    /// previously the sender-scale write stomped the style scale on the holder and a remote
    /// Plate/Arcane hand rendered ~1.6× too big. Attach-ons that size off the holder
    /// (<see cref="RemoteHandFan"/>) are deliberately unaffected by the style scale.</summary>
    private void BuildHands(int handStyle)
    {
        var style = Hands.HandStyles.Clamp(handStyle);
        _appliedHandStyle = handStyle;

        // Restore + free any ghost material clones BEFORE the old hand objects are destroyed
        // (Unity does not free materials with the GameObject that referenced them). Tick
        // re-applies the ghost to the freshly built rig on the very next frame.
        _ghost.Release();

        for (int i = _leftHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_leftHolder.GetChild(i).gameObject);
        for (int i = _rightHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_rightHolder.GetChild(i).gameObject);

        Transform leftVisual = new GameObject("HandVisual").transform;
        leftVisual.SetParent(_leftHolder, worldPositionStays: false);
        Transform rightVisual = new GameObject("HandVisual").transform;
        rightVisual.SetParent(_rightHolder, worldPositionStays: false);

        _leftRig = HandVisuals.Build(leftVisual, HandSide.Left, style);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig, HandSide.Left) : null;
        _rightRig = HandVisuals.Build(rightVisual, HandSide.Right, style);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig, HandSide.Right) : null;

        // Build applied the style scale for the built style (receiver-local config value
        // for the SENDER'S style); remember it so the live check in Tick only re-applies
        // on an actual config edit.
        _appliedStyleScale = HandVisuals.StyleScale(
            _leftRig != null ? _leftRig.VisualStyle : style);

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    // ---- head mask ----------------------------------------------------------------------

    /// <summary>(Re)build the head mask for the given mask id: clears any existing head visual,
    /// instantiates the chosen <see cref="HeadMaskLibrary"/> prefab, or stands in the placeholder
    /// head when that mask has not shipped yet. Re-applies the mod layer so the head camera renders
    /// the new mesh.</summary>
    private void BuildHeadMask(int maskId)
    {
        maskId = Mathf.Clamp(maskId, 0, HeadMaskLibrary.MaskCount - 1);
        _appliedMaskId = maskId;

        // Clear any existing head visual (children of the holder).
        for (int i = _headHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_headHolder.GetChild(i).gameObject);

        HeadMaskLibrary.BuildHead(_headHolder, maskId, _tint);

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    /// <summary>Stable per-player tint so avatars are distinguishable at a glance.</summary>
    private static Color TintFor(int playerId)
    {
        float hue = (playerId * 0.61803398875f) % 1f; // golden-ratio hashing → spread hues
        return Color.HSVToRGB(hue, 0.45f, 1f);
    }
}

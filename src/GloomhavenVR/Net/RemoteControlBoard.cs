using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A READ-ONLY cosmetic mirror of a remote player's control board, rendered at their REAL synced
/// world transform (<c>owner.BoardPosition/BoardRotation/BoardScale</c>, valid only while
/// <c>owner.HasBoard</c>). Since the 3D-parity pass the board SURFACE is the REAL bundled
/// control-board asset — <see cref="RemoteTrayVisual"/> clones the same Oak/Steel/Bronze prefab
/// the owner's own <c>PlayTray</c> instantiates, chosen by their synced style — and the old flat
/// "Frame" quad (the user-rejected "komisch 2D" board) survives only as the procedural fallback
/// for when the asset bundle is not resident. It shows that player's TWO round cards, seated in
/// the prefab's REAL slot recesses, so everyone can "see who placed what", in initiative order
/// (<c>InitiativeAbilityCard</c> first — mirrors the local board's
/// <c>PlayTray.SyncFromGameState</c> ordering).
///
/// JOIN-TIME (user requirement: a peer's board must appear the moment they join, not only after
/// the host assigns characters): the board FRAME + pose + style render from the first extras
/// packet that carries <c>HasBoard</c>, with NO <c>NetPlayerActors.ActorFor</c> gate — an
/// actorless peer simply shows an empty board. Only the CONTENT that genuinely needs the
/// host-replicated actor (round cards, initiative, rest state, pile counts, active cards) stays
/// actor-gated; the global panels (objectives, elements, round, initiative track) refresh either
/// way.
///
/// ANTI-CHEAT (the linchpin): the card FRONTS are shown ONLY when
/// <see cref="RevealGate.ShowRoundCardFronts"/> is true — i.e. never during the game's own secret
/// <c>SelectAbilityCardsOrLongRest</c> phase for a remote actor. Otherwise the cards show BACKS.
/// We never transmit card identities; the cards are read locally from the already-host-replicated
/// <c>CPlayerActor.CharacterClass</c>, and turned face-up strictly on the game's authoritative
/// reveal — so this opens no new cheat vector.
///
/// VISIBILITY (<see cref="NetModule.RemoteBoards"/>, a purely LOCAL rendering choice):
///   Off             → render nothing;
///   ActionPhaseOnly → render only once the owner's cards may be shown (i.e. NOT the secret
///                     selection phase — the whole board is hidden during selection);
///   Always          → render the frame always; card BACKS during selection, real faces on reveal.
///
/// CARD FACE ART — FULL DETAIL (user requirement: a player must be able to SHOW their board so the
/// others can READ the cards and advise on the next move). A face-up card here is the game's OWN
/// card widget: full painted art, both action halves with every icon and number, the initiative
/// disc, the level, the enhancement stickers. It is produced by <see cref="RemoteAbilityCardSource"/>
/// — which clones either the peer's own live <c>AbilityCardUI.fullAbilityCard</c> (those widgets
/// exist on our client for EVERY actor, not just the local one) or a widget borrowed from the game's
/// object pool by card id — onto a world-space canvas via <see cref="RemoteCardArt"/>. That class
/// carries the full evidence trail; the earlier claim here that "the full painted art only ever
/// exists for the LOCAL player's own hand" was simply wrong. The mod-drawn NAME + INITIATIVE panel
/// survives only as the last-resort fallback when neither source resolves. Backs reuse the mod's own
/// card-back texture (<c>CardMesh</c>), drawn unlit. The slot widget itself lives in
/// <see cref="RemoteBoardCard"/>, shared with the active-card column.
///
/// NOTHING NEW GOES ON THE WIRE for any of this. The card identities were already available locally
/// in the host-replicated <c>CPlayerActor.CharacterClass</c> — the same read that fed the old
/// name+initiative panel. All that changed is how that identity is DRAWN, so the cheat surface is
/// bit-for-bit the one <see cref="RevealGate"/> already governed.
///
/// FULL BOARD PARITY (standing user requirement: "ALLE Widgets … sollen auch beim fremden
/// Controllboard sichtbar und synchronisiert sein"). Beyond the two round cards this board now also
/// reproduces, MOD-DRAWN and at the same board-local offsets the LOCAL board docks its panels at:
///   • the objectives with their progress  (<see cref="RemoteObjectivesPanel"/> — GLOBAL),
///   • the element infusions               (<see cref="RemoteElementStrip"/>    — GLOBAL),
///   • the round number                    (<see cref="RemoteStatusReadouts"/>  — GLOBAL),
///   • the peer's initiative position      (<see cref="RemoteStatusReadouts"/>  — per-actor, gated),
///   • their short/long rest state         (<see cref="RemoteStatusReadouts"/>  — per-actor, split gate),
///   • their discard/burnt/item pile COUNTS on the three stacks (per-actor, public),
///   • their active/persistent cards       (<see cref="RemoteActiveCards"/>     — per-actor, gated),
///   • the scenario INITIATIVE TRACK       (<see cref="RemoteInitiativeTrack"/> — global list,
///                                          per-actor numbers under vanilla's own gate),
///   • and every piece of INTERACTIVE FURNITURE the local board wears
///                                         (<see cref="RemoteBoardFurniture"/> — see below).
/// NONE of that rides the wire: the global items are bit-identical on every client already, and the
/// per-actor items are read off the host-replicated <c>CPlayerActor</c> exactly like the round cards.
/// See <see cref="RemoteBoardContent"/> for the per-section anti-cheat derivation.
///
/// THE FURNITURE IS DRAWN, AND IT IS INERT. An earlier pass deliberately OMITTED a peer's own
/// controls (the CONFIRM/UNDO keycaps, the turn-flow cluster, the gear, the FOLLOW/PIN toggle, the
/// grab handle, the item-USE recess, the decision drawer, the slot overlays) on the argument that "a
/// button you cannot press is not information". The user rejected that: everything the local control
/// board shows must be shown on a peer's board too — but as a PURE DISPLAY, with nothing on it
/// interactable. <see cref="RemoteBoardFurniture"/> implements exactly that: meshes and text only,
/// no collider is ever created, nothing is added to <c>PlayTray.LaserTargets</c>, to
/// <c>VRInteractables</c> or to any other interaction registry, and a runtime guard
/// (<see cref="RemoteBoardFurniture.StripColliders"/>) destroys anything that ever slips through.
///
/// The transient reading fans (hand fan, item fan, pile browse, card flights) are handled by the
/// dedicated VR-only wire fields (<see cref="RemoteHandFan"/> / <see cref="RemoteItemFan"/> /
/// <see cref="RemoteBrowserFan"/> / <see cref="RemoteCardFx"/>).
///
/// Strict no-op offline / single-player / when the actor is null (<see cref="NetPlayerActors"/>
/// degrades to null there); everything is re-read each <see cref="Tick"/> (pose per frame, content
/// on the <see cref="RemoteBoardContent.RefreshSeconds"/> cadence).
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (VR-ONLY frame + GLOBAL / PER-ACTOR MODEL content). The board's
/// world POSE, SCALE and STYLE are VR-ONLY and ride the wire (extras <c>FlagHasBoard</c> = 24 B,
/// plus trailing-block byte A bits 5..6 for the style). Everything DRAWN on it is zero-wire and
/// carries its own tag: see <see cref="RemoteBoardContent"/> and <see cref="RemoteBoardFurniture"/>.
/// That split is the whole design — the wire pays only for where the board IS, never for what it
/// says. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteControlBoard
{
    // Frame geometry in the same "card real-metre" units as the local board (PlayTray BoardW/H),
    // so scaling by the owner's BoardScale reproduces their board's world size.
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>Board half-width in board-local metres — the anchor the off-edge docks (objectives
    /// left, piles/active cards right) offset from, mirroring <c>PlayTray.BoardHalfWidthLocal</c>.</summary>
    internal const float BoardHalfW = BoardW * 0.5f;

    // Two round-card slots at the REAL board layout: the authored card width × the local board's
    // 1.3 SlotScale — the exact size a card parked in the owner's recess renders at. (They were
    // 0.15 m "enlarged for at-a-distance legibility" on the flat board; on the real asset the
    // recesses dictate the size, and a wrong-sized card floating over a recess reads broken.)
    private const float CardW = 0.0635f * 1.3f;   // Defaults.CardWidth × PlayTray.SlotScale
    private const float CardH = CardW * (88f / 63.5f);
    private const float SlotSpacing = 0.155f;     // PlayTray.SlotSpacing (fallback layout)
    private const float SlotY = 0.015f;           // PlayTray procedural slot height (fallback)
    private const float ProudZ = -0.004f;   // toward the viewer (−Z), proud of the frame face

    /// <summary>Extra proud lift a card seated ON a real slot anchor gets (the anchor sits at the
    /// recess floor; a coplanar quad would z-fight the recess mesh).</summary>
    private const float CardOnAnchorProudZ = -0.003f;

    /// <summary>The shared proud depth every board-local surface sits at (−Z = toward the viewer).
    /// Exposed so the mod-drawn parity panels in <see cref="RemoteBoardContent"/> seat on the same
    /// plane as the round-card slots and the pile stacks.</summary>
    internal const float ProudZLocal = ProudZ;

    // ---- pile stacks (report 6) ---------------------------------------------------------------
    // The three card STACKS that hang off the right edge of the local control board
    // (PlayTray.PileMountBase + PileViewer's per-stack offsets: discard above, burnt below, items
    // two rows down). They exist here for one reason: a card flying into a peer's discard pile
    // (RemoteCardFx) needs a VISIBLE destination on that peer's board — before this, the remote
    // board had slots but no piles at all, so any pile-bound flight would have ended in empty air.
    // Kept deliberately small and back-textured (never a card identity — same anti-cheat stance as
    // everything else here).
    private const float PileX = Cards.PlayTray.BoardHalfWidthLocal + 0.012f + Cards.PlayTray.PileStackOffsetX;
    private const float PileSpacing = Cards.PlayTray.PileStackSpacing;

    /// <summary>DEFAULT board-local position of a play SLOT (0 = left, 1 = right) — the authored
    /// layout (<c>PlayTray.SlotSpacing</c>), used while no real tray visual is built (procedural
    /// fallback / board hidden). The LIVE layout — the real prefab's measured recess anchors —
    /// is served by the instance <see cref="AnchorLocalLive"/> and reaches the FX/fan consumers
    /// through <c>RemoteAvatar.BoardAnchorLocal</c>.</summary>
    internal static Vector3 SlotLocal(int slot) =>
        new((slot == 0 ? -0.5f : 0.5f) * SlotSpacing, SlotY, ProudZ);

    /// <summary>DEFAULT board-local position of a pile stack / the board centre for a card-FX
    /// anchor. Mirrors the LOCAL board's stack layout (<c>PlayTray.PileMountBase</c> +
    /// <c>PileViewer</c>'s ±spacing/2 and −1.5·spacing rows) so a peer's piles sit where that
    /// player's own piles sit. Prefer <see cref="AnchorLocalLive"/> when an instance is at hand —
    /// it substitutes the REAL prefab recess positions for the two slots.</summary>
    internal static Vector3 AnchorLocal(CardFxAnchor anchor) => anchor switch
    {
        CardFxAnchor.Slot0 => SlotLocal(0),
        CardFxAnchor.Slot1 => SlotLocal(1),
        CardFxAnchor.Discard => new Vector3(PileX, PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Burnt => new Vector3(PileX, -PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Items => new Vector3(PileX, -PileSpacing * 1.5f, ProudZ),
        _ => new Vector3(0f, 0f, ProudZ), // Board (and any unknown future id)
    };

    /// <summary>
    /// LIVE board-local anchor layout: like <see cref="AnchorLocal"/>, but the two round-card
    /// slots come from the REAL tray prefab's measured recess anchors once the 3D visual is
    /// built — so a card flight (<see cref="RemoteCardFx"/>) and a pile-browse arc land exactly
    /// in/on the rendered recess of whatever board style the peer runs, instead of on the old
    /// hardcoded flat-board offsets — and every other board anchor rides the same per-style
    /// content lift the rendered panels/piles sit at (<see cref="ContentProudLift"/>), so flight
    /// destination and rendered destination stay one point. Falls back to the defaults while the
    /// board has not built.
    /// </summary>
    internal Vector3 AnchorLocalLive(CardFxAnchor anchor)
    {
        if (_tray != null)
        {
            if (anchor == CardFxAnchor.Slot0)
                return _tray.SlotLocal(0) + new Vector3(0f, 0f, CardOnAnchorProudZ);
            if (anchor == CardFxAnchor.Slot1)
                return _tray.SlotLocal(1) + new Vector3(0f, 0f, CardOnAnchorProudZ);
            return AnchorLocal(anchor) + new Vector3(0f, 0f, ContentProudLift(_tray.Style));
        }
        return AnchorLocal(anchor);
    }

    /// <summary>
    /// Per-style proud LIFT (board-local −Z, toward the viewer) for the flat mod-drawn content —
    /// panels, readouts, pile counters — when it sits over the REAL board mesh. The Oak plate is
    /// (near) the authored z=0 plane the flat layout was tuned on; the Steel and Bronze meshes
    /// are visibly PROUDER of that plane — every authored per-style offset in Defaults says so
    /// (Steel: rest z −0.047, confirm z −0.047, initiative z −0.07, readout z −0.044; Bronze:
    /// −0.005..−0.02) — so unlifted content would be buried inside those boards. Values are the
    /// median of the shipped per-style z offsets; the next MP test must eyeball them per board
    /// (this is an approximation of the meshes, not a measurement).
    /// </summary>
    private static float ContentProudLift(Cards.ControlBoard style) => style switch
    {
        Cards.ControlBoard.Steel => -0.05f,
        Cards.ControlBoard.Bronze => -0.015f,
        _ => 0f,
    };

    private readonly RemoteAvatar _owner;

    private GameObject? _root;
    /// <summary>False until the synced pose was applied once — the first apply SNAPS (a fresh
    /// board must not ease in from the origin); later applies ease (see Tick).</summary>
    private bool _poseInit;
    private readonly RemoteBoardCard[] _cards = new RemoteBoardCard[2];
    private OwnerTag? _tag;

    // ---- board STYLE (extras block byte A bits 5..6) --------------------------------------------
    // The REAL 3D board asset for the style THIS peer chose (null → flat-quad fallback while the
    // bundle is absent). A style switch rebuilds the whole board from the new prefab (rare, cheap);
    // the fallback quad instead re-TINTS via _frameMat + _appliedStyle, exactly the pre-3D
    // behaviour, so a bundle-less client keeps working unchanged.
    private RemoteTrayVisual? _tray;
    private Material? _frameMat;
    private int _appliedStyle = -1;

    /// <summary>Next unscaled time to re-probe for the bundle when the board came up on the flat
    /// fallback — so a board built before the asset bundle finished loading upgrades itself to
    /// the real 3D asset instead of staying flat for the session.</summary>
    private float _nextTrayProbeAt;
    private const float TrayProbeSeconds = 5f;

    // ---- full-parity content (all mod-drawn, all zero-wire — see the class note) ----------------
    // Data class per widget — the same closed set as the CLASSIFICATION tags on the types
    // themselves (grep -rn "CLASSIFICATION:" Net/). None of these costs a wire byte; the only
    // wire input on this board is its own pose/scale/style plus the RemoteAvatar handed to
    // _furniture. Keep this column in step with the tags — it is the manifest a reader sees first.
    private RemoteObjectivesPanel? _objectives;   // GLOBAL
    private RemoteElementStrip? _elements;        // GLOBAL
    private RemoteStatusReadouts? _status;        // MIXED — GLOBAL round + PER-ACTOR initiative/rest
    private RemoteActiveCards? _active;           // PER-ACTOR MODEL — active/persistent cards
    private RemoteInitiativeTrack? _track;        // MIXED — GLOBAL actor list + PER-ACTOR initiative (gated)
    private RemoteBoardFurniture? _furniture;     // MIXED — DELIBERATELY-NOT neutral looks + PER-ACTOR
                                                  //   slots + VR-ONLY-derived pulses; INERT copies of
                                                  //   the board's interactive controls. Reads the wire
                                                  //   (RemoteAvatar) but adds no field to it.
    private readonly PileCounter?[] _piles = new PileCounter?[3]; // PER-ACTOR MODEL — discard / burnt /
                                                  //   items counts, deliberately UNGATED (vanilla lets
                                                  //   anyone open any player's card overview)

    /// <summary>Next content re-read time (unscaled). The POSE follows every frame; the model reads
    /// and the TMP repaints run on the <see cref="RemoteBoardContent.RefreshSeconds"/> cadence so a
    /// four-peer table stays free.</summary>
    private float _nextRefreshAt;

    /// <summary>Change-gate for the "what is this peer's board rendering" diagnostic (see
    /// <see cref="LogContent"/>) — one Info line per actual change, never per tick.</summary>
    private string _loggedContent = string.Empty;

    private readonly CAbilityCard?[] _ordered = new CAbilityCard?[2];

    public RemoteControlBoard(RemoteAvatar owner)
    {
        _owner = owner;
    }

    public void Tick(float dt)
    {
        // The visibility mode is read through the SHARED gate (RemoteBoardGate) rather than off the
        // ConfigEntry directly, because the board is no longer the only thing the setting governs:
        // the transient item / pile-browse fans and the card-flight FX are separate classes with
        // separate roots, and they now ask the same predicate. One expression, one meaning.
        RemoteBoardVisibility vis = RemoteBoardGate.Mode;
        RemoteBoardGate.LogModeIfChanged(vis); // evidence the panel's cycle button reaches the render path

        if (!_owner.HasBoard || vis == RemoteBoardVisibility.Off)
        {
            BlankCardFaces();
            SetActive(false);
            return;
        }

        // Read the owner's actor fresh each frame (null offline / before the host assigns
        // characters / benched). JOIN-TIME REQUIREMENT: the actor is NOT a gate for the board
        // SURFACE any more — a peer's board must appear the moment their first extras packet
        // lands, character assignment or not. An actorless peer has no cards, so there is
        // nothing the reveal gate could need to hide: "no actor" counts as "not in the secret
        // phase" for the visibility rule below.
        CPlayerActor? actor = NetPlayerActors.ActorFor(_owner.PlayerId);

        bool showFronts = actor != null && RevealGate.ShowRoundCardFronts(actor);

        bool showBoard = RemoteBoardGate.SurfaceVisible(vis, actor == null || showFronts);

        if (!showBoard)
        {
            // ANTI-CHEAT: hiding the root is not enough. A hosted card face that survives inside a
            // deactivated board would be re-activated by SetActive(true) on the frame the board
            // comes back — one statement BEFORE the slots re-evaluate the gate. Blanking the slots
            // here means there is no such face to re-activate, in the one case where it matters most
            // (RemoteBoardVisibility.ActionPhaseOnly hides the whole board *because* the gate shut).
            // Self-early-returning and allocation-free once blank, so it is free to run every frame.
            BlankCardFaces();
            SetActive(false);
            return;
        }

        // The peer switched their control board: tear the whole visual down and rebuild from the
        // new prefab — the real asset cannot be re-tinted into another board the way the fallback
        // quad could. Rare (a settings click on their side), and the rebuild is one frame.
        if (_root != null && _tray != null && _tray.Style != _owner.BoardStyle)
        {
            VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] style switch " +
                              $"{_tray.Style} → {_owner.BoardStyle} — rebuilding from the new prefab.");
            Destroy();
        }
        // A board that came up FLAT because the bundle was not resident yet upgrades itself to the
        // real asset once it is (slow probe — a few bundle-list walks per minute, only while flat).
        else if (_root != null && _tray == null && Time.unscaledTime >= _nextTrayProbeAt)
        {
            _nextTrayProbeAt = Time.unscaledTime + TrayProbeSeconds;
            if (RemoteTrayVisual.PrefabAvailable(_owner.BoardStyle))
            {
                VRLog.Info("Net", $"Remote board [{_owner.PlayerId}]: asset bundle now resident — " +
                                  "upgrading the flat fallback board to the real 3D asset.");
                Destroy();
            }
        }

        EnsureBuilt();
        SetActive(true);

        // Place at the REAL synced world transform. The wire carries the OWNER's exact pose
        // (full quantized quaternion — the Frei movement scheme adds no axis the pose doesn't
        // cover). Since mod build 2 the SENDER raises the extras cadence to the rig rate
        // (15 Hz) while the pose is CHANGING (NetAvatarDriver.TickExtrasSend, defect 7
        // "Bewegen kommt nicht flüssig an"), so during an active drag this easing gets the
        // same sample density the head/hands get — the exact pipeline whose smoothness is
        // already accepted — and an idle board still costs only 5 Hz. Ease with the shared
        // avatar sharpness; once the owner releases, the eased pose converges on the exact
        // transmitted one (snap on first build so a fresh board never lerps in from the origin).
        Vector3 wantPos = _owner.BoardPosition;
        Quaternion wantRot = _owner.BoardRotation;
        Transform rt = _root!.transform;
        if (!_poseInit)
        {
            _poseInit = true;
            rt.SetPositionAndRotation(wantPos, wantRot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * Mathf.Max(dt, 0f));
            rt.SetPositionAndRotation(
                Vector3.Lerp(rt.position, wantPos, k),
                Quaternion.Slerp(rt.rotation, wantRot, k));
        }
        _root.transform.localScale = Vector3.one * (_owner.BoardScale > 0f ? _owner.BoardScale : 1f);

        // Fallback board only: re-tint the flat frame when this peer switches their control board
        // (the real asset was rebuilt above instead; change-latched int compare either way).
        ApplyBoardStyle();

        if (actor != null)
            OrderRoundCards(actor);
        else
            _ordered[0] = _ordered[1] = null; // actorless peer: an EMPTY board, never stale cards
        // THE reveal decision for this peer's played cards, taken ONCE per frame here and passed
        // down: showFronts is RevealGate.ShowRoundCardFronts(actor) verbatim — false for a remote
        // actor while the game is in its own secret SelectAbilityCardsOrLongRest phase, true once
        // the selection is locked in and the characters are acting (and always true offline / for
        // our own actor / off-scenario). The slot only ever CREATES a face object inside its
        // front branch, so the fronts cannot exist a frame early. The actor is handed through purely
        // so the slot can find that player's own card widget to clone — it is never written to.
        for (int i = 0; i < 2; i++)
            _cards[i].Set(_ordered[i], showFronts, actor);

        _tag!.Tick();

        // Content (objectives / elements / round / initiative / rest / pile counts / active cards)
        // on the shared cadence — everything below is a MODEL read, not a wire read. Before the
        // actor exists only the GLOBAL panels refresh (they are bit-identical on every client);
        // the per-actor surfaces stay blank until the host assigns the character.
        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
            if (actor != null)
                RefreshContent(actor, showFronts);
            else
                RefreshGlobalContent();
        }
    }

    /// <summary>The actorless subset of <see cref="RefreshContent"/> (join-time, before the host
    /// assigns this peer a character): objectives, element infusions and the initiative track are
    /// GLOBAL scenario state and render fine without an actor; everything per-actor stays blank.</summary>
    private void RefreshGlobalContent()
    {
        try
        {
            _objectives?.Refresh();
            _elements?.Refresh();
            _track?.Refresh();
            // The furniture's SYNCED half (board-UI record: buttons + wanted glow) is wire-fed
            // and must follow the owner's board with or without an actor — only the
            // slot-occupancy-derived overlays need one, and they read the neutral flags here.
            _furniture?.Refresh(null, _owner, showFronts: false, slot0: false, slot1: false);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote board [{_owner.PlayerId}] global content refresh failed: {e.Message}");
        }
    }

    /// <summary>
    /// Re-read every parity surface from the LOCAL game model and repaint what changed. Wrapped as a
    /// whole: a half-initialised scenario state (mid-load, mid-teardown) must degrade to a stale
    /// board, never take down the remote-avatar tick that also drives this peer's head and hands.
    /// </summary>
    private void RefreshContent(CPlayerActor actor, bool showFronts)
    {
        try
        {
            _objectives?.Refresh();
            _elements?.Refresh();
            _status?.Refresh(actor, showFronts);
            _active?.Refresh(actor, showFronts);
            _track?.Refresh();

            // The inert furniture layer. It is fed the SAME reveal answer and the SAME round-card
            // occupancy the board is already rendering — see RemoteBoardFurniture for why nothing
            // derived from those two can leak anything the board does not already show.
            _furniture?.Refresh(actor, _owner, showFronts, _ordered[0] != null, _ordered[1] != null);

            // Pile counts — the SAME reads CardsGameApi.DiscardedCount/BurntCount and
            // ItemsPile.Count make for the local board, against this actor instead of the local
            // hand. PUBLIC information: vanilla lets anyone open ANY player's full card overview
            // from the initiative track (InitiativeTrackPlayerAvatar.OnClick →
            // CardsHandManager.ToggleViewAllCards), so a count on a stack reveals nothing new and
            // needs no reveal gate.
            CCharacterClass cc = actor.CharacterClass;
            int discard = cc != null ? cc.DiscardedAbilityCards.Count : 0;
            int burnt = cc != null ? cc.LostAbilityCards.Count + cc.PermanentlyLostAbilityCards.Count : 0;
            CInventory? inv = actor.Inventory;
            int items = inv?.AllItems != null ? inv.AllItems.Count : 0;
            _piles[0]?.Set(discard);
            _piles[1]?.Set(burnt);
            _piles[2]?.Set(items);

            LogContent(discard, burnt, items, showFronts);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote board [{_owner.PlayerId}] content refresh failed: {e.Message}");
        }
    }

    /// <summary>
    /// Change-gated diagnostic so the next hardware log states EXACTLY what a peer's board is
    /// rendering (grep: "Remote board content"). One line per actual change — the cadence tick
    /// itself is silent.
    /// </summary>
    private void LogContent(int discard, int burnt, int items, bool showFronts)
    {
        // FIDELITY + ANTI-CHEAT in one greppable line: which mechanism drew each round card
        // (LiveWidget / PooledBorrow = the REAL game card face; None = the mod-drawn fallback panel),
        // together with the gate answer that allowed a face at all. Grep: "Remote board content".
        string slots = $"{FaceTag(0, showFronts)}/{FaceTag(1, showFronts)}";
        string line = $"Remote board content [{_owner.PlayerId}]: " +
                      $"round='{(_status != null ? _status.RoundText : "-")}', " +
                      $"initiative={(_status != null ? _status.InitiativeText : "?")}, " +
                      $"rest='{(_status != null ? _status.RestText : string.Empty)}', " +
                      $"piles d/b/i={discard}/{burnt}/{items}, " +
                      $"round-card faces={slots}, " +
                      $"active={(_active != null ? _active.Count : 0)} card(s) " +
                      $"({(_active != null ? _active.RealFaceCount : 0)} real face(s)), " +
                      $"objectives={(_objectives != null ? _objectives.RowCount : 0)} row(s), " +
                      $"elements={(_elements != null ? _elements.ActiveCount : 0)} infused, " +
                      $"track={(_track != null ? _track.Count : 0)} entr(y/ies), " +
                      $"furniture[{(_furniture != null ? _furniture.StateLine : "-")}], " +
                      $"fronts={showFronts}";
        if (line == _loggedContent)
            return;
        _loggedContent = line;
        VRLog.Info("Net", line + " — all read LOCALLY from the replicated model (zero wire traffic); " +
                          "fronts gated by RevealGate.ShowRoundCardFronts (false ⇒ BACKS only, which " +
                          "is exactly the game's secret SelectAbilityCardsOrLongRest phase for a " +
                          "remote actor). A round-card face of LiveWidget/PooledBorrow is the REAL " +
                          "game card at full detail; 'panel' is the mod-drawn name+initiative " +
                          "fallback; 'back' means the gate is shut or the slot is empty.");
    }

    /// <summary>Per-slot fidelity tag for <see cref="LogContent"/>: the face path when a real face is
    /// up, otherwise what the slot is actually showing (mod panel when the gate is open but neither
    /// source resolved; a card BACK when the gate is shut; nothing at all when the slot is empty).
    /// Pure read of already-computed state — it re-derives no gate of its own.</summary>
    private string FaceTag(int slot, bool showFronts)
    {
        if (_ordered[slot] == null)
            return "empty";
        RemoteBoardCard card = _cards[slot];
        RemoteAbilityCardSource.FacePath path = card != null
            ? card.Path
            : RemoteAbilityCardSource.FacePath.None;
        if (path != RemoteAbilityCardSource.FacePath.None)
            return path.ToString();
        return showFronts ? "panel" : "back";
    }

    /// <summary>Mirror the local board's ordering: <c>InitiativeAbilityCard</c> first, then the
    /// remaining round card(s). Falls back to list order when the initiative card is not yet set
    /// (e.g. mid-selection) — exactly like <c>PlayTray.SyncFromGameState</c>.</summary>
    private void OrderRoundCards(CPlayerActor actor)
    {
        _ordered[0] = _ordered[1] = null;
        CCharacterClass cc = actor.CharacterClass;
        var round = cc.RoundAbilityCards;
        CAbilityCard? initiative = cc.InitiativeAbilityCard;

        int idx = 0;
        if (initiative != null && round.Contains(initiative))
            _ordered[idx++] = initiative;
        for (int i = 0; i < round.Count && idx < 2; i++)
        {
            CAbilityCard c = round[i];
            if (c == null || c == initiative)
                continue;
            _ordered[idx++] = c;
        }
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject($"GloomhavenVR.RemoteControlBoard[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;

        // THE BOARD SURFACE — the REAL bundled 3D asset for the style this peer synced
        // (RemoteTrayVisual: same prefab, same materials, same recesses as their own board),
        // replacing the old flat frame quad. The quad survives ONLY as the fallback for when the
        // bundle is not resident (then the probe in Tick upgrades it as soon as it is).
        _tray = RemoteTrayVisual.Build(_root.transform, _owner.BoardStyle);
        if (_tray == null)
        {
            // Fallback frame: a dark unlit slab, re-tintable to the peer's style (ApplyBoardStyle)
            // — bit-for-bit the pre-3D board, so a bundle-less client loses nothing it had.
            _frameMat = BoardVisual.Unlit(FrameColor(Cards.ControlBoard.Oak));
            BoardVisual.Quad(_root.transform, "Frame", new Vector2(BoardW, BoardH), _frameMat);
            _nextTrayProbeAt = Time.unscaledTime + TrayProbeSeconds;
        }
        _appliedStyle = -1; // force the first Tick to state what it applied (fallback tint path)

        // Round-card slots: ON the real recess anchors when the asset is up (a card then sits IN
        // the recess of whatever board the peer runs, at the same size their own card parks at),
        // else at the authored fallback layout on the flat frame.
        if (_tray != null)
        {
            _cards[0] = new RemoteBoardCard(_tray.SlotAnchor(0),
                new Vector3(0f, 0f, CardOnAnchorProudZ), CardW, CardH);
            _cards[1] = new RemoteBoardCard(_tray.SlotAnchor(1),
                new Vector3(0f, 0f, CardOnAnchorProudZ), CardW, CardH);
        }
        else
        {
            _cards[0] = new RemoteBoardCard(_root.transform, SlotLocal(0), CardW, CardH);
            _cards[1] = new RemoteBoardCard(_root.transform, SlotLocal(1), CardW, CardH);
        }

        // The flat mod-drawn CONTENT (panels, readouts, pile counters) hangs under one shared
        // parent that carries the per-style proud lift (ContentProudLift): the Steel/Bronze
        // meshes stand proud of the authored z=0 plane the flat layout was tuned on, and content
        // left at −0.004 would be buried inside them. Oak lift is 0 → parent is the root itself
        // and nothing moves. The card-FX anchors ride the same lift (AnchorLocalLive).
        Transform contentParent = _root.transform;
        float lift = _tray != null ? ContentProudLift(_tray.Style) : 0f;
        if (lift != 0f)
        {
            contentParent = new GameObject("ContentProud").transform;
            contentParent.SetParent(_root.transform, worldPositionStays: false);
            contentParent.localPosition = new Vector3(0f, 0f, lift);
        }

        // The three stacks (report 6 + 1:1 parity defect 3 "die Stapel sehen nicht aus wie auf dem
        // Original-Board"): the destinations a remote card flight lands on, built with the SAME
        // visual construction the owner's own stacks use (PileViewer.PileStack.Create — a 4-slab
        // jittered mini pile at the authored 0.62× card footprint, per-pile tint, count ON the top
        // slab, localized caption beneath, top slab greying out at zero) instead of the old single
        // flat card-back quad. Colors are the local stacks' verbatim; sizes come from the authored
        // Defaults so every client renders a given board identically regardless of local tuning.
        _piles[0] = new PileCounter(contentParent, "DiscardStack", AnchorLocal(CardFxAnchor.Discard),
            new Color(0.55f, 0.48f, 0.34f), PileViewer.Caption(PileKind.Discard));
        _piles[1] = new PileCounter(contentParent, "BurntStack", AnchorLocal(CardFxAnchor.Burnt),
            new Color(0.45f, 0.22f, 0.16f), PileViewer.Caption(PileKind.Burnt));
        _piles[2] = new PileCounter(contentParent, "ItemStack", AnchorLocal(CardFxAnchor.Items),
            new Color(0.30f, 0.42f, 0.26f), PileViewer.Caption(PileKind.Items));

        // Full-parity panels (all mod-drawn, all fed from the LOCAL model — see the class note).
        _objectives = new RemoteObjectivesPanel(contentParent);
        _elements = new RemoteElementStrip(contentParent);
        _status = new RemoteStatusReadouts(contentParent);
        _active = new RemoteActiveCards(contentParent);
        _track = new RemoteInitiativeTrack(contentParent);
        _furniture = new RemoteBoardFurniture(_root.transform, _owner.BoardStyle, _tray,
            AnchorLocalLive(CardFxAnchor.Slot0), AnchorLocalLive(CardFxAnchor.Slot1));
        _nextRefreshAt = 0f; // repaint on the very next tick

        // Ownership tag pinned just above the board's top-left corner, always facing the head.
        // Y clears the initiative track drawn above the top edge (RemoteInitiativeTrack, y 0.165
        // + half its 0.052 chip = 0.191) so the tag never sits on top of a track entry.
        _tag = new OwnerTag(_owner.PlayerId, _root.transform,
            new Vector3(-BoardW * 0.5f + 0.02f, 0.215f, ProudZ));

        VRLayers.Apply(_root);

        // FINAL INERTNESS GUARANTEE for the WHOLE board, not just the furniture: a remote player's
        // control board is a pure display. Nothing on it — not a keycap, not a card panel, not a
        // pile stack — may be pokeable, laser-targetable or grabbable. Everything above is built
        // from BoardVisual.Quad (collider stripped at creation) and TextMeshPro, and this sweep
        // turns that from a code-review claim into a runtime fact.
        RemoteBoardFurniture.StripColliders(_root, $"RemoteControlBoard[{_owner.PlayerId}]");

        VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] built " +
                          (_tray != null
                              ? $"on the REAL 3D '{_tray.Style}' board asset (the prefab that peer's own PlayTray renders) "
                              : "on the FLAT fallback frame (asset bundle not resident — will upgrade when it loads) ") +
                          "with FULL parity surfaces: " +
                          "2 round-card slots, 3 pile stacks with counts, objectives, elements, " +
                          "round + initiative + rest readouts, active-card column, initiative TRACK, " +
                          "and the complete interactive furniture (Confirm/Undo keycaps on their " +
                          "native dock mounts, turn-flow Skip cap, settings gear, FOLLOW/PIN toggle, " +
                          "grab-handle bar, item-USE recess + USE cap, decision drawer, pick field, " +
                          "slot snap/wanted glows, half-card dividers) — ALL OF IT INERT: no " +
                          "colliders, no laser targets, no poke zones, no grab handles, nothing in " +
                          "any interaction registry. It is a display of a control board, not one.");
    }

    // ------------------------------------------------------------------ board style --

    /// <summary>
    /// FALLBACK frame colour standing in for a control-board MATERIAL (flat-quad board only — the
    /// real 3D asset carries its own bundled materials). Oak keeps EXACTLY the colour this board
    /// has always had, so nothing changes for a peer on the default board (or on an older build,
    /// whose zeroed style bits also read as Oak); Steel is a cool gunmetal grey and Bronze a warm
    /// dark copper, i.e. the same three materials the real boards read as, at a glance and from
    /// across the table.
    /// </summary>
    private static Color FrameColor(Cards.ControlBoard style) => style switch
    {
        Cards.ControlBoard.Steel => new Color(0.13f, 0.14f, 0.17f, 1f),
        Cards.ControlBoard.Bronze => new Color(0.16f, 0.10f, 0.05f, 1f),
        _ => new Color(0.10f, 0.09f, 0.08f, 1f), // Oak — today's colour, unchanged
    };

    /// <summary>
    /// FALLBACK-BOARD path only: tint the flat frame quad to the board style the peer chose
    /// (received in the extras block's byte A bits 5..6). Change-latched: a no-op int compare
    /// until they actually switch. When the REAL 3D asset is up this is a strict no-op — a style
    /// switch there rebuilds the whole board from the new prefab instead (see Tick), because the
    /// real Oak/Steel/Bronze boards are different meshes, not different tints.
    /// </summary>
    private void ApplyBoardStyle()
    {
        Cards.ControlBoard style = _owner.BoardStyle;
        if (_tray != null || (int)style == _appliedStyle || _frameMat == null)
            return;
        _appliedStyle = (int)style;
        _frameMat.color = FrameColor(style);
        VRLog.Info("Net", $"Remote FALLBACK board [{_owner.PlayerId}] re-tinted to the '{style}' " +
                          "control board (byte A bits 5..6, zero extra bytes) — the real prefab is " +
                          "not available on this client yet.");
    }

    /// <summary>Drop every hosted card face on this board (round slots + active column) and reset the
    /// slots' change gates, so the next visible frame re-decides from scratch. No-op before the board
    /// has ever been built.</summary>
    private void BlankCardFaces()
    {
        for (int i = 0; i < _cards.Length; i++)
            _cards[i]?.Blank();
        _active?.Blank();
        _loggedContent = string.Empty; // the next visible refresh must re-state what is drawn
    }

    private void SetActive(bool active)
    {
        if (_root != null && _root.activeSelf != active)
            _root.SetActive(active);
    }

    public void Destroy()
    {
        _tag?.Destroy();
        _tag = null;
        // Drop every hosted card face FIRST. The clones are children of the board root and would die
        // with it anyway, but "we own the clone, we destroy the clone" is the contract these widgets
        // are built on (see RemoteAbilityCardSource) and it must not depend on Unity's destruction
        // order — nor on the board root still existing when a peer leaves mid-teardown.
        for (int i = 0; i < _cards.Length; i++)
            _cards[i]?.Destroy();
        _active?.Destroy();
        _poseInit = false; // a rebuilt board (style switch / bundle upgrade) snaps again
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        // Every parity surface is a CHILD of _root and dies with it — drop the handles so a rebuilt
        // board (peer re-join / scene change) can never repaint through a destroyed transform, and
        // so the change-gates start clean.
        _objectives = null;
        _elements = null;
        _status = null;
        _active = null;
        _track = null;
        _furniture = null;
        _piles[0] = _piles[1] = _piles[2] = null;
        _loggedContent = string.Empty;
        _nextRefreshAt = 0f;
        // The frame material belongs to the destroyed quad; drop the handle and the style latch so a
        // rebuilt board re-applies the peer's style from scratch instead of trusting a stale int.
        // The tray visual is a child of _root and died with it — dropping the handle here is what
        // makes the next EnsureBuilt re-instantiate the (possibly different-style) prefab.
        _tray = null;
        _frameMat = null;
        _appliedStyle = -1;
        _nextTrayProbeAt = 0f;
    }

    // ------------------------------------------------------------------ pile stack --

    /// <summary>
    /// One of the three pile stacks on a peer's board (discard / burnt / items): the SAME 4-slab
    /// mini pile the owner's own board wears (mirror of <c>PileViewer.PileStack.Create</c> — four
    /// thin jittered slabs stepping into the board, per-pile tint with darkened lower slabs, the
    /// live COUNT on the top slab, the localized caption beneath, and the top slab greying out at
    /// zero exactly like the local stack). It is also the destination a <see cref="RemoteCardFx"/>
    /// flight lands on. Sized from the authored Defaults (<c>Defaults.CardWidth</c> ×
    /// <c>PileViewer.PileStack.SlabFactor</c>) — the OWNER's [Cards] tuning is local config and
    /// deliberately not applied, as everywhere on this board. Collider-free by construction.
    ///
    /// The count is PUBLIC information — vanilla lets any player open ANY other player's full card
    /// overview straight off the initiative track (<c>InitiativeTrackPlayerAvatar.OnClick</c> →
    /// <c>CardsHandManager.ToggleViewAllCards</c>) — so it needs no reveal gate. Change-gated writes:
    /// a per-tick <c>TMP.text</c> assignment re-triggers auto-size layout.
    /// </summary>
    private sealed class PileCounter
    {
        /// <summary>Authored slab footprint — the local stack's <c>CardsConfig.CardWidth ×
        /// SlabFactor</c> at the shipped default.</summary>
        private const float SlabW = Defaults.CardWidth * PileViewer.PileStack.SlabFactor;
        private const float SlabH = SlabW * (88f / 63.5f);

        private readonly TextMeshPro _count;
        private readonly Material? _topMaterial;
        private readonly Color _baseColor;
        private int _shown = int.MinValue;

        public PileCounter(Transform parent, string name, Vector3 localPos, Color color,
            string caption)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, worldPositionStays: false);
            root.localPosition = localPos;
            _baseColor = color;

            // Stack body — the local recipe verbatim (PileViewer.PileStack.Create): 4 thin slabs,
            // each a step behind the previous (+Z is into the board) with a small alternating
            // jitter/tilt so it reads as a real pile; lower slabs darkened 45 %.
            Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            for (int i = 0; i < 4; i++)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Slab{i}";
                Object.Destroy(slab.GetComponent<Collider>());
                slab.transform.SetParent(root, worldPositionStays: false);
                slab.transform.localScale = new Vector3(SlabW, SlabH, 0.0018f);
                float jitter = (i % 2 == 0 ? 1f : -1f) * 0.0015f;
                slab.transform.localPosition = new Vector3(jitter, -jitter, 0.0022f * (3 - i));
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * 2.5f);
                if (shader != null)
                {
                    var material = new Material(shader)
                    {
                        color = i == 3 ? color : Color.Lerp(color, Color.black, 0.45f),
                    };
                    slab.GetComponent<MeshRenderer>().sharedMaterial = material;
                    if (i == 3)
                        _topMaterial = material;
                }
            }

            // Count on the top slab + caption beneath — same font/fit calls as the local stack.
            var countGo = new GameObject("Count");
            countGo.transform.SetParent(root, worldPositionStays: false);
            countGo.transform.localPosition = new Vector3(0f, 0f, -0.0025f); // viewer side (-Z)
            _count = countGo.AddComponent<TextMeshPro>();
            _count.text = "-";
            _count.alignment = TextAlignmentOptions.Center;
            _count.color = new Color(1f, 0.95f, 0.8f);
            WorldUI.NativeButtonSkin.ApplyFont(_count);
            TmpFit.Fit(_count, SlabW * 0.9f, SlabH * 0.62f, maxFontSize: 0.30f, wrap: false);

            var captionGo = new GameObject("Caption");
            captionGo.transform.SetParent(root, worldPositionStays: false);
            captionGo.transform.localPosition = new Vector3(0f, -SlabH * 0.5f - 0.016f, -0.0025f);
            var captionTmp = captionGo.AddComponent<TextMeshPro>();
            captionTmp.text = caption.ToUpperInvariant();
            captionTmp.alignment = TextAlignmentOptions.Center;
            captionTmp.color = new Color(0.85f, 0.8f, 0.7f);
            WorldUI.NativeButtonSkin.ApplyFont(captionTmp);
            TmpFit.Fit(captionTmp, 0.095f, 0.024f, maxFontSize: 0.22f, wrap: false);
        }

        /// <summary>Write the count (change-gated); an empty pile greys its TOP SLAB out, exactly
        /// like the local board's stack dims at zero (<c>PileStack.SetCount</c>).</summary>
        public void Set(int count)
        {
            if (count == _shown)
                return;
            _shown = count;
            _count.text = count.ToString();
            if (_topMaterial != null)
            {
                Color want = count > 0 ? _baseColor : Color.Lerp(_baseColor, Color.gray, 0.7f);
                if (_topMaterial.color != want)
                    _topMaterial.color = want;
            }
        }
    }
}

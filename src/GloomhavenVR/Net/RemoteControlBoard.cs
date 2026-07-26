using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A READ-ONLY cosmetic mirror of a remote player's control board, rendered at their REAL synced
/// world transform (<c>owner.BoardPosition/BoardRotation/BoardScale</c>, valid only while
/// <c>owner.HasBoard</c>). It shows that player's TWO round cards so everyone can "see who placed
/// what", in initiative order (<c>InitiativeAbilityCard</c> first — mirrors the local board's
/// <c>PlayTray.SyncFromGameState</c> ordering).
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
internal sealed class RemoteControlBoard
{
    // Frame geometry in the same "card real-metre" units as the local board (PlayTray BoardW/H),
    // so scaling by the owner's BoardScale reproduces their board's world size.
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>Board half-width in board-local metres — the anchor the off-edge docks (objectives
    /// left, piles/active cards right) offset from, mirroring <c>PlayTray.BoardHalfWidthLocal</c>.</summary>
    internal const float BoardHalfW = BoardW * 0.5f;

    // Two round-card slots, side by side and enlarged for at-a-distance legibility.
    private const float CardW = 0.15f;
    private const float CardH = CardW * (88f / 63.5f);
    private const float SlotX = 0.11f;      // ± slot centre X (board-local)
    private const float ProudZ = -0.004f;   // toward the viewer (−Z), proud of the frame face

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
    private const float PileW = 0.075f;
    private const float PileH = PileW * (88f / 63.5f);

    /// <summary>Board-local position of a play SLOT (0 = left, 1 = right) — the anchor a docking
    /// card flies into. Shared with <see cref="RemoteCardFx"/> so the flight and the rendered slot
    /// agree even when the board frame itself is hidden by the visibility setting.</summary>
    internal static Vector3 SlotLocal(int slot) => new(slot == 0 ? -SlotX : SlotX, 0f, ProudZ);

    /// <summary>Board-local position of a pile stack / the board centre for a card-FX anchor.
    /// Mirrors the LOCAL board's stack layout (<c>PlayTray.PileMountBase</c> +
    /// <c>PileViewer</c>'s ±spacing/2 and −1.5·spacing rows) so a peer's piles sit where that
    /// player's own piles sit.</summary>
    internal static Vector3 AnchorLocal(CardFxAnchor anchor) => anchor switch
    {
        CardFxAnchor.Slot0 => SlotLocal(0),
        CardFxAnchor.Slot1 => SlotLocal(1),
        CardFxAnchor.Discard => new Vector3(PileX, PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Burnt => new Vector3(PileX, -PileSpacing * 0.5f, ProudZ),
        CardFxAnchor.Items => new Vector3(PileX, -PileSpacing * 1.5f, ProudZ),
        _ => new Vector3(0f, 0f, ProudZ), // Board (and any unknown future id)
    };

    private readonly RemoteAvatar _owner;

    private GameObject? _root;
    private readonly RemoteBoardCard[] _cards = new RemoteBoardCard[2];
    private OwnerTag? _tag;

    // ---- board STYLE (extras block byte A bits 5..6) --------------------------------------------
    // The frame material of THIS peer's board (one Material instance per remote board — nothing is
    // shared, so tinting one peer's board can never touch another's) plus the change latch, so the
    // per-frame check costs an int compare until the peer actually switches board.
    private Material? _frameMat;
    private int _appliedStyle = -1;

    // ---- full-parity content (all mod-drawn, all zero-wire — see the class note) ----------------
    private RemoteObjectivesPanel? _objectives;   // GLOBAL
    private RemoteElementStrip? _elements;        // GLOBAL
    private RemoteStatusReadouts? _status;        // GLOBAL round + per-actor initiative/rest
    private RemoteActiveCards? _active;           // per-actor active/persistent cards
    private RemoteInitiativeTrack? _track;        // GLOBAL actor list + per-actor initiative (gated)
    private RemoteBoardFurniture? _furniture;     // INERT copies of the board's interactive controls
    private readonly PileCounter?[] _piles = new PileCounter?[3]; // discard / burnt / items

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
        RemoteBoardVisibility vis = NetModule.RemoteBoards != null
            ? NetModule.RemoteBoards.Value
            : RemoteBoardVisibility.Off;

        // Read the owner's actor fresh each frame (null offline / single-player / netcode absent /
        // benched) — a strict no-op in every one of those cases.
        CPlayerActor? actor = _owner.HasBoard && vis != RemoteBoardVisibility.Off
            ? NetPlayerActors.ActorFor(_owner.PlayerId)
            : null;

        bool showFronts = actor != null && RevealGate.ShowRoundCardFronts(actor);

        bool showBoard = actor != null && vis switch
        {
            RemoteBoardVisibility.Always => true,
            // ActionPhaseOnly: only once the cards may be shown (not the secret selection phase).
            RemoteBoardVisibility.ActionPhaseOnly => showFronts,
            _ => false,
        };

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

        EnsureBuilt();
        SetActive(true);

        // Place at the REAL synced world transform.
        _root!.transform.SetPositionAndRotation(_owner.BoardPosition, _owner.BoardRotation);
        _root.transform.localScale = Vector3.one * (_owner.BoardScale > 0f ? _owner.BoardScale : 1f);

        // Re-tint the frame when this peer switches their control board (change-latched, so it is a
        // single int compare on every other frame).
        ApplyBoardStyle();

        OrderRoundCards(actor!);
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
        // on the shared cadence — everything below is a MODEL read, not a wire read.
        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
            RefreshContent(actor!, showFronts);
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

        // Board frame: a dark rounded slab. Unlit so it reads regardless of scene lighting. Its
        // material is kept so the frame can be re-tinted to the peer's CHOSEN board style without
        // rebuilding anything (see ApplyBoardStyle); it starts at the Oak/default colour, which is
        // bit-for-bit what this board looked like before the style ever rode the wire.
        _frameMat = BoardVisual.Unlit(FrameColor(Cards.ControlBoard.Oak));
        BoardVisual.Quad(_root.transform, "Frame", new Vector2(BoardW, BoardH), _frameMat);
        _appliedStyle = -1; // force the first Tick to state what it applied

        _cards[0] = new RemoteBoardCard(_root.transform, SlotLocal(0), CardW, CardH);
        _cards[1] = new RemoteBoardCard(_root.transform, SlotLocal(1), CardW, CardH);

        // The three stacks (report 6): the destinations a remote card flight lands on. Card-back
        // texture, drawn unlit. Since the parity pass they also carry the peer's live pile COUNT +
        // the localized caption the local board's own stacks wear (PileViewer.Caption), so a peer's
        // "Abgelegt 7 / Verbrannt 2 / Gegenstände 3" reads at a glance instead of the numbers being
        // knowable only from that player's own seat.
        Texture? stackTex = CardMesh.CreateBackMaterial().mainTexture;
        Material stackMat = BoardVisual.Unlit(new Color(0.82f, 0.82f, 0.82f, 1f), stackTex);
        _piles[0] = new PileCounter(_root.transform, "DiscardStack", AnchorLocal(CardFxAnchor.Discard),
            stackMat, PileViewer.Caption(PileKind.Discard));
        _piles[1] = new PileCounter(_root.transform, "BurntStack", AnchorLocal(CardFxAnchor.Burnt),
            stackMat, PileViewer.Caption(PileKind.Burnt));
        _piles[2] = new PileCounter(_root.transform, "ItemStack", AnchorLocal(CardFxAnchor.Items),
            stackMat, PileViewer.Caption(PileKind.Items));

        // Full-parity panels (all mod-drawn, all fed from the LOCAL model — see the class note).
        _objectives = new RemoteObjectivesPanel(_root.transform);
        _elements = new RemoteElementStrip(_root.transform);
        _status = new RemoteStatusReadouts(_root.transform);
        _active = new RemoteActiveCards(_root.transform);
        _track = new RemoteInitiativeTrack(_root.transform);
        _furniture = new RemoteBoardFurniture(_root.transform);
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

        VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] built with FULL parity surfaces: " +
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
    /// Frame colour standing in for a control-board MATERIAL. Oak keeps EXACTLY the colour this
    /// board has always had, so nothing changes for a peer on the default board (or on an older
    /// build, whose zeroed style bits also read as Oak); Steel is a cool gunmetal grey and Bronze a
    /// warm dark copper, i.e. the same three materials the real boards read as, at a glance and
    /// from across the table.
    /// </summary>
    private static Color FrameColor(Cards.ControlBoard style) => style switch
    {
        Cards.ControlBoard.Steel => new Color(0.13f, 0.14f, 0.17f, 1f),
        Cards.ControlBoard.Bronze => new Color(0.16f, 0.10f, 0.05f, 1f),
        _ => new Color(0.10f, 0.09f, 0.08f, 1f), // Oak — today's colour, unchanged
    };

    /// <summary>
    /// Tint this peer's board frame to the board THEY chose (received in the extras block's byte A
    /// bits 5..6). Change-latched: a no-op int compare until they actually switch.
    ///
    /// WHY A TINT AND NOT THEIR PREFAB: a remote board is deliberately a MOD-DRAWN, INERT display
    /// (see the class note) — quads and text, no colliders, no interaction registry — not an
    /// instance of the real PlayTray prefab, which carries the whole interactive furniture and
    /// would have to be loaded, stripped and re-parented per peer per switch. The wire cost of the
    /// choice is two bits and the visual cost is one material colour, which is exactly the
    /// "peers see the board you actually use" the standing MP rule asks for, at the fidelity this
    /// display was built at.
    /// </summary>
    private void ApplyBoardStyle()
    {
        Cards.ControlBoard style = _owner.BoardStyle;
        if ((int)style == _appliedStyle || _frameMat == null)
            return;
        _appliedStyle = (int)style;
        _frameMat.color = FrameColor(style);
        VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] re-tinted to the '{style}' control " +
                          "board — the board style that peer picked in THEIR settings, received in " +
                          "the extras block (byte A bits 5..6, zero extra bytes). Cosmetic only: " +
                          "the remote board stays the same inert mod-drawn display.");
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
        _frameMat = null;
        _appliedStyle = -1;
    }

    // ------------------------------------------------------------------ pile stack --

    /// <summary>
    /// One of the three pile stacks on a peer's board (discard / burnt / items): the card-back slab
    /// that a <see cref="RemoteCardFx"/> flight lands on, PLUS the peer's live pile COUNT and the
    /// same localized caption the local board's own stack wears (<c>PileViewer.Caption</c>).
    ///
    /// The count is PUBLIC information — vanilla lets any player open ANY other player's full card
    /// overview straight off the initiative track (<c>InitiativeTrackPlayerAvatar.OnClick</c> →
    /// <c>CardsHandManager.ToggleViewAllCards</c>) — so it needs no reveal gate. Change-gated writes:
    /// a per-tick <c>TMP.text</c> assignment re-triggers auto-size layout.
    /// </summary>
    private sealed class PileCounter
    {
        private readonly TextMeshPro _count;
        private int _shown = int.MinValue;

        public PileCounter(Transform parent, string name, Vector3 localPos, Material slabMat,
            string caption)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, worldPositionStays: false);
            root.localPosition = localPos;

            BoardVisual.Quad(root, "Slab", new Vector2(PileW, PileH), slabMat);

            _count = RemoteBoardContent.Label(root, "Count", new Vector3(0f, 0f, -0.001f),
                new Vector2(PileW * 0.8f, PileH * 0.5f), 0.075f,
                new Color(1f, 0.95f, 0.8f), TextAlignmentOptions.Center, FontStyles.Bold);
            _count.text = "-";

            RemoteBoardContent.Label(root, "Caption",
                new Vector3(0f, -PileH * 0.5f - 0.014f, -0.001f),
                new Vector2(PileW * 1.25f, 0.020f), 0.036f,
                new Color(0.85f, 0.8f, 0.7f), TextAlignmentOptions.Center)
                .text = caption.ToUpperInvariant();
        }

        /// <summary>Write the count (change-gated); an empty pile greys out, exactly like the local
        /// board's stack dims its top slab at zero.</summary>
        public void Set(int count)
        {
            if (count == _shown)
                return;
            _shown = count;
            _count.text = count.ToString();
            _count.color = count > 0
                ? new Color(1f, 0.95f, 0.8f)
                : new Color(0.55f, 0.53f, 0.48f);
        }
    }
}

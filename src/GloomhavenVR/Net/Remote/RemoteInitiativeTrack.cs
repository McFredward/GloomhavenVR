using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Initiative TRACK — GLOBAL, mirrored from the game's own widget
// =================================================================================================

/// <summary>
/// The scenario's INITIATIVE TRACK, drawn above a peer's board — the mirror of the local board's
/// docked <c>InitiativeTrack</c> canvas (<c>PlayTray.InitiativeMount</c> /
/// <c>InitiativeTrackSurface</c>).
///
/// ─── DEFECT (a) OF THE 1:1-PARITY ROUND, AND WHAT IT ACTUALLY WAS ──────────────────────────────
/// The hardware screenshot showed this track as a row of plain GREEN and RED rectangles. That was
/// not a failure to find the data — the log said <c>track=8 entr(y/ies)</c>, so the entries were
/// resolved correctly all along — it was the RENDERING: this class drew its own tinted plate + name
/// + number per actor, and explicitly declared the portrait out of scope ("NOT REPRODUCED — the
/// PORTRAIT … reaching that per-actor texture from the mod is neither cheap nor cheat-relevant").
///
/// That premise was wrong, and in the same way <see cref="RemoteAbilityCardSource"/> proved the
/// "full card art only exists for the local hand" premise wrong. The portrait does not have to be
/// reached at all: <c>CharacterPortraitsProvider</c> has ALREADY assigned it to the live track's
/// <c>RawImage</c> on this client, and <c>Object.Instantiate</c> copies live component state. So
/// cloning the game's own track brings the portraits, the class colours, the initiative discs and
/// the reorder animation across for free.
///
/// ─── EVERY HIGHLIGHT THE TRACK CAN SHOW, AND WHERE EACH ONE COMES FROM ─────────────────────────
/// User ruling 2026-08-08: "auch die highlights der Initativreihenfolge auf dem remote board, so
/// wie der Spieler sie sieht. Sie unterscheidet sich also ggf. von der Anzeige auf dem eigenen
/// board." The inventory below is the answer to that, one line per state, so the next reader does
/// not have to re-derive which of them are safe to copy off the LOCAL widget.
///
/// GLOBAL (bit-identical on every client — copied straight off the mirrored widget, ZERO wire):
///   • the ENTRY SET (<c>UpdateInitiativeTrack</c>'s filter over the replicated actor list) and
///     the ENEMY block's order. NOT the PLAYER block's order — that was wrong here for four
///     builds and is defect (a) below;
///   • the inter-round REORDER SLIDE's TIMING and GEOMETRY: <c>trackReorderDuration</c> is a
///     serialized field of the one shared prefab, the trigger is <c>UpdateActors</c> off the
///     replicated message stream, and the travel is holder-local uGUI pixels produced by the same
///     <c>HorizontalLayoutGroup</c> over the same entry set at the same widths. The line in
///     <c>WorldUI/Surfaces/InitiativeReorderSlide</c> about each client fixing "its own row at its
///     own tray orientation" is about the panel's WORLD pose, which this mirror never copies — it
///     clones the widget, re-fits it into <c>PlayTray.InitiativeMountWidth</c> and copies LOCAL
///     rect state only. The slide's one per-viewer ingredient is which portrait STARTS in which
///     slot, and that is defect (a), not a defect of the slide;
///   • GRAYSCALE — <c>SetGrayscale</c>, i.e. an exhausted hero and, during the action phases,
///     every character that is not at turn (InitiativeTrack.cs:614/625). Its inputs are the phase
///     and <c>Choreographer.m_CurrentActor</c>, both host-replicated. It is a MATERIAL swap, which
///     is why <c>RemoteWidgetMirror.Pair.CopyMaterial</c> had to exist before it could mirror;
///   • the DEAD state (header off / dead image on, <c>SetDeadState</c>) — model, active flags;
///   • the EXTRA-TURN animation (<c>ShowExtraTurn(actor.IsTakingExtraTurn)</c>) — model;
///   • the DAMAGE-WARNING animation (<c>InitiativeTrackPlayerBehaviour.ShowWarning</c>, raised at
///     Choreographer.cs:5479 and cleared at :5582). It rides the game's own replicated message
///     <c>PlayerSelectingToAvoidDamageOrNot</c>, which EVERY client processes (the branches inside
///     it are about who holds the prompt, not about who runs the case), so it is already identical
///     on every machine and needs NOT ONE WIRE BYTE. It animates through a GUIAnimator, i.e.
///     LeanTween writes on transforms / colours / CanvasGroup alpha / material properties — all of
///     which Pair.Apply now carries;
///   • the persistent-ability chips and the multiplayer controller badge — model.
///
/// PER-VIEWER (must NEVER be copied off the local widget — each has its own wire record):
///   • HOVER: the name label, the enemy info popup, the minimized-entry width and the
///     ExtendedButton grow/nudge — extension record 16, applied by <see cref="ApplyHoverOverrides"/>;
///   • the mod's FOCUS / AT-TURN RINGS — extension record 22 plus this client's own read of who is
///     at turn, applied by <see cref="ApplyFocusRings"/>;
///   • vanilla's SELECTION FRAME — extension record 23, applied by
///     <see cref="ApplySelectionOverride"/>. See that method for why record 22 could not answer it.
///   • the PLAYER BLOCK'S ON-SCREEN ORDER — extension record 27, applied by
///     <see cref="ApplyOrderOverride"/>. Vanilla's <c>InitiativeTrackActorBehaviour.CompareTo</c>
///     sorts two PLAYER entries by <c>IsUnderMyControl</c> while online and in the card-selection
///     phase (decompiled GH.Runtime/InitiativeTrackActorBehaviour.cs:160-171), foreign first, so a
///     clone of this client's widget put THIS client's arrangement on every peer's board;
///   • the SELECTION-PHASE "still has to choose" RING — <see cref="ApplySelectionGlow"/>. The
///     mod's own <c>WorldUI.Surfaces.InitiativeSelectionGlow</c> builds that amber ring ONLY for
///     actors the LOCAL player controls, and it is a plain <c>Image</c> under the portrait, so
///     <c>Instantiate</c> cloned it and <c>Pair.Apply</c> drove it: every peer's board wore the
///     OBSERVER's ring set and never the owner's. It costs no wire of its own — record 27's owned
///     mask names the owner's characters, and whether one has COMMITTED is read here from the
///     replicated model (<c>CCharacterClass.RoundAbilityCards</c> / <c>LongRest</c>). The one
///     thing it does take off the wire is the owner's own <c>[SelectionReady] Enabled</c> — one
///     field on the SHARED tuning record 28 (id 238, <see cref="_selectionReadyOn"/>), not a
///     record of its own — because without it the mirror kept drawing rings for an owner who had
///     switched the cue out of their options.
///
/// DELIBERATELY NOT MIRRORED (the ruling's own exception, "während der Auswahlphase die
/// tatsächlichen Oberseiten der Karten"): the INITIATIVE NUMBER and the initiative FX state of a
/// foreign player during <c>SelectAbilityCardsOrLongRest</c>. Vanilla hides both behind
/// <c>IsUnderMyControl</c> (<c>InitiativeTrackPlayerAvatar.CalculateInitiative</c>,
/// <c>InitiativeTrackActorAvatar.RefreshInitiative</c>:203-227 — the FX literally encodes how many
/// round cards that player has committed). The mirror therefore keeps THIS client's entitled view
/// of those two, which is the strictly less-informed one; carrying the owner's would leak their
/// chosen initiative, i.e. their card.
///
/// <para>THAT MASK IS NOW AN EXPLICIT USER-RULED EXCEPTION (2026-08-08) and NOT a gap to be closed
/// later: a foreign player's initiative NUMBER reads "?" during card selection because the value
/// narrows which card was played as surely as the face does. Records 23 and 27 both switch state
/// on entries the number belongs to and NEITHER touches the number — record 27 carries a
/// permutation and an ownership mask, and the selection ring it pays for says only "this character
/// has not finished choosing", which vanilla's own multiplayer ready tracker broadcasts anyway.
/// The one thing this class must never learn how to do is fill that "?" in.</para>
///
/// ─── WHAT IT DRAWS NOW ─────────────────────────────────────────────────────────────────────────
/// PRIMARY — <see cref="RemoteWidgetMirror"/> over <c>InitiativeTrack.Instance.transform</c>: the
/// REAL widget, cloned once and puppeteered per frame from the original (see that class for why the
/// clone runs none of the game's code and can never be interacted with). This is the same single
/// track instance the local board docks, so it is by construction the same entry set, the same
/// numbers and the same "?"s the owner sees — with the PLAYER ORDER re-decided on top from record
/// 27 — including vanilla's own online gate
/// (<c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c>). No mod-side gate is needed or wanted: the pixels being copied are
/// pixels this client is already displaying.
///
/// FALLBACK — the previous mod-drawn chip strip, kept verbatim for the frames where
/// <c>InitiativeTrack.Instance</c> does not exist (menu, mid-load, a scenario tearing down). It
/// reads the same entry list in the same on-screen order (ascending sibling index under the track
/// holder, which is where vanilla's <c>UpdateSortingOrder</c> writes the display order) and falls
/// back again to <c>ScenarioManager.Scenario.AllAliveActors</c> + a label sort when even that is
/// gone. Which of the two is live is stated in the <c>Remote board content</c> log line, so a
/// hardware log PROVES which one the user is looking at.
///
/// SEAT — <see cref="RemoteBoardLayout.InitiativeMount"/>, i.e. the authored per-board
/// <c>InitiativeOffset</c> the owner's own <c>PlayTray.BuildMounts</c> assigns to its initiative
/// mount, keyed by that peer's synced style. The old remote-only constant (y 0.165, the flat-board
/// estimate) is gone: on the Steel board the owner's track sits at (0, 0.200, −0.070), which is
/// 9 mm up and 16 mm proud of where this used to draw it — part of defect (c).
/// </summary>
/// <remarks>CLASSIFICATION: MIXED. The mirrored WIDGET is GLOBAL — a scenario-wide singleton the
/// local client already renders, at ZERO wire — and the PER-VIEWER families laid on top of it cost
/// extension records 16 (hover), 22 (focus, shared with the board outlines), 23 (vanilla's
/// selection frame) and 27 (the player block's on-screen order plus the owner's controlled set,
/// which also pays for the selection-phase ring). Each names a PUBLIC track entry by its stable
/// ActorGuid hash and nothing else. The fallback's actor LIST is
/// <c>ScenarioManager.Scenario.AllAliveActors</c> and its per-actor NUMBERS are PER-ACTOR MODEL
/// gated by <see cref="RevealGate"/> exactly as vanilla gates its own. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteInitiativeTrack
{
    /// <summary>Track width budget — <c>PlayTray.InitiativeMountWidth</c> (= the board width), the
    /// same budget the owner's own docked track is fitted into.</summary>
    private const float Width = PlayTray.InitiativeMountWidth;

    private const float ChipH = 0.052f;
    private const float MaxChipW = 0.082f;
    private const int MaxChips = 8;

    private readonly Transform _root;
    private readonly Transform _fallbackRoot;
    private readonly RemoteWidgetMirror _mirror;
    private readonly Chip[] _chips = new Chip[MaxChips];
    private readonly List<CActor> _entries = new(MaxChips);
    private readonly List<CClass> _seen = new(MaxChips);
    private readonly List<InitiativeTrackActorBehaviour> _gameEntries = new(16);

    private string _signature = string.Empty;

    /// <summary>How many entries the track currently represents (diagnostics). With the mirror live
    /// this is the game track's own entry count; on the fallback it is the chip count.</summary>
    public int Count { get; private set; }

    /// <summary>Which mechanism is drawing the track right now (diagnostics — see
    /// <see cref="RemoteWidgetMirror.Fidelity"/>).</summary>
    public RemoteWidgetMirror.Fidelity Source { get; private set; } = RemoteWidgetMirror.Fidelity.None;

    /// <summary>Why the real widget is not being mirrored, for the diagnostic line (empty when it is).</summary>
    public string Reason => _mirror.Reason;

    /// <summary>The RESOLVED seat of this dock for the per-peer board log: the board-local mount
    /// position the authored layout put it at, and which measure sized the mirrored panel that
    /// grows up from it (the two numbers a "sits far too high" report is decided by).</summary>
    public string SeatLine => $"mount={_root.localPosition:F3} via {_mirror.MeasurePath}";

    public RemoteInitiativeTrack(Transform boardRoot, in RemoteBoardLayout layout,
                                 in RemoteBoardTuning tuning)
    {
        // THE OWNER'S OWN [SelectionReady] Enabled (record 28, wire id 238) — see
        // _selectionReadyOn for what it gates and for why the client this board belongs to is the
        // right one to take it from. Seeded here rather than read per frame because a change to
        // the owner's tuning tears this board down and rebuilds it (RemoteControlBoard's
        // _builtTuningRevision latch), which is the contract every other dial on this board is
        // already seated under; and an absent or pre-field record resolves to the shipped
        // Defaults.SelectionReady_Enabled inside RemoteBoardTuning itself, so the default has
        // exactly one home and this class holds no second copy of it.
        _selectionReadyOn = tuning.SelectionReadyOn;

        _root = new GameObject("InitiativeTrack").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The mount convention (PlayTray: bottom-centre of the initiative panel, grows UP above the
        // board's top edge) is reproduced verbatim — the mirror grows up from here, and so do the
        // fallback chips.
        _root.localPosition = layout.InitiativeMount;

        _mirror = new RemoteWidgetMirror("InitiativeTrack", _root,
            PlayTray.InitiativeMountWidth, PlayTray.InitiativeMountMaxHeight, Vector2.up,
            externallyShownBranch: IsEnemyInfoPopup);

        _fallbackRoot = new GameObject("Fallback").transform;
        _fallbackRoot.SetParent(_root, worldPositionStays: false);
        _fallbackRoot.localPosition = new Vector3(0f, ChipH * 0.5f, 0f);
        for (int i = 0; i < MaxChips; i++)
            _chips[i] = new Chip(_fallbackRoot);
        _fallbackRoot.gameObject.SetActive(false);

        _root.gameObject.SetActive(false);
    }

    /// <summary>Per-FRAME: keep the mirrored widget in step with the original, so the track's
    /// reorder slide and its animations play out on a peer's board instead of stepping at the 4 Hz
    /// content cadence — then re-assert every PER-VIEWER override on top, in a fixed order. The
    /// drive is a faithful copy of the LOCAL widget, so each override's job is to delete the
    /// observer's own state and re-apply the OWNER's: hover (record 16), vanilla's selection frame
    /// (record 23), the PLAYER BLOCK'S ORDER (record 27), the selection-phase "still has to choose"
    /// ring (record 27's owned mask + the replicated model), the mod's focus/turn rings (record 22)
    /// and the fallback strip's tint. No-op while the fallback is live.
    ///
    /// <para>THE ORDER OF THE PASSES IS LOAD-BEARING IN TWO PLACES, and only two. The ROW-X pass
    /// runs AFTER the hover pass, so the row's arrangement is the last word on a row x: vanilla's
    /// optional <c>hoverMovement</c> nudge writes an ABSOLUTE anchoredPosition and, on a prefab
    /// where the button's TargetRect happens to BE the entry root, would otherwise land on top of
    /// a permuted slot (in VR that nudge is zeroed by <c>InitiativePortraitPin</c> anyway, and on
    /// the shipped prefab the TargetRect is a child — belt and braces, not a live bug). The RINGS
    /// run LAST, so a ring is never seated on a node an earlier pass is still re-posing this
    /// frame.</para></summary>
    public void TickLive()
    {
        _mirror.TickLive();
        ApplyHoverOverrides();
        ApplySelectionOverride();
        ApplyOrderOverride();
        ApplySelectionGlow();
        ApplyFocusRings();
        ApplyFallbackFocusTint();
    }

    // ------------------------------------------------------------------ peer hover --
    // Defect pair of the initiative-mouseover report (hardware MP test 2026-08-04):
    //   (a) the peer's hover was NOWHERE — nothing of it rode the wire;
    //   (b) the LOCAL player's hover was EVERYWHERE — the mirror is a per-frame faithful copy of
    //       the local track widget, and vanilla's hover artefacts (the enemy round-action
    //       preview popup opening, the entry widening under minimization, the name label under
    //       the HIGHLIGHT key) are plain active-flag / rect state, which Pair.Apply copies like
    //       any other. So MY mouseover showed on the PEER's mirrored track.
    // Both are fixed by the same override pass: after every Sync, the hover-derived nodes of the
    // clone are FORCED from the PEER's synced hover (extras extension record 16) — the local
    // state the drive just copied is overwritten, and the peer's is applied. The overridden set:
    //   • the entry's info POPUP (MonsterBaseUI root) — active iff the peer hovers that entry
    //     with the popup open. The content shown is THIS client's copy of the public widget
    //     (already generated by the local track; vanilla renders it identically on every client).
    //   • the entry's NAME label — enabled iff the peer hovers the entry. Deliberately the one
    //     visible cue for PLAYER entries too (vanilla's own hover FX is uGUI material state a
    //     clone cannot carry, and the VR build blocks the player-card preview), so "the peer is
    //     pointing at X" is always readable.
    //   • the ENEMY entry's WIDTH under track minimization (NeedMinimizeEnemyInitiativeTrackAvatars):
    //     minimized unless the peer's hover has it maximized — undoing the local Maximize leak.
    //   • the entry's HOVER SCALE (and hoverMovement offset) — the FOURTH artefact of the same
    //     family, added after the MP hardware test 2026-08-07 ("wenn ich über meine eigene
    //     Initiativleiste hovere, wachsen die Initiative-Bilder auf dem Brett des Mitspielers
    //     mit"). Each entry's clickable avatarButton is an ExtendedButton whose ToggleHighlight
    //     (decompiled GH.Runtime/ExtendedButton.cs:445-478, reached from OnPointerEnter →
    //     OnHighlight) LeanTween.scales `overridedTargetRectScale ?? TargetRect` to
    //     highlightScaleFactor and back to Vector3.one on exit, plus an optional hoverMovement
    //     nudge on TargetRect.anchoredPosition; OnPointerDown/Up (ExtendedButton.cs:215/233) write
    //     further scales onto the same rect. Those are PLAIN TRANSFORM values, so
    //     RemoteWidgetMirror.Pair.Apply copied them like any other localScale/localPosition — the
    //     VR laser driving the LOCAL track's pointer events therefore grew the PEER's mirrored
    //     portraits too. The override forces the clone rect to the un-hovered rest pose and
    //     re-applies the grow only for the entry the PEER hovers (record 16), so the remote board
    //     grows exactly the portrait its own owner is pointing at and never the one I am.
    // KNOWN LIMIT, stated honestly: the entry's Mask.enabled flip that rides vanilla's
    // Maximize/Minimize is not re-driven (component enabled flags are not mirrored), so a
    // clipped avatar edge can differ by a few px while widths swap under minimization.

    /// <summary>Stable actor id of the entry the PEER hovers (0 = none), from record 16.</summary>
    private int _peerHoverActorId;

    /// <summary>True when the peer's hover has the entry's info popup open.</summary>
    private bool _peerHoverPopup;

    /// <summary>Change-gate for the hover log (actorId | popup flag; int.MinValue = never).</summary>
    private int _loggedHover = int.MinValue;

    /// <summary>Hand the owner's synced track hover in (called per frame by the board before
    /// <see cref="TickLive"/>). Cheap: two field writes + a change-gated log.</summary>
    public void SetPeerHover(int actorId, bool popupOpen)
    {
        _peerHoverActorId = actorId;
        _peerHoverPopup = actorId != 0 && popupOpen;
        int key = actorId != 0 ? actorId * 2 + (_peerHoverPopup ? 1 : 0) : -1;
        if (key == _loggedHover)
            return;
        _loggedHover = key;
        VRLog.Info("Net", "Remote initiative track hover: " +
                          (actorId != 0
                              ? $"actor {actorId}, popup {(_peerHoverPopup ? "OPEN" : "closed")} " +
                                "(extension record 16 — stable actor id; the popup content is " +
                                "this client's own copy of the public track widget). The LOCAL " +
                                "player's hover is stripped from this mirror — each board shows " +
                                "only what ITS owner does."
                              : "none (mirror renders the un-hovered base track)."));
    }

    // ------------------------------------------------------------------- peer focus --
    // User, verbatim: "Diese rötliche Farbe soll auch synchronisiert werden, wenn ein Mitspieler
    // gerade einen Character ausgewählt hat der nicht am Zug ist" and "Genauso in der
    // Initiativreihenfolge soll sichtbar sein wer gerade ausgewählt ist."
    //
    // The peer's CONTROL BOARD already wears the green/red turn frame (Net/RemoteFocusOutline);
    // this is the same state on their MIRRORED initiative track, so the two halves of one board
    // never tell different stories. Both are driven from extras record 22 through the SAME
    // Board.FocusCue palette the LOCAL track uses (Board/FocusDriver) — one blink clock, one pair
    // of colours, no divergence possible.
    //
    // WHAT IS DRAWN, and where:
    //   • MIRRORED path — a Board.UiRing (the shared SoftCueArt frame sprite, the same recipe the
    //     local track and the selection-phase cue use) parented under the CLONE of each entry's
    //     avatar portrait. Steady blue-white on the entry the peer is LOOKING at; green/red blink
    //     on the entry AT TURN when the peer owns it, steady gold otherwise.
    //   • FALLBACK path (mirror down, mod-drawn chips) — the chip PLATE is lerped toward the same
    //     colour by the same blink alpha. Tint only: chip transforms are written exclusively by
    //     the signature-gated repaint, never here.
    //
    // TRANSFORM DISCIPLINE (initiative row Y/Z leak, ModBuild 80): nothing in this block writes a
    // transform of a MIRRORED node or of a fallback chip. The rings are NEW GameObjects the mirror
    // knows nothing about — they are not in RemoteWidgetMirror's Pair table (which is index-paired
    // at clone-build time and only ever walks the SOURCE to validate), so Pair.Apply cannot touch
    // them and they cannot perturb it. They die with the clone on a rebuild and are re-resolved on
    // the very same RebuildStamp key the hover cache already uses.

    /// <summary>Stable actor id of the character the PEER is looking at (0 = none), record 22.</summary>
    private int _peerFocusActorId;

    /// <summary>
    /// Stable actor id of the character THE GAME IS WAITING ON as far as this peer is concerned —
    /// the entry that wears the attention ring, exactly as <c>FocusDriver.TickRings</c> rings
    /// <c>CharacterFocus.AttentionActor</c> on their own track.
    ///
    /// <para>It is NOT this client's own turn read any more. That was correct only while "waiting
    /// on" meant the turn; a pending DECISION is raised inside an enemy's action, where
    /// <c>Choreographer.CurrentPlayerActor</c> is null on every client, so the ring had nowhere to
    /// land. <c>CharacterFocus.AttentionIdForPeer</c> supplies the wire value when the peer owns
    /// the character and falls back to the replicated turn actor when they do not — see its doc for
    /// why that fallback is an identity rather than a guess.</para>
    /// </summary>
    private int _peerAttentionActorId;

    /// <summary>How the peer's focus relates to the character the game is waiting on — the colour of
    /// their board frame, and of the ring this track puts on the attention entry. Derived from
    /// record 22 alone (<c>CharacterFocus.MarkForPeer</c>), never from local state.</summary>
    private Board.FocusTurnMark _peerFocusMark;

    /// <summary>Change-gate for the focus log (focus id, attention id, mark).</summary>
    private (int Focus, int Attention, Board.FocusTurnMark Mark) _loggedFocus = (-1, -1, (Board.FocusTurnMark)(-1));

    /// <summary>Hand the owner's synced character focus in (called per frame by the board next to
    /// <see cref="SetPeerHover"/>). Cheap: three field writes + a change-gated log.</summary>
    public void SetPeerFocus(int focusActorId, int attentionActorId, Board.FocusTurnMark mark)
    {
        _peerFocusActorId = focusActorId;
        _peerAttentionActorId = attentionActorId;
        _peerFocusMark = mark;
        var key = (focusActorId, attentionActorId, mark);
        if (key == _loggedFocus)
            return;
        _loggedFocus = key;
        VRLog.Info("Net", "Remote initiative track focus: " +
                          (focusActorId != 0
                              ? $"looking at actor {focusActorId}, game waiting on actor " +
                                $"{attentionActorId}, mark {mark} (extension record 22 — the stable " +
                                "actor id of the character THIS peer is looking at, their " +
                                "'the character the game waits on is mine' bit and, when the two " +
                                "differ, that character's id). The mark comes off the record alone, " +
                                "so this mirror cannot disagree with the peer's own track about a " +
                                "decision their machine can see and this one cannot. Same FocusCue " +
                                "colours and blink phase as the local track — one palette, two surfaces."
                              : "none (no record 22 from this peer — the mirror rings nothing, which " +
                                "is exactly what a pre-record build shows)."));
    }

    // ---------------------------------------------------------------- peer selection frame --

    /// <summary>The stable ids of the entries the PEER's OWN track is framing (extension record
    /// 23). Only the first <see cref="_peerSelectionCount"/> entries are meaningful.</summary>
    private int[] _peerSelectionIds = System.Array.Empty<int>();

    private int _peerSelectionCount;

    /// <summary>Change-gate for the selection log (a cheap order-sensitive hash of the id set;
    /// int.MinValue = never logged).</summary>
    private int _loggedSelection = int.MinValue;

    /// <summary>Hand the owner's synced SELECTION FRAMES in (called per frame by the board next to
    /// <see cref="SetPeerHover"/> / <see cref="SetPeerFocus"/>). Cheap: two field writes and a
    /// change-gated log.</summary>
    public void SetPeerSelection(int[]? ids, int count)
    {
        _peerSelectionIds = ids ?? System.Array.Empty<int>();
        _peerSelectionCount = count > 0 && count <= _peerSelectionIds.Length ? count : 0;

        int key = _peerSelectionCount;
        for (int i = 0; i < _peerSelectionCount; i++)
            key = key * 31 + _peerSelectionIds[i];
        if (key == _loggedSelection)
            return;
        _loggedSelection = key;
        VRLog.Info("Net", "Remote initiative track selection: " +
                          (_peerSelectionCount > 0
                              ? $"{_peerSelectionCount} framed entr(y/ies) (extension record 23 — the " +
                                "stable ids of the entries THIS peer's own track is framing with " +
                                "vanilla's selectionObject, read off their live widget). Players, " +
                                "ENEMIES and objects alike; the LOCAL player's frame is forced off " +
                                "this mirror entirely."
                              : "none (no record 23 from this peer, or their own track shows no " +
                                "frame — either way the mirror draws none, which is honest; drawing " +
                                "MY frame on THEIR board is the bug this replaced)."));
    }

    /// <summary>True while <paramref name="actorId"/> is one of the entries the peer's own track is
    /// framing right now.</summary>
    private bool PeerFrames(int actorId)
    {
        if (actorId == 0)
            return false;
        for (int i = 0; i < _peerSelectionCount; i++)
        {
            if (_peerSelectionIds[i] == actorId)
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------ peer track ORDER --
    // DEFECT (a) OF THE 1:1 BOARD AUDIT, and the one this class's own doc got WRONG for four
    // builds: it listed "the ENTRY SET and their on-screen ORDER" as GLOBAL, zero wire. The set
    // is. The order is not, and vanilla says so in one branch —
    // InitiativeTrackActorBehaviour.CompareTo (decompiled GH.Runtime, :160-171):
    //
    //     if (FFSNetwork.IsOnline && PhaseManager.CurrentPhase.Type == SelectAbilityCardsOrLongRest
    //         && actor.IsPlayerByDefault() && other.Actor.IsPlayerByDefault()
    //         && (!actor.IsUnderMyControl || !other.Actor.IsUnderMyControl))
    //     { … the one that is NOT under my control sorts FIRST … }
    //
    // So during the card-selection phase EVERY client's track reads [the players I do not
    // control][the players I do], and this mirror — a per-frame clone of THIS client's widget —
    // put THIS client's arrangement on every peer's board. The mod had already written the fact
    // down twice, in NetProtocol.ExtIdTrackHover ("my index 3 can be your index 5") and in
    // InitiativeHoverSampler, which is exactly why the hover and selection records name entries by
    // stable id instead of by index. Nobody carried the ORDER itself.
    //
    // WHAT RIDES, AND WHY IT IS THE PIXEL. Record 27 carries the sender's own display order of the
    // PLAYER entries (ascending sibling index — where UpdateSortingOrder writes it) and a mask of
    // which of them they control. It is not re-derived here, although every INPUT is available: a
    // receiver could read FFSNet's replicated MyControllables for the ownership and the replicated
    // model for every initiative, and still be wrong, because CompareTo is an INCONSISTENT
    // comparator (two foreign players compare 0 while each compares -1 against one of mine) and
    // the outcome then depends on List<T>.Sort's introsort pivots and on the pre-sort input order.
    // That is the ModBuild-84 mistake — a plausible derivation, wrong in the states the user
    // watches most — and this is the third record in a row to refuse it.
    //
    // ENEMIES COST NOTHING: the branch requires BOTH sides to be player actors, so the enemy block
    // is identical on every client and is left to the mirror.

    /// <summary>The PEER's own on-screen order of their track's PLAYER entries, by stable actor id
    /// (extension record 27). Only the first <see cref="_peerOrderCount"/> entries are
    /// meaningful.</summary>
    private int[] _peerOrderIds = System.Array.Empty<int>();

    private int _peerOrderCount;

    /// <summary>Bit k = <see cref="_peerOrderIds"/>[k] is a character the PEER controls — the local
    /// flag no receiver can evaluate, and the gate on whose portrait the mirrored
    /// "still has to choose" ring may appear.</summary>
    private byte _peerOrderOwned;

    /// <summary>Change-gate for the order log (an order-sensitive hash of ids + mask).</summary>
    private int _loggedOrder = int.MinValue;

    /// <summary>Hand the owner's synced track ORDER in (called per frame by the board next to
    /// <see cref="SetPeerHover"/> / <see cref="SetPeerFocus"/> / <see cref="SetPeerSelection"/>).
    /// Cheap: three field writes and a change-gated log.</summary>
    public void SetPeerTrackOrder(int[]? ids, int count, byte ownedMask)
    {
        _peerOrderIds = ids ?? System.Array.Empty<int>();
        _peerOrderCount = count > 0 && count <= _peerOrderIds.Length ? count : 0;
        _peerOrderOwned = _peerOrderCount > 0 ? ownedMask : (byte)0;

        int key = _peerOrderCount * 31 + _peerOrderOwned;
        for (int i = 0; i < _peerOrderCount; i++)
            key = key * 31 + _peerOrderIds[i];
        if (key == _loggedOrder)
            return;
        _loggedOrder = key;
        VRLog.Info("Net", "Remote initiative track order: " +
                          (_peerOrderCount > 0
                              ? $"{_peerOrderCount} player entr(y/ies) in THIS PEER's display order, " +
                                $"owned mask 0x{_peerOrderOwned:X2} (extension record 27). Vanilla " +
                                "sorts player entries by IsUnderMyControl while online and in the " +
                                "card-selection phase (InitiativeTrackActorBehaviour.cs:160-171), so " +
                                "the arrangement this client's own track shows is NOT the one the " +
                                "board's owner sees; the mirrored row is permuted to theirs and the " +
                                "owned mask decides whose portraits may wear the amber " +
                                "'still has to choose' ring."
                              : "none (no record 27 from this peer, or they are outside the online " +
                                "card-selection phase — outside it every client's track sorts " +
                                "identically, so the mirrored arrangement is already correct and no " +
                                "override is applied)."));
    }

    /// <summary>True while <paramref name="actorId"/> is one of the entries record 27 names as
    /// being under the PEER's control.</summary>
    private bool PeerOwns(int actorId)
    {
        if (actorId == 0)
            return false;
        for (int i = 0; i < _peerOrderCount; i++)
        {
            if (_peerOrderIds[i] == actorId)
                return (_peerOrderOwned & (1 << i)) != 0;
        }
        return false;
    }

    /// <summary>Change-gate for the selection-override coverage line (one-shot per session).</summary>
    private bool _loggedSelectionCoverage;

    /// <summary>
    /// THE GAME'S OWN SELECTION FRAME, re-decided from the PEER's OWN TRACK — the third member of
    /// the "the mirror copied MY state onto THEIR board" family, after the hover popup/name (defect
    /// (b), 2026-08-04) and the hover GROW (2026-08-07).
    ///
    /// User, verbatim (2026-08-08): "auch die highlights der Initativreihenfolge auf dem remote
    /// board, so wie der Spieler sie sieht. Sie unterscheidet sich also ggf. von der Anzeige auf dem
    /// eigenen board."
    ///
    /// WHAT WAS WRONG, TWICE. Vanilla's selection is a plain GameObject the avatar switches on and
    /// off (<c>InitiativeTrackActorAvatar.ToggleSelection</c> → <c>selectionObject.SetActive</c>,
    /// driven by <c>InitiativeTrack.Select</c>/<c>Deselect</c>), and the LOCAL track is the only
    /// track this client has.
    /// <list type="number">
    /// <item>Before ModBuild 84, <c>RemoteWidgetMirror.Pair.Apply</c> copied <c>activeSelf</c>
    ///   verbatim, so MY selected portrait's frame stood on EVERY peer's mirrored track.</item>
    /// <item>ModBuild 84 stopped that and re-enabled the frame for the peer's record-22 focus id
    ///   instead. That fixed the wrong-SOURCE half and left a wrong-FACT half: the focus and the
    ///   frame are different things, and they come apart in the three commonest states at the
    ///   table — an ENEMY or a foreign player at turn (vanilla auto-selects
    ///   <c>Choreographer.m_CurrentActor</c>, InitiativeTrack.cs:620, while record 22 carries the
    ///   peer's own presented character or nothing), and a live MOD focus (which
    ///   <c>Board/Patches/SelectionGuardPatches</c> suppresses vanilla's whole <c>OnClick</c> for,
    ///   so their real frame never moves off the at-turn actor).</item>
    /// </list>
    ///
    /// WHAT IT DOES NOW. The frame is forced OFF on every mirrored entry and re-enabled only for
    /// the entries EXTENSION RECORD 23 names — the ids the peer's own <c>selectionObject</c>s were
    /// literally ACTIVE on when their packet was sampled. There is no re-derivation left to be
    /// wrong: whatever vanilla decided on their machine, for whatever reason, is what shows here,
    /// which is exactly the ruling. Vanilla's <c>IsTakingExtraTurn</c> suppression therefore needs
    /// no reimplementation either — it already ran on the SENDER, before the flag was read.
    ///
    /// AND THE ENEMY/OBJECT GAP IS CLOSED. The previous build recorded it as a standing limitation
    /// ("record 22 carries a CHARACTER focus, so a peer whose selection sits on an ENEMY shows no
    /// frame"). Record 23 carries whatever the track framed, in the same stable id space the hover
    /// record already uses for enemies, and <see cref="HoverNode.ActorId"/> is built from
    /// <c>NetFigures.StableActorId(beh.Actor)</c> for every entry — enemies included — so a peer
    /// who selects a monster now gets that monster framed on their mirrored track.
    ///
    /// A peer with NO record 23 (pre-record build, no scenario) gets NO selection frame at all
    /// rather than mine. That is the same choice the hover pass makes and for the same reason:
    /// showing nothing is honest, showing my state on their board is a lie.
    /// </summary>
    private void ApplySelectionOverride()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        int resolved = 0;
        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode node = _hoverNodes[i];
            if (node.Selection == null)
                continue;
            resolved++;
            bool want = PeerFrames(node.ActorId);
            if (node.Selection.activeSelf != want)
                node.Selection.SetActive(want);
        }

        if (!_loggedSelectionCoverage && _hoverNodes.Count > 0)
        {
            _loggedSelectionCoverage = true;
            VRLog.Info("Net", $"Remote initiative track selection override: {resolved}/{_hoverNodes.Count} " +
                              "entr(y/ies) resolved the game's own selectionObject on the clone. The LOCAL " +
                              "player's selection frame is forced OFF on this mirror and re-applied only for " +
                              "the entries the PEER's OWN track is framing (extension record 23, sampled from " +
                              "their live widget's active flag — not re-derived from their character focus, " +
                              "which is a different fact). Enemies and object entries included.");
        }
    }

    // ---- the row-x permutation's scratch, sized to the clone's own entry cap -----------------
    // Written and read inside ONE call of ApplyOrderOverride; fields only so the per-frame pass
    // allocates nothing.

    /// <summary>Indices into <see cref="_hoverNodes"/> of the PLAYER rows, in THIS client's
    /// on-screen order (ascending source sibling index).</summary>
    private readonly int[] _orderRow = new int[MaxChips];

    /// <summary>The row x each of those slots is at THIS frame, sampled before any write.</summary>
    private readonly float[] _orderSlotX = new float[MaxChips];

    /// <summary>For rank r, the <see cref="_hoverNodes"/> index of the entry the PEER has at rank r
    /// — resolved completely before a single transform is written, so a set mismatch bails with
    /// nothing half-applied.</summary>
    private readonly int[] _orderTarget = new int[MaxChips];

    /// <summary>Change-gate for the order-override diagnostic (a mismatch reason code; 0 = the
    /// override is applying cleanly).</summary>
    private int _loggedOrderState = int.MinValue;

    /// <summary>
    /// THE PLAYER BLOCK'S ON-SCREEN ORDER, re-decided from the PEER's own track — defect (a).
    ///
    /// <para>The mirror's drive copies each entry row's <c>anchoredPosition3D</c> off THIS client's
    /// widget, so the peer's board inherits THIS client's arrangement. During the online
    /// card-selection phase that arrangement is genuinely different from theirs
    /// (<c>InitiativeTrackActorBehaviour.CompareTo</c>:160-171 — foreign players first, mine last),
    /// so this pass PERMUTES the mirrored player rows into the order record 27 names.</para>
    ///
    /// <para>IT IS A PERMUTATION OF SLOTS, NOT A LAYOUT. The slots are read back off the clone
    /// itself — whatever x the drive just wrote for each player row — and re-dealt by the peer's
    /// rank. Nothing computes a position, so entry widths, the layout group's spacing, the enemy
    /// block and the panel fit are all untouched and cannot drift: the same set of x values goes
    /// back onto the same set of rows, only paired differently. ONLY x is written; y and z are
    /// carried through verbatim, which is the ModBuild-80 initiative-row discipline (that leak was
    /// exactly a y/z write on this row).</para>
    ///
    /// <para>IT BAILS WHOLE, NEVER PARTIALLY. The peer's id set and this client's player rows must
    /// correspond one-to-one; if they do not — a peer mid-round-transition, an exhausted hero that
    /// has landed on one machine and not yet the other, a party larger than the record's cap — the
    /// pass writes NOTHING and the board keeps the mirrored arrangement, which is the same "show
    /// the honest thing, never a guess" choice the hover and selection passes make. That is why
    /// the targets are resolved into <see cref="_orderTarget"/> in full before the first write.</para>
    ///
    /// <para>IT STANDS DOWN DURING THE REORDER SLIDE (<c>InitiativeTrack.isAnimating</c>). The
    /// slide is the one moment the row's x is owned by a tween rather than by the settled layout,
    /// and the two arrangements CONVERGE across it: the sort that starts the slide is the one that
    /// leaves the selection phase, after which every client's order is identical again. Re-dealing
    /// tweening positions would land the row in the wrong final slots, so the slide plays through
    /// as the mirror copies it. STATED LIMITATION: for those ~0.5 s a peer's board sees the slide
    /// START from this client's arrangement rather than the owner's. The destination, the duration
    /// and the easing are the same on every client (see the class doc's GLOBAL list), so only the
    /// first frames differ, and they differ into the correct final row.</para>
    /// </summary>
    private void ApplyOrderOverride()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        if (_peerOrderCount < 2)
        {
            LogOrderState(0, "no override wanted");
            return;                  // nothing to permute (or no record at all)
        }
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        try
        {
            InitiativeTrack track = InitiativeTrack.Instance;
            if (track != null && track.isAnimating)
            {
                LogOrderState(1, "the track is mid-reorder — the slide owns the row x this frame");
                return;
            }
        }
        catch { /* a torn-down singleton reads as "not animating"; the bails below still guard */ }

        // 1. THIS client's player rows, in ITS on-screen order (ascending SOURCE sibling index —
        //    the clone's own sibling order is frozen at Instantiate time and is not the display
        //    order). Selection sort over a handful of rows; no allocation.
        int n = 0;
        int taken = 0;
        while (n < _orderRow.Length)
        {
            int best = -1;
            int bestSibling = int.MaxValue;
            for (int i = 0; i < _hoverNodes.Count && i < 32; i++)
            {
                HoverNode node = _hoverNodes[i];
                if (!node.IsPlayer || node.Entry == null || node.SourceRow == null || node.ActorId == 0)
                    continue;
                if ((taken & (1 << i)) != 0)
                    continue;
                int sibling = node.SourceRow.GetSiblingIndex();
                if (sibling < bestSibling)
                {
                    bestSibling = sibling;
                    best = i;
                }
            }
            if (best < 0)
                break;
            taken |= 1 << best;
            _orderRow[n] = best;
            _orderSlotX[n] = _hoverNodes[best].Entry!.anchoredPosition3D.x;
            n++;
        }

        if (n != _peerOrderCount)
        {
            LogOrderState(2, $"this client shows {n} player row(s), the peer named " +
                             $"{_peerOrderCount} — the sets do not correspond, so the mirrored " +
                             "arrangement is left alone rather than half-permuted");
            return;
        }

        // 2. Resolve EVERY rank before writing anything.
        for (int r = 0; r < n; r++)
        {
            int want = _peerOrderIds[r];
            int found = -1;
            for (int k = 0; k < n; k++)
            {
                if (_hoverNodes[_orderRow[k]].ActorId == want)
                {
                    found = _orderRow[k];
                    break;
                }
            }
            if (found < 0)
            {
                LogOrderState(3, $"the peer named an entry (id {want}) this client's track does " +
                                 "not show — the mirrored arrangement is left alone");
                return;
            }
            _orderTarget[r] = found;
        }

        // 3. Deal the slots. x only.
        for (int r = 0; r < n; r++)
        {
            RectTransform? row = _hoverNodes[_orderTarget[r]].Entry;
            if (row == null)
                continue;
            Vector3 p = row.anchoredPosition3D;
            if (!Mathf.Approximately(p.x, _orderSlotX[r]))
                row.anchoredPosition3D = new Vector3(_orderSlotX[r], p.y, p.z);
        }
        LogOrderState(4, $"{n} player row(s) re-dealt into the PEER's own on-screen order");
    }

    /// <summary>Change-gated one-liner for what the order override is doing right now. The state
    /// code is the gate, so a stable state (the common one, "applying") logs once and then costs a
    /// single compare per frame.</summary>
    private void LogOrderState(int code, string what)
    {
        if (code == _loggedOrderState)
            return;
        _loggedOrderState = code;
        VRLog.Info("Net", $"Remote initiative track order override: {what} (extension record 27 — " +
                          "the PLAYER block's on-screen order is per-viewer during the online " +
                          "card-selection phase, InitiativeTrackActorBehaviour.cs:160-171; the " +
                          "ENEMY block never permutes and is left to the mirror).");
    }

    /// <summary>
    /// The BOARD OWNER's <c>[SelectionReady] Enabled</c> — whether they want the amber "still
    /// choosing" ring drawn AT ALL (record 28, wire id
    /// <see cref="NetProtocol.TuneSelectionReadyOn"/>, resolved as
    /// <see cref="RemoteBoardTuning.SelectionReadyOn"/>).
    ///
    /// <para>WHY IT HAD TO CROSS. Every other ingredient of this ring was already here — record
    /// 27's owned mask for whose characters they are, the replicated model for whether each has
    /// committed — so <see cref="ApplySelectionGlow"/> lit rings for an owner who had switched the
    /// cue OFF in their own options (<c>Board.SelectionReadyHighlighter</c>'s own gate): gone from
    /// their initiative bar, still burning around their figures on every peer's screen. The
    /// direction is the unusual one — the mirror drawing MORE than its owner, not less — and it is
    /// the same rule that put the other four overrides in this class here: a remote board is a
    /// picture of its OWNER's board, and that includes what they have chosen not to see. Shipped
    /// <c>true</c>, so the divergence only opens once somebody turns it off, which is why it
    /// survived four builds of this method.</para>
    ///
    /// <para>WHOSE DIAL THIS IS — SETTLED, NOT ASSUMED. The ring is drawn per FIGURE on a track
    /// that carries the whole scenario's actors, so the dial that governs an entry must be the one
    /// belonging to the client that CONTROLS THAT FIGURE. It is emphatically NOT this client's own
    /// <c>SelectionReadyHighlighter.EnabledEntry</c>: gating on the local dial would let one
    /// player's options silently suppress another player's ring, which is the same defect in the
    /// other direction. On this mirror the entry's controller and the board's owner are the same
    /// client BY CONSTRUCTION, and that is a check rather than a coincidence — step (2) of
    /// <see cref="ApplySelectionGlow"/> only ever lights a node <see cref="PeerOwns"/> accepts,
    /// i.e. one that record 27's owned mask names as being under THIS peer's control, and record
    /// 28 arrives from that same peer. If that gate is ever relaxed — a board lighting a ring for
    /// an actor a THIRD client controls — this field stops being the right answer and the dial
    /// would have to be resolved per actor from that actor's controller's own record 28. There is
    /// no such path today, and there is no per-actor tuning lookup to build it out of.</para>
    /// </summary>
    private readonly bool _selectionReadyOn;

    /// <summary>Change-gate for the selection-glow coverage line (one-shot per session).</summary>
    private bool _loggedGlowCoverage;

    /// <summary>
    /// THE SELECTION-PHASE "STILL HAS TO CHOOSE" RING, re-decided from the PEER's own set —
    /// defect (c), and the fourth member of the "the mirror copied MY state onto THEIR board"
    /// family after the hover popup, the hover grow and vanilla's selection frame.
    ///
    /// <para>WHAT WAS WRONG. <c>WorldUI.Surfaces.InitiativeSelectionGlow</c> hangs an amber
    /// <c>Image</c> under each portrait for every actor that still owes cards, and
    /// <c>Board.SelectionReadyHighlighter</c> feeds it a pending list filtered by
    /// <c>IsUnderControlOrSingle()</c> — i.e. the LOCAL player's characters and nobody else's. It
    /// is a plain Graphic under the track, so <c>Instantiate</c> cloned it and <c>Pair.Apply</c>
    /// drove its active flag and colour like any other: every peer's mirrored track wore the
    /// OBSERVER's rings, and the owner's — the only ones that board is a picture of — never
    /// appeared at all. Both halves are fixed here: the cloned ring is forced OFF, and a mod-owned
    /// one is lit for the OWNER's still-choosing characters.</para>
    ///
    /// <para>ZERO WIRE OF ITS OWN, and the check that establishes it. The fact splits in two.
    /// WHOSE characters they are is <c>CActor.IsUnderMyControl</c> — a local flag (CActor.cs:751,
    /// a plain settable bool restored from the save state), which is why record 27 already carries
    /// it as one mask byte alongside the order it had to send anyway. Whether a character has
    /// COMMITTED is NOT sent, because it does not have to be: vanilla's own
    /// <c>CPlayerActorExtensions.IsCardSelectionReady</c> tests
    /// <c>CharacterClass.RoundAbilityCards.Count &lt; 2 &amp;&amp; !CharacterClass.LongRest</c>,
    /// and that list is replicated LIVE through the selection phase
    /// (<c>ProxySetStartRoundDeckState</c> — the same list <c>RemoteControlBoard.SeatSlots</c>
    /// already draws a peer's played card BACKS from, and the same one
    /// <c>CPlayerActor.Initiative()</c> reads with no ownership gate). The only reason
    /// <c>IsCardSelectionReady</c> reports "ready" for a foreign actor is its explicit
    /// <c>(!FFSNetwork.IsOnline || IsUnderMyControl)</c> clause — a deliberate LOCAL gate over data
    /// that is present, not a data gap. So the receiver derives it.</para>
    ///
    /// <para>ONE FIELD ON A SHARED RECORD, WHICH IS THE ONE THING THE PARAGRAPH ABOVE DOES NOT
    /// BUY. Deriving WHAT the owner's cue would show is not the same as knowing WHETHER they want
    /// it shown: <c>SelectionReadyHighlighter</c> is behind a toggle, and every derivation here is
    /// blind to it, so an owner who switched it off had rings burning on every peer's screen. That
    /// gate is <see cref="_selectionReadyOn"/> — record 28's id 238, a field on the tuning record
    /// this board already resolves, defaulting to the shipped <c>true</c> so a pre-field peer
    /// mirrors exactly what they mirror today. See that field for whose dial it has to be and why
    /// the answer is not the local player's.</para>
    ///
    /// <para>NO DISCLOSURE. "That character has not committed yet" is already broadcast by vanilla
    /// twice over — the multiplayer ready tracker shows a per-character ready marker for the whole
    /// selection phase, and the hand tabs print every player's live "selected/2" count with no
    /// ownership gate (the argument <c>RemoteBoardFurniture</c>'s wanted-slot pulse already turns
    /// on). No card IDENTITY is read: a count and a boolean. The initiative NUMBER is untouched and
    /// still reads "?" for a foreign player, which is the user-ruled exception.</para>
    ///
    /// <para>ONE PALETTE, ONE CLOCK: the tint comes from
    /// <c>InitiativeSelectionGlow.PendingRingTint()</c> and the ±5 % breath from
    /// <c>Board.UiRing</c>'s shared <c>FocusCue.Phase</c>, which is the same 1.5 s unscaled sine
    /// the local cue's own scale pulse rides — so the mirrored ring and the local one cannot drift
    /// apart in colour, amplitude or phase. The ring is built at the local cue's own 8 px outset
    /// rather than <c>UiRing</c>'s wider default, so it is the same size as the original and nests
    /// INSIDE a focus ring on the same portrait instead of landing on top of one.</para>
    /// </summary>
    private void ApplySelectionGlow()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        // Record 27 exists only inside the online card-selection phase, which is exactly the
        // window SelectionReadyHighlighter runs its own cue in — so its ABSENCE clears every ring,
        // and no separate phase read is needed or wanted (a local phase read would be this
        // client's phase, not the owner's).
        bool window = _peerOrderCount > 0;

        // ...AND THE OWNER WANTS THE CUE AT ALL (record 28, id 238 — see _selectionReadyOn). Kept
        // separate from `window` on purpose: `window` still decides the coverage log below, so an
        // owner who has switched the ring off says so in the log instead of looking like a peer
        // who never entered the selection phase.
        bool draw = window && _selectionReadyOn;
        Color tint = draw ? WorldUI.Surfaces.InitiativeSelectionGlow.PendingRingTint() : default;

        int lit = 0;
        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode node = _hoverNodes[i];

            // (1) THE OBSERVER'S OWN RING NEVER STANDS ON A PEER'S BOARD. Forced off every frame,
            //     not once: Pair.Apply re-copies the source's active flag on every drive.
            if (node.LocalGlow != null && node.LocalGlow.activeSelf)
                node.LocalGlow.SetActive(false);

            // (2) THE OWNER'S RING.
            if (node.PendingRing == null)
                continue;
            bool want = draw && PeerOwns(node.ActorId) && StillChoosing(node.Player);
            if (want)
            {
                lit++;
                node.PendingRing.Apply(tint, breathe: true);
            }
            else
            {
                node.PendingRing.Apply(null, false);
            }
        }

        if (window && !_loggedGlowCoverage && _hoverNodes.Count > 0)
        {
            _loggedGlowCoverage = true;
            VRLog.Info("Net", $"Remote initiative track selection glow: {lit}/{_hoverNodes.Count} " +
                              "entr(y/ies) lit the amber 'still has to choose' ring for the BOARD " +
                              "OWNER's characters. The LOCAL player's own rings are forced OFF on " +
                              "this mirror (they rode the clone — InitiativeSelectionGlow builds " +
                              "them only for actors THIS client controls). Zero wire of its own: " +
                              "whose characters they are comes from record 27's owned mask, and " +
                              "whether each has committed is read from the replicated model " +
                              "(CCharacterClass.RoundAbilityCards / LongRest), never from the " +
                              "local IsCardSelectionReady, which is gated to this client. The " +
                              "owner's own [SelectionReady] Enabled is " +
                              (_selectionReadyOn
                                  ? "ON, so their pending characters wear the ring here exactly "
                                    + "as they do on their own initiative bar."
                                  : "OFF (record 28, id 238), so NO ring is lit on this board at "
                                    + "all — the owner has switched the cue out of their own "
                                    + "options and a mirror may not draw more than the board it "
                                    + "is a picture of. The LOCAL player's dial is deliberately "
                                    + "not consulted either way."));
        }
    }

    /// <summary>
    /// Does the game's REPLICATED model still owe this character a card selection? The commit half
    /// of vanilla's own <c>CPlayerActorExtensions.IsCardSelectionReady</c> —
    /// <c>RoundAbilityCards.Count &lt; 2 &amp;&amp; !LongRest</c> — minus its
    /// <c>IsUnderMyControl</c> clause, which is a local VISIBILITY gate rather than a data one.
    ///
    /// <para>The refinements vanilla folds in on top (a short rest selected in the local hand UI,
    /// <c>HaltMultiplayerProgression</c>, an open confirmation box) are LOCAL UI state on the
    /// owner's machine and are deliberately not reproduced: each of them can only make a character
    /// read "not ready" for a moment longer, so leaving them out can at worst clear a mirrored ring
    /// slightly early — never light one for a character who has finished. Failure suppresses:
    /// a half-torn actor reads as "done", i.e. no ring.</para>
    /// </summary>
    private static bool StillChoosing(CPlayerActor? actor)
    {
        try
        {
            CCharacterClass? cc = actor != null ? actor.CharacterClass : null;
            if (cc == null)
                return false;
            return !cc.LongRest && (cc.RoundAbilityCards?.Count ?? 2) < 2;
        }
        catch { return false; }
    }

    /// <summary>Change-gate for the local-ring suppression coverage line (one-shot per session).</summary>
    private bool _loggedLocalRingCoverage;

    /// <summary>
    /// Drive the focus/turn rings on the MIRRORED entries. Runs every frame (the blink is a
    /// continuous alpha), after <see cref="ApplyHoverOverrides"/> so a ring is never applied to a
    /// node the hover pass is still re-seating this frame. No-op on the fallback path.
    ///
    /// <para><b>AND IT DELETES THE OBSERVER'S OWN RINGS FIRST</b> — the fourth member of the "the
    /// mirror copied MY state onto THEIR board" family, after the hover popup/name (2026-08-04), the
    /// hover GROW (2026-08-07) and vanilla's selection frame (2026-08-08).</para>
    ///
    /// <para>User, verbatim (2026-08-09): "Der Rand der anzeigt welchen Character ich gerade
    /// ausgewählt habe, ist auch beim remote-board zu sehen bei MEINEN Characteren - das remote
    /// board sollte nur das Einzige was der Mitspieler sieht, nicht was ich sehe."</para>
    ///
    /// <para>WHY IT LEAKED. <c>Board.FocusDriver</c> builds its two ring families — the steady
    /// blue-white "the character I am looking at" ring and the green/red/gold at-turn ring — as
    /// plain <c>Image</c>s parented under the LOCAL entry's portrait (FocusDriver.cs:438,
    /// <c>UiRing.Build(PortraitRect(entry), …)</c>). This mirror is an <c>Instantiate</c> of that
    /// whole track, and <c>RemoteWidgetMirror.Pair.Apply</c> copies every non-root node's
    /// <c>activeSelf</c> AND its Graphic colour verbatim — so the rings were cloned and then driven,
    /// in my colour, on my characters, on every peer's board. That is the identical mechanism the
    /// amber selection-phase cue was fixed for one build earlier (see <see cref="ApplySelectionGlow"/>
    /// and <c>InitiativeSelectionGlow.LiveRingRectOf</c>); the focus rings were simply not in that
    /// pass's scope. The clones are resolved BY REFERENCE through
    /// <c>FocusDriver.LiveFocusRingRectOf</c> / <c>LiveTurnRingRectOf</c>, never by searching the
    /// clone for a GameObject name, so a rename cannot silently re-open the leak.</para>
    ///
    /// <para>The suppression is unconditional and runs BEFORE the peer's rings are applied: what a
    /// peer's board shows about focus is <see cref="_peerFocusActorId"/> /
    /// <see cref="_peerAttentionActorId"/> (record 22) and nothing else. It costs one active-flag
    /// compare per entry per frame — the flags are already false after the first frame, so the
    /// steady-state cost is the compare alone.</para>
    /// </summary>
    private void ApplyFocusRings()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        int suppressed = 0;
        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode n = _hoverNodes[i];
            if (n.LocalFocusRing != null)
            {
                suppressed++;
                if (n.LocalFocusRing.activeSelf)
                    n.LocalFocusRing.SetActive(false);
            }
            if (n.LocalTurnRing != null)
            {
                suppressed++;
                if (n.LocalTurnRing.activeSelf)
                    n.LocalTurnRing.SetActive(false);
            }
        }
        if (!_loggedLocalRingCoverage && suppressed > 0)
        {
            _loggedLocalRingCoverage = true;
            VRLog.Info("Net", $"Remote initiative track focus rings: {suppressed} cloned LOCAL ring(s) " +
                              "forced OFF on this mirror. They are Board.FocusDriver's own focus / " +
                              "at-turn outlines, which live under the LOCAL portraits and therefore " +
                              "rode RemoteWidgetMirror's Instantiate + Pair.Apply onto a peer's board " +
                              "(user: 'Der Rand der anzeigt welchen Character ich gerade ausgewählt " +
                              "habe, ist auch beim remote-board zu sehen bei MEINEN Characteren'). " +
                              "What this board draws about focus now comes from extension record 22 " +
                              "alone. Resolved by reference from the driver, never by name.");
        }

        // THE ATTENTION ENTRY IS THE PEER'S, not this client's turn read — a peer answering a
        // take-damage prompt during an ENEMY's action has no actor at turn on ANY machine, so the
        // local turn id would leave their mirrored ring nowhere to land while their own track rings
        // the deciding character. See _peerAttentionActorId.
        int attentionId = _peerAttentionActorId;
        // Two rings never stack on one portrait: when the peer's focus IS the character the game is
        // waiting on, the ATTENTION ring (which carries the urgent colour) wins — the same rule the
        // local track uses.
        Color? turnTint = _peerFocusMark != Board.FocusTurnMark.None
            ? Board.FocusCue.Tint(_peerFocusMark)
            : (attentionId != 0 ? Board.FocusCue.AtTurnRingTint() : (Color?)null);
        bool turnBreathes = _peerFocusMark != Board.FocusTurnMark.None;

        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode node = _hoverNodes[i];
            if (node.FocusRing == null || node.ActorId == 0)
                continue;
            bool isTurn = attentionId != 0 && node.ActorId == attentionId;
            bool isFocus = _peerFocusActorId != 0 && node.ActorId == _peerFocusActorId;

            if (isTurn && turnTint != null)
                node.FocusRing.Apply(turnTint, turnBreathes);
            else if (isFocus)
                node.FocusRing.Apply(Board.FocusCue.SelectionRingTint(), breathe: false);
            else
                node.FocusRing.Apply(null, false);
        }
    }

    /// <summary>
    /// The FALLBACK strip's rendering of the same state: the chip plate lerped toward the focus /
    /// turn colour by the shared blink alpha. TINT ONLY — position, scale and text belong to the
    /// signature-gated repaint in <see cref="RefreshFallback"/> and are not touched here (the
    /// blink would otherwise repaint the whole strip every frame, and a per-frame transform write
    /// on this row is exactly what the ModBuild-80 Y/Z leak fix exists to prevent).
    /// </summary>
    private void ApplyFallbackFocusTint()
    {
        if (Source != RemoteWidgetMirror.Fidelity.ModDrawn || _entries.Count == 0)
            return;

        // Same source as the mirrored path — the PEER's attention actor, so the fallback strip and
        // the mirrored track cannot disagree about which chip is lit.
        int attentionId = _peerAttentionActorId;
        Color? turnTint = _peerFocusMark != Board.FocusTurnMark.None
            ? Board.FocusCue.Tint(_peerFocusMark)
            : (attentionId != 0 ? Board.FocusCue.AtTurnRingTint() : (Color?)null);

        int n = Mathf.Min(_entries.Count, MaxChips);
        for (int i = 0; i < n; i++)
        {
            CActor a = _entries[i];
            int id = a != null ? ActorIdOf(a) : 0;
            if (id == 0)
            {
                _chips[i].SetFocusTint(null);
                continue;
            }
            if (attentionId != 0 && id == attentionId && turnTint != null)
                _chips[i].SetFocusTint(turnTint);
            // The FALLBACK strip has no selection FRAME to switch, so vanilla's frame (record 23)
            // and the mod's focus ring (record 22) collapse onto the one cue this path can draw:
            // the selection tint. Either fact lighting the chip is strictly better than a strip
            // that shows neither, and the mirrored path — which is what a real session runs —
            // keeps them visually distinct.
            else if ((_peerFocusActorId != 0 && id == _peerFocusActorId) || PeerFrames(id))
                _chips[i].SetFocusTint(Board.FocusCue.SelectionRingTint());
            else
                _chips[i].SetFocusTint(null);
        }
    }

    /// <summary>One override target: the clone-side nodes of one track entry whose state is
    /// hover-derived, resolved once per clone rebuild / content tick.</summary>
    private struct HoverNode
    {
        public int ActorId;

        /// <summary>Clone of the entry's <c>InitiativeTrackActorAvatar.selectionObject</c> — the
        /// GAME's own selection frame, which <c>Pair.Apply</c> copies from the LOCAL track and
        /// which <see cref="ApplySelectionOverride"/> therefore has to re-decide from the PEER's
        /// record-23 selection set. Null when the avatar carries no selection object.
        /// <para>No live <c>CActor</c> is cached beside it any more: vanilla's
        /// <c>IsTakingExtraTurn</c> exemption is not re-implemented here, because it already ran on
        /// the SENDER before the frame's active flag was sampled.</para></summary>
        public GameObject? Selection;

        /// <summary>Mod-owned focus/turn ring seated on the CLONE of this entry's avatar portrait
        /// (null while the portrait is not resolvable yet). Not a mirrored node — see the peer-focus
        /// block header for why the mirror cannot touch it and it cannot touch the mirror.</summary>
        public Board.UiRing? FocusRing;
        public GameObject? Popup;      // clone of the entry's MonsterBaseUI root (enemies only)

        /// <summary>Has <see cref="RemoteInitiativeTrack.LayoutMirroredPopup"/> already resolved
        /// this clone popup's layout? One-shot per CLONE REBUILD, because that is the lifetime of
        /// the geometry it writes — <c>EnsureHoverCache</c> rebuilds this list on every rebuild, so
        /// a fresh clone starts false again.</summary>
        public bool PopupLaidOut;
        public Graphic? Name;          // clone of the entry's name label
        public RectTransform? Entry;   // clone of the entry ROOT — the row this widens (enemies)
                                       // and the row ApplyOrderOverride re-deals the x of
        public float FullWidth;        // vanilla _startWidth
        public float MinWidth;         // _startWidth × config.MinimalEnemyAvatarDesiredWidth
        public bool HasWidths;

        // ---- the row's PLACE in the order (record 27) ----

        /// <summary>The LIVE entry transform, read per frame for its SIBLING INDEX — the display
        /// order vanilla's <c>UpdateSortingOrder</c> writes. The CLONE's sibling order is frozen at
        /// <c>Instantiate</c> time and re-sorts do not touch it, so it is not the display order and
        /// must never be read as one.</summary>
        public Transform? SourceRow;

        /// <summary>True for a player row. Vanilla's ownership branch requires BOTH sides to be
        /// player actors, so only these permute; the enemy block is global.</summary>
        public bool IsPlayer;

        /// <summary>The live <c>CPlayerActor</c> of a player row, for the ONE thing the selection
        /// glow derives locally: whether the game's replicated model still owes this character a
        /// card selection (<see cref="StillChoosing"/>). It is a MODEL read of a host-replicated
        /// list, not a re-derivation of anybody's per-viewer decision — the decision half
        /// (whose character it is) rides record 27's owned mask. Null on an enemy row.</summary>
        public CPlayerActor? Player;

        /// <summary>Clone of the LOCAL <c>InitiativeSelectionGlow</c> ring, when this client's own
        /// cue ever built one on this entry. Forced OFF every frame by
        /// <see cref="ApplySelectionGlow"/>: it is the OBSERVER's ring and must never stand on a
        /// peer's board. Null when this client never lit one here (the usual case for a character
        /// it does not control) — in which case there is nothing to suppress.</summary>
        public GameObject? LocalGlow;

        /// <summary>Clone of the LOCAL <c>Board.FocusDriver</c> focus ring — the steady blue-white
        /// "this is the character I am looking at" outline. Forced OFF every frame by
        /// <see cref="ApplyFocusRings"/>; see that method for the report it answers.</summary>
        public GameObject? LocalFocusRing;

        /// <summary>Clone of the LOCAL <c>Board.FocusDriver</c> at-turn ring (blinking green/red, or
        /// steady gold). The SECOND family on the same portrait — suppressing only the focus ring
        /// would leave half the same leak open.</summary>
        public GameObject? LocalTurnRing;

        /// <summary>The CLONE portrait this entry's mod-owned rings hang under — the key
        /// <see cref="_cloneRings"/> caches them by, so a content-cadence cache rebuild REUSES them
        /// instead of building a fresh pair on top of the old one. Null when the portrait is not
        /// resolvable yet.</summary>
        public RectTransform? Portrait;

        /// <summary>Mod-owned amber ring on the CLONE of this entry's portrait, lit for the BOARD
        /// OWNER's still-choosing characters. Built at the local cue's own outset so it is the same
        /// size as the original. Not a mirrored node — the same argument as
        /// <see cref="FocusRing"/>.</summary>
        public Board.UiRing? PendingRing;

        // ---- hover GROW (ExtendedButton.ToggleHighlight) ----
        public RectTransform? ScaleClone;   // clone of `overridedTargetRectScale ?? TargetRect`
        public float HighlightScale;        // vanilla highlightScaleFactor (<= 1 ⇒ no grow at all)
        // hoverMovement nudge: null MoveClone ⇒ this button has none (the common case).
        public RectTransform? MoveClone;    // clone of TargetRect
        public RectTransform? MoveSource;   // the LIVE TargetRect, read for its current pose
        public ExtendedButton? MoveButton;  // the LIVE button, read for its isMoved latch
        public Vector2 HoverMove;           // vanilla hoverMovement.xy
    }

    private readonly List<HoverNode> _hoverNodes = new(MaxChips);

    /// <summary>The mirror rebuild the cache was resolved against (-1 = never).</summary>
    private int _hoverCacheStamp = -1;

    /// <summary>One-shot: the hover-GROW override coverage line (how many entries got a scale
    /// clone resolved). Log anchor for the 2026-08-07 "local hover grew the peer's portraits"
    /// defect — zero coverage would mean the leak is still open.</summary>
    private bool _loggedGrowCoverage;

    /// <summary>Drop the cache so the next apply re-resolves (content tick / clone rebuild).</summary>
    private void InvalidateHoverCache() => _hoverCacheStamp = -1;

    /// <summary>The two MOD-OWNED rings this mirror hangs on one clone portrait.</summary>
    private readonly struct CloneRings
    {
        internal readonly Board.UiRing Focus;
        internal readonly Board.UiRing Pending;

        internal CloneRings(Board.UiRing focus, Board.UiRing pending)
        {
            Focus = focus;
            Pending = pending;
        }

        /// <summary>False once Unity has destroyed either GameObject (the portrait was pooled away
        /// under us) — the pair is then rebuilt rather than handed out.</summary>
        internal bool Alive => Focus.Alive && Pending.Alive;
    }

    /// <summary>
    /// Rings already built on a given CLONE portrait, so a cache rebuild REUSES them.
    ///
    /// <para><b>THE BUG THIS EXISTS FOR</b> — user 2026-08-09, verbatim: "ich sehe zusätzlich einen
    /// blauen Rahmen um alle Bilder der Charactere vom Mitspieler, nicht nur bei dem Character der
    /// der Mitspieler gerade ausgewählt hat."</para>
    ///
    /// <para>The hover cache is keyed on <c>RemoteWidgetMirror.RebuildStamp</c>, but
    /// <see cref="Refresh"/> ALSO invalidates it every content tick
    /// (<see cref="InvalidateHoverCache"/>), because an entry can swap its ACTOR without a
    /// structural rebuild. That second key is right for the ACTOR facts and catastrophically wrong
    /// for the rings: <c>UiRing.Build</c> creates a NEW GameObject under the clone portrait, the
    /// mirror's own <c>StructureMatches</c> only ever walks the SOURCE (RemoteWidgetMirror.cs:670)
    /// so a ring added to the CLONE never triggers a rebuild, and the previous pair was only
    /// dropped from a List — never destroyed and never switched off. So at the 4 Hz content cadence
    /// every entry grew two more rings per second, and any ring that happened to be LIT when the
    /// cache turned over was orphaned IN THE LIT STATE with nothing left that could ever clear it:
    /// <see cref="ApplyFocusRings"/> and <see cref="ApplySelectionGlow"/> only ever walk the CURRENT
    /// node list. Every portrait the peer had ever focused therefore kept a steady blue-white ring
    /// forever — which is exactly "a blue frame around ALL of them, not only the selected one" —
    /// on top of an unbounded GameObject/renderer leak on a board that is meant to be cheap.</para>
    ///
    /// <para>Keyed by the clone portrait rather than by the actor id on purpose: the ring's LIFETIME
    /// is the clone's, not the actor's. An entry that swaps actor keeps its portrait and simply
    /// re-targets the ring it already has, which is also why this costs no rebuild at all in the
    /// common case.</para>
    /// </summary>
    private readonly Dictionary<RectTransform, CloneRings> _cloneRings = new(MaxChips);

    /// <summary>The mirror rebuild <see cref="_cloneRings"/> was built against (-1 = never). A
    /// different value means the clone — and with it every ring — is gone.</summary>
    private int _cloneRingsStamp = -1;

    /// <summary>Scratch for <see cref="SweepCloneRings"/>; a field so the sweep allocates nothing.</summary>
    private readonly List<RectTransform> _cloneRingSweep = new(MaxChips);

    /// <summary>
    /// Hand <paramref name="node"/> the mod-owned rings for <paramref name="portraitClone"/>,
    /// building them only the FIRST time this clone portrait is seen. Reuse is the whole point —
    /// see <see cref="_cloneRings"/>.
    /// </summary>
    private void ResolveCloneRings(RectTransform? portraitClone, ref HoverNode node)
    {
        if (portraitClone == null)
            return; // portrait not resolvable yet — the next cache rebuild retries
        if (_cloneRings.TryGetValue(portraitClone, out CloneRings cached) && cached.Alive)
        {
            node.FocusRing = cached.Focus;
            node.PendingRing = cached.Pending;
            return;
        }
        Board.UiRing? focus = Board.UiRing.Build(portraitClone, "GloomhavenVR.RemoteFocusRing");
        // The OWNER's selection-phase ring, at the LOCAL cue's own 8 px outset so it is the same
        // size as the original and nests inside the focus ring above.
        Board.UiRing? pending = Board.UiRing.Build(
            portraitClone, "GloomhavenVR.RemoteSelectionGlow",
            WorldUI.Surfaces.InitiativeSelectionGlow.RingOutsetPixels);
        node.FocusRing = focus;
        node.PendingRing = pending;
        if (focus != null && pending != null)
            _cloneRings[portraitClone] = new CloneRings(focus, pending);
    }

    /// <summary>
    /// Destroy the rings of any clone portrait that has dropped out of the live node set.
    ///
    /// <para>Belt and braces rather than a live path: an entry can only leave the track by changing
    /// the SOURCE structure, which rebuilds the clone and empties the table wholesale. But a ring
    /// left standing on a portrait nobody drives again is precisely the failure mode
    /// <see cref="_cloneRings"/> was introduced to end, so the invariant is enforced instead of
    /// argued. Runs on a cache rebuild only (≤ a handful of entries, at the 4 Hz content cadence)
    /// and allocates nothing.</para>
    /// </summary>
    private void SweepCloneRings()
    {
        if (_cloneRings.Count == 0)
            return;
        _cloneRingSweep.Clear();
        foreach (KeyValuePair<RectTransform, CloneRings> kv in _cloneRings)
        {
            bool used = false;
            for (int i = 0; i < _hoverNodes.Count && !used; i++)
                used = ReferenceEquals(_hoverNodes[i].Portrait, kv.Key);
            if (used)
                continue;
            kv.Value.Focus.Destroy();
            kv.Value.Pending.Destroy();
            _cloneRingSweep.Add(kv.Key);
        }
        for (int i = 0; i < _cloneRingSweep.Count; i++)
            _cloneRings.Remove(_cloneRingSweep[i]);
        _cloneRingSweep.Clear();
    }

    /// <summary>
    /// Resolve the hover-derived clone nodes for every live track entry. Publicized game fields
    /// (<c>nameText</c>, <c>monsterBaseUI</c>, <c>_startWidth</c>, <c>_config</c>) — no
    /// reflection. Wrapped whole: a mid-rebuild track degrades to an empty cache (= no
    /// overrides this frame), never a throw inside the board tick.
    /// </summary>
    private void EnsureHoverCache()
    {
        if (_hoverCacheStamp == _mirror.RebuildStamp)
            return;
        _hoverCacheStamp = _mirror.RebuildStamp;
        if (_cloneRingsStamp != _mirror.RebuildStamp)
        {
            // The CLONE was rebuilt (or torn down). Every ring in the table was a CHILD of it and
            // died with it, so dropping the table IS the teardown — there is nothing left to
            // destroy, and keeping it would hand out dangling handles.
            _cloneRings.Clear();
            _cloneRingsStamp = _mirror.RebuildStamp;
        }
        _hoverNodes.Clear();
        try
        {
            InitiativeTrack track = InitiativeTrack.Instance;
            List<InitiativeTrackActorBehaviour>? ui = track != null ? track.actorsUI : null;
            if (ui == null)
                return;
            for (int i = 0; i < ui.Count; i++)
            {
                InitiativeTrackActorBehaviour beh = ui[i];
                if (beh == null || beh.Actor == null || beh.Avatar == null)
                    continue;
                var node = new HoverNode
                {
                    ActorId = NetFigures.StableActorId(beh.Actor),
                    // The row itself: its CLONE is what the order override re-deals, and its LIVE
                    // transform is the only place the display order can be read from.
                    Entry = _mirror.CloneOf(beh.transform) as RectTransform,
                    SourceRow = beh.transform,
                    IsPlayer = beh is InitiativeTrackPlayerBehaviour,
                    Player = beh.Actor as CPlayerActor,
                };

                // The LOCAL selection-phase ring, resolved BY REFERENCE from the cue that owns it
                // (never by searching the clone for a name) so it can be forced off on the mirror.
                RectTransform? localGlow =
                    WorldUI.Surfaces.InitiativeSelectionGlow.LiveRingRectOf(beh);
                Transform? localGlowClone = localGlow != null ? _mirror.CloneOf(localGlow) : null;
                node.LocalGlow = localGlowClone != null ? localGlowClone.gameObject : null;

                // The GAME's own selection frame on the CLONE (see ApplySelectionOverride).
                GameObject? selection = beh.Avatar.selectionObject;
                Transform? selectionClone = selection != null ? _mirror.CloneOf(selection.transform) : null;
                node.Selection = selectionClone != null ? selectionClone.gameObject : null;

                TMP_Text? name = beh.Avatar.nameText;
                Transform? nameClone = name != null ? _mirror.CloneOf(name.transform) : null;
                node.Name = nameClone != null ? nameClone.GetComponent<Graphic>() : null;

                // Focus / turn ring on the CLONE of the visible portrait — the avatar's only
                // RawImage, exactly the rect the LOCAL track's rings and the selection-phase cue
                // frame (the character face; every other graphic under the avatar is a TMP or an
                // Image). Rebuilt here rather than kept alive across rebuilds because the ring is
                // a CHILD of the clone and dies with it; RebuildStamp is the one key that knows.
                RectTransform? portrait = PortraitRectOf(beh);
                RectTransform? portraitClone = portrait != null
                    ? _mirror.CloneOf(portrait) as RectTransform
                    : null;
                node.Portrait = portraitClone;
                // The two mod-owned rings, REUSED across cache rebuilds — see ResolveCloneRings.
                ResolveCloneRings(portraitClone, ref node);

                // The LOCAL focus / at-turn rings, resolved BY REFERENCE from the driver that owns
                // them (Board.FocusDriver) exactly the way the amber cue above is, so they can be
                // forced off on the mirror. See ApplyFocusRings for the user report.
                RectTransform? localFocus = Board.FocusDriver.LiveFocusRingRectOf(beh);
                Transform? localFocusClone =
                    localFocus != null ? _mirror.CloneOf(localFocus) : null;
                node.LocalFocusRing = localFocusClone != null ? localFocusClone.gameObject : null;
                RectTransform? localTurn = Board.FocusDriver.LiveTurnRingRectOf(beh);
                Transform? localTurnClone = localTurn != null ? _mirror.CloneOf(localTurn) : null;
                node.LocalTurnRing = localTurnClone != null ? localTurnClone.gameObject : null;

                // Hover GROW: the exact rect ExtendedButton tweens (see the override note above).
                ExtendedButton? btn = beh.avatarButton;
                if (btn != null)
                {
                    RectTransform? scaleSrc = btn.overridedTargetRectScale != null
                        ? btn.overridedTargetRectScale
                        : btn.TargetRect;
                    node.ScaleClone = scaleSrc != null
                        ? _mirror.CloneOf(scaleSrc) as RectTransform
                        : null;
                    // The AUTHORED amplitudes, not the live fields: while the track is adopted into
                    // world space WorldUI.Surfaces.InitiativePortraitPin zeroes
                    // highlightScaleFactor/hoverMovement on the LOCAL buttons so vanilla's hover and
                    // press writers all land on the rect's rest value (user 2026-08-08: the
                    // portraits must not move on the Y axis, "auch nicht beim Anklicken"). That
                    // neutralisation is a LOCAL-presentation decision and must not delete the PEER's
                    // cue on their mirrored track, so the grow is re-decided from what the prefab
                    // authored. Falls through to the live field when nothing is pinned (flat/[Dev]
                    // play, or the track not adopted).
                    node.HighlightScale = WorldUI.Surfaces.InitiativePortraitPin.VanillaHighlightScale(btn);
                    Vector3 hoverMove = WorldUI.Surfaces.InitiativePortraitPin.VanillaHoverMovement(btn);
                    if (hoverMove != Vector3.zero && btn.TargetRect != null)
                    {
                        node.MoveClone = _mirror.CloneOf(btn.TargetRect) as RectTransform;
                        node.MoveSource = btn.TargetRect;
                        node.MoveButton = btn;
                        node.HoverMove = new Vector2(hoverMove.x, hoverMove.y);
                    }
                }

                if (beh is InitiativeTrackEnemyBehaviour enemy)
                {
                    MonsterBaseUI? popup = enemy.monsterBaseUI;
                    Transform? popupClone = popup != null ? _mirror.CloneOf(popup.transform) : null;
                    node.Popup = popupClone != null ? popupClone.gameObject : null;

                    // node.Entry (the row clone) is resolved for EVERY entry above — the order
                    // override needs it on player rows too — so only the widths are enemy-only.
                    Script.GUI.Configuration.InitiativeTrackConfigUI? cfg = enemy._config;
                    if (node.Entry != null && cfg != null && enemy._startWidth > 0f)
                    {
                        node.FullWidth = enemy._startWidth;
                        node.MinWidth = enemy._startWidth * cfg.MinimalEnemyAvatarDesiredWidth;
                        node.HasWidths = true;
                    }
                }
                _hoverNodes.Add(node);
            }

            SweepCloneRings();

            if (!_loggedGrowCoverage && _hoverNodes.Count > 0)
            {
                _loggedGrowCoverage = true;
                int grow = 0, move = 0;
                for (int i = 0; i < _hoverNodes.Count; i++)
                {
                    if (_hoverNodes[i].ScaleClone != null) grow++;
                    if (_hoverNodes[i].MoveClone != null) move++;
                }
                VRLog.Info("Net", $"Remote initiative track hover-grow override: {grow}/{_hoverNodes.Count} " +
                                  $"entr(y/ies) resolved their ExtendedButton scale rect ({move} with a " +
                                  "hoverMovement nudge). The LOCAL pointer's LeanTween grow is forced back " +
                                  "to Vector3.one on this mirror and re-applied only for the entry the PEER " +
                                  "hovers (record 16) — my own hover can no longer grow a peer's portraits.");
            }
        }
        catch
        {
            // No overrides this frame — and the next tick really must re-resolve, which the stamp
            // gate at the top of this method would otherwise refuse until the NEXT clone rebuild.
            // That line said "the next tick re-resolves" and, for as long as it stood alone, it was
            // not true. It matters more now than it did: the mirrored enemy-info popup's active flag
            // is no longer written by the drive at all (RemoteWidgetMirror.Pair.External), so
            // ApplyHoverOverrides is the ONLY thing that can put it away, and a cache stuck empty
            // would leave a peer's popup standing open over their board.
            _hoverNodes.Clear();
            InvalidateHoverCache();
        }
    }

    /// <summary>The VISIBLE portrait rect of a LIVE track entry — the avatar face, which is the
    /// avatar's only <c>RawImage</c>. Identical resolution to the local track's rings, so the two
    /// surfaces frame the same thing. Null while the avatar has not been pooled in yet.</summary>
    private static RectTransform? PortraitRectOf(InitiativeTrackActorBehaviour entry)
    {
        InitiativeTrackActorAvatar avatar = entry.Avatar;
        if (avatar == null)
            return null;
        RawImage[] raws = avatar.GetComponentsInChildren<RawImage>(includeInactive: false);
        for (int i = 0; i < raws.Length; i++)
        {
            if (raws[i] != null && raws[i].transform is RectTransform rt)
                return rt;
        }
        return null;
    }

    /// <summary>
    /// Force every hover-derived clone node to the PEER's synced hover state — after Sync, every
    /// frame the mirror is live. The drive re-copies the local state, this re-corrects it; all
    /// writes are change-gated, so an idle track costs a handful of compares.
    /// </summary>
    private void ApplyHoverOverrides()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        bool needMin = false;
        try
        {
            InitiativeTrack track = InitiativeTrack.Instance;
            needMin = track != null && track.NeedMinimizeEnemyInitiativeTrackAvatars;
        }
        catch { needMin = false; }

        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode node = _hoverNodes[i];
            bool hovered = _peerHoverActorId != 0 && node.ActorId == _peerHoverActorId;

            if (node.Name != null && node.Name.enabled != hovered)
                node.Name.enabled = hovered;

            if (node.Popup != null)
            {
                bool wantPopup = hovered && _peerHoverPopup;
                if (node.Popup.activeSelf != wantPopup)
                    node.Popup.SetActive(wantPopup);
                // ...and, the first time this clone's popup is actually ON SCREEN, make it resolve
                // its own layout. See LayoutMirroredPopup: the source is off, so nothing else ever
                // will. The latch is per CLONE REBUILD (EnsureHoverCache rebuilds the list), and it
                // is only taken when the rebuild could really run.
                if (wantPopup && !node.PopupLaidOut && node.Popup.activeInHierarchy)
                {
                    node.PopupLaidOut = LayoutMirroredPopup(node.ActorId, node.Popup);
                    _hoverNodes[i] = node;
                }
            }

            // HOVER GROW — re-decided from the PEER's hover, never copied.
            //
            // Vanilla's rest value is literally Vector3.one (ExtendedButton.ToggleHighlight's
            // inactive branch), so "not hovered by the peer" is a constant, not something read
            // back off the source: that is what makes this immune to the LOCAL pointer's
            // in-flight LeanTween AND to the extra OnPointerDown/Up scales, which all land on the
            // same rect. Z stays 1 exactly as vanilla writes it.
            if (node.ScaleClone != null)
            {
                float s = node.HighlightScale > 1f && hovered ? node.HighlightScale : 1f;
                Vector3 want = new(s, s, 1f);
                if (node.ScaleClone.localScale != want)
                    node.ScaleClone.localScale = want;
            }

            // The optional hoverMovement nudge on the SAME family of buttons. Its rest pose is
            // NOT a constant (the track relayouts every round), so it is derived from the live
            // source: strip vanilla's own offset while the LOCAL hover has it latched, then
            // re-add it only for the peer-hovered entry.
            if (node.MoveClone != null && node.MoveSource != null)
            {
                Vector2 rest = node.MoveSource.anchoredPosition;
                if (node.MoveButton != null && node.MoveButton.isMoved)
                    rest -= node.HoverMove;
                Vector2 want = hovered ? rest + node.HoverMove : rest;
                if (node.MoveClone.anchoredPosition != want)
                    node.MoveClone.anchoredPosition = want;
            }

            if (node.HasWidths && node.Entry != null)
            {
                // Vanilla's Maximize/Minimize, re-decided from the PEER's hover: under
                // minimization an enemy entry is wide only while its preview is open.
                float w = !needMin || (hovered && _peerHoverPopup) ? node.FullWidth : node.MinWidth;
                Vector2 sd = node.Entry.sizeDelta;
                if (!Mathf.Approximately(sd.x, w))
                    node.Entry.sizeDelta = new Vector2(w, sd.y);
            }
        }
    }

    // ------------------------------------------------- the mirrored enemy-info popup --

    /// <summary>
    /// The one branch of this mirror whose VISIBILITY this client does not decide — an enemy
    /// entry's <c>MonsterBaseUI</c> info popup, which <see cref="ApplyHoverOverrides"/> shows from
    /// the PEER's hover (record 16) while this client's own copy of it is switched off.
    ///
    /// <para>Handed to <see cref="RemoteWidgetMirror"/> at construction; see
    /// <c>RemoteWidgetMirror.Pair.External</c> for the whole derivation, and
    /// <see cref="LayoutMirroredPopup"/> for the other half of the fix.</para>
    /// </summary>
    private static bool IsEnemyInfoPopup(Transform node) => node.GetComponent<MonsterBaseUI>() != null;

    /// <summary>Per-clone cap on <see cref="LayoutMirroredPopup"/>'s line. Every line carries the
    /// running totals, so the cap costs detail and never the FACT — the "a cap that goes silent"
    /// ruling.</summary>
    private const int PopupLayoutLogCap = 8;

    /// <summary>At most this many captions are named individually in one line; the counts cover the
    /// rest. A truncated list is not an absence, so the totals are printed either way.</summary>
    private const int PopupLayoutLabelCap = 5;

    /// <summary>A caption averaging this many glyphs per line or fewer IS the collapse the user
    /// reported, not a wrap: an ability row legitimately runs to two or three lines, while "alle
    /// Buchstaben untereinander" is one or two glyphs on each of many.</summary>
    private const float PopupCollapseGlyphsPerLine = 2f;

    /// <summary>Clone the popup counters below belong to (<c>RemoteWidgetMirror.RebuildStamp</c>;
    /// <c>int.MinValue</c> = never). A fresh clone starts the tally again, because the geometry
    /// this method writes lives exactly as long as the clone does.</summary>
    private int _popupLayoutStamp = int.MinValue;
    private int _popupLayoutDone;       // popups laid out under the CURRENT clone
    private int _popupLayoutCollapsed;  // ...of which arrived with a collapsed caption
    private int _popupLayoutLogged;     // ...of which got a line

    /// <summary>
    /// MAKE THE MIRRORED POPUP RESOLVE ITS OWN LAYOUT, the first frame it is really on screen.
    ///
    /// <para><b>WHY IT IS NEEDED AT ALL.</b> See <c>RemoteWidgetMirror.Pair.External</c>: this
    /// branch is shown from the PEER's hover while the SOURCE is off, and uGUI runs no layout on an
    /// inactive object, so a card body that was regenerated and never opened on THIS client carries
    /// caption rects no layout has ever resolved — TMP then wraps after every glyph (user report
    /// 2026-09-05 item 14, <c>remote-generinfo-textproblem.jpg</c>). The mirror keeps the stock
    /// layout components alive inside the branch precisely so this call has something to run.</para>
    ///
    /// <para><b>WHY IT IS AN EXPLICIT CALL AND NOT LEFT TO <c>OnEnable</c>.</b> Activating the
    /// branch does dirty its layout groups, and Unity would rebuild them at the next canvas update
    /// on its own. Relying on that alone would make the fix depend on bookkeeping this code cannot
    /// read back; the explicit rebuild is one call, and the readback below is taken off what it
    /// produced rather than off what it intended.</para>
    ///
    /// <para><b>THE GUARD IS THE POINT.</b> <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c>
    /// strips disabled behaviours from its own work list, so a call while the board host is off is
    /// a silent no-op — the exact "a gated remedy never ran" shape this project has on file. The
    /// caller therefore only offers the popup once it is <c>activeInHierarchy</c>, and the latch is
    /// taken from THIS method's return value, so a refusal is retried next frame instead of being
    /// recorded as done.</para>
    ///
    /// <para>Returns whether the layout was actually resolved.</para>
    /// </summary>
    private bool LayoutMirroredPopup(int actorId, GameObject popup)
    {
        var rect = popup.transform as RectTransform;
        if (rect == null)
            return false;
        if (_popupLayoutStamp != _mirror.RebuildStamp)
        {
            _popupLayoutStamp = _mirror.RebuildStamp;
            _popupLayoutDone = 0;
            _popupLayoutCollapsed = 0;
            _popupLayoutLogged = 0;
        }

        int labels = 0, collapsedLabels = 0, worstLines = 0, drivers = 0;
        var report = new StringBuilder(192);
        try
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            // WHAT THE REBUILD HAD TO WORK WITH. Neutralize keeps the stock uGUI layout components
            // alive inside this branch and nothing else, so this count IS the premise of the fix:
            // rects that collapse are rects a LayoutGroup / ContentSizeFitter / LayoutElement
            // drives. Zero here with captions still collapsing falsifies that premise outright and
            // sends the next round to RemoteDialogOptions.LayoutCaption's remedy — WRITE the box
            // from measured numbers — instead of to a layout engine that has nothing to run.
            foreach (Behaviour driver in popup.GetComponentsInChildren<Behaviour>(includeInactive: true))
            {
                if (driver is LayoutGroup || driver is ContentSizeFitter || driver is LayoutElement)
                    drivers++;
            }

            // MEASURED, NOT ASSUMED — the discipline RemoteDialogOptions.LayoutCaption already logs
            // under: the line count comes back off TMP after a forced mesh update, so what is
            // printed is what was drawn.
            foreach (TMP_Text label in popup.GetComponentsInChildren<TMP_Text>(includeInactive: false))
            {
                if (label == null || string.IsNullOrEmpty(label.text))
                    continue;
                labels++;
                label.ForceMeshUpdate();
                TMP_TextInfo textInfo = label.textInfo;
                int lines = textInfo != null ? textInfo.lineCount : 0;
                int glyphs = textInfo != null ? textInfo.characterCount : 0;
                bool collapsed = lines >= 3 && glyphs <= lines * PopupCollapseGlyphsPerLine;
                if (collapsed)
                    collapsedLabels++;
                if (lines > worstLines)
                    worstLines = lines;
                if (labels > PopupLayoutLabelCap)
                    continue;
                if (report.Length > 0)
                    report.Append("; ");
                report.Append($"w {label.rectTransform.rect.width:F0}, font {label.fontSize:F1}, " +
                              $"lines={lines}, chars={glyphs}")
                      .Append(collapsed ? " COLLAPSED" : string.Empty);
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Mirrored enemy-info popup layout threw for actor {actorId} " +
                              $"({e.GetType().Name}: {e.Message}) — the clone keeps whatever rects " +
                              "Instantiate captured, which is what every build before this one had.");
            return false;
        }

        _popupLayoutDone++;
        if (collapsedLabels > 0)
            _popupLayoutCollapsed++;
        if (_popupLayoutLogged >= PopupLayoutLogCap)
            return true;
        _popupLayoutLogged++;

        // HW-VERIFY: grep MIRRORED ENEMY INFO LAYOUT — one line per enemy popup per clone rebuild,
        // naming each caption's rect WIDTH, its fitted font size and its MEASURED line count. The
        // collapse the user reported is then a number ("lines=20, chars=20 COLLAPSED"), not a
        // screenshot.
        // FALSIFIER, in two halves. "0 collapsed" on every line while the headset still shows a
        // column of letters means this rebuild did not reach the popup the eye sees — look at
        // whether ApplyHoverOverrides is showing a clone from an OLDER RebuildStamp than the one
        // measured here, or whether the card on screen is the LOCAL EnemyRevealSurface rather than
        // this mirror, NOT at the caption arithmetic. "0 stock layout driver(s) kept" WITH a
        // collapse means the premise is wrong instead: these rects are not layout-driven, so no
        // layout engine can resolve them and the remedy is RemoteDialogOptions.LayoutCaption's —
        // WRITE the caption box from measured numbers. "0 caption(s)" means the branch was empty at
        // the moment it was shown.
        VRLog.Note("Net", $"MIRRORED ENEMY INFO LAYOUT: actor {actorId}'s info popup laid ITSELF " +
                          $"out on the clone — {labels} caption(s), {collapsedLabels} collapsed, " +
                          $"worst {worstLines} line(s), {drivers} stock layout driver(s) kept " +
                          $"[{report}]. " +
                          $"{_popupLayoutDone} popup(s) laid out under this clone, " +
                          $"{_popupLayoutCollapsed} of them with a collapsed caption" +
                          (_popupLayoutLogged >= PopupLayoutLogCap
                              ? " (line cap reached — the totals still count every one)"
                              : string.Empty) + ". " +
                          "The SOURCE popup is this client's own MonsterBaseUI and is switched OFF " +
                          "whenever this client is not hovering that enemy, so no layout ever runs " +
                          "on it and the drive skips its whole branch; the clone's own uGUI layout " +
                          "is what resolves these rects. A caption averaging " +
                          $"{PopupCollapseGlyphsPerLine:F0} glyph(s) per line or fewer is the " +
                          "reported defect (one letter per line), never an ordinary wrap.");
        return true;
    }

    /// <summary>Content-cadence refresh: mirror the real widget when it exists, else repaint the
    /// fallback chips on an actual change. Wrapped whole — a half-initialised scenario must degrade
    /// to an empty track, never throw.</summary>
    public void Refresh()
    {
        InitiativeTrack? track = null;
        try { track = InitiativeTrack.Instance; }
        catch { track = null; }

        if (_mirror.Refresh(track != null ? track.transform : null))
        {
            Source = RemoteWidgetMirror.Fidelity.MirroredWidget;
            _mirror.SetShown(true);
            if (_fallbackRoot.gameObject.activeSelf)
                _fallbackRoot.gameObject.SetActive(false);
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            Count = CountGameEntries(track);
            _signature = string.Empty; // a later fallback must repaint from scratch
            // Content cadence: an entry can swap its ACTOR without a structural rebuild
            // (vanilla reuses entry objects across rounds), which the rebuild-stamp key cannot
            // see — so the hover cache re-resolves here too, then the overrides re-assert on
            // the freshly synced clone (the same frame must never show the local hover, and — the
            // same argument, the same family — never the local SELECTION FRAME either: a rebuild
            // lands with the LOCAL widget's active flags copied verbatim, so the frame is
            // re-decided from the peer's record-23 set before anything can be presented).
            InvalidateHoverCache();
            ApplyHoverOverrides();
            ApplySelectionOverride();
            return;
        }

        Source = RemoteWidgetMirror.Fidelity.ModDrawn;
        _mirror.SetShown(false);
        RefreshFallback();
    }

    public void Destroy() => _mirror.Destroy();

    /// <summary>Entry count of the LIVE game track (the number the mirrored picture is showing) —
    /// diagnostics only, and null-safe for the frames where the track is mid-rebuild.</summary>
    private static int CountGameEntries(InitiativeTrack? track)
    {
        try
        {
            List<InitiativeTrackActorBehaviour>? ui = track != null ? track.actorsUI : null;
            if (ui == null)
                return 0;
            int n = 0;
            for (int i = 0; i < ui.Count; i++)
                if (ui[i] != null && ui[i].gameObject.activeSelf)
                    n++;
            return n;
        }
        catch { return 0; }
    }

    // ------------------------------------------------------------------ fallback --

    /// <summary>The pre-mirror chip strip, kept for the frames where the game track does not exist.
    /// Re-reads the entries + initiatives and repaints on an actual change.</summary>
    private void RefreshFallback()
    {
        _entries.Clear();
        _seen.Clear();
        try
        {
            // The game track's own entries in their LIVE display order when it exists but could not
            // be mirrored; the model derivation when it does not.
            if (!CollectFromGameTrack())
            {
                Collect();
                _entries.Sort(static (a, b) => SortKey(a).CompareTo(SortKey(b)));
            }
        }
        catch { _entries.Clear(); }

        var sb = new StringBuilder(96);
        for (int i = 0; i < _entries.Count; i++)
            sb.Append(Text(_entries[i])).Append(':').Append(InitiativeLabel(_entries[i])).Append(';');
        // The PEER's hover is part of the painted state (the hovered chip is tinted), so it is
        // part of the repaint key — a hover change must repaint even when the entries did not.
        sb.Append('#').Append(_peerHoverActorId);
        string sig = sb.ToString();
        if (sig == _signature)
            return;
        _signature = sig;

        Count = _entries.Count;
        bool any = Count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);
        if (_fallbackRoot.gameObject.activeSelf != any)
            _fallbackRoot.gameObject.SetActive(any);

        float chipW = Mathf.Min(MaxChipW, Count > 0 ? Width / Count : MaxChipW);
        float left = -(Count - 1) * 0.5f * chipW;
        for (int i = 0; i < MaxChips; i++)
        {
            if (i >= Count)
            {
                _chips[i].SetShown(false);
                continue;
            }
            CActor a = _entries[i];
            _chips[i].Set(new Vector3(left + i * chipW, 0f, 0f), chipW, Text(a), InitiativeLabel(a),
                a is CPlayerActor, hovered: ActorIdOf(a) != 0 && ActorIdOf(a) == _peerHoverActorId);
        }
    }

    /// <summary>
    /// Read <c>InitiativeTrack.Instance.actorsUI</c> — the exact entries the shared 2D track
    /// shows — in their ON-SCREEN order (ascending sibling index; vanilla's sort writes the
    /// display order into the sibling order). Returns false when the game track is unavailable
    /// or empty so the caller can fall back to the model derivation.
    /// </summary>
    private bool CollectFromGameTrack()
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
            return false;
        List<InitiativeTrackActorBehaviour> ui = track.actorsUI;
        if (ui == null || ui.Count == 0)
            return false;

        _gameEntries.Clear();
        for (int i = 0; i < ui.Count; i++)
        {
            InitiativeTrackActorBehaviour beh = ui[i];
            if (beh == null || !beh.gameObject.activeSelf || beh.Actor == null)
                continue;
            _gameEntries.Add(beh);
        }
        if (_gameEntries.Count == 0)
            return false;

        // Display order = sibling order (left → right = acting order on the shared track).
        _gameEntries.Sort(static (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        for (int i = 0; i < _gameEntries.Count && _entries.Count < MaxChips; i++)
            _entries.Add(_gameEntries[i].Actor);
        return true;
    }

    /// <summary>FALLBACK: filter + dedupe like <c>InitiativeTrack.UpdateInitiativeTrack</c> (alive
    /// actors, one entry per <c>CClass</c>, no hero summons and no prop/object actors) — used only
    /// while the game track itself does not exist.</summary>
    private void Collect()
    {
        CScenario? scenario = ScenarioManager.Scenario;
        List<CActor>? actors = scenario?.AllAliveActors;
        if (actors == null)
            return;
        for (int i = 0; i < actors.Count && _entries.Count < MaxChips; i++)
        {
            CActor a = actors[i];
            if (a == null || a is CHeroSummonActor || a is CObjectActor)
                continue;
            CClass cls = a.Class;
            if (cls != null)
            {
                if (_seen.Contains(cls))
                    continue;
                _seen.Add(cls);
            }
            _entries.Add(a);
        }
    }

    /// <summary>Sort key: the actor's initiative, or a value past every real initiative when it is
    /// not knowable (gated player / monster card not yet revealed).</summary>
    private static int SortKey(CActor a)
    {
        string label = InitiativeLabel(a);
        return int.TryParse(label, out int v) ? v : int.MaxValue;
    }

    /// <summary>Vanilla's own display rule for the number: a foreign player reads "?" for exactly
    /// the frames <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> does (which is precisely
    /// <see cref="RevealGate.ShowRoundCardFronts"/>), and a monster follows vanilla's numeric rule
    /// (&lt;0 blank, 0 → "?", else the number).</summary>
    private static string InitiativeLabel(CActor a)
    {
        try
        {
            if (a is CPlayerActor pa && !RevealGate.ShowRoundCardFronts(pa))
                return "?";
            int v = a.Initiative();
            if (v < 0)
                return string.Empty;
            return v == 0 ? "?" : v.ToString();
        }
        catch { return "?"; }
    }

    /// <summary>The stable actor id the fallback chips match the peer's synced hover (record 16)
    /// against — the shared ActorGuid hash (<see cref="NetFigures.StableActorId"/>), NOT the
    /// per-class <c>CActor.ID</c>: two enemy classes' representative standees can share the same
    /// legacy ID and would tint the wrong chip.</summary>
    private static int ActorIdOf(CActor a) => NetFigures.StableActorId(a);

    /// <summary>Localized actor name — the identical <c>ActorLocKey()</c> string vanilla's own entry
    /// puts in its (hover-only) name label.</summary>
    private static string Text(CActor a)
    {
        try
        {
            string key = a.ActorLocKey() ?? string.Empty;
            return key.Length == 0 ? "?" : Loc.Game(key, key);
        }
        catch { return "?"; }
    }

    /// <summary>One FALLBACK track entry: a tinted plate, the actor name and the initiative number.
    /// Inert — vanilla's entry is a button (character switch / card overview); this one is three
    /// quads. Only ever visible while the real widget cannot be mirrored.</summary>
    private sealed class Chip
    {
        private readonly Transform _root;
        private readonly MeshRenderer _plate;
        private readonly Material _plateMat;
        private readonly TextMeshPro _name;
        private readonly TextMeshPro _initiative;

        private static readonly Color PlayerTint = new(0.20f, 0.26f, 0.20f, 0.95f);
        private static readonly Color EnemyTint = new(0.28f, 0.16f, 0.15f, 0.95f);

        public Chip(Transform parent)
        {
            _root = new GameObject("Chip").transform;
            _root.SetParent(parent, worldPositionStays: false);

            _plateMat = BoardVisual.Unlit(PlayerTint);
            // MR: alpha-only opacify — Set() keeps retinting the RGB (player/enemy), which the
            // helper deliberately leaves alone.
            WorldUI.MrBacking.Opacify(_plateMat);
            _plate = BoardVisual.Quad(_root, "Plate", new Vector2(MaxChipW * 0.94f, ChipH), _plateMat);
            _plate.transform.localPosition = new Vector3(0f, 0f, 0.001f);

            _initiative = RemoteBoardContent.Label(_root, "Initiative", new Vector3(0f, 0.010f, 0f),
                new Vector2(MaxChipW * 0.8f, 0.024f), 0.06f,
                new Color(1f, 0.93f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
            _name = RemoteBoardContent.Label(_root, "Name", new Vector3(0f, -0.014f, 0f),
                new Vector2(MaxChipW * 0.92f, 0.018f), 0.030f,
                new Color(0.86f, 0.83f, 0.75f), TextAlignmentOptions.Center);

            // DRAW ORDER is NOT set here any more: the chips ARE the docked initiative widget
            // while the mirror is down, and they sit at the same board-local depth it does, so the
            // owning board's cluster sweep seats them at the same tier by measuring exactly that
            // (BoardVisual.AdoptBoardOrder). Text stays in front of its plate via the 1 mm z
            // offset (equal orders fall back to distance), unchanged from before.

            _root.gameObject.SetActive(false);
        }

        public void SetShown(bool shown)
        {
            if (_root.gameObject.activeSelf != shown)
                _root.gameObject.SetActive(shown);
        }

        /// <summary>Peer-hover tint: the base plate colour lifted toward the shared telegraph
        /// gold — the fallback strip's rendering of extras record 16 (the mirror shows the real
        /// widget's own hover artefacts instead).</summary>
        private static readonly Color HoverTint = new(1f, 0.85f, 0.3f, 0.95f);

        /// <summary>The plate colour the signature-gated repaint last decided (base ± peer hover).
        /// <see cref="SetFocusTint"/> always lerps from THIS, never from the live material colour,
        /// so a per-frame blink can never compound into a washed-out plate.</summary>
        private Color _restColor = PlayerTint;

        /// <summary>The focus tint currently applied (change-gated: an idle chip costs one compare).</summary>
        private Color? _focusTint;

        public void Set(Vector3 localPos, float chipW, string name, string initiative, bool player,
            bool hovered = false)
        {
            SetShown(true);
            _root.localPosition = localPos;
            _plate.transform.localScale = new Vector3(chipW * 0.94f, ChipH, 1f);
            Color baseTint = player ? PlayerTint : EnemyTint;
            _restColor = hovered ? Color.Lerp(baseTint, HoverTint, 0.45f) : baseTint;
            _plateMat.color = _restColor;
            _focusTint = null; // the repaint just wrote the rest colour; the next tick re-applies
            RemoteBoardContent.SetText(_name, name);
            RemoteBoardContent.SetText(_initiative, initiative);
        }

        /// <summary>
        /// Peer character-focus / turn tint (extras record 22) — the FALLBACK strip's rendering of
        /// the ring the mirrored path draws. The tint's ALPHA is the shared
        /// <c>Board.FocusCue</c> blink, used here as the LERP AMOUNT, so a fallback chip pulses in
        /// the same phase and the same two colours as every other outline in the feature. Null
        /// restores the repaint's own colour.
        ///
        /// <para>TINT ONLY: this writes a material colour and nothing else. The chip's transform
        /// belongs to <c>RefreshFallback</c>'s signature-gated repaint — a per-frame transform
        /// write on this row is precisely what the ModBuild-80 initiative-row Y/Z leak fix
        /// exists to prevent.</para>
        /// </summary>
        public void SetFocusTint(Color? tint)
        {
            if (_focusTint == null && tint == null)
                return;
            if (_focusTint != null && tint != null && _focusTint.Value == tint.Value)
                return;
            _focusTint = tint;
            if (tint == null)
            {
                _plateMat.color = _restColor;
                return;
            }
            Color c = Color.Lerp(_restColor, new Color(tint.Value.r, tint.Value.g, tint.Value.b, _restColor.a),
                                 Mathf.Clamp01(tint.Value.a));
            c.a = _restColor.a; // the plate's own opacity is MrBacking's business, not the cue's
            _plateMat.color = c;
        }
    }
}

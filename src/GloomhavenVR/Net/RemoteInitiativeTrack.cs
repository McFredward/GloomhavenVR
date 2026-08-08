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
/// cloning the game's own track brings the portraits, the class colours, the initiative discs, the
/// selection frame, the hover state and the reorder animation across for free.
///
/// ─── WHAT IT DRAWS NOW ─────────────────────────────────────────────────────────────────────────
/// PRIMARY — <see cref="RemoteWidgetMirror"/> over <c>InitiativeTrack.Instance.transform</c>: the
/// REAL widget, cloned once and puppeteered per frame from the original (see that class for why the
/// clone runs none of the game's code and can never be interacted with). This is the same single
/// track instance the local board docks, so it is by construction the same ordering, the same
/// numbers and the same "?"s the owner sees — including vanilla's own online gate
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
/// <remarks>CLASSIFICATION: GLOBAL — ZERO wire. The mirrored widget is a scenario-wide singleton the
/// local client already renders; the fallback's actor LIST is
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

    public RemoteInitiativeTrack(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("InitiativeTrack").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The mount convention (PlayTray: bottom-centre of the initiative panel, grows UP above the
        // board's top edge) is reproduced verbatim — the mirror grows up from here, and so do the
        // fallback chips.
        _root.localPosition = layout.InitiativeMount;

        _mirror = new RemoteWidgetMirror("InitiativeTrack", _root,
            PlayTray.InitiativeMountWidth, PlayTray.InitiativeMountMaxHeight, Vector2.up);

        _fallbackRoot = new GameObject("Fallback").transform;
        _fallbackRoot.SetParent(_root, worldPositionStays: false);
        _fallbackRoot.localPosition = new Vector3(0f, ChipH * 0.5f, 0f);
        for (int i = 0; i < MaxChips; i++)
            _chips[i] = new Chip(_fallbackRoot);
        _fallbackRoot.gameObject.SetActive(false);

        _root.gameObject.SetActive(false);
    }

    /// <summary>Per-FRAME: keep the mirrored widget in step with the original, so the track's
    /// reorder slide and selection pop play out on a peer's board instead of stepping at the 4 Hz
    /// content cadence — then re-assert the HOVER OVERRIDES on top (the drive is a faithful copy
    /// of the LOCAL widget, and hover is per-player state; see <see cref="ApplyHoverOverrides"/>).
    /// No-op while the fallback is live.</summary>
    public void TickLive()
    {
        _mirror.TickLive();
        ApplyHoverOverrides();
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

    /// <summary>How the peer's focus relates to the character at turn — the colour of their board
    /// frame, and of the ring this track puts on the at-turn entry.</summary>
    private Board.FocusTurnMark _peerFocusMark;

    /// <summary>Change-gate for the focus log (actorId | mark; int.MinValue = never).</summary>
    private int _loggedFocus = int.MinValue;

    /// <summary>Hand the owner's synced character focus in (called per frame by the board next to
    /// <see cref="SetPeerHover"/>). Cheap: two field writes + a change-gated log.</summary>
    public void SetPeerFocus(int focusActorId, Board.FocusTurnMark mark)
    {
        _peerFocusActorId = focusActorId;
        _peerFocusMark = mark;
        int key = focusActorId != 0 ? focusActorId * 4 + (int)mark : -1;
        if (key == _loggedFocus)
            return;
        _loggedFocus = key;
        VRLog.Info("Net", "Remote initiative track focus: " +
                          (focusActorId != 0
                              ? $"actor {focusActorId}, turn mark {mark} (extension record 22 — the " +
                                "stable actor id of the character THIS peer is looking at, plus their " +
                                "local-only 'the actor at turn is mine' bit; whose turn it is, this " +
                                "client reads for itself). Same FocusCue colours and blink phase as " +
                                "the local track — one palette, two surfaces."
                              : "none (no record 22 from this peer — the mirror rings nothing, which " +
                                "is exactly what a pre-record build shows)."));
    }

    /// <summary>
    /// Drive the focus/turn rings on the MIRRORED entries. Runs every frame (the blink is a
    /// continuous alpha), after <see cref="ApplyHoverOverrides"/> so a ring is never applied to a
    /// node the hover pass is still re-seating this frame. No-op on the fallback path.
    /// </summary>
    private void ApplyFocusRings()
    {
        if (Source != RemoteWidgetMirror.Fidelity.MirroredWidget)
            return;
        EnsureHoverCache();
        if (_hoverNodes.Count == 0)
            return;

        int turnId = Board.CharacterFocus.TurnActorId;
        // Two rings never stack on one portrait: when the peer's focus IS the actor at turn the
        // TURN ring (which carries the urgent colour) wins — the same rule the local track uses.
        Color? turnTint = _peerFocusMark != Board.FocusTurnMark.None
            ? Board.FocusCue.Tint(_peerFocusMark)
            : (turnId != 0 ? Board.FocusCue.AtTurnRingTint() : (Color?)null);
        bool turnBreathes = _peerFocusMark != Board.FocusTurnMark.None;

        for (int i = 0; i < _hoverNodes.Count; i++)
        {
            HoverNode node = _hoverNodes[i];
            if (node.FocusRing == null || node.ActorId == 0)
                continue;
            bool isTurn = turnId != 0 && node.ActorId == turnId;
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

        int turnId = Board.CharacterFocus.TurnActorId;
        Color? turnTint = _peerFocusMark != Board.FocusTurnMark.None
            ? Board.FocusCue.Tint(_peerFocusMark)
            : (turnId != 0 ? Board.FocusCue.AtTurnRingTint() : (Color?)null);

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
            if (turnId != 0 && id == turnId && turnTint != null)
                _chips[i].SetFocusTint(turnTint);
            else if (_peerFocusActorId != 0 && id == _peerFocusActorId)
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

        /// <summary>Mod-owned focus/turn ring seated on the CLONE of this entry's avatar portrait
        /// (null while the portrait is not resolvable yet). Not a mirrored node — see the peer-focus
        /// block header for why the mirror cannot touch it and it cannot touch the mirror.</summary>
        public Board.UiRing? FocusRing;
        public GameObject? Popup;      // clone of the entry's MonsterBaseUI root (enemies only)
        public Graphic? Name;          // clone of the entry's name label
        public RectTransform? Entry;   // clone of the entry root (width override, enemies only)
        public float FullWidth;        // vanilla _startWidth
        public float MinWidth;         // _startWidth × config.MinimalEnemyAvatarDesiredWidth
        public bool HasWidths;

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
                var node = new HoverNode { ActorId = NetFigures.StableActorId(beh.Actor) };

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
                node.FocusRing = Board.UiRing.Build(portraitClone, "GloomhavenVR.RemoteFocusRing");

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

                    node.Entry = _mirror.CloneOf(beh.transform) as RectTransform;
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
            _hoverNodes.Clear(); // no overrides this frame; the next tick re-resolves
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
            // the freshly synced clone (the same frame must never show the local hover).
            InvalidateHoverCache();
            ApplyHoverOverrides();
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

            // The fallback chips ARE the docked initiative widget while the mirror is down, so
            // they take the same sub-ladder tier the mirror canvas uses: strictly above the
            // board's furniture (the peer's pick banner overlaps this strip) at every angle -
            // see BoardVisual's sub-ladder header. Text stays in front of its plate via the
            // 1 mm z offset (equal orders fall back to distance), unchanged from before.
            _plate.sortingOrder = BoardVisual.OrderDockedWidget;
            _initiative.sortingOrder = BoardVisual.OrderDockedWidget;
            _name.sortingOrder = BoardVisual.OrderDockedWidget;

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

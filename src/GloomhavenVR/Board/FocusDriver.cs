using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board;

/// <summary>
/// The LOCAL-side renderer of the character-focus cues: the initiative-track rings and the frame
/// around the local player's own control board. (A peer's board and Steam avatar are drawn by
/// <see cref="Net.RemoteFocusOutline"/>, from the same <see cref="FocusCue"/> palette.)
///
/// <para>WHAT IT DRAWS, and why each one exists:</para>
/// <list type="bullet">
/// <item>a STEADY blue-white ring on the initiative entry of the character the player is currently
///   looking at — the user's "the initiative track must make the current selection visible";</item>
/// <item>a ring on the entry of the character THE GAME IS WAITING ON — the user's "the initiative
///   track must also make it visually clear which character is currently at turn". It BLINKS green
///   when the local player owns that character and is looking at it, BLINKS red when they own it
///   but are looking elsewhere, and is a STEADY gold otherwise (a teammate's or a monster's turn:
///   a fact, not a demand).
///
///   <para>"Waiting on" is <c>CharacterFocus.AttentionActor</c> = the actor at turn, or — when
///   nobody is at turn, which is the WHOLE of an enemy's action — the character that owes an OPEN
///   DECISION. Reading only the turn was the 2026-08-08 defect: a take-damage prompt is raised
///   inside the enemy's Action phase, where <c>Choreographer.CurrentPlayerActor</c> is null, so the
///   character that actually owed the player a choice wore no ring and its board wore no stroke
///   ("im Falle einer Schadensauswahl ist das Highlighting nicht sichtbar, nur das rote Overlay").
///   The decision owner is NOT resolved here — it comes from
///   <c>Cards.CardsGameApi.DecidingHand</c> through <c>CharacterFocus.DecisionOwner</c>, the same
///   deciding-actor chain the card board presents from and <c>DecisionDockSurface.PromptOwner</c>
///   attributes the docked prompt with, so the highlight and the prompt it points at can never
///   name two different characters.</para>
///
///   <para>IT DOES NOT REPLACE VANILLA'S DAMAGE OVERLAY, and does not touch it: the red
///   <c>InitiativeTrackPlayerBehaviour.ShowWarning</c> pulse the game plays on the ATTACKED actor
///   (Choreographer.cs:5477) is a different statement — "this actor is being attacked" — and keeps
///   running unchanged. The two also need not sit on the same portrait: vanilla marks
///   <c>m_ActorBeingAttacked</c>, this marks the character that must DECIDE
///   (<c>actorToShowCardsFor</c>), and for a damaged summon those are the summon and its
///   summoner.</para></item>
/// <item>the same blinking green/red as a STROKE on the outer edge of the local player's own
///   control board — the user's "the board gets a red blinking outline" and "the control board of
///   the player who owns the character at turn gets a blinking outline". Since 2026-08-08 that is
///   ONE thin closed line traced along the board asset's own outer edge (<see cref="BoardFrame"/>)
///   — not the rectangle it used to be, not the inverted hull that drew a rim around every interior
///   recess ("KEINE weiteren Outlines innerhalb des Assets"), and since the third round not the
///   8–12 mm band standing 6 mm off the rim either ("der Strich ist mir zu dick" / "wirklich am
///   äußeren Rand des Assets"). The rectangle survives only as the fallback for the procedural
///   board, which IS a rectangle and is therefore correctly framed by one.</item>
/// </list>
///
/// <para>WHY THE TWO RINGS NEVER STACK: when the focused character IS the character at turn, only
/// the turn ring is drawn. Green already says "you are on the right one", and two concentric rings
/// on one small portrait read as noise rather than as two facts.</para>
///
/// <para>Entry resolution and ring seating are deliberately the SAME recipe the selection-phase
/// cue uses (<c>InitiativeSelectionGlow</c>): the game's public
/// <c>InitiativeTrack.FindInitiativeTrackActor(actor)</c> for the entry, its avatar's
/// <c>RawImage</c> for the visible portrait rect, a hollow non-raycasting frame as a nested child
/// at local z 0. The rings must never eat the portrait's click — that click is how the player
/// CHANGES the focus.</para>
///
/// <para>Runs in <c>LateUpdate</c>, after the game's own initiative writes, through
/// <see cref="TickGuard"/> so a throwing frame can never starve input (the standing rule from the
/// WorldUI input incident). Every ring is re-resolved each tick, so a pooled/reassigned entry can
/// never keep a stale colour, and everything clears the moment the phase gate shuts.</para>
/// </summary>
internal sealed class FocusDriver : MonoBehaviour
{
    /// <summary>Half-width of the local control board's slab in tray-root local metres — the ONE
    /// dimension <c>PlayTray</c> publishes. The slab is 0.64 × 0.32 m, i.e. exactly 2:1, so the
    /// half-height is half of this; see <see cref="BoardAspect"/>.
    /// FALLBACK ONLY since 2026-08-08 — see <see cref="TickBoardFrame"/>.</summary>
    private static float BoardHalfW => PlayTray.BoardHalfWidthLocal;

    /// <summary>The control board slab's height/width ratio (0.32 / 0.64). Stated here rather than
    /// mirrored from PlayTray's private constants, and only ever used to place a decoration —
    /// a board style with a different aspect would draw a slightly loose frame, never a wrong
    /// board. FALLBACK ONLY — the real board is traced, not circumscribed.</summary>
    private const float BoardAspect = 0.5f;

    /// <summary>Outward margin of the fallback board frame past the slab edge (metres), so the
    /// outline sits just OFF the board rather than on top of its art.</summary>
    private const float BoardFrameMargin = 0.022f;

    /// <summary>Bar thickness of the board frame (metres) — thick enough to read across the table,
    /// thin enough not to look like furniture.</summary>
    private const float BoardFrameThickness = 0.012f;

    /// <summary>Local z of the board frame: slightly toward the viewer (+Z points AWAY under the
    /// module convention), so the frame is never z-fought by the slab face.</summary>
    private const float BoardFrameZ = -0.004f;

    // Rings per initiative entry, kept (deactivated) for cheap reuse — same contract as the
    // selection-phase glow. Two families so an entry can hold both roles across frames without
    // rebuilding: the focus ring and the turn ring are separate GameObjects.
    private readonly Dictionary<InitiativeTrackActorBehaviour, UiRing> _focusRings = new(8);
    private readonly Dictionary<InitiativeTrackActorBehaviour, UiRing> _turnRings = new(8);
    private readonly List<InitiativeTrackActorBehaviour> _stale = new(8);

    /// <summary>The frame around the board asset's outer contour. Null only on the procedural
    /// fallback board — then <see cref="_rectFrame"/> carries the cue instead.</summary>
    private BoardFrame? _boardFrame;

    /// <summary>The legacy rectangle, now the FALLBACK path only (see <see cref="TickBoardFrame"/>).</summary>
    private WorldFrame? _rectFrame;
    private Transform? _boardFrameHost;

    private System.Action? _tickCached;

    /// <summary>
    /// The live driver, for the ONE cross-module question this class has to answer: WHICH
    /// GameObjects are the local player's own focus rings.
    ///
    /// <para>WHY IT EXISTS (user report 2026-08-09, verbatim: "Der Rand der anzeigt welchen
    /// Character ich gerade ausgewählt habe, ist auch beim remote-board zu sehen bei MEINEN
    /// Characteren - das remote board sollte nur das Einzige was der Mitspieler sieht, nicht was ich
    /// sehe"). The rings this driver builds are plain <c>Image</c>s parented under the LOCAL
    /// initiative entry's portrait, and a peer's mirrored board is an <c>Instantiate</c> of that
    /// whole track (<c>Net.RemoteWidgetMirror</c>) whose <c>Pair.Apply</c> copies every node's
    /// <c>activeSelf</c> and colour verbatim. So the OBSERVER's blue-white focus ring and gold/red
    /// at-turn ring stood on every peer's board — the same leak the amber selection-phase cue had
    /// and closed through <c>InitiativeSelectionGlow.LiveRingRectOf</c>, which this pair of lookups
    /// deliberately copies member for member.</para>
    ///
    /// <para>ONE driver exists at a time (<c>BoardModule</c> adds it to its own root), and a stale
    /// static would only ever answer "no ring" — the callers treat null as "this client never lit
    /// one here", which is the honest, suppressing answer.</para>
    /// </summary>
    private static FocusDriver? s_instance;

    private void Awake() => s_instance = this;

    /// <summary>The LIVE steady focus ring ("the character I am looking at") this driver has built
    /// on <paramref name="entry"/>, or null when it never lit one there. Sole caller:
    /// <c>Net.RemoteInitiativeTrack</c>, which forces the CLONE of it off — see
    /// <see cref="s_instance"/>.</summary>
    internal static RectTransform? LiveFocusRingRectOf(InitiativeTrackActorBehaviour? entry) =>
        RingRectOf(s_instance?._focusRings, entry);

    /// <summary>The LIVE at-turn ring (blinking green/red, or steady gold) this driver has built on
    /// <paramref name="entry"/>, or null. Same caller, same reason as
    /// <see cref="LiveFocusRingRectOf"/> — it is a SECOND family of GameObjects on the same
    /// portrait, so suppressing only the first would leave half the leak open.</summary>
    internal static RectTransform? LiveTurnRingRectOf(InitiativeTrackActorBehaviour? entry) =>
        RingRectOf(s_instance?._turnRings, entry);

    private static RectTransform? RingRectOf(
        Dictionary<InitiativeTrackActorBehaviour, UiRing>? rings,
        InitiativeTrackActorBehaviour? entry)
    {
        if (rings == null || entry == null)
            return null;
        return rings.TryGetValue(entry, out UiRing ring) ? ring.Rect : null;
    }

    private void LateUpdate() =>
        TickGuard.Run("Board.CharacterFocus", _tickCached ??= Tick);

    private void OnDestroy()
    {
        if (ReferenceEquals(s_instance, this))
            s_instance = null;
        ClearAllRings();
        _boardFrame?.Destroy();
        _boardFrame = null;
        _rectFrame?.Destroy();
        _rectFrame = null;
        _boardFrameHost = null;
    }

    private void Tick()
    {
        // Gate: no live scenario (or a teardown that left PhaseManager's static phase stale — the
        // exact trap SelectionReadyHighlighter documents) ⇒ everything off, nothing dereferenced.
        bool live = ScenarioLive() && CardsGameApi.InScenario
                    && ScenarioManager.Scenario != null;
        if (!live)
        {
            HideAll();
            return;
        }

        // FOCUS PIN (user ModBuild 139) — BEFORE anything is sampled, so the ring, the board frame
        // and the wire all describe the same character on the very frame a targeting window opens.
        // A focus taken before that window is returned to the acting character here; a switch INTO
        // the window is refused at the click seam instead. Cheap: one reference compare in the
        // steady state, and it early-returns outright while no focus override is live.
        CharacterFocus.EnforcePin();

        _tickFocused = CharacterFocus.LookingAt;
        // THE CHARACTER THE GAME IS WAITING ON — the actor at turn, or (nobody at turn: the whole
        // of an enemy's action) the one that owes an OPEN DECISION. Reading only the turn is what
        // left a take-damage decision unmarked; see CharacterFocus.AttentionActor.
        _tickAttention = CharacterFocus.AttentionActor;
        _tickMark = CharacterFocus.LocalMark;

        TickMrPalette();
        TickAttentionLog();
        // PER-CARRIER ISOLATION, on top of the tick-wide TickGuard. TickGuard already stops one
        // throwing subsystem from starving input (the WorldUI incident's standing rule), but it
        // aborts the WHOLE tick: a single bad initiative entry would silently take the board
        // outline down with it, and the two would then disagree about the state — the one thing
        // this feature exists to prevent. Each carrier is therefore isolated on its own, and names
        // itself once so a hardware log says WHICH cue died instead of "the focus cue died".
        //
        // The arguments travel in FIELDS and the three delegates are cached, because a lambda per
        // carrier per frame is a steady-state allocation in a LateUpdate — the same reason the
        // TickGuard entry point above caches its own <see cref="System.Action"/>.
        Carrier("initiative rings", _ringsCached ??= RunRings);
        Carrier("board frame", _boardCached ??= RunBoard);
        Carrier("read-only refresh", _readOnlyCached ??= TickReadOnlyRefresh);
    }

    private CPlayerActor? _tickFocused;
    private CPlayerActor? _tickAttention;
    private FocusTurnMark _tickMark;
    private System.Action? _ringsCached;
    private System.Action? _boardCached;
    private System.Action? _readOnlyCached;

    private void RunRings() => TickRings(_tickFocused, _tickAttention, _tickMark);

    private void RunBoard() => TickBoardFrame(_tickMark);

    /// <summary>Names already reported by <see cref="Carrier"/>, so a per-frame failure logs once
    /// instead of flooding — the carrier keeps being retried, only the shouting stops.</summary>
    private readonly HashSet<string> _reported = new(4);

    private void Carrier(string name, System.Action work)
    {
        try
        {
            work();
        }
        catch (System.Exception e)
        {
            if (_reported.Add(name))
            {
                VRLog.Warn("Board", $"Focus cue carrier '{name}' threw and was ISOLATED — the other "
                                    + "carriers still render this frame, so the board, the avatar "
                                    + $"ring and the track cannot drift apart over it. {e}");
            }
        }
    }

    /// <summary>
    /// "A scenario is actually running", with the guard the GAME forgets.
    ///
    /// <para>DEFECT (hardware log 2026-08-08, LogOutput.log around the campaign pick):
    /// <c>Board.CharacterFocus</c> threw a NullReferenceException every frame, and the stack
    /// bottoms out INSIDE the game: <c>GlobalData.get_CurrentGameState</c> ←
    /// <c>RevealGate.InScenario</c> ← this tick. <c>RevealGate</c>'s own guard
    /// (<c>save?.Global != null</c>) is correct and still not enough, because it is the GETTER that
    /// throws, not the access to it.</para>
    ///
    /// <para>ROOT CAUSE, READ FROM SOURCE (<c>decompiled/GH.Runtime/GlobalData.cs</c>:563): the
    /// Campaign branch of <c>CurrentGameState</c> reads
    /// <c>AdventureState.MapState.IsInScenarioPhase</c> with NO null check — while the Guildmaster
    /// branch three lines below it does have one. And <c>AdventureState.MapState</c>
    /// (<c>MapRuleLibrary.Adventure</c>) is a plain auto-property that is null until
    /// <c>StartAdventure</c> runs and null again after <c>End()</c>. So between "the player picked
    /// Campaign in the main menu" (GameMode is already Campaign) and "the campaign save finished
    /// loading" (MapState exists), the game's own property throws for anyone who reads it. The log
    /// puts the exception exactly there: the frames after the Campaign menu click and before
    /// "Loading started".</para>
    ///
    /// <para>THE FIX IS THE MISSING GUARD, not a swallowed exception: ask whether the object the
    /// getter is about to dereference exists, and answer "not in a scenario" when it does not.
    /// Nothing else about the gate changes, and the guard is confined to the exact branch that has
    /// the hole — the other <c>GameMode</c>s never touch <c>MapState</c>.</para>
    ///
    /// <para>Deliberately NOT fixed in <c>Net.RevealGate</c>: that gate is the multiplayer
    /// anti-cheat linchpin with several callers and is not this feature's to re-scope. The same
    /// guard belongs there eventually — see the snippet handed back with this round.</para>
    /// </summary>
    private static bool ScenarioLive()
    {
        SaveData? save = SaveData.Instance;
        GlobalData? global = save != null ? save.Global : null;
        if (global == null)
            return false;
        if (global.GameMode == EGameMode.Campaign && MapRuleLibrary.Adventure.AdventureState.MapState == null)
            return false; // campaign chosen, adventure not started yet — the getter would throw
        return Net.RevealGate.InScenario;
    }

    private bool _mrPalette;
    private bool _mrPaletteKnown;

    /// <summary>
    /// Log the ONE mode switch that changes what all three carriers look like, so a hardware round
    /// can tell "the cue is drawn in the wrong palette" from "the cue is drawn and the compositor
    /// ate it" without guessing. Change-gated: two lines per session at most.
    /// </summary>
    private void TickMrPalette()
    {
        bool mr = FocusCue.MrActive;
        if (_mrPaletteKnown && mr == _mrPalette)
            return;
        _mrPaletteKnown = true;
        _mrPalette = mr;
        if (mr)
        {
            Color key = Core.MixedReality.KeyColor.Value;
            VRLog.Info("Board", "Focus cue → MIXED-REALITY palette. Chroma key is "
                                + $"{Core.MixedReality.KeyColorName} (RGBA {key.r:0.##},{key.g:0.##},"
                                + $"{key.b:0.##}); the cue is OPAQUE (blink rides brightness, not "
                                + "alpha) and correct/wrong are white/amber instead of green/red. The "
                                + "old cue was a "
                                + "TRANSLUCENT green over that green key: the compositor keyed the "
                                + "blended pixel and replaced the outline with the room. The dark "
                                + "keyline ModBuild 83 hung on both edges of the board stroke is GONE "
                                + "(user: 'den schwarzen Rahmen braucht es auch nicht um den "
                                + "Outline-Strich') — the MR stroke is a single 4 mm line.");
        }
        else
        {
            VRLog.Info("Board", "Focus cue → normal (non-MR) palette: the original translucent "
                                + "green/red blink, unchanged.");
        }
    }

    /// <summary>Change-gate for <see cref="TickAttentionLog"/>: attention actor id, whether it came
    /// from the decision branch, and the mark. Zeroed state = never logged.</summary>
    private int _loggedAttentionId;

    private bool _loggedAttentionFromDecision;

    private FocusTurnMark _loggedAttentionMark = (FocusTurnMark)(-1);

    /// <summary>
    /// ONE line whenever the answer to "who is the game waiting on, and what is the cue doing about
    /// it" changes — never per frame (three-field change gate; a whole take-damage prompt is one
    /// line, and an idle table costs three compares).
    ///
    /// <para>It exists because this is the exact question the 2026-08-08 hardware report was about:
    /// "Der Character der aktuell eine Entscheidung treffen muss soll genauso gehighlighted werden
    /// wie zuvor auch — im Falle einer Schadensauswahl ist das Highlighting nicht sichtbar, nur das
    /// rote Overlay". The line names the owner, says WHERE the answer came from (turn vs open
    /// decision), states the mark that was applied, and — for the decision branch — names what used
    /// to suppress it, so a future "it is gone again" is answerable from the log alone.</para>
    /// </summary>
    private void TickAttentionLog()
    {
        CPlayerActor? attention = _tickAttention;
        bool fromDecision = attention != null && CharacterFocus.TurnActor == null;
        int id = Net.NetFigures.StableActorId(attention);
        if (id == _loggedAttentionId && fromDecision == _loggedAttentionFromDecision
            && _tickMark == _loggedAttentionMark)
            return;
        _loggedAttentionId = id;
        _loggedAttentionFromDecision = fromDecision;
        _loggedAttentionMark = _tickMark;

        if (attention == null)
        {
            VRLog.Info("Board", "[Focus] the game is waiting on NOBODY (no actor at turn, no open "
                                + "decision this client owns) — no attention ring on the initiative "
                                + "track and no stroke on the control board.");
            return;
        }

        string where = fromDecision
            ? "an OPEN DECISION it owes (Cards.CardsGameApi.DecidingHand — the same deciding-actor "
              + "chain the card board presents from and DecisionDockSurface.PromptOwner attributes "
              + "the docked prompt with); nobody is at turn, which is normal for the whole of an "
              + "enemy's action. BEFORE this build the cue read Choreographer.CurrentPlayerActor "
              + "ALONE, which is null exactly there — so the decision owner got NO ring and NO board "
              + "stroke, and vanilla's red InitiativeTrackPlayerBehaviour.ShowWarning pulse on the "
              + "ATTACKED actor (Choreographer.cs:5477) was the only mark left. That pulse is "
              + "untouched and still plays; this is drawn IN ADDITION to it"
            : "its TURN (Choreographer.CurrentPlayerActor)";

        string applied = _tickMark switch
        {
            FocusTurnMark.AtTurnCorrect =>
                "APPLIED: blinking 'correct' ring on its initiative entry + the same blink as the "
                + "stroke on the local control board (green, or calm white in mixed reality) — you "
                + "own it and you are looking at it",
            FocusTurnMark.AtTurnWrong =>
                "APPLIED: blinking 'wrong character' ring on its initiative entry + the same blink "
                + "as the stroke on the local control board (red, or pumping amber in mixed "
                + $"reality) — you own it but you are looking at '{CharacterFocus.Describe(_tickFocused)}'",
            _ =>
                "APPLIED: steady gold ring on its initiative entry only (this client does not "
                + "control that character, so the board stroke stays off — it states a fact, it "
                + "does not ask you for anything)",
        };

        VRLog.Info("Board", $"[Focus] the game is waiting on '{CharacterFocus.Describe(attention)}' "
                            + $"because of {where}. {applied}.");
    }

    /// <summary>
    /// Keep a live READ-ONLY focus view current. The watched character's hand, piles and played
    /// cards change as THEY act, and none of those changes raise an event on this client (the
    /// driver's rebuild edges are all about the LOCAL player's own hand). A low-rate poll is the
    /// honest answer: the alternative is a per-frame rebuild of a whole card board.
    /// </summary>
    private void TickReadOnlyRefresh()
    {
        if (!CharacterFocus.ReadOnlyView)
            return;
        if (Time.unscaledTime < _nextReadOnlyRefreshAt)
            return;
        _nextReadOnlyRefreshAt = Time.unscaledTime + ReadOnlyRefreshInterval;
        Cards.CardsDriver.RequestRebuild();
    }

    /// <summary>Poll period of the read-only focus refresh. 4 Hz: fast enough that a teammate
    /// playing a card is seen essentially immediately, slow enough that the rebuild cost is a
    /// rounding error next to the per-frame work the board already does.</summary>
    private const float ReadOnlyRefreshInterval = 0.25f;

    private float _nextReadOnlyRefreshAt;

    // ------------------------------------------------------------------------- initiative rings --

    private void TickRings(CPlayerActor? focused, CPlayerActor? attention, FocusTurnMark localMark)
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        if (track == null)
        {
            HideRings();
            return;
        }

        InitiativeTrackActorBehaviour? turnEntry = EntryFor(track, attention);
        // Two rings never stack on one portrait: when the focus IS the character the game waits on,
        // the attention ring (which carries the urgent colour) wins and the focus ring is skipped.
        InitiativeTrackActorBehaviour? focusEntry =
            ReferenceEquals(focused, attention) ? null : EntryFor(track, focused);

        // The attention ring: blinking in the local mark's colour while the local player owns the
        // character the game is waiting on (its turn, OR a decision it owes), steady gold otherwise.
        Color? turnTint = localMark != FocusTurnMark.None
            ? FocusCue.Tint(localMark)
            : (attention != null ? FocusCue.AtTurnRingTint() : (Color?)null);

        ApplyRing(_turnRings, turnEntry, turnTint, breathe: localMark != FocusTurnMark.None,
                  "GloomhavenVR.FocusTurnRing");
        ApplyRing(_focusRings, focusEntry,
                  focused != null ? FocusCue.SelectionRingTint() : (Color?)null,
                  breathe: false, "GloomhavenVR.FocusRing");
    }

    /// <summary>Show the ring for <paramref name="entry"/> in <paramref name="tint"/> and hide
    /// every OTHER ring of the same family (pooled entries are re-resolved every tick, so a
    /// reassigned portrait can never keep a stale colour).</summary>
    private void ApplyRing(Dictionary<InitiativeTrackActorBehaviour, UiRing> rings,
                           InitiativeTrackActorBehaviour? entry, Color? tint, bool breathe,
                           string name)
    {
        if (entry != null && tint != null)
        {
            UiRing? ring = EnsureRing(rings, entry, name);
            ring?.Apply(tint, breathe);
        }

        _stale.Clear();
        foreach (KeyValuePair<InitiativeTrackActorBehaviour, UiRing> kv in rings)
        {
            // Unity fake-null (a destroyed entry) is still a real reference — safe as a key.
            if (kv.Key == null || !kv.Value.Alive)
            {
                _stale.Add(kv.Key!);
                continue;
            }
            if (!ReferenceEquals(kv.Key, entry) || tint == null)
                kv.Value.Apply(null, false);
        }
        for (int i = 0; i < _stale.Count; i++)
        {
            if (rings.TryGetValue(_stale[i], out UiRing dead))
                dead.Destroy();
            rings.Remove(_stale[i]);
        }
    }

    private static UiRing? EnsureRing(Dictionary<InitiativeTrackActorBehaviour, UiRing> rings,
                                      InitiativeTrackActorBehaviour entry, string name)
    {
        if (rings.TryGetValue(entry, out UiRing existing))
        {
            if (existing.Alive)
                return existing;
            rings.Remove(entry); // destroyed / re-pooled with the old track — rebuild below
        }
        UiRing? built = UiRing.Build(PortraitRect(entry), name);
        if (built != null)
            rings[entry] = built;
        return built;
    }

    /// <summary>The entry of <paramref name="actor"/>, or null when it is not spawned / on screen.
    /// <c>FindInitiativeTrackActor</c> is the game's own public lookup and matches by reference,
    /// so the entry is exactly this actor's.</summary>
    private static InitiativeTrackActorBehaviour? EntryFor(InitiativeTrack track, CActor? actor)
    {
        if (actor == null)
            return null;
        InitiativeTrackActorBehaviour entry = track.FindInitiativeTrackActor(actor);
        return entry != null && entry.gameObject.activeInHierarchy ? entry : null;
    }

    /// <summary>The VISIBLE portrait rect of an entry — the avatar face, which is the avatar's only
    /// <c>RawImage</c> (every other graphic under it is a TMP or an Image). Null while the avatar
    /// has not been pooled in yet; the caller retries next tick.</summary>
    private static RectTransform? PortraitRect(InitiativeTrackActorBehaviour entry)
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

    // ---------------------------------------------------------------------------- board frame --

    /// <summary>
    /// The cue on the local player's own control board. The preferred renderer is
    /// <see cref="BoardFrame"/> — ONE thin closed stroke traced along the board asset's own outer
    /// edge, so the cue follows the real shape (bowed edges and rounded corners included) at any
    /// pose and any user scale, and draws NOTHING inside it. The old <see cref="WorldFrame"/>
    /// rectangle is kept ONLY for the procedural fallback board, whose contour genuinely is a
    /// rectangle.
    ///
    /// <para>Neither path ever writes the tray's transform — both build CHILDREN of the tray root,
    /// which is what keeps a FIXIERT (pinned, world-frozen) board legal: the freeze sentinel in
    /// <c>PlayTray.2.Watchdog</c> compares the ROOT's world pose, and parenting a decoration under
    /// it leaves that pose untouched. The outline is re-derived on a board SWITCH (a new root), the
    /// same edge the frame already used.</para>
    /// </summary>
    private void TickBoardFrame(FocusTurnMark localMark)
    {
        PlayTray? tray = PlayTray.Current;
        Transform? root = tray?.Root;
        if (root == null)
        {
            _boardFrame?.Apply(null);
            _rectFrame?.Apply(null);
            return;
        }
        if (!ReferenceEquals(_boardFrameHost, root)
            || (_boardFrame == null && _rectFrame == null))
        {
            // The tray root is re-created on a board SWITCH; both renderers are children and die
            // with it, so rebuild against the live root rather than resurrect a dangling handle.
            _boardFrame?.Destroy();
            _boardFrame = null;
            _rectFrame?.Destroy();
            _rectFrame = null;
            _boardFrameHost = root;

            _boardFrame = BoardFrame.Build(root, "local control board");
            // THE STROKE IS BOARD FURNITURE, and must be ordered like it (user 2026-08-08: "Es soll
            // wie jedes andere Element auch die Perspektive respektieren"). It is a transparent,
            // depth-less renderer sitting in the board's own plane, exactly like the status placard
            // and the keycap labels, and it shipped at the default sortingOrder 0 while all of those
            // ride the converted-panel DISTANCE ladder - so a menu spatially BEHIND the board still
            // painted over it. AdoptFurniture puts it in the control board's distance-ranked band
            // (CanvasConversion part 9), which is the one place that decision is made for this board.
            // Registered ONCE per built stroke, right here: the group captures the renderer's
            // creation-time sortingOrder as its in-band offset, so it must run before anything
            // writes that order.
            PlayTray.AdoptFurniture(_boardFrame?.RootObject);
            if (_boardFrame == null)
            {
                float w = BoardHalfW * 2f + BoardFrameMargin * 2f;
                float h = BoardHalfW * 2f * BoardAspect + BoardFrameMargin * 2f;
                _rectFrame = WorldFrame.Build(root, "GloomhavenVR.FocusBoardFrame",
                                              new Vector2(w, h), BoardFrameThickness, BoardFrameZ);
            }
        }
        Color? tint = FocusCue.Tint(localMark);
        _boardFrame?.Apply(tint);
        _rectFrame?.Apply(tint);
    }

    // ---------------------------------------------------------------------------------- teardown --

    private void HideAll()
    {
        HideRings();
        _boardFrame?.Apply(null);
        _rectFrame?.Apply(null);
    }

    private void HideRings()
    {
        foreach (UiRing ring in _focusRings.Values)
            ring.Apply(null, false);
        foreach (UiRing ring in _turnRings.Values)
            ring.Apply(null, false);
    }

    private void ClearAllRings()
    {
        foreach (UiRing ring in _focusRings.Values)
            ring.Destroy();
        foreach (UiRing ring in _turnRings.Values)
            ring.Destroy();
        _focusRings.Clear();
        _turnRings.Clear();
        _stale.Clear();
    }
}

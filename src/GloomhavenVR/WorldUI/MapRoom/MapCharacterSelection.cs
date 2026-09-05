using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using HarmonyLib;
using MapRuleLibrary.Party;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP-ROOM SELECTION FLOOR — "no character selected" is not a state on the map either.
///
/// <para>USER RULING (2026-08-15): <i>"Auch in der Map soll gelten: Für jeden Spieler dem min 1.
/// Character zugewiesen wurde ist immer irgendeiner dieser Charactere direkt ausgewählt, den
/// Zustand dass kein Character ausgewählt wurde soll es wirklich nur dann geben, wenn dem Spieler
/// noch keine Character zugewiesen wurde."</i> — for every player who has at least one character
/// ASSIGNED, one of THAT PLAYER'S characters is always selected; the empty state may exist only for
/// a player who has not been assigned any character yet. The word is <b>"auch"</b>: this rule
/// already holds for the scenario board (<see cref="Board.CharacterFocus"/>, whose class doc states
/// it as <c>"'No character selected' is not a state (user 2026-08-13, #11)"</c>) and he is extending
/// it to the map room. This class is that extension, and it deliberately reuses the board's shape —
/// a level-triggered floor plus one <c>SELECTION GUARD</c> vocabulary — so both rooms read the same
/// in one log.</para>
///
/// <para><b>WHAT "SELECTED" IS ON THE MAP, AND IT IS NOT THE BOARD'S ACTING HAND.</b> The map phase
/// has no <c>CPlayerActor</c> at all (<c>CMapCharacter.GetActor()</c> reads
/// <c>ScenarioManager.Scenario</c>, which is null here — see <c>Net.RevealGate</c>), so there is no
/// "hand the game presents" to fall back to. The map-side selection is one field:</para>
/// <list type="bullet">
/// <item><b>THE SELECTION</b> is <c>NewPartyDisplayUI.selectedCharacter</c>
/// (NewPartyDisplayUI.cs:143), a <c>NewPartyCharacterUI</c> = one of the four party SLOTS, exposed
/// publicly as <c>SelectedUISlot</c> (:226) and (differently typed, same field)
/// <c>SelectedCharacter</c> (:244). Its ONLY writer in the whole class is the private
/// <c>SelectCurrentCharacter(NewPartyCharacterUI)</c> (:856-863), reached only from
/// <c>OnCharacterSelect(bool, NewPartyCharacterUI)</c> (:871-915). The character behind the slot is
/// <c>NewPartyCharacterUI.Data</c> (:280), a <c>CMapCharacter</c>.</item>
/// <item><b>A HIGHLIGHT IS A DIFFERENT THING and writes no state.</b>
/// <c>NewPartyCharacterUI.OnHighlight(bool)</c> (:879), <c>ToggleDim</c> and <c>ToggleHoverDim</c>
/// only set a bool and call <c>UpdateSprite()</c>; <c>NewPartyDisplayUI.OnHoveredSlot</c> (:396-402)
/// dims the OTHER slots on hover. None of them touches <c>selectedCharacter</c>. Do not confuse the
/// two: the mod's own <c>Patches.PartyPreviewStorm</c> is about the HOVER path.</item>
/// <item><b>AND <c>MapParty.SelectedCharacters</c> IS NOT A SELECTION AT ALL</b>, despite the name:
/// it is the four-slot party ROSTER (<c>CMapParty.cs:70</c> <c>SelectedCharactersArray</c>, a
/// fixed-length-4 array with null holes; <c>:98</c> is the null-filtering projection). It is
/// savegame state and changing it is a networked party-composition change
/// (<c>UIAdventurePartyAssemblyWindow.SelectCharacter</c>). This class READS it as the candidate set
/// and never writes it.</item>
/// </list>
///
/// <para><b>WHAT MAKES IT DROP TO NOBODY.</b> <c>OnCharacterSelect(isSelected: false, …)</c> calls
/// <c>SelectCurrentCharacter(null)</c> (NewPartyDisplayUI.cs:895), and it is reached from
/// <c>UnselectCurrentCharacter()</c> (:832), <c>EscapeCurrentCharacter()</c> (:840),
/// <c>Cancel()</c> (:848), <c>DisableMapOptions()</c> (:1504), <c>OnControllerAreaFocused()</c>
/// (:2701) and — the big one — <c>Hide(request, instant, callback, deselectCurrentCharacter = true)</c>
/// (:702-705): <b>closing the party panel clears the selection by default</b>, and the panel is
/// closed for the whole of a map flight (see <c>WorldUI.SubViewRevival</c>). The game re-fills it
/// from exactly one place, <c>UILoadoutManager.AutoselectCharacter</c> (UILoadoutManager.cs:364-372),
/// which is registered on <c>PartyDisplay.OnShown</c> only while the LOADOUT window is up
/// (:330/:406). Outside the loadout nothing re-selects, which is precisely the reported hole.</para>
///
/// <para><b>ModBuild 447 — AND ONE MORE DROP SOURCE THE LIST ABOVE DID NOT HAVE, which is the one
/// the user could see.</b> Report, verbatim (2026-09-05): <i>"Wenn die Map gewechselt wird, blinkt
/// auch immer ganz kurz der ausgewählte Character in dem Character-UI Fenster. Ich möchte dass es da
/// stabil bleibt."</i> Every guildmaster mode change runs
/// <c>UIGuildmasterHUD.UpdateCurrentMode</c> (:435) → <c>modes[current].Exit()</c> →
/// <c>OnLeaveMap</c> (:523) → <c>NewPartyDisplayUI.DisableMapOptions()</c> (:1501), and the last
/// statement of THAT is <c>CloseWindows()</c> (:1316) whose own last statement is
/// <c>selectedCharacter?.Escape()</c> (:1323) — <c>NewPartyCharacterUI.Escape()</c> (:1453) calls
/// <c>OnClick()</c> on the slot that is already selected, which toggles it off and lands in
/// <c>SelectCurrentCharacter(null)</c>. The list above named <c>DisableMapOptions()</c> at :1504,
/// which is its <c>UnselectCurrentCharacter()</c> branch — and that branch deliberately spares an
/// ASSIGNED character. The method then throws the assigned one away four lines later through
/// <c>CloseWindows</c>. One method, both statements, opposite intents
/// ([[the-game-threw-away-its-own-fact]]).</para>
///
/// <para><b>WHY IT BLINKS ON A MAP SWITCH AND NOT ON A TEMPLE OR MERCHANT OPEN</b>, which is the
/// asymmetry that names the cause: a destination's <c>Enter()</c> reaches
/// <c>EnableSelectionMode</c> (:1408), which calls <c>DisableMapOptions()</c> itself and then
/// SYNCHRONOUSLY re-selects at its tail (:1435-1438). <c>CityMapMode</c> and <c>WorldMapMode</c>
/// have no such tail, so on those two nothing re-selects and this class is the only thing that
/// does. The ModBuild 446 log shows exactly that: four map switches, four drives; three destination
/// opens, no drive needed.</para>
///
/// <para><b>THE FIX IS TIMING, NOT A NEW WRITE.</b> The restore is the drive this class has always
/// made under the 2026-08-15 ruling; what was wrong is that it was reached by a 4 Hz poll, so the
/// panel painted nobody for up to 250 ms — about 22 frames at 90 Hz, which is what the user sees as
/// a blink. <see cref="Tick"/> now reacts to the DROP EDGE (somebody → nobody) on the frame it
/// happens. Nothing about who may write the selection, which character is chosen, or how often a
/// drive may be issued changed; see the block on the edge itself for why it cannot make
/// <see cref="DriveFuse"/> hair-trigger.</para>
///
/// <para><b>THE ANSWER IS TWO LAYERS, and which one applies is decided by whether the game has a
/// selection to write at all.</b></para>
/// <list type="number">
/// <item><b>PANEL OPEN ⇒ DRIVE THE GAME'S OWN SEAM.</b> <see cref="Drive"/> calls
/// <c>NewPartyDisplayUI.SelectCharacterById(string)</c> (:1455) — the very call
/// <c>AutoselectCharacter</c> makes, so the flat party display ends up in the state a flat client
/// would be in, not in a mod-invented one. Nothing else is written: no roster change, no
/// <c>GameAction</c>, no rules call.</item>
/// <item><b>PANEL CLOSED ⇒ A PRESENTATION FLOOR.</b> There is no selection to write (the next
/// <c>Hide</c> would clear it again, and driving a hidden window's tab navigation is a write war we
/// would lose — project ruling "concede the flag, own the number"). So <see cref="Current"/> answers
/// with a character of this player's anyway, READ-ONLY, exactly as
/// <c>CharacterFocus.LocalFloorHand</c> does for the board. The map room's consumers ask
/// <see cref="Current"/> and therefore never see nobody.</item>
/// </list>
///
/// <para><b>WHICH CHARACTER, AND WHY IT IS STABLE.</b> The game already has a preference and this
/// class copies it rather than inventing one — <c>AutoselectCharacter</c>'s predicate, minus its
/// battle-goal clause:</para>
/// <code>
/// _party.SelectedCharacters.FirstOrDefault(it =&gt;
///     (!FFSNetwork.IsOnline || it.IsUnderMyControl) &amp;&amp; _quest.GetChosenBattleGoal(it.CharacterID) == null)
/// </code>
/// <para>i.e. <b>the first character in party-ROSTER SLOT ORDER that this client controls</b>. The
/// <c>GetChosenBattleGoal</c> term is dropped deliberately: it is the battle-goal picker's own
/// convenience ("the first of mine that still needs a goal") and outside that flow it would answer
/// "nobody" for a party whose goals are all chosen — manufacturing the very empty state this class
/// exists to remove. Three properties follow:</para>
/// <list type="bullet">
/// <item><b>Stable across ticks</b>: <c>SelectedCharactersArray</c> is savegame order and only a
/// party-composition change moves it.</item>
/// <item><b>Predictable across peers</b>: the roster is replicated and the ownership term is the
/// game's own replicated assignment, so every peer can compute what every other peer will land on.
/// It is a deterministic function of shared state, not a local guess.</item>
/// <item><b>Continuous</b>: <see cref="_held"/> keeps the character we last supplied while it is
/// still ours and still in the roster, so a floor that engages twice does not move the player's view
/// between two of his own characters. Same argument, same shape as
/// <c>CharacterFocus._floorActor</c>.</item>
/// </list>
///
/// <para><b>"ASSIGNED" IS READ FROM THE GAME'S OWN OWNERSHIP, never guessed from the roster.</b>
/// A character's controllable id is the game's own ternary (NewPartyDisplayUI.cs:511,
/// NewPartyCharacterUI.cs:565, BenchedCharacter.cs:20/24): campaign ⇒
/// <c>CharacterName.GetHashCode()</c>, guildmaster ⇒
/// <c>CharacterClassManager.GetModelInstanceIDFromCharacterID(CharacterID)</c>. From there
/// <c>ControllableRegistry.GetControllable(id).Controller.PlayerID</c> is the assignment
/// (ControllableRegistry.cs:105/110), and it is the same integer on every client — it is what the
/// hardware logs print as <c>Controllable (ID: …) ASSIGNED to … (ID: 2)</c>
/// (NetworkPlayer.cs:272) and <c>… gained control over … (ControllableID: …)</c>
/// (GHNetworkControllable.cs:211-226). Two conventions of the game's are honoured verbatim:</para>
/// <list type="bullet">
/// <item><b>A participating controllable with <c>Controller == null</c> belongs to the HOST</b> —
/// <c>NetworkHeroAssignService.GetCharacterAssignations</c> (NetworkHeroAssignService.cs:55) writes
/// exactly that, and <c>NetworkControllable</c>'s constructor auto-assigns a non-benched controllable
/// to <c>PlayerRegistry.HostPlayer</c> (NetworkControllable.cs:111).</item>
/// <item><b>A BENCHED character is owned by nobody.</b> <c>BenchedCharacter.IsParticipant =&gt;
/// false</c> (BenchedCharacter.cs:11) and <c>NetworkControllable.ChangeControllableObject</c> RELEASES
/// the controller when the object becomes a bench entry (NetworkControllable.cs:130-141). A benched
/// character is also not in <c>SelectedCharactersArray</c> at all
/// (<c>NewPartyCharacterUI.InitAvailable</c> nulls the slot, NewPartyCharacterUI.cs:547/575), so the
/// candidate set excludes it twice over and the rule can never manufacture a selection out of one.
/// <see cref="IsBench"/> is the explicit third guard.</item>
/// </list>
///
/// <para><b>OFFLINE IS ITS OWN BRANCH, and must be — not a degenerate case of the online one.</b>
/// <c>NetworkControllable</c>'s constructor returns at <c>if (!FFSNetwork.IsOnline) return;</c>
/// (NetworkControllable.cs:88) before any controller is assigned, <c>PlayerRegistry.AllPlayers</c> is
/// empty, <c>MyPlayer</c> is null, and <c>CMapCharacter.IsUnderMyControl</c> is therefore NEVER set
/// (its only writers are the <c>IControllable</c> callbacks, both gated on
/// <c>PlayerRegistry.MyPlayer != null</c>). That is why the game itself writes
/// <c>!FFSNetwork.IsOnline || it.IsUnderMyControl</c> everywhere, and why this class branches on
/// <c>FFSNetwork.IsOnline</c> and never on "the registry looks empty". <b>Single player: every party
/// member is the local player's, so the rule must and does hold there too</b> — the empty state is
/// then reachable only before the party exists.</para>
///
/// <para><b>MULTIPLAYER: THE SELECTION IS LOCAL PRESENTATION, NOT REPLICATED.</b>
/// <c>OnCharacterSelect</c> raises no <c>GameAction</c> and sends nothing; the only networked thing
/// in the neighbourhood is the ROSTER swap (<c>NewPartyDisplayUI.MPSwitchCharacter</c>,
/// <c>UIAdventurePartyAssemblyWindow.SelectCharacter</c>), which this class never touches. So every
/// client enforces the ruling FOR ITSELF, against its own player's assignment, and a peer's board
/// cannot be changed from here. What a peer SEES is unchanged in mechanism: the mod already
/// broadcasts the map-room fan's character (<c>MapRoomHand.LocalFanCharacterKey</c> =
/// <c>FNV-1a(CMapCharacter.CharacterName)</c> in extras record 20), and after this change that key is
/// non-zero in the situations where it used to be 0 — the peer's mirrored fan shows a character
/// instead of nothing. No wire change, no new record, no new byte.</para>
///
/// <remarks>CLASSIFICATION: VR-ONLY presentation plus one call into the game's own local UI seam.
/// Costs no wire bytes. Writes no game state that the game would replicate — see the class doc's
/// multiplayer paragraph.</remarks>
/// </summary>
internal static class MapCharacterSelection
{
    private const string Scope = "WorldUI";

    /// <summary>How often the rule is evaluated when nothing has happened. 4 Hz is slow enough that
    /// a drive can never become a per-frame write, and that is still the only job this number has.
    ///
    /// <para><b>ModBuild 447 — IT USED TO CLAIM ONE MORE THING AND THE CLAIM WAS FALSE.</b> This doc
    /// said 4 Hz "is fast enough that a drop to nobody is invisible to the player". The user's eyes
    /// falsified it: <i>"Wenn die Map gewechselt wird, blinkt auch immer ganz kurz der ausgewählte
    /// Character in dem Character-UI Fenster. Ich möchte dass es da stabil bleibt."</i> A poll can
    /// only ever be at most one PERIOD late, and one period here is 250 ms — about 22 frames at
    /// 90 Hz of a party panel painting nobody. That is not a cadence to tune, it is the wrong shape:
    /// see <see cref="Tick"/>'s drop edge, which reacts on the frame the selection goes rather than
    /// on the next poll after it. This constant is unchanged and now covers only what it can: the
    /// case where nothing changed and we are simply re-checking.</para></summary>
    private const float EvaluateSeconds = 0.25f;

    /// <summary>The census/falsifier cadence — slow enough not to spam a log, fast enough that a
    /// transient violation lasting a second or two is caught by at least one line.</summary>
    private const float CensusSeconds = 5f;

    /// <summary>How many CONSECUTIVE drives may fail to take before the drive stands down for the
    /// session. It counts only drives whose OUTCOME was still "nobody" at the next evaluation —
    /// never the player's own deselect, which is a legitimate edge that this class is SUPPOSED to
    /// answer and which resets the counter the moment the answer sticks. (Project ruling: "a fuse
    /// cannot tell a hand from a loop" — so this fuse is keyed on our own failure, not on activity.)</summary>
    private const int DriveFuse = 6;

    /// <summary>ModBuild 447 — how many DROP EDGES print a line before the count alone carries them
    /// on the census. Twelve, because the ModBuild 446 session produced ten programmatic deselects
    /// in total and a hardware round must see every one of a normal session's; a window that dropped
    /// the selection every frame is a different defect and must not be able to drown the log while
    /// it is being diagnosed.</summary>
    private const int DropEdgeLinesPerSession = 12;

    private static float _nextEvaluate;
    private static float _nextCensus;

    /// <summary>
    /// ModBuild 447 — <b>DID THE PARTY DISPLAY HAVE SOMEBODY ON THE PREVIOUS FRAME?</b> One bool,
    /// so <see cref="Tick"/> can see the DROP itself rather than wait for the next poll to notice
    /// its consequence. Written every frame from the same <see cref="Selected"/> read the rule
    /// already uses, so the two can never disagree about what "selected" means.
    /// </summary>
    private static bool _hadSelection;

    /// <summary>Drop edges seen over this session, and how many of them arrived inside the 4 Hz
    /// window this round exists to close (i.e. would have been a visible blink). Both are printed on
    /// the census: a fix that never fires and a defect that never happens look identical without
    /// them, and this one is easy to leave in place doing nothing after some future round moves the
    /// game's deselect somewhere else.</summary>
    private static int _dropEdges;
    private static int _dropEdgesInsidePollWindow;

    /// <summary>The character the floor last supplied — continuity, see the class doc.</summary>
    private static CMapCharacter? _held;

    /// <summary>Edge guard for the guard lines: the state we last announced, so a 4 Hz evaluation is
    /// silent while nothing changes. Deliberately a STRING (the character name plus the branch), not
    /// an id: the same character reached through a different branch is a different thing to report,
    /// and the empty state has no id to key on.</summary>
    private static string _loggedEdge = string.Empty;

    /// <summary>The character id the last drive asked for, so the next evaluation can check the
    /// OUTCOME rather than trust the call (project ruling: "verify the outcome, not the path").</summary>
    private static string? _drivenId;

    /// <summary>Consecutive drives that did not take. See <see cref="DriveFuse"/>.</summary>
    private static int _driveFailures;

    /// <summary>True once the fuse has blown; the floor keeps working, only the drive stops.</summary>
    private static bool _driveStoodDown;

    // ------------------------------------------------------------------------------ the answer --

    /// <summary>
    /// THE CHARACTER THE MAP ROOM MUST PRESENT for the local player, and the one line every consumer
    /// should ask. The party display's own selection wins whenever it has one — the player's choice
    /// is never overridden — and <see cref="Floor"/> answers otherwise. Null means the empty state is
    /// genuinely correct: no map phase, no party yet, or this player has no assigned character.
    /// </summary>
    /// <param name="source">Where the answer came from, log-safe and short enough for one line.</param>
    internal static CMapCharacter? Current(out string source)
    {
        CMapCharacter? selected = Selected;
        if (selected != null)
        {
            source = "the PARTY DISPLAY's own selection (NewPartyDisplayUI.SelectedUISlot.Data)";
            _held = selected;
            return selected;
        }
        return Floor(out source);
    }

    /// <summary>
    /// The raw map-side selection — <c>NewPartyDisplayUI.PartyDisplay.SelectedUISlot.Data</c>, and
    /// nothing else. Null while the display holds nobody, is not built, or is gone. Never throws:
    /// a half-torn UI must not take the map room with it.
    /// </summary>
    internal static CMapCharacter? Selected
    {
        get
        {
            try
            {
                NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
                NewPartyCharacterUI? slot = display != null ? display.SelectedUISlot : null;
                // The slot must actually hold a party member: an Empty/Available slot can be
                // "selected" while the player is picking a mercenary for it, and that is not a
                // character to present.
                if (slot == null || slot.State != PartySlotState.Assigned)
                    return null;
                return slot.Data;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// THE FLOOR: one of the LOCAL player's own party characters, chosen by the game's own
    /// preference (see the class doc's "WHICH CHARACTER"), or null when this player genuinely has
    /// none — the one state in which showing nobody is correct.
    ///
    /// <para>Order: the character we are already holding while it is still ours and still in the
    /// roster, then the first roster slot this client controls. It never returns a character of
    /// another player's, in any branch, so it cannot invent a view the player is not entitled to and
    /// cannot put a teammate's loadout in this player's hand.</para>
    /// </summary>
    internal static CMapCharacter? Floor(out string source)
    {
        List<CMapCharacter> party = Roster();
        if (party.Count == 0)
        {
            source = "no party roster (no map phase, or the party is not built yet)";
            _held = null;
            return null;
        }

        // (1) continuity — keep what we already supplied while it is still ours and still seated.
        if (_held != null)
        {
            for (int i = 0; i < party.Count; i++)
            {
                if (!ReferenceEquals(party[i], _held))
                    continue;
                if (IsLocal(_held))
                {
                    source = "FLOOR (held): the character this client was already showing, still in "
                             + "the party and still under this client's control";
                    return _held;
                }
                break;
            }
            _held = null;
        }

        // (2) the game's own preference: first roster slot under this client's control.
        for (int i = 0; i < party.Count; i++)
        {
            if (!IsLocal(party[i]))
                continue;
            _held = party[i];
            source = "FLOOR (first owned): the FIRST party member in roster slot order that this "
                     + "client controls — the same preference UILoadoutManager.AutoselectCharacter "
                     + "encodes (UILoadoutManager.cs:364), minus its battle-goal clause";
            return party[i];
        }

        _held = null;
        source = FFSNetwork.IsOnline
            ? "this client controls NO party character (spectator, or every character of this "
              + "player is benched) — showing nobody is the truth here"
            : "an OFFLINE session with a party this client somehow does not control — should be "
              + "unreachable, since offline every merc is the player's";
        return null;
    }

    /// <summary>
    /// Is <paramref name="character"/> one the LOCAL client controls? Offline every party member is
    /// (see the class doc's offline paragraph — <c>IsUnderMyControl</c> is never set there, which is
    /// why the game itself always writes this disjunction). A bench entry is excluded explicitly,
    /// belt to the roster's braces.
    /// </summary>
    private static bool IsLocal(CMapCharacter? character)
    {
        if (character == null)
            return false;
        try
        {
            if (IsBench(character))
                return false;
            return !FFSNetwork.IsOnline || character.IsUnderMyControl;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>Scratch for <see cref="Roster"/> — reused, because the rule is evaluated four times a
    /// second and a fresh four-element list at that rate is exactly the garbage that gets copied into
    /// a hotter path later. Consumed entirely inside one caller before anything else can ask.</summary>
    private static readonly List<CMapCharacter> RosterScratch = new(4);

    /// <summary>
    /// The party roster, guarded — <c>CMapParty.SelectedCharactersArray</c> with its null holes
    /// removed, in SLOT ORDER (which is what makes the choice stable and peer-predictable).
    /// <c>AdventureState.MapState</c> is null outside a loaded campaign, and the array is
    /// fixed-length-4 with holes (CMapParty.cs:70).
    /// </summary>
    private static List<CMapCharacter> Roster()
    {
        List<CMapCharacter> result = RosterScratch;
        result.Clear();
        try
        {
            MapRuleLibrary.State.CMapState? state = MapRuleLibrary.Adventure.AdventureState.MapState;
            CMapParty? party = state != null ? state.MapParty : null;
            CMapCharacter[]? slots = party != null ? party.SelectedCharactersArray : null;
            if (slots == null)
                return result;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    result.Add(slots[i]);
            }
        }
        catch (System.Exception)
        {
            // A half-loaded campaign answers "no roster", which is the safe direction: the rule then
            // supplies nobody rather than a character read out of a torn state.
        }
        return result;
    }

    // ------------------------------------------------------------------------------- the driver --

    /// <summary>
    /// Per-frame entry point — call it while the map room stands. Everything below the two cadence
    /// compares is level-triggered: it looks at the OUTCOME ("is somebody selected?"), never at an
    /// event, so it cannot latch on a missed edge and cannot fire twice for one state.
    /// </summary>
    internal static void Tick()
    {
        float now = Time.unscaledTime;

        // ---- ModBuild 447 — THE DROP EDGE. ------------------------------------------------------
        //
        // USER REPORT, verbatim (2026-09-05): "Wenn die Map gewechselt wird, blinkt auch immer ganz
        // kurz der ausgewählte Character in dem Character-UI Fenster. Ich möchte dass es da stabil
        // bleibt."
        //
        // WHAT BLINKS, AND WHOSE FAULT EACH HALF IS. The GAME drops the selection on every
        // guildmaster mode change, and it does it in a place the class doc above did not list:
        // UIGuildmasterHUD.UpdateCurrentMode (:435) runs modes[current].Exit() -> OnLeaveMap (:523)
        // -> NewPartyDisplayUI.DisableMapOptions() (:1501), whose LAST statement is CloseWindows()
        // (:1316), whose last statement is `selectedCharacter?.Escape()` (:1323) —
        // NewPartyCharacterUI.Escape() (:1453) calls OnClick() on the already-selected slot, which
        // toggles it OFF and reaches SelectCurrentCharacter(null). The game contradicts itself
        // inside one method: DisableMapOptions' own first lines take care to unselect ONLY a slot
        // that is not Assigned, and then CloseWindows throws the Assigned one away anyway
        // ([[the-game-threw-away-its-own-fact]]).
        //
        // The mod already puts it back — that is this whole class, under the user's 2026-08-15
        // ruling — so nothing new is written here and no new owner of the selection is created. What
        // was wrong is WHEN: the restore was reached by a 4 Hz poll, so the panel painted nobody for
        // up to 250 ms. The ModBuild 446 log has all four map switches with the same three beats and
        // no fourth: press (:4996), the game's own programmatic deselect seen through the mod's
        // click patch (:5004 "CHARACTER CLICK SUPPRESSED THE AUTO-OPEN ... slot 0"), and the restore
        // (:5066 "the party display was showing NOBODY ... SELECTED 'Testi'"). Ten such suppressed
        // clicks in the session, seven of them within 2-11 lines of a GUILDMASTER WINDOW PRESS.
        //
        // WHY AN EDGE AND NOT A FASTER POLL. A poll is at most one period late whatever the period
        // is; only an edge is bounded by a frame. And it is deliberately keyed on the SELECTION, not
        // on the guildmaster mode: this class must not learn a second subsystem's state machine to
        // do its own job, and a drop is a drop whoever caused it — the same edge covers the panel
        // Hide(deselectCurrentCharacter: true) path the class doc already names.
        //
        // WHY IT CANNOT MAKE THE FUSE HAIR-TRIGGER, which is the objection it has to answer
        // ([[a-fuse-cannot-tell-a-hand-from-a-loop]]). DriveFuse counts CONSECUTIVE drives whose
        // outcome was still nobody, one per evaluation. This edge is `somebody -> nobody`, so it can
        // fire at most once per drop: the frame after a drive that landed there is a selection and
        // no edge, and the frame after a drive that did NOT land the previous frame was also nobody
        // and there is no edge either. A drop therefore adds exactly ONE evaluation, and the drive
        // rate stays the 4 Hz it has always been.
        //
        // COST: one Selected read per frame in place of two float compares — a static singleton
        // field, a cast, a field and two properties, inside the try/catch that read already carries.
        bool hasSelection = Selected != null;
        bool dropEdge = _hadSelection && !hasSelection;
        _hadSelection = hasSelection;
        if (dropEdge)
        {
            _dropEdges++;
            bool insidePollWindow = now < _nextEvaluate;
            if (insidePollWindow)
                _dropEdgesInsidePollWindow++;
            if (_dropEdges <= DropEdgeLinesPerSession)
            {
                // HW-VERIFY — the ONE line that says this round fired. It is Note (printed in a
                // shipped build) because the next hardware round has to read it: the user's report
                // is a sub-second visual and there is no other way to tell "the blink is gone
                // because the edge caught it" from "the blink is gone because the game stopped
                // deselecting". Capped, with the running total on the census, so a window that
                // dropped every frame could not drown the log ([[a-cap-that-goes-silent]]).
                VRLog.Note(Scope,
                    $"SELECTION GUARD DROP EDGE: map — the party display went from somebody to NOBODY "
                    + $"on THIS frame (edge {_dropEdges} of at most {DropEdgeLinesPerSession} printed; "
                    + $"{_dropEdgesInsidePollWindow} so far arrived inside the 4 Hz poll's own window). "
                    + (insidePollWindow
                        ? "IT LANDED INSIDE THE POLL WINDOW, which is the user's 'blinkt auch immer "
                          + "ganz kurz' case: before ModBuild 447 the panel would have painted nobody "
                          + "until the next poll, up to 250 ms = about 22 frames at 90 Hz. The rule is "
                          + "being evaluated on this frame instead"
                        : "The poll was due on this frame anyway, so this edge changed nothing and is "
                          + "printed only so the two cases cannot be confused")
                    + ". THE OUTCOME IS THE NEXT 'SELECTION GUARD: map —' LINE: it names who was put "
                    + "back and through which seam, or why nothing could be written. WHAT DROPPED IT "
                    + "is normally the game's own mode change — UIGuildmasterHUD.UpdateCurrentMode "
                    + "runs the leaving mode's Exit, which reaches NewPartyDisplayUI.CloseWindows and "
                    + "its closing selectedCharacter.Escape() -> OnClick() — so a CHARACTER CLICK "
                    + "SUPPRESSED THE AUTO-OPEN line within a few lines above is this edge's cause. "
                    + "An edge with no such line beside it is a DIFFERENT drop source and the lead.");
            }
        }

        bool evaluate = dropEdge || now >= _nextEvaluate;
        bool census = now >= _nextCensus;
        if (!evaluate && !census)
            return;
        if (evaluate)
            _nextEvaluate = now + EvaluateSeconds;
        if (census)
            _nextCensus = now + CensusSeconds;

        try
        {
            if (evaluate)
                Evaluate();
            if (census)
                Census();
        }
        catch (System.Exception ex)
        {
            // Presentation question: never take the map room down for it, and never go silent about
            // it either — a rule that stopped enforcing must say so.
            VRLog.Debug(Scope, $"SELECTION GUARD: map — the rule could not be evaluated ({ex.Message}); "
                               + "the map room keeps whatever it was showing.");
        }
    }

    /// <summary>
    /// One evaluation of the ruling for the LOCAL player: is somebody selected, and if not, may we
    /// supply one and through which layer? Emits at most one line per CHANGE of the answer.
    /// </summary>
    private static void Evaluate()
    {
        if (!InMapPhase)
        {
            _held = null;
            _drivenId = null;
            _loggedEdge = string.Empty;
            return;
        }

        CMapCharacter? selected = Selected;
        if (selected != null)
        {
            // The ruling holds by the game's own hand. Reset the drive's bookkeeping: whatever the
            // last drive did, the outcome is now correct, so no failure is outstanding.
            _held = selected;
            _drivenId = null;
            _driveFailures = 0;
            if (Edge($"ok:{selected.CharacterName}"))
                VRLog.Info(Scope, $"SELECTION GUARD: map — the party display is showing "
                                  + $"'{Name(selected)}'; nothing to supply.");
            return;
        }

        // Nobody is selected. Was that our own drive failing to take?
        if (_drivenId != null)
        {
            _driveFailures++;
            string failed = _drivenId;
            _drivenId = null;
            if (_driveFailures >= DriveFuse && !_driveStoodDown)
            {
                _driveStoodDown = true;
                VRLog.Warn(Scope,
                    $"SELECTION GUARD: map — the drive STOOD DOWN after {_driveFailures} consecutive "
                    + $"selections that did not take (last asked for '{failed}' through "
                    + "NewPartyDisplayUI.SelectCharacterById and the slot's own OnClick, and the "
                    + "display still shows nobody). Something else is clearing the selection every "
                    + "time, and continuing would be a write war. The PRESENTATION FLOOR is "
                    + "unaffected and still holds a character for the map room — only the flat "
                    + "party display is left as the game leaves it. The counter is reset by any "
                    + "selection that does stick, so this is a session verdict, not a permanent one.");
            }
        }

        CMapCharacter? floor = Floor(out string floorSource);
        if (floor == null)
        {
            // THE EMPTY STATE, AND IT IS CORRECT. This is the half of the ruling that says the state
            // must remain REACHABLE: a player with no assigned character shows nobody, and that is
            // not a bug.
            if (Edge("empty"))
                VRLog.Info(Scope,
                    "SELECTION GUARD: map — nobody is selected AND there is no character to supply "
                    + $"({floorSource}). The empty board is the truth here: the ruling reserves this "
                    + "state for exactly a player who has not been assigned a character.");
            return;
        }

        // Can we write the game's own selection, or is this a presentation-floor moment?
        if (CanDrive(out string? why))
        {
            Drive(floor, floorSource);
            return;
        }

        if (Edge($"floor:{floor.CharacterName}"))
            VRLog.Info(Scope,
                $"SELECTION GUARD: map — the party display shows nobody and cannot be written "
                + $"({why}), so the map room holds '{Name(floor)}' READ-ONLY instead of showing "
                + $"nobody — {floorSource}. 'No character selected' is not a state (user "
                + "2026-08-15, \"auch in der Map\"); the view returns to the player's own choice "
                + "the moment he makes one.");
    }

    /// <summary>
    /// May the game's own selection be written right now? Only into an OPEN, initialised party
    /// display that is not in the middle of a party-composition edit. Every false here is a state in
    /// which the flat game deliberately holds no selection, and writing one would be fought on the
    /// next frame — the project's "concede the flag, own the number" ruling. The FLOOR carries the
    /// rule in all of them.
    /// </summary>
    private static bool CanDrive(out string? why)
    {
        if (_driveStoodDown)
        {
            why = "the drive stood down earlier this session (see the STOOD DOWN line)";
            return false;
        }
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        if (display == null)
        {
            why = "there is no NewPartyDisplayUI in this scene";
            return false;
        }
        if (!display.Initialised)
        {
            why = "the party display is not initialised yet";
            return false;
        }
        if (!display.IsOpen)
        {
            // The common case, and the whole reason layer 2 exists: NewPartyDisplayUI.Hide's
            // deselectCurrentCharacter defaults to TRUE (NewPartyDisplayUI.cs:702-705), so a closed
            // panel HAS no selection by design and re-writing one would be undone by the next hide.
            why = "the party panel is CLOSED — its own Hide() clears the selection by design "
                  + "(NewPartyDisplayUI.cs:702), so a write here would be undone, not kept";
            return false;
        }
        if (SwitchingCharacter)
        {
            why = "a character switch is in flight (PlayerRegistry.IsSwitchingCharacter) — the "
                  + "game's own NewPartyCharacterUI.OnClick refuses in this window too";
            return false;
        }
        try
        {
            UIAdventurePartyAssemblyWindow? selector = display.CharacterSelector;
            if (selector != null && selector.IsVisible)
            {
                why = "the party-assembly roster is open — the player is editing party COMPOSITION, "
                      + "and a selection written under that is not his";
                return false;
            }
        }
        catch (System.Exception)
        {
            why = "the party-assembly roster could not be asked";
            return false;
        }
        why = null;
        return true;
    }

    /// <summary>
    /// Write the selection THROUGH THE GAME'S OWN SEAM. Primary is
    /// <c>NewPartyDisplayUI.SelectCharacterById(string)</c> (NewPartyDisplayUI.cs:1455) because that
    /// is literally the call <c>UILoadoutManager.AutoselectCharacter</c> makes for the same purpose.
    ///
    /// <para>It is followed IN THE SAME CALL by the slot's own <c>OnClick()</c> when the primary did
    /// not take, and that is not belt-and-braces superstition: <c>SelectCharacterById</c> routes
    /// through <c>_tab.Select(slot)</c>, and <c>UINavigationTabComponent</c> is in an assembly that is
    /// not decompiled — we cannot READ what it does, so we must MEASURE it (project ruling: "verify
    /// the outcome, not the path"). <c>NewPartyCharacterUI.OnClick()</c> (NewPartyCharacterUI.cs:802)
    /// is the flat client's own click handler, wired straight onto the slot's button
    /// (NewPartyCharacterUI.cs:332-338), and it reaches <c>selectedCharacter</c> without touching the
    /// tab component at all. It is a TOGGLE, which is exactly why it is safe here: we only reach it
    /// with the display holding nobody, so the worst case is one wasted tick and the next evaluation
    /// selects.</para>
    ///
    /// <para>Nothing else is written. No roster change, no <c>GameAction</c>, no rules call — see the
    /// class doc's multiplayer paragraph for why this cannot be observed by a peer.</para>
    /// </summary>
    private static void Drive(CMapCharacter character, string floorSource)
    {
        NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
        string? id = character.CharacterID;
        if (display == null || string.IsNullOrEmpty(id))
            return;

        string seam;
        try
        {
            display.SelectCharacterById(id!);
            seam = "NewPartyDisplayUI.SelectCharacterById";
            // THE OUTCOME, NOT THE PATH, and by REFERENCE — "somebody is selected now" is not the
            // question. SelectCharacterById resolves the slot by CharacterID, and a campaign can
            // hold two characters of the same class, so an id-only check could accept the wrong
            // hero; and a seam that lands on a TEAMMATE would satisfy "not empty" while breaking the
            // ruling, which is about one of THIS player's characters.
            if (!ReferenceEquals(Selected, character))
            {
                // The primary did not land where we asked. GetCharacterUI does not null-check its own
                // result inside SelectCharacterById (GetCharacterSlot(null) yields -1), so a missing
                // slot is one of the ways that happens — ask for the slot ourselves and use the click
                // seam, which reaches selectedCharacter without the tab component at all.
                NewPartyCharacterUI? slot = display.GetCharacterUI(id!);
                if (slot != null && slot.State == PartySlotState.Assigned
                    && ReferenceEquals(slot.Data, character))
                {
                    slot.OnClick();
                    seam = "NewPartyDisplayUI.SelectCharacterById, then the slot's own OnClick()";
                }
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"SELECTION GUARD: map — supplying '{Name(character)}' THREW "
                              + $"({ex.Message}); the map room falls back to holding it read-only. "
                              + "The flat party display is left as the game left it.");
            _drivenId = null;
            return;
        }

        // Remembered so the NEXT evaluation checks the outcome instead of trusting the call.
        _drivenId = id;

        if (!Edge($"drive:{character.CharacterName}"))
            return;
        int assigned = AssignedCount(LocalPlayerId);
        VRLog.Info(Scope,
            "SELECTION GUARD: map — the party display was showing NOBODY while this player has "
            + $"{(assigned < 0 ? "?" : assigned.ToString())} assigned character(s); SELECTED "
            + $"'{Name(character)}' through the game's own seam ({seam}) — {floorSource}. This is "
            + "the call UILoadoutManager.AutoselectCharacter makes (UILoadoutManager.cs:368); no "
            + "roster, no rules state and no wire byte was touched. 'No character selected' is not "
            + "a state (user 2026-08-15, \"auch in der Map\").");
    }

    /// <summary>
    /// ONE LINE PER EDGE. True exactly when the reported state <paramref name="key"/> differs from
    /// the last one announced, so the caller builds its sentence only on a change — a 4 Hz
    /// evaluation costs one string compare and nothing else while the answer is steady.
    ///
    /// <para>Deliberately a predicate rather than a method taking the message: a
    /// <c>Func&lt;string&gt;</c> parameter would allocate a closure on EVERY evaluation, including
    /// the overwhelming majority that log nothing, which is precisely the per-frame garbage this
    /// shape exists to avoid.</para>
    /// </summary>
    private static bool Edge(string key)
    {
        if (_loggedEdge == key)
            return false;
        _loggedEdge = key;
        return true;
    }

    // ------------------------------------------------------------------------------ the census --

    /// <summary>Scratch for the census line, reused for the same reason as <see cref="RosterScratch"/>.</summary>
    private static readonly System.Text.StringBuilder CensusText = new(512);

    /// <summary>The census's OWN copy of the roster. Deliberately not <see cref="RosterScratch"/>:
    /// the census holds its roster across a loop that calls <see cref="AssignedCount"/>, which
    /// rebuilds that scratch list — two consumers of one reused buffer is how a scratch list becomes
    /// a bug (the same note <c>MapRoomHand</c> keeps on its own second list).</summary>
    private static readonly List<CMapCharacter> CensusRoster = new(4);

    /// <summary>
    /// THE FALSIFIER, on a cadence. One line that answers by grep alone, for EVERY player: does he
    /// have at least one assigned character, and who is he showing? And then the verdict — a
    /// sentence that is TRUE ONLY IF THE RULING HOLDS, so a green log is evidence and not decoration.
    ///
    /// <para>Grep <c>SELECTION GUARD VERDICT</c> for the verdict alone. It reports four buckets and
    /// never folds any of them into <c>holds</c>: <c>holding</c> (assigned ≥ 1 and showing
    /// somebody), <c>VIOLATING</c> (assigned ≥ 1 and showing nobody — the ruling broken),
    /// <c>unobserved</c> (a player whose selection or whose assignment this client cannot read), and
    /// <c>no-assignment</c> (the ONLY group the ruling allows to show nobody).</para>
    ///
    /// <para>Where each half comes from. ASSIGNMENT is the game's own registry for every player
    /// alike (<see cref="AssignedCount"/>). SELECTION is this client's own display/floor for the
    /// LOCAL player, and for a peer it is the character key they broadcast for their map-room fan
    /// (<c>RemoteMapRoom.TryGetPeerFanCharacterKey</c>, extras record 20) resolved against the shared
    /// roster — the only view of a peer's selection that exists, since the selection itself is never
    /// replicated.</para>
    ///
    /// <para><b>AND THE INSTRUMENT PRINTS ITS OWN BLIND SPOT, because it has one.</b>
    /// <c>TryGetPeerFanCharacterKey</c> answers false both for "that peer has sent no map-room
    /// record" and for "that peer's record says they are showing nobody" — record 20 carries key 0
    /// for both, so this client cannot tell them apart. A peer therefore can NEVER be counted as
    /// VIOLATING here; they land in <c>unobserved</c>, and the verdict says so in its own words. The
    /// falsifier is consequently a statement about every player whose state this client can actually
    /// read, and the honest way to falsify the ruling for a peer is to read THEIR log, where they are
    /// the local player. Do not upgrade this to a pass.</para>
    /// </summary>
    private static void Census()
    {
        if (!InMapPhase)
            return;
        CensusRoster.Clear();
        CensusRoster.AddRange(Roster());
        if (CensusRoster.Count == 0)
            return;

        int local = LocalPlayerId;
        CMapCharacter? mine = Current(out string mineSource);

        CensusText.Length = 0;
        CensusText.Append("SELECTION GUARD CENSUS: map — ");

        int holds = 0;
        int violated = 0;
        int unobserved = 0;
        int unassigned = 0;

        List<int> players = PlayerIds(local);
        for (int i = 0; i < players.Count; i++)
        {
            int id = players[i];
            int assigned = AssignedCount(id);
            bool isLocal = id == local;

            string shown;
            if (isLocal)
                shown = mine != null ? $"'{Name(mine)}'" : "NOBODY";
            else if (RemoteMapRoom.TryGetPeerFanCharacterKey(id, out uint key))
            {
                CMapCharacter? peer = ByKey(CensusRoster, key);
                shown = peer != null ? $"'{Name(peer)}'" : $"key 0x{key:X8} (not in this roster)";
            }
            else
                shown = "unreadable from here (no map-room key: either no record yet, or their own "
                        + "record says nobody — record 20 cannot separate the two)";

            if (assigned < 0)
                unobserved++;              // the registry could not be read at all
            else if (assigned == 0)
                unassigned++;              // the one state the ruling allows to show nobody
            else if (!isLocal)
                unobserved++;              // see the blind-spot paragraph: never counted as a pass
            else if (mine == null)
                violated++;                // THIS client, assigned >= 1, showing nobody
            else
                holds++;

            if (i > 0)
                CensusText.Append("; ");
            CensusText.Append('p').Append(id).Append(' ').Append(PlayerName(id))
                      .Append(" assigned=").Append(assigned < 0 ? "?" : assigned.ToString())
                      .Append(" showing=").Append(shown);
        }

        CensusText.Append(". LOCAL source: ").Append(mineSource).Append('.');

        // The verdict, and it is a sentence that cannot be true while the ruling is broken here.
        CensusText.Append(" SELECTION GUARD VERDICT: ");
        CensusText.Append(violated == 0
            ? "RULING HOLDS — no player this client can read has at least one assigned character "
              + "and is showing nobody. COUNTS: "
            : "RULING VIOLATED — this client has at least one assigned character and is showing "
              + "NOBODY. COUNTS: ");
        CensusText.Append(holds).Append(" holding, ").Append(violated).Append(" VIOLATING, ")
                  .Append(unobserved).Append(" unobserved, ").Append(unassigned)
                  .Append(" with no assigned character — the last group is the ONLY one the ruling "
                          + "allows to show nobody, and 'unobserved' is a blind spot, not a pass; "
                          + "a peer is always unobserved here, so falsify their half in THEIR log.");

        // ModBuild 447 — THE DROP-EDGE LEDGER, and it is the falsifier for the "blinkt ganz kurz"
        // fix rather than a statistic. A drop edge is the frame the party display goes from somebody
        // to nobody; the SECOND number is the ones that landed inside the 4 Hz poll's own window,
        // i.e. exactly the drops that used to leave the panel painting nobody until the next poll —
        // up to 250 ms, about 22 frames at 90 Hz. So: a session in which the user switched maps and
        // this reads 0 edges means the edge is not seeing the game's deselect and the fix is inert
        // ([[a-held-instrument-reads-as-dead]]); a session in which it reads N edges and the
        // VIOLATED count above is also non-zero means the edge fires and the RESTORE is what fails,
        // which is a different defect and is read off the drive's own lines.
        CensusText.Append(" DROP-EDGE LEDGER (ModBuild 447): ").Append(_dropEdges)
                  .Append(" frame(s) this session on which the party display went from somebody to "
                          + "NOBODY, of which ").Append(_dropEdgesInsidePollWindow)
                  .Append(" arrived inside the 4 Hz poll's own window and were evaluated on the drop "
                          + "frame instead of up to 250 ms later — that second number IS the "
                          + "user's \"blinkt auch immer ganz kurz\" report, counted. The game's "
                          + "deselect is UIGuildmasterHUD.UpdateCurrentMode -> OnLeaveMap -> "
                          + "NewPartyDisplayUI.DisableMapOptions -> CloseWindows -> "
                          + "selectedCharacter.Escape() -> OnClick(), which is why every one of them "
                          + "sits beside a CHARACTER CLICK SUPPRESSED THE AUTO-OPEN line.");

        if (violated == 0)
            VRLog.Info(Scope, CensusText.ToString());
        else
            VRLog.Warn(Scope, CensusText.ToString());
    }

    /// <summary>The roster member whose <c>FNV-1a(CharacterName)</c> is <paramref name="key"/> — the
    /// receiving half of the map-room fan's own identification (extras record 20).</summary>
    private static CMapCharacter? ByKey(List<CMapCharacter> party, uint key)
    {
        for (int i = 0; i < party.Count; i++)
        {
            try
            {
                if (NetProtocol.HashMapKey(party[i].CharacterName) == key)
                    return party[i];
            }
            catch (System.Exception)
            {
                // A member whose name cannot be read is simply not the match.
            }
        }
        return null;
    }

    /// <summary>True while a map is loaded and no scenario is running — the same predicate
    /// <c>RevealGate.InMapPhase</c> uses, asked here without taking a dependency on the reveal
    /// rules.</summary>
    private static bool InMapPhase
    {
        get
        {
            try
            {
                return MapRuleLibrary.Adventure.AdventureState.MapState != null
                       && ScenarioRuleLibrary.ScenarioManager.Scenario == null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The display name the game itself would show — the player's own renaming wins
    /// (<c>CMapCharacter.DisplayCharacterName</c>, CMapCharacter.cs:49).</summary>
    private static string Name(CMapCharacter? character)
    {
        if (character == null)
            return "nobody";
        try
        {
            string? display = character.DisplayCharacterName;
            if (!string.IsNullOrEmpty(display))
                return display!;
            return character.CharacterName ?? character.CharacterID ?? "?";
        }
        catch (System.Exception)
        {
            return "?";
        }
    }

    // ------------------------------------------------- WHO OWNS WHAT — the game's own registry --
    //
    // REFLECTION-ONLY, for the reason Net/NetPlayerActors and MapRoom/MapRoomHand both state at
    // length: FFSNet.NetworkPlayer is EntityBehaviour<IPlayerState>, i.e. Bolt-derived, and this
    // build has (and needs) no bolt.dll. So the registry, the controllable and the player are all
    // handled as `object` and every member is reached through AccessTools. Anything missing degrades
    // to "unknown assignment", which costs the census its numbers and NOTHING else — the floor reads
    // CMapCharacter.IsUnderMyControl, which is the game's own local mirror and needs no reflection.
    //
    // THE SHAPE, from the game's source:
    //   ControllableRegistry.GetControllable(int) : NetworkControllable   (ControllableRegistry.cs:105)
    //   NetworkControllable.Controller            : NetworkPlayer        (NetworkControllable.cs:53)
    //   NetworkControllable.ControllableObject    : IControllable        (:55)
    //   NetworkPlayer.PlayerID                    : int                  (NetworkPlayer.cs:29)
    //   NetworkPlayer.Username                    : string               (:43)
    //   PlayerRegistry.AllPlayers                 : List<NetworkPlayer>  (PlayerRegistry.cs:13, a FIELD)
    //   PlayerRegistry.MyPlayer                   : NetworkPlayer        (:86)
    //   PlayerRegistry.HostPlayerID               : int = 1              (:37, a readonly FIELD)
    //   PlayerRegistry.IsSwitchingCharacter       : bool                 (:62)
    //   BenchedCharacter                          : IControllable        (BenchedCharacter.cs)

    private static bool _reflected;
    private static MethodInfo? _getControllable;   // static NetworkControllable GetControllable(int)
    private static PropertyInfo? _controller;      // NetworkPlayer
    private static PropertyInfo? _controllableObj; // IControllable
    private static PropertyInfo? _playerId;        // int
    private static PropertyInfo? _username;        // string
    private static FieldInfo? _allPlayers;         // static List<NetworkPlayer>
    private static PropertyInfo? _myPlayer;        // static NetworkPlayer
    private static FieldInfo? _hostPlayerId;       // static readonly int
    private static PropertyInfo? _isSwitching;     // static bool
    private static System.Type? _benchedType;

    private static void Reflect()
    {
        if (_reflected)
            return;
        _reflected = true;
        try
        {
            System.Type? registry = AccessTools.TypeByName("FFSNet.ControllableRegistry");
            System.Type? controllable = AccessTools.TypeByName("FFSNet.NetworkControllable");
            System.Type? player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            System.Type? players = AccessTools.TypeByName("FFSNet.PlayerRegistry");
            _benchedType = AccessTools.TypeByName("BenchedCharacter");

            _getControllable = registry?.GetMethod("GetControllable",
                BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            _controller = controllable?.GetProperty("Controller");
            _controllableObj = controllable?.GetProperty("ControllableObject");
            _playerId = player?.GetProperty("PlayerID");
            _username = player?.GetProperty("Username");
            _allPlayers = players?.GetField("AllPlayers", BindingFlags.Public | BindingFlags.Static);
            _myPlayer = players?.GetProperty("MyPlayer", BindingFlags.Public | BindingFlags.Static);
            _hostPlayerId = players?.GetField("HostPlayerID", BindingFlags.Public | BindingFlags.Static);
            _isSwitching = players?.GetProperty("IsSwitchingCharacter",
                BindingFlags.Public | BindingFlags.Static);
        }
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, $"SELECTION GUARD: map — the FFSNet reflection failed ({ex.Message}); "
                               + "the census reports assignment as unknown. The rule itself is "
                               + "unaffected: it reads CMapCharacter.IsUnderMyControl.");
        }
    }

    /// <summary>The controllable id the game itself would use for <paramref name="character"/> —
    /// campaign hashes the instance NAME (a campaign may hold two Brutes), guildmaster uses the
    /// class' model instance id. The ternary is the game's own, inlined identically at
    /// NewPartyDisplayUI.cs:511, NewPartyCharacterUI.cs:565 and BenchedCharacter.cs:20/24.
    /// 0 = unanswerable (and 0 is reserved by the registry itself, ControllableRegistry.cs:25).</summary>
    private static int ControllableId(CMapCharacter? character)
    {
        if (character == null)
            return 0;
        try
        {
            MapRuleLibrary.State.CMapState? state = MapRuleLibrary.Adventure.AdventureState.MapState;
            if (state == null)
                return 0;
            if (state.IsCampaign)
            {
                string? name = character.CharacterName;
                return string.IsNullOrEmpty(name) ? 0 : name!.GetHashCode();
            }
            string? id = character.CharacterID;
            return string.IsNullOrEmpty(id)
                ? 0
                : ScenarioRuleLibrary.CharacterClassManager.GetModelInstanceIDFromCharacterID(id);
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    /// <summary>Is <paramref name="character"/> currently a BENCH entry rather than a party member?
    /// Asked of the game's own registry: <c>BenchedCharacter</c> is the <c>IControllable</c> the map
    /// phase parks an unselected roster member on (BenchedCharacter.cs:15-26), and a bench entry is
    /// released from its controller by construction (NetworkControllable.cs:130-141). Unanswerable ⇒
    /// false, because the roster read has already excluded the bench and this is the belt.</summary>
    private static bool IsBench(CMapCharacter character)
    {
        Reflect();
        if (_getControllable == null || _controllableObj == null || _benchedType == null)
            return false;
        try
        {
            int id = ControllableId(character);
            if (id == 0)
                return false;
            object? controllable = _getControllable.Invoke(null, new object[] { id });
            if (controllable == null)
                return false;
            object? obj = _controllableObj.GetValue(controllable);
            return obj != null && _benchedType.IsInstanceOfType(obj);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// How many PARTY characters are assigned to <paramref name="playerId"/>, from the game's own
    /// ownership. Offline the whole roster is the local player's — see the class doc: offline no
    /// controller is ever assigned, so asking the registry there would answer 0 for everybody and
    /// make the ruling look violated in single player.
    ///
    /// <para>A participating controllable whose <c>Controller</c> is null belongs to the HOST, exactly
    /// as <c>NetworkHeroAssignService.GetCharacterAssignations</c> resolves it
    /// (NetworkHeroAssignService.cs:55).</para>
    /// </summary>
    private static int AssignedCount(int playerId)
    {
        List<CMapCharacter> party = Roster();
        if (!FFSNetwork.IsOnline)
            return playerId == LocalPlayerId ? party.Count : 0;

        Reflect();
        if (_getControllable == null || _controller == null || _playerId == null)
            return -1; // unanswerable; the census prints it as such rather than as 0

        int count = 0;
        for (int i = 0; i < party.Count; i++)
        {
            if (OwnerOf(party[i]) == playerId)
                count++;
        }
        return count;
    }

    /// <summary>The player id controlling <paramref name="character"/>, or 0 when unanswerable.
    /// Null controller ⇒ the host (the game's own convention, see <see cref="AssignedCount"/>).</summary>
    private static int OwnerOf(CMapCharacter? character)
    {
        Reflect();
        if (_getControllable == null || _controller == null || _playerId == null)
            return 0;
        try
        {
            int id = ControllableId(character);
            if (id == 0)
                return 0;
            object? controllable = _getControllable.Invoke(null, new object[] { id });
            if (controllable == null)
                return 0;
            if (_controllableObj != null && _benchedType != null)
            {
                object? obj = _controllableObj.GetValue(controllable);
                if (obj != null && _benchedType.IsInstanceOfType(obj))
                    return 0; // benched: owned by nobody, and never counted as "assigned"
            }
            object? player = _controller.GetValue(controllable);
            if (player == null)
                return HostPlayerId;
            return _playerId.GetValue(player) is int pid ? pid : 0;
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    /// <summary>Scratch for <see cref="PlayerIds"/>.</summary>
    private static readonly List<int> PlayerScratch = new(4);

    /// <summary>Every player in the session, local first. Offline there is no
    /// <c>PlayerRegistry.AllPlayers</c> at all (it is populated from Bolt), so the answer is the one
    /// local seat — which is correct: single player has exactly one player to hold the ruling for.</summary>
    private static List<int> PlayerIds(int local)
    {
        List<int> result = PlayerScratch;
        result.Clear();
        result.Add(local);
        if (!FFSNetwork.IsOnline)
            return result;
        Reflect();
        if (_allPlayers == null || _playerId == null)
            return result;
        try
        {
            if (_allPlayers.GetValue(null) is not System.Collections.IEnumerable all)
                return result;
            foreach (object? player in all)
            {
                if (player == null)
                    continue;
                if (_playerId.GetValue(player) is not int id || id == local || result.Contains(id))
                    continue;
                result.Add(id);
            }
        }
        catch (System.Exception)
        {
            // A torn registry answers "just me", which is honest rather than wrong: the census then
            // reports fewer players, and no peer is silently marked as holding the ruling.
        }
        return result;
    }

    /// <summary>The local player's transport id. Offline there is no <c>MyPlayer</c>, so this is the
    /// host id — offline the one seat IS the host by the game's own numbering
    /// (<c>PlayerRegistry.HostPlayerID = 1</c>).</summary>
    private static int LocalPlayerId
    {
        get
        {
            Reflect();
            if (_myPlayer == null || _playerId == null)
                return HostPlayerId;
            try
            {
                object? me = _myPlayer.GetValue(null);
                if (me == null)
                    return HostPlayerId;
                return _playerId.GetValue(me) is int id ? id : HostPlayerId;
            }
            catch (System.Exception)
            {
                return HostPlayerId;
            }
        }
    }

    /// <summary><c>PlayerRegistry.HostPlayerID</c>, or the literal 1 the game declares it as
    /// (PlayerRegistry.cs:37) when the field cannot be read.</summary>
    private static int HostPlayerId
    {
        get
        {
            Reflect();
            try
            {
                return _hostPlayerId?.GetValue(null) is int id ? id : 1;
            }
            catch (System.Exception)
            {
                return 1;
            }
        }
    }

    /// <summary><c>PlayerRegistry.IsSwitchingCharacter</c> — the window the game's own
    /// <c>NewPartyCharacterUI.OnClick</c> refuses in (NewPartyCharacterUI.cs:804). Unreadable ⇒
    /// false, which is the pre-existing behaviour of every offline session.</summary>
    private static bool SwitchingCharacter
    {
        get
        {
            Reflect();
            if (_isSwitching == null)
                return false;
            try
            {
                return _isSwitching.GetValue(null) is true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    /// <summary>A player's name for the census, in quotes, or a placeholder. Never throws.</summary>
    private static string PlayerName(int playerId)
    {
        Reflect();
        if (_allPlayers == null || _playerId == null || _username == null)
            return "'?'";
        try
        {
            if (_allPlayers.GetValue(null) is not System.Collections.IEnumerable all)
                return "'?'";
            foreach (object? player in all)
            {
                if (player == null || _playerId.GetValue(player) is not int id || id != playerId)
                    continue;
                return $"'{_username.GetValue(player) as string ?? "?"}'";
            }
        }
        catch (System.Exception)
        {
            // fall through
        }
        return playerId == LocalPlayerId ? "'this client'" : "'?'";
    }

    /// <summary>Forget everything scene-local. Called when the map room stands down, so a new
    /// campaign cannot inherit a held character or a blown fuse from the last one.</summary>
    internal static void Forget()
    {
        _held = null;
        _drivenId = null;
        _driveFailures = 0;
        _driveStoodDown = false;
        _loggedEdge = string.Empty;
        _nextEvaluate = 0f;
        _nextCensus = 0f;
    }
}

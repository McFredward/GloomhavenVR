// THE MAP ROOM'S CARD HAND — the scenario loadout, in your hand, on the world map.
//
// ─── THE USER'S ASK, VERBATIM (2026-08-21) ────────────────────────────────────────────────────
// "Ich will ein neues Feature in der World-Map: Die Kartenhand inklusive des Characterinfo am
//  Handgelenk des aktuell ausgewählten Characters soll voll angezeigt werden. Auch soll man die
//  Karten normal in die Hand (beide) nehmen können. Zwar kann man sonst nicht damit interagieren,
//  aber so kann man sich die aktuell ausgewählten Karten vor einem Szenario nochmal anschauen. Das
//  Feature soll deaktivierbar sein. Weiterhin gibt es keine Geheimnisse in dieser Phase, das heißt
//  schon hier sollen alle Karten voll sichtbar sein der jeweiligen Mitspieler im MP, wenn sie sich
//  die Karten anschauen."
//
// And his four rulings, asked and answered directly on the same day — these are DECIDED:
//   1. The fan shows the SCENARIO LOADOUT (the cards chosen for the upcoming scenario), not a
//      random hand.
//   2. It follows the character SELECTED IN THE PARTY DISPLAY, and must FOLLOW a change of that
//      selection live.
//   3. Cards are takeable in BOTH hands, for INSPECTION ONLY — no play, no discard, no reordering,
//      no game state change of any kind.
//   4. The feature is DEFAULT ON and must be switchable off.
// Plus: the selected character's WRIST INFO must be shown for that same character.
//
// ─── AND THE RULING THAT REWROTE IT (ModBuild 191 was REJECTED, 2026-08-21) ───────────────────
// "Die Karten sind dauerhaft da und reagieren nicht auf der roll der Hand. Es gibt keine Animation
//  kein Highlighting. Die Characterinfo am Handgelenk sieht anders aus. Ich will das es sich hier
//  1:1 genauso verhält wie im Szenario selber. Am Besten nutzt du auch die selben Code Segmente. Es
//  soll sich nicht vom Szenario unterscheiden wie sich die Karten verhalten! Auch beim
//  Characterwechsel soll es die entsprechende Animation geben etc."
//
// ─── WHAT THIS IS, IN ONE PARAGRAPH ───────────────────────────────────────────────────────────
// While the 3D map room stands (<see cref="MapRoomDriver.Active"/>), THE SCENARIO'S OWN CARD HAND
// is shown, holding the selected character's scenario loadout. Not a copy of it: the same
// <c>Cards.CardsDriver</c>, the same <c>Cards.CardFan</c> instance, the same
// <c>Hands.Interact.PalmGate</c>, the same reveal dials, the same reveal/collapse/exchange
// animations, the same laser and fingertip highlighting, the same grab-to-read gesture. This class
// is a CARD SOURCE for that machinery and nothing else — see MapRoomHand.2.Fan.cs for the seam and
// for what was actually gating the machinery out of the map phase. The same character's vital
// statistics ride a plate on the wrist, built to the scenario plate's own layout, pose dials, fade
// and row format (MapRoomHand.3.Wrist.cs). When [WorldUI] MapRoomHand is off, none of it is built
// and the map room is byte-identical to a build without this file.
//
// ─── WHERE THE LOADOUT COMES FROM — READ FROM THE GAME'S OWN SOURCE ───────────────────────────
// There is NO separate "loadout" object in this game. The chosen-cards-for-the-next-scenario set is
// stored directly on the persistent per-character map-phase model:
//
//   decompiled/MapRuleLibrary/MapRuleLibrary.Party/CMapCharacter.cs:24   class CMapCharacter
//   decompiled/MapRuleLibrary/MapRuleLibrary.Party/CMapCharacter.cs:59   public List<int> HandAbilityCardIDs { get; private set; }
//   decompiled/MapRuleLibrary/MapRuleLibrary.Party/CMapCharacter.cs:103  public int MaxCards { get; private set; }
//
// It is the very list the game's own card-selection screen mutates —
// UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect adds to it (:536) and OnAbilityCardDeselect
// removes from it (:558) — the very list the loadout validator counts against MaxCards
// (UILoadoutManager.cs:189), and the very list that becomes the scenario hand when you travel
// (CMapParty.ExportPlayerStates, CMapParty.cs:1234 → CScenario.AddPlayer, CScenario.cs:243 →
// CCharacterClass.SetHand, CCharacterClass.cs:1234). So "the cards chosen for the upcoming
// scenario" is not an inference from a name: it is the same list, read one step earlier.
//
// The IDs are resolved to CAbilityCard through the class pool, which is what the game's own
// convenience projection does (CMapCharacter.cs:141 `HandAbilityCards`). We deliberately do NOT
// call that property: it is written with LINQ `.Single(...)`, which THROWS for a character whose
// class is missing from CharacterClassManager (a modded or half-loaded class), and an exception out
// of a per-frame path in this project starves VR input entirely. ResolveLoadout below is the same
// lookup written with Find + a null check.
//
// ─── WHERE "THE SELECTED CHARACTER" COMES FROM — ALSO READ FROM SOURCE ────────────────────────
//   decompiled/GH.Runtime/APartyDisplayUI.cs:16       public abstract APartyCharacterUI SelectedCharacter { get; }
//   decompiled/GH.Runtime/NewPartyDisplayUI.cs:27     class NewPartyDisplayUI : APartyDisplayUI   (the ONLY implementation)
//   decompiled/GH.Runtime/NewPartyDisplayUI.cs:226    public NewPartyCharacterUI SelectedUISlot => selectedCharacter;
//   decompiled/GH.Runtime/NewPartyDisplayUI.cs:246    public static NewPartyDisplayUI PartyDisplay => Singleton<APartyDisplayUI>.Instance as NewPartyDisplayUI;
//   decompiled/GH.Runtime/NewPartyCharacterUI.cs:280  public CMapCharacter Data => characterData;
// The slot is written by NewPartyDisplayUI.SelectCurrentCharacter (:861) out of OnCharacterSelect
// (:871), which is the handler every selection path in that window funnels through.
//
// WHY A 4 Hz POLL AND NOT THE GAME'S OWN EVENT. NewPartyDisplayUI does expose
// `public event Action<bool, NewPartyCharacterUI> NewCharacterSelected` (:270) and it has no
// subscribers at all in the decompiled tree, so it is a free hook. We poll a cheap SIGNATURE
// instead, for a reason that is about correctness rather than taste: that event fires on a
// SELECTION change and on nothing else, while the thing the fan has to follow ALSO changes when the
// player EDITS the loadout of the character already selected (the card-selection screen mutates
// HandAbilityCardIDs directly — see the two line references above — and deliberately does not raise
// OnAbilityDeckUpdated on that path). A signature poll catches both with one mechanism and cannot
// leak a subscription across a scene teardown.
//
// THE RATE IS NOT FIXED (ModBuild 193). 4 Hz while the fan is DOWN; 20 Hz while it is OPEN in his
// hand, because item 8 asks for an ANIMATION he is watching for and 250 ms of it is plainly late —
// see PollIntervalWatching for the cost, which is one hash of at most twelve ints and nothing else.
// The event route stays refused for the reason above: it cannot see a loadout edit at all.
//
// ─── MULTIPLAYER, AND WHY THIS FILE ADDS NOTHING TO THE WIRE ──────────────────────────────────
// THE STANDING RULE — CARD IDENTITY NEVER GOES ON THE WIRE — is not touched here, and no wire field
// is added, changed or removed by this feature:
//   * The faces are read LOCALLY, from the game's own object pool by card ID
//     (Net/RemoteAbilityCardSource.TryPooledClone) — the same mechanism the scenario's remote fan
//     already uses, off data this client already has in CharacterClassManager.
//   * ONE EXISTING FIELD NOW CARRIES A DIFFERENT VALUE, and it is stated here rather than left to
//     be discovered: the map fan IS a Cards.CardFan since ModBuild 192, so `CardFan.Current` is
//     non-null while it is open and NetAvatarDriver's always-sent PresenceState.HandCardCount byte
//     (sampled at NetAvatarDriver.cs:842) reports the real count instead of 0. Zero bytes were
//     added; a count that used to be a lie while we held cards is now the truth — and it is the
//     ONLY thing off the wire the peer half below needs.
//   * RevealGate.ShowRoundCardFronts is WIDE OPEN in the map phase and its own doc comment says so
//     ("offline / single player / MAP / our own actor / post-reveal action phase"): its conjunction
//     includes RevealGate.InScenario, which is false here (RevealGate.cs:31-64, re-read 2026-08-21).
//     There is nothing to unlock and nothing to relax — the user's "es gibt keine Geheimnisse in
//     dieser Phase" is already the game's own rule, and RevealGate.ShowMapPhaseHandFronts now says
//     it in that class's own vocabulary instead of leaving it to be re-derived.
//
// THE PEERS' HALF EXISTS SINCE ModBuild 226 — user report 2026-08-22 item 4, "Handkarten sind nicht
// sichtbar im Multiplayer im Map-Bereich … Aktuell sieht man nur die Rückseiten". Until then this
// header claimed the peer half "has nowhere to hang", and that claim was already stale when it was
// written: a peer's fan of CARD BACKS was being drawn in this room all along (Net/RemoteHandFan
// hangs off RemoteAvatar, which is DontDestroyOnLoad with no phase gate), which is exactly what he
// photographed. What was missing was never the fan — it was the FACES, and the reason was a
// capability term in the receiver, not a secrecy rule (RevealGate's map-phase block has the whole
// derivation). This file supplies the missing capability through TryResolvePeerLoadout below; the
// faces are then drawn by RemoteHandFan out of the receiver's OWN card art, and no wire byte was
// added for any of it. What is still UNBUILT is deliberate multiplayer PRESENCE in this room — the
// avatars are unseated and every client's menu rig sits at the same authored vantage, so they
// interpenetrate (MapRoomDriver.cs:40-42, plan phase 8 of .planning/worldmap-3d.md).
//
// ─── THE FAILURE MODE ─────────────────────────────────────────────────────────────────────────
// Everything here is best-effort and fails to NOTHING. The capability probe runs ONCE per engage and
// names the CONSEQUENCE in a single Warn if a member it needs is unreachable; after that the whole
// feature latches down for the session and the map room behaves exactly as it did before. Every
// tick body is wrapped: an exception stands the feature down rather than propagating, because an
// unguarded throw in an Update in this project starves VR input entirely (see
// WorldUI/WorldUIModule's TickGuard and the standing note in .planning/STATE.md).
//
// UNITS. Every length in these three files is REAL METRES and is named so. Nothing here multiplies
// by the map room's ~198 game-units-per-metre rig scale, and nothing here may start to: the fan is
// parented to VRRigDriver.RigRoot, whose lossy scale IS the diorama scale, so a 0.0635 m card
// renders 6.35 cm wide at the eye at every zoom by construction. The ONE world-unit product on the
// whole path is the palm STANDOFF, which lives in Cards/CardFan.cs:808 and is documented there —
// this lane no longer forms one at all.

using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using HarmonyLib;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The map room's loadout hand and its wrist plate. One instance, owned by
/// <see cref="MapRoomDriver"/>, engaged and stood down with the room.
///
/// <para>It is a CARD SOURCE for the scenario's own card machinery, not a second one: it resolves
/// the selected character and its loadout, builds <c>Cards.VRCard</c>s for them, and publishes the
/// list through <c>Cards.CardsDriver.OffScenarioFanCards</c>. Reveal, roll response, layout, every
/// animation, highlighting, the grab-to-read gesture and the character-switch exchange are the
/// scenario's — see MapRoomHand.2.Fan.cs for the seam and for the gate that used to keep them
/// out.</para>
/// </summary>
internal sealed partial class MapRoomHand
{
    private const string Scope = "MapRoom";

    /// <summary>Seconds between selection/loadout signature polls while the fan is DOWN. See the
    /// header for why this is a poll and not <c>NewPartyDisplayUI.NewCharacterSelected</c>.</summary>
    private const float PollIntervalIdle = 0.25f;

    /// <summary>
    /// Seconds between polls while the fan is OPEN IN HIS HAND — 20 Hz.
    ///
    /// <para>WHY THE RATE IS RAISED, AND WHAT IT COSTS (ModBuild 193). The user's ask is that a card
    /// he ticks on or off "per Animation auftauchen oder verschwinden" WHILE he is looking at the
    /// fan. At 4 Hz the animation would start up to 250 ms after the click — plainly late for a
    /// cause-and-effect he is watching for, and late in the worst way, because the menu's own
    /// checkbox has already responded. There is no cheaper edge to find: the two writers
    /// (UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect :536 / OnAbilityCardDeselect :558)
    /// mutate <c>HandAbilityCardIDs</c> in place and raise nothing at all, and
    /// <c>NewPartyDisplayUI.NewCharacterSelected</c> fires on a SELECTION change only — subscribing
    /// to it would miss every loadout edit, which is the whole event this feature is about.</para>
    ///
    /// <para>THE COST IS ONE HASH OF AT MOST TWELVE INTS, sixteen extra times a second, and ONLY
    /// while the hand is actually raised: <see cref="ResolveCharacter"/>'s authoritative path is two
    /// property reads on the party display and returns before the roster scan, and
    /// <see cref="BuildSignature"/> is a loop over <c>HandAbilityCardIDs</c>. The roster fallback —
    /// the one path that walks the party — now fills a reusable list instead of allocating one
    /// (<see cref="PartyMembers"/>), so the fast rate cannot turn into 20 allocations a second even
    /// when nothing is selected. A rebuild is still only requested when the signature MOVES.</para>
    /// </summary>
    private const float PollIntervalWatching = 0.05f;

    /// <summary>
    /// The live poll period. Fast while the shared fan is open with our cards in it (he is looking
    /// at the fan, so an edit must land on it almost at once), idle otherwise.
    /// </summary>
    private static float PollInterval =>
        CardsDriver.OffScenarioFanIsOpen ? PollIntervalWatching : PollIntervalIdle;

    /// <summary>Hard clamp on the slab count. A Gloomhaven loadout is 8–12 cards
    /// (<c>CharacterYMLData.NumberAbilityCardsInBattle</c>); this is the same defensive cap
    /// <c>Net.RemoteHandFan.MaxCards</c> applies, so a corrupt save can never build an arc of
    /// hundreds of slabs.</summary>
    private const int MaxCards = 12;

    // ---- lifecycle state ---------------------------------------------------------------------

    /// <summary>True between <see cref="Engage"/> and <see cref="StandDown"/>.</summary>
    private bool _engaged;

    /// <summary>Latched off for the rest of the session by the capability probe or by a caught
    /// exception. Once set, this class does nothing at all until the plugin is reloaded — that is
    /// the "stands completely down" contract, and it is deliberately NOT re-armed on the next
    /// engage: a member that was missing once will be missing again, and a Warn per map entry is a
    /// log the next hardware round cannot read.</summary>
    private bool _failed;

    /// <summary>The capability probe has run (once per session, on the first engage).</summary>
    private bool _probed;

    // ---- what is currently shown -------------------------------------------------------------

    /// <summary>The character the fan and the wrist plate are currently built for.</summary>
    private CMapCharacter? _character;

    /// <summary>The resolved loadout, index-aligned with the slabs. Never null once built.</summary>
    private readonly List<CAbilityCard> _loadout = new(MaxCards);

    /// <summary>Change detector for <see cref="_character"/> + its loadout. Rebuilt from
    /// <see cref="BuildSignature"/> at <see cref="PollInterval"/>; a difference is the ONE thing
    /// that triggers a rebuild.</summary>
    private int _signature = int.MinValue;

    /// <summary>Where <see cref="_character"/> came from, for the state line — "the party display"
    /// or one of the named fallbacks.</summary>
    private string _characterSource = "not resolved";

    private float _nextPollAt;

    /// <summary>Unscaled time of the PREVIOUS poll. The edit that a diff reports happened somewhere
    /// inside the window that ends now, so this is what the diff line's latency bound is measured
    /// from — a real number rather than a restatement of the configured interval.</summary>
    private float _lastPollAt;

    // ==========================================================================================
    //  LIFECYCLE
    // ==========================================================================================

    /// <summary>
    /// The map room has been built. Runs the ONE capability probe and arms the poll; builds
    /// nothing yet, because the party display may legitimately not exist for several frames after
    /// the room does.
    /// </summary>
    internal void Engage()
    {
        if (_failed || _engaged)
            return;
        _engaged = true;
        _nextPollAt = 0f;      // resolve on the very next tick
        _lastPollAt = Time.unscaledTime;
        _signature = int.MinValue;
        Probe();
    }

    /// <summary>
    /// Per-frame upkeep while the room stands. Everything is inside one try: this is called from
    /// <see cref="MapRoomDriver.TickActive"/>, i.e. from the rig's own update, and a throw here
    /// would take the rig with it.
    /// </summary>
    internal void Tick()
    {
        if (_failed)
            return;

        // The toggle is level-triggered, not edge-triggered, so switching it off mid-session tears
        // the feature down within a frame and switching it on rebuilds it — "deaktivierbar" with no
        // restart. When it is off nothing below runs and nothing is left standing.
        bool want = _engaged
                    && MapRoomDriver.Active
                    && MapRoomConfigOn
                    && VRSession.IsRunning;

        try
        {
            if (!want)
            {
                // HasRetiredCards is in the list because a card that is mid-exchange is NOT in
                // HasFan any more — dropping the feature between a character switch and the end of
                // that exchange would otherwise leave the outgoing wave standing with nobody left
                // to sweep it.
                if (_character != null || HasFan || HasRetiredCards || HasWrist)
                    Release("the map-room hand is not wanted this frame "
                            + "([WorldUI] MapRoomHand off, the room stood down, or VR stopped)");
                return;
            }

            if (Time.unscaledTime >= _nextPollAt)
            {
                float now = Time.unscaledTime;
                // _lastPollAt is read by the diff line BEFORE it is advanced, so the window it
                // reports is the one the edit actually fell into.
                _nextPollAt = now + PollInterval;
                Reconcile();
                _lastPollAt = now;
            }

            // NOTE WHAT IS *NOT* HERE. No pose, no layout, no reveal test, no hover scan, no
            // animation clock. All of that runs in CardsDriver's own Update on the fan this class
            // feeds — which is the entire point of ModBuild 192 and the reason the map room cannot
            // drift away from the scenario. TickFan only prints faces and lets retired cards die.
            TickFan();
            TickWrist();
            TickStateLine();
        }
        catch (System.Exception ex)
        {
            _failed = true;
            VRLog.Warn(Scope, "MAP-ROOM HAND STOOD DOWN PERMANENTLY after an exception in its tick "
                + $"({ex.GetType().Name}: {ex.Message}). CONSEQUENCE: for the rest of this session the "
                + "world map shows no loadout fan and no wrist plate; the map room itself, the "
                + "parchment, the icons and every other feature are unaffected, because this class "
                + "owns nothing outside its own objects. Everything it built is being released now. "
                + $"Stack: {ex.StackTrace}");
            try
            {
                Release("the feature stood down after an exception");
            }
            catch (System.Exception inner)
            {
                VRLog.Warn(Scope, $"…and the release after that exception itself failed ({inner.Message}) — "
                    + "objects may be left in the room until the next scene change.");
            }
        }
    }

    /// <summary>
    /// Leave the map room. Idempotent, and the only exit — <see cref="MapRoomDriver.StandDown"/>
    /// comes here, so "leave nothing standing" has one implementation.
    /// </summary>
    internal void StandDown(string reason)
    {
        if (!_engaged)
            return;
        _engaged = false;
        try
        {
            Release(reason);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"Map-room hand stand-down ({reason}) hit {ex.GetType().Name}: {ex.Message} — "
                + "CONSEQUENCE: a slab or the wrist plate may survive until the next scene change. "
                + "No game object was adopted or re-posed by this feature, so nothing of the GAME's "
                + "is left in a modified state either way.");
        }
    }

    /// <summary>Release everything this class built, in the order that cannot orphan anything: the
    /// fan first (it hands the cards back to the driver BEFORE destroying them — see
    /// <see cref="ReleaseFan"/>), then the wrist plate.</summary>
    private void Release(string reason)
    {
        ReleaseFan(reason);
        ReleaseWrist();
        _character = null;
        s_localFanCharacterKey = 0u;
        _loadout.Clear();
        _characterSource = "not resolved";
        _signature = int.MinValue;
        _stateLinePending = false;   // a rebuild that was torn down before it was reported is not news
    }

    // ==========================================================================================
    //  THE DIAL
    // ==========================================================================================

    /// <summary>
    /// The feature switch, read LIVE every frame so the settings menu turns it off without a
    /// restart. Guarded because <see cref="WorldUIConfig"/> entries are null until Bind has run and
    /// the map room can, in principle, engage first; an unbound dial reads as the shipped default,
    /// which is ON (ruling 4).
    /// </summary>
    private static bool MapRoomConfigOn
    {
        get
        {
            var entry = WorldUIConfig.MapRoomHand;
            return entry == null ? Defaults.MapRoomHand : entry.Value;
        }
    }

    // ==========================================================================================
    //  RESOLUTION — WHICH CHARACTER, AND WHICH CARDS
    // ==========================================================================================

    /// <summary>
    /// Re-resolve the selected character and its loadout, and update the fan if either changed.
    /// Called at <see cref="PollInterval"/>, never per frame.
    ///
    /// <para>THE PRECEDENCE, stated here because both edges can land in the same poll (ModBuild 193,
    /// user item 8): A CHARACTER CHANGE ALWAYS WINS. When the resolved <c>CMapCharacter</c> is not
    /// the one the fan was built for, the whole hand really was exchanged and
    /// <see cref="RebuildFan"/> plays the exchange — retire every slab, <c>CardFan.BeginSwapOut</c>,
    /// <c>SetCards(swap: true)</c> — whatever else changed in the same tick. The single-card diff
    /// runs only for a loadout edit on the character already on screen, which is exactly the case
    /// the exchange is the wrong animation for. Nothing is lost either way: the exchange carries
    /// every card of BOTH hands, so a diff would have nothing left to report.</para>
    /// </summary>
    private void Reconcile()
    {
        CMapCharacter? character = ResolveCharacter(out string source);
        int signature = BuildSignature(character);
        if (signature == _signature)
        {
            // A removal deferred because the player was HOLDING that card retries here. The
            // signature will not move again on its own — the loadout already lost the card — so
            // without this the slab would sit in the fan until the next unrelated edit.
            if (_deferredLeave)
                UpdateFanCards();
            return;
        }

        // Read BEFORE _character is overwritten: "is this the same character the fan is showing".
        // Reference identity is the right test — CMapCharacter is the persistent per-character
        // model and a loadout edit mutates that very object in place, which is precisely why the
        // signature (and not the reference) is the change detector one line above.
        bool sameCharacter = character != null && ReferenceEquals(character, _character);

        _signature = signature;
        _character = character;
        s_localFanCharacterKey = Net.NetProtocol.HashMapKey(character?.CharacterName);
        _characterSource = source;
        _loadout.Clear();
        if (character != null)
            ResolveLoadout(character, _loadout);

        if (sameCharacter)
            UpdateFanCards();   // a loadout edit: diff, and animate only the difference
        else
            RebuildFan();       // a character change: the exchange, and it wins
        RebuildWrist();
        // DEFERRED BY ONE FRAME, on purpose. The state line reports whether the shared driver has
        // ADOPTED the published list, and the driver's rebuild runs in its own MonoBehaviour's
        // Update — which may not have happened yet this frame. Emitting here would report "not yet"
        // every single time and make a genuine failure to adopt indistinguishable from the normal
        // one-frame lag.
        _stateLineFrame = Time.frameCount;
        _stateLinePending = true;
    }

    /// <summary>A rebuild is waiting to be reported; see <see cref="Reconcile"/>.</summary>
    private bool _stateLinePending;

    /// <summary>The frame the pending state line was armed on.</summary>
    private int _stateLineFrame;

    /// <summary>Emit the armed state line once the driver has had a frame to consume the publish.</summary>
    private void TickStateLine()
    {
        if (!_stateLinePending || Time.frameCount <= _stateLineFrame)
            return;
        _stateLinePending = false;
        EmitStateLine();
    }

    /// <summary>
    /// THE CHARACTER THE FAN FOLLOWS (ruling 2). The party display's own selection wins; the two
    /// fallbacks below exist so the feature is VISIBLE the moment the player walks into the room,
    /// before they have touched the character screen, and both are named in the state line so a
    /// hardware log never has to guess which one answered.
    ///
    /// <para>Fallback order, and why each is safe:
    /// <list type="number">
    /// <item>THE PARTY DISPLAY (ruling 2, the authority). <c>NewPartyDisplayUI.PartyDisplay
    /// .SelectedUISlot.Data</c>.</item>
    /// <item>THE CHARACTER THIS CLIENT CONTROLS, when there is exactly one. <c>CMapCharacter
    /// .IsUnderMyControl</c> (CMapCharacter.cs, the map-phase twin of the ownership test
    /// <c>RevealGate</c> and <c>CardsGameApi.IsLocalHand</c> use). Exactly-one, because two owned
    /// characters is precisely the case where guessing would show the wrong one.</item>
    /// <item>THE FIRST PARTY MEMBER, offline only. Offline every merc is the player's, so there is
    /// no wrong answer to give — only a less useful one, which the player corrects by clicking a
    /// character. ONLINE this fallback is refused outright: showing an arbitrary teammate's loadout
    /// because nothing was selected would be the mod inventing a selection the player did not
    /// make.</item>
    /// </list></para>
    /// </summary>
    private static CMapCharacter? ResolveCharacter(out string source)
    {
        source = "no character resolved";

        // (1) The party display.
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            NewPartyCharacterUI? slot = display != null ? display.SelectedUISlot : null;
            CMapCharacter? data = slot != null ? slot.Data : null;
            if (data != null)
            {
                source = "the PARTY DISPLAY's own selection (NewPartyDisplayUI.SelectedUISlot.Data) "
                         + "— ruling 2's authority";
                return data;
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, $"Map-room hand: the party display could not be asked ({ex.Message}) — "
                + "falling through to the ownership fallback.");
        }

        // (2)/(3) The party roster.
        List<CMapCharacter> party = PartyMembers();
        if (party.Count == 0)
            return null;

        CMapCharacter? owned = null;
        int ownedCount = 0;
        for (int i = 0; i < party.Count; i++)
        {
            if (party[i].IsUnderMyControl)
            {
                owned ??= party[i];
                ownedCount++;
            }
        }
        if (ownedCount == 1 && owned != null)
        {
            source = "FALLBACK: nothing is selected in the party display, and exactly one party "
                     + "member is under this client's control (CMapCharacter.IsUnderMyControl) — "
                     + "selecting a character in the party screen overrides this within 250 ms (50 ms while the fan is up)";
            return owned;
        }

        if (!FFSNetwork.IsOnline)
        {
            source = "FALLBACK: nothing is selected in the party display and this is an OFFLINE "
                     + "session, so the first party member is shown (offline every merc is the "
                     + "player's own) — selecting a character overrides this within 250 ms (50 ms while the fan is up)";
            return party[0];
        }

        source = ownedCount > 1
            ? "nothing is selected in the party display and this client controls " + ownedCount
              + " characters — REFUSED rather than guessed (select one in the party screen)"
            : "nothing is selected in the party display and this client controls no map character "
              + "— REFUSED rather than guessed";
        return null;
    }

    /// <summary>Scratch for <see cref="PartyMembers"/> — see the note in its body.</summary>
    private static readonly List<CMapCharacter> s_partyScratch = new(4);

    /// <summary>
    /// The party roster, guarded. <c>AdventureState.MapState</c> is null outside a loaded
    /// campaign — the very null <c>RevealGate.InScenario</c> guards against (RevealGate.cs:47) —
    /// and <c>CMapParty.SelectedCharactersArray</c> is a fixed-size array with null holes
    /// (CMapParty.cs:70, and :98 is the game's own null-filtering projection).
    /// </summary>
    private static List<CMapCharacter> PartyMembers()
    {
        // REUSED, not allocated (ModBuild 193). This is the fallback arm of a poll that now runs at
        // 20 Hz while the fan is up, and a fresh four-element list twenty times a second is exactly
        // the kind of per-frame garbage that gets copied into a hotter path later. The list is
        // consumed entirely inside the one caller below before anything else can ask for it.
        List<CMapCharacter> result = s_partyScratch;
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
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, $"Map-room hand: the map party could not be read ({ex.Message}) — "
                + "no fallback character.");
        }
        return result;
    }

    /// <summary>
    /// Resolve <paramref name="character"/>'s loadout IDs to the card models, and put them in the
    /// order the fan must DRAW them: ascending INITIATIVE, left to right.
    ///
    /// <para>This is <c>CMapCharacter.HandAbilityCards</c> (CMapCharacter.cs:141) written without
    /// its <c>.Single(...)</c>: that LINQ throws for a character whose class is not in
    /// <c>CharacterClassManager.Classes</c>, and an exception on this path would cost VR input.
    /// <c>CharacterClassManager.Find</c> (CharacterClassManager.cs:68) is the game's own
    /// case-insensitive lookup over the same list.</para>
    ///
    /// <para>THE ORDER, AND WHY IT IS NO LONGER THE LIST'S OWN (user report 2026-08-22: "Die
    /// Kartenreihenfolge soll von links nach rechts nach der INITIATIVE der Karten sortiert sein —
    /// und ist es am Anfang auch. Aber wenn man Karten HINZUFÜGT, tauchen sie immer am RECHTEN RAND
    /// auf statt sich einzusortieren").</para>
    ///
    /// <para>ROOT CAUSE, read from the game's source and not inferred: the list this reads,
    /// <c>CMapCharacter.HandAbilityCardIDs</c>, is APPEND-ORDERED. Ticking a card in the party
    /// screen runs <c>UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect</c>, whose whole
    /// mutation is <c>HandAbilityCardIDs.Add(cardUI.AbilityCard.ID)</c> (:536) — the new id lands at
    /// the END, always. So did the card, in both of this file's update paths alike (the full
    /// <see cref="RebuildFan"/> and the single-card <see cref="UpdateFanCards"/> diff, which builds
    /// its new set "in loadout order"). The start looked right only because a fresh loadout is
    /// seeded by <c>CMapCharacter.SetCards</c> walking the class' AbilityCardsPool in pool order,
    /// and the FLAT game never renders this list as a row at all — its selection screen is a
    /// scrollable grid of the whole pool with the chosen ones marked, so there was no game order to
    /// inherit. The only place the game DOES lay a hand out left to right is the scenario
    /// (<c>CardsHandUI.SortCards</c> -> <c>cardsUI.Sort()</c> -> <c>AbilityCardUI.CompareTo</c>,
    /// AbilityCardUI.cs:1295), and its final term is exactly
    /// <c>abilityCard.Initiative.CompareTo(other.abilityCard.Initiative)</c>. THAT is the key fed
    /// here, so the map-room fan and the scenario fan order a hand by the same number rather than by
    /// two implementations that merely look alike.</para>
    ///
    /// <para>THE TIE-BREAK IS THE CARD ID, ascending, and it is chosen for a property the selection
    /// order cannot offer: the resulting order is a pure FUNCTION OF THE SET. Two cards of one class
    /// may share an initiative, and with an id tie-break adding, removing or re-adding any OTHER
    /// card can never reorder them — where a "keep selection order" tie-break would have moved a
    /// re-ticked card past its equal-initiative twin, because a deselect+select round trip appends
    /// its id at the end (OnAbilityCardDeselect removes at :558, OnAbilityCardSelect re-appends at
    /// :536). It is also identical on every client, which the selection order is not: a peer's
    /// <c>CardInventoryToken</c> replay (NewPartyDisplayUI.cs:1993-2002) rebuilds the list in the
    /// SENDER's append order. The sort itself is a stable insertion sort — n is at most
    /// <see cref="MaxCards"/> — so even if two cards ever shared BOTH numbers the rest of the hand
    /// would still not move. <c>List.Sort</c> is deliberately not used: it is an unstable introsort.</para>
    ///
    /// <para>A CARD WITH NO INITIATIVE cannot reach this list: every entry is a
    /// <c>CAbilityCard</c> resolved out of the class' AbilityCardsPool and
    /// <c>CBaseAbilityCard.Initiative</c> is a plain non-nullable int. The rest cards, which are the
    /// one hand entry in this game that HAS no initiative, are not ability cards and never enter a
    /// loadout (the scenario fan filters them separately, <c>CardsDriver.FillHandFan</c>'s
    /// <c>widget.IsLongRest</c> skip). The unresolvable case is therefore "the model went missing",
    /// and it is handled where it can actually happen — see <c>CardsDriver.FanInitiative</c>, which
    /// reports such a card as <c>?</c> and excludes it from the sortedness verdict rather than
    /// inventing a number for it.</para>
    /// </summary>
    private static void ResolveLoadout(CMapCharacter character, List<CAbilityCard> into)
    {
        try
        {
            List<int>? ids = character.HandAbilityCardIDs;
            if (ids == null || ids.Count == 0)
                return;
            CCharacterClass? klass = CharacterClassManager.Find(character.CharacterID);
            List<CAbilityCard>? pool = klass != null ? klass.AbilityCardsPool : null;
            if (pool == null)
                return;

            for (int i = 0; i < ids.Count && into.Count < MaxCards; i++)
            {
                int id = ids[i];
                for (int p = 0; p < pool.Count; p++)
                {
                    CAbilityCard candidate = pool[p];
                    if (candidate != null && candidate.ID == id)
                    {
                        into.Add(candidate);
                        break;
                    }
                }
            }

            SortByInitiative(into);
        }
        catch (System.Exception ex)
        {
            into.Clear();
            VRLog.Debug(Scope, $"Map-room hand: loadout resolution failed ({ex.Message}) — empty fan.");
        }
    }

    /// <summary>
    /// Order a resolved loadout the way the fan draws it: ascending initiative, card id as the
    /// tie-break. STABLE insertion sort (see <see cref="ResolveLoadout"/> for why stability is a
    /// requirement here and not a preference); allocation-free, and n never exceeds
    /// <see cref="MaxCards"/>.
    /// </summary>
    private static void SortByInitiative(List<CAbilityCard> cards)
    {
        for (int i = 1; i < cards.Count; i++)
        {
            CAbilityCard card = cards[i];
            int j = i - 1;
            while (j >= 0 && Precedes(card, cards[j]))
            {
                cards[j + 1] = cards[j];
                j--;
            }
            cards[j + 1] = card;
        }
    }

    /// <summary>Does <paramref name="a"/> belong strictly LEFT of <paramref name="b"/> in the fan?
    /// The game's own hand comparison (<c>AbilityCardUI.CompareTo</c>'s final term) plus the id
    /// tie-break. STRICT on purpose: equal keys report false, which is what keeps the insertion sort
    /// above stable.</summary>
    private static bool Precedes(CAbilityCard a, CAbilityCard b)
    {
        if (a.Initiative != b.Initiative)
            return a.Initiative < b.Initiative;
        return a.ID < b.ID;
    }

    /// <summary>
    /// A cheap value that changes exactly when the fan has to be rebuilt: the character IDENTITY
    /// plus the loadout ID sequence. Deliberately NOT the CMapCharacter reference alone — editing a
    /// loadout mutates the SAME object in place (OnAbilityCardSelect/Deselect append to and remove
    /// from the live list), so a reference compare would never notice a card being swapped.
    /// </summary>
    private static int BuildSignature(CMapCharacter? character)
    {
        if (character == null)
            return 0;
        unchecked
        {
            int hash = 17;
            string? name = character.CharacterName;
            string? id = character.CharacterID;
            hash = hash * 31 + (name != null ? name.GetHashCode() : 0);
            hash = hash * 31 + (id != null ? id.GetHashCode() : 0);
            hash = hash * 31 + character.Level;
            try
            {
                List<int>? ids = character.HandAbilityCardIDs;
                if (ids != null)
                {
                    hash = hash * 31 + ids.Count;
                    for (int i = 0; i < ids.Count; i++)
                        hash = hash * 31 + ids[i];
                }
            }
            catch (System.Exception)
            {
                // An unreadable list is itself a stable state — keep the identity hash and let the
                // rebuild show an empty fan rather than flapping between two signatures.
            }
            return hash == int.MinValue ? 0 : hash;
        }
    }

    // ==========================================================================================
    //  THE CAPABILITY PROBE — ONE Warn, then the whole feature stands down
    // ==========================================================================================

    /// <summary>
    /// Ask, ONCE per session, whether the three things this feature cannot work without are
    /// reachable. It deliberately does NOT require the party display to exist yet (it arrives with
    /// the map HUD, several frames after the room) — only the static data the faces are drawn from.
    /// </summary>
    private void Probe()
    {
        if (_probed)
            return;
        _probed = true;

        string? missing = null;
        try
        {
            if (CharacterClassManager.Classes == null || CharacterClassManager.Classes.Count == 0)
                missing = "CharacterClassManager.Classes is empty — the ability-card pool every "
                          + "loadout ID is resolved through has not loaded";
            else if (CharacterClassManager.AllAbilityCards == null)
                missing = "CharacterClassManager.AllAbilityCards is null — the game's own card "
                          + "registry, which ObjectPool.SpawnCard looks a card up in";
        }
        catch (System.Exception ex)
        {
            missing = $"{ex.GetType().Name} while reading CharacterClassManager ({ex.Message})";
        }

        if (missing == null)
            return;

        _failed = true;
        VRLog.Warn(Scope, "MAP-ROOM HAND UNAVAILABLE on this build: " + missing + ". CONSEQUENCE: the "
            + "world map will show NO loadout card fan and NO wrist character plate for the selected "
            + "character; the [WorldUI] MapRoomHand dial will appear to do nothing. Nothing else "
            + "changes — the map room, its parchment, its icons, travel and every other feature are "
            + "untouched, because this feature owns no game object and patches nothing. This is a "
            + "one-shot line: the feature is now stood down for the whole session and will not warn "
            + "again.");
    }

    // ==========================================================================================
    //  THE STATE LINE — how the next hardware round is judged
    // ==========================================================================================

    /// <summary>Scratch for the peer census. Static + reused: the state line is emitted on a
    /// rebuild, which is rare, but a per-emit allocation in a class that also runs per frame is the
    /// kind of thing that gets copied into a per-frame path later.</summary>
    private static readonly List<Vector3> s_peerHeads = new(8);

    /// <summary>
    /// THE map-room hand line. One line, and it is written so the NEXT hardware round can be judged
    /// on the user's five complaints SPECIFICALLY: which classes are driving the fan (named), what
    /// the roll response is, whether highlighting is live, what the wrist plate was built by, and
    /// what the character-switch animation is. Emitted on a REBUILD only — i.e. when the selection
    /// or the loadout changed — so a session produces a handful of these, not one per frame.
    /// </summary>
    private void EmitStateLine()
    {
        int peers;
        try
        {
            s_peerHeads.Clear();
            peers = NetAvatarDriver.CollectPeerHeads(s_peerHeads);
            s_peerHeads.Clear();
        }
        catch (System.Exception)
        {
            peers = -1;
        }

        string who = _character != null
            ? $"'{DisplayName(_character)}' (class id '{_character.CharacterID}', level {_character.Level})"
            : "<none>";
        int want = 0;
        try { want = _character != null ? _character.MaxCards : 0; }
        catch (System.Exception) { want = 0; }

        VRLog.Info(Scope,
            "MAP-ROOM HAND rebuilt.\n"
            + $"  character : {who}\n"
            + $"  source    : {_characterSource}\n"
            + $"  cards     : {_cards.Count} VRCard(s) built for a loadout of {_loadout.Count}"
            + (want > 0 ? $" (CMapCharacter.MaxCards wants {want})" : "")
            + "; read from CMapCharacter.HandAbilityCardIDs and resolved through "
            + "CharacterClassManager.Find(CharacterID).AbilityCardsPool. THIS IS THE SCENARIO "
            + "LOADOUT — the same list UIPartyCharacterAbilityCardsDisplay edits and "
            + "CMapParty.ExportPlayerStates turns into the scenario hand.\n"
            + "  DRIVEN BY : Cards.CardsDriver + Cards.CardFan + Hands.Interact.PalmGate + "
            + "Cards.VRCard — THE SCENARIO'S OWN CLASSES, not copies. This class only SOURCES the "
            + "cards (CardsDriver.OffScenarioFanCards). Read that as the answer to 'verhält es sich "
            + "wie im Szenario': if these names are here, there is exactly one implementation.\n"
            + $"    published: {(CardsDriver.OffScenarioFanActive ? "YES — the driver has ADOPTED this list into its fan" : "not yet — the driver has not run its rebuild since the publish (expected for at most one frame)")}\n"
            + "    (a)+(b) reveal/roll: CardsDriver.UpdatePalmGate on the NON-dominant hand's "
            + "Hands.Interact.PalmGate, thresholds [Cards] RevealEnterDegrees/RevealExitDegrees, "
            + "modes [Cards] RevealMode/RevealIgnoreWhenGrabbing. THE FAN IS DOWN UNTIL THE PALM "
            + "ROLLS UP. The per-frame proof of this is the driver's own change-deduped 'fan state: "
            + "... gateEnabled=, revealed=, open=' line — grep THAT, not this one.\n"
            + "    (c) animation      : CardFan.Open's fan-out reveal ([Cards] FanOpenDuration) and "
            + "Close's reverse collapse (FanCloseDuration), plus the eased palm follow and VRCard's "
            + "per-card home lerp. Highlighting: CardFan.UpdateFingertipHover (fingertip pop + "
            + "split) and CardsDriver.UpdateFanLaser -> UpdateFanHoverSplit (laser hover, haptic "
            + "tick, whole-fan split). LIVE — nothing in the map room suppresses them.\n"
            + "    (e) character swap : CardFan.BeginSwapOut + SetCards(swap: true), raised here "
            + "through CardsDriver.OffScenarioFanSwap. It only plays while the fan is OPEN, exactly "
            + "as in a scenario — switching character with your palm down is silent by design.\n"
            + "    (8) ONE CARD       : a LOADOUT edit on the character already shown does NOT come "
            + "here — it takes MapRoomHand.UpdateFanCards, which diffs by card id and animates only "
            + "the difference (VRCard.PlayAppear to join, CardFan.Remove + VRCard.Vanish to leave, "
            + "both the scenario's own). Grep 'MAP-ROOM HAND DIFF' for those; a CHARACTER change "
            + "always wins over a diff and produces THIS line instead.\n"
            + $"  faces     : {_frontsShown} of {_cards.Count} printed so far "
            + "(Net.RemoteAbilityCardSource pooled borrow — a widget spawned from the game's own "
            + "ObjectPool by card ID, cloned, handed straight back). PRINTING IS DEFERRED to the "
            + "first frame each card is visible, so 0 here is NORMAL until the hand is first raised; "
            + "a count that stays below the card count AFTER a reveal means the pool refused, and "
            + "those cards are card BACKS — the fail-safe, never a blank quad.\n"
            + $"  wrist     : {(HasWrist ? "plate BUILT on the " + _wristSideName + " wrist" : "NOT built")} "
            + $"— {_wristVerdict}. Built by MapRoomHand.3.Wrist against WorldUI.WristHud's own pose "
            + "dials, geometry, fade hysteresis, sorting ladder and ROW FORMAT (Level / HP h/max + "
            + "XP / Gold / conditions). WristHud itself cannot serve the map phase: its want-gate is "
            + "Choreographer.s_Choreographer != null (WristHud.cs:246) and every value it renders "
            + "comes off a CPlayerActor, which does not exist here.\n"
            + $"  peers     : {(peers < 0 ? "uncountable" : peers.ToString())} remote avatar(s) with a "
            + "valid head pose in this room right now. Their fans ARE drawn (they always were — "
            + "Net.RemoteHandFan hangs off RemoteAvatar, which has no phase gate) and since "
            + "ModBuild 226 they show FRONTS: grep 'Remote hand fan faces' for the per-peer verdict "
            + "and the tier that identified the hand. NOTE FOR THE READER: peers are visible on the "
            + "map only INCIDENTALLY and are UNSEATED — every client's menu rig sits at the same "
            + "authored vantage, so they interpenetrate. Deliberate map-room presence is plan "
            + "phase 8 and is UNBUILT. That is a decision, not a bug.\n"
            + "  wire      : NO FIELD ADDED — not for the local fan and not for the peer faces. "
            + "Faces are read locally from CharacterClassManager/ObjectPool and card identity stays "
            + "off the wire. ONE existing byte changes value: the fan is a real Cards.CardFan now, "
            + "so PresenceState.HandCardCount (sampled from CardFan.Current at "
            + "NetAvatarDriver.cs:842) reports the real count while the hand is up — and that same "
            + "byte is the ONLY wire input MapRoomHand.TryResolvePeerLoadout uses to name which "
            + "character a peer's fan is holding.\n"
            + "  DISPROOF  : cards never appear at all -> read the driver's 'fan state' line. "
            + "open=False with gateEnabled=True means the ROLL never crossed RevealEnterDegrees "
            + "(that is (b), and it is a dial, not this file). gateEnabled=False means the "
            + "interactor policy lost PalmGate — the map room must be VRMode.TableIdle; check the "
            + "'Mod room STANDS' line. fanBuffer=0 means the driver never adopted this list: this "
            + "file published and the rebuild did not happen. Cards appear but never animate or "
            + "highlight -> they are NOT in the fan (fanBuffer would be 0 too) and something else "
            + "is drawing them. WRONG character -> read 'source' above: 'PARTY DISPLAY' means the "
            + "game itself reports that selection, anything starting 'FALLBACK' means nothing was "
            + "selected in the party screen.");
    }

    // ==========================================================================================
    //  A PEER'S FAN — WHICH CHARACTER IS IT HOLDING? (user report 2026-08-22, item 4)
    // ==========================================================================================
    //
    // "Handkarten sind nicht sichtbar im Multiplayer im Map-Bereich. … die Handkarten sollen wie in
    //  der Aktionsphase im Szenario voll sichtbar sein, wenn man den Fächer eines anderen Spielers
    //  betrachtet. Aktuell sieht man nur die Rückseiten (wie es zur Auswahlphase der Fall ist)."
    //
    // THE SECRECY HALF OF THAT IS ANSWERED IN Net/RevealGate (ShowMapPhaseHandFronts, which carries
    // the whole derivation). What is answered HERE is the half only the map room can answer: a peer
    // is holding up n card slabs — WHICH character's loadout is that, so the receiver can draw the
    // faces out of its OWN copy of the data?
    //
    // WHY THIS LIVES IN THE MAP ROOM AND NOT IN Net/RemoteHandFan. "Which hand is a map-room fan
    // showing" is knowledge about how a map-room fan is BUILT, and that is this class: the party
    // roster, CMapCharacter.HandAbilityCardIDs, the class pool, the initiative order. The receiver
    // asks the map room the same question the map room asks itself for the LOCAL hand, and gets an
    // answer produced by the same three methods (PartyMembers / ResolveLoadout / SortByInitiative).
    // Net/RemoteHandFan holds no map-phase knowledge at all as a result — and this is deliberately
    // NOT a GetComponentInParent-style "is this fan related to the map room" test, which is the
    // question this repo has twice answered with the wrong predicate: nothing here inspects the
    // peer's objects, it asks the room.
    //
    // ─── THE IDENTIFICATION, AND EXACTLY HOW CERTAIN EACH TIER IS ────────────────────────────────
    // NOTHING NEW CROSSES THE WIRE FOR THIS and no card identity does. The only input off the wire
    // is the card COUNT that has been in every extras packet since the fan existed
    // (PresenceState.HandCardCount, sampled from CardFan.Current at NetAvatarDriver.cs:842 — the
    // very byte that started reporting the map fan's real size in ModBuild 192). Everything else is
    // this client's own replicated map state.
    //
    //   TIER 1 — THE SIZE NAMES THE HAND. A map-room fan is ALWAYS built from some party member's
    //   loadout (ResolveCharacter only ever returns a CMapCharacter out of MapParty). So if exactly
    //   ONE party member's HandAbilityCardIDs has the size the peer is holding, that member IS the
    //   one — not a guess, a deduction from the premise. Loadouts are replicated (the party screen's
    //   edits ride the game's own CardInventoryToken, NewPartyDisplayUI.cs:1993-2002), so every
    //   client counts the same numbers.
    //
    //   TIER 2 — THE GAME'S OWN OWNERSHIP, used only to break a size tie. When several members share
    //   the size, we narrow to the ones that peer CONTROLS, using the game's own authority for it:
    //   ControllableRegistry, whose per-character controllable is created for every map character in
    //   the map phase (BenchedCharacter.cs:18-25) and whose Controller is the NetworkPlayer the host
    //   assigned. This tier carries ONE assumption, stated rather than hidden: that a player's own
    //   fan shows a character they control. That is the normal case and not a certainty — the party
    //   display lets anybody select anybody (NewPartyDisplayUI.OnCharacterSelect has no ownership
    //   test at all, and this session's own log shows player 1 displaying player 2's Summoner). A
    //   peer inspecting a FOREIGN hand whose size collides with one of their own would therefore be
    //   drawn the wrong loadout — the ModBuild 84 class of defect.
    //
    //   NEITHER TIER GUESSES PAST THAT. An unresolved fan stays CARD BACKS and says why in one
    //   throttled line, because a plausible-but-wrong hand is worse than an honest absence.
    //
    // WHAT WOULD MAKE IT EXACT, for whoever owns the wire next: four bytes in extension record 20
    // (3D MAP ROOM) carrying FNV-1a(CMapCharacter.CharacterName) of the character the sender's fan
    // is built for — a KEY the game itself publishes, exactly like that record's existing pickKey
    // (FNV-1a(CLocationState.ID)). The receiver would look the character up by that key and the two
    // tiers below would collapse into one lookup. Card identity would STILL never ride the wire:
    // the faces are drawn from this client's own CharacterClassManager pool, as they are today.

    /// <summary>
    /// <c>FNV-1a(CMapCharacter.CharacterName)</c> of the character THIS client's map-room fan is
    /// showing, or 0 while no fan stands.
    ///
    /// <para>Published in extension record 20 so a peer can name the fan outright instead of
    /// deducing its owner — see <see cref="TryResolvePeerLoadout"/>'s tier 0 and the block above it.
    /// STATIC even though the character is per-instance, because there is exactly one hand
    /// (<c>MapRoomDriver.Hand</c>, a single static readonly) and a wire sampler has no instance to
    /// ask; it is written at the one place <c>_character</c> is assigned and at the one place the
    /// hand is released, so it cannot drift from the fan it describes. A NAME AND NOT A CARD: what
    /// this key identifies is which party member to look up in the receiver's OWN party, and the
    /// cards themselves are still drawn from the receiver's own art.</para>
    /// </summary>
    internal static uint LocalFanCharacterKey => s_localFanCharacterKey;

    /// <inheritdoc cref="LocalFanCharacterKey"/>
    private static uint s_localFanCharacterKey;

    /// <summary>
    /// Resolve the loadout a REMOTE player's map-room fan is holding, into <paramref name="into"/>
    /// (cleared first), in the same initiative order the local fan draws. Returns true iff the
    /// character could be identified with the certainty described in the block above; on false the
    /// caller must keep showing card BACKS.
    ///
    /// <para><paramref name="characterKey"/> is that peer's <see cref="LocalFanCharacterKey"/> off
    /// the wire, or 0 from a peer whose build does not send it — in which case the deduction tiers
    /// below answer exactly as they did before the field existed.</para>
    ///
    /// <para><paramref name="cardCount"/> is the peer's broadcast <c>HandCardCount</c>.
    /// <paramref name="verdict"/> is a human sentence naming which tier answered (or why none did)
    /// — it is written verbatim into the receiver's diagnostic so a hardware log can tell "the peer
    /// is holding a hand we cannot name" from "the map has no party" without a screenshot.</para>
    ///
    /// <para>ALLOCATION-FREE on the resolved path beyond what <see cref="ResolveLoadout"/> already
    /// costs, and it is not on a per-frame path: the caller re-asks only when the peer's card count
    /// changes or on its own slow cadence.</para>
    /// </summary>
    internal static bool TryResolvePeerLoadout(int playerId, int cardCount, uint characterKey,
                                               List<CAbilityCard> into, out string verdict)
    {
        into.Clear();
        verdict = "not resolved";
        if (cardCount <= 0)
        {
            verdict = "the peer is holding no cards";
            return false;
        }

        // PartyMembers hands back a SHARED reused list (s_partyScratch) that the local reconcile
        // path also consumes — copy out of it before anything else can ask for it again.
        s_peerScratch.Clear();
        List<CMapCharacter> party = PartyMembers();
        for (int i = 0; i < party.Count; i++)
            s_peerScratch.Add(party[i]);
        if (s_peerScratch.Count == 0)
        {
            verdict = "no map party is loaded on this client";
            return false;
        }

        // TIER 0 — THE PEER SAID SO (ModBuild 226). Not a deduction at all: the sender publishes
        // FNV-1a of the character its own fan is built for, and the only thing that happens here is
        // a lookup in this client's own party. It is tried FIRST and, when it answers, the two
        // deduction tiers below never run — they exist now only for a peer on an older build, whose
        // record carries no such field and leaves this 0.
        //
        // A KEY THAT DOES NOT RESOLVE FALLS THROUGH rather than refusing. The character may simply
        // not have replicated to this client yet, and a hash collision is possible in principle;
        // in both cases the size deduction below is still available and is still better than backs.
        // What it must never do is name the WRONG character, and it cannot: the match is on the
        // hash of the name, so a miss is a miss.
        if (characterKey != 0u)
        {
            for (int i = 0; i < s_peerScratch.Count; i++)
            {
                CMapCharacter c = s_peerScratch[i];
                if (Net.NetProtocol.HashMapKey(c.CharacterName) != characterKey)
                    continue;
                ResolveLoadout(c, into);
                verdict = $"NAMED: player {playerId} says their map fan is '{DisplayName(c)}' "
                          + $"(character key 0x{characterKey:X8} in record 20) — no deduction was "
                          + "needed";
                return into.Count > 0;
            }
        }

        // TIER 1 — the size names the hand.
        CMapCharacter? unique = null;
        int matches = 0;
        for (int i = 0; i < s_peerScratch.Count; i++)
        {
            if (LoadoutSize(s_peerScratch[i]) != cardCount)
                continue;
            unique ??= s_peerScratch[i];
            matches++;
        }
        if (matches == 0)
        {
            verdict = $"no party member has a loadout of {cardCount} card(s) — the peer's fan is "
                      + "mid-change, or their party state has not replicated here yet";
            return false;
        }
        if (matches == 1 && unique != null)
        {
            ResolveLoadout(unique, into);
            verdict = $"EXACT: '{DisplayName(unique)}' is the ONLY party member with a "
                      + $"{cardCount}-card loadout, and a map-room fan is always some party "
                      + "member's loadout";
            return into.Count > 0;
        }

        // TIER 2 — the game's own ownership, breaking the size tie.
        CMapCharacter? owned = null;
        int ownedMatches = 0;
        for (int i = 0; i < s_peerScratch.Count; i++)
        {
            CMapCharacter c = s_peerScratch[i];
            if (LoadoutSize(c) != cardCount || ControllerPlayerId(c) != playerId)
                continue;
            owned ??= c;
            ownedMatches++;
        }
        if (ownedMatches == 1 && owned != null)
        {
            ResolveLoadout(owned, into);
            verdict = $"OWNERSHIP: {matches} party members hold {cardCount} cards, and exactly one "
                      + $"of them — '{DisplayName(owned)}' — is controlled by player {playerId} "
                      + "(the game's own ControllableRegistry). ASSUMES the peer is displaying a "
                      + "character they control; the party screen does not require that";
            return into.Count > 0;
        }

        into.Clear();
        verdict = ownedMatches > 1
            ? $"AMBIGUOUS: player {playerId} controls {ownedMatches} characters with a "
              + $"{cardCount}-card loadout — showing BACKS rather than guessing which"
            : $"AMBIGUOUS: {matches} party members hold {cardCount} cards and none of them resolved "
              + $"to player {playerId} through ControllableRegistry — showing BACKS rather than "
              + "guessing which";
        return false;
    }

    /// <summary>Scratch for <see cref="TryResolvePeerLoadout"/>. Its own list, never
    /// <see cref="s_partyScratch"/>: this runs on the RECEIVE path while the local reconcile owns
    /// that one, and two consumers of one reused buffer is how a scratch list becomes a bug.</summary>
    private static readonly List<CMapCharacter> s_peerScratch = new(4);

    /// <summary>How many cards are in <paramref name="character"/>'s scenario loadout (-1 =
    /// unreadable). The raw ID count, deliberately NOT the resolved model count: the peer's
    /// broadcast number is the size of the fan THEY built, this is the size of the list it was
    /// built from, and running the class-pool lookup for every party member on every resolve to
    /// compare two numbers would be paying for a match we have not made yet.</summary>
    private static int LoadoutSize(CMapCharacter? character)
    {
        if (character == null)
            return -1;
        try
        {
            List<int>? ids = character.HandAbilityCardIDs;
            return ids != null ? ids.Count : -1;
        }
        catch (System.Exception)
        {
            return -1;
        }
    }

    // ---- WHO CONTROLS A MAP CHARACTER — the game's own registry, by reflection ----------------
    //
    // REFLECTION-ONLY, for the reason Net/NetPlayerActors states at length and this file must obey
    // too: FFSNet.NetworkPlayer is EntityBehaviour<IPlayerState>, i.e. Bolt-derived, and this build
    // has (and needs) no bolt.dll. So the registry, the controllable and the player are all handled
    // as `object` and every member is reached through AccessTools. BenchedCharacter — the map
    // phase's IControllable — is reached the same way rather than by type, because its interface
    // surface (GetNetworkEntityPrefabID → Photon.Bolt.PrefabId) is exactly the reference we cannot
    // take. Anything missing degrades to "unknown controller" (0), which costs tier 2 and nothing
    // else.
    //
    // THE SHAPE, from the game's source:
    //   ControllableRegistry.AllControllables : List<NetworkControllable>   (ControllableRegistry.cs:10)
    //   NetworkControllable.ControllableObject : IControllable              (used at :122)
    //   NetworkControllable.Controller         : NetworkPlayer              (used at :112)
    //   BenchedCharacter.CharacterData         : CMapCharacter              (BenchedCharacter.cs:9)
    // and the map phase really does create one controllable per map character — BenchedCharacter's
    // constructor does it for every roster member (:18-25). This session's Player.log shows six of
    // them created on entering the map and reassigned live ("Controllable (ID: -273653989) ASSIGNED
    // to ARMA (ID: 2)"), which is the evidence that this registry is populated and authoritative
    // outside a scenario.

    private static bool s_ctrlInit;
    private static PropertyInfo? s_allControllables;   // static List<NetworkControllable>
    private static PropertyInfo? s_controllableObject; // IControllable
    private static PropertyInfo? s_controller;         // NetworkPlayer
    private static PropertyInfo? s_ctrlPlayerId;       // int
    private static PropertyInfo? s_characterData;      // CMapCharacter
    private static System.Type? s_benchedType;         // BenchedCharacter (the map phase's IControllable)

    /// <summary>The FFSNet player id controlling <paramref name="character"/> in the map phase, or
    /// 0 when it cannot be answered (offline, netcode absent, a renamed member, no controllable for
    /// this character yet). Never throws.</summary>
    private static int ControllerPlayerId(CMapCharacter? character)
    {
        if (character == null)
            return 0;
        EnsureControllableReflection();
        if (s_allControllables == null || s_controllableObject == null || s_controller == null
            || s_ctrlPlayerId == null || s_characterData == null || s_benchedType == null)
            return 0;
        try
        {
            if (s_allControllables.GetValue(null) is not System.Collections.IEnumerable all)
                return 0;
            foreach (object? controllable in all)
            {
                if (controllable == null)
                    continue;
                object? obj = s_controllableObject.GetValue(controllable);
                if (obj == null || !s_benchedType.IsInstanceOfType(obj))
                    continue;   // a scenario CharacterManager, never in the map phase — skip it
                if (s_characterData.GetValue(obj) is not CMapCharacter data
                    || !ReferenceEquals(data, character))
                    continue;
                object? player = s_controller.GetValue(controllable);
                if (player == null)
                    return 0;
                return s_ctrlPlayerId.GetValue(player) is int id ? id : 0;
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Debug(Scope, "Map-room hand: the controllable registry could not be read "
                + $"({ex.Message}) — a peer's fan falls back to card BACKS when its size is "
                + "ambiguous.");
        }
        return 0;
    }

    /// <summary>Resolve the four reflection handles once per session. Silent on failure: the ONE
    /// consequence is that a size-ambiguous peer fan keeps its card backs, and the resolver's own
    /// verdict string already says so.</summary>
    private static void EnsureControllableReflection()
    {
        if (s_ctrlInit)
            return;
        s_ctrlInit = true;
        try
        {
            System.Type? registry = AccessTools.TypeByName("FFSNet.ControllableRegistry");
            System.Type? controllable = AccessTools.TypeByName("FFSNet.NetworkControllable");
            System.Type? player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            s_benchedType = AccessTools.TypeByName("BenchedCharacter");

            s_allControllables = registry?.GetProperty("AllControllables",
                BindingFlags.Public | BindingFlags.Static);
            s_controllableObject = controllable?.GetProperty("ControllableObject");
            s_controller = controllable?.GetProperty("Controller");
            s_ctrlPlayerId = player?.GetProperty("PlayerID");
            s_characterData = s_benchedType?.GetProperty("CharacterData");
        }
        catch (System.Exception ex)
        {
            s_allControllables = null;
            s_controllableObject = null;
            s_controller = null;
            s_ctrlPlayerId = null;
            s_characterData = null;
            s_benchedType = null;
            VRLog.Debug(Scope, $"Map-room hand: FFSNet controllable reflection failed ({ex.Message}) "
                + "— peer fans stay on the size-only identification.");
        }
    }

    /// <summary>The name to show: the player's own renaming wins, as it does in the game's own
    /// party window (<c>CMapCharacter.DisplayCharacterName</c>, CMapCharacter.cs:49).</summary>
    private static string DisplayName(CMapCharacter character)
    {
        try
        {
            string? display = character.DisplayCharacterName;
            if (!string.IsNullOrEmpty(display))
                return display!;
            return character.CharacterName ?? "?";
        }
        catch (System.Exception)
        {
            return "?";
        }
    }
}

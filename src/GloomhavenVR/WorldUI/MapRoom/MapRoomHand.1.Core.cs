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
// leak a subscription across a scene teardown. 4 Hz is 250 ms — under the threshold at which a
// player pressing a card in the party window would call the fan "stale".
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
//     (sampled at NetAvatarDriver.cs:842) reports the real count instead of 0. Peers draw a fan of
//     CARD BACKS on our avatar — RemoteHandFan gates every FRONT on `RevealGate.InScenario`
//     (RemoteHandFan.cs:888), which is FALSE on the map, so no identity can be shown even in
//     principle. Zero bytes were added; a count that used to be a lie while we held cards is now
//     the truth. If that is ever unwanted, the whole of the suppression is one line at
//     NetAvatarDriver.cs:842 — see this lane's change report.
//   * RevealGate.ShowRoundCardFronts is WIDE OPEN in the map phase and its own doc comment says so
//     ("offline / single player / MAP / our own actor / post-reveal action phase"): its conjunction
//     includes RevealGate.InScenario, which is false here (RevealGate.cs:31-64, re-read 2026-08-21).
//     There is nothing to unlock and nothing to relax — the user's "es gibt keine Geheimnisse in
//     dieser Phase" is already the game's own rule.
//
// THE PEERS' HALF IS NOT BUILT, AND IT IS NOT A SHORTCUT — IT HAS NOWHERE TO HANG. Remote avatars
// do exist in the map room, but only INCIDENTALLY: RemoteAvatar is DontDestroyOnLoad with no scene
// or phase gate (RemoteAvatar.cs:690), and MapRoomDriver's own class comment records the
// consequence — "Peers are already visible on the map today, unconditionally, and every client's
// menu rig sits at the same authored vantage — so avatars pile up. That is phase 8's problem,
// deliberately not fixed here." Deliberate multiplayer presence in this room is plan phase 8 of
// .planning/worldmap-3d.md and is UNBUILT. Hanging a peer's loadout fan off an unseated avatar that
// is interpenetrating every other avatar would produce a pile of fans at one world point, which is
// worse than the honest absence. The precise wire record the peer half would need is stated in this
// lane's change report; it does not belong in this file and is not implemented here.
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
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
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

    /// <summary>Seconds between selection/loadout signature polls. See the header for why this is a
    /// poll and not <c>NewPartyDisplayUI.NewCharacterSelected</c>.</summary>
    private const float PollInterval = 0.25f;

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
                _nextPollAt = Time.unscaledTime + PollInterval;
                Reconcile();
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
    /// Re-resolve the selected character and its loadout, and rebuild if either changed. Called at
    /// <see cref="PollInterval"/>, never per frame.
    /// </summary>
    private void Reconcile()
    {
        CMapCharacter? character = ResolveCharacter(out string source);
        int signature = BuildSignature(character);
        if (signature == _signature)
            return;

        _signature = signature;
        _character = character;
        _characterSource = source;
        _loadout.Clear();
        if (character != null)
            ResolveLoadout(character, _loadout);

        RebuildFan();
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
                     + "selecting a character in the party screen overrides this within 250 ms";
            return owned;
        }

        if (!FFSNetwork.IsOnline)
        {
            source = "FALLBACK: nothing is selected in the party display and this is an OFFLINE "
                     + "session, so the first party member is shown (offline every merc is the "
                     + "player's own) — selecting a character overrides this within 250 ms";
            return party[0];
        }

        source = ownedCount > 1
            ? "nothing is selected in the party display and this client controls " + ownedCount
              + " characters — REFUSED rather than guessed (select one in the party screen)"
            : "nothing is selected in the party display and this client controls no map character "
              + "— REFUSED rather than guessed";
        return null;
    }

    /// <summary>
    /// The party roster, guarded. <c>AdventureState.MapState</c> is null outside a loaded
    /// campaign — the very null <c>RevealGate.InScenario</c> guards against (RevealGate.cs:47) —
    /// and <c>CMapParty.SelectedCharactersArray</c> is a fixed-size array with null holes
    /// (CMapParty.cs:70, and :98 is the game's own null-filtering projection).
    /// </summary>
    private static List<CMapCharacter> PartyMembers()
    {
        var result = new List<CMapCharacter>(4);
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
    /// Resolve <paramref name="character"/>'s loadout IDs to the card models, in the order the
    /// player's own selection put them in (<c>HandAbilityCardIDs</c>'s order, which is append order
    /// from UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect) rather than pool order.
    ///
    /// <para>This is <c>CMapCharacter.HandAbilityCards</c> (CMapCharacter.cs:141) written without
    /// its <c>.Single(...)</c>: that LINQ throws for a character whose class is not in
    /// <c>CharacterClassManager.Classes</c>, and an exception on this path would cost VR input.
    /// <c>CharacterClassManager.Find</c> (CharacterClassManager.cs:68) is the game's own
    /// case-insensitive lookup over the same list.</para>
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
        }
        catch (System.Exception ex)
        {
            into.Clear();
            VRLog.Debug(Scope, $"Map-room hand: loadout resolution failed ({ex.Message}) — empty fan.");
        }
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
            + "valid head pose in this room right now. NOTE FOR THE READER: peers are visible on the "
            + "map only INCIDENTALLY (RemoteAvatar is DontDestroyOnLoad with no phase gate) and are "
            + "UNSEATED — every client's menu rig sits at the same authored vantage, so they "
            + "interpenetrate. Deliberate map-room presence is plan phase 8 and is UNBUILT, so NO "
            + "peer loadout fan is drawn by this build. That is a decision, not a bug.\n"
            + "  wire      : NO FIELD ADDED. Faces are read locally from CharacterClassManager/"
            + "ObjectPool and card identity stays off the wire. ONE existing byte changes value: the "
            + "fan is a real Cards.CardFan now, so PresenceState.HandCardCount (sampled from "
            + "CardFan.Current at NetAvatarDriver.cs:842) reports the real count while the hand is "
            + "up; peers draw BACKS only, because RemoteHandFan gates fronts on RevealGate.InScenario "
            + "which is false here.\n"
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

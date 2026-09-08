using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE 3D MAP ROOM ON THE WIRE — record <see cref="NetProtocol.ExtIdMapRoom"/> (20): who else is
/// standing at this table, which map surface they are showing, which icon they are pointing at and
/// which one is selected.
///
/// <para>USER REQUEST (2026-08-22, verbatim): "Multiplayer für die 3D-Map: a) Welche Map angezeigt
/// wird (Gloomhaven oder World-Map) soll synchronisiert werden. b) Welche Quest gerade angeklickt
/// ist soll synchronisiert werden. c) Die mouseover Infotafeln sollen synchronisiert werden."</para>
///
/// <para><b>NO NEW AUTHORITY IS CREATED — every drive is one of the game's own UI seams.</b> There
/// are exactly two: one press of the game's own guildmaster world/city button, through the table
/// rail's single dispatch (<c>ExecuteEvents.pointerClickHandler</c> on the real <c>Toggle</c>), and
/// one adopted SELECTION, through <c>MapLocationInteractor.AdoptSelection</c> — which is the same
/// <c>pointerClickHandler</c> on the real <c>MapLocation</c> the local player's own trigger reaches,
/// or the game's own <c>MapLocation.Deselect()</c>. The game's whole guard chain still decides in
/// both cases, including its refusal when the city is not unlocked and its <c>IsSelectable()</c>.
/// Nothing here writes a game field, and nothing here sends a game action.</para>
///
/// <para><b>THE SELECTION WAS NOT A DRIVE UNTIL ModBuild 226 — a USER RULING CHANGED IT</b> (report
/// 13, verbatim): <i>"Welches Icon ausgewählt ist wird nicht richtig synchronisiert. Es soll nur
/// eine einzige Auswahl geben die global alle sehen."</i> Record 20 used to carry the pick as a
/// label and nothing more, on the reasoning that the committed quest selection is already on the
/// game's own wire. That reasoning measured the wrong fact, and the decompiled sources say so:
/// <c>ConfirmSelectedLocation</c> is host-only and fires from the READY-UP, its receiver
/// <c>ProxyHostSelectedLocation</c> raises a CONFIRM PROMPT rather than a selection, and a client's
/// own <c>Select()</c> never leaves the machine. "Which icon is selected right now" is carried
/// nowhere by the game, so it is carried here — as an EDGE (<c>selectStamp</c> + <c>selectKey</c>),
/// with the same symmetric authority and the same adopted-change suppression the surface has, and
/// with <c>selectKey == 0</c> on an edge meaning a DESELECTION. See
/// <see cref="NetProtocol.ExtIdMapRoom"/>.</para>
///
/// <para><b>SURFACE AUTHORITY: ANYBODY MAY SWITCH, EVERYONE FOLLOWS — a USER RULING</b>, taken over
/// the host-authoritative alternative. The adoption is therefore <b>edge-triggered</b>: a peer's
/// surface is applied ONCE, on the packet whose <c>surfaceStamp</c> differs from the last one seen
/// from that peer, and never again. Nothing is re-asserted per frame, which is what keeps this out
/// of a write war with the three already-synced game actions that move the surface on their own
/// (<c>MoveToNewNode</c>'s receiver, the <c>SelectQuest</c> receive path's <c>PreviewQuest</c>, and
/// <c>MapChoreographer.MultiplayerStartup</c>). <b>The swap hazard is accepted, not a defect:</b>
/// if two players switch in the same instant they trade views once and the next press settles it.
/// Do not "fix" it by re-applying continuously — see <see cref="NetProtocol.ExtIdMapRoom"/>.</para>
///
/// <para><b>HOVER PLACARDS: A PEER'S PLACARD IS THE GAME'S OWN CARD — a USER RULING</b> (report 6,
/// verbatim): <i>"Mouseover der Symbole in der Map soll nicht das Steam-Symbol sein, sondern das
/// richtige Mouseover das der Spieler auch sieht, zu dem jeweiligen Spieler hingedreht, direkt über
/// dem jeweiligen Symbol. Aktuell sieht man das Steam-Logo zusammen mit einem kleinen Text. Der soll
/// auch weg - es soll 1:1 so aussehen wie es für den Spieler auch aussieht."</i>
/// <b>THIS SUPERSEDES HIS EARLIER CHOICE, "jede fremde Tafel zusätzlich, mit Namen" — the name row
/// and the Steam picture are gone and must not be restored from that older note.</b> Ownership is
/// now carried by the placard's FACING and by nothing else, which is why the facing is a
/// requirement and not a nicety.</para>
///
/// <para><b>WHAT GATED THE REAL CARD OUT, AND WHAT IS FED INSTEAD.</b> The gate is ownership, not
/// drawing: <c>UIQuestPopupManager</c> holds exactly ONE <c>questPreviewPopup</c> and previews only
/// while <c>selectedQuest == null</c>, so the manager cannot show four cards and the local player's
/// hover would lose its own card to a peer's. ModBuild 222 concluded from that that a peer's
/// placard had to be drawn by this mod; the conclusion was wrong. What cannot be shared is the
/// manager's SINGLE INSTANCE — so each peer gets an INSTANCE OF ITS OWN:
/// <c>Object.Instantiate</c> of the game's own <c>UIQuestPreviewPopup</c> GameObject, fed the same
/// <c>IQuest</c> the game's own <c>MapLocation.PreviewQuest</c> would feed it for that node
/// (<c>new Quest(loc.LocationQuest)</c>, or <c>new HeadqueartQuest()</c> for the capital), through
/// the popup's own public <c>SetQuest</c>. It is the real card because it IS the real card's
/// prefab, filled by the real card's own code. The manager is never touched, so the local hover
/// keeps its card and its rules exactly as before.</para>
///
/// <para>The clone is built the way <c>WorldUI.Surfaces.StatPanelSurface</c> builds its second
/// stat-panel copy, which is the proven pattern for this in this project: instantiate under an
/// INACTIVE holder so Unity never runs a single <c>Awake</c>, fill it, DestroyImmediate every
/// lifecycle component while it has still never been activated (the <c>UIWindow</c> — so it can
/// never enter <c>UIWindow.GetWindow</c>, never raise a window event and never be picked up by
/// <c>ModalFallback</c>'s catch-all — the <c>UIQuestPreviewPopup</c> itself and the game's
/// <c>UIFollowMapLocation</c>), force it opaque and inert, and only then hand the bare imagery to
/// <c>CanvasConversion.Convert</c>. Its pose is <c>MapRoom.HoverCardPose.Place</c> — the same code
/// that seats the local card, so "directly above the icon" is the identical geometry and not a
/// second implementation of it.</para>
///
/// <para><b>THEY STILL DO NOT DRIVE THE LOCAL HOVER, and that separation is unchanged:</b> calling
/// <c>MapLocation.OnPointerEnter</c> for a peer would fight this client's own pointer, would steal
/// the single preview card from the local player, and would fire the game's mouse-enter sound once
/// per peer per icon — a clicking storm with three people sweeping beams. Nothing here touches a
/// <c>MapLocation</c> at all; the clone is fed from the location, never through it.</para>
///
/// <para><b>AND NO PLACARD IS DRAWN WHILE A QUEST IS SELECTED</b>
/// (<see cref="WorldUI.MapRoom.MapHoverVerdict.AQuestIsSelected"/>). That is the game's own rule —
/// <c>PreviewQuest</c> previews only while <c>selectedQuest == null</c> — and with the selection
/// now a single global fact it holds identically on every machine: in that state the peer whose
/// placard it would be sees no card either, so drawing one would be the opposite of "1:1 so wie es
/// für den Spieler auch aussieht".</para>
///
/// <para><b>THE CLUTTER CASE IS BOUNDED BY LAYOUT, NOT BY DROPPING PLACARDS.</b> A placard sits
/// directly above its own icon — that is the ruling — so the lane index counts only the readers of
/// THE SAME icon, in ascending player-id order with the local player's own hover taking lane 0.
/// Two peers pointing at two different icons therefore both sit at the base lift, exactly where
/// they belong; two readers of one icon are separated first by their facings (each card is turned
/// to a different person) and then, for the shoulder-to-shoulder case that defeats that, by
/// <see cref="SameIconLaneLiftMeters"/>. The order is the player id, so every machine draws the
/// same picture and nothing is ever hidden.</para>
///
/// <para>BOTH GATES ARE <c>MapRoomDriver.Active</c>. A client with the 3D map off writes no record
/// (its packet is byte-identical to ModBuild 221's) and applies none (no placard, no surface edge,
/// nothing drawn). Local settings take precedence; opting into the room IS the consent.</para>
/// </summary>
internal static class RemoteMapRoom
{
    private const string Scope = "Net";

    /// <summary>How long a peer's map-room record stays believed after its last packet. The same
    /// fifteen-missed-packets window record 19 uses at
    /// <see cref="NetProtocol.ExtrasSendRateHz"/> = 5 Hz.</summary>
    private const float PeerStaleSeconds = 3f;

    /// <summary>Seconds between two attempts to press the same adopted surface. The game
    /// legitimately REFUSES the city cap when the city is not unlocked
    /// (<c>UIGuildmasterHUD.IsAvailable</c>, <c>MapChoreographer.OpenCityMap</c> returns early
    /// outside a campaign), and a per-frame press would be a press storm against a rule that is
    /// right.</summary>
    private const float SurfaceRetrySeconds = 1f;

    /// <summary>How many times one adopted edge is retried before it is abandoned with a stated
    /// reason. Five seconds of trying is far longer than any transition, and giving up is correct:
    /// a refusal that persists is the game saying no.</summary>
    private const int SurfaceMaxAttempts = 5;

    /// <summary>What a peer last said about their map room. Value type, one per sender, replaced
    /// whole on every packet — the decision family's "full state, newest wins" contract.</summary>
    private readonly struct PeerRoom
    {
        public PeerRoom(byte flags, byte surfaceStamp, uint pickKey, byte selectStamp,
                        uint selectKey, uint fanCharacterKey, float at)
        {
            Flags = flags;
            SurfaceStamp = surfaceStamp;
            PickKey = pickKey;
            SelectStamp = selectStamp;
            SelectKey = selectKey;
            FanCharacterKey = fanCharacterKey;
            At = at;
        }

        public readonly byte Flags;
        public readonly byte SurfaceStamp;
        public readonly uint PickKey;

        /// <summary>The peer's wrapping SELECTION stamp. Zero and unchanging from a peer whose
        /// record is the old six-byte form, which is exactly "does not participate".</summary>
        public readonly byte SelectStamp;

        /// <summary>The key of the peer's selection, or 0 for "nothing is selected". Only ever
        /// acted on when <see cref="SelectStamp"/> changed.</summary>
        public readonly uint SelectKey;

        /// <summary><c>FNV-1a(CMapCharacter.CharacterName)</c> of the party member whose loadout
        /// that peer's 3D-map card fan is showing, 0 for "no fan open" — which is also what a peer
        /// on ModBuild 225 leaves here, since their record has no such field.</summary>
        public readonly uint FanCharacterKey;

        public readonly float At;

        public bool InRoom => (Flags & NetProtocol.MapRoomInRoomBit) != 0;
        public bool IsHost => (Flags & NetProtocol.MapRoomHostBit) != 0;
        public bool SurfaceKnown => (Flags & NetProtocol.MapRoomSurfaceKnownBit) != 0;
        public bool IsCity => (Flags & NetProtocol.MapRoomSurfaceCityBit) != 0;
        public bool PickValid => (Flags & NetProtocol.MapRoomPickValidBit) != 0;
        public bool PickStaged => (Flags & NetProtocol.MapRoomPickStagedBit) != 0;
    }

    private static readonly Dictionary<int, PeerRoom> Peers = new();

    /// <summary>The surface stamp we have already CONSUMED from each peer. An edge is the
    /// difference between this and the stamp on the wire; once consumed it is never re-applied,
    /// which is the whole of "adopt once, never continuously".</summary>
    private static readonly Dictionary<int, byte> ConsumedStamp = new();

    /// <summary>Peers we have seen at all, so a first packet is not mistaken for an edge. A peer
    /// arriving with a stamp of 3 has not just changed anything — we simply have no history.</summary>
    private static readonly Dictionary<int, byte> SeenStamp = new();

    /// <inheritdoc cref="ConsumedStamp"/>
    private static readonly Dictionary<int, byte> ConsumedSelectStamp = new();

    /// <inheritdoc cref="SeenStamp"/>
    private static readonly Dictionary<int, byte> SeenSelectStamp = new();

    // ---- local state ------------------------------------------------------------------------

    /// <summary>The surface we last PUBLISHED (never <c>Unknown</c> — an unknown surface publishes
    /// no stamp change at all, because "I do not know" is not a change).</summary>
    private static MapIconLayer.MapSurface _publishedSurface = MapIconLayer.MapSurface.Unknown;

    /// <summary>Our own wrapping surface stamp: bumped once per COMPLETED local world↔city
    /// change THAT A HUMAN HERE MADE. See <see cref="_adoptedSurface"/>.</summary>
    private static byte _localSurfaceStamp;

    /// <summary>
    /// The surface this client pressed ON A PEER'S BEHALF, so the resulting change publishes NO
    /// stamp edge of its own. <c>Unknown</c> = nothing adopted.
    ///
    /// <para><b>THIS IS WHAT STOPS THE SWAP HAZARD FROM BECOMING AN OSCILLATION, and it is the one
    /// subtlety in the whole record.</b> The stamp means "a human at THIS table switched", not "the
    /// surface here changed". Without this field an adopted press would itself look like a switch,
    /// publish an edge, and be adopted back — so two players who switched in opposite directions in
    /// the same instant would trade views forever instead of once. With it, the accepted hazard is
    /// exactly what the user accepted: <b>they trade views ONE time and then stand still</b>, and
    /// the next human press settles it outright.</para>
    ///
    /// <para>Note what this does NOT suppress: the surface changes the GAME's own already-synced
    /// actions cause (<c>MoveToNewNode</c>, the <c>SelectQuest</c> receive path,
    /// <c>MultiplayerStartup</c>). Those do publish an edge — and they are harmless, because every
    /// client received the same game action and is therefore already on that surface, so every
    /// receiver's change gate finds nothing to do.</para>
    /// </summary>
    private static MapIconLayer.MapSurface _adoptedSurface = MapIconLayer.MapSurface.Unknown;

    /// <summary>The selection key we last PUBLISHED, and whether we have published one at all in
    /// this room. Zero is a legal value ("nothing selected"), so the validity flag is separate.</summary>
    private static uint _publishedSelectKey;

    /// <inheritdoc cref="_publishedSelectKey"/>
    private static bool _publishedSelectValid;

    /// <summary>Our own wrapping SELECTION stamp: bumped once per COMPLETED local selection change
    /// THAT A HUMAN HERE MADE. See <see cref="_adoptedSelectKey"/>.</summary>
    private static byte _localSelectStamp;

    /// <summary>
    /// The selection this client made ON A PEER'S BEHALF, so the resulting change publishes NO
    /// stamp edge of its own — the exact counterpart of <see cref="_adoptedSurface"/>, and it is
    /// load-bearing for the same reason.
    ///
    /// <para>Without it, adopting a peer's selection would itself look like a selection made here,
    /// publish an edge, and be adopted back: two clients would bounce one selection between them
    /// for ever at the packet rate. With it, the accepted hazard is the surface's — if two people
    /// select in the very same instant they trade selections ONCE and then stand still, and the
    /// next click settles it outright.</para>
    ///
    /// <para>IT LIVES EXACTLY ONE SAMPLE, AND AT MOST <see cref="AdoptedSuppressSeconds"/>. Cleared
    /// on the first <see cref="Sample"/> after the adoption whether or not the key matched, because
    /// the game is free to REFUSE an adopted click (<c>IsSelectable()</c>) — and a suppression flag
    /// left standing after a refusal would swallow the next genuine local selection of that same
    /// node. The clock is the belt to that braces: the extras packet is only sent when something
    /// CHANGED, so a refused adoption produces no change, no packet and therefore no
    /// <see cref="Sample"/> at all — without the deadline the flag would simply wait there.</para>
    /// </summary>
    private static uint _adoptedSelectKey;

    /// <inheritdoc cref="_adoptedSelectKey"/>
    private static bool _hasAdoptedSelect;

    /// <inheritdoc cref="_adoptedSelectKey"/>
    private static float _adoptedSelectAt;

    /// <summary>How long an unconsumed adopted-selection suppression stays believed. Far longer
    /// than the frame the click runs in and far shorter than any two deliberate human clicks on the
    /// same icon.</summary>
    private const float AdoptedSuppressSeconds = 2f;

    /// <summary>The adopted surface we are currently trying to press, its deadline and its attempt
    /// count. <c>Unknown</c> = nothing pending.</summary>
    private static MapIconLayer.MapSurface _wantSurface = MapIconLayer.MapSurface.Unknown;
    private static int _wantFromPeer;
    private static float _wantNextAttempt;
    private static int _wantAttempts;

    // ---- the key cache ----------------------------------------------------------------------
    // MANDATORY, not an optimisation: MapChoreographer.InitMap destroys and respawns every
    // location on a quest unlock, a city<->world switch and a travel animation, so a key->location
    // map that is not rebuilt points at dead objects. The rebuild trigger is the interactor's own
    // ScanGeneration, which it bumps exactly when it REPLACES the set.

    private static readonly List<uint> CacheKeys = new(64);
    private static readonly List<MapLocation> CacheLocations = new(64);
    private static int _cacheGeneration = int.MinValue;
    private static bool _cacheValid;

    private static readonly List<int> Scratch = new(4);

    // ---- diagnostics ------------------------------------------------------------------------

    private static string _lastNote = string.Empty;
    private static float _nextNoteAt;
    private const float NoteThrottleSeconds = 5f;

    /// <summary>Drop everything on session end / shutdown, so a new session re-derives every fact
    /// rather than inheriting one — including every placard, which owns a GameObject.</summary>
    internal static void Reset()
    {
        Peers.Clear();
        // ---- shared-gaze hunk: the same teardown edge.
        RemoteSharedGaze.Reset();
        ConsumedStamp.Clear();
        SeenStamp.Clear();
        ConsumedSelectStamp.Clear();
        SeenSelectStamp.Clear();
        _publishedSurface = MapIconLayer.MapSurface.Unknown;
        _localSurfaceStamp = 0;
        _adoptedSurface = MapIconLayer.MapSurface.Unknown;
        _publishedSelectKey = 0u;
        _publishedSelectValid = false;
        _localSelectStamp = 0;
        _adoptedSelectKey = 0u;
        _hasAdoptedSelect = false;
        _adoptedSelectAt = 0f;
        _wantSurface = MapIconLayer.MapSurface.Unknown;
        _wantFromPeer = 0;
        _wantNextAttempt = 0f;
        _wantAttempts = 0;
        CacheKeys.Clear();
        CacheLocations.Clear();
        _cacheGeneration = int.MinValue;
        _cacheValid = false;
        _lastNote = string.Empty;
        _nextNoteAt = 0f;
        Placards.ReleaseAll();
    }

    /// <summary>
    /// Whether the extras packet must go out NOW rather than on its own cadence.
    ///
    /// <para>THREE EDGES PRE-EMPT: entering the room, a COMPLETED local world↔city change, and the
    /// SELECTION changing. All three are discrete, human-paced acts whose whole purpose is to be
    /// looked at, so up to 200 ms of cadence latency between two headsets is exactly the "did that
    /// work?" this feature exists to remove — and since report 13 the selection is not merely
    /// looked at but FOLLOWED, so its latency is the whole feature.</para>
    ///
    /// <para>THE HOVER KEY DELIBERATELY DOES NOT. Sweeping the beam across a row of icons produces
    /// a new hover key at up to the rig rate; letting that pre-empt would turn a wrist flick into a
    /// packet burst. It rides the ordinary 5 Hz cadence instead — the established distinction
    /// between the pre-empting and the capped idioms.</para>
    ///
    /// <para>NEITHER DOES THE MAP-FAN CHARACTER KEY, and for the opposite reason to the hover's: it
    /// changes only when the player picks a different character on the party display, which is rare
    /// and slow, and nothing acts on the edge — a receiver reads it as a LEVEL when it next re-runs
    /// its resolve. So the 5 Hz cadence carries it and pre-empting for it would buy nothing.</para>
    ///
    /// <para>PURE: this reads live state and compares it against what was last SENT. It must not
    /// mutate anything, because the rate gate evaluates it on every frame and
    /// <see cref="Sample"/> only runs on the frames the gate lets through.</para>
    /// </summary>
    internal static bool SendDue
    {
        get
        {
            if (!MapRoomDriver.Active)
                return false;
            if (!_sentValid)
                return true;   // arriving in the room is itself the first edge
            MapIconLayer.MapSurface surface = MapIconLayer.CurrentSurface;
            if (surface != MapIconLayer.MapSurface.Unknown && surface != _publishedSurface)
                return true;
            MapLocationInteractor? locations = MapRoomDriver.ActiveLocations;
            MapLocation? staged = locations != null ? locations.Staged : null;
            return KeyOf(staged) != _sentSelectKey;
        }
    }

    /// <summary>What the last packet really carried, so <see cref="SendDue"/> can compare against
    /// it rather than against a guess. False until the first record goes out.</summary>
    private static bool _sentValid;

    /// <inheritdoc cref="_sentValid"/>
    private static uint _sentSelectKey;

    // ---- send side --------------------------------------------------------------------------

    /// <summary>
    /// Fill the map-room fields of the outgoing extras packet. Leaves
    /// <see cref="PresenceState.HasMapRoom"/> false — and therefore the whole record absent, and
    /// the packet byte-identical to ModBuild 221's — whenever this client's own 3D map room is not
    /// standing.
    /// </summary>
    internal static void Sample(ref PresenceState extras)
    {
        if (!MapRoomDriver.Active)
        {
            // Leaving the room forgets our own publishing state, so re-entering it does not send a
            // stamp edge nobody made. BOTH stamps: a player who walks out with a quest selected and
            // comes back would otherwise publish "I just selected this" on their first packet, and
            // every peer would adopt a selection nobody had touched.
            _publishedSurface = MapIconLayer.MapSurface.Unknown;
            _publishedSelectValid = false;
            _publishedSelectKey = 0u;
            _hasAdoptedSelect = false;
            _sentValid = false;
            _sentSelectKey = 0u;
            // ---- shared-gaze hunk: THE ROOM-DOWN EDGE. The host's decision about where the party
            // is looking is a constant of ONE room visit, so leaving forgets it — a decision that
            // outlived the room would seat the next visit's windows from where somebody looked in
            // the last one. Called for its edge, not for its answer.
            RemoteSharedGaze.SampleHostYaw(out _);
            return;
        }

        MapIconLayer.MapSurface surface = MapIconLayer.CurrentSurface;
        if (surface != MapIconLayer.MapSurface.Unknown && surface != _publishedSurface)
        {
            // A COMPLETED local change. Unknown is deliberately not a change: both map GameObjects
            // are down for the frames of a transition, and publishing a stamp for that would send
            // an edge in the middle of a switch — twice per switch instead of once.
            //
            // AND AN ADOPTED CHANGE PUBLISHES NO EDGE (see _adoptedSurface): the stamp means "a
            // human at THIS table switched". Bumping it for a press we made on somebody else's
            // behalf is what would turn the accepted one-time swap into an endless one.
            bool adopted = surface == _adoptedSurface;
            _adoptedSurface = MapIconLayer.MapSurface.Unknown;
            if (_publishedSurface != MapIconLayer.MapSurface.Unknown && !adopted)
            {
                unchecked { _localSurfaceStamp++; }
                VRLog.Info(Scope, $"MAP ROOM surface CHANGED here: {_publishedSurface} → {surface} "
                                  + $"— record 20's surface stamp is now {_localSurfaceStamp}, and "
                                  + "every peer standing in a 3D map room adopts it EXACTLY ONCE on "
                                  + "that edge. The user's ruling is that anybody may switch and "
                                  + "everyone follows; the stamp is what makes 'follows' mean a "
                                  + "single press rather than a per-frame assertion.");
            }
            _publishedSurface = surface;
        }

        MapLocationInteractor? locations = MapRoomDriver.ActiveLocations;
        MapLocation? staged = locations != null ? locations.Staged : null;
        MapLocation? hover = locations != null ? locations.Hover : null;
        // THE PICK IS THE HOVER, AND SINCE ModBuild 226 ONLY THE HOVER. It used to be
        // `staged ?? hover`, because the pick field was the only thing that could carry a selection
        // at all; the selection now has its own stamp and key below, so overriding the hover with
        // it would only ever hide the fact the pick field is FOR — "das richtige Mouseover", which
        // is what a peer's placard is built from. The staged bit is still published, and now says
        // the one thing left for it to say: this player's pointer is on their own selection.
        uint key = KeyOf(hover);

        // ---- the SELECTION edge (report 13) --------------------------------------------------
        // Same three steps the surface takes, in the same order and for the same reasons: consume
        // the adopted-change suppression FIRST (it lives exactly one sample — see
        // _adoptedSelectKey), then compare against what was last PUBLISHED, then bump the stamp
        // only for a change a human at THIS table made.
        uint selectKey = KeyOf(staged);
        if (_hasAdoptedSelect && Time.unscaledTime - _adoptedSelectAt > AdoptedSuppressSeconds)
            _hasAdoptedSelect = false;   // the adoption was refused and produced no change at all
        bool adoptedSelect = _hasAdoptedSelect && selectKey == _adoptedSelectKey;
        _hasAdoptedSelect = false;
        if (!_publishedSelectValid)
        {
            // FIRST SAMPLE IN THIS ROOM IS NOT A CHANGE. Whatever is selected when the room comes
            // up was selected before anybody could have watched it happen.
            _publishedSelectValid = true;
            _publishedSelectKey = selectKey;
        }
        else if (selectKey != _publishedSelectKey)
        {
            uint had = _publishedSelectKey;
            _publishedSelectKey = selectKey;
            if (!adoptedSelect)
            {
                unchecked { _localSelectStamp++; }
                VRLog.Info(Scope, "MAP ROOM selection CHANGED here: "
                                  + $"{Describe(had)} → {NameOf(staged, selectKey)} — record 20's "
                                  + $"selection stamp is now {_localSelectStamp}, and every peer "
                                  + "standing in a 3D map room adopts it EXACTLY ONCE on that edge "
                                  + "(the user's ruling is that there is only ONE selection and "
                                  + "everybody sees it). Key 0 on an edge is a DESELECTION, which "
                                  + "is how 'nur eine einzige Auswahl' holds in both directions.");
            }
            else
            {
                VRLog.Info(Scope, "MAP ROOM selection changed here to "
                                  + $"{NameOf(staged, selectKey)} because THIS CLIENT ADOPTED a "
                                  + "peer's edge — so NO stamp is published for it. That "
                                  + "suppression is what stops two clients from bouncing one "
                                  + "selection between them for ever; do not remove it.");
            }
        }

        byte flags = NetProtocol.MapRoomInRoomBit;
        if (FFSNetwork.IsHost)
            flags |= NetProtocol.MapRoomHostBit;
        if (surface != MapIconLayer.MapSurface.Unknown)
        {
            flags |= NetProtocol.MapRoomSurfaceKnownBit;
            if (surface == MapIconLayer.MapSurface.City)
                flags |= NetProtocol.MapRoomSurfaceCityBit;
        }
        if (key != 0u)
        {
            flags |= NetProtocol.MapRoomPickValidBit;
            // "The hover IS the selection", the one statement the staged bit still makes now that
            // the selection travels in its own fields. It is a comparison of the two keys rather
            // than `staged != null`, which used to be enough only because the pick WAS the staged
            // location whenever there was one.
            if (selectKey != 0u && key == selectKey)
                flags |= NetProtocol.MapRoomPickStagedBit;
        }

        extras.HasMapRoom = true;
        extras.MapRoomFlags = flags;
        extras.MapRoomSurfaceStamp = _localSurfaceStamp;
        extras.MapRoomPickKey = key;
        extras.MapRoomSelectStamp = _localSelectStamp;
        extras.MapRoomSelectKey = selectKey;
        // Which party member THIS client's map fan is showing. Costs nothing to sample — the hand
        // keeps the key beside the character it already resolved — and it saves the receiver from
        // deducing the fan's owner from its card count, a deduction whose tie-break assumes you only
        // ever display characters you control. See MapRoomHand.LocalFanCharacterKey.
        extras.MapRoomFanCharacterKey = WorldUI.MapRoom.MapRoomHand.LocalFanCharacterKey;
        // ---- THE SHARED GAZE YAW (its own hunk; everything it needs lives in RemoteSharedGaze) ---
        // The HOST's one decision about where the party is looking, so a shared window spawns in
        // front of the players and in the SAME place for all of them. A non-host answers "no
        // decision" and the bit stays clear, which is today's fixed-axis behaviour. The flags byte
        // is rewritten here rather than above so this stays one liftable block.
        extras.MapRoomGazeYaw = RemoteSharedGaze.SampleHostYaw(out bool gazeValid);
        if (gazeValid)
            extras.MapRoomFlags |= NetProtocol.MapRoomGazeValidBit;
        // ---- end of the shared-gaze hunk ---------------------------------------------------------

        // What SendDue compares the next frame's live state against.
        _sentValid = true;
        _sentSelectKey = selectKey;
    }

    // ---- receive side -----------------------------------------------------------------------

    /// <summary>Take one peer's freshly parsed extras packet. Pure bookkeeping — nothing is driven
    /// here, so a burst of packets in one frame costs one apply, not one per packet.</summary>
    internal static void Observe(int senderId, in PresenceState p)
    {
        if (senderId <= 0)
            return;
        if (!p.HasMapRoom)
        {
            // Absence is a DEFINED state: that peer is not in the 3D map room. Forgetting them is
            // exactly what a pre-record build gives us.
            Forget(senderId);
            return;
        }
        Peers[senderId] = new PeerRoom(p.MapRoomFlags, p.MapRoomSurfaceStamp, p.MapRoomPickKey,
                                       p.MapRoomSelectStamp, p.MapRoomSelectKey,
                                       p.MapRoomFanCharacterKey, Time.unscaledTime);
        // ---- shared-gaze hunk: the same packet, read for the two facts that decision needs (is
        // this peer in the room, and is it the host publishing a gaze yaw). Fed from HERE rather
        // than from a second parse so the two views of "who is in the room" cannot drift apart.
        RemoteSharedGaze.Observe(senderId, in p);
    }

    /// <summary>
    /// The character key a peer's 3D-map card fan is built for, 0 when they have none open or their
    /// build does not publish one.
    ///
    /// <para>Read by <c>RemoteHandFan</c> when it resolves which loadout to print on that peer's
    /// fan. A LABEL, not an instruction, like every other key on this record: the receiver looks it
    /// up in its OWN party and draws from its OWN card art, and a key that resolves to nothing
    /// simply leaves the older size-deduction in charge.</para>
    /// </summary>
    internal static bool TryGetPeerFanCharacterKey(int playerId, out uint key)
    {
        key = Peers.TryGetValue(playerId, out PeerRoom s) && s.InRoom ? s.FanCharacterKey : 0u;
        return key != 0u;
    }

    private static void Forget(int senderId)
    {
        Peers.Remove(senderId);
        // ---- shared-gaze hunk: the same forget edge, so "who is in the room" cannot differ.
        RemoteSharedGaze.Forget(senderId);
        ConsumedStamp.Remove(senderId);
        SeenStamp.Remove(senderId);
        // THE SELECTION HISTORY GOES WITH THEM, AND THAT IS THE SAFE DIRECTION: a peer who leaves
        // and comes back is a peer we have no history for, so their first packet is not an edge and
        // cannot drag this table onto a selection nobody just made. The cost is that a selection
        // change made while they were unheard-of is missed, which the next real click corrects.
        ConsumedSelectStamp.Remove(senderId);
        SeenSelectStamp.Remove(senderId);
    }

    /// <summary>
    /// Apply whatever the room has agreed on, once per frame.
    ///
    /// <para>THE GATE IS HERE AND NOT IN THE PARSER, on purpose: a parser that discards data cannot
    /// be tested for what it discarded, and the wire tests must be able to see every field of a
    /// record this client will not act on.</para>
    /// </summary>
    internal static void Resolve()
    {
        PruneStale();
        if (!MapRoomDriver.Active)
        {
            // The room is down (or was never up — the 3D map is off for this player). Nothing on
            // the wire may switch anybody's map or draw anything here: the picture is byte-for-byte
            // ModBuild 221's, one comparison per frame.
            Placards.ReleaseAll();
            _wantSurface = MapIconLayer.MapSurface.Unknown;
            _adoptedSurface = MapIconLayer.MapSurface.Unknown;
            return;
        }
        // ONE cache rebuild per frame, ahead of both consumers: the selection resolver and the
        // placards both turn keys into live MapLocations, and rebuilding twice would be two scans
        // of a set that changes only on InitMap.
        RefreshCache();
        ResolveSurface();
        ResolveSelection();
        ResolvePlacards();
    }

    private static void PruneStale()
    {
        if (Peers.Count == 0)
            return;
        float now = Time.unscaledTime;
        Scratch.Clear();
        foreach (KeyValuePair<int, PeerRoom> kv in Peers)
        {
            if (now - kv.Value.At > PeerStaleSeconds)
                Scratch.Add(kv.Key);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Forget(Scratch[i]);
        Scratch.Clear();
    }

    // ---- 2a: the world ↔ city surface -------------------------------------------------------

    private static void ResolveSurface()
    {
        MapIconLayer.MapSurface mine = MapIconLayer.CurrentSurface;

        // ADOPT ON EDGE. Walk every peer in the room and take the FIRST unconsumed stamp change.
        // Order does not matter for correctness — each edge is consumed exactly once and the last
        // one applied wins — and the walk is over at most three peers.
        foreach (KeyValuePair<int, PeerRoom> kv in Peers)
        {
            PeerRoom s = kv.Value;
            if (!s.InRoom || !s.SurfaceKnown)
                continue;
            if (!SeenStamp.TryGetValue(kv.Key, out byte seen))
            {
                // FIRST SIGHT IS NOT AN EDGE. A peer arriving mid-session with stamp 3 has not just
                // changed anything; treating it as a change would drag whoever joined last onto
                // whatever surface the other player happens to be on, which is an instruction
                // nobody gave.
                SeenStamp[kv.Key] = s.SurfaceStamp;
                ConsumedStamp[kv.Key] = s.SurfaceStamp;
                continue;
            }
            if (seen == s.SurfaceStamp)
                continue;
            SeenStamp[kv.Key] = s.SurfaceStamp;
            if (ConsumedStamp.TryGetValue(kv.Key, out byte done) && done == s.SurfaceStamp)
                continue;
            ConsumedStamp[kv.Key] = s.SurfaceStamp;
            MapIconLayer.MapSurface want = s.IsCity
                ? MapIconLayer.MapSurface.City
                : MapIconLayer.MapSurface.World;
            _wantSurface = want;
            _wantFromPeer = kv.Key;
            _wantNextAttempt = 0f;
            _wantAttempts = 0;
            VRLog.Info(Scope, $"MAP ROOM surface EDGE from player {kv.Key}: they switched to "
                              + $"{want} (stamp {s.SurfaceStamp}). This client will press the "
                              + "game's own guildmaster cap ONCE if it is not already there — never "
                              + "continuously, because a re-asserted binary value is a write war and "
                              + "the user chose symmetric authority ('anybody may switch, everyone "
                              + "follows'). If two of us switched in the same instant we trade views "
                              + "once and the next press settles it: that is ACCEPTED, not a defect.");
        }

        if (_wantSurface == MapIconLayer.MapSurface.Unknown)
            return;

        // Already there — including the very common case where one of the game's OWN synced
        // actions (MoveToNewNode, the SelectQuest receive path, MultiplayerStartup) has already
        // moved the surface for us. A record that finds the surface correct is a no-op, and that
        // is what keeps this out of the game's way.
        if (mine == _wantSurface)
        {
            _wantSurface = MapIconLayer.MapSurface.Unknown;
            return;
        }
        if (mine == MapIconLayer.MapSurface.Unknown)
            return; // a transition is in flight; pressing into it would be a second switch

        float now = Time.unscaledTime;
        if (now < _wantNextAttempt)
            return;
        _wantNextAttempt = now + SurfaceRetrySeconds;
        _wantAttempts++;

        EGuildmasterMode mode = _wantSurface == MapIconLayer.MapSurface.City
            ? EGuildmasterMode.City
            : EGuildmasterMode.WorldMap;
        // Mark the change we are about to cause as ADOPTED before the press, not after: the press
        // runs the game's own click path synchronously and the surface can already have flipped by
        // the time it returns.
        _adoptedSurface = _wantSurface;
        bool pressed = MapRoomDriver.PressGuildmasterMode(
            mode, $"record 20 surface edge from player {_wantFromPeer}");
        if (pressed)
        {
            // Pressed. Whether the surface actually CHANGES is the game's decision — the press ran
            // its whole guard chain — so the loop above keeps checking until it does or gives up.
            return;
        }
        if (_wantAttempts < SurfaceMaxAttempts)
            return;

        MapIconLayer.MapSurface gaveUp = _wantSurface;
        _wantSurface = MapIconLayer.MapSurface.Unknown;
        _adoptedSurface = MapIconLayer.MapSurface.Unknown;
        VRLog.Info(Scope, $"MAP ROOM surface edge from player {_wantFromPeer} ABANDONED after "
                          + $"{SurfaceMaxAttempts} attempt(s): this client is on {mine} and could "
                          + $"not reach {gaveUp}. THAT IS OFTEN CORRECT, not a failure — the game "
                          + "legitimately refuses the city cap when the city is not unlocked for "
                          + "this save (UIGuildmasterHUD.IsAvailable, and OpenCityMap returns early "
                          + "outside a campaign), and the rail mirrors the game's own "
                          + "interactability. Nothing was forced: the press is "
                          + "ExecuteEvents.pointerClickHandler on the real Toggle and its refusal is "
                          + "the game's answer, not ours.");
    }

    // ---- THE ROOM'S ONE SELECTION (report 13) ------------------------------------------------

    /// <summary>
    /// Adopt a peer's SELECTION edge — at most one per frame, through the game's own click seam.
    ///
    /// <para>Structurally identical to <see cref="ResolveSurface"/>, deliberately: the same
    /// first-sight-is-not-an-edge rule, the same consume-once bookkeeping, the same last-edge-wins
    /// walk over at most three peers, and the same accepted one-time swap if two people act in the
    /// very same instant. The differences are what an edge names (a location key, where 0 means
    /// "nothing") and how it is applied (<c>MapLocationInteractor.AdoptSelection</c>, which is the
    /// game's own <c>pointerClickHandler</c> / <c>Deselect()</c> and nothing else).</para>
    ///
    /// <para>AN EDGE IS CONSUMED EVEN WHEN IT CANNOT BE APPLIED. A key that resolves to no live
    /// location here means the peer is on the other map surface, or our location set has churned;
    /// re-trying it on every later packet would either do nothing for ever or fire late, on a map
    /// the selection was never made on. It is dropped with a stated reason, and the peer's next
    /// real click is a new edge.</para>
    /// </summary>
    private static void ResolveSelection()
    {
        MapLocationInteractor? interactor = MapRoomDriver.ActiveLocations;
        if (interactor == null)
            return;

        bool have = false;
        uint wantKey = 0u;
        int fromPeer = 0;
        foreach (KeyValuePair<int, PeerRoom> kv in Peers)
        {
            PeerRoom s = kv.Value;
            if (!s.InRoom)
                continue;
            if (!SeenSelectStamp.TryGetValue(kv.Key, out byte seen))
            {
                SeenSelectStamp[kv.Key] = s.SelectStamp;
                ConsumedSelectStamp[kv.Key] = s.SelectStamp;
                continue;
            }
            if (seen == s.SelectStamp)
                continue;
            SeenSelectStamp[kv.Key] = s.SelectStamp;
            if (ConsumedSelectStamp.TryGetValue(kv.Key, out byte done) && done == s.SelectStamp)
                continue;
            ConsumedSelectStamp[kv.Key] = s.SelectStamp;
            have = true;
            wantKey = s.SelectKey;
            fromPeer = kv.Key;
        }
        if (!have)
            return;

        MapLocation? want = null;
        if (wantKey != 0u && (!TryResolveKey(wantKey, out want) || want == null))
        {
            Note($"player {fromPeer} SELECTED map key 0x{wantKey:X8}, which resolves to no live "
                 + $"location here ({CacheLocations.Count} known) — they are almost certainly on "
                 + "the other map surface, and the surface edge that follows will bring this client "
                 + "there. The edge is CONSUMED rather than retried: an instruction replayed onto a "
                 + "map it was not made on is worse than one that was missed");
            return;
        }

        // Suppress the echo BEFORE driving: AdoptSelection runs the game's click path
        // synchronously, so the local selection can already have changed by the time it returns.
        _adoptedSelectKey = wantKey;
        _hasAdoptedSelect = true;
        _adoptedSelectAt = Time.unscaledTime;
        bool drove = interactor.AdoptSelection(want, $"player {fromPeer}'s selection edge");
        VRLog.Info(Scope, $"MAP ROOM selection EDGE from player {fromPeer}: "
                          + $"{NameOf(want, wantKey)}. "
                          + (drove
                              ? "APPLIED through the game's own seam — the same "
                                + "ExecuteEvents.pointerClickHandler a local trigger dispatches (or "
                                + "MapLocation.Deselect for key 0), so IsSelectable() and the game's "
                                + "own click action still decide. If nothing visibly happened, the "
                                + "game refused it and would have refused the same click here."
                              : "ALREADY the selection at this table — nothing was driven. An edge "
                                + "that arrives twice therefore costs one comparison, which is why "
                                + "a duplicated packet cannot double-click anything.")
                          + " The user's ruling is ONE selection everybody sees; this client never "
                          + "re-asserts it, because a level would be a write war and an edge "
                          + "consumed on arrival cannot oscillate.");
    }

    // ---- 2c: the peers' hovers, as the game's OWN placards -----------------------------------

    private static void ResolvePlacards()
    {
        // THE GAME'S OWN RULE FIRST. While a quest is selected, UIQuestPopupManager.PreviewQuest
        // previews nothing at all — so the peer whose placard this would be is looking at no card
        // either, and drawing one would be exactly the opposite of "1:1 so wie es für den Spieler
        // auch aussieht". With the selection now global this reads the same on every machine.
        if (WorldUI.MapRoom.MapHoverVerdict.AQuestIsSelected)
        {
            Placards.ReleaseAll();
            return;
        }

        MapLocationInteractor? interactor = MapRoomDriver.ActiveLocations;
        if (interactor == null)
        {
            Placards.ReleaseAll();
            return;
        }

        // Ascending player-id order, so every machine assigns the same lanes — see the class doc.
        Scratch.Clear();
        foreach (KeyValuePair<int, PeerRoom> kv in Peers)
        {
            PeerRoom s = kv.Value;
            if (s.InRoom && s.PickValid && s.PickKey != 0u)
                Scratch.Add(kv.Key);
        }
        Scratch.Sort();

        // The LOCAL player's own hovered location takes lane 0 of its own icon: the local card is
        // seated on the same anchor by the same code, so a peer reading the same icon has to start
        // above it or the two would intersect.
        MapLocation? localHover = interactor.Hover;

        Placards.BeginFrame();
        for (int i = 0; i < Scratch.Count; i++)
        {
            int playerId = Scratch[i];
            PeerRoom s = Peers[playerId];
            if (!TryResolveKey(s.PickKey, out MapLocation? loc) || loc == null)
            {
                // Unresolvable against THIS client's live locations — the peer is on the other map
                // surface, or their set has churned and ours has not caught up. The only thing a
                // receiver does with a key it cannot resolve is nothing.
                Note($"player {playerId} is pointing at map key 0x{s.PickKey:X8}, which resolves to "
                     + $"no live location here ({CacheLocations.Count} known). They are almost "
                     + "certainly on the other map surface; a key is a MATCH GATE, so the placard is "
                     + "simply not drawn rather than guessed at");
                continue;
            }
            if (!interactor.TryAnchorFor(loc, out Vector3 anchor))
                continue;

            // LANE = rank among the readers of THIS icon only (see the class doc): a placard
            // belongs directly above its own symbol, so two peers on two icons both sit at the
            // base lift instead of one of them floating a row up for no reason.
            int lane = ReferenceEquals(loc, localHover) ? 1 : 0;
            for (int j = 0; j < i; j++)
            {
                if (TryResolveKey(Peers[Scratch[j]].PickKey, out MapLocation? other)
                    && ReferenceEquals(other, loc))
                    lane++;
            }
            Placards.Show(playerId, lane, loc, s.PickKey, anchor);
        }
        Scratch.Clear();
        Placards.EndFrame();
    }

    // ---- identity ---------------------------------------------------------------------------

    /// <summary>
    /// The wire key of one location — <c>FNV-1a(CLocationState.ID)</c>, folded so 0 stays "none".
    ///
    /// <para>THAT ID IS THE IDENTITY THE GAME ITSELF PUTS ON ITS OWN WIRE: the host sends
    /// <c>new LocationToken(location.Location.ID)</c> for <c>GameActionType.SelectQuest</c> and the
    /// receiver resolves it with <c>SingleOrDefault(x =&gt; x.Location.ID == locationId)</c>. If
    /// that string were not identical on both machines the vanilla game's own quest selection would
    /// be broken, so its cross-client stability is not an assumption here — it is load-bearing for
    /// the unmodded game.</para>
    ///
    /// <para>WHAT IS DELIBERATELY NOT USED: a spawn-order INDEX (the two mod collectors walk the two
    /// parents in opposite orders and both have unordered <c>FindObjectsOfType</c> fallbacks), the
    /// GameObject NAME (every location is a clone of one prefab and <c>Init</c> never renames it,
    /// so the name is the same string for all of them), and <c>CMapScenarioState.ScenarioID</c>
    /// (RNG-rolled per playthrough).</para>
    /// </summary>
    private static uint KeyOf(MapLocation? loc)
    {
        if (loc == null)
            return 0u;
        try
        {
            MapRuleLibrary.MapState.CLocationState? state = loc.Location;
            return state != null ? NetProtocol.HashMapKey(state.ID) : 0u;
        }
        catch (System.Exception)
        {
            return 0u;
        }
    }

    private static void RefreshCache()
    {
        MapLocationInteractor? interactor = MapRoomDriver.ActiveLocations;
        if (interactor == null)
        {
            CacheKeys.Clear();
            CacheLocations.Clear();
            _cacheValid = false;
            return;
        }
        int generation = interactor.ScanGeneration;
        if (_cacheValid && generation == _cacheGeneration)
            return;
        _cacheGeneration = generation;
        _cacheValid = true;
        CacheKeys.Clear();
        CacheLocations.Clear();

        int collisions = 0;
        string collided = string.Empty;
        int n = interactor.LocationCount;
        for (int i = 0; i < n; i++)
        {
            MapLocation? loc = interactor.LocationAt(i);
            uint key = KeyOf(loc);
            if (key == 0u || loc == null)
                continue;
            int at = CacheKeys.IndexOf(key);
            if (at >= 0)
            {
                // The game itself searches m_Scenarios and then m_Villages and takes the first
                // match, so FIRST WINS here too — and the collision is LOGGED rather than resolved
                // silently, because "two live locations hash the same" is a fact a later round must
                // be able to read out of the log rather than re-derive.
                collisions++;
                if (collided.Length == 0)
                    collided = $"'{loc.name}' vs '{CacheLocations[at].name}' on 0x{key:X8}";
                continue;
            }
            CacheKeys.Add(key);
            CacheLocations.Add(loc);
        }

        VRLog.Info(Scope, $"MAP ROOM wire key cache rebuilt (scan generation {generation}) — "
                          + $"{CacheKeys.Count} live location(s) keyed by FNV-1a of their own "
                          + "CLocationState.ID, the same string the GAME sends in a LocationToken "
                          + "for SelectQuest. REBUILT ON THE SET CHANGE, not on a cadence: "
                          + "MapChoreographer.InitMap destroys and respawns every location on a "
                          + "quest unlock, a city↔world switch and a travel animation, so a cache "
                          + "that survives one points at dead objects."
                          + (collisions > 0
                              ? $" WARNING: {collisions} key collision(s) — {collided}. First wins, "
                                + "the same order the game's own resolver uses (m_Scenarios then "
                                + "m_Villages). THE CONSEQUENCE OF A COLLISION GREW IN ModBuild 226 "
                                + "AND THIS LINE SAYS SO: it used to read 'can never select "
                                + "anything, because no receiver here calls Select()', and that "
                                + "became false when record 20 started carrying the room's ONE "
                                + "selection (user ruling, report 13). A collision can now select "
                                + "the WRONG icon as well as label it — still only through the "
                                + "game's own click seam, still only on an EDGE, and still bounded "
                                + "by IsSelectable(). The game's own SelectQuest resolver uses the "
                                + "same string with the same first-match rule, so a collision here "
                                + "is a collision there too."
                              : string.Empty));
    }

    private static bool TryResolveKey(uint key, out MapLocation? loc)
    {
        loc = null;
        if (key == 0u)
            return false;
        for (int i = 0; i < CacheKeys.Count; i++)
        {
            if (CacheKeys[i] != key)
                continue;
            MapLocation candidate = CacheLocations[i];
            if (candidate == null)
                return false; // destroyed under us; the next rescan rebuilds the cache
            loc = candidate;
            return true;
        }
        return false;
    }

    private static void Note(string reason)
    {
        float now = Time.unscaledTime;
        if (reason == _lastNote && now < _nextNoteAt)
            return;
        _lastNote = reason;
        _nextNoteAt = now + NoteThrottleSeconds;
        VRLog.Info(Scope, $"MAP ROOM record 20 IGNORED — {reason}. Ignoring is always the safe "
                          + "direction: a placard not drawn is re-offered by the next packet 200 ms "
                          + "later, while a placard drawn on the wrong icon would be a statement "
                          + "about where somebody is looking that is simply false.");
    }

    /// <summary>
    /// Extra lift for the SECOND and further readers of ONE icon, real metres above the anchor.
    ///
    /// <para>Lane 0 gets NO extra lift at all — "direkt über dem jeweiligen Symbol" is the ruling,
    /// and the anchor already carries the icon-clearing gap
    /// (<c>MapLocationInteractor.TryAnchorFor</c>, the same one the local card sits on). This
    /// number only exists for the case two people read the SAME icon at once, and even then it is
    /// the second separator rather than the first: each card is turned to a different person, so
    /// two readers standing anywhere but shoulder to shoulder are already apart. Approximate on
    /// purpose — a preview card's height depends on how many enemies and rewards that quest has,
    /// and measuring it would mean measuring a card that has not finished laying out yet.</para>
    /// </summary>
    private const float SameIconLaneLiftMeters = 0.18f;

    /// <summary>
    /// THE PLACARDS — one instance of THE GAME'S OWN quest-preview popup per pointing peer, built
    /// and torn down here and owned by nothing else.
    ///
    /// <para>USER RULING (report 6, verbatim): <i>"Mouseover der Symbole in der Map soll nicht das
    /// Steam-Symbol sein, sondern das richtige Mouseover das der Spieler auch sieht, zu dem
    /// jeweiligen Spieler hingedreht, direkt über dem jeweiligen Symbol. Aktuell sieht man das
    /// Steam-Logo zusammen mit einem kleinen Text. Der soll auch weg - es soll 1:1 so aussehen wie
    /// es für den Spieler auch aussieht."</i> <b>This supersedes the earlier "jede fremde Tafel
    /// zusätzlich, mit Namen": there is no name row and no Steam picture any more, and they must
    /// not be restored from that older note.</b> Whose placard it is, is said by which way it
    /// faces — see <c>MapRoom.HoverCardPose</c>, which poses it.</para>
    ///
    /// <para><b>WHY A CLONE AND NOT A SECOND RENDERER.</b> What kept the real card out of a foreign
    /// hover was never drawing — it was ownership: <c>UIQuestPopupManager</c> holds ONE
    /// <c>questPreviewPopup</c> and previews only while <c>selectedQuest == null</c>, so routing a
    /// peer's hover through the manager would take the local player's own card away and would fire
    /// the game's mouse-enter chain per peer per icon. The thing that cannot be shared is the
    /// INSTANCE, so each peer is given one: <c>Object.Instantiate</c> of the game's own popup,
    /// filled through its own public <c>SetQuest</c> with the same <c>IQuest</c> the game's own
    /// <c>MapLocation.PreviewQuest</c> would build for that node (<c>new Quest(LocationQuest)</c>,
    /// or <c>new HeadqueartQuest()</c> for the capital — MapLocation.cs:612-652). The manager, the
    /// local card and every <c>MapLocation</c> are untouched.</para>
    ///
    /// <para><b>THE BUILD IS <c>StatPanelSurface</c>'s PROVEN ONE, STEP FOR STEP</b>, because a
    /// half-live copy of a game window is how a mod steals a singleton or a window id:
    /// <list type="number">
    /// <item>instantiate under an INACTIVE holder, so Unity never calls a single <c>Awake</c>;</item>
    /// <item>fill it while it is still inert (<c>SetQuest</c> touches only serialized references —
    /// <c>UIQuestDescription.Setup</c>, the enemy/reward pools — and needs no lifecycle);</item>
    /// <item><c>DestroyImmediate</c> every lifecycle component while it has still never been
    /// activated: the <c>UIWindow</c> (so the clone can never enter <c>UIWindow.GetWindow</c>,
    /// never raise a visibility event, and never be seen by <c>ModalFallback</c>'s catch-all), the
    /// <c>UIQuestPreviewPopup</c> itself (its <c>Awake</c> would dereference the window we just
    /// took away) and the game's <c>UIFollowMapLocation</c> (which writes a screen-derived
    /// <c>localPosition</c> through a camera this mod freezes — see <c>HoverCardPose</c>);</item>
    /// <item>force it opaque and non-interactive, because the popup it was copied from may have
    /// been mid-fade or hidden outright (<c>UIWindow.SetCanvasAlpha</c> leaves alpha 0 and, with
    /// <c>m_DisableOnZeroAlpha</c>, the object deactivated);</item>
    /// <item>only then hand the bare imagery to <c>CanvasConversion.Convert</c>, which is what
    /// finally activates it — at which point no component with any lifecycle is left in it.</item>
    /// </list></para>
    ///
    /// <para>A NODE THE PEER ONLY SWEPT PAST GETS NO CLONE. The key must stand still for
    /// <c>SettleSeconds</c> before anything is built: a peer sweeping their laser across a row of
    /// icons publishes a new key every packet, and instantiating a quest card five times a second
    /// would be both a frame cost and a churn of addressable portrait loads.</para>
    ///
    /// <para>THAT GATE ALSO BOUNDS THE ONE EXPOSURE THIS INHERITS FROM THE GAME'S OWN CARD.
    /// <c>UIQuestEnemy.ShowEnemy</c> is <c>async void</c> and finishes by writing into its own
    /// <c>Image</c> after an addressable sprite load; a card destroyed while that load is in flight
    /// is the game's own hazard too (its preview popup is torn down by every <c>InitMap</c>), and
    /// it is not made worse here as long as cards are not created and destroyed at packet rate.
    /// Cancelling the load on the way out was considered and NOT done: <c>CancelLoad</c> would
    /// surface an <c>OperationCanceledException</c> out of the same <c>async void</c>, which trades
    /// one unattributable log line for another.</para>
    /// </summary>
    private static class Placards
    {
        /// <summary>How long a peer's pick key must stand still before their placard is built.
        /// Just over one packet interval at <c>NetProtocol.ExtrasSendRateHz</c> = 5 Hz, so a key
        /// that survives one repeat counts as "they are looking at this".</summary>
        private const float SettleSeconds = 0.25f;

        /// <summary>
        /// Host scale factor for a peer we have no avatar for yet — the SHIPPED legibility, as a
        /// multiple of <c>PanelLayout.WorldScale</c> and the canvas metres-per-pixel.
        ///
        /// <para>THE REAL NUMBER IS THE OWNER'S AND ARRIVES ON THE WIRE — see
        /// <see cref="ScaleFactorFor"/>. This is what a placard is drawn at in the one window where
        /// the owner's dial genuinely is not known: a peer whose id we are placing a card for but
        /// who has no <c>RemoteAvatar</c> yet (joining, not embodied). It is not a second source of
        /// truth and it does not latch — see the correction paragraph on <see cref="ScaleFactorFor"/>.</para>
        ///
        /// <para>DERIVED, NOT TYPED. This was <c>0.875f</c>: 0.7 × 1.25, the product of the
        /// small-dialog cap (<see cref="ModalFallback.WindowScaleFactor"/>, 0.7) and the legibility
        /// default of a PREVIOUS build. The shipped dial is
        /// <see cref="Defaults.WindowLegibility"/> = 1.5, so the correct product is 1.05 and every
        /// peer's placard was drawn at 83 % of the size the same icon gave the local player.
        /// Nothing could catch it: it is a PRODUCT of two defaults, which is precisely the case
        /// <c>scripts/check-remote-defaults.py</c> documents itself as unable to pin (see its
        /// RemoteItemFan._radius note — "their two factors are pinned individually instead").
        /// Written as the MULTIPLY so that moving either factor moves this with it and the stale
        /// copy cannot come back; neither factor may be re-typed as a number here.</para>
        /// </summary>
        private const float FallbackScaleFactor =
            ModalFallback.WindowScaleFactor * Defaults.WindowLegibility;

        /// <summary>
        /// The host scale factor ONE peer's placard is drawn at: the small-dialog cap times
        /// <b>that peer's own</b> <c>[WorldUI] WindowLegibility</c>.
        ///
        /// <para>USER RULING, 2026-08-28, verbatim: <i>"Auch hier soll die 1:1 Regel gelten, also
        /// die Größe des Besitzers."</i> A peer's placard is a picture of the card THEY are
        /// reading, so it is drawn at the size THEY are reading it at — the project's 1:1 rule,
        /// whose only exceptions live in <c>Net/RevealGate.cs</c>. This settles the ownership
        /// question that <c>HoverCardPose</c> and <see cref="RemoteBoardTooltip"/> had answered in
        /// opposite directions; the tooltip (owner's <c>HoverInfoScale</c> off record 28) was
        /// right, and this is now the same shape from the same record.</para>
        ///
        /// <para>THE ARGUMENT THAT LOST IS RECORDED IN <c>HoverCardPose</c>'s CLASS DOC so nobody
        /// re-runs it, together with what the ruling costs: the dial is clamped 1.0..1.75
        /// (<c>ModalFallback.WindowLegibilityMin</c>/<c>Max</c>), so two players at opposite ends
        /// of it see the same icon's placard at sizes 75 % apart, deliberately. At the shipped
        /// default on both sides the factor is 0.7 × 1.5 = 1.05, i.e. unchanged from what
        /// <see cref="FallbackScaleFactor"/> already produced — the ruling is visible only once
        /// somebody moves the dial, which is exactly the case it was made for.</para>
        ///
        /// <para>NOT LATCHED ON THE REVISION EDGE, AND HERE IS WHY. Every other record-28 consumer
        /// (<c>RemoteControlBoard</c>, <c>RemoteItemFan</c>, <c>RemoteHandFan</c>) caches the dials
        /// it needs and refreshes them when <c>RemoteAvatar.BoardTuningRevision</c> moves, because
        /// those numbers size CONSTRUCTED geometry and re-reading them would mean rebuilding it.
        /// Nothing is constructed from this one: <see cref="Show"/> already writes the host scale
        /// every frame behind an epsilon compare, so a plain read per placard per frame — a
        /// dictionary probe and a float, the same cost as the <see cref="EyeOf"/> lookup beside it
        /// — is both cheaper than a latch and strictly more correct. It is also how the second
        /// requirement is met for free: a peer whose record 28 has not arrived draws at the shipped
        /// default (<c>RemoteAvatar</c>'s constructor seeds <c>BoardTuning</c> from an EMPTY payload,
        /// so every dial resolves to this client's shipped value) and CORRECTS ITSELF on the first
        /// frame after the record lands, rather than staying wrong for the session. There is no
        /// state to invalidate when a peer leaves and rejoins.</para>
        ///
        /// <para>ONE HONEST GAP, STATED NOT HIDDEN: the LOCAL card's size is
        /// <c>ModalFallback.DeriveWindowScale</c>, which is <c>Min(cap, boardRelative)</c> — the cap
        /// is what this reproduces, and the board-relative term binds only for a window wide enough
        /// that <c>ModalTargetWidthMeters</c> is the smaller of the two. That term is computed from
        /// the placard's OWN authored width, which is the same prefab on both machines, so it is not
        /// an owner-versus-viewer question and the ruling does not reach it; if a quest preview popup
        /// is ever authored wide enough for it to bind, a placard and the local card would differ by
        /// that term on BOTH machines equally. Reproducing it here would mean measuring a converted
        /// rect before the pose runs, for a case that does not occur today.</para>
        /// </summary>
        /// <summary>Change gate for <see cref="LogPlacardScale"/>, keyed by player id — one line per
        /// peer per real change, never per frame. INSTRUMENT-ONLY: nothing else reads it.</summary>
        private static readonly Dictionary<int, float> LoggedScale = new();

        /// <summary>
        /// THE ONE LINE THAT DECIDES WHOSE DIALS SIZED A PEER'S PLACARD — grep
        /// <c>MAP PLACARD SCALE</c>, on BOTH machines.
        ///
        /// <para>It exists because R2 finding F3 is a breach that is INVISIBLE at the shipped
        /// defaults and invisible to every checker: <c>check-remote-defaults.py</c> compares frozen
        /// constants, <c>check-wire-coverage.py</c> asks whether a dial is on the wire (this one is,
        /// id 143), and neither can see which SIDE's copy a live <c>ConfigEntry.Value</c> read comes
        /// from. So the deciding field is printed instead: this client's own
        /// <c>[WorldUI] CanvasScaleMm</c> beside the owner's legibility factor and the metres the
        /// product lands at.</para>
        ///
        /// <para>HOW TO READ IT. Compare the <c>viewerCanvasScaleMm</c> field in the line for peer N
        /// on this machine against the same field in peer N's OWN log (their line for whoever they
        /// are watching, or their <c>DeriveWindowScale</c> line). Equal ⇒ the breach is dormant and
        /// the placards agree. Unequal ⇒ this viewer is reading that peer's placard at
        /// <c>viewer/owner</c> times the size the peer is reading it at, and only that ratio, since
        /// every other factor in the product is already theirs or a constant.</para>
        /// </summary>
        private static void LogPlacardScale(int playerId, float scale)
        {
            if (LoggedScale.TryGetValue(playerId, out float was) && Mathf.Abs(was - scale) <= 1e-6f)
                return;
            LoggedScale[playerId] = scale;
            float viewerMm = WorldUI.WorldUIConfig.CanvasScaleMm.Value;
            bool haveOwner = NetAvatarDriver.TryGetPeerWindowLegibility(playerId, out float legibility);
            // HW-VERIFY: grep MAP PLACARD SCALE.
            VRLog.Note("Net", $"MAP PLACARD SCALE [player {playerId}]: host scale {scale:F5} = "
                + $"viewerCanvasScaleMm {viewerMm:F3} x 0.001 x worldScale "
                + $"{WorldUI.PanelLayout.WorldScale:F3} x cap {ModalFallback.WindowScaleFactor:F3} x "
                + $"ownerWindowLegibility {legibility:F3}"
                + (haveOwner ? "" : " (SHIPPED default — that peer has no avatar yet)")
                + ". THE LEGIBILITY FACTOR IS THE OWNER'S (record 28 id 180, user ruling "
                + "2026-08-28); viewerCanvasScaleMm IS NOT — it is THIS client's [WorldUI] "
                + "CanvasScaleMm, while the owner sizes their own card from THEIRS "
                + "(ModalFallback.DeriveWindowScale). The owner's copy rides record 28 as id 143 "
                + "and is decoded as RemoteBoardTuning.CanvasScaleMm; this class is keyed by player "
                + "id and has no route to it without an accessor beside "
                + "NetAvatarDriver.TryGetPeerWindowLegibility. Diff this field against the same "
                + "field in that peer's own log: equal means the breach is dormant, unequal means "
                + "this viewer sees that peer's placard at exactly viewer/owner times the size the "
                + "peer is reading it at.");
        }

        private static float ScaleFactorFor(int playerId) =>
            ModalFallback.WindowScaleFactor
            * (NetAvatarDriver.TryGetPeerWindowLegibility(playerId, out float legibility)
                   ? legibility
                   : Defaults.WindowLegibility);

        private sealed class Card
        {
            /// <summary>The inactive parent every clone is born under and returns to on release —
            /// see the class doc's step 1. Destroying it destroys the clone with it.</summary>
            internal GameObject? Holder;

            internal ConvertedPanel? Panel;

            /// <summary>The key the standing clone was BUILT for, 0 when nothing is built.</summary>
            internal uint ShownKey;

            /// <summary>The key the peer is currently on and since when — the settle gate.</summary>
            internal uint WantKey;

            /// <inheritdoc cref="WantKey"/>
            internal float WantSince;

            // NO CACHED HEAD TRANSFORM. It was here while the peer's head was reached by
            // GameObject.Find and the cache was what kept a scene sweep off the tick; since the
            // driver answers by player id (NetAvatarDriver.TryGetPeerHead) the lookup is a
            // dictionary probe and a cache would only be one more thing to invalidate when a peer
            // leaves and rejoins.

            internal bool TouchedThisFrame;
        }

        private static readonly Dictionary<int, Card> Cards = new(4);
        private static readonly List<int> Drop = new(4);

        /// <summary>Set once the game's own popup could not be found, so the warning is not
        /// repeated per frame per peer.</summary>
        private static bool _warnedNoSource;

        internal static void BeginFrame()
        {
            foreach (KeyValuePair<int, Card> kv in Cards)
                kv.Value.TouchedThisFrame = false;
        }

        internal static void EndFrame()
        {
            Drop.Clear();
            foreach (KeyValuePair<int, Card> kv in Cards)
            {
                if (!kv.Value.TouchedThisFrame)
                    Drop.Add(kv.Key);
            }
            for (int i = 0; i < Drop.Count; i++)
            {
                Destroy(Cards[Drop[i]]);
                Cards.Remove(Drop[i]);
            }
            Drop.Clear();
        }

        internal static void ReleaseAll()
        {
            if (Cards.Count == 0)
                return;
            foreach (KeyValuePair<int, Card> kv in Cards)
                Destroy(kv.Value);
            Cards.Clear();
        }

        /// <summary>
        /// Stand one peer's placard on its icon this frame, building it first if the peer has held
        /// that node long enough to mean it.
        /// </summary>
        internal static void Show(int playerId, int lane, MapLocation loc, uint key, Vector3 anchor)
        {
            if (!Cards.TryGetValue(playerId, out Card? card) || card == null)
            {
                card = new Card();
                Cards[playerId] = card;
            }
            card.TouchedThisFrame = true;

            float now = Time.unscaledTime;
            if (card.WantKey != key)
            {
                card.WantKey = key;
                card.WantSince = now;
            }
            if (card.ShownKey != key)
            {
                if (now - card.WantSince < SettleSeconds)
                    return;   // still sweeping — do not instantiate a card they are not reading
                if (!Rebuild(card, playerId, loc, key))
                    return;
            }
            if (card.Panel == null || !card.Panel.IsAlive || card.Panel.HostGo == null)
            {
                // The conversion went away under us (a module teardown releases every panel).
                // Drop the clone with it; the settle gate rebuilds on a later frame.
                Destroy(card);
                return;
            }

            // SIZE FIRST, POSE SECOND: HoverCardPose seats the card by measuring its drawn content
            // in host-local units and converting that through the host transform, so a scale
            // written afterwards would move the card it just seated.
            //
            // ONE OF THE THREE FACTORS IS STILL THIS VIEWER'S, AND THAT IS A KNOWN 1:1 BREACH
            // (R2 finding F3, 2026-09-07). ScaleFactorFor(playerId) IS the owner's — the small-
            // dialog cap times THEIR [WorldUI] WindowLegibility off record 28 id 180, under the
            // 2026-08-28 ruling quoted on that method. PanelLayout.WorldScale is a shipped constant.
            // CanvasScaleMm.Value is THIS VIEWER'S live dial, and the sentence that used to open
            // this comment — "THE FACTOR IS THE OWNER'S DIAL, not this viewer's" — was therefore
            // true of two of the three terms it annotated. What falsified it: the owner's own card
            // is sized through ModalFallback.DeriveWindowScale, which reads the same
            // CanvasScaleMm.Value on THEIR machine (ModalFallback.9.Spawn.cs:1767-1770), so the two
            // expressions differ in exactly this one term; and the project's own
            // WorldUI/Modal/SharedWindowSizeLaw.cs:55-62 names [WorldUI] WindowLegibility and
            // [WorldUI] CanvasScaleMm in one breath as the two dials of this kind, with "a MIRROR
            // wears the OWNER's dial" as the recorded ruling for both.
            //
            // THE OWNER'S COPY IS ALREADY ON THE WIRE — NetProtocol.TuneCanvasScaleMm, id 143,
            // sampled at BoardTuning.cs:328-329 and decoded as RemoteBoardTuning.CanvasScaleMm
            // (BoardTuning.cs:1023, 1319). The sibling mirror consumes it correctly
            // (RemoteBoardTooltip.cs:179, Mathf.Max(0.01f, tuning.CanvasScaleMm)) because it is
            // handed a RemoteAvatar. This class is static and keyed by PLAYER ID: the only route
            // from an id to a peer's tuning is an accessor beside
            // NetAvatarDriver.TryGetPeerWindowLegibility, and NetAvatarDriver was not handed to
            // this lane. The one-line-plus-accessor patch is in the round report.
            //
            // IT IS DELIBERATELY NOT "FIXED" BY FREEZING THE TERM TO Defaults.CanvasScaleMm. That
            // would be correct only where the owner is untuned and would newly BREAK the case where
            // both players have tuned the dial to the same value — trading one wrong picture for a
            // different one, in a project whose memory already records what freezing a hand-tuned
            // value costs. The arithmetic stands until the owner's number can actually be read.
            //
            // Re-read every frame on purpose: it is how a placard built before that peer's record 28
            // arrived corrects itself instead of staying at the shipped default for the session.
            float scale = WorldUI.WorldUIConfig.CanvasScaleMm.Value * 0.001f
                          * WorldUI.PanelLayout.WorldScale * ScaleFactorFor(playerId);
            LogPlacardScale(playerId, scale);
            Transform host = card.Panel.HostGo.transform;
            if (Mathf.Abs(host.localScale.x - scale) > 1e-6f)
                host.localScale = Vector3.one * scale;

            float lift = 0f;
            if (lane > 0)
            {
                float rig = Rig.RigTarget.Current != null
                    ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
                    : 1f;
                lift = lane * SameIconLaneLiftMeters * rig;
            }
            var seat = new Vector3(anchor.x, anchor.y + lift, anchor.z);

            // THE FACING IS THE OWNERSHIP. Null leaves the rotation alone rather than turning the
            // card to the local head, which would make a foreign placard indistinguishable from
            // the local player's own — the one thing this presentation may not do.
            HoverCardPose.Place(card.Panel, hasAnchor: true, anchor: seat,
                                viewer: EyeOf(card, playerId));
        }

        /// <summary>
        /// Where the peer this placard belongs to is standing, or null.
        ///
        /// <para>ASKED OF THE DRIVER, WHICH HAS IT IN A DICTIONARY. The position rides the rig
        /// packets the embodiment sync receives anyway, so this needs no wire field and no traffic:
        /// <see cref="NetAvatarDriver.TryGetPeerHead"/> reads the same last-received head
        /// <c>CollectPeerHeads</c> reads, keeping the player id that one deliberately drops. It was
        /// written for this caller in the same build. The accessor replaced a
        /// <c>GameObject.Find("GloomhavenVR.RemoteAvatar[id]")</c> — a whole-scene sweep reaching a
        /// private naming convention from outside — which this class no longer needs to cache or
        /// rate-limit, because a dictionary lookup per placard per tick is free.</para>
        ///
        /// <para>A peer with no avatar yet (not embodied, still joining) returns null, and the
        /// placard then keeps whatever facing it had. Turning it to the LOCAL head instead would be
        /// worse than saying nothing: it would look exactly like the local player's own card, and
        /// the facing is the only thing that says whose placard this is.</para>
        /// </summary>
        private static Vector3? EyeOf(Card card, int playerId) =>
            NetAvatarDriver.TryGetPeerHead(playerId, out Vector3 head) ? head : null;

        /// <summary>Build (or rebuild) one peer's placard for <paramref name="key"/>. False means
        /// nothing was built and the reason has been logged — or is a plain "this location has no
        /// preview", which is the game's own answer and not a gap.</summary>
        private static bool Rebuild(Card card, int playerId, MapLocation loc, uint key)
        {
            Destroy(card);

            UIQuestPreviewPopup? src = MapHoverVerdict.GamePreviewPopup();
            if (src == null)
            {
                if (!_warnedNoSource)
                {
                    _warnedNoSource = true;
                    VRLog.Warn(Scope, "MAP ROOM peer placards: the game's own UIQuestPreviewPopup "
                                      + "could not be found in this scene, so a peer's hover card "
                                      + "cannot be built from it. CONSEQUENCE: no foreign placards "
                                      + "at all this session — deliberately nothing rather than a "
                                      + "mod-drawn substitute, because the user's ruling is that "
                                      + "the placard must be the REAL card ('1:1 so wie es für den "
                                      + "Spieler auch aussieht'). Everything else in the map room "
                                      + "is unaffected.");
                }
                return false;
            }

            Assets.Script.GUI.Quest.IQuest? quest = QuestFor(loc);
            if (quest == null)
                return false;   // the game has no preview for this node either (HasQuestPreview)

            GameObject? holder = null;
            try
            {
                holder = new GameObject($"{HoverCardPose.PeerPlacardNamePrefix}Holder[{playerId}]");
                holder.SetActive(false);   // MUST precede the Instantiate — keeps every Awake away
                GameObject clone = Object.Instantiate(src.gameObject, holder.transform, false);
                clone.name = $"{HoverCardPose.PeerPlacardNamePrefix}[{playerId}]";
                // The source may itself be deactivated (UIWindow.ChangeActive switches a
                // zero-alpha window off), and an inactive clone would convert into a host that
                // draws nothing. Safe here: the holder is inactive, so this sets activeSelf and
                // nothing else runs.
                clone.SetActive(true);

                var popup = clone.GetComponent<UIQuestPreviewPopup>();
                if (popup != null)
                    popup.SetQuest(quest);

                int stripped = Strip(clone);
                ForceOpaque(clone);

                var rect = clone.transform as RectTransform;
                ConvertedPanel? panel = rect != null
                    ? CanvasConversion.Convert(rect, $"MapPeerPlacard{playerId}", pokeable: false,
                        fitContent: true, sortingOrder: ModalFallback.ModalHostSortingOrder,
                        useModLayer: true, flattenWindow: true)
                    : null;
                if (panel == null)
                {
                    Object.Destroy(holder);
                    return false;
                }
                if (panel.HostRaycaster != null)
                    panel.HostRaycaster.enabled = false;   // a placard is a label, never a target

                card.Holder = holder;
                card.Panel = panel;
                card.ShownKey = key;
                VRLog.Info(Scope, $"MAP ROOM peer placard built for player {playerId} on "
                                  + $"{NameOf(loc, key)} — an INSTANCE OF THE GAME'S OWN "
                                  + "UIQuestPreviewPopup, filled through its own SetQuest with the "
                                  + "same IQuest MapLocation.PreviewQuest would have built, "
                                  + $"{stripped} lifecycle component(s) stripped while it had never "
                                  + "been activated. It is the real card because it IS the real "
                                  + "card's prefab; the game's single questPreviewPopup and the "
                                  + "local player's own hover are untouched.");
                return true;
            }
            catch (System.Exception ex)
            {
                if (holder != null)
                    Object.Destroy(holder);
                card.Holder = null;
                card.Panel = null;
                card.ShownKey = 0u;
                VRLog.Warn(Scope, $"MAP ROOM peer placard for player {playerId} could not be built "
                                  + $"({ex.GetType().Name}: {ex.Message}). CONSEQUENCE: that peer's "
                                  + "hover shows nothing here this time; the settle gate tries "
                                  + "again when they next hold a node. Nothing game-owned was "
                                  + "touched — the clone lived under an inactive holder and never "
                                  + "ran a single Awake.");
                return false;
            }
        }

        /// <summary>
        /// The <c>IQuest</c> the game's own <c>MapLocation.PreviewQuest</c> would build for this
        /// node (decompiled MapLocation.cs:612-652), and null where its <c>HasQuestPreview()</c>
        /// would be false (:341-354) — i.e. the node the game itself shows no card for.
        ///
        /// <para>The two city-mode variants the game has (<c>PreviewWorldQuestFromCity</c> and the
        /// <c>HeadquartersLocation</c> holder) differ only in WHERE the manager parks the popup and
        /// in a HUD highlight it toggles as a side effect. Neither applies here: this placard's
        /// position is the icon's own anchor, and a foreign hover must not toggle a local HUD.</para>
        /// </summary>
        private static Assets.Script.GUI.Quest.IQuest? QuestFor(MapLocation loc)
        {
            try
            {
                MapRuleLibrary.MapState.CQuestState? quest = loc.LocationQuest;
                if (quest != null)
                    return new Assets.Script.GUI.Quest.Quest(quest);
                return loc.MapLocationType == MapLocation.EMapLocationType.Headquarters
                    ? new Assets.Script.GUI.Quest.HeadqueartQuest()
                    : null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// <c>DestroyImmediate</c> every component that would come alive when the conversion
        /// activates the clone — see the class doc's step 3 for why each one is on this list.
        /// Plain viewer components (Image / TMP text / layout / the enemy and reward rows) are the
        /// imagery and are kept.
        /// </summary>
        private static int Strip(GameObject clone)
        {
            int stripped = 0;
            // The popup FIRST: its Awake dereferences the UIWindow, so it may not outlive it.
            UIQuestPreviewPopup[] popups = clone.GetComponentsInChildren<UIQuestPreviewPopup>(true);
            for (int i = 0; i < popups.Length; i++)
            {
                if (popups[i] == null)
                    continue;
                Object.DestroyImmediate(popups[i]);
                stripped++;
            }
            UIFollowMapLocation[] follows = clone.GetComponentsInChildren<UIFollowMapLocation>(true);
            for (int i = 0; i < follows.Length; i++)
            {
                if (follows[i] == null)
                    continue;
                Object.DestroyImmediate(follows[i]);
                stripped++;
            }
            UnityEngine.UI.UIWindow[] windows =
                clone.GetComponentsInChildren<UnityEngine.UI.UIWindow>(true);
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] == null)
                    continue;
                Object.DestroyImmediate(windows[i]);
                stripped++;
            }
            return stripped;
        }

        /// <summary>Force the copy fully opaque and inert: it may have been taken from a popup that
        /// was hidden or mid-fade, and a <c>CanvasGroup</c> at alpha 0 converts into a host that
        /// draws nothing at all. <c>blocksRaycasts</c> off for the same reason the conversion is
        /// not pokeable — a placard is a label, and ModBuild 187 lost a whole round to a hover card
        /// that ate the very ray that was hovering its icon.</summary>
        private static void ForceOpaque(GameObject clone)
        {
            CanvasGroup[] groups = clone.GetComponentsInChildren<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] == null)
                    continue;
                groups[i].alpha = 1f;
                groups[i].interactable = false;
                groups[i].blocksRaycasts = false;
            }
            Canvas[] canvases = clone.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                // UIWindow._disableCanvas leaves a hidden window's own Canvas switched off, and
                // that state is copied with everything else.
                if (canvases[i] != null && !canvases[i].enabled)
                    canvases[i].enabled = true;
            }
        }

        /// <summary>Release the conversion (which returns the clone under its inactive holder) and
        /// destroy the holder, taking the clone with it. Destroying it touches nothing game-owned:
        /// it carries no UIWindow and no popup component, and those never ran an Awake.</summary>
        private static void Destroy(Card card)
        {
            if (card.Panel != null)
            {
                CanvasConversion.Release(card.Panel);
                card.Panel = null;
            }
            if (card.Holder != null)
            {
                Object.Destroy(card.Holder);
                card.Holder = null;
            }
            card.ShownKey = 0u;
        }
    }

    // ---- naming, for the log lines the next round has to diff --------------------------------

    /// <summary>
    /// One node, named the way both sides of the wire can be compared: the location's own localized
    /// name plus the key that travelled.
    ///
    /// <para><c>CLocationState.Location.LocalisedName</c> is a localization KEY out of the campaign
    /// YML, so it goes through the game's own translator and reads in the viewer's language, not
    /// the sender's — which is exactly why the KEY is printed beside it. Two logs from two machines
    /// in two languages are still diffable on the hex.</para>
    /// </summary>
    private static string NameOf(MapLocation? loc, uint key)
    {
        if (key == 0u)
            return "NOTHING (no selection)";
        string name = "<not live on this map>";
        if (loc != null)
        {
            try
            {
                MapRuleLibrary.MapState.CLocationState? state = loc.Location;
                string? locKey = state != null && state.Location != null
                    ? state.Location.LocalisedName
                    : null;
                name = string.IsNullOrEmpty(locKey)
                    ? (state != null ? state.ID ?? loc.name : loc.name)
                    : Loc.Game(locKey!, state!.ID ?? locKey!);
            }
            catch (System.Exception)
            {
                name = loc.name;
            }
        }
        return $"'{name}' (key 0x{key:X8})";
    }

    /// <summary>The same, for a key whose location has to be looked up first.</summary>
    private static string Describe(uint key) =>
        key == 0u
            ? "NOTHING (no selection)"
            : NameOf(TryResolveKey(key, out MapLocation? loc) ? loc : null, key);
}

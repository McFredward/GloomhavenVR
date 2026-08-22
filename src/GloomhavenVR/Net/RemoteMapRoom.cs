using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE 3D MAP ROOM ON THE WIRE — record <see cref="NetProtocol.ExtIdMapRoom"/> (20): who else is
/// standing at this table, which map surface they are showing, and which icon they are pointing at.
///
/// <para>USER REQUEST (2026-08-22, verbatim): "Multiplayer für die 3D-Map: a) Welche Map angezeigt
/// wird (Gloomhaven oder World-Map) soll synchronisiert werden. b) Welche Quest gerade angeklickt
/// ist soll synchronisiert werden. c) Die mouseover Infotafeln sollen synchronisiert werden."</para>
///
/// <para><b>NO NEW AUTHORITY IS CREATED.</b> The only thing this class ever DRIVES is one press of
/// the game's own guildmaster world/city button, through the table rail's single dispatch
/// (<c>ExecuteEvents.pointerClickHandler</c> on the real <c>Toggle</c>), so the game's whole guard
/// chain still decides — including its refusal when the city is not unlocked. It never calls
/// <c>MapLocation.Select()</c>, never calls <c>Deselect()</c>, never writes a game field and never
/// sends a game action. A peer's pick becomes a mod-drawn placard and nothing else.</para>
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
/// <para><b>HOVER PLACARDS: EVERY PEER GETS ONE — a USER RULING</b>, taken over "one placard, last
/// shown". The game owns exactly ONE <c>questPreviewPopup</c> and previews only while nothing is
/// selected, so the game's own card cannot be shown four times; these placards are therefore
/// mod-drawn world-space labels of this mod's own, built the way <see cref="RemoteNameTag"/> builds
/// a peer's head tag and carrying the same identity (<see cref="NetPlayerActors.NameFor"/> and that
/// peer's Steam picture). <b>They deliberately do NOT drive the local hover:</b> calling
/// <c>MapLocation.OnPointerEnter</c> for a peer would fight this client's own pointer, would steal
/// the single preview card from the local player, and would fire the game's mouse-enter sound once
/// per peer per icon — a clicking storm with three people sweeping beams.</para>
///
/// <para><b>THE CLUTTER CASE IS BOUNDED BY LAYOUT, NOT BY DROPPING PLACARDS.</b> Every placard
/// rides at <c>BaseLift + lane × RowPitch</c> above its own icon, where <c>lane</c> is that peer's
/// rank in the ASCENDING PLAYER-ID order of everyone currently pointing. Four players pointing at
/// ONE icon therefore stack into four rows that cannot overlap; four players pointing at four
/// adjacent icons ride at four different heights and read as four rows rather than one smear. The
/// order is the player id, so it is the same on every machine — two players describing the picture
/// describe the same picture — and with at most four players the tower is at most three remote rows
/// tall (<see cref="RowPitchMeters"/> each). Nothing is ever hidden.</para>
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
        public PeerRoom(byte flags, byte surfaceStamp, uint pickKey, float at)
        {
            Flags = flags;
            SurfaceStamp = surfaceStamp;
            PickKey = pickKey;
            At = at;
        }

        public readonly byte Flags;
        public readonly byte SurfaceStamp;
        public readonly uint PickKey;
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
        ConsumedStamp.Clear();
        SeenStamp.Clear();
        _publishedSurface = MapIconLayer.MapSurface.Unknown;
        _localSurfaceStamp = 0;
        _adoptedSurface = MapIconLayer.MapSurface.Unknown;
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
    /// staged pick changing. All three are discrete, human-paced acts whose whole purpose is to be
    /// looked at, so up to 200 ms of cadence latency between two headsets is exactly the "did that
    /// work?" this feature exists to remove.</para>
    ///
    /// <para>THE HOVER KEY DELIBERATELY DOES NOT. Sweeping the beam across a row of icons produces
    /// a new hover key at up to the rig rate; letting that pre-empt would turn a wrist flick into a
    /// packet burst. It rides the ordinary 5 Hz cadence instead — the established distinction
    /// between the pre-empting and the capped idioms.</para>
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
            return KeyOf(staged) != _sentStagedKey;
        }
    }

    /// <summary>What the last packet really carried, so <see cref="SendDue"/> can compare against
    /// it rather than against a guess. False until the first record goes out.</summary>
    private static bool _sentValid;

    /// <inheritdoc cref="_sentValid"/>
    private static uint _sentStagedKey;

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
            // stamp edge nobody made.
            _publishedSurface = MapIconLayer.MapSurface.Unknown;
            _sentValid = false;
            _sentStagedKey = 0u;
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
        MapLocation? pick = staged != null ? staged : hover;
        uint key = KeyOf(pick);

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
            if (staged != null)
                flags |= NetProtocol.MapRoomPickStagedBit;
        }

        extras.HasMapRoom = true;
        extras.MapRoomFlags = flags;
        extras.MapRoomSurfaceStamp = _localSurfaceStamp;
        extras.MapRoomPickKey = key;

        // What SendDue compares the next frame's live state against.
        _sentValid = true;
        _sentStagedKey = KeyOf(staged);
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
                                       Time.unscaledTime);
    }

    private static void Forget(int senderId)
    {
        Peers.Remove(senderId);
        ConsumedStamp.Remove(senderId);
        SeenStamp.Remove(senderId);
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
        ResolveSurface();
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

    // ---- 2b + 2c: the peers' picks, as placards ---------------------------------------------

    private static void ResolvePlacards()
    {
        RefreshCache();

        // The LANE is the peer's rank in ascending player-id order among everyone currently
        // pointing — see the class doc. Built here, once per frame, over at most three peers.
        Scratch.Clear();
        foreach (KeyValuePair<int, PeerRoom> kv in Peers)
        {
            PeerRoom s = kv.Value;
            if (s.InRoom && s.PickValid && s.PickKey != 0u)
                Scratch.Add(kv.Key);
        }
        Scratch.Sort();

        Placards.BeginFrame();
        for (int lane = 0; lane < Scratch.Count; lane++)
        {
            int playerId = Scratch[lane];
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
            MapLocationInteractor? interactor = MapRoomDriver.ActiveLocations;
            if (interactor == null || !interactor.TryAnchorFor(loc, out Vector3 anchor))
                continue;
            Placards.Show(playerId, lane, loc, anchor, s.PickStaged);
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
                                + "m_Villages). A collision can only ever label the WRONG ICON; it "
                                + "can never select anything, because no receiver here calls Select()."
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

    /// <summary>Where a lane's placard sits above its icon, real metres above the anchor. Row 0
    /// clears the icon itself; each further row clears the one below it.</summary>
    private const float BaseLiftMeters = 0.06f;

    /// <inheritdoc cref="BaseLiftMeters"/>
    private const float RowPitchMeters = 0.055f;

    /// <summary>
    /// THE PLACARDS — one mod-drawn world-space label per pointing peer, built and torn down here
    /// and owned by nothing else.
    ///
    /// <para>Shape, materials and billboard convention are <see cref="RemoteNameTag"/>'s: an unlit
    /// quad carrying that peer's Steam picture beside a TMP row, aimed +Z AWAY from the local head
    /// so its front faces the reader. Two rows: the LOCATION'S OWN localized name on top (that is
    /// the headline of the game's own info card, and it is what makes this an "Infotafel" rather
    /// than a marker), and the peer's name under it.</para>
    ///
    /// <para>NO MOD-AUTHORED WORDS AT ALL. Both strings come from data that is already localized —
    /// the location's <c>LocalisedName</c> key through the game's own translator, and the peer's
    /// username through the game's own netcode — so this feature needs no entry in
    /// <c>Core/Loc.cs</c> and cannot ship an English string to a German player. Hover versus STAGED
    /// is therefore expressed by the accent colour of the peer row, not by a word: the blue this
    /// project already uses for "this is a VR peer" (<see cref="PlayerBadges"/>'s badge) for a
    /// hover, a warm amber for a staged selection.</para>
    /// </summary>
    private static class Placards
    {
        /// <summary>Hover accent: the same #8FD8FF this project already uses to mark a modded peer
        /// in the game's own roster rows (<see cref="PlayerBadges"/>). One cue, two carriers.</summary>
        private static readonly Color HoverAccent = new(0.56f, 0.85f, 1f);

        /// <summary>Staged accent: a warm amber, far from the hover blue in BOTH hue and luminance
        /// so the two stay apart for a red/green-deficient viewer and in the desaturated periphery
        /// of a headset lens.</summary>
        private static readonly Color StagedAccent = new(0.98f, 0.78f, 0.35f);

        private const float AvatarSize = 0.055f;
        private const float RowWidth = 0.34f;
        private const float RowHeight = 0.045f;
        private const float Pad = 0.010f;

        private sealed class Card
        {
            internal GameObject? Root;
            internal TextMeshPro? Title;
            internal TextMeshPro? Who;
            internal Transform? AvatarQuad;
            internal Material? AvatarMat;
            internal Sprite? ShownAvatar;
            internal string ShownName = string.Empty;
            internal string ShownTitle = string.Empty;
            internal bool ShownStaged;
            internal bool TouchedThisFrame;
        }

        private static readonly Dictionary<int, Card> Cards = new(4);
        private static readonly List<int> Drop = new(4);

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

        internal static void Show(int playerId, int lane, MapLocation loc, Vector3 anchor,
                                  bool staged)
        {
            if (!Cards.TryGetValue(playerId, out Card? card) || card == null || card.Root == null)
            {
                card = new Card();
                Cards[playerId] = card;
                Build(card, playerId);
            }
            card.TouchedThisFrame = true;
            if (card.Root == null)
                return;

            string title = TitleOf(loc);
            string who = NetPlayerActors.NameFor(playerId) ?? string.Empty;
            if (string.IsNullOrEmpty(who))
                who = $"Player {playerId}";
            Sprite? avatar = NetPlayerActors.AvatarFor(playerId);

            // CHANGE-GATED: the strings and the picture are re-read every frame (the roster fills
            // late on join, and the Steam picture is fetched asynchronously) but only WRITTEN when
            // one of them really changed. A TMP text assignment rebuilds a mesh.
            if (title != card.ShownTitle && card.Title != null)
            {
                card.Title.text = title;
                card.ShownTitle = title;
            }
            if ((who != card.ShownName || staged != card.ShownStaged) && card.Who != null)
            {
                card.Who.text = who;
                card.Who.color = staged ? StagedAccent : HoverAccent;
                card.ShownName = who;
                card.ShownStaged = staged;
            }
            if (!ReferenceEquals(avatar, card.ShownAvatar))
            {
                ApplyAvatar(card, avatar);
                card.ShownAvatar = avatar;
            }

            // Placement. The lift is in REAL metres carried by the rig scale, exactly as the hover
            // card's own lift is — this room runs at ~198 world units per metre and mixing the two
            // has shipped as a bug here before.
            float scale = Rig.RigTarget.Current != null
                ? Mathf.Max(Rig.RigTarget.Current.lossyScale.x, 0.0001f)
                : 1f;
            float lift = (BaseLiftMeters + lane * RowPitchMeters) * scale;
            Transform t = card.Root.transform;
            t.position = new Vector3(anchor.x, anchor.y + lift, anchor.z);
            t.localScale = Vector3.one * scale;

            Camera? head = Rig.VRRigDriver.HeadCamera != null
                ? Rig.VRRigDriver.HeadCamera
                : Camera.main;
            if (head == null)
                return;
            Vector3 away = t.position - head.transform.position;
            // Flattened to the horizon: a placard read from above a table must stay upright, the
            // same convention the hover card and every name tag use.
            away.y = 0f;
            if (away.sqrMagnitude > 1e-6f)
                t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        private static void Build(Card card, int playerId)
        {
            var root = new GameObject($"GloomhavenVR.MapPickPlacard[{playerId}]");
            card.Root = root;

            var titleGo = new GameObject("Location");
            titleGo.transform.SetParent(root.transform, worldPositionStays: false);
            var title = titleGo.AddComponent<TextMeshPro>();
            title.text = string.Empty;
            title.alignment = TextAlignmentOptions.Center;
            title.color = new Color(1f, 0.95f, 0.85f);   // the OwnerTag / RemoteNameTag off-white
            title.fontStyle = FontStyles.Bold;
            TmpFit.Fit(title, RowWidth, RowHeight, maxFontSize: 0.038f, wrap: false);
            titleGo.transform.localPosition = new Vector3(0f, RowHeight * 0.6f, -0.001f);
            WorldUI.MrBacking.Label(title);
            card.Title = title;

            var whoGo = new GameObject("Who");
            whoGo.transform.SetParent(root.transform, worldPositionStays: false);
            var who = whoGo.AddComponent<TextMeshPro>();
            who.text = string.Empty;
            who.alignment = TextAlignmentOptions.Left;
            who.color = HoverAccent;
            TmpFit.Fit(who, RowWidth - AvatarSize - Pad, RowHeight, maxFontSize: 0.030f,
                       wrap: false);
            whoGo.transform.localPosition =
                new Vector3((AvatarSize + Pad) * 0.5f, -RowHeight * 0.4f, -0.001f);
            WorldUI.MrBacking.Label(who);
            card.Who = who;

            Core.VRLayers.Apply(root);
        }

        private static void ApplyAvatar(Card card, Sprite? avatar)
        {
            if (card.Root == null)
                return;
            if (card.AvatarQuad != null)
                Object.Destroy(card.AvatarQuad.gameObject);
            card.AvatarQuad = null;
            if (card.AvatarMat != null)
                Object.Destroy(card.AvatarMat);   // a material is an asset; Unity never frees it
            card.AvatarMat = null;

            Texture? tex = avatar != null ? avatar.texture : null;
            if (tex == null)
                return;
            card.AvatarMat = BoardVisual.Unlit(Color.white, tex);
            Rect r = avatar!.rect;
            card.AvatarMat.mainTextureOffset = new Vector2(r.x / tex.width, r.y / tex.height);
            card.AvatarMat.mainTextureScale = new Vector2(r.width / tex.width, r.height / tex.height);
            MeshRenderer quad = BoardVisual.Quad(card.Root.transform, "Avatar",
                new Vector2(AvatarSize, AvatarSize), card.AvatarMat);
            quad.transform.localPosition =
                new Vector3(-(RowWidth - AvatarSize) * 0.5f, -RowHeight * 0.4f, 0f);
            card.AvatarQuad = quad.transform;
            Core.VRLayers.Apply(card.Root);
        }

        /// <summary>
        /// The headline of the placard: the location's OWN localized name.
        ///
        /// <para><c>CLocationState.Location.LocalisedName</c> is a localization KEY out of the
        /// campaign YML, so it goes through the game's own translator and reads in the viewer's
        /// language, not the pointer's. Falls back to the raw id — which is at least a stable,
        /// recognisable string — rather than to an empty plate.</para>
        /// </summary>
        private static string TitleOf(MapLocation loc)
        {
            try
            {
                MapRuleLibrary.MapState.CLocationState? state = loc.Location;
                if (state == null)
                    return string.Empty;
                string? key = state.Location != null ? state.Location.LocalisedName : null;
                if (string.IsNullOrEmpty(key))
                    return state.ID ?? string.Empty;
                return Loc.Game(key!, state.ID ?? key!);
            }
            catch (System.Exception)
            {
                return string.Empty;
            }
        }

        private static void Destroy(Card card)
        {
            if (card.AvatarMat != null)
                Object.Destroy(card.AvatarMat);
            card.AvatarMat = null;
            if (card.Root != null)
                Object.Destroy(card.Root);
            card.Root = null;
        }
    }
}

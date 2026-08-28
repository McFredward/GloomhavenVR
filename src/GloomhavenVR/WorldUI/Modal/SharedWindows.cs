using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Which floated windows every player in the same room sees the SAME STATE of, and therefore which
/// ones wear the shared grab bar and have their pose driven from the wire.
///
/// <para><b>USER REQUEST (2026-08-22, verbatim):</b> "Die Fenster die für alle Spieler sichtbar
/// sind sollen eine andere Farbe beim dem Greifbalken haben (zB Blau) um anzuzeigen, dass es ein
/// Fenster ist das alle sehen. Die Position dieser Fenster sollen voll synchronsiert werden, auch
/// wenn es jemand woanders hinverschiebt."</para>
///
/// <para><b>THE DEFINITION, and it is deliberately narrow.</b> A floated window is SHARED when its
/// on-screen state is driven from the wire <i>for this client, right now</i> — i.e. when some
/// record makes another player's click or drag change what this client sees in that window. NOT
/// "this class of window is shared in principle". That is the only definition under which the blue
/// bar is a TRUE STATEMENT to the person looking at it: the bar's job is to tell this player what
/// moving it will do.</para>
///
/// <para><b>THE CONSEQUENCE FOR A PLAYER WITH THE 3D MAP OFF.</b> The map story window and the
/// quest window are synced only among players who have the 3D world map switched on — that is the
/// user's own scoping ("das soll hier nur für die Spieler gelten die die 3D-Worldmap ausgeschaltet
/// haben - aller anderen synchronsieren sich den aktuellen Stand der Story"). For a map-OFF player
/// those two windows are exactly as private as the merchant's: moving one moves nothing for anybody
/// and nobody else's drag moves theirs. So they keep the ordinary brass bar. A blue bar there would
/// be a false statement, and the first time that player moved the window and nothing happened
/// elsewhere they would report it as a defect.
/// <b>The other reading, recorded so it is not re-litigated:</b> one could argue the colour
/// describes the window CLASS. Rejected — it makes the colour un-actionable, and the same window
/// really is private for that player.</para>
///
/// <para><b>PARTICIPATION CAN FLIP WHILE A WINDOW STANDS</b> — the player toggles the 3D map mid
/// life (<c>MapRoomDriver</c> reads its config live), and since ModBuild 290 the SESSION itself can
/// come up or drop under a standing window. So a caller must re-evaluate the answer per tick and
/// change-gate what it does with it — one bool comparison per floated window per frame — rather
/// than deciding once at build time. The three BEHAVIOURS this predicate gates follow the same way
/// and by the same route: <c>GrabbableModal._shared</c> is refreshed from
/// <see cref="SharedWindows.ParticipatesHere"/> once per tick in <c>SyncSharedState</c>, and the
/// release re-face, the remote pose easing and the corner network badge all read THAT — so a window
/// that was open when the session came up starts behaving as shared without being reopened, and one
/// that was BADGED and GRABBED when the session dropped simply finishes its carry as a private
/// window (it re-faces on release, which is correct: there is no room left to disagree with).</para>
///
/// <para><b>THE SCENARIO STORY WINDOW IS SHARED FOR EVERY PLAYER IN A SESSION</b> and must never
/// acquire an opt-in gate OF ITS OWN — no 3D-map switch, no per-window preference: wire record 19
/// has none, it ships, and the user has already accepted that behaviour ("Im Szenario selber gilt
/// das Selbe für die Dialogfenster, das sollte bereits implementiert worden sein").
/// <b>ModBuild 290 — WHAT THAT SENTENCE DOES NOT SAY, and used to be read as saying.</b> It says
/// nothing about a client with no session at all. Until this build the kind answered
/// <c>true</c> unconditionally, so a SINGLE-PLAYER scenario grew a blue bar on a window whose pose
/// nobody could ever receive or publish — the exact false statement the definition above forbids.
/// USER RULING (2026-08-25, verbatim): <i>"Weiterhin bin ich im Singleplayer, ich möchte dass es
/// keine 'blauen' Fenster im Singleplayer gibt. Wechselt der Spieler von Singleplayer zum
/// Multiplayer werden diese entsprechenden betroffenen Fenster 'blau' und verhalten sich
/// entsprechend. Solange der Singleplayer aktiv ist sollen alle Fenster Singleplayer-Fenster sein
/// und sich auch entsprechend verhalten."</i> The SESSION gate in
/// <see cref="SharedWindows.ParticipatesHere"/> is that ruling and applies to every kind; the
/// "no gate" above survives as what it always meant — <b>no gate BELOW the session</b>.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT IN THE SET</b>, each for a stated reason — this list is the
/// most useful part of the file, because every entry is a mistake somebody would otherwise make:
/// <list type="bullet">
/// <item><b>The road/city event panel</b> (<c>UIEventPanel</c>) — <b>THIS ENTRY WAS WRONG AND HAS
/// MOVED INTO THE SET (ModBuild 231, <see cref="SharedWindowKind.Encounter"/>).</b> What it said
/// was: <i>"already synced by the game itself … adding a mod record for it would be a second
/// source of truth for a fact the game already owns"</i>. The FACT half is still true and is
/// re-stated on the enum member — the page advance really is
/// <c>Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent, ActionPhaseType.MapEvent, …)</c>
/// (decompiled/GH.Runtime/UIEventPanel.cs:606/610/724, received by <c>ClientContinueRoadEvent</c>
/// :869) and no mod record may ever carry its CONTENT. The CONCLUSION was wrong twice over.
/// (1) It answered a question about CONTENT with a verdict about the whole window, and the shared
/// set is about the POSE and the bar. (2) By this file's own definition — "some record makes
/// another player's click change what this client sees in that window" — the event panel is the
/// most sharply shared window in the game: it is the GAME's record that does it. Wearing the brass
/// bar there was the false statement, not the blue one. USER REQUEST (2026-08-23, verbatim): <i>"Die
/// 'Begegnung' ist ein Storyfenster und soll wie das Storyfenster auch 'blau' sein also voll
/// synchronisiert sein."</i></item>
/// <item><b>The guildmaster destination windows</b> (merchant, temple, trainer, town records) — the
/// user's own ruling for the map room: "Da jeder seine eigene UI sieht, sollen diese UI Element
/// nicht synchronisiert werden".</item>
/// <item><b>The hover card</b> — it has no grab bar and no X at all, so there is nothing to tint,
/// and its pose is owned externally every frame ([[settle-gate-vs-external-writer]]).</item>
/// <item><b>The scenario decision / results windows</b> (records 12/24/29) — those are cosmetic
/// MIRRORS of a peer's own board, not a shared object. Nobody else's drag moves your copy.</item>
/// </list></para>
///
/// <para><b>WHY THIS LIVES IN <c>WorldUI</c> AND NOT IN <c>Net</c>.</b> The window chrome must not
/// take a dependency on the net module. The dependency direction that already exists is
/// <c>Net → WorldUI</c> (<c>RemoteStorySync</c> calls into <see cref="ModalFallback"/>), and this
/// keeps it pointing that way.</para>
/// </summary>
internal enum SharedWindowKind : byte
{
    /// <summary>Not a shared window.</summary>
    None = 0,

    /// <summary>The scenario's story/dialog box — <c>StoryController.window</c>. Wire record 19,
    /// shipped, and untouched by the map-room work.</summary>
    ScenarioStory = 1,

    /// <summary>The campaign map's story box — <c>MapStoryController.window</c>. A DIFFERENT
    /// controller from the scenario's, which is why record 19 is inert on the map: it resolves only
    /// <c>Singleton&lt;StoryController&gt;</c>.
    ///
    /// <para><b>ModBuild 237 — THIS KIND FOLLOWS THE COMPOSED HOST WHILE THE COMPOSITE STANDS.</b>
    /// <see cref="SharedWindowIdentity"/> holds the answer and this file reads it; that class carries
    /// the whole design and the user request it comes from. In one sentence: while
    /// <c>StoryComposite</c>'s claim is standing, the story window's own content is inside a mod-owned
    /// dock inside a floated <c>UILoadoutManager</c> window, so THAT is the window the story is being
    /// told in, and it is the one that wears the blue bar, has its pose published and applied, and is
    /// exempt from the release re-face. When the claim lapses the identity hands back and the host is
    /// an ordinary private window again.</para>
    ///
    /// <para><b>ModBuild 236 REFUSED THAT, AND THE THREE REASONS IT GAVE ARE KEPT HERE BECAUSE TWO OF
    /// THEM ARE STILL LIVE CONSTRAINTS RATHER THAN OBJECTIONS.</b></para>
    /// <list type="number">
    /// <item><b><c>Net/RemoteMapStory</c> kept its move baseline PER KIND, not per window
    /// instance.</b> <c>TrackFrame</c> caches <c>FramePos</c>/<c>FrameRot</c>/<c>FrameSize</c> of the
    /// grab frame <see cref="SharedWindows.TryGetGrab"/> hands it and calls any change a MOVE, so
    /// swapping the underlying transform under a live baseline read as a drag: it set <c>Moving</c>,
    /// published a pose and could elect this client the room's LAST MOVER — pushing the loadout
    /// screen's pose onto every peer's story box. FIXED IN THAT FILE: the baseline now records the
    /// GRAB FRAME it was taken from and a change of grab frame is treated exactly as a reset — and
    /// it also forgets the pose OWNERSHIP, which the original one-line fix did not and which is the
    /// half that would have kept this client elected as last mover under a stale stamp.</item>
    /// <item><b>The composed host is the loadout screen, whose CONTENT is private by an existing user
    /// ruling</b> ("Da jeder seine eigene UI sieht, sollen diese UI Element nicht synchronisiert
    /// werden"): each player picks his own loadout and his own battle goals on it. STILL TRUE, AND
    /// STILL ENFORCED — nothing of the host's content goes on the wire. Record 21 reads the page, the
    /// page count and the dialog hash off <c>MapStoryController.dialogBox</c> and nothing else off
    /// anything, and the identity is handed back the moment the story stops being told, i.e. BEFORE
    /// the battle-goal phase. Only the POSE follows the host.</item>
    /// <item><b>The kind would flip under a live grab</b> when the composite stands down, changing the
    /// release re-face gate and <c>PanelPoseWatch</c>'s <c>peerOwned</c> flag mid-carry. STILL TRUE,
    /// AND IT IS WHY THE SWAP IS DEFERRED: <see cref="SharedWindowIdentity"/> refuses to move the kind
    /// while a hand is on either bar, with no timeout.</item>
    /// </list>
    /// <para><b>THE CONTENT HALF WAS NEVER AFFECTED BY ANY OF THIS.</b>
    /// <c>Net/RemoteMapStory</c> resolves the story PAGE through <c>MapStoryController.dialogBox</c>
    /// directly — MapBox() at RemoteMapStory.cs:379-389, read on the send path at :512/:525 and
    /// driven by ResolveStoryPage at :1194 through the game's own ShowLine at :1264 — and never
    /// through this class, so page, text and
    /// the finished bit kept syncing normally through ModBuild 236 while the dialog was parked —
    /// parking changes a transform's PARENT, not the singleton the controller is reached
    /// through.</para></summary>
    MapStory = 2,

    /// <summary>The quest-confirmation popup, <c>UIWindowID.QuestPopup</c>.</summary>
    QuestConfirm = 3,

    /// <summary>
    /// The road/city ENCOUNTER — "Begegnung" — <c>UIEventPanel</c>, the game window
    /// <c>'UI Event Window'</c> (<c>UIWindowID.EventsPanel</c> in the ModBuild 231 hardware log).
    ///
    /// <para><b>WHAT IT IS, ESTABLISHED FROM THE GAME AND NOT FROM THE GERMAN WORD.</b> "Begegnung"
    /// is the localisation of ENCOUNTER, and the game has exactly one encounter window:
    /// <c>UICityEncounterButton.OpenCityEvent</c> → <c>UIGuildmasterHUD.OpenCityEncounter</c> →
    /// <c>MapChoreographer.OpenCityEvent</c> (decompiled/GH.Runtime/UICityEncounterButton.cs:52,
    /// UIGuildmasterHUD.cs:687), which raises <c>Singleton&lt;UIEventPanel&gt;</c> — a window whose
    /// own serialized fields are <c>eventImage</c>, <c>eventTitle</c>, <c>eventDescription</c> and
    /// two headers literally named <c>[Header("City Encounter")]</c> / <c>[Header("Road
    /// Encounter")]</c>, with <c>showAudioItem = "PlaySound_UIMapEncounter"</c>
    /// (decompiled/GH.Runtime/UIEventPanel.cs:34-72). It is NOT a
    /// <see cref="MapStoryController"/> window: a tree-wide grep of the decompiled sources finds
    /// fourteen callers of <c>Singleton&lt;MapStoryController&gt;.Instance.Show</c> and
    /// <c>UIEventPanel</c> is not one of them.</para>
    ///
    /// <para><b>POSE ONLY, LIKE <see cref="QuestConfirm"/>, AND FOR A STRONGER REASON.</b> Its
    /// content is not merely "probably" synced — the page advance IS a game action:
    /// <c>Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent, ActionPhaseType.MapEvent,
    /// …)</c> (UIEventPanel.cs:606/610/724) received by <c>ClientContinueRoadEvent</c> (:869). So the
    /// blue bar is already a TRUE statement about this window's content before the mod does
    /// anything, and the only thing missing was the half the mod owns: WHERE the panel stands. No
    /// mod code may drive its buttons and its record carries
    /// <c>NetProtocol.StoryPageNone</c>.</para>
    ///
    /// <para><b>WHY IT NEEDED A WIRE KIND OF ITS OWN</b> rather than riding kind 1: kind 1 resolves
    /// <c>Singleton&lt;MapStoryController&gt;</c>, and the record's whole addressing is the kind
    /// byte. A pose published under kind 1 would be applied to the map story box.</para>
    /// </summary>
    Encounter = 4,
}

/// <summary>See <see cref="SharedWindowKind"/> for the whole design; this is the accessor both the
/// grab-bar colour and the pose sync call.</summary>
internal static class SharedWindows
{
    // THE BLUE BAR TINT USED TO LIVE HERE, and it is gone rather than left standing with no reader.
    // It was `internal static Color BarTint { get; } = new Color(0.24f, 0.47f, 0.78f)`, and it had
    // exactly one reader in the whole codebase: the line in GrabbableModal that chose between it and
    // a private brass. The user's ruling (2026-08-28) replaced the coloured bar outright — "Mach
    // stattdessen rechts oben in der Ecke ein kleines (nicht aufdringliches) Netzwerksymbol in das
    // Fenster" — so a shared window's rod must now look EXACTLY like a private one, and the way to
    // guarantee that is for there to be no colour left to apply. What this file still owns is the
    // PREDICATE (KindOf / ParticipatesHere); what a caller does with the answer moved to the badge.

    /// <summary>
    /// What kind of shared window this is, INDEPENDENT of whether this client currently
    /// participates in its sync.
    ///
    /// <para>A pure lookup against two singletons, one static field and one <c>UIWindowID</c>: it
    /// never converts, places or releases anything, so the predicate cannot change which windows
    /// float or when. Both story windows are identified by INSTANCE COMPARE against their singleton
    /// and not by id, because the id is scene-serialized and the enum has no Story member at all —
    /// the singleton IS the identity (decompiled StoryController.cs:65-66,
    /// MapStoryController.cs:42).</para>
    ///
    /// <para><b>ModBuild 237 — <see cref="SharedWindowKind.MapStory"/> MAY RESOLVE TO A DIFFERENT
    /// WINDOW, AND EXACTLY ONE WINDOW CARRIES IT AT A TIME.</b> While
    /// <see cref="SharedWindowIdentity.MapStoryHost"/> names a composed host, that host answers
    /// <c>MapStory</c> and the story box answers <c>None</c> — two windows wearing the blue bar for
    /// one kind would make the pose sync's own addressing ambiguous, and the bar would be a false
    /// statement on whichever of them nobody is syncing. The host test is FIRST because it is one
    /// field read plus one <c>ReferenceEquals</c>, and because it is the common case for the whole
    /// length of a quest intro; when no composite stands it costs one reference-null test on the way
    /// to the unchanged singleton compare. It is never a hierarchy walk and never a
    /// <c>GetComponentInParent</c> — this predicate is asked per floated window per frame by the bar
    /// tint, and [[containment-is-not-identity]] besides.</para>
    /// </summary>
    internal static SharedWindowKind KindOf(UIWindow? window)
    {
        if (window == null)
            return SharedWindowKind.None;

        if (Singleton<StoryController>.IsInitialized)
        {
            StoryController sc = Singleton<StoryController>.Instance;
            if (sc != null && ReferenceEquals(sc.window, window))
                return SharedWindowKind.ScenarioStory;
        }

        UIWindow? composed = SharedWindowIdentity.MapStoryHost;
        if (composed != null)
        {
            // The composite is standing: the host owns the kind and the story box does not. Falling
            // through for the story box is deliberate — it is refused from the float set while the
            // claim stands, so it has no bar to tint and no pose to publish, and answering MapStory
            // for it would point RemoteMapStory's grab lookup at a window that is not on screen.
            if (ReferenceEquals(composed, window))
                return SharedWindowKind.MapStory;
        }
        else if (Singleton<MapStoryController>.IsInitialized)
        {
            MapStoryController mc = Singleton<MapStoryController>.Instance;
            if (mc != null && ReferenceEquals(mc.window, window))
                return SharedWindowKind.MapStory;
        }

        // THE ENCOUNTER IS AN INSTANCE COMPARE against its own singleton, exactly like the two
        // story boxes above and for the same two reasons. (1) IDENTITY: UIEventPanel is a
        // Singleton<UIEventPanel> carrying [RequireComponent(typeof(UIWindow))] (decompiled
        // UIEventPanel.cs:26-27), so "the UIWindow on the singleton's GameObject" IS the encounter
        // window by construction — no name (the game localises its UI and Unity appends "(Clone)")
        // and no id (this one IS serialized as EventsPanel in the ModBuild 231 log, but a
        // scene-serialized id has burned this project before). (2) COST: this predicate is asked per
        // floated window per frame by the bar tint, and a reference compare against a singleton is
        // cheaper than a GetComponent on every window that is not the encounter.
        if (Singleton<UIEventPanel>.IsInitialized)
        {
            UIEventPanel ep = Singleton<UIEventPanel>.Instance;
            if (ep != null && ReferenceEquals(ep.gameObject, window.gameObject))
                return SharedWindowKind.Encounter;
        }

        return window.ID == UIWindowID.QuestPopup ? SharedWindowKind.QuestConfirm : SharedWindowKind.None;
    }

    /// <summary>
    /// Is THIS client a participant in that kind's sync right now?
    ///
    /// <para><b>THE SESSION GATE COMES FIRST AND IT COVERS EVERY KIND (ModBuild 290).</b> With no
    /// networked session there is no room, no peer, no record and nothing to participate IN, so no
    /// window is shared — see <see cref="SessionIsOnline"/> for the predicate, the reading of
    /// "singleplayer" it commits to, and the falsifier it prints on the transition.</para>
    ///
    /// <para>Under it, both map-phase kinds require the 3D map room to be standing, which is the
    /// user's scoping read literally. <see cref="SharedWindowKind.ScenarioStory"/> has no gate
    /// BELOW the session and must never grow one: the scenario dialog is shared for every player in
    /// a session, unconditionally, and the map-room switch has nothing to say about it.</para>
    /// </summary>
    internal static bool ParticipatesHere(SharedWindowKind kind)
    {
        if (kind == SharedWindowKind.None)
            return false;               // cheapest first: the answer for almost every window asked
        if (!SessionIsOnline())
            return false;               // singleplayer: every window is a private window
        return kind switch
        {
            SharedWindowKind.ScenarioStory => true,
            // THE ENCOUNTER JOINS THE MAP-PHASE GATE, not the scenario one, and that is read from
            // the game rather than assumed: a road/city event is raised from MapChoreographer on
            // the campaign map and its record travels in the map room's parchment frame like the
            // other two. A player with the 3D map switched off gets no pose from anybody and
            // publishes none — the same scoping the user set for kinds 1 and 2 ("das soll hier nur
            // für die Spieler gelten die die 3D-Worldmap ausgeschaltet haben"). Note that this gate
            // is about the POSE only: that player's encounter CONTENT is still synced, by the game,
            // exactly as it always was.
            SharedWindowKind.MapStory or SharedWindowKind.QuestConfirm or SharedWindowKind.Encounter
                => MapRoomDriver.Active,
            _ => false,
        };
    }

    /// <summary>The last answer <see cref="SessionIsOnline"/> gave, so the falsifier prints on the
    /// EDGE and not per frame. −1 = never asked, which is why it is an int and not a bool: the very
    /// first answer is worth a line whichever way it goes.</summary>
    private static int _lastOnline = -1;

    /// <summary>
    /// IS A NETWORKED SESSION LIVE ON THIS CLIENT RIGHT NOW?
    ///
    /// <para><b>THE SOURCE OF TRUTH IS THE GAME'S OWN, NOT A SECOND ONE.</b>
    /// <c>FFSNetwork.IsOnline</c> is <c>BoltNetwork.IsRunning &amp;&amp; !IsShuttingDown</c>
    /// (decompiled FFSNetwork.cs:25-33) and it is what the rest of this mod already asks —
    /// <c>Net.RevealGate</c>, <c>Net.InitiativeHoverSampler</c>, <c>Board.EnemyInfoPhaseSkip</c> and
    /// <see cref="WristHud"/> all read it directly and unguarded, and <c>VROptionsTab.Cheats</c>
    /// reads it behind a fail-closed try/catch. Deliberately NOT the mod's own
    /// <c>Net.INetTransport.IsOnline</c>: that one resolves the same property through cached
    /// REFLECTION on every call, and this predicate is asked per floated window per frame. It is
    /// also what keeps <c>WorldUI</c> from taking a dependency on <c>Net</c>, which is the rule
    /// stated at the top of this file — <c>FFSNetwork</c> is a GAME type, like the two story
    /// singletons above it.</para>
    ///
    /// <para><b>THE READING OF "SINGLEPLAYER" THIS COMMITS TO, and it is a choice.</b> Singleplayer
    /// means NOT CONNECTED TO A SESSION — not "connected but currently alone". Two reasons.
    /// (1) The user's own words are a session-state change ("Wechselt der Spieler von Singleplayer
    /// zum Multiplayer"), and Bolt running is exactly that change. (2) The alternative fails on its
    /// own terms: while hosting alone this client's records really are being published, and the
    /// moment somebody joins mid-scenario the bar would have to be right ALREADY — a bar that turns
    /// blue on a stranger's join is a second, later surprise, and the pose he receives would be
    /// against a window this client had been re-facing on release. The one-player online session is
    /// therefore MULTIPLAYER by this predicate, and the log line below prints the player count so a
    /// single hardware session settles it if the user disagrees, with no code change needed.</para>
    ///
    /// <para><b>COST.</b> One static property read resolving to a static bool, i.e. the same order
    /// as the <c>Singleton&lt;T&gt;.IsInitialized</c> test it joins in
    /// <see cref="KindOf"/>. Unguarded on purpose: if <c>FFSNetwork</c> could throw here it would
    /// already be throwing in <c>RevealGate</c>, which runs on the card path every frame
    /// ([[grep-for-throws-first]] — a swallowed per-frame throw is worse than a loud one).</para>
    /// </summary>
    internal static bool SessionIsOnline()
    {
        bool online = FFSNetwork.IsOnline;
        int now = online ? 1 : 0;
        if (now == _lastOnline)
            return online;              // the common case: one int compare, no allocation, no log

        _lastOnline = now;
        VRLog.Info("WorldUI",
            $"SHARED WINDOW SESSION GATE: this client is now {(online ? "ONLINE" : "OFFLINE")} "
            + $"(FFSNetwork.IsOnline={online}, IsHost={FFSNetwork.IsHost}, "
            + $"IsClient={FFSNetwork.IsClient}, players in the registry="
            + $"{PlayerCountForLog()}). "
            + (online
                ? "Every SHARED KIND may now participate, so the affected windows turn BLUE on the "
                  + "next tick and start behaving as shared — no release re-face, no facing dial, "
                  + "pose published and applied. Nothing was reopened and nothing was restarted: "
                  + "the bar tint, the re-face gate and the pose easing all read this predicate "
                  + "once per tick through GrabbableModal._shared, so a window standing open when "
                  + "the session came up changes behaviour in place."
                : "NO WINDOW IS SHARED. Every floated window is a private window and behaves like "
                  + "one — brass bar, release re-face under [WorldUI] WindowFacing, and the "
                  + "ordinary head-relative spawn placement instead of the shared board anchor. "
                  + "This is the singleplayer ruling of 2026-08-25 and NOT a fault.")
            + " READ THE PLAYER COUNT IF THE QUESTION IS 'connected but alone': this gate counts a "
            + "one-player session as MULTIPLAYER on purpose (see SessionIsOnline), and a count of 1 "
            + "beside ONLINE is that case observed rather than assumed.");
        return online;
    }

    /// <summary>How many players the game's registry holds, for the transition line only. Never on
    /// the hot path: it is read on the session EDGE, which happens twice per session.</summary>
    private static string PlayerCountForLog()
    {
        try
        {
            return FFSNet.PlayerRegistry.AllPlayers != null
                ? FFSNet.PlayerRegistry.AllPlayers.Count.ToString()
                : "no registry";
        }
        catch (System.Exception e)
        {
            return $"unreadable ({e.GetType().Name})";
        }
    }

    /// <summary>THE PREDICATE every lane calls: is this window shared FOR THIS CLIENT right now?
    ///
    /// <para><b>THE THIRD LANE (2026-08-22, user request 7b, verbatim):</b> "Da es ein Fenster für
    /// alle ist, sollen diese Fenster nach dem Greifen auch nicht die Orientierung nach dem Spieler
    /// ändern, wie es die anderen Fenster tun." A private window yaws to face its owner when they
    /// let go (<c>GrabbableModal.OnGrabFinished</c>). On a window that belongs to EVERYBODY that
    /// same yaw is a defect: it turns the window away from everyone else, and — worse — it happens
    /// on the SENDER too, so the pose that was supposed to be 1:1 is silently corrected on one
    /// client after it was published. So this predicate also gates the release re-face, on every
    /// client, and that gate is NOT configurable (request 8's three modes are for LOCAL windows
    /// only: "Remote-Fenster (blau) sollen das gar nicht haben").
    /// <b>IN SINGLEPLAYER THIS PREDICATE IS FALSE FOR EVERY WINDOW</b> (ModBuild 290,
    /// <see cref="SessionIsOnline"/>), so the release re-face runs and request 8's three modes apply
    /// to the story box exactly as they do to the merchant's — "sollen alle Fenster
    /// Singleplayer-Fenster sein und sich auch entsprechend verhalten", which is the same sentence
    /// read from the behaviour side.</para>
    ///
    /// <para><b>WHAT IS DELIBERATELY NOT GATED BY IT: the SPAWN facing</b>
    /// (<c>PanelPlacement.Spawn</c> / <c>ClampIntoView</c>, logged as "one-shot facing applied" by
    /// <c>ModalFallback.8.Convert</c>). A freshly opened shared window has no agreed pose at all —
    /// record 21 carries a pose block only once somebody has MOVED the window — so each client
    /// places its own copy in its own view, exactly as it always has. Suppressing the spawn facing
    /// would leave the window at whatever rotation the host happened to be built with, on every
    /// client, which is worse for everyone and agrees with nobody. The full argument, and the
    /// re-face gate this one is not, is written at those two call sites.</para>
    /// </summary>
    internal static bool IsShared(UIWindow? window)
    {
        // THE SESSION FIRST, and it is a reorder rather than a new gate: ParticipatesHere asks the
        // same question and would refuse anyway. Asking it here means a SINGLE-PLAYER client does
        // not run KindOf's singleton compares once per floated window per frame for an answer that
        // is already decided. Identical result, strictly less work.
        if (!SessionIsOnline())
            return false;
        SharedWindowKind kind = KindOf(window);
        return kind != SharedWindowKind.None && ParticipatesHere(kind);
    }

    /// <summary>
    /// IS A HAND ON A SHARED WINDOW'S GRAB BAR ON THIS CLIENT RIGHT NOW?
    ///
    /// <para><b>WHY THIS EXISTS: user request 7 (2026-08-22, verbatim)</b> — "Die Bewegungen der
    /// 'blauen' MP-Fenster, die 1:1 synchronisiert werden sollen, sollen auch die Bewegung und die
    /// Position voll übertragen (flüssig, wie bei der Position des Boards auch)". The board's
    /// smoothness is TWO mechanisms, not one, and this is the SENDER half of it: while the owner is
    /// dragging their board, <c>NetAvatarDriver.TickExtrasSend</c> raises the whole extras packet to
    /// the RIG rate (<c>NetProtocol.SendRateHz</c> = 15 Hz) instead of the idle
    /// <c>ExtrasSendRateHz</c> = 5 Hz, so the receiver's easing gets the same sample density the
    /// head and hands already get ("Bewegen kommt nicht flüssig an", defect 7 of the 1:1-parity
    /// round — see the boardMoving/poseDue pair there and the note in
    /// <c>Net.RemoteControlBoard</c>). A shared window that is being carried is the same kind of
    /// motion and now rides the same cadence.</para>
    ///
    /// <para><b>WHY "GRABBED" AND NOT "THE POSE CHANGED".</b> The board's own test compares the
    /// sampled pose against the last SENT one, because the board has exactly one pose and the sender
    /// already holds it. There is no such single quantity here — three window kinds, each of which
    /// may be absent — and a per-kind last-sent cache in this class would be a second copy of state
    /// <c>Net.RemoteMapStory</c> / <c>Net.RemoteStorySync</c> already keep. A hand on the bar is a
    /// strict SUPERSET of the interval the pose changes in, it cannot false-positive on a standing
    /// window (nothing else touches the bar), and it costs the same. The cadence therefore rises
    /// exactly while somebody is dragging and falls back the moment they let go.</para>
    ///
    /// <para><b>COST ON A CLIENT WITH NOTHING SHARED OPEN:</b> since ModBuild 290, on a
    /// SINGLE-PLAYER client it is four <see cref="SessionIsOnline"/> reads and nothing else — every
    /// kind is refused before any window is looked for. In a session it is one
    /// <c>Singleton.IsInitialized</c> test for the scenario story box, and for the two map kinds not
    /// even that — they are behind <c>MapRoomDriver.Active</c>, which is false for every scenario
    /// session and for every player with the 3D map switched off.</para>
    /// </summary>
    internal static bool AnyGrabbedHere() =>
        GrabbedHere(SharedWindowKind.ScenarioStory)
        || GrabbedHere(SharedWindowKind.MapStory)
        || GrabbedHere(SharedWindowKind.QuestConfirm)
        || GrabbedHere(SharedWindowKind.Encounter);

    /// <summary>One kind's answer for <see cref="AnyGrabbedHere"/> — participation first, so a
    /// non-participating client never even looks for the window.</summary>
    private static bool GrabbedHere(SharedWindowKind kind) =>
        ParticipatesHere(kind)
        && TryGetGrab(kind, out GrabbableModal? grab)
        && grab != null
        && grab.IsGrabbed;

    /// <summary>The game window behind a kind, or null when that controller is not up.</summary>
    internal static UIWindow? WindowOf(SharedWindowKind kind)
    {
        switch (kind)
        {
            case SharedWindowKind.ScenarioStory:
                if (!Singleton<StoryController>.IsInitialized)
                    return null;
                StoryController sc = Singleton<StoryController>.Instance;
                return sc != null ? sc.window : null;

            case SharedWindowKind.MapStory:
                // ModBuild 237 — THE COMPOSED HOST FIRST, and it must be the same answer
                // KindOf gives or the bar and the pose would address different windows. The Unity
                // null test is what drops a host destroyed between two ticks of
                // SharedWindowIdentity.TickMapStory; the fall-through is then the ordinary answer.
                UIWindow? composed = SharedWindowIdentity.MapStoryHost;
                if (composed != null)
                    return composed;
                if (!Singleton<MapStoryController>.IsInitialized)
                    return null;
                MapStoryController mc = Singleton<MapStoryController>.Instance;
                return mc != null ? mc.window : null;

            case SharedWindowKind.Encounter:
                // THE ENCOUNTER HAS A SINGLETON TOO, so it is resolved the same way the two story
                // boxes are and never by id: UIEventPanel is a Singleton<UIEventPanel> carrying
                // [RequireComponent(typeof(UIWindow))] (decompiled UIEventPanel.cs:26-27), so the
                // UIWindow on its own GameObject IS its window by construction. Its own private
                // `myWindow` field is that same GetComponent, cached in Awake (:133).
                if (!Singleton<UIEventPanel>.IsInitialized)
                    return null;
                UIEventPanel ep = Singleton<UIEventPanel>.Instance;
                return ep != null ? ep.GetComponent<UIWindow>() : null;

            default:
                return null;   // QuestConfirm is found by id on the float list, not by a singleton
        }
    }

    /// <summary>
    /// The mod-owned grab frame of the floated window of that kind.
    ///
    /// <para>Returns false when no such window is open, when it is not converted (it fell back to
    /// the flat screen), or when it is still behind the reveal gate. Every one of those means
    /// "this client has no grabbable window of that kind" — NEVER "this client cannot take part in
    /// the sync". The page/advance path must not consult this, and a client that cannot place a
    /// window simply keeps its own placement while remaining a full participant.</para>
    /// </summary>
    internal static bool TryGetGrab(SharedWindowKind kind, out GrabbableModal? grab)
    {
        if (kind == SharedWindowKind.QuestConfirm)
            return ModalFallback.TryGetGrabById(UIWindowID.QuestPopup, out grab);

        return ModalFallback.TryGetGrabFor(WindowOf(kind), out grab);
    }
}

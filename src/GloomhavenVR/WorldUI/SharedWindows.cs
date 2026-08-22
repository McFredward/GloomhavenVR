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
/// <para><b>PARTICIPATION CAN FLIP WHILE A WINDOW STANDS</b> (the player toggles the 3D map mid
/// life; <c>MapRoomDriver</c> reads its config live). So a caller must re-evaluate the tint per
/// tick and change-gate the write — one <see cref="Color"/> comparison per floated window per
/// frame — rather than deciding once at build time.</para>
///
/// <para><b>THE SCENARIO STORY WINDOW IS SHARED FOR EVERYBODY</b> and must never acquire an opt-in
/// gate: wire record 19 has none, it ships, and the user has already accepted that behaviour ("Im
/// Szenario selber gilt das Selbe für die Dialogfenster, das sollte bereits implementiert worden
/// sein").</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT IN THE SET</b>, each for a stated reason — this list is the
/// most useful part of the file, because every entry is a mistake somebody would otherwise make:
/// <list type="bullet">
/// <item><b>The road/city event panel</b> (<c>UIEventPanel</c>) — <b>already synced by the game
/// itself</b>: <c>Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent,
/// ActionPhaseType.MapEvent, …)</c> (decompiled UIEventPanel.cs:606/610/724), received by
/// <c>ClientContinueRoadEvent</c> (:869). It LOOKS like a story window and is not one: its page
/// advance is a GAME ACTION. Adding a mod record for it would be a second source of truth for a
/// fact the game already owns, which this project forbids.</item>
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
    /// <c>Singleton&lt;StoryController&gt;</c>.</summary>
    MapStory = 2,

    /// <summary>The quest-confirmation popup, <c>UIWindowID.QuestPopup</c>.</summary>
    QuestConfirm = 3,
}

/// <summary>See <see cref="SharedWindowKind"/> for the whole design; this is the accessor both the
/// grab-bar colour and the pose sync call.</summary>
internal static class SharedWindows
{
    /// <summary>
    /// The grab-bar tint a SHARED window's bar carries. Blue, because the user named blue ("zB
    /// Blau") and because the private bar is a warm brass — the two are far apart in hue AND in
    /// luminance, so they stay distinguishable for a red/green-deficient viewer and in the
    /// desaturated periphery of a headset lens.
    /// </summary>
    internal static Color BarTint { get; } = new Color(0.24f, 0.47f, 0.78f);

    /// <summary>
    /// What kind of shared window this is, INDEPENDENT of whether this client currently
    /// participates in its sync.
    ///
    /// <para>A pure lookup against two singletons and one <c>UIWindowID</c>: it never converts,
    /// places or releases anything, so the predicate cannot change which windows float or when.
    /// Both story windows are identified by INSTANCE COMPARE against their singleton and not by id,
    /// because the id is scene-serialized and the enum has no Story member at all — the singleton
    /// IS the identity (decompiled StoryController.cs:65-66, MapStoryController.cs:42).</para>
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

        if (Singleton<MapStoryController>.IsInitialized)
        {
            MapStoryController mc = Singleton<MapStoryController>.Instance;
            if (mc != null && ReferenceEquals(mc.window, window))
                return SharedWindowKind.MapStory;
        }

        return window.ID == UIWindowID.QuestPopup ? SharedWindowKind.QuestConfirm : SharedWindowKind.None;
    }

    /// <summary>
    /// Is THIS client a participant in that kind's sync right now?
    ///
    /// <para>Both map-phase kinds require the 3D map room to be standing, which is the user's
    /// scoping read literally. <see cref="SharedWindowKind.ScenarioStory"/> has no gate and must
    /// never grow one.</para>
    /// </summary>
    internal static bool ParticipatesHere(SharedWindowKind kind) => kind switch
    {
        SharedWindowKind.ScenarioStory => true,
        SharedWindowKind.MapStory or SharedWindowKind.QuestConfirm => MapRoomDriver.Active,
        _ => false,
    };

    /// <summary>THE PREDICATE both lanes call: is this window shared FOR THIS CLIENT right now?
    /// </summary>
    internal static bool IsShared(UIWindow? window)
    {
        SharedWindowKind kind = KindOf(window);
        return kind != SharedWindowKind.None && ParticipatesHere(kind);
    }

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
                if (!Singleton<MapStoryController>.IsInitialized)
                    return null;
                MapStoryController mc = Singleton<MapStoryController>.Instance;
                return mc != null ? mc.window : null;

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

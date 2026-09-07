// =============================================================================================
// REGISTRATION: already done — Plugin.RegisterModules() contains "_modules.Add(new
// Net.NetModule());" (since cac5474), between the WorldUI and Compat modules.
//
// This header used to carry instructions to add that line "in a SEPARATE change". It stayed
// after the line landed, so it read as a to-do to exactly the audience most likely to act on
// it, and acting on it would have registered the module TWICE. Do not restore it.
//
// The one real constraint it recorded, kept: NetModule must be registered AFTER RigModule and
// HandsModule, because it reads VRRigDriver.HeadCamera and VRHands. Anywhere after HandsModule
// is fine; nothing depends on its position relative to Compat or Dev.
// =============================================================================================

using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Multiplayer VR embodiment module: broadcasts the local head + hands over the game's own
/// Photon-Bolt side-channel and renders remote VR players as floating head "mask" + hands
/// (Demeo style). Cosmetic only — never mutates authoritative game state, and every path is a
/// no-op in single-player / offline / netcode-absent / non-modded-peer scenarios.
///
/// Lifecycle mirrors the other <see cref="IVRModule"/>s (CoreModule / HandsModule): a single
/// persistent DontDestroyOnLoad driver GameObject created in <see cref="Init"/> and destroyed
/// in <see cref="Shutdown"/>; the Harmony receive hook rides the shared
/// <see cref="VRSession.Harmony"/> instance (removed wholesale by Plugin.OnDestroy's
/// UnpatchSelf), so hot-reload stays clean.
///
/// Registered in <c>Plugin.RegisterModules()</c>. The whole subsystem sits behind the
/// <see cref="Enabled"/> kill-switch: with it off, <see cref="Init"/> binds the config and
/// returns before the transport or the driver GameObject exist, so no Harmony hook is installed
/// and nothing is sent or rendered.
/// </summary>
internal sealed class NetModule : IVRModule
{
    public string Name => "Net";

    /// <summary>
    /// Kill-switch for the whole embodiment sync (default ON). This is brand-new networking
    /// that installs a Harmony hook into the game's Bolt receive path; every path is
    /// cosmetic + desync-safe by design, but the toggle lets the user disable it entirely if
    /// a multiplayer session ever misbehaves — no rebuild needed (dev.gloomhavenvr.net.cfg).
    /// </summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>
    /// Which of the three head "masks" the LOCAL player wears (0..2). Stamped onto every outgoing
    /// rig packet so other VR players render the right mask, and read by the local
    /// <see cref="WorldUI.AvatarMirror"/> preview. Bound independently of <see cref="Enabled"/> so
    /// the mask picker + mirror work even with the networking hook turned off.
    /// </summary>
    /// <summary>
    /// How long this client waits, in seconds, for its local phase to catch up with an incoming
    /// action before the GAME declares a desynchronisation and ends the session. The game's own
    /// value is 5 s (<c>NetworkManager.incorrectActionDetectedTimeOutDuration</c>); this writes
    /// <c>ActionProcessor.MaxConsecutiveIncorrectActionsAllowed</c> to match the number set here.
    ///
    /// <para>STRICTLY LOCAL and cannot corrupt anything: each client counts its own retries, and
    /// an action is still only ever executed once its phase matches. Being patient cannot make a
    /// peer desynchronise, cannot apply anything early and cannot apply anything twice. Set to 5
    /// to leave the game's behaviour exactly as shipped. See
    /// <c>.planning/multiplayer/DESYNC-ANALYSIS.md</c> §3 and R2.</para>
    /// </summary>
    internal static ConfigEntry<float> DesyncPatienceSeconds = null!;

    internal static ConfigEntry<int> MaskId = null!;

    /// <summary>
    /// Uniform visual SIZE of that head mask (1 = authored size — today's look, so nothing changes
    /// until it is tuned). Lives next to <see cref="MaskId"/> because it is the same cosmetic
    /// choice: the mask a player wears. Applies LIVE (the head visual's scale is re-read every
    /// tick by <see cref="RemoteAvatar"/> and <see cref="WorldUI.AvatarMirror"/> — no rebuild, no
    /// restart) and it is SYNCHRONIZED: peers render your mask at the size YOU picked, the same
    /// contract as the mask style, the hand style and the ghost-hand strength. Range mirrors the
    /// wire's quantization window (<see cref="NetProtocol.MaskSizeMin"/>..
    /// <see cref="NetProtocol.MaskSizeMax"/>) so a config value can never be clipped in transit.
    /// </summary>
    internal static ConfigEntry<float> MaskSize = null!;

    /// <summary>
    /// Floating name tag above each remote player's head mask: username + Steam avatar picture,
    /// read from the game's own player registry (<see cref="NetPlayerActors"/> — ZERO wire bytes
    /// of ours). Purely LOCAL rendering; applies live (<see cref="RemoteNameTag"/> re-reads the
    /// gate every tick, so toggling hides/shows without a restart). Default ON.
    /// </summary>
    internal static ConfigEntry<bool> NameTags = null!;

    /// <summary>
    /// THE ONE READ OF <c>[Net] NameTags</c>. Both floating identity rows ask this: the tag above a
    /// peer's head mask (<see cref="RemoteNameTag"/>) and the tag on the corner of their
    /// control board (<see cref="OwnerTag"/>).
    ///
    /// <para>ONLY THE HEAD TAG USED TO ASK. The board tag was built unconditionally at
    /// <c>RemoteControlBoard.BuildBoard</c> and never read the entry at all, so turning the setting
    /// OFF left every peer's username readable off their board corner. The shipped description says
    /// the opposite in both languages — "turn OFF to hide <b>all</b> tags without a restart" /
    /// "AUS blendet <b>alle</b> Schilder ohne Neustart aus" — and the row is named for the object,
    /// "Name tags" / "Namensschilder", not for one of its two carriers. The setting is honoured as
    /// it is written.</para>
    ///
    /// <para>Re-read every tick (one bool) on both carriers, so a flip applies live with no restart
    /// and no rebuild, which is the other half of what the description promises. A null entry —
    /// before the config binds — reads as OFF, exactly as the head tag has always treated it.</para>
    /// </summary>
    internal static bool NameTagsWanted => NameTags != null && NameTags.Value;

    /// <summary>
    /// Show the local player their own avatar in a mirror floating in front of the head (a local
    /// cosmetic preview — independent of the net send, works in single-player). Default off.
    /// </summary>
    internal static ConfigEntry<bool> MirrorEnabled = null!;

    /// <summary>
    /// How much of OTHER players' cosmetic control boards this client renders (Off / only during
    /// the action phase / always). A purely LOCAL rendering choice — never affects game state or
    /// what we transmit. The anti-cheat reveal gate (<see cref="RevealGate"/>) always applies ON
    /// TOP: even in <see cref="RemoteBoardVisibility.Always"/>, a remote player's round cards show
    /// as BACKS during the secret selection phase. Default
    /// <see cref="RemoteBoardVisibility.ActionPhaseOnly"/>.
    /// </summary>
    internal static ConfigEntry<RemoteBoardVisibility> RemoteBoards = null!;

    /// <summary>
    /// Mod-version handshake guard (default ON): when a remote MODDED peer runs a different
    /// <see cref="NetProtocol.ModBuild"/>, show the blocking "Als Flat-Spieler joinen /
    /// Abbrechen" dialog (<see cref="VersionGuard"/>). OFF = never ask; mismatched builds then
    /// simply talk their best-effort additive wire to each other (safe — the TLV format
    /// guarantees old readers skip unknown fields — but cross-version cosmetics may differ).
    /// </summary>
    internal static ConfigEntry<bool> VersionGuardEnabled = null!;

    private static ConfigFile? _config;

    private GameObject? _driverGo;
    private GameObject? _watchGo;
    private INetTransport? _transport;
    private NetAvatarDriver? _driver;

    /// <summary>
    /// Bind the [Net] config entries (bind-once). Separated from <see cref="Init"/> so the mask
    /// picker + mirror can force it even in scenarios where the networking hook is off; the
    /// entries always exist for <see cref="LocalRigSampler"/>, <c>SettingsPanel</c> and
    /// <see cref="WorldUI.AvatarMirror"/> to read.
    /// </summary>
    internal static void BindConfig()
    {
        if (_config != null)
            return;
        _config = ModuleConfig.Create("net");
        // PEER-BOARD SEE-THROUGH lives in its own module file and binds itself, but it binds LAZILY
        // on the first peer board built — which means its rows do not exist in the settings browser
        // until someone else joins, i.e. exactly when the player wants to find them. Bound here so
        // the dials are present from startup. It is a separate ConfigFile, so this is a call and not
        // a dependency; PeerBoardFadeTuning.Bind is idempotent.
        PeerBoardFadeTuning.Bind();
        Enabled = _config.Bind("Net", "Enabled", Defaults.Net_Enabled,
            "Multiplayer VR embodiment sync: broadcast your head + hands over the game's own " +
            "netcode so other VR players see you (Demeo style), and render remote VR players. " +
            "Cosmetic only, never affects game state; a no-op in single-player and safe with " +
            "flat/non-modded players. Turn OFF to fully remove the networking hook.");
        DesyncPatienceSeconds = _config.Bind("Net", "DesyncPatienceSeconds", Defaults.DesyncPatienceSeconds,
            new ConfigDescription(
                "Wie lange dieser Client wartet, bis seine lokale Phase zu einer eingehenden Aktion " +
                "aufgeschlossen hat, bevor das SPIEL eine Desynchronisation meldet und die Sitzung " +
                "beendet. Das Spiel selbst gibt dafuer 5 Sekunden — in VR ist der lokale Client " +
                "messbar der langsamere, und jeder Ruckler geht von diesem Budget ab. Wirkt NUR " +
                "lokal: jeder Client zaehlt seine eigenen Versuche, eine Aktion wird weiterhin erst " +
                "ausgefuehrt wenn die Phase passt, und Geduld kann bei niemandem sonst etwas " +
                "ausloesen. 5 = exakt das Verhalten des unmodifizierten Spiels.",
                new AcceptableValueRange<float>(5f, 60f)));
        MaskId = _config.Bind("Net", "MaskId", Defaults.MaskId,
            new ConfigDescription(
                "Which head mask the local player wears (0..2). Picked in the in-VR settings panel; " +
                "synchronized so other VR players see the right mask on you.",
                new AcceptableValueRange<int>(0, HeadMaskLibrary.MaskCount - 1)));
        MaskSize = _config.Bind("Net", "MaskSize", Defaults.MaskSize,
            new ConfigDescription(
                "Uniform size of your head mask (1 = authored size). Tuned live in the in-VR " +
                "settings panel (Avatar > Maskengroesse); synchronized, so other VR players see " +
                "your mask at exactly the size you picked. Purely cosmetic.",
                new AcceptableValueRange<float>(NetProtocol.MaskSizeMin, NetProtocol.MaskSizeMax)));
        NameTags = _config.Bind("Net", "NameTags", Defaults.NameTags,
            "Floating name tag above each remote VR player's head mask: their username plus " +
            "their Steam avatar picture, read from the game's own player registry (nothing " +
            "extra is sent over the network). Scales with that player's world zoom so it stays " +
            "attached to their avatar. Purely local rendering; applies live — turn OFF to hide " +
            "all tags without a restart.");
        MirrorEnabled = _config.Bind("Net", "MirrorEnabled", Defaults.MirrorEnabled,
            "Show yourself in a mirror floating in front of your head so you can see your chosen " +
            "mask + hands. Local cosmetic preview only — independent of networking, works in " +
            "single-player. Toggle in the in-VR settings panel.");
        VersionGuardEnabled = _config.Bind("Net", "VersionGuard", Defaults.Net_VersionGuard,
            "Mod version handshake in multiplayer: when another MODDED player runs a different " +
            "mod build, show a dialog offering to join as a flat player (VR stays on locally, " +
            "mod networking off for the session) or to leave the session. Flat players without " +
            "the mod never trigger it. Turn OFF to skip the dialog and let mismatched builds " +
            "talk their best-effort compatible wire format.");
        RemoteBoards = _config.Bind("Net", "RemoteBoards", Defaults.RemoteBoards,
            "Wie viel von den Kontrolltafeln der Mitspieler du siehst: Off (nie), ActionPhaseOnly " +
            "(nur in der Aktionsphase — waehrend der geheimen Kartenauswahl ausgeblendet), Always " +
            "(immer). Rein lokale Darstellung; aendert nie den Spielzustand. Der Anti-Cheat-Schutz " +
            "greift zusaetzlich immer: waehrend der Auswahl zeigen fremde Rundenkarten stets nur " +
            "die RUECKSEITE, erst nach dem Aufdecken die echten Karten.");
    }

    public void Init()
    {
        BindConfig();

        // BEFORE the kill-switch, deliberately. This watches the GAME's netcode, not the mod's
        // avatar sync — and "turn the VR networking off because multiplayer is misbehaving" is
        // precisely the session whose desync report is worth the most. See DesyncWatch.
        Desync.DesyncWatch.Install();
        _watchGo = new GameObject("GloomhavenVR.DesyncWatch");
        Object.DontDestroyOnLoad(_watchGo);
        _watchGo.hideFlags = HideFlags.HideAndDontSave;
        _watchGo.AddComponent<Desync.DesyncWatchDriver>();

        if (!Enabled.Value)
        {
            VRLog.Info(Name, "VR embodiment sync disabled by config — networking hook not installed.");
            return;
        }

        // Requires a live Harmony instance for the receive hook. When VR is disabled the whole
        // plugin is a no-op before modules init, so if we got here Harmony exists — but guard
        // anyway and degrade to nothing rather than throw.
        if (VRSession.Harmony == null)
        {
            VRLog.Warn(Name, "No Harmony instance — VR embodiment sync not installed.");
            return;
        }

        _transport = new FfsNetTransport(VRSession.Harmony);
        _transport.Install();

        // ANY PLAYER MAY ANSWER AN ENCOUNTER (user request 2026-09-05) — see EncounterChoice for
        // the whole argument, including why the client's press travels on the game's SIDE-ACTION
        // channel and not as a queued GameAction. Registered HERE, behind the same [Net] Enabled
        // switch as the transport, on purpose: the client's unlock is gated on having SEEN the
        // host's mod packets (VersionGuard.IsModdedPeer), which only exist while that transport
        // runs. So a host with Net off simply never advertises, no client unlocks, and the feature
        // is absent rather than half-present — which is the failure mode that would matter.
        VRSession.Harmony.PatchAll(typeof(ClientButtonLocker_TryLockButton_Patch));
        VRSession.Harmony.PatchAll(typeof(UIEventPanel_ContinueEvent_Patch));
        VRSession.Harmony.PatchAll(typeof(UIEventPanel_CompleteEvent_Patch));
        VRSession.Harmony.PatchAll(typeof(UIEventPanel_ClientContinueRoadEvent_Patch));

        // ANY PLAYER MAY OPERATE THE ASSIGNMENT WINDOW (user request item 3, 2026-09-07) — the
        // same request as the encounter one above, for the loot/gold window, and it follows that
        // file term for term. See AssignmentChoice for the whole argument, including why the press
        // rides the mod's own side-action SENTINEL rather than the game's DistributeUI* actions
        // (those carry ActionPhaseType.NONE, so a queued client action ends in HandleDesync).
        // Registered HERE behind the same [Net] Enabled switch and for the same reason: the
        // client's unlock is gated on VersionGuard.IsModdedPeer, which only answers while this
        // transport runs, so a host with Net off means no client unlocks and the feature is absent
        // rather than half-present.
        VRSession.Harmony.PatchAll(typeof(UIDistributeReward_Distribute_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributePointsPopup_Hide_AssignmentPatch));
        VRSession.Harmony.PatchAll(typeof(UIDistributePointsSlot_EnableAddPoints_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributePointsSlot_EnableRemovePoints_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributeReward_SetButtonInteractable_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributePointsSlot_AddPoint_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributePointsSlot_RemovePoint_Patch));
        VRSession.Harmony.PatchAll(typeof(UIDistributeReward_OnConfirmClick_Patch));

        _driverGo = new GameObject("GloomhavenVR.NetAvatarDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;

        _driver = _driverGo.AddComponent<NetAvatarDriver>();
        // Identity board anchor = game world space, the shared frame in Gloomhaven's
        // server-authoritative model (see AvatarSerializer / IBoardAnchor).
        _driver.Configure(_transport, WorldAnchor.Instance);

        VRLog.Info(Name, "VR embodiment sync installed (send/receive head + hands over FFSNet side actions).");
    }

    public void Shutdown()
    {
        Desync.DesyncWatch.Uninstall();
        if (_watchGo != null)
        {
            Object.Destroy(_watchGo);
            _watchGo = null;
        }

        _transport?.Uninstall();
        _transport = null;

        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }
        _driver = null;

        // Drop the mask-prefab cache so a hot-reloaded / freshly-shipped bundle is re-probed.
        HeadMaskLibrary.Reset();
    }
}

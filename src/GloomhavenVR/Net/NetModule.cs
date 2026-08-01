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
    private INetTransport? _transport;
    private NetAvatarDriver? _driver;

    /// <summary>
    /// Bind the [Net] config entries (bind-once). Separated from <see cref="Init"/> so the mask
    /// picker + mirror can force it even in scenarios where the networking hook is off; the
    /// entries always exist for <see cref="LocalRigSampler"/>, <see cref="SettingsPanel"/> and
    /// <see cref="WorldUI.AvatarMirror"/> to read.
    /// </summary>
    internal static void BindConfig()
    {
        if (_config != null)
            return;
        _config = ModuleConfig.Create("net");
        Enabled = _config.Bind("Net", "Enabled", Defaults.Net_Enabled,
            "Multiplayer VR embodiment sync: broadcast your head + hands over the game's own " +
            "netcode so other VR players see you (Demeo style), and render remote VR players. " +
            "Cosmetic only, never affects game state; a no-op in single-player and safe with " +
            "flat/non-modded players. Turn OFF to fully remove the networking hook.");
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

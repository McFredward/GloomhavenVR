// =============================================================================================
// REGISTRATION (do this in a SEPARATE change to avoid conflicting with parallel workers):
//   File: src/GloomhavenVR/Plugin.cs, method RegisterModules() — add after the WorldUI line
//   (currently Plugin.cs:359, "_modules.Add(new WorldUI.WorldUIModule());"):
//
//       _modules.Add(new Net.NetModule());
//
//   Order: after the Rig/Hands modules (it reads VRRigDriver.HeadCamera + VRHands) and
//   before Compat is fine. It is safe anywhere after HandsModule.
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
/// NOT REGISTERED in Plugin.cs yet — see the registration note at the top of this file. The
/// module is inert until that one line is added, so it cannot affect the rest of the mod.
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

    private GameObject? _driverGo;
    private INetTransport? _transport;
    private NetAvatarDriver? _driver;

    public void Init()
    {
        Enabled ??= ModuleConfig.Create("net").Bind("Net", "Enabled", true,
            "Multiplayer VR embodiment sync: broadcast your head + hands over the game's own " +
            "netcode so other VR players see you (Demeo style), and render remote VR players. " +
            "Cosmetic only, never affects game state; a no-op in single-player and safe with " +
            "flat/non-modded players. Turn OFF to fully remove the networking hook.");
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
    }
}

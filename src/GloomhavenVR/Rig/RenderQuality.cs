using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR render-quality enforcement (aliasing fix, hardware items #5b/#6): hardware MSAA on
/// the XR eye textures plus forced anisotropic texture filtering.
///
/// WHY THE HMD HAD NO AA AT ALL: the game's own anti-aliasing (FXAA/SMAA) lives in the
/// PostProcessLayer that the mod kill-switches ([Compat] DisablePostProcessing — the layer
/// breaks stereo), and the game boots into its LOWEST quality level ("[PlatformLayer.cs]
/// Enabling QualitySettings named Fastest") whose <see cref="QualitySettings.antiAliasing"/>
/// is 0 — so the VR view rendered with zero AA of any kind: strong shimmer ("grisseln") on
/// card line art, control-board and initiative-track edges, worst at distance. The mod's
/// head camera runs the FORWARD rendering path ([Rig] ForwardRendering, default on), where
/// hardware MSAA works — nothing ever switched it on.
///
/// ENFORCEMENT PATTERN (same trap as the skin-weights floor in
/// <c>HandsDriver.EnforceGlobalSkinWeights</c>): the game swaps quality levels at boot and
/// when the menu applies the user's saved graphics settings, and every swap rewrites
/// <see cref="QualitySettings.antiAliasing"/> from the level's own value — a set-once fix
/// silently dies at the next swap. So <see cref="Tick"/> re-asserts per frame from the
/// VRRigDriver guarded tail (cheap int compare), logging only on actual transitions.
///
/// EYE-TEXTURE PLUMBING: on built-in XR + the OpenXR plugin the engine allocates the eye
/// textures (swapchain) from <c>XRSettings.eyeTextureDesc</c>, whose msaa follows
/// <see cref="QualitySettings.antiAliasing"/> for the built-in render pipeline; changing it
/// re-allocates the swapchain live (no rig rebuild). Belt-and-braces, we also push the
/// level straight to every live <see cref="XRDisplaySubsystem"/> via
/// <see cref="XRDisplaySubsystem.SetMSAALevel"/> — the same engine hook SRPs use — so the
/// display side never lags a quality-level fight.
///
/// TEXTURE-SIDE SHIMMER: distant sparkle on card faces also comes from texture sampling;
/// <see cref="ApplyAniso"/> forces anisotropic filtering globally
/// (<see cref="AnisotropicFiltering.ForceEnable"/> + a global min-aniso floor via
/// <see cref="Texture.SetGlobalAnisotropicFilteringLimits"/>) — safe: it only raises
/// sampling quality, never touches game content.
/// </summary>
internal static class RenderQuality
{
    /// <summary>Valid MSAA sample counts, in cycle order for the settings-panel button.</summary>
    private static readonly int[] MsaaSteps = { 0, 2, 4, 8 };

    /// <summary>Forced minimum / global maximum aniso level while ForceAnisotropic is on.</summary>
    private const int ForcedMinAniso = 8;
    private const int GlobalMaxAniso = 16;

    private static ConfigFile? _file;
    internal static ConfigEntry<int>? MsaaLevel;
    internal static ConfigEntry<bool>? ForceAnisotropic;

    private static readonly List<XRDisplaySubsystem> Displays = new(2);
    private static int _lastPushedDisplayMsaa = -1;
    private static bool _anisoForced;
    private static AnisotropicFiltering _anisoOriginal;

    /// <summary>
    /// Bind-once against the rig's own module config (dev.gloomhavenvr.rig.cfg —
    /// canonical <see cref="ModuleConfig"/> pattern; Plugin.cs's main config is owned
    /// by another seam). Lazy: called from the tick and the settings-panel accessors.
    /// </summary>
    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("rig");
        MsaaLevel = _file.Bind("RenderQuality", "MsaaLevel", 4, new ConfigDescription(
            "Hardware MSAA sample count for the VR eye textures (0 = off, 2/4/8). The game's own "
            + "AA lives in the PostProcessLayer the mod disables and its boot quality level sets "
            + "antiAliasing 0, so without this the HMD has NO anti-aliasing (shimmering card line "
            + "art / board edges). Re-asserted every frame against the game's quality-level swaps; "
            + "applies live (the XR swapchain re-allocates). Requires the forward rendering path "
            + "([Rig] ForwardRendering, default on) — the deferred path ignores MSAA.",
            new AcceptableValueList<int>(0, 2, 4, 8)));
        ForceAnisotropic = _file.Bind("RenderQuality", "ForceAnisotropic", true,
            "Force anisotropic texture filtering for ALL textures (plus a global min-aniso floor). "
            + "Cuts distant shimmer on flat-on-view textures — card faces, initiative portraits, "
            + "board art. Purely a sampling-quality raise; disable to restore the game's setting.");
    }

    /// <summary>Per-frame enforcement (VRRigDriver guarded tail step "Rig.RenderQuality").</summary>
    internal static void Tick()
    {
        if (!VRSession.IsRunning)
            return;
        Bind();
        ApplyMsaa();
        ApplyAniso();
    }

    /// <summary>Snap an arbitrary persisted value to the nearest valid sample count.</summary>
    private static int Sanitize(int level) =>
        level >= 8 ? 8 : level >= 4 ? 4 : level >= 2 ? 2 : 0;

    private static void ApplyMsaa()
    {
        int wanted = Sanitize(MsaaLevel!.Value);

        int current = QualitySettings.antiAliasing;
        if (current != wanted)
        {
            QualitySettings.antiAliasing = wanted;
            string quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            VRLog.Info("Rig", $"MSAA (re)asserted {current}x → {wanted}x (quality level '{quality}'; " +
                              "the game rewrites antiAliasing on every quality-level swap, so this " +
                              "re-arms per frame like the skin-weights floor).");
        }

        // Push to the live XR display once per value change — the built-in pipeline mirrors
        // QualitySettings into the eye-texture desc itself, but an explicit SetMSAALevel makes
        // the swapchain re-allocation deterministic (and covers any pipeline that doesn't).
        if (wanted != _lastPushedDisplayMsaa)
        {
            SubsystemManager.GetInstances(Displays);
            if (Displays.Count == 0)
                return; // display not up yet — retry next tick
            for (int i = 0; i < Displays.Count; i++)
                Displays[i].SetMSAALevel(Mathf.Max(wanted, 1)); // XR API: 1 = no MSAA
            _lastPushedDisplayMsaa = wanted;
            VRLog.Info("Rig", $"XR display MSAA level pushed to {Mathf.Max(wanted, 1)} on " +
                              $"{Displays.Count} display subsystem(s) — eye textures re-allocate live.");
        }
    }

    private static void ApplyAniso()
    {
        if (ForceAnisotropic!.Value)
        {
            if (QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable)
                return;
            if (!_anisoForced)
                _anisoOriginal = QualitySettings.anisotropicFiltering; // restore point
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(ForcedMinAniso, GlobalMaxAniso);
            VRLog.Info("Rig", $"Anisotropic filtering forced globally (was {_anisoOriginal}; " +
                              $"min {ForcedMinAniso}, max {GlobalMaxAniso}) — texture-side " +
                              "distant-shimmer mitigation for card/board art.");
            _anisoForced = true;
        }
        else if (_anisoForced)
        {
            QualitySettings.anisotropicFiltering = _anisoOriginal;
            Texture.SetGlobalAnisotropicFilteringLimits(-1, -1); // engine: -1/-1 resets limits
            _anisoForced = false;
            VRLog.Info("Rig", $"Anisotropic filtering force released — restored {_anisoOriginal}.");
        }
    }

    // ---- settings-panel accessors (WorldUI SettingsPanel "MSAA" cycle row) -------------------

    internal static string MsaaLabel()
    {
        Bind();
        int v = Sanitize(MsaaLevel!.Value);
        return v == 0 ? "Off" : $"{v}x";
    }

    internal static void CycleMsaa()
    {
        Bind();
        int idx = System.Array.IndexOf(MsaaSteps, Sanitize(MsaaLevel!.Value));
        MsaaLevel!.Value = MsaaSteps[(idx + 1) % MsaaSteps.Length];
        // No further plumbing needed: Tick's compare re-asserts QualitySettings and pushes
        // the new level to the XR display next frame; BepInEx persists on set.
    }
}

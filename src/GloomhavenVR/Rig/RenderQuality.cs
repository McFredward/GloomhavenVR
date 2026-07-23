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
///
/// "MSAA CHANGES NOTHING" DIAGNOSIS (hardware round 2 — build 8b0553034 logged the full
/// assert/push chain yet the user saw ZERO difference cycling 2x/4x/8x): the log only ever
/// proved WE set the level, never that the eye texture came back multisampled — the OpenXR
/// runtime (VDXR here) binds MSAA at SWAPCHAIN creation and may cap or ignore the request.
/// <see cref="LogEyeTargetDiagnostics"/> closes that gap: a few frames after every MSAA /
/// resolution-scale change (and once per rig build) it prints the ENGINE-SIDE truth —
/// <see cref="XRSettings.eyeTextureDesc"/>.msaaSamples (what the engine asks the XR display
/// to allocate) plus the per-<see cref="XRDisplaySubsystem"/> render-pass renderTargetDesc
/// when the display exposes it — and an explicit VERDICT line. samples==wanted ⇒ MSAA is
/// genuinely active and the residual shimmer is SHADER/texture aliasing MSAA cannot touch
/// (specular sparkle, sub-pixel texture detail — geometry-edge only); samples==1 ⇒ the
/// plugin/runtime ignored QualitySettings + SetMSAALevel and the supersampling lever below
/// is the fix. The delay (<see cref="DiagDelayFrames"/>) lets the live swapchain
/// re-allocation land before we read the desc back.
///
/// SUPERSAMPLING FALLBACK ([RenderQuality] EyeResolutionScale →
/// <see cref="XRSettings.eyeTextureResolutionScale"/>): brute-force AA that ALWAYS works —
/// it raises the eye-texture allocation itself, so it helps geometry edges AND
/// shader/texture shimmer, independent of what the OpenXR runtime does with MSAA. Costs
/// GPU fill/bandwidth ∝ scale² (1.4 ≈ 2× pixel work, 2.0 = 4×). Applies live (the
/// swapchain re-allocates, same plumbing as MSAA); settings-panel row can hook
/// <see cref="EyeResolutionScale"/> later — config suffices for hardware A/B.
///
/// REBUILD TEST PATH ([RenderQuality] RebuildRigOnMsaaChange, default OFF): tears the rig
/// down/up on MSAA changes. The OpenXR swapchain is owned by the XR SESSION, not our
/// camera, so a rig rebuild is NOT expected to re-bind MSAA — this exists to
/// prove/disprove exactly that on hardware without a new build.
/// </summary>
internal static class RenderQuality
{
    /// <summary>Valid MSAA sample counts, in cycle order for the settings-panel button.</summary>
    private static readonly int[] MsaaSteps = { 0, 2, 4, 8 };

    /// <summary>Forced minimum / global maximum aniso level while ForceAnisotropic is on.</summary>
    private const int ForcedMinAniso = 8;
    private const int GlobalMaxAniso = 16;

    /// <summary>Config bounds of the supersampling lever (native = 1).</summary>
    private const float MinEyeScale = 0.8f;
    private const float MaxEyeScale = 2.0f;

    /// <summary>
    /// Frames between an MSAA/eye-scale change and the eye-target diagnostic readback —
    /// long enough for the live swapchain re-allocation to land before we read the desc.
    /// </summary>
    private const int DiagDelayFrames = 30;

    private static ConfigFile? _file;
    internal static ConfigEntry<int>? MsaaLevel;
    internal static ConfigEntry<bool>? ForceAnisotropic;
    internal static ConfigEntry<float>? EyeResolutionScale;
    internal static ConfigEntry<bool>? RebuildRigOnMsaaChange;

    private static readonly List<XRDisplaySubsystem> Displays = new(2);
    private static int _lastPushedDisplayMsaa = -1;
    private static float _lastLoggedEyeScale = -1f;
    private static int _diagCountdown;
    private static string _diagReason = "";
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
        EyeResolutionScale = _file.Bind("RenderQuality", "EyeResolutionScale", 1.0f, new ConfigDescription(
            "Supersampling: XR eye-texture resolution scale (1 = native). Brute-force anti-aliasing "
            + "that works even where the OpenXR runtime caps/ignores MSAA, and the only lever against "
            + "SHADER/texture shimmer (specular sparkle, sub-pixel detail) that geometry-edge MSAA "
            + "cannot touch. GPU cost grows with the SQUARE of the value: 1.4 ≈ 2x pixel work, 2.0 = 4x "
            + "— drop frames means drop this. Below 1 reclaims perf at the cost of sharpness. "
            + "Applies live (the eye-texture swapchain re-allocates).",
            new AcceptableValueRange<float>(MinEyeScale, MaxEyeScale)));
        RebuildRigOnMsaaChange = _file.Bind("RenderQuality", "RebuildRigOnMsaaChange", false,
            "DIAGNOSTIC ONLY: tear down and rebuild the VR rig whenever the MSAA level changes. The "
            + "OpenXR swapchain is owned by the XR session (not our camera), so a rig rebuild is NOT "
            + "expected to re-bind MSAA — this toggle exists to prove/disprove that on hardware. "
            + "Causes a brief view reset per MSAA change; leave off in normal play.");
    }

    /// <summary>Per-frame enforcement (VRRigDriver guarded tail step "Rig.RenderQuality").</summary>
    internal static void Tick()
    {
        if (!VRSession.IsRunning)
            return;
        Bind();
        ApplyMsaa();
        ApplyEyeScale();
        ApplyAniso();
        if (_diagCountdown > 0 && --_diagCountdown == 0)
            LogEyeTargetDiagnostics(_diagReason);
    }

    /// <summary>
    /// Schedule the eye-target diagnostic readback <see cref="DiagDelayFrames"/> frames out
    /// (rig build, MSAA push, eye-scale change) — delayed so the swapchain re-allocation the
    /// change triggers has landed by the time we read <see cref="XRSettings.eyeTextureDesc"/>.
    /// </summary>
    internal static void RequestEyeTargetDiagnostics(string reason)
    {
        _diagReason = reason;
        _diagCountdown = DiagDelayFrames;
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
            bool firstPush = _lastPushedDisplayMsaa < 0;
            _lastPushedDisplayMsaa = wanted;
            VRLog.Info("Rig", $"XR display MSAA level pushed to {Mathf.Max(wanted, 1)} on " +
                              $"{Displays.Count} display subsystem(s) — eye textures re-allocate live.");
            // Read the ACTUAL eye-target sample count back once the re-allocation had time
            // to land — this is the line that proves (or disproves) the MSAA took effect.
            RequestEyeTargetDiagnostics($"MSAA push {Mathf.Max(wanted, 1)}x");
            // Opt-in hardware experiment (see class doc): does a rig rebuild re-bind MSAA?
            // Skipped on the boot-time first push — only user-driven CHANGES trigger it.
            if (!firstPush && RebuildRigOnMsaaChange!.Value)
                VRRigDriver.RequestRebuild($"[RenderQuality] RebuildRigOnMsaaChange test path (MSAA → {wanted}x)");
        }
    }

    /// <summary>
    /// Supersampling lever: assert <c>[RenderQuality] EyeResolutionScale</c> onto
    /// <see cref="XRSettings.eyeTextureResolutionScale"/>. Re-asserted per frame (one float
    /// compare — the setter re-allocates the swapchain, so it must never be spammed while
    /// equal); logs + schedules the diagnostic readback once per distinct target value.
    /// Fully reversible: 1.0 restores the native allocation.
    /// </summary>
    private static void ApplyEyeScale()
    {
        float wanted = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);
        if (Mathf.Abs(XRSettings.eyeTextureResolutionScale - wanted) < 0.0005f)
            return;
        XRSettings.eyeTextureResolutionScale = wanted;
        if (Mathf.Abs(_lastLoggedEyeScale - wanted) < 0.0005f)
            return; // engine hasn't reflected the write yet (device settling) — logged already
        _lastLoggedEyeScale = wanted;
        VRLog.Info("Rig", $"Eye-texture resolution scale asserted → {wanted:F2} " +
                          $"(supersampling AA; GPU cost ∝ scale² ≈ {wanted * wanted:F2}x pixel work; " +
                          "eye textures re-allocate live).");
        RequestEyeTargetDiagnostics($"eyeTextureResolutionScale → {wanted:F2}");
    }

    /// <summary>
    /// The delayed readback that settles the "MSAA changes nothing" question with engine-side
    /// truth (class doc, diagnosis section): eyeTextureDesc sample count + per-display
    /// render-pass descs + head-camera render-path state, then an explicit VERDICT line.
    /// </summary>
    private static void LogEyeTargetDiagnostics(string reason)
    {
        int wanted = Sanitize(MsaaLevel!.Value);
        RenderTextureDescriptor desc = XRSettings.eyeTextureDesc;

        Camera? head = VRRigDriver.HeadCamera;
        string headState = head == null
            ? "no head camera"
            : $"head cam allowMSAA={head.allowMSAA}, allowHDR={head.allowHDR}, " +
              $"path={head.actualRenderingPath}, depthTex={head.depthTextureMode}, " +
              $"target={(head.targetTexture != null ? head.targetTexture.name : "XR eye target (direct)")}";

        VRLog.Info("Rig", $"EYE-TARGET DIAG ({reason}): QualitySettings.antiAliasing={QualitySettings.antiAliasing}, " +
                          $"eyeTextureDesc {desc.width}x{desc.height} msaaSamples={desc.msaaSamples} " +
                          $"fmt={desc.colorFormat} dim={desc.dimension} sRGB={desc.sRGB}; " +
                          $"eyeTexture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
                          $"resolutionScale={XRSettings.eyeTextureResolutionScale:F2}, " +
                          $"viewportScale={XRSettings.renderViewportScale:F2}, " +
                          $"stereo={XRSettings.stereoRenderingMode}, device='{XRSettings.loadedDeviceName}', " +
                          $"gfx={SystemInfo.graphicsDeviceType}; {headState}.");

        // Display-side render passes: on the BUILT-IN pipeline the display may expose 0
        // passes outside SRP render callbacks — logged either way so absence is evidence too.
        SubsystemManager.GetInstances(Displays);
        for (int i = 0; i < Displays.Count; i++)
        {
            try
            {
                int passes = Displays[i].GetRenderPassCount();
                if (passes == 0)
                {
                    VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} exposes 0 render passes from " +
                                      "script (normal on built-in pipeline — eyeTextureDesc above is " +
                                      "the authoritative engine-side value).");
                    continue;
                }
                for (int p = 0; p < passes; p++)
                {
                    Displays[i].GetRenderPass(p, out XRDisplaySubsystem.XRRenderPass pass);
                    RenderTextureDescriptor rt = pass.renderTargetDesc;
                    VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} renderPass {p}: " +
                                      $"{rt.width}x{rt.height} msaaSamples={rt.msaaSamples} fmt={rt.colorFormat} " +
                                      $"dim={rt.dimension}.");
                }
            }
            catch (System.Exception e)
            {
                VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} render-pass query threw " +
                                  $"'{e.Message}' — eyeTextureDesc above remains the evidence.");
            }
        }

        string verdict = wanted == 0
            ? "MSAA is OFF by config — desc.msaaSamples should read 1; nothing to verify."
            : desc.msaaSamples >= wanted
                ? $"VERDICT: the engine allocates the eye textures MULTISAMPLED ({desc.msaaSamples}x) — MSAA " +
                  "is genuinely active. If aliasing still looks unchanged, the shimmer is SHADER/TEXTURE " +
                  "aliasing (specular sparkle, sub-pixel detail) that geometry-edge MSAA cannot fix — " +
                  "raise [RenderQuality] EyeResolutionScale (supersampling) instead."
                : $"VERDICT: eye-texture desc reports {desc.msaaSamples}x but {wanted}x was requested — " +
                  "QualitySettings/SetMSAALevel is NOT binding (OpenXR runtime cap at swapchain creation, " +
                  "e.g. VDXR). Working lever: [RenderQuality] EyeResolutionScale supersampling; " +
                  "RebuildRigOnMsaaChange tests the rebuild hypothesis.";
        VRLog.Info("Rig", $"EYE-TARGET DIAG: {verdict}");
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

    // ---- settings-panel accessors (WorldUI SettingsPanel "Supersampling" stepper row) ---------

    /// <summary>Stepper readout for the supersampling row ("0.8x".."2.0x").</summary>
    internal static string EyeScaleLabel()
    {
        Bind();
        return $"{Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale):0.0}x";
    }

    /// <summary>
    /// Step <c>[RenderQuality] EyeResolutionScale</c> by ±0.1, clamped to 0.8–2.0 (rounded to
    /// one decimal so repeated presses never drift off the 0.1 grid). NOTE (hardware-proven):
    /// under VDXR the MSAA row does NOTHING — the OpenXR runtime caps the swapchain at 1x
    /// (see the EYE-TARGET DIAG verdict) — so this supersampling lever is the working
    /// anti-aliasing control there. Applies live (Tick re-asserts the scale and the
    /// eye-texture swapchain re-allocates); BepInEx persists on set.
    /// </summary>
    internal static void StepEyeScale(int delta)
    {
        Bind();
        float next = EyeResolutionScale!.Value + delta * 0.1f;
        EyeResolutionScale.Value = Mathf.Clamp(Mathf.Round(next * 10f) / 10f, MinEyeScale, MaxEyeScale);
    }
}

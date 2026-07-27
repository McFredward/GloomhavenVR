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
/// EYE-TEXTURE PLUMBING: on built-in XR + the OpenXR plugin the engine drives the eye-render
/// MSAA from <see cref="QualitySettings.antiAliasing"/>; changing it re-allocates the render
/// targets live (no rig rebuild). Belt-and-braces, we also push the level straight to every
/// live <see cref="XRDisplaySubsystem"/> via <see cref="XRDisplaySubsystem.SetMSAALevel"/> —
/// the same engine hook SRPs use — so the display side never lags a quality-level fight.
///
/// TEXTURE-SIDE SHIMMER: distant sparkle on card faces also comes from texture sampling;
/// <see cref="ApplyAniso"/> forces anisotropic filtering globally
/// (<see cref="AnisotropicFiltering.ForceEnable"/> + a global min-aniso floor via
/// <see cref="Texture.SetGlobalAnisotropicFilteringLimits"/>) — safe: it only raises
/// sampling quality, never touches game content.
///
/// RETRACTED 2026-07 — "eyeTextureDesc.msaaSamples==1 ⇒ MSAA is not binding" WAS A WRONG
/// INFERENCE, and this file used to print it as a VERDICT. It is not merely unproven, it is
/// unprovable from that reading, and it cost a whole performance investigation: the reader
/// trusted the log line and concluded MSAA was inert and therefore free. The facts:
///
/// - <see cref="XRSettings.eyeTextureDesc"/> and the per-display render-pass renderTargetDesc
///   describe the SUBMITTED swapchain image. A compositor swapchain image is single-sample by
///   definition — multisample surfaces are resolved before submission. msaaSamples==1 there is
///   therefore the EXPECTED reading whether MSAA ran or not; it cannot distinguish the cases.
/// - The decisive evidence is the rendered image, and it is unambiguous: cycling this row in
///   the VR settings VISIBLY changes edge quality in the headset (user report 2026-07, "MSAA
///   IST sichtbar im Spiel wenn man es anmacht … insbesondere beim Controllboard").
/// - And the change can only be the eye render, by elimination: the MSAA row's ONLY reachable
///   targets are the XR eye path and the desktop backbuffer, because EVERY RenderTexture the
///   mod allocates pins <c>antiAliasing = 1</c> explicitly (FlatScreen.2.CameraStack:265,
///   FlatScreen.3.Desktop:118, FlatScreenStereo.2.Compositor:179, FlatScreenStereo.3.Map:37
///   and :1661) and a camera rendering into a RenderTexture samples at THAT texture's count,
///   not QualitySettings'. The control board is a mod-built object seen only through the HMD.
///
/// So MSAA binds, it is a real user-visible quality feature, and it is NOT free.
/// <see cref="LogEyeTargetDiagnostics"/> now reports the descs and states what they can and
/// cannot decide; it no longer pronounces a verdict it has no evidence for.
///
/// THE COST SIDE — WHY THE TWO LEVERS BELOW ARE ONE DECISION: the runtime already hands us a
/// heavily supersampled target (hardware log: 3072x3264 per eye against a 2064x2208 Quest 3
/// panel = 1.49x linear, 2.2x the displayable pixels) and MultiPass renders it TWICE. MSAA and
/// supersampling attack the SAME artefact — geometry edges — and their sample counts multiply:
/// effective edge samples per display pixel ≈ msaa x linearOversample². At 8x MSAA on a 1.49x
/// target that is ~17.7, far past the point where more helps. Meanwhile the cost of the two is
/// NOT the same shape: resolution scales every per-pixel stage (shading, rasterization, the
/// MSAA surfaces and their resolve) with scale², whereas MSAA multiplies the surface and
/// resolve bandwidth but not the shading. So the cheap quality is bought by trimming MSAA
/// first and resolution second — which is exactly the order <see cref="Presets"/> uses.
/// NOT VERIFIED ON HARDWARE: the perceptual claim ("6.4 effective samples looks like 17.7").
/// The sample arithmetic is verifiable; how it looks is what the A/B in the settings is for.
///
/// RESOLUTION LEVER ([RenderQuality] EyeResolutionScale →
/// <see cref="XRSettings.eyeTextureResolutionScale"/>): scales the eye-texture allocation.
/// Above 1 it is brute-force AA that also fixes SHADER/TEXTURE shimmer (specular sparkle,
/// sub-pixel detail) which geometry-edge MSAA cannot touch; below 1 it is the single largest
/// GPU saving available here, because all per-pixel work scales with scale². Applies live.
///
/// VIEWPORT FALLBACK ([RenderQuality] ViewportScaleFallback, default ON): some OpenXR
/// providers negotiate the swapchain once at session start and ignore
/// <see cref="XRSettings.eyeTextureResolutionScale"/> afterwards. We cannot know which from
/// static inspection, so we MEASURE: <see cref="LogEyeTargetDiagnostics"/> reads
/// <see cref="XRSettings.eyeTextureWidth"/> back against the baseline captured at scale 1.0
/// and, if the allocation did not move, engages <see cref="XRSettings.renderViewportScale"/>
/// instead — rendering into a sub-rect of the existing swapchain, which every provider
/// honours. Either way the log names WHICH lever actually bound, so the resolution row is
/// never silently dead.
///
/// REBUILD TEST PATH ([RenderQuality] RebuildRigOnMsaaChange, default OFF): tears the rig
/// down/up on MSAA changes. Retained as an escape hatch for providers that only re-negotiate
/// sample counts at session start; the question it was written to settle (does MSAA bind at
/// all?) is answered above, so leave it off in normal play.
/// </summary>
internal static class RenderQuality
{
    /// <summary>Valid MSAA sample counts, in cycle order for the settings-panel button.</summary>
    private static readonly int[] MsaaSteps = { 0, 2, 4, 8 };

    /// <summary>Forced minimum / global maximum aniso level while ForceAnisotropic is on.</summary>
    private const int ForcedMinAniso = 8;
    private const int GlobalMaxAniso = 16;

    /// <summary>
    /// Config bounds of the resolution lever (native = 1). The floor was 0.8 while this was
    /// an anti-aliasing knob only; it is 0.5 now that the same entry is the primary GPU
    /// lever — 0.8 caps the saving at 36% of pixel work, which is not enough to pull a
    /// machine out of runtime reprojection.
    /// </summary>
    private const float MinEyeScale = 0.5f;
    private const float MaxEyeScale = 2.0f;

    /// <summary>Tolerance for "the eye texture actually re-allocated" (fraction of expected width).</summary>
    private const float EyeWidthMatchTolerance = 0.05f;

    /// <summary>
    /// Frames between an MSAA/eye-scale change and the eye-target diagnostic readback —
    /// long enough for the live swapchain re-allocation to land before we read the desc.
    /// </summary>
    private const int DiagDelayFrames = 30;

    /// <summary>
    /// One named point on the MSAA x resolution curve. The two levers below trade against the
    /// SAME artefact and their sample counts multiply (class doc), so offering them as two
    /// independent numbers asks the player to solve a two-variable problem blind. A preset is
    /// one choice; the individual rows stay visible and adjustable afterwards, and any manual
    /// edit simply reads back as "custom" — nothing is hidden or locked.
    /// Ordered best-looking → cheapest; <see cref="CyclePreset"/> walks them in this order.
    /// Values sit on the 0.1 stepper grid so the preset and the stepper never disagree.
    /// </summary>
    private readonly struct Preset
    {
        internal readonly string LocId;
        internal readonly int Msaa;
        internal readonly float Scale;

        internal Preset(string locId, int msaa, float scale)
        {
            LocId = locId;
            Msaa = msaa;
            Scale = scale;
        }
    }

    /// <summary>
    /// MSAA is trimmed BEFORE resolution, deliberately: resolution below 1 also softens
    /// TEXTURE detail (and the game's card atlases are mipless — see the Cards FACE TEXTURE
    /// DIAG line), whereas MSAA only ever touched geometry edges, which the remaining
    /// supersample still covers. Relative per-pixel cost is noted per entry as a MODEL, not a
    /// measurement: shading ∝ scale², MSAA surface+resolve bandwidth ∝ msaa x scale².
    /// </summary>
    private static readonly Preset[] Presets =
    {
        new("preset_quality",     8, 1.0f), // shading 100%, msaa bandwidth 100%
        new("preset_balanced",    4, 0.9f), // shading  81%, msaa bandwidth  41%
        new("preset_performance", 2, 0.8f), // shading  64%, msaa bandwidth  16%
        new("preset_minimum",     0, 0.6f), // shading  36%, msaa bandwidth   5%
    };

    private static ConfigFile? _file;
    internal static ConfigEntry<int>? MsaaLevel;
    internal static ConfigEntry<bool>? ForceAnisotropic;
    internal static ConfigEntry<float>? EyeResolutionScale;
    internal static ConfigEntry<bool>? RebuildRigOnMsaaChange;
    internal static ConfigEntry<bool>? ViewportScaleFallback;
    internal static ConfigEntry<int>? PixelLightCount;

    private static readonly List<XRDisplaySubsystem> Displays = new(2);
    private static int _lastPushedDisplayMsaa = -1;
    private static float _lastLoggedEyeScale = -1f;
    private static int _lastLoggedPixelLights = int.MinValue;
    /// <summary>The game's own pixelLightCount before we first capped it (-1 = we have not).</summary>
    private static int _pixelLightsOriginal = -1;
    private static int _diagCountdown;
    private static string _diagReason = "";
    private static bool _anisoForced;
    private static AnisotropicFiltering _anisoOriginal;

    /// <summary>
    /// Eye-texture width observed while the resolution scale was 1.0 — the yardstick the
    /// readback measures a scaled allocation against. 0 = not sampled yet.
    /// </summary>
    private static int _baseEyeWidth;

    /// <summary>
    /// Which lever the resolution row is actually riding on, decided by readback:
    /// null = undecided (no scaled value asserted yet), true = the eye texture re-allocated
    /// (eyeTextureResolutionScale bound), false = it did not and renderViewportScale took over.
    /// </summary>
    private static bool? _eyeScaleBinds;

    /// <summary>Last value written to <see cref="XRSettings.renderViewportScale"/> by us (1 = untouched).</summary>
    private static float _viewportScaleApplied = 1f;

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
            "Hardware MSAA sample count for the VR eye render (0 = off, 2/4/8). The game's own "
            + "AA lives in the PostProcessLayer the mod disables and its boot quality level sets "
            + "antiAliasing 0, so without this the HMD has NO anti-aliasing (shimmering card line "
            + "art / board edges). Re-asserted every frame against the game's quality-level swaps; "
            + "applies live. Requires the forward rendering path ([Rig] ForwardRendering, default "
            + "on) — the deferred path ignores MSAA. COSTS GPU TIME: it multiplies the render-target "
            + "and resolve bandwidth of a target the runtime already hands us supersampled, twice "
            + "per frame under MultiPass. Trade it against EyeResolutionScale below — both fight "
            + "geometry aliasing and their sample counts multiply, so 8x on a heavily supersampled "
            + "target is the expensive half of a job already mostly done.",
            new AcceptableValueList<int>(0, 2, 4, 8)));
        ForceAnisotropic = _file.Bind("RenderQuality", "ForceAnisotropic", true,
            "Force anisotropic texture filtering for ALL textures (plus a global min-aniso floor). "
            + "Cuts distant shimmer on flat-on-view textures — card faces, initiative portraits, "
            + "board art. Purely a sampling-quality raise; disable to restore the game's setting.");
        EyeResolutionScale = _file.Bind("RenderQuality", "EyeResolutionScale", 1.0f, new ConfigDescription(
            "Render resolution per eye, relative to what the OpenXR runtime asks for (1 = as asked). "
            + "THE primary GPU lever: essentially all per-pixel work — shading, rasterization, the "
            + "MSAA surfaces and their resolve — scales with the SQUARE of this value. 0.7 = about "
            + "half the pixel work, 1.4 = about double. Note the runtime's request is itself usually "
            + "supersampled (Virtual Desktop / SteamVR resolution sliders sit on top of this), so "
            + "below 1 is often still above panel resolution — check the [Rig] EYE-TARGET DIAG line "
            + "for the actual pixel count. Above 1 is the only lever against SHADER/TEXTURE shimmer "
            + "(specular sparkle, sub-pixel detail) that geometry-edge MSAA cannot touch; below 1 "
            + "softens texture detail before it softens edges. Applies live.",
            new AcceptableValueRange<float>(MinEyeScale, MaxEyeScale)));
        ViewportScaleFallback = _file.Bind("RenderQuality", "ViewportScaleFallback", true,
            "If EyeResolutionScale does not move the eye-texture allocation (some OpenXR providers "
            + "negotiate the swapchain once at session start and ignore it afterwards), fall back to "
            + "XRSettings.renderViewportScale, which renders into a sub-rect of the existing swapchain "
            + "and is honoured everywhere. The choice is made by READING THE ALLOCATION BACK, not by "
            + "guessing, and the [Rig] EYE-TARGET DIAG line names which lever bound. Off = only ever "
            + "use eyeTextureResolutionScale (the resolution row then silently does nothing on such a "
            + "provider — for A/B only).");
        RebuildRigOnMsaaChange = _file.Bind("RenderQuality", "RebuildRigOnMsaaChange", false,
            "Tear down and rebuild the VR rig whenever the MSAA level changes. Escape hatch for "
            + "OpenXR providers that only re-negotiate sample counts at session start. The question "
            + "this was originally written to settle — whether MSAA binds at all — is answered (it "
            + "does; it is visibly effective in the headset), so this is no longer a diagnostic. "
            + "Causes a brief view reset per MSAA change; leave off in normal play.");

        PixelLightCount = _file.Bind("RenderQuality", "PixelLightCount", -1, new ConfigDescription(
            "Maximum number of PER-PIXEL lights (-1 = leave the game's own value alone, which is "
            + "what ships). In the built-in forward renderer every per-pixel light beyond the first "
            + "costs an ADDITIONAL FULL DRAW CALL for every renderer it touches — a scenario "
            + "measured 4 per-pixel lights against ~1500 visible renderers, so this multiplies the "
            + "submission volume that the [Perf] SPLIT line shows as the frame's wall. Lights beyond "
            + "this count still light the scene, but per VERTEX, which costs no extra pass. THE "
            + "TRADE IS REAL AND VISIBLE: point-light falloff on walls and floors gets flatter, and "
            + "this dungeon is lit by 16 point lights. The game exposes no control for this, which "
            + "is why the mod does. Re-asserted per frame like MsaaLevel, because the game rewrites "
            + "QualitySettings on every quality-level swap.",
            new AcceptableValueRange<int>(-1, 8)));
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
        ApplyPixelLights();
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

    /// <summary>
    /// Cap <see cref="QualitySettings.pixelLightCount"/> ([RenderQuality] PixelLightCount).
    ///
    /// <para>WHY THIS EXISTS. Six hardware sessions established that the frame's wall is
    /// main-thread DRAW-CALL SUBMISSION, not pixels: culling measured 0.08 ms against 21.5 ms of
    /// submission, and resolution, MSAA, shadows, the depth prepass and the culling mask each
    /// moved it by under 10 %. The one multiplier nothing had touched is this one — in the
    /// built-in FORWARD renderer each per-pixel light past the first re-submits every renderer it
    /// affects. The scenario runs 4 of them over ~1500 visible renderers.</para>
    ///
    /// <para>-1 (the default) does NOTHING, deliberately: this is a visible trade, not a cleanup.
    /// Lights above the cap still light the scene per VERTEX, so nothing goes dark — but the
    /// falloff on walls and floors flattens, and this dungeon is lit by 16 point lights.</para>
    /// </summary>
    private static void ApplyPixelLights()
    {
        int wanted = PixelLightCount!.Value;

        if (wanted < 0)
        {
            // RESTORE, not "do nothing" — the first version of this returned here, so switching
            // back to -1 left the cap in place and the lights never came back. Every mutation of
            // game state in this mod has to be reversible, and a sentinel that means "hands off"
            // has to HAND BACK anything already taken.
            if (_pixelLightsOriginal >= 0)
            {
                QualitySettings.pixelLightCount = _pixelLightsOriginal;
                VRLog.Info("Rig", $"Per-pixel light cap released — restored the game's own value "
                                  + $"{_pixelLightsOriginal}. (Note the game rewrites this on every "
                                  + "quality-level swap, so the restored value is whatever it last set.)");
                _pixelLightsOriginal = -1;
                _lastLoggedPixelLights = int.MinValue;
            }
            return;
        }

        int current = QualitySettings.pixelLightCount;
        if (current == wanted)
            return;

        // Remember what the game had BEFORE we ever touched it, so -1 can give it back.
        if (_pixelLightsOriginal < 0)
            _pixelLightsOriginal = current;

        QualitySettings.pixelLightCount = wanted;
        if (wanted != _lastLoggedPixelLights)
        {
            _lastLoggedPixelLights = wanted;
            VRLog.Info("Rig", $"Per-pixel light cap asserted {current} → {wanted}. In forward "
                              + "rendering each light past the first costs one extra draw call per "
                              + "renderer it touches; lights above the cap fall back to per-vertex "
                              + "shading (no extra pass, flatter falloff). Watch the [Perf] SPLIT "
                              + "line's HeadCamera submit figure — that is the number this moves.");
        }
    }

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
    /// Resolution lever: assert <c>[RenderQuality] EyeResolutionScale</c> onto
    /// <see cref="XRSettings.eyeTextureResolutionScale"/>. Re-asserted per frame (one float
    /// compare — the setter re-allocates the eye textures, so it must never be spammed while
    /// equal); logs + schedules the diagnostic readback once per distinct target value.
    /// Fully reversible: 1.0 restores the native allocation AND releases the viewport fallback.
    /// The baseline width is sampled while the scale is still 1.0 — that is the yardstick
    /// <see cref="VerifyEyeScaleBound"/> later measures the allocation against.
    /// </summary>
    private static void ApplyEyeScale()
    {
        float wanted = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);

        if (_baseEyeWidth == 0
            && Mathf.Abs(XRSettings.eyeTextureResolutionScale - 1f) < 0.0005f
            && XRSettings.eyeTextureWidth > 0)
        {
            _baseEyeWidth = XRSettings.eyeTextureWidth;
        }

        // Back at native: undo the viewport fallback too, so "1.0" always means "exactly what
        // the runtime asked for" no matter which lever we were riding.
        if (Mathf.Abs(wanted - 1f) < 0.0005f && _viewportScaleApplied != 1f)
            ReleaseViewportScale();

        if (Mathf.Abs(XRSettings.eyeTextureResolutionScale - wanted) < 0.0005f)
        {
            // The allocation lever is already where we want it. If a previous readback proved
            // it does not bind here, keep the viewport fallback tracking the wanted value.
            if (_eyeScaleBinds == false && ViewportScaleFallback!.Value
                && Mathf.Abs(_viewportScaleApplied - wanted) > 0.0005f && Mathf.Abs(wanted - 1f) >= 0.0005f)
            {
                ApplyViewportScale(wanted, "resolution row changed while the viewport fallback is engaged");
            }
            return;
        }

        // Write ONCE per distinct target, tracked on our own side rather than on the property's
        // readback. A provider that ignores this setter also never reflects it in the getter, so
        // the compare above would be true forever and we would re-issue a swapchain-reallocating
        // write every single frame — on exactly the runtimes where it buys nothing.
        if (Mathf.Abs(_lastLoggedEyeScale - wanted) < 0.0005f)
            return;
        XRSettings.eyeTextureResolutionScale = wanted;
        _lastLoggedEyeScale = wanted;
        VRLog.Info("Rig", $"Eye render resolution scale asserted → {wanted:F2} " +
                          $"(per-pixel GPU work ∝ scale² ≈ {wanted * wanted:F2}x; applies live). " +
                          "Whether the ALLOCATION follows is read back below — this runtime may " +
                          "instead need the renderViewportScale fallback.");
        RequestEyeTargetDiagnostics($"eyeTextureResolutionScale → {wanted:F2}");
    }

    /// <summary>
    /// Readback half of the resolution lever, run from <see cref="LogEyeTargetDiagnostics"/>
    /// once the re-allocation has had <see cref="DiagDelayFrames"/> to land: did the eye
    /// texture actually change size? If it did, we are done. If it did not, the provider
    /// ignored the allocation request and <see cref="XRSettings.renderViewportScale"/> takes
    /// over — the same pixel saving, achieved by rendering into a sub-rect of the swapchain
    /// the provider already gave us. Returns the sentence appended to the DIAG line.
    /// </summary>
    private static string VerifyEyeScaleBound()
    {
        float wanted = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);
        int actual = XRSettings.eyeTextureWidth;

        if (Mathf.Abs(wanted - 1f) < 0.0005f)
            return "resolution scale is 1.0 — nothing to verify (the runtime's own request stands).";
        if (_baseEyeWidth <= 0 || actual <= 0)
            return "resolution scale cannot be verified yet — no baseline eye width sampled at scale 1.0 " +
                   "(the scale was already off-native when the rig came up); the row still applies, but " +
                   "which lever carries it is unknown this session.";

        float expected = _baseEyeWidth * wanted;
        bool allocationMoved = Mathf.Abs(actual - expected) <= expected * EyeWidthMatchTolerance;

        if (allocationMoved)
        {
            if (_eyeScaleBinds != true)
            {
                _eyeScaleBinds = true;
                if (_viewportScaleApplied != 1f)
                    ReleaseViewportScale();
            }
            return $"resolution scale BOUND via eyeTextureResolutionScale — eye texture went " +
                   $"{_baseEyeWidth}px → {actual}px wide (expected ~{expected:F0}px at {wanted:F2}x).";
        }

        _eyeScaleBinds = false;
        if (!ViewportScaleFallback!.Value)
            return $"resolution scale did NOT bind — eye texture is still {actual}px wide, expected " +
                   $"~{expected:F0}px at {wanted:F2}x. This provider ignores eyeTextureResolutionScale " +
                   "and [RenderQuality] ViewportScaleFallback is OFF, so the resolution row is doing " +
                   "NOTHING. Switch the fallback on.";

        ApplyViewportScale(wanted, $"eyeTextureResolutionScale did not move the allocation " +
                                   $"({actual}px, expected ~{expected:F0}px)");
        return $"resolution scale did not bind via eyeTextureResolutionScale (eye texture still " +
               $"{actual}px wide) — renderViewportScale {wanted:F2} engaged instead; the pixel saving " +
               "is real, it just comes from rendering a sub-rect of the swapchain rather than a " +
               "smaller swapchain.";
    }

    private static void ApplyViewportScale(float wanted, string reason)
    {
        XRSettings.renderViewportScale = wanted;
        _viewportScaleApplied = wanted;
        VRLog.Info("Rig", $"renderViewportScale → {wanted:F2} ({reason}). Per-pixel GPU work " +
                          $"∝ scale² ≈ {wanted * wanted:F2}x; the compositor samples only the " +
                          "rendered sub-rect, so the view is unchanged apart from sharpness.");
    }

    private static void ReleaseViewportScale()
    {
        XRSettings.renderViewportScale = 1f;
        _viewportScaleApplied = 1f;
        VRLog.Info("Rig", "renderViewportScale released → 1.00 (resolution row back at native).");
    }

    /// <summary>
    /// The delayed readback: eyeTextureDesc + per-display render-pass descs + head-camera
    /// render-path state, then the per-frame pixel-sample budget and the resolution-lever
    /// verification.
    ///
    /// NOTE ON WHAT THIS CAN PROVE (class doc, RETRACTED section): the sample counts printed
    /// here describe the SUBMITTED swapchain image, which is single-sample by definition, so
    /// they say nothing about whether MSAA ran. This method used to close with a VERDICT
    /// asserting the opposite and it misdirected a whole performance investigation. It now
    /// prints the numbers, states their limits, and leaves the MSAA question to the one
    /// instrument that can answer it — looking through the headset with the row toggled.
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

        // What the descs above CAN and CANNOT decide. Read this before drawing a conclusion
        // from msaaSamples: an earlier version of this method printed "MSAA is NOT binding"
        // whenever it read 1 here, which is not an inference the number supports, and a whole
        // performance investigation was built on top of that sentence.
        VRLog.Info("Rig", "EYE-TARGET DIAG: the sample counts above describe the SUBMITTED SWAPCHAIN " +
                          "IMAGE, which is single-sample by definition (multisample surfaces are " +
                          "resolved before submission). msaaSamples=1 there is therefore the EXPECTED " +
                          "reading whether MSAA ran or not, and NOTHING here can tell the two apart. " +
                          $"What is known: we requested {wanted}x and QualitySettings.antiAliasing " +
                          $"reads {QualitySettings.antiAliasing}. Whether that lands is decided by the " +
                          "one instrument that can decide it — looking through the headset with the " +
                          "MSAA row toggled (hardware 2026-07: it is clearly visible, so it does land, " +
                          "and it is therefore also costing GPU time).");

        // Per-frame pixel-sample budget: the single number to watch when trading the two
        // quality levers. It is the product of everything the levers touch, so it moves
        // exactly as much as the settings change — unlike the descs, it is directly comparable
        // across settings and across sessions.
        int eyeW = XRSettings.eyeTextureWidth;
        int eyeH = XRSettings.eyeTextureHeight;
        float viewport = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int eyePasses = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass ? 2 : 1;
        double megaSamples = eyeW * (double)eyeH * viewport * viewport
                             * Mathf.Max(wanted, 1) * eyePasses / 1_000_000.0;
        VRLog.Info("Rig", $"EYE-TARGET DIAG: pixel-sample budget = {megaSamples:F1} Msamples/frame " +
                          $"({eyeW}x{eyeH} per eye x viewport {viewport:F2}² x {Mathf.Max(wanted, 1)} MSAA " +
                          $"samples x {eyePasses} pass(es), {XRSettings.stereoRenderingMode}). This is the " +
                          "number the quality preset moves; halve it and the GPU frame time should fall " +
                          "roughly in step wherever the frame is fill/bandwidth bound.");

        VRLog.Info("Rig", $"EYE-TARGET DIAG: {VerifyEyeScaleBound()}");
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
        int next = MsaaSteps[(idx + 1) % MsaaSteps.Length];
        // Close the measurement window on the OLD value before writing the new one, so the log
        // carries one [Perf] FRAME/SPLIT summary per MSAA level instead of one summary averaging
        // however many levels the tester happened to cycle inside a 30 s interval.
        Core.PerfMonitor.MarkChange($"MSAA {Sanitize(MsaaLevel!.Value)}x → {next}x");
        MsaaLevel!.Value = next;
        // No further plumbing needed: Tick's compare re-asserts QualitySettings and pushes
        // the new level to the XR display next frame; BepInEx persists on set.
    }

    // ---- settings-panel accessors (WorldUI SettingsPanel "Supersampling" stepper row) ---------

    /// <summary>Stepper readout for the resolution row ("0.5x".."2.0x").</summary>
    internal static string EyeScaleLabel()
    {
        Bind();
        return $"{Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale):0.0}x";
    }

    /// <summary>
    /// Step <c>[RenderQuality] EyeResolutionScale</c> by ±0.1, clamped to
    /// <see cref="MinEyeScale"/>–<see cref="MaxEyeScale"/> (rounded to one decimal so repeated
    /// presses never drift off the 0.1 grid — the same grid <see cref="Presets"/> sits on, so
    /// stepping onto a preset's value reads back as that preset). Applies live: Tick re-asserts
    /// the scale, and the readback then decides whether the allocation or the viewport lever
    /// carries it. BepInEx persists on set.
    /// </summary>
    internal static void StepEyeScale(int delta)
    {
        Bind();
        float next = EyeResolutionScale!.Value + delta * 0.1f;
        float clamped = Mathf.Clamp(Mathf.Round(next * 10f) / 10f, MinEyeScale, MaxEyeScale);
        if (Mathf.Abs(clamped - EyeResolutionScale.Value) > 0.001f)
            Core.PerfMonitor.MarkChange($"eye resolution {EyeResolutionScale.Value:F1}x → {clamped:F1}x");
        EyeResolutionScale.Value = clamped;
    }

    // ---- settings-panel accessors (WorldUI SettingsPanel graphics-preset cycle row) -----------

    /// <summary>
    /// Index of the preset the CURRENT values match, or -1 for a hand-tuned combination. The
    /// preset is DERIVED, never stored: there is no fourth piece of state to fall out of sync
    /// with the two rows below it, and editing either row simply reads back as "custom".
    /// </summary>
    private static int CurrentPresetIndex()
    {
        int msaa = Sanitize(MsaaLevel!.Value);
        float scale = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);
        for (int i = 0; i < Presets.Length; i++)
        {
            if (Presets[i].Msaa == msaa && Mathf.Abs(Presets[i].Scale - scale) < 0.005f)
                return i;
        }
        return -1;
    }

    /// <summary>Cycle-button readout: the matching preset's name, or "custom".</summary>
    internal static string PresetLabel()
    {
        Bind();
        int idx = CurrentPresetIndex();
        return Core.Loc.Mod(idx < 0 ? "preset_custom" : Presets[idx].LocId);
    }

    /// <summary>
    /// Advance to the next preset (from "custom", start at the best-looking one) and write both
    /// levers. Nothing is locked afterwards — the MSAA and resolution rows stay live, and moving
    /// either one just drops the readout back to "custom".
    /// </summary>
    internal static void CyclePreset()
    {
        Bind();
        int next = (CurrentPresetIndex() + 1) % Presets.Length;
        Preset p = Presets[next];
        // The 2026-07 hardware sweep cycled all four presets inside ONE 30 s summary interval, so
        // every window straddled several settings and the four "measurements" it produced could
        // not be attributed to anything. Closing the window here is what makes the sweep readable.
        Core.PerfMonitor.MarkChange($"graphics preset → '{p.LocId}' (MSAA {p.Msaa}x, eye {p.Scale:F2}x)");
        MsaaLevel!.Value = p.Msaa;
        EyeResolutionScale!.Value = p.Scale;
        VRLog.Info("Rig", $"Graphics preset '{p.LocId}' applied — MSAA {p.Msaa}x, eye resolution " +
                          $"{p.Scale:F2}x. Modelled per-pixel shading ∝ {p.Scale * p.Scale:F2}x and " +
                          $"MSAA surface/resolve bandwidth ∝ {Mathf.Max(p.Msaa, 1) * p.Scale * p.Scale:F2}x " +
                          "relative to 1.0x/1x; watch the [Perf] FRAME 'gpu' figure for the real answer.");
    }
}

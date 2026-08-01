using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// Config-gated kill-switches applied while VR runs (ARCHITECTURE §3 / TOOLCHAIN R2):
/// data-driven by component type name so no compile-time reference to
/// Unity.Postprocessing.Runtime / ThirdParty is needed.
///
/// Phase 1 scope (registered last among the FEATURE modules on purpose — fixups on top of
/// everything else; <c>Core.DevModule</c> is appended after it and is inert unless
/// <c>[Dev] Enabled</c>, see <c>Plugin._modules</c>):
/// - <c>DisablePostProcessing</c> (default true): PPv2 <c>PostProcessLayer</c> +
///   <c>PostProcessVolume</c> (Unity.Postprocessing.Runtime.dll) — image-effect stack
///   unverified under stereo/MultiPass.
/// - <c>DisableVolumetricFog</c> (default true): <c>VolumetricFogAndMist.VolumetricFog</c>
///   (ThirdParty.dll; verified present in the decompiled sources) — image-effect fog,
///   known stereo hazard.
/// - <c>DisableComponents</c>: free-form extra type names (e.g. BeautifyEffect).
///
/// Components are re-disabled on every scene load (scenario scenes are additive).
/// Note: desktop OpenXR requires D3D11 — Unity 2021.3's Windows default. If the game
/// was forced onto another API, launch with <c>-force-d3d11</c> (docs/TESTING-P1.md).
/// </summary>
internal sealed class CompatModule : IVRModule
{
    public string Name => "Compat";

    private static readonly string[] PostProcessingTypes =
    [
        "UnityEngine.Rendering.PostProcessing.PostProcessLayer",
        "UnityEngine.Rendering.PostProcessing.PostProcessVolume"
    ];

    private const string VolumetricFogType = "VolumetricFogAndMist.VolumetricFog";

    private readonly List<Type> _typesToDisable = [];

    /// <summary>Everything we disabled, so hot-reload shutdown can re-enable it.</summary>
    private readonly List<Behaviour> _disabled = [];

    private bool _hooked;

    public void Init()
    {
        if (!VRSession.IsRunning)
        {
            VRLog.Debug(Name, "VR not running — compat kill-switches not applied.");
            return;
        }

        // Auto-skip the game's "press any key to continue" screen (InitialInputScreen) straight
        // into the main menu — self-contained Harmony patch, no-op if the game type is absent.
        VRSession.Harmony?.PatchAll(typeof(InitialInputSkip));

        // ISSUE #4 — walls, round 3 (whole-wall redesign; hardware falsified the per-pixel
        // approach — see WallFadeDisable / Core.WallSegmentFade headers):
        // 1. WallFadeDisable pins the GLOBAL shader gate int (ToggleWallFade) to 0
        //    UNCONDITIONALLY via a Harmony postfix on Main.Update — the game's per-pixel
        //    screen-space fade is never allowed to run wholesale in VR (its occlusion map is
        //    generated for the parked game camera, and even a correct head-camera map pops
        //    wall parts under fast head motion — hardware rounds 1+2).
        // 2. WallSegmentFade (gated LIVE by [Compat] WallFade) fades whole ProceduralWall
        //    segments instead: head-position occlusion decision with dwell hysteresis and a
        //    damped fade, delivered per renderer through MaterialPropertyBlocks that re-open
        //    the same shader gate with a substituted constant occlusion map (property
        //    precedence MPB > material > global). OFF = every wall bit-for-bit solid.
        // Both are VR-gated, reversible, live-togglable from the VR settings panel.
        VRSession.Harmony?.PatchAll(typeof(WallFadeDisable));
        WallSegmentFade.Install();

        // Tutorial VR bridge ([Compat] TutorialVRAdapt, default on): the tutorial's
        // camera-familiarization step waits on the flat room-camera button
        // (CameraRoomButtonPressed — its ONLY producer, RoomCameraButton.OnClick, is
        // unreachable while the P1 rig patches park the game camera), so the scripted hint
        // chain deadlocks right after the camera hint (hardware log
        // .planning/debug/tutorial/LogOutput.log:989). Three read-only/additive seams, all
        // runtime-gated to tutorial scenarios (TutorialVR.IsTutorialActive — never touches
        // normal play, refuses online sessions):
        // - TutorialFlowPatches: postfix diagnostics that dump every scripted message's
        //   display/dismiss trigger + loc keys (the tutorial data is an unreadable binary
        //   blob in the repo — the dump makes the next hardware run the proof).
        // - TutorialHintPatches: swaps camera-controls hint text (keyed by LOCALIZATION
        //   KEY, never display string) for VR movement instructions (Loc DE+EN).
        // - TutorialVR.NotifyLocomotion (fed by Rig.WorldGrab/SnapTurn): posts the game's
        //   own CameraRoomButtonPressed UIEvent once real VR locomotion happened while a
        //   tutorial trigger provably waits for it. Zero wire (the event's single
        //   subscriber is LevelEventsController).
        if (Plugin.TutorialVRAdapt.Value)
        {
            VRSession.Harmony?.PatchAll(typeof(LevelEventsController_StartListeningForEvents_Patch));
            VRSession.Harmony?.PatchAll(typeof(LevelEventsController_MessageWasDisplayed_Patch));
            VRSession.Harmony?.PatchAll(typeof(LevelEventsController_MessageWasDismissed_Patch));
            VRSession.Harmony?.PatchAll(typeof(LevelMessagePageUI_OnLanguageChanged_Patch));
            VRSession.Harmony?.PatchAll(typeof(LevelMessageUILayout_Title_Patch));
            VRLog.Info(Name, "Tutorial VR bridge armed — camera step completes from world-grab "
                + "locomotion, flat camera hints show VR movement text (tutorial scenarios only).");
        }

        var names = new List<string>();
        if (Plugin.DisablePostProcessing.Value)
            names.AddRange(PostProcessingTypes);
        if (Plugin.DisableVolumetricFog.Value)
            names.Add(VolumetricFogType);
        names.AddRange(Plugin.DisableComponents.Value
            .Split(',')
            .Select(n => n.Trim())
            .Where(n => n.Length > 0));

        foreach (string name in names)
        {
            Type? type = ResolveType(name);
            if (type == null)
                VRLog.Warn(Name, $"Component type not found (skipped): {name}");
            else if (typeof(Behaviour).IsAssignableFrom(type))
                _typesToDisable.Add(type);
            else
                VRLog.Warn(Name, $"Type is not a Behaviour (skipped): {name}");
        }

        if (_typesToDisable.Count == 0)
        {
            VRLog.Info(Name, "No compat kill-switches active.");
            return;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
        _hooked = true;
        ApplyKillSwitches();

        VRLog.Info(Name, $"Kill-switches armed for: {string.Join(", ", _typesToDisable.Select(t => t.Name))}.");
    }

    public void Shutdown()
    {
        // WallFadeDisable is a Harmony patch, removed on hot-reload by Plugin.OnDestroy's
        // _harmony.UnpatchSelf() (INVARIANTS §9) — NOT by anything on VRSession, which only
        // HOLDS the shared Harmony instance. There is no VRSession.UnpatchAll; UnpatchAll and
        // UnpatchSelf are different Harmony APIs with different blast radii, and this module's
        // whole design is "degrade cleanly", so the distinction is worth stating correctly.
        // Nothing to undo here for the patch; what this line DOES undo is the segment fade,
        // which clears every property block and destroys its textures.
        WallSegmentFade.Uninstall();
        if (_hooked)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _hooked = false;
        }

        int restored = 0;
        foreach (Behaviour behaviour in _disabled.Where(b => b != null))
        {
            behaviour.enabled = true;
            restored++;
        }
        if (restored > 0)
            VRLog.Info(Name, $"Re-enabled {restored} components on shutdown.");

        _disabled.Clear();
        _typesToDisable.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (VRSession.IsRunning)
            ApplyKillSwitches();
    }

    private void ApplyKillSwitches()
    {
        foreach (Type type in _typesToDisable)
        {
            int count = 0;
            foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
            {
                if (obj is Behaviour { enabled: true } behaviour)
                {
                    behaviour.enabled = false;
                    _disabled.Add(behaviour);
                    count++;
                }
            }
            if (count > 0)
                VRLog.Info(Name, $"Disabled {count}x {type.Name}.");
        }
    }

    /// <summary>Resolve a type by (assembly-qualified or plain) full name across loaded assemblies.</summary>
    private static Type? ResolveType(string name)
    {
        Type? type = Type.GetType(name, throwOnError: false);
        if (type != null)
            return type;

        string plainName = name.Split(',')[0].Trim();
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(plainName, throwOnError: false))
            .FirstOrDefault(t => t != null);
    }
}

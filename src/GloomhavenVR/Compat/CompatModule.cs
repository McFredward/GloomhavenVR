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
        // approach). Two halves, both VR-gated and reversible; the evidence and the rejected
        // per-pixel route are in the WallFadeDisable / Core.WallSegmentFade headers:
        // 1. WallFadeDisable pins the GLOBAL shader gate int (ToggleWallFade) to 0
        //    UNCONDITIONALLY via a Harmony postfix on Main.Update, so the game's per-pixel
        //    screen-space fade never runs wholesale in VR.
        // 2. WallSegmentFade (gated LIVE by [Compat] WallFade) fades whole ProceduralWall
        //    segments instead, per renderer through MaterialPropertyBlocks that re-open the same
        //    shader gate with a substituted constant occlusion map (property precedence
        //    MPB > material > global). OFF = every wall bit-for-bit solid.
        VRSession.Harmony?.PatchAll(typeof(WallFadeDisable));

        // PRIORITY (user report, ModBuild 232): "Ich konnte im Szenario den Multiplayer nicht mehr
        // starten. Wenn ich auf den button gedrückt habe, ist nichts passiert!" A STALE
        // UILoadoutManager.OnSwitchedToMultiplayer listener — armed by every single-player run of
        // the loadout screen and only ever removed in OnDestroy — throws a NullReferenceException
        // out of MPConfirmEnterScenario (UILoadoutManager.cs:516, a null
        // Singleton<UIMapMultiplayerController>) the next time the player hosts. UnityEvent.Invoke
        // has no per-listener catch, so that throw amputates every listener after it on
        // HostingStartedEvent — including the Host button's OWN completion callback. The guard
        // declines the stale listener; the watch makes this failure class loud forever and re-runs
        // whatever a future thrower amputates. Both are documented in their own headers.
        VRSession.Harmony?.PatchAll(typeof(LoadoutHostingGuard));
        HostingChainWatch.Install();
        // The GAME's own card particles are authored for its full-size 2D card and spray across
        // the diorama when a card is swept to a pile — pinned off through the game's own low-spec
        // switch (see CardParticlesOff; live-gated by [Cards] GameCardParticles).
        CardParticlesOff.Install();

        // PERF S1 (2026-08-09, .planning/perf-zoomed-out.md): the mod's periodic full-scene
        // FindObjectsOfType sweeps cost ~10-15 ms EACH in a 3000-renderer room and were the
        // measured cause of the 20-100 ms hitches. These three registries let the game enrol
        // its own occlusion volumes / door props / map tiles, and every reader below gets the
        // IDENTICAL set (same active + hideFlags filter) from a ten-entry list instead.
        // Installed BEFORE WallSegmentFade and the loader watchdog, which read them.
        // SceneRegistry.Install() arms the three enrolment postfixes itself and falls back to
        // the old sweep for any that could not be applied — an empty registry must be
        // impossible, because for the wall system it would read as "no rooms, no doors".
        SceneRegistry.Install();

        WallSegmentFade.Install();

        // Revealed-room geometry (fehlender_boden2.png root cause): Apparance synthesizes
        // map content around Camera.main, which the rig PARKS — a door-open reveal
        // re-creates the room's native entities (ApparanceEntity.CheckEntity destroys them
        // while hidden) and re-synthesis against the parked viewpoint never materializes
        // the floor/walls. The driver points the engine's own EnableDetailFocus/DetailFocus
        // override at the VR head instead. Reversible, no Harmony, rendering-only.
        ApparanceDetailFocus.Install();

        // Revealed-room geometry, round 4 (fehlender_boden3.png residue): the reveal-time
        // content now BUILDS (ApparanceDetailFocus) but its renderers can stay stuck in the
        // game MaterialLoader's mid-load state — active, disabled, materials never assigned
        // (hardware census: 130/133 renderers 'disabled' under the revealed tile). The
        // game's loader has no retry path at all (MaterialLoader.Start is its only trigger,
        // once per lifetime). This watchdog re-triggers/finishes stuck entries; see the
        // MaterialLoaderHeal header for the stranding mechanisms.
        MaterialLoaderHeal.Install();

        // The mirroring floor (user report 2026-08-15, spiegeltiles.jpg; ruling 2026-08-18 "Das
        // Wasser soll auf jeden Fall dargstellt werden - aber eben in einer VR-freundlichen
        // Variante. Einfach ausblenden ist keine Option."). A room whose hexes are the game's
        // WATER TERRAIN — 'TERRAIN_Water_Plane' on shader 'VFX/Water_Shd_Trans' (q2900), 17 quads
        // over a Crypt_Water basin — reads across a VR table as a mirror that swims with the head.
        // The mod's own census printed both causes on hardware (LogOutput.log:942): _Smoothness
        // 0.754 against a scene with liveProbes=0 and reflectionMode=Skybox, so the only thing the
        // surface can reflect is GH_Evil_Sky_MAT; and depthTextureMode=None on the head camera, so
        // the shader's depth fade and shore foam read an unwritten _CameraDepthTexture and pin the
        // whole quad at one extreme. WaterTerrainVR RETUNES those quads through a per-renderer
        // property block — the water always renders, and the 158 hide path is gone. Installed after
        // the loader heal so a just-healed water quad is retuned on the next tick.
        WaterTerrainVR.Install();
        // Round 7: loaders ENROLL THEMSELVES via this postfix on the game's
        // MaterialLoader.LoadMaterials — scanning for them proved unreliable twice
        // (Apparance parents generated content under HideAndDontSave containers that
        // FindObjectsOfType skips entirely). Bookkeeping only, vanilla path untouched.
        VRSession.Harmony?.PatchAll(typeof(MaterialLoader_LoadMaterials_RegisterPatch));

        // Tutorial VR bridge (unconditional since 2026-08-13): the tutorial's
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
        // UNCONDITIONAL since the 2026-08-13 user ruling: the [Compat] TutorialVRAdapt kill switch
        // is gone. Its OFF restored vanilla behaviour, and vanilla behaviour IS the deadlock this
        // bridge exists for, so it could only ever strand the tutorial. Scope is unchanged — every
        // seam below is still runtime-gated to tutorial scenarios.
        VRSession.Harmony?.PatchAll(typeof(LevelEventsController_StartListeningForEvents_Patch));
        VRSession.Harmony?.PatchAll(typeof(LevelEventsController_MessageWasDisplayed_Patch));
        VRSession.Harmony?.PatchAll(typeof(LevelEventsController_MessageWasDismissed_Patch));
        VRSession.Harmony?.PatchAll(typeof(LevelMessagePageUI_OnLanguageChanged_Patch));
        VRSession.Harmony?.PatchAll(typeof(LevelMessageUILayout_Title_Patch));
        // - TutorialChainHold: the sequencing gate for the mod-owned extra VR step
        //   (TutorialGrabStep). Prefix on LevelEventsController.ShowLevelMessage — the single
        //   funnel every scripted message passes through — so the tutorial's NEXT window
        //   (TB_11) waits until the player has actually taken a figure into their hand, instead
        //   of opening in the same ProcessEvent call that dismisses HT_10 and arms our step.
        //   Inert unless the step engages it; releases hand the message straight back.
        VRSession.Harmony?.PatchAll(typeof(TutorialChainHold));
        // - TutorialCameraSkip: postfix on LevelMessageUILayoutGroup.Show — the last statement of
        //   the handler's display coroutine. Once the VR controls lesson has run, the tutorial's
        //   two camera-introduction boxes (TB_2_2 / TB_3, which the lesson replaces) have their
        //   own Continue button pressed for the player (LevelMessageUILayout.CloseButtonPressed,
        //   the click's own handler), so the chain moves on through the box's own
        //   LevelMessageDismissed event and the player never reads a "nothing to do here" page
        //   (user ruling 2026-09-03). Gated to tutorial scenarios and to a lesson that ran.
        VRSession.Harmony?.PatchAll(typeof(LevelMessageUILayoutGroup_Show_Patch));
        VRLog.Info(Name, "Tutorial VR bridge armed — camera step completes from world-grab "
            + "locomotion, flat camera hints show VR movement text (tutorial scenarios only).");

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
        HostingChainWatch.Uninstall(); // drops the log hook and the driver GO — the Harmony guard goes with UnpatchSelf
        WallSegmentFade.Uninstall();
        ApparanceDetailFocus.Uninstall(); // restores the engine's authored viewpoint source
        MaterialLoaderHeal.Uninstall();   // healed loads are the game's own intended state — nothing to revert
        WaterTerrainVR.Uninstall();       // clears every property block we wrote — authored material back, bit-for-bit
        CardParticlesOff.Uninstall();     // restores the game's own NoCardsParticles value verbatim
        SceneRegistry.Shutdown();         // the enrolment postfixes go with UnpatchSelf — the lists must not outlive them
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

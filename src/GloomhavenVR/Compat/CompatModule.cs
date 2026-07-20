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
/// Phase 1 scope (registered last on purpose — fixups on top of everything else):
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

        // ISSUE #4 — occlusion stack. Registration ORDER MATTERS: sceneLoaded handlers fire in
        // subscription order, so per scene load we get census (pristine shipped state) → wall
        // depth enforcement → ZTest enforcement. All reflection-only, VR-gated, reversible.
        //  1. Occlusion census (always-on diagnostic): per-shader-group report of queue/ZWrite/ZTest
        //     over all world renderers — the evidence engine for the wall-ZWrite hypothesis.
        GlowOcclusion.InstallCensus();
        //  2. Walls: WallFadeDisable pins the GLOBAL fade shader int off (Harmony); WallSolidifier
        //     neutralizes the fade driver components (legacy, proven absent) AND — config-gated
        //     "SolidWallDepth", default on — forces wall materials to write depth at Geometry queue
        //     so transparent VFX/UI behind a wall can no longer paint over it.
        VRSession.Harmony?.PatchAll(typeof(WallFadeDisable));
        WallSolidifier.Install();
        //  3. ZTest enforcement — FULLY REVERTED at the user's request (it never fixed the real
        //     bleeders anyway: the census proved they expose no _ZTest property). Not installed.
        //  4. Occlusion probe (line-of-sight hide) — FULLY REVERTED: the user rejected ANY
        //     toggling ("flames must never go on/off"). Not installed. NOTE the persisted-config
        //     trap that bit us here: flipping a BepInEx default does NOT change an already-saved
        //     cfg value — the probe kept running from the old `OcclusionProbe = true` file. The
        //     only reliable off is not installing (this), not a default flip.
        //  5. Depth shader swap + TorchLightShadows + ActorBars bar depth-test — FULLY REVERTED:
        //     the Overlay/Amp_Basic replacements visibly changed the original look (darker unseen
        //     tiles, static moths, altered flames), which the user explicitly forbids. Not
        //     installed. The occlusion problem stays OPEN until a solution exists that preserves
        //     the original look 1:1 (e.g. exact-copy shader variants that only add ZTest).
        //     The always-on CENSUS above stays — evidence only, mutates nothing.

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
        // LIFO restore: the occlusion probe was installed last (and only toggles enabled flags),
        // so it unhooks first — before GlowOcclusion tears down the sweep driver it rides on.
        // Then ZTest enforcement (applied AFTER wall solidification) reverts before the walls,
        // so a renderer touched by both ends on WallSolidifier's true originals.
        DepthShaderSwap.Uninstall();
        OcclusionProbe.Uninstall();
        GlowOcclusion.Uninstall();
        WallSolidifier.Uninstall();

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

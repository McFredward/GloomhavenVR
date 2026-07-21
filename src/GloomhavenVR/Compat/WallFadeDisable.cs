using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 — the flat game fades/hides walls so its top-down camera can see behind them.
/// This patch owns HALF of the mechanism: the global int GATE <c>ToggleWallFade</c>. The wall
/// shaders only run their fade logic when it is <c>1</c> (verified in the DXBC disassembly of
/// <c>Amp_Basic_WallFade</c> / <c>Amp_Low/Amp_Basic_WallFade_Low</c>: the low variant's
/// fragment starts with <c>ine cb0[4].x, 0</c> on exactly this global; the game's own
/// DebugMenu "disable wall fade" simply sets it to <c>0</c>, decompiled
/// <c>GH.Runtime/DebugMenu.cs:1968</c>).
///
/// The gate is NOT the whole story (hardware round 1 falsified that theory): behind the gate,
/// the fade CONDITION samples the screen-space play-area occlusion map
/// <c>_TilesOcclusionMap</c> and discards the wall fragment when it is nearer than the play
/// area behind that pixel. That map is rendered per frame by the game's
/// <c>TilesOcclusionGenerator</c> CommandBuffer from the camera it runs on — in VR that is
/// the parked game camera, so the head camera's render has no valid map and the fade never
/// triggers even with the gate open. <see cref="Core.WallFadeOcclusionFeed"/> (installed by
/// <see cref="CompatModule"/>, same [Compat] WallFade toggle) supplies that missing input by
/// mirroring the generator's CommandBuffer onto the head camera. With BOTH halves in place a
/// wall fades exactly while it occludes the play area from the HMD, and un-fades when not —
/// the game's own behavior, driven by the player's real head.
///
/// [Compat] WallFade decides which way the global is pinned (consulted LIVE on every call,
/// so the settings-panel toggle applies instantly, no re-patching):
/// - OFF (default): pin <c>0</c> — walls always solid, the VR behavior so far.
/// - ON: pin <c>1</c> — the gate opens; with the occlusion feed active the game's own
///   view-dependent fade runs, following the HMD.
///
/// The global is natively asserted to <c>1</c> from several places: <c>Main.Start</c>
/// (<c>GH.Runtime/Main.cs:47</c>), <c>ActivateWallFadeInGame.Start/Update</c>, and
/// <c>ToggleWallTransparencyGlobal.Update</c>. Rather than chase every setter, this patch
/// postfixes <c>Main.Update()</c> — the core game singleton, present in every 3D scene and
/// ticked every frame — and re-asserts the configured value after the game's own logic runs.
/// That authoritatively pins the effect regardless of which component last wrote it and needs
/// no per-scene component to exist (asserting <c>1</c> also heals scenes where our earlier
/// <c>0</c> would otherwise stick because no game component re-raises it).
///
/// Fully reflection-guarded (<see cref="AccessTools"/>): if the <c>Main</c> type or its
/// <c>Update()</c> can't be found (renamed/removed by a game update), <see cref="TargetMethod"/>
/// returns null so Harmony patches nothing and wall fade is left 100% vanilla. Registered only
/// while VR runs (see <c>CompatModule.Init</c>), so desktop play is untouched; reversible on
/// hot-reload via Harmony <c>UnpatchAll</c>. Purely visual and local (a shader global) —
/// multiplayer peers never see a difference.
/// </summary>
[HarmonyPatch]
internal static class WallFadeDisable
{
    private const string MainTypeName = "Main";
    private const string GlobalIntName = "ToggleWallFade";

    private static readonly int _toggleWallFade = Shader.PropertyToID(GlobalIntName);
    private static bool _degraded;

    /// <summary>
    /// Harmony asks this for the method to patch. Resolving here (and returning null on any
    /// miss) is what degrades the whole patch to a strict no-op when the game type/method is
    /// absent.
    /// </summary>
    private static MethodBase? TargetMethod()
    {
        try
        {
            Type? main = AccessTools.TypeByName(MainTypeName);
            if (main == null || !typeof(MonoBehaviour).IsAssignableFrom(main))
                return Degrade($"type not found: {MainTypeName}");

            MethodInfo? update = AccessTools.Method(main, "Update");
            if (update == null)
                return Degrade($"method not found: {MainTypeName}.Update()");

            return update;
        }
        catch (Exception e)
        {
            return Degrade($"resolution threw: {e.Message}");
        }
    }

    /// <summary>
    /// Runs after the game's per-frame <c>Main.Update</c>. Pins the global wall-fade switch
    /// to the [Compat] WallFade choice — <c>0</c> (default, walls always solid) or <c>1</c>
    /// (the game's own HMD-following fade) — so any component that wrote it this frame
    /// (Main.Start, ActivateWallFadeInGame, ToggleWallTransparencyGlobal, DebugMenu) is
    /// overridden. Reading the config entry every call is what makes the settings-panel
    /// toggle live: no re-patching, the very next frame renders the new state.
    /// </summary>
    private static void Postfix()
    {
        try
        {
            int wanted = Plugin.WallFade != null && Plugin.WallFade.Value ? 1 : 0;
            if (Shader.GetGlobalInt(_toggleWallFade) != wanted)
                Shader.SetGlobalInt(_toggleWallFade, wanted);
        }
        catch (Exception e)
        {
            Degrade($"apply threw: {e.Message}");
        }
    }

    /// <summary>Log the first failure and thereafter stay silent; returns null for TargetMethod.</summary>
    private static MethodBase? Degrade(string reason)
    {
        if (!_degraded)
        {
            _degraded = true;
            VRLog.Warn("WallFadeDisable",
                $"disabled — {reason}. Wall see-through fade left vanilla.");
        }
        return null;
    }
}

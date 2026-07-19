using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 — the flat game fades/hides walls (and other props) so its top-down camera can
/// see behind them. The whole effect is gated by a single global shader int,
/// <c>ToggleWallFade</c>: the wall shaders only fade when it is <c>1</c>, and the game's own
/// DebugMenu "disable wall fade" simply sets it to <c>0</c> (decompiled
/// <c>GH.Runtime/DebugMenu.cs:1968</c>). In VR this see-behind fade is unwanted — walls should
/// always render solid.
///
/// The global is asserted to <c>1</c> from several places: <c>Main.Start</c>
/// (<c>GH.Runtime/Main.cs:47</c>), <c>ActivateWallFadeInGame.Start/Update</c>, and
/// <c>ToggleWallTransparencyGlobal.Update</c>. Rather than chase every setter, this patch
/// postfixes <c>Main.Update()</c> — the core game singleton, present in every 3D scene and
/// ticked every frame — and re-asserts <c>ToggleWallFade = 0</c> after the game's own logic
/// runs. That authoritatively pins the effect off regardless of which component last wrote it,
/// exactly as the game's DebugMenu disable would, and needs no per-scene component to exist.
///
/// Fully reflection-guarded (<see cref="AccessTools"/>): if the <c>Main</c> type or its
/// <c>Update()</c> can't be found (renamed/removed by a game update), <see cref="TargetMethod"/>
/// returns null so Harmony patches nothing and wall fade is left 100% vanilla. Registered only
/// while VR runs (see <c>CompatModule.Init</c>), so desktop play is untouched; reversible on
/// hot-reload via Harmony <c>UnpatchAll</c>.
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
    /// off so any component that re-enabled it this frame (Main.Start, ActivateWallFadeInGame,
    /// ToggleWallTransparencyGlobal, DebugMenu) is overridden and walls render solid.
    /// </summary>
    private static void Postfix()
    {
        try
        {
            if (Shader.GetGlobalInt(_toggleWallFade) != 0)
                Shader.SetGlobalInt(_toggleWallFade, 0);
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

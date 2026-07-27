using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 — the flat game fades/hides walls so its top-down camera can see behind them, via a
/// per-pixel screen-space discard in the wall shaders (<c>Amp_Basic_WallFade[_Low]</c>) gated by
/// the GLOBAL int <c>ToggleWallFade</c> (verified in the DXBC disassembly: the low variant's
/// fragment starts with <c>ine cb0[4].x, 0</c> on exactly this global; the game's own DebugMenu
/// "disable wall fade" simply sets it to <c>0</c>, decompiled <c>GH.Runtime/DebugMenu.cs:1968</c>).
///
/// IN VR the global gate must be pinned to <c>0</c> UNCONDITIONALLY — regardless of the
/// [Compat] WallFade toggle. Two hardware rounds established why:
/// <list type="number">
/// <item>The fade condition samples the screen-space <c>_TilesOcclusionMap</c>, rendered per
///   frame by <c>TilesOcclusionGenerator</c>'s CommandBuffer on the PARKED game camera — the
///   map is only valid for that camera's viewpoint. An open global gate makes every wall
///   sample a wrong-viewpoint map on the VR head camera (garbage per-pixel discards).</item>
/// <item>Even with a correct head-camera map (round 2's occlusion feed), the per-pixel
///   screen-space discard itself is unusable in VR: fast head movements make PARTS of walls
///   pop in and out — the mechanism is designed for a slow flat camera.</item>
/// </list>
///
/// The [Compat] WallFade toggle instead drives <see cref="Core.WallSegmentFade"/> (installed by
/// <see cref="CompatModule"/>): a WHOLE-WALL, temporally smoothed fade that re-opens the very
/// same shader gate PER RENDERER via MaterialPropertyBlocks (Unity property precedence
/// MPB &gt; material &gt; global) together with a substituted constant occlusion map — so only
/// walls the mod decided to fade run the shader's fade path, and they run it on view-stable
/// inputs. With the global pinned 0, every untouched wall renders bit-for-bit solid.
///
/// The global is natively asserted to <c>1</c> from several places: <c>Main.Start</c>
/// (<c>GH.Runtime/Main.cs:47</c>), <c>ActivateWallFadeInGame.Start/Update</c>, and
/// <c>ToggleWallTransparencyGlobal.Update</c>. Rather than chase every setter, this patch
/// postfixes <c>Main.Update()</c> — the core game singleton, present in every 3D scene and
/// ticked every frame — and re-asserts <c>0</c> after the game's own logic runs.
///
/// Fully reflection-guarded (<see cref="AccessTools"/>): if the <c>Main</c> type or its
/// <c>Update()</c> can't be found (renamed/removed by a game update), <see cref="TargetMethod"/>
/// returns null so Harmony patches nothing and wall fade is left 100% vanilla. Registered only
/// while VR runs (see <c>CompatModule.Init</c>), so desktop play is untouched; reversible on
/// hot-reload by <c>Plugin.OnDestroy</c>'s <c>_harmony.UnpatchSelf()</c>, which removes every
/// patch this mod applied and nothing else (INVARIANTS §9). Note it is <c>UnpatchSelf</c>, not
/// <c>UnpatchAll</c> — different Harmony APIs with different blast radii, and naming the wrong
/// one in the doc of the patch whose whole design is "degrade cleanly" invites a bad edit.
/// Purely visual and local (a shader global) — multiplayer peers never see a difference.
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
    /// Runs after the game's per-frame <c>Main.Update</c>. Pins the global wall-fade gate to
    /// <c>0</c> so any component that raised it this frame (Main.Start, ActivateWallFadeInGame,
    /// ToggleWallTransparencyGlobal, DebugMenu) is overridden — walls render solid unless
    /// <see cref="Core.WallSegmentFade"/> opens the gate per renderer.
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

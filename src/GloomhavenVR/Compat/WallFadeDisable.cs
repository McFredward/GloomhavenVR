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
/// <c>GH.Runtime/DebugMenu.cs:1968</c>). The fade CONDITION itself lives entirely in the wall
/// shaders (no C# feeds a position — the only CPU-side control is this 0/1 int), so it is
/// evaluated per rendering camera from the camera built-ins. In VR the one stereo camera is
/// the mod's own head-tracked <c>GloomhavenVR.HeadCamera</c> (VRRigDriver owns it; every game
/// camera is swept out of stereo by VRCameraPolicy), which means: with the global at <c>1</c>,
/// a wall fades exactly while the HMD's view angle onto it would occlude the play area, and
/// un-fades when not — the game's own behavior, driven by the player's real head.
///
/// [Compat] WallFade decides which way the global is pinned (consulted LIVE on every call,
/// so the settings-panel toggle applies instantly, no re-patching):
/// - OFF (default): pin <c>0</c> — walls always solid, the VR behavior so far.
/// - ON: pin <c>1</c> — the game's own view-dependent fade runs, now following the HMD.
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

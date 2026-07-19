using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Compat;

/// <summary>
/// ISSUE #4 (companion to <see cref="WallFadeDisable"/>) — makes walls authoritatively opaque.
///
/// <see cref="WallFadeDisable"/> only pins the GLOBAL shader int <c>ToggleWallFade = 0</c>. That
/// alone is insufficient because the fade has a second, independent gate: a PER-MATERIAL int
/// <c>_ToggleWallFadeLocal</c> that each wall's <c>ToggleWallFadeScript</c> sets to <c>1</c> in its
/// <c>Start()</c> whenever its public <c>ToggleWallFadeLocal</c> bool is false (which is the default)
/// — see decompiled <c>GH.Runtime/ToggleWallFadeScript.cs</c>. So walls fade locally regardless of
/// the global. In addition <c>ToggleWallTransparencyGlobal.Update()</c> re-asserts the global back to
/// its public <c>WallTransparencyEnabled</c> bool every frame (undefined MonoBehaviour update order →
/// it can beat our Main.Update postfix), and <c>ActivateWallFadeInGame</c> re-sets the global on its
/// manual toggles.
///
/// On every (additive) scenario scene load — and once at Init while VR runs, mirroring
/// CompatModule's own pattern — this scans the loaded scene by TYPE NAME via reflection only (no
/// compile-time refs to game types) and neutralizes all three drivers:
///   a. <c>ToggleWallTransparencyGlobal</c> — set <c>WallTransparencyEnabled = false</c> and disable
///      the Behaviour so it stops re-asserting the global to 1.
///   b. <c>ToggleWallFadeScript</c> — force each child renderer's <c>_ToggleWallFadeLocal = 0</c>
///      (the game's own code path, but with 0 → local fade OFF).
///   c. <c>ActivateWallFadeInGame</c> — disable the Behaviour so it doesn't re-set the global.
///
/// Strict no-op when VR isn't running. Every game-type lookup is reflection-guarded: a missing type
/// is logged once and skipped, never thrown. Reversible on <see cref="Uninstall"/> (hot-reload).
///
/// NOTE for follow-up (#4 torches/candles/glow): making walls truly opaque + depth-writing is
/// expected to fix props being visible through walls (walls fade → lit props behind show through).
/// If a future round shows candle/torch GLOW still bleeds through solid walls, that would indicate a
/// separate always-on-top / no-ZTest glow mechanism (not confirmed in the decompiled sources read
/// this round) — investigate then; do not add prop-specific hacks here.
/// </summary>
internal static class WallSolidifier
{
    private const string Name = "WallSolidifier";
    private const string GlobalIntName = "ToggleWallFade";
    private const string LocalIntName = "_ToggleWallFadeLocal";

    private const string TransparencyGlobalType = "ToggleWallTransparencyGlobal";
    private const string FadeScriptType = "ToggleWallFadeScript";
    private const string ActivateType = "ActivateWallFadeInGame";

    private static readonly int _globalProp = Shader.PropertyToID(GlobalIntName);
    private static readonly int _localProp = Shader.PropertyToID(LocalIntName);

    private static bool _hooked;
    private static readonly System.Collections.Generic.HashSet<string> _loggedMissing = [];

    /// <summary>Hook scene loads and run one immediate scan. Idempotent; no-op if VR isn't running.</summary>
    public static void Install()
    {
        if (_hooked || !VRSession.IsRunning)
            return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        _hooked = true;
        Scan();
    }

    /// <summary>Unhook scene loads (hot-reload safety). Does not attempt to restore game state.</summary>
    public static void Uninstall()
    {
        if (!_hooked)
            return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _hooked = false;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (VRSession.IsRunning)
            Scan();
    }

    private static void Scan()
    {
        if (!VRSession.IsRunning)
            return;

        try
        {
            int global = SafeGetGlobal();

            int transpCount = NeutralizeTransparencyGlobal();
            (int fadeCount, string? diag) = NeutralizeFadeScripts();
            int activateCount = NeutralizeActivate();

            VRLog.Info(Name,
                $"scan: {GlobalIntName} global={global}; found ToggleWallTransparencyGlobal={transpCount}, "
                + $"ToggleWallFadeScript={fadeCount}, ActivateWallFadeInGame={activateCount}.");

            if (diag != null)
                VRLog.Info(Name, diag);
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"scan threw (walls may fade): {e.Message}");
        }
    }

    /// <summary>Set WallTransparencyEnabled=false and disable each instance so it stops re-asserting the global.</summary>
    private static int NeutralizeTransparencyGlobal()
    {
        Type? type = ResolveType(TransparencyGlobalType);
        if (type == null)
        {
            LogMissingOnce(TransparencyGlobalType);
            return 0;
        }

        FieldInfo? field = AccessTools.Field(type, "WallTransparencyEnabled");
        int count = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            try { field?.SetValue(obj, false); }
            catch (Exception e) { VRLog.Warn(Name, $"WallTransparencyEnabled set failed: {e.Message}"); }

            if (obj is Behaviour b)
                b.enabled = false;
            count++;
        }
        return count;
    }

    /// <summary>Force each fade-script's child renderers to _ToggleWallFadeLocal=0. Returns count + a one-off diagnostic for the first wall renderer.</summary>
    private static (int count, string? diag) NeutralizeFadeScripts()
    {
        Type? type = ResolveType(FadeScriptType);
        if (type == null)
        {
            LogMissingOnce(FadeScriptType);
            return (0, null);
        }

        int count = 0;
        string? diag = null;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            count++;
            if (obj is not Component comp)
                continue;

            Renderer[] renderers;
            try { renderers = comp.GetComponentsInChildren<Renderer>(includeInactive: true); }
            catch { continue; }

            foreach (Renderer? rend in renderers)
            {
                Material? mat = rend != null ? rend.material : null;
                if (mat == null)
                    continue;

                // Emit the before/after diagnostic for the very first wall renderer we touch.
                if (diag == null)
                {
                    int before = SafeGetMatInt(mat);
                    mat.SetInt(_localProp, 0);
                    int after = SafeGetMatInt(mat);
                    string shader = mat.shader != null ? mat.shader.name : "<null-shader>";
                    diag = $"first wall renderer '{rend!.name}' shader='{shader}' "
                        + $"{LocalIntName}: before={before} after={after}.";
                }
                else
                {
                    mat.SetInt(_localProp, 0);
                }
            }
        }
        return (count, diag);
    }

    /// <summary>Disable each ActivateWallFadeInGame so it doesn't re-set the global on its manual toggles.</summary>
    private static int NeutralizeActivate()
    {
        Type? type = ResolveType(ActivateType);
        if (type == null)
        {
            LogMissingOnce(ActivateType);
            return 0;
        }

        int count = 0;
        foreach (UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type, includeInactive: true))
        {
            if (obj is Behaviour b)
                b.enabled = false;
            count++;
        }
        return count;
    }

    private static int SafeGetGlobal()
    {
        try { return Shader.GetGlobalInt(_globalProp); }
        catch { return -1; }
    }

    private static int SafeGetMatInt(Material mat)
    {
        try { return mat.GetInt(_localProp); }
        catch { return -1; }
    }

    private static void LogMissingOnce(string typeName)
    {
        if (_loggedMissing.Add(typeName))
            VRLog.Info(Name, $"type not found (skipped): {typeName}.");
    }

    /// <summary>Resolve a type by (assembly-qualified or plain) name across loaded assemblies.</summary>
    private static Type? ResolveType(string name)
    {
        Type? type = AccessTools.TypeByName(name);
        if (type != null)
            return type;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(asm => asm.GetType(name, throwOnError: false))
            .FirstOrDefault(t => t != null);
    }
}

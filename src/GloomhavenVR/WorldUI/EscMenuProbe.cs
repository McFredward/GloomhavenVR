using System;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// TEMPORARY diagnostic probe for the pause-menu REOPEN bug. The b31c286 log proved the
/// <c>ESCMenu</c> is genuinely DESTROYED on a mod X-close (<c>Singleton&lt;ESCMenu&gt;.IsInitialized</c>
/// = False AND a scene scan including inactive objects finds none), and only a scenario reload
/// re-creates it — so after one close, no X can reopen it.
///
/// The destroyer is NOT obvious from static analysis: <c>UIWindow.Hide()</c> only transitions
/// visual state (never destroys), and <c>CanvasConversion.Release</c> reparents the window back to
/// its original parent BEFORE destroying its float host. So something else tears the ESCMenu down.
/// This prefix logs the FULL managed call stack the instant <c>ESCMenu.OnDestroy</c> runs, so the
/// next hardware log names the exact destroyer (the mod's float/restore, a game teardown, or a
/// scene event) and the fix can be surgical. Remove once the reopen bug is fixed.
/// </summary>
[HarmonyPatch(typeof(ESCMenu), "OnDestroy")]
internal static class EscMenu_OnDestroy_Probe
{
    [HarmonyPrefix]
    private static void Prefix(ESCMenu __instance)
    {
        // Log the parent chain at destroy time: if the menu is still a child of a mod float host
        // ("GloomhavenVR.Panel_...") the CanvasConversion.Release host-destroy cascade killed it
        // (now fixed by detaching unconditionally). If it's under a game parent instead, the game
        // itself tore it down and the fix must move elsewhere.
        string parents = "(none)";
        var t = __instance != null ? __instance.transform.parent : null;
        if (t != null)
        {
            parents = t.name;
            if (t.parent != null) parents += " < " + t.parent.name;
        }
        VRLog.Warn("WorldUI", $"ESCMenu.OnDestroy — pause menu DESTROYED while parented under '{parents}'. " +
                              "If that is a 'GloomhavenVR.Panel_' host, the Release cascade was the cause.\n" +
                              Environment.StackTrace);
    }
}

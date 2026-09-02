using System;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// NO EXIT CONTROL MAY BE LEFT LATCHED — the second deadlock of the 2026-09-02 round (user:
/// "NOCH EIN DEADLOCK ENTDECKT: Ich konnte aus dem Tutorial heraus nicht mehr ins Hauptmenu
/// zurückkehren, da das Fenster das mich normalerweise fragt nicht mehr angezeigt wurde.
/// Vollzieh das nach und verhindere so etwas").
///
/// <para><b>THE MECHANISM IS ONE LINE OF THE GAME'S</b> (decompiled UIMenuOption.cs:120-127):</para>
/// <code>
/// public virtual void Select()
/// {
///     if (isSelected) return;          // &lt;-- HERE
///     SetSelected(isSelected: true);
///     selectAnimation.Play();
///     onSelected?.Invoke();            // == ESCMenu.LoadMainMenu (ESCMenu.cs:92)
/// </code>
/// <c>isSelected</c> is set true by <c>Select()</c> itself and cleared <b>only</b> by
/// <c>Deselect()</c>, which the game wires as the confirmation dialog's <b>Cancel</b> callback
/// (ESCMenu.cs:400/414 for "Hauptmenü", :450/464 for "Beenden"). So any route that closes that
/// confirmation WITHOUT running the game's own "Nein" leaves the row latched — and from then on
/// every click on it is a guaranteed silent no-op. No log, no exception, nothing: the click is
/// delivered, the handler returns on its first statement, and the player is looking at a dead
/// button. His "<i>nicht mehr</i> angezeigt" — no LONGER — is exactly that shape: it worked once
/// and never again.
///
/// <para><b>AND THE MOD ADDS TWO SUCH ROUTES TO THAT WINDOW</b>: the floated modal's own close-X
/// plate and the escape chord. The 2026-09-02 log shows three <c>uGUI click: 'Main Menu'</c> and
/// nothing at all after any of them — no confirmation, no menu hide, no scene load, no throw.
/// <c>LoadMainMenu</c> demonstrably never ran; its first statement unpauses the 3D world and both
/// branches of it do something visible.</para>
///
/// <para><b>THE FIX IS NOT TO REMOVE THOSE ROUTES</b> — they are the rescue for a floated window,
/// and the standing ruling is that it must ALWAYS be possible to get out of a menu. The fix is to
/// make the latch impossible: a freshly opened pause menu has no business remembering a selected
/// row, so every exit row is cleared on every open. <c>SetSelected(false)</c> rather than
/// <c>Deselect()</c>, because <c>Deselect()</c> also invokes <c>onDeselected</c>, which for these
/// rows moves the EventSystem focus.</para>
///
/// <para>ONE PATCH POINT COVERS BOTH MENUS: <c>UIScenarioEscMenu.OnShow</c> calls
/// <c>base.OnShow()</c> (UIScenarioEscMenu.cs:290-293), so a postfix on <c>ESCMenu.OnShow</c>
/// runs for the scenario menu too.</para>
///
/// <para><b>THE GENERAL FORM, for the next round:</b> a click that is DELIVERED is not an action
/// that RAN. The mod logs <c>uGUI click</c>; for a control that is the player's only way out, it
/// has to be able to say the action actually fired.</para>
/// </summary>
[HarmonyPatch(typeof(ESCMenu), "OnShow")]
internal static class ESCMenu_OnShow_LatchGuard_Patch
{
    private static void Postfix(ESCMenu __instance)
    {
        try
        {
            int cleared = 0;
            cleared += Clear(__instance.mainMenuButton);
            cleared += Clear(__instance.exitButton);
            if (__instance is UIScenarioEscMenu scenario)
                cleared += Clear(scenario.quitDungeonButton);
            if (cleared == 0)
                return;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a
            // tier the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py
            // enforces it.
            VRLog.Note("WorldUI", $"PAUSE MENU EXIT LATCH: cleared {cleared} exit row(s) that were "
                + "still marked selected when the pause menu opened. UIMenuOption.Select() refuses "
                + "to invoke its action while isSelected is true, so each of those rows would have "
                + "been a DEAD button — clicked, delivered, and silently doing nothing. A non-zero "
                + "count here means the previous confirmation dialog was closed by something other "
                + "than the game's own Cancel button (the mod's modal X or the escape chord), which "
                + "is the 2026-09-02 'ich konnte nicht mehr ins Hauptmenu' deadlock.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", "PAUSE MENU EXIT LATCH guard threw: "
                + $"{ex.GetType().Name}: {ex.Message}. The rows are left as the game had them.");
        }
    }

    private static int Clear(UIMainMenuOption? row)
    {
        if (row == null || !row.IsSelected)
            return 0;
        row.SetSelected(false);
        return 1;
    }
}

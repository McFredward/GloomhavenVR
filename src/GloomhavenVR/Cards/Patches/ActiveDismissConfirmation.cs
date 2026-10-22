using System;
using HarmonyLib;
using UnityEngine.Events;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// Replace only the synchronous native popup requested while a physical active card is placed
/// in the board recess. The maintainer requested the same place-then-confirm flow as items, with
/// one explicit cap after releasing the card. Capturing the original closure preserves its exact
/// cancellation, native UI refresh, choreography and multiplayer behavior without copying any
/// game-state writes. This prefix never confirms anything; leaving the scope restores ordinary
/// flat, gamepad and unrelated VR confirmations even if preparation throws.
/// </summary>
[HarmonyPatch(typeof(UIConfirmationBoxManager), nameof(UIConfirmationBoxManager.ShowCancelActiveAbility))]
internal static class ActiveDismissConfirmation
{
    [ThreadStatic]
    private static CardsGameApi.ActiveCardDismissal? _capture;

    internal static bool Capture(CardsGameApi.ActiveCardDismissal pending, Action request)
    {
        if (_capture != null)
            return false;
        _capture = pending;
        try
        {
            request();
            return pending.IsPending;
        }
        finally
        {
            _capture = null;
        }
    }

    private static bool Prefix(UIConfirmationBoxManager __instance, UnityAction onActionConfirmed,
                               UnityAction onCancelled)
        => _capture == null || !_capture.Capture(__instance, onActionConfirmed, onCancelled);
}

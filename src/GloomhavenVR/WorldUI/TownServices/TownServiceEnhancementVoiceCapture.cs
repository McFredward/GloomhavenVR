using System;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Attach speech to the original enhancement decision, never to hovering or
/// card inspection. The native action still owns payment, selection and continuation.</summary>
[HarmonyPatch(typeof(UIEnhancementConfirmationBox), nameof(UIEnhancementConfirmationBox.ShowConfirmation),
    new[] { typeof(string), typeof(string), typeof(Sprite), typeof(string), typeof(Action), typeof(string), typeof(string), typeof(Action) })]
internal static class TownServiceEnhancementVoiceCapture
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref Action onActionConfirmed)
    {
        if (!TownServiceEnhancementHandoff.HasCurrentOffering) return;
        Action native = onActionConfirmed;
        onActionConfirmed = () =>
        {
            bool offered = TownServiceEnhancementHandoff.HasCurrentOffering;
            native();
            if (offered) TownServiceVoice.RequestReaction(3, TownVoiceReaction.EnchantressEnhance);
        };
    }
}

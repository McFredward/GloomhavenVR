using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// Build617's long native ActionSelection callback calls FullAbilityCard.UpdateView on every
/// hand widget. Its private UpdateScale/UpdatePosition bodies only write the flat hand's root
/// transform. CardFace.Maintain immediately repairs those writes on an adopted VR face: they
/// never constitute the VR layout or hover animation, but dirty the canvas and change its fit
/// temporarily. Keep ViewSettings and every native hover/event/hand/validity callback intact;
/// suppress only these two redundant geometry writes while the actual VR host still owns it.
/// This removes proven presentation churn, not the whole measured 207 ms native callback.
/// </summary>
internal static class AdoptedCardLayout
{
    internal static bool OwnsGeometry(FullAbilityCard? face)
    {
        if (!VRSession.IsRunning || !HandSuppression.Active || !CardArtGuard.IsAdopted(face))
            return false;
        if (face!.ViewSettings == null)
            return false; // Invalid native settings must retain their original failure path.
        // An adopted face can be handed to a native dialog before Maintain sees that move.
        // The registry alone is insufficient; yielded/returned/dialog-owned faces must keep
        // their original geometry. Likewise, never suppress a remote prefab clone's writers.
        VRCard? owner = face!.GetComponentInParent<VRCard>(includeInactive: true);
        return owner != null && owner.HasAdoptedFace && ReferenceEquals(owner.FullCard, face);
    }
}

[HarmonyPatch(typeof(FullAbilityCard), "UpdateScale")]
internal static class FullAbilityCard_UpdateScale_AdoptedLayout
{
    private static bool Prefix(FullAbilityCard __instance) => !AdoptedCardLayout.OwnsGeometry(__instance);
}

[HarmonyPatch(typeof(FullAbilityCard), "UpdatePosition")]
internal static class FullAbilityCard_UpdatePosition_AdoptedLayout
{
    private static bool Prefix(FullAbilityCard __instance) => !AdoptedCardLayout.OwnsGeometry(__instance);
}

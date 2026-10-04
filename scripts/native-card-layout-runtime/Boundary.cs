using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
}
namespace GloomhavenVR.Cards
{
    // Ownership is the external seam. Original FullAbilityCard, its ViewSettings,
    // private transform writers, hover event and all Unity hierarchy are real.
    internal static class HandSuppression { internal static bool Active = true; }
    internal static class CardArtGuard
    {
        internal static readonly HashSet<FullAbilityCard> Adopted = new();
        internal static bool IsAdopted(FullAbilityCard? face) => face != null && Adopted.Contains(face);
    }
    internal sealed class VRCard : MonoBehaviour
    {
        internal bool HasAdoptedFace;
        internal FullAbilityCard? FullCard;
    }
}

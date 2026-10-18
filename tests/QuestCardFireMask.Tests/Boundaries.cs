using UnityEngine;
using UnityEngine.UI;

public sealed class FullAbilityCard : MonoBehaviour
{
    public CardEffects? cardEffects;
    public bool Adopted = true;
}
public sealed class CardEffects : MonoBehaviour
{
    public bool initialized = true;
    public Image[] imgComp = System.Array.Empty<Image>();
    public Image? _headerImage, _uiFxOverlay;
    public Texture? overlayFrameBurn;
}
namespace GloomhavenVR.Cards
{
    internal static class CardArtGuard
    {
        internal static bool IsAdopted(FullAbilityCard face) => face.Adopted;
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    internal static class VRLog
    {
        internal static void Warn(string area, string message) => UnityEngine.Debug.LogWarning(message);
        internal static void Note(string area, string message) => UnityEngine.Debug.Log(message);
    }
}

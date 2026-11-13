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
        internal static void Debug(string area, string message) => UnityEngine.Debug.Log(message);
        internal static void Note(string area, string message) => UnityEngine.Debug.Log(message);
    }
}

public sealed class ItemCardUI : MonoBehaviour
{
    public ItemCardEffects? cardEffects;
}
public sealed class ItemCardEffects : MonoBehaviour
{
    public Vector2 cardBounds;
    public Image[] imgComp = System.Array.Empty<Image>();
    public Texture? overlayFrameBurn, overlayFrameGhost;
    private Image? fgFx;
    public void SetNativeOverlay(Image image) => fgFx = image;
    public Image? NativeOverlay => fgFx;
}
namespace GloomhavenVR.Net
{
    internal sealed class FixtureNode { internal int Role, Flags; }
    internal static class NativePlaybackProperties { internal const uint ParticleMask = 1u << 18; }
    internal static class CardAppearanceBindings { internal static readonly int Particle = Shader.PropertyToID("_ParticleTexture"); }
    internal sealed class FixtureFlameBindings { internal Texture? BurnTexture, GhostTexture; }
    internal sealed partial class RemoteCardArt
    {
        private enum CardFxLook { None, Burn, Ghost }
        private const int FlameQueueCeiling = 3090;
        private int _faceQueue = 3000;
        private readonly GloomhavenVR.Cards.QuestCardFireMaskBindings _questFlameMasks = new();
        private readonly System.Collections.Generic.List<Material> _ownedMaterials = new();
        private Texture? _flameBurnTexture, _flameGhostTexture;
        private readonly FixtureFlameBindings _nativeBindings = new();
        private ItemCardEffects? _itemNativeSource;
        internal bool ItemCallbackSawNumeric;
        private void MeasureFaceQueue(Image image)
        { ItemCallbackSawNumeric = GloomhavenVR.Cards.QuestCardFireMask.Matches(image.material.GetTexture("_ParticleTexture"), _itemNativeSource?.overlayFrameBurn); }
        internal void Configure(Texture burn, Texture ghost, ItemCardEffects source)
        {
            _flameBurnTexture = _nativeBindings.BurnTexture = burn;
            _flameGhostTexture = _nativeBindings.GhostTexture = ghost;
            _itemNativeSource = source;
        }
        internal void Own(Material material) => _ownedMaterials.Add(material);
        internal void LegacyFlame(Material material, bool ghost) => WriteFlameConstants(material, ghost ? CardFxLook.Ghost : CardFxLook.Burn);
        internal void Release() => ReleaseOwnedMaterials();
        internal void ReleaseItemMaterials(ActualItemMaterials items) => items.Destroy(_questFlameMasks.Release);
    }
}

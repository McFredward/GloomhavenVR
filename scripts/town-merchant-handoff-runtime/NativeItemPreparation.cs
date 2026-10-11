using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using UnityEngine;

// Explicit game/addressable boundaries. The item source, inactive borrow/finally,
// immutable catalogue walk and actual sprite binding compile from production.
public sealed class NativeSpriteReference { public Sprite? Sprite; }
public sealed class NativeItemDefinition
{ public string Art = "OriginalItemBackground"; public readonly List<int> ValidEquipCharacterClassIDs = new() { 1 }; }
public sealed class UIInfoTools
{
    public static UIInfoTools? Instance;
    public readonly NativeSpriteReference Background = new(), Icon = new();
    public NativeSpriteReference GetItemBackgroundSprite(string art) => Background;
    public NativeSpriteReference GetCharacterAssemblyIcon(int id) => Icon;
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceNativeAssets
    { internal static NativeItemDefinition? FindItemData(int id) => id > 0 ? new NativeItemDefinition() : null; }
}
namespace GloomhavenVR.Cards
{
    internal static class CardArtPin
    {
        internal static readonly HashSet<NativeSpriteReference> Requested = new();
        internal static void PinReference(NativeSpriteReference reference) => Requested.Add(reference);
        internal static Sprite? PreparedSprite(NativeSpriteReference reference, out bool pending)
        { pending = reference.Sprite == null; return reference.Sprite; }
    }
}
namespace GloomhavenVR.Net
{
    internal static class RevealGate { internal static bool InScenario; }
    // The generic art backend remains a boundary here: this test isolates cold source
    // readiness and exact original sprite selection, not card fitting or mip rendering.
    internal sealed class RemoteCardArt
    {
        internal enum SpentLook { None, Spent, Consumed }
        internal GameObject? Clone;
        private int _key = int.MinValue;
        internal bool ShowsKey(int key) => Clone != null && key == _key;
        internal void MaintainMipBake() { }
        internal void HideFront() { if (Clone != null) UnityEngine.Object.DestroyImmediate(Clone); Clone = null; _key = int.MinValue; }
        internal bool ShowFront(GameObject source, int key, object? skinSource, Action<GameObject> beforeActivate, SpentLook spentLook)
        {
            var host = new GameObject("InactiveArtBoundary"); host.SetActive(false);
            Clone = UnityEngine.Object.Instantiate(source, host.transform, false);
            beforeActivate(Clone); Clone.SetActive(true); host.SetActive(true); _key = key;
            return true;
        }
    }
}
public static partial class NativeItemPreparationProof
{
    public static void Run(Action<bool, string> check)
    {
        var tools = new UIInfoTools(); UIInfoTools.Instance = tools;
        CardArtPin.Requested.Clear(); RevealGate.InScenario = false;
        var source = new CItem(9001); var art = new RemoteCardArt();
        int spawns = ObjectPool.Spawns;
        check(!RemoteItemCardSource.PrepareMapItemForLoading(source.ID),
            "cold native item pins cannot report loading readiness before original art is resident");
        check(CardArtPin.Requested.Contains(tools.Background) && !CardArtPin.Requested.Contains(tools.Icon),
            "cold preparation begins the original background before dependent class artwork");
        check(!RemoteItemCardSource.ShowFace(art, source) && art.Clone == null,
            "cold public map source builds no grey backing clone on first reveal");
        check(ObjectPool.Spawns == spawns + 1, "cold preparation performs one inactive canonical native borrow");
        tools.Background.Sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
        check(!RemoteItemCardSource.PrepareMapItemForLoading(source.ID) && CardArtPin.Requested.Contains(tools.Icon),
            "resident background alone cannot report readiness while original class icon is still cold");
        tools.Icon.Sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
        check(RemoteItemCardSource.PrepareMapItemForLoading(source.ID) && ObjectPool.Spawns == spawns + 1,
            "warm original sprites reuse the prepared native pool without another borrow");
        check(RemoteItemCardSource.ShowFace(art, source) && art.Clone != null,
            "warm public map source builds its original face on the first reveal");
        check(art.Clone!.GetComponent<ItemCardUI>() == null
            && art.Clone.GetComponent<UnityEngine.UI.Image>().sprite == tools.Background.Sprite
            && art.Clone.transform.Find("OriginalClassIcon").GetComponent<UnityEngine.UI.Image>().sprite == tools.Icon.Sprite,
            "native map face keeps original engine sprites with no active gameplay or navigation controller");
        spawns = ObjectPool.Spawns; ObjectPool.instance!.ClearItemPools();
        check(RemoteItemCardSource.PrepareMapItemForLoading(source.ID) && ObjectPool.Spawns == spawns + 1,
            "same singleton cleared item pools rewarm instead of trusting old readiness");
        art.HideFront(); UIInfoTools.Instance = null;
    }
}

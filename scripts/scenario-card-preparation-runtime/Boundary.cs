using System;
using System.Collections.Generic;
using UnityEngine;

// Only native game and Addressables dependencies are boundaries. The tested scheduler,
// pin ownership, mip/readback/cache, and wrapper reservation code are full production files.
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static readonly List<string> Lines = new();
        internal static void Debug(string scope, string text) => Lines.Add(text);
        internal static void Info(string scope, string text) => Lines.Add(text);
        internal static void Warn(string scope, string text) => Lines.Add(text);
        internal static void Alert(string scope, string text) => Lines.Add(text);
        internal static void Note(string scope, string text) => Lines.Add(text);
    }
    internal static class PerfMonitor
    {
        internal readonly struct Measure : IDisposable { public void Dispose() { } }
        internal static Measure Scope(string name) => new();
        internal static void Count(string name) { }
    }
    internal static class TickGuard { internal static void Run(string name, Action action, string scope) => action(); }
    internal static class BundleDiagnostics { internal static string Explain(string path) => "fixture procedural fallback"; }
}
namespace GloomhavenVR.WorldUI { internal static class FixtureNamespace { } }
namespace GloomhavenVR.Cards
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal static class CardsConfig
    {
        internal static readonly Setting<bool> FaceMipBake = new(true);
        internal static readonly Setting<ControlBoard> Board = new(ControlBoard.Oak);
    }
    internal enum ControlBoard { Oak, Steel, Bronze }
    internal static class CardsDriver { internal static bool NativeSceneLoadInProgress; }
    internal static class CardsGameApi { internal static string CardName(AbilityCardUI card) => card.name; }
    internal static class CardArtPrewarm { internal static void NoteFanOpened() { } }
    internal static class CardFace { internal static void Offer(Component root) { } internal static void MaintainArtArrival() { } }
    internal static class CardFxBounds { internal static void Reseat(Component root) { } }
    internal sealed class VRCard : MonoBehaviour
    {
        internal static int Builds, NativeAdoptions;
        internal AbilityCardUI? GameCard;
        internal bool HasAdoptedFace, Grabbable;
        internal void Build(GameObject? prefab) { Builds++; }
        internal bool AttachGameCard(AbilityCardUI widget) { GameCard = widget; HasAdoptedFace = true; NativeAdoptions++; return true; }
        internal void DetachGameCard() { GameCard = null; HasAdoptedFace = false; }
        internal void ReturnBorrowedFaceForSceneLoad() => HasAdoptedFace = false;
        internal void ForgetActionHighlight() { }
        internal void SetHome(Transform parent, Vector3 pos, Quaternion rotation, float scale, bool instant) => transform.SetParent(parent, false);
    }
}
namespace ScenarioRuleLibrary
{
    public sealed class CAbilityCard
    {
        public string ClassModel = "";
        public string ClassCharacterConfig = "";
    }
    public sealed class CCharacterClass
    {
        public List<CAbilityCard> AbilityCardsPool = new();
        public List<CAbilityCard> SelectedAbilityCards = new();
        public List<CAbilityCard> HandAbilityCards = new();
    }
    public sealed class CPlayerActor { public CCharacterClass CharacterClass = new(); }
    public sealed class FixtureScenario { public List<CPlayerActor> PlayerActors = new(); }
    public static class ScenarioManager { public static FixtureScenario? Scenario; }
}
public sealed class AbilityCardUI : MonoBehaviour { }
public sealed class CardsHandUI : MonoBehaviour { public readonly List<AbilityCardUI> cardsUI = new(); }
public sealed class AbilityCardUISkin
{
    public Sprite? defaultTopActionRegularSprite, defaultBottomActionRegularSprite;
    public SpriteMemoryManagement.ReferenceToSprite? TitleSprite, TopActionRegularSprite, BottomActionRegularSprite;
    public SpriteMemoryManagement.ReferenceToSprite? TopActionHighlightSprite;
}
public sealed class ElementConfigUI : ScriptableObject { public Sprite? useIcon; }
public sealed class UIInfoTools : MonoBehaviour
{
    [Serializable] public struct EffectInfo { public Sprite Icon; }
    public static UIInfoTools? Instance;
    public string Focus = "first";
    public Sprite? AugmentIcon;
    public ElementConfigUI? darkConfig;
    public EffectInfo Shield;
    private Sprite[] activeAbilityIcons = Array.Empty<Sprite>();
    public readonly Dictionary<string, AbilityCardUISkin> Skins = new();
    public AbilityCardUISkin GetCardSkin(string model, string custom) => Skins[model];
    public void InstallIcons(Sprite sprite) => activeAbilityIcons = new[] { sprite };
    public Sprite[] ReadIcons() => activeAbilityIcons;
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public sealed class FixtureLoad<T>
    {
        public bool Valid = true, Done;
        public T Result = default!;
        public AsyncOperationStatus Status;
    }
    public readonly struct AsyncOperationHandle<T>
    {
        private readonly FixtureLoad<T>? load;
        public AsyncOperationHandle(FixtureLoad<T> load) { this.load = load; }
        public bool IsValid() => load != null && load.Valid;
        public bool IsDone => load != null && load.Done;
        public AsyncOperationStatus Status => load?.Status ?? AsyncOperationStatus.None;
        public T Result => load != null ? load.Result : default!;
        public FixtureLoad<T> Load => load!;
    }
}
namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;
    public sealed class AssetReferenceSprite
    {
        public string AssetGUID;
        public bool NativeOperationTouched;
        public AssetReferenceSprite(string guid) { AssetGUID = guid; }
    }
    public static class Addressables
    {
        public static readonly Dictionary<string, Sprite> Art = new();
        public static readonly Dictionary<string, FixtureLoad<Sprite>> Loads = new();
        public static int Requests, Releases;
        public static bool AutoComplete = true;
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(object key)
        {
            if (typeof(T) != typeof(Sprite) || key is not string guid) throw new Exception("GUID-only sprite pin required");
            Requests++;
            var load = new FixtureLoad<Sprite>();
            if (Art.TryGetValue(guid, out Sprite sprite)) { load.Result = sprite; load.Status = AsyncOperationStatus.Succeeded; }
            else load.Status = AsyncOperationStatus.Failed;
            load.Done = AutoComplete;
            Loads[guid] = load;
            return (AsyncOperationHandle<T>)(object)new AsyncOperationHandle<Sprite>(load);
        }
        public static void Release<T>(AsyncOperationHandle<T> handle) { Releases++; handle.Load.Valid = false; }
    }
}
namespace SpriteMemoryManagement
{
    public sealed class ReferenceToSprite
    {
        public Sprite SpecialSprite = null!;
        public UnityEngine.AddressableAssets.AssetReferenceSprite SpriteReference = null!;
        public bool InitializedWithSpecialSprite => SpecialSprite != null;
        public ReferenceToSprite(Sprite sprite) { SpecialSprite = sprite; }
        public ReferenceToSprite(string guid) { SpriteReference = new(guid); }
    }
}

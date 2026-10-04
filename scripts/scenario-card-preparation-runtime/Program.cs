using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using SpriteMemoryManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DiagnosticProgram
{
    public static int Checks;
    public static string Metrics = "";
    private static readonly List<Object> Assets = new();
    private static readonly List<Sprite> ExpectedArt = new();
    private static VRCardFactory? factory;
    private static void Check(bool value, string reason) { Checks++; if (!value) throw new Exception(reason); }
    private static Sprite Art(string name, int seed)
    {
        var texture = new Texture2D(16, 32, TextureFormat.RGBA32, false) { name = name + "-atlas" };
        Assets.Add(texture);
        var pixels = new Color32[16 * 32];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 16; x++)
            pixels[y * 16 + x] = new Color32((byte)(30 + (x * 7 + seed * 5) % 200), (byte)(30 + y * 6), (byte)(40 + seed * 20), 255);
        texture.SetPixels32(pixels); texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(2, 3, 10, 24), new Vector2(.3f, .7f), 64, 0, SpriteMeshType.FullRect, new Vector4(1, 2, 3, 4));
        sprite.name = name; Assets.Add(sprite); ExpectedArt.Add(sprite); return sprite;
    }
    private static ReferenceToSprite Reference(string guid, int seed)
    {
        Addressables.Art[guid] = Art(guid, seed); return new ReferenceToSprite(guid);
    }
    private static AbilityCardUISkin Skin(string name, int seed) => new()
    {
        TitleSprite = Reference(name + "-title", seed),
        TopActionRegularSprite = Reference(name + "-top", seed + 1),
        BottomActionRegularSprite = Reference(name + "-bottom", seed + 2),
        TopActionHighlightSprite = Reference(name + "-highlight", seed + 3),
        defaultTopActionRegularSprite = Art(name + "-default", seed + 4),
    };
    private static CAbilityCard Card(string model) => new() { ClassModel = model };
    private static void Expire()
    {
        typeof(ScenarioCardPreparation).GetField("s_deadline", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Time.realtimeSinceStartup - 1f);
    }
    private static Color32[] Read(Texture texture)
    {
        RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture? previous = RenderTexture.active;
        var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        try { Graphics.Blit(texture, rt); RenderTexture.active = rt; copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); return copy.GetPixels32(); }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(copy); }
    }
    private static IEnumerator Complete()
    {
        for (int frames = 0; !ScenarioCardPreparation.IsReady; frames++)
        {
            int before = ScenarioCardPreparation.SpritesPrepared;
            int builds = VRCard.Builds;
            ScenarioCardPreparation.Tick();
            int prepared = ScenarioCardPreparation.SpritesPrepared;
            int reserved = VRCard.Builds;
            for (int offer = 0; offer < 10; offer++) ScenarioCardPreparation.Tick();
            Check(prepared == ScenarioCardPreparation.SpritesPrepared && reserved == VRCard.Builds,
                "repeated same-frame loader offers cannot multiply preparation work");
            Check(ScenarioCardPreparation.SpritesPrepared - before <= 1 && VRCard.Builds - builds <= 1,
                "loading preparation performs at most one expensive sprite or backing per frame");
            Check(frames < 250, "bounded preparation reaches readiness");
            yield return null;
        }
    }
    public static IEnumerator Run()
    {
        CardsDriver.NativeSceneLoadInProgress = false;
        var tools = new GameObject("native-tools").AddComponent<UIInfoTools>(); UIInfoTools.Instance = tools;
        ScenarioManager.Scenario = new FixtureScenario();
        for (int i = 0; i < 3; i++)
        {
            string name = "class-" + i; tools.Skins[name] = Skin(name, i + 1);
            var player = new CPlayerActor();
            for (int n = 0; n < 5 + i * 2; n++)
            {
                CAbilityCard card = Card(name); player.CharacterClass.SelectedAbilityCards.Add(card);
                player.CharacterClass.HandAbilityCards.Add(card); player.CharacterClass.AbilityCardsPool.Add(card);
            }
            ScenarioManager.Scenario.PlayerActors.Add(player);
        }
        tools.Skins["borrowed"] = Skin("borrowed", 7);
        ScenarioManager.Scenario.PlayerActors[2].CharacterClass.AbilityCardsPool.Add(Card("borrowed"));
        tools.AugmentIcon = Art("native-action", 10);
        tools.InstallIcons(Art("private-ability-icon", 11));
        tools.darkConfig = ScriptableObject.CreateInstance<ElementConfigUI>(); Assets.Add(tools.darkConfig);
        tools.darkConfig.useIcon = Art("ConsumeDark", 12);
        tools.Shield = new UIInfoTools.EffectInfo { Icon = Art("native-shield", 13) };
        Image nativeImage = new GameObject("original-native-image", typeof(RectTransform)).AddComponent<Image>();
        Sprite original = tools.Skins["class-2"].defaultTopActionRegularSprite!;
        nativeImage.sprite = original;
        factory = new VRCardFactory();
        ScenarioCardPreparation.Begin(); yield return Complete();
        Check(ScenarioCardPreparation.Classes == 4, "all scenario classes and transferred ability skins are prepared without focus");
        Check(tools.Focus == "first" && nativeImage.sprite == original && VRCard.NativeAdoptions == 0,
            "preparation never selects a character activates native widgets or assigns original artwork");
        Check(ScenarioCardPreparation.BackingsTarget == 21 && factory.PreparedBlankCount == 21 && factory.All.Count == 0,
            "reserved wrappers remain absent from live hand driver inventories");
        Check(ScenarioCardPreparation.Failures == 0 && Addressables.Requests == 16,
            "every original skin addressable loads once by GUID before ordinary focus changes");
        foreach (AbilityCardUISkin skin in tools.Skins.Values)
            Check(!skin.TitleSprite!.SpriteReference!.NativeOperationTouched,
                "native AssetReference operation handles remain untouched");
        foreach (Sprite source in ExpectedArt)
        {
            // Calling the usual local/remote entry AFTER preparation must be a cache hit.
            var replacements = (System.Collections.IDictionary)typeof(CardFaceMipBake).GetField("s_replacementBySource", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Check(replacements.Contains(source.GetInstanceID()), "ordinary local and remote fronts have no cold sprite miss after preparation");
            Sprite replacement = CardFaceMipBake.ReplacementFor(source)
                ?? throw new Exception("prepared replacements retain original card geometry");
            Check(replacement.texture.mipmapCount > 1 && replacement.rect == source.rect
                && replacement.border == source.border && replacement.pivot == source.pivot && replacement.pixelsPerUnit == source.pixelsPerUnit,
                "prepared replacements retain original card geometry");
            Check(ReferenceEquals(CardFaceMipBake.ReplacementFor(source), replacement)
                && ReferenceEquals(CardFaceMipBake.OriginalFor(replacement), source),
                "local and remote paths share one replacement with reversible original provenance");
            Color32[] expected = Read(source.texture), actual = Read(replacement.texture);
            Check(expected.Length == actual.Length && expected.Zip(actual, (a, b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) + Math.Abs(a.a - b.a)).Max() <= 4,
                "real GPU readback preserves original upright sprite pixels for every class and icon");
        }
        CardFaceMipBake.Rescan(nativeImage);
        Check(nativeImage.sprite == CardFaceMipBake.ReplacementFor(original), "ordinary adoption applies the already prepared shared replacement");
        CardFaceMipBake.RestoreSprites(nativeImage);
        Check(nativeImage.sprite == original, "native pool restoration retains source artwork");
        int built = VRCard.Builds;
        VRCard first = factory.CreateBlank();
        Check(VRCard.Builds == built && factory.PreparedBlankCount == 20 && factory.All.Count == 1
            && !first.gameObject.activeInHierarchy && first.GameCard == null,
            "first ordinary card consumes a prebuilt backing without rebuilding or fabricating native content");
        var widget = new GameObject("native-widget").AddComponent<AbilityCardUI>();
        VRCard adopted = factory.GetOrCreate(widget);
        Check(VRCard.Builds == built && adopted.GameCard == widget && adopted.HasAdoptedFace,
            "real native widget adoption remains immediate on the ordinary factory path");
        int releases = Addressables.Releases;
        int artCount = Addressables.Requests;
        ScenarioCardPreparation.Reset();
        Check(factory.PreparedBlankCount == 0 && factory.All.Count == 2 && adopted.GameCard == widget
            && Addressables.Releases == releases && CardFaceMipBake.ReplacementFor(original) != null,
            "reset releases only unused backings retaining native and remote active art plus shared pins");
        tools.Focus = "class-2";
        ScenarioCardPreparation.Begin(); yield return Complete();
        Check(Addressables.Requests == artCount && tools.Focus == "class-2" && factory.All.Count == 2,
            "repeated scenario preparation and class switch reuse pins without changing native focus or live cards");
        Check(ScenarioCardPreparation.BackingsTarget == 19, "already live card wrappers are not duplicated during preparation");
        ScenarioCardPreparation.Reset();
        // Async completion is observed from our handle, not by restarting native loaders.
        Addressables.AutoComplete = false;
        ReferenceToSprite delayed = Reference("late-extra", 14);
        CardArtPin.PinReference(delayed);
        Check(CardArtPin.PreparedSprite(delayed, out bool pending) == null && pending,
            "own pending addressable preserves the native asynchronous loader");
        Addressables.Loads["late-extra"].Done = true;
        Check(CardArtPin.PreparedSprite(delayed, out pending) == Addressables.Art["late-extra"] && !pending,
            "own asynchronous result becomes available without native context writes");
        var unavailable = new ReferenceToSprite("missing-native-art");
        tools.Skins["class-0"].TopActionHighlightSprite = unavailable;
        Addressables.AutoComplete = true;
        ScenarioCardPreparation.Begin(); yield return Complete();
        Check(ScenarioCardPreparation.Failures == 1 && ScenarioCardPreparation.IsReady,
            "missing original art finishes preparation while ordinary native fallback remains available");
        ScenarioCardPreparation.Reset();
        Addressables.AutoComplete = false;
        tools.Skins["class-0"].TopActionHighlightSprite = Reference("never-finishes", 15);
        ScenarioCardPreparation.Begin(); ScenarioCardPreparation.Tick();
        // Start all references, then expire their waiting deadline; readiness is still bounded.
        for (int i = 0; i < 50; i++) { yield return null; ScenarioCardPreparation.Tick(); }
        Expire(); yield return Complete();
        Check(ScenarioCardPreparation.Failures > 0 && ScenarioCardPreparation.IsReady,
            "an addressable that never finishes cannot deadlock the scenario loading gate");
        ScenarioCardPreparation.Reset();
        UIInfoTools.Instance = null; ScenarioCardPreparation.Begin(); ScenarioCardPreparation.Tick();
        Check(!ScenarioCardPreparation.IsReady, "late original scenario dependencies are awaited during loading");
        Expire(); yield return null; ScenarioCardPreparation.Tick();
        Check(ScenarioCardPreparation.IsReady && ScenarioCardPreparation.Failures == 1,
            "missing native prerequisites cannot trap the player behind a loading spinner");
        Metrics = $"classes=4, original sprites={ExpectedArt.Count}, GUID requests={Addressables.Requests}, backing reservations=21, real GPU pixel comparisons={ExpectedArt.Count - 2}; no native focus/Show/Awake/Image preparation writes";
    }
    public static void Cleanup()
    {
        ScenarioCardPreparation.Reset(); factory?.Dispose(); factory = null; CardArtPin.ReleaseAll("fixture teardown");
        foreach (Object asset in Assets) if (asset != null) Object.DestroyImmediate(asset);
        Assets.Clear();
    }
}

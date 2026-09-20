using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using ScenarioRuleLibrary;
using SpriteMemoryManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Owns only presentation-side Addressables handles for public town widgets. Inactive
/// borrowed cards cannot finish their native Show coroutines, and inert remote copies must never
/// run those controllers. Load the same original assets independently, then let Mirror.Apply bind
/// them through the original asset registry. No source Image or game-owned load request is written.</summary>
internal static class TownServiceNativeAssets
{
    private sealed class Load
    {
        internal string Key = string.Empty;
        internal AsyncOperationHandle<Sprite> Handle;
        internal bool Held;
        internal int Attempts;
        internal float Started, RetryAt, RegisterAt;
    }
    private static readonly Dictionary<string, Load> Loads = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, FieldInfo[]> ReferenceFields = new();
    private static readonly FieldInfo[] SkinSprites = Array.FindAll(typeof(AbilityCardUISkin).GetFields(), f => f.FieldType == typeof(Sprite));
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static readonly HashSet<string> ReportedUnavailable = new(StringComparer.Ordinal);
    private static float _nextTick;
    private const int MaxAttempts = 3;

    internal static void PrepareCard(CAbilityCard? card, ItemCardUI? item = null)
    {
        if (!MapRoomDriver.Active) return;
        if (item != null) PrepareItem(item);
        if (card == null || UIInfoTools.Instance == null) return;
        try
        {
            AbilityCardUISkin skin = UIInfoTools.Instance.GetCardSkin(card.ClassModel, card.ClassCharacterConfig);
            if (skin == null) return;
            Prepare(skin.TitleSprite);
            Prepare(skin.TopActionRegularSprite); Prepare(skin.TopActionHighlightSprite);
            Prepare(skin.TopActionSelectedSprite); Prepare(skin.TopActionDisabledSprite);
            Prepare(skin.BottomActionRegularSprite); Prepare(skin.BottomActionHighlightSprite);
            Prepare(skin.BottomActionSelectedSprite); Prepare(skin.BottomActionDisabledSprite);
            // Preview/default/long-rest decorations are already resident, not more loads.
            foreach (FieldInfo field in SkinSprites) Register(field.GetValue(skin) as Sprite);
        }
        catch (Exception e) { Report("class skin", e); }
    }

    internal static void PrepareItem(ItemCardUI? source)
    {
        if (!MapRoomDriver.Active || source == null || source.item == null || UIInfoTools.Instance == null) return;
        try
        {
            CItem item = source.item;
            Prepare(UIInfoTools.Instance.GetItemBackgroundSprite(item.YMLData.Art));
            if (item.YMLData.ValidEquipCharacterClassIDs.Count > 0)
                Prepare(UIInfoTools.Instance.GetCharacterAssemblyIcon(item.YMLData.ValidEquipCharacterClassIDs[0]));
        }
        catch (Exception e) { Report("item art", e); }
    }

    internal static void PrepareCharacter(ECharacter model, string? custom = null)
    {
        if (!MapRoomDriver.Active || UIInfoTools.Instance == null) return;
        try
        {
            UIInfoTools info = UIInfoTools.Instance;
            Prepare(info.GetCharacterSpriteRef(model, false, custom));
            Prepare(info.GetCharacterSpriteRef(model, true, custom));
            Prepare(info.GetCharacterAssemblyIcon(model, custom));
            Register(info.GetCharacterAssemblySprite(model, custom));
            Register(info.GetNewAdventureCharacterPortrait(model, false, custom));
            Register(info.GetNewAdventureCharacterPortrait(model, true, custom));
        }
        catch (Exception e) { Report("selected character art", e); }
    }

    internal static void PrepareActorPortrait(string model, string? customPortrait = null)
    {
        if (!MapRoomDriver.Active || string.IsNullOrEmpty(model) || UIInfoTools.Instance == null) return;
        try { Prepare(UIInfoTools.Instance.GetActorPortraitRef(model, customPortrait)); }
        catch (Exception e) { Report("selected actor portrait", e); }
    }

    internal static void PrepareRoot(Transform? root)
    {
        if (!MapRoomDriver.Active || root == null) return;
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            try { Register(image.sprite); Register(image.overrideSprite); }
            catch (Exception e) { Report("resident image", e); }
        }
        foreach (RawImage image in root.GetComponentsInChildren<RawImage>(true))
        {
            try
            {
                // This is a read-only reverse lookup; never restore or swap the source image.
                if (image.texture != null && PanelMipBake.OriginalFor(image.texture) is Texture2D original)
                    TownServiceMirror.Assets.Key(original);
            }
            catch (Exception e) { Report("resident texture", e); }
        }
        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            try
            {
                SpriteState state = selectable.spriteState;
                Register(state.highlightedSprite); Register(state.pressedSprite);
                Register(state.selectedSprite); Register(state.disabledSprite);
            }
            catch (Exception e) { Report("resident control states", e); }
        }
        foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().Assembly != typeof(ItemCardUI).Assembly) continue;
            try
            {
                // Only direct native ReferenceToSprite fields, never arbitrary object graphs,
                // static catalogues, property getters or controller methods. This picks up the
                // source's already-selected portrait/icon references before neutralization.
                foreach (FieldInfo field in Fields(component.GetType())) Prepare(field.GetValue(component) as ReferenceToSprite);
                if (component is ItemCardUI item) PrepareItem(item);
            }
            catch (Exception e) { Report("native sprite reference", e); }
        }
    }

    private static FieldInfo[] Fields(Type type)
    {
        if (ReferenceFields.TryGetValue(type, out FieldInfo[]? fields)) return fields;
        var result = new List<FieldInfo>();
        for (Type? current = type; current != null && current.Assembly == typeof(ItemCardUI).Assembly; current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType == typeof(ReferenceToSprite)) result.Add(field);
        fields = result.ToArray(); ReferenceFields.Add(type, fields); return fields;
    }

    private static void Prepare(ReferenceToSprite? reference)
    {
        if (reference == null) return;
        if (reference.InitializedWithSpecialSprite) { Register(reference.SpecialSprite); return; }
        AssetReferenceSprite asset = reference.SpriteReference;
        if (asset == null) return;
        // RuntimeKey retains a subobject suffix when supplied. Never pass the game's
        // AssetReference object or call ReferenceToSprite.GetAsyncSprite: both own state.
        string key = asset.RuntimeKey as string ?? asset.AssetGUID;
        if (string.IsNullOrEmpty(key) || Loads.ContainsKey(key)) return;
        var load = new Load { Key = key }; Loads.Add(key, load); Start(load);
    }

    private static void Start(Load load)
    {
        load.Attempts++; load.Started = Time.unscaledTime;
        try
        {
            load.Handle = Addressables.LoadAssetAsync<Sprite>(load.Key);
            load.Held = load.Handle.IsValid();
            if (!load.Held) Failed(load, "invalid handle");
        }
        catch (Exception e) { Failed(load, e.GetType().Name + ": " + e.Message); }
    }

    internal static void Tick()
    {
        if (!MapRoomDriver.Active || Time.unscaledTime < _nextTick) return;
        float now = Time.unscaledTime; _nextTick = now + 0.1f;
        foreach (Load load in Loads.Values)
        {
            if (!load.Held)
            {
                if (load.Attempts < MaxAttempts && now >= load.RetryAt) Start(load);
                continue;
            }
            try
            {
                if (!load.Handle.IsValid()) { Failed(load, "handle became invalid"); continue; }
                if (!load.Handle.IsDone)
                {
                    if (now - load.Started > 30) Failed(load, "load timed out");
                    continue;
                }
                if (load.Handle.Status != AsyncOperationStatus.Succeeded || load.Handle.Result == null)
                { Failed(load, "original sprite unavailable"); continue; }
                if (now < load.RegisterAt) continue;
                Register(load.Handle.Result);
                // Re-establish descriptors after a mirror registry reset while our original
                // asset remains pinned. No last-load-wins GUID primary keys or atlas aliases.
                load.RegisterAt = now + 2;
            }
            catch (Exception e) { Failed(load, e.GetType().Name + ": " + e.Message); }
        }
    }

    private static void Register(Sprite? sprite)
    {
        if (sprite == null) return;
        Sprite original = CardFaceMipBake.OriginalFor(sprite);
        TownServiceMirror.Assets.Key(original.texture);
        TownServiceMirror.Assets.Key(original);
    }

    private static void Failed(Load load, string reason)
    {
        Release(load);
        load.RetryAt = Time.unscaledTime + (load.Attempts == 1 ? 1 : 4);
        Report("addressable " + load.Key, new InvalidOperationException(reason + "; attempt " + load.Attempts + "/" + MaxAttempts));
        if (load.Attempts >= MaxAttempts) ReportUnavailable(load.Key);
    }

    private static void Report(string context, Exception error)
    {
        if (Reported.Count >= 8 || !Reported.Add(context)) return;
        VRLog.Warn("WorldUI", "TOWN NATIVE ART: " + context + " — " + error.GetType().Name + ": " + error.Message);
    }

    private static void ReportUnavailable(string key)
    {
        if (ReportedUnavailable.Count >= 8 || !ReportedUnavailable.Add(key)) return;
        VRLog.Note("TownServices", "Original town artwork could not be preloaded after three attempts (" + key + ").");
    }

    private static void ReportReset()
    {
        Reported.Clear(); ReportedUnavailable.Clear();
    }

    private static void Release(Load load)
    {
        if (!load.Held) return;
        load.Held = false;
        try { if (load.Handle.IsValid()) Addressables.Release(load.Handle); }
        catch (Exception e) { Report("own handle release", e); }
    }

    internal static void Shutdown()
    {
        foreach (Load load in Loads.Values) Release(load);
        Loads.Clear(); ReportReset(); _nextTick = 0;
    }
}

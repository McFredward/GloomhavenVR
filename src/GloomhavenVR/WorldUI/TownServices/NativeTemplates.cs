using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AsmodeeNet.Foundation;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

/// <summary>Canonical original prefab provenance for town widgets. The bank owns only inactive,
/// neutralized copies; a visitor's gameplay window is never opened to build another visitor's UI.</summary>
internal static class NativeTemplates
{
    internal sealed class Part
    {
        internal string Path = string.Empty;
        internal Transform Original = null!;
        internal readonly HashSet<Transform> Excluded = new();
    }
    private sealed class Entry
    {
        internal Transform Original = null!;
        internal GameObject Copy = null!;
        internal readonly List<Part> Parts = new();
    }
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<Transform, string> Roots = new();
    private static GameObject? _bank;
    private static UIGuildmasterHUD? _hud;
    internal static bool Ready => _hud != null && _bank != null;

    internal static bool Initialize()
    {
        UIGuildmasterHUD? hud = Singleton<UIGuildmasterHUD>.Instance;
        if (hud == null) return false;
        if (_hud == hud && _bank != null) return true;
        Shutdown(); _hud = hud;
        _bank = new GameObject("GVR original town widget provenance"); _bank.SetActive(false);
        Object.DontDestroyOnLoad(_bank);
        Add("merchant", hud.shopWindow);
        Add("merchant.inventory", hud.shopWindow.ItemInventory);
        Add("merchant.row", hud.shopWindow.ItemInventory.slotPrefab);
        Add("merchant.tooltip", hud.shopWindow.ItemInventory.itemTooltip);
        Add("temple", hud.templeWindow);
        Add("temple.inventory", hud.templeWindow.Shop);
        Add("temple.row", hud.templeWindow.Shop.slotPrefab);
        Add("temple.tooltip", hud.templeWindow.Shop.tooltip);
        Add("enchant", hud.enhancementWindow);
        Add("enchant.inventory", hud.enhancementWindow.enhancementShop);
        Add("enchant.row", hud.enhancementWindow.enhancementShop.slotPrefab);
        Add("enchant.tooltip", hud.enhancementWindow.enhancementShop.tooltip);
        Add("enchant.holder", hud.enhancementWindow.cardHolder);
        Add("enchant.cards", hud.enhancementWindow.CardsDisplay);
        Add("enchant.scroll", hud.enhancementWindow.CardsDisplay.abilityCardsPanel);
        Add("enchant.cardrow", hud.enhancementWindow.CardsDisplay.slotPrefab);
        if (hud.enhancementWindow.CardsDisplay.slotPrefab.enhancementPoints.Count > 0)
            Add("enchant.point", hud.enhancementWindow.CardsDisplay.slotPrefab.enhancementPoints[0]);
        if (hud.enhancementWindow.highlightAbilityPool.Count > 0)
            Add("enchant.highlight", hud.enhancementWindow.highlightAbilityPool[0]);
        Add("banner", hud.banner);
        Add("item.confirm", Singleton<UIItemConfirmationBox>.Instance);
        Add("enhance.confirm", Singleton<UIEnhancementConfirmationBox>.Instance);
        // Discover the complete immutable canonical hierarchy only after all logical roots are
        // known, so sections and pooled rows cannot accidentally be duplicated in their parent.
        foreach (var entry in Entries) Freeze(entry.Key, entry.Value);
        TownServiceMirror.ResolveTemplate = Resolve;
        return true;
    }

    private static void Add(string key, Component? source)
    {
        if (source == null) return;
        Entries.Add(key, new Entry { Original = source.transform }); Roots[source.transform] = key;
    }
    internal static Transform? Original(string key) => Entries.TryGetValue(key, out Entry? entry) && entry.Original != null ? entry.Original : null;
    internal static bool IsBoundary(Transform node) => Roots.ContainsKey(node) || IsDynamic(node);
    internal static bool IsDynamic(Transform node) => node.GetComponent<UIShopItemSlot>() != null
        || node.GetComponent<UITempleShopSlot>() != null || node.GetComponent<UINewEnhancementShopSlot>() != null
        || node.GetComponent<UIEnhanceCardSlot>() != null || node.GetComponent<UIEnhanceCardPoint>() != null
        || node.GetComponent<UIEnhancementButtonHighlight>() != null || node.GetComponent<AbilityCardUI>() != null
        || node.GetComponent<ItemCardUI>() != null;

    private static void Freeze(string key, Entry entry)
    {
        if (_bank == null || entry.Original == null) throw new InvalidOperationException("Original town template is unavailable: " + key);
        entry.Copy = Object.Instantiate(entry.Original.gameObject, _bank.transform, false);
        Prune(entry.Original, entry.Copy.transform);
        RemoteWidgetMirror.Neutralize(entry.Copy, RemoteWidgetMirror.LayoutOwner.Source, null);
        entry.Copy.SetActive(false);
        Partition(entry.Copy.transform, string.Empty, entry.Parts);
    }
    private static void Prune(Transform source, Transform copy)
    {
        for (int i = source.childCount - 1; i >= 0; i--)
        {
            Transform child = source.GetChild(i), duplicate = copy.GetChild(i);
            if (IsBoundary(child) || child.GetComponent<TMP_SubMeshUI>() != null || child.GetComponent<TMP_SubMesh>() != null)
                Object.DestroyImmediate(duplicate.gameObject);
            else Prune(child, duplicate);
        }
    }
    private static int Count(Transform root)
    { int count = 1; for (int i = 0; i < root.childCount; i++) count += Count(root.GetChild(i)); return count; }
    private static void Partition(Transform root, string path, List<Part> parts)
    {
        var part = new Part { Path = path, Original = root }; parts.Add(part);
        int remaining = Count(root);
        if (remaining <= 128) return;
        for (int i = root.childCount - 1; i >= 0 && remaining > 128; i--)
        {
            Transform child = root.GetChild(i); part.Excluded.Add(child); remaining -= Count(child);
            Partition(child, Append(path, child), parts);
        }
    }
    internal static string Append(string path, Transform child)
    {
        int occurrence = 0;
        for (int i = 0; i < child.GetSiblingIndex(); i++) if (child.parent.GetChild(i).name == child.name) occurrence++;
        string token = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(child.name)) + ":" + occurrence;
        return path.Length == 0 ? token : path + "/" + token;
    }
    internal static Transform? At(Transform root, string path)
    {
        if (path.Length == 0) return root;
        foreach (string token in path.Split('/'))
        {
            int colon = token.LastIndexOf(':'); if (colon < 0) return null;
            string name = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token.Substring(0, colon)));
            int occurrence = int.Parse(token.Substring(colon + 1), CultureInfo.InvariantCulture);
            Transform? found = null;
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i).name == name && occurrence-- == 0) { found = root.GetChild(i); break; }
            if (found == null) return null; root = found;
        }
        return root;
    }
    internal static IReadOnlyList<Part> Parts(string key)
    {
        EnsureCard(key);
        if (!Entries.TryGetValue(key, out Entry? entry)) throw new InvalidDataException("Missing original town widget: " + key);
        return entry.Parts;
    }
    internal static bool Resolve(byte service, ushort template, string address)
    {
        int split = address.IndexOf('|'); if (split < 1) return false;
        string key = address.Substring(0, split), path = address.Substring(split + 1);
        foreach (Part part in Parts(key))
            if (part.Path == path)
            { TownServiceMirror.RegisterTemplate(service, template, part.Original, part.Excluded.Contains, address); return true; }
        return false;
    }
    internal static string CardKey(AbilityCardUI card) => "card." + card.CardID.ToString(CultureInfo.InvariantCulture);
    private static void EnsureCard(string key)
    {
        bool item = key.StartsWith("item.", StringComparison.Ordinal) && key != "item.confirm";
        if (Entries.ContainsKey(key) || !item && !key.StartsWith("card.", StringComparison.Ordinal)) return;
        if (!int.TryParse(key.Substring(5), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0 || ObjectPool.instance == null)
            throw new InvalidDataException("Invalid original ability-card template identity.");
        CAbilityCard? model = CharacterClassManager.AllAbilityCards.Find(card => card.ID == id);
        if (!item && model == null) throw new InvalidDataException("Original ability-card model is unavailable: " + id);
        var holder = new GameObject("GVR inactive original service card borrow"); holder.SetActive(false);
        holder.transform.SetParent(ObjectPool.instance.transform, false);
        GameObject? borrowed = null;
        try
        {
            borrowed = ObjectPool.SpawnCard(id, item ? ObjectPool.ECardType.Item : ObjectPool.ECardType.Ability, holder.transform,
                resetLocalScale: true, resetToMiddle: true, resetLocalRotation: false, activate: false);
            if (!item) borrowed.GetComponent<AbilityCardUI>().Init(model!, disableEventDetection: true);
            var entry = new Entry { Original = borrowed.transform }; Freeze(key, entry); Entries.Add(key, entry);
        }
        finally
        {
            if (borrowed != null) ObjectPool.RecycleCard(id, item ? ObjectPool.ECardType.Item : ObjectPool.ECardType.Ability, borrowed);
            Object.Destroy(holder);
        }
    }
    internal static void Shutdown()
    {
        TownServiceMirror.ResolveTemplate = null;
        Entries.Clear(); Roots.Clear(); _hud = null;
        if (_bank != null) Object.Destroy(_bank); _bank = null;
    }
}

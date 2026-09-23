using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Deliberately small native boundaries: the production methods are compiled unchanged by
// check-town-service-provenance.py. A YMLData read simulates the real pre-throw error modal.
static class Test
{
    static int checks;
    public static void Assert(bool condition, string name) { checks++; if (!condition) throw new Exception("ASSERT: " + name); }
    public static void Main()
    {
        var good = new ItemCardYMLData { ID = 7, Art = "seven", ValidEquipCharacterClassIDs = new() { "class" } };
        ScenarioRuleClient.SRLYML.ItemCards.Add(good);
        ScenarioRuleClient.SRLYML.ItemCards.Add(new() { ID = 8, Art = "eight" });
        var zero = new CItem(0);
        TownServiceNativeAssets.PrepareItem(new() { item = zero });
        Assert(CItem.GetterCalls == 0, "uninitialized template never invokes the native error getter");
        Assert(TownServiceNativeAssets.Prepared.Count == 0, "uninitialized template starts no art load");
        TownServiceNativeAssets.PrepareItem(new() { item = new CItem(7) });
        Assert(CItem.GetterCalls == 0, "valid item art uses immutable lookup");
        Assert(TownServiceNativeAssets.Prepared.Contains("seven") && TownServiceNativeAssets.Prepared.Contains("class"), "valid original background and class artwork are loaded");
        for (int i = 0; i < 20; i++) TownServiceNativeAssets.PrepareItem(new() { item = new CItem(99) });
        Assert(CItem.GetterCalls == 0 && VRLog.Notes == 1, "unknown positive ID reports once without opening game modal");
        Assert(ReferenceEquals(TownServiceNativeAssets.FindItemData(7), good), "lookup returns original catalogue definition");
        ScenarioRuleClient.SRLYML.ItemCards.Add(new() { ID = 7 });
        bool duplicate = false;
        try { TownServiceNativeAssets.FindItemData(7); } catch (InvalidOperationException) { duplicate = true; }
        Assert(duplicate, "duplicate data remains a detectable error");
        ScenarioRuleClient.SRLYML.ItemCards.RemoveAt(2);
        foreach (int previousId in new[] { 0, 8 })
        {
            NativeTemplates.Clear();
            var previous = new CItem(previousId); ObjectPool.Next = new() { item = previous };
            NativeTemplates.Run("item.7");
            Assert(NativeTemplates.FrozenId == 7, "borrow receives requested ID even with default or stale model");
            Assert(ReferenceEquals(ObjectPool.Next.item, previous), "borrow restores original pooled model");
            Assert(ObjectPool.Returns > 0 && !ObjectPool.Activated, "borrow returns without native activation");
            Assert(CItem.GetterCalls == 0, "freeze traversal never invokes error getter");
        }
        NativeTemplates.Clear(); ObjectPool.Spawns = 0;
        bool absent = false;
        try { NativeTemplates.Run("item.99"); } catch (InvalidDataException) { absent = true; }
        Assert(absent && ObjectPool.Spawns == 0, "missing item rejected before native pool can raise error dialog");
        NativeTemplates.Clear(); NativeTemplates.ThrowFreeze = true;
        var original = new CItem(8); ObjectPool.Next = new() { item = original }; int returns = ObjectPool.Returns;
        try { NativeTemplates.Run("item.7"); } catch (InvalidOperationException) { }
        Assert(ReferenceEquals(ObjectPool.Next.item, original) && ObjectPool.Returns == returns + 1, "failed freeze also restores and returns borrow");
        NativeTemplates.ThrowFreeze = false;
        NativeTemplates.Clear(); CharacterClassManager.AllAbilityCards.Add(new() { ID = 3 });
        NativeTemplates.Run("card.3");
        Assert(ObjectPool.LastAbility.Initialized && !ObjectPool.Activated, "validated ability template still initializes inactive");
        NativeTemplates.Clear(); NativeTemplates.Run("face.3");
        Assert(ReferenceEquals(NativeTemplates.FrozenRoot, ObjectPool.LastAbility.fullAbilityCard.transform), "parked map card freezes original full face subtree");
        Assert(ObjectPool.LastAbility.Initialized && !ObjectPool.Activated, "parked face initializes without gameplay activation");
        NativeTemplates.Clear(); int before = ObjectPool.Spawns;
        bool invalidAbility = false;
        try { NativeTemplates.Run("card.99"); } catch (InvalidDataException) { invalidAbility = true; }
        Assert(invalidAbility && ObjectPool.Spawns == before, "missing ability rejected before native pool");
        Console.WriteLine("PASS town provenance: " + checks + " assertions");
    }
}
class CItem
{
    public int ID; public CItem(int id) { ID = id; }
    public static int GetterCalls;
    public ItemCardYMLData YMLData { get { GetterCalls++; throw new Exception("NATIVE ERROR MODAL OPENED"); } }
}
class ItemCardYMLData { public int ID; public string Art = ""; public List<string> ValidEquipCharacterClassIDs = new(); }
static class ScenarioRuleClient { public static Data SRLYML = new(); }
class Data { public List<ItemCardYMLData> ItemCards = new(); }
class ItemCardUI { public CItem? item; }
class UIInfoTools
{
    public static UIInfoTools Instance = new();
    public string GetItemBackgroundSprite(string art) => art;
    public string GetCharacterAssemblyIcon(string id) => id;
}
static class MapRoomDriver { public static bool Active = true; }
static class VRLog { public static int Notes; public static void Note(string scope, string text) { Notes++; } }
static partial class TownServiceNativeAssets
{
    static readonly HashSet<string> ReportedUnavailable = new();
    public static readonly List<string> Prepared = new();
    static void Prepare(string art) { Prepared.Add(art); }
    static void Report(string context, Exception error) { }
    internal static void PrepareCard(CAbilityCard? card, ItemCardUI? item = null) { if (item != null) PrepareItem(item); }
}
class Transform { public GameObject gameObject = null!; public void SetParent(Transform parent, bool stay) { } }
class GameObject
{
    public Transform transform; public ItemCardUI? Item; public AbilityCardUI? Ability;
    public GameObject(string name = "") { transform = new() { gameObject = this }; }
    public void SetActive(bool active) { if (active) ObjectPool.Activated = true; }
    public T GetComponent<T>() where T : class => (Item as T ?? Ability as T)!;
}
static class Object { public static void Destroy(GameObject obj) { } }
class CAbilityCard { public int ID; }
class FullAbilityCard { public Transform transform = new GameObject("full").transform; }
class AbilityCardUI { public FullAbilityCard fullAbilityCard = new(); public bool Initialized; public void Init(CAbilityCard model, bool disableEventDetection) { Initialized = disableEventDetection; } }
static class CharacterClassManager { public static List<CAbilityCard> AllAbilityCards = new(); }
class ObjectPool
{
    public static ObjectPool instance = new(); public Transform transform = new();
    public enum ECardType { Item, Ability }
    public static ItemCardUI Next = new(); public static AbilityCardUI LastAbility = new();
    public static int Spawns, Returns; public static bool Activated;
    public static GameObject SpawnCard(int id, ECardType type, Transform parent, bool resetLocalScale, bool resetToMiddle, bool resetLocalRotation, bool activate)
    { Spawns++; Activated |= activate; LastAbility = new(); return new() { Item = type == ECardType.Item ? Next : null, Ability = type == ECardType.Ability ? LastAbility : null }; }
    public static void RecycleCard(int id, ECardType type, GameObject obj) { Returns++; }
}
static class RemoteItemCardSource { public static void ReturnBorrowed(int id, GameObject obj) { ObjectPool.Returns++; } }
static partial class NativeTemplates
{
    class Entry { public Transform Original = null!; }
    static readonly Dictionary<string, Entry> Entries = new();
    public static int FrozenId; public static bool ThrowFreeze; public static Transform? FrozenRoot;
    public static void Clear() { Entries.Clear(); }
    public static void Run(string key) { EnsureCard(key); }
    static void Freeze(string key, Entry entry)
    {
        FrozenRoot = entry.Original;
        var item = entry.Original.gameObject.Item;
        if (item != null) { TownServiceNativeAssets.PrepareItem(item); FrozenId = item.item!.ID; }
        if (ThrowFreeze) throw new InvalidOperationException("fixture freeze failure");
    }
}

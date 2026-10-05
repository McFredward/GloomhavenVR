using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI;

internal static partial class NativeTemplates
{
    private const string TempleCounterPrefix = "temple.tooltip.counters.";
    private const int MaxTempleCounters = 64;
    private static readonly string[] TempleCounterKeys = CreateTempleCounterKeys();
    private static readonly FieldInfo TempleModifier = TempleField(typeof(UITempleSlotTooltip), "modifier");
    private static readonly FieldInfo TempleCounterHolder = TempleField(
        typeof(UIAttackModifier<UIPerkAttackModifierCounter>), "countersHolder");
    private static readonly FieldInfo TempleCounterPrefab = TempleField(
        typeof(UIAttackModifier<UIPerkAttackModifierCounter>), "attackModCounterPrefab");

    internal static string TempleTooltipKey(Transform source)
    {
        UITempleSlotTooltip? tooltip = source.GetComponent<UITempleSlotTooltip>();
        if (tooltip == null) throw new InvalidDataException("Original temple tooltip is unavailable.");
        UIPerkAttackModifier modifier = TempleRead<UIPerkAttackModifier>(TempleModifier, tooltip);
        Transform holder = TempleRead<Transform>(TempleCounterHolder, modifier);
        int count = 0;
        for (int i = 0; i < holder.childCount; i++)
            if (holder.GetChild(i).GetComponent<UIPerkAttackModifierCounter>() != null) count++;
        if (count > MaxTempleCounters) throw new InvalidDataException("Original temple tooltip exceeds its counter budget.");
        return TempleCounterKeys[count];
    }

    private static void EnsureTempleTooltip(string key)
    {
        if (!key.StartsWith(TempleCounterPrefix, StringComparison.Ordinal) || Entries.ContainsKey(key)) return;
        if (!int.TryParse(key.Substring(TempleCounterPrefix.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out int count) || count < 0 || count > MaxTempleCounters
            || !StringComparer.Ordinal.Equals(key, TempleCounterKeys[count]))
            throw new InvalidDataException("Invalid original temple tooltip counter identity.");
        Transform? source = Original("temple.tooltip");
        if (source == null || _bank == null) throw new InvalidDataException("Original temple tooltip is still loading.");
        UITempleSlotTooltip originalTooltip = source.GetComponent<UITempleSlotTooltip>();
        UIPerkAttackModifier originalModifier = TempleRead<UIPerkAttackModifier>(TempleModifier, originalTooltip);
        UIPerkAttackModifierCounter prefab = TempleRead<UIPerkAttackModifierCounter>(TempleCounterPrefab, originalModifier);
        // UITempleSlotTooltip is not UITooltip. Its native Build adds a retained
        // UIPerkAttackModifier counter pool, so its complete topology depends on
        // previously inspected blessings. Build622 froze only startup topology.
        // Reproduce the original counter children on an inactive temporary copy;
        // never call Show, Build, UpdateCounters or a gameplay callback. All text,
        // icons, opacity and counter state still come from the owner's snapshot.
        GameObject construction = Object.Instantiate(source.gameObject, _bank.transform, false);
        try
        {
            UITempleSlotTooltip tooltip = construction.GetComponent<UITempleSlotTooltip>();
            UIPerkAttackModifier modifier = TempleRead<UIPerkAttackModifier>(TempleModifier, tooltip);
            Transform holder = TempleRead<Transform>(TempleCounterHolder, modifier);
            if (!holder.IsChildOf(construction.transform))
                throw new InvalidDataException("Original temple tooltip counter parent is invalid.");
            for (int i = holder.childCount - 1; i >= 0; i--)
                if (holder.GetChild(i).GetComponent<UIPerkAttackModifierCounter>() != null)
                    Object.DestroyImmediate(holder.GetChild(i).gameObject);
            for (int i = 0; i < count; i++)
            {
                GameObject copy = Object.Instantiate(prefab.gameObject, holder, false);
                copy.name = prefab.name; copy.SetActive(true);
            }
            var entry = new Entry { Original = construction.transform };
            Freeze(key, entry); Entries.Add(key, entry);
        }
        finally { Object.Destroy(construction); }
    }

    private static FieldInfo TempleField(Type type, string name) => type.GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidDataException("Original temple tooltip field is unavailable: " + name);

    private static T TempleRead<T>(FieldInfo field, object source) where T : Object =>
        field.GetValue(source) is T value && value != null ? value
            : throw new InvalidDataException("Original temple tooltip reference is unavailable: " + field.Name);

    private static string[] CreateTempleCounterKeys()
    {
        var keys = new string[MaxTempleCounters + 1];
        for (int i = 0; i < keys.Length; i++) keys[i] = TempleCounterPrefix + i.ToString(CultureInfo.InvariantCulture);
        return keys;
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>
/// Lift the complete original ability/item widget during a UI eye capture. Native
/// Exact native ability/item/monster/modifier identities cover pooled hover popups,
/// level-up, deck/enhancement views and item confirmations; no card-name/image
/// heuristic or substitute artwork is involved. Enrolled world UI and original
/// screen-space canvases qualify, while physical hand/board/card-bake canvases do not.
/// </summary>
internal static class MenuDepthCards
{
    private sealed class Kind
    {
        internal readonly Type Type;
        internal readonly string[][] Backgrounds;
        internal readonly bool Container;
        internal Kind(string name, bool container, params string[][] backgrounds)
        {
            Type = typeof(StoryImageViewer).Assembly.GetType(name)!;
            Container = container; Backgrounds = backgrounds;
        }
    }

    // Exact original serialized graphic references, including the action-only
    // Full variants whose header/default backing fields are legitimately empty.
    // The depth pass uses native sprite UV/alpha/masks, not a rectangular bounds fill.
    private static readonly Kind[] Kinds =
    {
        new("FullAbilityCard", false, new[] { "headerImage" }, new[] { "buttonsHolderImage" },
            new[] { "topActionButton", "actionButton", "targetGraphic" },
            new[] { "topActionButton", "defaultActionButton", "targetGraphic" },
            new[] { "bottomActionButton", "actionButton", "targetGraphic" },
            new[] { "bottomActionButton", "defaultActionButton", "targetGraphic" }),
        new("MiniAbilityCard", false, new[] { "miniAbilityBackgroundImage" }),
        new("ItemCardUI", false, new[] { "cardBackground" }),
        new("MonsterRoundCardUI", false, new[] { "background" }),
        new("MonsterBaseUI", false, new[] { "backgroundImage" }, new[] { "textBackgroundOpacityImage" }),
        new("UIPerkAttackModifier", false, new[] { "button", "targetGraphic" }),
        // The native scenario modifier prefab declares its printed backing as
        // the Image on this exact typed owner, without a serialized Image field.
        new("UIScenarioAttackModifier", false, new[] { "$rootImage" }),
        new("AbilityCardUI", true),
    };
    private static readonly Dictionary<Type, Kind?> Types = new();
    private static readonly Dictionary<MonoBehaviour, MenuDepthCardPlane> Cards = new();
    private static readonly List<MonoBehaviour> Dead = new(16);
    private static readonly Dictionary<(Type, string), MemberInfo?> Members = new();

    internal static IEnumerable<Type> NativeTypes()
    {
        for (int i = 0; i < Kinds.Length; i++) if (Kinds[i].Type != null) yield return Kinds[i].Type;
    }

    internal static bool IsCardOwner(MonoBehaviour owner) => owner != null && Classify(owner.GetType()) != null;

    private static Kind? Classify(Type type)
    {
        if (Types.TryGetValue(type, out Kind? cached)) return cached;
        Kind? found = null;
        for (int i = 0; i < Kinds.Length; i++)
            if (Kinds[i].Type != null && Kinds[i].Type.IsAssignableFrom(type)) { found = Kinds[i]; break; }
        Types.Add(type, found); return found;
    }

    internal static bool IsNativeFlatGraphic(Graphic graphic)
    {
        Canvas canvas = graphic.canvas;
        return canvas != null && canvas.rootCanvas != null && canvas.rootCanvas.renderMode != RenderMode.WorldSpace;
    }

    internal static void Register(MonoBehaviour owner, Transform? windowRoot)
    {
        Kind? kind = Classify(owner.GetType());
        if (kind == null) return;
        if (kind.Container)
        {
            RegisterMember(owner, "fullAbilityCard", windowRoot);
            RegisterMember(owner, "miniAbilityCard", windowRoot);
            return;
        }
        var rect = owner.transform as RectTransform;
        if (rect == null) return;
        Canvas sourceCanvas = owner.GetComponentInParent<Canvas>();
        if (windowRoot == null && (sourceCanvas == null || sourceCanvas.rootCanvas == null
            || sourceCanvas.rootCanvas.renderMode == RenderMode.WorldSpace)) return;
        var graphics = new List<Graphic>(kind.Backgrounds.Length);
        for (int i = 0; i < kind.Backgrounds.Length; i++)
        {
            object? value = owner;
            for (int p = 0; value != null && p < kind.Backgrounds[i].Length; p++) value = Read(value, kind.Backgrounds[i][p]);
            if (value is Graphic graphic && !graphics.Contains(graphic)) graphics.Add(graphic);
        }
        if (Cards.TryGetValue(owner, out MenuDepthCardPlane plane) && plane != null)
        {
            plane.Rebind(windowRoot, graphics);
            // Source pruning may have observed the old native Graphic destroyed
            // while this pooled widget stayed active. A real replacement birth
            // renews the same plane's membership as well as its exact art references.
            MenuDepthSources.Register(plane); return;
        }
        if (graphics.Count == 0) return; // Original fields may still be assigned by a native late initializer.
        plane = owner.gameObject.AddComponent<MenuDepthCardPlane>();
        plane.Configure(owner, rect, graphics, windowRoot);
        Cards.Add(owner, plane);
        // A parent card may arrive after one of its separately pooled modifier or
        // monster children. Refresh only known card ancestors at this real birth,
        // rather than searching a menu hierarchy on every frame/capture.
        foreach (MenuDepthCardPlane known in Cards.Values) if (known != null) known.RebindParent();
    }

    private static void RegisterMember(MonoBehaviour owner, string field, Transform? windowRoot)
    {
        // The game also reparents a full face into a different confirmation or
        // preview owner. Its former AbilityCardUI must not steal that new scope.
        if (Read(owner, field) is MonoBehaviour child && child.transform.IsChildOf(owner.transform)) Register(child, windowRoot);
    }

    private static object? Read(object value, string name)
    {
        if (name == "$rootImage" && value is Component component) return component.GetComponent<Image>();
        var key = (value.GetType(), name);
        if (!Members.TryGetValue(key, out MemberInfo? member))
        {
            member = (MemberInfo?)AccessTools.Field(key.Item1, name) ?? AccessTools.Property(key.Item1, name);
            Members.Add(key, member);
        }
        return member is FieldInfo field ? field.GetValue(value)
            : member is PropertyInfo property ? property.GetValue(value, null) : null;
    }

    internal static void Maintain()
    {
        Dead.Clear();
        foreach (KeyValuePair<MonoBehaviour, MenuDepthCardPlane> item in Cards)
            if (item.Key == null || item.Value == null) Dead.Add(item.Key!);
        for (int i = 0; i < Dead.Count; i++)
        {
            if (Cards.TryGetValue(Dead[i], out MenuDepthCardPlane plane) && plane != null) UnityEngine.Object.Destroy(plane);
            Cards.Remove(Dead[i]);
        }
    }

    internal static void Shutdown()
    {
        foreach (MenuDepthCardPlane plane in Cards.Values)
            if (plane != null) { plane.enabled = false; UnityEngine.Object.Destroy(plane); }
        Cards.Clear(); Dead.Clear();
    }
}

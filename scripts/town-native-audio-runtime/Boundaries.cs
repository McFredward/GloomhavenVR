using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine { }

namespace HarmonyLib
{
    internal sealed class HarmonyMethod
    {
        internal HarmonyMethod(Type type, string name) { Type = type; Name = name; }
        internal Type Type { get; }
        internal string Name { get; }
    }

    internal static class AccessTools
    {
        internal static MethodInfo? Method(Type type, string name, Type[]? parameters = null) =>
            parameters == null ? type.GetMethod(name) : type.GetMethod(name, parameters);
    }

    internal sealed class Harmony
    {
        internal readonly List<string> Targets = new();
        internal void Patch(MethodBase original, HarmonyMethod? prefix = null,
            HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null)
        {
            Targets.Add(original.DeclaringType!.Name + "." + original.Name + ":"
                + (prefix?.Name ?? string.Empty) + ":" + (finalizer?.Name ?? string.Empty));
        }
    }
}

internal sealed class BoolSetting { internal bool Value; }

internal class UIWindow
{
    private readonly HashSet<Type> _components = new();
    internal string name = "fixture window";
    internal string AudioItemShow { get; set; } = "open";
    internal string AudioItemHide { get; set; } = "close";
    internal void Add<T>() => _components.Add(typeof(T));
    internal T? GetComponent<T>() where T : class => _components.Contains(typeof(T)) ? (T)Activator.CreateInstance(typeof(T))! : null;
    public void Show(bool instant) { }
    public void Hide(bool instant) { }
}

internal sealed class UIShopItemWindow { }
internal sealed class UITempleWindow { }
internal sealed class UINewEnhancementWindow { }

internal sealed class UIPartyCharacterEnhancementAbilityCardsDisplay
{
    internal string audioItemShow = "card-tab";
    internal UIWindow? Window;
    internal T? GetComponentInParent<T>() where T : class => Window as T;
    public void Display() { }
}

namespace GloomhavenVR.Core
{
    using HarmonyLib;
    internal static class VRSession { internal static Harmony? Harmony = new(); }
    internal static class VRLog
    {
        internal static readonly List<string> DebugLines = new();
        internal static void Debug(string scope, string text) => DebugLines.Add(scope + ":" + text);
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
}

namespace GloomhavenVR.WorldUI
{
    internal static partial class ModalFallback { }
    internal static class WorldUIConfig { internal static BoolSetting ImmersiveTownServices = new(); }
    internal static class TownServiceEnhancementHandoff { internal static bool Enabled; }
}

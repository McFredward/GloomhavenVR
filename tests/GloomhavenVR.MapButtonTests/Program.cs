using System;
using System.Collections.Generic;
using UnityEngine.UI;
using GloomhavenVR.WorldUI.MapRoom;

static class Program
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    { _assertions++; if (!value) throw new Exception("Map button assertion: " + reason); }
    static void Main()
    {
        var rail = new MapButtonRail();
        var map = new UIGuildmasterButton(EGuildmasterMode.WorldMap);
        var shop = new UIGuildmasterButton(EGuildmasterMode.Merchant);
        rail.Add(map); rail.Add(shop);
        shop.Toggle.SetIsOnWithoutNotify(true);
        int mapChanged = 0, shopOff = 0, nativeContinuation = 0;
        bool helpVisible = true;
        map.Toggle.onValueChanged += selected =>
        {
            if (!selected) return;
            mapChanged++;
            // Native FTUE completion is a separate listener beside OnSelected. The old
            // Select() path deliberately suppressed the entire toggle event list.
            nativeContinuation++;
            helpVisible = false;
        };
        shop.Toggle.onValueChanged += selected => { if (!selected) shopOff++; };
        rail.Press(map);
        Check(map.ModeSelections == 1, "one native mode entry per press");
        Check(mapChanged == 1 && nativeContinuation == 1, "native tutorial toggle listeners receive the world-map press");
        Check(!helpVisible, "native tutorial listener can retire its current hint");
        Check(!shop.Toggle.isOn && shopOff == 1 && map.Toggle.isOn, "inactive group siblings receive native deselection");
        Check(shop.ModeSelections == 0, "deselecting a sibling never enters its mode");
        rail.Press(map); // stale-on toggle must still deliver one authorized request
        Check(map.ModeSelections == 2 && mapChanged == 2, "already-on target is normalized without double selection");
        for (int i = 0; i < 100; i++) rail.Press(map);
        Check(map.ModeSelections == 102 && mapChanged == 102, "repeated presses never double-call mode or tutorial callbacks");
        Check(shopOff == 1, "already-off siblings are not notified repeatedly");
        var unrelated = new UIGuildmasterButton(EGuildmasterMode.City);
        unrelated.Toggle.SetIsOnWithoutNotify(true);
        rail.Press(shop);
        Check(unrelated.Toggle.isOn && unrelated.ModeSelections == 0, "only declared rail siblings participate");
        Check(shop.ModeSelections == 1 && !map.Toggle.isOn, "merchant entry retains mode callback");
        Console.WriteLine($"Map button production press: {_assertions} assertions passed.");
    }
}

namespace UnityEngine.UI
{
    internal sealed class Toggle
    {
        private bool _on;
        internal event Action<bool>? onValueChanged;
        internal bool isOn { get => _on; set { if (_on == value) return; _on = value; onValueChanged?.Invoke(value); } }
        internal void SetIsOnWithoutNotify(bool value) => _on = value;
    }
}
internal enum EGuildmasterMode { WorldMap, Merchant, City }
internal sealed class UIGuildmasterButton
{
    internal readonly EGuildmasterMode GuildmasterMode;
    internal readonly Toggle Toggle = new();
    internal int ModeSelections;
    internal UIGuildmasterButton(EGuildmasterMode mode)
    {
        GuildmasterMode = mode;
        Toggle.onValueChanged += value => { if (value) ModeSelections++; };
    }
    // Native Select/Deselect use UIEventSyncExtensions.SetValue, which silences
    // all toggle events and manually invokes only their own mode callback.
    internal void Deselect() => Toggle.SetIsOnWithoutNotify(false);
    internal void Select() { if (!Toggle.isOn) { Toggle.SetIsOnWithoutNotify(true); ModeSelections++; } }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Note(string scope, string text) { }
        internal static void Error(string scope, string text) => throw new Exception(text);
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal sealed partial class MapButtonRail
    {
        private const string Scope = "MapRoom";
        private sealed class Cap { internal UIGuildmasterButton Button = null!; internal Toggle Toggle = null!; }
        private readonly List<Cap> _caps = new();
        private static Toggle ToggleOf(UIGuildmasterButton button) => button.Toggle;
        internal void Add(UIGuildmasterButton button) => _caps.Add(new Cap { Button = button, Toggle = button.Toggle });
        internal void Press(UIGuildmasterButton button) => SelectThroughTheGamesOwnApi(button, "test", true);
    }
}

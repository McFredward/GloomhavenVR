using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Adventure;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string name)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("ASSERT: " + name);
    }
    private static UIGuildmasterHUD Reset()
    {
        var hud = new UIGuildmasterHUD();
        Singleton<UIGuildmasterHUD>.Instance = hud; Singleton<UIGuildmasterHUD>.IsInitialized = true;
        Singleton<MapFTUEManager>.Instance = new MapFTUEManager(); Singleton<MapFTUEManager>.IsInitialized = true;
        AdventureState.MapState.IsCampaign = true;
        Party(2);
        MapInputGate.IsBlocked = false; StoryComposite.PointOfNoReturn = false;
        InputManager.GamePadInUse = false; MapFTUEManager.IsPlaying = false;
        return hud;
    }
    private static void Party(int count)
    {
        var chars = AdventureState.MapState.MapParty.SelectedCharacters; chars.Clear();
        for (int i = 0; i < count; i++) chars.Add(new object());
    }
    private static void Main()
    {
        UIGuildmasterHUD hud = Reset(); UICityEncounterButton city = hud.City;
        Check(ReferenceEquals(MapCityEventSource.Resolve(), city), "campaign resolves original city encounter component");
        Check(MapCityEventSource.Pressable(city), "ordinary eligible campaign event");
        AdventureState.MapState.IsCampaign = false;
        Check(MapCityEventSource.Resolve() == null && !MapCityEventSource.Pressable(city), "Guildmaster has no city event control");
        Reset(); Singleton<UIGuildmasterHUD>.IsInitialized = false;
        Check(MapCityEventSource.Resolve() == null && !MapCityEventSource.Pressable(city), "missing HUD fails closed");
        hud = Reset(); city = hud.City; hud.Requests = null;
        Check(!MapCityEventSource.Pressable(city), "unavailable native request set fails closed");
        Singleton<UIGuildmasterHUD>.Instance = new MissingRequestFieldHUD();
        Check(!MapCityEventSource.Pressable(city), "missing reflected native request field fails closed");
        Check(MapCityEventSource.Resolve() == null, "missing reflected city field is retried without synthetic button");
        hud = Reset(); city = hud.City;
        var first = new Component(); var second = new Component();
        hud.Requests!.Add(first); hud.Requests.Add(second);
        Check(!MapCityEventSource.Pressable(city), "native options requests block city event");
        Check(hud.Requests.Count == 2, "eligibility never consumes native locks");
        hud.Requests.Remove(first);
        Check(!MapCityEventSource.Pressable(city), "one outstanding native lock still blocks");
        hud.Requests.Remove(second);
        Check(MapCityEventSource.Pressable(city), "last native lock release restores eligibility");
        for (int count = 0; count <= 4; count++)
        {
            Party(count);
            Check(MapCityEventSource.Pressable(city) == (count > 1), "native party-size condition " + count);
        }
        Party(2); city.gameObject.activeSelf = false;
        Check(!MapCityEventSource.Pressable(city), "inactive mouse-mode control cannot be bypassed");
        InputManager.GamePadInUse = true;
        Check(MapCityEventSource.Pressable(city), "gamepad presentation-only hiding has physical cap path");
        city.Interactable = false;
        Check(!MapCityEventSource.Pressable(city), "native interactability still wins over gamepad fallback");

        // Independently vary every gate. Neither visibility nor gamepad input may erase a lock.
        for (int bits = 0; bits < 512; bits++)
        {
            hud = Reset(); city = hud.City;
            bool campaign = (bits & 1) != 0, interactable = (bits & 2) != 0;
            bool active = (bits & 4) != 0, gamepad = (bits & 8) != 0;
            bool mask = (bits & 16) != 0, committed = (bits & 32) != 0;
            bool nativeLock = (bits & 64) != 0, lesson = (bits & 128) != 0, completed = (bits & 256) != 0;
            AdventureState.MapState.IsCampaign = campaign; city.Interactable = interactable;
            city.gameObject.activeSelf = active; InputManager.GamePadInUse = gamepad;
            MapInputGate.IsBlocked = mask; StoryComposite.PointOfNoReturn = committed;
            if (nativeLock) hud.Requests!.Add(new Component());
            MapFTUEManager.IsPlaying = lesson; Singleton<MapFTUEManager>.Instance.Completed = completed;
            bool expected = campaign && interactable && (active || gamepad) && !mask && !committed && !nativeLock && (!lesson || completed);
            Check(MapCityEventSource.Pressable(city) == expected, "independent native gate combination " + bits);
            Check(hud.Requests!.Count == (nativeLock ? 1 : 0), "request collection unchanged " + bits);
        }
        Console.WriteLine($"City event production source: {_checks} assertions passed.");
    }
}

using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;

internal static class Program
{
    private static int _checks;
    internal static void Check(bool condition, string message)
    { _checks++; if (!condition) throw new Exception(message); }
    private static int Main()
    {
        var hud = new UIGuildmasterHUD();
        foreach (EGuildmasterMode service in new[] { EGuildmasterMode.Merchant, EGuildmasterMode.Temple, EGuildmasterMode.Enchantress })
        foreach (EGuildmasterMode map in new[] { EGuildmasterMode.City, EGuildmasterMode.WorldMap })
        {
            var native = new MapChoreographer(hud);
            MapRoomDriver.Choreographer = native;
            GuildmasterDestinations.Window = new UIWindow();
            hud.Mode = service;
            hud.Update(map);
            Check(hud.Mode == service, "open service controller was exited by map switch");
            Check(hud.Exits == 0 && hud.Enters == 0, "native service lifecycle was invoked");
            Check(native.Surface == map && native.VisibleLocationsMode == map, "map and original location visibility disagree");
            Check(GuildmasterDestinations.Home == map, "closing must return to the chosen map");
            Check(GuildmasterDestinations.Window.RowCount == 5 && GuildmasterDestinations.Window.Title == "original service", "native service content changed");
            hud.Update(EGuildmasterMode.None);
            Check(hud.Exits == 1 && hud.Mode == EGuildmasterMode.None, "explicit close did not exit exactly once");
            hud.Exits = hud.Enters = 0;
        }
        foreach (int gate in new[] { 0, 1, 2, 3, 4 })
        {
            MapRoomDriver.Active = gate != 0; WorldUIConfig.ConversionActive = gate != 1;
            GuildmasterDestinations.Window = gate == 2 ? null : new UIWindow { IsOpen = gate != 3 };
            ModalFallback.Live = gate != 4; hud.Mode = EGuildmasterMode.Temple;
            hud.Update(EGuildmasterMode.City);
            Check(hud.Mode == EGuildmasterMode.City, "flat, closed or releasing service intercepted");
        }
        MapRoomDriver.Active = WorldUIConfig.ConversionActive = ModalFallback.Live = true;
        GuildmasterDestinations.Window = new UIWindow(); hud.Mode = EGuildmasterMode.Temple;
        MapRoomDriver.Choreographer = new MapChoreographer(hud) { Throw = true };
        hud.Update(EGuildmasterMode.City);
        Check(hud.Mode == EGuildmasterMode.Temple, "failed map switch lost service mode");
        MapRoomDriver.Choreographer = new MapChoreographer(hud);
        hud.Update(EGuildmasterMode.WorldMap);
        Check(MapRoomDriver.Choreographer.Surface == EGuildmasterMode.WorldMap, "exception leaked nested-switch scope");
        Console.WriteLine("Town window map switch: " + _checks + " causal runtime assertions passed.");
        return 0;
    }
}
public enum EGuildmasterMode { None, Merchant, Temple, Enchantress, City, WorldMap }
public sealed class UIWindow { public bool IsOpen = true; public string Title = "original service"; public int RowCount = 5; }
public sealed class UIGuildmasterHUD
{
    public EGuildmasterMode Mode; public int Exits, Enters;
    public void UpdateCurrentMode(EGuildmasterMode next) => Update(next);
    public void Update(EGuildmasterMode next)
    {
        if (!TownWindowMapSwitch.Prefix(this, next, ref Mode)) return;
        Exits++; Enters++; Mode = next;
        if (GuildmasterDestinations.Window is {} window) { window.Title = next.ToString(); window.RowCount = 0; }
    }
}
public sealed class MapChoreographer
{
    private readonly UIGuildmasterHUD _hud; public bool Throw;
    public EGuildmasterMode Surface, VisibleLocationsMode;
    public MapChoreographer(UIGuildmasterHUD hud) { _hud = hud; }
    public void OpenCityMap(bool transition) => Open(EGuildmasterMode.City);
    public void OpenWorldMap(bool transition) => Open(EGuildmasterMode.WorldMap);
    private void Open(EGuildmasterMode map)
    {
        if (Throw) throw new Exception("native switch failure");
        Surface = map; _hud.Update(map); VisibleLocationsMode = _hud.Mode;
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static bool WantsDebug => true; internal static void Debug(string scope, string value) {} internal static void Note(string scope, string value) {} }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal static class ModalFallback { internal static bool Live = true; internal static bool FloatIsLive(UIWindow window) => Live; }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active = true; internal static MapChoreographer? Choreographer; }
    internal static class GuildmasterDestinations
    {
        internal static UIWindow? Window; internal static EGuildmasterMode Home;
        internal static bool IsMapSurfaceMode(EGuildmasterMode mode) => mode is EGuildmasterMode.City or EGuildmasterMode.WorldMap;
        internal static UIWindow? ModeWindow(EGuildmasterMode mode) => Window;
        internal static void RememberMapSurface(EGuildmasterMode mode) => Home = mode;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute { internal HarmonyPatch(Type type, string name) {} }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute {}
}

using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;

internal static class Program
{
    private static int _checks;
    internal static void Check(bool value, string why)
    { ++_checks; if (!value) throw new InvalidOperationException(why); }
    private static (DialogSurface Surface, UIConfirmationBoxManager Manager) Start(bool alreadyOpen = false)
    {
        CanvasConversion.Reset(); VRLog.Warnings = 0;
        VRModeStateMachine.TableInFrontOfPlayer = true;
        WorldUIConfig.ConversionActive = WorldUIConfig.Dialogs.Value = true;
        FlatScreen.ManualScreenActive = false;
        var manager = new UIConfirmationBoxManager();
        Singleton<UIConfirmationBoxManager>.Instance = manager;
        if (alreadyOpen) manager.Show();
        var surface = new DialogSurface(); surface.Tick();
        return (surface, manager);
    }
    private static void Main()
    {
        var (surface, manager) = Start(true);
        Check(Choreographer.s_Choreographer == null && CanvasConversion.Converts == 1,
            "already-open map confirmation converts without scenario choreographer");
        Check(CanvasConversion.Last!.ModLayerEnabled,"standalone confirmation admits isolated filtered capture and scoped blur treatment");
        Check(CanvasConversion.Last!.HideBackground,"standalone confirmation removes only full-frame backdrops before reveal and allocation");
        CanvasConversion.Last!.HostRaycaster.enabled = false;
        surface.Tick();
        Check(CanvasConversion.Last.HostRaycaster.enabled, "modal lock cannot disable original confirmation raycaster");
        int confirms = 0, cancels = 0;
        for (int i = 0; i < 40; ++i)
        {
            manager.Show(); surface.Tick();
            manager.Confirm(() => ++confirms);
            manager.Confirm(() => ++confirms);
            surface.Tick();
            Check(!manager.CurrentBox.IsOpen && confirms == i + 1, "native confirm continues exactly once");
            manager.Show(); surface.Tick();
            manager.Cancel(() => ++cancels);
            manager.Cancel(() => ++cancels);
            surface.Tick();
            Check(!manager.CurrentBox.IsOpen && cancels == i + 1, "native cancel continues exactly once");
        }
        manager.Show(); surface.Tick();
        int converts = CanvasConversion.Converts;
        manager.Confirm(() => manager.Show()); surface.Tick();
        Check(manager.CurrentBox.IsOpen && CanvasConversion.Converts == converts + 1,
            "queued confirmation reopens immediately on the same native box");
        FlatScreen.ManualScreenActive = true; surface.Tick();
        Check(!CanvasConversion.ActivePanels.Contains(CanvasConversion.Last!) && manager.CurrentBox.IsOpen,
            "desktop rescue releases only presentation, preserving native waiter");
        FlatScreen.ManualScreenActive = false; surface.Tick();
        Check(CanvasConversion.ActivePanels.Contains(CanvasConversion.Last!), "return from desktop restores already-open confirmation");
        VRModeStateMachine.TableInFrontOfPlayer = false; surface.Tick();
        Check(!CanvasConversion.ActivePanels.Contains(CanvasConversion.Last!) && DialogSurface.FallbackWindow == null,
            "leaving table releases world presentation without hiding native window");
        VRModeStateMachine.TableInFrontOfPlayer = true; surface.Tick();
        Check(CanvasConversion.ActivePanels.Contains(CanvasConversion.Last!), "return to map recovers without a new Show event");
        surface.Shutdown();

        foreach (string failure in new[] { "null", "throw", "place" })
        {
            (surface, manager) = Start();
            CanvasConversion.ReturnNull = failure == "null";
            CanvasConversion.ThrowConvert = failure == "throw";
            CanvasConversion.ThrowPlace = failure == "place";
            manager.Show(); surface.Tick();
            Check(DialogSurface.FallbackWindow == manager.CurrentBox.Window && manager.CurrentBox.IsOpen,
                "failed dedicated conversion publishes exact original fallback window: " + failure);
            Check(failure != "place" || CanvasConversion.Releases == 1,
                "partially converted dialog releases before fallback owns it");
            for (int i = 0; i < 20; ++i) surface.Tick();
            Check(CanvasConversion.Converts == 1 && VRLog.Warnings == 1,
                "failed opening transfers ownership once without per-frame retry or log flood");
            manager.Hide(); surface.Tick();
            Check(DialogSurface.FallbackWindow == null, "closed native dialog releases fallback claim");
            CanvasConversion.ReturnNull = CanvasConversion.ThrowConvert = CanvasConversion.ThrowPlace = false;
            manager.Show(); surface.Tick();
            Check(CanvasConversion.Last != null && CanvasConversion.ActivePanels.Contains(CanvasConversion.Last),
                "next native opening retries dedicated conversion after prior failure");
            surface.Shutdown();
        }
        (surface, manager) = Start();
        WorldUIConfig.Dialogs.Value = false; manager.Show(); surface.Tick();
        Check(DialogSurface.FallbackWindow == manager.CurrentBox.Window && CanvasConversion.Converts == 0,
            "disabled dedicated surface delegates to generic fallback");
        surface.Shutdown();
        (surface, manager) = Start();
        CanvasConversion.ThrowPlace = CanvasConversion.ThrowRelease = true;
        manager.Show();
        try { surface.Tick(); } catch (InvalidOperationException) { }
        Check(CanvasConversion.Last != null && CanvasConversion.ActivePanels.Contains(CanvasConversion.Last) && DialogSurface.FallbackWindow == null,
            "failed restoration retains conversion ownership for retry");
        CanvasConversion.ThrowRelease = false; surface.Tick();
        Check(CanvasConversion.Releases == 1 && DialogSurface.FallbackWindow == manager.CurrentBox.Window,
            "retry restores original subtree before fallback publication");
        surface.Shutdown();
        LateCameraCases();
        OwnerCases.Run();
        CaptureBoundaryCases745.Run();
        Console.WriteLine($"Dialog surface: {_checks} assertions passed.");
    }

    private static void LateCameraCases()
    {
        var (surface, manager) = Start();
        CanvasConversion.WorldCamera = null;
        manager.Show(); surface.Tick();
        Check(CanvasConversion.Converts == 1 && CanvasConversion.Placements == 0 && manager.CurrentBox.IsOpen,
            "camera-less native request retains its original conversion without completing the choice");
        for (int i = 0; i < 6; i++) surface.Tick();
        Check(CanvasConversion.Converts == 1 && CanvasConversion.Placements == 0,
            "camera-less opening does not allocate repeated hosts");
        var head = new UnityEngine.Camera(); head.transform.position = new UnityEngine.Vector3(4,5,6);
        CanvasConversion.WorldCamera = head; surface.Tick();
        var panel = CanvasConversion.Last!;
        Check(CanvasConversion.Placements == 1 && panel.HostRect.position.x == 4
            && panel.HostRect.position.y == 5 && panel.HostRect.position.z == 6.75f,
            "late head availability places the pending original confirmation exactly once");
        panel.HostRect.position = new UnityEngine.Vector3(9,8,7);
        head.transform.position = new UnityEngine.Vector3(1,2,3);
        for (int i = 0; i < 6; i++) surface.Tick();
        Check(CanvasConversion.Placements == 1 && panel.HostRect.position.x == 9
            && panel.HostRect.position.y == 8 && panel.HostRect.position.z == 7,
            "settled or manually moved confirmation never follows later head motion");
        manager.Hide(); surface.Tick(); CanvasConversion.WorldCamera = null;
        manager.Show(); surface.Tick(); CanvasConversion.WorldCamera = head; surface.Tick();
        Check(CanvasConversion.Placements == 2 && CanvasConversion.Converts == 2,
            "a fresh pooled opening gets its own late-camera placement");
        surface.Shutdown();
    }
}

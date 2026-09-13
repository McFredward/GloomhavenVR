using GloomhavenVR.Core.Events;
using GloomhavenVR.Cards.Patches;
using GloomhavenVR.WorldUI;

internal static partial class Program
{
    private static void FlatScreenPolicyCases()
    {
        var screen = new FlatScreen();
        var manager = new UIManager();
        UIManager.Instance = manager;
        manager.elementsLockUI.Add(Hand());
        WorldUIConfig.ConversionActive = true;
        WorldUIConfig.Dialogs.Value = true;
        VRModeStateMachine.CurrentMode = VRMode.ModalUI;
        HandSuppression.Active = true;
        Check(!screen.Read(), "Production FlatScreen suppresses the empty native card-loss fallback");
        ModalFallback.ScreenWanted = true;
        Check(screen.Read(), "Explicit modal composite stays above card-loss suppression");
        ModalFallback.ScreenWanted = false;
        Check(screen.Read(manual: true), "Manual screen request stays above card-loss suppression");
        Check(screen.Read(rescue: true), "Programmatic rescue stays above card-loss suppression");
        VRModeStateMachine.CurrentMode = VRMode.Menu2D;
        Check(screen.Read(), "Main menu stays visible even with a stale native hand transaction");
        VRModeStateMachine.CurrentMode = VRMode.TableIdle;
        Check(!screen.Read(), "Ordinary table mode does not request a desktop screen");
        VRModeStateMachine.CurrentMode = VRMode.ModalUI;
        manager.elementsLockUI.Add(new UnityEngine.GameObject());
        Check(screen.Read(), "Production FlatScreen retains fallback for mixed native owners");
        manager.elementsLockUI.Clear();
        Check(screen.Read(), "Production FlatScreen immediately releases cancelled hand suppression");
        ModalFallback.WindowModalActive = true;
        Check(!screen.Read(), "An existing converted native window owns its modal without FlatScreen");
        ModalFallback.WindowModalActive = false;
        screen.ConfirmationOpen = true;
        Check(!screen.Read(), "Converted confirmation retains existing desktop suppression");
        WorldUIConfig.Dialogs.Value = false;
        Check(screen.Read(), "Disabled dialog conversion retains native confirmation fallback");
        WorldUIConfig.Dialogs.Value = true;
        screen.ConfirmationOpen = false;
        HandSuppression.BurnActive = true;
        Check(!screen.Read(), "Existing local burn tail still suppresses its empty fallback");
        ModalFallback.ScreenWanted = true;
        Check(screen.Read(), "Explicit modal composite also outranks the existing local burn tail");
        ModalFallback.ScreenWanted = false;
        HandSuppression.BurnActive = false;
        GloomhavenVR.MapRoom.MapRoomDriver.Active = true;
        Check(!screen.Read(manual: true), "Map room keeps its existing manual-screen exclusion");
        Check(screen.Read(rescue: true), "Programmatic rescue still bypasses map-room exclusion");
        LoadingIndicator.FlatScreenSuppressed = true;
        Check(!screen.Read(rescue: true), "Loading exclusion stays above rescue");
        LoadingIndicator.FlatScreenSuppressed = false;
        GloomhavenVR.MapRoom.MapRoomDriver.Active = false;
        WorldUIConfig.ConversionActive = false;
        Check(!screen.Read(rescue: true), "Disabled conversion stays above rescue");
    }
}

using ScenarioRuleLibrary.CustomLevels;

// Only capability, scene and presentation endpoints are fixture boundaries.
// The complete production TutorialGrabStep and native-message-hold lifecycle run.
public sealed class LevelMessagesUIHandler
{
    public static LevelMessagesUIHandler? s_Instance;
    public bool DisplayDelayInEffect;
    public CLevelMessage? CurrentlyDisplayedHelpTextMessage;
    public int Shows, Hides;
    public void ShowHelpText(CLevelMessage message)
    { Shows++; CurrentlyDisplayedHelpTextMessage = message; }
    public void HideCurrentlyShownHelpTextMessage()
    { Hides++; CurrentlyDisplayedHelpTextMessage = null; }
}
namespace UnityEngine { public static class Time { public static float unscaledTime; } }
namespace GloomhavenVR.WorldUI { }
namespace GloomhavenVR.Board.FigureGrab
{
    public static class FigureGrabConfig { public static readonly Capability GrabFigures = new(); }
    public sealed class Capability { public bool Value = true; }
    public static class HeldFigures { public static int Count; }
    public static class FigureIntentPeek { public static bool Active; }
}

namespace UnityEngine
{
    static class Object
    {
        public static object[] Instances = [];
        public static T[] FindObjectsOfType<T>(bool inactive) => Instances.OfType<T>().ToArray();
    }
    static class Time { public static float unscaledTime; }
    static class Mathf
    {
        public static float Abs(float n) => Math.Abs(n);
        public static float Clamp(float n, float min, float max) => Math.Clamp(n, min, max);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
}
namespace GloomhavenVR.Core
{
    static class VRLog
    {
        public static void Info(string a, string b) { }
        public static void Error(string a, string b) { }
        public static void Debug(string a, string b) { }
        public static void Warn(string a, string b) { }
    }
    static class Loc { public static string Mod(string key) => key; }
    sealed class Setting<T>(T initial)
    {
        private T _value = initial;
        public event Action<object, EventArgs>? SettingChanged;
        public T Value { get => _value; set { _value = value; SettingChanged?.Invoke(this, EventArgs.Empty); } }
    }
}
namespace GloomhavenVR
{
    static class Plugin { public static Core.Setting<string> PrimaryHand = new("Right"); }
}
namespace GloomhavenVR.Core.Events
{
    enum VRMode { Menu2D, TableIdle, CardSelection, HalfSelection, BoardTargeting, ModalUI }
    static class VRModeStateMachine { public static VRMode CurrentMode; }
}
namespace GloomhavenVR.Hands
{
    enum HandSide { Left, Right }
    enum HapticPreset { HoverTick }
    class VRHand(HandSide side)
    {
        public HandSide Side = side;
        public bool HasPose = true, ThumbstickClick, SecondaryButton;
        public (float x, float y) Thumbstick;
        public int Haptics;
        public void SendHaptic(HapticPreset preset) { Haptics++; }
    }
    static class VRHands
    {
        public static VRHand Left = new(HandSide.Left), Right = new(HandSide.Right);
        public static VRHand? Primary = Right;
        public static VRHand Get(HandSide side) => side == HandSide.Left ? Left : Right;
    }
}
namespace GloomhavenVR.Rig
{
    using GloomhavenVR.Core;
    using GloomhavenVR.Hands;
    enum TurnMode { Off, Snap, Smooth }
    enum TurnHandChoice { Left, Right, Dominant }
    static class ComfortSettings
    {
        public static event Action<string>? AnyChanged;
        public static void RaiseChanged(string key) => AnyChanged?.Invoke(key);
        public static bool IsBound = true;
        public static Setting<TurnMode> Turn = new(TurnMode.Snap);
        public static Setting<TurnHandChoice> TurnHand = new(TurnHandChoice.Right);
    }
    static class LocalTurnControl
    {
        public static bool ThisSeatActs = true;
        public static HandSide Resolve(TurnHandChoice hand) => hand switch
        {
            TurnHandChoice.Left => HandSide.Left,
            TurnHandChoice.Right => HandSide.Right,
            _ => VRHands.Primary?.Side ?? HandSide.Right
        };
    }
}
namespace GloomhavenVR.Board
{
    using GloomhavenVR.Core;
    static class BoardConfig
    {
        public static Setting<AoeRotationInputMode> AoeRotationInput = new(AoeRotationInputMode.UpperFaceButtons);
        public static Setting<float> AoeFlickThreshold = new(.7f);
        public static Setting<float> AoeRepeatInterval = new(.4f);
    }
    class BoardDriver { }
}
class Choreographer
{
    public static Choreographer? s_Choreographer = new();
    public enum ChoreographerStateType { Play, WaitingForTileSelected }
    public class WaitState { public ChoreographerStateType m_State; }
    public WaitState m_WaitState = new();
}
static class TimeManager { public static bool IsPaused; }
class WorldspaceStarHexDisplay
{
    public object m_SavedAbility = new();
    public bool m_TurningRight = true;
    private float _lastDirection;
    public static WorldspaceStarHexDisplay? Instance;
    public enum WorldSpaceStarDisplayState { ShowNone, MovementSelection, TargetSelection }
    public enum EAbilityDisplayType { Normal, AreaOfEffect, EnemyAreaOfEffect, SelectObjectPositionAreaOfEffect }
    public WorldSpaceStarDisplayState CurrentDisplayState = WorldSpaceStarDisplayState.TargetSelection;
    public EAbilityDisplayType CurrentAbilityDisplayType = EAbilityDisplayType.AreaOfEffect;
    public bool m_HexDisplayToggledOff, m_AllHexesHighlighted, LockView;
    public int AbilityRange = 3, AreaEffectAngle, Calls, Attacks, Objects;
    public void RotateAOEClockwise(bool turnRight)
    {
        if (_lastDirection + .3f < UnityEngine.Time.unscaledTime)
        { m_TurningRight = turnRight; _lastDirection = UnityEngine.Time.unscaledTime; }
        Calls++; AreaEffectAngle = (AreaEffectAngle + (m_TurningRight ? 300 : 60)) % 360;
    }
    public void DisplayAOEStars() { Attacks++; }
    public void DisplaySelectObjectPositionAOEStars() { Objects++; }
}
namespace GloomhavenVR.Compat
{
    static class TutorialVR { public static bool Enabled = true, IsTutorialActive = true; }
}
class TextField { public string text = "unchanged"; }
class MessagePage { public string? PageTextKey, PageTextKeyController; }
class Message { public string? TitleKey, TitleKeyController; }
class LevelMessagePageUI { public MessagePage? page; public TextField? information = new(); }
class LevelMessageUILayout { public Message? _message; public TextField? title = new(); }

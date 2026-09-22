using System.Collections.Generic;

namespace UnityEngine
{
    internal class Component { internal readonly GameObject gameObject = new(); }
    internal sealed class GameObject { internal bool activeSelf = true; }
}

namespace MapRuleLibrary.Adventure
{
    internal static class AdventureState { internal static readonly TestMapState MapState = new(); }
    internal sealed class TestMapState
    {
        internal bool IsCampaign = true;
        internal readonly TestParty MapParty = new();
    }
    internal sealed class TestParty { internal readonly List<object> SelectedCharacters = new(); }
}

internal static class Singleton<T> where T : class
{
    internal static T Instance = null!;
    internal static bool IsInitialized;
}
internal class UIGuildmasterHUD
{
    // These are deliberately private and exactly named: production reflection runs unchanged.
    private readonly UICityEncounterButton cityEncounterButton = new();
    private HashSet<UnityEngine.Component>? disableOptionsRequests = new();
    internal UICityEncounterButton City => cityEncounterButton;
    internal HashSet<UnityEngine.Component>? Requests { get => disableOptionsRequests; set => disableOptionsRequests = value; }
}
// Reflection against the runtime type cannot find a renamed/missing private request field.
internal sealed class MissingRequestFieldHUD : UIGuildmasterHUD { }
internal sealed class UICityEncounterButton : UnityEngine.Component { internal bool Interactable = true; }
internal static class InputManager { internal static bool GamePadInUse; }
internal static class StoryComposite { internal static bool PointOfNoReturn; }
internal enum EMapFTUEStep { BuyItem }
internal sealed class MapFTUEManager
{
    internal static bool IsPlaying;
    internal bool Completed = true;
    internal bool HasCompletedStep(EMapFTUEStep step) => step == EMapFTUEStep.BuyItem && Completed;
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapInputGate { internal static bool IsBlocked; }
}

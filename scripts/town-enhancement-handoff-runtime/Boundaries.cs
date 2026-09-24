// The production handoff runs with real Unity transforms, components and native Selectable.
// Game models/controllers and the established fan flight are explicit boundaries. This fixture
// proves ownership, original-callback dispatch and lifecycle; it does not render a headset frame.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TMPro
{
    public class TMP_Text : MonoBehaviour { public string text = ""; public float fontSize; public RectTransform rectTransform => (RectTransform)transform; }
}
namespace ScenarioRuleLibrary { public sealed class CAbilityCard { public int ID; } }
public sealed class Owner { public string CharacterID = "owner"; }
public class Singleton<T> { public static T? Instance; }
public class UIEnhancementConfirmationBox : MonoBehaviour {}
public class UIWindow : MonoBehaviour { public bool IsOpen = true; }
public class AbilityCardUI : MonoBehaviour { public ScenarioRuleLibrary.CAbilityCard AbilityCard = null!; public FullAbilityCard fullAbilityCard = null!; }
public class FullAbilityCard : MonoBehaviour { }
public class UIEnhanceCardSlot : MonoBehaviour
{
    public AbilityCardUI AbilityCard = null!; public Selectable Selectable = null!; public Action? Selected;
    public void Select() => Selected?.Invoke();
}
public class UINewEnhancementWindow : MonoBehaviour
{
    public sealed class Display { public List<UIEnhanceCardSlot> slotsPool = new(); }
    public Display CardsDisplay = new(); public Owner character = new(); public AbilityCardUI? selectedCard;
    public bool _isConfirmationBoxOpened; public int Clears;
    public void DeselectCurrentCard() { }
    public void OnSelectedCardToEnhance(AbilityCardUI? card) { selectedCard = card; if (card == null) Clears++; }
}
public enum EGuildmasterMode { None, Enchantress, Merchant }
namespace GloomhavenVR { public static class Defaults { public const bool MapRoomHand = true; } }
namespace GloomhavenVR.Core.Events
{
    public enum VRMode { TableIdle, ModalUI }
    public static class VRModeStateMachine { public static VRMode CurrentMode; }
}
namespace GloomhavenVR.Core
{
    public static class Loc { public static string Mod(string key) => key; }
    public static class VRLayers { public static void Apply(GameObject root) { } }
    public static class VRLog { public static void Debug(string a, string b) { } public static void Warn(string a, string b) { } }
}
namespace GloomhavenVR.Hands
{
    public class VRHand { public string Side="Right"; public float WorldScale=1f; public bool HasPose = true; public Holder Grabber; public VRHand(){Grabber=new Holder(this);} }
    public partial class Holder {
        public object? Held; private bool _enabled=true; private VRHand _hand; private string _grabLabel="fixture";
        public Holder(VRHand hand){_hand=hand;} private void LogRefusal(string text){}
        private static string DescribeGrabbable(object obj)=>"card";
        private void BeginGrab(Interact.IGrabbable target,bool releaseOnTriggerUp,string button,string source){Held=target;target.OnGrab(_hand);}
    }
    public static class VRHands { public static VRHand? Left, Right, Primary; }
}
namespace GloomhavenVR.Rig { public static class VRRigDriver { public static Camera? HeadCamera; } }
namespace GloomhavenVR.Cards
{
    public static class CardsConfig { public static float CardHeight=.24f; public static Config InspectScale=new(); public class Config { public float Value=1f; } }
    public class CardBase : MonoBehaviour { public virtual bool CanGrab=>true; }
    public partial class VRCard : CardBase, Hands.Interact.IGrabbable
    {
        public Owner Owner = new(); public ScenarioRuleLibrary.CAbilityCard Model = new();
        public bool Owned = true, InLoadout = true, CurrentCharacter = true, IsHeld, IsFlying, IsVanishing, Grabbable, InspectOnly, AllowsGateHand;
        public event Action<VRCard, Hands.VRHand>? Grabbed;
        public void SetHome(Transform home, Vector3 p, Quaternion q, float scale)
        { transform.SetParent(home, false); transform.localPosition = p; transform.localRotation = q; transform.localScale = Vector3.one * scale; }
        public bool FullCollider; public void ResetColliderRegion(){FullCollider=true;}
        public void SetHandPopSuppressed(bool value) {}
        public void OnGrab(Hands.VRHand hand)=>Grab(hand);
        public void Grab(Hands.VRHand hand) { IsHeld = true; hand.Grabber.Held = this; Grabbed?.Invoke(this, hand); }
    }
    public sealed class CardFan { public static CardFan? Current = new(); public int Removed; public void Remove(VRCard card) => Removed++; }
    public static class CardsDriver
    {
        public static int Returned, Rebuilds; public static VRCard? LastReturned; public static Transform? FanRoot;
        public static bool OffScenarioFanActive = true; public static Action? Completed;
        public static IReadOnlyList<VRCard>? OffScenarioFanCards;
        public static void RequestRebuild() => Rebuilds++;
        public static void ReturnTownOffering(VRCard card, Action? completed = null)
        { Returned++; LastReturned = card; card.IsFlying = true; Completed = completed; card.transform.SetParent(FanRoot, true); }
        public static void Complete() { if (LastReturned != null) LastReturned.IsFlying = false; Completed?.Invoke(); Completed = null; }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    public static class MapRoomDriver
    {
        public static bool Active = true, CanVisit = true; public static int Visits;
        public static bool CanVisitTownService(EGuildmasterMode mode) => CanVisit;
        public static bool PressGuildmasterMode(EGuildmasterMode mode, string reason) { Visits++; GuildmasterDestinations.Mode = mode; return true; }
    }
    public static class GuildmasterDestinations { public static EGuildmasterMode Mode; public static EGuildmasterMode CurrentDestinationMode() => Mode; }
    public static class MapRoomHand
    {
        public static bool TryOwnedTownCard(Cards.VRCard card, out Owner? owner, out ScenarioRuleLibrary.CAbilityCard? model)
        { owner = card.Owner; model = card.Model; return MapRoomDriver.Active && card.Owned && card.InLoadout && card.CurrentCharacter; }
    }
}
namespace GloomhavenVR.WorldUI
{
    public static class TownServicePalmConfirmation { public static bool Owned; public static bool OwnsCurrent(UIWindow? window)=>Owned && window!=null; public static void Begin(UIEnhancementConfirmationBox box,Transform seat){} public static void CancelOwned(UIWindow window){} }
    public static class TownServicePresentation { public static uint Session = 22; public static float SessionAge = 3f; }
    public static class WorldUIConfig
    {
        public static readonly ToggleValue ImmersiveTownServices = new(); public static ToggleValue? MapRoomHand = new();
        public class ToggleValue { public bool Value = true; }
    }
    public static class StoryComposite { public static bool PointOfNoReturn; }
    public sealed class TownServiceStation { public Transform Root = null!; }
    public static class TownServicePopulation
    {
        public static TownServiceStation? Station;
        public static bool Available(byte service) => Station != null;
        public static TownServiceStation? Acquire(byte service) => Station;
    }
    public static class TownServiceMerchantZone
    {
        public static GameObject CreateTemplate(TMPro.TMP_Text? unused)
        {
            var go = new GameObject("Zone", typeof(RectTransform), typeof(CanvasGroup));
            new GameObject("Border", typeof(RectTransform)).transform.SetParent(go.transform, false);
            new GameObject("Caption", typeof(RectTransform), typeof(TMPro.TMP_Text)).transform.SetParent(go.transform, false);
            return go;
        }
    }
}

namespace GloomhavenVR.Hands.Interact {
 public interface IGrabbable {bool CanGrab{get;} void OnGrab(VRHand hand);}
 public interface IGrabbableHandFilter {bool AllowsHand(VRHand hand);}
}

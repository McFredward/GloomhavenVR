// Native Unity transforms, CanvasGroup, Button and UnityEvent are used directly.
// The HUD mode source, conversion lookup/transfer and game navigation/audio are
// declared ports. UIWindow Show/Hide/state callbacks and confirmation methods
// are bound separately from the installed game's unmodified assembly.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public enum EGuildmasterMode { None, Merchant, Temple, Enchantress }
public enum BoxConfirmationType { Buy, Sell, General }
public class Singleton<T> where T : class
{ public static T? Instance; public static bool IsInitialized => Instance != null; }
public static class Probe
{
 public static readonly List<string> Events = new();
 public static int ObservationFaults;
 // Only failure-path variants replace the observation call with this declared
 // presentation port. The production prefix's try/catch and native Show are unchanged.
 public static void ThrowObservation(UIItemConfirmationBox _) { ObservationFaults++;throw new InvalidOperationException("fixture observation fault"); }
}
public static class InputManager { public static bool GamePadInUse => false; }
public static class AudioControllerUtils { public static void PlaySound(string _) { } }
public enum EscapeKeyAction { None, Toggle }
public static class UIWindowManager
{ public static void RegisterEscapable(UIWindow _) { } public static void UnregisterEscapable(UIWindow _) { } }
public sealed class UIGameStateMachine
{
 public void Enter(Script.GUI.SMNavigation.States.CampaignMapStates.CampaignMapStateTag _) => Probe.Events.Add("confirmation-enter");
 public void ToNonMenuPreviousState() => Probe.Events.Add("previous:" + GloomhavenVR.WorldUI.MapRoom.GuildmasterDestinations.Mode);
}
public sealed class UINavigation { public readonly UIGameStateMachine StateMachine = new(); }
namespace Script.GUI.SMNavigation.States.CampaignMapStates { public enum CampaignMapStateTag { EnhancmentConfirmation } }
namespace GLOOM { public static class LocalizationManager { public static string GetTranslation(string value) => value; } }
public sealed class SkipFrameKeyActionHandlerBlocker { public void Run() { } }
public sealed class ControllerInputAreaLocal { public int Enables, Destroys; public void Enable() => Enables++; public void Destroy() => Destroys++; }
public class ExtendedButton : Button { public string TextLanguageKey = ""; public string? mouseDownAudioItem; }
public sealed class Label { public string text = ""; }
public static class StringExtensions { public static bool IsNOTNullOrEmpty(this string text) => !String.IsNullOrEmpty(text); }
namespace ScenarioRuleLibrary { public sealed class CItem { public string ID = "fixture-original-item"; } }
// Artwork construction is a declared port; native Show/reset/recycle/callback ordering is bound
// from the installed game, with actual Unity objects and buttons for its original content.
public sealed class ItemCardUI : MonoBehaviour
{ public string CardID = "fixture-original-item"; public ScenarioRuleLibrary.CItem item = null!; public int Reloads; public void ReloadBackground() { Reloads++; } }
public static class ObjectPool
{
 public enum ECardType { Item }
 public static GameObject SpawnCard(string id,ECardType _,Transform parent,bool resetLocalScale,bool resetToMiddle)
 { var card=new GameObject(id,typeof(RectTransform),typeof(ItemCardUI));card.transform.SetParent(parent,false);return card; }
 public static void RecycleCard(string id,ECardType _,GameObject card) => UnityEngine.Object.DestroyImmediate(card);
}
namespace GloomhavenVR.Core
{
 internal static class VRLog { internal static readonly List<string> Alerts=new(); internal static void Note(string _, string text) { } internal static void Info(string _,string text) { } internal static void Alert(string _, string text) => Alerts.Add(text); }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
 internal static class GuildmasterDestinations
 {
  internal static EGuildmasterMode Mode;
  internal static readonly Dictionary<EGuildmasterMode, UIWindow> Windows = new();
  internal static EGuildmasterMode CurrentDestinationMode() => Mode;
  internal static UIWindow? ModeWindow(EGuildmasterMode mode) => Windows.TryGetValue(mode, out var window) ? window : null;
 }
}
namespace GloomhavenVR.WorldUI
{
 internal static class ConfirmationOnOwner {internal static bool IsOwned(UIWindow window)=>false;}
 internal static class TownServicePresentation { internal static bool Quiet, Owned; internal static bool OwnsWindow(UIWindow window) => Owned || TownServiceWindowMask.OwnsRetiring(window); internal static bool IsQuietController(UIWindow window,byte service) => Quiet; }
 internal static class ChromeParkTuning { internal const float OffsetEpsilonPx = .02f; }
 internal sealed class ConvertedPanel { internal readonly UIWindow Window; internal ConvertedPanel(UIWindow window) { Window=window; } }
 internal static class ModalFallback
 {
  internal static readonly Dictionary<UIWindow,ConvertedPanel> Panels = new();
  internal static ConvertedPanel? PanelFor(UIWindow window) => Panels.TryGetValue(window,out var panel) ? panel : null;
  internal static bool FloatIsLive(UIWindow window) => PanelFor(window) != null && window.IsOpen;
 }
 internal static class CanvasConversion
 {
  internal static int Transfers;
  // The independent native input suite binds the actual transfer and raycaster.
  // This lifecycle suite checks that production seating calls the two edges.
  internal static void TransferSeatedSubtree(RectTransform root, ConvertedPanel? destination) { Transfers++; }
 }
}

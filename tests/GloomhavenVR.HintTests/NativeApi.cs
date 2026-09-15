// Minimal API model for production provenance and handover code. No renderer or Unity pose claims.
using System;
using System.Collections.Generic;
using System.Linq;
using GLOO.Introduction;

namespace UnityEngine
{
    public class Object
    {
        public string name = "test";
        public static readonly List<Object> Objects = new();
        public static T[] FindObjectsOfType<T>(bool includeInactive) => Objects.OfType<T>().ToArray();
    }
    public class Transform : Object { }
    public class Component : Object { public Transform transform = new(); }
}
namespace UnityEngine.UI { public class UIWindow : UnityEngine.Component { public bool NativeHidden; } }
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
        public HarmonyPatch(Type type, string method, Type[] parameters) { }
    }
}
namespace GLOO.Introduction
{
    public class UIIntroduceBase : UnityEngine.Component { public UIIntroduceProcess process = new(); }
    public class UIIntroduceProcess : UnityEngine.Component { }
    public class UIIntroduceProcessHighlight : UIIntroduceProcess { }
    public class UIIntroductionManager { public LevelMessageUILayoutGroup? LayoutGroup; }
    public class IntroductionConfigUI { }
    public class IntroductionStepUI { }
    public enum EIntroductionConcept { LinkedQuest }
}
public class UIPartyCharacterAbilityCardsDisplay : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UIPartyCharacterEquipmentDisplay : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UIPerksWindow : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UILevelUpWindow : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UIShopItemWindow : UnityEngine.Component { public UIIntroduceBase? introduction, ftueStep; }
public class UINewEnhancementWindow : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UIBattleGoalPickerWindow : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UITempleWindow : UnityEngine.Component { public UIIntroduceBase? introduction; }
public class UICharacterCreatorPersonalQuestStep : UnityEngine.Component { public UIIntroduceBase? introduction, ftuePQ; }
public class UICharacterCreatorClassStep : UnityEngine.Component { public UIIntroduceBase? ftueClass; }
public class UICampaignAdventurePartyAssemblyWindow : UnityEngine.Component { public UIIntroduceBase? ftueStep; }
public class QuestManager : UnityEngine.Component { public UIIntroduceBase? questIntroduction; public UnityEngine.Component? questLog; }
public class UIIntroductionRewardsProcess : UnityEngine.Component { public UIIntroduceProcess process = new(); }
public class CampaignRewardsManager : UnityEngine.Component { public UIIntroductionRewardsProcess? introductionProcess; public UnityEngine.Component? rewardsWindow; }
public static class Singleton<T> where T : new() { public static bool IsInitialized; public static T Instance = new(); }
public class LevelMessageUILayoutGroup { public UnityEngine.UI.UIWindow window = new(); }
namespace GloomhavenVR.Core { public static class VRLog { public static void Warn(string scope, string text) { } } }
namespace GloomhavenVR.WorldUI
{
    internal static class Events { internal static readonly List<string> Trace = new(); }
    internal class ConvertedPanel { internal bool Released; }
    internal class Grab { internal bool Destroyed; internal void Destroy() { Destroyed = true; Events.Trace.Add("grab"); } }
    internal class WindowPanel
    {
        internal UnityEngine.UI.UIWindow Window = new();
        internal ConvertedPanel Panel = new();
        internal bool UserClosing;
        internal Grab? Grab;
    }
    internal static partial class ModalFallback
    {
        internal static readonly List<WindowPanel> Converted = new();
        private static void ReleasePreConvertHide(UnityEngine.UI.UIWindow w, string why) => Events.Trace.Add("prehide");
        private static void ReleaseScreenBind(UnityEngine.UI.UIWindow w, string why) => Events.Trace.Add("screenbind");
    }
    internal static class WindowMaterialise
    {
        internal static void DropPreRoll(ConvertedPanel p, string why) => Events.Trace.Add("preroll");
        internal static void Cancel(ConvertedPanel p, string why) => Events.Trace.Add("cancel");
    }
    internal static class CanvasConversion
    {
        internal static void Release(ConvertedPanel p) { p.Released = true; Events.Trace.Add("release"); }
    }
}

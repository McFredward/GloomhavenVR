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
    public class Transform : Object
    {
        public Transform? parent;
        internal readonly Dictionary<Type, Component> Components = new();
        public T? GetComponent<T>() where T : Component => Components.TryGetValue(typeof(T), out Component? c) ? (T)c : null;
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Rect { public float width, height; }
    public class RectTransform : Transform
    {
        public enum Axis { Horizontal, Vertical }
        public Vector2 sizeDelta;
        public Rect rect => new() { width = sizeDelta.x, height = sizeDelta.y };
        public void SetSizeWithCurrentAnchors(Axis axis, float size)
        { if (axis == Axis.Horizontal) sizeDelta.x = size; else sizeDelta.y = size; }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float value) => Math.Abs(value);
    }
    public class Component : Object
    {
        public Transform transform = new();
        public T? GetComponent<T>() where T : Component => transform.GetComponent<T>();
    }
    public class Behaviour : Component { public bool enabled = true; }
}
namespace UnityEngine.UI
{
    public class UIWindow : UnityEngine.Component { public bool NativeHidden; }
    public class ContentSizeFitter : UnityEngine.Behaviour { }
    public class LayoutGroup : UnityEngine.Behaviour { }
}
namespace TMPro
{
    public class TMP_Text : UnityEngine.Component
    {
        public TMP_Text() { transform = new UnityEngine.RectTransform(); }
        public UnityEngine.RectTransform rectTransform => (UnityEngine.RectTransform)transform;
        public bool enableWordWrapping, enableAutoSizing;
        public float fontSize = 24f;
        public string text = "";
        public int MeshUpdates;
        public UnityEngine.Vector2 GetPreferredValues(string value, float width, float height)
        {
            float lines = enableWordWrapping ? (float)Math.Ceiling(value.Length * fontSize * .5f / width) : 1f;
            return new UnityEngine.Vector2(width, Math.Max(1f, lines) * fontSize * 1.2f);
        }
        public void ForceMeshUpdate() { MeshUpdates++; }
    }
}
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
    internal class ConvertedPanel { internal bool Released; internal UnityEngine.Transform? Target; internal Action? OnCancel; internal int ReleaseCount; }
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
        internal static void Cancel(ConvertedPanel p, string why) { Events.Trace.Add("cancel"); p.OnCancel?.Invoke(); }
    }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> ActivePanels = new();
        internal static void Release(ConvertedPanel p) { p.Released = true; p.ReleaseCount++; ActivePanels.Remove(p); Events.Trace.Add("release"); }
    }
}

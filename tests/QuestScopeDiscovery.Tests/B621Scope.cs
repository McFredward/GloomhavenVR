// Test-only B621 production snapshot for the measured before/after CPU comparison.
// Native source a06d33a1d24fb29872c3ad44e4f9901d74df3082, SHA-256 7ea280576c7422844900618f78053cfd4aefcabf2bf90d4a0c88f393f688cb71.
// Only the component class name is changed; this file is never selected by the builder.
#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Quest
{
    /// <summary>Only the explicitly excluded original entries; real mod input/tooltips remain owners.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class QuestGameScopeB621 : MonoBehaviour
    {
        sealed class Entry
        {
            internal Component button, owner, tooltip;
            internal GameObject tooltipHost;
            internal string key, tooltipText, previousTooltipText;
            internal bool workshop, attachedTooltip, previousInteractable, interactionChanged, tooltipCreated, previousTooltipEnabled;
            internal object model;
            internal int purchaseMode;
        }
        readonly List<Entry> entries = new List<Entry>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly List<MonoBehaviour> sceneBehaviours = new List<MonoBehaviour>();
        readonly List<MonoBehaviour> behaviourScratch = new List<MonoBehaviour>();
        readonly Dictionary<Type, string> typeNames = new Dictionary<Type, string>();
        QuestSceneObjects sceneObjects;
        float nextScan;

        // Scalars only: the existing startup snapshot can price this actual
        // discovery on hardware without another scene census or log stream.
        public static int DiscoveryScans { get; private set; }
        public static int SceneComponentCount { get; private set; }
        public static double DiscoveryLastMs { get; private set; }
        public static double DiscoveryWorstMs { get; private set; }

        void LateUpdate()
        {
            if (!QuestStandalonePlatform.Enabled) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1;
                bool measure = QuestStandalonePlatform.DebugLogging;
                long started = measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                try { Scan(); }
                finally
                {
                    if (measure)
                    {
                        DiscoveryScans++;
                        SceneComponentCount = sceneBehaviours.Count;
                        DiscoveryLastMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency;
                        DiscoveryWorstMs = Math.Max(DiscoveryWorstMs, DiscoveryLastMs);
                    }
                }
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                if (entry.button == null) { entries.RemoveAt(i); continue; }
                // Native suboption buttons are pooled. Once rebound, leave their
                // new content and native availability untouched; never carry the
                // Workshop exclusion onto Credits, Multiplayer or Campaign.
                try
                {
                    if (entry.workshop && !ReferenceEquals(WorkshopModel(entry.button), entry.model)) { entries.RemoveAt(i); continue; }
                    if (entry.purchaseMode != 0 && !IsPurchase(entry.owner, entry.purchaseMode))
                    { ReleasePurchase(entry); entries.RemoveAt(i); continue; }
                    Disable(entry);
                }
                catch (Exception error) { Report("scope-button:" + entry.button.GetType().FullName, error); }
            }
        }

        void Scan()
        {
            if (sceneObjects == null) sceneObjects = new QuestSceneObjects(this);
            sceneObjects.Collect(sceneBehaviours, behaviourScratch);
            foreach (MonoBehaviour behaviour in sceneBehaviours)
            {
                if (behaviour == null || !behaviour.gameObject.scene.IsValid() || !behaviour.gameObject.scene.isLoaded) continue;
                Type identity = behaviour.GetType();
                string type;
                if (!typeNames.TryGetValue(identity, out type))
                { type = identity.FullName; typeNames.Add(identity, type); }
                try
                {
                    if (type == "GLOOM.MainMenu.UIMainOptionsMenu")
                    {
                        object option = RequiredField(behaviour, "guildmasterButton");
                        if (option != null)
                        {
                            object button = option.GetType().GetProperty("Button", BindingFlags.Public | BindingFlags.Instance)?.GetValue(option);
                            if (!(button is Component)) throw new InvalidOperationException("Original Guildmaster Button ABI is missing.");
                            // The owned MainMenu scene has a separate authored
                            // UITextTooltipTarget on this GameObject, while the
                            // UIMainMenuOption.tooltip field is null. Its public
                            // SetTooltip method therefore silently does nothing.
                            // Bind that original target; keep its hover geometry,
                            // native renderer and Guildmaster callbacks intact.
                            Add((Component)button, "guildmasterUnavailable", false, null, attachedTooltip: true);
                        }
                    }
                    else if (type == "VoiceChat.VoceChatOptions")
                    {
                        object button = RequiredField(behaviour, "_switchChatButton");
                        if (button is Component) Add((Component)button, "startupVoiceUnavailable", false, null);
                    }
                    else if (type == "UIBuyDLCSlot" || type == "UIPromotionDLCSlot")
                    {
                        // Keep the original ad image, title, animation and native
                        // promotion cycle. Only its exact serialized purchase
                        // button is disabled; no label/name heuristics are used.
                        object button = RequiredField(behaviour, "button");
                        if (button is Component) Add((Component)button, "dlcPurchaseOnPc", false, null);
                    }
                    else if (type == "GLOOM.MainMenu.UILoadGameSlot" && IsPurchase(behaviour, 1))
                    {
                        Add((Component)RequiredField(behaviour, "loadButton"), "dlcPurchaseOnPc", false, null, behaviour, 1);
                    }
                    else if (type == "UIDLCSelectorOption" && IsPurchase(behaviour, 2))
                    {
                        // The native mouse toggle is hidden in purchase mode.
                        // Retain the promotion panel and use its active surface
                        // for the original hover renderer; owned DLC toggles stay native.
                        Add((Component)RequiredField(behaviour, "_gamepadToggle"), "dlcPurchaseOnPc", false, null,
                            behaviour, 2, (GameObject)RequiredField(behaviour, "_dlcPurchaseablePanel"));
                    }
                    else if (type == "GLOOM.MainMenu.UIMainMenuSuboption")
                    {
                        object model = WorkshopModel(behaviour);
                        if (model != null) Add(behaviour, "excluded", true, model);
                    }
                }
                catch (Exception error) { Report("scope-scan:" + type, error); }
            }
        }

        void Add(Component button, string key, bool workshop, object model, Component owner = null, int purchaseMode = 0, GameObject tooltipHost = null, bool attachedTooltip = false)
        {
            foreach (Entry existing in entries) if (existing.button == button && ReferenceEquals(existing.model, model)) return;
            var entry = new Entry { button = button, key = key, workshop = workshop, model = model, owner = owner, attachedTooltip = attachedTooltip,
                purchaseMode = purchaseMode, tooltipHost = tooltipHost, previousInteractable = button is Selectable && ((Selectable)button).interactable };
            entries.Add(entry);
            if (reported.Add("bound:" + button.GetType().FullName + ":" + (workshop ? "Workshop" : key)))
                UnityEngine.Debug.Log("[Quest startup] original excluded entry retained with native hover tooltip=" + (workshop ? "Workshop" : key));
        }

        static void Disable(Entry entry)
        {
            bool changed = false;
            if (entry.button is Selectable selectable)
            {
                changed = selectable.interactable;
                if (changed) entry.interactionChanged = true;
                selectable.interactable = false;
            }
            else
            {
                PropertyInfo interaction = entry.button.GetType().GetProperty("IsInteractable", BindingFlags.Public | BindingFlags.Instance);
                if (interaction == null || interaction.PropertyType != typeof(bool) || !interaction.CanWrite)
                    throw new InvalidOperationException("Original excluded entry has no supported native interaction property.");
                if ((bool)interaction.GetValue(entry.button)) { interaction.SetValue(entry.button, false); changed = true; }
            }
            string text = QuestText.Get(entry.key, Application.systemLanguage == SystemLanguage.German);
            if (!changed && entry.tooltipText == text) return;
            MethodInfo tooltip = entry.button.GetType().GetMethod("SetTooltip", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool), typeof(string) }, null);
            if (tooltip != null && !entry.attachedTooltip) tooltip.Invoke(entry.button, new object[] { true, text });
            else if (entry.button is Selectable || entry.attachedTooltip)
            {
                // Voice's original ExtendedButton has no menu SetTooltip API.
                // Reuse the game's tooltip event handlers and rendering instead
                // of introducing another pointer or diagnostic tooltip canvas.
                // GH.Runtime is explicitly referenced by the recovered player,
                // rather than a compile-time dependency of this Unity script.
                // Resolve only its exact original component, with checked ABI.
                Type nativeType = Type.GetType("UITextTooltipTarget, GH.Runtime", true);
                if (!typeof(MonoBehaviour).IsAssignableFrom(nativeType)) throw new InvalidOperationException("Original tooltip component ABI is missing.");
                PropertyInfo enabled = nativeType.GetProperty("TooltipEnabled", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo setText = nativeType.GetMethod("SetText", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(bool), typeof(string) }, null);
                if (enabled == null || enabled.PropertyType != typeof(bool) || !enabled.CanWrite || setText == null || setText.ReturnType != typeof(void))
                    throw new InvalidOperationException("Original tooltip text/enabled ABI is missing.");
                if (entry.tooltip == null)
                {
                    GameObject host = entry.tooltipHost != null ? entry.tooltipHost : entry.button.gameObject;
                    entry.tooltip = host.GetComponent(nativeType);
                    if (entry.attachedTooltip && entry.tooltip == null)
                        throw new InvalidOperationException("Original Guildmaster attached tooltip binding is missing.");
                    if (entry.tooltip != null)
                    {
                        entry.previousTooltipEnabled = (bool)enabled.GetValue(entry.tooltip);
                        entry.previousTooltipText = nativeType.GetProperty("ShownTooltipText", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry.tooltip) as string;
                    }
                    else { entry.tooltip = host.AddComponent(nativeType); entry.tooltipCreated = true; }
                }
                if (entry.tooltip == null) throw new InvalidOperationException("Unity refused the original tooltip component.");
                if (entry.attachedTooltip) ((MonoBehaviour)entry.tooltip).enabled = true;
                enabled.SetValue(entry.tooltip, true);
                setText.Invoke(entry.tooltip, new object[] { text, true, null });
            }
            else throw new InvalidOperationException("Original excluded entry has no native tooltip method.");
            entry.tooltipText = text;
        }

        static bool IsPurchase(Component owner, int mode)
        {
            if (owner == null) return false;
            if (mode == 1)
            {
                // Exact original mode identity assigned by UILoadGameSlot.SetData;
                // this is never inferred from rendered/localized text.
                return RequiredField(RequiredField(owner, "loadButton"), "textLanguageKey") as string == "Consoles/LEARN_MORE";
            }
            return ((GameObject)RequiredField(owner, "_dlcPurchaseablePanel")).activeSelf;
        }

        static void ReleasePurchase(Entry entry)
        {
            // Missing-DLC save rows and selector options are pooled. Undo only
            // this purchase presentation when native binding returns to loading
            // or selecting an owned DLC; callbacks were never replaced.
            if (entry.interactionChanged && entry.button is Selectable selectable && !selectable.interactable)
                selectable.interactable = entry.previousInteractable;
            if (entry.tooltip == null) return;
            if (entry.tooltipCreated) { UnityEngine.Object.Destroy(entry.tooltip); return; }
            Type type = entry.tooltip.GetType();
            if (type.GetProperty("ShownTooltipText", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry.tooltip) as string != entry.tooltipText) return;
            type.GetProperty("TooltipEnabled", BindingFlags.Public | BindingFlags.Instance).SetValue(entry.tooltip, entry.previousTooltipEnabled);
            type.GetMethod("SetText", new[] { typeof(string), typeof(bool), typeof(string) }).Invoke(entry.tooltip,
                new object[] { entry.previousTooltipText, true, null });
        }

        static object WorkshopModel(Component row)
        {
            // The original panel's Show creates an onSelected closure containing
            // the exact MenuSuboption. Read its stable NameLocKey identity; never
            // infer Workshop from a translated label, object name or pool index.
            Type optionType = row.GetType();
            while (optionType != null && optionType.FullName != "UIMenuOption") optionType = optionType.BaseType;
            if (optionType == null) return null;
            FieldInfo selection = optionType.GetField("onSelected", BindingFlags.Instance | BindingFlags.NonPublic);
            if (selection == null) throw new InvalidOperationException("Original suboption callback ABI is missing.");
            var callback = selection.GetValue(row) as Delegate;
            if (callback == null) return null;
            foreach (Delegate invocation in callback.GetInvocationList())
            {
                object target = invocation.Target;
                if (target == null) continue;
                foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (field.FieldType.FullName != "GLOOM.MainMenu.MenuSuboption") continue;
                    object model = field.GetValue(target);
                    if (model != null && model.GetType().GetProperty("NameLocKey", BindingFlags.Public | BindingFlags.Instance)?.GetValue(model) as string == "GUI_MODDING")
                        return model;
                }
            }
            return null;
        }

        static object RequiredField(object owner, string name)
        {
            FieldInfo field = null;
            for (Type type = owner.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field == null) throw new InvalidOperationException("Original scope field ABI is missing: " + name);
            return field.GetValue(owner);
        }
        void Report(string key, Exception error)
        {
            if (reported.Add(key)) UnityEngine.Debug.LogError("[Quest startup] excluded native entry binding failed " + key + ": " + error);
        }
    }
}
#endif

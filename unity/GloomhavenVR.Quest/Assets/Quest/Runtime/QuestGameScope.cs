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
    public sealed class QuestGameScope : MonoBehaviour
    {
        sealed class Entry { internal Component button; internal string key, tooltipText; internal bool workshop; internal object model; internal UITextTooltipTarget tooltip; }
        readonly List<Entry> entries = new List<Entry>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        float nextScan;

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1; Scan(); }
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
                    Disable(entry);
                }
                catch (Exception error) { Report("scope-button:" + entry.button.GetType().FullName, error); }
            }
        }

        void Scan()
        {
            foreach (MonoBehaviour behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour == null || !behaviour.gameObject.scene.IsValid() || !behaviour.gameObject.scene.isLoaded) continue;
                string type = behaviour.GetType().FullName;
                try
                {
                    if (type == "GLOOM.MainMenu.UIMainOptionsMenu")
                    {
                        object option = RequiredField(behaviour, "guildmasterButton");
                        if (option != null)
                        {
                            object button = option.GetType().GetProperty("Button", BindingFlags.Public | BindingFlags.Instance)?.GetValue(option);
                            if (!(button is Component)) throw new InvalidOperationException("Original Guildmaster Button ABI is missing.");
                            Add((Component)button, "excluded", false, null);
                        }
                    }
                    else if (type == "VoiceChat.VoceChatOptions")
                    {
                        object button = RequiredField(behaviour, "_switchChatButton");
                        if (button is Component) Add((Component)button, "startupVoiceUnavailable", false, null);
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

        void Add(Component button, string key, bool workshop, object model)
        {
            foreach (Entry existing in entries) if (existing.button == button && ReferenceEquals(existing.model, model)) return;
            var entry = new Entry { button = button, key = key, workshop = workshop, model = model };
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
            if (tooltip != null) tooltip.Invoke(entry.button, new object[] { true, text });
            else if (entry.button is Selectable)
            {
                // Voice's original ExtendedButton has no menu SetTooltip API.
                // Reuse the game's tooltip event handlers and rendering instead
                // of introducing another pointer or diagnostic tooltip canvas.
                if (entry.tooltip == null) entry.tooltip = entry.button.GetComponent<UITextTooltipTarget>() ?? entry.button.gameObject.AddComponent<UITextTooltipTarget>();
                entry.tooltip.TooltipEnabled = true;
                entry.tooltip.SetText(text, true);
            }
            else throw new InvalidOperationException("Original excluded entry has no native tooltip method.");
            entry.tooltipText = text;
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
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
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

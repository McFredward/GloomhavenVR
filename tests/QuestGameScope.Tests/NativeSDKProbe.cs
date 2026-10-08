using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using UnityEngine;
using UnityEngine.UI;

// Execute reflection contracts against the owned native game assembly without
// constructing a Unity object or invoking a native engine/gameplay callback.
static class NativeSDKProbe
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("FAIL native scope ABI " + message); }
    static FieldInfo? Field(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }
    public static int Main(string[] args)
    {
        try
        {
            string game = Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string path = Path.Combine(game, name.Name + ".dll");
                return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
            };
            var original = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "GH.Runtime.dll"));
            var names = new[] { "GLOOM.MainMenu.UIMainOptionsMenu", "VoiceChat.VoceChatOptions", "UIBuyDLCSlot", "UIPromotionDLCSlot", "GLOOM.MainMenu.UILoadGameSlot", "UIDLCSelectorOption", "GLOOM.MainMenu.UIMainMenuSuboption" };
            foreach (string name in names)
            {
                var owner = Type.GetType(name + ", GH.Runtime", true)!;
                Check(owner.Assembly == original && typeof(MonoBehaviour).IsAssignableFrom(owner) && !owner.IsAbstract, "exact scope owner " + name);
            }
            var find = typeof(Resources).GetMethod("FindObjectsOfTypeAll", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
            Check(find != null && find.ReturnType == typeof(UnityEngine.Object[]), "native exact-type discovery API");
            var discovery = new GloomhavenVR.Quest.QuestScopeObjects();
            var targets = (Type[])typeof(GloomhavenVR.Quest.QuestScopeObjects).GetField("targets", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(discovery)!;
            Check(targets.Select(t => t.FullName).SequenceEqual(names), "production exact original type bindings validated once without an assembly census");
            var tooltip = Type.GetType("UITextTooltipTarget, GH.Runtime", true)!;
            Check(typeof(MonoBehaviour).IsAssignableFrom(tooltip), "tooltip MonoBehaviour");
            var enabled = tooltip.GetProperty("TooltipEnabled", BindingFlags.Public | BindingFlags.Instance);
            Check(enabled != null && enabled.PropertyType == typeof(bool) && enabled.CanRead && enabled.CanWrite, "inherited TooltipEnabled bool");
            var setText = tooltip.GetMethod("SetText", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string), typeof(bool), typeof(string) }, null);
            Check(setText != null && setText.ReturnType == typeof(void), "SetText string/bool/string");
            Check(tooltip.GetProperty("ShownTooltipText", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string), "ShownTooltipText ownership");
            Check(tooltip.GetProperty("CanBeShown", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(bool), "CanBeShown bool");
            var main = original.GetType("GLOOM.MainMenu.UIMainOptionsMenu", true)!;
            var guildmaster = Field(main, "guildmasterButton");
            Check(guildmaster != null && guildmaster.IsPrivate && guildmaster.FieldType.FullName == "GLOOM.MainMenu.MainOptionOpenSuboptions", "exact original Guildmaster option binding");
            var guildmasterButton = guildmaster!.FieldType.GetProperty("Button", BindingFlags.Public | BindingFlags.Instance);
            Check(guildmasterButton != null && guildmasterButton.CanRead && guildmasterButton.PropertyType.FullName == "UIMainMenuOption", "original Guildmaster button property");
            Check(Field(guildmasterButton!.PropertyType, "tooltip")?.FieldType == tooltip, "native menu tooltip pointer can be distinct from attached target");
            var buy = original.GetType("UIBuyDLCSlot", true)!;
            var promotion = original.GetType("UIPromotionDLCSlot", true)!;
            Check(buy.BaseType == promotion, "buy promotion inheritance");
            var button = Field(buy, "button");
            Check(button != null && button.IsPrivate && button.DeclaringType == promotion && typeof(Selectable).IsAssignableFrom(button.FieldType), "private inherited native purchase button");
            Check(Field(promotion, "promotionImage") != null && Field(promotion, "title") != null, "native promo image/title bindings");
            var load = original.GetType("GLOOM.MainMenu.UILoadGameSlot", true)!;
            var loadButton = Field(load, "loadButton");
            Check(loadButton != null && typeof(Selectable).IsAssignableFrom(loadButton.FieldType), "native load button");
            Check(Field(loadButton!.FieldType, "textLanguageKey")?.FieldType == typeof(string), "exact native load identity field");
            Check(loadButton.FieldType.GetProperty("TextLanguageKey")?.CanRead == false, "write-only localized property is not read");
            var selector = original.GetType("UIDLCSelectorOption", true)!;
            Check(Field(selector, "_gamepadToggle")?.FieldType == typeof(Toggle), "native selector gamepad toggle");
            Check(Field(selector, "_dlcPurchaseablePanel")?.FieldType == typeof(GameObject), "native selector active purchase panel");
            var create = original.GetType("GLOOM.MainMenu.UICreateGameDLCStep", true)!;
            Check(typeof(MonoBehaviour).IsAssignableFrom(create), "native campaign DLC step retained");
#if GHVR_QUEST_GAME
            foreach (string name in new[] { "ClearHotkeySessions", "DisposeButtons" })
            {
                var nativeMethod = typeof(ErrorMessage).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                Check(nativeMethod != null && nativeMethod.ReturnType == typeof(void), "native notice retirement " + name);
            }
            var cancel = typeof(SaveData).GetMethod("OnCancelCreateLocalSave", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(bool), typeof(Action) }, null);
            Check(cancel != null && cancel.ReturnType == typeof(void), "original cancellation bool/Action ABI");
            var helper = typeof(GloomhavenVR.Quest.QuestGameScope).GetMethod("NotifyGuildmasterUnavailable", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(bool), typeof(Action) }, null);
            Check(helper == null, "full game does not expose the retired Guildmaster rejection notice");
#endif
            Console.WriteLine("PASS Quest native scope SDK reflection: " + assertions + " original ABI assertions; no Unity callbacks invoked");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}

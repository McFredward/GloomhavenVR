using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Quest;
using UnityEngine;

// Native message/cancellation call seams only. This does not render a Unity
// window or certify headset admission; the Cecil fixtures check native guards.
public enum KeyAction { UI_CANCEL }
public sealed class ErrorMessage
{
    public delegate void ErrorDelegate();
    public sealed class LabelAction
    {
        public readonly string Key;
        public readonly ErrorDelegate Action;
        public readonly KeyAction Hotkey;
        public LabelAction(string key, ErrorDelegate action, KeyAction hotkey)
        { Key = key; Action = action; Hotkey = hotkey; }
    }
    public bool ShowingMessage;
    public int ClearedSessions, DisposedButtons, Hidden, Shows;
    public string? Title, Message;
    public List<LabelAction> Buttons = new();
    void ClearHotkeySessions() { ClearedSessions++; }
    void DisposeButtons() { DisposedButtons++; Buttons.Clear(); ShowingMessage = false; }
    public void Hide() { Hidden++; ShowingMessage = false; }
    public void ShowGenericDebugMessage(string title, string text, List<LabelAction> buttons)
    {
        if (ShowingMessage) return; // Original native early-out, essential here.
        Shows++; Title = title; Message = text; Buttons = buttons; ShowingMessage = true;
    }
}
public sealed class SceneController
{
    public static readonly SceneController Instance = new();
    public ErrorMessage GlobalErrorMessage = new();
}
public sealed class SaveData
{
    public static readonly SaveData Instance = new();
    public int Cancellations, MenuReturns;
    public readonly byte[] ImportedSave = { 1, 2, 3, 4 };
    void OnCancelCreateLocalSave(bool loadMenuOnCancel, Action? callback)
    { Cancellations++; callback?.Invoke(); if (loadMenuOnCancel) MenuReturns++; else SceneController.Instance.GlobalErrorMessage.Hide(); }
}
static class GuildmasterNotice
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("FAIL " + message); }
    static void Main()
    {
        foreach (bool german in new[] { false, true })
        foreach (bool visible in new[] { false, true })
        foreach (bool returnMenu in new[] { false, true })
        foreach (bool hasCallback in new[] { false, true })
        {
            Application.systemLanguage = german ? SystemLanguage.German : SystemLanguage.English;
            var notice = new ErrorMessage { ShowingMessage = visible };
            if (visible) notice.Buttons.Add(new ErrorMessage.LabelAction("STALE_NETWORK_ERROR", () => throw new Exception("stale callback fired"), KeyAction.UI_CANCEL));
            SceneController.Instance.GlobalErrorMessage = notice;
            SaveData.Instance.Cancellations = SaveData.Instance.MenuReturns = 0;
            int callbacks = 0;
            Action? callback = hasCallback ? () => callbacks++ : null;
            QuestGameScope.NotifyGuildmasterUnavailable(returnMenu, callback!);
            Check(notice.Shows == 1 && notice.ShowingMessage, "unavailable notice replaces visible native failure");
            Check(notice.ClearedSessions == (visible ? 1 : 0) && notice.DisposedButtons == (visible ? 1 : 0), "native old buttons and hotkeys retired exactly once");
            Check(notice.Title == "Guildmaster" && notice.Message == QuestText.Get("guildmasterUnavailable", german), "localized scope explanation");
            Check(notice.Buttons.Count == 1 && notice.Buttons[0].Key == "GUI_CANCEL" && notice.Buttons[0].Hotkey == KeyAction.UI_CANCEL, "original cancel hotkey and label");
            Check(callbacks == 0 && SaveData.Instance.Cancellations == 0, "caller interaction restored on acknowledgement");
            var action = notice.Buttons[0].Action;
            action(); action();
            Check(callbacks == (hasCallback ? 1 : 0), "caller cancellation exactly once");
            Check(SaveData.Instance.Cancellations == (returnMenu || hasCallback ? 1 : 0) && SaveData.Instance.MenuReturns == (returnMenu ? 1 : 0), "original cancellation preserves menu flag");
            Check(returnMenu || !notice.ShowingMessage, "notice closes when staying in native menu");
            Check(Convert.ToHexString(SaveData.Instance.ImportedSave) == "01020304", "imported save bytes unchanged");
        }
        QuestStandalonePlatform.Enabled = false;
        SceneController.Instance.GlobalErrorMessage = new ErrorMessage();
        QuestGameScope.NotifyGuildmasterUnavailable(true, () => throw new Exception("desktop callback"));
        Check(SceneController.Instance.GlobalErrorMessage.Shows == 0, "disabled Quest platform untouched");
        Console.WriteLine("PASS Quest Guildmaster notice: " + assertions + " managed call assertions; no headset rendering claim");
    }
}

using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using Script.GUI.Controller;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

static class Program
{
    static int assertions;
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception("FAIL " + message); }
    sealed class Window
    {
        public readonly GameObject Host = new("native window");
        public readonly TMP_InputField Field = new() { name = "native party name", gameObject = new("field") };
        public readonly UIKeyboard Keyboard = new() { name = "native keyboard", gameObject = new("keyboard") };
        public readonly ControllerInputKeyboard Controller = new();
        public readonly GameObject Key = new("key");
        public Window(bool hidden = false)
        {
            Field.transform.parent = Keyboard.transform.parent = Host.transform;
            Key.transform.parent = Keyboard.transform;
            Field.shouldHideSoftKeyboard = hidden; Field.HideWrites = 0;
            Keyboard.gameObject.SetActive(false);
            Controller.gameObject = Keyboard.gameObject;
            Controller.keyboard = Keyboard; Controller.m_KeyboardInputField = Field;
            Keyboard.OnSelectedKeyCode.AddListener(Controller.ProcessKeyCode);
        }
        public void Open() { VRKeyboard.NoticeClick(Field.gameObject); Field.ActivateInputField(); }
        public void KeyPress(KeyCode code)
        {
            VRKeyboard.NoticeClick(Key);
            Field.DeactivateInputField();
            EventSystem.current!.currentSelectedGameObject = Key;
            Keyboard.OnSelectedKeyCode.Invoke(code);
            VRKeyboard.Tick();
        }
    }
    static void Reset(bool quest)
    {
        VRKeyboard.Shutdown(); UnityEngine.Object.All.Clear();
        QuestStandalonePlatform.Enabled = quest;
        Application.platform = quest ? RuntimePlatform.Android : RuntimePlatform.WindowsPlayer;
        TouchScreenKeyboard.isSupported = quest;
        VRSession.IsRunning = true; WorldUIConfig.KeyboardAutoCase.Value = true;
        EventSystem.current = new EventSystem(); Time.unscaledTime = 0;
    }
    static void Desktop()
    {
        Reset(false); var window = new Window();
        window.Open();
        Check(window.Field.HideWrites == 0, "desktop flags unchanged on attach");
        window.KeyPress(KeyCode.A); window.KeyPress(KeyCode.B);
        Check(window.Field.text == "Ab", "desktop existing writer and sentence case retained");
        VRKeyboard.NoticeClick(null);
        Check(window.Field.HideWrites == 0, "desktop flags unchanged on detach");
        Check(window.Keyboard.OnSelectedKeyCode.ListenerCount == 1, "desktop native handler restored");
    }
    static void AndroidAdmission()
    {
        Reset(true);
        var plain = new TMP_InputField(); plain.ProcessEvent(new Event { character = 'A' });
        Check(plain.text == "" && plain.Validations == 0, "authored Android gate rejects before validation");
        var window = new Window();
        int changes = 0; window.Field.onValueChanged.AddListener(_ => changes++);
        window.Open();
        Check(window.Field.shouldHideSoftKeyboard, "Quest software keyboard hidden before original activation");
        Check(window.Field.OverlayRequests == 0, "Quest activation never requests a second overlay keyboard");
        Check(window.Field.ActivationRequests == 1, "original field activation retained");
        Check(VRKeyboard.IsShowing && window.Keyboard.IsActive, "original keyboard remains visible");
        Check(window.Keyboard.OnSelectedKeyCode.ListenerCount == 1, "one typing listener while owned");
        window.KeyPress(KeyCode.A); window.KeyPress(KeyCode.B); window.KeyPress(KeyCode.Space); window.KeyPress(KeyCode.C);
        Check(window.Field.text == "Ab C", "Quest keys use existing ProcessEvent writer");
        Check(window.Field.ProcessEvents == 4 && changes == 4, "one native event and change per character");
        Check(window.Controller.KeysProcessed == 0, "native handler parked while mod types");
        Check(!window.Field.isFocused && VRKeyboard.IsShowing, "key focus cannot dismiss the keyboard");
        VRKeyboard.Tick(); Check(window.Field.ActivationRequests == 1, "typing never refocuses and selects all");
        window.KeyPress(KeyCode.Delete); Check(window.Field.text == "Ab ", "existing delete writer retained");
        VRKeyboard.NoticeClick(null);
        Check(!window.Field.shouldHideSoftKeyboard, "authored Quest software flag restored on detach");
        Check(!VRKeyboard.IsShowing && !window.Keyboard.IsActive, "click away closes the original keyboard");
        Check(window.Keyboard.OnSelectedKeyCode.ListenerCount == 1, "native listener restored exactly once");
        int writes = window.Field.HideWrites;
        VRKeyboard.Shutdown(); Check(window.Field.HideWrites == writes, "shutdown cannot restore a released lease twice");
    }
    static void NativeValidation()
    {
        Reset(true); var window = new Window();
        window.Field.characterLimit = 2;
        window.Field.Validator = c => c == 'X' || c == 'x' ? '\0' : char.ToUpperInvariant(c);
        int changes = 0; window.Field.onValueChanged.AddListener(_ => changes++);
        window.Open();
        window.KeyPress(KeyCode.A); window.KeyPress(KeyCode.X); window.KeyPress(KeyCode.B); window.KeyPress(KeyCode.C);
        Check(window.Field.text == "AB", "native validation and character limit retained");
        Check(window.Field.Validations == 4 && changes == 2, "rejected native characters do not synthesize change callbacks");
        int edits = 0; string? submitted = null;
        window.Field.onEndEdit.AddListener(value => { edits++; submitted = value; });
        window.KeyPress(KeyCode.Return);
        Check(edits == 1 && submitted == "AB", "existing Enter commit callback sees native text once");
        Check(!VRKeyboard.IsShowing && !window.Field.shouldHideSoftKeyboard, "Enter releases Quest field ownership");
        window.Open(); window.KeyPress(KeyCode.Escape);
        Check(edits == 1 && !VRKeyboard.IsShowing, "Escape retains existing no-submit policy");
    }
    static void LeaseOwnership()
    {
        Reset(true); var window = new Window(true);
        window.Open(); VRKeyboard.NoticeClick(null);
        Check(window.Field.shouldHideSoftKeyboard, "already hidden native field remains hidden");
        Check(window.Field.HideWrites == 0, "already hidden native field is never written");
        Reset(true); window = new Window(); window.Open();
        window.Field.shouldHideSoftKeyboard = false;
        int writes = window.Field.HideWrites;
        VRKeyboard.NoticeClick(null);
        Check(window.Field.HideWrites == writes, "native software flag change is not overwritten on detach");
        Reset(true); window = new Window(); window.Open();
        var second = new Window(); second.Open();
        Check(!window.Field.shouldHideSoftKeyboard && second.Field.shouldHideSoftKeyboard, "switching fields releases only the previous Quest field");
        Check(window.Keyboard.OnSelectedKeyCode.ListenerCount == 1, "field switch restores previous native handler");
        second.Host.SetActive(false); VRKeyboard.Tick();
        Check(!second.Field.shouldHideSoftKeyboard && !VRKeyboard.IsShowing, "closing the field window releases Quest ownership");
        Reset(true); window = new Window(); window.Open(); window.Keyboard.gameObject.SetActive(false); VRKeyboard.Tick();
        Check(!window.Field.shouldHideSoftKeyboard && !VRKeyboard.IsShowing, "native keyboard close releases Quest ownership");
        Reset(true); window = new Window(); window.Open(); VRKeyboard.Shutdown();
        Check(!window.Field.shouldHideSoftKeyboard && !VRKeyboard.IsShowing, "hot reload releases Quest ownership");
        Reset(true); window = new Window(); window.Field.readOnly = true; window.Open();
        Check(window.Field.HideWrites == 0 && !VRKeyboard.IsShowing, "read only click cannot lease or open keyboard");
    }
    public static int Main()
    {
        try
        {
            Desktop(); AndroidAdmission(); NativeValidation(); LeaseOwnership();
            Console.WriteLine("PASS Quest keyboard input: " + assertions + " production ownership, writer, validation and lifecycle assertions");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error); return 1; }
        finally { VRKeyboard.Shutdown(); }
    }
}

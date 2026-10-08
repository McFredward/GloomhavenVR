#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Reflection;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Exposes the original invite keyboard to the diagnostic pointer, retaining its key pipeline.</summary>
    public sealed class QuestGameKeyboard : MonoBehaviour
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        Component window, field, keyboard;
        MonoBehaviour controller;
        MethodInfo show, hide, updateLabel;
        PropertyInfo text, caret, interactable;
        bool previousControllerEnabled, lastVisible;
        string lastFailure;
        float nextCheck;
        public bool Bound { get { return field != null && keyboard != null && controller != null; } }
        public bool Visible { get { return keyboard != null && keyboard.gameObject.activeInHierarchy; } }
        public string Failure { get; private set; }

        internal void BeforeOriginalClick(GameObject target)
        {
            if (!Bound || !Visible || target == null || !target.transform.IsChildOf(keyboard.transform)) return;
            foreach (MonoBehaviour row in target.GetComponentsInParent<MonoBehaviour>())
                if (row != null && row.GetType().FullName == "Script.GUI.Controller.Keyboard.UIKeyboardKey")
                {
                    // The clicked original button changes EventSystem focus. Set only
                    // the native field's caret before its original ProcessEvent listener
                    // runs; characters, validation and onValueChanged remain original.
                    try { CaretAtEnd(); } catch (Exception e) { Block(e); }
                    return;
                }
        }
        internal void AfterOriginalClick(GameObject target)
        {
            if (target == null) return;
            foreach (MonoBehaviour candidate in target.GetComponentsInParent<MonoBehaviour>())
            {
                if (candidate == null || candidate.GetType().FullName != "GLOOM.MainMenu.UIMultiplayerJoinSessionWindow") continue;
                Component invite = ReadComponent(candidate, "inviteCodeInput");
                if (invite == null || !target.transform.IsChildOf(invite.transform)) continue;
                try { Open(candidate, invite); } catch (Exception e) { Block(e); }
                return;
            }
        }
        void Open(Component owner, Component invite)
        {
            if (invite.GetType().FullName != "TMPro.TMP_InputField") throw new InvalidOperationException("Original invite input ABI changed.");
            Component originalKeyboard = ReadComponent(owner, "controllerKeyboard");
            if (originalKeyboard == null || originalKeyboard.GetType().FullName != "UIKeyboard") throw new InvalidOperationException("Original invite UIKeyboard is absent.");
            MonoBehaviour originalController = null;
            foreach (MonoBehaviour candidate in originalKeyboard.GetComponents<MonoBehaviour>())
                if (candidate != null && candidate.GetType().FullName == "Script.GUI.Controller.ControllerInputKeyboard"
                    && ReadComponent(candidate, "m_KeyboardInputField") == invite && ReadComponent(candidate, "keyboard") == originalKeyboard)
                {
                    if (originalController != null) throw new InvalidOperationException("Original invite keyboard has ambiguous character processors.");
                    originalController = candidate;
                }
            if (originalController == null) throw new InvalidOperationException("Original invite keyboard character processor is absent.");
            if (interactable == null || field != invite) interactable = invite.GetType().GetProperty("interactable");
            if (interactable == null || !(bool)interactable.GetValue(invite)) return;
            if (field != invite || keyboard != originalKeyboard || controller != originalController)
            {
                Close();
                window = owner; field = invite; keyboard = originalKeyboard; controller = originalController;
                previousControllerEnabled = controller.enabled;
                show = keyboard.GetType().GetMethod("Show", Type.EmptyTypes); hide = keyboard.GetType().GetMethod("Hide", Type.EmptyTypes);
                text = field.GetType().GetProperty("text"); caret = field.GetType().GetProperty("caretPosition");
                updateLabel = field.GetType().GetMethod("ForceLabelUpdate", Type.EmptyTypes);
                interactable = field.GetType().GetProperty("interactable");
                if (show == null || hide == null || text == null || text.PropertyType != typeof(string) || caret == null || !caret.CanWrite || caret.PropertyType != typeof(int) || updateLabel == null)
                    throw new InvalidOperationException("Original invite keyboard/TMP metadata ABI changed.");
            }
            // These two original components share the inactive keyboard GameObject.
            // In desktop mode ControllerInputKeyboard.OnEnable would immediately Hide
            // that GameObject. Disable only this component before Show: Unity still
            // runs its Awake, which registers the original ProcessKeyCode event listener.
            controller.enabled = false;
            field.GetType().GetMethod("DeactivateInputField", Type.EmptyTypes)?.Invoke(field, null);
            show.Invoke(keyboard, null); CaretAtEnd(); Failure = null;
            if (!Visible) throw new InvalidOperationException("Original invite keyboard remained hidden.");
            ReportVisibility();
        }
        static Component ReadComponent(Component owner, string name)
        { return owner.GetType().GetField(name, Fields)?.GetValue(owner) as Component; }
        void CaretAtEnd() { caret.SetValue(field, ((string)text.GetValue(field) ?? "").Length); updateLabel.Invoke(field, null); }
        void Update()
        {
            if (Time.unscaledTime < nextCheck) return; nextCheck = Time.unscaledTime + .2f;
            if (!Bound) return;
            if (window == null || !window.gameObject.activeInHierarchy || !field.gameObject.activeInHierarchy || !(bool)interactable.GetValue(field)) { Close(); return; }
            ReportVisibility();
        }
        void ReportVisibility()
        {
            bool visible = Visible; if (visible == lastVisible) return; lastVisible = visible;
            Debug.Log("[Quest startup] original invite keyboard visible=" + visible + "; native KeyCode->TMP ProcessEvent/validation; no direct network invocation.");
        }
        void Block(Exception exception)
        {
            RecordFailure(exception); Close();
        }
        void RecordFailure(Exception exception)
        {
            Exception reason = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
            Failure = reason.GetType().Name + ": " + reason.Message;
            if (Failure.Length > 400) Failure = Failure.Substring(0, 400);
            if (Failure != lastFailure) { lastFailure = Failure; Debug.LogWarning("[Quest startup] original invite keyboard blocked: " + Failure); }
        }
        void Close()
        {
            Component oldKeyboard = keyboard; MonoBehaviour oldController = controller; MethodInfo oldHide = hide; bool oldEnabled = previousControllerEnabled;
            window = field = keyboard = null; controller = null; show = hide = updateLabel = null; text = caret = interactable = null;
            try { if (oldKeyboard != null && oldHide != null && oldKeyboard.gameObject.activeSelf) oldHide.Invoke(oldKeyboard, null); }
            catch (Exception e) { RecordFailure(e); }
            try { if (oldController != null) oldController.enabled = oldEnabled; }
            catch (Exception e) { RecordFailure(e); }
            if (lastVisible) { lastVisible = false; Debug.Log("[Quest startup] original invite keyboard closed."); }
        }
        void OnDestroy() { Close(); }
    }
}
#endif

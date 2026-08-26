using System;
using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>
    /// DECISION DOCK registry (test #22, generalizes the test-#21 take-damage claim):
    /// the ID/predicate → dock map of in-scenario decision/confirmation prompts whose
    /// REAL interactive widgets <see cref="Surfaces.DecisionDockSurface"/> docks in the
    /// reserved zone BELOW the two cards on the control board. Adding a prompt is a
    /// single <see cref="Prompt"/> entry in <see cref="Prompts"/>: a name, its
    /// <c>UIWindow</c>, its open predicate, and how to isolate its actionable widget
    /// row. While a prompt is claimed the generic modal path (float + ModalUI) stands
    /// down COMPLETELY for its window — these prompts have follow-ups that need the
    /// normal interactors (the burn choice continues in the card fan). The claim is
    /// consulted level-triggered every tick against the WINDOW INSTANCE (not the ID),
    /// so it covers BOTH the ID-tracked TakeDamagePanel AND the ID-less poll-tracked
    /// dialogPopup, and the moment a claim breaks (surface off, tray gone, grace
    /// expired) the window is handled generically again — a wrongly-floated window is
    /// recoverable, a dropped one is a silent deadlock (the DurabilityPanel rule).
    ///
    /// See the report for the SYSTEMATIC dock-vs-float verdict on every scenario
    /// decision window/popup. Now wired (test #24 item 5): <c>YesNoDialog</c> — the
    /// short-rest confirmation ("Bist du sicher?"; serialized yesButton/noButton) that
    /// the game shows next to the 2D short-rest button (a HUD dialogHolder, mislocated
    /// and unpressable in VR → a deadlock); its Yes/No row now docks at the same board
    /// spot as every other confirm. Deliberately NOT dockable: <c>UIEventPanel</c> (a scroll-list of
    /// variable event options + rewards — no discrete widget row to isolate; stays
    /// floating), and every passive info popup (no choice row at all).
    /// </summary>
    internal static class DecisionDock
    {
        /// <summary>
        /// One dockable prompt. Two predicates that DIFFER on purpose (the take-damage
        /// burn flow, test #22): <see cref="IsOpen"/> — the window is OPEN — gates the
        /// CLAIM (generic float + ModalUI stand down, keeping the card fan live for the
        /// follow-up), and STAYS TRUE while the game alpha-hides the panel during the
        /// LoseCard pick + burn-confirm (TakeDamagePanel.ToggleVisibility only drives an
        /// inner CanvasGroup, not the window). <see cref="IsActive"/> — the prompt's
        /// widgets are the ones to DOCK RIGHT NOW (open AND visible AND topmost) —
        /// gates what the surface converts, so an alpha-hidden panel behind an open
        /// burn-confirm dialog is not docked over it. All delegates are cheap and
        /// allocation-free in steady state.
        /// </summary>
        internal sealed class Prompt
        {
            internal readonly string Name;
            internal readonly Func<UIWindow?> Window;
            internal readonly Func<bool> IsOpen;
            internal readonly Func<bool> IsActive;
            internal readonly Func<RectTransform?> FindRow;

            internal Prompt(string name, Func<UIWindow?> window, Func<bool> isOpen,
                Func<bool> isActive, Func<RectTransform?> findRow)
            {
                Name = name;
                Window = window;
                IsOpen = isOpen;
                IsActive = isActive;
                FindRow = findRow;
            }
        }

        /// <summary>Reused between per-tick row isolations (single active prompt — see the surface).</summary>
        private static readonly List<Transform> RowScratch = new(4);

        /// <summary>A window handed back to the generic float after the surface's grace expired.</summary>
        private static UIWindow? _gaveUp;

        // DialogPopup FIRST: it is the follow-up confirm layered ON TOP of the
        // take-damage panel, so when both windows are open it is the active prompt.
        internal static readonly Prompt[] Prompts =
        {
            // UIManager.dialogPopup (ID None — poll-tracked): the burn-confirm and the
            // short-rest lose-card confirms (CardsHandUI.cs:826/850/909/2069). Its
            // option buttons are pooled InputButtons under horizontal/verticalOptions-
            // Holder; the actionable widgets are their ExtendedButton transforms. The
            // embedded card lives under contentHolder — NOT part of the row (a parallel
            // worker renders the card); it is suppressed with the rest of the window.
            // A modal dialog is active whenever it is open (nothing layers over it).
            new("DialogPopup",
                static () => DialogPop()?.Window,
                static () => { DialogPopup? d = DialogPop(); return d != null && d.IsOpen(); },
                static () => { DialogPopup? d = DialogPop(); return d != null && d.IsOpen(); },
                static () =>
                {
                    DialogPopup? d = DialogPop();
                    if (d == null || d.Window == null)
                        return null;
                    RowScratch.Clear();
                    List<InputButton>? buttons = d.optionButtons;
                    if (buttons != null)
                    {
                        for (int i = 0; i < buttons.Count; i++)
                        {
                            InputButton ib = buttons[i];
                            if (ib == null || !ib.gameObject.activeInHierarchy)
                                continue;
                            ExtendedButton eb = ib.ExtendedButton;
                            if (eb != null)
                                RowScratch.Add(eb.transform);
                        }
                    }
                    return IsolateRow(d.Window, RowScratch);
                }),

            // TakeDamagePanel (UIWindowID TakeDamagePanel): two burn toggles + the
            // take-damage button (all serialized on the Singleton). IsOpen (claim) is
            // window-open ALONE — it must stay claimed through the whole burn flow so
            // the generic float/ModalUI never wakes and the LoseCard fan stays live —
            // but IsActive (dock) also requires the panel be VISIBLE: while the game
            // alpha-hides it (canvasGroupVisbility → 0) behind the LoseCard pick + the
            // burn-confirm dialog, its row must NOT dock over the dialog.
            new("TakeDamagePanel",
                static () => TakeDamage()?.myWindow,
                static () => { TakeDamagePanel? p = TakeDamage(); return p != null && p.myWindow != null && p.myWindow.IsOpen; },
                static () =>
                {
                    TakeDamagePanel? p = TakeDamage();
                    if (p == null || p.myWindow == null || !p.myWindow.IsOpen)
                        return false;
                    CanvasGroup? cg = p.canvasGroupVisbility; // game's inner visibility toggle
                    return cg == null || cg.alpha > 0.01f;
                },
                static () =>
                {
                    TakeDamagePanel? p = TakeDamage();
                    if (p == null || p.myWindow == null)
                        return null;
                    RowScratch.Clear();
                    if (p.burnAvailableCardsToggle != null)
                        RowScratch.Add(p.burnAvailableCardsToggle.transform);
                    if (p.burnDiscardedCardsToggle != null)
                        RowScratch.Add(p.burnDiscardedCardsToggle.transform);
                    if (p.takeDamageButton != null)
                        RowScratch.Add(p.takeDamageButton.transform);
                    return IsolateRow(p.myWindow, RowScratch);
                }),

            // YesNoDialog (ID scene-serialized, poll-tracked): the short-rest
            // confirmation ("GUI_SHORT_REST_CONFIRMATION", ShortRest.cs:100/244 — a
            // YesNoDialog shown NEXT TO the 2D short-rest button in a HUD dialogHolder,
            // mislocated + unpressable in VR → the test #24/#25 deadlock). It carries
            // serialized yesButton/noButton (ExtendedButton : Button, standard onClick
            // wired on window.onShown, YesNoDialog.cs:112-113 → OnYes/OnNoClickHandle →
            // the game's own short-rest confirm/cancel callbacks, ShortRest.cs:100-119)
            // AND a descriptionText, all children of the serialized dialog container
            // `box` (moved by YesNoDialog.Show).
            //
            // WHOLE-WINDOW DOCK (test #25 items 1b/1c — THE deadlock fix): dock the
            // ENTIRE dialog box, not the isolated yes/no row. Isolating only the common
            // ancestor of the two buttons dropped the question text (a sibling of the
            // button row under `box`) so the player could not read what they were
            // confirming (1b), AND the button-only row measured as EMPTY content — the
            // content-fit found "nothing visible", so the docked poke/laser plane
            // collapsed to a degenerate rect and the ExtendedButton clicks never landed
            // (1c, the dead Ja/Nein). `box` is a strict descendant of the window root
            // (YesNoDialog is [RequireComponent(UIWindow)] — window sits on the root,
            // box is its child), so it satisfies the surface's descendant contract,
            // brings the readable question along, and gives the fit real graphics to
            // size the interactive plane on. The buttons' onClick already fired on
            // onShown, so a laser/poke ExecuteEvents click on the docked box drives the
            // real handlers. A modal popup is active whenever its window is open
            // (window.IsPopUp, nothing layers over it); the window remainder (mislocated
            // 2D frame/backdrop) is suppressed like every docked prompt — harmless to the
            // docked box, which has been reparented out onto our host. Reached via the
            // active hand's ShortRest.yesNoDialog (CardsGameApi.ShortRestDialog).
            new("YesNoDialog",
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null ? d.window : null; },
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null && d.window != null && d.window.IsOpen; },
                static () => { YesNoDialog? d = ShortRestYesNo(); return d != null && d.window != null && d.window.IsOpen; },
                static () =>
                {
                    YesNoDialog? d = ShortRestYesNo();
                    if (d == null || d.window == null)
                        return null;
                    // Whole-window: the dialog box carries the question text + both
                    // buttons together (a strict descendant of the window root).
                    RectTransform? box = d.box;
                    if (box != null && !ReferenceEquals(box, d.window.transform)
                        && box.IsChildOf(d.window.transform))
                        return box;
                    // Fallback (box unexpectedly null): isolate the yes/no row so at
                    // least the buttons dock rather than deadlocking on a missing target.
                    RowScratch.Clear();
                    if (d.yesButton != null)
                        RowScratch.Add(d.yesButton.transform);
                    if (d.noButton != null)
                        RowScratch.Add(d.noButton.transform);
                    return IsolateRow(d.window, RowScratch);
                }),
        };

        private static TakeDamagePanel? TakeDamage() =>
            Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;

        /// <summary>The active hand's short-rest confirmation YesNoDialog (test #24 item 5), or null.</summary>
        private static YesNoDialog? ShortRestYesNo() => Cards.CardsGameApi.ShortRestDialog();

        private static DialogPopup? DialogPop()
        {
            UIManager? m = UIManager.Instance;
            return m != null ? m.dialogPopup : null;
        }

        /// <summary>The decision-dock feature is live (config + conversion + in a scenario).</summary>
        private static bool FeatureEnabled =>
            WorldUIConfig.DecisionDock.Value && WorldUIConfig.ConversionActive
            && Choreographer.s_Choreographer != null;

        /// <summary>Clear a stale hand-off once its window closed, so the next prompt re-arms.</summary>
        private static void PruneGaveUp()
        {
            // Unity-null (destroyed) trips the '!= null' guard already; a live-but-closed
            // window clears here so the same prompt can re-dock on its next open.
            if (_gaveUp != null && !_gaveUp.IsOpen)
                _gaveUp = null;
        }

        /// <summary>
        /// The prompt whose widgets to dock right now: the first ACTIVE (open + visible
        /// + topmost) prompt. Skips a window the grace handed back to the generic float.
        /// DialogPopup is listed first, so a burn-confirm layered over the alpha-hidden
        /// take-damage panel wins.
        /// </summary>
        internal static Prompt? ActivePrompt()
        {
            PruneGaveUp();
            if (!FeatureEnabled)
                return null;
            for (int i = 0; i < Prompts.Length; i++)
            {
                Prompt p = Prompts[i];
                UIWindow? w = p.Window();
                if (w == null || ReferenceEquals(w, _gaveUp))
                    continue;
                if (p.IsActive())
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Does the decision dock currently own this window? Consulted by
        /// <see cref="ModalFallback"/> (stand the generic path down) AND by the
        /// surface's WantConverted — so the surface docks EXACTLY what the generic
        /// path releases. Instance-based (not ID) so it covers both the ID-tracked
        /// TakeDamagePanel and the poll-tracked dialogPopup.
        /// </summary>
        internal static bool ClaimsWindow(UIWindow? window)
        {
            if (window == null || !FeatureEnabled)
                return false;
            PruneGaveUp();
            if (ReferenceEquals(window, _gaveUp))
                return false;
            for (int i = 0; i < Prompts.Length; i++)
            {
                Prompt p = Prompts[i];
                UIWindow? w = p.Window();
                if (w != null && ReferenceEquals(w, window) && p.IsOpen())
                    return true;
            }
            return false;
        }

        /// <summary>Hand a window back to the generic float (the surface's grace expired for it).</summary>
        internal static void MarkGaveUp(UIWindow window) => _gaveUp = window;

        /// <summary>Drop the hand-off state (surface shutdown / module detach).</summary>
        internal static void Reset() => _gaveUp = null;

        /// <summary>
        /// The interactive ROW: deepest common ancestor of the actionable widgets that
        /// is a STRICT descendant of the window root — found structurally, never by
        /// name. Null (→ retry, grace running) when there are no widgets, or the
        /// ancestor is the window root itself / outside it (docking that would drag the
        /// vignette + card along). A single-widget prompt lifts to the widget's layout
        /// container so a proper row (not a lone control) docks.
        /// </summary>
        internal static RectTransform? IsolateRow(UIWindow window, List<Transform> widgets)
        {
            if (window == null || widgets.Count == 0)
                return null;
            Transform? ca = null;
            for (int i = 0; i < widgets.Count; i++)
                ca = ca == null ? widgets[i] : CommonAncestor(ca, widgets[i]);
            if (ca == null)
                return null;
            if (widgets.Count == 1 && ca.parent != null)
                ca = ca.parent;
            if (ReferenceEquals(ca, window.transform) || !ca.IsChildOf(window.transform))
                return null;
            return ca as RectTransform;
        }

        /// <summary>Deepest common ancestor of two transforms (null-tolerant).</summary>
        private static Transform? CommonAncestor(Transform? a, Transform? b)
        {
            if (a == null || b == null)
                return null;
            int da = Depth(a), db = Depth(b);
            while (da > db) { a = a!.parent; da--; }
            while (db > da) { b = b!.parent; db--; }
            while (a != null && b != null && !ReferenceEquals(a, b))
            {
                a = a.parent;
                b = b.parent;
            }
            return a != null && ReferenceEquals(a, b) ? a : null;
        }

        private static int Depth(Transform t)
        {
            int d = 0;
            for (Transform? p = t.parent; p != null; p = p.parent)
                d++;
            return d;
        }
    }
}

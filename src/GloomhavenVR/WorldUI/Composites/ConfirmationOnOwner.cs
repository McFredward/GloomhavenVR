using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Keeps an original native confirmation inside the actual screen which requested it.
/// Build719 hardware showed the level-up confirmation floating separately; DialogSurface
/// converted every pooled confirmation at the HMD, irrespective of its native producer.
/// Producer scopes record identity at the original Show call (see the companion file),
/// never whichever window happens to be open or focused on a later frame. Requests with
/// no proved producer retain their existing standalone continuation path.
/// </summary>
internal static partial class ConfirmationOnOwner
{
    private sealed class Seat
    {
        internal readonly Component Box;
        internal readonly Request Request;
        internal readonly RectTransform Rect;
        internal readonly Transform? Home;
        internal readonly int Index;
        internal readonly Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
        internal readonly Vector3 LocalPosition, Scale;
        internal readonly Quaternion Rotation;
        internal readonly LayoutElement Layout;
        internal readonly bool AddedLayout, IgnoreLayout;
        internal readonly ConvertedPanel Panel;
        internal readonly RectTransform Mount;
        internal readonly Vector2 Frame;
        // A native box must not acquire its producer's scene lifetime.
        // Its chrome-free capture host stays in its own scene and shares the owner's
        // exact world pose/scale, rather than acquiring a second placement author.
        internal ConvertedPanel? PersistentMember;

        internal Seat(Component box, Request request, RectTransform rect,
            ConvertedPanel panel, RectTransform mount)
        {
            Box = box; Request = request; Rect = rect; Panel = panel; Mount = mount;
            Home = rect.parent; Index = rect.GetSiblingIndex();
            AnchorMin = rect.anchorMin; AnchorMax = rect.anchorMax; Pivot = rect.pivot;
            Position = rect.anchoredPosition; LocalPosition = rect.localPosition;
            Size = rect.sizeDelta; Scale = rect.localScale; Rotation = rect.localRotation;
            Frame = rect.rect.size;
            LayoutElement? existingLayout = rect.GetComponent<LayoutElement>();
            AddedLayout = existingLayout == null;
            Layout = existingLayout ?? rect.gameObject.AddComponent<LayoutElement>();
            IgnoreLayout = Layout.ignoreLayout;
        }
    }

    private static readonly List<Seat> Seats = new(2);
    private const float Epsilon = ChromeParkTuning.OffsetEpsilonPx;
    private static int _reports;

    internal static bool IsOwned(UIWindow? window)
    {
        for (int i = 0; i < Seats.Count; i++)
            if (window != null && ReferenceEquals(window.transform, Seats[i].Rect))
                return true;
        return false;
    }

    // Called before modal enrollment and independently by DialogSurface: no shown edge or
    // discovery order may decide whether a late-joining/recreated source can be contained.
    internal static void Tick()
    {
        for (int i = Seats.Count - 1; i >= 0; i--)
        {
            Seat seat = Seats[i];
            if (ForeignOwner(seat.Box)) continue;
            if (!Enabled || seat.Box == null || !StillShowing(seat.Box)
                || !SourceAlive(seat.Request) || !PanelAlive(seat.Panel)
                || (seat.PersistentMember != null && !PanelAlive(seat.PersistentMember)))
                ReleaseAt(i);
        }
        UIConfirmationBoxManager? manager = Singleton<UIConfirmationBoxManager>.IsInitialized
            ? Singleton<UIConfirmationBoxManager>.Instance : null;
        if (manager != null && manager.CurrentBox != null)
            TrySeat(manager.CurrentBox);
        UIConfirmationBoxManager? menu = UIConfirmationBoxManager.MainMenuInstance;
        if (menu != null && !ReferenceEquals(menu, manager) && menu.CurrentBox != null)
            TrySeat(menu.CurrentBox);
        UIItemConfirmationBox? item = Singleton<UIItemConfirmationBox>.IsInitialized
            ? Singleton<UIItemConfirmationBox>.Instance : null;
        if (item != null) TrySeat(item);
    }

    private static bool Enabled => WorldUIConfig.ConversionActive
        && (VRModeStateMachine.TableInFrontOfPlayer || MenuWorldContext.IsReady)
        && !FlatScreen.ManualScreenActive;

    internal static bool TrySeat(ConfirmationBox box) => TrySeat((Component)box);

    private static bool TrySeat(Component box)
    {
        if (box == null) return false;
        if (ForeignOwner(box))
        {
            Requests.Remove(box); // the immersive native request must not become a later flat claim
            return IsOwned(box.GetComponent<UIWindow>());
        }
        if (!Enabled || !StillShowing(box))
        {
            Release(box);
            if (!StillShowing(box)) Requests.Remove(box);
            return false;
        }
        if (!Requests.TryGetValue(box, out Request? request) || !SourceAlive(request))
        {
            Release(box);
            return IsOwned(box.GetComponent<UIWindow>());
        }

        ConvertedPanel? panel = FindPanel(request.Source.transform);
        if (panel == null)
        {
            Release(box);
            // A refused restoration retains its claim rather than creating a competing float.
            return IsOwned(box.GetComponent<UIWindow>());
        }
        RectTransform? mount = request.Window != null
            && request.Window.transform.IsChildOf(panel.Target)
            ? request.Window.transform as RectTransform : panel.Target;
        RectTransform? rect = box.transform as RectTransform;
        // Build742: every proved native producer owns its prompt, including Perks.
        // Its pooled DDOL box has the same persistence contract as level-up. Scene
        // identity and producer class are not presentation permissions: both same-
        // and cross-scene boxes need the same foreground/background treatment.
        if (mount == null || rect == null || ReferenceEquals(mount, rect)
            || mount.IsChildOf(rect))
        {
            Release(box);
            return IsOwned(box.GetComponent<UIWindow>());
        }

        for (int i = Seats.Count - 1; i >= 0; i--)
        {
            Seat current = Seats[i];
            if (!ReferenceEquals(current.Box, box)) continue;
            if (ReferenceEquals(current.Request, request) && ReferenceEquals(current.Mount, mount)
                && ReferenceEquals(current.Panel, panel))
            {
                Apply(current);
                return true;
            }
            if (!ReleaseAt(i)) return true;
        }

        try
        {
            UIWindow? window = box.GetComponent<UIWindow>();
            // Capture native home only after all standalone capture/fit/grab writers retired.
            if (window == null || !ModalFallback.ReleaseForComposite(window)) return false;
            if (box is UIItemConfirmationBox && !MapDialogSeat.ReleaseForConfirmationOwner(window)) return false;
            if (ForeignOwner(box)) return false;
            Vector2 size = rect.rect.size;
            if (rect.parent == null || size.x < 2f || size.y < 2f) return false;
            var seat = new Seat(box, request, rect, panel, mount);
            Seats.Add(seat); // failed adoption can now unwind its complete native snapshot
            seat.Layout.ignoreLayout = true;
            // Convert preserves the original's DontDestroyOnLoad scene before
            // reparenting. This is a backing capture only: no grab bar, materialize,
            // HMD placement or shared-window pose is created or requested.
            // Build738: the generic confirmation's original full-frame
            // SimpleGrabPassBlur must take the same isolated capture and
            // scoped blur remedy as ordinary windows. The default native
            // UI layer bypassed that path in both this and the old float.
            // Build738 hardware refused that capture at its session
            // budget, leaving the full-frame Black_Backdrop and depth-writing
            // GrabPass blur directly in the eyes. Hide those original full-cover
            // graphics before reveal, independently of capture admission. The
            // smaller native Box, text, buttons and modal callbacks stay intact;
            // Release restores the recorded graphics for the flat native home.
            // Its 32 px authored foreground offset is inside the order ladder's
            // 2 cm tie margin at the accepted owner scale. Build738 therefore
            // kept the new default-tier member at 116 behind its owner at 132,
            // even though the member was nearer. Seed this owned confirmation
            // one tier above its actual owner so the existing tie rule paints
            // the small dialog over that owner from the first visible frame.
            seat.PersistentMember = CanvasConversion.Convert(rect, "LevelUpConfirmationMember",
                pokeTuning: Hands.Interact.PokeSurfaceTuning.SmallDialog, fitContent: false,
                sortingOrder: panel.BaseSortingOrder + 1,
                flattenWindow: true, useModLayer: true, transparentBackground: true);
            if (seat.PersistentMember == null) { Release(box); return false; }
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            Apply(seat);
            PanelSupersample.BindConfirmationMember(panel, seat.PersistentMember);
            if (_reports++ < 24)
                VRLog.Info("WorldUI", $"CONFIRMATION OWNER: '{box.name}' is part of native producer "
                    + $"'{request.Source.name}' inside '{panel.Target.name}'; original buttons and callbacks retained.");
            return true;
        }
        catch (Exception ex)
        {
            Release(box);
            if (!request.ReportedFailure)
            {
                request.ReportedFailure = true;
                VRLog.Alert("WorldUI", "CONFIRMATION OWNER: original containment failed ("
                    + ex.GetType().Name + "); existing standalone continuation remains available.");
            }
            return IsOwned(box.GetComponent<UIWindow>());
        }
    }

    private static bool ForeignOwner(Component box)
    {
        if (box == null) return false;
        UIWindow? window = box.GetComponent<UIWindow>();
        return window != null && TownServicePresentation.OwnsWindow(window);
    }

    // Existing immersive/composite owners call the common transfer seam before
    // taking the original. Retire this complete lease before they record its home.
    internal static bool ReleaseForExternalOwner(UIWindow window)
    {
        if (window == null) return false;
        for (int i = Seats.Count - 1; i >= 0; i--)
        {
            Seat seat = Seats[i];
            if (!ReferenceEquals(seat.Rect, window.transform)) continue;
            if (!ReleaseAt(i)) return false;
            Requests.Remove(seat.Box);
        }
        return !IsOwned(window);
    }

    private static bool SourceAlive(Request request) => request.Source != null
        && request.Source.gameObject.activeInHierarchy
        && (request.Window == null || request.Window.gameObject.activeInHierarchy);

    private static bool StillShowing(Component box)
    {
        UIWindow? window = box.GetComponent<UIWindow>();
        return (box is ConfirmationBox generic && generic.IsOpen)
            || (box is UIItemConfirmationBox item && item.IsActive)
            || (window != null && window.IsVisible);
    }

    private static bool PanelAlive(ConvertedPanel panel)
    {
        if (!panel.IsAlive || panel.HostGo == null || !panel.HostGo.activeInHierarchy) return false;
        for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
            if (ReferenceEquals(CanvasConversion.ActivePanels[i], panel)) return true;
        return FlatScreen.IsMenuOwner(panel);
    }

    private static ConvertedPanel? FindPanel(Transform source)
    {
        // Walk outward: the closest actual containing conversion wins. No source key,
        // focus, topmost menu, translated text or another player's private screen is guessed.
        for (Transform? node = source; node != null; node = node.parent)
            for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
            {
                ConvertedPanel panel = CanvasConversion.ActivePanels[i];
                if (ReferenceEquals(panel.Target, node) && PanelAlive(panel)) return panel;
            }
        return FlatScreen.TryGetMenuOwner(source, out ConvertedPanel? menuPanel) ? menuPanel : null;
    }

    private static void Apply(Seat seat)
    {
        RectTransform rect = seat.Rect;
        if ((rect.sizeDelta - seat.Frame).sqrMagnitude > Epsilon * Epsilon)
            rect.sizeDelta = seat.Frame;
        if (rect.anchoredPosition.sqrMagnitude > Epsilon * Epsilon)
            rect.anchoredPosition = Vector2.zero;
        Vector3 position = rect.localPosition;
        if (position.z != 0f) rect.localPosition = new Vector3(position.x, position.y, 0f);
        if (seat.PersistentMember != null)
        {
            RectTransform host = seat.PersistentMember.HostRect;
            // Native pixels, not head distance/zoom. Its pose follows only the accepted
            // owner matrix, including manual drag/resize; a coplanar source would z-fight.
            host.SetPositionAndRotation(seat.Mount.TransformPoint(new Vector3(0f, 0f, -32f)), seat.Mount.rotation);
            host.localScale = seat.Mount.lossyScale;
            if (seat.PersistentMember.HostRaycaster != null)
                seat.PersistentMember.HostRaycaster.enabled = true;
            return;
        }

    }

    internal static void Release(ConfirmationBox box) => Release((Component)box);

    private static void Release(Component box)
    {
        for (int i = Seats.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Seats[i].Box, box)) ReleaseAt(i);
    }

    private static bool ReleaseAt(int index)
    {
        Seat seat = Seats[index];
        RectTransform rect = seat.Rect;
        if (rect == null) { Seats.RemoveAt(index); return true; }
        try
        {
            // Modal native Hide/Cancel owns progression. In particular, IsOpen=false
            // can be deliberate parent dimming while a confirmation is up. Never infer
            // cancellation from it, and never invoke a callback from presentation teardown.
            if (seat.PersistentMember != null)
            {
                CanvasConversion.Release(seat.PersistentMember);
                // ConvertedPanel.IsAlive means its original Target survives,
                // which is required after a successful release. It is not a
                // conversion lease: using it here retained every persistent
                // seat forever and re-released a closed box on every tick.
                // The original-home check below still retains a refused restore.
                if (PanelAlive(seat.PersistentMember)) return false;
            }
            rect.SetParent(seat.Home != null ? seat.Home : null, worldPositionStays: false);
            if (!ReferenceEquals(rect.parent, seat.Home != null ? seat.Home : null))
                return false; // Unity can reject reparenting without throwing: retain the restoration record.
            CanvasConversion.TransferSeatedSubtree(rect, null);
            rect.SetSiblingIndex(seat.Index);
            rect.anchorMin = seat.AnchorMin; rect.anchorMax = seat.AnchorMax; rect.pivot = seat.Pivot;
            rect.sizeDelta = seat.Size; rect.anchoredPosition = seat.Position;
            Vector3 position = rect.localPosition;
            rect.localPosition = new Vector3(position.x, position.y, seat.LocalPosition.z);
            rect.localRotation = seat.Rotation; rect.localScale = seat.Scale;
            if (seat.AddedLayout) UnityEngine.Object.Destroy(seat.Layout);
            else if (seat.Layout != null) seat.Layout.ignoreLayout = seat.IgnoreLayout;
            Seats.RemoveAt(index);
            return true;
        }
        catch (Exception ex)
        {
            if (!seat.Request.ReportedFailure)
            {
                seat.Request.ReportedFailure = true;
                VRLog.Alert("WorldUI", "CONFIRMATION OWNER: restoring original dialog failed ("
                    + ex.GetType().Name + "); retaining its restoration record; no native callback was dispatched.");
            }
            return false;
        }
    }

    internal static void Reset()
    {
        for (int i = Seats.Count - 1; i >= 0; i--) ReleaseAt(i);
        Requests = new System.Runtime.CompilerServices.ConditionalWeakTable<Component, Request>();
        NavigationOpenings.Clear();
        NativeShowDepth = 0;
        CurrentScope = null;
    }
}

using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Keeps an original generic confirmation inside the actual screen which requested it.
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
        internal readonly ConfirmationBox Box;
        internal readonly Request Request;
        internal readonly RectTransform Rect;
        internal readonly Transform? Home;
        internal readonly int Index;
        internal readonly Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
        internal readonly Vector3 LocalPosition, Scale;
        internal readonly Quaternion Rotation;
        internal readonly LayoutElement Layout;
        internal readonly bool AddedLayout, IgnoreLayout;
        internal readonly MovedSubtreeLayers Layers = new(64);
        internal readonly ConvertedPanel Panel;
        internal readonly RectTransform Mount;
        internal readonly Vector2 Frame;
        // A persistent native box must not become a child of a scene-owned level-up.
        // Its chrome-free capture host stays in its own scene and shares the owner's
        // exact world pose/scale, rather than acquiring a second placement author.
        internal ConvertedPanel? PersistentMember;

        internal Seat(ConfirmationBox box, Request request, RectTransform rect,
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
            if (!Enabled || seat.Box == null || !StillShowing(seat.Box)
                || !SourceAlive(seat.Request) || !PanelAlive(seat.Panel)
                || (seat.PersistentMember != null && !seat.PersistentMember.IsAlive))
                ReleaseAt(i);
        }
        UIConfirmationBoxManager? manager = Singleton<UIConfirmationBoxManager>.IsInitialized
            ? Singleton<UIConfirmationBoxManager>.Instance : null;
        if (manager != null && manager.CurrentBox != null)
            TrySeat(manager.CurrentBox);
        UIConfirmationBoxManager? menu = UIConfirmationBoxManager.MainMenuInstance;
        if (menu != null && !ReferenceEquals(menu, manager) && menu.CurrentBox != null)
            TrySeat(menu.CurrentBox);
    }

    private static bool Enabled => WorldUIConfig.ConversionActive
        && VRModeStateMachine.TableInFrontOfPlayer && !FlatScreen.ManualScreenActive;

    internal static bool TrySeat(ConfirmationBox box)
    {
        if (box == null) return false;
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
        bool separateScene = mount != null && rect != null && mount.gameObject.scene != rect.gameObject.scene;
        // Only the explicitly requested level-up family receives a persistent visual
        // member. Unrelated cross-scene producers keep their existing standalone path.
        if (mount == null || rect == null || ReferenceEquals(mount, rect)
            || mount.IsChildOf(rect) || (separateScene && request.Source is not UILevelUpWindow
                && request.Source is not UIResetLevelUpWindow))
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
            Vector2 size = rect.rect.size;
            if (rect.parent == null || size.x < 2f || size.y < 2f) return false;
            var seat = new Seat(box, request, rect, panel, mount);
            Seats.Add(seat); // failed adoption can now unwind its complete native snapshot
            seat.Layout.ignoreLayout = true;
            if (separateScene)
            {
                // Convert preserves the original's DontDestroyOnLoad scene before
                // reparenting. This is a backing capture only: no grab bar, materialize,
                // HMD placement or shared-window pose is created or requested.
                seat.PersistentMember = CanvasConversion.Convert(rect, "LevelUpConfirmationMember",
                    pokeTuning: Hands.Interact.PokeSurfaceTuning.SmallDialog, fitContent: false,
                    flattenWindow: true);
                if (seat.PersistentMember == null) { Release(box); return false; }
            }
            else rect.SetParent(mount, worldPositionStays: false);
            if (seat.PersistentMember == null && !ReferenceEquals(rect.parent, mount))
            {
                Release(box);
                return IsOwned(window);
            }
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            if (seat.PersistentMember == null)
            {
                CanvasConversion.TransferSeatedSubtree(rect, panel);
                seat.Layers.Begin(mount.gameObject.layer);
                seat.Layers.Walk(rect);
            }
            Apply(seat);
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

    private static bool SourceAlive(Request request) => request.Source != null
        && request.Source.gameObject.activeInHierarchy
        && (request.Window == null || request.Window.gameObject.activeInHierarchy);

    private static bool StillShowing(ConfirmationBox box)
    {
        UIWindow? window = box.GetComponent<UIWindow>();
        return box.IsOpen || (window != null && window.IsVisible);
    }

    private static bool PanelAlive(ConvertedPanel panel)
    {
        if (!panel.IsAlive || panel.HostGo == null || !panel.HostGo.activeInHierarchy) return false;
        for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
            if (ReferenceEquals(CanvasConversion.ActivePanels[i], panel)) return true;
        return false;
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
        return null;
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
        if (seat.Layers.Written != seat.Mount.gameObject.layer)
        {
            seat.Layers.Begin(seat.Mount.gameObject.layer);
            seat.Layers.Walk(rect);
        }
        else if (rect.gameObject.layer != seat.Mount.gameObject.layer)
            seat.Layers.Walk(rect);
        // This is the containing host's raycaster, not an input permission override.
        // Native disabled/non-interactable controls still refuse their original callbacks.
        if (seat.Panel.HostRaycaster != null && !seat.Panel.HostRaycaster.enabled)
            seat.Panel.HostRaycaster.enabled = true;
    }

    internal static void Release(ConfirmationBox box)
    {
        for (int i = Seats.Count - 1; i >= 0; i--)
            if (ReferenceEquals(Seats[i].Box, box)) ReleaseAt(i);
    }

    private static bool ReleaseAt(int index)
    {
        Seat seat = Seats[index];
        RectTransform rect = seat.Rect;
        if (rect == null) { Seats.RemoveAt(index); seat.Layers.Forget(); return true; }
        try
        {
            // Modal native Hide/Cancel owns progression. In particular, IsOpen=false
            // can be deliberate parent dimming while a confirmation is up. Never infer
            // cancellation from it, and never invoke a callback from presentation teardown.
            if (seat.PersistentMember != null)
            {
                CanvasConversion.Release(seat.PersistentMember);
                if (seat.PersistentMember.IsAlive) return false;
            }
            rect.SetParent(seat.Home != null ? seat.Home : null, worldPositionStays: false);
            if (!ReferenceEquals(rect.parent, seat.Home != null ? seat.Home : null))
                return false; // Unity can reject reparenting without throwing: retain the restoration record.
            CanvasConversion.TransferSeatedSubtree(rect, null);
            seat.Layers.Restore();
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
        Requests = new System.Runtime.CompilerServices.ConditionalWeakTable<ConfirmationBox, Request>();
        CurrentScope = null;
    }
}

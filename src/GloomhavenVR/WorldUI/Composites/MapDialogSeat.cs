using Action = System.Action;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Seats each native map confirmation inside the converted window that raised it.
/// The item box announces its merchant/party discriminator. The shared temple/
/// enhancement box records the destination at its own Show edge, rather than
/// interpreting a later destination switch as a new request. Closing that original
/// host cancels through the game's own Hide path before the next HUD mode enters.
/// Native confirmation callbacks, navigation and fade completion remain authoritative.
/// </summary>
internal static class MapDialogSeat
{
    private const string Scope = "WorldUI";

    /// <summary>Below this the dialog has not laid out yet and a park would own a zero size — the
    /// ModBuild 232 defect. Same floor and same reason as
    /// <c>EnchantressComposite.MinParkSizePx</c>.</summary>
    private const float MinParkSizePx = 2f;

    /// <summary>Copied from <see cref="ChromeParkTuning.OffsetEpsilonPx"/> for its reason: a settled
    /// uGUI layout is not bit-identical frame to frame, and re-writing the solved offset every frame
    /// is indistinguishable from a write war in a log.</summary>
    private const float OffsetEpsilonPx = ChromeParkTuning.OffsetEpsilonPx;

    /// <summary>Cap on the per-raise answer line, per seat, per session. A dialog is a deliberate
    /// player act and cannot be per-frame chatter, but a stuck open/close loop could be; the cap
    /// bounds it without hiding the first raises, which are the ones a hardware round reads.</summary>
    private const int MaxRaiseReports = 24;

    /// <summary>Scratch for the layer walk (allocation-free steady state).</summary>
    private static readonly List<Transform> LayerScratch = new(256);

    /// <summary>
    /// ONE SEAT PER DIALOG. A class field per dialog rather than one shared "current dialog" slot,
    /// deliberately: the user's third requirement is that two dialogs may stand at once, and while
    /// the GAME refuses that today (see the class doc) nothing in THIS file may be the reason. Two
    /// seats is also what makes the merchant-dialog-while-the-character-panel-is-open case work,
    /// which is the one concurrent pairing that can actually arise.
    /// </summary>
    private sealed class Seat
    {
        internal Seat(string label)
        {
            Label = label;
            HostHidden = () => OnRaisingHostHidden(this);
        }

        /// <summary>Human name for the log — the dialog this seat is about.</summary>
        internal string Label { get; }

        internal RectTransform? Parked;
        internal UIWindow? Host;
        internal UIWindow? CancellationHost;
        internal readonly Action HostHidden;
        internal UIWindow? NativeDialog;
        internal EGuildmasterMode? RequestedMode;
        internal bool Closing;

        internal Transform? Home;
        internal UIWindow? HomeWindow;
        internal int HomeIndex;
        internal Vector2 HomeAnchorMin;
        internal Vector2 HomeAnchorMax;
        internal Vector2 HomePivot;
        internal Vector2 HomeAnchoredPos;
        internal Vector2 HomeSizeDelta;
        internal Quaternion HomeRotation = Quaternion.identity;
        internal Vector3 HomeScale = Vector3.one;
        internal string HomeWindowName = "<unresolved>";

        internal LayoutElement? AddedIgnore;
        internal bool HomeIgnoreLayout;

        /// <summary>The size captured BEFORE the anchor write — the ModBuild 232 term.</summary>
        internal Vector2 ParkedSize;

        internal int ParkedFromLayer = -1;
        internal int ParkedToLayer = -1;
        internal int ParkedLayerCount;

        internal string WaitReported = string.Empty;
        internal int RaiseReports;

        /// <summary>Set by the game's own event; the merchant/party discriminator. Null until the
        /// game has announced a raise this session.</summary>
        internal BoxConfirmationType? RequestedAs;

        internal bool Subscribed;
    }

    private static readonly Seat ItemSeat = new("the item confirmation box (buy / sell / equip)");
    private static readonly Seat EnhancementSeat = new("the enhancement confirmation box (enchantress / temple)");
    private static UIWindow? _enhancementWindow;

    /// <summary>
    /// One tick. Called from <c>ModalFallback.TickCatchAll</c> beside
    /// <c>EnchantressComposite.Tick</c> — the same per-tick seam, for the same reason it sits there:
    /// a hand-back must be able to happen on a tick where every one of this class's own
    /// preconditions is already false.
    /// </summary>
    internal static void Tick()
    {
        TickItemBox();
        TickEnhancementBox();
    }

    // =============================================================================================
    // THE ITEM CONFIRMATION BOX — the reported defect
    // =============================================================================================

    private static void TickItemBox()
    {
        UIItemConfirmationBox? box = Singleton<UIItemConfirmationBox>.IsInitialized
            ? Singleton<UIItemConfirmationBox>.Instance
            : null;
        if (box == null)
        {
            Release(ItemSeat, "the item confirmation box singleton is gone");
            Wait(ItemSeat, string.Empty);
            return;
        }

        // Subscribe ONCE to the game's own announcement of who is asking. This is the whole
        // discriminator and it is the game's, not ours: Buy/Sell come only from UIShopItemInventory,
        // General only from UIPartyItemInventoryDisplay.
        if (!ItemSeat.Subscribed)
        {
            box.ConfirmationBoxRequested += OnItemConfirmationRequested;
            ItemSeat.Subscribed = true;
        }

        var window = box.GetComponent<UIWindow>();
        if (window == null || !StillShowing(window))
        {
            Release(ItemSeat, "the item confirmation box is closed and has finished fading out");
            Wait(ItemSeat, string.Empty);
            ItemSeat.RequestedAs = null;
            return;
        }

        // THE RAISER. General is the character UI's own — and the box's home already IS the party
        // display, so the correct answer for that case is to move nothing at all.
        if (ItemSeat.RequestedAs is not (BoxConfirmationType.Buy or BoxConfirmationType.Sell))
        {
            Release(ItemSeat, "the item confirmation box was raised by the character UI's own party "
                              + "inventory (BoxConfirmationType.General), whose window IS this box's "
                              + "home — there is nothing to move");
            Wait(ItemSeat, string.Empty);
            return;
        }

        UIWindow? raiser = GuildmasterDestinations.ModeWindow(EGuildmasterMode.Merchant);
        SeatOn(ItemSeat, window, raiser, "the merchant", EGuildmasterMode.Merchant);
    }

    /// <summary>
    /// The game announces the raiser on its own public event before it touches anything else
    /// (UIItemConfirmationBox.cs:84). Recording it is the entire read; nothing is written back.
    /// </summary>
    private static void OnItemConfirmationRequested(BoxConfirmationType type)
    {
        ItemSeat.RequestedAs = type;
    }

    // =============================================================================================
    // THE ENHANCEMENT CONFIRMATION BOX — the enchantress and the temple
    // =============================================================================================

    private static void TickEnhancementBox()
    {
        UIEnhancementConfirmationBox? box = Singleton<UIEnhancementConfirmationBox>.IsInitialized
            ? Singleton<UIEnhancementConfirmationBox>.Instance
            : null;
        var window = box != null ? box.GetComponent<UIWindow>() : null;
        ObserveEnhancementWindow(window);
        if (window == null || !StillShowing(window))
        {
            Release(EnhancementSeat, "the enhancement confirmation box has finished hiding");
            EnhancementSeat.RequestedMode = null;
            EnhancementSeat.Closing = false;
            Wait(EnhancementSeat, string.Empty);
            return;
        }

        if (TownServicePresentation.OwnsWindow(window))
        {
            Release(EnhancementSeat, "the immersive station owns this native confirmation");
            EnhancementSeat.RequestedMode = null;
            EnhancementSeat.Closing = false;
            Wait(EnhancementSeat, string.Empty);
            return;
        }

        // A singleton can first be discovered after its Show event. Resolve once on
        // that first live observation; a destination switch is never another raise.
        if (EnhancementSeat.RequestedMode == null && !EnhancementSeat.Closing)
            OnEnhancementShown();
        if (EnhancementSeat.Closing)
        {
            // A departing host completes cancellation instantly. Retain the
            // original owner until native zero visibility permits hand-back.
            if (EnhancementSeat.Parked != null && EnhancementSeat.Host != null)
                ApplyPose(EnhancementSeat, (RectTransform)EnhancementSeat.Host.transform);
            return;
        }
        EGuildmasterMode? mode = EnhancementSeat.RequestedMode;
        if (mode is not (EGuildmasterMode.Enchantress or EGuildmasterMode.Temple))
        {
            Release(EnhancementSeat, "no map destination raised this confirmation");
            Wait(EnhancementSeat, string.Empty);
            return;
        }
        UIWindow? raiser = GuildmasterDestinations.ModeWindow(mode.Value);
        SeatOn(EnhancementSeat, window, raiser,
               mode == EGuildmasterMode.Enchantress ? "the enchantress" : "the temple", mode.Value);
    }

    private static void ObserveEnhancementWindow(UIWindow? window)
    {
        if (ReferenceEquals(_enhancementWindow, window)) return;
        if (_enhancementWindow != null) _enhancementWindow.OnShow -= OnEnhancementShown;
        Release(EnhancementSeat, "the native enhancement confirmation instance changed");
        _enhancementWindow = window;
        EnhancementSeat.NativeDialog = window;
        EnhancementSeat.RequestedMode = null;
        EnhancementSeat.Closing = false;
        if (window != null) window.OnShow += OnEnhancementShown;
    }

    private static void OnEnhancementShown()
    {
        EnhancementSeat.Closing = false;
        EnhancementSeat.RequestedMode = GuildmasterDestinations.CurrentDestinationMode();
        EGuildmasterMode? mode = EnhancementSeat.RequestedMode;
        UIWindow? host = mode is EGuildmasterMode.Enchantress or EGuildmasterMode.Temple
            ? GuildmasterDestinations.ModeWindow(mode.Value) : null;
        // A flat host can close before its initial conversion finishes. Bind its
        // native Hide edge immediately, while leaving quiet immersive controllers
        // and their separately owned transaction masks untouched.
        if (host != null && host.IsOpen
            && !TownServicePresentation.OwnsWindow(host)
            && (_enhancementWindow == null || !TownServicePresentation.OwnsWindow(_enhancementWindow))
            && !TownServicePresentation.IsQuietController(host,
                mode == EGuildmasterMode.Temple ? (byte)2 : (byte)3))
            BindHostCancellation(EnhancementSeat, host);
    }

    private static void BindHostCancellation(Seat seat, UIWindow host)
    {
        if (ReferenceEquals(seat.CancellationHost, host)) return;
        DetachHostCancellation(seat);
        seat.CancellationHost = host;
        host.OnHide += seat.HostHidden;
    }

    private static void DetachHostCancellation(Seat seat)
    {
        if (seat.CancellationHost != null) seat.CancellationHost.OnHide -= seat.HostHidden;
        seat.CancellationHost = null;
    }

    private static void OnRaisingHostHidden(Seat seat)
    {
        if (!ReferenceEquals(seat, EnhancementSeat) || seat.NativeDialog == null
            || !StillShowing(seat.NativeDialog) || TownServicePresentation.OwnsWindow(seat.NativeDialog)) return;
        seat.Closing = true;
        // UIWindow.OnHide runs inside the outgoing HUD mode's Exit, before the
        // HUD enters another mode. Complete the native cancellation here: a new
        // ShowConfirmation resets this singleton's transition listeners and must
        // not be able to discard the outgoing temple cancellation callback.
        // Ordinary confirm/cancel button fades are unchanged.
        if (seat.NativeDialog.IsOpen)
            seat.NativeDialog.Hide(instant: true);
        else
            // A button already chose an outcome and started hiding. Complete that
            // existing tween without firing another OnHide or changing its result.
            seat.NativeDialog.StartAlphaTween(0f, 0f, ignoreTimeScale: true);
    }

    /// <summary>
    /// IS THIS DIALOG STILL ON SCREEN? <c>IsOpen</c> ALONE IS THE WRONG QUESTION.
    ///
    /// <para><c>UIWindow.IsOpen</c> is <c>m_CurrentVisualState == VisualState.Shown</c>
    /// (UIWindow.cs:317) and <c>EvaluateAndTransitionToVisualState</c> assigns that field BEFORE the
    /// alpha tween it starts has run (UIWindow.cs:540-548). So a dialog that the player has just
    /// dismissed reads NOT open while it is still visibly fading out. Handing it back on that edge
    /// would teleport a half-faded dialog from the merchant's window to the character UI's, which is
    /// the one thing this class exists to stop the player seeing ([[open-is-not-drawing]]).</para>
    ///
    /// <para>The alpha term is not a guess and not a settle gate: <c>UIWindow</c> carries
    /// <c>[RequireComponent(typeof(CanvasGroup))]</c> (UIWindow.cs:14), so the group is always
    /// there, and UIWindow's own visibility test reads exactly this field the same way
    /// (<c>m_CanvasGroup.alpha &gt; 0f</c>, UIWindow.cs:309). If the group were ever missing this
    /// falls back to <c>IsOpen</c>, which is the pre-473 behaviour and never holds a park forever.
    /// A park held open by a stuck alpha is bounded anyway: the host's own <c>FloatIsLive</c> term
    /// hands it back the moment the raising window stops being a live float.</para>
    /// </summary>
    private static bool StillShowing(UIWindow window)
    {
        if (window.IsOpen)
            return true;
        var group = window.GetComponent<CanvasGroup>();
        return group != null && group.alpha > 0f;
    }

    // =============================================================================================
    // THE SHARED SEATING RULE
    // =============================================================================================

    /// <summary>
    /// THE RULE, ONCE: a dialog is drawn inside the window that raised it, whenever that window is a
    /// live floated host and is not already the dialog's host. Everything above this decides only
    /// WHO the raiser is; nothing below it knows which dialog it is holding.
    /// </summary>
    private static void SeatOn(Seat seat, UIWindow dialog, UIWindow? raiser, string who,
                               EGuildmasterMode mode)
    {
        if (raiser == null)
        {
            Release(seat, $"{who}'s window is unreachable");
            Wait(seat, $"{seat.Label} is open and {who} raised it, but "
                       + $"GuildmasterDestinations.ModeWindow({mode}) is null, so there is no frame "
                       + "to seat it in");
            return;
        }

        var hostRect = raiser.transform as RectTransform;
        if (hostRect == null)
        {
            Release(seat, $"{who}'s window has no RectTransform");
            Wait(seat, $"'{raiser.name}' has no RectTransform, so there is no frame to seat "
                       + $"{seat.Label} in");
            return;
        }

        if (ModalFallback.PanelFor(raiser) == null || !ModalFallback.FloatIsLive(raiser))
        {
            Release(seat, $"{who}'s window stopped being a live float", releaseCancellation: false);
            Wait(seat, $"{seat.Label} is open and {who} raised it, but '{raiser.name}' "
                       + $"(ID {raiser.ID}) is not a live floated panel yet. Until it is there is no "
                       + "world frame to seat into, and the dialog is drawn exactly where the game "
                       + "put it — which is the shipped behaviour, not a regression");
            return;
        }

        var rect = dialog.transform as RectTransform;
        if (rect == null)
        {
            Wait(seat, $"{seat.Label} has no RectTransform");
            return;
        }

        // Already seated on the right window: two reference compares and no writes.
        if (seat.Parked != null && ReferenceEquals(seat.Parked, rect)
            && ReferenceEquals(seat.Host, raiser))
        {
            ApplyPose(seat, hostRect);
            return;
        }

        // Seated on the WRONG window (the player switched destinations under an open dialog) —
        // hand back first, so the home this park records is the real one and never a previous park's.
        if (seat.Parked != null)
            Release(seat, "the raising window changed while the dialog was open");

        Vector2 measured = rect.rect.size;
        if (measured.x < MinParkSizePx || measured.y < MinParkSizePx)
        {
            Wait(seat, $"{seat.Label} has not finished laying out — its rect is "
                       + $"{measured.x:F0}x{measured.y:F0} px, below the {MinParkSizePx:F0} px floor. "
                       + "A park measured now would own a zero size, which is the ModBuild 232 defect "
                       + "this class inherits the fix for");
            return;
        }

        Park(seat, dialog, raiser, hostRect, rect, measured, who);
    }

    private static void Park(Seat seat, UIWindow dialog, UIWindow raiser, RectTransform hostRect,
                             RectTransform rect, Vector2 measured, string who)
    {
        try
        {
            // MEASURED AND RECORDED FIRST — everything below changes what this rect would answer.
            seat.Home = rect.parent;
            seat.HomeIndex = rect.GetSiblingIndex();
            seat.HomeAnchorMin = rect.anchorMin;
            seat.HomeAnchorMax = rect.anchorMax;
            seat.HomePivot = rect.pivot;
            seat.HomeAnchoredPos = rect.anchoredPosition;
            seat.HomeSizeDelta = rect.sizeDelta;
            seat.HomeRotation = rect.localRotation;
            seat.HomeScale = rect.localScale;
            seat.HomeWindowName = "<none above the dialog>";
            seat.HomeWindow = null;
            for (Transform? t = seat.Home; t != null; t = t.parent)
            {
                var w = t.GetComponent<UIWindow>();
                if (w == null)
                    continue;
                seat.HomeWindowName = t.name;
                seat.HomeWindow = w;
                break;
            }

            // ignoreLayout BEFORE the move — MapTravelConfirm's discipline, so the destination's
            // layout (if it ever grows one) never rebuilds with this rect in its rectChildren.
            var le = rect.GetComponent<LayoutElement>();
            seat.AddedIgnore = null;
            seat.HomeIgnoreLayout = false;
            if (le == null)
            {
                le = rect.gameObject.AddComponent<LayoutElement>();
                seat.AddedIgnore = le;
            }
            else
            {
                seat.HomeIgnoreLayout = le.ignoreLayout;
            }
            le.ignoreLayout = true;

            seat.ParkedSize = measured;
            seat.Parked = rect;
            seat.Host = raiser;
            rect.SetParent(hostRect, worldPositionStays: false);
            CanvasConversion.TransferSeatedSubtree(rect, ModalFallback.PanelFor(raiser));
            rect.SetAsLastSibling();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = measured;      // ← the size this seat now owns (ModBuild 232)
            rect.localRotation = Quaternion.identity;
            if (ReferenceEquals(seat, EnhancementSeat)) BindHostCancellation(seat, raiser);

            // THE LAYER, read live off the destination's own root — see the class note.
            seat.ParkedToLayer = hostRect.gameObject.layer;
            seat.ParkedFromLayer = rect.gameObject.layer;
            seat.ParkedLayerCount = WriteLayer(rect, seat.ParkedToLayer);

            // Placed BEFORE the line that reports it, so the pose the line quotes is the one the
            // player is about to see ([[an-instrument-can-assert-a-cause]]).
            ApplyPose(seat, hostRect);
            LogSeated(seat, dialog, raiser, hostRect, rect, who);
            seat.WaitReported = string.Empty;
        }
        catch (System.Exception e)
        {
            VRLog.Alert(Scope, $"MAP DIALOG SEAT: seating {seat.Label} on '{raiser.name}' threw "
                               + $"({e.GetType().Name}) — unwinding to the split presentation, which "
                               + "is the status quo and is what the player already has.");
            Release(seat, "the park itself failed");
        }
    }

    /// <summary>
    /// Centre the dialog in its host, in the host's own local space, and re-assert the size the park
    /// captured. Change-gated on <see cref="OffsetEpsilonPx"/> so a settled layout costs two
    /// comparisons and no write ([[dont-win-a-write-war]]).
    ///
    /// <para>The pivot subtraction is not decoration: <c>anchoredPosition</c> is measured from the
    /// anchor, which with anchorMin=anchorMax=(0.5,0.5) is the centre of the host's rect — i.e.
    /// <c>hostRect.rect.center</c> in that same local space. The two coincide only when the host's
    /// pivot is (0.5,0.5), and assuming that would ship and then break on the first destination
    /// window whose pivot is not centred ([[verify-outcome-not-path]]).</para>
    /// </summary>
    private static void ApplyPose(Seat seat, RectTransform hostRect)
    {
        RectTransform? rect = seat.Parked;
        if (rect == null)
            return;

        // The ModBuild 232 re-assert: a stretch child whose anchors were collapsed draws at zero
        // unless the captured size is written back.
        if ((rect.sizeDelta - seat.ParkedSize).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            rect.sizeDelta = seat.ParkedSize;

        // THE DERIVATION, WRITTEN OUT BECAUSE THE ANSWER IS A CONSTANT AND A CONSTANT LOOKS LIKE AN
        // ASSUMPTION. EnchantressComposite solves `slot.center - win.rect.center` because its slot
        // is a BAND beside the ink. This seat's slot is the host's whole rect, so its centre IS
        // `hostRect.rect.center`; and with anchorMin=anchorMax=(0.5,0.5) the anchor already sits at
        // that same point in the host's local space. The offset from the anchor to where we want the
        // (0.5,0.5)-pivoted dialog is therefore hostRect.rect.center - hostRect.rect.center = zero,
        // for ANY host pivot — the pivot term cancels rather than being assumed away.
        Vector2 want = Vector2.zero;
        if ((rect.anchoredPosition - want).sqrMagnitude > OffsetEpsilonPx * OffsetEpsilonPx)
            rect.anchoredPosition = want;
    }

    /// <summary>Write <paramref name="layer"/> over the whole subtree and return how many objects
    /// changed. Stops at foreign <c>Renderer</c>/<c>Camera</c> subtrees for
    /// <c>PanelSupersample.ApplyCaptureLayer</c>'s reason: 3D content inside a uGUI window is owned
    /// by another camera that culls BY LAYER ([[canvasrenderer-is-not-a-renderer]]).</summary>
    private static int WriteLayer(Transform root, int layer)
    {
        int moved = 0;
        LayerScratch.Clear();
        LayerScratch.Add(root);
        while (LayerScratch.Count > 0)
        {
            int last = LayerScratch.Count - 1;
            Transform t = LayerScratch[last];
            LayerScratch.RemoveAt(last);
            if (t == null)
                continue;
            if (!ReferenceEquals(t, root)
                && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;   // and NOT its children either
            if (t.gameObject.layer != layer)
            {
                t.gameObject.layer = layer;
                moved++;
            }
            for (int i = t.childCount - 1; i >= 0; i--)
                LayerScratch.Add(t.GetChild(i));
        }
        LayerScratch.Clear();
        return moved;
    }

    /// <summary>Hand the dialog back to the game VERBATIM. Idempotent and never throws out.</summary>
    private static void Release(Seat seat, string why, bool releaseCancellation = true)
    {
        if (releaseCancellation) DetachHostCancellation(seat);
        if (seat.Parked == null)
        {
            seat.Host = null;
            return;
        }
        RectTransform rect = seat.Parked;
        seat.Parked = null;
        seat.Host = null;
        seat.ParkedSize = Vector2.zero;
        try
        {
            if (seat.AddedIgnore != null)
            {
                Object.Destroy(seat.AddedIgnore);
                seat.AddedIgnore = null;
            }
            else
            {
                var le = rect != null ? rect.GetComponent<LayoutElement>() : null;
                if (le != null)
                    le.ignoreLayout = seat.HomeIgnoreLayout;   // verbatim, not assumed-false
            }
            if (rect == null || seat.Home == null)
                return;
            rect.SetParent(seat.Home, worldPositionStays: false);
            CanvasConversion.TransferSeatedSubtree(rect,
                seat.HomeWindow != null ? ModalFallback.PanelFor(seat.HomeWindow) : null);
            rect.SetSiblingIndex(Mathf.Clamp(seat.HomeIndex, 0, Mathf.Max(0, seat.Home.childCount - 1)));
            rect.anchorMin = seat.HomeAnchorMin;
            rect.anchorMax = seat.HomeAnchorMax;
            rect.pivot = seat.HomePivot;
            rect.sizeDelta = seat.HomeSizeDelta;
            rect.anchoredPosition = seat.HomeAnchoredPos;
            rect.localRotation = seat.HomeRotation;
            rect.localScale = seat.HomeScale;
            // THE LAYER GOES BACK TO WHAT THE DIALOG'S NEW SIBLINGS ARE ON, READ LIVE — not to the
            // number the park recorded. While it was away the home window may have engaged or
            // released supersampling, and "the layer my siblings are on" is right in both cases.
            int homeLayer = seat.Home.gameObject.layer;
            int moved = WriteLayer(rect, homeLayer);
            VRLog.Info(Scope, $"MAP DIALOG SEAT HANDED BACK — {why}. {seat.Label} ('{rect.name}') is "
                              + $"back under '{seat.Home.name}' (inside '{seat.HomeWindowName}') at "
                              + $"sibling index {seat.HomeIndex} with its authored anchors "
                              + $"({seat.HomeAnchorMin.x:F2},{seat.HomeAnchorMin.y:F2})-"
                              + $"({seat.HomeAnchorMax.x:F2},{seat.HomeAnchorMax.y:F2}), pivot "
                              + $"({seat.HomePivot.x:F2},{seat.HomePivot.y:F2}), sizeDelta "
                              + $"({seat.HomeSizeDelta.x:F0},{seat.HomeSizeDelta.y:F0}), offset "
                              + $"({seat.HomeAnchoredPos.x:F0},{seat.HomeAnchoredPos.y:F0}), rotation "
                              + $"and scale restored VERBATIM, and {moved} object(s) put back on layer "
                              + $"{homeLayer} (read live off the home parent, not off the "
                              + $"{seat.ParkedFromLayer} this park recorded). NOTHING WAS CLAIMED AND "
                              + "NOTHING WAS HELD BACK, so there is no claim to lapse and no window "
                              + "waiting on this line.");
        }
        catch (System.Exception e)
        {
            VRLog.Alert(Scope, $"MAP DIALOG SEAT: handing {seat.Label} back threw ({e.GetType().Name}) "
                               + "— it may be left under the raising window's root. It is the GAME's "
                               + "own object, the game re-parents nothing, and the next "
                               + "ShowConfirmation re-lays it out.");
        }
        finally
        {
            seat.Home = null;
            seat.HomeWindow = null;
            seat.ParkedLayerCount = 0;
            seat.ParkedFromLayer = -1;
            seat.ParkedToLayer = -1;
        }
    }

    // =============================================================================================
    // INSTRUMENTATION — the answer line the next hardware round reads
    // =============================================================================================

    /// <summary>
    /// ONE LINE PER RAISE, and it must be readable WITHOUT a screenshot — he sent one this time and
    /// should not have to next time. It names, in this order: the dialog, the window the mod chose
    /// to host it, the window that RAISED it, whether those two are the same, the window the dialog
    /// CAME FROM, and how many dialogs are seated concurrently.
    /// </summary>
    private static void LogSeated(Seat seat, UIWindow dialog, UIWindow raiser, RectTransform hostRect,
                                  RectTransform rect, string who)
    {
        if (seat.RaiseReports >= MaxRaiseReports)
            return;
        seat.RaiseReports++;
        int concurrent = (ItemSeat.Parked != null ? 1 : 0) + (EnhancementSeat.Parked != null ? 1 : 0);
        Rect frame = hostRect.rect;
        // HW-VERIFY
        VRLog.Note(Scope, $"MAP DIALOG SEATED: {seat.Label} — DIALOG '{rect.name}' (ID {dialog.ID}); "
                          + $"HOST CHOSEN '{raiser.name}' (ID {raiser.ID}); RAISED BY {who}; "
                          + $"SAME WINDOW: YES (the host is the raiser by construction — this class "
                          + $"has no other way to pick one). IT CAME FROM '{seat.HomeWindowName}', "
                          + $"which is {(seat.HomeWindowName == raiser.name ? "THE SAME WINDOW, so "
                              + "nothing was actually wrong for this raise" : "A DIFFERENT WINDOW — "
                              + "that difference IS the reported defect, and this line is it being "
                              + "corrected")}. Seated at the host's rect centre in a "
                          + $"{frame.width:F0}x{frame.height:F0} px frame, owning size "
                          + $"{seat.ParkedSize.x:F0}x{seat.ParkedSize.y:F0} px; {seat.ParkedLayerCount} "
                          + $"object(s) moved from layer {seat.ParkedFromLayer} to {seat.ParkedToLayer}. "
                          + $"DIALOGS SEATED CONCURRENTLY: {concurrent} of 2 seats. READ THE COUNT "
                          + "LIKE THIS: 2 would mean an item dialog and an enhancement dialog stand "
                          + "together; it can only ever read 1 for two destinations, because "
                          + "UIGuildmasterHUD holds a single currentMode and exits the outgoing mode "
                          + "before entering the next (UIGuildmasterHUD.cs:441-448), so the merchant "
                          + "and the enchantress windows themselves cannot both be open. That is the "
                          + "GAME's refusal of the user's third requirement, not this mod's — nothing "
                          + "in this file is a single slot.");
    }

    /// <summary>A not-yet, latched on its own text so a standing condition prints once. A WAIT
    /// always leaves the dialog exactly where the game drew it.</summary>
    private static void Wait(Seat seat, string why)
    {
        if (why.Length == 0)
        {
            seat.WaitReported = string.Empty;
            return;
        }
        if (why == seat.WaitReported)
            return;
        seat.WaitReported = why;
        VRLog.Info(Scope, $"MAP DIALOG SEAT WAIT — {why}. Nothing has been moved and nothing will be "
                          + "until this reads differently; the dialog is drawn where the game puts "
                          + "it, which is the shipped behaviour and never nowhere.");
    }

    /// <summary>Session reset — called from <c>ModalFallback.Reset</c> beside
    /// <c>EnchantressComposite.Reset</c>. Hands both seats back first: a scene teardown that left a
    /// game singleton re-parented under a destroyed window is how a dialog goes missing for a whole
    /// session.</summary>
    internal static void Reset()
    {
        Release(ItemSeat, "the module was reset");
        Release(EnhancementSeat, "the module was reset");
        ObserveEnhancementWindow(null);
        ResetSeat(ItemSeat);
        ResetSeat(EnhancementSeat);
    }

    private static void ResetSeat(Seat seat)
    {
        seat.WaitReported = string.Empty;
        seat.RaiseReports = 0;
        seat.RequestedAs = null;
        seat.RequestedMode = null;
        seat.Closing = false;
        seat.HomeWindowName = "<unresolved>";
        // The event subscription is deliberately NOT dropped here: it is taken against the game's
        // own singleton, which outlives a module reset, and re-subscribing without unsubscribing
        // would double-fire. Dropping it would need the singleton to still be reachable at teardown,
        // which is exactly when it is not.
    }
}

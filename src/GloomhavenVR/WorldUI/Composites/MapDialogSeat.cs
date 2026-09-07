using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>A MAP-ROOM WINDOW'S DIALOG OPENS ON THAT WINDOW.</b>
///
/// <para><b>USER REPORT, 2026-09-07, item 12, verbatim:</b> <i>"Ich habe das erste mal versucht
/// wirklich einen Gegenstand zu kaufen. Und wie man in gegenstand_kaufen.jpg sehen kann ist das
/// Dialogfenster nicht in dem Händler-Fenster aufgegangen, sondern in dem Fenster der Character-UI.
/// Das soll nicht so sein - ich möchte das dieser Dialog beim Händler direkt auftaucht, da er den
/// Händler auch betrifft. Bitte gehe systematisch den Code durch - das soll für alle Dialoge von
/// Fenstern in der Map-Umgebung gelten, zB auch für die Zauberin etc."</i></para>
///
/// <para><b>HE HAS REPORTED THIS DEFECT BEFORE, ABOUT A DIFFERENT OBJECT.</b> On 2026-08-23 it was
/// the enchantress' card list: <i>"Die Kartenauswahl für die Magierin spawnt hingegen auf dem
/// Fenster der Character-UI"</i> — same window, same complaint, same word. That one was fixed for
/// that one object by <see cref="EnchantressComposite"/>. This class is the reason he now asks for
/// the CLASS instead of the instance, and it is the class.</para>
///
/// <para><b>THE MECHANISM, NAMED FROM THE LOG AND FROM THE GAME'S OWN SOURCE.</b> The dialog in the
/// photograph is <c>UI Item Confirmation Box</c>. The single-player log's own scene census prints
/// its full path at Player.log:1815:</para>
/// <code>Canvas/MainMenu Manager/CustomPartySetup/New Party display Variant/UI Item Confirmation Box</code>
/// <para>— that is the GAME's hierarchy, not this mod's. The box is a child transform of the party
/// display prefab, and <c>UIItemConfirmationBox</c> is a <c>Singleton&lt;&gt;</c>
/// (UIItemConfirmationBox.cs:10): ONE instance, shared by every surface that needs to confirm an
/// item. In the flat game that parenting is invisible, because both windows are full-screen and
/// stacked. In VR each converted window becomes a separate physical panel metres apart, and the
/// parenting suddenly IS a place.</para>
///
/// <para><b>WHY THE MOD DID NOT FLOAT IT SEPARATELY, AND WHY THAT WAS RIGHT.</b>
/// <c>ModalFallback.RendersInsideFloatedAncestor</c> (ModalFallback.7.Close.cs:1156) walks the
/// dialog's parents, finds <c>New Party display</c> in <c>Converted</c>, and refuses the float:
/// "the parent wins" (ModBuild 181/184/196). That rule is CORRECT and is not touched here — it is
/// what draws a genuine sub-view (the equipment tab, the battle-goal picker) inside its host
/// instead of beside it, and ModBuild 232 already shipped the opposite and the user photographed
/// the result and complained (<c>.planning/debug/character_ui_quest_getrennt.jpg</c>). The rule's
/// PREMISE was wrong here, not its mode: the ancestor it found is where the prefab happens to keep
/// a shared singleton, not the window the dialog is about. So this class corrects the premise — it
/// moves the dialog under the window that raised it — and the existing rule then produces the right
/// answer with no change to it and no third code path
/// ([[correct-the-premise-not-the-mode]]).</para>
///
/// <para><b>THE EVIDENCE THAT IT WAS NEVER FLOATED.</b> The log's own <c>MODAL WINDOW X AUDIT</c>
/// line states its exhaustiveness: "a window that appears in NEITHER was never converted at all".
/// <c>UI Item Confirmation Box</c> appears in <c>UIWindow SHOWN</c> once (:17879) and in ZERO
/// <c>MODAL WINDOW</c> lines. Three further lines put it inside the character panel by name:
/// <c>GRAB BAR PLATE FLOOR for 'New Party display' … the FULL-FRAME PLATE 'UI Item Confirmation
/// Box' reaches down to y=-540 px</c> (:17891), and <c>MAP ROOM deselect REFUSED … the ray was on
/// the floated window panel 'GloomhavenVR.Panel_Modal_New Party display' (widget 'UI Item
/// Confirmation Box')</c> (:18066).</para>
///
/// <para><b>THE ENUMERATION — EVERY DIALOG A MAP-ROOM WINDOW CAN RAISE.</b> Taken by walking the
/// call sites of every confirmation entry point in the decompiled game, not by name matching. The
/// five destination window classes are <see cref="GuildmasterDestinations.IsDestination"/>'s own
/// table (<c>UIShopItemWindow</c>, <c>UITempleWindow</c>, <c>UITrainerWindow</c>,
/// <c>UINewEnhancementWindow</c>, <c>UITownRecordsWindow</c>), and the map table adds the character
/// UI:</para>
/// <list type="table">
///   <item><description><b>Merchant</b> (<c>UIShopItemInventory</c>) — BUY :1108 and SELL :1079
///   raise <c>UIItemConfirmationBox</c> with <c>BoxConfirmationType.Buy</c> / <c>.Sell</c>.
///   <b>THIS IS THE REPORTED CASE.</b> Seated on the shop window.</description></item>
///   <item><description><b>Enchantress</b> (<c>UINewEnhancementWindow</c>) — BUY :520 and SELL :559
///   raise <c>UIEnhancementConfirmationBox</c>. Seated on the enhancement window.</description></item>
///   <item><description><b>Temple</b> (<c>UITempleWindow</c>) — :176 raises the SAME
///   <c>UIEnhancementConfirmationBox</c> singleton. Seated on the temple window.</description></item>
///   <item><description><b>Character UI</b> (<c>UIPartyItemInventoryDisplay</c>) — :415 and :470
///   raise <c>UIItemConfirmationBox</c> with <c>BoxConfirmationType.General</c>. Its home ALREADY
///   IS the party display, so this class leaves it exactly where it is. The defect and the correct
///   case are the same object; only the raiser differs, which is why a rule keyed on the OBJECT
///   would have broken the working half.</description></item>
///   <item><description><b>Trainer</b> and <b>Town Records / Mercenary Log</b> raise NO dialog —
///   both go to <c>UIAdventureRewardsManager.ShowRewards</c> (UITrainerWindow.cs:138,
///   UITownRecordsWindow.cs:132), which is a rewards panel and not a confirmation. Enumerated and
///   found empty, which is a finding and not an omission.</description></item>
///   <item><description>The generic <c>UIConfirmationBoxManager</c>
///   (UIPartyItemInventoryDisplay.cs:403) is <c>UIWindowID.ConfirmationBox</c> and is already owned
///   by <c>DialogSurface</c> — <c>ModalFallback.10.CatchAll.CatchAllKnownHandled</c> lists it. Not
///   taken here; two owners for one window is the DOUBLE HOST defect.</description></item>
/// </list>
///
/// <para><b>HOW THE RAISER IS RESOLVED — FROM THE GAME'S OWN ANSWER, NEVER FROM A NAME.</b> Two
/// different questions, because the game answers them in two different places:</para>
/// <list type="number">
///   <item><description><c>UIItemConfirmationBox</c> is raisable from TWO surfaces that can be open
///   at the same time (the photograph shows the merchant panel and the character panel both
///   standing), so "which mode is current" is NOT sufficient. The game already carries the answer:
///   <c>ShowConfirmation</c>'s first argument is a <c>BoxConfirmationType</c>, and it announces it
///   on its own public event <c>ConfirmationBoxRequested</c> (UIItemConfirmationBox.cs:40, fired
///   at :84 before anything else). <c>Buy</c>/<c>Sell</c> come only from the merchant,
///   <c>General</c> only from the party inventory. This class subscribes to that event and reads
///   nothing else. No Harmony patch, no game state written.</description></item>
///   <item><description><c>UIEnhancementConfirmationBox</c> has no such discriminator and needs
///   none: its only two raisers are the enchantress and the temple, both guildmaster DESTINATIONS,
///   and the game permits exactly one destination mode at a time
///   (<c>UIGuildmasterHUD.UpdateCurrentMode</c> holds a single <c>currentMode</c> and calls
///   <c>modes[currentMode].Exit()</c> before entering the next, UIGuildmasterHUD.cs:435-448).
///   Neither is raisable from the character UI. So <c>GuildmasterDestinations.ModeWindow(current)</c>
///   is exact.</description></item>
/// </list>
///
/// <para><b>CONCURRENT DIALOGS — THE USER'S THIRD REQUIREMENT, AND THE HONEST ANSWER.</b> He asked:
/// <i>"Das es unabhängige Fenster sind soll es auch so möglich sein, dass ein Dialogfenster bei der
/// Zauberin und beim Händler parallel offen sind."</i> This class does not forbid it — each dialog
/// carries its OWN <see cref="Seat"/> with its own park state, so nothing here is a single slot.
/// The mod's modal machinery does not forbid it either: <c>Converted</c>, <c>Open</c> and
/// <c>OpenWindows</c> are collections, and <c>MenuExclusivity</c>'s rule for
/// <c>MenuPlace.MapRoom</c> is <c>MenuArbitration.Parallel</c>.</para>
///
/// <para><b>IT IS REFUSED ONE LEVEL ABOVE ALL OF THAT, BY THE GAME.</b> The merchant window and the
/// enchantress window can never be open together: <c>UIGuildmasterHUD</c> drives them from a Unity
/// <c>ToggleGroup</c> through a single <c>currentMode</c> field, and <c>UpdateCurrentMode</c> exits
/// the outgoing mode unconditionally (UIGuildmasterHUD.cs:441-448) — which for the merchant is
/// <c>shopWindow.ExitShop()</c>. Two dialogs on two destinations therefore cannot arise, because
/// their two windows cannot. <b>WHAT IT WOULD COST:</b> running two guildmaster modes at once means
/// two <c>Enter()</c>ed modes writing one <c>banner.ShowMode</c>, two <c>ControllerInputArea</c>
/// stacks against one focus stack, and one <c>UINavigation</c> state machine holding one state —
/// i.e. rebuilding the guildmaster HUD's mode machinery rather than mirroring it. That is a change
/// to the game's model driven from presentation code, which the standing rules forbid twice over
/// ("never write game state from presentation code"; "mirror the real thing, never rebuild an
/// approximation"). <b>SO IT IS NOT ATTEMPTED, AND THE INSTRUMENT REPORTS THE COUNT SO THE NEXT LOG
/// SAYS SO WITHOUT A SCREENSHOT.</b> The one pairing that CAN stand today — a merchant dialog while
/// the character panel is also open — already works, because those are two windows and two seats.
/// </para>
///
/// <para><b>WHY A PARK AND NOT A FLOAT.</b> The three shapes are surveyed in
/// <see cref="EnchantressComposite"/>'s class doc and the answer is the same one, for the same
/// reason, plus one that is specific here: the dialog IS a <c>UIWindow</c>, so floating it would
/// put it BESIDE its host — the exact presentation ModBuild 232 shipped and the user rejected in a
/// photograph. He asked for it <i>"in dem Händler-Fenster"</i>. A park puts it in.</para>
///
/// <para><b>THE ModBuild 232 STRETCH-CHILD LESSON IS INHERITED, NOT RE-LEARNED.</b> The dialog is a
/// full-frame plate (the log's GRAB BAR line measures it spanning the whole 1988x1080 frame), i.e.
/// almost certainly a STRETCH child. Collapsing a stretch child's anchors to a point sets its drawn
/// size to ZERO, so the size is MEASURED BEFORE the anchor write and re-asserted every tick, exactly
/// as <c>StoryComposite</c> and <see cref="EnchantressComposite"/> do
/// ([[anchors-own-a-stretch-child-size]]).</para>
///
/// <para><b>THE LAYER TRANSFER.</b> Both ends are converted panels and either may be supersampled
/// onto a private capture layer, so the park writes the DESTINATION window root's own layer over the
/// moved subtree and the hand-back writes the HOME PARENT's own layer, both read LIVE. Re-parenting
/// without this is [[one-shared-layer-leaks]] in its purest form: a perfectly seated, perfectly
/// sized, totally invisible dialog.</para>
///
/// <para><b>A WINDOW MUST NEVER BE INVISIBLE.</b> Every precondition that is not yet true is a WAIT,
/// and a WAIT leaves the dialog exactly where the game drew it — which is today's shipped behaviour.
/// Nothing here can produce a state in which the dialog is drawn nowhere. The park is also
/// unconditionally handed back when the dialog hides, when its host stops being a live float, and on
/// module reset.</para>
///
/// <para><b>MULTIPLAYER.</b> Local presentation only: transform re-parents and layer writes on this
/// client's own scene objects, decided from this client's own <c>UIGuildmasterHUD</c> and the game's
/// own event. Nothing is read from or written to the wire and no game state is touched — no
/// <c>Show</c>, no <c>Hide</c>, no <c>Enter</c>, no <c>Exit</c>. A peer sees his OWN merchant dialog
/// on his OWN merchant window by running this same code against his own scene; the map room's
/// windows are per-client presentation and the purchase itself is committed by the game's own
/// button, on the wire, exactly as before. A FLAT (unmodded) player is unaffected: nothing here
/// reaches him, and the dialog he sees is the one the game draws. There is no per-sub-feature sync
/// setting because there is nothing to sync.</para>
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
        }

        /// <summary>Human name for the log — the dialog this seat is about.</summary>
        internal string Label { get; }

        internal RectTransform? Parked;
        internal UIWindow? Host;

        internal Transform? Home;
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
        if (box == null)
        {
            Release(EnhancementSeat, "the enhancement confirmation box singleton is gone");
            Wait(EnhancementSeat, string.Empty);
            return;
        }

        var window = box.GetComponent<UIWindow>();
        if (window == null || !StillShowing(window))
        {
            Release(EnhancementSeat, "the enhancement confirmation box is closed and has finished "
                                     + "fading out");
            Wait(EnhancementSeat, string.Empty);
            return;
        }

        // Its only two raisers are destinations, and the game permits one destination at a time, so
        // the current mode IS the raiser. Asked through GuildmasterDestinations so this class and the
        // rail's close path can never disagree about which window a mode owns.
        EGuildmasterMode mode = GuildmasterDestinations.CurrentDestinationMode();
        if (mode is not (EGuildmasterMode.Enchantress or EGuildmasterMode.Temple))
        {
            Release(EnhancementSeat, $"the enhancement confirmation box is open but the current "
                                     + $"guildmaster mode is {mode}, which is neither the enchantress "
                                     + "nor the temple — no destination claims it, so it stays where "
                                     + "the game drew it");
            Wait(EnhancementSeat, string.Empty);
            return;
        }

        UIWindow? raiser = GuildmasterDestinations.ModeWindow(mode);
        SeatOn(EnhancementSeat, window, raiser,
               mode == EGuildmasterMode.Enchantress ? "the enchantress" : "the temple", mode);
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
            Release(seat, $"{who}'s window stopped being a live float");
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
            for (Transform? t = seat.Home; t != null; t = t.parent)
            {
                var w = t.GetComponent<UIWindow>();
                if (w == null)
                    continue;
                seat.HomeWindowName = t.name;
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

            rect.SetParent(hostRect, worldPositionStays: false);
            rect.SetAsLastSibling();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = measured;      // ← the size this seat now owns (ModBuild 232)
            rect.localRotation = Quaternion.identity;
            seat.ParkedSize = measured;
            seat.Parked = rect;
            seat.Host = raiser;

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
    private static void Release(Seat seat, string why)
    {
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
        ResetSeat(ItemSeat);
        ResetSeat(EnhancementSeat);
    }

    private static void ResetSeat(Seat seat)
    {
        seat.WaitReported = string.Empty;
        seat.RaiseReports = 0;
        seat.RequestedAs = null;
        seat.HomeWindowName = "<unresolved>";
        // The event subscription is deliberately NOT dropped here: it is taken against the game's
        // own singleton, which outlives a module reset, and re-subscribing without unsubscribing
        // would double-fire. Dropping it would need the singleton to still be reachable at teardown,
        // which is exactly when it is not.
    }
}

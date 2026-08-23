using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// WHICH CLASS OF REFUSAL A ROW OF <see cref="FloatRefusalTable"/> records. There are exactly two,
/// and the point of naming them is that they are the two kinds of object for which the catch-all's
/// default — "float anything you do not recognise, because a dropped window is a silent deadlock" —
/// is the WRONG default rather than merely an ugly one.
/// </summary>
internal enum FloatRefusalClass
{
    /// <summary>
    /// A SCREEN-SPACE VEIL: a full-canvas sheet whose entire job is to cover, dim or block the FLAT
    /// picture while the game waits for something. It carries no information the player reads off
    /// its surface, so once the content it is covering has become a physical object standing in the
    /// room, the veil has nothing left to cover and is only a pane of nearly-nothing hanging in the
    /// air. Floating one produces a window that is empty by construction.
    /// </summary>
    ScreenSpaceVeil,

    /// <summary>
    /// A BARE CONTROL: a single button, toggle or strip that happens to carry a <c>UIWindow</c>
    /// because the game reuses that component as a show/hide helper. It belongs INSIDE the window
    /// whose decision it confirms, not beside it in a frame of its own with a grab bar and a close
    /// cross. This is the same observation ModBuild 188 made about a <c>UIWindow</c> sitting on an
    /// <c>ExtendedScrollRect</c>: "not a window in any sense the player would recognise".
    /// </summary>
    BareControl,
}

/// <summary>
/// One row of the refusal table: the component that IDENTIFIES the object, the class of refusal, a
/// one-sentence reason in the user's terms, what happens to the object instead, and — for a
/// CONDITIONAL row — the claim that has to be in force for the refusal to apply at all.
///
/// <para>THE IDENTITY IS A COMPONENT TYPE AND NEVER A NAME. The argument is ModBuild 194's, word for
/// word: the game localises its UI, Unity appends <c>(Clone)</c> to instantiated copies, and a
/// prefab variant can be renamed by an asset update without any code change. A component type
/// survives all three, and it also survives the <c>UIWindowID</c> being <c>None</c> — which it is
/// for both of the rows below and for most of the windows this table will ever be asked about.
/// The test is always <c>GetComponent</c> on the window's OWN GameObject: the IS-A form of the
/// question, because this project has twice shipped <c>GetComponentIn{Parent,Children}</c> where it
/// meant "IS an X" and both times caught a whole screen.</para>
/// </summary>
internal sealed class FloatRefusalRule
{
    /// <summary>The game component that identifies this object. Matched on the window's own GameObject.</summary>
    public readonly Type Component;

    /// <summary>Which of the two refusal classes this row is.</summary>
    public readonly FloatRefusalClass Class;

    /// <summary>One sentence, in the terms the user would use, for why this must not be a window.</summary>
    public readonly string Reason;

    /// <summary>What happens to the object INSTEAD of floating — printed in the refusal line, so a
    /// hardware log states where the thing went rather than only that it did not float.</summary>
    public readonly string Instead;

    /// <summary>
    /// NULL for an unconditional row. Otherwise: is some other subsystem taking responsibility for
    /// showing this object somewhere better RIGHT NOW? Asked every tick, level-triggered, of the
    /// window's own GameObject — a conditional refusal that stops being answered lapses within a
    /// tick and the object floats again. See <see cref="FloatRefusalTable"/> for why that direction
    /// is the safe one.
    /// </summary>
    public readonly Func<GameObject, bool>? HeldBy;

    /// <summary>The claimant's own words for what it did with the object — printed verbatim.
    /// Null for an unconditional row.</summary>
    public readonly Func<string>? WhyHeld;

    public FloatRefusalRule(Type component, FloatRefusalClass cls, string reason, string instead,
                            Func<GameObject, bool>? heldBy = null, Func<string>? whyHeld = null)
    {
        Component = component;
        Class = cls;
        Reason = reason;
        Instead = instead;
        HeldBy = heldBy;
        WhyHeld = whyHeld;
    }

    /// <summary>True when this row's refusal depends on somebody else's live claim.</summary>
    public bool IsConditional => HeldBy != null;
}

/// <summary>
/// THE WINDOWS THAT MUST NEVER FLOAT, AND WHY EACH ONE IS ON THE LIST.
///
/// <para><b>USER REPORT (2026-08-23), verbatim, items 2 and 3:</b> <i>"Beim Joinen von dem
/// Mitspieler ist wieder ein leeres Fenster erschienen ohne Inhalt. Siehe
/// leeres_fenster_multiplayer.jpg. … Wieso auch immer ist kurz dannach dann auch ein frei
/// schwebender Button erschienen in einem eigenen Fenster siehe
/// frei_schwebender_button_multiplayer.jpg. Das darf nicht sein."</i> The two screenshots are a
/// small empty frame carrying nothing but a close cross, and a "Quest wählen" button hanging in the
/// forest inside its own frame with a close cross.</para>
///
/// <para><b>THE CLASS BEHIND BOTH.</b> The catch-all floats ANY <c>UIWindow</c> it does not
/// recognise. That is the right default and it is not being weakened here: the DurabilityPanel rule
/// stands — a wrongly-floated window is recoverable (grab bar, X, escape chord, an attributable Warn
/// in the log), a dropped one is a silent deadlock. But the default assumes every unknown is a real
/// panel, and it is wrong for two kinds of object that are not panels at all: a SCREEN-SPACE VEIL
/// whose whole job is to cover the flat picture, and a BARE CONTROL that belongs inside the window
/// whose decision it confirms. Those two kinds are named as <see cref="FloatRefusalClass"/> members
/// rather than buried as two special cases in an <c>if</c>, because the next one will be a third
/// instance of one of them and should cost one table row.</para>
///
/// <para><b>THE TABLE IS DATA AND THE LOG LINE IS ITS OUTPUT.</b> Every row carries the sentence
/// that gets printed when it fires, so a hardware log names the object, the class, the reason and
/// where the thing went instead — the same discipline as
/// <c>ModalFallback.MapRoomPermanentReason</c> and <c>ModalFallback.WindowGroups</c>.</para>
///
/// <para><b>A REFUSED WINDOW IS LEFT IN A DEFENSIBLE STATE, WHICH IS THE ONE HARD CONSTRAINT ON THIS
/// WHOLE MECHANISM.</b> Refusing to float is a decision about PRESENTATION only. Nothing here calls
/// <c>Hide()</c>, <c>Escape()</c> or <c>SetActive</c>, writes a <c>CanvasGroup</c>, disables a
/// <c>Canvas</c> or touches game state, and nothing goes on the wire. The object keeps its ordinary
/// 2D rendering exactly as the game left it. The one way a refusal COULD have stranded something
/// invisible is the pre-convert blackout (<c>ModalFallback.11.PreConvertHide</c>), which switches a
/// just-opened window's canvases off for the single frame before the conversion takes over and would
/// otherwise sit on its 8-frame budget and hand the window back with a warning
/// (<c>MODAL PRE-CONVERT BLACKOUT: 'UI Quest Popup' was still un-floated after 9 frames</c>). Two
/// things answer that. (1) Today it cannot arise: the blackout only runs for windows on the ENROLLED
/// path (<c>OnWindow</c> returns before <c>PreConvertHide</c> unless <c>IsFallbackWindow(e.Id)</c>),
/// and both rows below have <c>UIWindowID.None</c> — which is why the ModBuild 231 log contains 51
/// blackout lines and not one of them names either window. (2) It is not left to that anyway: the
/// catch-all releases any blackout in force the instant it refuses, so a future row with an enrolled
/// id cannot re-introduce the failure. See the call site in <c>ModalFallback.10.CatchAll</c>.</para>
///
/// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> This table decides which local GameObject
/// a local uGUI subtree is drawn under on THIS client. No game state, no <c>NetProtocol</c> surface,
/// and two players may legitimately disagree about every verdict in it.</para>
/// </summary>
internal static class FloatRefusalTable
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// THE TABLE. Two rows, both defended from the decompiled source and from the ModBuild 231
    /// hardware log; candidates that could not be defended were left off deliberately (see the
    /// round's report — a row with no evidence behind it would be a guess sitting in a table that
    /// reads like a fact, which is the mistake <c>ModalFallback.WindowGroups</c> already records).
    /// </summary>
    private static readonly FloatRefusalRule[] Rules =
    {
        // -------------------------------------------------------------------------------------
        // ROW 1 — THE MULTIPLAYER LOCK OVERLAY. Unconditional: never float, ever.
        //
        // WHAT IT IS, FROM SOURCE (decompiled/GH.Runtime/UIMultiplayerLockOverlay.cs): a
        // Singleton whose ShowLock(request, textLockKey = "GUI_MULTIPLAYER_GAME_PAUSED",
        // blur = true) does exactly four things (:31-44) — record the request, enable a `blurMask`
        // Image, `window.Show()`, and `InputManager.RequestDisableInput(this, EKeyActionTag.All)` —
        // then puts the actual MESSAGE somewhere else entirely: `Singleton<HelpBox>.Instance.Show(…,
        // "GUI_MULTIPLAYER", …)` (:39-42). So the window itself carries no text. It is refcounted
        // (a dictionary of requests; HideLock only hides when the last one goes, :46-76) and it is
        // the game's way of saying "another player is deciding, wait".
        //
        // WHY THAT MAKES IT AN EMPTY WINDOW BY CONSTRUCTION, from the ModBuild 231 log rather than
        // from inference. Its identity line is
        //   WINDOW IDENTITY 'MP Lock Overlay' (ID None): path Campaign Canvas/MP Lock Overlay;
        //   rect 1920x1080; components [RectTransform, CanvasRenderer, Image, CanvasGroup, UIWindow,
        //   UIMultiplayerLockOverlay]; nearest ancestor UIWindow <none>.
        // and its fit line reads "measured 1920x1080 px at (0,0) from 1 visible graphic(s)". ONE
        // graphic, and it is the veil's own dimming Image. That is precisely
        // leeres_fenster_multiplayer.jpg: a frame with a close cross and nothing in it.
        //
        // AND FLOATING IT PUT A SHEET IN FRONT OF THE PLAYER. The same log's HIT RECT line for the
        // float says "the window's own frame is 1200x675 mm and the interactive area is 1200x675 mm"
        // at a reading distance of 1.08 m — a 1.2 m × 0.68 m laser-catching pane hanging between the
        // player and the table, which the slot packer then had to overlap onto two windows that have
        // no X ("PERMANENT WINDOWS COVERED: … 'New Party display' by 66° of its 80°"). Refusing the
        // float REMOVES that pane from the room; it does not add anything.
        //
        // WHAT IS NOT TOUCHED. The game's own lock is untouched and must be: the input disable, the
        // HelpBox text and the refcount are the game's business and the mod commits nothing here.
        new(typeof(UIMultiplayerLockOverlay), FloatRefusalClass.ScreenSpaceVeil,
            "it is the multiplayer WAIT VEIL — a full-screen dimming sheet the game puts over the "
            + "FLAT picture while another player is deciding. Its own message is not on it "
            + "(ShowLock routes the text to the HelpBox instead), so in VR it is 1.9 m of "
            + "nearly-nothing with a close cross on it: the empty window of "
            + "leeres_fenster_multiplayer.jpg",
            "nothing is done to it. It keeps its ordinary 2D rendering on 'Campaign Canvas', which "
            + "the 3D map room does not render, so it is simply not seen — and the game's own lock "
            + "(InputManager.RequestDisableInput, the HelpBox text, the request refcount) runs "
            + "untouched, exactly as it does on the flat screen"),

        // -------------------------------------------------------------------------------------
        // ROW 2 — THE MULTIPLAYER READY TOGGLE. CONDITIONAL, and the condition is the whole point.
        //
        // WHAT IT IS, FROM SOURCE (decompiled/GH.Runtime/UIReadyToggle.cs): a Singleton carrying
        // [RequireComponent(typeof(Toggle), typeof(UIWindow))] (:18-19) — so its UIWindow and its
        // UIReadyToggle are ONE GameObject by construction and this test cannot reach any other
        // window's parts. Its "IsVisible" is literally `window.IsOpen` (:138); the UIWindow is a
        // show/hide helper for a button and nothing more. The ModBuild 231 identity line measures it
        // at 307x65 px, a ROOT under 'Campaign Canvas' with "nearest ancestor UIWindow <none>".
        // It is reused for several ready-ups (quests, city events, town records, retirement, the
        // lobby's slot assignment) — Initialize() takes the labels per use (:452).
        //
        // WHY THE REFUSAL IS CONDITIONAL AND NOT ABSOLUTE. Two rules meet here and each is right on
        // its own: "a bare confirm must not float as a window" and "a window must never be
        // invisible". The second is older and it OUTRANKS the first — a confirm the player cannot
        // reach is a deadlock, and the round this row was written in already contained two of those.
        // So the refusal holds only while somebody has taken responsibility for drawing the button
        // somewhere better, and that responsibility is recorded in
        // WorldUI/MapRoom/ReadyToggleParkClaim — see its class doc for why the fact lives in a file
        // of its own rather than as a property on either side. This table is the READER. The claim
        // is a LEVEL and not a latch: it expires within a second unless the parker re-asserts it, so
        // a parker that stands down, throws or simply stops running gives the button back — ugly,
        // and reachable, which is the correct trade. The lapse is warned about loudly (see
        // Refuse below), because that warning is the only thing that will tell a future round the
        // parker broke.
        //
        // THE OBJECT IDENTITY IS CHECKED, NOT JUST THE FLAG. UIReadyToggle is a SINGLETON reused
        // across flows; a claim about one use of it must not silently refuse another. So the row
        // asks whether the claim is about THIS GameObject.
        new(typeof(UIReadyToggle), FloatRefusalClass.BareControl,
            "it is a BARE CONFIRM BUTTON that happens to carry a UIWindow (307x65 px, "
            + "[RequireComponent(typeof(Toggle), typeof(UIWindow))]) — online it is the 'Quest "
            + "wählen' confirm, i.e. the same act the offline flow puts on the quest card itself. "
            + "Floated on its own it is the button hanging in mid-air inside its own frame of "
            + "frei_schwebender_button_multiplayer.jpg",
            "it is drawn INSIDE the window whose decision it confirms — MapTravelConfirm parks it "
            + "under the quest information, on the same measured zero and the same two [WorldUI] "
            + "TravelButtonOffset dials the offline travel button already uses",
            heldBy: go => MapRoom.ReadyToggleParkClaim.Claimed
                          && ReferenceEquals(MapRoom.ReadyToggleParkClaim.ClaimedObject, go),
            whyHeld: () => MapRoom.ReadyToggleParkClaim.Why),
    };

    /// <summary>Windows whose refusal is in force RIGHT NOW — the edge state behind the one-line-per
    /// -edge logging. Never a policy input: the verdict is always recomputed.</summary>
    private static readonly HashSet<UIWindow> RefusedNow = new();

    /// <summary>Per-window-name count of lapse warnings already printed, so a parker that flaps
    /// cannot turn the safety valve into a per-second log flood. See <see cref="MaxLapseWarnings"/>.</summary>
    private static readonly Dictionary<string, int> LapseWarnings = new();

    /// <summary>How many times a single window type may announce a lapsed refusal before the line is
    /// capped. Three is enough to establish that it is happening; more is noise, and the ONE thing a
    /// future round needs from this line ("the parker stopped claiming it") is in the first one.</summary>
    private const int MaxLapseWarnings = 3;

    /// <summary>
    /// PURE VERDICT — no logging, no state. Does the table refuse to float this window this instant?
    /// Safe to call from anywhere and as often as needed (the eligibility path calls it from a
    /// recursive ancestor walk), which is exactly why the edge logging is NOT in here.
    /// </summary>
    internal static bool Refuses(UIWindow? window) => Refuses(window, out _);

    /// <summary>Pure verdict, with the row that answered.</summary>
    internal static bool Refuses(UIWindow? window, out FloatRefusalRule? rule)
    {
        rule = null;
        if (window == null)
            return false;
        GameObject go = window.gameObject;
        for (int i = 0; i < Rules.Length; i++)
        {
            FloatRefusalRule candidate = Rules[i];
            if (window.GetComponent(candidate.Component) == null)
                continue;
            // A conditional row refuses ONLY while its claim is in force for this very object.
            if (candidate.HeldBy != null && !candidate.HeldBy(go))
                return false;
            rule = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// THE EDGE-LOGGING FORM, called exactly ONCE per window per tick from the catch-all's own loop
    /// (never from the eligibility predicate, which is re-entered several times per tick). Returns
    /// the same verdict as <see cref="Refuses(UIWindow?)"/> and, in addition, prints ONE line per
    /// EDGE:
    ///
    /// <list type="bullet">
    /// <item>ENTERING a refusal: an Info line naming the object, the refusal class, the reason, what
    /// happens to it instead and — for a conditional row — the claimant's own words.</item>
    /// <item>LEAVING a refusal while the window is still open: a WARNING, because for a conditional
    /// row that means the subsystem which had taken responsibility stopped answering. The button
    /// floats again on this very tick (the safety valve), and this line is the only thing that will
    /// tell a future round the parker broke. Capped at <see cref="MaxLapseWarnings"/> per window
    /// type so a flapping claim cannot flood the log.</item>
    /// </list>
    /// </summary>
    internal static bool Refuse(UIWindow? window)
    {
        if (window == null)
            return false;
        bool refuse = Refuses(window, out FloatRefusalRule? rule);
        bool wasRefused = RefusedNow.Contains(window);

        if (refuse && !wasRefused)
        {
            RefusedNow.Add(window);
            VRLog.Info(Scope, $"FLOAT REFUSED: '{window.name}' (ID {window.ID}) is a "
                              + $"{rule!.Class} and is NOT floated — {rule.Reason}. INSTEAD: "
                              + $"{rule.Instead}."
                              + (rule.IsConditional
                                  ? " THE REFUSAL IS CONDITIONAL and holds only while that claim "
                                    + $"stands. The claimant's own words: \"{rule.WhyHeld!()}\". If "
                                    + "the claim lapses this window floats again within a tick and "
                                    + "says so — a bare control in a frame of its own is ugly, an "
                                    + "unreachable confirm is a deadlock, and the second rule wins."
                                  : " THE REFUSAL IS UNCONDITIONAL: there is no state in which "
                                    + "floating this object is the right answer.")
                              + " Nothing was written to the game: no Hide, no Escape, no CanvasGroup "
                              + "and no Canvas — this is a presentation decision and it is local to "
                              + "this client.");
            return true;
        }

        if (!refuse && wasRefused)
        {
            RefusedNow.Remove(window);
            // A window the game has closed did not "lapse" — it simply went away. Only a refusal
            // that stopped applying to a window that is STILL UP is news.
            if (window.IsOpen)
                WarnLapse(window);
        }
        return refuse;
    }

    private static void WarnLapse(UIWindow window)
    {
        string name = window.name;
        LapseWarnings.TryGetValue(name, out int printed);
        if (printed >= MaxLapseWarnings)
            return;
        LapseWarnings[name] = printed + 1;
        bool last = printed + 1 == MaxLapseWarnings;
        VRLog.Warn(Scope, $"FLOAT REFUSAL LAPSED: '{name}' (ID {window.ID}) is still open, but the "
                          + "subsystem that had taken responsibility for drawing it somewhere better "
                          + $"STOPPED CLAIMING IT (last words: \"{ReadyToggleParkClaimWhy()}\"). The "
                          + "refusal is therefore off and this window floats on its own again from "
                          + "this tick — that is the SAFETY VALVE, not a regression: a bare control "
                          + "in a frame of its own is ugly, a confirm the player cannot reach is a "
                          + "deadlock, and the second rule outranks the first. IF YOU ARE READING "
                          + "THIS IN A HARDWARE LOG, THE PARKER IS THE BUG: it either threw, stood "
                          + "down, or stopped running while the control was up. Nothing was written "
                          + "to the game here."
                          + (last
                              ? $" This is the {MaxLapseWarnings}. and LAST time this line is printed "
                                + "for this window type — a claim that flaps would otherwise flood "
                                + "the log once per lapse."
                              : string.Empty));
    }

    /// <summary>The claim's own last words, for the lapse line. Safe when nothing was ever claimed.</summary>
    private static string ReadyToggleParkClaimWhy() => MapRoom.ReadyToggleParkClaim.Why;

    /// <summary>
    /// Drop every trace of this window (the game closed it). Called from the catch-all's own
    /// transition observer, so a window that closes while refused and re-opens later starts from a
    /// clean edge — the ready toggle is a SINGLETON and comes back as the same instance, which
    /// without this would swallow the next refusal's Info line and fake a lapse warning on the
    /// re-open.
    /// </summary>
    internal static void Forget(UIWindow? window)
    {
        // ReferenceEquals, NOT Unity's `!= null`: this is also the prune path for a window that was
        // DESTROYED with its scene while refused, and a destroyed UnityEngine.Object compares equal
        // to null through the overload while still being a live dictionary key. Using the overload
        // here would leave one dead reference per scene load in the set forever.
        if (!ReferenceEquals(window, null))
            RefusedNow.Remove(window!);
    }

    /// <summary>Teardown (module detach) — mirrors the catch-all's other resets.</summary>
    internal static void Reset()
    {
        RefusedNow.Clear();
        LapseWarnings.Clear();
    }

    /// <summary>
    /// The refusal, phrased for a diagnostic that has to explain why a window is not floating.
    /// Returns null when the table has nothing to say about this window.
    /// </summary>
    internal static string? Describe(UIWindow? window)
    {
        if (window == null)
            return null;
        GameObject go = window.gameObject;
        for (int i = 0; i < Rules.Length; i++)
        {
            FloatRefusalRule rule = Rules[i];
            if (window.GetComponent(rule.Component) == null)
                continue;
            bool held = rule.HeldBy == null || rule.HeldBy(go);
            if (!held)
                return $"the REFUSAL TABLE has a {rule.Class} row for this window "
                       + $"({rule.Component.Name}), but its claim is NOT in force right now, so the "
                       + "refusal does not apply and the window floats normally — "
                       + $"last words from the claimant: \"{rule.WhyHeld!()}\"";
            return $"the REFUSAL TABLE refuses it as a {rule.Class} ({rule.Component.Name} on its "
                   + $"own GameObject): {rule.Reason}. INSTEAD: {rule.Instead}";
        }
        return null;
    }
}

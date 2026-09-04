using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// P6: a short TAP of the NON-dominant lower face button (A/X) opens or closes the
/// game's own PAUSE menu (<c>ESCMenu</c>, the ESC / pause screen) — the user wants it to
/// appear "just like the main menu, a screen in front of you", closable with another tap.
/// Options remain reachable from there via the pause menu's own Options button.
///
/// Nothing here draws the screen: opening the game window is enough. The pause menu's
/// <c>UIWindowID.ESCMenu</c> is in <see cref="ModalFallback"/>'s FallbackIds set, so the
/// moment it shows the mod asserts <c>VRMode.ModalUI</c> and floats it in front of the
/// player in VR (window-style) or on the full flat screen — no FlatScreen/ModalFallback
/// change needed. The long-hold escape chord can also close it.
///
/// The tap edge comes from <see cref="NonDominantHold.ShortTapThisFrame"/> (a
/// sub-threshold, unconsumed release); the settings short-hold and the manual/escape
/// long-holds own the at/above-threshold band, so the tap never collides with them.
///
/// State is kept in sync with the REAL window (<c>ESCMenu.IsOpen</c>): if the player
/// closes it another way (its own Resume/Back, the escape chord, a scene change), the
/// latch re-syncs so the next tap opens it again rather than trying to close an
/// already-closed window.
/// </summary>
internal sealed class OptionsToggle
{
    /// <summary>Intended/last-observed open state of the pause menu, mirrored from <c>ESCMenu.IsOpen</c>.</summary>
    private bool _open;

    /// <summary>
    /// PRESS-IDENTITY GATE (P6 reopen fix, replaces the old <c>_armed</c> release latch).
    /// The id of the physical press that must NOT toggle because it already served the
    /// menu's most-recent open/close. The old latch keyed re-arming off
    /// <see cref="NonDominantHold.ButtonIsUp"/>, which is TRUE on the very release frame
    /// that produces the tap — so it re-armed and toggled on the same edge, and could not
    /// tell the press that CLOSED the menu apart from the press that should RE-OPEN it.
    /// The failure mode: in a scenario the game treats the controllers as a GAMEPAD, so
    /// the X press is consumed by the game's own gamepad-escape and closes ESCMenu itself
    /// (there is never an "OPTIONS TAP CLOSED" line — only the "closed externally"
    /// re-sync); the mod then saw that same press's release with the menu already closed
    /// and the reopen edge was lost/ambiguous. Keying on the press IDENTITY instead makes
    /// one physical press serve exactly one intent: the close/open press is "spent", its
    /// release is ignored, and the NEXT independent press (a new <see cref="NonDominantHold.PressId"/>)
    /// always toggles — open→close(any path)→open(next press)→… indefinitely.
    /// Sentinel <c>-1</c> matches no real press (ids start at 1).
    /// </summary>
    private int _spentPressId = -1;

    /// <summary>
    /// Cached ESCMenu (REOPEN fix). Root cause found in the press-diagnostic log: after the mod
    /// CLOSES the menu via <c>menu.Hide()</c>, its GameObject is DEACTIVATED and
    /// <c>Singleton&lt;ESCMenu&gt;.IsInitialized</c> flips FALSE (the game's own close/gamepad-escape
    /// leaves it initialized — which is why external closes reopened fine, but a mod X-close did
    /// not). With the singleton null, <see cref="Tick"/> early-returned and no X could ever reopen
    /// it until a scenario reload re-created the singleton — exactly the reported symptom. A
    /// deactivated (not destroyed) MonoBehaviour is still a live C# reference, so we cache it the
    /// first time the singleton is valid and keep using it: reopen re-activates its GameObject and
    /// calls Show().
    ///
    /// <para>STALENESS — ModBuild 289's defect, and why this cache is no longer trusted on its
    /// own. The paragraph above claimed the cache "self-invalidates on scene unload (the object
    /// is destroyed → Unity-null)". That premise is FALSE for one of the two ESC menus.
    /// <c>UIMapEscMenu</c> survives a scene load: the ModBuild 289 log has the campaign map's
    /// <c>UI Map Esc Menu</c> still present, as a scene ROOT with <c>active=True</c>, in the
    /// MenuLogo "SCENE SWEEP (at Awake)" census taken during the MAIN MENU scene's Awake phase,
    /// after the map had been left. It is never destroyed, so a Unity fake-null test never clears
    /// it, so the mod kept calling <c>Show()</c> on the MAP's pause menu inside a SCENARIO — where
    /// its <c>CheckMultiplayerButton</c> dereferences a <c>MapChoreographer</c> that does not
    /// exist. See <see cref="ResolveMenu"/>, which no longer lets this field answer on its own.</para>
    /// </summary>
    private ESCMenu? _menu;

    /// <summary>
    /// The handle of the scene that was ACTIVE when <see cref="_menu"/> was resolved. A cached
    /// menu that outlived a scene change is not usable even though Unity still says it is alive,
    /// so the cache may only answer while this still matches the live active scene. Handle 0 is
    /// no scene, which matches nothing.
    /// </summary>
    private int _menuSceneHandle;

    /// <summary>Where <see cref="_menu"/> came from, verbatim in every open/close log line, so a
    /// future log says WHICH menu was shown and HOW it was chosen without needing a crash to
    /// reveal it (ModBuild 289's log could only name the map menu because the game threw).</summary>
    private string _menuSource = "unresolved";

    /// <summary>
    /// BELT reconcile state (Issue 1). After the mod acts on a tap it records the INTENDED
    /// open state (<see cref="_intendedOpen"/>) and the press it acted on
    /// (<see cref="_intendPressId"/>), and arms <see cref="_reconcilePending"/> for the ONE
    /// immediately-following tick. Now that the game's own controller ESC-menu show/toggle is
    /// suppressed (<see cref="Patches.EscMenuInputBlock"/>), a second-actor flip should never
    /// happen — but if a residual one does, it lands within a frame or two of the press. So the
    /// reconcile re-checks the live window state against the intent exactly once, keyed to the
    /// SAME still-active press (no new press since). A legitimate laser "closed externally" comes
    /// far later (human reaction time), long after this one-shot window has closed, so it is never
    /// fought — the existing external-resync below owns that case.
    /// </summary>
    private bool _reconcilePending;
    private bool _intendedOpen;
    private int _intendPressId = -1;

    /// <summary>
    /// OPTIONS KEY instrument state (user ruling 2026-09-03: the options key opens and closes
    /// the pause/options menu and NEVER any other window). Every tap takes a census of the game's
    /// OPEN windows outside the key's domain before and after the action and prints the verdict —
    /// change-gated on the picture (menu edge + before-list + verdict) and capped per session, so
    /// a hammered button cannot flood the log. The press and withheld counters ride on the line so
    /// a single printed line is never mistaken for a single press ([[held-instrument-reads-as-dead]]).
    /// </summary>
    private const int OptionsKeyLineCap = 10;

    /// <summary>
    /// A CAP MUST GO QUIET, NOT GO SILENT (user report 2026-09-04). The rapid-press burst that
    /// produced "am linken Rand des auges so ein Rand der dem Kopf folgt" carried 47 taps and NOT
    /// ONE <c>OPTIONS KEY</c> line, because <see cref="OptionsKeyLineCap"/> had been spent an hour
    /// earlier on ordinary presses. The instrument was at its most useful exactly where it had
    /// nothing to say — the same defect as [[a-truncated-list-is-not-absence]], one layer down: a
    /// reader who greps this token and finds nothing concludes the player never pressed the key.
    ///
    /// <para>Past the cap the per-press line stops and a SUMMARY takes over, one line per this many
    /// withheld presses. It carries the counts rather than the pictures, so the log still answers
    /// "how often, and did the ruling hold every time" at a fixed cost per press.</para>
    /// </summary>
    private const int OptionsKeySummaryEvery = 25;

    /// <summary>
    /// A VERDICT THAT IS NOT "TOUCHED NOTHING ELSE" IS NOT CAPPED AT TEN. That verdict is the
    /// falsifier for the 2026-09-03 ruling, and withholding it is withholding the one line the
    /// ruling is checked with; the ordinary "nothing happened" lines are what the cap is for. It is
    /// still bounded — by this larger cap, and before that by the change gate, which prints a
    /// repeated identical violation exactly once.
    /// </summary>
    private const int OptionsKeyViolationLineCap = 40;

    private int _optionsKeyPresses;
    private int _optionsKeyLines;
    private int _optionsKeyViolationLines;
    private int _optionsKeyWithheld;
    /// <summary>Presses folded into the SUMMARY since the last one was printed, and how many of
    /// them read TOUCHED NOTHING ELSE. The difference is the only number that matters.</summary>
    private int _optionsKeySinceSummary;
    private int _optionsKeyCleanSinceSummary;
    private int _optionsKeySummaries;
    /// <summary>The last verdict folded into the SUMMARY that was NOT "TOUCHED NOTHING ELSE", so a
    /// violation past both caps still reaches the log by name instead of only as a count.</summary>
    private string _optionsKeyLastDirtyVerdict = string.Empty;
    private string _optionsKeyLastSig = string.Empty;
    private readonly List<UIWindow> _censusBefore = new(16);
    private readonly List<UIWindow> _censusAfter = new(16);

    public void Tick()
    {
        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
            return;

        // THE LEFT RIM (ModBuild 423): the settle pump for the whole-scene eye census, placed ABOVE
        // the ResolveMenu early return because a settled read must still happen after a tap chain
        // that ended with the pause menu gone. Two float compares and a return when nothing is
        // armed; the sweep itself only ever runs on a tap edge or a due settle, never per frame.
        EyeReachCensus.Tick();

        // WHICH MENU. Resolve the ESC menu that belongs to the CURRENT context rather than
        // whatever was cached first — see ResolveMenu. Cheap every frame (a static field read and
        // one reference compare); the expensive scene scan inside it still runs on a tap only.
        ESCMenu? menu = ResolveMenu();
        if (menu == null)
        {
            _open = false; // no pause menu (wrong scene / destroyed) — drop the state
            _spentPressId = NonDominantHold.PressId; // spend any in-flight press across the boundary
            return;
        }

        // PARENT open-state (cheap, every frame). The re-sync latch tracks the ESC menu
        // itself: opening a sub-menu leaves the parent open behind it, so this is the
        // meaningful "external change" edge. The TOGGLE DECISION below reads the full
        // live picture (parent + sub-menus) freshly on the tap, so it can never desync.
        bool escOpen = menu.IsOpen;

        // BELT reconcile (Issue 1): the tick immediately AFTER the mod acted on a tap, re-assert
        // the intent ONCE if the live window state disagrees and no new independent press has
        // happened. One-shot + same-press keyed, so it catches a residual second-actor flip
        // (which races within a frame or two) without ever fighting a much-later laser external
        // close (handled by the external-resync just below).
        if (_reconcilePending)
        {
            _reconcilePending = false; // one-shot: only the tick right after the action
            if (NonDominantHold.PressId == _intendPressId)
            {
                OpenState live = Probe(menu);
                if (live.Any != _intendedOpen)
                {
                    string reassertWhy = string.Empty;
                    bool reasserted = true;
                    if (_intendedOpen)
                        reasserted = OpenMenu(menu, out reassertWhy);
                    else
                        CloseAll(menu, live);
                    _open = _intendedOpen && reasserted; // record what happened, not what was asked for
                    _spentPressId = _intendPressId; // press stays spent; its release must not re-toggle
                    escOpen = menu.IsOpen;          // re-read so the external-resync below stays consistent
                    VRLog.Info("WorldUI", $"[OptionsToggle] reconcile: live state ({live.Any}) disagreed with " +
                                          $"intent ({_intendedOpen}) on the same press — re-asserted " +
                                          $"{(_intendedOpen ? "OPEN" : "CLOSED")} on {menu.GetType().Name}" +
                                          $"{(reasserted ? "." : $" but the window STILL reports closed: {reassertWhy}.")}");
                }
            }
        }

        // Re-sync an externally opened/closed menu (the game's own gamepad-escape on the
        // X button, the menu's Resume/Back, the escape chord, a laser click, a scene
        // change). The press that COINCIDED with this external change is spent: its
        // release must not toggle, so the game closing the menu on a press cannot bounce
        // straight back open, and — crucially — the NEXT independent press reopens.
        if (_open != escOpen)
        {
            _open = escOpen;
            _spentPressId = NonDominantHold.PressId;
            VRLog.Info("WorldUI", escOpen
                ? "OptionsToggle: pause menu opened externally — X-tap re-synced (now open; this press is spent, " +
                  "the next independent press will close it)."
                : "OptionsToggle: pause menu closed externally — X-tap re-synced (now closed; this press is spent, " +
                  "the next independent press will open it).");
        }

        if (!NonDominantHold.ShortTapThisFrame)
            return;

        // THE LEFT RIM (ModBuild 423). Every short-tap edge is reported to the eye census BEFORE the
        // spent-press guard below, because a spent press is still a press the player made, and the
        // reproducer — "Wenn man schnell hintereinander die Optionstaste drückt" — is a statement
        // about his thumb, not about which taps this class chose to act on. The census decides for
        // itself whether this tap fires a run; it is capped per session and never sweeps per frame.
        EyeReachCensus.NoteOptionsTap();

        // Consume the press so no later consumer this frame acts on the same tap.
        NonDominantHold.Consumed = true;

        // Distinct-edge guard: the press that just opened/closed the menu externally (or
        // that the mod itself already toggled on) is spent — only a genuinely fresh,
        // independent press may toggle. This is what guarantees a reliable REOPEN. Kept
        // EXACTLY as the core reopen fix: one physical press serves exactly one intent.
        if (NonDominantHold.PressId == _spentPressId)
        {
            VRLog.Info("WorldUI", "OPTIONS TAP: ignored — this press already served the menu's open/close " +
                                  "(spent); the next independent X press toggles.");
            return;
        }

        // LIVE open-state, read from the ACTUAL game windows on THIS tap — never a cached
        // `_open` bool that a stray external Show/Hide (e.g. ESCMenu.OnControllerAreaFocused
        // re-showing the parent) can desync. A sub-menu that is focused/open while the ESC
        // menu is closed still forces a CLOSE, so X while any sub-menu shows always closes
        // everything rather than "reopening". These probes run ONLY on the tap frame (never
        // per-frame); the owners are reused by the close branch below.
        //
        // COST. The old comment here asserted these probes were "cheap at human tap cadence".
        // ModBuild 289 falsified that: [Perf] SPIKE frames 8957 / 13406 / 14908 each attributed
        // 10.5-11.2 ms of an 11.11 ms budget to OptionsToggle — one dropped frame per tap, and on
        // the throwing taps that whole budget bought nothing. So the tap frame is now MEASURED
        // rather than asserted (see LogTapCost), and the compendium probe no longer sweeps the
        // scene (see FindOpenCompendiumWindow).
        long tProbe0 = Stopwatch.GetTimestamp();
        OpenState st = Probe(menu);
        long tProbe1 = Stopwatch.GetTimestamp();
        bool actuallyOpen = st.Any;
        // OPTIONS KEY census, BEFORE the action: every game window open right now that is not the
        // pause menu, one of its sub-windows or the mod's settings window. Same registry walk as
        // the compendium probe (no scene sweep), tap-frequency only.
        CensusOthers(_censusBefore);

        string menuName = menu.GetType().Name;
        VRLog.Info("WorldUI", $"[OptionsToggle] X tap on {menuName} (source: {_menuSource}): " +
                              $"actuallyOpen={actuallyOpen} (esc={st.Esc} opt={st.Opt} " +
                              $"mp={st.Mp} comp={st.Comp}) -> {(actuallyOpen ? "CLOSE" : "OPEN")}");

        int pressId = NonDominantHold.PressId;
        if (actuallyOpen)
        {
            CloseAll(menu, st);
            _open = false;
            _spentPressId = pressId; // this press did the close — it must not reopen
            ArmReconcile(intendedOpen: false, pressId);
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            VRLog.Info("WorldUI", $"OPTIONS TAP: {menuName} + all sub-menus CLOSED (X tap) — back to the game.");
        }
        else
        {
            // VERIFY THE OUTCOME, do not assert it. The old success line said "pause menu OPENED"
            // unconditionally and printed activeInHierarchy, which is not the same question:
            // UIWindow.Show() can return with the window still Hidden (it early-returns when the
            // window is inactive or disabled, and before ModBuild 290 a throwing onTransitionBegin
            // listener abandoned the transition before 'm_CurrentVisualState = state'). So ask the
            // window afterwards and, when the answer is no, say WHY.
            bool opened = OpenMenu(menu, out string why);
            if (!opened && TryFallbackMenu(menu, out ESCMenu other, out why))
            {
                opened = true;
                menuName = other.GetType().Name;
            }
            // The latch records what HAPPENED, not what was asked for. Recording `true` after a
            // failed open used to produce a phantom "pause menu closed externally" line on the very
            // next tick and left the user alternating between a dead open and a dead close; with
            // the truth in here, a hammered button retries the OPEN every time, which is what the
            // player is asking for.
            _open = opened;
            _spentPressId = pressId; // this press did the open — it must not re-close
            ArmReconcile(intendedOpen: true, pressId);
            NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
            if (opened)
            {
                // Controls lesson: the "open the menu" step. Reported on a VERIFIED open only —
                // the branch it sits in is the one that already asked the window whether it is
                // actually up. A step that ticked on a tap which did nothing would teach the
                // player something false about their own controller.
                Compat.ControlsProgress.Notify(Compat.ControlAction.OpenMenu);
                VRLog.Info("WorldUI", $"OPTIONS TAP: {menuName} OPENED (X tap) — floats in front of the " +
                                      "player in VR. VERIFIED: the window reports IsOpen after Show().");
            }
            else
                VRLog.Error("WorldUI", $"OPTIONS TAP: {menuName} DID NOT OPEN (X tap) — Show() returned and " +
                                       $"the window still reports IsOpen=false. REASON: {why}. This violates " +
                                       "the standing user ruling that the options menu must ALWAYS be openable; " +
                                       "treat this line as the lead, not the tap that produced it.");
        }

        // OPTIONS KEY census, AFTER the action. UIWindow.Hide/Show flip IsOpen synchronously, so
        // anything the mod's CloseAll or the game's own Show/Hide cascade did to another window on
        // this call stack is visible right here, on the tap frame.
        CensusOthers(_censusAfter);
        LogOptionsKey(menuName, actuallyOpen);
        LogTapCost(tProbe0, tProbe1, Stopwatch.GetTimestamp(), st, actuallyOpen);
    }

    /// <summary>
    /// Every registered, OPEN game window outside the options key's domain
    /// (<see cref="MenuWindowFamily.IsOptionsKeyDomain"/>). Registry walk, not a scene sweep.
    /// </summary>
    private static void CensusOthers(List<UIWindow> into)
    {
        into.Clear();
        foreach (UIWindow w in UIWindow.GetWindows())
        {
            if (w != null && w.IsOpen && !MenuWindowFamily.IsOptionsKeyDomain(w))
                into.Add(w);
        }
    }

    /// <summary>
    /// The verdict line for the 2026-09-03 ruling. HID = open before the tap, closed after it;
    /// OPENED = the reverse. Either one is a window the options key touched that it must not.
    /// Change-gated on the picture and capped at <see cref="OptionsKeyLineCap"/> per session; the
    /// counters on the line say how many presses the printed lines stand for.
    /// </summary>
    private void LogOptionsKey(string menuName, bool closed)
    {
        _optionsKeyPresses++;
        var sb = new StringBuilder(96);
        int hid = 0;
        for (int i = 0; i < _censusBefore.Count; i++)
        {
            UIWindow w = _censusBefore[i];
            if (w == null || w.IsOpen)
                continue;
            hid++;
            if (hid <= 4)
                sb.Append(hid == 1 ? "'" : ", '").Append(w.name).Append("' (ID ").Append(w.ID).Append(')');
        }
        string hidNames = sb.ToString();
        sb.Clear();
        int opened = 0;
        for (int i = 0; i < _censusAfter.Count; i++)
        {
            UIWindow w = _censusAfter[i];
            if (w == null || _censusBefore.Contains(w))
                continue;
            opened++;
            if (opened <= 4)
                sb.Append(opened == 1 ? "'" : ", '").Append(w.name).Append("' (ID ").Append(w.ID).Append(')');
        }
        string openedNames = sb.ToString();
        sb.Clear();
        int before = _censusBefore.Count;
        for (int i = 0; i < before && i < 4; i++)
            sb.Append(i == 0 ? "'" : ", '").Append(_censusBefore[i].name).Append('\'');
        string beforeNames = sb.ToString();

        string verdict = hid == 0 && opened == 0
            ? "TOUCHED NOTHING ELSE"
            : (hid > 0 ? $"HID: {hidNames}{(hid > 4 ? $" (+{hid - 4} more)" : string.Empty)}" : string.Empty)
              + (hid > 0 && opened > 0 ? "; " : string.Empty)
              + (opened > 0 ? $"OPENED: {openedNames}{(opened > 4 ? $" (+{opened - 4} more)" : string.Empty)}" : string.Empty);
        string sig = $"{menuName}|{closed}|{before}|{beforeNames}|{verdict}";
        // THE CAP IS PER VERDICT CLASS. A press that touched nothing else is bookkeeping and spends
        // the ordinary budget; a press that HID or OPENED something is the ruling being broken and
        // spends the violation budget, which is four times larger. Before 2026-09-04 both shared one
        // ten-line budget, so a session's ordinary presses could spend the whole allowance and leave
        // a later violation unprintable.
        bool clean = hid == 0 && opened == 0;
        bool capped = clean
            ? _optionsKeyLines >= OptionsKeyLineCap
            : _optionsKeyViolationLines >= OptionsKeyViolationLineCap;
        if (sig == _optionsKeyLastSig || capped)
        {
            _optionsKeyWithheld++;
            if (capped)
                FoldIntoOptionsKeySummary(clean, verdict);
            return;
        }
        _optionsKeyLastSig = sig;
        if (clean)
            _optionsKeyLines++;
        else
            _optionsKeyViolationLines++;
        int sinceLast = _optionsKeyWithheld;
        _optionsKeyWithheld = 0;
        // HW-VERIFY: the falsifier for the 2026-09-03 ruling ("only the pause/options menu opens and
        // closes on the options key — NEVER other windows"). A HID verdict naming a window the player
        // had to act on is the ModBuild 407 story deadlock again; TOUCHED NOTHING ELSE on every press
        // is the ruling holding.
        VRLog.Note("WorldUI",
            $"OPTIONS KEY press #{_optionsKeyPresses}: {menuName} {(closed ? "CLOSED" : "OPENED")}. " +
            $"OTHER open windows BEFORE: {before}{(before > 0 ? $" [{beforeNames}{(before > 4 ? $" +{before - 4} more" : string.Empty)}]" : string.Empty)}, " +
            $"AFTER: {_censusAfter.Count}. VERDICT: {verdict}. " +
            "Counted on the game's own UIWindow registry (IsOpen, measured on the tap's call stack, after " +
            "the mod's close/open and the game's cascade both ran); the pause menu, its sub-windows and " +
            "the mod's settings window are the key's domain and are not counted. " +
            // The budget named here is the one this verdict class actually spends (2026-09-04):
            // a TOUCHED NOTHING ELSE line spends the ordinary cap, a HID/OPENED line the violation
            // cap. Printing the other class's counter would misstate how much headroom is left for
            // the line that matters.
            $"Line {(clean ? _optionsKeyLines : _optionsKeyViolationLines)} of " +
            $"{(clean ? OptionsKeyLineCap : OptionsKeyViolationLineCap)} this session; {sinceLast} press(es) since " +
            "the last line showed the same picture and were not printed.");
    }

    /// <summary>
    /// What a spent budget prints INSTEAD of nothing. Accumulates the presses the caps withheld and
    /// emits one line per <see cref="OptionsKeySummaryEvery"/> of them.
    ///
    /// <para>WHY THIS EXISTS: the 2026-09-04 report ("Wenn man schnell hintereinander die
    /// Optionstaste drückt erscheint am linken Rand des auges so ein Rand der dem Kopf folgt statt
    /// dem Optionsmenu") was investigated from a log in which that burst — 47 taps — produced NOT
    /// ONE <c>OPTIONS KEY</c> line, because the ten-line cap had been spent long before it. The cap
    /// was right to stop the flood and wrong to stop the evidence.</para>
    ///
    /// <para>The summary carries COUNTS, not pictures, so its cost per press is fixed. The one field
    /// that decides the ruling survives verbatim: how many of the folded presses read TOUCHED
    /// NOTHING ELSE, and the last verdict that did not.</para>
    /// </summary>
    private void FoldIntoOptionsKeySummary(bool clean, string verdict)
    {
        _optionsKeySinceSummary++;
        if (clean)
            _optionsKeyCleanSinceSummary++;
        else
            _optionsKeyLastDirtyVerdict = verdict;
        if (_optionsKeySinceSummary < OptionsKeySummaryEvery)
            return;

        int folded = _optionsKeySinceSummary;
        int cleanFolded = _optionsKeyCleanSinceSummary;
        string dirty = _optionsKeyLastDirtyVerdict;
        _optionsKeySinceSummary = 0;
        _optionsKeyCleanSinceSummary = 0;
        _optionsKeyLastDirtyVerdict = string.Empty;
        _optionsKeySummaries++;

        // HW-VERIFY: past the per-press cap this is the ONLY line that says the options key was
        // pressed at all, and the only one that says whether the 2026-09-03 ruling held while it
        // was. Bounded by construction: one line per OptionsKeySummaryEvery withheld presses.
        VRLog.Note("WorldUI",
            $"OPTIONS KEY SUMMARY #{_optionsKeySummaries}: {folded} further press(es) folded " +
            $"(press #{_optionsKeyPresses} is the latest), of which {cleanFolded} read TOUCHED NOTHING " +
            $"ELSE and {folded - cleanFolded} did not. " +
            (dirty.Length > 0
                ? $"The last verdict that was NOT clean: {dirty}. That is the 2026-09-03 ruling broken, "
                  + "and the per-press lines above it name the picture."
                : "Every folded press left the other open windows exactly as it found them, which is "
                  + "the 2026-09-03 ruling holding.") +
            " The per-press line is capped per verdict class; this summary is what a spent budget " +
            "prints instead of going silent, so a burst can never read as no presses at all.");
    }

    /// <summary>Record the intent the mod just asserted so the next tick can belt-reconcile it once.</summary>
    private void ArmReconcile(bool intendedOpen, int pressId)
    {
        _intendedOpen = intendedOpen;
        _intendPressId = pressId;
        _reconcilePending = true;
    }

    /// <summary>
    /// WHICH ESC MENU — the ModBuild 290 fix for "the X button did nothing, twenty taps in a row".
    ///
    /// <para>THE SHAPE OF THE BUG. The game has exactly two ESC menus, <c>UIMapEscMenu</c> (the
    /// campaign map's) and <c>UIScenarioEscMenu</c> (the dungeon's), and they are BOTH
    /// <c>ESCMenu : Singleton&lt;ESCMenu&gt;</c> — one static slot, last <c>Awake</c> wins. The
    /// map's menu additionally survives scene loads. ModBuild 289: scenario #1 opened the scenario
    /// menu correctly (the map menu did not exist yet); the player then visited the map, where
    /// <see cref="_menu"/> was filled with <c>UIMapEscMenu</c>; entering scenario #2 destroyed
    /// nothing the cache pointed at, so every subsequent tap called <c>Show()</c> on the MAP's
    /// pause menu inside a dungeon, where <c>UIMapEscMenu.CheckMultiplayerButton</c> reads
    /// <c>Singleton&lt;MapChoreographer&gt;.Instance.PartyAtHQ</c> on a null instance.</para>
    ///
    /// <para>THE POLICY, in priority order:</para>
    /// <list type="number">
    /// <item><description>THE GAME'S OWN REGISTRATION WINS. <c>Singleton&lt;ESCMenu&gt;.Instance</c>
    /// is whichever menu awoke last, which inside a scenario is always the scenario menu and on
    /// the map is always the map menu. This alone answers the ModBuild 289 case: the singleton was
    /// never wrong — only the mod's cache was. It is a static field read, so it is free every
    /// frame.</description></item>
    /// <item><description>THE CACHE ANSWERS ONLY WITHIN ITS OWN SCENE. It exists for one reason
    /// (a menu the mod hid is deactivated and can drop out of the singleton, which is what used to
    /// break REOPEN), and that reason never spans a scene change. A cached menu that outlived the
    /// scene it was resolved in is dropped, which is precisely the invalidation the old
    /// "self-invalidates on scene unload" premise assumed Unity would do for free and does
    /// not.</description></item>
    /// <item><description>THE SCAN IS RANKED, NOT <c>found[0]</c>. With both menus alive at once
    /// the old fallback picked whichever the engine listed first, i.e. a coin flip between the
    /// right menu and a crash. Candidates are now scored on scene membership, live-ness, and
    /// whether their own prerequisite exists — a <c>UIMapEscMenu</c> without a
    /// <c>MapChoreographer</c> is the exact object that threw, so it is ranked last rather than
    /// merely hoped against.</description></item>
    /// </list>
    /// </summary>
    private ESCMenu? ResolveMenu()
    {
        // 1. The game's live registration.
        ESCMenu? live = Singleton<ESCMenu>.IsInitialized ? Singleton<ESCMenu>.Instance : null;
        if (live != null)
        {
            if (!ReferenceEquals(live, _menu))
                Adopt(live, "Singleton<ESCMenu> (the game's own live registration)");
            return _menu;
        }

        // 2. The cache, scoped to the scene it was taken in.
        if (_menu != null)
        {
            if (SceneManager.GetActiveScene().handle == _menuSceneHandle)
                return _menu;

            VRLog.Info("WorldUI",
                $"[OptionsToggle] cache DROPPED: the cached {_menu.GetType().Name} was resolved while a " +
                "DIFFERENT scene was active and the game's Singleton<ESCMenu> is now empty, so it can no " +
                "longer be trusted to be this context's pause menu. UIMapEscMenu survives scene loads, so " +
                "a Unity fake-null test would never have cleared it — this is the invalidation that " +
                "ModBuild 289's twenty dead taps were missing.");
            _menu = null;
            _menuSource = "unresolved";
            _open = false;
            // Deliberately NOT spending the in-flight press. The tap that reached this line is the
            // player asking for the menu; spending it would make the FIRST tap after every context
            // change do nothing, which is the same complaint under a different cause. The toggle
            // decision below re-reads the newly resolved window's live state anyway, so a stale
            // `_open` cannot survive to mis-toggle.
        }

        // 3. EXPENSIVE recovery ONLY on an actual tap (never per-frame).
        if (!NonDominantHold.ShortTapThisFrame)
            return null;

        List<ESCMenu> ranked = RankedCandidates(out string census);
        if (ranked.Count == 0)
        {
            VRLog.Info("WorldUI",
                "OPTIONS TAP: no ESCMenu object exists (Singleton<ESCMenu>.IsInitialized=false, " +
                "scene-scan incl-inactive found none) — the game DESTROYED the pause menu on close; " +
                "a reload currently re-creates it.");
            return null;
        }

        VRLog.Info("WorldUI",
            $"[OptionsToggle] ESCMenu RANKED SCAN picked {ranked[0].GetType().Name} out of " +
            $"{ranked.Count} candidate(s): {census}. The scan runs only when the game's own singleton " +
            "is empty AND the cache has expired.");
        Adopt(ranked[0], $"ranked scene scan ({ranked.Count} candidate(s))");
        return _menu;
    }

    /// <summary>
    /// Every live <c>ESCMenu</c> in the scene, best-first. The scoring is the whole point: the
    /// pre-ModBuild-290 fallback took <c>found[0]</c>, and once BOTH menus are alive at once —
    /// which is the state the campaign map leaves behind, because <c>UIMapEscMenu</c> is never
    /// destroyed — that was a coin flip between the right menu and a guaranteed crash.
    ///
    /// <para>Three terms, in the order they matter:</para>
    /// <list type="bullet">
    /// <item><description><b>+4 in the active scene</b> — the strongest available signal that a
    /// menu belongs to what the player is currently looking at.</description></item>
    /// <item><description><b>+2 active in the hierarchy</b> — a live menu beats a parked one, but
    /// only as a tie-break: the mod's own close path DEACTIVATES the menu it hid, so
    /// "inactive" must never disqualify a candidate outright (that was the original reopen bug).
    /// </description></item>
    /// <item><description><b>-8 needs a MapChoreographer and there is none</b> — the exact object
    /// that threw in ModBuild 289. <c>UIMapEscMenu.CheckMultiplayerButton</c> and its
    /// <c>OnShow</c> both dereference <c>Singleton&lt;MapChoreographer&gt;.Instance</c> with no
    /// null check, so off the campaign map that menu is not merely a worse choice, it is a broken
    /// one. The weight is larger than the other two combined so it always loses to any
    /// alternative, and it is still only a RANKING — with no other candidate it is picked anyway,
    /// because a partially-working menu beats no menu at all (the ruling).</description></item>
    /// </list>
    /// </summary>
    private static List<ESCMenu> RankedCandidates(out string census)
    {
        ESCMenu[] found = UnityEngine.Object.FindObjectsOfType<ESCMenu>(includeInactive: true);
        Scene activeScene = SceneManager.GetActiveScene();
        bool choreographer = Singleton<MapChoreographer>.IsInitialized;

        var kept = new List<ESCMenu>(found.Length);
        var scores = new List<int>(found.Length);
        var text = new StringBuilder();
        for (int i = 0; i < found.Length; i++)
        {
            ESCMenu c = found[i];
            if (c == null)
                continue;

            bool inActiveScene = c.gameObject.scene == activeScene;
            bool liveInHierarchy = c.gameObject.activeInHierarchy;
            bool wantsChoreographer = c is UIMapEscMenu;
            int score = (inActiveScene ? 4 : 0) + (liveInHierarchy ? 2 : 0)
                        + (wantsChoreographer && !choreographer ? -8 : 0);

            // Insertion sort, best-first. There are two ESCMenu subclasses in the whole game, so
            // the list is never longer than a handful and the O(n^2) is free.
            int at = 0;
            while (at < scores.Count && scores[at] >= score)
                at++;
            kept.Insert(at, c);
            scores.Insert(at, score);

            if (text.Length > 0)
                text.Append("; ");
            text.Append($"{c.GetType().Name} score={score} (activeScene={inActiveScene} " +
                        $"activeInHierarchy={liveInHierarchy} needsMapChoreographer={wantsChoreographer})");
        }

        census = $"{text} [MapChoreographer alive={choreographer}]";
        return kept;
    }

    /// <summary>
    /// SECOND CHANCE. The first-choice menu returned from <c>Show()</c> still reporting closed, so
    /// try every OTHER ESCMenu in the scene before giving up on the tap. This can never produce two
    /// open menus, because it only runs when the first one demonstrably did not open.
    ///
    /// <para>It exists because the mod removed the game's own redundancy: while VR runs,
    /// <see cref="Patches.ShowUIWindowSuppressor"/> blocks the game's <c>UI_PAUSE</c> auto-show so
    /// that one X press cannot toggle the menu twice. That is correct — both menus register that
    /// handler in <c>Awake</c> and only unregister in <c>OnDestroy</c>, so with the never-destroyed
    /// map menu still subscribed, un-suppressing it would open BOTH menus at once inside a
    /// scenario. The redundancy therefore has to come from here instead.</para>
    /// </summary>
    private bool TryFallbackMenu(ESCMenu failed, out ESCMenu opened, out string why)
    {
        opened = failed;
        why = "no other ESCMenu exists to fall back to";
        List<ESCMenu> ranked = RankedCandidates(out string census);
        for (int i = 0; i < ranked.Count; i++)
        {
            ESCMenu c = ranked[i];
            if (ReferenceEquals(c, failed))
                continue;
            if (!OpenMenu(c, out why))
            {
                VRLog.Warn("WorldUI",
                    $"[OptionsToggle] second-chance {c.GetType().Name} ALSO refused to open: {why}");
                continue;
            }

            opened = c;
            Adopt(c, "second chance after the first-choice menu refused to open");
            VRLog.Warn("WorldUI",
                $"[OptionsToggle] SECOND CHANCE TOOK: {failed.GetType().Name} refused to open, so " +
                $"{c.GetType().Name} was opened instead and is now the menu this mod drives. " +
                $"Candidates: {census}. The options menu is never allowed to be unopenable, so a " +
                "first-choice failure falls through rather than ending the tap.");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Take <paramref name="menu"/> as the working ESC menu and re-base every piece of state that
    /// was keyed to the previous one. The open-latch is re-read from the NEW window rather than
    /// carried over, so switching menus can never present as a phantom "opened/closed externally"
    /// edge. The in-flight press is deliberately NOT spent: adoption can land on the very tap the
    /// player made, and spending it would trade "the menu never opens" for "the first tap after
    /// every context change never opens it".
    /// </summary>
    private void Adopt(ESCMenu menu, string source)
    {
        string previous = _menu != null ? _menu.GetType().Name : "<none>";
        _menu = menu;
        _menuSource = source;
        _menuSceneHandle = SceneManager.GetActiveScene().handle;
        _open = menu.IsOpen;
        VRLog.Info("WorldUI",
            $"[OptionsToggle] ESC MENU RESOLVED: now driving {menu.GetType().Name} " +
            $"(was {previous}), from {source}; window IsOpen={_open}, " +
            $"activeInHierarchy={menu.gameObject.activeInHierarchy}. The runtime TYPE is logged on " +
            "every open from here on, so a future log names the menu without needing a crash to do it.");
    }

    /// <summary>
    /// MEASURE the tap frame instead of asserting it is cheap. ModBuild 289 attributed 10.5-11.2 ms
    /// to OptionsToggle on three separate tap frames against an 11.11 ms budget, while the doc
    /// comment above the probes claimed they were "cheap at human tap cadence" — a comment is not
    /// a measurement, and two of those three frames were spent opening nothing at all.
    ///
    /// <para>The split is deliberately only two terms, because that is all it takes to settle the
    /// question: everything the MOD does before touching the game is the probe, and everything
    /// after is the game's own <c>Show()</c>/<c>Hide()</c> cascade running on our call stack. If
    /// ACTION dominates, the 10 ms is the game's window machinery and the mod cannot make it
    /// cheaper by tuning its own probes; if PROBE dominates, it is ours to fix. Tap-frequency
    /// only, so this instrument costs nothing between taps.</para>
    /// </summary>
    private void LogTapCost(long t0, long t1, long t2, OpenState st, bool closed)
    {
        double toMs = 1000.0 / Stopwatch.Frequency;
        double probeMs = (t1 - t0) * toMs;
        double actionMs = (t2 - t1) * toMs;
        VRLog.Info("WorldUI",
            $"[OptionsToggle] TAP COST {(probeMs + actionMs):F2} ms = PROBE {probeMs:F2} ms " +
            $"(open-state of the whole ESC family; {st.Scanned} registered UIWindow(s) walked via " +
            $"UIWindow.GetWindows(), NOT a FindObjectsOfType sweep) + ACTION {actionMs:F2} ms " +
            $"(the game's own {(closed ? "Hide" : "Show")} cascade plus the mod's float bookkeeping, " +
            "running on our call stack). READ IT AGAINST THE 11.11 ms BUDGET: ModBuild 289's SPIKE " +
            "lines put 10.5-11.2 ms here with no split, so whichever of these two terms is large is " +
            "the answer, and only one of them is the mod's to fix.");
    }

    /// <summary>
    /// Live open-state of the whole ESC-menu family (parent + Options/Multiplayer/Compendium
    /// sub-windows), read from the ACTUAL game windows. Tap-frequency only (never per-frame):
    /// the singleton lookups and the one compendium scene scan are cheap at human cadence. The
    /// captured windows are reused by <see cref="CloseAll"/> so no second lookup is needed.
    /// </summary>
    private static OpenState Probe(ESCMenu menu)
    {
        bool escOpen = menu.IsOpen;
        UIOptionsWindow? optOwner = Singleton<UIOptionsWindow>.IsInitialized ? Singleton<UIOptionsWindow>.Instance : null;
        UIMultiplayerEscSubmenu? mpOwner = Singleton<UIMultiplayerEscSubmenu>.IsInitialized ? Singleton<UIMultiplayerEscSubmenu>.Instance : null;
        UIWindow? optWin = optOwner != null ? optOwner.GetComponent<UIWindow>() : null;
        UIWindow? mpWin = mpOwner != null ? mpOwner.Window : null;
        UIWindow? compWin = FindOpenCompendiumWindow(out int scanned); // side-effect-free; only ever an OPEN instance
        bool optOpen = optWin != null && optWin.IsOpen;
        bool mpOpen = mpWin != null && mpWin.IsOpen;
        bool compOpen = compWin != null;
        return new OpenState(escOpen, optOpen, mpOpen, compOpen, optWin, mpWin, compWin, scanned);
    }

    /// <summary>
    /// FIX A (controller-X close): route EVERY window of the ESC-menu family through the SAME
    /// path the corner-X uses — <see cref="ModalFallback.CloseFloatedWindow"/>. That path sets
    /// the float's <c>UserClosing</c> flag, the ONLY thing that ever drops a STICKY float (the
    /// whole reachable-menu family is sticky: ModalFallback keeps it floated and force-visible
    /// — <c>ReassertStickyVisible</c> — even after the game hides it). The old direct
    /// <c>owner.Hide()</c>/<c>menu.Hide()</c> calls closed the GAME windows but never set that
    /// flag, so the floated host stayed alive and force-visible forever: the hardware log showed
    /// "hidden — untracked" with NO "released — restored to its 2D home" line, and the pause menu
    /// never visually disappeared on a controller-X close.
    ///
    /// Escape-suppression interplay: <c>UIWindow.Escape()</c> is Harmony-blocked for
    /// ID==ESCMenu (<see cref="Patches.EscMenuInputBlock"/> returns "unhandled" without
    /// toggling), but CloseFloatedWindow calls <c>Hide()</c> whenever the window is still open
    /// after <c>Escape()</c> — so the ESC menu still closes through its own OnHide cascade
    /// (verified: UIWindow.Hide flips IsOpen synchronously, so the reconcile probe on the next
    /// tick sees the family closed; the float itself is released by ModalFallback's next tick,
    /// which counts as closed too — the probe reads game IsOpen, never the float).
    ///
    /// Order: open sub-windows first, then any REMAINING sticky family float (a float whose
    /// game window the single-window toggle already hid reports IsOpen==false, so the live
    /// probes cannot see it), and the parent ESC menu LAST — its OnHide →
    /// toggleGroup.SetAllTogglesOff cascade stays the belt-and-suspenders final word.
    ///
    /// <para>REACH (2026-09-03). The "remaining sticky family float" sweep is
    /// <see cref="ModalFallback.CloseStickyFloatsExceptEscMenu"/>, and until the ModBuild 407
    /// defect it took every sticky float rather than the family: in the map room that is every
    /// floated window, and it closed the quest-start story window on a press meant for the pause
    /// menu (log lines 5478-5490). The sweep is now scoped to
    /// <see cref="MenuWindowFamily.IsEscMenuSubWindow"/>; the three explicit closes above are the
    /// same sub-windows by singleton. Nothing on this path may touch a window outside
    /// <see cref="MenuWindowFamily.IsOptionsKeyDomain"/>, and <see cref="LogOptionsKey"/> measures
    /// that on every press.</para>
    /// </summary>
    private static void CloseAll(ESCMenu menu, OpenState st)
    {
        VRLog.Info("WorldUI", "OPTIONS TAP: close routed through CloseFloatedWindow (release path)");
        if (st.Opt)
            ModalFallback.CloseFloatedWindow(st.OptWin);   // Options UIWindow (UserClosing → Escape/Hide)
        if (st.Mp)
            ModalFallback.CloseFloatedWindow(st.MpWin);    // Multiplayer submenu UIWindow
        if (st.Comp)
            ModalFallback.CloseFloatedWindow(st.CompWin);  // compendium UIWindow
        // Sticky floats the game already hid (single-window toggle) are NOT IsOpen, so the
        // probes above cannot reach them — drop every remaining floated family window too.
        ModalFallback.CloseStickyFloatsExceptEscMenu();
        ModalFallback.CloseFloatedWindow(menu.GetComponent<UIWindow>()); // parent LAST
    }

    /// <summary>
    /// Open the ESC menu. ESCMenu has no public Show; its opener is just myWindow.Show(), which
    /// fires ESCMenu.OnShow via its onTransitionBegin listener. Belt-and-suspenders re-openability:
    /// re-activate the window GameObject if a previous close left it inactive, so Show() never
    /// depends on the window having stayed active since the last open.
    ///
    /// <para>RETURNS THE OUTCOME rather than assuming it. <c>UIWindow.Show(bool)</c> silently
    /// early-returns when <c>IsActive()</c> is false (<c>enabled &amp;&amp; activeInHierarchy</c>,
    /// UIWindow.cs:417) and, before the ModBuild 290 show-safety finalizers, a throwing
    /// <c>onTransitionBegin</c> listener abandoned the transition before
    /// <c>m_CurrentVisualState = state</c>. <c>m_CurrentVisualState</c> is assigned SYNCHRONOUSLY
    /// inside <c>EvaluateAndTransitionToVisualState</c> (the alpha tween that follows is the only
    /// asynchronous part), so reading <c>IsOpen</c> straight after <c>Show()</c> is a verdict, not
    /// a race. <paramref name="why"/> names the blocker when the verdict is false.</para>
    /// </summary>
    private static bool OpenMenu(ESCMenu menu, out string why)
    {
        var w = menu.GetComponent<UIWindow>();
        if (w == null)
        {
            why = $"{menu.GetType().Name} has no UIWindow component to show";
            return false;
        }

        // activeSelf only clears the object's OWN switch; an inactive ANCESTOR still blocks
        // UIWindow.IsActive(), and Show() would then return having done nothing at all.
        if (!w.gameObject.activeSelf)
            w.gameObject.SetActive(true);

        w.Show();
        if (w.IsOpen)
        {
            why = string.Empty;
            return true;
        }

        why = DescribeShowBlocker(w);
        return false;
    }

    /// <summary>
    /// Name the concrete reason a <c>UIWindow.Show()</c> left the window closed. Cold path only
    /// (a failed open), so the ancestor walk and the string work cost nothing in normal play.
    /// </summary>
    private static string DescribeShowBlocker(UIWindow w)
    {
        if (!w.enabled)
            return "the UIWindow component itself is disabled, so UIWindow.IsActive() is false and Show() " +
                   "returned immediately";
        if (!w.gameObject.activeInHierarchy)
        {
            UnityEngine.Transform? t = w.transform;
            while (t != null && t.gameObject.activeSelf)
                t = t.parent;
            string blocker = t != null ? t.name : "<unknown>";
            return $"an ANCESTOR is inactive ('{blocker}'), so UIWindow.IsActive() is false and Show() " +
                   "returned immediately — re-activating the window's own GameObject cannot reach it";
        }
        return "the window was active and Show() ran, but m_CurrentVisualState is still Hidden — " +
               "something threw inside UIWindow.EvaluateAndTransitionToVisualState (onShown / " +
               "onTransitionBegin) before the state was assigned. Look for an [ESC MENU SHOW SAFETY] " +
               "line: if there is none, the throw came from a listener the finalizers do not cover";
    }

    /// <summary>Immutable snapshot of the ESC-menu family's live open-state plus the resolved
    /// sub-window <c>UIWindow</c>s (FIX A: <see cref="CloseAll"/> passes these to
    /// <see cref="ModalFallback.CloseFloatedWindow"/> — the corner-X release path).</summary>
    private readonly struct OpenState
    {
        internal readonly bool Esc;
        internal readonly bool Opt;
        internal readonly bool Mp;
        internal readonly bool Comp;
        internal readonly UIWindow? OptWin;
        internal readonly UIWindow? MpWin;
        internal readonly UIWindow? CompWin;

        /// <summary>How many registered <c>UIWindow</c>s the compendium probe walked, for the
        /// tap-cost line — so the next log states the probe's size instead of implying it.</summary>
        internal readonly int Scanned;

        internal bool Any => Esc || Opt || Mp || Comp;

        internal OpenState(bool esc, bool opt, bool mp, bool comp,
            UIWindow? optWin, UIWindow? mpWin, UIWindow? compWin, int scanned)
        {
            Esc = esc;
            Opt = opt;
            Mp = mp;
            Comp = comp;
            OptWin = optWin;
            MpWin = mpWin;
            CompWin = compWin;
            Scanned = scanned;
        }
    }

    /// <summary>
    /// The compendium sub-window IF it is currently OPEN, else null. Deliberately NOT
    /// <c>Singleton&lt;SpecialUIProvider&gt;.Instance.CompendiumUIObject</c>: that getter
    /// INSTANTIATES the compendium prefab on first access (a synchronous Addressables load —
    /// SpecialUIProvider.GetCompendiumUI), so probing it merely to test open-state would create
    /// the window and hitch the frame. Compendium window ID verified as
    /// <c>UIWindowID.CompendiumPanel</c> (ModalFallback FallbackIds / SyncEscMenuTabHighlights).
    ///
    /// <para>PERF (ModBuild 290): this used to be <c>FindObjectsOfType&lt;UIWindow&gt;()</c> — a
    /// full scene sweep, on the frame the player is already paying for the game's own
    /// <c>Show()</c>. <c>UIWindow</c> keeps its own static registry, <c>_uiWindows</c>, added to
    /// in <c>OnEnable</c> and removed from in <c>OnDisable</c> (UIWindow.cs:69/398/404), exposed
    /// as <c>UIWindow.GetWindows()</c> and used by the game itself in
    /// <c>UIWindowManager.HideOrShowWindows</c>. It holds exactly the ENABLED windows, which is a
    /// superset of the open ones, so it answers this question with strictly the same result and
    /// no sweep. <c>FindObjectsOfType</c> is this codebase's most-repeated performance defect and
    /// has twice shipped described as "near-free"; the registry removes the argument entirely.</para>
    /// </summary>
    private static UIWindow? FindOpenCompendiumWindow(out int scanned)
    {
        scanned = 0;
        foreach (UIWindow w in UIWindow.GetWindows())
        {
            scanned++;
            if (w != null && w.ID == UIWindowID.CompendiumPanel && w.IsOpen)
                return w;
        }
        return null;
    }
}

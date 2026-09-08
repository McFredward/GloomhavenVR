using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using MapRuleLibrary.Party;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// THE PARTY-PREVIEW STORM — the mercenary roster rebuilding its character sheet ~28 times a
/// second while the laser rests on one slot.
///
/// <para>WHAT THE USER SEES. The 3D map room's permanent character window hosts the mercenary
/// roster. Point the laser at a mercenary and hold the hand still: the 3D character in the
/// window flickers, the sheet's text jitters, and the frame time goes with it.</para>
///
/// <para>WHAT THE MOD'S OWN INSTRUMENTS MEASURED (ModBuild 202 hardware log). The
/// <c>CHARACTER 3D CADENCE</c> line reported <c>28 call(s) — Display x28</c> in 1.01 s and
/// <c>18 call(s)</c> in 1.04 s, requester <c>UIAdventurePartyAssemblyCharacterDisplay</c> on
/// every one, with its own verdict "THIS IS A FLICKER RATE ... something is fighting over it".
/// In the same seconds <c>[Interact] uGUI hover ENTER/EXIT: 'UI Campaign PartyRoster Slot'</c>
/// alternated 77 times in 2.16 s ≈ 35.6 transitions/second.</para>
///
/// <para>ROOT CAUSE, READ OUT OF THE DECOMPILED GAME. It is a closed feedback loop between the
/// slot's hover animation and the ray that triggers it:</para>
/// <list type="number">
/// <item><c>UIAdventurePartyAssemblyRosterSlot.OnPointerEnter</c> (:257-285) GROWS the slot
/// (<c>animationRect.sizeDelta = rectSize + hoverFactor</c>, :266), rewrites the portrait's
/// pivot and anchors (:267-271), and then calls
/// <c>_scrollRect.ScrollToFit(base.gameObject.transform as RectTransform)</c> (:284) — the list
/// SCROLLS so the now-taller slot fits, which translates the slot out from under a stationary
/// ray. Finally it fires <c>OnCharacterHover?.Invoke(character)</c> (:285).</item>
/// <item><c>OnPointerExit</c> → <c>Unhighlight()</c> (:302-313) shrinks the slot back, the
/// layout reflows, and the slot returns under the ray. Enter → exit → enter is closed.</item>
/// <item>Both edges cost a full rebuild.
/// <c>UIAdventurePartyAssemblyWindow.Awake</c> (:61-62) wires
/// <c>OnCharacterHover → PreviewCharacterInfo</c> and
/// <c>OnCharacterUnhover → FinishPreviewCharacterInfo</c>, and
/// <c>FinishPreviewCharacterInfo</c> (:334-347) calls <c>PreviewCharacterInfo(selectedCharacter)</c>
/// at :341 — so the EXIT edge rebuilds too.</item>
/// <item><c>PreviewCharacterInfo</c> (:325-332) → <c>characterDisplay.Display(character)</c>
/// (UIAdventurePartyAssemblyCharacterDisplay.cs:56-65), which does <c>window.Show(instant:false)</c>,
/// a live 3D model swap through <c>Character3DDisplayManager</c>, and
/// <c>foreach (UICharacterInformation item in information) item.Display(characterData);</c> —
/// and <c>UICharacterClassInformation.Display</c> (:55-87) rewrites SIX TextMeshPro strings
/// unconditionally, several carrying <c>&lt;sprite name="…"&gt;</c> rich-text tags, i.e. six mesh
/// regenerations per lap per information panel.</item>
/// </list>
///
/// <para>WHY ONLY IN VR. On a desktop the mouse cursor moves WITH the layout (it is a screen
/// pixel the list scrolls under), and a hand on a mouse is never perfectly still anyway. A VR
/// laser is a ray cast from a hand held roughly still in WORLD space: the UI moves, the ray does
/// not, so the loop closes and stays closed. Nothing in the game is broken; the VR pointing model
/// is what makes a self-moving hover target oscillate.</para>
///
/// <para>THE FIX IS IN TWO LAYERS, AND THIS FILE IS THE SECOND.</para>
/// <list type="bullet">
/// <item>PRIMARY, in our own input code: <see cref="Hands.Interact.UguiPointer"/> now holds an
/// exit back for a few consecutive frames when the ray's new target enters NO new widget (null,
/// or an ancestor already entered — the roster list itself, which is exactly what the log's
/// "[already entered (shared ancestor …)]" note names). That breaks the loop at the pointer, for
/// every self-moving hover target, not just this one.</item>
/// <item>BELT AND BRACES, here: a prefix that skips
/// <c>UIAdventurePartyAssemblyWindow.PreviewCharacterInfo</c> when the character it is asked to
/// preview is ALREADY the one the display is showing. Redundant work only — the state the
/// original would write is bit-identical to the state that is already there.</item>
/// </list>
///
/// <para>WHAT IS DELIBERATELY NOT BLOCKED.</para>
/// <list type="bullet">
/// <item>Hovering a DIFFERENT mercenary. The suppression is keyed on reference identity of the
/// requested <see cref="CMapCharacter"/> against the one the display holds, so any other
/// mercenary — and any other CMapCharacter instance, even for the same class — previews exactly
/// as before. The feature is intact; only the repeat is removed.</item>
/// <item>The first preview, and every preview into a CLOSED display. <c>Display</c> is also what
/// RE-OPENS the sheet (<c>window.Show(instant:false)</c>), and the game never clears
/// <c>characterDisplay.character</c> on hide — so suppression additionally requires the display's
/// own <c>UIWindow.IsOpen</c>. <c>UIWindow</c> sets <c>m_CurrentVisualState</c> at the START of
/// its transition (UIWindow.cs:548), so this is true from the first frame of the show and false
/// from the first frame of the hide: never a window that fails to come back.</item>
/// <item>The CAMPAIGN subclass's own bookkeeping. <c>UICampaignAdventurePartyAssemblyWindow</c>
/// overrides <c>PreviewCharacterInfo</c> (:233-240) and, AFTER <c>base.PreviewCharacterInfo</c>,
/// sets <c>activeDeleteCondition = selectedCharacter == character</c> and shows/hides the delete
/// button. That is cheap, local, and NOT idempotent with respect to a selection change, so it
/// must keep running. Patching the BASE method rather than the override achieves exactly that:
/// the override's <c>base.</c> call is a direct call to the patched MethodInfo, so the prefix
/// runs on the campaign path too (which is the path the log names) while the two lines after it
/// still execute every time. One patch, both types, no lost state.</item>
/// <item>Gamepad navigation. The base method also calls <c>characterDisplay.EnableNavigation()</c>
/// when the controller area is focused; the suppression is gated on
/// <c>!InputManager.GamePadInUse</c> so that branch is out of scope by construction. In VR that
/// flag is held false anyway (<see cref="InputModeGuard"/>), and the game's own
/// <c>FinishPreviewCharacterInfo</c> early-outs on it, so the storm cannot occur with it set.</item>
/// </list>
///
/// <para>DESKTOP IS BYTE-IDENTICAL. The prefix returns <c>true</c> immediately unless VR canvas
/// conversion is active (<see cref="WorldUIConfig.ConversionActive"/>), so with the mod off, or
/// before conversion, nothing here changes a single call.</para>
///
/// <para>MULTIPLAYER. This is local input and local UI only. The prefix skips a UI REBUILD of
/// data that is already displayed; it writes no game state, touches no rule library and no Bolt
/// entity, and sends nothing on the wire, so there is nothing another peer could observe. The
/// hover hysteresis it reports on is likewise pure local pointer dispatch.</para>
///
/// <para>DEGRADES SAFELY. Every private field is resolved once through
/// <see cref="AccessTools"/>. If any is missing, the class logs ONE warning naming the
/// consequence and stands down permanently — no suppression, vanilla behaviour, nothing thrown.
/// Registered by <c>WorldUIModule</c>.</para>
/// </summary>
[HarmonyPatch]
internal static class PartyPreviewStorm
{
    private const string Scope = "WorldUI";

    // ---- reflected game state ---------------------------------------------------------------

    /// <summary><c>UIAdventurePartyAssemblyWindow.characterDisplay</c> (protected).</summary>
    private static FieldInfo? _windowDisplay;

    /// <summary><c>UIAdventurePartyAssemblyCharacterDisplay.character</c> (protected) — the game's
    /// OWN record of what it last displayed, written at the end of <c>Display</c>. Read rather
    /// than shadowed: a second copy of this fact could disagree with it.</summary>
    private static FieldInfo? _displayCharacter;

    /// <summary><c>UIAdventurePartyAssemblyCharacterDisplay.window</c> (private) — the sheet's own
    /// <see cref="UIWindow"/>, so "is it actually on screen" comes from the game too.</summary>
    private static FieldInfo? _displayWindow;

    private static bool _resolved;
    private static bool _standDown;

    // ---- accounting for the one report line -------------------------------------------------

    /// <summary>A report covers at most this much activity, so both halves are read as RATES.</summary>
    private const float ReportSeconds = 5f;

    private static float _windowStart = -1f;
    private static int _hoverHeld;
    private static int _hoverDispatched;
    private static int _previewSeen;
    private static int _previewSuppressed;
    private static int _previewPassed;
    private static string _lastRequester = "<none>";

    // ---- the hover-hysteresis feed (called by Hands.Interact.UguiPointer) --------------------

    /// <summary>
    /// A pure hover LOSS was held back by the exit hysteresis this frame. Two int writes and a
    /// float compare; called at most once per pointer per frame, and only on a hover CHANGE.
    /// </summary>
    internal static void NoteHoverHeld()
    {
        _hoverHeld++;
        MaybeReport();
    }

    /// <summary>A hover change was dispatched to the game (entered, or an exit that survived the
    /// hysteresis).</summary>
    internal static void NoteHoverDispatched()
    {
        _hoverDispatched++;
        MaybeReport();
    }

    // ---- the seam ----------------------------------------------------------------------------

    /// <summary>
    /// Skip <c>PreviewCharacterInfo</c> when the requested character is already the displayed one
    /// and the sheet is open. Patched on the BASE type on purpose — see the class doc: the
    /// campaign override reaches this body through <c>base.</c> and keeps its own two lines.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(UIAdventurePartyAssemblyWindow), "PreviewCharacterInfo",
        typeof(CMapCharacter))]
    private static bool BeforePreviewCharacterInfo(UIAdventurePartyAssemblyWindow __instance,
                                                   CMapCharacter character)
    {
        _previewSeen++;
        _lastRequester = __instance != null ? __instance.GetType().Name : "<null window>";

        if (_standDown || !WorldUIConfig.ConversionActive || InputManager.GamePadInUse)
            return Pass();
        if (!Resolve() || __instance == null || character == null)
            return Pass();

        if (_windowDisplay!.GetValue(__instance) is not UIAdventurePartyAssemblyCharacterDisplay display
            || display == null)
            return Pass();

        // The sheet must be on screen: Display() is also what re-opens it.
        if (_displayWindow!.GetValue(display) is not UIWindow sheet || sheet == null || !sheet.IsOpen)
            return Pass();

        // Reference identity, not equality: only a provably identical rebuild is skipped.
        if (!ReferenceEquals(_displayCharacter!.GetValue(display), character))
            return Pass();

        _previewSuppressed++;
        MaybeReport();
        return false;
    }

    private static bool Pass()
    {
        _previewPassed++;
        MaybeReport();
        return true;
    }

    // ---- reflection --------------------------------------------------------------------------

    private static bool Resolve()
    {
        if (_resolved)
            return !_standDown;
        _resolved = true;

        _windowDisplay = AccessTools.Field(typeof(UIAdventurePartyAssemblyWindow), "characterDisplay");
        _displayCharacter = AccessTools.Field(typeof(UIAdventurePartyAssemblyCharacterDisplay), "character");
        _displayWindow = AccessTools.Field(typeof(UIAdventurePartyAssemblyCharacterDisplay), "window");

        if (_windowDisplay != null && _displayCharacter != null && _displayWindow != null)
            return true;

        _standDown = true;
        // 2026-09 refactor, F-74 — a one-shot stand-down; the line itself tells the reader what
        // the CHARACTER 3D CADENCE numbers will look like as a result, which is unusable if the
        // stand-down is invisible.
        VRLog.Alert(Scope,
            "PartyPreviewStorm STOOD DOWN: could not resolve "
            + $"characterDisplay={_windowDisplay != null}, character={_displayCharacter != null}, "
            + $"window={_displayWindow != null}. The redundant-rebuild suppression is OFF for this "
            + "session (vanilla behaviour, nothing is broken); the roster-hover storm is then held "
            + "only by the pointer's exit hysteresis, so expect CHARACTER 3D CADENCE to report a "
            + "higher Display count than it otherwise would.");
        return false;
    }

    // ---- the report --------------------------------------------------------------------------

    private static void MaybeReport()
    {
        float now = Time.unscaledTime;
        if (_windowStart < 0f)
        {
            _windowStart = now;
            return;
        }
        if (now - _windowStart < ReportSeconds)
            return;
        float span = now - _windowStart;
        _windowStart = now;

        int hoverTotal = _hoverHeld + _hoverDispatched;
        if (hoverTotal == 0 && _previewSeen == 0)
            return; // nothing happened; do not print a line of zeroes every 5 s

        // HW-VERIFY (2026-09 refactor, F-80) — this line's own text says it "is the ATTRIBUTION
        // for the CHARACTER 3D CADENCE line" and that "(3) Both counters at 0 while the user reports
        // flicker means neither cause is live and the next round must not be spent here". A line
        // that directs where a hardware round is spent has to be in a hardware log. THE THROTTLE
        // WAS CHECKED, not assumed: MaybeReport advances its window start BEFORE the
        // nothing-happened early-out above, so it cannot be defeated the way ActorPropBody.cs:933
        // was, and the line is suppressed entirely when both counters are zero — ceiling one line
        // per 5 s, only while the roster is hovered. ITS PARTNER, CHARACTER 3D CADENCE, IS
        // DELIBERATELY LEFT AT Info: a 1 s window makes it four times noisier and this line already
        // names its verdict in prose, so one printing half decides the round.
        VRLog.Note(Scope,
            $"PARTY PREVIEW STORM ({span:F2}s): hover changes {hoverTotal} — "
            + $"{_hoverDispatched} dispatched, {_hoverHeld} SWALLOWED by the "
            + $"{Hands.Interact.UguiPointer.ExitHysteresisFrames}-frame exit hysteresis "
            + $"({(hoverTotal > 0 ? _hoverHeld * 100 / hoverTotal : 0)}% held); "
            + $"PreviewCharacterInfo {_previewSeen} call(s) — {_previewSuppressed} SUPPRESSED "
            + $"(same character, sheet already open), {_previewPassed} passed through; "
            + $"last requester '{_lastRequester}'. "
            + "HOW TO READ IT. This line is the ATTRIBUTION for the CHARACTER 3D CADENCE line: "
            + "cadence measures the OUTCOME (how often the display was actually rebuilt), this "
            + "measures the two causes we can act on. (1) A large SWALLOWED count with a small "
            + "dispatched count is the pointer fix working: the laser was falling off a "
            + "self-moving hover target and back on again, and those laps never reached the game "
            + "at all. SWALLOWED near 0 while cadence still calls this a flicker rate means the "
            + "oscillation is SLOWER than the hysteresis window (a loss phase of 6+ frames) or is "
            + "an A-to-B alternation between two DIFFERENT widgets, which the hysteresis "
            + "deliberately never damps — look at the hover ENTER/EXIT names next. (2) SUPPRESSED "
            + "counts the rebuilds this patch removed; passed counts the ones it let through, "
            + "which includes every genuine mercenary change and every preview into a closed "
            + "sheet. SUPPRESSED high AND cadence still high means something OTHER than "
            + "PreviewCharacterInfo is calling Display — read the cadence line's own requester "
            + "list, because it names the caller. (3) Both counters at 0 while the user reports "
            + "flicker means neither cause is live and the next round must not be spent here.");

        _hoverHeld = _hoverDispatched = 0;
        _previewSeen = _previewSuppressed = _previewPassed = 0;
    }
}

using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Where the player is standing, as far as MENU ARBITRATION is concerned. Deliberately four
/// values and not a bool: <see cref="Elsewhere"/> is the honest answer during a scene load, a
/// loading screen or a boot frame, and it must not be silently folded into either of the two
/// behaviours below.
/// </summary>
internal enum MenuPlace
{
    /// <summary>The game's main menu — <c>UIMainOptionsMenu</c>, no ESC menu, no board.</summary>
    MainMenu,

    /// <summary>A live scenario: a scenario board exists.</summary>
    Scenario,

    /// <summary>The mod's 3D map room.</summary>
    MapRoom,

    /// <summary>None of the three could be established this instant.</summary>
    Elsewhere,
}

/// <summary>How many player menus may stand at once in a given <see cref="MenuPlace"/>.</summary>
internal enum MenuArbitration
{
    /// <summary>
    /// BOTH MAY STAND. The ModBuild 337 ruling: <i>"Ich will das es möglich ist das man die
    /// Optionen und die VR Optionen öffnet parallel ohne Probleme."</i>
    /// </summary>
    Parallel,

    /// <summary>
    /// ONE AT A TIME. The main menu's own native rule — its rows are a single-select
    /// <c>ToggleGroup</c>, so opening one entry closes whatever the previous one had opened.
    /// </summary>
    OneAtATime,
}

/// <summary>
/// <b>THE ONE PLACE THAT ANSWERS "WHAT HAPPENS TO THE VR OPTIONS PANE WHEN THE PLAYER OPENS
/// SOMETHING ELSE HERE?", WITH "HERE" AS AN EXPLICIT INPUT.</b>
///
/// <para><b>THE DEFECT (user, 2026-09-02, hardware round on ModBuild 348, verbatim):</b>
/// <i>"Im Hauptmenu wenn ich die VR-Optionen offen hatte, und dann etwas andere aufmache,
/// verschwinden die VR-Optionen nicht mehr was sie dann darüber legen lässt - dann kann man nichts
/// mehr steuern. Hier soll es sich anders Verhalten als im Szenario oder in der map-umgebung wenn
/// es als Fenster spawnt."</i></para>
///
/// <para><b>BOTH BEHAVIOURS ARE CORRECT; THE MOD DID NOT KNOW THE DIFFERENCE.</b> ModBuild 336/337
/// took the mod's cloned menu row out of the menu's single-select <c>ToggleGroup</c>
/// (<c>VRMenuEntry.Detach</c>) because in the PAUSE menu each row had literally become the other
/// window's close button, and the user's ruling was that the two settings windows must be able to
/// stand open together. That act was applied to BOTH cloned rows — the pause-menu one and the
/// main-menu one — because at the time there was one row-cloning path and no notion of "here". In
/// the main menu the group was not a coupling to be broken: <b>it was the only thing that closed
/// the VR pane when the player picked another entry</b>, because in that menu every row's
/// <c>Deselect</c> is what shuts whatever that row had opened
/// (<c>UIMainOptionsMenu.InitializeButton</c>, decompiled :162-176).</para>
///
/// <para><b>WHY IT COST HIM CONTROL AND NOT MERELY TIDINESS — measured, ModBuild 348 log.</b> In
/// the main menu the mod is in <c>VRMode.Menu2D</c> and every game window is composited onto the
/// ONE flat screen; the hardware log's own verdict line for the window he opened says so:
/// <c>MODAL PRESENTATION VERDICT 'UI Main Option Submenu' (ID OptionsSubmenu): ON THE FLAT SCREEN
/// … [room=False, mode=Menu2D, flat screen SHOWN]</c>. On a single 2D composite the sorting order
/// alone decides who is drawn on top AND who wins the hit test, and the same log's canvas census
/// gives both numbers: the mod's pane lives on <c>canvas='Persistent UI_unified' …
/// order=1100</c> while the whole main menu lives on <c>canvas='Canvas' … order=0</c>. A
/// 1164x1080 pane at order 1100 therefore covers a 1920x1080 menu at order 0, and the pointer —
/// which in this mode is a single hit test on that composite (<c>FlatScreen pointer: trigger PRESS
/// at RT pixel</c> → <c>DirectClick at …</c>) — finds the pane's backing first. Nothing behind it
/// can be reached. That is <i>"darüber legen"</i> and <i>"nichts mehr steuern"</i> in one fact, and
/// it is a Z-ORDER fact rather than a pose or a lost-input one: the window he opened was drawn, it
/// was simply drawn underneath.</para>
///
/// <para><b>THE RULE, IN ONE SENTENCE.</b> In the MAIN MENU the VR options pane takes part in the
/// menu's own one-at-a-time arbitration — it closes when another entry is opened, and opening it
/// closes the entry that was open; in a SCENARIO and in the MAP ROOM, where it spawns as a
/// free-floating window of its own, nothing changes and both windows may stand
/// (<see cref="MenuArbitration"/>).</para>
///
/// <para><b>WHAT THIS DOES NOT DO, ON PURPOSE.</b> It does not put the cloned row back into the
/// game's <c>ToggleGroup</c>, in any place. Re-entering the group would restore the exact coupling
/// 336/337 removed (the game wires its Optionen row's DEselect to <c>UIOptionsWindow.Hide()</c> and
/// ours to <c>VROptionsTab.Close()</c>, so each row becomes the other window's close button), and
/// it would re-introduce the lit-toggle bounce-back that <c>Toggle.Set</c> performs when the last
/// member of a group with <c>allowSwitchOff == false</c> is switched off — a row latched lit over a
/// closed window is a door that has stopped answering, against <i>"Es MUSS immer möglich sein das
/// Optionsmenu zu öffnen."</i> The arbitration is therefore performed BY THE MOD, over a set of
/// rows it only ever reads and deselects through the game's own public <c>Deselect()</c>.</para>
///
/// <para><b>AND IT DOES NOT RE-CLASSIFY THE WINDOW.</b> <see cref="MenuWindowFamily"/> answers
/// "what KIND of window is this" and its verdicts are untouched: the pane is still a player menu,
/// still non-blocking, still never a game modal (ModBuild 344). This class answers a different
/// question — "who yields to whom HERE" — and the two are deliberately separate, because the
/// family of a window does not change when the player walks from the main menu into a scenario and
/// the arbitration does.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here is networked and nothing here writes game state. The main
/// menu is not a networked context at all — no Bolt/FFSNet session exists before a game is loaded —
/// and in the two places that ARE networked (scenario, map room) this class returns
/// <see cref="MenuArbitration.Parallel"/>, i.e. the pre-existing behaviour, so it cannot change what
/// either peer does. A <c>UIWindow</c> and a menu row exist only on the client they were opened on;
/// both peers evaluate this identically against their own local menus.</para>
///
/// <para>GREP: <c>MENU ARBITRATION</c>.</para>
/// </summary>
internal static class MenuExclusivity
{
    /// <summary>One line per session for the yield, and one for the refusal — verdicts, not
    /// chatter. Cleared by <see cref="ResetLogLatches"/> on teardown.</summary>
    private static bool _loggedYield;
    private static bool _loggedRefusal;

    /// <summary>
    /// WHERE THE PLAYER IS, from the mod's existing authorities and no new one.
    ///
    /// <para>The map room is <c>MapRoomDriver.Active</c> (the "the room is actually standing"
    /// signal every subsystem outside the rig is documented to test, MapRoomDriver.cs:126-128); a
    /// scenario is <c>VRModeStateMachine.ScenarioBoardExists</c> (self-declared canonical,
    /// VRModeStateMachine.cs:97-109); the main menu is <c>MainMenuUIManager.Instance != null</c>,
    /// the idiom already used by <c>SelfUpdateModule.MainMenuIsUp</c> (:390), <c>LoadingIndicator</c>
    /// (:548) and <c>ModVersionLabel</c> (:160). No scene name is read.</para>
    ///
    /// <para><b>WHY NOT <c>VRMode.Menu2D</c>, which looks like the obvious answer.</b> Its own doc
    /// says it "covers EVERYTHING before an actual combat scenario — main menu, campaign/world map,
    /// guildmaster, merchant, level-up", so it is true in half a dozen places that are not the main
    /// menu. This class must not shorten a menu's life in any of them.</para>
    ///
    /// <para><b>THE ORDER IS THE SAFE ORDER AND CANNOT CHANGE A VERDICT TODAY.</b> The two places
    /// the 337 ruling protects are tested FIRST, so any ambiguity resolves toward
    /// <see cref="MenuArbitration.Parallel"/> — toward leaving the pane standing, never toward
    /// making it vanish. In practice the three are disjoint (the main-menu scene has neither a
    /// <c>Choreographer</c> nor a <c>MapChoreographer</c>), so the ordering is a guard rather than a
    /// tie-break.</para>
    /// </summary>
    internal static MenuPlace Where
    {
        get
        {
            try
            {
                if (MapRoom.MapRoomDriver.Active)
                    return MenuPlace.MapRoom;
                if (VRModeStateMachine.ScenarioBoardExists)
                    return MenuPlace.Scenario;
                if (MainMenuIsUp())
                    return MenuPlace.MainMenu;
                return MenuPlace.Elsewhere;
            }
            catch (Exception)
            {
                // A context that cannot be established is Elsewhere, which arbitrates like a
                // scenario — i.e. changes nothing. Never let a failed read close a menu.
                return MenuPlace.Elsewhere;
            }
        }
    }

    /// <summary>
    /// <c>MainMenuUIManager</c> is a plain <c>MonoBehaviour</c> with a static <c>Instance</c>
    /// (decompiled :72, set in <c>Awake</c> :86, nulled in <c>OnDestroy</c> :94), so its own
    /// lifetime is the answer. Wrapped for the same reason <c>SelfUpdateModule</c> wraps it: during
    /// boot the type may not be loaded, and "not loaded" means "no menu".
    /// </summary>
    private static bool MainMenuIsUp()
    {
        try
        {
            return GLOOM.MainMenu.MainMenuUIManager.Instance != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// HOW MANY PLAYER MENUS MAY STAND AT ONCE IN <paramref name="place"/>. The context is a
    /// parameter rather than a read inside the body so a caller can state the place it means and so
    /// this stays one table rather than a scattering of <c>if (mainMenu)</c>.
    /// </summary>
    internal static MenuArbitration Rule(MenuPlace place) => place switch
    {
        // The menu's own native behaviour, restored: its rows are a single-select ToggleGroup and
        // every row's Deselect closes what that row opened.
        MenuPlace.MainMenu => MenuArbitration.OneAtATime,

        // ModBuild 337, unchanged and load-bearing: in a scenario and in the map room the pane
        // spawns as a free-floating window of its own and the two settings windows are independent.
        MenuPlace.Scenario => MenuArbitration.Parallel,
        MenuPlace.MapRoom => MenuArbitration.Parallel,

        // Unknown place: behave exactly as before this build. Closing a menu on a guess is the one
        // outcome that could break "es MUSS immer möglich sein das Optionsmenu zu öffnen".
        _ => MenuArbitration.Parallel,
    };

    /// <summary>The rule for wherever the player is standing right now.</summary>
    internal static MenuArbitration RuleHere => Rule(Where);

    /// <summary>
    /// The pane has just yielded to <paramref name="rival"/>. One line per session, naming the
    /// measurement that made the overlap unrecoverable so a future round does not have to
    /// re-derive it.
    /// </summary>
    internal static void NoteYielded(string rival, string ourRow, Canvas? paneCanvas,
                                     Canvas? rivalCanvas)
    {
        if (_loggedYield)
            return;
        _loggedYield = true;
        // HW-VERIFY: this is the ModBuild 349 fix's own falsifier and the ONLY line that proves the
        // rule ran. If the next main-menu hardware round opens the VR options, then opens another
        // main-menu entry, and this line is ABSENT while the pane is still standing over it, the
        // arbitration never fired — start at VRMenuEntry.TickMainMenuExclusivity and check whether
        // the rival row set resolved at all (UIMainOptionsMenu.menuOptions is filled in Start()).
        VRLog.Note("WorldUI", "MENU ARBITRATION: the VR options pane YIELDED — the player opened "
                              + $"'{rival}' in the MAIN MENU while it was up, so our own row "
                              + $"'{ourRow}' was cleared and the pane closed. This is the main "
                              + "menu's own one-at-a-time rule, performed by the mod rather than by "
                              + "the game's ToggleGroup: the cloned row is still OUT of that group "
                              + "(ModBuild 336/337), so neither window is the other's close button "
                              + "and the scenario/map-room behaviour is untouched — there both "
                              + "windows still stand in parallel. WHY THIS HAD TO HAPPEN: in the "
                              + "main menu everything is composited onto the one flat screen "
                              + "(VRMode.Menu2D), so sorting order alone decides who is drawn on "
                              + "top and who wins the hit test — "
                              + $"pane {Describe(paneCanvas)} vs opened window {Describe(rivalCanvas)}. "
                              + "A pane that draws over the menu also eats every click aimed at it, "
                              + "which is the 2026-09-02 report's 'dann kann man nichts mehr "
                              + "steuern'.");
    }

    /// <summary>
    /// The pane was opened while a rival entry was ALREADY lit and the rival could not be cleared,
    /// so the yield rule stands down for this open rather than closing the menu the player just
    /// asked for. One line per session.
    /// </summary>
    internal static void NoteRefusedToArm(string rival)
    {
        if (_loggedRefusal)
            return;
        _loggedRefusal = true;
        // HW-VERIFY: this is the one hole in the main-menu arbitration and it must be visible. The
        // rule refuses to fire until it has once seen the main menu with no other entry lit, so
        // that a rival which cannot be deselected can never close the window the player just
        // opened. If this line appears, the pane CAN still end up drawn over that rival — and the
        // named row is the lead.
        VRLog.Note("WorldUI", $"MENU ARBITRATION: the main-menu entry '{rival}' was still lit when "
                              + "the VR options pane opened, and deselecting it did not take. The "
                              + "yield rule therefore STANDS DOWN for this open — deliberately: it "
                              + "arms only after it has seen the menu with no other entry lit, so a "
                              + "row that refuses to clear can never close the window the player "
                              + "just asked for ('es MUSS immer möglich sein das Optionsmenu zu "
                              + "öffnen'). The cost is that the pane may be drawn over that one "
                              + "window until the player closes it himself. The row named here is "
                              + "the lead: read why its Deselect() did not clear IsSelected.");
    }

    /// <summary>Canvas name / sorting order / override flag, or a plain "&lt;none&gt;". This is the
    /// pair of numbers that decides the overlap, so it is measured at the moment of the verdict
    /// rather than asserted from an earlier log.</summary>
    private static string Describe(Canvas? canvas)
        => canvas == null
            ? "<no canvas>"
            : $"'{canvas.name}' sortingOrder {canvas.sortingOrder}"
              + (canvas.overrideSorting ? " (overrideSorting)" : string.Empty);

    /// <summary>Teardown: the two verdict latches belong to the session, not to the process.</summary>
    internal static void ResetLogLatches()
    {
        _loggedYield = false;
        _loggedRefusal = false;
    }
}

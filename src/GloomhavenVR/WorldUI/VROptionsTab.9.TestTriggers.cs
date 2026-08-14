using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Erweitert ▸ <b>Test-Auslöser</b>: the one page in this menu whose rows DO something instead of
/// editing something.
///
/// <para><b>USER REQUEST</b> (hardware, ModBuild 141, verbatim): "ich brauche zum Testen im Erweitert
/// Menu die möglichkeit die Elemente und Easter eggs einzeln auf Knopfdruck auslösen zu können." Both
/// effects are otherwise unjudgeable: an element response needs the game to infuse that element, and
/// an apparition happens on a schedule whose median gap is nearly two minutes and which picks one of
/// six at random. So each gets a button.</para>
///
/// <para><b>IT MUST READ AS A TEST AID, and that is a requirement rather than a matter of taste.</b>
/// The page sits in the same menu as the settings, so a player who wanders in must not come away
/// believing they have changed the game. Three things carry that: the page is called "Test-Auslöser",
/// the first line under the heading states what it does NOT touch (nothing is sent, no game state
/// changes), and every effect is temporary and says so in its heading. Nothing on this page writes a
/// ConfigEntry, so there is also nothing for the player to undo.</para>
///
/// <para><b>MULTIPLAYER: HARMLESS BY CONSTRUCTION, which is worth stating because the project's
/// standing rule is that everything is synchronised 1:1 and this is a deliberate, narrow exception.</b>
/// A press changes only what THIS client draws. The element force is applied between sensing and
/// publishing in <see cref="ElementMood"/> — the game's element board is read and never written,
/// which matters because that board is a desync invariant the game compares every round (the full
/// citation is in ElementMood, at the override). A haunt force is a shader global; a haunt is not
/// state at all, so a peer keeps computing the real schedule from the shared clock and is
/// unaffected. Nothing here goes on the wire, and nothing here is a game action.</para>
///
/// <para><b>NO NEW CONFIG ENTRIES, deliberately.</b> The ten-file localisation footprint an option
/// costs (bound description, German description, catalog name, curated row, dependency rule, …) buys
/// nothing here: an action is not a value, it has nothing to persist, and a hold duration on a dial
/// would be one more thing that can be wrong when the tester reports what they saw. Both durations
/// are constants with their reasoning written next to them
/// (<see cref="ElementMood.ForceSeconds"/>, <see cref="Haunt.ForceHoldSeconds"/>). What this page
/// does add to the Loc tables is captions and hints, exactly like every other row in this menu.</para>
///
/// <para><b>THE PAGE DOES NOT REBUILD ITSELF ON A PRESS</b>, unlike the variant-copy rows, which do.
/// Those change the settings the page is showing, so the page has to be redrawn; these change nothing
/// on screen. Rebuilding would destroy the very button under the pointer between two presses of a
/// button a tester presses repeatedly, and the feedback that matters is not in the menu anyway — it
/// is the environment behind it, and the log line each press writes.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// Caption keys for the six elements, in the GAME'S OWN <c>EElement</c> order (Fire, Ice, Air,
    /// Earth, Light, Dark — ScenarioRuleLibrary/ElementInfusionBoardManager.cs:10-20). The index into
    /// this array IS the element index handed to <see cref="ElementMood.Force"/>, so the order is not
    /// cosmetic: reordering these captions would relabel the buttons without relabelling what they
    /// do. It is also the order the element strip and the mood channel already use, so a tester
    /// reading the log sees the same sequence they pressed.
    /// </summary>
    private static readonly string[] ElementCaptionKeys =
    {
        "vr_tt_fire", "vr_tt_ice", "vr_tt_air", "vr_tt_earth", "vr_tt_light", "vr_tt_dark",
    };

    /// <summary>
    /// The page: a way back, what this is, and then the buttons — six elements as Strong, the same
    /// six as Waning, one per apparition, and a stop.
    ///
    /// <para>TWELVE ELEMENT BUTTONS RATHER THAN A PICKER PLUS A STRENGTH CHOOSER. The obvious saving
    /// is one element picker and one Strong/Waning picker over two buttons; it was rejected because
    /// it puts the meaning of a press into invisible state. On a test page the tester has to be able
    /// to say "I pressed Feuer schwindend and saw X" without first checking what two dropdowns were
    /// left on — and a mode that persists between presses is exactly how a tester ends up reporting
    /// the wrong effect. Twelve rows in a scroll pane cost nothing; a wrong bug report costs a
    /// hardware round trip. The two states are separated into two HEADED blocks so that each button
    /// carries only the element name, which keeps the captions short and the page scannable.</para>
    ///
    /// <para>WANING IS THERE BECAUSE IT IS THE ONE THAT CAN BE WRONG. Strong is a constant 1.0 and
    /// either shows or does not; waning is a plateau that BREATHES between 0.28 and 0.52 on the
    /// shared clock, which is the part with a phase, a period and an amplitude to get wrong, and the
    /// part a player is meant to read from across the table.</para>
    /// </summary>
    private static int BuildTestTriggers()
    {
        if (ContentRoot == null)
            return 0;

        // Same way out as a catalog topic has, in the same place, so the page behaves like the rest
        // of the advanced section even though its content does not.
        BuildLinkRow(ContentRoot, "‹ " + Loc.Mod("cat_debug"), () =>
        {
            _view = View.AdvancedIndex;
            TickGuard.Run("VROptionsTab.TriggersBack", Rebuild, "WorldUI");
        });

        BuildHeader(ContentRoot, Loc.Mod("vr_tt_page"), "h_vr_tt_page");

        // THREE NOTES, AND THEY ARE THE FEATURE'S SAFETY RAILS, not decoration:
        //   1. what it does not touch (local only, nothing sent, no game state) — the standing
        //      "everything is synchronised" rule means a reader must be told where the exception is;
        //   2. where it works at all, so an inert press in the main menu is expected rather than a
        //      bug report;
        //   3. that a press overrides the effect's own on/off switch for those seconds — the
        //      alternative was a button that silently refuses, which is indistinguishable from a
        //      broken one (the full argument is at ElementMood.Force and Haunt.Force).
        BuildNote(ContentRoot, Loc.Mod("vr_tt_local"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_scope"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_override"));

        int rows = 0;
        rows += BuildElementTriggers(waning: false, "vr_tt_elem_strong");
        rows += BuildElementTriggers(waning: true, "vr_tt_elem_waning");
        rows += BuildHauntTriggers();

        // ONE PRESS PUTS EVERYTHING BACK. Both overrides expire on their own — that is the invariant,
        // and this row is not what enforces it. It exists so a tester who has seen enough does not
        // have to sit out the remaining seconds before judging the real state, and because "how do I
        // stop this" is the first question a page of triggers raises.
        BuildLinkRow(ContentRoot, Loc.Mod("vr_tt_stop"), () =>
        {
            // Logged HERE rather than only inside the two ClearForce calls: both are idempotent and
            // say nothing when there was nothing to clear, and a press that leaves no trace at all is
            // the one thing this log may not do — "I pressed stop" has to be readable even when the
            // answer is "there was nothing running".
            VRLog.Info("WorldUI", "TEST TRIGGER: 'stop' pressed on the Erweitert test page — element "
                                  + $"force {(ElementMood.Forcing ? "was standing" : "was not standing")}, "
                                  + $"apparition force {(Haunt.Forcing ? "was standing" : "was not standing")}. "
                                  + "Both channels go back to the real state; nothing else is touched.");
            ElementMood.ClearForce("the tester pressed 'stop test triggers'");
            Haunt.ClearForce("the tester pressed 'stop test triggers'");
        }, asAction: true);
        rows++;

        return rows;
    }

    /// <summary>One headed block of six element buttons, all in the same state.</summary>
    private static int BuildElementTriggers(bool waning, string headerKey)
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod(headerKey), "h_" + headerKey, sub: true);

        for (int i = 0; i < ElementCaptionKeys.Length; i++)
        {
            // Captured per row: the loop variable would otherwise be shared by all six closures and
            // every button would force Dark.
            int element = i;
            BuildLinkRow(ContentRoot, Loc.Mod(ElementCaptionKeys[i]),
                         () => ElementMood.Force(element, waning), asAction: true);
        }

        return ElementCaptionKeys.Length;
    }

    /// <summary>
    /// One button per apparition of the room the player is standing in.
    ///
    /// <para>THE LABELS NAME BOTH ROOMS, because one id is two different apparitions. The table is
    /// the bundle's, not this file's — it lives beside each catalogue in
    /// <c>BuildEnvironmentRooms.cs</c> (grep <c>HAUNT FORCE ID TABLE</c>) and in
    /// <c>EnvHaunt.cginc</c>'s contract block. Repeated here as "cellar / forest" per row rather
    /// than switched on the live style: the page must be readable before the room is measured, and
    /// a caption that changes under the tester between two presses is worse than one that says
    /// both. If the catalogues are ever reordered, this list is stale and the log line (which
    /// prints the id) is what catches it.</para>
    ///
    /// <para>ONE ROW SAYS "draws nothing" ON PURPOSE (id 3 in the cellar, the cobweb tremble). A
    /// tester who presses it and sees no apparition would otherwise file a broken button; the
    /// caption tells them to watch the webs instead.</para>
    /// </summary>
    private static int BuildHauntTriggers()
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod("vr_tt_haunts"), "h_vr_tt_haunts", sub: true);
        BuildNote(ContentRoot, Loc.Mod("vr_tt_haunt_note"));

        for (int i = 0; i < Haunt.EventCount; i++)
        {
            int id = i;
            // 1-based in the caption, 0-based on the wire to the shader: the tester counts from one,
            // the card index counts from zero, and the log prints the id so the two can be lined up.
            string caption = Loc.Mod("vr_tt_haunt_n").Replace("{0}", (i + 1).ToString())
                             + " — " + Loc.Mod("vr_tt_haunt_" + i);
            BuildLinkRow(ContentRoot, caption, () => Haunt.Force(id), asAction: true);
        }

        return Haunt.EventCount;
    }
}

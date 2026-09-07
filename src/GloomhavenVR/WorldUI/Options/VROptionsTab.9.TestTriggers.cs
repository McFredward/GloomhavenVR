using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

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
/// <para><b>FOLLOW-UP USER REQUEST</b> (hardware, verbatim): "In der Triggertestview möchte ich wenn
/// ich etwas triggere das es dauerhaft an ist und mit erneutem toggle wieder ausgemacht wird. So kann
/// ich die Mischungen besser testen." <b>EVERY ROW ON THIS PAGE IS NOW A LATCH.</b> A press turns the
/// effect on and leaves it on; the same press again turns it off. Nothing expires — the two timed
/// holds this page used to advertise (8 s for an element, 14 s for an apparition) are gone from the
/// code and from the captions. Three consequences shape the rest of this file:</para>
/// <list type="number">
/// <item><b>MIXTURES.</b> The reason the user gives for wanting the latch is the mixture, so the
/// element force became per-element: any subset of the six can stand at once, in either state. That
/// is a change in <see cref="ElementMood"/>, not here — this page only stopped assuming there is one.
/// </item>
/// <item><b>THE ROWS HAVE TO SHOW THEIR STATE.</b> A fire-and-forget button needs no readout because
/// the press IS the whole interaction; a latch that does not say whether it is on is unusable the
/// moment a second one is pressed, which is exactly the case the user asked for. So every trigger row
/// carries its state in its own caption and is repainted after every press — see
/// <see cref="RefreshLatchRows"/> for why that is a caption repaint and not a rebuild.</item>
/// <item><b>THE TWO HALVES BEHAVE DIFFERENTLY, and the page says so in German rather than hiding
/// it.</b> An element is a STATE, so "permanently on" is literal. An apparition is an EVENT with an
/// authored envelope of a few seconds, so a latched one LOOPS (Haunt.Tick re-anchors it), and only
/// ONE can be latched — the shader's force channel carries a single card id and this lane may not
/// widen it without a bundle re-bake. Pressing a second apparition therefore releases the first. The
/// full argument, including the rejected alternatives, is in Core/Haunt.cs above the loop
/// constants.</item>
/// </list>
///
/// <para><b>IT MUST READ AS A TEST AID, and that is a requirement rather than a matter of taste.</b>
/// The page sits in the same menu as the settings, so a player who wanders in must not come away
/// believing they have changed the game. Three things carry that: the page is called "Test-Auslöser",
/// the first line under the heading states what it does NOT touch (nothing is sent, no game state
/// changes), and every row says whether it is on. Nothing on this page writes a ConfigEntry, so there
/// is also nothing for the player to undo. THE LATCH RAISES THE STAKES ON EXACTLY ONE OF THOSE: an
/// override that no longer expires by itself can be left standing, so the stop row is no longer a
/// convenience but the page's guarantee, and it is stated in the German caption as "everything off".
/// The other two safety rails are unchanged, and every route that tears the environment down
/// (scenario end, VR stand-down, mixed reality, a style change) still drops every latch on its way
/// out — both StandDown implementations clear the force BEFORE their own idempotence guard.</para>
///
/// <para><b>MULTIPLAYER: SYNCHRONISED SINCE 2026-08-15, and this paragraph used to argue the exact
/// opposite.</b> It read: "HARMLESS BY CONSTRUCTION ... A press changes only what THIS client draws
/// ... Nothing here goes on the wire ... a peer cannot tell that a latch is standing at all." The
/// user has ruled otherwise, verbatim: "Auch wenn jemand im Debugmenu ein Event startet sollte dies
/// auch von ALLEN im Multiplayer sichtbar sein statt nur lokal, also synchronisiert werden."</para>
///
/// <para>What the old paragraph got RIGHT is untouched and is why the change cost this file two
/// lines: the element force is still applied between sensing and publishing in
/// <see cref="ElementMood"/>, so the game's element board — a desync invariant it compares every
/// round — is still read and never written, for one latched element or for six; and a haunt force is
/// still only a shader global over a schedule that is not state. Nothing here is a game action and
/// there is still no value the game could desync over. What travels is the LATCH SET (extension
/// record 32, <c>Net/RemoteTestTriggers</c>): a style, a card id, six element bits per column and the
/// press time on the shared clock. Every receiver evaluates that against its own copy of the same
/// clock, so a press is seen by everyone, in the same place, on the same second.</para>
///
/// <para><b>AND THE LOCAL SETTINGS STILL WIN</b> (user ruling, same day: "die lokalen Einstellungen
/// haben Vorrang"). A peer's press reaches this client only if its own environment dial matches
/// theirs and the relevant feature switch is on; otherwise it is a silent no-op, and a player who
/// has switched the horror or the element response off is NOT desynced and is never corrected.
/// The local view is never routed through the network either: the button below calls
/// <c>Force</c> directly and the environment answers on the same frame, exactly as it did before.</para>
///
/// <para><b>NO NEW CONFIG ENTRIES, deliberately — AND ONE EXISTING ONE, WHICH IS NOT THE SAME
/// THING.</b> This page still creates no <c>ConfigEntry</c> of its own: the ten-file localisation
/// footprint an option costs buys nothing for a button, an action is not a value and has nothing to
/// persist, and the durations that were the only tunable numbers here no longer exist. What changed
/// in ModBuild 467 is that ONE dial that already existed came to live on this page —
/// <c>[FigureGrab] OcclusionMapOffOnHeadCamera</c>, the held-prop white-flash A/B. It was bound as
/// a setting because it has to survive the frame it is switched on in, and it was FILED as a
/// setting, which is where it went wrong: <c>ConfigCatalog.TopicOf</c> put it under Erweitert ▸
/// Hände ▸ "Figuren-Offsets" and the user could not find it (verbatim: "Ich konnte die
/// OcclusionMapOffOnHeadCamera in Erweitert nirgends finden, wo ist sie?"). It is not a figure
/// offset; it is a test aid with a visible cost, which is this page's subject exactly.</para>
///
/// <para><b>AND IT IS THE ONE ROW HERE THAT PERSISTS, so the page says so instead of hiding it.</b>
/// The class doc's own rule two paragraphs up — "Nothing on this page writes a ConfigEntry, so there
/// is also nothing for the player to undo" — is no longer true of every row, and a page whose
/// safety rail is a sentence may not leave that sentence stale. The row keeps its own note
/// (<c>vr_tt_occl_note</c>): it states that this one stays on across a restart and what it costs
/// while it is on. It is drawn with the menu's ordinary bool row rather than a latch caption,
/// deliberately: a checkbox is what a persistent value looks like everywhere else in this menu, and
/// dressing a persistent setting up as a fire-and-forget latch is the confusion this page exists to
/// avoid, pointed the other way. The catalog listings do not draw it a second time — see
/// <c>OwnPageRows</c> in <c>VROptionsTab.8.Dependencies.cs</c> for why the hand-off lives in the
/// row filter and not in <c>ConfigCatalog</c>'s withholding tables.</para>
///
/// <para><b>THE PAGE DOES NOT REBUILD ITSELF ON A PRESS</b>, unlike the variant-copy rows, which do.
/// Those change the settings the page is showing, so the page has to be redrawn. Rebuilding would
/// destroy the very button under the pointer between two presses of a button a tester presses
/// repeatedly — and the latch makes that worse rather than better, because now the tester presses
/// several rows in a row to build a mixture. So a press repaints the captions in place instead. The
/// feedback that matters is still not in the menu anyway: it is the environment behind it, and the
/// log line each press writes.</para>
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
    /// The latch rows' captions, each with the delegate that re-reads its live on/off state.
    ///
    /// <para>THE SAME SHAPE THE SETTINGS ROWS ALREADY USE for a live value —
    /// <c>VROptionsTab.2.Rows.cs</c>'s <c>ValueLabels</c> is a list of (label, read) pairs and
    /// <c>Apply</c> repaints all of them after every edit. This page cannot join that list: its rows
    /// have no <c>ConfigItem</c>, and <c>Apply</c> takes one to decide whether the edit also needs a
    /// rebuild. So it keeps its own list of the same shape, repainted by the same rule — ALL of them
    /// after ANY press, never just the row that was pressed, because one press can change another
    /// row: latching Fire·Waning unlights Fire·Strong, latching an apparition unlights whichever one
    /// was latched before, and the stop row unlights everything.</para>
    /// </summary>
    private static readonly List<(TMP_Text label, Func<string> read)> LatchLabels = new(24);

    /// <summary>
    /// The page: a way back, what this is, and then the buttons — six elements as Strong, the same
    /// six as Waning, one per apparition, and an all-off.
    ///
    /// <para>TWELVE ELEMENT BUTTONS RATHER THAN A PICKER PLUS A STRENGTH CHOOSER. The obvious saving
    /// is one element picker and one Strong/Waning picker over two buttons; it was rejected because
    /// it puts the meaning of a press into invisible state. On a test page the tester has to be able
    /// to say "I pressed Feuer schwindend and saw X" without first checking what two dropdowns were
    /// left on. The latch made this decision stronger, not weaker: with mixtures there is no single
    /// "current element" for a picker to point at, and twelve rows that each say AN or aus are the
    /// readout. Twelve rows in a scroll pane cost nothing; a wrong bug report costs a hardware round
    /// trip. The two states are separated into two HEADED blocks so that each button carries only the
    /// element name plus its state, which keeps the captions short and the page scannable.</para>
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

        // The labels of the PREVIOUS visit are about to be destroyed with the page. Repainting a
        // destroyed TMP_Text is a null-reference on Unity's fake-null, and the repaint runs from a
        // button callback where an exception eats the rest of the press.
        LatchLabels.Clear();

        // Same way out as a catalog topic has, in the same place, so the page behaves like the rest
        // of the advanced section even though its content does not.
        BuildLinkRow(ContentRoot, "‹ " + Loc.Mod("cat_debug"), () =>
        {
            _view = View.AdvancedIndex;
            TickGuard.Run("VROptionsTab.TriggersBack", Rebuild, "WorldUI");
        });

        BuildHeader(ContentRoot, Loc.Mod("vr_tt_page"), "h_vr_tt_page");

        // FOUR NOTES, AND THEY ARE THE FEATURE'S SAFETY RAILS, not decoration:
        //   1. what it does and does not touch. It USED to read "local only, nothing sent, no game
        //      state" and be an exception to the standing "everything is synchronised" rule; since
        //      2026-08-15 it is not an exception at all — a press is shared with every player who
        //      has the same environment and the feature on — and what the note has to carry now is
        //      the part that is still true and still surprising: no GAME state changes, and the
        //      other players' own settings still decide what they see;
        //   2. HOW THE ROWS BEHAVE — press = on and stays on, press again = off, several elements at
        //      once. It is the first note a tester needs and the one thing about this page that is
        //      not guessable from a row that says "Feuer"; it is also where the user's own reason
        //      (the mixtures) is answered in the UI rather than only in a comment;
        //   3. where it works at all, so an inert press in the main menu is expected rather than a
        //      bug report;
        //   4. that a press overrides the effect's own on/off switch for as long as the latch stands
        //      — the alternative was a button that silently refuses, which is indistinguishable from
        //      a broken one (the full argument is at ElementMood.Force and Haunt.Force).
        BuildNote(ContentRoot, Loc.Mod("vr_tt_local"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_latch"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_scope"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_override"));

        int rows = 0;
        rows += BuildElementTriggers(waning: false, "vr_tt_elem_strong");
        rows += BuildElementTriggers(waning: true, "vr_tt_elem_waning");
        rows += BuildHauntTriggers();
        rows += BuildDiagnosticSwitches();

        // ONE PRESS PUTS EVERYTHING BACK, AND IT IS NOW LOAD-BEARING. While both overrides expired on
        // their own, this row was a convenience — a tester who had seen enough did not have to sit
        // out the remaining seconds. Nothing expires any more, so this row is the page's guarantee
        // that a tester can always get back to the real state in one press without hunting for which
        // of eighteen rows they left latched. It clears EVERY element latch and the apparition latch
        // together, which is why the German caption says "alles" rather than naming them.
        BuildLinkRow(ContentRoot, Loc.Mod("vr_tt_stop"), () =>
        {
            // Logged HERE rather than only inside the two ClearForce calls: both are idempotent and
            // say nothing when there was nothing to clear, and a press that leaves no trace at all is
            // the one thing this log may not do — "I pressed stop" has to be readable even when the
            // answer is "there was nothing running".
            VRLog.Info("WorldUI", "TEST TRIGGER: 'all off' pressed on the Erweitert test page — element "
                                  + $"latches {(ElementMood.Forcing ? "were standing" : "were not standing")}, "
                                  + $"apparition latch {(Haunt.Forcing ? "was standing" : "was not standing")}. "
                                  + "Both channels go back to the real state; nothing else is touched.");
            ElementMood.ClearForce("the tester pressed 'all test triggers off'");
            Haunt.ClearForce("the tester pressed 'all test triggers off'");
            // …AND EVERY PEER'S TOO. The stop row is this page's guarantee that one press gets back
            // to the real state, and since 2026-08-15 the override is shared — so the guarantee has
            // to reach the people who are seeing it. Taking ownership with an empty set is what
            // makes the net layer transmit its explicit release burst.
            Net.RemoteTestTriggers.NoteLocalPress("all test triggers off");
            RefreshLatchRows();
        }, asAction: true);
        rows++;

        return rows;
    }

    /// <summary>
    /// The diagnostic SWITCHES — the rows on this page that are real settings rather than latches.
    ///
    /// <para>ONE ROW TODAY, and the block exists rather than the row being inlined because the
    /// reason it is here generalises: a diagnostic A/B has to persist across the frame it is armed
    /// in, so it is bound as a <c>ConfigEntry</c>, and being bound is what got the last one filed by
    /// its SECTION into Erweitert ▸ Hände ▸ "Figuren-Offsets" where the user could not find it. The
    /// next one will be bound for the same reason and must not repeat the same filing. Adding it
    /// costs one <c>Lookup</c> line here and one entry in <c>OwnPageRows</c>.</para>
    ///
    /// <para><b>IT IS RESOLVED, NOT ASSUMED.</b> <see cref="Lookup"/> already warns when a curated
    /// key has been renamed out from under the menu, and it warns here too — the row is simply
    /// skipped rather than throwing, which keeps the eighteen latch rows above it working. The block
    /// draws NOTHING at all if the key is gone: a heading and a note over no control would advertise
    /// a switch that is not there, which is the failure this whole page's design keeps rejecting.</para>
    ///
    /// <para><b>THE COST IS IN THE PAGE, NOT ONLY IN THE TOOLTIP.</b> While the occlusion A/B is on,
    /// flames and other effects stop being hidden by walls — the gate it holds down is the game's
    /// GLOBAL master switch for the occlusion term, so every shader that reads it loses the term for
    /// the VR view's pass (PropOcclusionGate's class doc proves that by disassembly). The user was
    /// told this in chat; a tester who reads the menu instead has to be told it there. So the cost is
    /// a NOTE under the heading, in both languages, and not only the hover hint — a hint nobody
    /// hovers is not a warning.</para>
    ///
    /// <para>Live on <c>SettingChanged</c> with no restart: the write goes through the same
    /// <c>ConfigCatalog.ToggleBool</c> every other bool row uses, which sets <c>ConfigEntry.Value</c>
    /// and therefore fires <c>SettingChanged</c>, which <c>BoardModule</c> has wired to
    /// <c>PropOcclusionGate.Sync()</c> since ModBuild 466. The page does not rebuild itself on the
    /// press either — <c>Apply</c> only rebuilds for a variant selector or a dependency parent, and
    /// this key is neither — so the class doc's "no rebuild under the pointer" rule survives.</para>
    /// </summary>
    private static int BuildDiagnosticSwitches()
    {
        if (ContentRoot == null)
            return 0;

        ConfigCatalog.ConfigItem? occlusion = Lookup("FigureGrab", "OcclusionMapOffOnHeadCamera");
        if (occlusion == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod("vr_tt_diag"), "h_vr_tt_diag", sub: true);
        BuildNote(ContentRoot, Loc.Mod("vr_tt_diag_persist"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_occl_note"));
        return BuildItem(occlusion, hintKey: "h_vr_tt_occl");
    }

    /// <summary>One headed block of six element buttons, all latching into the same state.</summary>
    private static int BuildElementTriggers(bool waning, string headerKey)
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod(headerKey), "h_" + headerKey, sub: true);

        for (int i = 0; i < ElementCaptionKeys.Length; i++)
        {
            // Captured per row: the loop variable would otherwise be shared by all six closures and
            // every button would latch Dark.
            int element = i;
            string name = Loc.Mod(ElementCaptionKeys[i]);
            RegisterLatchRow(
                BuildLinkRow(ContentRoot, LatchCaption(name, ElementMood.IsForced(element, waning)),
                             () =>
                             {
                                 ElementMood.Force(element, waning);
                                 // AFTER the force, never before: NoteLocalPress only says "this
                                 // client owns the shared override now", and it publishes whatever
                                 // the two channels hold at that moment. Calling it first would
                                 // broadcast the state before the press.
                                 Net.RemoteTestTriggers.NoteLocalPress($"element {element} "
                                                                       + (waning ? "waning" : "strong"));
                                 RefreshLatchRows();
                             }, asAction: true),
                () => LatchCaption(name, ElementMood.IsForced(element, waning)));
        }

        return ElementCaptionKeys.Length;
    }

    /// <summary>
    /// One button per apparition <b>of the room that is actually standing</b>, and none for the
    /// other room's.
    ///
    /// <para><b>USER REQUEST, verbatim:</b> "Verändere die möglichen Optionen die man triggern kann
    /// in dem Test menu direkt je nachdem welche Umgebung geladen ist statt immer alle Knöpfe
    /// darzustellen." The page used to draw six rows unconditionally and label each one
    /// "cellar / forest", which was wrong twice over the moment the two rooms stopped agreeing:
    /// <see cref="Haunt.CardsIn"/> is 6 in the cellar and 3 in the wood since ModBuild 147, so in the
    /// wood THREE OF THE SIX BUTTONS NAMED CARDS THAT DO NOT EXIST — <see cref="Haunt.Force"/>
    /// refuses them with a silent <c>false</c>, which from the tester's side is a dead button. And a
    /// caption that names two apparitions makes the tester do the room lookup that this code can do
    /// for them.</para>
    ///
    /// <para><b>THE ROW SET IS DECIDED AT BUILD TIME, NOT PER FRAME, and that is what keeps the
    /// page's own rule intact</b> (it must never rebuild itself under the pointer — see the class
    /// doc). The only thing that can change which room is standing is the environment dial, which
    /// lives on a different page and rebuilds this one on the way back; a latch press cannot change
    /// it. So the style is read once, here, and <see cref="RefreshLatchRows"/> keeps repainting
    /// captions in place exactly as before — no row is ever added or removed while the page is
    /// open.</para>
    ///
    /// <para><b>WHEN NO HAUNTED ROOM IS STANDING</b> (Default, OffBlack, mixed reality, the main
    /// menu) the block draws its heading and one note saying so, and NO buttons. Six inert buttons
    /// are indistinguishable from six broken ones; a sentence is not.</para>
    ///
    /// <para>The captions are per room now (<c>vr_tt_hc_*</c> for the cellar, <c>vr_tt_hf_*</c> for
    /// the wood) and the authority for both is still the bundle: each catalogue's own
    /// <c>HAUNT FORCE ID TABLE</c> in <c>BuildEnvironmentRooms.cs</c>. If a catalogue is reordered
    /// these strings go stale and only the log's id will still be right. ONE CELLAR ROW SAYS "draws
    /// nothing" ON PURPOSE (id 3, the cobweb tremble): a tester who presses it and sees no
    /// apparition would otherwise file a broken button.</para>
    ///
    /// <para><b>AND TWO ROWS ARE NOW MISSING RATHER THAN INERT</b> (ModBuild 149). The wood's card 0
    /// and the cellar's card 2 held events the user has had deleted, and their INDICES could not go
    /// with them — the shader's group partition needs a card count that is a multiple of three, so
    /// both survive in the bake as placeholders that draw nothing at all (<see cref="Haunt.IsInert"/>
    /// carries the list and the argument). Those rows are SKIPPED. <b>The numbering is deliberately
    /// NOT closed up:</b> the caption number is <c>id + 1</c>, so the cellar's rows now read 1, 2, 4,
    /// 5, 6 with a gap where the door used to be. A gap looks like a mistake for about a second; a
    /// renumbered list makes the tester's "I pressed 3" refer to a different apparition than the
    /// log's "apparition 3", which costs a whole hardware round.</para>
    ///
    /// <para>THE BLOCK CARRIES ITS OWN NOTE because this half does not behave like the element half
    /// above it, and a tester who discovered that by pressing would file it as a bug: only one
    /// apparition can be latched (the shader's force channel carries one card id), and a latched one
    /// REPEATS rather than standing still (its authored envelope is a few seconds long, so "on"
    /// can only honestly mean "again and again"). Both facts are stated in German rather than
    /// inferred.</para>
    /// </summary>
    private static int BuildHauntTriggers()
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.Mod("vr_tt_haunts"), "h_vr_tt_haunts", sub: true);

        // The room, read ONCE. SkyAlternative.Style is the player's dial rather than what is drawn
        // this frame, and that is the right source for a page that has to be readable in the main
        // menu too: it answers "which room's apparitions would these buttons play", which is exactly
        // what a tester about to start a scenario is asking.
        SkyStyle style = SkyAlternative.Style.Value;
        bool haunted = style == SkyStyle.Cellar || style == SkyStyle.SwampNight;
        if (!haunted)
        {
            BuildNote(ContentRoot, Loc.Mod("vr_tt_haunt_none"));
            return 0;
        }

        bool cellar = style == SkyStyle.Cellar;
        BuildNote(ContentRoot, Loc.Mod(cellar ? "vr_tt_haunt_room_c" : "vr_tt_haunt_room_f"));
        BuildNote(ContentRoot, Loc.Mod("vr_tt_haunt_one"));

        // THE ROOM'S OWN COUNT, never Haunt.EventCount — that constant is the MAXIMUM and is what
        // sizes the figure mask, not what any one room has.
        int count = Haunt.CardsIn(style);
        string prefix = cellar ? "vr_tt_hc_" : "vr_tt_hf_";
        int drawn = 0;
        for (int i = 0; i < count; i++)
        {
            // ...MINUS THE INERT ONES. ModBuild 149 deleted two events on the user's order (the
            // wood's eyeshines and the cellar's stair-top door / moon-pool swell) and neither index
            // could be removed with them — the shader's group partition needs a multiple of three
            // cards per room, so both survive as placeholders that draw nothing (Haunt.IsInert). A
            // button for one of those is the exact failure this page's own doc calls out two
            // paragraphs up: "Six inert buttons are indistinguishable from six broken ones." So the
            // row is not drawn at all, rather than drawn with a caption apologising for itself.
            if (Haunt.IsInert(style, i))
                continue;

            int id = i;
            // 1-based in the caption, 0-based on the wire to the shader: the tester counts from one,
            // the card index counts from zero, and the log prints the id so the two can be lined up.
            string caption = Loc.Mod("vr_tt_haunt_n").Replace("{0}", (i + 1).ToString())
                             + " — " + Loc.Mod(prefix + i);
            RegisterLatchRow(
                BuildLinkRow(ContentRoot, LatchCaption(caption, Haunt.IsForced(id)), () =>
                {
                    Haunt.Force(id);
                    Net.RemoteTestTriggers.NoteLocalPress($"apparition {id}");
                    RefreshLatchRows();
                }, asAction: true),
                () => LatchCaption(caption, Haunt.IsForced(id)));
            drawn++;
        }

        // The COUNT THAT IS RETURNED IS THE COUNT THAT WAS DRAWN, not the room's card count: the
        // caller adds it to the page's row total, which is what the page-built log line reports and
        // what a reader uses to tell "the page came up empty" from "the page came up".
        return drawn;
    }

    /// <summary>
    /// A latch row's caption: what it triggers, plus whether it is on.
    ///
    /// <para>THE STATE IS IN THE CAPTION rather than in a control of its own, and that is the cheapest
    /// HONEST option rather than the laziest one. The obvious alternative is the menu's own toggle
    /// row, and it is wrong twice over: a toggle row's switch is bound to a <c>ConfigEntry</c> that
    /// these rows do not have, and a checkbox in a settings menu reads as "this is a setting you have
    /// changed" — the one impression this page may not leave (see the class doc). The row already is
    /// a button with a hover and a press colour; giving its own label the state keeps it a button.</para>
    ///
    /// <para>ASYMMETRIC BY DESIGN: the on marker is short and upper-case and the off marker is quiet,
    /// so a tester scanning eighteen rows for the three that are lit finds them by shape rather than
    /// by reading. Both markers come from <see cref="Loc"/> like every other user-facing string, and
    /// both are plain letters — no symbol or box-drawing glyph, because the menu's font atlas is the
    /// game's and a missing glyph would render as a replacement box on hardware only.</para>
    /// </summary>
    private static string LatchCaption(string what, bool on) =>
        Loc.Mod(on ? "vr_tt_on" : "vr_tt_off").Replace("{0}", what);

    /// <summary>
    /// Remember a row's caption label so <see cref="RefreshLatchRows"/> can repaint it.
    ///
    /// <para>The label is found the same way the row builders find it — <c>FindPart</c> on the
    /// authored "Title" child, with the type search as its fallback — rather than by taking the first
    /// TMP in the row: an action row still carries the donor's hidden value label and its inactive
    /// gamepad key tip, and painting the caption into one of those would put the state somewhere
    /// nobody can see. A row whose Title cannot be found is simply not registered; it keeps working as
    /// a button and only loses its readout, which is the right way for a game-update surprise to
    /// fail.</para>
    /// </summary>
    private static void RegisterLatchRow(GameObject row, Func<string> caption)
    {
        TMP_Text? label = FindPart<TMP_Text>(row.transform, "Title");
        if (label == null)
            return;

        LatchLabels.Add((label, caption));
    }

    /// <summary>
    /// Repaint every latch row's caption from the live state.
    ///
    /// <para>THIS IS THE WHOLE REASON THE ROWS CAN SHOW STATE WITHOUT A REBUILD, and a rebuild is
    /// explicitly out (it would destroy the very button under the pointer — see the class doc). It is
    /// also why the repaint is driven by the PRESS and not by a per-frame tick: the only things that
    /// can change a latch are the buttons on this page, and this page cannot be open while the routes
    /// that stand the channels down are being taken. A frame hook would cost eighteen delegate calls
    /// and eighteen TMP writes per frame, forever, to catch a case that cannot happen while anyone is
    /// looking. The one state it cannot show is a latch dropped by a stand-down with the page still
    /// open — and a stand-down means the scenario or the environment is gone, at which point the
    /// tester has lost more than a caption.</para>
    ///
    /// <para>Every read is guarded individually: this runs inside a UI button callback, where a throw
    /// swallows the rest of the press, and one unreadable row must not stop the other seventeen
    /// repainting. Same rule the settings rows' repaint already follows.</para>
    /// </summary>
    private static void RefreshLatchRows()
    {
        for (int i = 0; i < LatchLabels.Count; i++)
        {
            (TMP_Text label, Func<string> read) = LatchLabels[i];
            if (label == null)
                continue;
            try
            {
                label.text = read();
            }
            catch (Exception e)
            {
                VRLog.Warn("WorldUI", $"VR options tab: repainting a test-trigger row threw ({e.Message}). "
                                      + "The latch itself is unaffected — only this row's ON/OFF caption "
                                      + "is stale until the next press.");
            }
        }
    }
}

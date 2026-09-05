using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The curated everyday view: the categories of settings a player actually reaches for, chosen by
/// hand, plus "Debug" which still reaches every one of the mod's remaining entries.
///
/// <para>WHY CURATION IS AN EXPLICIT LIST AND NOT A RULE. Every mechanical shortcut tried on this
/// data fails on real entries. Sorting by topic leaves migration flags like
/// <c>TableScaleDefault25Applied</c> and <c>BoardScaleDefault04Applied</c> sitting between
/// "Free Movement" and "Turn Mode" — internal bookkeeping the player must never touch, with names
/// that look like settings. Filtering on a key-name pattern would take <c>DebugGizmos</c> out and
/// leave <c>SavedScaleMultiplier</c> in. The catalog's <c>Pin</c> rank covers only the [Perf]
/// section. So the everyday list is written down, one entry at a time, and everything not on it
/// stays reachable under "Erweitert" rather than being hidden.</para>
///
/// <para>NOTHING IS LOST BY BEING LEFT OFF. That is what makes an explicit list safe here: the
/// advanced view is the complete catalog, so a curation mistake costs one extra click, never
/// access to a setting.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// One curated row: which config entry it edits, and WHAT IT IS CALLED.
    ///
    /// <para>The caption is a localization key rather than the config key, because the config key is
    /// a programmer's name. Left to itself the row reads "Actor Bars", "Action Element Hints",
    /// "Masked Reaim Deadband" — in German too, since the catalog's display name is just the key
    /// with its camel humps spaced out. A player-facing menu has to be in the player's language,
    /// captions included, so every curated row carries its own.</para>
    /// </summary>
    internal readonly struct CuratedEntry
    {
        internal readonly string Section;
        internal readonly string Key;
        internal readonly string CaptionKey;

        /// <summary>English/German caption written HERE instead of in a Loc key — see <see cref="Say"/>.</summary>
        internal readonly string En;
        internal readonly string De;

        /// <summary>
        /// Non-null on an ACTION row: a row that DOES something once instead of editing a
        /// ConfigEntry. It has no <see cref="Section"/>/<see cref="Key"/> at all and is therefore
        /// invisible to <c>Lookup</c>, to <c>ConfigCatalog</c> and to
        /// <c>scripts/check-options-coverage.py</c> — which is correct: there is no setting behind
        /// it to be advertised, curated twice, or split from a family.
        ///
        /// <para><b>WHY AN EVERYDAY TAB GETS ONE AT ALL.</b> The action rows this menu already had
        /// (the variant copies, the test triggers, the cheats) all live under Erweitert, and the
        /// reason was always the same — they are power moves. This one is the opposite: the user
        /// asked for it in exactly these terms (2026-09-05, "einfach nur ein Button 'Spawn
        /// Kampflog' oder so"), and it belongs beside the preference it complements, on the page
        /// where a player looks for the combat log. Filing it under Erweitert would be the
        /// "a setting that cannot be found" mistake with an action instead of a dial.</para>
        /// </summary>
        internal readonly System.Action? OnPress;

        internal CuratedEntry(string section, string key, string captionKey)
            : this(section, key, captionKey, string.Empty, string.Empty)
        {
        }

        internal CuratedEntry(string section, string key, string captionKey, string en, string de)
        {
            Section = section;
            Key = key;
            CaptionKey = captionKey;
            En = en;
            De = de;
            OnPress = null;
        }

        private CuratedEntry(string en, string de, System.Action onPress)
        {
            Section = string.Empty;
            Key = string.Empty;
            CaptionKey = string.Empty;
            En = en;
            De = de;
            OnPress = onPress;
        }

        /// <summary>An action row: caption in both languages, and what pressing it does.</summary>
        internal static CuratedEntry Press(string en, string de, System.Action onPress) =>
            new(en, de, onPress);

        /// <summary>True for a row with no ConfigEntry behind it — see <see cref="OnPress"/>.</summary>
        internal bool IsAction => OnPress != null;

        /// <summary>
        /// The row's label. A local <see cref="En"/>/<see cref="De"/> pair wins over the Loc key,
        /// and an empty result is the documented fall-through to the catalog's display name.
        /// </summary>
        internal string Caption => En.Length > 0 ? Say(En, De) : Loc.Mod(CaptionKey);

        /// <summary>Hint key, derived from the caption key so the two can never drift apart.</summary>
        internal string HintKey => "h_" + CaptionKey;
    }

    /// <summary>
    /// A user-facing string written in THIS file rather than in <c>Core/Loc/Loc.cs</c>.
    ///
    /// <para>WHY THE EXCEPTION EXISTS. Loc.cs is the mod's translation table and remains the right
    /// home for a caption that outlives a restructure. What it is NOT good at is a label whose ONLY
    /// reason to exist is the shape of the tree in this file: renaming a tab then means editing two
    /// files that no checker ties together, and the failure mode is silent — <see cref="Loc.Mod"/>
    /// returns the ID itself when the table misses, so the tab is captioned "cat_environment" and
    /// nothing says so until somebody looks at it in a headset.</para>
    ///
    /// <para>NOT the identically-shaped <c>Text()</c> at the bottom of VROptionsTab.Cheats.cs, and
    /// the duplication is deliberate: that file is built to be DELETED in one step, and a
    /// dependency from the permanent curated tree onto a temporary file would break the build the
    /// day the cheats page is removed.</para>
    /// </summary>
    private static string Say(string english, string german) =>
        string.Equals(Loc.CurrentLanguage, "German", StringComparison.Ordinal) ? german : english;

    /// <summary>A titled block of settings inside a category — the second level of order.</summary>
    internal sealed class CuratedSection
    {
        internal string LocKey = string.Empty;
        internal CuratedEntry[] Entries = System.Array.Empty<CuratedEntry>();

        /// <summary>Optional local label, used in place of <see cref="LocKey"/> — see <see cref="Say"/>.</summary>
        internal string En = string.Empty;
        internal string De = string.Empty;

        internal string Label => En.Length > 0 ? Say(En, De) : Loc.Mod(LocKey);
    }

    /// <summary>One sub-tab: a caption and the sections beneath it.</summary>
    internal sealed class CuratedCategory
    {
        internal string LocKey = string.Empty;
        internal CuratedSection[] Sections = System.Array.Empty<CuratedSection>();

        /// <summary>Optional local label, used in place of <see cref="LocKey"/> — see <see cref="Say"/>.</summary>
        internal string En = string.Empty;
        internal string De = string.Empty;

        internal string Label => En.Length > 0 ? Say(En, De) : Loc.Mod(LocKey);
    }

    /// <summary>
    /// THE EVERYDAY TABS — Komfort, Grafik, Brett &amp; Karten, Tafeln, Avatar &amp; Mehrspieler,
    /// Erweitert — each naming what the player is trying to DO (sit comfortably, make the picture
    /// right, run their play surface, deal with the mod's panels, show up to others, tune).
    ///
    /// <para>2026-08 MENU OVERHAUL (user ruling, verbatim: "Setze erstmal alle Vorschläge zu den
    /// Settings deinerseits so um" — implement every recommendation of the five-part menu audit,
    /// .planning/menu-audit/01..05.md). The structural changes, each traceable to an audit finding:</para>
    /// <list type="bullet">
    /// <item>"Avatar" (4 rows) and "Mehrspieler" (6, two of them duplicates) MERGED into one tab
    /// "Avatar &amp; Mehrspieler" — two mini-tabs whose only glue was a duplicated mask pair were
    /// a navigation crutch, not structure (audit 05, S5). The duplicates are gone with the seam.</item>
    /// <item>"Brett &amp; Karten" is a NEW tab, made of the "Karten &amp; Brett" section that sat
    /// under "Tafeln": whoever tunes how their card fan opens does not look under "panels", and
    /// the section had outgrown a heading (audit 05, S6).</item>
    /// <item>"Debug" is CALLED "Erweitert" now (audit 05, S1): it holds everyday settings'
    /// deep twins, not developer switches, and the code doc always called it the advanced view —
    /// only the label disagreed. The Loc key <c>cat_debug</c> is kept (text-only rename).</item>
    /// <item>Tab ORDER is body → picture → play surface → panels → social → rest: the player works
    /// from "I feel sick / I see badly" toward fine-tuning (audit 05 §2.1).</item>
    /// <item>PROMOTIONS: the audit's NORMAL-audience findings moved into the everyday view
    /// (resolution/MSAA, panel-visibility switches, the 2D screen's size, card size, zoom limits,
    /// hand size, …). Nothing moved OUT of the catalog for it — a curated row is an extra door,
    /// never a wall.</item>
    /// </list>
    ///
    /// <para>2026-08-22 SETTINGS AUDIT, questions (b) and (c) (user, verbatim: <i>"b) Prüfe jede
    /// Einstellung ob du sie User zutrauen würdest, wenn nicht gehören sie in Erweitert."</i> and
    /// <i>"c) Überprüfe die Kategorien und ordne sie eventuell neu wenn du denkst das es intuitiver
    /// und Userfreundlicher wäre."</i> — .planning/settings-audit.md §3 and §4). What changed:</para>
    /// <list type="bullet">
    /// <item>NEW TAB "Umgebung &amp; Ton", holding Umgebung / Grusel / Ton / Sichtbarkeit /
    /// Karte 3D. The mod's entire audio surface was filed under <b>Grafik</b> — and one level down,
    /// under a catalog topic literally named "Bild &amp; Darstellung". Nobody looks for a volume
    /// slider under Graphics.</item>
    /// <item>"Grafik" is CALLED "Bild" and only holds the picture: nine rows in four sections where
    /// it had thirty in four, its "Darstellung" block down from twenty rows of six families to four
    /// rows of one. Same fault, same remedy as audit 05 S7 on Komfort. The Loc key
    /// <c>cat_graphics</c> is kept (text-only rename, like <c>cat_debug</c> → "Erweitert").</item>
    /// <item>SIX ROWS PROMOTED into the everyday view — <c>[Board] AutoFocusOnTurn</c> and
    /// <c>HoverHaptics</c>, <c>[FigureGrab] GrabFigures</c>, the three <c>[Hands] GhostHand*</c>,
    /// <c>[WorldUI] BarFixedSize</c>, <c>[PeerBoardFade] Mode</c> — each perceivable, each harmless
    /// when wrong, each previously reachable only by knowing which Erweitert topic to open.</item>
    /// <item>FIVE ROWS DEMOTED out of it — <c>[WorldUI] PanelSupersampleFactor</c> and
    /// <c>PanelMipLodOffset</c> (invisible engineering trades), <c>TravelButtonOffset{X,Y}</c> (he
    /// asked for them "im debug menu"), <c>[Core] AutoRestartForGraphicsJobs</c> (a one-off boot
    /// behaviour). Each one names the reason at the place it left.</item>
    /// </list>
    ///
    /// <para>2026-09-05 — THE SAME AUDIT, RUN AGAIN BECAUSE IT HAD DECAYED (user, verbatim):
    /// <i>"b) In den Nicht-Erweitert Kategorien sind wieder viel zu viele detaillierte
    /// Einstellungen gelandet. Sowas wie Offsets etc. gehört da nicht hin - Denke immer daran das
    /// die einfachen Spieler die Zielgruppe sind. 'Power-User' können dann in 'Erweitert' gehen."</i>
    /// and <i>"d) Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich
    /// nicht."</i> The word "wieder" is the finding: this list drifts back the same way every time,
    /// by a row being curated at the moment somebody asks about the thing it tunes.</para>
    /// <list type="bullet">
    /// <item>FIFTEEN ROWS DEMOTED, and the shape they share is the one he named. Nine were the
    /// whole <b>"Map-Items in der Hand"</b> heading (<c>[FigureGrab] PropHeld*</c> — three metre
    /// offsets, three degree rotations, three pose booleans that only mean something beside them);
    /// three were the control board's numeric ARRIVAL dials (<c>[Cards] SpawnMaxReachMeters</c>,
    /// <c>SpawnMaxBearingDegrees</c>, <c>SpawnBoardWidthDegrees</c> — read once, at spawn, so
    /// turning one while looking at the board does nothing); and three were single calibration
    /// constants sitting beside the switch they calibrate (<c>[Cards] PokePadPixels</c> in authored
    /// pixels, <c>[Comfort] LaserCarryReelSpeed</c> in m/s, <c>[WorldUI] GrabBarTweenMs</c> in ms).
    /// Every one of the fifteen is on a NAMED Erweitert heading, not in an "Allgemein" grab-bag —
    /// checked per key, and one new heading label was written for the nine
    /// (<c>ConfigCatalog.GroupWordLabel</c> "Prop" → "Map-Items in der Hand").</item>
    /// <item>THE GRAPHICS PRESET IS RETIRED OUTRIGHT, which is the one thing on this page that is
    /// not merely a demotion: "entferne" is not satisfied by a row moving one level down, so
    /// <c>[RenderQuality] QualityPreset</c> left the CATALOG as well (the <c>IsRetired</c>
    /// description marker) and its apply path was deleted with it. The three dials it used to write
    /// — <c>EyeResolutionScale</c>, <c>MsaaLevel</c>, <c>ForceAnisotropic</c> — stay curated and are
    /// the authority; nothing but the player can move them now.</item>
    /// <item>NOT DEMOTED, THOUGH THEY FAIL THE TEST ON THEIR FACE, because a verbatim ruling from
    /// the same player put them here and one ruling does not get quietly overwritten by the next:
    /// <c>[WorldUI] BarHeightOffset</c> (an offset by name — "Weiterhin finde ich den offset für
    /// die healthbar nicht"), the two zoom limits beside the pinch gesture, and the wall
    /// see-through's two doors. They are raised as questions instead.</item>
    /// </list>
    ///
    /// <para>A CURATED ROW IS AN EXTRA DOOR, NEVER A WALL — and that is what makes a demotion
    /// cheap. Nothing leaves the CATALOG when it leaves this list: Erweitert is the catalog's own
    /// index, so a key stops being named here and is on its topic page the same build, with the
    /// same name, the same hover text and the same control. Read every "GONE from the curated page"
    /// note below as "one navigation level deeper", never as "removed".</para>
    ///
    /// <para>Section headers inside a tab are one navigation level cheaper than another tab, which
    /// is why grouping happens there and a new tab has to earn itself.</para>
    ///
    /// <para>ERWEITERT IS THE ODD ONE and gets no hand-picked list at all. Hand-curating several
    /// hundred tuning constants would be guesswork with no user to serve, and any list would rot
    /// silently as entries are added. It is instead the catalog's own topic index: a page of topic
    /// links, one topic at a time behind each. That subdivision maintains itself — a new setting
    /// appears under its topic without anyone remembering to add it — and it is the only place in
    /// the tab where completeness matters more than selection.</para>
    /// </summary>
    internal static readonly CuratedCategory[] Curated =
    {
        // ==================================== KOMFORT =====================================
        new()
        {
            LocKey = "comfort",
            Sections = new CuratedSection[]
            {
                // THE "Tisch & Welt" SECTION IS GONE ENTIRELY, one 2026-08 user ruling at a time,
                // and it is worth writing down that all three rows died of the SAME cause — the
                // player already had a better, direct control for each:
                //   * "Tischgröße" ([Rig] WorldScale) — duplicated the two-hand pinch gesture and
                //     read as having no effect; the entry is a documented legacy no-op now.
                //   * "Weltneigung" ([Rig] WorldTiltDegrees) — removed with the parked world tilt
                //     (VRRigDriver.WorldTilt.cs); reviving that feature restores this row too.
                //   * "Tischhöhe" ([Comfort] TableHeightOffset) — removed with the setting itself:
                //     stick flight (Rig/Flight.cs) plus the world grab let the player put their
                //     eyes anywhere, so a dial that nudged the RECENTER height was a second, worse
                //     way to do it ("durch das freie Bewegen braucht man das nicht mehr").
                // Do not re-create the section for a new table-pose slider: the gesture and the
                // stick are the controls, and this heading is where that lesson was learned.
                //
                // THE OLD 14-ROW "Bewegung & Drehen" BLOCK IS THREE SECTIONS NOW (menu audit 05,
                // S7: fourteen rows from three sense families under one heading; sections are
                // one navigation level cheaper than a tab, so the two missing headings were
                // simply added): Drehen / Fortbewegung / Welt greifen.
                new()
                {
                    LocKey = "sec_turning",
                    Entries = new CuratedEntry[]
                    {
                        new("Comfort", "TurnMode", "turning"),
                        new("Comfort", "SnapTurnDegrees", "vr_o_snapdeg"),
                        new("Comfort", "SmoothTurnSpeed", "vr_o_smoothspeed"),
                        new("Comfort", "TurnHand", "vr_o_turnhand"),
                    },
                },
                new()
                {
                    LocKey = "sec_locomotion",
                    Entries = new CuratedEntry[]
                    {
                        new("Comfort", "FlightEnabled", "vr_o_flight"),
                        new("Comfort", "FlightDirection", "vr_o_flightdir"),
                        new("Comfort", "FlightMaxSpeed", "vr_o_flightspeed"),
                        new("Comfort", "FlightHand", "vr_o_flighthand"),
                        // EMPTY CAPTION KEY ON PURPOSE, and it is the documented degradation rather
                        // than an omission: Caption(item, "") falls through to item.Display, i.e.
                        // Loc.ConfigNames' "Hoch/Runter am Drehstick", and HintKey "h_" misses so
                        // the tooltip falls through to the bound description. Both fallbacks are
                        // the ones VROptionsTab.2.Rows already relies on for every uncurated row.
                        new("Comfort", "TurnStickVertical", ""),
                        new("Comfort", "FreeMovement", "free_movement"),
                        // PROMOTED from Erweitert ▸ Brett & Zielen (2026-08-22 settings audit,
                        // question (b) — "Prüfe jede Einstellung ob du sie User zutrauen würdest").
                        // A camera that moves on its own is THE classic VR-comfort complaint, and
                        // this is the switch for it: the view snaps to whichever character is at
                        // turn. It belonged on a locomotion page from the day it was bound and was
                        // instead three levels deep under a topic named after the board. Harmless
                        // wrong (you pan back by hand), perceivable instantly, so it passes the
                        // test the question sets. Empty caption key — [Board] AutoFocusOnTurn's
                        // localized name ("Automatisch zum Character am Zug") already says it.
                        new("Board", "AutoFocusOnTurn", ""),
                    },
                },
                new()
                {
                    LocKey = "sec_worldgrab",
                    Entries = new CuratedEntry[]
                    {
                        new("Comfort", "WorldGrabEnabled", "world_grab"),
                        new("Comfort", "VerticalDrag", "vr_o_vdrag"),
                        new("Comfort", "RotateEnabled", "vr_o_rotate"),
                        new("Comfort", "ScaleEnabled", "vr_o_scale"),
                        // THE TWO ZOOM BOUNDS WENT BACK TO ERWEITERT (2026-09-05). Audit 05 §2.1
                        // had promoted them as "direct neighbours of 'Welt skalieren'", and the
                        // user has now ruled against exactly that argument, asked as a question:
                        // "Ja auch die Höhe der Lebensbalken und die Grenzen sind
                        // Experteneinstellungen und gehören in Erweitert." They are raw scale
                        // factors on a gesture that already has its own floor and ceiling in
                        // practice — a player discovers the reachable zoom by pinching, not by
                        // typing a multiplier. The GESTURE's own switch ("Welt skalieren") stays
                        // here; only its two numeric clamps left.
                        new("Comfort", "RecenterHoldSeconds", "vr_o_recenterhold"),
                        // Keep-your-place after taking the headset off and re-donning it —
                        // promoted per ruling 20 (audit 03 UNCERTAIN, resolved NORMAL).
                        new("Comfort", "KeepPlaceOnReorigin", "vr_o_keepplace"),
                    },
                },
                new()
                {
                    // User request: the wall see-through is a comfort-relevant, user-facing
                    // feature and must be findable HERE, not only under Grafik (where it also
                    // stays — same ConfigEntry, both rows drive the one live value).
                    //
                    // THE 2026-08-22 AUDIT PROPOSED DISSOLVING THIS SECTION (its Finding 4: two
                    // rows do not need a heading inside a five-section tab) and moving both rows
                    // into the new "Umgebung & Ton" tab. THAT HALF WAS OVERRULED and this section
                    // stands unchanged, because the ruling it exists for is explicit — "the wall
                    // see-through … must be findable HERE, not only under Grafik" — and a tab
                    // called "Umgebung & Ton" is not "here". What the audit's Sichtbarkeit section
                    // DID get is the row that used to sit on the Grafik page: the second door
                    // simply moved one tab over with the rest of the world block, so [Compat]
                    // WallFade still has exactly two curated rows, not three. Both write the same
                    // ConfigEntry, so the duplicate costs nothing but a line in this file.
                    // THE SECOND COPY IS GONE (2026-09-05, user: "Entferne die Dopplung.").
                    // These three rows stood a second time under Umgebung & Ton ▸ Sichtbarkeit —
                    // a duplicate that existed only as the descendant of the row that used to sit
                    // on Grafik ▸ Darstellung and travelled with the world block. THIS copy is the
                    // one that survives because it is the one an explicit ruling put here ("the
                    // wall see-through … must be findable HERE, not only under Grafik"), and the
                    // page that ruling was measured against no longer holds the world block at
                    // all. Both copies wrote the same ConfigEntry, so nothing changes but where
                    // the row is found — and it is found where he asked for it.
                    LocKey = "sec_visibility",
                    Entries = new CuratedEntry[]
                    {
                        new("Compat", "WallFade", "wall_see_through"),
                        // Fort superstructures fade with the walls — ruling 18 put the switch
                        // directly beside the walls-transparent toggle it extends. Folds under
                        // WallFade (VROptionsTab.8.Dependencies.cs).
                        new("WallFade", "StackedShellFade", "vr_o_stackedfade"),
                        new("WallFade", "WalkInStandDown", "vr_o_walkin"),
                        // [WallFade] SyncPeerFades MOVED to Avatar & Mehrspieler ▸ Zusammen
                        // spielen (user item 2, 2026-08-11: "Mach alle Einstellungen die mit dem
                        // Multiplayer zu tun haben … auch in den Multiplayer Tab. zB. 'Wände mit
                        // Spielern synchronisieren' sollte genau da verortet sein." — "put every
                        // setting that has to do with multiplayer into the multiplayer tab too.
                        // E.g. 'synchronise walls with players' belongs exactly there.") — moved,
                        // not duplicated.
                    },
                },
                new()
                {
                    LocKey = "sec_hands_aim",
                    Entries = new CuratedEntry[]
                    {
                        // The controls lesson. It belongs on the FIRST page a newcomer
                        // opens, next to the other "how do my hands work" rows, because
                        // that is where somebody goes when the controls are the problem.
                        new("Compat", "ControlsLesson", ""),
                        new("Hands", "PrimaryHand", "vr_o_primaryhand"),
                        // The grip-gated fingertip hex touch — an INPUT decision the tutorial
                        // teaches by name, so it belongs on the everyday list next to the laser
                        // rows rather than only under "Erweitert" ([Board] section, not [Hands]:
                        // curated rows are grouped by what the player is doing, not by cfg file).
                        new("Board", "TouchTilesWithFingertip", "vr_o_fingertiptouch"),
                        new("Hands", "ScrollWithStickOnly", "vr_o_stickscroll"),
                        new("Hands", "LaserFingerOrigin", "vr_o_laserorigin"),
                        // ACCESSIBILITY (ruling 15): the grip-plateau remap surfaces as an
                        // everyday option under an accessibility-flavoured name — a player with
                        // a weak grip lowers it so a partial squeeze already counts as a fist.
                        // The caption says whom it is for; the raw calibration framing stays on
                        // the Erweitert twin ("Vollgriff ab Griffwert").
                        new("Hands", "CurlInputFullAt", "vr_o_curlassist"),
                        // PROMOTED from Erweitert ▸ Brett & Zielen (audit question (b)): controller
                        // rumble when the target under your finger changes. Every game exposes its
                        // haptics as a taste setting, the worst case of getting it wrong is a
                        // quieter controller, and it belongs with the aiming rows it fires from.
                        // Empty caption key — "Vibration bei Wechsel" is already the row's name.
                        new("Board", "HoverHaptics", ""),
                    },
                },
            },
        },
        // ====================================== BILD ======================================
        // THE TAB IS CALLED "Bild" NOW, AND IT ONLY HOLDS THE PICTURE (2026-08-22 settings audit,
        // question (c), user verbatim: "Überprüfe die Kategorien und ordne sie eventuell neu wenn
        // du denkst das es intuitiver und Userfreundlicher wäre." — he chose the audit's FULL
        // proposal over the cheap one).
        //
        // WHAT WAS WRONG. "Grafik ▸ Darstellung" had grown to twenty rows from SIX unrelated
        // families — render quality, floated-window sharpness, game-compat fixups, which world you
        // stand in, that world's mood, and the mod's entire SOUND surface. That is precisely the
        // fault an earlier audit fixed one tab over (05, S7: "fourteen rows from three sense
        // families under one heading" became Drehen / Fortbewegung / Welt greifen) grown back here.
        // The sharpest edge of it: FOUR of those rows were volume and ambience dials. Nobody looks
        // for a volume slider under "Graphics", and in Erweitert it was worse — [EnvSound] rides
        // rig.cfg, so the catalog filed it under a topic literally named "Bild & Darstellung".
        //
        // WHAT MOVED, AND WHAT A PLAYER GAINS BY IT: the world block (Umgebung, Grusel, Ton,
        // Sichtbarkeit, Karte 3D) is the next tab down, so a player looking for sound finds a
        // heading that says Ton, and a player looking for the picture finds nine rows that are all
        // about the picture. The LocKey stays "cat_graphics" so nothing that references the tab has
        // to move; only the label changed (Loc.cs, same trick as cat_debug → "Erweitert").
        new()
        {
            LocKey = "cat_graphics",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "sec_presentation",
                    Entries = new CuratedEntry[]
                    {
                        // "Grafik-Voreinstellung" ([RenderQuality] QualityPreset) STOOD HERE, FIRST,
                        // from 2026-08-23 to 2026-09-05 — Qualität / Ausgewogen / Leistung /
                        // Schwache Hardware / Eigene, as a localized dropdown, writing MSAA, eye
                        // resolution and the per-pixel light cap in one press.
                        //
                        // THE WHOLE OFFERING IS RETIRED, by user ruling (2026-09-05, verbatim):
                        // "Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich
                        // nicht." THIS IS THE ONE DEMOTION IN THIS FILE THAT IS NOT ALSO A DOOR ONE
                        // LEVEL DEEPER: deleting the row alone would have left the dropdown on
                        // Erweitert ▸ Bild & Darstellung, which does not satisfy "entferne". So the
                        // ENTRY left the catalog too, by the marker ConfigCatalog.IsRetired reads —
                        // its bound description opens "LEGACY — no effect" now (Rig/RenderQuality.cs)
                        // — and every apply path went with it, so nothing can write the three dials
                        // below except the player.
                        //
                        // THE THREE DIALS ARE UNCHANGED AND ARE THE AUTHORITY, which is the half of
                        // this that matters: no shipped value moved, Defaults.PixelLightCount stays
                        // 0, and the preset's own key stays bound at its old default so an existing
                        // dev.gloomhavenvr.rig.cfg is not rewritten. Do not re-create the row.
                        // The two dials every headset owner looks for first (audit 03: the
                        // Grafik tab had NO render-quality row at all since the old panel's
                        // preset cycle lost its caller) — plus the two pure quality raises.
                        //
                        // THE RESOLUTION ROW IS REACHABLE AND WAS ALL ALONG — checked against the
                        // ModBuild 226 log rather than assumed, because the report above reads like
                        // a missing row and is not one. It has sat on this page, second from the
                        // top of the mod's most prominent settings tab, since the 2026-08 overhaul.
                        // What his session shows is that the row never MOVED: [RenderQuality]
                        // EyeResolutionScale read 1.00 in all thirty EYE-TARGET DIAG blocks and the
                        // log carries zero "Eye render resolution scale asserted" lines. Whatever
                        // resolution he changed was upstream of the mod (the game's own options
                        // page, or Virtual Desktop's slider), and until this build the log had no
                        // sentence that could tell him so. It has one now, on every rig build.
                        new("RenderQuality", "EyeResolutionScale", "vr_o_eyeres"),
                        new("RenderQuality", "MsaaLevel", "vr_o_msaa"),
                        new("RenderQuality", "ForceAnisotropic", "vr_o_aniso"),
                        // [RenderQuality] PixelLightCount STOOD HERE under ruling 19 ("the
                        // pixel-light cap is a visible look-vs-frames trade the game itself never
                        // exposes in VR"). IT IS OFF THE CURATED PAGE SINCE 2026-08-23, by a newer
                        // ruling from the same player, verbatim: "Die Pixellichter option ist zu
                        // gefährlich für normale Nutzer, sie sollte in Erweitert verschwinden und
                        // per default auch in allen Graphik-Voreinstellungen auf 0 geschaltet sein."
                        // Both halves shipped together: Defaults.PixelLightCount is 0 now and all
                        // four entries of RenderQuality.Presets carried 0, so the everyday player
                        // already HAS the cheap setting and never has to find this row — while
                        // raising it, which is the dangerous direction, costs one navigation level.
                        // THE SECOND HALF IS NOW SATISFIED BY THERE BEING NO PRESETS AT ALL (user
                        // ruling 2026-09-05, the tombstone at the top of this section): the preset
                        // table is deleted, so nothing but the player can hand out a pixel light.
                        // THE FIRST HALF IS UNTOUCHED AND MUST STAY SO — Defaults.PixelLightCount
                        // is still 0 and that is what the shipped install runs.
                        //
                        // NOTHING IS LOST AND NOTHING HAD TO MOVE, and unlike the notes below this
                        // was VERIFIED rather than trusted: ConfigCatalog enumerates every BOUND
                        // entry of every module file (Rebuild → Describe) and filters on exactly two
                        // things, IsRetired (a description prefix marker this entry does not carry)
                        // and the NotOffered table (which holds no RenderQuality key). The module is
                        // "rig" — [RenderQuality] rides dev.gloomhavenvr.rig.cfg — so TopicOf sends
                        // it to ConfigTopic.Visual, i.e. Erweitert ▸ "Bild & Darstellung", under the
                        // "cfg_sec_renderquality" heading its six siblings already produce (well
                        // past ConfigCatalog.MinClusterSize, so it is a heading of its own and not
                        // the "Allgemein" collector). Its stepper is unchanged: ConfigSteps keeps
                        // ["RenderQuality/PixelLightCount"] = 1d, a light at a time.
                        // FOUR ROWS, ONE FAMILY, AND THAT IS THE WHOLE SECTION NOW. Everything
                        // below this line used to be in it; see the tab header for where each part
                        // went and why.
                        //
                        // "Post-Processing aus" ([Compat] DisablePostProcessing) and
                        // "Volumennebel aus" ([Compat] DisableVolumetricFog) stood here. They are
                        // OFF THE CURATED PAGE since the 2026-08-22 settings audit, by the user's
                        // ruling on its open question 4. Both are START-UP-ONLY, so flipping one
                        // appears to do nothing — which reads as a broken row on the most
                        // prominent page in the menu — and both sit on the stereo-hazard side of a
                        // trade a player has no way to see (PPv2 is unverified under stereo
                        // rendering; the fog is a known stereo hazard). NOTHING IS LOST AND
                        // NOTHING HAD TO MOVE: Erweitert is the catalog's own index, so both are
                        // on Erweitert ▸ "Bild & Darstellung" the moment this list stops naming
                        // them. Same shape as the [MapRoom] size dials below.
                        //
                        // THE WHOLE WORLD BLOCK MOVED ONE TAB DOWN (audit question (c)):
                        // [Compat] WallFade, [Sky] Style, the campaign-map switch ([Rig]
                        // Vanilla2DMap since ModBuild 230, Experimental3DMap before it), [WorldUI]
                        // MapRoomHand, [Elements] ×2, [Haunt] ×2 and [EnvSound] ×2 are the
                        // "Umgebung & Ton" tab now, under headings that name what they are. They
                        // are the same ConfigEntries and the same order; only the page changed.
                        // THE MAP ROOM'S FIVE SIZE DIALS USED TO SIT HERE — [MapRoom] IconScale /
                        // CityIconScale / GloomhavenIconScale / PartyMarkerScale / PathWidthScale,
                        // promoted onto this page at ModBuild 193 and 194 so the "separat
                        // justieren" report could compare them side by side.
                        //
                        // THEY ARE GONE FROM THE CURATED PAGE AGAIN (user ruling, hardware:
                        // "Symbolgrößen gehören ins ERWEITERT Menü!"). Nothing is lost by it and
                        // nothing had to move: Erweitert is the catalog's own index, so every one of
                        // the five is on Erweitert ▸ "Bild & Darstellung" the moment this list stops
                        // naming them — and now that [MapRoom] contributes FIVE entries it clears
                        // ConfigCatalog.MinClusterSize and gets its own heading there instead of
                        // being swept into the "Allgemein" collector, which is what made a curated
                        // row the rescue back at 193. The five stay adjacent, in the same reading
                        // order, one navigation level deeper.
                        //
                        // They still fold under the campaign-map switch wherever they are shown
                        // ([Rig] Vanilla2DMap == Off since ModBuild 230; VROptionsTab.8
                        // .Dependencies, DependentSections claims the whole [MapRoom] section), so
                        // on a flat-map install they are off screen exactly as before — note that
                        // "a flat-map install" is now the OPT-OUT rather than the default, so these
                        // five are on screen for most players where before they were hidden.
                        //
                        // DO NOT RE-PROMOTE without a fresh ruling: this is the second placement of
                        // the same family, and the user named the destination himself.
                        //
                        // THE TRAVEL-CONFIRM BUTTON'S TWO PLACEMENT DIALS (ModBuild 194) STOOD
                        // HERE AND ARE GONE FROM THE CURATED PAGE — because that is where he asked
                        // for them, and this page was never it. His words, both halves: "Geb mir
                        // dann IM DEBUG MENU die offsets um ihm zu verschieben - ich stell es
                        // selber ein." The 194 build put them on the most prominent curated page
                        // in the mod instead; that is the same misplacement the "Symbolgrößen
                        // gehören ins ERWEITERT Menü!" ruling corrected two entries above, on the
                        // same feature, in the same build.
                        //
                        // WHAT IS UNTOUCHED, and must stay untouched: both DEFAULTS are 0 and both
                        // RANGES are unchanged. Three solved placements were rejected in a row
                        // (191/192/193), the mod ships the ModBuild 190 pose — which 0/0
                        // reproduces exactly — and he does the moving. Only the LOCATION of the
                        // rows was ever on the table.
                        //
                        // AND THEY ARE NOT LOST: Erweitert ▸ Menüs & Tafeln lists both by hand
                        // under a "Karte 3D" heading beside [WorldUI] MapRoomHand
                        // (VROptionsTab.7.TopicTrees.cs), and they keep their ◀/▶ stepper —
                        // PrefersStepper below is unchanged, per his other ruling on these two
                        // ("nicht Schieberegler, sondern die Pfeile").
                        //
                        // ELEMENT MOOD, HAUNT AND ENV SOUND stood here too and are the new
                        // "Umgebung & Ton" tab's Umgebung / Grusel / Ton sections. The reason is
                        // question (c) and it is the sharpest single finding of the whole audit:
                        // the mod's ENTIRE audio surface was filed under "Grafik".
                        //
                        // THE ROOM TONE ROWS ARE GONE ALTOGETHER, and this note is here so nobody
                        // re-adds them from the ModBuild 154 request. "EnvSound/AmbienceBed" and
                        // "EnvSound/AmbienceBedGain" sat here from 154 to 222 and were deleted at
                        // ModBuild 223 with the two continuous room tones they switched and scaled
                        // — user, 2026-08-22: "Im Keller hören sich die Geräusche an wie Rauschen
                        // bei nem Fernseher" and "Statt generrell durchgehende sounds zu machen
                        // lieber die Tierrufe". The ruling and the measurements are in
                        // Core/EnvSound.cs's THE ROOM TONES, DELETED.
                        // "Forward-Rendering" ([Rig] ForwardRendering) stood here — the single
                        // most dangerous row the 2026-08-22 settings audit found, because it was
                        // CURATED. Its off state reinstated the documented see-through-walls
                        // defect the forward path exists to fix AND silently killed the MSAA row
                        // above it, at the next start, from a page that looks like quality
                        // sliders. The user's ruling on the audit's open question 4 was to delete
                        // it outright: it is a constant now (Plugin.ForwardRendering), so this is
                        // not a row that moved to Erweitert — there is no key left to show.
                        // "Hauptmenü in VR" ([Rig] MenuRig) is GONE with its dial (user ruling
                        // 2026-08-13): off built no rig at all outside a scenario — the main
                        // menu had nothing to hang on. Unconditional now.
                    },
                },
                new()
                {
                    // THE FLOATED WINDOWS' SHARPNESS, its own heading since the 2026-08-22 audit.
                    // Two rows, not four: the SWITCH and the SIZE stay curated; the two calibration
                    // dials behind them are on Erweitert ▸ Menüs & Tafeln (see below).
                    LocKey = "sec_windows_panels",
                    Entries = new CuratedEntry[]
                    {
                        // THE SWITCH. Put on a curated page at ModBuild 192 for the reason [Rig]
                        // the campaign-map switch was at 176: the dial existed, was bound and was wired,
                        // and was still unreachable in practice. ModBuild 191 shipped the
                        // supersample pair OFF by default and the hardware log of the session
                        // testing it contains zero PANEL SUPERSAMPLE lines — the path never ran,
                        // because the only place the switch appeared was the raw catalog under a
                        // spaced-out English key. A measure nobody can turn on measures nothing.
                        //
                        // EMPTY CAPTION KEYS, the documented degradation the [Comfort]
                        // TurnStickVertical row already relies on: Caption(item, "") falls
                        // through to item.Display, i.e. the localized name in Loc.ConfigNames
                        // ("Fenster: scharf zeichnen"), and HintKey "h_" misses so the tooltip
                        // falls through to the bound description's German translation in
                        // Loc.ConfigDescriptions.German. Both strings are written for a player
                        // and both are already localized, so a hand-written caption here would
                        // only be a second place to keep the same words in step.
                        new("WorldUI", "PanelSupersample", ""),
                        // AND THE SIZE. [WorldUI] WindowLegibility is the other way to spend
                        // headset pixels on a window, and the two belong on one screen.
                        new("WorldUI", "WindowLegibility", ""),
                        // AND THE DISTANCE — new in ModBuild 230, on this page and not on
                        // Bewegung, because the player's sentence for it is about a WINDOW ("die
                        // Fenster die man mit dem Laser festhält … zu einem ziehen"), and this is
                        // the page he is already on when he is thinking about how a window reads.
                        // It is a locomotion row only in the sense that it TAKES a stick axis from
                        // the two flight rows on Bewegung while a window is held; that trade is
                        // stated in its own German description rather than by filing it next to
                        // the rows it overrules, where it would read as a flight setting and be
                        // folded under [Comfort] FlightEnabled — which it must NOT be, since it
                        // has to stay reachable for a player who flies with neither stick.
                        // Same empty caption keys, same documented degradation as above.
                        new("Comfort", "LaserCarryReel", ""),
                        // "Ziehgeschwindigkeit (m/s)" ([Comfort] LaserCarryReelSpeed) STOOD HERE
                        // and is GONE from the curated page — 2026-09-05 ruling (b), verbatim:
                        // "In den Nicht-Erweitert Kategorien sind wieder viel zu viele detaillierte
                        // Einstellungen gelandet. Sowas wie Offsets etc. gehört da nicht hin."
                        // It is the CALIBRATION of the switch on the line above it, in metres per
                        // second, and this section already retired two dials for exactly that shape
                        // (the two immediately below). The SWITCH stays; its speed constant does
                        // not. NOTHING IS LOST AND NOTHING HAD TO MOVE: [Comfort] holds 22 keys,
                        // under ConfigCatalog.SectionSplitThreshold, so the whole section is ONE
                        // group under Erweitert ▸ Bewegung and the speed lands directly beside the
                        // switch it belongs to, one navigation level deeper.
                        //
                        // ITS TWO CALIBRATION DIALS ARE OFF THE CURATED PAGE (2026-08-22 settings
                        // audit, question (b): "Prüfe jede Einstellung ob du sie User zutrauen
                        // würdest, wenn nicht gehören sie in Erweitert."). Both fail that test in
                        // the same way — they are engineering trades whose PRICE is invisible in
                        // the headset:
                        //   * "Fenster: Schärfegrad" ([WorldUI] PanelSupersampleFactor) buys a
                        //     little sharpness for FOUR TIMES the memory per doubling — 20–90 MB
                        //     per window, up to seven windows open at once. A player cannot see
                        //     VRAM exhaustion coming and cannot recover from it by looking at the
                        //     row that caused it. The SWITCH above stays; this was its calibration.
                        //   * "Fenster: Nachschärfen (Filter)" ([WorldUI] PanelMipLodOffset) buys
                        //     half a mip level and its own text names the cost: "THE PRICE IS
                        //     ALIASING". ModBuild 204 took the shipped value back to 0 because the
                        //     dial handed back roughly half of the one fix that closed the STILL
                        //     case. That is not a trade a player has any way to judge.
                        // A CURATED ROW IS AN EXTRA DOOR, NEVER A WALL: nothing left the catalog
                        // here. Both are on Erweitert ▸ Menüs & Tafeln, listed by hand right beside
                        // the two rows above (VROptionsTab.7.TopicTrees.cs), so the whole family is
                        // still on one screen — one navigation level deeper.
                    },
                },
                new()
                {
                    // The mod's own panel offered Mixed Reality here, with a key-colour picker.
                    // Rig/VoidColor is NOT the same setting and does not replace it: MR also
                    // disables every skybox and sweeps the sky geometry, which is the half that
                    // makes passthrough compositing work at all.
                    LocKey = "mixed_reality",
                    Entries = new CuratedEntry[]
                    {
                        new("MixedReality", "Enabled", "vr_o_mrenabled"),
                        new("MixedReality", "KeyColor", "key_color"),
                    },
                },
                // THE "Leistung" SECTION IS GONE, both its rows with it, and the second one is the
                // 2026-08-22 audit's question (b) applied to the last member of a family:
                //
                //   * "Parallele Bildabgabe" ([Core] EnableGraphicsJobs) left at the same audit by
                //     the user's ruling on open question 4. It is already on, so there is nothing
                //     to gain by touching it and the single largest performance finding of the
                //     whole project to lose; it writes boot.config and needs a restart, so the row
                //     cannot even show what it did.
                //   * "Automatisch neu starten" ([Core] AutoRestartForGraphicsJobs) followed it.
                //     It is a dependent of the row above, it is START-UP-ONLY, and what it decides
                //     is whether ONE automatic relaunch happens — a boot behaviour the player meets
                //     exactly once, before they have ever opened this menu. A heading with a single
                //     row about something that already happened is not a settings page.
                //
                // Both are on Erweitert ▸ System & Start, side by side, which is where a switch
                // about how the engine boots belongs. A curated row is an extra door, never a wall.
                new()
                {
                    // Ruling 11: what the desktop monitor mirrors is an everyday choice for
                    // anyone with a spectator at the desk or a stream running — one row, its
                    // own heading, because it is about the MONITOR and nothing above is.
                    LocKey = "vr_sec_monitor",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "DesktopMirrorLeftEye", "vr_o_mirroreye"),
                    },
                },
            },
        },
        // ================================= UMGEBUNG & TON =================================
        // NEW TAB (2026-08-22 settings audit, question (c); the user chose the full proposal over
        // the cheap one). It answers the audit's two worst structural findings at once:
        //
        //   FINDING 2 — THE MOD'S ENTIRE AUDIO SURFACE WAS FILED UNDER "GRAFIK". Five sound
        //   settings exist and all five were somewhere else: four [EnvSound] rows on Grafik ▸
        //   Darstellung and [Cards] CardSoundsEnabled under Brett & Karten ▸ Karten. Nobody looks
        //   for a volume slider under Graphics. One level down it was worse: [EnvSound] binds on
        //   rig.cfg, so ConfigCatalog.TopicOf filed it under a topic literally named "Bild &
        //   Darstellung" / "Picture & rendering", and there was no "Ton" anywhere in the menu.
        //   Both halves are fixed — the section below, and a real ConfigTopic.Sound.
        //
        //   FINDING 1 — "Grafik ▸ Darstellung" WAS A TWENTY-ROW GRAB-BAG of six families, the
        //   exact fault an earlier audit fixed on Komfort (05, S7) grown back one tab over. Taking
        //   the world out of it halves it.
        //
        // WHY A TAB AND NOT A SECTION. A section is one navigation level cheaper than a tab, so a
        // new tab has to earn itself — this one carries FIVE headings and twelve rows that share a
        // subject nothing else on the picture page shares: not how the world is DRAWN, but which
        // world you are in, what mood it is in, what it sounds like, what you can see THROUGH, and
        // the 3D map you travel it on. Tab order keeps the audited principle (body → picture →
        // world → play surface → panels → social → tuning).
        new()
        {
            LocKey = "cat_environment",
            // THE TAB IS CALLED "Umgebung" / "World" NOW — ONE WORD (user report 2026-09-02,
            // verbatim: "aktuell gibt es ein Tab der heißt ('Umgebung // & // Ton) // repräsentieren
            // hier Zeilenumbrüche", i.e. it was rendering as THREE lines with a lone ampersand in
            // the middle).
            //
            // The mechanical half of that report is fixed in the layout and not here — see
            // VROptionsTab.2.Rows.NoOrphanCaption, which fuses a one-character or symbol-only word
            // to its neighbour with U+00A0 so NO caption, present or future, can strand one on a
            // line. This rename is the other half, and it is worth stating why both were needed:
            // the Loc string ALREADY carried the authored break "Umgebung &\nTon", and the 210 px
            // column still could not hold the line "Umgebung &" at the fitted size, so TMP broke
            // the authored line a second time. The rule stops the ampersand being alone; only a
            // shorter name stops the column having to shrink the caption at all.
            //
            // "Umgebung" and not "Umgebung & Ton" shortened: the tab holds Schauplatz / Grusel /
            // Ton / Sichtbarkeit / Karte 3D, and every one of those is a property of the world you
            // are standing in. Sound has its own heading INSIDE, one level cheaper than a tab and
            // exactly where the 2026-08-22 audit put it; the tab name does not have to repeat it.
            En = "World",
            De = "Umgebung",
            Sections = new CuratedSection[]
            {
                new()
                {
                    // WHICH WORLD, AND WHAT MOOD IT IS IN. The environment chooser leads, because
                    // everything under it is a property OF the world it picks — the curated file
                    // has argued exactly that since the element mood arrived ("both are properties
                    // OF the environment the row above chooses") and then left the whole block on
                    // a render page anyway.
                    LocKey = "sec_environment",
                    // "Schauplatz" / "Scenery", not "Umgebung": the TAB is called Umgebung now (see
                    // the note above), and a section that repeats its own tab's name tells the
                    // reader nothing about which of the five headings to read. Schauplatz is what
                    // these three rows actually pick — WHERE the scenario is staged and what mood
                    // it is in. The LocKey is kept so nothing that references the section moves.
                    En = "Scenery",
                    De = "Schauplatz",
                    Entries = new CuratedEntry[]
                    {
                        // The environment choice (user ruling 2026-08-12: real 3D environments
                        // replaced the panorama skyboxes) — a special row (TryBuildSpecialRow) so
                        // the dropdown reads localized ("DnD-Keller") instead of the raw enum
                        // members; its hint states the user's own MR rule ("Bei Mixed Reality ist
                        // der Himmel immer aus."). No dependency-layer entry: a choice row has no
                        // children.
                        new("Sky", "Style", "vr_o_sky"),
                        // ELEMENT MOOD — the environment's answer to the element infusions. The
                        // strength dial folds under the toggle (VROptionsTab.8.Dependencies.cs).
                        new("Elements", "EnvironmentResponse", "vr_o_elemmood"),
                        new("Elements", "ResponseStrength", "vr_o_elemmood_amt"),
                    },
                },
                new()
                {
                    // HAUNT — the creepy easter eggs (user request, 2026-08-14). Its own heading
                    // now rather than four rows deep in a render list: a player who has just
                    // switched the cellar on is exactly the player who wants to know this exists,
                    // and "Grusel" says what it is in one word. The frequency dial folds under the
                    // toggle (VROptionsTab.8.Dependencies.cs).
                    LocKey = "sec_haunt",
                    Entries = new CuratedEntry[]
                    {
                        new("Haunt", "EasterEggs", "vr_o_haunt"),
                        new("Haunt", "Frequency", "vr_o_haunt_freq"),
                    },
                },
                new()
                {
                    // THE MOD'S WHOLE AUDIO SURFACE, ON ONE HEADING, FOR THE FIRST TIME (Finding
                    // 2). What a player gains: a row called "Ton" is where a player looks for
                    // volume, and until now the answer was "under Graphics, below the MSAA level".
                    // The volume folds under the switch (VROptionsTab.8.Dependencies.cs).
                    LocKey = "sec_sound",
                    Entries = new CuratedEntry[]
                    {
                        // ENV SOUND — the environment HEARD (user request, 2026-08-14). The user's
                        // correction the same day put the apparitions' cues on THIS toggle rather
                        // than giving them one of their own, so this switch is also what makes the
                        // haunt block above audible — one heading further down the same page.
                        new("EnvSound", "Enabled", "vr_o_envsound"),
                        new("EnvSound", "Gain", "vr_o_envsound_amt"),
                        // MOVED here from Brett & Karten ▸ Karten (audit question (c)). Ruling 6 is
                        // untouched and is the reason this row exists at all: ONE everyday on/off
                        // for all five card/fan sounds, an AND over the five audio-item strings
                        // that never rewrites them. Rulings 5/7 are untouched too — the five
                        // STRINGS stay on Erweitert ▸ Karten & Fächer ▸ Klänge with their master.
                        // What moved is only the everyday DOOR, from a page about cards to the one
                        // page in the menu that is about sound: a player turning the volume down
                        // and a player turning card clicks off are the same player in the same
                        // moment, and they were two tabs apart.
                        new("Cards", "CardSoundsEnabled", "vr_o_cardsounds"),
                    },
                },
                // A "Sichtbarkeit" SECTION STOOD HERE with the wall see-through and its two
                // companions — the SECOND copy of the three rows in Komfort ▸ Sichtbarkeit. It is
                // gone as a section, heading and all (2026-09-05, user: "Entferne die Dopplung."),
                // because a section that loses every row may not stay as an empty heading. The
                // surviving copy is the Komfort one: that is where his own earlier ruling put it,
                // and the Grafik page this second door was inherited from no longer holds the
                // world block at all. One ConfigEntry, one row again.
                new()
                {
                    // THE CAMPAIGN MAP — the other "which world am I standing in" decision, and
                    // the reason it was curated at ModBuild 176 still holds: without a row it was
                    // unreachable in practice (no display name, so the menu spaced the raw key out
                    // to "Experimental 3D Map") and the user asked where it was.
                    //
                    // ==================================================================
                    //  ModBuild 230 — WHY THIS TAB, AND WHY THE ROW WAS ALREADY HERE
                    // ==================================================================
                    // THE RULING, third clause, verbatim: "Die Einstellung soll außerhalb von
                    // Erweitert einstellbar sein." VERIFIED AGAINST THE CODE RATHER THAN ACTED ON:
                    // the row was ALREADY outside Erweitert at HEAD and had been since ModBuild
                    // 176. This list is what puts a row on a curated tab — VROptionsTab.3.Content
                    // .BuildCurated:146 resolves every CuratedEntry through Lookup and
                    // draws it — and "Rig/Experimental3DMap" was named right here, in this
                    // section, on the "Umgebung & Ton" tab. What was NOT reachable was the row's
                    // MEANING: see the caption note below, which is the defect that made the row
                    // read as a wall of prose rather than as a setting. So the third clause is
                    // answered by keeping the placement and fixing the label, and by writing down
                    // the evidence that the placement works so the next reader does not re-derive
                    // it from the same wrong premise.
                    //
                    // THE MECHANISM, CHECKED END TO END (ConfigCatalog.Rebuild → Describe →
                    // TopicOf, because this file's comments are not evidence about that file):
                    //   1. Rebuild walks every registered module's ConfigFile and calls Describe
                    //      per entry. Describe drops an entry ONLY on two tests: IsRetired (the
                    //      description starts "LEGACY — no effect" / "RESERVED —" / "DEPRECATED —")
                    //      and membership of NotOffered. [Rig] Vanilla2DMap fails both — its bound
                    //      description opens "OFF BY DEFAULT, and off is the 3D map room" and the
                    //      key is not in that table — so it IS in the catalog.
                    //   2. TopicOf's "Rig" case is a one-key list and this key is the one entry on
                    //      it: ConfigTopic.Environment. That is what decides the ERWEITERT page it
                    //      also appears on (Erweitert ▸ Umgebung), and it was renamed with the key.
                    //   3. Lookup (VROptionsTab.4.Curated.cs:1314) finds it by "Rig/Vanilla2DMap"
                    //      out of the same catalog, and WARNS instead of silently skipping when a
                    //      curated key no longer exists — which is the safety net under a rename:
                    //      a typo here produces "curated entry [Rig] Vanilla2DMap no longer exists"
                    //      in the log rather than a row that quietly disappears.
                    //   4. IsRowVisible then applies the variant filter and the dependency fold.
                    //      This row declares no dependency and is exempt from the [MapRoom] section
                    //      rule anyway (it is in [Rig], not [MapRoom]), so it always draws.
                    //
                    // AND IT IS THE RIGHT TAB, not merely the tab it was on. "Umgebung & Ton" is
                    // the tab whose own header argues it holds "not how the world is DRAWN, but
                    // which world you are in … and the 3D map you travel it on" — which is exactly
                    // what this switch decides. The competing candidate is "Brett & Karten", the
                    // play-surface tab; it loses because the campaign map is not the play surface
                    // (the board is), and because the five [MapRoom] dials and the two travel
                    // offsets that fold under this switch are already filed under Umgebung and
                    // Menüs & Tafeln respectively. A player asking "which map do I get" is asking
                    // the same question as "which world am I standing in", one heading above.
                    LocKey = "sec_map3d",
                    Entries = new CuratedEntry[]
                    {
                        // THE CAPTION KEY CHANGED WITH THE ENTRY, and fixing it was overdue on its
                        // own account. "vr_o_3dmap" was NOT a caption: Loc.cs:711 held a five-line
                        // paragraph under it and there was no "h_vr_o_3dmap" hint at all, so
                        // CuratedEntry.Caption (=> Loc.Mod(CaptionKey)) handed BuildItem an entire
                        // explanatory text as the row's LABEL — the one place in this list where
                        // the caption/hint pair every other row uses was inverted. "vr_o_2dmap"
                        // restores the shape: a short label ("Originale 2D-Karte") plus
                        // "h_vr_o_2dmap" for the sentence that explains it, exactly like
                        // vr_o_sky / h_vr_o_sky two headings up.
                        new("Rig", "Vanilla2DMap", "vr_o_2dmap"),
                        // The map room's card hand, DIRECTLY under the switch that decides whether
                        // there is a room. It folds under Vanilla2DMap == Off
                        // (VROptionsTab.8.Dependencies.cs), so it is only ever on screen while
                        // there is a map room for it to be in — which is now the DEFAULT state,
                        // i.e. this row is visible out of the box where before it was hidden out of
                        // the box. Empty caption key: the localized name and the German description
                        // carry the row.
                        new("WorldUI", "MapRoomHand", ""),
                        // THE MOUSEOVER ANIMATION ON A MAP SYMBOL (user, 2026-09-03: "Bitte
                        // deaktiviere die animationen für das mouseover im Kartenraum wenn ich
                        // über ein Kartensymbol hovere - an der Stelle möchte ich es nicht.").
                        //
                        // FILED BY WHAT THE PLAYER LOOKS AT, which is the map room — the same
                        // heading as the switch that decides there IS a map room and as the room's
                        // card hand, one row up. Filing it by its owning file would have put it
                        // under a hover/pointer heading, where nobody tuning the 3D map would
                        // look; that is the ModBuild 340 defect ("Weiterhin finde ich den offset
                        // für die healthbar nicht") in its general form.
                        //
                        // IT IS NOT A SIZE DIAL, so the standing ruling that sent the five
                        // [MapRoom] size dials to Erweitert ("Symbolgrößen gehören ins ERWEITERT
                        // Menü!", ModBuild 196) does not reach it and is not being re-litigated:
                        // this is a yes/no about whether a thing MOVES, the same kind of row as
                        // the two above it. It folds under Vanilla2DMap == Off with the rest of
                        // its section (VROptionsTab.8.Dependencies' [MapRoom] section rule), so it
                        // is only ever on screen while there is a map room for it to act in.
                        //
                        // Empty caption key: the localized display name ("Karte 3D: Symbol-
                        // Animation") and the German description carry the row, exactly like
                        // MapRoomHand above.
                        new("MapRoom", "HoverAnimation", ""),
                        // NOT HERE, and neither is on this page by accident:
                        //   * the five [MapRoom] SIZE dials — "Symbolgrößen gehören ins ERWEITERT
                        //     Menü!" (user ruling, hardware, ModBuild 196). They are Erweitert ▸
                        //     Umgebung, adjacent and in Pinned() reading order, and DO NOT
                        //     RE-PROMOTE them without a fresh ruling.
                        //   * [WorldUI] TravelButtonOffset{X,Y} — "Geb mir dann IM DEBUG MENU die
                        //     offsets um ihm zu verschieben - ich stell es selber ein." They are on
                        //     Erweitert ▸ Menüs & Tafeln ▸ Karte 3D, with their ◀/▶ stepper and
                        //     with both defaults still 0.
                    },
                },
            },
        },
        // ================================ BRETT & KARTEN ==================================
        // NEW TAB (audit 05, S6 + ruling F3): the "Karten & Brett" section had been filed under
        // "Tafeln", which mixes audience and object — whoever tunes how their card fan opens
        // does not look under "panels". The control board and the cards ARE the mod's play
        // surface; they carry their own tab now, between Grafik and Tafeln.
        new()
        {
            LocKey = "cat_boardcards",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "vr_sec_controlboard",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "Board", "control_board"),
                        new("Cards", "TrayScale", "vr_o_trayscale"),
                        new("Cards", "TrayFollow", "vr_o_trayfollow"),
                        // "Finger-Druck: Zusatz-Hitbox …(px)" ([Cards] PokePadPixels, ModBuild 403)
                        // STOOD HERE and is GONE from the curated page — 2026-09-05 ruling (b),
                        // verbatim: "In den Nicht-Erweitert Kategorien sind wieder viel zu viele
                        // detaillierte Einstellungen gelandet. Sowas wie Offsets etc. gehört da
                        // nicht hin - Denke immer daran das die einfachen Spieler die Zielgruppe
                        // sind." It is a hitbox padding measured in AUTHORED PIXELS around a
                        // collider the player cannot see, and a player cannot see it change either:
                        // the only visible effect is a press that used to miss now landing. Its own
                        // caption has to say "Zusatz-Hitbox" to mean anything, which is the test
                        // failing out loud. NOTHING IS LOST: it is listed by hand on Erweitert ▸
                        // Karten & Fächer ▸ Steuerbrett (VROptionsTab.7.TopicTrees.cs) beside the
                        // board rows it belongs to, rather than being left to fall into the
                        // "Allgemein" grab-bag at the bottom of that page.
                        // Item 12: the board movement scheme (Frei / Begrenzt / Begrenzt mit
                        // Neigung) — a special row (TryBuildSpecialRow) so the dropdown shows
                        // localized labels instead of the raw enum member names.
                        new("Cards", "BoardMoveMode", "vr_o_boardmove"),
                        // The pitch window the "Begrenzt mit Neigung" scheme clamps to. The keys
                        // are per-board, so all three variants are listed and the per-variant
                        // filter (IsShownForCurrentVariant) shows exactly the selected board's
                        // pair — the user asked where these rows live, and the answer must be
                        // HERE, right under the movement scheme they belong to (they also stay
                        // reachable under Erweitert ▸ Steuerbrett like all per-board tuning).
                        new("Cards", "BoardPitchMin_Oak", "vr_o_pitchmin"),
                        new("Cards", "BoardPitchMin_Steel", "vr_o_pitchmin"),
                        new("Cards", "BoardPitchMin_Bronze", "vr_o_pitchmin"),
                        new("Cards", "BoardPitchMax_Oak", "vr_o_pitchmax"),
                        new("Cards", "BoardPitchMax_Steel", "vr_o_pitchmax"),
                        new("Cards", "BoardPitchMax_Bronze", "vr_o_pitchmax"),
                        // "The board starts on the left of your head" — a comprehensible
                        // spawn-side choice (audit 01 NORMAL); its three fine-tune metre dials
                        // (Spawn*Meters) stay under Erweitert.
                        new("Cards", "SpawnLeftOfHead", "vr_o_spawnleft"),
                        // THE THREE NUMERIC ARRIVAL DIALS STOOD HERE AND ARE GONE FROM THE CURATED
                        // PAGE — [Cards] SpawnMaxReachMeters, SpawnMaxBearingDegrees and
                        // SpawnBoardWidthDegrees, curated on 2026-09-05 off two hardware reports
                        // and demoted the same day by the ruling those reports' round produced,
                        // verbatim: "In den Nicht-Erweitert Kategorien sind wieder viel zu viele
                        // detaillierte Einstellungen gelandet. Sowas wie Offsets etc. gehört da
                        // nicht hin - Denke immer daran das die einfachen Spieler die Zielgruppe
                        // sind. 'Power-User' können dann in 'Erweitert' gehen."
                        //
                        // ALL THREE FAIL THE SAME TWO TESTS. They are metres and degrees against a
                        // guard the player cannot see (max reach / max angle) or an ANGULAR width
                        // he cannot picture, AND they are read once, at the moment the board
                        // arrives — so changing one while looking at the board does nothing at all,
                        // which reads as a broken row. That is the exact reasoning that retired
                        // [Compat] DisablePostProcessing and DisableVolumetricFog from the Bild
                        // page, and it is why the three SpawnSide/Forward/DownMeters offsets were
                        // never curated in the first place. What the player is left with is the
                        // decision ("Brett startet links") and the LIVE size he can turn while
                        // looking at it ("Brett: Größe", [Cards] TrayScale, three rows up) — which
                        // is the row a "das Board ist zu groß" complaint actually wants.
                        //
                        // NOTHING IS LOST AND NOTHING HAD TO MOVE, and this was checked rather than
                        // assumed: all three are already listed BY HAND on Erweitert ▸ Karten &
                        // Fächer ▸ Steuerbrett (VROptionsTab.7.TopicTrees.cs, the vr_ct_board
                        // block), adjacent and in this same reading order, beside the three
                        // Spawn*Meters offsets they belong with. They are one navigation level
                        // deeper, not gone.
                    },
                },
                new()
                {
                    // PROMOTED from Erweitert ▸ Hände & Figuren (2026-08-22 settings audit,
                    // question (b)). [FigureGrab] GrabFigures is the on/off for a whole HEADLINE
                    // feature — picking the miniatures up off the board with your hand — and it
                    // was three navigation levels deep behind a topic name. It is also the master
                    // the entire [FigureGrab] section folds under, so a player who does not find
                    // this row cannot find any of the thirty dials behind it either. Perceivable
                    // the moment you reach for a figure, harmless when wrong (nothing lifts), and
                    // it belongs on the tab about the play surface it acts on. Empty caption key —
                    // "Figuren greifen" is already the row's name.
                    LocKey = "vr_sec_figures",
                    Entries = new CuratedEntry[]
                    {
                        new("FigureGrab", "GrabFigures", ""),
                    },
                },
                // ==========================================================================
                //  "MAP-ITEMS IN DER HAND" — THE WHOLE HEADING IS GONE FROM THE CURATED PAGE
                // ==========================================================================
                // NINE ROWS STOOD HERE from ModBuild 350: [FigureGrab] PropHeldOffsetSide/Up/
                // Forward (metres), PropHeldRotPitch/Yaw/Roll (degrees), PropHeldUpright,
                // PropHeldUprightAtGrab and PropHeldSameInBothHands.
                //
                // THE RULING THAT TOOK THEM, verbatim (2026-09-05): "In den Nicht-Erweitert
                // Kategorien sind wieder viel zu viele detaillierte Einstellungen gelandet. Sowas
                // wie Offsets etc. gehört da nicht hin - Denke immer daran das die einfachen
                // Spieler die Zielgruppe sind. 'Power-User' können dann in 'Erweitert' gehen."
                // Six of the nine are literally the named shape — a position nudge in metres and a
                // rotation nudge in degrees, meaningful only relative to a grip the player cannot
                // see — and this is the same player who set the precedent for exactly this family
                // one feature over: "Geb mir dann IM DEBUG MENU die offsets um ihm zu verschieben -
                // ich stell es selber ein." Erweitert is that menu.
                //
                // WHY THE THREE BOOLEANS WENT TOO, rather than being kept as a rump section. They
                // are pose micro-behaviours OF the same six numbers (hold it upright, take upright
                // at the moment of grab, use the left-hand pose in both hands) — a player who is
                // not turning the offsets has no question they answer. Keeping them would have
                // split one family across two navigation levels, which is the fault
                // scripts/check-options-coverage.py check 4 exists to catch, and it would have
                // left a heading named after items in your hand carrying three switches and none
                // of the reason it was written. The section's own header said so: "WHY ALL EIGHT
                // ROWS ARE HERE AND NOT ONE OF THEM ONE LEVEL DEEPER … These eight ARE the family."
                // They still are; the family simply lives one level deeper now. The heading goes
                // with them under the standing ruling that no section may ship empty.
                //
                // NOTHING IS LOST, AND THE DESTINATION WAS CHECKED RATHER THAN ASSUMED (see
                // ConfigCatalog.GroupAll → GroupWord → GroupWordLabel): [FigureGrab] binds 27 keys,
                // past ConfigCatalog.SectionSplitThreshold, so the section splits by leading key
                // word and all nine — every [FigureGrab] key beginning "Prop" — form ONE cluster of
                // their own, well past MinClusterSize, on Erweitert ▸ Hände & Figuren. That cluster
                // had no localized heading (GroupWordLabel would have returned the raw English
                // "Prop" into a German menu), so this round gave it one: "cfg_gw_prop" =
                // "Map-Items in der Hand", the same words this section carried. The family arrives
                // there whole, under the name it had here, one navigation level deeper.
                //
                // NO VALUE WAS TOUCHED. Every default is the figure value the mod already ships
                // (Defaults.Board.cs), PropHeldSameInBothHands still ships ON, and its row
                // still exists one level deeper — this round moves rows, it does not tune.
                new()
                {
                    // ==================================================================
                    //  LEBENSBALKEN — MOVED HERE FROM "Tafeln", AND COMPLETED (2026-09-02)
                    // ==================================================================
                    // USER REPORT, verbatim: "Weiterhin finde ich den offset für die healthbar
                    // nicht - daraus resultiert ein weiterer Task: Die Optionen sollten immer
                    // sonnvoll in Kategorien geclustert sein, so dass man sie schnell finden kann!"
                    //
                    // TWO FAULTS, ONE SYMPTOM, and both are fixed here.
                    //
                    // 1. THE ROW DID NOT EXIST. [WorldUI] BarHeightOffset was bound at ModBuild
                    //    339 — for him, on his ask — and never curated. Uncurated means it fell to
                    //    the catalog's own index, and there it is not even on the hand-arranged
                    //    "Lebensbalken" heading of Erweitert ▸ Menüs & Tafeln (VROptionsTab.7
                    //    .TopicTrees, vr_pt_bars): a key that tree does not name lands in the
                    //    "Allgemein" grab-bag at the BOTTOM of the page, which is the exact thing
                    //    that tree was written to empty. It is listed in both places now.
                    //
                    // 2. THE FAMILY WAS UNDER THE WRONG TAB. The bars floated above the FIGURES on
                    //    the BOARD and their heading sat on "Tafeln" — the tab for the panels and
                    //    windows the mod draws. That is filing by implementation (ActorBars is
                    //    WorldUI code, its keys are in the [WorldUI] section) instead of by the
                    //    object the player is looking at, which is the whole disease this round is
                    //    about. Here the heading sits directly under "Figuren", one line below the
                    //    switch for picking those same figures up: somebody who thinks "die Balken
                    //    über den Figuren sitzen zu hoch" reads the tab that names the board, then
                    //    the heading that names the bars. Nothing was lost by the move — every one
                    //    of these four keys is still on Erweitert ▸ Menüs & Tafeln ▸ Lebensbalken.
                    //
                    // The rule this section is the poster child for is now MACHINE-CHECKED:
                    // scripts/check-options-coverage.py fails the build when a key joins a curated
                    // family without joining its heading. This exact defect is what it was written
                    // against; its four checks are falsified in both directions in the round notes.
                    LocKey = "vr_sec_bars",
                    Entries = new CuratedEntry[]
                    {
                        // The bars themselves are no longer switchable ([WorldUI] ActorBars,
                        // removed 2026-08-13) — only how big they are, how high they sit, and
                        // whether walls hide them. HP is not optional content.
                        // ONE size dial. The clamp's two ends were rows here too until
                        // 2026-08-13 ("Mindest und Maximalgröße der Lebensbalken haben keinen
                        // sehbaren einfluss … ziemlich unintuitiv"): they bounded the table-zoom
                        // FOLLOW factor, which is 1.0 at the shipped zoom, so neither row could
                        // move a pixel where the player stands. The clamp itself is kept — as the
                        // constants ActorBars.ZoomFollowMin/Max — so the size below still holds at
                        // every zoom, which is what the original request actually asked for.
                        new("WorldUI", "BarSizeScale", "vr_o_barsize"),
                        // THE ROW HE COULD NOT FIND. Directly under the size, because "zu groß"
                        // and "zu hoch" are the same complaint arriving in two words.
                        //
                        // THE CAPTION IS WRITTEN HERE, not as a Loc key, and not left empty. Empty
                        // would fall through to the catalog display name — and [WorldUI]
                        // BarHeightOffset has no Loc.ConfigNames entry either, so the fall-through
                        // is the camel humps spaced out: "Bar Height Offset", in German too. That
                        // is a programmer's name on the one row this whole round exists to make
                        // findable. Core/Loc/Loc.cs is owned by another lane this round, so the
                        // pair lives on the entry (see Say) — migrating it to a Loc key later is a
                        // pure move and changes nothing on screen.
                        //
                        // NO HINT KEY, deliberately: the CaptionKey is empty, so HintKey is "h_",
                        // which misses, and the tooltip falls through to the entry's own bound
                        // description — five sentences of German that already explain world units,
                        // the sign convention and that it rides ON TOP of the per-figure head
                        // measurement rather than replacing it. That is a better tooltip than any
                        // one-liner written here would be.
                        //
                        // THE VALUE IS UNTOUCHED. Defaults.BarHeightOffset stays 0 (the measured
                        // height stands); this round moves rows, it does not tune.
                        // "Lebensbalken: Höhe" ([WorldUI] BarHeightOffset) STOOD HERE and is on
                        // Erweitert since 2026-09-05. It was curated because he could not find it
                        // ("Weiterhin finde ich den offset für die healthbar nicht"), and when the
                        // new "offsets do not belong on everyday pages" ruling was put to him with
                        // this row named as the possible exception, he answered that it is not one:
                        // "Ja auch die Höhe der Lebensbalken … sind Experteneinstellungen und
                        // gehören in Erweitert." FINDABILITY WAS THE ORIGINAL COMPLAINT AND IT IS
                        // ANSWERED SEPARATELY: the key had no Loc.ConfigNames entry at all, which
                        // is why it could only ever be found through a hand-written caption here.
                        // It has one now, carrying these exact words, so Erweitert lists it under
                        // its German name instead of a raw key.
                        // PROMOTED from Erweitert ▸ Menüs & Tafeln (2026-08-22 settings audit,
                        // question (b)): "Balken: Abstand ignorieren" is the THIRD member of a
                        // three-row family whose other two are already here, and the heading above
                        // it says so. A family that shares a name should share a page — that is
                        // the argument this section was created with. Directly visible (bars stop
                        // shrinking with distance), harmless when wrong, one toggle back.
                        new("WorldUI", "BarFixedSize", ""),
                        new("WorldUI", "BarsOccluded", "vr_o_barsoccluded"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_cardhand",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "RevealMode", "vr_o_revealmode"),
                        new("Cards", "InspectScale", "vr_o_inspectscale"),
                        // TAKE A CARD INTO YOUR HAND (2026-08-29). This is the everyday door to a
                        // whole second way of holding a card, and it belongs on the page called
                        // "Karten & Hand" for the same reason RevealMode does: it is about what
                        // happens when you take a card, not about how one looks.
                        //
                        // ONLY THE SWITCH IS HERE. Its two pose dials (InHandPitch, the card's
                        // angle out of the palm; InHandPinchOffset, a nudge on the grip point) stay
                        // in the raw Erweitert catalog — they tune a hand-modelled grip, which is
                        // exactly the kind of value that gets set once after a look in the headset
                        // and never again. Empty caption key: the localized name ("Karte in die
                        // Hand nehmen") is already written for a player.
                        new("Cards", "InHandHold", ""),
                        // THE card size — 49 read sites, the single most player-visible size
                        // dial in the whole [Cards] section (audit 01 NORMAL).
                        new("Cards", "CardWidth", "vr_o_cardwidth"),
                        // "Karten-Geräusche" ([Cards] CardSoundsEnabled) stood here. It is on
                        // Umgebung & Ton ▸ Ton since the 2026-08-22 audit, together with the only
                        // other sound rows the mod has — see that section for the argument.
                        // Ruling 6 (ONE everyday on/off for all five card/fan sounds) and rulings
                        // 5/7 (the five STRINGS stay on Erweitert ▸ Karten & Fächer ▸ Klänge) are
                        // both untouched; only the everyday door moved.
                    },
                },
                new()
                {
                    LocKey = "vr_sec_piles_hints",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "WantedSlotHint", "vr_o_slothint"),
                        // The selection-ready pulse is board-flavoured (it reminds you the
                        // board is waiting on your card pick), so it lives with the board
                        // rather than with the panels — audit 04 straggler, placed here.
                        new("SelectionReady", "Enabled", "vr_o_selready"),
                        // The user's own ask ("Größe der Infotafeln … einstellen können") —
                        // sizes the hover info cards over the play area (audit 02 NORMAL).
                        new("WorldUI", "HoverInfoScale", "vr_o_hoverinfo"),
                    },
                },
            },
        },
        // ===================================== TAFELN =====================================
        // The ONE home of every display the mod draws (audit 05 §2.1): the half of the panel
        // family that only lived under Debug — Initiative, Elemente, Aufgaben, Statustafeln,
        // Info-Karten, Tooltips, Handgelenk-Anzeige, Ladeanzeige — joins the curated switches.
        //
        // "Lebensbalken" IS NO LONGER ONE OF THEM (2026-09-02). The heading moved to Brett &
        // Karten, under Figuren — see the long note there for the report it answers. The claim
        // this tab makes above is still true and is exactly what the move corrects: the bars are
        // not a display the mod DRAWS AS A PANEL, they are a readout stuck to a miniature on the
        // board, and "every display the mod draws" was a net wide enough to catch them by
        // implementation rather than by what the player is looking at. Do not pull them back
        // without re-reading that note: this tab is where the offset went missing.
        new()
        {
            LocKey = "cat_panels",
            Sections = new CuratedSection[]
            {
                new()
                {
                    // SETTINGS AUDIT ROUND 2 (user ruling 2026-08-13, verbatim: "Die
                    // Initativreihenfolge ausschalten zu können am Controllboard macht keinen
                    // Sinn."). This section used to be fourteen rows; nine of them are gone with
                    // their dials — Initiative-Leiste, Elemente-Tafel, Aufgaben, Statustafeln,
                    // Info-Karten, Gegnerkarten, Lebensbalken, Tooltips and Element-Hinweise —
                    // because switching any of them off released the panel back to its 2D home,
                    // which in VR is nowhere. What is left is what the flat game also lets you
                    // hide, or what the mod ADDED and nobody needs: the combat log, the wrist
                    // HUD, the loading spinner, and the two presentation choices (Dialoge /
                    // Entscheidungsleiste) whose off-state shows the SAME prompt as a floating
                    // window. The table-edge button cluster's switch left this list on 2026-08-25
                    // with the cluster itself — its one live member is a board keycap now and
                    // follows the board, exactly as CONFIRM and UNDO always have.
                    LocKey = "vr_sec_panels",
                    Entries = new CuratedEntry[]
                    {
                        // THE COMBAT LOG IS ONE ACTION AND ONE PREFERENCE, in that order, right
                        // here — the page a player opens when he wants the combat log (user,
                        // 2026-09-05: "einfach nur ein Button 'Spawn Kampflog' oder so … Es soll
                        // aber eine weitere Einstellung geben, die angibt, ob der Kampflog zu
                        // Beginn spawnen soll oder nicht"). The BUTTON is first because it is what
                        // he is here for; the preference under it answers the other question. The
                        // old single toggle that tried to be both is what shipped a log nobody
                        // could summon — see CombatLogSurface's show/hide seam.
                        CuratedEntry.Press("Spawn combat log now", "Kampflog jetzt einblenden",
                                           Surfaces.CombatLogSurface.SpawnFromOptions),
                        new("WorldUI", "CombatLog", "show_combat_log"),
                        new("WorldUI", "WristHud", "vr_o_wristhud"),
                        new("WorldUI", "Dialogs", "vr_o_dialogs"),
                        new("WorldUI", "DecisionDock", "vr_o_decisiondock"),
                        new("WorldUI", "LoadingIndicator", "vr_o_loading"),
                    },
                },
                new()
                {
                    // The floating 2D screen's everyday face: whether the intro plays on it,
                    // how wide it is, how far away it hangs (audit 02 NORMAL). Its two kill
                    // switches (FlatScreen / FlatScreenAutoShow) are deliberate footguns and
                    // stay under Erweitert.
                    LocKey = "vr_sec_screen2d",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "ShowIntro", "vr_o_showintro"),
                        new("WorldUI", "ScreenWidth", "vr_o_screenwidth"),
                        new("WorldUI", "ScreenDistance", "vr_o_screendist"),
                    },
                },
                new()
                {
                    // How the panels are operated: poke-to-click, the firm-press guard on
                    // decisions, the gaze-following hex hint, the keycap animation.
                    LocKey = "vr_sec_interaction",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "PokeClick", "vr_o_pokeclick"),
                        new("WorldUI", "DecisionPokeDeliberate", "vr_o_pokefirm"),
                        // User request 8 (2026-08-22): when a released window turns to face you.
                        // HERE and not on Komfort, because this section is "how the panels are
                        // operated" and the dial is about what happens at the end of one of those
                        // operations — the same argument that put the poke rows here. Empty caption
                        // key: "Beim Loslassen zu dir drehen" (Loc.ConfigNames) is already the row's
                        // name, and a second hand-written caption could only drift from it. A special
                        // row (TryBuildSpecialRow) so the three-way choice reads as a localized
                        // dropdown instead of the raw enum members cycling in English.
                        new("WorldUI", "WindowFacing", ""),
                        // "Greifbalken: Übergang (ms)" ([WorldUI] GrabBarTweenMs) STOOD HERE from
                        // 2026-09-03 ("es ploppt") — how long the grab bar under a window takes to
                        // grow, shrink or slide when the window's content re-seats it.
                        //
                        // IT IS GONE FROM THE CURATED PAGE — 2026-09-05 ruling (b), verbatim: "In
                        // den Nicht-Erweitert Kategorien sind wieder viel zu viele detaillierte
                        // Einstellungen gelandet. Sowas wie Offsets etc. gehört da nicht hin."
                        // WHAT ANSWERED "es ploppt" WAS THE DEFAULT, NOT THE DIAL: a tween duration
                        // in milliseconds is a smoothing constant, the row's own caption has to
                        // name an internal part ("Greifbalken") to mean anything, and its effect
                        // only ever appears in the half-second after the mod re-seats a bar the
                        // player is not looking at. NOTHING IS LOST AND NOTHING HAD TO MOVE: it has
                        // been listed by hand on Erweitert ▸ Menüs & Tafeln since the day it was
                        // bound (VROptionsTab.7.TopicTrees.cs), so it keeps its named heading one
                        // navigation level deeper. THE SHIPPED VALUE IS UNTOUCHED.
                        new("WorldUI", "HexHintFollowView", "vr_o_hexhintfollow"),
                        new("ButtonAnim", "Enable", "vr_o_buttonanim"),
                    },
                },
                new()
                {
                    // [Keyboard] Enabled is gone (user ruling 2026-08-13): a text field you
                    // cannot type into is not a setting. Only the capitalisation taste is left.
                    LocKey = "vr_sec_keyboard",
                    Entries = new CuratedEntry[]
                    {
                        new("Keyboard", "AutoCapitalise", "vr_o_keyboardcase"),
                    },
                },
            },
        },
        // ============================= AVATAR & MEHRSPIELER ===============================
        // ONE TAB where two mini-tabs stood (audit 05, S5 + ruling F1): Avatar had four rows,
        // Mehrspieler six — and two of those six were the mask pair REPEATED from Avatar as a
        // navigation crutch. Merged, the duplicates simply ceased: what you choose about your
        // appearance and what only means anything with someone else in the game are two
        // SECTIONS of one social tab now, not two tabs with a seam through the mask.
        new()
        {
            LocKey = "cat_avatar_mp",
            Sections = new CuratedSection[]
            {
                new()
                {
                    // AVATAR WAS ALMOST ENTIRELY DEAD once. Of its six appearance rows only
                    // HandStyle did anything: four were the shared hand-seat keys (deleted in
                    // the 2026-08 dead-settings sweep) and the fifth was HandColor, which only
                    // ever tinted the procedural fallback hand. What a player actually chooses
                    // is here: the hand model, ITS SIZE, the mask, its size, and the mirror to
                    // check the result in.
                    //
                    // HAND SIZE IS BACK (ruling 4). It moved to Debug once as "calibration,
                    // not a choice" — the overhaul ruled it an everyday avatar choice after
                    // all. The keys are per-style ({Glove,Plate,Arcane}Scale), so all three
                    // are listed and the per-variant filter shows exactly the worn style's
                    // row, captioned variant-free ("Handgröße") — same pattern as the board
                    // pitch window in Brett & Karten.
                    LocKey = "vr_sec_your_look",
                    Entries = new CuratedEntry[]
                    {
                        new("Hands", "HandStyle", "vr_o_handstyle"),
                        new("Hands", "GloveScale", "vr_o_handscale"),
                        new("Hands", "PlateScale", "vr_o_handscale"),
                        new("Hands", "ArcaneScale", "vr_o_handscale"),
                        new("Net", "MaskId", "head_mask"),
                        new("Net", "MaskSize", "mask_size"),
                        new("Net", "MirrorEnabled", "mirror"),
                        // THE GHOST HAND, three rows, PROMOTED from Erweitert ▸ Hände & Figuren
                        // (2026-08-22 settings audit, question (b)). This is a readability choice
                        // about your own hands and it reads as one: the hand mesh fades while it
                        // is carrying the open fan or a held card "so the hand mesh stops covering
                        // card details". It is exactly as much an appearance decision as the hand
                        // MODEL two rows up, and a player who cannot read their own cards has no
                        // way to guess the fix is filed under a topic called "Hände & Figuren".
                        // The strength is already clamped 0.05–0.95, so the hand can never vanish
                        // entirely however the dial is set — which is what makes all three safe
                        // for this page whatever a player does to them.
                        //
                        // NO DEPENDENCY RULE, deliberately not claimed: the strength does NOT fold
                        // under the two switches (VROptionsTab.8.Dependencies.cs declares none for
                        // [Hands] GhostHand*), so all three rows are always on screen together.
                        // Declaring one would be the tidier menu and it is NOT done here, because
                        // that file is a separate contract — noted rather than changed.
                        //
                        // Empty caption keys — all three localized names ("Geisterhand bei Fächer",
                        // "… bei Karte", "Geisterhand-Stärke") are already written for a player.
                        new("Hands", "GhostHandOnFan", ""),
                        new("Hands", "GhostHandOnHeldCard", ""),
                        new("Hands", "GhostHandStrength", ""),
                    },
                },
                new()
                {
                    // EVERYTHING THAT IS ABOUT PLAYING WITH OTHERS LIVES HERE (user item 2,
                    // 2026-08-11: "Mach alle Einstellungen die mit dem Multiplayer zu tun haben
                    // (unjd nicht in Erweitert gehören) auch in den Multiplayer Tab. zB. 'Wände
                    // mit Spielern synchronisieren' sollte genau da verortet sein."). The three
                    // [Net] rows that depend on the sync being on sit DIRECTLY under its switch
                    // (they fold with it — VROptionsTab.8.Dependencies.cs); SpawnInCircle closes
                    // the block because it seats you with or without the embodiment sync.
                    LocKey = "vr_sec_mp_presence",
                    Entries = new CuratedEntry[]
                    {
                        new("Net", "Enabled", "vr_o_netenabled"),
                        new("Net", "RemoteBoards", "remote_boards"),
                        new("Net", "NameTags", "vr_o_nametags"),
                        // MOVED here from Komfort ▸ Sichtbarkeit (item 2's own example): the
                        // receiver-side MP wall-fade sync is a together-play setting first.
                        new("WallFade", "SyncPeerFades", "wallfade_sync"),
                        // PROMOTED from the raw catalog (2026-08-22 settings audit, question (b)).
                        // "Boards vor dem Spielfeld" ([PeerBoardFade] Mode) is new in ModBuild 222
                        // and ships OFF: a player whose view of the field is blocked by a
                        // team-mate's control board has a fix and, until this row, no way to find
                        // it. It is purely local (nothing goes on the wire, the owner's board is
                        // unchanged for everyone else), so the worst case of getting it wrong is
                        // that a board you wanted to see goes see-through — one dropdown back. Its
                        // five thresholds stay on Erweitert ▸ Mehrspieler, where they now land by
                        // topic instead of falling into "Sonstiges" (ConfigCatalog.TopicOf).
                        new("PeerBoardFade", "Mode", ""),
                        new("Rig", "SpawnInCircle", "vr_o_circle"),
                        // NOT here: [Net] VersionGuard (ruling 14) — disabling the version
                        // handshake is an expert escape hatch, it stays under Erweitert.
                    },
                },
            },
        },
    };

    /// <summary>
    /// Section+Key → the catalog entry, rebuilt whenever the catalog is. Section and key alone
    /// are enough: the mod's config files do not repeat a section name between them.
    /// </summary>
    private static readonly Dictionary<string, ConfigCatalog.ConfigItem> ByKey = new(512);

    private static int _lookupSignature = -1;

    /// <summary>
    /// Refresh the lookup when the catalog has changed. Keyed on the entry count rather than a
    /// dirty flag, because the catalog rebuilds itself on its own schedule and the tab only ever
    /// sees the result.
    /// </summary>
    private static void EnsureLookup()
    {
        if (_lookupSignature == ConfigCatalog.TotalEntries && ByKey.Count > 0)
            return;

        ByKey.Clear();
        for (int t = 0; t < ConfigCatalog.TopicCount; t++)
        {
            IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups((ConfigCatalog.ConfigTopic)t);
            for (int g = 0; g < groups.Count; g++)
            {
                List<ConfigCatalog.ConfigItem> items = groups[g].Items;
                for (int i = 0; i < items.Count; i++)
                {
                    string id = Id(items[i].Section, items[i].Key);
                    if (!ByKey.ContainsKey(id))
                        ByKey[id] = items[i];
                }
            }
        }

        _lookupSignature = ConfigCatalog.TotalEntries;
    }

    private static string Id(string section, string key) => section + "/" + key;

    /// <summary>
    /// Rows whose CONTROL does not follow from the entry's type.
    ///
    /// <para>The key colour is stored as a Color, so the generic builder would offer four numeric
    /// steppers — R, G, B and A. That is not a setting a player can use: the value is only
    /// meaningful as one of a handful of named chroma keys, which is exactly how the mod's own
    /// panel presented it. So this one entry gets a named-preset dropdown instead, over
    /// <see cref="MixedReality"/>'s own preset list.</para>
    ///
    /// <para>The HEAD MASK is the third (user request 2026-08-09: "Ich will die Maske beim Avatar im
    /// Optionsmenü auch mit nem Dropdown auswählen können statt einem Schieberegler wie aktuell").
    /// <c>[Net] MaskId</c> is an int carrying an <c>AcceptableValueRange(0, MaskCount-1)</c>, so the
    /// generic builder classified it as a bounded Number and gave it a slider — a three-position bar
    /// reading "0", "1", "2", which says nothing about which mask you are putting on your face. It
    /// is an ENUMERATION whose members merely happen to be stored as small integers, exactly the
    /// case this table exists for; the size dial next to it stays a slider, because that one really
    /// is a continuous quantity.</para>
    ///
    /// <para>Kept as a lookup rather than an if-chain inside the row kit: the kit stays free of
    /// knowledge about individual settings, and the next entry that needs a hand-made control is
    /// one table row rather than another branch.</para>
    /// </summary>
    private static bool HasSpecialRow(ConfigCatalog.ConfigItem item) =>
        (string.Equals(item.Section, "MixedReality", StringComparison.Ordinal)
         && string.Equals(item.Key, "KeyColor", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Cards", StringComparison.Ordinal)
            && string.Equals(item.Key, "BoardMoveMode", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Net", StringComparison.Ordinal)
            && string.Equals(item.Key, "MaskId", StringComparison.Ordinal))
        || (string.Equals(item.Section, "Sky", StringComparison.Ordinal)
            && string.Equals(item.Key, "Style", StringComparison.Ordinal))
        // The FIFTH, for the same reason as the board movement scheme: [WorldUI] WindowFacing is an
        // enum, so the generic builder classifies it as a Choice and gives it a CYCLE showing the
        // raw member names "LaserOnly/Always/Never" — English, in a German menu, on a row a player
        // is meant to choose from. It is a user-facing choice of three named behaviours, which is
        // exactly what this table is for.
        || (string.Equals(item.Section, "WorldUI", StringComparison.Ordinal)
            && string.Equals(item.Key, "WindowFacing", StringComparison.Ordinal))
        // THE SIXTH, and the third instance of one defect: [PeerBoardFade] Mode is an enum, so the
        // generic Choice row labelled its dropdown "Off / Transparent / Hidden" — the raw C# member
        // names, in both languages, on a CURATED everyday row (Avatar & Mehrspieler ▸ Zusammen
        // spielen). The user found it from the wrong end: "Die Board-Transparenz Option im Dropdown
        // 'Off' sollte 'Permanent' heißen stattdessen." He is reading a German menu and being shown
        // a programmer's identifier — the same complaint the two branches above answer.
        || (string.Equals(item.Section, "PeerBoardFade", StringComparison.Ordinal)
            && string.Equals(item.Key, "Mode", StringComparison.Ordinal));
    // ONE MORE STOOD HERE (it was the sixth branch until the peer-board row above took that place)
    // — the graphics preset ([RenderQuality] QualityPreset), a bounded int that
    // needed a hand-built dropdown because the generic ladder would have drawn five named looks as
    // five unnamed positions on a drag bar. IT IS GONE with the offering itself (user ruling
    // 2026-09-05: "Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich
    // nicht."), and the branch had to go with the row rather than being left behind: the entry is
    // retired now, so no page can produce it and the branch could only ever be dead code holding a
    // live reference to the apply path this ruling exists to remove.

    /// <summary>
    /// Bounded numbers that must be edited with the ◀ / ▶ STEPPER even though they have both ends
    /// declared and would therefore get a slider (see <c>BuildRow</c> in VROptionsTab.2.Rows.cs).
    ///
    /// <para>USER RULING (hardware, verbatim): "'Reiseknopf seitlich' und 'Reise-Knopf Höhe' sollen
    /// keine Schieberegler sein, sondern die Pfeile, wo man den echten Wert einfach einstellen
    /// kann." He is not describing a preference about bars. He is describing what these two dials
    /// ARE FOR: three shipped placements of the travel-confirm button were rejected in a row
    /// (ModBuild 191/192/193), the mod ships the 190 pose, and he sets the offset himself — "ich
    /// stell es selber ein". That is a job of REPEATING one known amount and reading the number
    /// back, and the two controls are not equally good at it. A slider's handle maps the whole
    /// range onto a few centimetres of panel, so a hand at arm's length lands on whatever the ray
    /// happens to hit and the same value can never be dialled twice; the stepper moves by a written
    /// amount per press (<see cref="ConfigSteps"/>: 0.01 window heights for both) and the value
    /// label beside the arrows is the number itself.</para>
    ///
    /// <para>THE RANGES ARE NOT TOUCHED and neither are the defaults — both stay 0, which is the
    /// ModBuild 190 pose he asked to have back. This is the control, and nothing else.</para>
    ///
    /// <para>THE TWO ROWS LIVE ON ERWEITERT ▸ MENÜS &amp; TAFELN ▸ KARTE 3D since the 2026-08-22
    /// audit, not on a curated page — the other half of the same sentence ("Geb mir dann IM DEBUG
    /// MENU die offsets…"). This table is unaffected by that move: it is keyed on the config entry,
    /// not on the page, so both rows keep their arrows wherever they are built.</para>
    ///
    /// <para>A table rather than a branch, for the reason <see cref="HasSpecialRow"/> is one: the
    /// row kit stays free of knowledge about individual settings.</para>
    /// </summary>
    private static bool PrefersStepper(ConfigCatalog.ConfigItem item) =>
        string.Equals(item.Section, "WorldUI", StringComparison.Ordinal)
        && (string.Equals(item.Key, "TravelButtonOffsetXWindowHeights", StringComparison.Ordinal)
            || string.Equals(item.Key, "TravelButtonOffsetYWindowHeights", StringComparison.Ordinal));

    private static bool TryBuildSpecialRow(Transform parent, ConfigCatalog.ConfigItem item, string? caption,
                                           string? hintKey)
    {
        if (!HasSpecialRow(item))
            return false;

        // Item 12: the board movement scheme is a user-facing CHOICE (like the board material or
        // the hand style), so the dropdown must read in the player's language — the generic
        // choice row would show the raw enum members "Free/Limited/LimitedPitch". The dropdown
        // index maps 1:1 onto the enum values (Free=0/Limited=1/LimitedPitch=2, documented at
        // the enum as the index map).
        if (string.Equals(item.Section, "Cards", StringComparison.Ordinal))
        {
            string[] modeNames =
            {
                Loc.Mod("boardmove_free"),
                Loc.Mod("boardmove_limited"),
                Loc.Mod("boardmove_pitch"),
            };
            BuildPresetRow(parent, item, caption, hintKey, modeNames,
                           (int)Cards.CardsConfig.BoardMoveMode.Value,
                           index => Cards.CardsConfig.BoardMoveMode.Value = (Cards.BoardMoveMode)index);
            return true;
        }

        // WHEN A RELEASED WINDOW TURNS TO FACE YOU (user request 8, 2026-08-22) — a three-way choice
        // of named behaviours, so a dropdown and not the ◀/▶ cycle the generic Choice row builds.
        // The dropdown index maps 1:1 onto WindowFaceMode (LaserOnly=0/Always=1/Never=2, documented
        // as the index map at the enum), and the labels come from Loc.WindowFacingModeName so the
        // row reads "Nur mit Laser / Immer / Nie" rather than the raw member names.
        if (string.Equals(item.Section, "WorldUI", StringComparison.Ordinal))
        {
            string[] facingNames =
            {
                Loc.WindowFacingModeName(0),
                Loc.WindowFacingModeName(1),
                Loc.WindowFacingModeName(2),
            };
            BuildPresetRow(parent, item, caption, hintKey, facingNames,
                           (int)WorldUIConfig.WindowFacing.Value,
                           index => WorldUIConfig.WindowFacing.Value = (WindowFaceMode)index);
            return true;
        }

        // WHAT A MITSPIELER'S BOARD DOES WHILE IT STANDS IN FRONT OF THE PLAY FIELD — three named
        // behaviours, so a dropdown that reads in the player's language instead of the generic
        // Choice row's raw member names ("Off / Transparent / Hidden", identical in both languages
        // until this branch). The index maps 1:1 onto PeerBoardFadeMode (Off=0/Transparent=1/
        // Hidden=2, declared with those explicit values at the enum).
        //
        // WRITTEN THROUGH item.Entry.BoxedValue RATHER THAN PeerBoardFadeTuning.FadeMode, which is
        // what the branches above do with their own typed entries: that field is NULLABLE (the
        // section binds only once NetModule.BindConfig has run) and reaching this branch already
        // proves the catalog holds the BOUND entry — item.Entry IS PeerBoardFadeTuning.FadeMode.
        // Going through the item removes a null check that could only ever be dead code.
        if (string.Equals(item.Section, "PeerBoardFade", StringComparison.Ordinal))
        {
            string[] fadeNames =
            {
                Loc.Mod("peerboardfade_off"),
                Loc.Mod("peerboardfade_transparent"),
                Loc.Mod("peerboardfade_hidden"),
            };
            int currentMode = item.Entry.BoxedValue is Net.PeerBoardFadeMode mode ? (int)mode : 0;
            BuildPresetRow(parent, item, caption, hintKey, fadeNames, currentMode,
                           index => item.Entry.BoxedValue = (Net.PeerBoardFadeMode)index);
            return true;
        }

        // THE GRAPHICS PRESET BRANCH STOOD HERE — one dropdown standing in for three dials (MSAA,
        // eye resolution, the per-pixel light cap), reading its current index off those three dials
        // and writing all three on a pick. It is DELETED, not merely unreachable, with the offering
        // it drew (user ruling 2026-09-05: "Entferne die Graphik-Profile wieder in den
        // VR-Einstellungen, die mag ich nicht.") — and deleting it is what actually removes the
        // hazard, because RenderQuality.ApplyPresetByIndex was the ONLY path in the mod that could
        // overwrite a hand-set MSAA or eye-resolution value, and this call site was its only live
        // caller. The three dials are the authority now, with exactly one writer: the player.
        // Rig/RenderQuality.cs carries the same tombstone at the other end.

        // The ENVIRONMENT is a user-facing CHOICE like the board movement scheme above: the
        // dropdown must read in the player's language ("DnD-Keller"), not the raw enum members
        // (Default/Cellar/SwampNight stay the config/log identity). Index maps 1:1 onto the
        // enum values (Default=0/Cellar=1/SwampNight=2, documented at Core.SkyStyle).
        // Reaching this branch means the catalog produced the bound [Sky] Style entry.
        if (string.Equals(item.Section, "Sky", StringComparison.Ordinal))
        {
            // FIVE ENVIRONMENTS, THE SAME FIVE THE TILE STRIP OFFERS, AND THE SAME WRITE. This
            // dropdown is what the row ladder falls back to when the picture strip cannot build,
            // and it used to be a DIFFERENT control: four values instead of five, a bare write to
            // [Sky] Style, and no clear of [MixedReality] Enabled — so a player who reached the
            // safety net could pick "Keller" and keep looking at their living room, which is the
            // outcome the strip's own ChooseSky exists to prevent (2026-09 redundancy audit, R45).
            // Index 0-3 map 1:1 onto SkyStyle (documented at Core.SkyStyle), 4 is mixed reality;
            // that vocabulary and both writes live in VariantTilesTable now, so the two doors
            // cannot answer differently again.
            string[] skyNames =
            {
                Loc.Mod("sky_default"),
                Loc.Mod("sky_cellar"),
                Loc.Mod("sky_swamp"),
                Loc.Mod("sky_off"),
                Loc.Mod("vr_o_mrenabled"),
            };
            BuildPresetRow(parent, item, caption, hintKey, skyNames,
                           EnvironmentIndex(),
                           index =>
                           {
                               // True = [MixedReality] Enabled moved, and it is a dependency parent
                               // whose children have to fold away with it — a repaint is not enough.
                               if (ChooseEnvironment(index))
                                   TickGuard.Run("VROptionsTab.EnvironmentFallback", Rebuild, "WorldUI");
                           });
            return true;
        }

        // THE HEAD MASK IS A NAMED CHOICE, NOT A NUMBER (see HasSpecialRow). The option list is
        // built from Net.HeadMaskLibrary — the class that owns MaskCount, the Mask_<id>.prefab path
        // convention and the wire clamp — rather than typed out here, so adding a mask is one edit
        // in that class plus its name string and this row grows by itself. Index IS the config
        // value by construction (MaskNames is in id order), which is why the apply is a plain write.
        // Reaching this branch at all means the catalog produced a bound Net/MaskId entry, so the
        // ConfigEntry is live.
        if (string.Equals(item.Section, "Net", StringComparison.Ordinal))
        {
            int current = Net.NetModule.MaskId.Value;
            string[] maskNames = Net.HeadMaskLibrary.MaskNames();
            int offered = maskNames.Length;

            // AN ID WE DO NOT OFFER IS SHOWN, NOT SWALLOWED. In practice BepInEx's own
            // AcceptableValueRange(0, MaskCount-1) has already clamped any hand-edited or legacy
            // value into range before this panel ever reads it, so the dropdown cannot be what
            // destroys it — but "cannot happen" is not a reason to display a lie, and
            // BuildPresetRow's own Mathf.Clamp would quietly show mask 0 while the config still
            // held something else. An out-of-range id (a config carried back from a build that
            // shipped more masks, a range that shrank) instead gets its OWN trailing entry naming
            // the raw number, the dropdown opens on that entry, and NOTHING is written until the
            // player deliberately picks a real mask — the apply refuses the synthetic index, so
            // re-selecting it is a no-op rather than a fabricated value.
            if (current < 0 || current >= offered)
            {
                var widened = new string[offered + 1];
                maskNames.CopyTo(widened, 0);
                widened[offered] = $"{Loc.Mod("mask")} {current}?";
                current = offered;
                maskNames = widened;
            }

            BuildPresetRow(parent, item, caption, hintKey, maskNames, current,
                           index =>
                           {
                               if (index >= 0 && index < offered)
                                   Net.NetModule.MaskId.Value = index; // BepInEx persists on set
                           });
            return true;
        }

        BuildPresetRow(parent, item, caption, hintKey, MixedReality.KeyColorNames, MixedReality.KeyColorIndex,
                       MixedReality.SetKeyColor);
        return true;
    }

    /// <summary>
    /// Look one curated entry up. A miss is REPORTED, not silently skipped: it means a setting was
    /// renamed or removed and the everyday list has quietly lost a row — exactly the kind of decay
    /// that is invisible until a player goes looking for something that used to be there.
    /// </summary>
    private static ConfigCatalog.ConfigItem? Lookup(string section, string key)
    {
        EnsureLookup();
        if (ByKey.TryGetValue(Id(section, key), out ConfigCatalog.ConfigItem item))
            return item;

        VRLog.Warn("WorldUI", $"VR options tab: curated entry [{section}] {key} no longer exists — "
                              + "the row is skipped. It was renamed or removed; the curated list in "
                              + "VROptionsTab.4.Curated.cs needs updating.");
        return null;
    }
}

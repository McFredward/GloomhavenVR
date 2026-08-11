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

        internal CuratedEntry(string section, string key, string captionKey)
        {
            Section = section;
            Key = key;
            CaptionKey = captionKey;
        }

        internal string Caption => Loc.Mod(CaptionKey);

        /// <summary>Hint key, derived from the caption key so the two can never drift apart.</summary>
        internal string HintKey => "h_" + CaptionKey;
    }

    /// <summary>A titled block of settings inside a category — the second level of order.</summary>
    internal sealed class CuratedSection
    {
        internal string LocKey = string.Empty;
        internal CuratedEntry[] Entries = System.Array.Empty<CuratedEntry>();

        internal string Label => Loc.Mod(LocKey);
    }

    /// <summary>One sub-tab: a caption and the sections beneath it.</summary>
    internal sealed class CuratedCategory
    {
        internal string LocKey = string.Empty;
        internal CuratedSection[] Sections = System.Array.Empty<CuratedSection>();

        internal string Label => Loc.Mod(LocKey);
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
                        new("Comfort", "FreeMovement", "free_movement"),
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
                        // The two ends of the zoom the pinch gesture may reach — direct
                        // neighbours of "Welt skalieren" that only lived under Erweitert while
                        // the gesture they clamp was an everyday control (audit 05 §2.1).
                        new("Comfort", "ScaleMin", "vr_o_zoommin"),
                        new("Comfort", "ScaleMax", "vr_o_zoommax"),
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
                    LocKey = "sec_visibility",
                    Entries = new CuratedEntry[]
                    {
                        new("Compat", "WallFade", "wall_see_through"),
                        // Fort superstructures fade with the walls — ruling 18 put the switch
                        // directly beside the walls-transparent toggle it extends.
                        new("WallFade", "StackedShellFade", "vr_o_stackedfade"),
                        // MP wall-fade sync (wire record 17): receiver-side toggle — it must
                        // be reachable in-headset next to the feature it extends.
                        new("WallFade", "SyncPeerFades", "wallfade_sync"),
                    },
                },
                new()
                {
                    LocKey = "sec_hands_aim",
                    Entries = new CuratedEntry[]
                    {
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
                    },
                },
            },
        },
        // ===================================== GRAFIK =====================================
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
                        // The two dials every headset owner looks for first (audit 03: the
                        // Grafik tab had NO render-quality row at all since the old panel's
                        // preset cycle lost its caller) — plus the two pure quality raises.
                        new("RenderQuality", "EyeResolutionScale", "vr_o_eyeres"),
                        new("RenderQuality", "MsaaLevel", "vr_o_msaa"),
                        new("RenderQuality", "ForceAnisotropic", "vr_o_aniso"),
                        // Ruling 19: the pixel-light cap is a visible look-vs-frames trade the
                        // game itself never exposes in VR.
                        new("RenderQuality", "PixelLightCount", "vr_o_pixellights"),
                        new("Compat", "DisablePostProcessing", "disable_post"),
                        new("Compat", "DisableVolumetricFog", "vr_o_fog"),
                        new("Compat", "WallFade", "wall_see_through"),
                        new("Rig", "ForwardRendering", "vr_o_forward"),
                        new("Rig", "MenuRig", "vr_o_menurig"),
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
                        new("MixedReality", "Enabled", "mixed_reality"),
                        new("MixedReality", "KeyColor", "key_color"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_performance",
                    Entries = new CuratedEntry[]
                    {
                        new("Core", "EnableGraphicsJobs", "vr_o_gfxjobs"),
                        new("Core", "AutoRestartForGraphicsJobs", "vr_o_autorestart"),
                    },
                },
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
                    },
                },
                new()
                {
                    LocKey = "vr_sec_cardhand",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "RevealMode", "vr_o_revealmode"),
                        new("Cards", "GrabButton", "vr_o_grabbutton"),
                        new("Cards", "InspectScale", "vr_o_inspectscale"),
                        // THE card size — 49 read sites, the single most player-visible size
                        // dial in the whole [Cards] section (audit 01 NORMAL).
                        new("Cards", "CardWidth", "vr_o_cardwidth"),
                        // Ruling 6: ONE everyday on/off for all five card/fan sounds. The five
                        // audio-item STRINGS stay under Erweitert ▸ Karten & Fächer ▸ Klänge
                        // (rulings 5/7: string entries are power-user material); this bool is
                        // an AND over them and never rewrites them.
                        new("Cards", "CardSoundsEnabled", "vr_o_cardsounds"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_piles_hints",
                    Entries = new CuratedEntry[]
                    {
                        new("Cards", "PileViewer", "vr_o_pileviewer"),
                        new("Cards", "ActivePile", "vr_o_activepile"),
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
        new()
        {
            LocKey = "cat_panels",
            Sections = new CuratedSection[]
            {
                new()
                {
                    LocKey = "vr_sec_panels",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "CombatLog", "show_combat_log"),
                        new("WorldUI", "InitiativeTrack", "vr_o_initiative"),
                        new("WorldUI", "ElementBoard", "vr_o_elemboard"),
                        new("WorldUI", "Objectives", "vr_o_objectives"),
                        new("WorldUI", "StatPanels", "vr_o_statpanels"),
                        new("WorldUI", "PropInfoCards", "vr_o_propinfo"),
                        new("WorldUI", "WristHud", "vr_o_wristhud"),
                        new("WorldUI", "ButtonCluster", "vr_o_buttoncluster"),
                        new("WorldUI", "Tooltips", "vr_o_tooltips"),
                        new("WorldUI", "ActionElementHints", "element_hints"),
                        new("WorldUI", "Dialogs", "vr_o_dialogs"),
                        new("WorldUI", "DecisionDock", "vr_o_decisiondock"),
                        new("WorldUI", "EnemyReveal", "vr_o_enemyreveal"),
                        new("WorldUI", "LoadingIndicator", "vr_o_loading"),
                    },
                },
                new()
                {
                    // The bar family has its own heading now: with occlusion joining the
                    // curated size trio (audit 04 straggler) it is five rows of one object,
                    // and the naming pass unified them on "Lebensbalken: …" — a family that
                    // shares a name should share a heading.
                    LocKey = "vr_sec_bars",
                    Entries = new CuratedEntry[]
                    {
                        new("WorldUI", "ActorBars", "vr_o_actorbars"),
                        // User request: the health bars' SIZE, plus the two ends of the clamp
                        // that keeps the table zoom from carrying that size away ("sowie ein
                        // minimum und maximum der Größe, damit sie sich trotz zoomen nie über
                        // die Grenzen hinaus skalieren können").
                        new("WorldUI", "BarSizeScale", "vr_o_barsize"),
                        new("WorldUI", "BarZoomMinScale", "vr_o_barsizemin"),
                        new("WorldUI", "BarZoomMaxScale", "vr_o_barsizemax"),
                        new("WorldUI", "BarsOccluded", "vr_o_barsoccluded"),
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
                        new("WorldUI", "HexHintFollowView", "vr_o_hexhintfollow"),
                        new("ButtonAnim", "Enable", "vr_o_buttonanim"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_keyboard",
                    Entries = new CuratedEntry[]
                    {
                        new("Keyboard", "Enabled", "vr_o_keyboard"),
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
                        new("Hands", "HandStyle", "hands"),
                        new("Hands", "GloveScale", "vr_o_handscale"),
                        new("Hands", "PlateScale", "vr_o_handscale"),
                        new("Hands", "ArcaneScale", "vr_o_handscale"),
                        new("Net", "MaskId", "head_mask"),
                        new("Net", "MaskSize", "mask_size"),
                        new("Net", "MirrorEnabled", "mirror"),
                    },
                },
                new()
                {
                    LocKey = "vr_sec_mp_presence",
                    Entries = new CuratedEntry[]
                    {
                        new("Net", "Enabled", "vr_o_netenabled"),
                        new("Rig", "SpawnInCircle", "vr_o_circle"),
                        new("Net", "RemoteBoards", "remote_boards"),
                        new("Net", "NameTags", "vr_o_nametags"),
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
            && string.Equals(item.Key, "MaskId", StringComparison.Ordinal));

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

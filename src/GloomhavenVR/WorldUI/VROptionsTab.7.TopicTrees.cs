using System.Collections.Generic;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Erweitert &gt; "Menüs &amp; Tafeln" and "Karten &amp; Fächer": the second and third
/// hand-arranged topic pages, after the control board's (<c>VROptionsTab.6.BoardTopic.cs</c>).
///
/// <para>WHY THESE TWO. The 2026-08 menu audit (05-structure.md, S3) found both topics carrying
/// the exact disease the user had already reported once on the per-board page ("Aktuell sucht man
/// dort immer rum ohne wirklich zu finden wonach man sucht"): the automatic key-prefix grouping
/// over an oversized section produces a dozen leading-word clusters, and <c>FoldSmallGroups</c>
/// sweeps every under-threshold cluster — Flat, Drag, Poke, Video, Click, Manual, Keyboard, … —
/// into ONE 30–40-row "Allgemein" grab-bag. "Menüs &amp; Tafeln" (~80 entries) and "Karten &amp;
/// Fächer" (~95) were the two largest topics without a tree. The user's blanket ruling on the
/// audit ("Setze erstmal alle Vorschläge zu den Settings deinerseits so um") ordered the same
/// remedy the board page got: an explicit two-level heading tree, arranged by what the player is
/// trying to adjust, with localized hover hints on every heading.</para>
///
/// <para>SAME MACHINERY, SAME PROMISES. The trees reuse the board page's own row types
/// (<see cref="BoardRef"/> with <c>perBoard: false</c> — these topics hold no per-board keys;
/// the catalog routes those to the Steuerbrett page) and the same two invariants: resolution
/// CLAIMS every placed item so the catch-all cannot show it twice, and everything the tree does
/// not claim still renders under a final "Allgemein" heading. A curation gap costs one heading,
/// never a setting.</para>
///
/// <para>The per-pile triples (<c>FanStepDegrees_Items/_Discard/_Burnt</c>) are listed as three
/// explicit rows: the pile kinds are not variants of a player choice (the way the boards and
/// hand styles are), so the variant-folding machinery rightly leaves all three visible.</para>
/// </summary>
internal static partial class VROptionsTab
{
    // ==========================================================================================
    //  Erweitert ▸ Menüs & Tafeln
    // ==========================================================================================

    /// <summary>
    /// The panels topic, in reading order: the panel switches a player toggles most, then the
    /// three panel families big enough to own a heading (combat log, health bars, the 2D screen),
    /// then input, the hex hint, window plumbing and the keyboard. Labels and hover hints live in
    /// <c>Loc.cs</c> under the same keys ("vr_pt_*" / "h_vr_pt_*").
    /// </summary>
    private static readonly BoardTop[] PanelsTree =
    {
        new()
        {
            LocKey = "vr_pt_switches",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_pt_sw_show",
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "Master", perBoard: false),
                        new BoardRef("WorldUI", "InitiativeTrack", perBoard: false),
                        new BoardRef("WorldUI", "ElementBoard", perBoard: false),
                        new BoardRef("WorldUI", "Objectives", perBoard: false),
                        new BoardRef("WorldUI", "StatPanels", perBoard: false),
                        new BoardRef("WorldUI", "PropInfoCards", perBoard: false),
                        new BoardRef("WorldUI", "EnemyReveal", perBoard: false),
                        new BoardRef("WorldUI", "Dialogs", perBoard: false),
                        new BoardRef("WorldUI", "DecisionDock", perBoard: false),
                        new BoardRef("WorldUI", "UseBars", perBoard: false),
                        new BoardRef("WorldUI", "DoomPicker", perBoard: false),
                        new BoardRef("WorldUI", "DistributePanel", perBoard: false),
                        new BoardRef("WorldUI", "TrayNativeControls", perBoard: false),
                        new BoardRef("WorldUI", "ButtonCluster", perBoard: false),
                        new BoardRef("WorldUI", "WristHud", perBoard: false),
                        new BoardRef("WorldUI", "Tooltips", perBoard: false),
                        new BoardRef("WorldUI", "ActionElementHints", perBoard: false),
                        new BoardRef("WorldUI", "LoadingIndicator", perBoard: false),
                    },
                },
                new()
                {
                    LocKey = "vr_pt_sw_fine",
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "HoverInfoScale", perBoard: false),
                        new BoardRef("WorldUI", "EnemyRevealBoardClearance", perBoard: false),
                        new BoardRef("WorldUI", "PanelMipBake", perBoard: false),
                        new BoardRef("WorldUI", "CanvasScaleMm", perBoard: false),
                        new BoardRef("WorldUI", "InitiativeDepthMaxSpreadPx", perBoard: false),
                        new BoardRef("WorldUI", "PanelsFollowView", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_combatlog",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "CombatLog", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogFollowSeat", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogForward", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogRight", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogUp", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogScale", perBoard: false),
                        new BoardRef("WorldUI", "CombatLogUserClosed", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_bars",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "ActorBars", perBoard: false),
                        new BoardRef("WorldUI", "BarSizeScale", perBoard: false),
                        new BoardRef("WorldUI", "BarZoomMinScale", perBoard: false),
                        new BoardRef("WorldUI", "BarZoomMaxScale", perBoard: false),
                        new BoardRef("WorldUI", "BarFixedSize", perBoard: false),
                        new BoardRef("WorldUI", "BarsOccluded", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_screen",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_pt_screen_basic",
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "FlatScreen", perBoard: false),
                        new BoardRef("WorldUI", "FlatScreenAutoShow", perBoard: false),
                        new BoardRef("WorldUI", "ShowIntro", perBoard: false),
                        new BoardRef("WorldUI", "ScreenWidth", perBoard: false),
                        new BoardRef("WorldUI", "ScreenDistance", perBoard: false),
                        new BoardRef("WorldUI", "DesktopMirrorLeftEye", perBoard: false),
                    },
                },
                new()
                {
                    LocKey = "vr_pt_screen_depth",
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "StereoScreen", perBoard: false),
                        new BoardRef("WorldUI", "ScreenDepthStrength", perBoard: false),
                        new BoardRef("WorldUI", "ScreenParallaxScale", perBoard: false),
                        new BoardRef("WorldUI", "VideoDepthLayer", perBoard: false),
                        new BoardRef("WorldUI", "VideoDepth", perBoard: false),
                        new BoardRef("WorldUI", "ScreenLayerSplit", perBoard: false),
                        new BoardRef("WorldUI", "DemoteOverlaySolidClears", perBoard: false),
                    },
                },
                new()
                {
                    LocKey = "vr_pt_screen_map",
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "MapAlbedoRender", perBoard: false),
                        new BoardRef("WorldUI", "MapWindOpacity", perBoard: false),
                        new BoardRef("WorldUI", "ScreenLeftMirrorFallback", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_click",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "PokeClick", perBoard: false),
                        new BoardRef("WorldUI", "PokePressDepthMm", perBoard: false),
                        new BoardRef("WorldUI", "DecisionPokeDeliberate", perBoard: false),
                        new BoardRef("WorldUI", "ClickMode", perBoard: false),
                        new BoardRef("WorldUI", "ClickLatch", perBoard: false),
                        new BoardRef("WorldUI", "DragUnlockDegrees", perBoard: false),
                        new BoardRef("WorldUI", "DragUnlockSeconds", perBoard: false),
                        new BoardRef("WorldUI", "ForceMouseMode", perBoard: false),
                        new BoardRef("WorldUI", "SuppressPhysicalMouse", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_hexhint",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "HexHintFollowView", perBoard: false),
                        new BoardRef("WorldUI", "HexHintDistance", perBoard: false),
                        new BoardRef("WorldUI", "HexHintDrop", perBoard: false),
                        new BoardRef("WorldUI", "HexHintSide", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_pt_windows",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("WorldUI", "ModalStyle", perBoard: false),
                        new BoardRef("WorldUI", "CatchAllModals", perBoard: false),
                        new BoardRef("WorldUI", "MenuPopupFloat", perBoard: false),
                        new BoardRef("WorldUI", "ManualScreenChord", perBoard: false),
                        new BoardRef("WorldUI", "ManualScreenChordSeconds", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_sec_keyboard",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Keyboard", "Enabled", perBoard: false),
                        new BoardRef("Keyboard", "AutoCapitalise", perBoard: false),
                    },
                },
            },
        },
    };

    // ==========================================================================================
    //  Erweitert ▸ Karten & Fächer
    // ==========================================================================================

    /// <summary>
    /// The cards topic: fan shape first (that is what the ~30 Fan* dials are), then behaviour,
    /// animations, the item pile's dressing, the held card, the piles, the control board's
    /// spawn/pose state, and the sounds. Labels/hints: "vr_ct_*" / "h_vr_ct_*" in <c>Loc.cs</c>.
    /// </summary>
    private static readonly BoardTop[] CardsTree =
    {
        new()
        {
            LocKey = "vr_ct_fanshape",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "CardWidth", perBoard: false),
                        new BoardRef("Cards", "FanPerCardStepDegrees", perBoard: false),
                        new BoardRef("Cards", "FanArcSweepDegrees", perBoard: false),
                        new BoardRef("Cards", "FanEffectiveRadius", perBoard: false),
                        new BoardRef("Cards", "FanPalmOffset", perBoard: false),
                        new BoardRef("Cards", "FanCurveByFill", perBoard: false),
                        new BoardRef("Cards", "FanMaxHandForCurve", perBoard: false),
                        new BoardRef("Cards", "FanFlatCurvatureFactor", perBoard: false),
                        new BoardRef("Cards", "FanCurvePower", perBoard: false),
                        new BoardRef("Cards", "FanCurveMinCards", perBoard: false),
                        new BoardRef("Cards", "FanTiltFactor", perBoard: false),
                        new BoardRef("Cards", "FanSideDepthCurve", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_ct_fanbehavior",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "RevealMode", perBoard: false),
                        new BoardRef("Cards", "RevealEnterDegrees", perBoard: false),
                        new BoardRef("Cards", "RevealExitDegrees", perBoard: false),
                        new BoardRef("Cards", "RevealIgnoreWhenGrabbing", perBoard: false),
                        new BoardRef("Cards", "FanFollowSmoothing", perBoard: false),
                        new BoardRef("Cards", "FanFollowDeadzone", perBoard: false),
                        new BoardRef("Cards", "FanFaceViewer", perBoard: false),
                        new BoardRef("Cards", "FanGazeApexFollow", perBoard: false),
                        new BoardRef("Cards", "FanGazeSmoothing", perBoard: false),
                        new BoardRef("Cards", "FanGazeBias", perBoard: false),
                        new BoardRef("Cards", "FanSplitMultiplier", perBoard: false),
                        new BoardRef("Cards", "FanSplitFalloff", perBoard: false),
                        new BoardRef("Cards", "FanHoverSplitScale", perBoard: false),
                        new BoardRef("Cards", "FanSelectedPopForward", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_ct_anim",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_ct_anim_open",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "FanOpenDuration", perBoard: false),
                        new BoardRef("Cards", "FanOpenStagger", perBoard: false),
                        new BoardRef("Cards", "FanCloseDuration", perBoard: false),
                        new BoardRef("Cards", "CardLerpSpeed", perBoard: false),
                    },
                },
                new()
                {
                    LocKey = "vr_ct_anim_swap",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "FanSwapDuration", perBoard: false),
                        new BoardRef("Cards", "FanSwapStagger", perBoard: false),
                        new BoardRef("Cards", "FanSwapOverlap", perBoard: false),
                        new BoardRef("Cards", "FanSwapTravel", perBoard: false),
                        new BoardRef("Cards", "FanSwapArc", perBoard: false),
                        new BoardRef("Cards", "FanSwapSpinDegrees", perBoard: false),
                        new BoardRef("Cards", "FanSwapSeedScale", perBoard: false),
                        new BoardRef("Cards", "FanSwapSettleOvershoot", perBoard: false),
                    },
                },
                new()
                {
                    // Both OFF by user ruling 2026-08-03 (the burst sprayed across the board);
                    // kept reachable here, not promoted (rulings 8/9).
                    LocKey = "vr_ct_anim_fx",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "CardDust", perBoard: false),
                        new BoardRef("Cards", "GameCardParticles", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_ct_items",
            Subs = new BoardSub[]
            {
                new()
                {
                    LocKey = "vr_ct_items_fan",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "ItemFanOpenDuration", perBoard: false),
                        new BoardRef("Cards", "ItemFanOpenStagger", perBoard: false),
                        new BoardRef("Cards", "ItemFanOpenArc", perBoard: false),
                        new BoardRef("Cards", "ItemFanOpenSpinDegrees", perBoard: false),
                        new BoardRef("Cards", "ItemFanSeedScale", perBoard: false),
                        new BoardRef("Cards", "ItemFanSettleOvershoot", perBoard: false),
                        new BoardRef("Cards", "ItemFanCloseDuration", perBoard: false),
                        new BoardRef("Cards", "ItemFanCloseStagger", perBoard: false),
                    },
                },
                new()
                {
                    LocKey = "vr_ct_items_cue",
                    Entries = new[]
                    {
                        new BoardRef("Cards", "ItemCueBeatSeconds", perBoard: false),
                        new BoardRef("Cards", "ItemCueRingReach", perBoard: false),
                        new BoardRef("Cards", "ItemCueRingAlpha", perBoard: false),
                        new BoardRef("Cards", "ItemCueEmberRate", perBoard: false),
                        new BoardRef("Cards", "ItemCueEmberSize", perBoard: false),
                        new BoardRef("Cards", "ItemBerthRingThickness", perBoard: false),
                        new BoardRef("Cards", "ItemBerthGlow", perBoard: false),
                        new BoardRef("Cards", "ItemBerthPingSeconds", perBoard: false),
                        new BoardRef("Cards", "ItemBerthPingReach", perBoard: false),
                        new BoardRef("Cards", "ItemBerthRevealSeconds", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            LocKey = "vr_ct_held",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "GrabButton", perBoard: false),
                        new BoardRef("Cards", "InspectScale", perBoard: false),
                        new BoardRef("Cards", "HeldFaceBias", perBoard: false),
                        new BoardRef("Cards", "HeldPinchOffset", perBoard: false),
                        new BoardRef("Cards", "HeldForward", perBoard: false),
                        new BoardRef("Cards", "HeldOffPalm", perBoard: false),
                        new BoardRef("Cards", "WantedSlotHint", perBoard: false),
                        new BoardRef("Cards", "SlotCardInset", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            // The pile stacks docked at the board edge and their poke-open browse fans. The
            // per-PILE triples (_Items/_Discard/_Burnt) are three real rows each — pile kinds
            // are not a player-chosen variant, so nothing folds them.
            LocKey = "vr_ct_piles",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "PileViewer", perBoard: false),
                        new BoardRef("Cards", "ActivePile", perBoard: false),
                        new BoardRef("Cards", "FanRadius", perBoard: false),
                        new BoardRef("Cards", "BrowseFanOffset", perBoard: false),
                        new BoardRef("Cards", "FanStepDegrees_Items", perBoard: false),
                        new BoardRef("Cards", "FanStepDegrees_Discard", perBoard: false),
                        new BoardRef("Cards", "FanStepDegrees_Burnt", perBoard: false),
                        new BoardRef("Cards", "FanRadiusFactor_Items", perBoard: false),
                        new BoardRef("Cards", "FanRadiusFactor_Discard", perBoard: false),
                        new BoardRef("Cards", "FanRadiusFactor_Burnt", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            // The control board's choice, everyday pose dials, grab-written pose STATE and the
            // spawn recipe — the "edit only to reset" block, in one place.
            LocKey = "vr_ct_board",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "Board", perBoard: false),
                        new BoardRef("Cards", "TrayScale", perBoard: false),
                        new BoardRef("Cards", "TrayFollow", perBoard: false),
                        new BoardRef("Cards", "BoardMoveMode", perBoard: false),
                        new BoardRef("Cards", "TrayYaw", perBoard: false),
                        new BoardRef("Cards", "TrayPitch", perBoard: false),
                        new BoardRef("Cards", "TrayForward", perBoard: false),
                        new BoardRef("Cards", "TrayDown", perBoard: false),
                        new BoardRef("Cards", "TrayRight", perBoard: false),
                        new BoardRef("Cards", "SpawnLeftOfHead", perBoard: false),
                        new BoardRef("Cards", "SpawnSideMeters", perBoard: false),
                        new BoardRef("Cards", "SpawnForwardMeters", perBoard: false),
                        new BoardRef("Cards", "SpawnDownMeters", perBoard: false),
                        new BoardRef("Cards", "BoardMinWidthMeters", perBoard: false),
                        new BoardRef("Cards", "BoardMaxWidthMeters", perBoard: false),
                    },
                },
            },
        },
        new()
        {
            // Rulings 5/6/7: the everyday switch ([Cards] CardSoundsEnabled, also curated in
            // Brett & Karten) leads the block; the five free-text audio-item strings stay
            // power-user rows behind it.
            LocKey = "vr_ct_sounds",
            Subs = new BoardSub[]
            {
                new()
                {
                    Entries = new[]
                    {
                        new BoardRef("Cards", "CardSoundsEnabled", perBoard: false),
                        new BoardRef("Cards", "FanRevealSound", perBoard: false),
                        new BoardRef("Cards", "FanHideSound", perBoard: false),
                        new BoardRef("Cards", "CardGrabSound", perBoard: false),
                        new BoardRef("Cards", "CardPlaceSound", perBoard: false),
                        new BoardRef("Cards", "CardTakeBackSound", perBoard: false),
                    },
                },
            },
        },
    };

    // ==========================================================================================
    //  Shared tree body builder
    // ==========================================================================================

    /// <summary>
    /// A topic page's body from a hand-arranged tree — the generic sibling of
    /// <see cref="BuildBoardTopicBody"/>, minus the per-board variant note and copy rows (these
    /// topics hold no per-variant families; the defensive <see cref="IsShownForCurrentVariant"/>
    /// filter stays, so a variant key that ever lands here is still folded correctly). Called by
    /// <c>BuildTopic</c> in place of the automatic grouping; the back link and the topic header
    /// are already built by the caller.
    /// </summary>
    private static int BuildTreeTopicBody(ConfigCatalog.ConfigTopic topic, BoardTop[] tree)
    {
        if (ContentRoot == null)
            return 0;

        IReadOnlyList<ConfigCatalog.ConfigGroup> groups = ConfigCatalog.Groups(topic);
        var all = new List<ConfigCatalog.ConfigItem>(160);
        for (int g = 0; g < groups.Count; g++)
            all.AddRange(groups[g].Items);

        var placed = new HashSet<ConfigCatalog.ConfigItem>();
        var resolved = new List<ConfigCatalog.ConfigItem>(24);
        int rows = 0;

        for (int t = 0; t < tree.Length; t++)
        {
            BoardTop top = tree[t];

            // Resolve the WHOLE top first: a heading over nothing reads as a setting that
            // failed to load, and every resolved item is claimed away from the catch-all.
            var subItems = new List<ConfigCatalog.ConfigItem>[top.Subs.Length];
            int visibleInTop = 0;
            for (int s = 0; s < top.Subs.Length; s++)
            {
                resolved.Clear();
                BoardRef[] entries = top.Subs[s].Entries;
                for (int e = 0; e < entries.Length; e++)
                    ResolveBoardRef(entries[e], resolved, placed);

                subItems[s] = new List<ConfigCatalog.ConfigItem>(resolved.Count);
                for (int i = 0; i < resolved.Count; i++)
                {
                    if (!IsShownForCurrentVariant(resolved[i]))
                        continue;
                    subItems[s].Add(resolved[i]);
                    visibleInTop++;
                }
            }

            if (visibleInTop == 0)
                continue;

            BuildHeader(ContentRoot, Loc.Mod(top.LocKey), "h_" + top.LocKey);
            for (int s = 0; s < top.Subs.Length; s++)
            {
                List<ConfigCatalog.ConfigItem> items = subItems[s];
                if (items.Count == 0)
                    continue;
                string? locKey = top.Subs[s].LocKey;
                if (locKey != null)
                    BuildHeader(ContentRoot, Loc.Mod(locKey), "h_" + locKey, sub: true);
                for (int i = 0; i < items.Count; i++)
                    rows += BuildItem(items[i], VariantFreeCaption(items[i]));
            }
        }

        // THE SAFETY NET (same promise as the board page and the curated tabs): everything the
        // tree did not claim — typically an entry bound after this file was arranged — still
        // renders, under "Allgemein", instead of silently vanishing.
        bool miscHeaded = false;
        for (int i = 0; i < all.Count; i++)
        {
            ConfigCatalog.ConfigItem item = all[i];
            if (placed.Contains(item) || !IsShownForCurrentVariant(item))
                continue;
            if (!miscHeaded)
            {
                miscHeaded = true;
                BuildHeader(ContentRoot, Loc.Mod("cfg_group_misc"), "h_vr_tree_misc");
            }
            rows += BuildItem(item, VariantFreeCaption(item));
        }

        return rows;
    }
}

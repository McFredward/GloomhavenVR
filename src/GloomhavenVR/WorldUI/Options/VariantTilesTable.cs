using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The choices offered by the four picture pickers. The drawing lives in VariantTiles.cs.
/// Each tile writes its existing typed config entry and uses the existing localized name.
/// A valid current value selects one tile; an unknown board or mask value selects none rather
/// than pretending that a different asset was chosen.
/// </summary>
internal static partial class VROptionsTab
{
    private const string TileResourcePrefix = "GloomhavenVR.Assets.";

    /// <summary>The tiles for one picker, or null when this setting is not one.</summary>
    private static VariantTile[]? VariantTilesFor(ConfigCatalog.ConfigItem item)
    {
        if (string.Equals(item.Section, "Sky", StringComparison.Ordinal)
            && string.Equals(item.Key, "Style", StringComparison.Ordinal))
            return EnvironmentTiles();

        if (string.Equals(item.Section, "Hands", StringComparison.Ordinal)
            && string.Equals(item.Key, "HandStyle", StringComparison.Ordinal))
            return HandTiles();

        if (string.Equals(item.Section, "Net", StringComparison.Ordinal)
            && string.Equals(item.Key, "MaskId", StringComparison.Ordinal))
            return MaskTiles();

        if (string.Equals(item.Section, "Cards", StringComparison.Ordinal)
            && string.Equals(item.Key, "Board", StringComparison.Ordinal))
            return BoardTiles();

        return null;
    }

    // ---- boards -----------------------------------------------------------------------------

    /// <summary>Keep enum identities, localized names and the existing tray rebuild event.
    /// Apply also rebuilds the per-board settings below this strip.</summary>
    private static VariantTile[] BoardTiles()
    {
        var tiles = new VariantTile[ControlBoards.Count];
        for (int i = 0; i < tiles.Length; i++)
        {
            ControlBoard board = (ControlBoard)i;
            tiles[i] = new VariantTile
            {
                Resource = TileResourcePrefix + "tile_board_" + board.ToString().ToLowerInvariant() + ".png",
                Label = () => ControlBoards.DisplayName(board),
                Selected = () => CardsConfig.Board != null && CardsConfig.Board.Value == board,
                Choose = () =>
                {
                    if (CardsConfig.Board != null)
                        CardsConfig.Board.Value = board;
                    return false;
                },
            };
        }
        return tiles;
    }

    // ---- environment ------------------------------------------------------------------------

    /// <summary>Is mixed reality on? False whenever the entry has not been bound yet, which is the
    /// same answer <c>MixedReality.BackingsWanted</c> gives before the rig has ticked.</summary>
    private static bool MixedRealityOn => MixedReality.Enabled != null && MixedReality.Enabled.Value;

    /// <summary>
    /// The sky the game is actually showing. Not a bare equality against the stored value:
    /// <c>Enum.Parse</c> accepts numeric strings, so a hand-edited cfg can hold <c>(SkyStyle)7</c>,
    /// and <c>SkyAlternative.Tick</c> renders that as Default. This agrees with the picture.
    /// </summary>
    private static SkyStyle EffectiveSky()
    {
        if (SkyAlternative.Style == null)
            return SkyStyle.Default;
        SkyStyle style = SkyAlternative.Style.Value;
        return style is SkyStyle.Cellar or SkyStyle.SwampNight or SkyStyle.OffBlack
            ? style
            : SkyStyle.Default;
    }

    /// <summary>
    /// Pick a sky. Clears mixed reality ONLY IF it was on — otherwise the player picks "Keller" and
    /// keeps looking at their living room, which is the one outcome this strip exists to prevent.
    /// Returns true in exactly that case, because <c>[MixedReality] Enabled</c> is a dependency
    /// parent and its children have to fold away with it.
    /// </summary>
    private static bool ChooseSky(SkyStyle style)
    {
        if (SkyAlternative.Style != null)
            SkyAlternative.Style.Value = style;

        if (!MixedRealityOn)
            return false;
        MixedReality.Enabled.Value = false;
        return true;
    }

    /// <summary>
    /// Turn mixed reality on WITHOUT touching <c>[Sky] Style</c>. MR is a mode laid over the
    /// environment choice, not another value of it: leaving the sky alone means switching MR back
    /// off returns the room the player had picked, instead of silently resetting them to Default.
    /// </summary>
    private static bool ChooseMixedReality()
    {
        if (MixedReality.Enabled == null || MixedReality.Enabled.Value)
            return false;
        MixedReality.Enabled.Value = true;
        return true;
    }

    /// <summary>
    /// THE ENVIRONMENT CHOICE, INDEPENDENT OF THE CONTROL THAT DRAWS IT.
    ///
    /// <para>Five environments over TWO config keys: <c>[Sky] Style</c>'s four members plus
    /// <c>[MixedReality] Enabled</c> as the fifth. The picture-tile strip below is the control the
    /// player gets; the dropdown in <c>VROptionsTab.4.Curated.cs</c> is the one the row ladder falls
    /// back to when <c>VariantTilesFor</c> hands back nothing (missing art is NOT that case — a
    /// tile without a picture is still built, label-only). That fallback used to write <c>[Sky]
    /// Style</c> and nothing else, so a player who reached it could pick "Keller" and go on looking
    /// at their living room — the one outcome this strip exists to prevent, argued at
    /// <see cref="ChooseSky"/>, and the argument only ever reached one of the two doors (2026-09
    /// redundancy audit, R45).</para>
    ///
    /// <para>Both doors go through these two methods now. The INDEX is the shared vocabulary: 0-3 are
    /// <see cref="SkyStyle"/> 1:1 (documented at Core.SkyStyle), 4 is mixed reality.</para>
    /// </summary>
    internal const int MixedRealityEnvironmentIndex = 4;

    /// <summary>The environment the player is looking at, as an index into the five. Mixed reality
    /// wins when it is on, exactly as the tiles' own <c>Selected</c> predicates do — with MR on,
    /// none of the four skies is what is being rendered.</summary>
    internal static int EnvironmentIndex() =>
        MixedRealityOn ? MixedRealityEnvironmentIndex : (int)EffectiveSky();

    /// <summary>Write an environment choice. Returns true when the pane has to be REBUILT rather
    /// than repainted, which is what a change to the <c>[MixedReality] Enabled</c> dependency parent
    /// costs. Out-of-range indices land on the sky half and are clamped by
    /// <see cref="ChooseSky"/>'s own cast, which is the same answer <see cref="EffectiveSky"/>
    /// gives for a hand-edited cfg.</summary>
    internal static bool ChooseEnvironment(int index) =>
        index == MixedRealityEnvironmentIndex ? ChooseMixedReality() : ChooseSky((SkyStyle)index);

    private static VariantTile SkyTile(SkyStyle style, string art, string locKey) => new()
    {
        Resource = TileResourcePrefix + art + ".png",
        Label = () => Loc.Mod(locKey),
        // A sky tile is lit only while MR is OFF: with MR on, none of these is what the player sees.
        Selected = () => !MixedRealityOn && EffectiveSky() == style,
        Choose = () => ChooseSky(style),
    };

    /// <summary>
    /// The five environments the player perceives, in the order the user listed them as a group.
    /// Four are <see cref="SkyStyle"/> members; the fifth is <c>[MixedReality] Enabled</c> — see the
    /// class doc on <c>VariantTiles.cs</c> for why one strip spans two keys.
    /// </summary>
    private static VariantTile[] EnvironmentTiles() => new[]
    {
        SkyTile(SkyStyle.Default, "tile_env_default", "sky_default"),
        SkyTile(SkyStyle.Cellar, "tile_env_cellar", "sky_cellar"),
        SkyTile(SkyStyle.SwampNight, "tile_env_swamp", "sky_swamp"),
        SkyTile(SkyStyle.OffBlack, "tile_env_offblack", "sky_off"),
        new VariantTile
        {
            Resource = TileResourcePrefix + "tile_env_mr.png",
            Label = () => Loc.Mod("vr_o_mrenabled"),
            Selected = () => EnvironmentIndex() == MixedRealityEnvironmentIndex,
            Choose = () => ChooseEnvironment(MixedRealityEnvironmentIndex),
        },
    };

    // ---- hands ------------------------------------------------------------------------------

    /// <summary>
    /// One tile per <see cref="HandStyle"/>. The names are the ones the per-variant heading already
    /// uses (<c>vr_style_*</c> — "Lederhandschuh", "Panzerhandschuh", "Magierhandschuh"), which also
    /// closes an old wart: the row used to be the GENERIC enum control and showed the raw English
    /// member names Glove/Plate/Arcane in a German menu.
    ///
    /// <para>No rebuild is requested from here. <c>[Hands] HandStyle</c> is a variant selector, and
    /// <c>Apply</c> rebuilds the page for those on its own so the per-variant rows below become the
    /// new hand's.</para>
    /// </summary>
    private static VariantTile[] HandTiles()
    {
        var art = new Dictionary<HandStyle, string>(3)
        {
            [HandStyle.Glove] = "tile_hand_glove",
            [HandStyle.Plate] = "tile_hand_plate",
            [HandStyle.Arcane] = "tile_hand_arcane",
        };

        var tiles = new List<VariantTile>(HandStyles.Count);
        for (int i = 0; i < HandStyles.Count; i++)
        {
            HandStyle style = HandStyles.Clamp(i);
            string name = style.ToString();
            tiles.Add(new VariantTile
            {
                Resource = art.TryGetValue(style, out string? file) ? TileResourcePrefix + file + ".png" : string.Empty,
                Label = () => Loc.Mod("vr_style_" + name.ToLowerInvariant()),
                Selected = () => Plugin.HandStyle != null
                                 && HandStyles.Clamp((int)Plugin.HandStyle.Value) == style,
                Choose = () =>
                {
                    if (Plugin.HandStyle != null)
                        Plugin.HandStyle.Value = style;
                    return false;
                },
            });
        }
        return tiles.ToArray();
    }

    // ---- masks ------------------------------------------------------------------------------

    /// <summary>
    /// One tile per head mask, built from <c>HeadMaskLibrary.MaskCount</c> and its own name table —
    /// the same property the dropdown had, and for the same reason: a fourth mask is one edit in
    /// <c>HeadMaskLibrary</c> plus its loc string, and this strip grows on its own. A mask that has
    /// no tile art yet degrades to a labelled tile rather than vanishing from the picker.
    /// </summary>
    private static VariantTile[] MaskTiles()
    {
        string[] names = HeadMaskLibrary.MaskNames();
        var tiles = new List<VariantTile>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int id = i;
            string name = names[i];
            tiles.Add(new VariantTile
            {
                Resource = TileResourcePrefix + "tile_mask_" + id + ".png",
                Label = () => name,
                // NOT Mathf.Clamp(...) == id, which lit the LAST tile for any id past the end.
                // The dropdown this strip replaced spends a paragraph refusing exactly that —
                // "'cannot happen' is not a reason to display a lie" (VROptionsTab.4.Curated.cs) —
                // and then the live path did it anyway (2026-09 redundancy audit, R45). The
                // dropdown's answer, a synthetic trailing entry naming the raw number, has no tile
                // to be drawn on; the honest tile-strip answer is that NO tile is lit, so the pane
                // says "not one of these" instead of naming the wrong one. Unreachable in practice
                // either way: [Net] MaskId is bound with AcceptableValueRange(0, MaskCount-1), so
                // BepInEx clamps a hand-edited value before this ever reads it.
                Selected = () => NetModule.MaskId != null && NetModule.MaskId.Value == id,
                Choose = () =>
                {
                    if (NetModule.MaskId != null)
                        NetModule.MaskId.Value = id;
                    return false;
                },
            });
        }
        return tiles.ToArray();
    }
}

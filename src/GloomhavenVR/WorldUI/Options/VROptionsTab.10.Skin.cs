using System.Text;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE SKIN: what the mod's own graphics in this menu are painted with, and where those values
/// come from.
///
/// <para><b>THE DEFECT</b> (user, 2026-09-05, verbatim): <i>"Ich mag die Veränderung am Aussehen
/// nicht im Optionsmenu: a) Du hast einfach einen einfarbigen Hintergrund reingehauen der zu groß
/// ist. Ich will teilweise schon Transparenz haben, mach nur da einen Hintergrund rein in den
/// rechteckigen Elementen wo man etwas lesen muss. b) Der Hintergrund sollte der selbe sein wie im
/// Menü auch, das ist nicht so langweilig einfarbig. c) Ich mag die neuen Buttons nicht. Es sollten
/// schon richtige rechteckige Buttons sein."</i></para>
///
/// <para><b>THE CAUSE, AND IT WAS SITTING IN THE PREVIOUS ROUND'S OWN INSTRUMENT.</b> The shipped
/// probe read <c>row plate = Image 'Background' sprite 'Panel_Divider' rgba=0.05,0.05,0.05,1</c>.
/// A colour alpha of 1 was taken to mean the row already had an opaque plate, so the illegibility
/// had to be coming from somewhere else — and the remedy became a full-pane slab behind
/// everything. That inference is wrong, and the correction is the whole of this round: <b>a colour
/// alpha does not make a graphic opaque, the SPRITE'S TEXELS do.</b> <c>Panel_Divider</c> is a
/// 512x10 sprite — the mod's own main-menu census has been printing that size all along
/// (<c>art='Panel_Divider' 512x10</c>, on nodes literally named <c>Title</c>) — which is a RULE
/// under a heading, not a panel. Tinting a 10-texel divider near-black at full alpha paints a line,
/// not a surface. The rows were never "a translucent plate"; they were an opaque tint over a sprite
/// that draws almost nothing, and the pane slab was a second surface compensating for it.</para>
///
/// <para><b>SO THE REMEDY IS PER-ELEMENT AND WEARS THE GAME'S OWN ART.</b> Three parts, one per
/// item of the report:</para>
/// <list type="number">
///   <item><description>The full-pane ground is GONE — <see cref="RemoveLegacyGround"/> takes any
///   surviving plate off the pane. Where nothing is read, the window is see-through again, which is
///   the transparency the user asked to keep (item a).</description></item>
///   <item><description><see cref="SkinRowPlate"/> backs each ROW — the rectangular element that
///   carries the text — and nothing else. The plate is the row's own rect, inset, so consecutive
///   rows read as separate rectangles with the pane visible between and around them (item a).
///   </description></item>
///   <item><description>It is painted with a sprite HARVESTED FROM THE GAME
///   (<see cref="HarvestMenuSkin"/>), tinted with the colour the game's own row Background is
///   authored at. The game's panel art carries grain, so the backing is the menu's surface rather
///   than a flat fill (item b), and the tint is the game's own value rather than a third guess at
///   an RGBA.</description></item>
/// </list>
///
/// <para><b>AND THE BUTTONS (item c) WERE A GEOMETRY BUG, NOT A TASTE ONE.</b> The previous round
/// seated the action face by COPYING the authored control rect out of the row's option column — and
/// on the toggle template that first child is the toggle SWITCH, a small square. So a "button"
/// inherited a checkbox's footprint and then wore a harvested dropdown arrow, which is exactly the
/// thing being rejected. <c>BuildActionPlate</c> now fills the option column — the same wide seat
/// every slider and dropdown in this window occupies — wears the game's own button face
/// (<see cref="_buttonSkin"/>, harvested off the window's own <c>UIMainMenuOption</c> tabs) with
/// that button's own hover and press colours, and carries a caption instead of a glyph.</para>
///
/// <para><b>WHY THE BUTTON IS NO LONGER DERIVED FROM THE ROW.</b> <see cref="ActionPlateRest"/>
/// used to sample the row Background's colour. That was defensible while the row was transparent;
/// it is wrong now that <see cref="SkinRowPlate"/> paints the row with that very colour, because a
/// button and its backing would come out the same near-black and the button would have no visible
/// edge. It reads the game's BUTTON ColorBlock instead — a different graphic, answering a different
/// question.</para>
///
/// <para><b>THE MEASUREMENT SHIPS WITH IT.</b> <see cref="ProbeMenuSkin"/> keeps every field it
/// already printed and APPENDS the ones that decide the sprite question: each sprite's rect, its
/// 9-slice border, its texture and — where the texture is readable — the fraction of its texels
/// that are actually opaque. A divider and a panel are indistinguishable by name and obvious by
/// those numbers.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>
    /// Name of the full-pane ground plate ModBuild 437 added. IT IS NO LONGER BUILT — the constant
    /// survives because the plate still has to be FOUND to be removed (see
    /// <see cref="RemoveLegacyGround"/>), and because a name is the only handle a sweep over a
    /// holder it did not build can have.
    /// </summary>
    private const string GroundName = "GloomhavenVR.MenuGround";

    /// <summary>Name of the per-row backing plate — also how it is found again on a re-skin.</summary>
    private const string RowPlateName = "GloomhavenVR.RowPlate";

    /// <summary>One-shot guard for the ground-removal line: one pane, not one rebuild.</summary>
    private static bool _groundRemovalLogged;

    /// <summary>One-shot guard for the row-plate line: one row shape, not one row.</summary>
    private static bool _rowPlateLogged;

    /// <summary>
    /// The game's own resting visual for a settings row's plate, sampled off the toggle donor's
    /// <c>Background</c> child — the graphic every cloned row in this menu already wears.
    ///
    /// <para>Only its COLOUR is used now, and only as the tint for <see cref="SkinRowPlate"/>. Its
    /// sprite is <c>Panel_Divider</c>, a 512x10 rule, which is the defect this round is correcting:
    /// the colour is the game's judgement about how dark a row should be and is worth keeping; the
    /// art is a line and cannot back anything.</para>
    /// </summary>
    private static (Sprite? sprite, Image.Type type, Color colour)? _rowPlateStyle;

    /// <summary>One-shot guard for the derived-plate line: one row shape, not one row.</summary>
    private static bool _actionPlateLogged;

    /// <summary>
    /// A surface harvested off the game: the sprite, the draw type it is used at, and where it was
    /// found. The draw TYPE travels with the sprite because a 9-sliced sprite drawn as Simple
    /// stretches its corners and a border-less sprite drawn as Sliced is a per-frame Unity warning
    /// — the donor already knows which it is, and guessing is how that warning ships.
    /// </summary>
    private readonly struct HarvestedSkin
    {
        internal readonly Sprite Sprite;
        internal readonly Image.Type DrawType;
        internal readonly float PixelsPerUnitMultiplier;
        internal readonly string Source;

        internal HarvestedSkin(Image donor, string source)
        {
            Sprite = donor.sprite;
            DrawType = donor.type;
            PixelsPerUnitMultiplier = donor.pixelsPerUnitMultiplier;
            Source = source;
        }

        /// <summary>Dress a target Image in this surface. The TINT is the caller's business.</summary>
        internal void ApplyTo(Image target)
        {
            target.sprite = Sprite;
            target.type = DrawType;
            target.pixelsPerUnitMultiplier = PixelsPerUnitMultiplier;
        }
    }

    /// <summary>The game's own readable-panel art, or null when nothing in reach qualifies.</summary>
    private static HarvestedSkin? _panelSkin;

    /// <summary>The game's own button face, harvested off this window's <c>UIMainMenuOption</c> tabs.</summary>
    private static HarvestedSkin? _buttonSkin;

    /// <summary>The game's own button ColorBlock, harvested beside <see cref="_buttonSkin"/>.</summary>
    private static ColorBlock? _buttonColors;

    /// <summary>What <see cref="HarvestMenuSkin"/> decided and why — printed once by the probe.</summary>
    private static string _skinVerdict = "not harvested";

    /// <summary>
    /// The smallest side, in TEXELS, a sprite may have and still be treated as a surface rather
    /// than a rule.
    ///
    /// <para>This is the whole divider test, and it is deliberately a measurement of the ART and
    /// not of the name. <c>Panel_Divider</c> is 512x10 and <c>Separator</c> is 299x26; a panel
    /// authored to be stretched in both directions is square-ish or 9-sliced. Anything thinner than
    /// this in either direction cannot cover a row however it is tinted, which is precisely how the
    /// previous round's reading went wrong.</para>
    /// </summary>
    private const float PanelMinTexels = 32f;

    /// <summary>
    /// The smallest side, in the donor's own rect units, a DRAWN graphic may have and still count
    /// as evidence that its sprite is a panel. A big sprite drawn into a 12-unit icon slot says
    /// nothing about how it behaves as a surface.
    /// </summary>
    private const float PanelMinDrawn = 24f;

    /// <summary>
    /// How far from square a NON-9-sliced sprite may be and still be read as a tileable surface.
    /// Pictures fail this by construction: a controller diagram or a backdrop is wide, a portrait
    /// is tall. A 9-slice is exempt — stretching to any aspect is what it is authored for.
    /// </summary>
    private const float PanelMaxAspect = 1.25f;

    /// <summary>
    /// The drawn area, in the donor's own rect units, a NON-9-sliced sprite must be used at before
    /// its squareness counts as evidence. It separates a surface from an icon that happens to be
    /// square: the game draws <c>PaperTexture</c> behind its own headings at roughly 1280x56, which
    /// is an order of magnitude past this; a 40x40 icon is an order of magnitude below it.
    /// </summary>
    private const float PanelMinDrawnArea = 6000f;

    /// <summary>
    /// How far a row's backing plate is inset from the top and bottom of the row, as a fraction of
    /// the row's height.
    ///
    /// <para>WHY THERE IS AN INSET AT ALL, since the plate is otherwise the row's own rect. The
    /// thing that used to separate one row from the next was the row Background's own art — the
    /// <c>Panel_Divider</c> rule. That art is still there and still drawn (this plate is seated
    /// UNDER it, and nothing about the Background is written), but a full-bleed backing behind
    /// every row in a list is a continuous slab again, which is the "zu groß" being corrected. The
    /// gap is what makes the rows read as the "rechteckige Elemente" the report asks for. It is a
    /// fraction of the row rather than a pixel count for the same reason the action plate's inset
    /// is: a menu whose row height changes in a game update takes this with it.</para>
    /// </summary>
    private const float RowPlateInset = 0.06f;

    /// <summary>
    /// What an action plate is painted when the game gave up no button ColorBlock: the warm bronze
    /// the 2026-09-05 round settled on, at FULL alpha. Only the transparency was ever reported;
    /// the hue was not, so the hue is kept.
    /// </summary>
    private static readonly Color ActionPlateFallback = new(0.30f, 0.25f, 0.15f, 1f);

    /// <summary>Hover tint for an action plate — the menu's gold, unchanged in hue and now opaque.
    /// Hover and press feedback are what make a row feel like a button, so only the resting state
    /// was ever at fault.</summary>
    private static readonly Color ActionPlateHover = new(0.50f, 0.40f, 0.18f, 1f);

    /// <summary>Press tint for an action plate — the same gold, brighter, opaque.</summary>
    private static readonly Color ActionPlatePress = new(0.68f, 0.55f, 0.24f, 1f);

    /// <summary>
    /// HOW LONG A HOVER TINT TAKES TO CROSS-FADE, in seconds — the one number every hand-built
    /// <c>Button.colors</c> in this mod's own chrome sets, and the one every one of them had as a
    /// bare 0.08f literal (2026-09 redundancy audit, R20). Three sites: the action-row plate
    /// (VROptionsTab.2.Rows.cs), the variant picture tile (VariantTiles.cs) and the modal close X
    /// (WorldUI/Grab/ModalCloseButton.cs, not converted this round — it is another lane's file).
    /// Named rather than merged: the three PALETTES are legitimately different (the X multiplies a
    /// flat grey over a plate, the other two brighten toward the menu's gold), but the timing is
    /// one perceptual decision — "a subtle affordance, no loud colour flash", quoted from the X's
    /// own doc — and three copies of it could only ever drift into three different feels.
    /// Unity's own default is 0.1 s; this is deliberately a shade faster.
    /// </summary>
    internal const float HoverTintFadeSeconds = 0.08f;

    /// <summary>Sample the game's own row plate once, off the captured toggle template.</summary>
    private static void SampleRowPlate(GameObject? toggleTemplate)
    {
        _rowPlateStyle = null;
        _actionPlateLogged = false;
        _rowPlateLogged = false;
        if (toggleTemplate == null)
            return;

        Transform? background = toggleTemplate.transform.Find("Background");
        Image? image = background != null ? background.GetComponent<Image>() : null;
        if (image == null)
            return;

        _rowPlateStyle = (image.sprite, image.type, image.color);
    }

    /// <summary>
    /// The tint a row's backing plate is painted with: the colour the GAME authors its own settings
    /// row at, forced opaque.
    ///
    /// <para>Full alpha always, for the reason <see cref="ApplyHeaderCaption"/> records about the
    /// headers — an authored or inherited alpha below 1 is what hid those once already. The last
    /// resort is the repo's panel neutral: dark, already shipped and already judged behind every
    /// floated menu in mixed reality, so a template-less menu still gets something a caption reads
    /// against rather than nothing.</para>
    /// </summary>
    private static Color RowPlateTint()
    {
        if (_rowPlateStyle is { } style)
            return new Color(style.colour.r, style.colour.g, style.colour.b, 1f);
        return MrBacking.PanelNeutral;
    }

    /// <summary>
    /// The resting colour of an action row's button: the game's own button ColorBlock, forced
    /// opaque, or the previous round's hue when the game gave up no ColorBlock.
    ///
    /// <para>NOT DERIVED FROM THE ROW ANY MORE, and that is the correction. It used to sample the
    /// row Background's colour, which was a different graphic while the row was transparent. Now
    /// that <see cref="SkinRowPlate"/> paints the row with that same colour, deriving the button
    /// from it would put a near-black button on a near-black backing with no visible edge — a
    /// button that is not "richtig rechteckig" because it has no boundary at all.</para>
    /// </summary>
    private static Color ActionPlateRest()
    {
        if (_buttonColors is { } block)
            return new Color(block.normalColor.r, block.normalColor.g, block.normalColor.b, 1f);
        return ActionPlateFallback;
    }

    /// <summary>The hovered colour for an action button — the game's own, or the menu's gold.</summary>
    private static Color ActionPlateHot()
    {
        if (_buttonColors is { } block)
            return new Color(block.highlightedColor.r, block.highlightedColor.g,
                             block.highlightedColor.b, 1f);
        return ActionPlateHover;
    }

    /// <summary>The pressed colour for an action button — the game's own, or the menu's gold.</summary>
    private static Color ActionPlateDown()
    {
        if (_buttonColors is { } block)
            return new Color(block.pressedColor.r, block.pressedColor.g, block.pressedColor.b, 1f);
        return ActionPlatePress;
    }

    /// <summary>
    /// Dress an action button in the game's own button art.
    ///
    /// <para>THE IMAGE ITSELF STAYS WHITE — the <c>Button</c>'s ColorBlock multiplies the graphic's
    /// colour, so the resting colour goes in the ColorBlock and putting it here too would square
    /// it.</para>
    ///
    /// <para>The button face is preferred over the panel art and the panel art over nothing, so a
    /// game update that moves the tab skin costs the button its border and never its visibility: a
    /// sprite-less Image is a plain tinted rectangle, which is still a bounded, rectangular,
    /// hoverable button.</para>
    /// </summary>
    private static void PaintActionPlate(Image plate)
    {
        HarvestedSkin? skin = _buttonSkin ?? _panelSkin;
        skin?.ApplyTo(plate);

        plate.color = Color.white;
        plate.raycastTarget = true;

        if (_actionPlateLogged)
            return;
        _actionPlateLogged = true;
        Color rest = ActionPlateRest();
        Color hot = ActionPlateHot();
        Color down = ActionPlateDown();
        // HW-VERIFY
        VRLog.Note("WorldUI",
            "VR OPTIONS ACTION BUTTON: the action rows now carry a RECTANGULAR button filling the "
            + "row's option column — the same seat every slider and dropdown in this window sits in "
            + "— wearing "
            + (skin is { } worn
                   ? $"the game's own art (sprite '{worn.Sprite.name}', drawn {worn.DrawType}, from "
                     + $"{worn.Source})"
                   : "no sprite (a plain tinted rectangle — nothing in reach gave up a button face)")
            + $" and captioned. Resting RGBA {rest.r:0.##},{rest.g:0.##},{rest.b:0.##},1 "
            + (_buttonColors != null
                   ? "from the game's own button ColorBlock"
                   : "from the fallback hue — no game ColorBlock was in reach")
            + $"; hover {hot.r:0.##},{hot.g:0.##},{hot.b:0.##},1, "
            + $"press {down.r:0.##},{down.g:0.##},{down.b:0.##},1. "
            + "ModBuild 437 seated this face by COPYING the option column's first child, which on "
            + "the toggle template is the toggle SWITCH — so the button inherited a checkbox's "
            + "square footprint and wore a harvested dropdown arrow. That is the geometry bug "
            + "behind 'Ich mag die neuen Buttons nicht'.");
    }

    /// <summary>
    /// Dress an already-built, already-tinted mod graphic in the game's harvested panel art.
    ///
    /// <para>For surfaces that were ALREADY opaque and already the right size — the variant tiles'
    /// plates — where the report's objection is only b), "das ist nicht so langweilig einfarbig".
    /// The caller keeps its own colour; this writes the sprite and nothing else, so a harvest that
    /// found nothing leaves the graphic exactly as it shipped.</para>
    /// </summary>
    private static void SkinAsPanel(Image target) => _panelSkin?.ApplyTo(target);

    /// <summary>
    /// Back ONE row — the rectangular element that carries the text — and nothing around it.
    ///
    /// <para>THIS IS THE WHOLE OF ITEMS a) AND b). A mod-owned plate is seated as the row's FIRST
    /// child, so it draws under the row's authored <c>Background</c> (its divider rule survives and
    /// still separates the rows) and under every caption and control. It is inset top and bottom by
    /// <see cref="RowPlateInset"/> so the pane shows between rows and the list reads as a stack of
    /// rectangles rather than one slab, and it takes NO raycasts — the row's own hit behaviour is
    /// not part of the report and <c>EnsureRowHitArea</c> already owns it.</para>
    ///
    /// <para>It is painted with the game's harvested panel art at the game's own row colour. A
    /// harvest that found nothing leaves the plate sprite-less — a flat tinted rectangle, which is
    /// the least-bad option and is still only as large as the element it backs.</para>
    ///
    /// <para>NEVER-EMPTY GUARD: this runs inside the row builder, where a throw costs the row and
    /// then the list. Every failure is a no-op, and the caller catches anyway.</para>
    /// </summary>
    private static void SkinRowPlate(GameObject row)
    {
        if (row.transform is not RectTransform rowRect)
            return;

        Transform? existing = row.transform.Find(RowPlateName);
        Image? plate = existing != null ? existing.GetComponent<Image>() : null;
        if (plate == null)
        {
            var go = new GameObject(RowPlateName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(rowRect, worldPositionStays: false);
            rect.anchorMin = new Vector2(0f, RowPlateInset);
            rect.anchorMax = new Vector2(1f, 1f - RowPlateInset);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            plate = go.AddComponent<Image>();
            plate.raycastTarget = false;
        }

        plate.transform.SetAsFirstSibling();
        _panelSkin?.ApplyTo(plate);
        Color tint = RowPlateTint();
        plate.color = tint;
        if (!plate.gameObject.activeSelf)
            plate.gameObject.SetActive(true);

        if (_rowPlateLogged)
            return;
        _rowPlateLogged = true;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"VR OPTIONS ROW PLATE: each row now carries its own backing '{RowPlateName}', inset "
            + $"{RowPlateInset:P0} top and bottom of the row so the pane stays visible between rows, "
            + "painted "
            + (_panelSkin is { } skin
                   ? $"with the game's own panel art (sprite '{skin.Sprite.name}', drawn "
                     + $"{skin.DrawType}, from {skin.Source})"
                   : "WITHOUT a sprite — nothing in reach passed the panel test, so this is a flat "
                     + "tinted rectangle and the next round should widen the harvest")
            + $" at RGBA {tint.r:0.##},{tint.g:0.##},{tint.b:0.##},1 "
            + (_rowPlateStyle != null
                   ? "taken from the game's own row Background"
                   : "from the repo's panel neutral — no donor row was captured")
            + ". The full-pane ground of ModBuild 437 is gone with it: only the rectangular elements "
            + "that carry text are backed, everything else in the pane is see-through again.");
    }

    /// <summary>
    /// Take ModBuild 437's full-pane ground back off the pane.
    ///
    /// <para>The plate is no longer built, so on a fresh session there is nothing to find and this
    /// costs one <c>Find</c> per show. It runs anyway because the pane is re-parented and rebuilt by
    /// machinery this file does not own and because a plate left behind by a hot-reloaded assembly
    /// is exactly the "einfarbiger Hintergrund der zu groß ist" being removed — a removal that only
    /// happens if one particular call ran is a removal that sometimes did not.</para>
    ///
    /// <para>NEVER-EMPTY GUARD: every failure is a no-op, and the worst case is the window looking
    /// exactly as ModBuild 437 shipped it.</para>
    /// </summary>
    private static void RemoveLegacyGround()
    {
        if (_window == null)
            return;

        Transform? ground = _window.transform.Find(GroundName);
        if (ground == null)
            return;

        UnityEngine.Object.DestroyImmediate(ground.gameObject);
        if (_groundRemovalLogged)
            return;
        _groundRemovalLogged = true;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"VR OPTIONS GROUND REMOVED: the full-pane plate '{GroundName}' ModBuild 437 put behind "
            + "the whole detached pane is off. It was the 'einfarbiger Hintergrund der zu groß ist' "
            + "in the report, and it existed because the previous round read the row Background's "
            + "colour alpha of 1 as proof the rows were already opaque. They were not: that alpha "
            + "tints 'Panel_Divider', a 512x10 rule. The backing is per-row now — see VR OPTIONS ROW "
            + "PLATE.");
    }

    /// <summary>
    /// Find the game's own readable-panel art and its own button face, ONCE, by measuring sprites
    /// rather than by reading their names.
    ///
    /// <para>THE NAME IS NOT EVIDENCE, which is this round's whole lesson. <c>Panel_Divider</c>
    /// sounds like a panel and is a 512x10 rule; the widest art the previous probe found on this
    /// window is called <c>xbox11</c>, which says nothing at all. So a candidate has to pass on
    /// geometry: at least <see cref="PanelMinTexels"/> texels on its SHORT side (a rule fails this
    /// by construction), and drawn by its donor into a rect at least <see cref="PanelMinDrawn"/>
    /// units on both sides (a big sprite squeezed into an icon slot is not evidence about
    /// surfaces). Among the survivors a 9-SLICED sprite wins — a non-zero border is an author
    /// saying "this is meant to be stretched as a panel" — and then the largest short side.</para>
    ///
    /// <para>THE SEARCH WIDENS RATHER THAN GUESSING. The options window's own subtree first,
    /// because a surface from this window is the one the user means by "wie im Menü"; then its
    /// canvas; and only then every loaded Image, once, at template-capture cadence — the same
    /// cadence the main-menu census already runs at, and never per frame.</para>
    ///
    /// <para>The BUTTON face is harvested separately and only from the window's own
    /// <c>UIMainMenuOption</c> tabs, together with their <c>ColorBlock</c>: a button's resting,
    /// hover and press colours are a set, and taking the art from one widget and the states from
    /// another is how a hover ends up brighter than a press.</para>
    /// </summary>
    private static void HarvestMenuSkin(UIOptionsWindow host)
    {
        _panelSkin = null;
        _buttonSkin = null;
        _buttonColors = null;
        _skinVerdict = "nothing in reach passed the panel test";

        try
        {
            (HarvestedSkin? found, string where) =
                BestPanelIn(host.GetComponentsInChildren<Image>(true), "the options window");
            if (found == null)
            {
                Canvas? canvas = host.GetComponentInParent<Canvas>();
                if (canvas != null)
                    (found, where) = BestPanelIn(canvas.GetComponentsInChildren<Image>(true),
                                                 $"the canvas '{canvas.name}'");
            }
            if (found == null)
                (found, where) = BestPanelIn(Resources.FindObjectsOfTypeAll<Image>(),
                                             "every loaded Image");

            _panelSkin = found;
            _skinVerdict = found is { } skin
                ? $"panel art '{skin.Sprite.name}' {DescribeSprite(skin.Sprite)} drawn {skin.DrawType}, "
                  + $"found in {where} on {skin.Source}"
                : $"NO panel art found in {where} — every candidate was thinner than "
                  + $"{PanelMinTexels:0} texels on its short side or drawn smaller than "
                  + $"{PanelMinDrawn:0} units";
        }
        catch (System.Exception e)
        {
            _skinVerdict = $"the panel harvest threw ({e.Message})";
        }

        try
        {
            HarvestButtonSkin();
        }
        catch (System.Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: the button-face harvest threw ({e.Message}) — the "
                                  + "action rows fall back to a plain tinted rectangle, which is still "
                                  + "a bounded, hoverable button.");
        }
    }

    /// <summary>
    /// The best panel candidate in a set of Images, with the node it was found on.
    ///
    /// <para><b>THE ADMISSION TEST IS DELIBERATELY NARROW, because the failure mode of a wide one
    /// is grotesque.</b> This menu's reach contains controller diagrams, character portraits and
    /// full-screen backdrops, and every one of them is a big sprite drawn into a big rect. Stretched
    /// behind a settings row and tinted near-black, a squashed Xbox controller would satisfy every
    /// loose "is it big enough" rule and would be far worse than the flat plate it replaced. So a
    /// candidate gets in by ONE of two arguments, each of which is an author saying something about
    /// how the art is meant to be used:</para>
    /// <list type="bullet">
    ///   <item><description><b>It is 9-sliced.</b> A non-zero border means "stretch me as a panel"
    ///   and nothing else does. Any aspect is fine, because that is what a 9-slice is for.
    ///   </description></item>
    ///   <item><description><b>It is square and drawn large.</b> A square texture drawn as a big
    ///   backing is a tileable SURFACE — <c>PaperTexture</c> is 512x512 and the game draws it behind
    ///   its own headings. Pictures are not square: diagrams and backdrops are wide, portraits are
    ///   tall. The area floor is what separates a surface from an icon that happens to be square.
    ///   </description></item>
    /// </list>
    /// <para>Nothing qualifying is a legitimate answer and is reported as one: the plates then have
    /// no sprite, which is a flat tinted rectangle — still opaque, still only as large as the
    /// element it backs, and still readable.</para>
    /// </summary>
    private static (HarvestedSkin? skin, string where) BestPanelIn(Image[] candidates, string where)
    {
        Image? best = null;
        float bestScore = 0f;

        for (int i = 0; i < candidates.Length; i++)
        {
            Image image = candidates[i];
            if (image == null || image.sprite == null)
                continue;
            // A Mask's graphic is a stencil, not a surface: it is never drawn as itself.
            if (image.GetComponent<Mask>() != null)
                continue;
            if (image.transform is not RectTransform rect)
                continue;
            float drawnW = Mathf.Abs(rect.rect.width);
            float drawnH = Mathf.Abs(rect.rect.height);
            if (drawnW < PanelMinDrawn || drawnH < PanelMinDrawn)
                continue;

            Sprite sprite = image.sprite;
            float shortSide = Mathf.Min(sprite.rect.width, sprite.rect.height);
            float longSide = Mathf.Max(sprite.rect.width, sprite.rect.height);
            if (shortSide < PanelMinTexels)
                continue;

            Vector4 border = sprite.border;
            bool sliced = border.x > 0f || border.y > 0f || border.z > 0f || border.w > 0f;
            bool tileable = longSide <= shortSide * PanelMaxAspect
                            && drawnW * drawnH >= PanelMinDrawnArea;
            if (!sliced && !tileable)
                continue;

            float score = (sliced ? 10000f : 0f) + shortSide;
            if (score <= bestScore)
                continue;
            best = image;
            bestScore = score;
        }

        return best == null
            ? (null, where)
            : (new HarvestedSkin(best, PathOfNode(best.transform)), where);
    }

    /// <summary>
    /// The game's own button face and colour states, off the window's <c>UIMainMenuOption</c> tab —
    /// the one widget in this window that IS a button the user already presses.
    /// </summary>
    private static void HarvestButtonSkin()
    {
        if (_categoryTemplate == null)
            return;

        var selectable = _categoryTemplate.GetComponentInChildren<Selectable>(true);
        if (selectable != null && selectable.transition == Selectable.Transition.ColorTint)
            _buttonColors = selectable.colors;

        // THE SELECTABLE'S OWN TARGET GRAPHIC IS THE BUTTON'S FACE BY DEFINITION — it is the graphic
        // the ColorBlock above tints, so art and states come from the same widget. Taken first for
        // exactly that reason: the widest-Image search below is a fallback, and on a tab it can
        // land on a focus mask or a highlight overlay, which would ship a button that looks
        // permanently hovered.
        if (selectable != null && selectable.targetGraphic is Image face && face.sprite != null)
        {
            _buttonSkin = new HarvestedSkin(face, PathOfNode(face.transform) + " (targetGraphic)");
            return;
        }

        Image? widest = null;
        float widestArea = 0f;
        foreach (Image image in _categoryTemplate.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null || image.GetComponent<Mask>() != null)
                continue;
            if (image.transform is not RectTransform rect)
                continue;
            float area = Mathf.Abs(rect.rect.width * rect.rect.height);
            if (area <= widestArea)
                continue;
            widest = image;
            widestArea = area;
        }

        if (widest != null)
            _buttonSkin = new HarvestedSkin(widest, PathOfNode(widest.transform));
    }

    /// <summary>A short node path, for a log line that has to say WHERE a sprite came from.</summary>
    private static string PathOfNode(Transform node)
    {
        Transform? parent = node.parent;
        return parent != null ? $"'{parent.name}/{node.name}'" : $"'{node.name}'";
    }

    /// <summary>
    /// MEASURE THE THREE GRAPHICS THIS FILE REASONS ABOUT, once per session, with their art and
    /// their colours — which is what neither the existing probe nor any shipped log has ever said.
    ///
    /// <para>The row archetype probe in <c>VROptionsTab.1.Inject.cs</c> dumps names and component
    /// TYPES, so "Background [Image]" is everything the record holds about the surface every mod
    /// row is painted on. Two rounds of this menu's plate were then picked by eye against a ground
    /// nobody had measured. These three readings are the data that decides the next one: if the
    /// options window's own panel art turns out to be a plain 9-slice at full alpha, the flat
    /// neutral the ground plate painted can be replaced by the game's own panel and the menu stops
    /// merely READING like the others and starts being one.</para>
    ///
    /// <para>APPENDED 2026-09-05, and the appended half is the half that matters. The original
    /// three readings printed a colour and a sprite NAME, and a name is what the last round
    /// reasoned from: <c>rgba=...,1</c> on a sprite called <c>Panel_Divider</c> was read as "the
    /// row already has an opaque plate". The sprite's own rect, its 9-slice border, its texture and
    /// its opaque texel fraction are what actually decide whether a graphic covers anything, so
    /// they are printed now — for the row plate, for the window's panel art, and for whatever the
    /// harvest picked. Nothing already printed was reworded.</para>
    /// </summary>
    private static void ProbeMenuSkin(UIOptionsWindow host)
    {
        try
        {
            var sb = new StringBuilder(640);
            sb.Append("VR OPTIONS SKIN: ");

            Transform? background = _toggleTemplate != null
                ? _toggleTemplate.transform.Find("Background")
                : null;
            Image? rowPlate = background != null ? background.GetComponent<Image>() : null;
            sb.Append("row plate = ")
              .Append(DescribeGraphic(background != null ? background.GetComponent<Graphic>() : null));

            sb.Append("; pane ground = ")
              .Append(DescribeGraphic(_window != null ? _window.GetComponent<Graphic>() : null));

            sb.Append("; widest panel art on '").Append(host.name).Append("' outside its tab panes = ")
              .Append(DescribePanelArt(host));

            sb.Append(". The pane ground is what the mod's rows are composited over once the pane is "
                      + "detached; the panel art is what every OTHER floated menu keeps and this one "
                      + "never had.");

            // ---- APPENDED 2026-09-05: the fields that decide whether a sprite is a surface ----
            sb.Append(" ROW PLATE ART: ")
              .Append(rowPlate != null && rowPlate.sprite != null
                          ? $"'{rowPlate.sprite.name}' {DescribeSprite(rowPlate.sprite)}, drawn "
                            + $"{rowPlate.type} into a {DescribeRect(rowPlate.transform)} rect"
                          : "the donor row has no sprite to measure");
            sb.Append(". WINDOW PANEL ART: ").Append(DescribePanelArtSprite(host));
            sb.Append(". HARVEST: ").Append(_skinVerdict);
            sb.Append("; button face = ")
              .Append(_buttonSkin is { } face
                          ? $"'{face.Sprite.name}' {DescribeSprite(face.Sprite)} drawn {face.DrawType} "
                            + $"from {face.Source}"
                          : "none")
              .Append(", button ColorBlock = ")
              .Append(_buttonColors is { } block
                          ? $"normal {block.normalColor.r:0.##},{block.normalColor.g:0.##},"
                            + $"{block.normalColor.b:0.##},{block.normalColor.a:0.##}"
                          : "none");
            sb.Append(". A graphic's COLOUR alpha says nothing about whether it covers its rect — "
                      + "its sprite's texels do, which is why the rect, the 9-slice border and the "
                      + "opaque texel fraction are printed here. A short side under "
                      + $"{PanelMinTexels:0} texels is a rule, not a panel.");
            // HW-VERIFY
            VRLog.Note("WorldUI", sb.ToString());
        }
        catch (System.Exception e)
        {
            VRLog.Warn("WorldUI", $"VR options tab: the skin probe threw ({e.Message}) — the menu is "
                                  + "unaffected, only this measurement is missing.");
        }
    }

    /// <summary>One graphic, with the two facts the shipped probe never recorded: its art and its RGBA.</summary>
    private static string DescribeGraphic(Graphic? graphic)
    {
        if (graphic == null)
            return "none";

        string art = graphic switch
        {
            Image image => image.sprite != null ? $"sprite '{image.sprite.name}'" : "no sprite",
            RawImage raw => raw.texture != null ? $"texture '{raw.texture.name}'" : "no texture",
            _ => "not an image",
        };
        Color c = graphic.color;
        return $"{graphic.GetType().Name} '{graphic.name}' {art} "
               + $"rgba={c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##} enabled={graphic.enabled}";
    }

    /// <summary>
    /// One sprite, measured: the four numbers that separate a panel from a rule, and the one that
    /// settles it outright when the atlas happens to be readable.
    /// </summary>
    private static string DescribeSprite(Sprite sprite)
    {
        Rect r = sprite.rect;
        Vector4 b = sprite.border;
        Texture2D? tex = sprite.texture;
        return $"rect {r.width:0}x{r.height:0}, border {b.x:0}/{b.y:0}/{b.z:0}/{b.w:0} "
               + $"({(b == Vector4.zero ? "NOT 9-sliced — a fill or a rule" : "9-sliced — a panel")}), "
               + $"texture '{(tex != null ? tex.name : "none")}' "
               + $"{(tex != null ? $"{tex.width}x{tex.height}" : "?")}, packed={sprite.packed}, "
               + $"{DescribeCoverage(sprite)}";
    }

    /// <summary>
    /// The fraction of a sprite's texels that are actually opaque — the reading that decides the
    /// whole question, when the atlas allows it.
    ///
    /// <para>Sampled on a grid capped at 64x64 taps, so a 4k atlas region costs what a 64px icon
    /// costs, and only ever from the probe (once per session). An unreadable texture says so rather
    /// than being guessed at: the rect and the border above are then the evidence.</para>
    /// </summary>
    private static string DescribeCoverage(Sprite sprite)
    {
        try
        {
            Texture2D? tex = sprite.texture;
            if (tex == null)
                return "coverage: no texture";
            if (!tex.isReadable)
                return "coverage: texture NOT readable (judge by rect and border above)";

            Rect r = sprite.textureRect;
            int x = Mathf.Max(0, Mathf.FloorToInt(r.x));
            int y = Mathf.Max(0, Mathf.FloorToInt(r.y));
            int w = Mathf.Min(tex.width - x, Mathf.CeilToInt(r.width));
            int h = Mathf.Min(tex.height - y, Mathf.CeilToInt(r.height));
            if (w <= 0 || h <= 0)
                return "coverage: empty rect";

            int stepX = Mathf.Max(1, w / 64);
            int stepY = Mathf.Max(1, h / 64);
            int taps = 0, opaque = 0;
            for (int py = y; py < y + h; py += stepY)
            {
                for (int px = x; px < x + w; px += stepX)
                {
                    taps++;
                    if (tex.GetPixel(px, py).a >= 0.5f)
                        opaque++;
                }
            }
            return taps == 0
                ? "coverage: no taps"
                : $"coverage: {(float)opaque / taps:P0} of {taps} taps opaque";
        }
        catch (System.Exception e)
        {
            return $"coverage: unreadable ({e.Message})";
        }
    }

    /// <summary>A rect's drawn size, for a line that has to say how big a graphic actually is.</summary>
    private static string DescribeRect(Transform node)
        => node is RectTransform rect ? $"{rect.rect.width:0}x{rect.rect.height:0}" : "sizeless";

    /// <summary>
    /// The options window's own panel art: the widest sprite-bearing <see cref="Image"/> that is
    /// NOT inside one of its tab panes.
    ///
    /// <para>Tab panes are excluded because a pane is the very thing the mod clones — a pane's own
    /// ground is not the window's panel, and counting it would make the measurement agree with this
    /// file's hypothesis by construction.</para>
    /// </summary>
    private static string DescribePanelArt(UIOptionsWindow host)
    {
        Image? widest = WidestPanelArt(host, out float widestArea);
        if (widest == null)
            return "none";

        float hostArea = host.transform is RectTransform hostRect
            ? Mathf.Abs(hostRect.rect.width * hostRect.rect.height)
            : 0f;
        float cover = hostArea > 1f ? widestArea / hostArea : 0f;
        return DescribeGraphic(widest) + $" covering {cover:P0} of the window rect";
    }

    /// <summary>The same graphic as <see cref="DescribePanelArt"/>, measured as a SPRITE.</summary>
    private static string DescribePanelArtSprite(UIOptionsWindow host)
    {
        Image? widest = WidestPanelArt(host, out _);
        return widest != null && widest.sprite != null
            ? $"'{widest.sprite.name}' {DescribeSprite(widest.sprite)}, drawn {widest.type} into a "
              + $"{DescribeRect(widest.transform)} rect"
            : "none";
    }

    /// <summary>The widest sprite-bearing Image on the host outside its tab panes, and its area.</summary>
    private static Image? WidestPanelArt(UIOptionsWindow host, out float widestArea)
    {
        Image? widest = null;
        widestArea = 0f;
        if (host.transform is not RectTransform)
            return null;

        foreach (Image image in host.GetComponentsInChildren<Image>(true))
        {
            if (image == null || image.sprite == null || image.GetComponent<Mask>() != null)
                continue;
            if (image.GetComponentInParent<UISubmenuGOWindow>() != null)
                continue;
            if (image.transform is not RectTransform rect)
                continue;

            float area = Mathf.Abs(rect.rect.width * rect.rect.height);
            if (area <= widestArea)
                continue;
            widest = image;
            widestArea = area;
        }

        return widest;
    }
}

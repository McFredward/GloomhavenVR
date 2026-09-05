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
/// <para><b>THE DEFECT</b> (user, 2026-09-05, verbatim): <i>"Die Transparenz beim VR-Optionsmenu
/// ist manchmal ein Problem, die Dinge sind dann nicht zu erkennen — ich will das das VR
/// Optionsmenu sich daher mehr angleicht an die anderen Menus vom Aussehen für eine bessere
/// Lesbarkeit und Bedienbarkeit."</i></para>
///
/// <para><b>WHY IT IS "MANCHMAL", AND WHY THAT WORD IS THE WHOLE DIAGNOSIS.</b> A translucent
/// surface is legible exactly as often as what is behind it happens to be dark and quiet, and what
/// is behind THIS window is not a constant. The VR options menu is not a window the game ships: it
/// is the options window's TAB PANE, cloned and then detached onto the canvas
/// (<c>VROptionsTab.1.Inject.cs</c>, <c>TryDetach</c>) so it can float as a window of its own. In
/// the flat game that pane is never seen alone — it is drawn INSIDE <c>UI Options
/// Window_unified</c>, whose opaque panel art is the ground every one of its pixels was authored
/// against. Detached, the pane brings only its own <c>Main Area</c> graphic with it, and behind
/// that is whatever the world happens to hold: the lit scenario, the 3D map room, another floated
/// window, the main menu's backdrop. Same rows, same alphas, four different legibilities — which is
/// precisely the report.</para>
///
/// <para><b>THIS IS NOT THE SAME AS THE OTHER MENUS, AND THAT IS THE ASYMMETRY THE USER IS
/// NAMING.</b> The ESC and Options windows lose their full-window BLUR/backing overlay when the
/// mod floats them (<c>ModalFallback.WantsTransparentBackground</c> →
/// <c>CanvasConversion.HideFullScreenBackground</c>), but they keep their own window PANEL — that
/// sweep only disables a graphic covering ≥85% of the window frame, and a menu's panel art is
/// smaller than its full-screen veil. So every other floated menu still stands on an opaque plate
/// of its own. The VR options window never had one to keep. It is also exempt from that sweep
/// altogether (its <c>UIWindowID</c> is <c>None</c> by design, ModBuild 337, and
/// <c>WantsTransparentBackground</c> is an ID set) — which is why no <c>MODAL BACKGROUND</c> line
/// has ever named it, and why the remedy here cannot be undone by it either.</para>
///
/// <para><b>THE REMEDY IS A GROUND, NOT A TUNED ALPHA.</b> <see cref="EnsureOpaqueGround"/> puts
/// one mod-owned, fully opaque plate behind the whole pane. It is the repo's own panel neutral —
/// <see cref="MrBacking.PanelNeutral"/>, the same colour that already stands behind every floated
/// menu in mixed reality and that the user has accepted there — so it is a value this project has
/// shipped and had judged, not a third guess at an RGBA. Nothing else about the rows changes:
/// they are clones of the game's own rows and they now sit on a ground as solid as the one they
/// were authored against, which is what "wie die anderen Menus" means.</para>
///
/// <para><b>AND THE MEASUREMENT IS SHIPPED WITH IT.</b> The one thing this round could not read
/// off a log is what the pane's own <c>Main Area</c> graphic and the options window's own panel art
/// actually are — the existing probe (<c>VROptionsTab.1.Inject.cs</c>, <c>ProbeRowArchetypes</c>)
/// dumps component TYPES and never a colour. <see cref="ProbeMenuSkin"/> logs the three graphics
/// this file reasons about, with sprite, RGBA and rect coverage, so the next round can replace the
/// flat neutral with the game's own panel art on evidence instead of on this file's argument.</para>
/// </summary>
internal static partial class VROptionsTab
{
    /// <summary>Name of the mod-owned ground plate — also how it is found again after a rebuild.</summary>
    private const string GroundName = "GloomhavenVR.MenuGround";

    /// <summary>The ground plate, kept so the per-show rebuild neither duplicates nor re-searches it.</summary>
    private static Image? _ground;

    /// <summary>
    /// The game's own resting visual for a settings row's plate, sampled off the toggle donor's
    /// <c>Background</c> child — the graphic every cloned row in this menu already wears.
    ///
    /// <para>Sampled rather than chosen because the shipped action-row plate was chosen twice and
    /// was wrong both times: black at 30% "vanished on the window's own dark ground", and the warm
    /// bronze that replaced it went out at alpha 0.50, which is the same bug one shade further on.
    /// The alpha the donor was AUTHORED at is kept with the colour, because it is the thing that
    /// decides whether the sample carries usable colour at all — an invisible graphic's RGB says
    /// nothing.</para>
    /// </summary>
    private static (Sprite? sprite, Image.Type type, Color colour)? _rowPlateStyle;

    /// <summary>One-shot guard for the derived-plate line: one row shape, not one row.</summary>
    private static bool _actionPlateLogged;

    /// <summary>
    /// Below this authored alpha the donor's row plate carries no colour worth deriving from, and
    /// the action plate falls back to the value the last round tuned by eye — opacified.
    /// </summary>
    private const float RowPlateReadableAlpha = 0.10f;

    /// <summary>
    /// What an action plate is painted when the donor row has no readable plate of its own: the
    /// warm bronze the previous round settled on, at FULL alpha. Only the transparency was ever
    /// reported; the hue was not, so the hue is kept.
    /// </summary>
    private static readonly Color ActionPlateFallback = new(0.30f, 0.25f, 0.15f, 1f);

    /// <summary>Hover tint for an action plate — the menu's gold, unchanged in hue and now opaque.
    /// Hover and press feedback are what make a row feel like a button, so only the resting state
    /// was ever at fault.</summary>
    private static readonly Color ActionPlateHover = new(0.50f, 0.40f, 0.18f, 1f);

    /// <summary>Press tint for an action plate — the same gold, brighter, opaque.</summary>
    private static readonly Color ActionPlatePress = new(0.68f, 0.55f, 0.24f, 1f);

    /// <summary>Sample the game's own row plate once, off the captured toggle template.</summary>
    private static void SampleRowPlate(GameObject? toggleTemplate)
    {
        _rowPlateStyle = null;
        _actionPlateLogged = false;
        if (toggleTemplate == null)
            return;

        Transform? background = toggleTemplate.transform.Find("Background");
        Image? image = background != null ? background.GetComponent<Image>() : null;
        if (image == null)
            return;

        _rowPlateStyle = (image.sprite, image.type, image.color);
    }

    /// <summary>
    /// The resting colour of an action row's plate: the game's own row plate at FULL alpha, or the
    /// previous round's hue at full alpha when the donor has nothing readable to derive from.
    ///
    /// <para>Full alpha ALWAYS, which is the same rule <see cref="ApplyHeaderCaption"/> already
    /// enforces on the section headers, for the reason recorded there — an authored or inherited
    /// alpha below 1 is what hid those once already.</para>
    /// </summary>
    private static Color ActionPlateRest()
    {
        if (_rowPlateStyle is { } style && style.colour.a >= RowPlateReadableAlpha)
            return new Color(style.colour.r, style.colour.g, style.colour.b, 1f);
        return ActionPlateFallback;
    }

    /// <summary>
    /// Dress an action plate in the game's own row art.
    ///
    /// <para>THE IMAGE ITSELF STAYS WHITE — the <c>Button</c>'s ColorBlock multiplies the graphic's
    /// colour, so the derived resting colour goes in the ColorBlock and putting it here too would
    /// square it. The SPRITE and its draw TYPE are copied verbatim, because a 9-sliced sprite drawn
    /// as Simple stretches its corners and a Simple sprite drawn as Sliced with no border is a
    /// per-frame Unity warning; the donor already knows which it is.</para>
    ///
    /// <para>A donor whose plate is authored near-invisible gives no sprite either — its art is as
    /// uninformative as its colour — and the plate falls back to a plain tinted rectangle.</para>
    /// </summary>
    private static void PaintActionPlate(Image plate)
    {
        Color rest = ActionPlateRest();
        bool derived = _rowPlateStyle is { } style && style.colour.a >= RowPlateReadableAlpha;
        if (derived && _rowPlateStyle is { } art && art.sprite != null)
        {
            plate.sprite = art.sprite;
            plate.type = art.type;
        }

        plate.color = Color.white;
        plate.raycastTarget = true;

        if (_actionPlateLogged)
            return;
        _actionPlateLogged = true;
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"VR OPTIONS ACTION ROW: plate seated in the option column, resting colour RGBA "
            + $"{rest.r:0.##},{rest.g:0.##},{rest.b:0.##},{rest.a:0.##} "
            + (derived
                ? "DERIVED from the game's own row Background "
                  + $"(sprite '{(plate.sprite != null ? plate.sprite.name : "none")}')"
                : "from the FALLBACK hue — the donor row's Background is authored below alpha "
                  + $"{RowPlateReadableAlpha:0.##} and carries no colour worth deriving from")
            + $"; hover {ActionPlateHover.r:0.##},{ActionPlateHover.g:0.##},{ActionPlateHover.b:0.##},1, "
            + $"press {ActionPlatePress.r:0.##},{ActionPlatePress.g:0.##},{ActionPlatePress.b:0.##},1. "
            + "The shipped plate rested at alpha 0.50 across the WHOLE row; it is now opaque and "
            + "compact, and the caption is a normal left-column row caption again.");
    }

    /// <summary>
    /// Put — and keep — one opaque plate behind the whole detached pane.
    ///
    /// <para>Called from the per-show rebuild rather than once at injection, because the pane is
    /// re-parented, re-sized and re-shown by machinery this file does not own, and a plate that
    /// exists only if one particular call ran is a plate that is sometimes missing. It is
    /// idempotent: an existing plate is re-seated as the first child (so it stays behind every row)
    /// and re-painted, and nothing is allocated.</para>
    ///
    /// <para>FIRST CHILD, not a sibling of the pane and not a parent of it. A uGUI parent draws its
    /// own Graphic BEFORE its children, so a first child sits above the pane's authored
    /// <c>Main Area</c> tint and below everything the mod builds — which is the slot a window's
    /// panel art occupies.</para>
    ///
    /// <para>It does NOT take raycasts. The pane's own hit behaviour is not part of the report, and
    /// a new full-window raycast target underneath a scroll view is a behaviour change smuggled in
    /// behind a colour fix.</para>
    ///
    /// <para>NEVER-EMPTY GUARD: every failure here is a no-op. A missing pane returns before
    /// anything is built, and the plate is ADDED to the pane rather than replacing any part of it,
    /// so the worst case is a window that looks exactly as it did before this round.</para>
    /// </summary>
    private static void EnsureOpaqueGround()
    {
        if (_window == null)
            return;

        Transform pane = _window.transform;

        // A pane that was torn down and rebuilt leaves a fake-null or foreign-parented plate
        // behind; either way the cached one is not the one this pane needs.
        if (_ground != null && (_ground.transform == null || _ground.transform.parent != pane))
            _ground = null;

        if (_ground == null)
        {
            Transform? existing = pane.Find(GroundName);
            _ground = existing != null ? existing.GetComponent<Image>() : null;
        }

        bool made = false;
        if (_ground == null)
        {
            var go = new GameObject(GroundName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(pane, worldPositionStays: false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            _ground = go.AddComponent<Image>();
            _ground.raycastTarget = false;
            made = true;
        }

        _ground.transform.SetAsFirstSibling();
        _ground.color = MrBacking.PanelNeutral;
        if (!_ground.gameObject.activeSelf)
            _ground.gameObject.SetActive(true);

        if (made)
        {
            Color neutral = MrBacking.PanelNeutral;
            // HW-VERIFY
            VRLog.Note("WorldUI",
                $"VR OPTIONS GROUND: added an opaque plate '{GroundName}' behind the whole detached "
                + $"pane '{pane.name}', painted RGBA {neutral.r:0.##},{neutral.g:0.##},{neutral.b:0.##},1 "
                + "(the repo's panel neutral — the same ground every floated menu already stands on "
                + "in mixed reality). This pane is the options window's TAB PANE detached onto the "
                + "canvas, so until now it carried no window panel of its own and its rows were "
                + "composited over whatever the world held, which is the 'manchmal nicht zu "
                + "erkennen' in the report. The plate takes no raycasts and is drawn first, so "
                + "nothing else about the window changed.");
        }
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
    /// neutral <see cref="EnsureOpaqueGround"/> paints can be replaced by the game's own panel and
    /// the menu stops merely READING like the others and starts being one.</para>
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
            sb.Append("row plate = ")
              .Append(DescribeGraphic(background != null ? background.GetComponent<Graphic>() : null));

            sb.Append("; pane ground = ")
              .Append(DescribeGraphic(_window != null ? _window.GetComponent<Graphic>() : null));

            sb.Append("; widest panel art on '").Append(host.name).Append("' outside its tab panes = ")
              .Append(DescribePanelArt(host));

            sb.Append(". The pane ground is what the mod's rows are composited over once the pane is "
                      + "detached; the panel art is what every OTHER floated menu keeps and this one "
                      + "never had.");
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
    /// The options window's own panel art: the widest sprite-bearing <see cref="Image"/> that is
    /// NOT inside one of its tab panes.
    ///
    /// <para>Tab panes are excluded because a pane is the very thing the mod clones — a pane's own
    /// ground is not the window's panel, and counting it would make the measurement agree with this
    /// file's hypothesis by construction.</para>
    /// </summary>
    private static string DescribePanelArt(UIOptionsWindow host)
    {
        if (host.transform is not RectTransform hostRect)
            return "the host has no rect";

        float hostArea = Mathf.Abs(hostRect.rect.width * hostRect.rect.height);
        Image? widest = null;
        float widestArea = 0f;

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

        if (widest == null)
            return "none";

        float cover = hostArea > 1f ? widestArea / hostArea : 0f;
        return DescribeGraphic(widest) + $" covering {cover:P0} of the window rect";
    }
}

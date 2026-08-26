using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Element infusions ("Elemente") — GLOBAL
// =================================================================================================

/// <summary>
/// The element infusion board, drawn in the LEFT column below the objectives — the mirror of the
/// local board's docked <c>ElementBoardSurface</c>, seated at
/// <see cref="RemoteBoardLayout.ElementMount"/> (<c>PlayTray.ElementMountBase</c> plus the AUTHORED
/// per-board <c>ElementsOffset</c>, keyed by the peer's synced style). The seat used to drop that
/// per-board term, which on the Steel and Bronze boards buried the strip 40 mm behind where the
/// owner has it — part of defect (c) of the 1:1-parity round.
///
/// SOURCE (global, zero wire): <c>ElementInfusionBoardManager.ElementColumn(EElement)</c>, the exact
/// static the game's own <c>InfusionBoardUI.UpdateBoard</c> reads to decide each chip's state. The
/// infusion table is scenario-wide and identical on every client, so this needs no traffic and
/// reveals nothing.
///
/// PRESENTATION (user report 2026-08-04, element-darstellung.png "man sieht dort nur Quadrate"):
/// each chip is the game's OWN element disc — the exact strong/waning SPRITE the local docked
/// <c>InfusionBoardUI</c> shows, resolved from the singleton's authored per-element config
/// (<c>elementConfigs[i].strongIcon</c> / <c>.waningIcon</c>, the very sprites its Awake hands each
/// <c>InfusionElementUI</c>). Drawn by a <see cref="SpriteRenderer"/>, which honours atlas packing
/// (rect, rotation, tight meshes) that a hand-UV'd quad cannot, and routed through the shared
/// <see cref="CardFaceMipBake"/> cache so the disc samples a mipmapped copy of the game's mipless
/// UI atlas instead of shimmering (the proven card-face treatment).
///
/// WHY THE OLD LOOK WAS "blank colored squares", root cause read from source: the chips were bare
/// <c>BoardVisual.Quad</c>s wearing an UNTEXTURED <c>BoardVisual.Unlit(tint)</c> material — no
/// texture was ever assigned anywhere in this file, so a flat tinted rectangle was the DESIGNED
/// output, not a load failure. (No RenderTexture path is involved — irrelevant here, the icons are
/// plain sprites.) The tinted quad survives only as the FALLBACK for frames where the game's
/// infusion board singleton does not exist yet (menu / loading window), exactly the situations the
/// hardcoded colour table already covered.
///
/// STATES mirror vanilla (<c>InfusionElementUI.SetState</c>): an INERT element is not drawn at all
/// (vanilla <c>SetActive(false)</c>s it), STRONG shows <c>strongIcon</c>, WANING shows
/// <c>waningIcon</c> — both untinted at full size, because the waning artwork itself conveys the
/// state (vanilla swaps the sprite; it does not dim or shrink). The dim+shrink treatment remains
/// only on the colour-quad fallback, where there is no artwork to do that job.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ElementInfusionBoardManager.ElementColumn</c> + the local client's own
/// <c>InfusionBoardUI</c> sprites (game-owned assets, read-only). See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteElementStrip
{
    /// <summary>Element dock width budget — <c>PlayTray.ElementMountWidth</c>. The mount origin is
    /// RIGHT-centre growing LEFT (the objectives convention), so the strip centres half a width to
    /// the left of it.</summary>
    private const float Width = PlayTray.ElementMountWidth;
    private const float ChipSize = 0.030f;
    private const float ChipStep = 0.038f;

    private static readonly Color[] Fallback =
    {
        new(0.95f, 0.40f, 0.15f), // Fire
        new(0.45f, 0.80f, 1.00f), // Ice
        new(0.72f, 0.80f, 0.86f), // Air
        new(0.45f, 0.72f, 0.30f), // Earth
        new(1.00f, 0.95f, 0.58f), // Light
        new(0.56f, 0.40f, 0.82f), // Dark
    };

    private readonly Transform _root;

    /// <summary>
    /// MIXED-REALITY backing plate behind the chip run (user: the MR text/content backing must
    /// cover REMOTE boards too). The owner's element board is a CONVERTED panel, so MrBacking's
    /// panel sweep puts an opaque host plate behind it in MR; this mod-drawn mirror is no panel
    /// and its discs floated bare over the passthrough room. The plate is authored at ALPHA 0 —
    /// a fully transparent draw, so normal mode renders pixel-identically — and registered with
    /// <c>MrBacking.Opacify</c>, which drives it to alpha 1 while MR is on and restores the
    /// recorded 0 exactly on off. Refit to the visible run on every repaint.
    /// </summary>
    private readonly MeshRenderer _mrPlate;
    private readonly Transform[] _chips = new Transform[6];
    private readonly MeshRenderer[] _quads = new MeshRenderer[6];
    private readonly Material[] _mats = new Material[6];
    private readonly SpriteRenderer[] _icons = new SpriteRenderer[6];

    /// <summary>The game's authored per-element disc sprites, resolved lazily off
    /// <c>InfusionBoardUI.Instance</c> (null slots until the singleton exists — menu/loading).
    /// [i,0] = strong, [i,1] = waning.</summary>
    private readonly Sprite?[,] _sprites = new Sprite?[6, 2];
    private bool _spritesResolved;
    private bool _resolveLogged;

    private int _signature = -1;

    /// <summary>How many non-inert elements the strip currently draws (diagnostics).</summary>
    public int ActiveCount { get; private set; }

    public RemoteElementStrip(Transform boardRoot, in RemoteBoardLayout layout)
    {
        // MOUNT (position + authored per-board scale, exactly like PlayTray.BuildMounts sets its
        // own element mount) …
        var mount = new GameObject("ElementMount").transform;
        mount.SetParent(boardRoot, worldPositionStays: false);
        mount.localPosition = layout.ElementMount;
        mount.localScale = Vector3.one * layout.ElementScale;

        // … and the strip itself, half a dock width to the LEFT of it — the mount's origin is
        // RIGHT-centre growing left (the objectives convention), so the shift belongs INSIDE the
        // mount, where the scale applies to it too.
        _root = new GameObject("Elements").transform;
        _root.SetParent(mount, worldPositionStays: false);
        _root.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);

        // MR backing plate (see the field doc): the repo's dark panel neutral at alpha 0, seated
        // slightly BEHIND the chips toward the board (+Z) like every remote text plate.
        _mrPlate = BoardVisual.Quad(_root, "MrPlate", new Vector2(1f, 1f),
            BoardVisual.Unlit(new Color(0.12f, 0.11f, 0.10f, 0f)));
        _mrPlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
        _mrPlate.gameObject.SetActive(false);
        WorldUI.MrBacking.Opacify(_mrPlate.sharedMaterial);

        for (int i = 0; i < 6; i++)
        {
            // One positioned CHIP root per element; the quad fallback and the sprite icon are
            // siblings under it, so the layout below moves one transform whichever renders.
            // Everything is built HERE, at board-build time, because VRLayers.Apply runs over the
            // finished board once — a renderer created later would miss the re-layer and be
            // invisible to the mod head camera.
            var chip = new GameObject($"Element_{(ElementInfusionBoardManager.EElement)i}").transform;
            chip.SetParent(_root, worldPositionStays: false);
            _chips[i] = chip;

            _mats[i] = BoardVisual.Unlit(Fallback[i]);
            _quads[i] = BoardVisual.Quad(chip, "Fallback", new Vector2(1f, 1f), _mats[i]);

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(chip, worldPositionStays: false);
            var icon = iconGo.AddComponent<SpriteRenderer>();
            icon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            icon.receiveShadows = false;
            icon.enabled = false;
            _icons[i] = icon;

            chip.gameObject.SetActive(false);
        }
    }

    /// <summary>Re-read the infusion table and repaint on an actual change.</summary>
    public void Refresh()
    {
        // Late sprite resolution: the singleton comes up with the scenario UI, typically after the
        // board was built. Cheap while unresolved (one null check per cadence tick); on success the
        // signature is invalidated so the very next repaint switches the quads over to the discs.
        if (!_spritesResolved && TryResolveSprites())
            _signature = -1;

        int sig = 0;
        int visible = 0;
        var state = new ElementInfusionBoardManager.EColumn[6];
        for (int i = 0; i < 6; i++)
        {
            ElementInfusionBoardManager.EColumn col;
            try { col = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { col = ElementInfusionBoardManager.EColumn.Inert; }
            state[i] = col;
            sig = sig * 3 + (int)col;
            if (col != ElementInfusionBoardManager.EColumn.Inert)
                visible++;
        }
        if (sig == _signature)
            return;
        _signature = sig;
        ActiveCount = visible;

        // MR plate: hug the visible run (centred at the strip origin, like the run itself), with a
        // small out-pad so the disc edges sit on plate rather than passthrough room; hidden while
        // no element is up (an empty strip must not show a bare plate in MR).
        bool anyChips = visible > 0;
        if (_mrPlate.gameObject.activeSelf != anyChips)
            _mrPlate.gameObject.SetActive(anyChips);
        if (anyChips)
        {
            float runW = (visible - 1) * ChipStep + ChipSize + 0.010f;
            _mrPlate.transform.localScale = new Vector3(runW, ChipSize + 0.010f, 1f);
        }

        // Pack the visible chips left-to-right and centre the run, exactly like the game's own
        // horizontal element holder does with its layout group.
        float left = -(visible - 1) * 0.5f * ChipStep;
        int slot = 0;
        for (int i = 0; i < 6; i++)
        {
            bool on = state[i] != ElementInfusionBoardManager.EColumn.Inert;
            if (_chips[i].gameObject.activeSelf != on)
                _chips[i].gameObject.SetActive(on);
            if (!on)
                continue;
            bool strong = state[i] == ElementInfusionBoardManager.EColumn.Strong;
            _chips[i].localPosition = new Vector3(left + slot * ChipStep, 0f, 0f);
            slot++;

            Sprite? sprite = _sprites[i, strong ? 0 : 1];
            if (sprite != null)
            {
                // THE REAL DISC: vanilla parity is the sprite itself — untinted, full chip size in
                // both states (the waning artwork carries the waning look; see the class doc).
                ApplyIcon(_icons[i], sprite);
                if (_quads[i].enabled)
                    _quads[i].enabled = false;
            }
            else
            {
                // FALLBACK (no singleton yet, or an unresolvable sprite): the tinted quad, with the
                // old dim+shrink standing in for the missing waning artwork.
                if (_icons[i].enabled)
                    _icons[i].enabled = false;
                if (!_quads[i].enabled)
                    _quads[i].enabled = true;
                Color c = ColorFor((ElementInfusionBoardManager.EElement)i, i);
                _mats[i].color = strong ? c : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.80f);
                float s = strong ? ChipSize : ChipSize * 0.74f;
                _quads[i].transform.localScale = new Vector3(s, s, 1f);
            }
        }
    }

    /// <summary>
    /// Point <paramref name="icon"/> at (the mip-baked copy of) <paramref name="sprite"/> and fit
    /// it into the chip footprint. A SpriteRenderer draws a sprite at
    /// <c>rect/pixelsPerUnit</c> world units, so the fit divides the chip size by the larger
    /// bounds axis — the disc fills the chip without distortion whatever the atlas padding is.
    /// </summary>
    private static void ApplyIcon(SpriteRenderer icon, Sprite sprite)
    {
        // Shared mip-bake cache (the local card-face treatment): the element discs live on the
        // game's mipless UI atlases, and a mipless bilinear sprite on a world-space board aliases
        // in texture space. ReplacementFor returns null both for "already mipped" and for sprites
        // it must not reproduce (rotated/tight packing) — the original then renders as-is, which
        // is never worse than before. Guarded: a bake surprise must not cost the strip its icons.
        Sprite show = sprite;
        try
        {
            Sprite? baked = CardFaceMipBake.ReplacementFor(sprite);
            if (baked != null)
                show = baked;
        }
        catch { /* keep the original sprite */ }

        if (icon.sprite != show)
            icon.sprite = show;
        Vector3 size = show.bounds.size; // rect / pixelsPerUnit, world units at scale 1
        float axis = Mathf.Max(size.x, size.y);
        float fit = axis > 0.0001f ? ChipSize / axis : 1f;
        var scale = new Vector3(fit, fit, 1f);
        if (icon.transform.localScale != scale)
            icon.transform.localScale = scale;
        if (!icon.enabled)
            icon.enabled = true;
    }

    /// <summary>
    /// Pull the authored strong/waning disc sprites off the game's infusion-board singleton —
    /// <c>InfusionBoardUI.elementConfigs</c> (publicized serialized field), the exact array its own
    /// Awake feeds every <c>InfusionElementUI.Init</c>. Read-only: sprites are assets, never
    /// mutated. Returns true once at least one element resolved; a throwing/absent singleton just
    /// leaves the fallback quads in place until the next cadence tick.
    /// </summary>
    private bool TryResolveSprites()
    {
        try
        {
            InfusionBoardUI? board = InfusionBoardUI.Instance;
            if (board == null || board.elementConfigs == null)
                return false;
            int resolved = 0;
            var configs = board.elementConfigs;
            for (int c = 0; c < configs.Length; c++)
            {
                int i = (int)configs[c].element;
                if (i < 0 || i >= 6)
                    continue;
                _sprites[i, 0] = configs[c].strongIcon;
                _sprites[i, 1] = configs[c].waningIcon;
                if (configs[c].strongIcon != null)
                    resolved++;
            }
            if (resolved == 0)
                return false;
            _spritesResolved = true;
            if (!_resolveLogged)
            {
                _resolveLogged = true;
                VRLog.Info("Net", $"Remote element strip: {resolved}/6 element disc sprites resolved " +
                                  "from InfusionBoardUI.elementConfigs — the strip now draws the " +
                                  "game's own strong/waning discs (mip-baked via the shared card " +
                                  "cache) instead of the tinted fallback squares.");
            }
            return true;
        }
        catch (System.Exception ex)
        {
            if (!_resolveLogged)
            {
                _resolveLogged = true;
                VRLog.Warn("Net", $"Remote element strip: disc sprite resolution failed " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the strip keeps the tinted " +
                                  "fallback chips.");
            }
            return false;
        }
    }

    private static Color ColorFor(ElementInfusionBoardManager.EElement e, int index)
    {
        try
        {
            if (UIInfoTools.Instance != null)
                return UIInfoTools.Instance.GetElementHighlightColor(e, 1f);
        }
        catch { /* menu / loading window — fall through */ }
        return Fallback[index];
    }

}

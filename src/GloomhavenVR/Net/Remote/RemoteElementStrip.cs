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
///
/// TRANSITIONS, AND THE HONEST LINE THROUGH THE MIDDLE OF THEM. The owner's docked board plays four
/// distinct animations, and they do NOT come from one place — which is the whole finding:
/// <list type="bullet">
/// <item>CREATED (<c>InfusionElementUI.ShowCreated</c>, played on <c>null|Inert -&gt; Strong</c> and
///   <c>null|Inert -&gt; Waning</c>) and its counterpart, an element dropping OUT of the board's
///   list. Both are a pure function of <c>ElementInfusionBoardManager.ElementColumn</c> — the exact
///   static <c>InfusionBoardUI.UpdateBoard</c> itself switches on (InfusionBoardUI.cs:196-236,
///   InfusionElementUI.cs:117-147) — which is scenario-wide, host-replicated and already the ONLY
///   input this strip reads. So they are DERIVABLE, they need no wire field, and
///   <see cref="TickTransitions"/> below plays them. <c>Strong -&gt; Waning</c> deliberately gets
///   none: vanilla's own branch there calls <c>StopAnimations()</c> and only swaps the sprite,
///   which this strip already did.</item>
/// <item>CREATING (the pending "this element is being infused" pulse, <c>ShowCreating</c> +
///   <c>creationImage</c>), RESERVED (which HIDES the chip outright,
///   <c>InfusionBoardUI.UpdateBoard</c> line "else if (IsElementReserved…) SetActive(false)") and
///   AVAILABLE (<c>SetAvailableElements</c> -&gt; <c>availableHighlight.enabled</c>). All three come
///   from <c>InfusionBoardUI</c>'s PRIVATE per-client lists <c>elementsInCreation</c> /
///   <c>elementsReserved</c>, and from a <c>SetAvailableElements</c> call — and every writer of
///   those is the LOCAL player's own UI flow (<c>FullAbilityCard.OnAbilityClick</c>:726/812,
///   <c>ItemsUI</c>:154-161, <c>UIUseItemsBar</c>:590-594, <c>AugmentationHolder</c>:27/56,
///   <c>UIUseAugmentationsBar</c>:129). They are NOT replicated. A peer's client holds the acting
///   player's outcome, never their pending selection, so these three cannot be derived at any
///   price and are the wire debt this lane files rather than fakes.</item>
/// </list>
/// The transition ramp itself borrows the mod's own card vocabulary — <c>VRCard.SmootherStep</c>
/// over <c>DockAppearSeconds</c> (0.28 s) / <c>DockVanishSeconds</c> (0.30 s) — because the owner's
/// <c>animatorCreated</c> is an authored <c>GUIAnimator</c> subclass whose curve lives in prefab
/// scene data this mod cannot read. Stated rather than hidden: the mirror says "an element just
/// came up / just went out" on the house curve, which is a truthful statement of an event the owner
/// really does see animate. It emits NO dust — the owner's is a UI animator, not a particle puff,
/// and manufacturing one would be inventing a picture rather than mirroring it.
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

    // ------------------------------------------------------- created / consumed transitions --

    /// <summary>Whether chip <c>i</c> was NON-INERT on the previous repaint — the edge the whole
    /// transition rests on. Kept beside <see cref="_signature"/> rather than derived from it: the
    /// signature is one packed number for the whole strip and cannot say WHICH element moved.
    /// </summary>
    private readonly bool[] _shownOn = new bool[6];

    /// <summary>Per-chip ramp: whether one is running, whether it is the arrival direction, and how
    /// far in it is. Six parallel arrays rather than a struct list because the set is fixed at six
    /// and indexed by <c>EElement</c> everywhere else in this file.</summary>
    private readonly bool[] _ramping = new bool[6];
    private readonly bool[] _rampIn = new bool[6];
    private readonly float[] _rampElapsed = new float[6];

    /// <summary>The alpha the last repaint wrote into chip <c>i</c>'s FALLBACK quad colour — 1 for
    /// a strong chip, 0.80 for the dimmed waning stand-in. The ramp scales THIS rather than
    /// overwriting it, or a waning fallback chip would fade in to the wrong opacity. The sprite
    /// path needs no twin: a <c>SpriteRenderer</c>'s untinted white is always 1.</summary>
    private readonly float[] _quadBaseAlpha = { 1f, 1f, 1f, 1f, 1f, 1f };

    /// <summary>
    /// THE NO-STORM SEED. The first repaint after a board build populates the whole strip at once,
    /// and every element already up would otherwise materialise in front of a peer who was looking
    /// at a settled board a frame earlier — an animation nobody's owner is playing. Same rule, same
    /// reason, as <c>CardsDriver</c>'s <c>_dockAnimSuppressed</c> and <c>RemoteBoardCard</c>'s own
    /// seed: the first content is silent, every later change animates.
    /// </summary>
    private bool _transitionsSeeded;

    /// <summary>The settle scale a chip grows from / shrinks to under the fade. The mod's own card
    /// number (<c>VRCard.DustSettleScale</c>, a <c>private const</c> over there), restated here
    /// rather than borrowed from <see cref="RemoteBoardCard"/> because a strip and a card recess
    /// share no tuning surface and coupling them would only invent one.</summary>
    private const float ChipSettleScale = 0.82f;

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

        // The transition clock. This class is plain C# and its owner refreshes content on a 4 Hz
        // cadence — one or two samples across a 0.3 s ramp, i.e. the snap the ramp exists to
        // remove — so the strip carries its own tick, exactly as RemoteBoardCard's MaterialisePump
        // and RemoteBoardFurniture's RemoteCapFx do. It rides the strip root, so it stops with the
        // board: a strip nobody is drawing has nothing to animate.
        _root.gameObject.AddComponent<ElementTransitionPump>().Strip = this;
    }

    /// <summary>
    /// Advance every running chip transition by one frame — the CREATED pop and its counterpart,
    /// the crumble of an element that just left the board. See the class doc for the evidence that
    /// exactly these two are derivable from <c>ElementColumn</c> and that the CREATING / RESERVED /
    /// AVAILABLE animations are not.
    ///
    /// <para>Curve, durations and hitch cap are the mod's card ramp, referenced rather than copied
    /// (<c>VRCard.SmootherStep</c>, <c>DockAppearSeconds</c> 0.28 s, <c>DockVanishSeconds</c>
    /// 0.30 s), on UNSCALED time because the element board moves during card phases, which pause
    /// <c>timeScale</c>. Never throws: every write is to an object this class minted.</para>
    /// </summary>
    internal void TickTransitions()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        for (int i = 0; i < 6; i++)
        {
            if (!_ramping[i])
                continue;
            _rampElapsed[i] += dt;
            float duration = _rampIn[i] ? VRCard.DockAppearSeconds : VRCard.DockVanishSeconds;
            float t = duration > 0f ? Mathf.Clamp01(_rampElapsed[i] / duration) : 1f;
            float st = VRCard.SmootherStep(t);
            ApplyChip(i, _rampIn[i] ? st : 1f - st);
            if (t < 1f)
                continue;
            _ramping[i] = false;
            // Both directions land SETTLED, not at the ramp's end value: a chip that crumbled away
            // is about to be hidden and must come back opaque and full-size the next time its
            // element is infused, exactly as VRCard resets its own alpha before parking.
            ApplyChip(i, 1f);
            if (!_rampIn[i] && _chips[i].gameObject.activeSelf)
                _chips[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Arm chip <paramref name="i"/>'s transition and paint its frame zero here rather
    /// than on the next tick — otherwise an arriving element shows one fully settled frame before
    /// it starts arriving, which is the snap the ramp exists to remove.</summary>
    private void BeginChipRamp(int i, bool arriving)
    {
        _ramping[i] = true;
        _rampIn[i] = arriving;
        _rampElapsed[i] = 0f;
        ApplyChip(i, arriving ? 0f : 1f);
    }

    /// <summary>Settle chip <paramref name="i"/> at once — a repaint of a chip that is STAYING up
    /// (the Strong to Waning swap, which vanilla explicitly does not animate) must not inherit a
    /// half-faded alpha from a ramp that was still running. Free when nothing is running.</summary>
    private void EndChipRamp(int i)
    {
        if (!_ramping[i])
            return;
        _ramping[i] = false;
        _rampElapsed[i] = 0f;
        ApplyChip(i, 1f);
    }

    /// <summary>
    /// Write one point of chip <paramref name="i"/>'s ramp. <paramref name="visible"/> is the
    /// smootherstepped fraction read from the arrival end, so one method serves both directions.
    ///
    /// <para>The scale rides the CHIP ROOT, whose two children (the fallback quad and the sprite
    /// icon) already carry their own fitted scales — so the settle multiplies onto whichever of the
    /// two is rendering without either of them needing to know about it. Both alphas are written
    /// even though only one renderer is enabled, for the same reason
    /// <c>RemoteBoardCard.ApplyMaterialise</c> writes all three of its materials: the repaint can
    /// switch which path draws while a ramp is running, and the other one must not be sitting at
    /// full opacity when it does.</para>
    /// </summary>
    private void ApplyChip(int i, float visible)
    {
        float a = Mathf.Clamp01(visible);
        _chips[i].localScale = Vector3.one * Mathf.Lerp(ChipSettleScale, 1f, a);
        Color quad = _mats[i].color;
        quad.a = _quadBaseAlpha[i] * a;
        _mats[i].color = quad;
        Color icon = _icons[i].color;
        icon.a = a;
        _icons[i].color = icon;
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
        //
        // RESIDUE, STATED: the plate is sized to the run this repaint LANDS on, so an element that
        // is crumbling out (see TickTransitions) sits beside the shrunken plate for up to
        // DockVanishSeconds (0.30 s) — in MR only, over passthrough, fading the whole time. Sizing
        // it to the union instead would make the plate itself jump wider than the discs for the
        // same 0.30 s, which is the more visible of the two artefacts on a backing whose entire job
        // is to be unnoticed.
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
            bool wasOn = _shownOn[i];
            _shownOn[i] = on;
            if (!on)
            {
                // LEFT THE BOARD (consumed, or waned all the way out). It keeps its seat and its
                // artwork and crumbles in place — the run below has already re-centred WITHOUT it,
                // which is why the position write is inside the `on` branch and this chip is not
                // repositioned. TickTransitions hides it when the ramp lands.
                if (wasOn && _transitionsSeeded)
                    BeginChipRamp(i, arriving: false);
                else if (_chips[i].gameObject.activeSelf && !_ramping[i])
                    _chips[i].gameObject.SetActive(false);
                continue;
            }
            if (!_chips[i].gameObject.activeSelf)
                _chips[i].gameObject.SetActive(true);
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
                // The base the ramp SCALES (see _quadBaseAlpha) — recorded on the same statement
                // that writes the colour, so the two can never be about different repaints.
                _quadBaseAlpha[i] = _mats[i].color.a;
                float s = strong ? ChipSize : ChipSize * 0.74f;
                _quads[i].transform.localScale = new Vector3(s, s, 1f);
            }

            // CREATED, or a settled repaint. The paint above has just overwritten the quad's alpha,
            // so a running ramp has to be re-applied (or ended) here and not before it.
            if (!wasOn && _transitionsSeeded)
                BeginChipRamp(i, arriving: true);
            else
                EndChipRamp(i);
        }

        // Every element the strip is going to show this repaint has now been seeded once, so the
        // NEXT change is a real one and animates. See _transitionsSeeded.
        _transitionsSeeded = true;
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

/// <summary>
/// The per-frame pump for one strip's chip transitions — the element-board twin of
/// <c>RemoteBoardCard.MaterialisePump</c>, and it exists for the identical reason: the strip is
/// plain C# and its owner repaints it on a 4 Hz content cadence, which cannot carry a 0.3 s ramp.
/// It rides the strip root, so a board that stops being drawn stops ticking.
/// </summary>
internal sealed class ElementTransitionPump : MonoBehaviour
{
    /// <summary>The strip this pump drives. Assigned once, at build time.</summary>
    internal RemoteElementStrip? Strip;

    private void Update() => Strip?.TickTransitions();
}

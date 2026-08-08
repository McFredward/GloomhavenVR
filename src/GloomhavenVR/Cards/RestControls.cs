using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Short-rest / long-rest controls on the play tray — two comfortable tray-styled
/// <see cref="PlayTray.BoardButton"/>s (test #24 item 3) sitting together in the rest
/// zone, replacing the old small round disc tokens.
///
/// Short rest: press → <c>ShortRest.MouseClick()</c> (via CardsGameApi.ToggleShortRest),
/// i.e. exactly the 2D widget path — the game's own yes/no confirmation dialog (now
/// docked on the board, item 5), then <c>CardsHandUI.PerformShortRest</c> with its
/// burn/redraw dialogs. The mod short button is the SOLE short-rest control: the native
/// "Kurze Rast" widget never docks any more (<see cref="Surfaces.TrayControlDockSurface"/>
/// hardcodes <c>ShortRestDocked =&gt; false</c> and its docked-control list is empty), and
/// <see cref="TickStatus"/> never consults it — visibility is <c>canShort || shortSelected</c>.
/// This doc used to describe a hide/reappear handshake with the native widget; there is none.
/// Long rest: press → toggles the long-rest pseudo-card (CardID −1) through the game's
/// own fan selection (CardsGameApi.ToggleLongRest) — there is NO discrete native
/// long-rest button, so the long-rest control STAYS mod-drawn; the burn-a-discarded-
/// card step arrives later as <c>CardHandMode.LoseCard</c> served by the fan.
/// Buttons dim when the action is unavailable and accent when selected (same gating
/// the 2D widgets use — see CardsGameApi.CanShortRest/CanLongRest).
/// </summary>
internal sealed class RestControls
{
    private PlayTray.BoardButton? _shortButton;
    private PlayTray.BoardButton? _longButton;
    private bool _locHooked;

    // Issue 3 (rest-button relevance visibility): last-logged visibility of each keycap, so the
    // hide/appear flips are logged edge-triggered (once per change) rather than per frame. Seeded
    // true — the keycaps are built visible, so the first "not relevant" tick logs the hide.
    private bool _lastShortVisible = true;
    private bool _lastLongVisible = true;

    /// <summary>Raised on press; CardsDriver queues the actual game call.</summary>
    internal System.Action? ShortRestRequested;
    internal System.Action? LongRestRequested;

    // ---- multiplayer board-UI read seam (Net.NetAvatarDriver.TickExtrasSend) -----------------
    // The RestControls instance is a private of CardsDriver, so the LIVE keycap visibility is
    // published as statics the same way PileBrowser.Current / ItemsPile.Current publish theirs.
    // Written by TickStatus (per frame while the tray shows), cleared on Destroy so a torn-down
    // board never advertises stale rest discs.

    /// <summary>True while the SHORT-rest keycap is visible on the local board.</summary>
    internal static bool ShortRestShown { get; private set; }

    /// <summary>True while the LONG-rest keycap is visible on the local board.</summary>
    internal static bool LongRestShown { get; private set; }

    // ---- multiplayer cap-STATE read seam (board-UI record byte 2) -----------------------------
    // "Buttons dim when the action is unavailable and accent when selected" (the class doc's own
    // sentence) was true LOCALLY and invisible to everyone else: a peer's mirrored discs were drawn
    // at their authored ACCENT colour permanently, so a dead disc and a selected one looked the
    // same as an available one. Both halves of the pair are published here, off the very values
    // TickStatus hands SetState, so the wire state IS the rendered state.

    /// <summary>True while the SHORT-rest keycap is ENABLED (a short rest is available). A visible
    /// but DISABLED disc is what the owner sees after selecting a rest that is no longer offered —
    /// dark wood, not the accent.</summary>
    internal static bool ShortRestEnabled { get; private set; }

    /// <summary>True while the SHORT-rest keycap is ACCENTED (that rest is selected).</summary>
    internal static bool ShortRestAccent { get; private set; }

    /// <summary>True while the LONG-rest keycap is ENABLED — see <see cref="ShortRestEnabled"/>.</summary>
    internal static bool LongRestEnabled { get; private set; }

    /// <summary>True while the LONG-rest keycap is ACCENTED (selected).</summary>
    internal static bool LongRestAccent { get; private set; }

    internal void EnsureBuilt(PlayTray tray)
    {
        // Feature 6a: the rest controls seat in the board's two rest-notches. PER-BOARD: the
        // diameter/side, the full X/Y/Z offset AND the cap SHAPE (Round disc / Square keycap)
        // come from the active board's config (the debug menu tunes them live) — this REPLACES
        // the old unreliable raycast auto-seating, so the buttons seat at a PREDICTABLE anchor +
        // per-board offset depth. Round-2: an extra per-board SPACING spreads the two apart.
        ControlBoard active = CardsConfig.CurrentBoard;
        float diameter = CardsConfig.RestButtonDiameter(active).Value;
        bool round = CardsConfig.RestButtonShape(active).Value == ButtonShape.Round;

        // Category split ([RestButtons], user: "every value applies ONLY to its own category" —
        // same pattern PlayTray.BuildButtons uses for [BoardButtons]): the rest keycaps read the
        // [RestButtons] set EXCLUSIVELY — SQUARE caps take its Width/Height (rectangular caps),
        // and BOTH shapes take its Depth/Travel. ROUND discs keep the per-board authored diameter
        // (the square W/H do not apply, exactly like the Confirm/Undo round caps). The numeric
        // defaults reproduce today's look bit-identically (0.105 × 0.105 × 0.012 / 4 mm travel =
        // the authored RestButtonDiameter / RoundButtonThickness / BoardButton.CapTravel).
        WorldUI.ButtonTuning.Bind();
        float thickness = WorldUI.ButtonTuning.RestCapDepth;
        float travel = WorldUI.ButtonTuning.RestCapTravel;
        var size = round
            ? new Vector2(diameter, diameter)                                                  // round: per-board diameter
            : new Vector2(WorldUI.ButtonTuning.RestCapWidth, WorldUI.ButtonTuning.RestCapHeight); // square: [RestButtons] W/H
        // Per-board disc offset: X/Y in the board plane, Z = proud depth toward the player
        // (NEGATIVE = prouder). Replaces the old localPosition(insetX,0,0) + raycast reseat.
        Vector3 offset = CardsConfig.RestButtonOffset(active).Value;
        float spacing = CardsConfig.RestButtonSpacing(active).Value;
        int built = 0;

        if (_shortButton == null && tray.ShortRestAnchor != null)
        {
            _shortButton = PlayTray.BoardButton.Create(tray.ShortRestAnchor, size,
                new Color(0.62f, 0.52f, 0.30f), // T4: parchment-gold accent when selected (antique)
                // No game key exists for a short rest button (GUI_SHORT_REST is absent) — mod string.
                Core.Loc.Mod("short_rest"),
                () => ShortRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel);
            _shortButton.WireCap = Net.NetProtocol.CapPressShortRest; // mirror the press dip to peers
            tray.RegisterLaserTarget(_shortButton.Collider!, _shortButton);
            built++;
        }
        if (_longButton == null && tray.LongRestAnchor != null)
        {
            _longButton = PlayTray.BoardButton.Create(tray.LongRestAnchor, size,
                new Color(0.37f, 0.44f, 0.56f), // T4: antique slate-blue accent when selected
                Core.Loc.Game("GUI_LONG_REST", "Long rest"),
                () => LongRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round, travel: travel);
            _longButton.WireCap = Net.NetProtocol.CapPressLongRest;
            tray.RegisterLaserTarget(_longButton.Collider!, _longButton);
            built++;
        }

        SetOffset(offset, spacing); // per-board X/Y in plane, Z proud, ± spacing/2 along Y

        // Live language following: the rest-button captions are built once, so re-read them
        // whenever the game language changes (subscribe once; Destroy detaches).
        if (!_locHooked)
        {
            _locHooked = true;
            Core.Loc.OnChanged += RefreshLabels;
        }

        if (built > 0)
            Core.VRLog.Info("Cards", $"RestControls: built {built} {(round ? "ROUND" : "SQUARE")} rest button(s) " +
                $"for {active} (size {size.x:F3}×{size.y:F3} m, [RestButtons] W/H/D " +
                $"{WorldUI.ButtonTuning.RestCapWidth:F3}/{WorldUI.ButtonTuning.RestCapHeight:F3}/{WorldUI.ButtonTuning.RestCapDepth:F3} m, " +
                $"travel {WorldUI.ButtonTuning.RestCapTravel:F3} m, offset {offset}, spacing {spacing:F3} m) — " +
                "per-board predictable seat, no raycast.");
    }

    /// <summary>
    /// Live-apply (Part F): move both rest buttons to a new per-board X/Y/Z offset + inter-button
    /// spacing in place (no rebuild). The short (upper) button takes +spacing/2 along the board's
    /// short axis, the long (lower) button −spacing/2.
    /// </summary>
    internal void SetOffset(Vector3 offset, float spacing)
    {
        if (_shortButton != null)
            _shortButton.transform.localPosition = offset + new Vector3(0f, spacing * 0.5f, 0f);
        if (_longButton != null)
            _longButton.transform.localPosition = offset + new Vector3(0f, -spacing * 0.5f, 0f);
    }

    /// <summary>Re-read the rest-button captions in the current language (live-follow, Loc.OnChanged).</summary>
    internal void RefreshLabels()
    {
        _shortButton?.SetLabel(Core.Loc.Mod("short_rest"));
        _longButton?.SetLabel(Core.Loc.Game("GUI_LONG_REST", "Long rest"));
    }

    internal void Destroy()
    {
        if (_locHooked)
        {
            Core.Loc.OnChanged -= RefreshLabels;
            _locHooked = false;
        }
        if (_shortButton != null)
            Object.DestroyImmediate(_shortButton.gameObject);
        if (_longButton != null)
            Object.DestroyImmediate(_longButton.gameObject);
        _shortButton = null;
        _longButton = null;
        ShortRestShown = false; // never advertise rest discs off a torn-down board
        LongRestShown = false;
        ShortRestEnabled = ShortRestAccent = false;
        LongRestEnabled = LongRestAccent = false;
    }

    /// <summary>
    /// Refresh each rest keycap's RELEVANCE visibility + availability/selected state (per frame
    /// while the tray shows). Issue 3 (user): the short/long-rest buttons must DISAPPEAR when a
    /// rest isn't a relevant option — exactly like the played cards clear during the action phase
    /// / enemy turns. The <see cref="PlayTray.BoardButton.SetVisible"/> flip animates via the
    /// button worker.
    /// </summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // A rest is only OFFERED during the card-SELECTION phase (SelectAbilityCardsOrLongRest),
        // and the game distinguishes short vs long availability independently — so each keycap
        // follows its OWN availability signal (CardsGameApi.CanShortRest / CanLongRest, both of
        // which already gate on the selection phase and return false during action/enemy turns,
        // mirroring the card board's IsActionTurn clear). A keycap stays up while its rest is
        // SELECTED / mid-choice (|| …Selected) so it does not vanish the instant it is chosen —
        // its accent then reads as the commitment. Item 1 note kept: the native "Kurze Rast"
        // widget never docks (TrayControlDockSurface.ShortRestDocked permanently false), so this
        // mod keycap is the sole short-rest control; long rest never had a discrete uGUI widget.
        bool canShort = false, canLong = false, shortSelected = false, longSelected = false;
        if (hand != null)
        {
            canShort = CardsGameApi.CanShortRest(hand);
            canLong = CardsGameApi.CanLongRest(hand);
            shortSelected = CardsGameApi.IsShortRestSelected(hand);
            longSelected = CardsGameApi.IsLongRestSelected(hand);
        }

        bool shortVisible = canShort || shortSelected;
        bool longVisible = canLong || longSelected;
        if (shortVisible != _lastShortVisible || longVisible != _lastLongVisible)
        {
            _lastShortVisible = shortVisible;
            _lastLongVisible = longVisible;
            Core.VRLog.Info("Cards", $"Rest buttons visibility: short={shortVisible} " +
                                     $"(canShort={canShort}, selected={shortSelected}), long={longVisible} " +
                                     $"(canLong={canLong}, selected={longSelected}) — hidden when a rest " +
                                     "isn't offered (action phase / enemy turns).");
        }

        _shortButton?.SetVisible(shortVisible);
        _longButton?.SetVisible(longVisible);
        _shortButton?.SetState(canShort, accent: shortSelected);
        _longButton?.SetState(canLong, accent: longSelected);

        // Publish for the multiplayer board-UI record (only meaningful while the caps exist).
        ShortRestShown = _shortButton != null && shortVisible;
        LongRestShown = _longButton != null && longVisible;
        // …and the cap STATES the very SetState calls above just applied (record 4 byte 2), so a
        // peer's mirrored disc dims and accents in the same frames the owner's does.
        ShortRestEnabled = canShort;
        ShortRestAccent = shortSelected;
        LongRestEnabled = canLong;
        LongRestAccent = longSelected;
    }
}

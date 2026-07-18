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
/// burn/redraw dialogs. The REAL native "Kurze Rast" widget docks over the short-rest
/// anchor when available (<see cref="Surfaces.TrayControlDockSurface"/>); the mod
/// short button then HIDES and reappears when the native widget undocks.
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

    /// <summary>Raised on press; CardsDriver queues the actual game call.</summary>
    internal System.Action? ShortRestRequested;
    internal System.Action? LongRestRequested;

    internal void EnsureBuilt(PlayTray tray)
    {
        // Feature 6a: the rest controls seat in the board's two rest-notches. PER-BOARD: the
        // diameter/side, the full X/Y/Z offset AND the cap SHAPE (Round disc / Square keycap)
        // come from the active board's config (the debug menu tunes them live) — this REPLACES
        // the old unreliable raycast auto-seating, so the buttons seat at a PREDICTABLE anchor +
        // per-board offset depth. Round-2: an extra per-board SPACING spreads the two apart.
        ControlBoard active = CardsConfig.CurrentBoard;
        float diameter = CardsConfig.RestButtonDiameter(active).Value;
        float thickness = CardsConfig.RoundButtonThickness.Value;
        bool round = CardsConfig.RestButtonShape(active).Value == ButtonShape.Round;
        var size = new Vector2(diameter, diameter); // round overrides from diameter; square uses this side
        // Per-board disc offset: X/Y in the board plane, Z = proud depth toward the player
        // (NEGATIVE = prouder). Replaces the old localPosition(insetX,0,0) + raycast reseat.
        Vector3 offset = CardsConfig.RestButtonOffset(active).Value;
        float spacing = CardsConfig.RestButtonSpacing(active).Value;
        int built = 0;

        if (_shortButton == null && tray.ShortRestAnchor != null)
        {
            _shortButton = PlayTray.BoardButton.Create(tray.ShortRestAnchor, size,
                new Color(0.72f, 0.6f, 0.28f), // warm gold accent when selected
                // No game key exists for a short rest button (GUI_SHORT_REST is absent) — mod string.
                Core.Loc.Mod("short_rest"),
                () => ShortRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round);
            tray.RegisterLaserTarget(_shortButton.Collider!, _shortButton);
            built++;
        }
        if (_longButton == null && tray.LongRestAnchor != null)
        {
            _longButton = PlayTray.BoardButton.Create(tray.LongRestAnchor, size,
                new Color(0.4f, 0.55f, 0.85f), // cool blue accent when selected
                Core.Loc.Game("GUI_LONG_REST", "Long rest"),
                () => LongRestRequested?.Invoke(),
                round: round, diameter: diameter, thickness: thickness, boxy: !round);
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
                $"for {active} (size {diameter:F3} m, thickness {thickness:F3} m, offset {offset}, " +
                $"spacing {spacing:F3} m) — per-board predictable seat, no raycast.");
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
    }

    /// <summary>Refresh availability/selected state + short-button visibility (per frame while the tray shows).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // Item 1: the native "Kurze Rast" widget NO LONGER docks (it undocked/redocked as
        // the game toggled it, flickering against this round button). The round short-rest
        // disc is now the SOLE short-rest control and is ALWAYS shown — no dock gate, no
        // flicker (TrayControlDockSurface.ShortRestDocked is permanently false). Long rest
        // never had a discrete uGUI widget, so it was always mod-drawn.
        _shortButton?.SetVisible(true);

        bool canShort = false, canLong = false, shortSelected = false, longSelected = false;
        if (hand != null)
        {
            canShort = CardsGameApi.CanShortRest(hand);
            canLong = CardsGameApi.CanLongRest(hand);
            shortSelected = CardsGameApi.IsShortRestSelected(hand);
            longSelected = CardsGameApi.IsLongRestSelected(hand);
        }
        _shortButton?.SetState(canShort, accent: shortSelected);
        _longButton?.SetState(canLong, accent: longSelected);
    }
}

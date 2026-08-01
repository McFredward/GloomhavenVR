using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 5 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Region: status (TickStatus).

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ status --

    // Last shown round number (change-gated; int.MinValue = never). Rebuilding the
    // string only on CHANGE avoids a per-frame ToString allocation AND a per-frame TMP
    // text assignment — every rewrite re-triggers TMP's auto-size layout (test #13).
    private int _roundShown = int.MinValue;

    // Confirmed-state label ("✓ <GUI_READY>"), built once — TickStatus runs per
    // frame and the concat would allocate every tick (badge/round lesson, test #13).
    private string? _confirmedLabel;

    // Language the follow/gear labels + cached round/confirmed strings were built in.
    // The TickStatus guard re-localizes them on an actual game-language change (live follow).
    private string _labelLang = string.Empty;

    /// <summary>Update round readout, badge, confirm/undo button states + labels (each frame while visible; cheap).</summary>
    internal void TickStatus(CardsHandUI? hand)
    {
        // ButtonTuning live-apply (user #9): geometry entries rebuild the keycaps in place.
        ApplyButtonTuningIfChanged();

        // Task #2 follow-up: re-assert the slot-dock grab apron every tick (idempotent
        // flag check inside SetDockGrabPad) — a card that entered occupancy while HELD
        // (PlaceCard skips the held card, "the release path homes it") gets its apron
        // the moment it rests in the slot, regardless of which path homed it.
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _occupants[s];
            if (occ != null && !occ.IsHeld)
                occ.SetDockGrabPad(true);
        }

        // Live language following: the follow/gear labels are set at events only and the
        // round/confirmed strings are cached, so on an ACTUAL language change re-label the
        // frame buttons and invalidate the caches (the per-tick logic below re-localizes the
        // round readout + confirm/undo labels via CardsGameApi/Loc.Game). Change-gated so the
        // TMP writes never happen per frame.
        string lang = Core.Loc.CurrentLanguage;
        if (lang != _labelLang)
        {
            _labelLang = lang;
            _confirmedLabel = null;     // rebuild "✓ READY" in the new language
            _roundShown = int.MinValue; // force the round readout to re-localize
            if (_followToggle != null)
                _followToggle.SetLabel(CardsConfig.TrayFollow.Value
                    ? Core.Loc.Mod("follow") : Core.Loc.Mod("pinned"));
        }

        // Round readout (test #18): the PhaseBanner world conversion is GONE — the
        // round number lives on the dashboard instead, read from the same state the
        // banner showed (CardsGameApi.RoundNumber). Change-gated: TMP rewrites
        // re-trigger auto-size layout (the badge flicker lesson, test #13).
        if (_roundLabel != null)
        {
            int round = CardsGameApi.RoundNumber();
            if (round != _roundShown)
            {
                _roundShown = round;
                string text;
                if (round <= 0)
                {
                    text = "-";
                }
                else
                {
                    // The banner's own text: GUI_START_ROUND_BANNER is "Runde {0}"
                    // (PhaseBannerHandler.ShowStartRound). Guard the Format — a
                    // malformed localization must not kill the status tick.
                    try
                    {
                        text = string.Format(
                            Core.Loc.Game("GUI_START_ROUND_BANNER", "Round {0}"), round);
                    }
                    catch (System.FormatException)
                    {
                        text = $"Round {round}";
                    }
                }
                _roundLabel.text = text;
                VRLog.Info("Cards", $"Board: round readout → '{text}'.");
            }
        }

        // Test #24 item 3: the mod-drawn initiative badge over slot 0 is GONE — the
        // current initiative already reads on the docked initiative track (top edge),
        // so the redundant number circle was removed. Physical card swap still swaps
        // initiative (CardsDriver drives it on the slot gesture). We still read the
        // ready state here for the CONFIRM accent below.
        bool ready = hand != null && CardsGameApi.IsSelectionReady(hand);

        // Solo-host card-selection rescue (bug #5b): when hosting online with no other
        // players the game leaves BOTH commit affordances dead (SP ReadyButton
        // deactivated, MP ready toggle forced non-interactable until someone connects),
        // so the round can never advance. Re-run the game's own re-enable each tick while
        // stuck; a no-op in every other case (≥2 players, offline, other phases). Once it
        // flips the toggle interactable the CONFIRM visibility below surfaces the button.
        CardsGameApi.EnsureSoloHostSelectionCommittable();

        // Test #23 item 4: the REAL ReadyButton / UndoButton dock at these same
        // positions when the native-controls surface is active. While a native
        // widget holds, its mod-drawn twin hides (they overlap) and its state mirror
        // is skipped; when it undocks (feature off / widget hidden / no tray) the mod
        // button reappears with its full state logic — never a missing control.
        // Item 7: hide the mod Confirm ONLY when a REAL, VISIBLE native Continue replaces
        // it. In the "all cards of all characters placed" state the native ReadyButton
        // stays docked + interactable while the game drives its canvasGroup alpha to ~0
        // (VR hides the 2D stack) — docked but not rendering. Gating on ContinueDocked
        // alone hid the mod Confirm too, leaving nothing visible yet still pressable
        // (the docked host's raycaster). Gate on docked AND visible so the mod Confirm
        // shows whenever it is the only thing the player can actually see/press; the
        // docked-but-invisible host's raycaster is stood down in TrayControlDockSurface.
        if (_confirm != null
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueDocked
            && WorldUI.Surfaces.TrayControlDockSurface.ContinueVisible)
        {
            _confirm.SetVisible(false);
        }
        else if (_confirm != null && _pickConfirmLabel != null)
        {
            // EVENT-DISCARD pick flow: the driver overrode the CONFIRM keycap — either the
            // game's confirm dialog is open (label = ITS commit option, e.g. "Karten
            // abwerfen"; the press routes to DialogPopup's own callback) or a >2-card
            // requirement has a full batch to lock ("WEITER"). Always shown + accented:
            // this is the flow's ONLY reachable commit affordance in VR (the deadlock fix).
            _confirm.SetVisible(true);
            _confirm.SetState(true, accent: true);
            _confirm.SetLabel(_pickConfirmLabel);
        }
        else if (_confirm != null)
        {
            // Ready-state mirror (test #19): while THIS hand's player has confirmed
            // (online card selection — the only game state where a confirm persists
            // and is revocable, see CardsGameApi.ReadyToggle), the button flips to
            // a distinct gold "✓ …" state; pressing it then REVOKES through the
            // game's own un-ready path (CardsDriver.OnConfirmRequested). Switching
            // the active hand re-reads the state each tick, so the display always
            // tracks the shown character. Offline the game has no confirmed-waiting
            // state (END SELECTION starts the round at once) — normal affordance.
            bool confirmed = hand != null && CardsGameApi.IsConfirmed(hand);
            bool canConfirm = hand != null
                              && (CardsGameApi.CanConfirm() || CardsGameApi.ReadyToggleAvailable());
            // Item 7 ("wenn es nicht drückbar ist dann soll es dort auch nicht erscheinen"):
            // only SHOW confirm when its action is actually possible right now — a valid
            // confirmable selection exists (canConfirm) or the player has confirmed and can
            // revoke (confirmed). Otherwise HIDE it (not merely disable), so an unpressable
            // button never appears; it returns the instant the action becomes possible.
            bool show = canConfirm || confirmed;
            _confirm.SetVisible(show);
            if (show)
            {
                // Pick flows (test #28): during a single-card pick, CONFIRM is the mode's
                // mirrored confirm affordance (the ReadyButton path the recover flows arm) —
                // accent it as soon as the game reports it fireable.
                _confirm.SetState(true,
                    accent: (ready || _pickActive) && canConfirm && !confirmed, confirmed: confirmed);
                _confirm.SetLabel(confirmed
                    ? _confirmedLabel ??= "✓ " + Core.Loc.Game("GUI_READY", "READY")
                    : CardsGameApi.ReadyToggleAvailable() && !CardsGameApi.CanConfirm()
                        ? Core.Loc.Game("GUI_END_SELECTION", "END SELECTION")
                        : CardsGameApi.ConfirmLabel());
            }
        }
        if (_undo != null && WorldUI.Surfaces.TrayControlDockSurface.UndoDocked)
        {
            _undo.SetVisible(false);
        }
        else if (_undo != null && _pickUndoLabel != null)
        {
            // EVENT-DISCARD pick flow: UNDO becomes the confirm dialog's own cancel
            // ("Wähle eine andere Karte") while it is open — the reachable stand-in for
            // the 2D popup's second option.
            _undo.SetVisible(true);
            _undo.SetState(true, accent: false);
            _undo.SetLabel(_pickUndoLabel);
        }
        else if (_undo != null)
        {
            // Item 7: only SHOW undo when there is actually something to undo — hide it
            // (not just disable) otherwise; it reappears the instant an undo is available.
            bool canUndo = hand != null && CardsGameApi.CanUndo();
            _undo.SetVisible(canUndo);
            if (canUndo)
            {
                _undo.SetState(true, accent: false);
                _undo.SetLabel(CardsGameApi.UndoLabel());
            }
        }
    }

    // -------------------------------------------------- pick banner + keycap overrides --

    /// <summary>
    /// EVENT-DISCARD VR FLOW: driver-pushed pick status. <paramref name="banner"/> is the
    /// progress line shown on the hovering placard above the board's top edge (null hides
    /// it); <paramref name="confirmLabel"/>/<paramref name="undoLabel"/> override the
    /// CONFIRM/UNDO keycap labels while a pick confirm affordance is live (null returns
    /// each keycap to its normal game-state logic in <see cref="TickStatus"/>). The
    /// banner text write is change-gated (TMP rewrites re-trigger auto-size layout —
    /// the badge/round lesson, test #13); the label overrides are plain field stores
    /// consumed by the per-tick keycap logic.
    /// </summary>
    internal void SetPickStatus(string? banner, string? confirmLabel, string? undoLabel)
    {
        _pickConfirmLabel = confirmLabel;
        _pickUndoLabel = undoLabel;
        if (banner == _pickBannerText)
            return;
        _pickBannerText = banner;
        if (banner == null)
        {
            if (_pickBannerRoot != null && _pickBannerRoot.activeSelf)
                _pickBannerRoot.SetActive(false);
            return;
        }
        EnsurePickBanner();
        if (_pickBannerLabel != null)
            _pickBannerLabel.text = banner;
        if (_pickBannerRoot != null && !_pickBannerRoot.activeSelf)
            _pickBannerRoot.SetActive(true);
        VRLog.Info("Cards", $"Pick banner: \"{banner}\".");
    }

    /// <summary>
    /// Build the pick progress placard lazily: a parchment strip hovering ABOVE the
    /// board's top/back edge (clear of the docked initiative track at y≈0.10 and the
    /// round readout), facing the player like every board element (_boardFaceFrame).
    /// Same collider-free plate+TMP construction as the round readout / EmptyFanHint —
    /// purely visual, mod layer only; child of the tray root so a board switch destroys
    /// and re-creates it with the board.
    /// </summary>
    private void EnsurePickBanner()
    {
        if (_pickBannerRoot != null || _root == null)
            return;

        _pickBannerRoot = new GameObject("PickStatusBanner");
        _pickBannerRoot.transform.SetParent(_root, worldPositionStays: false);
        _pickBannerRoot.transform.localPosition = new Vector3(0f, BoardH * 0.5f + 0.10f, -0.02f);
        _pickBannerRoot.transform.localRotation = _boardFaceFrame;
        Core.VRLayers.Apply(_pickBannerRoot);

        var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
        plate.name = "Plate";
        Object.Destroy(plate.GetComponent<Collider>());
        plate.transform.SetParent(_pickBannerRoot.transform, worldPositionStays: false);
        plate.transform.localScale = new Vector3(0.44f, 0.055f, 1f);
        plate.transform.localPosition = new Vector3(0f, 0f, 0.004f); // behind the text, toward the board
        Core.VRLayers.Apply(plate);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            var mat = new Material(shader) { color = new Color(0.85f, 0.78f, 0.62f, 0.85f) }; // parchment
            plate.GetComponent<MeshRenderer>().sharedMaterial = mat;
            WorldUI.MrBacking.Opacify(mat); // 0.85 parchment lets the room shimmer through in MR
        }

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(_pickBannerRoot.transform, worldPositionStays: false);
        Core.VRLayers.Apply(textGo);
        _pickBannerLabel = textGo.AddComponent<TextMeshPro>();
        _pickBannerLabel.alignment = TextAlignmentOptions.Center;
        _pickBannerLabel.color = new Color(0.24f, 0.17f, 0.10f); // ink brown on parchment
        Core.TmpFit.Fit(_pickBannerLabel, 0.42f, 0.048f, maxFontSize: 0.30f, wrap: true);

        _pickBannerRoot.SetActive(false);
    }
}

using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Round number / initiative / rest — GLOBAL + PER-ACTOR MODEL
// =================================================================================================

/// <summary>
/// The three small readouts on a peer's board frame:
///
/// ROUND (GLOBAL) — "Runde N" on the top-right, the mirror of the local board's own round readout
/// (<c>PlayTray.BuildRoundReadout</c>) reading the SAME state through
/// <c>CardsGameApi.RoundNumber()</c> (<c>Choreographer.m_CurrentState.RoundNumber</c>) and the SAME
/// <c>GUI_START_ROUND_BANNER</c> localization. Scenario-wide: no wire, no secret.
///
/// INITIATIVE (PER-ACTOR MODEL, gated) — the peer's initiative number. This is what their docked
/// initiative track shows for them, and the anti-cheat rule is copied verbatim from the vanilla
/// widget: <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c> — which is precisely <see cref="RevealGate.ShowRoundCardFronts"/>.
/// So this shows "?" in exactly the frames vanilla shows "?", and the real number in exactly the
/// frames vanilla already shows it on the shared initiative track. No new information exists.
///
/// REST (PER-ACTOR MODEL, split gate) — <c>CCharacterClass.HasShortRested</c> /
/// <c>HasLongRested</c> are PAST-TENSE facts about a rest that already resolved in front of
/// everybody, so they are shown unconditionally. <c>CCharacterClass.LongRest</c> is the PENDING
/// long-rest SELECTION and is therefore secret during the selection phase — vanilla gates its own
/// display of it on the same condition (<c>InitiativeTrackActorAvatar</c>: the long-rest branch is
/// inside the <c>flag</c> = not-hidden test), so this one goes through
/// <see cref="RevealGate.ShowRoundCardFronts"/> as well.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (GLOBAL + PER-ACTOR MODEL) — ZERO wire either way. The ROUND
/// number is GLOBAL (<c>CardsGameApi.RoundNumber()</c>); the INITIATIVE number and the REST state
/// are PER-ACTOR MODEL, read off the host-replicated <c>CPlayerActor.CharacterClass</c> via
/// <c>NetPlayerActors.ActorFor</c> and gated by <see cref="RevealGate"/> (rest uses the SPLIT gate
/// described above). One of the three genuinely MIXED types — which is why the classification is a
/// doc tag and not a marker interface: an interface would have to lie about this one. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteStatusReadouts
{
    private readonly TextMeshPro _round;
    private readonly TextMeshPro _initiative;
    private readonly TextMeshPro _rest;
    private readonly Transform _restRoot;
    private readonly Transform _iniRoot;

    private int _roundShown = int.MinValue;
    private string _langShown = string.Empty;

    /// <summary>The round label's preferred font size — VERBATIM the owner's own literal
    /// (<c>Cards.PlayTray.BuildRoundReadout</c> passes 0.32f to the same <c>Core.TmpFit.Fit</c>).
    /// Named rather than repeated so the next person who changes one changes both, and so the
    /// grep for "0.32" lands on a constant with this doc on it.</summary>
    private const float RoundLabelMaxFont = 0.32f;

    /// <summary>Last rendered values, for the change-gated diagnostic line.</summary>
    public string InitiativeText { get; private set; } = "?";
    public string RestText { get; private set; } = string.Empty;
    public string RoundText { get; private set; } = "-";

    public RemoteStatusReadouts(Transform boardRoot, in RemoteBoardLayout layout)
    {
        // --- round: top-right, at the OWNER's own seat — PlayTray.ReadoutBase plus the AUTHORED
        //     per-board ReadoutOffset, keyed by the peer's synced style (RemoteBoardLayout). The
        //     old hardcoded (0.235, 0.125, −0.004) dropped the per-board term, which on the Steel
        //     board of the hardware session put "Runde N" 40 mm right, 22 mm low and 44 mm behind
        //     where the owner has it — part of defect (c) of the 1:1-parity round.
        var roundRoot = new GameObject("RoundReadout").transform;
        roundRoot.SetParent(boardRoot, worldPositionStays: false);
        roundRoot.localPosition = layout.ReadoutMount;
        // THE BACKING PLATE — defect (c) of this round ("die Runden-Anzeige sitzt auf einem grauen
        // Kasten, den der Besitzer nicht hat").
        //
        // The owner HAS a plate here (PlayTray.BuildRoundReadout builds one at the same 0.13 x
        // 0.036 m) — but theirs is LIT (Tint → Standard) and seated 6 mm INTO the board behind the
        // label, so under the scenario's own lighting it reads as a shadow on the board and the
        // gold text simply floats on the wood. This copy was an UNLIT Sprites/Default quad at the
        // same dark RGB, and an unlit quad ignores the scene entirely: it renders that colour at
        // full brightness, flat and matte, next to a lit and shadowed board — the "grey box" in
        // the screenshot. Parity here is not "remove the plate", it is "build the owner's plate":
        // the same lit shader ladder the board's own furniture uses and the same 6 mm seat, so
        // whatever the owner sees at their round readout is exactly what a peer sees at theirs.
        BoardVisual.Quad(roundRoot, "Plate", new Vector2(0.13f, 0.036f),
            RemoteBoardContent.BoardLit(new Color(0.12f, 0.11f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.006f);
        // THE GLYPH SIZE IS THE OWNER'S OWN NUMBER, NOT A SECOND ONE. User report 2026-08-13,
        // verbatim: "Die Rundenanzeige beim remote board ist unter Umständen super klein und
        // skalliert nicht richtig. Auf dem eigenen board ist alles ok."
        //
        // ROOT CAUSE — one literal, twice, and the two disagreed. The plate (0.13 x 0.036), the
        // rect (0.12 x 0.028) and the seat (layout.ReadoutMount = PlayTray.ReadoutBase + the
        // owner's tuned ReadoutOffset off record 28) were already identical to
        // PlayTray.BuildRoundReadout. Only the maxFontSize handed to TmpFit differed: the owner
        // passes 0.32 (TmpFit clamps it to the height cap 0.028 x 6.5 = 0.182), this copy passed
        // 0.06. Two separate defects fall out of that one number:
        //   • 0.182 / 0.06 = 3.03x — the mirrored "Runde N" was a third of the owner's height at
        //     every board scale, which is the "super klein";
        //   • 0.06 is BELOW TmpFit.MinFontSize (0.08), so the auto-size band came out INVERTED
        //     (fontSizeMax < fontSizeMin). TMP's grow and shrink branches are both gated on that
        //     band, so the label stopped responding to its box at all and its rendered size
        //     depended on which side of the quantiser the string landed — which is exactly the
        //     "unter Umständen" and "skalliert nicht richtig".
        // The fix is to pass the OWNER'S literal, so there is one number rather than two agreeing.
        // There is no round-readout SIZE dial anywhere (config, record 28, BoardTunePages) — this
        // was never a tuning that failed to travel, so no wire field is needed or added.
        _round = RemoteBoardContent.Label(roundRoot, "Text", Vector3.zero,
            new Vector2(0.12f, 0.028f), RoundLabelMaxFont,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center);
        RemoteBoardContent.SetText(_round, "-");
        Core.VRLog.Info("Net", "ROUND MIRROR: round readout label fitted at maxFont " +
                               $"{RoundLabelMaxFont:F2} (the owner's own PlayTray.BuildRoundReadout " +
                               $"literal) into a {0.12f:F3} x {0.028f:F3} m rect — TmpFit clamps it " +
                               $"to the height cap {0.028f * 6.5f:F3}, which is above the 0.08 " +
                               "auto-size floor, so the band is valid and the glyphs are the same " +
                               "size the owner reads. Was 0.06: 3.03x too small AND below the " +
                               "floor (inverted band = the 'skalliert nicht richtig').");

        // --- initiative: top-centre, in the strip between the round-card tops (y 0.104) and the
        //     board's top edge (y 0.16) — where the local board's docked initiative track sits.
        //     REMOTE-ONLY BY CONSTRUCTION: the owner's own board has no such badge (their number is
        //     on the docked initiative track), so it is a stand-in that only earns its place while
        //     the real track cannot be shown — see SetShownWhileTrackFallback.
        _iniRoot = new GameObject("InitiativeReadout").transform;
        Transform iniRoot = _iniRoot;
        iniRoot.SetParent(boardRoot, worldPositionStays: false);
        iniRoot.localPosition = new Vector3(0f, 0.132f, RemoteControlBoard.ProudZLocal);
        BoardVisual.Quad(iniRoot, "Plate", new Vector2(0.11f, 0.042f),
            BoardVisual.Unlit(new Color(0.12f, 0.11f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        RemoteBoardContent.Label(iniRoot, "Caption", new Vector3(-0.035f, 0f, 0f),
            new Vector2(0.034f, 0.026f), 0.035f,
            new Color(0.75f, 0.70f, 0.60f), TextAlignmentOptions.Center).text = "INI";
        _initiative = RemoteBoardContent.Label(iniRoot, "Value", new Vector3(0.016f, 0f, 0f),
            new Vector2(0.060f, 0.034f), 0.075f,
            new Color(1f, 0.93f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
        RemoteBoardContent.SetText(_initiative, "?");

        // --- rest: bottom-left, the local board's rest zone. Collision budget (board-local, the
        //     same arithmetic PlayTray.BuildMounts documents for its own furniture): the plate spans
        //     x −0.295..−0.105 and y −0.146..−0.110, so it clears the round-card slot above it
        //     (slot 0 bottom edge y −0.104) by 6 mm and stays inside the board (bottom edge −0.16).
        _restRoot = new GameObject("RestReadout").transform;
        _restRoot.SetParent(boardRoot, worldPositionStays: false);
        _restRoot.localPosition = new Vector3(-0.20f, -0.128f, RemoteControlBoard.ProudZLocal);
        BoardVisual.Quad(_restRoot, "Plate", new Vector2(0.19f, 0.036f),
            BoardVisual.Unlit(new Color(0.14f, 0.12f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        _rest = RemoteBoardContent.Label(_restRoot, "Text", Vector3.zero,
            new Vector2(0.18f, 0.028f), 0.05f,
            new Color(0.95f, 0.86f, 0.62f), TextAlignmentOptions.Center, FontStyles.Bold);
        _restRoot.gameObject.SetActive(false);
    }

    /// <summary>
    /// Show or hide the mod's own "INI" badge. It is a REMOTE-ONLY stand-in: the owner's board does
    /// not carry one — their initiative is on the docked initiative TRACK — so as soon as
    /// <see cref="RemoteInitiativeTrack"/> is mirroring the real track (which shows this player's
    /// number in the same place and under the same vanilla gate), the badge stops being parity and
    /// starts being an extra widget the owner does not have. It comes back the moment the track
    /// falls back to the mod-drawn chip strip, so the number is never simply lost.
    /// </summary>
    public void SetShownWhileTrackFallback(bool shown)
    {
        if (_iniRoot != null && _iniRoot.gameObject.activeSelf != shown)
            _iniRoot.gameObject.SetActive(shown);
    }

    /// <summary>Re-read round / initiative / rest. <paramref name="showFronts"/> is the shared
    /// <see cref="RevealGate"/> answer for this actor.</summary>
    public void Refresh(CPlayerActor actor, bool showFronts)
    {
        // Language change invalidates the cached round string (the local board does the same).
        string lang = Loc.CurrentLanguage;
        if (lang != _langShown)
        {
            _langShown = lang;
            _roundShown = int.MinValue;
        }

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
                try { text = string.Format(Loc.Game("GUI_START_ROUND_BANNER", "Round {0}"), round); }
                catch (System.FormatException) { text = $"Round {round}"; }
            }
            RoundText = text;
            RemoteBoardContent.SetText(_round, text);
        }

        // Initiative — vanilla's own rule, verbatim (see the class note).
        string ini = "?";
        if (showFronts)
        {
            int value = 0;
            try { value = actor.Initiative(); }
            catch { value = 0; }
            if (value != 0)
                ini = value.ToString();
        }
        InitiativeText = ini;
        RemoteBoardContent.SetText(_initiative, ini);

        // Rest — past-tense facts always, the pending long-rest SELECTION only once revealed.
        CCharacterClass cc = actor.CharacterClass;
        string rest = string.Empty;
        if (cc != null)
        {
            if (cc.HasLongRested)
                rest = Loc.Game("GUI_LONG_REST", "Long rest");
            else if (cc.HasShortRested)
                rest = Loc.Mod("short_rest");
            else if (showFronts && cc.LongRest)
                rest = Loc.Game("GUI_LONG_REST", "Long rest");
        }
        RestText = rest;
        bool show = rest.Length > 0;
        if (_restRoot.gameObject.activeSelf != show)
            _restRoot.gameObject.SetActive(show);
        if (show)
            RemoteBoardContent.SetText(_rest, rest.ToUpperInvariant());
    }
}

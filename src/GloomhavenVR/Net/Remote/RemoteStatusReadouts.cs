using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Round number / initiative — GLOBAL + PER-ACTOR MODEL
// =================================================================================================

/// <summary>
/// ModBuild 486: native widgets are the sole presentation. The historical procedural composition
/// below is retained only as uncalled diagnostic/source history; it is not an availability fallback.
/// The remote-only initiative badge is likewise permanently hidden.
///
/// The two small readouts on a peer's board frame:
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
/// REST — THE PLATE IS GONE, AND IT MAY NOT COME BACK. This class used to draw a third readout:
/// a 0.19 x 0.036 m plate at the board's bottom-left carrying a bold "SHORT REST" / "LONG REST",
/// fed by <c>CCharacterClass.HasShortRested</c> / <c>HasLongRested</c> (past-tense facts, shown
/// unconditionally) and by the pending <c>LongRest</c> SELECTION behind
/// <see cref="RevealGate.ShowRoundCardFronts"/>. The GATING was sound. The WIDGET was not: THE
/// OWNER HAS NO SUCH PLATE. Grepping those three members across <c>Cards/</c> and <c>WorldUI/</c>
/// finds no owner-side readout at all, because the local board states a rest through its rest DISC
/// CAPS — and this mirror already reproduces their four states off the synced cap byte
/// (<c>RemoteBoardFurniture.ApplyCapStates</c>), so the information was never missing here either.
/// It was therefore a widget every peer could see and the owner could not, which is exactly what
/// <c>RemoteBoardFurniture.BuildHalfDivider</c> was deleted for, and it goes the same way: no wire
/// field was owed and none could have helped, because there is no owner-side state to gate it on.
/// Deleted rather than gated. Zero wire either way.
///
/// The "INI" badge below survives the same question only because it is a STAND-IN for a widget the
/// owner DOES have — their docked initiative track — and takes itself off the board the moment that
/// track is really being mirrored (<see cref="SetShownWhileTrackFallback"/>). A rest plate had no
/// such counterpart to stand in for.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (GLOBAL + PER-ACTOR MODEL) — ZERO wire either way. The ROUND
/// number is GLOBAL (<c>CardsGameApi.RoundNumber()</c>); the INITIATIVE number is PER-ACTOR MODEL,
/// read off the host-replicated <c>CPlayerActor</c> via <c>NetPlayerActors.ActorFor</c> and gated
/// by <see cref="RevealGate"/>. One of the three genuinely MIXED types — which is why the
/// classification is a doc tag and not a marker interface: an interface would have to lie about
/// this one. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteStatusReadouts
{
    private readonly TextMeshPro _round;
    private readonly TextMeshPro _initiative;
    private readonly Transform _iniRoot;

    private int _roundShown = int.MinValue;
    private string _langShown = string.Empty;

    /// <summary>The peer's synced board style — which board's material the round number is cut
    /// into. Off <c>RemoteBoardLayout.Style</c>, i.e. record 28, which already carries it because
    /// the board PREFAB is chosen from it. No new wire field, and no possibility of an oak number
    /// on a bronze board.</summary>
    private readonly ControlBoard _style;

    /// <summary>The round label's preferred font size — VERBATIM the owner's own literal
    /// (<c>Cards.PlayTray.BuildRoundReadout</c> passes 0.32f to the same <c>Core.TmpFit.Fit</c>).
    /// Named rather than repeated so the next person who changes one changes both, and so the
    /// grep for "0.32" lands on a constant with this doc on it.</summary>
    private const float RoundLabelMaxFont = 0.32f;

    /// <summary>Last rendered values, for the change-gated diagnostic line.</summary>
    public string InitiativeText { get; private set; } = "?";
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
        // THE BACKING PLATE IS GONE ON BOTH BOARDS — and how that happened is worth keeping,
        // because it is the second correction to the same widget and the first one was right at
        // the time.
        //
        // Defect (c) of the 1:1 round was "die Runden-Anzeige sitzt auf einem grauen Kasten, den
        // der Besitzer nicht hat". The owner DID have a plate; theirs was LIT and seated 6 mm into
        // the board, so it read as a shadow, while this copy was an UNLIT quad at the same RGB,
        // which renders that colour flat and at full brightness beside a lit board. Parity then
        // was not "remove the plate", it was "build the owner's plate", and that is what shipped.
        //
        // The user has now ruled on the plate itself, naming the round text: "es nativ und
        // immersiv in dem board verarbeitet ist, nicht einfach als schwebender Text darüber … Das
        // gilt übrigens auch für den Rundentext." So the plate is deleted from the OWNER's board
        // (PlayTray.BuildRoundReadout) and from this mirror of it in one change, and the number is
        // CUT INTO the board instead. Nothing is drawn behind the glyphs any more on either board,
        // so the two can no longer disagree about what that something looks like.
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
        //   • 0.06 was BELOW TmpFit.MinFontSize (0.08 at the time), so the auto-size band came out
        //     INVERTED (fontSizeMax < fontSizeMin). TMP's grow and shrink branches are both gated
        //     on that band, so the label stopped responding to its box at all and its rendered size
        //     depended on which side of the quantiser the string landed — which is exactly the
        //     "unter Umständen" and "skalliert nicht richtig".
        //     THAT SECOND DEFECT IS NOW STRUCTURALLY IMPOSSIBLE (2026-08-25): TmpFit clamps
        //     fontSizeMin to fontSizeMax, so no caller can invert the band, and the floor itself
        //     dropped from 0.08 to 0.045 with the caption-truncation fix. The FIRST defect — one
        //     literal written twice — is still why this passes the owner's number rather than its
        //     own, and it is the reason that matters.
        // The fix is to pass the OWNER'S literal, so there is one number rather than two agreeing.
        // There is no round-readout SIZE dial anywhere (config, record 28, BoardTunePages) — this
        // was never a tuning that failed to travel, so no wire field is needed or added.
        _style = layout.Style;
        _round = Cards.BoardEngraving.Create(roundRoot, "RoundText", Vector3.zero,
            new Vector2(0.12f, 0.028f), RoundLabelMaxFont, _style);
        RemoteBoardContent.SetText(_round, "-");
        Core.VRLog.Info("Net", "ROUND MIRROR: round readout label fitted at maxFont " +
                               $"{RoundLabelMaxFont:F2} (the owner's own PlayTray.BuildRoundReadout " +
                               $"literal) into a {0.12f:F3} x {0.028f:F3} m rect — TmpFit clamps it " +
                               $"to the height cap {0.028f * 6.5f:F3}, which is above the " +
                               $"{Core.TmpFit.MinFontSize:F3} auto-size floor, so the band is valid " +
                               "and the glyphs are the same size the owner reads. Was 0.06: 3.03x " +
                               "too small AND below the then-0.08 floor (inverted band = the " +
                               "'skalliert nicht richtig'; TmpFit clamps min to max now, so that " +
                               "half cannot recur).");

        // --- initiative: top-centre, in the strip between the round-card tops (y 0.104) and the
        //     board's top edge (y 0.16) — where the local board's docked initiative track sits.
        //     REMOTE-ONLY BY CONSTRUCTION: the owner's own board has no such badge (their number is
        //     on the docked initiative track), so it is a stand-in that only earns its place while
        //     the real track cannot be shown — see SetShownWhileTrackFallback.
        _iniRoot = new GameObject("InitiativeReadout").transform;
        _iniRoot.gameObject.SetActive(false); // retired surrogate: never visible, including construction failure
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

        // NO REST READOUT IS BUILT HERE — see the class note. The bottom-left plate that used to
        // stand at (−0.20, −0.128) was the one widget on this board with no owner-side original,
        // and a rest already reads off the mirrored rest disc caps.
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

    /// <summary>Re-read round / initiative. <paramref name="showFronts"/> is the shared
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
            // The HUD font is harvested off a live game widget and can arrive AFTER this label was
            // built, which would leave the carve unstyled on a board built early in a session.
            // Re-applying on the change-gated path is cheap and idempotent, and it is the same
            // late-font ladder the keycap labels already ride.
            Cards.BoardEngraving.Restyle(_round, _style);
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
    }
}

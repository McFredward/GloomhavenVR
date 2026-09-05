using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic ghost of a remote player's ITEM fan (<see cref="ItemsPile"/>) — the counterpart of
/// <see cref="RemoteHandFan"/> for equipped item cards.
///
/// ROOT CAUSE this exists (user report 5, multiplayer half): the extras packet only ever carried
/// the ABILITY hand-card count, so when a peer raised their item fan — a very visible, near-square
/// arc of cards floating over their palm or their board — every other player saw absolutely
/// nothing. The item fan is now broadcast as an additive count + a held/board-anchored flag
/// (<see cref="NetProtocol.FlagItemFan"/>), and this renders it.
///
/// ANTI-CHEAT / bandwidth: no item identity, art or state ever rides the WIRE — a peer transmits how
/// many item cards are up and where the fan is, nothing more. Item cards are near-square rather than
/// 63.5×88, so the slab uses its own dimensions; the real PER-CARD face size is deliberately NOT
/// transmitted.
///
/// <para>THAT DECISION WAS RE-EXAMINED against the 2026-08-08 1:1 ruling and DELIBERATELY KEPT, so
/// here is the reasoning rather than an assertion. The owner's item chip sizes itself from the REAL
/// hosted <c>ItemCardUI</c> rect (<c>ItemsPile.ItemChip.FaceWidth/FaceHeight</c>) — a
/// PER-CARD measurement of a game widget, in units of their <c>[Cards] CardWidth</c>. Two of those
/// three inputs already reach this client without a byte: the widget is the game's own prefab
/// (identical on every machine, and this fan already hosts it through <see cref="RemotePileFronts"/>
/// off the peer's host-replicated <c>CInventory.AllItems</c>), and CardWidth now rides extension
/// record 28. What is left is the residual per-card aspect, and buying it would cost a length-
/// prefixed list of up to 12 sizes — ~25 bytes on EVERY packet while a fan is open, more than the
/// whole board-tuning record's typical cost — to correct a difference the receiver can measure for
/// itself from the same widget it is already drawing. A record that pays 25 B/packet for a number
/// the receiver holds locally is the "second source of truth" the tooltip's own frame-metrics note
/// rejects. If the near-square constants below are ever seen to disagree with a real item on
/// hardware, the fix is to measure the hosted face here — not to transmit it.</para>
///
/// <para>THAT REASONING WAS SOUND AND ONE OF ITS TWO PREMISES WAS NOT BEING HONOURED (2026-08-27).
/// It says the peer's CardWidth "already reaches this client without a byte" over record 28 — true
/// of the WIRE, and this renderer was not reading it: the chip box was a bare
/// <c>const CardW = 0.075f</c>, 5.5 % under the owner's own <c>CardWidth × ChipScale</c> at the
/// shipped defaults and wrong by their whole tuning range once they had moved the dial. It is
/// <see cref="_cardWidth"/> now; the aspect argument above is unchanged and still stands.</para>
///
/// CARD FRONTS (user ruling 2026-08-08, "Die Oberseiten der Karten des remote Spielers soll auch
/// überall sichtbar sein … NUR in der Auswahlphase sieht man überall nur die Rückseiten"): this arc
/// used to be BACKS UNCONDITIONALLY. That made the secrecy rule a PLACE rule, in direct conflict with
/// the PHASE rule the round-card slots and the hand fan already obeyed. Each slab now carries the REAL
/// item card face whenever <see cref="RevealGate.ShowRoundCardFronts"/> is open for the displayed
/// actor, drawn by <see cref="RemotePileFronts"/> from that peer's own host-replicated
/// <c>CInventory.AllItems</c> — the identical list the LOCAL <see cref="ItemsPile"/> reads. Zero new
/// wire bytes: the item identities were already on this client, they were simply never drawn.
///
/// THE CARD IN THE USE RECESS (extension record 26, 2026-08-09): one of these slabs may be lying in
/// the owner's item-USE recess rather than standing in the arc. WHICH one arrives as a bare arc
/// INDEX and the slab is re-parented onto <see cref="RemoteBoardFurniture.ItemUseRecess"/>, so it
/// lies flat IN the recess and rides that board — never billboarded at a head. The 0.28 s arrival
/// settle and the glide back out are replayed from the index's own EDGES on the local clock, like
/// the fan's deal-out and fold-in; see the field block below for the whole argument.
///
/// PLACEMENT mirrors the local fan's two modes: HAND-HELD → floating a palm standoff above the
/// sender's DOMINANT hand (the hand that pinch-grabbed the item stack); BOARD-ANCHORED → floating
/// above the sender's synced control board at the same shared board-top spot the local fan uses.
/// Faces away from the owner's head so the owner's side reads as the "front" and everyone else sees
/// backs — the same convention as every other remote card visual.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY (the fan's existence, size and placement) + PER-ACTOR MODEL (its
/// card faces) + WIRE (extension record 28, via <see cref="RemoteBoardTuning"/> — the owner's own
/// open/close ANIMATION dials, and only the ones they have actually moved) — costs wire bytes: extras
/// <c>FlagItemFan</c> + one count byte, plus the two pure flags
/// <c>FlagItemFanHeld</c> / <c>FlagItemFanLeft</c> (0 B each, and both INERT — the whole-fan grab is
/// gone), plus extension record 26 (ONE index byte, and only while a card really lies in the owner's
/// item-use recess). Item IDENTITY and per-item face size
/// are DELIBERATELY-NOT transmitted; the FACES are resolved locally off the host-replicated
/// <c>Inventory.AllItems</c> behind <see cref="RevealGate"/> (<see cref="RemotePileFronts"/>). The
/// item's real effect syncs authoritatively through <c>UseItemService</c>, not through here. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteItemFan
{
    // ---- geometry (mirror of ItemsPile's arc constants) --------------------------------------
    private const int MaxCards = 12;
    /// <summary>
    /// LEGACY chip box width — what this fan cut its slabs to before the owner's own
    /// <c>[Cards] CardWidth</c> reached it, kept ONLY as documentation of the defect.
    ///
    /// <para>IT WAS WRONG AND IT WAS WRONG AT THE DEFAULTS, exactly as
    /// <c>RemoteActiveCards.LegacyCardW</c> was. The owner's chip is the item card's face fitted
    /// into <c>[Cards] CardWidth</c> × <c>CardHeight</c> and then stood in the arc at
    /// <see cref="ChipScale"/> (<c>ItemsPile.ItemChip.Create</c> → <c>SetHome(pos, rot, ChipScale)</c>),
    /// i.e. <c>0.0635 × 1.25 = 79.4 mm</c> wide at the shipped default. This constant is 75.0 mm —
    /// <b>5.5 % small on every peer's board with nobody having tuned anything</b>, and wrong by the
    /// owner's whole tuning range once they had. It survived every checker for the reason
    /// RemoteActiveCards records: the coverage guard watches DIALS, and it had no way to see that
    /// the metric underneath the dials was a bare constant.</para>
    /// </summary>
    private const float LegacyCardW = 0.075f;

    /// <summary>The pre-measurement aspect guess (<c>box height = width × 1.15</c>) — a card TALLER
    /// than it is wide, which a Gloomhaven item card is not. Superseded at runtime by the applied
    /// Item footprint; see <see cref="ResolvedCardH"/> and the <see cref="_cardH"/> block comment,
    /// which is where this number's whole story lives.</summary>
    private const float LegacyAspect = 1.15f;

    /// <summary>The OWNER's own <c>[Cards] CardWidth</c> (extension record 28, id 70) — the metric
    /// their item chips are fitted into before <see cref="ChipScale"/>. Refreshed by
    /// <see cref="SyncTuning"/>; the INITIALISER is the shipped default, which is what an untuned or
    /// pre-record peer's fan is still drawn with (held against <c>[Cards] CardWidth</c> by
    /// scripts/check-remote-defaults.py).</summary>
    private float _cardWidth = Defaults.CardWidth;

    /// <summary>The chip BOX the slab bodies are cut to: the owner's card width taken up to the arc
    /// by <see cref="ChipScale"/>, so a slab at <c>localScale = 1</c> is exactly the size their chip
    /// stands in the arc at. (Folding ChipScale into the BOX rather than into the slab transform is
    /// deliberate: every scale in this file — the emerge seed, the settle overshoot, the recess fit,
    /// the collapse — is expressed against a base of 1, and a base of 1.25 would have to be threaded
    /// through all four.)</summary>
    private float ChipBoxW => _cardWidth * ChipScale;

    /// <summary>The chip box HEIGHT this fan degrades to while the item footprint is unknown — the
    /// historical <see cref="LegacyAspect"/> guess against <see cref="ChipBoxW"/>, so a cold cache
    /// draws the same rounded slab it always did, at the corrected width.</summary>
    private float ChipBoxHFallback => ChipBoxW * LegacyAspect;

    private const float MaxArcDegrees = 110f;            // ItemsPile.MaxArcDegrees
    private const float ZStagger = 0.004f;               // ItemsPile.ZStagger (draw order)
    /// <summary>
    /// THE HAND-HELD FAN'S PALM STANDOFF, and it is a FROZEN HISTORICAL NUMBER, not a mirror.
    ///
    /// <para>It used to cite <c>ItemsPile.HandPalmOffset</c>. THAT CONSTANT NO LONGER EXISTS: commit
    /// 15286350 deleted it along with the whole-fan trigger grab (user rulings 2026-08-02 for the
    /// item fan, 2026-08-06 for the browse fan), and the owner's fan has had exactly one anchoring
    /// since — board-anchored, head-relative only when no board exists. So there is nothing left on
    /// the owner's side for this to follow, and pointing it at the nearest surviving dial would be
    /// worse than the dead citation: <c>[Cards] FanPalmOffset</c> (0.09 m shipped) is the HAND
    /// fan's standoff, a different control at a different number, and adopting it is exactly the
    /// "wrong entry" mistake <c>scripts/check-remote-defaults.py</c> exists to catch.</para>
    ///
    /// <para>WHY THE BRANCH SURVIVES AT ALL. <c>ItemsPile.IsHandHeld</c> is hardcoded <c>false</c> and
    /// is kept as a property precisely because it is a WIRE SEAM — <c>NetAvatarDriver</c> fills the
    /// extras field from it and the packet layout must not shift — so the bit is on the wire, always
    /// clear, and this branch is the receiver's half of that seam. It is INERT today. Deleting the
    /// number without deleting the branch is what would leave the trap: a reader "fixing" this by
    /// re-syncing to a constant that is gone, or to the hand fan's.</para>
    /// </summary>
    private const float HandPalmOffset = 0.16f;         // FROZEN: the owner's constant is gone
    private const float BoardFloatHeight = 0.26f;        // ItemsPile.BoardFloatHeight
    private const float BoardFloatProudZ = -0.05f;       // ItemsPile.BoardFloatProudZ
    private const float Smoothing = 14f;

    private readonly RemoteAvatar _owner;
    private GameObject? _root;
    private readonly List<GameObject> _cards = new(MaxCards);

    /// <summary>The FRONT layer over those slabs (user ruling 2026-08-08) — one overlay per slab,
    /// gated on <see cref="RevealGate.ShowRoundCardFronts"/> and fed from the peer's own replicated
    /// inventory. Owned here, destroyed with the fan.</summary>
    private readonly RemotePileFronts _fronts;

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  THE ITEM CARD'S REAL SHAPE — why this fan showed "Ränder" where the owner's chips do not
    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //
    // USER REPORT (MP hardware round, ModBuild 137, verbatim): "Die Gegenstand-Karten im Fächer vom
    // Mitspieler auf dem remote-board haben noch Ränder statt richtig ausgeschnitten zu sein! Sie
    // sollen genau so für mich sichtbar sein wie für den Mitspieler (1:1 Regel)."
    //
    // NOT a missing punch-out. This fan has gone through <c>CardMesh.AttachBody(…, CardBodyKind.Item,
    // …)</c> since round 17, and the hardware log of this very round proves the mesh is cut:
    //   "CARD BODY (Item): punched-out mesh built for 75,0x86,3 mm — 10 contour vertices".
    // The defect is the BOX that mesh is cut to. 75.0 x 86.3 mm comes from <see cref="LegacyCardW"/>
    // x <see cref="LegacyAspect"/>, a hand-written guess of a card TALLER than it
    // is wide. A Gloomhaven item card is the opposite: the same log's silhouette capture measures its
    // face at 270 x 258 px (footprint 224 x 214), i.e. WIDER than tall. Two consequences, both
    // visible as a border:
    //
    //   1. LETTERBOX. <see cref="RemotePileFronts"/> fits the cloned item-card face into the slab box
    //      keeping aspect, so a 1.047-aspect card inside a 0.869-aspect box renders 75.0 x 71.7 mm
    //      inside an 86.3 mm tall slab — ~7 mm of bare slab above and below every card. That slab
    //      wears the card-BACK material on both submeshes, so the leftover reads as a decorative
    //      frame around the art. This is the exact defect the LOCAL fan fixed long ago and wrote
    //      down: "#1: item cards are NEAR-SQUARE, not the tall ability rect — fitting them into the
    //      w×h ability box left black backing bars top/bottom" (<c>Cards.ItemsPile.ItemChip.Create</c>,
    //      which measures the hosted card's native rect into _faceWidth/_faceHeight and sizes the
    //      body to THAT). The mirror never got the same treatment.
    //   2. STRETCHED SILHOUETTE. The Item contour is derived once, in footprint space, and mapped
    //      onto whatever box the body is built at — so a wrong-aspect box also distorts the outline
    //      the punch-out is supposed to reproduce.
    //
    // THE FIX. Take the aspect from the one place that already knows it and that both players
    // compute identically: the applied Item FOOTPRINT (<c>CardMesh.Footprint(CardBodyKind.Item)</c>),
    // whose grid is proportional to the measured item-card face rect. No wire field, no per-card
    // measurement, no second cache: the footprint is the cache (persisted, CacheVersion-stamped, art
    // -asset provenance), and <c>AttachBody</c> keys its shaped meshes on (kind, w, h), so every slab
    // of every peer shares ONE mesh — a fan of twelve chips costs a single dictionary hit.
    //
    // DEGRADATION (explicit requirement): with no footprint yet — first run for this shape, or a
    // capture that was refused — <see cref="ResolvedCardH"/> returns <see cref="ChipBoxHFallback"/>
    // and <c>AttachBody</c> hands out the plain rounded slab, i.e. EXACTLY today's look. There is no
    // state in which a wrong silhouette is drawn: the aspect and the contour come from the same
    // measurement, so either both are known or neither is.
    //
    // NO POP WHEN IT ARRIVES MID-SESSION (cold cache): <see cref="EnsureCardHeight"/> re-attaches the
    // body meshes on the EXISTING slabs and re-binds the front overlays in the same frame, the same
    // "swap when learned" step CardMesh already performs on its own body registry — the slabs
    // themselves are never destroyed, so the arc does not blink.

    /// <summary>The card-box height the slabs are currently built at — <see cref="ResolvedCardH"/> at
    /// the time of the last (re)build. Starts on the legacy guess so a cold cache is bit-identical to
    /// the previous build.</summary>
    private float _cardH = Defaults.CardWidth * ChipScale * LegacyAspect;

    /// <summary>
    /// <c>RemoteCardArt.BorderFraction</c>, mirrored (it is private there). The front overlay fits a
    /// cloned face to <c>(1 − BorderFraction)</c> of the box it is handed, which is right for an
    /// ABILITY card — the local <c>CardFace</c> insets its face inside the slab the same way — but
    /// wrong for an item chip: <c>ItemsPile</c> sizes the chip body to the card's rendered rect
    /// EXACTLY, with no inset at all, so the peer's copy must not sit a border's width inside its own
    /// punched-out silhouette. Cancelled by handing the overlay a box scaled up by the same factor,
    /// so the face lands flush on the body edge — the body itself is untouched.
    /// </summary>
    private const float FrontBorderFraction = 0.06f;

    // ---- WHICH CHIPS LIE TAPPED (the SPENT look, ItemsPile.Relayout requirement 3) --------------
    // One flag per equipped item, index-aligned with the arc exactly as the front overlays are, and
    // read off the peer's host-replicated inventory by the shared walk in
    // RemotePileFronts.TryResolveItemSpentFlags (see its doc for the alignment and anti-cheat
    // arguments). Refreshed on the board-content cadence — the same 4 Hz RemotePileFronts resolves
    // its faces on — plus immediately on a slab-count edge, because a chip arriving or leaving
    // re-indexes every flag after it and a quarter second of a tap on the wrong chip reads as a bug.
    // An unreadable inventory leaves the list empty, which draws every chip upright: the state a
    // fresh arc is in, so the failure direction is "no tap", never "a tap on a guess".
    private readonly List<bool> _spent = new(MaxCards);
    private float _nextSpentResolveAt;
    private int _spentResolvedCount = -1;
    private int _loggedSpent = -1;

    /// <summary>True iff arc slab <paramref name="i"/> is a SPENT item — see <see cref="_spent"/>.
    /// Out-of-range (the model list is shorter than the arc, e.g. while a chip is in flight) reads
    /// as "not spent", which is the upright default.</summary>
    private bool IsSpent(int i) => i >= 0 && i < _spent.Count && _spent[i];

    /// <summary>Re-read the peer's per-item spent flags on the board-content cadence, or at once
    /// when the arc's slab count has changed under them.</summary>
    private void SyncSpentStates(int count)
    {
        if (count == _spentResolvedCount && Time.unscaledTime < _nextSpentResolveAt)
            return;
        _nextSpentResolveAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
        _spentResolvedCount = count;
        RemotePileFronts.TryResolveItemSpentFlags(_owner, _spent);

        int tapped = 0;
        for (int i = 0; i < _spent.Count && i < count; i++)
        {
            if (_spent[i])
                tapped++;
        }
        if (tapped == _loggedSpent)
            return;
        _loggedSpent = tapped;
        VRLog.Info("Net", $"Remote item fan tapped chips [player {_owner.PlayerId}]: {tapped} of " +
                          $"{count} slab(s) lie SPENT (rolled a further 90°, ItemsPile.Relayout's own " +
                          "requirement 3). Read from the peer's host-replicated Inventory.AllItems " +
                          "index-aligned with the fan — zero wire bytes, no item identity, and no " +
                          "RevealGate involvement: a slot state is not a card face.");
    }

    /// <summary>Change key for the <c>REMOTE ITEM CUT</c> diagnostic — the box height it last
    /// reported. Change-gated rather than one-shot so a fan that is built on a COLD cache (rounded
    /// slab, legacy box) and corrects itself when the footprint lands says so both times; in the
    /// normal warm-cache session it is exactly one line per peer fan.</summary>
    private float _loggedCutH = float.NaN;

    private int _builtCount = -1;
    private bool _poseInit;
    private int _loggedCount = -1;
    private bool _loggedHeld;
    /// <summary>One-shot latch for the "hidden by the remote-board setting" line (see Tick).</summary>
    private bool _gateHiddenLogged;

    // ---- emerge / collapse (the peer-visible "Auf- und Zuklappen" of the item Fach) ------------
    // The local fan does BOTH: ItemsPile.EmergeAll deals every chip OUT of the items stack into the
    // arc, and ItemsPile.CollapseChips folds every chip back INTO that stack before it dies. This
    // ghost used to do neither properly — it spread from its own centre and then vanished instantly
    // on close — so a peer never saw the fan close AT ALL, it just blinked out.
    //
    // Driven, like RemoteBrowserFan's, off the RECEIVER'S state transition (count 0↔N) rather than
    // off an event: the receiver already knows the sender's board pose and therefore where their
    // ITEMS stack is (RemoteControlBoard.AnchorLocal), so both arcs replay locally for zero extra
    // wire bytes. Timings match the local ones so both players see the same motion.
    //
    // ─── THE PRESENCE PASS (user report 2026-08-08: "Ich mag die Animation im Item-Pile sehr aber
    // sie ist (insbesondere in mixed Reality) etwas zu dezent.") ────────────────────────────────
    // The local fly-out stopped being an exponential home-lerp and became a parametric, per-chip
    // animation: a centre-out deal-out ripple, a mid-flight bow toward the viewer, growth from a
    // much smaller seed, a signed unfold roll, and an ease-out-BACK overshoot at the settle. The
    // fold-in is the same curve reversed (outermost chip first, ease-in-BACK wind-up).
    //
    // ALL OF IT IS REPRODUCED HERE, and that is not optional polish: the standing 1:1 ruling names
    // "alle Interaktionen, **Animationen** und Anzeigen des Controllboards". An owner whose item
    // fan deals out with an audible sense of weight while every peer sees the old smooth blob is
    // precisely the divergence the ruling forbids — and it is the kind nobody can spot from inside
    // their own headset. The maths below is ItemsPile.ItemChip's, term for term; the only
    // difference is that it runs in fan-LOCAL space on a slab instead of on a hosted card widget.
    private float _emergeElapsed = -1f;
    private Vector3 _emergeSeedLocal;                 // the sender's items stack, in fan-local space

    // ---- the owner's own ANIMATION dials (extension record 28) --------------------------------
    // Wire-overridable fallbacks, exactly like RemoteHandFan's geometry fields: the value the owner
    // set where they moved the dial, this client's shipped constant where they did not — which is
    // the same number, so an untuned peer is drawn with the shipped animation. Refreshed by
    // SyncTuning() on the owner's tuning revision, read as plain floats in between (the layout loop
    // touches them per slab per frame and RemoteBoardTuning is a wide struct).
    // scripts/check-remote-defaults.py holds all eight to the Defaults entries the local binds read.
    private float _openSeconds = Defaults.ItemFanOpenDuration;
    private float _openStagger = Defaults.ItemFanOpenStagger;
    private float _openArc = Defaults.ItemFanOpenArc;
    private float _openSpinDegrees = Defaults.ItemFanOpenSpinDegrees;
    private float _seedScale = Defaults.ItemFanSeedScale;
    private float _settleOvershoot = Defaults.ItemFanSettleOvershoot;
    private float _closeSeconds = Defaults.ItemFanCloseDuration;
    private float _closeStagger = Defaults.ItemFanCloseStagger;

    // ---- and the owner's own item-fan GEOMETRY (record 28, ids 79 / 158 / 198), wire-borne only
    // since the record was paged. These were `const Radius = 0.1792f * 1.7f` and
    // `const MaxStepDegrees = 10f`, and the radius was not merely un-synced but WRONG: the local
    // ItemsPile builds its arc from `[Cards] FanRadius × FanRadiusFactor_Items` = 0.16 × 1.7 =
    // 0.272 m, while this literal had been re-typed from FanEffectiveRadius (0.1792), giving
    // 0.30464 — a 12 % wider fan on every peer's screen than on the owner's, for every player,
    // tuned or not. That is exactly the class of drift scripts/check-remote-defaults.py exists to
    // catch and could not, because a bare literal has no pair to check. Reading it off the wire
    // fixes the untuned case and the tuned case in the same stroke.
    private float _radius = Defaults.FanRadius * Defaults.FanRadiusFactor_Items;
    private float _maxStepDegrees = Defaults.FanStepDegrees_Items;

    /// <summary>[Cards] CardLerpSpeed — the exponential the chips fly to their slots on, the
    /// owner's rather than ours (id 157).</summary>
    private float _lerpSpeed = Defaults.CardLerpSpeed;

    // ---- and the owner's own HOVER dials (record 28, ids 74 / 75 / 140 / 141) -------------------
    // THE 1:1 GAP THIS CLOSES (user report 2026-08-09, "das selbe Feedback … auch visuell"). The
    // owner's item fan does TWO things when a chip is singled out: it LIFTS that chip and it SPLITS
    // the arc apart around it (ItemsPile.Relayout, through the shared FanSweep.SplitOffset). This
    // renderer reproduced the lift and not the split, so a peer watching that fan saw a card rise
    // out of a rank that never made room for it — half an animation, and the standing 1:1 ruling
    // names animations explicitly. These are the SAME four ability-fan dials RemoteHandFan already
    // reads for the hand fan (it is literally the same split, on the same wire ids), so there is
    // nothing new to sample and no new id: the item fan simply starts consuming what record 28 has
    // been carrying since it was paged.
    //
    // WHAT IS DELIBERATELY *NOT* MIRRORED: the CONTROLLER TICK the owner feels when a chip lifts
    // under their hand (ItemsPile.UpdateHandSweep). That is not board state — it is a pulse in the
    // motor of the owner's own controller, caused by the owner's own hand being at that chip. A peer
    // has no hand there, so replaying it would be a phantom rather than a mirror. Everything a peer
    // can SEE of that hover — which chip, how far it lifts, and the gap the arc opens around it — is
    // synced, which is what the 1:1 ruling asks for; the ability fan draws the line in exactly the
    // same place (RemoteHandFan replays the highlight INDEX and never a haptic).
    private float _popForward = Defaults.FanSelectedPopForward;
    private float _splitMultiplier = Defaults.FanSplitMultiplier;
    private float _splitFalloff = Defaults.FanSplitFalloff;
    private float _splitScale = Defaults.FanHoverSplitScale;

    /// <summary>The <see cref="RemoteAvatar.BoardTuningRevision"/> the eight dials above were last
    /// refreshed at (−1 = never). Latched, not value-compared — the resolve already happens once
    /// per real change in <see cref="RemoteAvatar"/>.</summary>
    private int _tuningRevision = -1;

    // ---- the card LYING IN the owner's item-use recess (extension record 26) --------------------
    // THE GAP THIS CLOSES (2026-08-09). The owner lays a usable item card into their board's
    // item-USE recess; it re-parents onto the recess and settles into it over ClipSettleSeconds.
    // This ghost replayed a bare COUNT into the arc and RemoteBoardFurniture carried recess
    // VISIBILITY only, so on every other machine that card was still drawn out here in the arc while
    // the mirrored recess stood empty. The standing multiplayer ruling of 2026-08-08 covers "alle
    // Interaktionen, Animationen und Anzeigen des Controllboards", and a card lying in a recess is
    // such an Anzeige.
    //
    // WHAT ARRIVES IS ONE INDEX (NetProtocol.ExtIdItemUseClip) — a position in the very arc this
    // renderer already builds. So the fix is not a new object at all: the slab that would have been
    // drawn at that arc position is RE-PARENTED onto the mirrored recess, exactly as the owner's own
    // chip is re-parented onto theirs (ItemsPile.ItemChip.ClipIntoSlot). The hierarchy then holds it
    // there rigidly — right rotation, right board scale, right visibility, zero per-frame work — and
    // it LIES IN the recess rather than billboarding to anyone's head. Routing it through the synced
    // held-card pose slot was considered and rejected for precisely that: that receiver aims its
    // slab at the owner's head, so the card would have floated upright over the recess.
    //
    // THE ARRIVAL IS ANIMATED FROM THE EDGE, not streamed. The receiver knows the frame the index
    // appears, so it runs the owner's own settle on its own clock — same re-parent-keeping-world-
    // pose, same exponential, same duration — the identical trick that already replays the fan's
    // whole deal-out and fold-in for zero wire bytes. Streaming a pose would cost ~20 B × 15 Hz for
    // a 0.28 s movement both machines can derive from one byte.
    //
    // TAKING IT BACK is the same edge in reverse: the index disappears, the slab returns to the fan
    // root keeping its world pose and GLIDES to its arc slot over ReleaseGlideSeconds — the mirror
    // of ItemChip.ReturnToFan, which is the animation the owner sees when they pull the card out.
    private int _clipIndex = -1;          // slab currently parented to the mirrored recess, -1 = none
    private float _clipSettle;            // seconds left of the arrival ease (0 = landed / none)
    private float _clipFitScale = 1f;     // recess-local scale it settles to (fitted to the plate)
    private int _returnIndex = -1;        // slab gliding back to the arc after a take-back, -1 = none
    private float _returnGlide;           // seconds left of that glide
    private int _loggedClip = -2;         // change gate for the clip log line

    // ---- THE CARD THAT OUTLIVES THE FAN (2026-08-09) ------------------------------------------
    //
    // THE GAP THIS CLOSES, and it is the receiver-side half of a defect the user reported on their
    // OWN board: "Wenn ich eine Gegenstandskarte in den Overlay gelegt habe und dann in die Welt
    // klicke um den Fächer zu schließen verschwindet auch die abgelegte Gegenstandskarte — das soll
    // nicht sein. Sie soll liegen bleiben." The owner's card now stays lying in their recess after
    // the arc folds away (ItemsPile._keptClip). This renderer did the very thing the owner's pile
    // was just stopped from doing: the count dropped to 0, BeginCollapse called
    // ReleaseClip(returnToArc:false), and the recess slab was brought home and folded into the
    // items stack with the rest of the arc — so on every peer the card left the recess at exactly
    // the moment the owner clicked their fan away, while the owner watched it lie there.
    //
    // DETACHED, NOT REBUILT. The slab stays exactly where it is — the same GameObject, still
    // parented to the mirrored recess, still wearing its resolved front — and is simply made EXEMPT
    // from the arc's life-cycle: skipped by the fold-in, not released by Hide, and carried ACROSS
    // the next Rebuild to whatever arc position the owner's re-opened fan gives it. Rebuilding it
    // as a fresh slab was rejected for the obvious reason: a card that blinks out and a new one that
    // appears is the pop the standing ruling forbids, and it is the one transition where BOTH
    // players are looking straight at the recess.
    //
    // It keeps its seat in _cards (at _clipIndex) rather than being lifted into a field of its own:
    // every index-parallel structure in this file — the collapse capture, the pop ramps, the front
    // overlays — would otherwise have to be re-indexed at the close and again at the re-open, and
    // an index-parallel list that is edited in two places is how a slab ends up wearing another
    // card's face. _builtCount is invalidated at the detach instead, so a returning fan always
    // rebuilds and the re-adoption below is the only place the seat can change.
    private bool _clipDetached;

    /// <summary>Unscaled seconds left of the DETACHED slab's solo fold into the items stack (0 =
    /// none). It runs when the owner's record 26 goes away while their fan is still closed — i.e.
    /// they cancelled or confirmed the placement without ever re-opening the arc — and it is the
    /// mirror of the owner's own RetireChipToPile: the card is seen flying home, never blinked
    /// out.</summary>
    private float _soloElapsed = -1f;
    private Vector3 _soloFrom;
    private Quaternion _soloFromRot = Quaternion.identity;
    private float _soloFromScale = 1f;
    private Vector3 _soloTo;

    /// <summary>Unscaled seconds the arrival settle runs — MIRROR of
    /// <c>Cards.ItemsPile.ItemChip.ClipSettleSeconds</c>, the window the owner's own card eases from
    /// the release pose into the recess frame in. Held equal by scripts/check-mirrors.sh: a peer
    /// whose settle is a different LENGTH is watching a different animation, which is exactly what
    /// the 1:1 ruling forbids.</summary>
    private const float ClipSettleSeconds = 0.28f;

    /// <summary>Unscaled seconds the take-back glide runs — MIRROR of
    /// <c>Cards.ItemsPile.ItemChip.ReleaseGlideSeconds</c>, the window the owner's card flies home to
    /// its arc slot in.</summary>
    private const float ReleaseGlideSeconds = 0.35f;

    /// <summary>How much of the recess's clear inner plate a card lying in it fills, so the gold rim
    /// stays visible all the way round — MIRROR of <c>Cards.ItemsPile.UseSlotFillFraction</c>. The
    /// plate itself is <see cref="RemoteBoardFurniture.ItemUseInnerWidth"/>/<c>…Height</c>, i.e. THIS
    /// board's own geometry, so the fit is derived exactly the way the owner derives theirs: from
    /// the recess in front of the card, never from the fan's chip scale.</summary>
    private const float UseSlotFillFraction = 0.94f;

    private float _collapseElapsed = -1f;
    private Vector3 _collapseTo;
    private readonly List<Vector3> _collapseFrom = new(MaxCards);
    private readonly List<Quaternion> _collapseFromRot = new(MaxCards);
    private readonly List<float> _collapseFromScale = new(MaxCards);

    /// <summary>Drop the three index-parallel capture lists together. They are only ever filled and
    /// cleared as a set, and a partial clear would leave the collapse indexing a dead slab.</summary>
    private void ClearCollapseCapture()
    {
        _collapseFrom.Clear();
        _collapseFromRot.Clear();
        _collapseFromScale.Clear();
    }

    public RemoteItemFan(RemoteAvatar owner)
    {
        _owner = owner;
        _fronts = new RemotePileFronts(owner, "item fan");
    }

    public void Tick(float dt)
    {
        dt = Mathf.Max(dt, 0f);

        // The owner's own animation dials (record 28) BEFORE anything reads them — including the
        // collapse below, which must run on the owner's close timing even though the fan is already
        // logically gone.
        SyncTuning();

        // The card LEFT LYING in the mirrored recess after the arc folded away (see _clipDetached).
        // Serviced before anything else, because while it is detached it is the only thing this fan
        // is still drawing and it has a life of its own: it holds the recess, it re-seats itself
        // across a board rebuild, and it flies home when the owner's record 26 goes away.
        if (_clipDetached)
            TickDetachedRecess(dt);

        // A collapse (the fan closing) runs to completion on its own — the count already went to 0,
        // so this is the only thing keeping the chips on screen.
        if (_collapseElapsed >= 0f)
        {
            TickCollapse(dt);
            return;
        }

        int count = Mathf.Clamp(_owner.ItemCardCount, 0, MaxCards);
        if (count == 0)
        {
            // The fold-in has already run and a card stayed behind in the recess: there is nothing
            // left for the arc paths to do, and Hide() must not be allowed to release it.
            if (_clipDetached)
                return;
            // Close edge: prefer the collapse-into-the-stack glide; BeginCollapse returns false when
            // there is nothing up or no stack to aim at, and only then do we blink out as before.
            if (!BeginCollapse())
                Hide();
            return;
        }

        // VISIBILITY ([Net] RemoteBoards — audit 2026-07). A BOARD-ANCHORED item fan is part of the
        // peer's board: it floats at a fixed board-local spot above their control board and emerges
        // out of that board's items stack. Before this check it ignored the setting entirely, so a
        // player who had chosen "Aus" — or "Aktionsphase" during the secret selection phase — saw a
        // near-square arc of card backs blooming in empty air exactly where the board they had asked
        // NOT to see would have been. It obeys the shared gate now. A HAND-HELD fan is avatar
        // content (it rides the sender's palm) and is deliberately left alone: the setting governs
        // boards, not hands.
        // Hidden INSTANTLY rather than collapsed: the collapse animation flies the chips into the
        // board's items stack, and that stack is exactly what the gate just hid — an arc gliding
        // into nothing is worse than the fan simply not being there. The next time the gate opens
        // with the fan still up, _root is inactive, so the normal emerge-out-of-the-stack runs.
        if (!_owner.ItemFanHeld && !RemoteBoardGate.ShowBoardSurface(_owner))
        {
            if (!_gateHiddenLogged)
            {
                _gateHiddenLogged = true;
                VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: board-anchored fan " +
                                  (RemoteBoardScenarioGate.Open
                                      ? $"HIDDEN by [Net] RemoteBoards = {RemoteBoardGate.Mode} "
                                      : "HIDDEN because this client is not in a scenario, so no "
                                        + "peer's board exists at all (grep 'Remote board scenario "
                                        + "gate') — the dial is not what shut this ") +
                                  "(it belongs to that peer's board, which this client is not drawing).");
            }
            Hide();
            return;
        }
        _gateHiddenLogged = false;

        if (!TryResolvePose(out Vector3 target, out Quaternion rot))
        {
            Hide(); // holding hand not tracked / no board pose — no anchor, so nothing to show
            return;
        }

        EnsureRoot();
        if (_root == null)
            return;
        if (count != _builtCount)
            Rebuild(count);
        else
            // Cold cache only: the Item footprint can land AFTER these slabs were built (the local
            // capture needs a real item card to have been hosted once). Two static array reads per
            // frame, and it upgrades the bodies in place rather than rebuilding the arc — see
            // EnsureCardHeight.
            EnsureCardHeight();
        // Scale lives on the root (the fan is NOT parented under a scaled holder) and re-applies
        // every frame (one compare). It reproduces WHICHEVER transform the LOCAL fan hangs under
        // — the same rule RemoteBrowserFan documents: a HELD fan rides the palm (rig scale), a
        // BOARD-ANCHORED one is a child of the board root (ItemsPile.Open) and wears the BOARD
        // scale. The board branch used to apply the rig scale — the receiver-side fidelity bug
        // the old TryResolvePose note recorded as "needs a hardware round"; fixed in the 1:1
        // parity round (the board scales independently of world zoom, so the two routinely
        // differed and the peer's fan rendered the wrong size).
        float rootScale = _owner.ItemFanHeld
            ? _owner.AppliedScale
            : _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        if (!Mathf.Approximately(_root.transform.localScale.x, rootScale))
            _root.transform.localScale = Vector3.one * rootScale;
        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true;   // snap on the frame we appear, never ease in from a stale pose
            _root.transform.SetPositionAndRotation(target, rot);
            SeedEmerge();       // …and the chips fly OUT of the sender's ITEMS stack, like the local fan
        }

        Transform t = _root.transform;
        if (_poseInit)
        {
            _poseInit = false;
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rot, k));
        }

        // THE CARD IN THE RECESS (extension record 26) — resolved BEFORE the layout, because the
        // layout must know which slab it may not write this frame: the clipped one belongs to the
        // recess's hierarchy, not to the arc.
        ResolveClip();
        // …and WHICH chips lie tapped, before the layout that rolls them (see SyncSpentStates).
        SyncSpentStates(count);
        Layout(count, dt);
        TickClipSettle(dt);

        // THE FRONT LAYER (user ruling 2026-08-08). After the layout, so a face is only ever asked for
        // on a slab that already sits where it belongs. The gate inside is evaluated every frame; the
        // model resolve behind it rides the board-content cadence — see RemotePileFronts.
        _fronts.Tick(RemotePileFronts.Content.Items);
        TickUsableFrames(count);

        if (count != _loggedCount || _owner.ItemFanHeld != _loggedHeld)
        {
            _loggedCount = count;
            _loggedHeld = _owner.ItemFanHeld;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: {count} item card(s), " +
                              $"{(_owner.ItemFanHeld ? $"hand-held above their {(_owner.ItemFanLeftHand ? "LEFT" : "RIGHT")} palm" : "anchored above their board")} " +
                              "— no item identity on the wire; whether the slabs show FRONTS or BACKS " +
                              "is stated separately by the \"Remote item fan faces\" line (RevealGate " +
                              "decides, per phase).");
        }
    }

    /// <summary>Where the fan sits this frame: above the sender's dominant palm when they hold it,
    /// otherwise at their synced board-local fan anchor (record 5; authored default for
    /// pre-record senders). False when neither reference exists yet.
    ///
    /// NEAR-TWIN OF <c>RemoteBrowserFan.TryResolveAnchor</c> — kept separate because the browse
    /// fan carries per-card enlargement and a root-scale out-param. Since the 1:1 parity round
    /// the SCALE rule finally agrees between the two: both reproduce whichever transform the
    /// LOCAL fan hangs under (held → rig scale, board-anchored → board scale; see the scale
    /// note in <see cref="Tick"/> — the old rig-scale-on-the-board-branch divergence was a
    /// receiver-side fidelity bug, now fixed).</summary>
    private bool TryResolvePose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        float scale = _owner.AppliedScale;

        if (_owner.ItemFanHeld)
        {
            // The grabbing hand rides the wire as a flag (FlagItemFanLeft) — the owner may raise the
            // item fan with either hand, and guessing the dominant one put it on the wrong arm.
            Transform? holder = _owner.ItemFanLeftHand ? _owner.LeftHandHolder : _owner.RightHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            pos = holder.position + holder.up * (HandPalmOffset * scale);
        }
        else
        {
            if (!_owner.HasBoard)
                return false;
            float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
            // Defect 6 ("die Fächer sitzen nicht da, wo der Besitzer sie hat"): prefer the
            // SYNCED board-local anchor (extension record 5) — the owner's real fan spot
            // including their per-board [Cards] offsets. The authored default survives only
            // for pre-record senders / while the record is absent.
            Vector3 local = _owner.HasFanAnchor
                ? _owner.FanAnchorLocal
                : new Vector3(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);
            pos = _owner.BoardPosition + _owner.BoardRotation * (local * bs);
        }

        // Fronts (−Z) toward the owner, backs toward everyone else — the shared card convention.
        //
        // …AND THE READING PITCH. `ItemsPile.FaceHead` does not stop at the billboard: it appends
        // `* Quaternion.Euler(-12f, 0f, 0f)` ("tilt back a touch"). This mirror omitted it, so a
        // peer's item fan stood 12 DEGREES more upright than the arc its owner was reading, at the
        // shipped defaults and with nobody having tuned anything. One shared constant with the
        // browse fan, which was missing exactly the same term.
        Transform? head = _owner.HeadHolder;
        if (head != null)
        {
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f)
                rot = Quaternion.LookRotation(away.normalized, Vector3.up)
                      * Quaternion.Euler(RemotePileFronts.FanReadingPitchDegrees, 0f, 0f);
        }
        return true;
    }

    /// <summary>Arc the slabs in fan-local space — the same reading arc <see cref="ItemsPile"/>
    /// lays its chips out on (capped sweep, capped per-card step, z-staggered for draw order),
    /// dealing them out of the <see cref="SeedEmerge"/> seed on the owner's own fly-out curve.</summary>
    private void Layout(int n, float dt)
    {
        float step = n > 1 ? Mathf.Min(_maxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;
        float mid = (n - 1) * 0.5f;

        // The fly-out clock. It runs until the LAST slab (the outermost pair) has finished its own
        // duration — not for a fixed settle window — so lengthening the stagger cannot silently
        // truncate the deal-out into a snap on the trailing cards.
        bool easing = _emergeElapsed >= 0f;
        if (easing)
        {
            _emergeElapsed += dt;
            if (_emergeElapsed >= mid * _openStagger + _openSeconds)
            {
                _emergeElapsed = -1f; // settled: assert the slots exactly from here on
                easing = false;
            }
        }

        // WHICH ITEM CHIP THE OWNER IS SINGLING OUT (extension record 6, byte 1 — hardware MP
        // test 2026-08-04: "Das Highlighting der Karten in den Fächern ist nicht synchronisiert").
        // ROOT CAUSE of that report: the SENDER has fed the item fan's HighlightedIndex into the
        // card-highlight record since build 36 (NetAvatarDriver reads ItemsPile.HighlightedIndex
        // when no pile browser is open — the two board fans share wire byte 1 because at most one
        // is ever open), and RemoteBrowserFan renders it for the browse arc — but THIS renderer
        // never consumed the index, so a peer sweeping their equipped items showed a flat arc on
        // every other screen. Same fix shape as RemoteBrowserFan.Layout: clamp the synced index
        // against OUR live slab count (a packet can arrive a frame off the count it was measured
        // against) and lift that one slab.
        int hovered = _owner.FanHighlightIndex;
        if (hovered < 0 || hovered >= _cards.Count)
            hovered = -1;

        for (int i = 0; i < _cards.Count; i++)
        {
            // The slab lying in the owner's use recess is NOT in this arc: it is a child of the
            // mirrored recess and its pose is the recess's own frame (see ResolveClip /
            // TickClipSettle). Writing an arc slot over it here would be the second, disagreeing
            // answer — the exact defect ItemsPile fixed locally by having Relayout skip its own
            // PendingUse chip.
            if (i == _clipIndex)
            {
                TickRecessPop(i, hovered, dt);
                continue;
            }
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            // ITEMSPILE.RELAYOUT, TERM FOR TERM — including the two factors this loop was missing.
            // The owner bends the arc around a pivot below the root at `(cos(rad) - 1) * radius *
            // 0.55f` and rolls each chip at `-angle * 0.85f`; both were absent here, so a peer's item
            // fan bowed 1.82x too DEEP and leaned 1.18x too STEEP with nobody having tuned anything.
            // At the shipped radius (FanRadius 0.16 x FanRadiusFactor_Items 1.7 = 0.272 m) the end
            // chip sat 25.5 mm below the arc centre against the owner's 14.0 mm at 6 chips, and
            // 116.0 mm against 63.8 mm at 12 — a 52 mm gap at the ends of an arc 0.45 m across —
            // while it stood rolled 25.0 deg against the owner's 21.25 at 6 chips and 55.0 against
            // 46.75 at 12, i.e. 3.75 and 8.25 degrees of lean too much. RemoteBrowserFan had
            // carried both factors as named constants since it was written; the item mirror never
            // got them, which is why they now live in ONE place (RemotePileFronts) that both read.
            var pos = new Vector3(Mathf.Sin(rad) * _radius,
                                  (Mathf.Cos(rad) - 1f) * _radius * RemotePileFronts.FanArchFactor,
                                  -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle * RemotePileFronts.FanTiltFactor);
            // A SPENT item lies TAPPED, rolled a further 90 degrees in its slot — ItemsPile.Relayout
            // ("SPENT items lie 'tapped': roll the chip 90° in its slot (requirement 3)"), which
            // classifies off CItem.SlotState. This mirror had NO state branch at all, so a peer's
            // spent items stood upright in the arc while their owner looked at them lying sideways —
            // a 90 DEGREE disagreement on the one cue that says "already used this round".
            //
            // ZERO NEW WIRE: the state is read off the very list RemotePileFronts.Resolve already
            // walks index-aligned for the FACES (CPlayerActor.Inventory.AllItems, host-replicated).
            // Read INDEPENDENTLY of RevealGate on purpose: the gate governs card FRONTS — the secret
            // ability-card selection window — and the owner's own arc taps in every phase, so gating
            // the tap would make the mirrored arc disagree with its owner for the one phase the
            // 1:1 ruling has no exception for. A slot state is not a card identity.
            if (IsSpent(i))
                rot *= Quaternion.Euler(0f, 0f, 90f);
            // THE WHOLE-ARC SPLIT around the singled-out chip (ItemsPile.Relayout: "the pivot holds
            // still, its neighbours slide along their OWN local right so the winner reads
            // unmistakably"). This renderer used to reproduce the lift and NOT the split, which made
            // the mirrored fan the only one of the three that showed half of its owner's hover
            // animation — a 1:1 miss the standing ruling names explicitly. Same shared formula, same
            // ×ChipScale gain, and the pivot itself does not move: its lift comes below, exactly as
            // the owner's chip applies its pop on top of the finished arc pose.
            //
            // …UNLESS THE SINGLED-OUT CARD IS THE ONE LYING IN THE RECESS (see TickRecessPop). Record
            // 6 now also names that card — it has a perfectly good arc index and its lift is a board
            // animation the 1:1 ruling covers — but it is NOT at an arc position, so splitting the
            // arc around its seat would open a gap around a card that is not there. The owner's own
            // arc does not split for it either, and since 2026-08 it says so in the same two terms
            // this line does: ItemsPile.SplitPivotIndex is that fan's HighlightedIndex — the very
            // number this renderer is sent — minus exactly this case (`hovered >= 0 && hovered !=
            // ClippedChipIndex`, ClippedChipIndex being what record 26 fills _clipIndex from). So
            // declining here is not an exception to the rule, it IS the rule, verified on both ends.
            //
            // AND THE INDEX NEEDS NO SOURCE FLAG. This renderer splits on the bare position it is
            // sent, which used to be a knowing over-reach: the owner's arcs split for their HAND
            // sweep's winner only, so a laser-only hover lifted a chip in a rigid arc here and in an
            // opened one there, and a REQUEST for one wire bit ("this index is the hand sweep's
            // winner") stood in RemoteBrowserFan for it. That request is withdrawn — the pile arcs
            // were lagging CardFan, which has always taken its own pivot from the driver's laser
            // hover first (`_hoveredIndex >= 0 ? _hoveredIndex : _pokeHoveredIndex`), and both pile
            // arcs now do the same. Zero new ids, zero new bytes; see RemoteBrowserFan's split block.
            if (hovered >= 0 && hovered != _clipIndex && i != hovered)
                pos += rot * new Vector3(SplitOffset(i - hovered) * ChipScale, 0f, 0f);

            // THE LIFT, exactly as the owner's own chip applies it (ItemsPile.ItemChip.Update):
            // PopUp out of the arc along fan-local +Y, the owner's own [Cards] FanSelectedPopForward
            // toward their viewer along local −Z, ×PopScale enlargement, ramped at PopLerpSpeed.
            // Reproduced from the synced INDEX alone — the ramp runs on the LOCAL clock, so the wire
            // carries a position and never an animation. −Z is toward the owner's head here (the fan
            // billboards its back at everyone else), which matches the local chip popping toward ITS
            // viewer, and +Y is that same viewer's up.
            //
            // NOT rotated by `rot`, mirroring the owner term for term. The forward component is
            // indifferent (every rotation in this arc is a ROLL about Z, which leaves Z alone), but
            // the UP component is not: a SPENT item lies tapped at a further 90°, and lifting it
            // along its own local up would send that one card sideways while its neighbours rise.
            // See ItemsPile.ItemChip.Update for the full derivation.
            float popT = PopAmount(i, hovered, dt);
            if (popT > 0f)
                pos += new Vector3(0f, PopUp * popT, -_popForward * popT);
            float scale = 1f + (PopScale - 1f) * popT;
            Transform t = _cards[i].transform;
            if (easing)
            {
                // The owner's own fly-out, term for term (ItemsPile.ItemChip.TickEmerge): a per-slab
                // clock offset by the centre-out ripple, an ease-out-BACK onto the arc slot, a
                // mid-flight bow along local −Z (toward the fan's own viewer, exactly as the local
                // chip bows toward its owner), growth out of the seed size, and an unfold roll that
                // eases on the CLAMPED progress — a card that overshoots its roll reads as a wobble.
                float u = Mathf.Clamp01((_emergeElapsed - Mathf.Abs(i - mid) * _openStagger) / _openSeconds);
                float e = EaseOutBack(u, _settleOvershoot);
                Vector3 seed = _emergeSeedLocal + new Vector3(0f, 0f, -ZStagger * i);
                Vector3 p = Vector3.LerpUnclamped(seed, pos, e);
                p.z -= _openArc * Mathf.Sin(u * Mathf.PI);
                t.localPosition = p;
                Quaternion spin = Quaternion.Euler(0f, 0f, SpinSign(i, mid) * _openSpinDegrees);
                t.localRotation = rot * Quaternion.Slerp(spin, Quaternion.identity, e);
                t.localScale = Vector3.one * Mathf.LerpUnclamped(_seedScale, scale, e);
            }
            else if (i == _returnIndex && _returnGlide > 0f)
            {
                // TAKE-BACK GLIDE — the mirror of ItemChip.ReturnToFan: the slab was just handed
                // back from the recess frame to the fan root keeping its world pose, so it starts
                // wherever it was lying and eases to its arc slot on the same exponential the
                // owner's card flies home on. Only the CLOCK is local; the motion is theirs.
                _returnGlide -= dt;
                float k = 1f - Mathf.Exp(-_lerpSpeed * Mathf.Max(dt, 0f));
                t.localPosition = Vector3.Lerp(t.localPosition, pos, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, rot, k);
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * scale, k);
                if (_returnGlide <= 0f)
                {
                    _returnGlide = 0f;
                    _returnIndex = -1; // landed: the plain assignment below owns it again
                }
            }
            else
            {
                t.localPosition = pos;
                t.localRotation = rot;
                t.localScale = Vector3.one * scale;
            }
        }
        LogHighlightIfChanged(hovered);
    }

    // ---------------------------------------------------------------- highlight (record 6) --

    /// <summary>ItemsPile.ItemChip.PopScale — the enlargement a lifted item chip takes locally
    /// (×1.18, which is VRCard's own number). READ from <c>VRCard.PopScale</c> (the FRACTION, so
    /// the multiplier is 1 + it), exactly as the owner's own chip reads it.</summary>
    private const float PopScale = 1f + Cards.VRCard.PopScale;

    /// <summary>ItemsPile.ItemChip.PopUp — the UPWARD component of the lift (fan-local metres),
    /// <c>VRCard</c>'s 0.012 m, the same constant <see cref="RemoteBrowserFan"/> mirrors for the
    /// browse arc. The owner's chip gained this term in the parity pass (its lift used to be
    /// forward-only); without it here the mirrored chip would creep toward the viewer while the
    /// owner's rises out of the arc. The FORWARD component is not a constant at all any more — it is
    /// the owner's own <c>[Cards] FanSelectedPopForward</c>, see <see cref="_popForward"/>.</summary>
    private const float PopUp = Cards.VRCard.PopUp;

    /// <summary>ItemsPile.ItemChip.PopLerpSpeed — the ramp rate of the local pop (units/second),
    /// which the parity pass set to <c>VRCard</c>'s own 8/s. See <see cref="PopAmount"/> for the
    /// matching change of CURVE: the owner ramps on MoveTowards (linear), as do
    /// <see cref="RemoteHandFan"/> and <see cref="RemoteBrowserFan"/>; this file was the only one
    /// easing exponentially, so its lift arrived on a different curve than the one it mirrors.
    /// READ from <c>VRCard.PopRate</c>, the one home the owner's chip reads it from too.</summary>
    private const float PopLerpSpeed = Cards.VRCard.PopRate;

    /// <summary>ItemsPile.ChipScale — item chips stand in the arc 1.25× the authored card size, and
    /// the owner scales the shared split offset by it (ItemsPile.Relayout) so the gap keeps pace with
    /// the wider chips. Same fan-local metric space here (the arc radius is the owner's own, off the
    /// wire), so the mirror must apply the same factor or its arc opens 25 % too narrow.</summary>
    private const float ChipScale = 1.25f;

    /// <summary>Sideways slide of a split neighbour <paramref name="signed"/> slots away from the
    /// highlighted chip — the shared <c>Cards.FanSweep.SplitOffset</c> (the shape the hand fan, the
    /// browse fan and the item fan all split on) against the OWNER's resolved dials rather than
    /// ours. It said "byte-for-byte RemoteHandFan.SplitOffset" and was one; the three fans now
    /// genuinely share the one formula instead of three spellings of it.</summary>
    private float SplitOffset(int signed)
        => Cards.FanSweep.SplitOffset(signed, _splitMultiplier, _splitFalloff, _splitScale);

    /// <summary>Per-slab pop ramp (0..1), index-aligned with <c>_cards</c> — per slab so a lift
    /// MOVING along the arc has the old chip relaxing while the new one rises, exactly like the
    /// owner's own sweep.</summary>
    private readonly List<float> _pop = new(MaxCards);

    /// <summary>Last highlighted index stated in the log (−2 = never).</summary>
    private int _loggedHighlight = -2;

    /// <summary>Advance and return slab <paramref name="i"/>'s pop ramp toward 1 while it is the
    /// highlighted chip and toward 0 otherwise, on the local chip's own exponential.</summary>
    private float PopAmount(int i, int hovered, float dt)
    {
        while (_pop.Count <= i)
            _pop.Add(0f);
        float target = i == hovered ? 1f : 0f;
        // MoveTowards, not an exponential Lerp: the owner's chip ramps
        // `Mathf.MoveTowards(_pop, target, PopLerpSpeed * dt)` (ItemsPile.ItemChip.Update), exactly
        // as VRCard does and as RemoteHandFan/RemoteBrowserFan mirror for the other two fans. This
        // renderer was the odd one out — an exponential ease reaches ~63 % in the time the linear
        // ramp reaches 100 %, so the mirrored lift lagged the owner's for its whole travel and then
        // needed a snap threshold to finish at all. Linear arrives on its own; no tail snap needed.
        _pop[i] = Mathf.MoveTowards(_pop[i], target, PopLerpSpeed * Mathf.Max(dt, 0f));
        return _pop[i];
    }

    /// <summary>Drop every pop ramp (fan closed / rebuilt) so a re-opened fan never starts with a
    /// stale chip already lifted.</summary>
    private void ClearPops()
    {
        for (int i = 0; i < _pop.Count; i++)
            _pop[i] = 0f;
        _loggedHighlight = -2;
    }

    /// <summary>Change-gated evidence that the synced item-fan highlight reached the render path
    /// (grep: "Remote item fan highlight").</summary>
    private void LogHighlightIfChanged(int hovered)
    {
        if (hovered == _loggedHighlight)
            return;
        _loggedHighlight = hovered;
        VRLog.Info("Net", $"Remote item fan highlight [{_owner.PlayerId}]: " +
                          $"index {(hovered >= 0 ? hovered.ToString() : "none")} of " +
                          $"{_cards.Count} slab(s) (wire index {_owner.FanHighlightIndex}) — " +
                          "the chip lifts on ItemsPile's own pop, from the INDEX alone " +
                          "(extension record 6: no item identity).");
    }

    // ------------------------------------------------------ the card in the use recess (26) --

    /// <summary>
    /// Reconcile the synced clip index (<see cref="RemoteAvatar.ItemUseClipIndex"/>) with what this
    /// fan is actually rendering, and act only on the EDGES: a card entering the recess is
    /// re-parented onto it and starts its settle, a card leaving is handed back to the fan root and
    /// starts its glide home. Runs before <see cref="Layout"/>, which must not write the clipped
    /// slab.
    ///
    /// <para>THREE THINGS ARE VALIDATED, and each is a way a peer's card could otherwise end up
    /// somewhere the owner's is not:</para>
    /// <list type="bullet">
    /// <item>the index is clamped against OUR live slab count — a packet can legitimately arrive a
    /// frame either side of a fan resize, which is the same reason the highlight index is clamped
    /// here and not at the reader;</item>
    /// <item>the recess must exist AND be active in the hierarchy. It is shown from the board-UI
    /// record's own bit and hidden with the whole board by the <c>[Net] RemoteBoards</c> gate, so a
    /// card must never be left lying on a recess this client is not drawing — it falls back to the
    /// arc, which is exactly what pre-record builds showed;</item>
    /// <item>the parent is RE-ASSERTED while the state holds, so a board rebuild (which destroys and
    /// re-creates the furniture, and with it the recess transform) re-adopts the card instead of
    /// leaving it orphaned in mid-air — the receiver-side twin of <c>TickPendingUse</c>'s own
    /// "re-assert after a board rebuild" line.</item>
    /// </list>
    /// </summary>
    private void ResolveClip()
    {
        Transform? recess = _owner.ItemUseRecess;
        bool recessUsable = recess != null && recess.gameObject.activeInHierarchy;

        int want = _owner.ItemUseClipIndex;
        if (want < 0 || want >= _cards.Count || !recessUsable)
            want = -1;

        if (want == _clipIndex)
        {
            // Steady state. The settle is over as far as the hierarchy is concerned, so the only
            // per-frame work is proving we still own the parent we think we own.
            if (_clipIndex >= 0 && recess != null)
            {
                Transform t = _cards[_clipIndex].transform;
                if (t.parent != recess)
                {
                    t.SetParent(recess, worldPositionStays: true);
                    _clipSettle = ClipSettleSeconds; // re-seat visibly, never a teleport
                }
            }
            return;
        }

        // LEAVING first, so a clip that MOVES from one fan position to another (the owner swaps the
        // placed card — the demand pick's own "a fresh drop replaces an older clip" rule) releases
        // the old slab before the new one is taken.
        if (_clipIndex >= 0)
            ReleaseClip(returnToArc: true);

        if (want >= 0 && recess != null)
        {
            Transform t = _cards[want].transform;
            // Keep the WORLD pose across the re-parent and ease into the recess frame from there —
            // the card is seen travelling out of the arc and into the recess, which is the same
            // statement the owner's own ClipIntoSlot makes ("everything that moves is seen moving").
            t.SetParent(recess, worldPositionStays: true);
            _clipIndex = want;
            _clipSettle = ClipSettleSeconds;
            _clipFitScale = RecessFitScale();
            _returnIndex = -1; // a slab cannot be gliding home and lying in the recess at once
            _returnGlide = 0f;
        }
        LogClipIfChanged(recessUsable);
    }

    /// <summary>
    /// Advance the arrival settle: ease the clipped slab's RECESS-LOCAL pose toward the recess's own
    /// frame (zero position, identity rotation, <see cref="_clipFitScale"/>), then land on it
    /// exactly and stop writing. Term for term <c>ItemsPile.ItemChip.TickClipSettle</c>, including
    /// the property that matters most: once the window closes the transform hierarchy holds the card
    /// rigidly with no per-frame work at all — no chase, no residual error, nothing to swim behind a
    /// moving board or a moving head.
    /// </summary>
    private void TickClipSettle(float dt)
    {
        if (_clipIndex < 0 || _clipSettle <= 0f || _clipIndex >= _cards.Count)
            return;
        Transform t = _cards[_clipIndex].transform;
        _clipSettle -= dt;
        if (_clipSettle <= 0f)
        {
            _clipSettle = 0f;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * _clipFitScale;
            return;
        }
        float k = 1f - Mathf.Exp(-_lerpSpeed * Mathf.Max(dt, 0f));
        t.localPosition = Vector3.Lerp(t.localPosition, Vector3.zero, k);
        t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.identity, k);
        t.localScale = Vector3.Lerp(t.localScale, Vector3.one * _clipFitScale, k);
    }

    /// <summary>
    /// THE LIFT OF THE SLAB LYING IN THE MIRRORED RECESS — the receiver's copy of
    /// <c>ItemsPile.ItemChip.TickRecessPop</c> (user report 2026-08-09: "Ich will das die
    /// Gegenstandskarte die auf dem Overlay liegt auch nach oben hinweg gehighlighted wird …").
    ///
    /// <para>WHY IT IS MIRRORED AT ALL, stated because the standing 1:1 ruling deserves an answer and
    /// not an exemption. A hover is inherently about the LOCAL player's hand, and the mod has already
    /// drawn that line twice — for the ability fan and for the item arc — in exactly one place: the
    /// highlighted card's POSITION travels, the HAPTIC does not (see <c>ItemsPile.UpdateHandSweep</c>'s
    /// note: a pulse in the motor of one player's controller is not board state, and there is no hand
    /// of theirs at that card on a peer's machine). A card visibly rising out of the berth on the
    /// owner's control board IS board state — an <i>Anzeige</i> of their board, which is the wording
    /// the ruling uses — so it travels, on the same terms and through the same channel as the arc's
    /// lift: a bare INDEX on extension record 6, never an item identity, with the ramp run on the
    /// LOCAL clock so the wire carries a position and never an animation.</para>
    ///
    /// <para>NO WIRE CHANGE AND NO NEW DIAL: the clipped slab already has an arc index (it is the very
    /// number record 26 sends), so <c>ItemsPile.HighlightedIndex</c> simply names it when it is the
    /// card being singled out, and this method recognises the case by comparing the received highlight
    /// against the clip index it already holds.</para>
    ///
    /// <para>THE MOTION IS THE OWNER'S, term for term: <see cref="PopUp"/> along the recess frame's
    /// +Y (the board's up), the owner's own <c>FanSelectedPopForward</c> along its −Z (out of the
    /// board toward them), ×<see cref="PopScale"/> on top of the recess FIT scale, ramped at
    /// <see cref="PopLerpSpeed"/> through the shared <see cref="PopAmount"/>. Rotation is left at the
    /// identity the settle landed on — a lift is not a rotation, and the clipped slab carries no
    /// tapped roll in the recess frame.</para>
    ///
    /// <para>THE ARRIVAL SETTLE WINS while it is running, exactly as it does locally: the two
    /// animations must never write the same frame, and holding the ramp at zero until the card has
    /// landed is what keeps the lift from starting halfway up when the settle's final exact
    /// assignment lands. And when nothing is lifted this writes NOTHING, so the hierarchy goes on
    /// holding the slab rigidly — the property <see cref="TickClipSettle"/> exists to protect.</para>
    ///
    /// <para>ONE WINDOW IT CANNOT COVER, honestly: while the owner's ARC IS DOWN and only the card
    /// remains (<see cref="_clipDetached"/>), the sender has no open fan to read the highlight off —
    /// <c>ItemsPile.Current</c> is unpublished by the close — so record 6 carries nothing and this
    /// path is not reached. The card still LIES in the mirrored recess (record 26 / the detached
    /// service); only its hover lift is dark there. Closing that needs a sender-side source, which
    /// lives outside this renderer.</para>
    /// </summary>
    private void TickRecessPop(int i, int hovered, float dt)
    {
        if (_clipSettle > 0f)
            return; // the arrival owns the pose; the lift starts once the card has landed
        while (_pop.Count <= i)
            _pop.Add(0f);
        // Tested BEFORE the ramp is advanced, exactly as the owner's own guard is: asking afterwards
        // would skip the very frame the ramp lands on zero and leave the slab stranded a millimetre
        // or two out of its berth for as long as nobody hovered it again.
        if (i != hovered && _pop[i] <= 0f)
            return; // seated and un-hovered: touch nothing (see the settle's own note)
        float popT = PopAmount(i, hovered, dt);
        Transform t = _cards[i].transform;
        t.localPosition = new Vector3(0f, PopUp * popT, -_popForward * popT);
        t.localScale = Vector3.one * (_clipFitScale * (1f + (PopScale - 1f) * popT));
    }

    /// <summary>
    /// Hand the clipped slab back to the fan root KEEPING its world pose, so nothing jumps, and
    /// (when <paramref name="returnToArc"/>) start the glide home <see cref="Layout"/> runs for it.
    /// Every exit from the clipped state goes through here — the owner taking the card back, the
    /// recess disappearing, the fan closing, rebuilding or being destroyed — because the slab is a
    /// child of a transform this fan does NOT own, and a fan that hides its root while one of its
    /// slabs lives elsewhere leaves that slab hanging in the air.
    /// </summary>
    private void ReleaseClip(bool returnToArc)
    {
        int i = _clipIndex;
        _clipIndex = -1;
        _clipSettle = 0f;
        if (i < 0 || i >= _cards.Count || _root == null)
            return;
        GameObject card = _cards[i];
        if (card == null)
            return;
        card.transform.SetParent(_root.transform, worldPositionStays: true);
        if (!returnToArc)
            return;
        _returnIndex = i;
        _returnGlide = ReleaseGlideSeconds;
    }

    /// <summary>
    /// The recess-local scale a card lying in the recess comes to rest at: its own slab face fitted
    /// into THIS board's inner plate (<see cref="RemoteBoardFurniture.ItemUseInnerWidth"/>/Height),
    /// keeping its aspect, with the gold rim left showing. The receiver-side twin of
    /// <c>ItemsPile.UseSlotFitScale</c> — and derived the same way, from the RECESS in front of the
    /// card rather than from the fan's chip scale, which is what stops a near-square item face
    /// hanging over the tall card-shaped frame on either side.
    /// </summary>
    /// <remarks>Instance-scoped since 2026-08-13: the fit is against the slab's REAL height
    /// (<see cref="_cardH"/>, measured from the item footprint), not the old constant guess — the
    /// local twin fits the chip's measured face for exactly the same reason.</remarks>
    private float RecessFitScale()
    {
        // THE PLATE IS THE OWNER'S, NOT THE SHIPPED DEFAULT. This used to read
        // RemoteBoardFurniture.ItemUseInnerWidth/Height, a const pair frozen at Defaults.CardWidth,
        // while the berth those numbers describe is laid out from the owner's own [Cards] CardWidth
        // (record 28 id 70) — so on a board whose owner had retuned the dial, the card was fitted
        // to a berth of a different size than the one drawn under it. _cardWidth is that same wire
        // value, already held and refreshed here by SyncTuning, so the two derivations are one.
        float innerW = _cardWidth * RemoteBoardFurniture.UseSlotInnerFactor;
        float innerH = _cardWidth * RemoteBoardFurniture.ItemCardAspect
                       * RemoteBoardFurniture.UseSlotInnerFactor;
        return Mathf.Min(innerW / ChipBoxW, innerH / _cardH) * UseSlotFillFraction;
    }

    /// <summary>
    /// Per-frame service of the slab left LYING IN the mirrored recess after the owner's arc folded
    /// away (see <see cref="_clipDetached"/>). Three jobs, and each one is the receiver's copy of
    /// something the owner's own <c>ItemsPile.TickPlacedWhileClosed</c> does for the real card:
    /// <list type="number">
    /// <item>HOLD it — finish the arrival settle if it was still running, and re-assert the parent
    /// when a board rebuild replaces the recess transform under it (the same "re-assert after a
    /// board rebuild" rule <see cref="ResolveClip"/>'s steady state carries);</item>
    /// <item>KEEP ITS FACE resolved. <see cref="Hide"/> no longer hides the fronts while a card is
    /// detached, so the front layer has to keep ticking or the peer would be looking at a blank
    /// back-slab where the owner sees the real item card;</item>
    /// <item>FLY IT HOME when record 26 goes away while the fan is still closed — the owner
    /// cancelled the placement or confirmed it without re-opening their arc. The owner's card folds
    /// into their items stack in that case (<c>ItemsPile.RetireChipToPile</c>), so this plays the
    /// same fold: the close curve, into the peer's own items stack, and only then is the slab
    /// destroyed. Nothing pops.</item>
    /// </list>
    /// </summary>
    private void TickDetachedRecess(float dt)
    {
        GameObject? slab = _clipIndex >= 0 && _clipIndex < _cards.Count ? _cards[_clipIndex] : null;
        if (slab == null)
        {
            _clipDetached = false;
            _soloElapsed = -1f;
            return;
        }

        // ---- the solo fold into the items stack, once started, runs to completion ----------------
        if (_soloElapsed >= 0f)
        {
            _soloElapsed += dt;
            float u = Mathf.Clamp01(_soloElapsed / _closeSeconds);
            float e = EaseInBack(u, _settleOvershoot);
            Transform st = slab.transform;
            st.position = Vector3.LerpUnclamped(_soloFrom, _soloTo, e);
            st.rotation = _soloFromRot
                        * Quaternion.Slerp(Quaternion.identity,
                                           Quaternion.Euler(0f, 0f, _openSpinDegrees), u);
            st.localScale = Vector3.one * Mathf.LerpUnclamped(_soloFromScale, _soloFromScale * _seedScale, e);
            if (u < 1f)
                return;
            _soloElapsed = -1f;
            _clipDetached = false;
            DestroyAllSlabs();
            VRLog.Info("Net", $"Remote item recess [{_owner.PlayerId}]: the placed card has folded back " +
                              "into their items stack — the owner cancelled or confirmed it without " +
                              "re-opening their fan, so there was no arc for it to glide into " +
                              "(mirror of the owner's own RetireChipToPile).");
            return;
        }

        Transform? recess = _owner.ItemUseRecess;
        bool recessUsable = recess != null && recess.gameObject.activeInHierarchy;

        // ---- the owner's recess emptied (cancel / confirm) → fly it home -------------------------
        if (_owner.ItemUseClipIndex < 0 || !recessUsable)
        {
            if (TryItemStackWorld(out Vector3 stackWorld))
            {
                Transform st = slab.transform;
                _soloFrom = st.position;
                _soloFromRot = st.rotation;
                _soloFromScale = st.localScale.x;
                _soloTo = stackWorld;
                _soloElapsed = 0f;
                return;
            }
            // No board pose to aim at: there is no honest animation, and leaving the slab on a
            // recess the owner has emptied is worse than dropping it.
            _clipDetached = false;
            DestroyAllSlabs();
            return;
        }

        // ---- hold it ---------------------------------------------------------------------------
        if (slab.transform.parent != recess)
        {
            slab.transform.SetParent(recess, worldPositionStays: true);
            _clipSettle = ClipSettleSeconds; // re-seat visibly, never a teleport
            _clipFitScale = RecessFitScale();
        }
        TickClipSettle(dt);
        _fronts.Tick(RemotePileFronts.Content.Items);
    }

    /// <summary>Destroy every slab this fan owns and reset everything index-parallel with them —
    /// the arc slabs left inactive under a hidden root AND a detached recess survivor, which is the
    /// case the plain <see cref="Hide"/> path never has to handle. The next appearance rebuilds from
    /// scratch (<see cref="_builtCount"/> = −1).</summary>
    private void DestroyAllSlabs()
    {
        _fronts.Destroy();
        ClearUsableFrames();
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        ClearCollapseCapture();
        ClearPops();
        _builtCount = -1;
        _clipIndex = -1;
        _clipSettle = 0f;
        _clipDetached = false;
        _loggedClip = -2;
        _returnIndex = -1;
        _returnGlide = 0f;
        _collapseElapsed = -1f;
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
    }

    /// <summary>Change-gated evidence that the synced clip reached the render path (grep:
    /// "Remote item recess").</summary>
    private void LogClipIfChanged(bool recessUsable)
    {
        if (_clipIndex == _loggedClip)
            return;
        _loggedClip = _clipIndex;
        VRLog.Info("Net", $"Remote item recess [{_owner.PlayerId}]: " +
                          (_clipIndex >= 0
                              ? $"fan position {_clipIndex} of {_cards.Count} slab(s) now LIES IN the " +
                                $"mirrored item-use recess, settling over {ClipSettleSeconds:F2}s "
                              : "the recess is EMPTY again; the card glides back into the arc ") +
                          $"(wire index {_owner.ItemUseClipIndex}, extension record 26 — an arc " +
                          "POSITION, no item identity" +
                          (recessUsable ? ")." : "; this client is not drawing that recess right now)."));
    }

    // ------------------------------------------------------------------ emerge / collapse --

    /// <summary>
    /// Seed every chip ON the sender's ITEMS stack, shrunk to <see cref="_seedScale"/> and rolled by
    /// the owner's unfold angle, so the parametric ease in <see cref="Layout"/> deals them OUT of the
    /// pile — the wire-free replay of <c>ItemsPile.EmergeAll</c> + <c>ItemChip.BeginEmerge</c>. Falls
    /// back to the fan centre when the sender's board pose is unknown, so the worst case is a
    /// spread-open, never a pop-in.
    ///
    /// <para>The seed pose is written on the SAME frame the root is activated (see the caller), so a
    /// slab whose stagger delay has not elapsed yet is sitting on the stack from its first rendered
    /// frame — it never shows up in the arc and then jumps back. That is the receiver-side half of
    /// the owner's "nothing may pop" ruling.</para>
    /// </summary>
    private void SeedEmerge()
    {
        if (_root == null)
            return;
        _emergeSeedLocal = Vector3.zero;
        if (TryItemStackWorld(out Vector3 stackWorld))
            _emergeSeedLocal = _root.transform.InverseTransformPoint(stackWorld);
        for (int i = 0; i < _cards.Count; i++)
        {
            // THE CARD ALREADY LYING IN THE RECESS IS NOT DEALT OUT (2026-08-09). When the owner
            // re-opens a fan whose placed card never left their recess, Rebuild has just re-adopted
            // that slab at its new arc position (see _clipDetached) — it is a child of the RECESS,
            // so seeding it here would write a fan-local stack pose into RECESS-local space and
            // fling the card off the board on the very frame the arc comes back. It stays where it
            // lies; Layout skips it for the same reason.
            if (i == _clipIndex || _cards[i] == null)
                continue;
            Transform t = _cards[i].transform;
            t.localPosition = _emergeSeedLocal + new Vector3(0f, 0f, -ZStagger * i); // keep the draw order stable
            t.localScale = Vector3.one * _seedScale;
            // ROTATION is deliberately not seeded here: the unfold roll is expressed relative to the
            // slab's ARC rotation, which only Layout knows, and Layout runs unconditionally later in
            // the SAME Tick call at progress 0 — i.e. it writes the exact seed pose before anything
            // is rendered. Seeding a rotation here would be a second, disagreeing answer.
        }
        _emergeElapsed = 0f;
    }

    /// <summary>Which way slab <paramref name="i"/> rolls out of the stack: outward from the fan
    /// centre, so the arc UNFOLDS rather than sliding open (<c>ItemsPile.EmergeAll</c>'s rule).</summary>
    private static float SpinSign(int i, float mid) => i - mid >= 0f ? 1f : -1f;

    /// <summary>Ease-out BACK — <c>ItemsPile.ItemChip.EaseOutBack</c> verbatim. <paramref name="s"/>
    /// = 0 degenerates to the plain ease-out cubic, which is what an owner who has turned the
    /// overshoot off is looking at.</summary>
    private static float EaseOutBack(float t, float s)
    {
        float u = t - 1f;
        return 1f + u * u * ((s + 1f) * u + s);
    }

    /// <summary>Ease-in BACK — the collapse's wind-up, <c>ItemsPile.ItemChip.EaseInBack</c> verbatim.</summary>
    private static float EaseInBack(float t, float s) => t * t * ((s + 1f) * t - s);

    /// <summary>
    /// Pull the owner's own item-fan ANIMATION dials out of their resolved tuning (extension record
    /// 28) when it has actually changed. Every dial they have not touched resolves to this client's
    /// shipped constant — the same number — so an untuned peer's fan opens exactly as this build
    /// ships it. No rebuild is ever needed: none of these changes the slab GEOMETRY, only the curve
    /// the slabs travel on, and that is recomputed every frame anyway.
    /// </summary>
    private void SyncTuning()
    {
        if (_tuningRevision == _owner.BoardTuningRevision)
            return;
        _tuningRevision = _owner.BoardTuningRevision;
        RemoteBoardTuning t = _owner.BoardTuning;
        _openSeconds = Mathf.Max(0.01f, t.ItemFanOpenDuration);
        _openStagger = Mathf.Max(0f, t.ItemFanOpenStagger);
        _openArc = Mathf.Max(0f, t.ItemFanOpenArc);
        _openSpinDegrees = t.ItemFanOpenSpinDegrees;
        _seedScale = Mathf.Clamp(t.ItemFanSeedScale, 0.02f, 1f);
        _settleOvershoot = Mathf.Clamp(t.ItemFanSettleOvershoot, 0f, 3f);
        _closeSeconds = Mathf.Max(0.01f, t.ItemFanCloseDuration);
        _closeStagger = Mathf.Max(0f, t.ItemFanCloseStagger);
        // The fan's SHAPE, wire-borne only since record 28 was paged (see the field declarations —
        // the radius was also plain wrong here, by 12 %, for every player). Guarded above zero
        // because a wire value is never trusted: a zero radius collapses the arc onto a point.
        _radius = Mathf.Max(0.02f, t.FanRadius * t.FanRadiusFactorItems);
        _maxStepDegrees = Mathf.Max(0.5f, t.FanStepDegreesItems);
        _lerpSpeed = Mathf.Max(0.5f, t.CardLerpSpeed);
        // …and the chip's own SIZE (id 70), which was a bare 0.075 m — see LegacyCardW for what that
        // cost at the shipped defaults. Nothing else has to be poked: ResolvedCardH derives the box
        // height from ChipBoxW, so a width change moves _cardH too, and EnsureCardHeight (or the
        // count-edge Rebuild) re-cuts the shared body mesh and re-binds the front boxes in place on
        // the very next frame — the arc never blinks.
        _cardWidth = Mathf.Max(0.01f, t.CardWidth);
        // The HOVER response (parity pass 2026-08-09): the owner's lift distance and the arc split
        // they open around the lifted chip. The very same four ability-fan dials RemoteHandFan reads
        // for the hand fan — the item fan splits on the identical shared formula, so it consumes the
        // identical wire ids (74 / 75 / 140 / 141) and needs none of its own. Unclamped on purpose,
        // like RemoteHandFan: a zero or negative split is a legitimate "no split" setting and a lift
        // of zero is a legitimate "no pop", where a zero RADIUS would collapse the arc onto a point,
        // which is why that one is guarded above.
        _popForward = t.FanSelectedPopForward;
        _splitMultiplier = t.FanSplitMultiplier;
        _splitFalloff = t.FanSplitFalloff;
        _splitScale = t.FanHoverSplitScale;
    }

    /// <summary>
    /// Close edge: glide every chip back INTO the sender's items stack over
    /// <c>CollapseSeconds</c> instead of blinking the fan out — the replay of
    /// <c>ItemsPile.CollapseChips</c>. The root pose is frozen for the duration and the chips are
    /// driven in WORLD space, mirroring how the local chips are re-parented out of the fan root
    /// before they glide. Returns false (caller hides instantly) when there is nothing to collapse
    /// or no stack to collapse into.
    /// </summary>
    private bool BeginCollapse()
    {
        if (_root == null || !_root.activeSelf || _cards.Count == 0)
            return false;
        if (!TryItemStackWorld(out Vector3 stackWorld))
            return false;

        // …UNLESS THE CARD IS STILL IN THE RECESS (2026-08-09, see _clipDetached). The owner's fan
        // folding away is not a decision about the card lying in their use recess: it stays there
        // until they pick it up, confirm it with USE, or play moves on. So when record 26 still
        // names a slab at the close edge, that slab is DETACHED — left parented to the mirrored
        // recess, skipped by the fold-in below and by Hide's release — and the arc folds away
        // around it, exactly as the owner sees it.
        Transform? recessNow = _owner.ItemUseRecess;
        bool keepInRecess = _clipIndex >= 0 && _clipIndex < _cards.Count
                            && _owner.ItemUseClipIndex >= 0
                            && recessNow != null && recessNow.gameObject.activeInHierarchy;
        if (keepInRecess)
        {
            _clipDetached = true;
            // The slab set is no longer "an arc of _builtCount slabs": force the next appearance
            // through Rebuild, which is where the survivor is re-adopted at its new arc position.
            _builtCount = -1;
        }
        else
        {
            // Nothing is lying in the recess (or this client is not drawing one): a slab still
            // parented there belongs to the RECESS's hierarchy, and the collapse drives every slab
            // in world space off a pose captured right here — so it comes home to the fan root
            // first (keeping its world pose, so the capture is unchanged) and folds into the stack
            // with the rest. Without this the fan would "close" while one card stayed in the recess.
            ReleaseClip(returnToArc: false);
        }

        _emergeElapsed = -1f;
        _collapseTo = stackWorld;
        ClearCollapseCapture();
        for (int i = 0; i < _cards.Count; i++)
        {
            Transform t = _cards[i].transform;
            _collapseFrom.Add(t.position);
            _collapseFromRot.Add(t.rotation);
            _collapseFromScale.Add(t.localScale.x);
        }
        _collapseElapsed = 0f;

        float total = (_cards.Count - 1) * 0.5f * _closeStagger + _closeSeconds;
        VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closing — {_cards.Count} item card(s) " +
                          $"fold back into their items stack outermost-first ({total:F2}s total: " +
                          $"{_closeSeconds:F2}s each, {_closeStagger:F3}s per place), matching the local fan." +
                          (keepInRecess
                              ? $" Slab {_clipIndex} STAYS LYING in the mirrored item-use recess and takes " +
                                "no part in the fold-in — the owner's fan closing is not a decision about " +
                                "the card they placed (extension record 26 still names it)."
                              : string.Empty));
        _loggedCount = 0; // Hide's own "closed" line is redundant with this one
        return true;
    }

    /// <summary>
    /// The fold-in, driven in WORLD space off the poses captured at the close — the replay of
    /// <c>ItemsPile.CollapseChips</c> + <c>ItemChip</c>'s collapse tick, including the presence pass:
    /// the REVERSE ripple (outermost slab first, so the close is the open played backwards), the
    /// ease-in-BACK wind-up (the slab lifts AWAY from the stack for a moment before it is pulled in
    /// — the anticipation that says where the card is about to go before it goes there), and the
    /// unfold roll wound back on.
    ///
    /// <para>A slab still waiting out its delay holds its captured pose exactly, because the curve is
    /// the identity at t = 0. Nothing is hidden and nothing is moved early.</para>
    /// </summary>
    private void TickCollapse(float dt)
    {
        _collapseElapsed += dt;
        int n = _cards.Count;
        float mid = (n - 1) * 0.5f;
        bool allDone = true;
        for (int i = 0; i < n && i < _collapseFrom.Count; i++)
        {
            // The detached recess slab is not in this arc any more (see _clipDetached): it lies on
            // the board and must neither be moved nor keep the fold-in waiting.
            if (_clipDetached && i == _clipIndex)
                continue;
            // (mid − |i − mid|): the outermost pair starts at 0, the centre slab last — the exact
            // reverse of the fly-out's centre-out ripple.
            float delay = (mid - Mathf.Abs(i - mid)) * _closeStagger;
            float u = Mathf.Clamp01((_collapseElapsed - delay) / _closeSeconds);
            float e = EaseInBack(u, _settleOvershoot);
            Transform t = _cards[i].transform;
            t.position = Vector3.LerpUnclamped(_collapseFrom[i], _collapseTo, e);
            t.rotation = _collapseFromRot[i]
                       * Quaternion.Slerp(Quaternion.identity,
                                          Quaternion.Euler(0f, 0f, SpinSign(i, mid) * _openSpinDegrees), u);
            t.localScale = Vector3.one
                         * Mathf.LerpUnclamped(_collapseFromScale[i], _collapseFromScale[i] * _seedScale, e);
            if (u < 1f)
                allDone = false;
        }
        if (!allDone)
            return;
        _collapseElapsed = -1f;
        Hide();
    }

    /// <summary>World position of the sender's ITEMS stack, resolved through the SHARED board-local
    /// stack layout against their own synced board pose — the point the chips emerge from and
    /// collapse into, wherever that player parked their board.</summary>
    private bool TryItemStackWorld(out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition
                + _owner.BoardRotation * (_owner.BoardAnchorLocal(CardFxAnchor.Items) * bs);
        return true;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteItemFan[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        // Sized by the sender's rig scale so the fan reads the same physical size as their hands.
        _root.transform.localScale = Vector3.one * _owner.AppliedScale;
        _root.SetActive(false);
        // USER ITEM 7 (2026-09-02: "Die offenen Faecher, die ueber dem board schweben und zu dem
        // board gehoeren (Gegenstaende, verbrannt, abgeworfen) sollen auch in dem selben Masse
        // transparent sein, wenn das board transparent ist"). This fan belongs to the owner's
        // board, but it is a SCENE-ROOT object posed from the board each frame, not a child of
        // it — so PeerBoardFade's hierarchy census could never reach it. It registers instead,
        // and its renderers are then driven by the SAME alpha, in the same loop, on the same
        // frame as the board's own; there is no second ramp for it to drift from. Inert while
        // [PeerBoardFade] Mode is Off.
        PeerBoardFade.Follow(_owner.PlayerId, _root.transform);
        VRLayers.Apply(_root);
    }

    /// <summary>
    /// The item card's real box height for <see cref="ChipBoxW"/>, taken from the applied Item
    /// FOOTPRINT — see the block comment at <see cref="_cardH"/>. Returns the historical
    /// <see cref="ChipBoxHFallback"/> guess while no footprint exists (cold cache / refused capture), so the
    /// degraded look is exactly the previous build's. Cheap: two static array reads.
    ///
    /// <para>Instance rather than static since the box width became the OWNER's card width times
    /// <see cref="ChipScale"/> — the aspect is a property of the shared game widget, but the box it
    /// is applied to is a property of the peer whose fan this is.</para>
    /// </summary>
    private float ResolvedCardH()
    {
        byte[]? foot = CardMesh.Footprint(CardBodyKind.Item, out int fw, out int fh);
        if (foot == null || fw <= 1 || fh <= 1)
            return ChipBoxHFallback;
        // Sanity band. A footprint is a measurement of a game widget and could in principle come
        // back degenerate; a card box outside 0.5x..2x the card width is not an item card, and the
        // standing rule is to degrade to the previous look rather than to a wrong shape.
        float h = ChipBoxW * (fh / (float)fw);
        return h < ChipBoxW * 0.5f || h > ChipBoxW * 2f ? ChipBoxHFallback : h;
    }

    /// <summary>The box handed to the front overlays: the slab box grown by the overlay's own inset
    /// so the cloned face lands FLUSH on the punched-out body edge — see <see cref="FrontBorderFraction"/>.</summary>
    private float FrontBoxW => ChipBoxW / (1f - FrontBorderFraction);

    private float FrontBoxH => _cardH / (1f - FrontBorderFraction);

    /// <summary>
    /// Adopt the item card's real aspect the moment it becomes known (warm cache: before the first
    /// slab exists; cold cache: the frame the local capture lands). Re-attaches the shared shaped
    /// body mesh on the EXISTING slabs and re-binds the front overlays — no slab is destroyed, so the
    /// arc never blinks, and <see cref="RemotePileFronts.Rebuild"/>'s reset makes the next
    /// <c>Tick</c> re-resolve the faces in the same frame rather than on the 4 Hz cadence.
    /// </summary>
    private void EnsureCardHeight()
    {
        float want = ResolvedCardH();
        if (Mathf.Approximately(want, _cardH))
            return;
        float was = _cardH;
        _cardH = want;
        for (int i = 0; i < _cards.Count; i++)
        {
            GameObject slab = _cards[i];
            MeshFilter? mf = slab != null ? slab.GetComponent<MeshFilter>() : null;
            if (mf != null)
                CardMesh.AttachBody(mf, CardBodyKind.Item, ChipBoxW, _cardH);
        }
        if (_cards.Count > 0)
            _fronts.Rebuild(_cards, FrontBoxW, FrontBoxH);
        _clipFitScale = RecessFitScale();
        LogCut(was);
    }

    /// <summary>The one <c>REMOTE ITEM CUT</c> line per session: what the peer's item chips are cut
    /// to, and which measurement it came from — so the next MP log proves the shape without a
    /// screenshot.</summary>
    private void LogCut(float previousH)
    {
        if (Mathf.Approximately(_loggedCutH, _cardH))
            return;
        _loggedCutH = _cardH;
        CardMesh.Footprint(CardBodyKind.Item, out int fw, out int fh);
        VRLog.Info("Net", "REMOTE ITEM CUT: a peer's item chips are now cut to the ITEM card's own " +
                          $"box {ChipBoxW * 1000f:F1}x{_cardH * 1000f:F1} mm (was " +
                          $"{LegacyCardW * 1000f:F1}x{previousH * 1000f:F1} mm — a hand-written width " +
                          $"and a hand-written 1.15 aspect; the width is the owner's own [Cards] " +
                          $"CardWidth x ChipScale now, {_cardWidth * 1000f:F2}x{ChipScale:F2}), aspect taken " +
                          $"from the applied Item footprint {fw}x{fh} captured from " +
                          $"'{CardMesh.FootprintSource(CardBodyKind.Item) ?? "n/a"}' — the SAME persisted, " +
                          "CacheVersion-stamped mask the owner's own chips are punched out with, so no " +
                          "silhouette is recomputed per remote card and every slab of every peer shares " +
                          "CardMesh's one cached shaped mesh per (kind, w, h). Punched out: " +
                          $"{CardMesh.SilhouetteApplied(CardBodyKind.Item)}. The front overlay box is " +
                          $"grown to {FrontBoxW * 1000f:F1}x{FrontBoxH * 1000f:F1} mm to cancel " +
                          "RemoteCardArt's 6 % inset, so the cloned card face lands FLUSH on that " +
                          "outline instead of leaving a ring of card-back slab around it (user, " +
                          "2026-08-13: 'haben noch Ränder statt richtig ausgeschnitten zu sein').");
    }

    private void Rebuild(int count)
    {
        // The slabs about to be built take the item card's REAL box (see the _cardH block comment);
        // on a cold cache this is still the legacy guess and the bodies stay rounded rects, exactly
        // as before, until EnsureCardHeight upgrades them in place.
        float previousH = _cardH;
        _cardH = ResolvedCardH();

        // THE RECESS SURVIVOR IS CARRIED ACROSS (see _clipDetached): the owner re-opened their fan
        // while their card is still lying in the recess, so the slab that IS that card must come
        // through this rebuild as the same object at whatever arc position record 26 now names.
        // Destroying it and letting ResolveClip re-parent a fresh slab would blink the card out of
        // the recess and fly a new one in — the pop the standing ruling forbids, on the one surface
        // both players are looking straight at.
        GameObject? survivor = null;
        if (_clipDetached && _clipIndex >= 0 && _clipIndex < _cards.Count)
        {
            survivor = _cards[_clipIndex];
            // null! — the whole list is cleared two statements below; this only takes the survivor
            // out of the destroy sweep while it stays parented to the recess.
            _cards[_clipIndex] = null!;
        }
        else
        {
            // The clipped slab is a child of the mirrored RECESS, not of this fan's root — bring it
            // home before the slabs are destroyed, and drop the clip state with them: the indices
            // about to be handed out address a different set of objects.
            ReleaseClip(returnToArc: false);
        }
        _clipDetached = false;
        _soloElapsed = -1f;
        _clipIndex = -1;
        _clipSettle = 0f;
        _returnIndex = -1;
        _returnGlide = 0f;
        _loggedClip = -2;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        ClearCollapseCapture(); // parallel to _cards — never let it outlive the slabs it indexed
        ClearPops();            // index-aligned with _cards too — a rebuilt arc starts flat
        ClearUsableFrames();    // index-aligned with _cards as well (record 35's frames)

        Material back = CardMesh.CreateBackMaterial(CardBodyKind.Item); // SHARED cache — never ours to destroy
        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Item{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            var mf = card.AddComponent<MeshFilter>();
            // Round 17 (1:1 board rule): ITEM kind — this fan mirrors the owner's item chips, so
            // the chip adopts the same punched-out Item body via CardMesh.AttachBody (shared cached
            // mesh, never ours to destroy; upgraded in place when the Item contour is learned).
            // …at the item card's REAL box, not the old 75 mm x 1.15 guess: a wrong-aspect box both
            // letterboxes the face (the reported "Ränder") and stretches the very outline the
            // punch-out reproduces. See the _cardH block comment.
            CardMesh.AttachBody(mf, CardBodyKind.Item, ChipBoxW, _cardH);
            var mr = card.AddComponent<MeshRenderer>();
            // Two submeshes (front+rim | back), both wearing the shared Item back material: the
            // arc deliberately shows the BACK on both faces, exactly like the old two-quad slab.
            // …and both slots keep it while the slab is showing a card BACK. When a front is up,
            // Net/Remote/RemoteCardArt drops submesh 0's FRONT FAN from the MESH instead (see
            // CardMesh.SetBodyFaceHosted): this fan follows the peer board's see-through ramp, and
            // two coincident surfaces at one uniform alpha compose rather than occlude, so the fan
            // behind the print used to paint the card back's gold lattice over a peer's card front.
            // Nothing here changes — the material array stays exactly this, at length 2.
            mr.sharedMaterials = new[] { back, back };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }
        // …and seat the survivor at the position the owner's re-opened fan gives it, replacing the
        // fresh slab that was just built for it. It is already parented to the recess and already
        // settled, so _clipSettle stays 0: it does not re-arrive, it simply never left.
        if (survivor != null)
        {
            int want = _owner.ItemUseClipIndex;
            Transform? recess = _owner.ItemUseRecess;
            if (want >= 0 && want < _cards.Count && recess != null
                && recess.gameObject.activeInHierarchy && survivor.transform.parent == recess)
            {
                if (_cards[want] != null)
                    Object.Destroy(_cards[want]);
                _cards[want] = survivor;
                _clipIndex = want;
                _clipSettle = 0f;
                _clipFitScale = RecessFitScale();
                VRLayers.Apply(survivor); // it is not under _root, so the root sweep below misses it
            }
            else
            {
                // The owner took it back / the recess went away between the two edges: it has no
                // seat any more, and ResolveClip will not adopt an object this list does not own.
                Object.Destroy(survivor);
            }
        }
        _builtCount = count;
        VRLayers.Apply(_root!);
        // Re-bind the front overlays onto the NEW slabs (the old ones died with their hosts above).
        // The card size handed over is the UNSCALED slab size: the overlay is a child of the slab, so
        // it inherits the emerge seed scale and the pop enlargement for free, like the slab's own mesh.
        _fronts.Rebuild(_cards, FrontBoxW, FrontBoxH);
        LogCut(previousH);
    }

    /// <summary>
    /// WHICH mirrored chips wear the owner's gold "you can play this NOW" frame — the receiver of
    /// extension record <see cref="NetProtocol.ExtIdItemUsable"/>, and the fix for the 2026-09-02
    /// report's item 5 ("diese ist aber nicht beim remote board … sichtbar").
    ///
    /// <para>CHANGE-GATED DOWN TO ONE USHORT COMPARE in the steady state, which is what makes it
    /// affordable at 90 Hz on every peer: the mask only moves when the owner's turn, their bonus
    /// offer or their inventory does, all of them human-paced. The mapping walk behind the gate is
    /// over a bounded equipment list (at most a handful of entries).</para>
    ///
    /// <para>NOT GATED BY <see cref="RevealGate"/>, for the reason
    /// <c>RemotePileFronts.TryResolveItemSpentFlags</c> gives in full beside it: the gate governs
    /// card FRONTS in the secret selection window, and a frame around a POSITION in an inventory the
    /// flat game shows to everyone is neither a front nor a secret. Gating it would make the
    /// mirrored arc disagree with its owner in the one phase the 1:1 ruling grants no exception
    /// to.</para>
    ///
    /// <para>THE BEAT PERIOD IS THE VIEWER'S OWN <c>[Cards] ItemCueBeatSeconds</c> AND THAT IS
    /// DELIBERATE. It is not a per-sub-feature sync carve-out: the dial is not a property of the
    /// owner's board at all, it is the rhythm THIS client already beats every one of its own item
    /// cues on, and the whole point of <see cref="WorldUI.SoftFramePulse"/> reading
    /// <c>Time.unscaledTime</c> is that every framed card in the room breathes in phase. Following
    /// the owner's period here would put a peer's frames out of phase with the viewer's own — one
    /// cue rendered as two — which is the opposite of what the 1:1 rule is protecting. WHICH cards
    /// are framed is the owner's answer and rides the wire; how fast the room breathes is the room's.
    /// </para>
    /// </summary>
    private void TickUsableFrames(int count)
    {
        ushort mask = _owner.ItemUsableMask;
        bool dirty = mask != _usableMask || count != _usableBuiltCount;
        _usableMask = mask;
        _usableBuiltCount = count;
        if (!dirty)
            return;

        if (mask == 0)
        {
            // Nothing is playable on that board — or the sender predates record 35. Both mean the
            // arc this build drew before: bare slabs.
            for (int i = 0; i < _usableFrames.Count; i++)
            {
                GameObject? off = _usableFrames[i];
                if (off != null && off.activeSelf)
                    off.SetActive(false);
            }
            if (_loggedUsableMask != 0)
            {
                _loggedUsableMask = 0;
                VRLog.Info("Net", $"Remote ITEM usable [player {_owner.PlayerId}]: no frames — "
                    + "record 35 absent or 0x0000 (nothing is playable on that board right now).");
            }
            return;
        }

        if (!RemoteUsableFrame.ResolveSlots(_owner, mask, _usableSlots))
        {
            // The peer's inventory is unreadable this frame: leave the arc bare rather than framing
            // a guess. Same failure direction the front layer takes.
            for (int i = 0; i < _usableFrames.Count; i++)
            {
                GameObject? off = _usableFrames[i];
                if (off != null && off.activeSelf)
                    off.SetActive(false);
            }
            return;
        }

        int shown = 0;
        for (int i = 0; i < count && i < _cards.Count; i++)
        {
            bool on = i < _usableSlots.Count && _usableSlots[i];
            GameObject? slab = _cards[i];
            if (slab == null)
                continue;
            while (_usableFrames.Count <= i)
                _usableFrames.Add(null);
            if (on && _usableFrames[i] == null)
                _usableFrames[i] = RemoteUsableFrame.Build(slab.transform, ChipBoxW, _cardH);
            GameObject? frame = _usableFrames[i];
            if (frame == null)
                continue;
            if (frame.activeSelf != on)
                frame.SetActive(on);
            if (on)
                shown++;
        }
        if (mask != _loggedUsableMask)
        {
            _loggedUsableMask = mask;
            // HW-VERIFY: ModBuild 352 item 5 — the RECEIVER edge of the mirrored per-item pulse.
            // Either tester can be the one watching and the co-player runs at the shipped default
            // level, so it has to print on both machines; change-gated on the wire mask, which is
            // human-paced. Read it against the owner's "Item usable mask SENT" line: the same hex on
            // both machines with no frames here means the MIRROR failed, not the mask.
            VRLog.Note("Net", $"Remote ITEM usable [player {_owner.PlayerId}]: mask 0x{mask:X4} "
                + $"(record 35) -> {shown} of {count} mirrored chip(s) framed. The mask is over "
                + "Inventory.AllItems RAW index and the arc skips null entries, so the bits are "
                + "re-seated by counting non-nulls; a frame on the neighbouring card would mean "
                + "that mapping, not the wire, is wrong.");
        }
    }

    /// <summary>Drop every usable frame with the slabs they hang off. Index-parallel with
    /// <see cref="_cards"/>: a frame that outlived its slab would be an orphan canvas on a peer's
    /// board, and one that outlived the LIST would be re-seated onto another card.</summary>
    private void ClearUsableFrames()
    {
        for (int i = 0; i < _usableFrames.Count; i++)
        {
            if (_usableFrames[i] != null)
                Object.Destroy(_usableFrames[i]);
        }
        _usableFrames.Clear();
        _usableSlots.Clear();
        _usableMask = 0;
        _usableBuiltCount = -1;
        _loggedUsableMask = 0;
    }

    /// <summary>One usable FRAME per arc slot, index-parallel with <see cref="_cards"/>; null until
    /// that position has been framed once (a fan nobody can play out of costs nothing).</summary>
    private readonly System.Collections.Generic.List<GameObject?> _usableFrames = new();

    /// <summary>Reused mapping buffer: one flag per ARC slot, produced from the RAW-index mask.
    /// </summary>
    private readonly System.Collections.Generic.List<bool> _usableSlots = new();

    private ushort _usableMask;
    private int _usableBuiltCount = -1;
    private ushort _loggedUsableMask;

    private void Hide()
    {
        // FIRST, always: a slab parented to the mirrored recess is NOT under _root, so deactivating
        // the root would leave it lying on that board with no fan behind it. No glide home — the
        // fan is going away, and a card gliding into an arc nobody can see is worse than the card
        // simply not being there (the same argument the gate-hidden branch in Tick makes).
        //
        // …EXCEPT when the card is DELIBERATELY left lying there (see _clipDetached): "no fan behind
        // it" is then the intended state, not an orphan. Its front stays resolved too — hiding the
        // faces would leave the peer looking at a blank back-slab in the recess while the owner is
        // looking at the real card, which is the divergence the 1:1 ruling forbids.
        if (!_clipDetached)
        {
            ReleaseClip(returnToArc: false);
            _returnIndex = -1;
            _returnGlide = 0f;
            _loggedClip = -2;
            _fronts.HideAll(); // a hidden fan keeps no game-widget clones alive
        }
        if (_loggedCount > 0)
        {
            _loggedCount = 0;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closed.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _emergeElapsed = -1f;   // next appearance emerges out of the stack again
        _collapseElapsed = -1f;
        ClearCollapseCapture();
        ClearPops();            // a re-opened fan never starts with a stale chip lifted
        // …and a re-opened fan re-reads WHICH chips lie tapped on its first frame rather than
        // wearing up to a cadence tick of the flags it closed with. An item is very often spent
        // BETWEEN a close and the next open — that is what the fan is opened for.
        _spentResolvedCount = -1;
    }

    public void Destroy()
    {
        _fronts.Destroy();
        ClearUsableFrames();
        // The clipped slab hangs off the mirrored recess, so destroying _root would not take it
        // with it — it has to be destroyed in its own right or it outlives the whole fan on that
        // peer's board.
        if (_clipIndex >= 0 && _clipIndex < _cards.Count && _cards[_clipIndex] != null)
            Object.Destroy(_cards[_clipIndex]);
        _clipIndex = -1;
        _clipSettle = 0f;
        _clipDetached = false;
        _soloElapsed = -1f;
        _returnIndex = -1;
        _returnGlide = 0f;
        _cards.Clear();
        ClearCollapseCapture();
        _builtCount = -1;
        // Round 17: the body mesh is CardMesh's SHARED cache (AttachBody) — never ours to destroy.
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}

using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE FRONT OF ONE CARD A PEER IS PHYSICALLY HOLDING — the receiver half of extension record
/// <see cref="NetProtocol.ExtIdHeldCardFace"/>, one instance per held-card POSE SLOT.
///
/// <para>WHAT IT CLOSES (2026-09-02 multiplayer report, item 6): "Die Vorderseite SOLL man sehen
/// auch von Karten die ein Spieler gerade in der Hand hat. Nur die Rückseite angezeigt werden soll
/// nur in der Auswahlphase, in allen anderen Phasen sollen die Karten immer sichtbar sein, egal ob
/// auf dem Fächer oder in der Hand eines Mitspielers." The rule he is restating has been
/// implemented since 2026-08-08 as <see cref="RevealGate.ShowRoundCardFronts"/> and ~20 remote card
/// surfaces route through it. The ONE surface it could not reach was the card in a peer's hand,
/// which <c>RemoteAvatar.BuildCardSlab</c> draws as a back on both faces.</para>
///
/// <para>THE REASON THAT SURFACE WAS UNREACHABLE WAS WRONG, and the argument is worth restating
/// here because the version that stood in <c>RemoteAvatar</c> for eight builds is the version a
/// future reader will otherwise re-derive. It ran: every other surface was free to turn face-up
/// because the card identities were already on this client in the host-replicated model, but "which
/// of my cards is pinched between my fingers" is a VR fact that exists nowhere in that model, so
/// naming it would mean putting a CARD ID in a packet. The first half is true; the conclusion is
/// not. The receiver never needed an identity — it needed a POSITION in a list it is already
/// reading in full to draw the fan the card was plucked out of. That is what arrives here.</para>
///
/// <para>THE GATE IS THE SAME CALL, NOT A COPY OF THE RULE. <see cref="Resolve"/> asks
/// <see cref="RevealGate.InScenario"/> and <see cref="RevealGate.ShowRoundCardFronts"/> for the
/// character this peer's board is displaying — the identical pair
/// <see cref="RemotePileFronts.Tick"/> asks, evaluated EVERY frame for the same reason it states
/// (a leak window that exists only for a cadence tick is still a leak window). During the game's
/// secret <c>SelectAbilityCardsOrLongRest</c> window the slab is a back and no clone exists at all,
/// so the borrow path has nothing to borrow. Nothing here widens the window and nothing here
/// re-implements it.</para>
///
/// <para>THE LENGTH BYTE IS THE SAFETY, and it is why this can be trusted where a bare index could
/// not. ModBuild 351 shipped a stopgap in <see cref="RemoteHandFan"/> because this client's copy of
/// a peer's hand can lag a whole choreographer turn behind theirs — an owner burns a card and every
/// face after it draws one seat out. A shifted FRONT is wrong in a way the player cannot read and
/// would act on. So the sender states how long the list it indexed was, and a front is refused
/// outright unless this client's copy is exactly that long. The record can fail to draw a card; it
/// cannot draw the wrong one.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. Every list is walked read-only, the face is a throwaway
/// CLONE built by <see cref="RemoteCardArt"/> from the game's own widget, and the body mesh comes
/// out of <see cref="CardMesh"/>'s shared cache (never ours to destroy).</para>
/// </summary>
internal sealed class RemoteHeldCardFace
{
    private readonly RemoteAvatar _owner;
    private readonly int _slot;                 // 1 or 2 — the POSE SLOT this instance serves

    private Transform? _slab;                   // the slab we are overlaying (owned by RemoteAvatar)
    private RemoteCardArt? _art;
    private MeshFilter? _filter;
    private MeshRenderer? _renderer;

    /// <summary>The body kind the slab's mesh currently wears. An ITEM card is nearly square and an
    /// ability card is tall; drawing an item face on the ability silhouette letterboxes it, which is
    /// the same "wrong-aspect box" defect <see cref="RemoteItemFan"/>'s own note records.
    ///
    /// <para>CHOSEN FROM THE WIRE, NOT FROM THE RESOLVED FACE — see <see cref="KindOf"/>. It used to
    /// be swapped WITH the face and swapped back when the card was put down, which meant a card
    /// whose front is refused (the game's own secret selection window) wore the ability shape no
    /// matter what it was: 2026-09-06 report item 5, second half.</para></summary>
    private CardBodyKind _bodyKind = CardBodyKind.Ability;

    // What we last RESOLVED, so the walk over the peer's lists runs on an edge + a cadence rather
    // than every frame; the SHOW call below is per-frame on purpose (RemoteCardArt dedups it and
    // uses the steady path for its mip-bake upkeep, exactly as the hand fan does).
    private byte _resolvedCode = 0xFF;
    private byte _resolvedCount;
    private int _resolvedActor;
    private float _nextResolveAt;
    private FullAbilityCard? _face;             // resolved ability face, if any
    private CItem? _item;                       // resolved item, if any

    /// <summary>The resolved MAP-ROOM loadout card, if any. A separate field from
    /// <see cref="_face"/> because the map room has no <c>FullAbilityCard</c> widget to point at —
    /// there is no <c>CardsHandManager</c> there — so the face is printed from the model through
    /// <see cref="RemoteAbilityCardSource.ShowFullFace"/>, the same call the peer's map FAN prints
    /// its own faces with.</summary>
    private CAbilityCard? _mapCard;

    private bool _loggedShown;
    private byte _loggedCode = 0xFF;

    /// <summary>Reused buffers — this runs per peer per frame and must not allocate.</summary>
    private readonly List<AbilityCardUI> _pileBuf = new();

    internal RemoteHeldCardFace(RemoteAvatar owner, int slot)
    {
        _owner = owner;
        _slot = slot;
    }

    /// <summary>
    /// Drive this slot for one frame. <paramref name="slab"/> is the held-card slab
    /// <c>RemoteAvatar.UpdateCardSlab</c> owns (null until the peer has held something);
    /// <paramref name="held"/> is whether a card is in that slot right now; <paramref name="code"/>
    /// and <paramref name="count"/> are the record's two bytes for this slot.
    ///
    /// <para>Never throws for the caller: the whole resolve is inside a try/catch that fails to a
    /// BACK, because this runs inside the avatar tick and an unguarded throw there starves VR
    /// input.</para>
    /// </summary>
    internal void Tick(Transform? slab, bool held, byte code, byte count)
    {
        if (slab == null || !held)
        {
            // REPORTED AS ZERO RATHER THAN NOT REPORTED. A census row that is simply left alone
            // keeps its last reading forever, so a card put down half an hour ago would still be
            // saying "1 BACK" in every line since. Nothing held is a real answer and it prints as
            // one; the row disappears only when the peer does.
            Report(0, 0, "nothing in this hand");
            Hide();
            return;
        }
        if (!ReferenceEquals(slab, _slab))
        {
            // A rebuilt slab (the avatar's slab is lazily built once, but a torn-down peer can
            // hand us a new one) invalidates the overlay bound to the old transform.
            DestroyArt();
            _slab = slab;
            _filter = slab.GetComponent<MeshFilter>();
            _renderer = slab.GetComponent<MeshRenderer>();
        }
        // THE SILHOUETTE IS THE WIRE'S ANSWER, AND IT IS ANSWERED BEFORE THE FACE IS. Keep the body
        // cut to the rectangle the print paints, every frame this slot is live: the ability box is
        // derived from an OBSERVED face size that changes once per session, so a one-shot cut at
        // build time would leave the cold-start guess standing. Two int compares in the steady
        // state (see EnsureBody).
        //
        // …AND IT IS CUT ON EVERY PATH FROM HERE DOWN, INCLUDING THE ONES THAT DRAW A BACK, which is
        // the 2026-09-06 report's item 5, second half: "Weiterhin in der Auswahlphase wenn der
        // Spieler eine Itemkarte nimmt (und man nur die Rückseite sieht) ist die Karteform/Mesh die
        // einer Handkarte in der Hand statt die Viereckige Form der Itemkarte." The kind used to be
        // read off the RESOLVED face (`_item != null ? Item : Ability`), which is a fact that only
        // exists once a FRONT has been resolved — so every back was an ability silhouette by
        // construction, and a card whose front is refused on purpose (the game's own secret
        // SelectAbilityCardsOrLongRest window, exactly the phase he names) could never be anything
        // else. The wire has always known better: record 36's code byte carries the SOURCE LIST in
        // bits 5..7, and HeldFaceListItems IS "this is an item card". No new field, and no face has
        // to be resolvable for the shape to be right.
        //
        // Reading the LIST rather than HeldFaceNamesCard on purpose: a sender that knows the list
        // but could not seat the card (HeldFaceIndexUnknown) still knows it is an item, and the
        // shape of the back is not a secret in any phase — it is the same silhouette the owner and
        // every onlooker can see in his hand.
        EnsureBody(KindOf(code));
        if (!NetProtocol.HeldFaceNamesCard(code))
        {
            // A CARD IS IN THEIR FIST AND THE RECORD NAMES NO SEAT FOR IT — the state the 2026-09-05
            // evidence turned out to be made of, and the one state the old code reported as if
            // nothing were being drawn at all. It is a BACK on screen, so it is a BACK in the census,
            // and the rule names the SENDER rather than this receiver: no amount of work here can
            // draw a face for a seat nobody named.
            Report(0, 1, "the sender named no seat for this card (record 36 code 0) — nothing this "
                       + "receiver can do; read the owner's 'Held-card face SENT' line");
            HideKeepBody(code);
            return;
        }

        CPlayerActor? actor = null;
        RevealGate.CardFaceSource source = RevealGate.CardFaceSource.None;
        try
        {
            // ONE CALL, AND IT ANSWERS BOTH HALVES — may this surface show a front, and from WHICH
            // source. It used to be `RevealGate.InScenario && RevealGate.ShowRoundCardFronts(actor)`,
            // which is two questions wearing one boolean: the second is the SECRECY rule and is wide
            // open on the map, the first is a CAPABILITY rule ("can a face be resolved here at all")
            // that is false in the map room BY DEFINITION. Leaving the capability answer standing as
            // the secrecy answer is the ModBuild-192 defect RevealGate's own map-phase block is
            // written about — it was fixed on the hand FAN and never on this surface, so in the map
            // room a peer's fan showed its fronts while the card in his hand showed only its back
            // (report item 5a). The two surfaces now switch on the SAME call, so they cannot drift
            // apart again by one of them being edited.
            //
            // The actor stays optional on purpose: there is no CPlayerActor in the map room at all,
            // and asking a CMapCharacter for one THROWS. A null actor is now the map room's normal
            // state rather than a refusal.
            actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            source = RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: reveal "
                              + $"gate threw ({ex.Message}) — showing the back.");
            source = RevealGate.CardFaceSource.None;
        }
        if (source == RevealGate.CardFaceSource.None)
        {
            Report(0, 1, "RevealGate.CardFaces(Selectable) named no source — the game's own secret "
                       + "SelectAbilityCardsOrLongRest window, or no context to resolve a face in");
            // A BACK, BUT STILL AN ITEM-SHAPED BACK. This branch is the report's second half almost
            // word for word — "in der Auswahlphase … man nur die Rückseite sieht" — and it used to
            // call Hide(), which resets the silhouette to ABILITY. The gate governs the FACE; it
            // has nothing to say about the outline. See the EnsureBody call above.
            HideKeepBody(code);
            return;
        }

        int actorId = NetFigures.StableActorId(actor);
        bool due = code != _resolvedCode || count != _resolvedCount || actorId != _resolvedActor
                   || Time.unscaledTime >= _nextResolveAt;
        if (due)
        {
            _nextResolveAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
            _resolvedCode = code;
            _resolvedCount = count;
            _resolvedActor = actorId;
            _face = null;
            _item = null;
            _mapCard = null;
            try
            {
                Resolve(actor, source, code, count);
            }
            catch (System.Exception ex)
            {
                _face = null;
                _item = null;
                _mapCard = null;
                VRLog.Warn("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: front "
                                  + $"resolve failed ({ex.Message}) — showing the back.");
            }
        }

        if (_face == null && _item == null && _mapCard == null)
        {
            Report(0, 1, $"the seat the sender named ({Describe(code, count)}) did not resolve on "
                       + "this client — a list of a different length, or an empty seat");
            HideArtKeepResolve();
            return;
        }

        // The kind is already cut from the wire list at the top of Tick — this is the belt that says
        // the two agree. They cannot disagree by construction (only HeldFaceListItems resolves an
        // _item, and only the other lists resolve a face), and if a future list ever breaks that the
        // WIRE is the answer, because it is the one both a front and a back can be drawn from.
        EnsureBody(KindOf(code));
        RemoteCardArt art = EnsureArt();
        bool shown = _item != null
            ? RemoteItemCardSource.ShowFace(art, _item)
            : _mapCard != null
                ? RemoteAbilityCardSource.ShowFullFace(art, null, _mapCard)
                  != RemoteAbilityCardSource.FacePath.None
                : art.ShowFront(_face!);
        if (!shown)
        {
            Report(0, 1, "the seat resolved but the face CLONE failed to build");
            HideArtKeepResolve();
            return;
        }
        Report(1, 0, $"{source} — resolved {Describe(code, count)} against this client's own copy of "
                   + "that host-replicated list");
        if (!_loggedShown || code != _loggedCode)
        {
            _loggedShown = true;
            _loggedCode = code;
            // HW-VERIFY: ModBuild 352 item 6 — the RECEIVER edge of the held-card front. Either
            // tester can be the one watching, and the co-player runs at the shipped default level,
            // so this has to print on both machines. One line per pluck (change-gated on the wire
            // code). Read together with the sender's "Held-card face SENT": that line naming a seat
            // while this one is missing says the MIRROR failed, not the sampler.
            VRLog.Note("Net", $"Remote held card FRONT [player {_owner.PlayerId} slot {_slot}]: "
                + $"showing {Describe(code, count)} — resolved from THIS client's own copy of that "
                + "host-replicated list, which is exactly as long as the sender said. No card "
                + $"identity crossed the wire; RevealGate.CardFaces named {source} as the "
                + "source"
                + (source == RevealGate.CardFaceSource.MapLoadout
                    ? ", i.e. the MAP ROOM — no scenario is running, there is no CPlayerActor here, "
                      + "and the face comes from the peer's replicated map loadout through the same "
                      + "list his mirrored fan is drawing (report item 5a: this surface used to show "
                      + "only a back here while that fan showed fronts)."
                    : $" for '{Board.CharacterFocus.Describe(actor)}'."));
        }
    }

    /// <summary>
    /// Seat <paramref name="code"/> in the peer's own copy of the list it names, leaving
    /// <see cref="_face"/> / <see cref="_item"/> null when it cannot be seated — which lands the
    /// caller on the BACK the build before this one drew.
    ///
    /// <para>THE LENGTH CHECK IS THE FIRST THING EVERY BRANCH DOES, and it is not defensive
    /// padding: an index is only a name while both clients' copies of the list agree, and the one
    /// failure this record must not have is a front drawn on the wrong card. Every list here is
    /// built by the SAME expression the sender indexed into
    /// (<c>LocalRigSampler.NameHeldCard</c>) and by the same expression the corresponding fan
    /// mirror already resolves its own fronts from, so a filter that drifts in one place is caught
    /// by the count in the other.</para>
    /// </summary>
    private void Resolve(CPlayerActor? actor, RevealGate.CardFaceSource source, byte code, byte count)
    {
        byte list = NetProtocol.HeldFaceList(code);
        int at = NetProtocol.HeldFaceIndex(code);

        // THE MAP ROOM. There is no CPlayerActor, no CardsHandManager and no cardsUI here, so the
        // list is the peer's map LOADOUT and it is asked of the peer's OWN hand fan rather than
        // re-resolved: RemoteHandFan already resolves that character's loadout for the fan beside
        // this card (through the record-20 character key), and a second resolve here could answer
        // with a different character on the frame the peer switches. One resolve, one answer, and
        // the seat this record names is a seat in the very list the fan is drawing.
        if (source == RevealGate.CardFaceSource.MapLoadout)
        {
            if (list != NetProtocol.HeldFaceListMapLoadout)
                return; // a scenario list named while no scenario is running — say nothing
            _mapCard = _owner.HandFan?.MapLoadoutSeat(at, count);
            return;
        }
        if (list == NetProtocol.HeldFaceListMapLoadout)
            return; // a map list named inside a running scenario — likewise say nothing

        if (actor == null)
            return;

        if (list == NetProtocol.HeldFaceListItems)
        {
            CInventory? inv = actor.Inventory;
            List<CItem>? all = inv != null ? inv.AllItems : null;
            if (all == null || all.Count != count || at >= all.Count)
                return;
            _item = all[at];   // RAW index, nulls included — the sender's index space
            return;
        }

        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = manager != null ? manager.GetHand(actor) : null;
        if (hand == null)
            return;

        if (list == NetProtocol.HeldFaceListDiscard || list == NetProtocol.HeldFaceListBurnt)
        {
            // THE PILE ARC'S INDEX SPACE, as one call. This used to be GetPileWidgets followed by
            // an inline PileWidgetIsArcMember narrowing written out here — while the SENDER
            // (LocalRigSampler.NameHeldCard) indexed and counted the raw getter. Two expressions
            // for one wire index space is the defect this record's own doc block forbids; both
            // sides now call CardsGameApi.GetPileArcWidgets and there is nothing to keep in step.
            CardsGameApi.GetPileArcWidgets(hand, list == NetProtocol.HeldFaceListBurnt, _pileBuf);
            if (_pileBuf.Count != count || at >= _pileBuf.Count)
            {
                _pileBuf.Clear();
                return;
            }
            AbilityCardUI widget = _pileBuf[at];
            _face = widget != null ? widget.fullAbilityCard : null;
            _pileBuf.Clear();
            return;
        }

        if (list != NetProtocol.HeldFaceListHand)
            return;
        List<AbilityCardUI>? cards = hand.cardsUI;
        if (cards == null)
            return;
        // THE SHARED MEMBERSHIP EXPRESSION, not a fourth copy of its terms. This spelled out
        // `CardType == Hand && fullAbilityCard != null`, which was a copy of the receiver's fan
        // filter of the day and agreed with NEITHER the owner's own arc nor the sender's sampler —
        // so the seat this record named and the seat counted to here were two different index
        // spaces the moment a long-rest placeholder sat in the hand. See CardsGameApi.HandFanMember.
        int n = 0;
        AbilityCardUI? found = null;
        for (int i = 0; i < cards.Count; i++)
        {
            if (!CardsGameApi.HandFanMember(cards[i], actor))
                continue;
            if (n == at)
                found = cards[i];
            n++;
        }
        if (n != count || found == null)
            return;
        _face = found.fullAbilityCard;
    }

    /// <summary>
    /// THE BODY BOX THIS SLAB IS CUT TO, for <paramref name="kind"/> — and the fix for the
    /// 2026-09-05 report item 1: the printed front on a peer's held card was too small for its body,
    /// so a frame of card back stood around it, and the owner sees no such frame on their own card.
    ///
    /// <para>THE RIM WAS ARITHMETIC, AND IT WAS THE SAME ARITHMETIC THE HAND FAN ALREADY FIXED.
    /// <see cref="RemoteCardArt"/> letterboxes a cloned face into the box it is handed and insets it
    /// by <see cref="RemoteCardArt.BorderFraction"/>, so a 63.5 x 88.0 mm box PRINTS 54.04 x 82.72 mm
    /// (a 294 x 450 px face, height-limited fit, times 0.94). This slab handed the overlay the
    /// NOMINAL card box and cut its body to the nominal card box as well — so 4.73 mm of card back
    /// per side at the sides and 2.64 mm at the ends stood around the print. That rim is the report's
    /// "Rahmen", and the same numbers made the peer's card 17.5 % WIDER than the owner's own, which
    /// is the 1:1 breach he names in the same sentence: the OWNER's card has no such rim because
    /// <c>VRCard.SetCanvasSize</c> scales the local BACKING MESH to the printed rect instead of
    /// leaving it at the nominal card. <c>Net/RemoteHandFan</c> adopted that step in ModBuild 305
    /// (its report 12) and this surface did not — the identical defect, one slab over. It is fixed
    /// the same way and through the same shared definition (<c>CardFace.VisibleFaceRect</c>), so a
    /// future third surface cannot pick a third answer.</para>
    ///
    /// <para>AN ITEM CARD IS THE OTHER WAY ROUND AND STAYS THAT WAY. Its box comes from the applied
    /// Item FOOTPRINT — it IS the card's real outline, the way <c>ItemsPile</c> cuts the owner's own
    /// chip — so the body must not shrink; the ART box is grown by <c>1/(1 - BorderFraction)</c>
    /// instead (see <see cref="ArtBox"/>), which is exactly what <see cref="RemoteItemFan"/> does for
    /// the arc this card was plucked out of. Two kinds, one printed-equals-body outcome.</para>
    /// </summary>
    private static Vector2 BodyBox(CardBodyKind kind)
    {
        float w = RemoteHandFan.DefaultCardWidth;
        if (kind == CardBodyKind.Item)
            return new Vector2(w, ItemBoxHeight(w));
        return CardFace.VisibleFaceRect(w, RemoteHandFan.DefaultCardHeight);
    }

    /// <summary>The box handed to <see cref="RemoteCardArt"/> for <paramref name="kind"/>. ABILITY:
    /// the NOMINAL card, because the overlay's own letterbox-and-inset turns that into exactly
    /// <see cref="BodyBox"/>. ITEM: the body box grown by <c>1/(1 - BorderFraction)</c>, which
    /// cancels that inset so the print lands flush on the punched-out item outline.</summary>
    private static Vector2 ArtBox(CardBodyKind kind)
    {
        if (kind != CardBodyKind.Item)
            return new Vector2(RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
        Vector2 body = BodyBox(kind);
        const float k = 1f - RemoteCardArt.BorderFraction;
        return new Vector2(body.x / k, body.y / k);
    }

    /// <summary>Cut the slab's BODY to <see cref="BodyBox"/> for <paramref name="kind"/>. Gated on
    /// the kind AND on <c>CardFace.FacePixelsRevision</c>, because the ability box is derived from
    /// the face pixel size this client has OBSERVED and that answer changes once per session, the
    /// first time a real card face is hosted — a gate on the kind alone would leave this slab cut to
    /// the cold-start guess for the rest of the run. The meshes and the back materials both come out
    /// of <see cref="CardMesh"/>'s shared caches and are never ours to destroy; the two submeshes
    /// keep wearing the back material on both faces exactly as the slab was built, because the FRONT
    /// is an overlay canvas in front of the mesh, not a material slot.</summary>
    private void EnsureBody(CardBodyKind kind)
    {
        if (_filter == null || _renderer == null)
            return;
        if (kind == _bodyKind && _bodyFaceRevision == CardFace.FacePixelsRevision)
            return;
        bool kindChanged = kind != _bodyKind;
        _bodyFaceRevision = CardFace.FacePixelsRevision;
        Vector2 box = BodyBox(kind);
        CardMesh.AttachBody(_filter, kind, box.x, box.y);
        if (kindChanged)
        {
            Material back = CardMesh.CreateBackMaterial(kind);
            _renderer.sharedMaterials = new[] { back, back };
            // The ART box follows the KIND, so an overlay built for the previous one would keep
            // printing at the wrong aspect. Cheap to drop: the clone is a throwaway either way.
            _art?.Destroy();
            _art = null;
        }
        _bodyKind = kind;
        // HW-VERIFY: 2026-09-06 report item 5, second half. This line names the SILHOUETTE the peer
        // is looking at and the fact it was chosen from, so a square item back reads as "body ->
        // Item" here and a wrong-shaped one reads as "body -> Ability" beside a 'Held-card face
        // SENT' line that says items. Change-gated on the kind and on the observed face-pixel
        // revision, i.e. a couple of lines per slot per session, and it must print on the machine
        // WATCHING the other player — so it is at Note, not Info.
        VRLog.Note("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: body -> "
            + $"{kind} at {box.x * 1000f:F2}x{box.y * 1000f:F2} mm — an item card is nearly square "
            + "and an ability card is tall, so the silhouette follows record 36's SOURCE LIST (the "
            + "wire's own answer, available on a BACK too) instead of the resolved face, which only "
            + "exists once a FRONT has been drawn and left every covered item card wearing the "
            + "ability shape; and the ABILITY box is CardFace.VisibleFaceRect, i.e. the rectangle "
            + "the print actually paints, so no card back shows around it (report item 1).");
    }

    /// <summary>The <c>CardFace.FacePixelsRevision</c> the current body was cut for — see
    /// <see cref="EnsureBody"/> for why a kind-only gate was not enough.</summary>
    private int _bodyFaceRevision = -1;

    /// <summary>The item card's box height for <paramref name="w"/>, from the applied Item
    /// FOOTPRINT — the same two static array reads (and the same 0.5x..2x sanity band, degrading to
    /// the same legacy guess) <see cref="RemoteItemFan"/>'s own ResolvedCardH does, so the held slab
    /// and the arc it came out of are cut to one aspect.</summary>
    private static float ItemBoxHeight(float w)
    {
        byte[]? foot = CardMesh.Footprint(CardBodyKind.Item, out int fw, out int fh);
        if (foot == null || fw <= 1 || fh <= 1)
            return w * 1.15f;
        float h = w * (fh / (float)fw);
        return h < w * 0.5f || h > w * 2f ? w * 1.15f : h;
    }

    private RemoteCardArt EnsureArt()
    {
        // The art is bound to the slab transform and inherits its live scale, so the box handed
        // over is the UNSCALED slab box — the same contract the fans' overlays are built on. WHICH
        // box that is per kind, and why it is not simply the body box, is ArtBox's own note.
        Vector2 box = ArtBox(_bodyKind);
        return _art ??= new RemoteCardArt(_slab!, box.x, box.y);
    }

    /// <summary>Drop the drawn front but KEEP what was resolved, so the next frame does not re-walk
    /// the peer's lists just because a clone failed to build.</summary>
    private void HideArtKeepResolve()
    {
        _art?.HideFront();
        if (_loggedShown)
        {
            _loggedShown = false;
            VRLog.Info("Net", $"Remote held card FRONT [player {_owner.PlayerId} slot {_slot}]: "
                + "back to the BACK — the named seat did not resolve on this client (a list that "
                + "does not match the sender's length, an empty seat, or a clone that failed). A "
                + "back is the safe answer; a shifted front is not.");
        }
    }

    /// <summary>
    /// WHICH BODY A HELD CARD WEARS, from record 36's code byte alone — the SOURCE LIST is the kind.
    /// This is the whole of the 2026-09-06 report item 5's second half: it is the one answer that
    /// exists on every path, including the ones on which no face may be resolved, whereas the
    /// resolved <see cref="_item"/> it replaced exists only after a FRONT has been drawn.
    ///
    /// <para>Deliberately total: an unknown or reserved list, and a code naming nothing at all,
    /// answer ABILITY — which is the silhouette this surface drew before record 36 existed, and the
    /// one an ability card (much the commoner case) actually needs.</para>
    /// </summary>
    private static CardBodyKind KindOf(byte code)
        => NetProtocol.HeldFaceList(code) == NetProtocol.HeldFaceListItems
            ? CardBodyKind.Item
            : CardBodyKind.Ability;

    /// <summary>Drop the front and the resolve, but cut the body to the kind
    /// <paramref name="code"/> names — the BACK paths taken while a card really is in this peer's
    /// fist. See <see cref="KindOf"/>.</summary>
    private void HideKeepBody(byte code) => Hide(KindOf(code));

    /// <summary>Nothing is held in this slot: drop the front, restore the ability body, and forget
    /// the resolve so the next hold starts clean.</summary>
    internal void Hide() => Hide(CardBodyKind.Ability);

    private void Hide(CardBodyKind kind)
    {
        _art?.HideFront();
        _face = null;
        _item = null;
        _mapCard = null;
        _resolvedCode = 0xFF;
        _resolvedCount = 0;
        _resolvedActor = 0;
        EnsureBody(kind);
        if (_loggedShown)
        {
            _loggedShown = false;
            _loggedCode = 0xFF;
            VRLog.Info("Net", $"Remote held card FRONT [player {_owner.PlayerId} slot {_slot}]: "
                + "hidden — the card was put down, or the secret selection phase closed the gate.");
        }
    }

    private void DestroyArt()
    {
        _art?.Destroy();
        _art = null;
        _filter = null;
        _renderer = null;
        _bodyKind = CardBodyKind.Ability;
    }

    internal void Destroy()
    {
        DestroyArt();
        _slab = null;
        _pileBuf.Clear();
    }

    /// <summary>This slot's verdict for the per-population census — see
    /// <see cref="PeerCardFaceCensus"/> for why a sampler exists beside this class's own
    /// change-gated lines. One call per frame per slot, on every path including the ones that draw
    /// nothing: a population that reports only when it succeeds cannot be distinguished from one
    /// that is not running, and that ambiguity is what made the previous round's logs silent about
    /// this exact surface.</summary>
    private void Report(int fronts, int backs, string rule) =>
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.HeldCard, _owner.PlayerId,
                                  fronts, backs, $"slot {_slot}: {rule}");

    /// <summary>One slot as a log phrase — the LIST and the SEAT, never a card. Deliberately the
    /// same wording the sender's edge log uses, so the two lines can be read as a pair.</summary>
    private static string Describe(byte code, byte count)
    {
        byte list = NetProtocol.HeldFaceList(code);
        string where = list switch
        {
            NetProtocol.HeldFaceListHand => "hand fan",
            NetProtocol.HeldFaceListDiscard => "discard pile",
            NetProtocol.HeldFaceListBurnt => "burnt pile",
            NetProtocol.HeldFaceListItems => "items (AllItems raw index)",
            NetProtocol.HeldFaceListMapLoadout => "map-room loadout",
            _ => "list " + list,
        };
        return $"{where} seat {NetProtocol.HeldFaceIndex(code)} of {count}";
    }
}

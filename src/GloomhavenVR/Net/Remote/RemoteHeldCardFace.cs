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
    /// the same "wrong-aspect box" defect <see cref="RemoteItemFan"/>'s own note records. The body
    /// is therefore swapped with the face and swapped back when the card is put down.</summary>
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
        if (slab == null || !held || !NetProtocol.HeldFaceNamesCard(code))
        {
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

        CPlayerActor? actor = null;
        bool open = false;
        try
        {
            actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            // THE GATE, every frame and in one place: the same pair RemotePileFronts.Tick asks.
            open = actor != null && RevealGate.InScenario && RevealGate.ShowRoundCardFronts(actor);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: reveal "
                              + $"gate threw ({ex.Message}) — showing the back.");
            open = false;
        }
        if (!open)
        {
            Hide();
            return;
        }

        int actorId = NetFigures.StableActorId(actor!);
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
            try
            {
                Resolve(actor!, code, count);
            }
            catch (System.Exception ex)
            {
                _face = null;
                _item = null;
                VRLog.Warn("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: front "
                                  + $"resolve failed ({ex.Message}) — showing the back.");
            }
        }

        if (_face == null && _item == null)
        {
            HideArtKeepResolve();
            return;
        }

        EnsureBody(_item != null ? CardBodyKind.Item : CardBodyKind.Ability);
        RemoteCardArt art = EnsureArt();
        bool shown = _item != null
            ? RemoteItemCardSource.ShowFace(art, _item)
            : art.ShowFront(_face!);
        if (!shown)
        {
            HideArtKeepResolve();
            return;
        }
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
                + "identity crossed the wire; RevealGate.ShowRoundCardFronts is open for "
                + $"'{Board.CharacterFocus.Describe(actor)}'.");
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
    private void Resolve(CPlayerActor actor, byte code, byte count)
    {
        byte list = NetProtocol.HeldFaceList(code);
        int at = NetProtocol.HeldFaceIndex(code);

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
            CardsGameApi.GetPileWidgets(hand, list == NetProtocol.HeldFaceListBurnt, _pileBuf);
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
        // RemoteHandFan.ResolveHandFronts' filter, term for term — and the sender's, term for term.
        int n = 0;
        AbilityCardUI? found = null;
        for (int i = 0; i < cards.Count; i++)
        {
            AbilityCardUI c = cards[i];
            if (c == null || c.CardType != CardPileType.Hand || c.fullAbilityCard == null)
                continue;
            if (n == at)
                found = c;
            n++;
        }
        if (n != count || found == null)
            return;
        _face = found.fullAbilityCard;
    }

    /// <summary>Swap the slab's BODY between the ability and item silhouettes, change-gated to a
    /// single enum compare. The meshes and the back materials both come out of
    /// <see cref="CardMesh"/>'s shared caches and are never ours to destroy — the two submeshes keep
    /// wearing the back material on both faces exactly as the slab was built, because the FRONT is
    /// an overlay canvas in front of the mesh, not a material slot.</summary>
    private void EnsureBody(CardBodyKind kind)
    {
        if (kind == _bodyKind || _filter == null || _renderer == null)
            return;
        float w = RemoteHandFan.DefaultCardWidth;
        float h = kind == CardBodyKind.Item ? ItemBoxHeight(w) : RemoteHandFan.DefaultCardHeight;
        CardMesh.AttachBody(_filter, kind, w, h);
        Material back = CardMesh.CreateBackMaterial(kind);
        _renderer.sharedMaterials = new[] { back, back };
        _bodyKind = kind;
        VRLog.Info("Net", $"Remote held card [player {_owner.PlayerId} slot {_slot}]: body -> "
            + $"{kind} at {w * 1000f:F1}x{h * 1000f:F1} mm — an item card is nearly square and an "
            + "ability card is tall, so the silhouette follows the face instead of letterboxing it.");
    }

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
        float w = RemoteHandFan.DefaultCardWidth;
        float h = _bodyKind == CardBodyKind.Item
            ? ItemBoxHeight(w)
            : RemoteHandFan.DefaultCardHeight;
        // The art is bound to the slab transform and inherits its live scale, so the box handed
        // over is the UNSCALED slab box — the same contract the fans' overlays are built on.
        return _art ??= new RemoteCardArt(_slab!, w, h);
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

    /// <summary>Nothing is held in this slot (or the secret window is open): drop the front,
    /// restore the ability body, and forget the resolve so the next hold starts clean.</summary>
    internal void Hide()
    {
        _art?.HideFront();
        _face = null;
        _item = null;
        _resolvedCode = 0xFF;
        _resolvedCount = 0;
        _resolvedActor = 0;
        EnsureBody(CardBodyKind.Ability);
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
            _ => "list " + list,
        };
        return $"{where} seat {NetProtocol.HeldFaceIndex(code)} of {count}";
    }
}

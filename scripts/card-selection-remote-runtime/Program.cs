using System;
using System.Linq;
using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new InvalidOperationException(reason); }

    private static PresenceState Transfer(PlayTray? tray, RemoteAvatar peer, RemoteBoardFurniture furniture,
        int handCount = 0, int[]? order = null, CardsHandUI? hand = null)
    {
        PresenceState published = OwnerPublication.Publish(tray, hand);
        published.HandCardCount = (byte)handCount;
        published.HasFanArcOrder = order != null;
        published.FanArcOrder = order;
        published.FanArcOrderCount = order?.Length ?? 0;
        byte[] bytes = new byte[PresenceSerializer.MaxSize];
        int length = PresenceSerializer.Write(in published, bytes);
        Check(length > 0 && PresenceSerializer.TryRead(bytes, length, out _), "production presence packet decodes");
        PresenceSerializer.TryRead(bytes, length, out PresenceState received);
        Check(received.HasBoardUi == published.HasBoardUi, "record presence is not confused with all-clear state");
        Check(received.BoardOverlayMask == published.BoardOverlayMask, "record four overlay byte survives actual wire");
        Check(!received.HasHeldCardFace && !received.HasHeldMapCard, "selection overlay packet has no private identity");
        peer.Apply(in received);
        furniture.TickWire(peer, peer.BoardSlotMask);
        return received;
    }

    private static void Masks()
    {
        var tray = new PlayTray(); var peer = new RemoteAvatar(); var furniture = new RemoteBoardFurniture();
        var first = new VRCard(); var second = new VRCard();
        // Include all sixteen wanted/occupied combinations: e.g. native confirmations,
        // focus-hidden state and transient choices are not always simple complements.
        for (int wanted = 0; wanted < 4; wanted++)
        for (int occupied = 0; occupied < 4; occupied++)
        for (int snap = -1; snap < 2; snap++)
        {
            tray.SetWantedSlots(wanted); tray.HighlightedSlot = snap;
            first.transform.SetParent((occupied & 1) != 0 ? tray.Slot(0) : null);
            second.transform.SetParent((occupied & 2) != 0 ? tray.Slot(1) : null);
            PresenceState p = Transfer(tray, peer, furniture);
            int expected = wanted | (occupied << 3) | 32 | ((snap + 1) << 6);
            Check(peer.SlotOccupancyKnown && peer.BoardSlotMask == occupied, "physical withdrawal clears remote occupancy");
            for (int slot = 0; slot < 2; slot++)
            {
                Check(furniture.WantedActive(slot) == ((wanted & (1 << slot)) != 0), "all wanted masks reach the remote overlay");
                Check(furniture.WantedActive(slot) == tray.WantedActive(slot), "remote wanted activation equals local activation");
                Check(furniture.SnapActive(slot) == (slot == snap), "held hover telegraph follows the same recess");
            }
            Check(p.BoardOverlayMask == expected, "record four uses pinned wanted/occupancy/snap bit layout");
        }
        int writes = furniture.Writes;
        Transfer(tray, peer, furniture);
        Check(writes == furniture.Writes, "identical full snapshots do not restart the overlay");
    }

    private static void WithdrawalAndFocus()
    {
        var tray = new PlayTray(); var peer = new RemoteAvatar(); var furniture = new RemoteBoardFurniture();
        var a = new VRCard(); var b = new VRCard(); var replacement = new VRCard();
        a.transform.SetParent(tray.Slot(0)); b.transform.SetParent(tray.Slot(1));
        tray.SetWantedSlots(0); Transfer(tray, peer, furniture, 6, new[] { 1, 0, 2, 3, 4, 5 });
        // Grip begins before reparenting; the real SlotHoldsCard excludes held children.
        a.IsHeld = true; tray.SetWantedSlots(1);
        Check(tray.OccupiedSlotMask == 2, "held card no longer occupies its former recess");
        Transfer(tray, peer, furniture, 6, new[] { 1, 0, 2, 3, 4, 5 });
        Check(peer.BoardSlotMask == 2 && furniture.WantedActive(0), "withdrawal reopens remote overlay while card is held");
        a.transform.SetParent(null); a.IsHeld = false;
        Transfer(tray, peer, furniture, 7, new[] { 6, 1, 0, 2, 3, 4, 5 });
        Check(peer.HandCardCount == 7 && peer.FanArcOrder!.SequenceEqual(new[] { 6, 1, 0, 2, 3, 4, 5 }), "withdrawal restores actual published fan count and insertion order");
        Check(peer.BoardSlotMask == 2, "physical withdrawal clears remote occupancy");
        // Native deselection may replicate after our cosmetic packet. Occupancy and
        // overlay stay owner-authored; source audit separately binds SeatSlots' empty path.
        for (int delayedNativeFrame = 0; delayedNativeFrame < 8; delayedNativeFrame++)
        {
            Transfer(tray, peer, furniture, 7, new[] { 6, 1, 0, 2, 3, 4, 5 });
            Check(peer.BoardSlotMask == 2 && furniture.WantedActive(0), "late native model cannot reoccupy the published empty recess");
        }
        replacement.transform.SetParent(tray.Slot(0)); tray.SetWantedSlots(0);
        Transfer(tray, peer, furniture, 6, new[] { 1, 0, 2, 3, 4, 5 });
        Check(peer.BoardSlotMask == 3 && !furniture.WantedActive(0), "replacement hides only the filled overlay");
        // Unconfirmed swap has the same final nibble; order still rides a complete packet.
        replacement.transform.SetParent(tray.Slot(1)); b.transform.SetParent(tray.Slot(0));
        Transfer(tray, peer, furniture, 6, new[] { 5, 4, 3, 2, 1, 0 });
        Check(peer.BoardSlotMask == 3 && peer.FanArcOrder!.SequenceEqual(new[] { 5, 4, 3, 2, 1, 0 }), "unchanged occupancy never suppresses updated fan ordering");
        // Focus away/back is a different owner snapshot, not remembered furniture.
        var other = new PlayTray(); other.SetWantedSlots(3);
        Transfer(other, peer, furniture, 9, Enumerable.Range(0, 9).ToArray());
        Check(peer.BoardSlotMask == 0 && peer.WantedGlowMask == 3, "focus switch replaces both recesses and overlays");
        Transfer(tray, peer, furniture, 6, Enumerable.Range(0, 6).ToArray());
        Check(peer.BoardSlotMask == 3 && peer.WantedGlowMask == 0, "focus return restores current physical state");
        replacement.transform.SetParent(null); b.transform.SetParent(null); tray.SetWantedSlots(3);
        // Drop the first changed packet. The following ordinary full snapshot heals it.
        PresenceState lost = OwnerPublication.Publish(tray); _ = lost;
        Transfer(tray, peer, furniture, 8, Enumerable.Range(0, 8).ToArray());
        Check(peer.BoardSlotMask == 0 && peer.WantedGlowMask == 3, "periodic full snapshot heals a lost initial withdrawal packet");
        var latePeer = new RemoteAvatar(); var lateFurniture = new RemoteBoardFurniture();
        Transfer(tray, latePeer, lateFurniture, 8, Enumerable.Range(0, 8).ToArray());
        Check(latePeer.BoardSlotMask == 0 && lateFurniture.WantedActive(0) && lateFurniture.WantedActive(1), "late join reconstructs both empty overlays without event history");
        Transfer(null, peer, furniture);
        Check(!peer.HasBoardUi && !peer.SlotOccupancyKnown && peer.WantedGlowMask == 0, "missing owner board clears published record state");
        Transfer(tray, peer, furniture, 8, Enumerable.Range(0, 8).ToArray());
        Check(peer.SlotOccupancyKnown && peer.BoardSlotMask == 0, "board recreation accepts explicit empty occupancy");
    }

    private static void HeldWire()
    {
        // Actual rig codec runs at a separate cadence. No held-card face address is
        // introduced when a secret card leaves a recess; only pose/state may travel.
        var state = new AvatarState { HasHeldCard = true, WorldScale = 1, DominantRight = true,
            HeldCardPose = new RigPose { Position = new Vector3(.3f, .5f, .7f), Rotation = Quaternion.identity } };
        byte[] bytes = new byte[AvatarSerializer.MaxSize];
        int length = AvatarSerializer.Write(in state, bytes);
        Check(AvatarSerializer.TryRead(bytes, length, out AvatarState received), "held withdrawal rig packet decodes");
        Check(received.HasHeldCard && !received.HasHeldCardFace, "held selection card transmits pose without private face address");
        Check((received.HeldCardPose.Position - state.HeldCardPose.Position).magnitude < .003f, "held withdrawal pose survives original quantization");
        state.HasHeldCard = false;
        length = AvatarSerializer.Write(in state, bytes);
        Check(AvatarSerializer.TryRead(bytes, length, out received) && !received.HasHeldCard, "release packet clears held-card state");
    }

    private static void OccupiedSwap()
    {
        var tray = new PlayTray(); var peer = new RemoteAvatar(); var furniture = new RemoteBoardFurniture();
        var nativeA = new CAbilityCard { Name = "fixture-secret-A", Initiative = 17 };
        var nativeB = new CAbilityCard { Name = "fixture-secret-B", Initiative = 44 };
        var actor = new CPlayerActor(); var hand = new CardsHandUI { PlayerActor = actor };
        actor.CharacterClass.InitiativeAbilityCard = nativeA;
        actor.CharacterClass.RoundAbilityCards.Add(nativeB); actor.CharacterClass.RoundAbilityCards.Add(nativeA);
        var a = new VRCard { FullCard = new FullAbilityCard { abilityCard = nativeA } };
        var b = new VRCard { FullCard = new FullAbilityCard { abilityCard = nativeB } };
        var board = new RemoteControlBoard(peer);
        tray.SetWantedSlots(0);
        for (int swap = 0; swap < 12; swap++)
        {
            bool reversed = swap % 2 == 1;
            a.transform.SetParent(tray.Slot(reversed ? 1 : 0));
            b.transform.SetParent(tray.Slot(reversed ? 0 : 1));
            PresenceState packet = Transfer(tray, peer, furniture, 6, Enumerable.Range(0, 6).ToArray(), hand);
            board.OrderRoundCards(actor);
            Check(peer.BoardSlotMask == 3 && peer.WantedGlowMask == 0, "occupied swap keeps original overlay and occupancy");
            Check(packet.HasSlotOrder && peer.SlotOrderKnown && peer.SlotOrderSwapped == reversed,
                "remote model order follows the two physical recesses");
            Check(ReferenceEquals(board.Ordered(0), reversed ? nativeB : nativeA)
                && ReferenceEquals(board.Ordered(1), reversed ? nativeA : nativeB),
                "remote model order follows the two physical recesses");
            // Native initiative may catch up independently without changing physical order.
            actor.CharacterClass.InitiativeAbilityCard = reversed ? nativeB : nativeA;
            Transfer(tray, peer, furniture, 6, Enumerable.Range(0, 6).ToArray(), hand);
            board.OrderRoundCards(actor);
            Check(ReferenceEquals(board.Ordered(0), reversed ? nativeB : nativeA),
                "initiative replication cannot reverse the owner-authored physical pair");
            actor.CharacterClass.InitiativeAbilityCard = nativeA;
        }
        a.IsHeld = true; tray.SetWantedSlots(1);
        PresenceState withdrawn = Transfer(tray, peer, furniture, 6, Enumerable.Range(0, 6).ToArray(), hand);
        Check(!withdrawn.HasSlotOrder && !peer.SlotOrderKnown, "withdrawn pair forgets its former two-card order");
    }

    private static void VisibilityPolicy()
    {
        var actor = new CPlayerActor();
        FFSNetwork.IsOnline = true; RevealGate.IsSecretSelectionPhase = true;
        Check(!RevealGate.ShowPeerCardFronts(actor), "remote selection cards remain covered even for locally controlled actors");
        Check(GloomhavenVR.Net.CardPresentationPolicy.AllowsFace(true, true, true, true), "local controlled selection cards retain their real fronts");
        Check(!GloomhavenVR.Net.CardPresentationPolicy.AllowsFace(true, true, false, true), "missing actor context cannot reveal a private front");
        RevealGate.IsSecretSelectionPhase = false;
        Check(RevealGate.ShowPeerCardFronts(actor), "remote action phase restores card fronts");
        Check(!RevealGate.ShowPeerCardFronts(null), "missing remote actor stays covered");
        FFSNetwork.IsOnline = false; RevealGate.IsSecretSelectionPhase = true;
        Check(RevealGate.ShowPeerCardFronts(actor), "offline behavior retains original face visibility");
        FFSNetwork.IsOnline = true;
    }

    private static int Main()
    {
        try
        {
            Masks(); WithdrawalAndFocus(); HeldWire(); OccupiedSwap(); VisibilityPolicy();
            Console.WriteLine(checks + " production runtime assertions; hierarchy/activation boundary; no native render/network/HMD claim");
            return 0;
        }
        catch (Exception error)
        { Console.WriteLine("FAIL at assertion " + checks + ": " + error); return 1; }
    }
}

using System;
using System.IO;
using GloomhavenVR.Net;

namespace GloomhavenVR.WireTests;

internal static class CardAppearanceVectors
{
    internal static void Run(Harness t, string root)
    {
        t.Case("58 native card appearance: independent layout and complete frame boundaries");
        var buffer = new byte[CardAppearanceCodec.MaxSize];
        int length = CardAppearanceCodec.Write(new CardAppearanceSnapshot(1f, Array.Empty<CardAppearanceState>()), buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 0C 3A 06 FF 00 00 00 80 3F"), buffer, length, "empty owner frame clears appearance without changing legacy streams");
        var text = new CardAppearanceNode { Role = 7, Flags = 7 };
        for (int i = 0; i < 8; i++) text.Values[i] = 1f;
        var state = new CardAppearanceState { ActorId = 42, FaceCode = 0x20, ListCount = 1, Nodes = new[] { text } };
        var original = new CardAppearanceSnapshot(2f, new[] { state });
        length = CardAppearanceCodec.Write(original, buffer);
        t.Wire(Hex.Bytes("31 52 56 47 03 0C 3A 33 00 01 00 00 00 40 2A 00 00 00 20 01 01 07 07 00 00 00 00 "
            + "00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F 00 00 80 3F"),
            buffer, length, "one native TMP role exact schema including owner renderer colour");
        t.True(CardAppearanceCodec.TryRead(buffer, length, out var read) && CardAppearanceState.Same(original.States[0], read!.States[0]), "native output survives publication");
        for (int n = 0; n < length; n++) t.True(!CardAppearanceCodec.TryRead(buffer, n, out _), "every truncated first node rejects");
        byte[] corrupt = new byte[length + 2]; Array.Copy(buffer, corrupt, length); corrupt[length] = 200;
        t.True(CardAppearanceCodec.TryRead(corrupt, length + 2, out _), "unknown additive trailing record skips by length");
        corrupt[7]--; t.True(!CardAppearanceCodec.TryRead(corrupt, length + 2, out _), "short node cannot borrow following record");
        Array.Copy(buffer, corrupt, length); corrupt[16] = 0; corrupt[17] = 0; // Actor42 still retained; change exact actor separately below.
        Array.Copy(buffer, corrupt, length); Array.Clear(corrupt, 14, 4);
        t.True(!CardAppearanceCodec.TryRead(corrupt, length, out _), "unknown actor is not an appearance address");
        Array.Copy(buffer, corrupt, length); corrupt[19] = 0;
        t.True(!CardAppearanceCodec.TryRead(corrupt, length, out _), "empty source list cannot name a card");
        Array.Copy(buffer, corrupt, length); corrupt[30] = 0x7F; corrupt[29] = 0x80;
        t.True(!CardAppearanceCodec.TryRead(corrupt, length, out _), "nonfinite native channel rejects atomically");
        state.Nodes = new[] { text, text.Copy() };
        bool threw = false; try { _ = new CardAppearanceSnapshot(2, new[] { state }); } catch (ArgumentException) { threw = true; }
        t.True(threw, "duplicate native roles rejected at publication");
        var states = new CardAppearanceState[CardAppearanceState.CountMax];
        for (int c = 0; c < states.Length; c++)
        {
            var nodes = new CardAppearanceNode[CardAppearanceNode.RoleCount];
            for (byte r = 0; r < nodes.Length; r++)
            {
                var node = new CardAppearanceNode { Role = r, Flags = (byte)(r == 11 ? 11 : 3), Mask = CardAppearanceNode.AllowedMask(r), Binding = r >= 12 ? (uint)(r + 1) : 0 };
                for (int f = 0; f < node.Values.Length; f++) if (r < 12 && CardAppearanceNode.Carries(node.Mask, f)) node.Values[f] = (f + 1) * 0.03125f;
                if (r >= 12) node.Values[0] = 0.5f;
                nodes[r] = node;
            }
            states[c] = new CardAppearanceState { ActorId = c + 1, FaceCode = 0x20, ListCount = 1, Nodes = nodes };
        }
        var maximum = new CardAppearanceSnapshot(9, states);
        length = CardAppearanceCodec.Write(maximum, buffer);
        t.True(length == 40966 && length < CardAppearanceCodec.MaxSize, "all native roles, material properties and eight groups fit measured transport bound");
        t.True(CardAppearanceCodec.TryRead(buffer, length, out read) && read!.States.Length == states.Length, "maximum frame complete roundtrip");
        if (read != null) for (int i = 0; i < states.Length; i++) t.True(CardAppearanceState.Same(maximum.States[i], read.States[i]), "every maximum role restored");
        // Boundaries between complete nodes/cards are still incomplete snapshots.
        for (int n = 6; n < length;)
        { int next = n + 2 + buffer[n + 1]; t.True(next == length || !CardAppearanceCodec.TryRead(buffer, next, out _), "complete prefix never publishes partial card population"); n = next; }
        var moved = new CardAppearanceSnapshot(10, states); moved.States[0].ListCount = 2;
        t.True(!CardAppearanceSnapshot.SameIdentity(maximum, moved), "source list count is part of frame identity");
        var low = new CardAppearanceNode { Role = 0, Flags = 19, Mask = 1 };
        low.Values[8] = 0.375f;
        var lowFrame = new CardAppearanceSnapshot(11, new[] { new CardAppearanceState { ActorId = 1, FaceCode = 0x20, ListCount = 1, Nodes = new[] { low } } });
        length = CardAppearanceCodec.Write(lowFrame, buffer);
        t.True(CardAppearanceCodec.TryRead(buffer, length, out read) && (read!.States[0].Nodes[0].Flags & 16) != 0
            && read.States[0].Nodes[0].Values[8] == 0.375f, "owner low-material variant travels independently of viewer settings");
        low.Role = 11;
        t.True(!low.Validate(), "card material variant cannot alias a flame role");
        low.Role = 12; low.Binding = 1; low.Mask = 0;
        t.True(!low.Validate(), "card material variant cannot alias a group role");
        string native = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteCardArt.Native.cs"));
        t.True(native.Contains("CardAppearanceMirror.TryGet") && native.Contains("private void LateUpdate() => Art?.ApplyNativeAppearance()"), "actual output paints after ordinary mirror drivers");
        t.True(native.Contains("_nativeOutputApplied && !ExplicitFlightOwnsLook") && native.Contains("ClearPendingNativeAppearance();"),
            "lost native list authority clears stale material once while explicit flights retain their own burn");
        t.True(native.Contains("!_host.activeInHierarchy") && !native.Contains("!_clone.activeInHierarchy"),
            "owner-hidden clone can reappear; only the policy-owned host blocks sampling");
        t.True(native.Contains("CardAppearanceBindings.AuthoredMaterial(node.Role)") && native.Contains("_nativeBindings.LowMaterial"),
            "both native material variants use their original authored assets");
        t.True(!native.Contains("group.alpha = 1f"), "original group opacity never forcibly normalized");
        string fan = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteHandFan.cs"));
        t.True(!fan.Contains("TickUsedCardFx(count, showFronts, mapFronts);"), "recovered fan never replays hidden local widget effects");
        string recess = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteBoardCard.cs"));
        t.True(recess.Contains("_art.SetNativeAppearance(_materialiseOwner.PlayerId, owner, card);")
            && !recess.Contains("DriveUsedCardFx(playerId, slot, topSpent, bottomSpent);"),
            "ordinary recess clones bind owner output without replaying a synthetic whole-card clock");
        string held = File.ReadAllText(Path.Combine(root, "src/GloomhavenVR/Net/Remote/RemoteHeldCardFace.cs"));
        t.True(fan.Contains("face.SetNativeAppearance(_owner.PlayerId, null, card);")
            && held.Contains("_activeCard ?? _mapCard ??"),
            "map fan and held models clear inherited proxy paint even without a scenario appearance address");
    }
}

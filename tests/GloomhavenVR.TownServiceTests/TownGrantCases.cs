using System;
using GloomhavenVR.Net.TownServices;

internal static class TownGrantCases
{
    private static int _checks;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new Exception("Town grant: " + label); }
    internal static void Run()
    {
        foreach (TownGrantKind kind in Enum.GetValues<TownGrantKind>())
        {
            var message = new TownGrantMessage(kind, 2, 17, 0x01020304, 0x12345678,
                kind == TownGrantKind.Request ? 0u : 0xABCDEF01u,
                kind == TownGrantKind.Release ? 0u : 0x01234567u);
            byte[] bytes = TownServiceGrantCodec.Write(in message);
            Check(bytes.Length == 28 && Convert.ToHexString(bytes, 0, 6) == "315256470319", "stable GVR1 type25 header");
            Check(TownServiceGrantCodec.TryRead(bytes, bytes.Length, out TownGrantMessage result), "round trip " + kind);
            Check(result.Kind == kind && result.Service == 2 && result.Player == 17
                && result.Session == 0x01020304 && result.Nonce == 0x12345678
                && result.Epoch == message.Epoch && result.Sequence == message.Sequence, "identity preserved " + kind);
            for (int length = 0; length < bytes.Length; length++)
                Check(!TownServiceGrantCodec.TryRead(bytes, length, out _), "truncated grant rejected");
            byte[] corrupted = (byte[])bytes.Clone(); corrupted[5] = 19;
            Check(!TownServiceGrantCodec.TryRead(corrupted, corrupted.Length, out _), "cosmetic town packet cannot become a grant");
            corrupted = (byte[])bytes.Clone(); corrupted[7] = 4;
            Check(!TownServiceGrantCodec.TryRead(corrupted, corrupted.Length, out _), "fourth resident rejected");
        }
        var ledger = new TownServiceGrantLedger();
        Check(ledger.Request(1, 2, 10, 11, 1f), "merchant first offer acquired");
        Check(!ledger.Request(1, 3, 20, 21, 1.1f), "same resident second offer denied");
        Check(ledger.Request(2, 3, 20, 21, 1.1f), "other resident independent");
        Check(ledger.Owner(1, 1.1f) == 2 && ledger.Owner(2, 1.1f) == 3, "different residents retain independent owners");
        Check(ledger.TryOwner(1, 1.1f, out int owner, out uint session) && owner == 2 && session == 10,
            "occupation resolves the coordinator's exact physical offer session");
        Check(!ledger.TryOwner(0, 1.1f, out owner, out session) && owner == 0 && session == 0,
            "invalid occupation lookup returns no residual identity");
        Check(!ledger.Request(1, 2, 10, 12, 1.2f), "new offer cannot steal old matching visitor's lease");
        ledger.Release(1, 3, 20, 21);
        Check(ledger.Holds(1, 2, 10, 11, 1.3f), "foreign release inert");
        ledger.Release(1, 2, 10, 12);
        Check(ledger.Holds(1, 2, 10, 11, 1.3f), "wrong nonce release inert");
        Check(ledger.Request(1, 2, 10, 11, 4f), "matching heartbeat renews");
        Check(ledger.Holds(1, 2, 10, 11, 11.9f) && !ledger.Holds(1, 2, 10, 11, 12f), "coordinator lease expires at bounded deadline");
        Check(!ledger.TryOwner(1, 12f, out owner, out session) && owner == 0 && session == 0,
            "expired coordinator reservation cannot retain an occupation badge");
        Check(ledger.Request(1, 3, 20, 21, 12f), "expired resident reassigns");
        ledger.ForgetPeer(3);
        Check(ledger.Owner(1, 12f) == 0 && ledger.Owner(2, 12f) == 0, "disconnect releases every lease of that player");
        Check(!ledger.TryOwner(1, 12f, out owner, out session) && owner == 0 && session == 0,
            "native peer removal clears the public occupant identity");
        Check(ledger.Request(3, 4, 30, 31, 13f), "third NPC independently usable");
        Check(ledger.TryOwner(3, 13f, out owner, out session) && owner == 4 && session == 30,
            "enchantress occupation is independent of merchant reservation");
        ledger.Release(3, 4, 29, 31);
        Check(ledger.TryOwner(3, 13f, out owner, out session), "a prior session release cannot clear the current card's occupant");
        ledger.Release(3, 4, 30, 31);
        Check(!ledger.TryOwner(3, 13f, out owner, out session) && owner == 0 && session == 0,
            "exact physical card release immediately clears its occupant identity");
        ledger.Clear();
        Check(ledger.Owner(3, 13f) == 0, "scene reset clears grant");
        Check(!ledger.TryOwner(3, 13f, out owner, out session) && owner == 0 && session == 0,
            "scene reset leaves no stored occupation identity");
        Console.WriteLine($"Town grant protocol: {_checks} assertions.");
    }
}

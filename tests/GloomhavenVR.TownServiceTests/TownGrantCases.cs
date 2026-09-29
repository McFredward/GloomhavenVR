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
        Check(!ledger.Request(1, 2, 10, 12, 1.2f), "new offer cannot steal old matching visitor's lease");
        ledger.Release(1, 3, 20, 21);
        Check(ledger.Holds(1, 2, 10, 11, 1.3f), "foreign release inert");
        ledger.Release(1, 2, 10, 12);
        Check(ledger.Holds(1, 2, 10, 11, 1.3f), "wrong nonce release inert");
        Check(ledger.Request(1, 2, 10, 11, 4f), "matching heartbeat renews");
        Check(ledger.Holds(1, 2, 10, 11, 11.9f) && !ledger.Holds(1, 2, 10, 11, 12f), "coordinator lease expires at bounded deadline");
        Check(ledger.Request(1, 3, 20, 21, 12f), "expired resident reassigns");
        ledger.ForgetPeer(3);
        Check(ledger.Owner(1, 12f) == 0 && ledger.Owner(2, 12f) == 0, "disconnect releases every lease of that player");
        Check(ledger.Request(3, 4, 30, 31, 13f), "third NPC independently usable");
        ledger.Clear();
        Check(ledger.Owner(3, 13f) == 0, "scene reset clears grant");
        Console.WriteLine($"Town grant protocol: {_checks} assertions.");
    }
}

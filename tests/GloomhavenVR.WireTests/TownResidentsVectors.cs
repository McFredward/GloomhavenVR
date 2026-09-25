using System;
using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>Independent byte vectors for the permanent resident extension. In particular,
/// malformed encoded quaternions must be rejected before the legacy pose reader normalizes them.</summary>
internal static class TownResidentsVectors
{
    private static readonly byte[] Golden = Hex.Bytes(@"
        4F 73 01
        00 00 80 3F 00 00 00 40 00 00 40 40 00 00 00 00 00 00 FF 7F
        00 00 80 3F 00 00 20 40 FF 01 00 00 80 BD 00 00 00 BE
        00 00 80 BF 00 00 00 00 00 00 00 3F 00 00 FF 7F 00 00 00 00
        00 00 00 3F 00 00 40 40 80 00 00 00 80 BD 00 00 00 BE
        00 00 00 00 00 00 00 C0 00 00 80 40 00 40 00 40 00 40 00 40
        00 00 00 40 00 00 00 00 00 01 00 00 80 BD 00 00 00 BE");

    private static TownResidentsState Example() => new()
    {
        Active = true,
        Merchant = Pose(new Vector3(1f, 2f, 3f), Quaternion.identity, 1f, 2.5f, 255, 1),
        Temple = Pose(new Vector3(-1f, 0f, .5f), new Quaternion(0f, 1f, 0f, 0f), .5f, 3f, 128, 0),
        Enchantress = Pose(new Vector3(0f, -2f, 4f), new Quaternion(.5f, .5f, .5f, .5f), 2f, 0f, 0, 1)
    };
    private static TownResidentPose Pose(Vector3 position, Quaternion rotation, float scale, float age, byte visibility, byte clip) =>
        new() { Pose = new RigPose { Position = position, Rotation = rotation }, Scale = scale, Age = age, Visibility = visibility, Clip = clip, ActorFloorOffset = -.0625f, FurnitureBottom = -.125f };
    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private static TownResidentsState WithCloth()
    {
        var state = Example(); state.HasCloth = true;
        state.TempleLeft = new TownClothRunnerState
        { Left = new Vector2(.015f, -.010f), Right = new Vector2(-.020f, .030f),
            LeftVelocity = new Vector2(.040f, -.020f), RightVelocity = new Vector2(-.060f, .080f) };
        state.TempleRight = new TownClothRunnerState
        { Left = new Vector2(-.010f, .020f), Right = new Vector2(.025f, -.015f),
            LeftVelocity = new Vector2(-.020f, .030f), RightVelocity = new Vector2(.040f, -.040f) };
        state.EnchantressCloth = new TownClothRunnerState
        { Left = new Vector2(.005f, -.005f), Right = new Vector2(-.007f, .009f),
            LeftVelocity = new Vector2(.012f, -.014f), RightVelocity = new Vector2(-.018f, .020f) };
        return state;
    }
    private static void FloatAt(byte[] b, int offset, float value) => BitConverter.GetBytes(value).CopyTo(b, offset);
    private static byte[] Packet(byte[] record)
    {
        byte[] header = Hex.Bytes("31 52 56 47 03 01 80 00 80 00 02");
        var bytes = new byte[header.Length + record.Length + 4];
        header.CopyTo(bytes, 0); record.CopyTo(bytes, header.Length);
        Hex.Bytes("03 02 1E 02").CopyTo(bytes, header.Length + record.Length); // build542 after resident data
        return bytes;
    }

    internal static void Run(Harness t)
    {
        t.Case("town residents79: fixed independent golden bytes and explicit opt-out");
        var state = Example(); var bytes = new byte[PresenceSerializer.MaxSize]; int offset = 0;
        t.True(TownResidentsCodec.Write(bytes, ref offset, in state), "three residents serialize");
        t.Wire(Golden, bytes, offset, "golden79 freezes identity, lengths, order, signed poses, scales, clocks and clips");
        t.True(TownResidentsCodec.TryRead(Golden, 2, 115, out var decoded) && decoded.Active,
            "reader independently consumes literal golden79");
        for (int n = 0; n < 3; n++)
        {
            var expected = state.At(n); var actual = decoded.At(n);
            t.True(expected.Pose.Position == actual.Pose.Position && Quaternion.Angle(expected.Pose.Rotation, actual.Pose.Rotation) < .01f
                && expected.Scale == actual.Scale && expected.Age == actual.Age
                && expected.ActorFloorOffset == actual.ActorFloorOffset && expected.FurnitureBottom == actual.FurnitureBottom
                && expected.Visibility == actual.Visibility && expected.Clip == actual.Clip,
                "resident " + n + " preserves its own pose and animation");
        }
        var presence = new PresenceState { HasTownResidents = true, TownResidents = state };
        int length = PresenceSerializer.Write(in presence, bytes);
        var complete = new byte[11 + Golden.Length];
        Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01").CopyTo(complete, 0); Golden.CopyTo(complete, 11);
        t.Wire(complete, bytes, length, "presence writer declares exactly one resident extension");
        t.True(PresenceSerializer.TryRead(complete, complete.Length, out var full) && full.HasTownResidents && full.TownResidents.Active,
            "full extras reader exposes independent persistent population");
        presence.TownResidents.Active = false;
        length = PresenceSerializer.Write(in presence, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 80 00 80 00 01 4F 01 00"), bytes, length,
            "opt-out is explicit and contains no stale poses");
        t.True(PresenceSerializer.TryRead(bytes, length, out full) && full.HasTownResidents && !full.TownResidents.Active,
            "opt-out differs from an older client with no resident extension");
        presence.HasTownResidents = false; presence.TownResidents = state;
        length = PresenceSerializer.Write(in presence, bytes);
        t.Wire(Hex.Bytes("31 52 56 47 03 01 00 00"), bytes, length, "omission preserves legacy idle bytes exactly");
        t.True(PresenceSerializer.TryRead(bytes, length, out full) && !full.HasTownResidents && !full.TownResidents.Active,
            "fresh decode never inherits a previous permanent population");

        t.Case("town residents79: owner cloth controls append after the unchanged pose prefix");
        var cloth = WithCloth(); var extended = new byte[Golden.Length + 24]; Golden.CopyTo(extended, 0);
        extended[1] = 139;
        Hex.Bytes("0F F6 EC 1E 14 F6 E2 28 F6 14 19 F1 F6 0F 14 EC 05 FB F9 09 06 F9 F7 0A")
            .CopyTo(extended, Golden.Length);
        offset = 0;
        t.True(TownResidentsCodec.Write(bytes, ref offset, in cloth), "owner writes complete cloth controls");
        t.Wire(extended, bytes, offset, "literal signed millimetre/velocity tail preserves the old pose prefix");
        t.True(TownResidentsCodec.TryRead(extended, 2, 139, out decoded) && decoded.HasCloth,
            "observer reads explicit cloth tail");
        t.True(decoded.TempleLeft.Left == cloth.TempleLeft.Left && decoded.TempleRight.Right == cloth.TempleRight.Right
            && decoded.EnchantressCloth.LeftVelocity == cloth.EnchantressCloth.LeftVelocity,
            "owner and observer reconstruct the same three edge controls");
        t.True(TownResidentsCodec.TryRead(Golden, 2, 115, out decoded) && !decoded.HasCloth,
            "old 115-byte record leaves cloth motion absent, never stale");
        for (int cut = 115; cut < 139; cut++)
            t.True(!TownResidentsCodec.TryRead(extended, 2, cut, out decoded) && !decoded.Active,
                "partial cloth tail " + cut + " rejects atomically");
        foreach (int tailIndex in new[] { 115, 119, 123, 131 })
        {
            byte[] invalid = (byte[])extended.Clone(); invalid[2 + tailIndex] = 0x80;
            t.True(!TownResidentsCodec.TryRead(invalid, 2, 139, out decoded) && !decoded.Active,
                "out-of-range signed cloth control " + tailIndex + " rejects atomically");
        }
        var invalidSource = cloth; invalidSource.TempleRight.RightVelocity.x = float.NaN;
        byte[] beforeCloth = (byte[])bytes.Clone(); offset = 0;
        t.True(!TownResidentsCodec.Write(bytes, ref offset, in invalidSource) && offset == 0 && Same(bytes, beforeCloth),
            "nonfinite owner cloth velocity cannot leak partial bytes");

        t.Case("town residents79: every truncation and invalid offset stays bounded");
        for (int cut = 0; cut < Golden.Length; cut++)
        {
            var shortBuffer = new byte[cut]; Array.Copy(Golden, shortBuffer, cut);
            t.True(!TownResidentsCodec.TryRead(shortBuffer, 2, 115, out decoded) && !decoded.Active,
                "physical buffer truncation " + cut + " rejects without partial output");
        }
        for (int payload = 0; payload < 115; payload++)
            t.True(!TownResidentsCodec.TryRead(Golden, 2, payload, out decoded) && !decoded.Active,
                "declared active payload truncation " + payload + " cannot create residents");
        foreach (int invalid in new[] { int.MinValue, -1, Golden.Length, int.MaxValue })
            t.True(!TownResidentsCodec.TryRead(Golden, invalid, 115, out decoded), "invalid read offset " + invalid);
        for (int cut = 0; cut < complete.Length; cut++)
        {
            bool parsed = PresenceSerializer.TryRead(complete, cut, out full);
            t.True(!parsed || !full.HasTownResidents, "full packet truncation " + cut + " cannot expose a partial population");
        }
        for (int flag = 2; flag <= 255; flag++)
        {
            byte[] malformed = (byte[])Golden.Clone(); malformed[2] = (byte)flag;
            t.True(!TownResidentsCodec.TryRead(malformed, 2, 115, out decoded), "reserved active flag " + flag);
        }
        foreach (byte[] malformed in new[] { Hex.Bytes("4F 01 01"), Hex.Bytes("4F 00"), Hex.Bytes("4F 02 00 00") })
        {
            byte[] packet = Packet(malformed);
            t.True(PresenceSerializer.TryRead(packet, packet.Length, out full) && !full.HasTownResidents
                && full.HasModVersion && full.ModBuild == 542, "malformed resident record preserves subsequent native version");
        }
        byte[] future = (byte[])Golden.Clone(); future[0] = 254;
        byte[] unknownPacket = Packet(future);
        t.True(PresenceSerializer.TryRead(unknownPacket, unknownPacket.Length, out full) && !full.HasTownResidents
            && full.HasModVersion && full.ModBuild == 542,
            "unknown record follows the legacy length-skip path without consuming the following version");

        t.Case("town residents79: independently reject invalid numeric wire values");
        for (int n = 0; n < 3; n++)
        {
            int start = 3 + n * 38;
            foreach (int field in new[] { 0, 4, 8, 20, 24, 30, 34 })
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    byte[] bad = (byte[])Golden.Clone(); FloatAt(bad, start + field, invalid);
                    t.True(!TownResidentsCodec.TryRead(bad, 2, 115, out decoded) && !decoded.Active,
                        "nonfinite resident " + n + " field " + field + " rejects atomically");
                }
            foreach (float invalid in new[] { -1f, 0f, .01f, 10.01f })
            {
                byte[] bad = (byte[])Golden.Clone(); FloatAt(bad, start + 20, invalid);
                t.True(!TownResidentsCodec.TryRead(bad, 2, 115, out decoded), "invalid resident scale " + n + "/" + invalid);
            }
            foreach (float invalid in new[] { -1f, 10000001f })
            {
                byte[] bad = (byte[])Golden.Clone(); FloatAt(bad, start + 24, invalid);
                t.True(!TownResidentsCodec.TryRead(bad, 2, 115, out decoded), "invalid resident animation age " + n + "/" + invalid);
            }
            foreach (int field in new[] { 30, 34 })
                foreach (float invalid in new[] { -.501f, .501f })
                {
                    byte[] bad = (byte[])Golden.Clone(); FloatAt(bad, start + field, invalid);
                    t.True(!TownResidentsCodec.TryRead(bad, 2, 115, out decoded), "invalid ground adjustment " + n + "/" + field);
                }
            byte[] far = (byte[])Golden.Clone(); FloatAt(far, start, 101f);
            t.True(!TownResidentsCodec.TryRead(far, 2, 115, out decoded), "implausible resident position " + n);
            byte[] clip = (byte[])Golden.Clone(); clip[start + 29] = 2;
            t.True(!TownResidentsCodec.TryRead(clip, 2, 115, out decoded), "unknown animation clip " + n);
            foreach (byte[] rotation in new[]
            {
                Hex.Bytes("00 00 00 00 00 00 00 00"), // zero
                Hex.Bytes("01 00 00 00 00 00 00 00"), // tiny but nonzero, must not become a valid half-turn
                Hex.Bytes("FF 7F FF 7F FF 7F FF 7F"), // squared norm4, must not become (.5,.5,.5,.5)
                Hex.Bytes("00 80 00 80 00 00 00 00") // signed minimum pair, squared norm>2
            })
            {
                byte[] bad = (byte[])Golden.Clone(); rotation.CopyTo(bad, start + 12);
                t.True(!TownResidentsCodec.TryRead(bad, 2, 115, out decoded) && !decoded.Active,
                    "malformed encoded resident quaternion " + n + "/" + Hex.Show(rotation, rotation.Length));
            }
        }

        t.Case("town residents79: failed writes are atomic at every buffer boundary");
        for (int size = 0; size < Golden.Length + 4; size++)
        {
            var limited = new byte[size]; Array.Fill(limited, (byte)0xCD); byte[] before = (byte[])limited.Clone();
            offset = 4;
            t.True(!TownResidentsCodec.Write(limited, ref offset, in state) && offset == 4 && Same(before, limited),
                "insufficient destination capacity " + size + " leaves offset and every byte untouched");
        }
        var inactive = new TownResidentsState();
        for (int size = 0; size < 3; size++)
        {
            var limited = new byte[size]; Array.Fill(limited, (byte)0xCD); byte[] before = (byte[])limited.Clone(); offset = 0;
            t.True(!TownResidentsCodec.Write(limited, ref offset, in inactive) && offset == 0 && Same(before, limited),
                "insufficient opt-out destination " + size + " is also atomic");
        }
        var exact = new byte[Golden.Length + 4]; Array.Fill(exact, (byte)0xCD); offset = 4;
        t.True(TownResidentsCodec.Write(exact, ref offset, in state) && offset == exact.Length
            && exact[0] == 0xCD && exact[1] == 0xCD && exact[2] == 0xCD && exact[3] == 0xCD,
            "exact destination capacity succeeds without touching prefix bytes");
        foreach (int invalid in new[] { int.MinValue, -1, bytes.Length, int.MaxValue })
        {
            Array.Fill(bytes, (byte)0xCD); byte[] before = (byte[])bytes.Clone(); offset = invalid;
            t.True(!TownResidentsCodec.Write(bytes, ref offset, in state) && offset == invalid && Same(before, bytes),
                "invalid writer offset " + invalid + " has no side effects");
        }
        for (int n = 0; n < 3; n++)
            foreach (int fault in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var bad = Example(); var entry = bad.At(n);
                if (fault == 0) entry.Pose.Rotation = new Quaternion(0f, 0f, 0f, 2f);
                if (fault == 1) entry.Pose.Rotation = new Quaternion(float.NaN, 0f, 0f, 1f);
                if (fault == 2) entry.Pose.Position = new Vector3(float.NaN, 0f, 0f);
                if (fault == 3) entry.Scale = 0f;
                if (fault == 4) entry.Age = -1f;
                if (fault == 5) entry.Clip = 2;
                if (fault == 6) entry.Pose.Position = new Vector3(101f, 0f, 0f);
                if (fault == 7) entry.ActorFloorOffset = .51f;
                if (fault == 8) entry.FurnitureBottom = float.NaN;
                bad.Set(n, entry); Array.Fill(bytes, (byte)0xCD); byte[] before = (byte[])bytes.Clone(); offset = 4;
                t.True(!TownResidentsCodec.Write(bytes, ref offset, in bad) && offset == 4 && Same(bytes, before),
                    "invalid source resident " + n + "/" + fault + " writes nothing");
            }
        for (int size = 11; size < 11 + Golden.Length; size++)
        {
            var limited = new byte[size]; Array.Fill(limited, (byte)0xCD);
            presence.HasTownResidents = true; length = PresenceSerializer.Write(in presence, limited);
            t.True(length == 11 && limited[10] == 0, "extras omits whole resident record at capacity " + size);
            bool untouched = true; for (int i = 11; i < size; i++) untouched &= limited[i] == 0xCD;
            t.True(untouched, "extras never leaves a partial resident record at capacity " + size);
        }

        t.Case("town residents79: snapshot budget and protocol identity");
        offset = 6869;
        t.True(TownResidentsCodec.Write(bytes, ref offset, in state) && offset == 6986,
            "the complete maximum resident record adds117 to the established6869-byte worst case");
        offset = 6869;
        t.True(TownResidentsCodec.Write(bytes, ref offset, in cloth) && offset == 7010,
            "extended resident record adds141 bytes including24 cloth controls");
        t.True(PresenceSerializer.MaxSize == 7930 && PresenceSerializer.MaxSize - (offset + 96 + 54 + 3 + 510) == 257
            && ExtrasFragments.MaxSnapshotBytes - (offset + 96 + 54 + 3 + 510) == 7,
            "cloth and opening records retain exact send and fragment margins");
        t.True(NetProtocol.Version == 3 && NetProtocol.ExtIdTownResidents == 79 && TownResidentsCodec.LegacyPayload == 115
            && TownResidentsCodec.MaxPayload == 139,
            "new residents retain wirev3 and never reuse a historical record identifier");
    }
}

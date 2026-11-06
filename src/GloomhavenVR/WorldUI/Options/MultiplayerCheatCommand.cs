using System;
using System.IO;
using System.Text;

namespace GloomhavenVR.WorldUI;

internal enum MultiplayerCheatKind : byte { Gold = 1, Unlock = 2, Reveal = 3, Requirements = 4 }

/// <summary>Small native-action payload. It never rides the cosmetic presence stream.</summary>
internal sealed class MultiplayerCheatCommand
{
    internal const int MaxBytes = 512;
    private const uint Magic = 0x31434847; // GHC1, independent of embodiment GVR1.
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal ulong Sequence, Roster;
    internal MultiplayerCheatKind Kind;
    internal int MapSeed, Mode, ScenarioSeed;
    internal string Scenario = "", Character = "", CharacterName = "";
    internal bool PartyGold, Enabled;

    internal byte[] Write()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Utf8, true);
        writer.Write(Magic); writer.Write((byte)1); writer.Write((byte)Kind);
        writer.Write(Sequence); writer.Write(Roster); writer.Write(MapSeed); writer.Write(Mode);
        writer.Write(ScenarioSeed); Text(writer, Scenario); Text(writer, Character); Text(writer, CharacterName);
        writer.Write(PartyGold); writer.Write(Enabled);
        byte[] result = stream.ToArray();
        if (result.Length > MaxBytes) throw new InvalidDataException("Cheat command exceeds its bound.");
        return result;
    }
    internal static bool TryRead(byte[]? bytes, out MultiplayerCheatCommand? command)
    {
        command = null;
        if (bytes == null || bytes.Length < 38 || bytes.Length > MaxBytes) return false;
        try
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream, Utf8, true);
            if (reader.ReadUInt32() != Magic || reader.ReadByte() != 1) return false;
            var value = new MultiplayerCheatCommand { Kind = (MultiplayerCheatKind)reader.ReadByte(),
                Sequence = reader.ReadUInt64(), Roster = reader.ReadUInt64(), MapSeed = reader.ReadInt32(),
                Mode = reader.ReadInt32(), ScenarioSeed = reader.ReadInt32(), Scenario = Text(reader), Character = Text(reader), CharacterName = Text(reader) };
            byte party = reader.ReadByte(), enabled = reader.ReadByte();
            if (party > 1 || enabled > 1 || stream.Position != stream.Length || value.Sequence == 0 || value.Roster == 0
                || value.Kind < MultiplayerCheatKind.Gold || value.Kind > MultiplayerCheatKind.Requirements) return false;
            value.PartyGold = party != 0; value.Enabled = enabled != 0;
            if (value.Kind != MultiplayerCheatKind.Gold && (value.Character.Length != 0 || value.CharacterName.Length != 0 || value.PartyGold)
                || value.Kind != MultiplayerCheatKind.Requirements && value.Enabled
                || value.Kind == MultiplayerCheatKind.Gold && value.Character.Length == 0
                || value.Kind == MultiplayerCheatKind.Reveal && value.Scenario.Length == 0
                || value.Kind != MultiplayerCheatKind.Reveal && (value.Scenario.Length != 0 || value.ScenarioSeed != 0)) return false;
            command = value; return true;
        }
        catch (Exception e) when (e is IOException || e is ArgumentException || e is InvalidDataException) { return false; }
    }
    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Utf8.GetBytes(value);
        if (bytes.Length > 192) throw new InvalidDataException("Cheat identity exceeds its bound.");
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }
    private static string Text(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        if (count > 192 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Invalid cheat identity length.");
        return Utf8.GetString(reader.ReadBytes(count));
    }
}

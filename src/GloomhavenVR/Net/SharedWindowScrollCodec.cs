using System;
using System.IO;
using System.Text;

namespace GloomhavenVR.Net;

internal enum SharedScrollOperation : byte { State = 1, Wheel = 2, Position = 3, Closed = 4, PointerDown = 5, PointerDrag = 6, PointerUp = 7 }

/// <summary>Public viewport metadata only: no artwork, arbitrary callbacks or game commands.</summary>
internal sealed class SharedWindowScrollMessage
{
    internal SharedScrollOperation Operation;
    internal byte Kind, Control, Flags, Direction;
    internal uint Epoch, Opening, Generation, Revision, Sequence;
    internal int Author, Requester, Pointer;
    internal ulong Content;
    internal string Path = string.Empty, Viewport = string.Empty;
    internal float X, Y, RangeX, RangeY, HandleSize;
}

internal static class SharedWindowScrollCodec
{
    internal const byte MessageType = 31, RecordId = 126;
    internal const int MaxPathBytes = 360, MaxSize = 800;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static byte[] Write(SharedWindowScrollMessage message)
    {
        if (!Valid(message)) throw new ArgumentOutOfRangeException(nameof(message));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(NetProtocol.Magic); writer.Write(NetProtocol.Version); writer.Write(MessageType);
        writer.Write(RecordId); writer.Write((ushort)0); writer.Write((byte)1);
        writer.Write((byte)message.Operation); writer.Write(message.Kind); writer.Write(message.Control);
        writer.Write(message.Flags); writer.Write(message.Direction); writer.Write(message.Epoch);
        writer.Write(message.Content); writer.Write(message.Opening); writer.Write(message.Generation); writer.Write(message.Revision);
        writer.Write(message.Sequence); writer.Write(message.Author); writer.Write(message.Requester); writer.Write(message.Pointer);
        Text(writer, message.Path); Text(writer, message.Viewport);
        writer.Write(message.X); writer.Write(message.Y); writer.Write(message.RangeX);
        writer.Write(message.RangeY); writer.Write(message.HandleSize);
        byte[] result = stream.ToArray();
        if (result.Length > MaxSize) throw new InvalidDataException("Shared scroll packet exceeds its single-event budget.");
        ushort size = (ushort)(result.Length - 9); result[7] = (byte)size; result[8] = (byte)(size >> 8);
        return result;
    }
    internal static bool TryRead(byte[]? bytes, int length, out SharedWindowScrollMessage message)
    {
        message = new SharedWindowScrollMessage();
        if (bytes == null || length < 79 || length > MaxSize || bytes.Length < length) return false;
        try
        {
            using var stream = new MemoryStream(bytes, 0, length, false); using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != NetProtocol.Magic || reader.ReadByte() != NetProtocol.Version
                || reader.ReadByte() != MessageType || reader.ReadByte() != RecordId
                || reader.ReadUInt16() != length - 9 || reader.ReadByte() != 1) return false;
            message.Operation = (SharedScrollOperation)reader.ReadByte(); message.Kind = reader.ReadByte();
            message.Control = reader.ReadByte(); message.Flags = reader.ReadByte(); message.Direction = reader.ReadByte();
            message.Epoch = reader.ReadUInt32(); message.Content = reader.ReadUInt64(); message.Opening = reader.ReadUInt32(); message.Generation = reader.ReadUInt32();
            message.Revision = reader.ReadUInt32(); message.Sequence = reader.ReadUInt32();
            message.Author = reader.ReadInt32(); message.Requester = reader.ReadInt32(); message.Pointer = reader.ReadInt32();
            message.Path = Text(reader); message.Viewport = Text(reader);
            message.X = reader.ReadSingle(); message.Y = reader.ReadSingle(); message.RangeX = reader.ReadSingle();
            message.RangeY = reader.ReadSingle(); message.HandleSize = reader.ReadSingle();
            return stream.Position == length && Valid(message);
        }
        catch (Exception ex) when (ex is EndOfStreamException || ex is DecoderFallbackException || ex is InvalidDataException)
        { return false; }
    }
    private static void Text(BinaryWriter writer, string value)
    { byte[] bytes = Utf8.GetBytes(value); writer.Write((ushort)bytes.Length); writer.Write(bytes); }
    private static string Text(BinaryReader reader)
    {
        int size = reader.ReadUInt16(); if (size > MaxPathBytes) throw new InvalidDataException();
        byte[] bytes = reader.ReadBytes(size); if (bytes.Length != size) throw new EndOfStreamException();
        return Utf8.GetString(bytes);
    }
    internal static bool Newer(uint next, uint before) => next != before && unchecked(next - before) < 0x80000000u;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static bool Valid(SharedWindowScrollMessage m)
    {
        if (m == null || m.Kind < 1 || m.Kind > 7 || m.Author <= 0 || m.Epoch == 0 || m.Opening == 0
            || m.Path == null || m.Viewport == null || Utf8.GetByteCount(m.Path) > MaxPathBytes
            || Utf8.GetByteCount(m.Viewport) > MaxPathBytes || !Finite(m.X) || !Finite(m.Y)
            || !Finite(m.RangeX) || !Finite(m.RangeY) || !Finite(m.HandleSize)) return false;
        if (m.Operation == SharedScrollOperation.Closed)
            return m.Control == 0 && m.Generation == 0 && m.Revision != 0 && m.Sequence == 0 && m.Requester == 0 && m.Pointer == 0
                && m.Path.Length == 0 && m.Viewport.Length == 0 && m.Flags == 0 && m.Direction == 0
                && m.X == 0 && m.Y == 0 && m.RangeX == 0 && m.RangeY == 0 && m.HandleSize == 0;
        if ((m.Control != 1 && m.Control != 2) || m.Generation == 0 || m.Path.Length == 0
            || m.Path[0] != '/' || m.Flags > 7 || m.Direction > 3) return false;
        if (m.Operation == SharedScrollOperation.State)
            return m.Revision != 0 && m.Sequence == 0 && m.Requester == 0 && m.Pointer == 0 && m.RangeX >= 0 && m.RangeY >= 0
                && m.RangeX <= 1e7f && m.RangeY <= 1e7f && m.HandleSize >= 0 && m.HandleSize <= 1
                && Math.Abs(m.X) <= 10000 && Math.Abs(m.Y) <= 10000;
        return (m.Operation == SharedScrollOperation.Wheel || m.Operation == SharedScrollOperation.Position
                || m.Operation >= SharedScrollOperation.PointerDown && m.Operation <= SharedScrollOperation.PointerUp)
            && m.Revision == 0 && m.Sequence != 0 && m.Requester > 0 && m.Pointer >= -65536 && m.Pointer <= 65536 && m.Viewport.Length == 0
            && m.RangeX == 0 && m.RangeY == 0 && m.HandleSize == 0 && m.Flags == 0 && m.Direction == 0
            && Math.Abs(m.X) <= (m.Operation == SharedScrollOperation.Wheel ? 256 : 10000)
            && Math.Abs(m.Y) <= (m.Operation == SharedScrollOperation.Wheel ? 256 : 10000);
    }
}

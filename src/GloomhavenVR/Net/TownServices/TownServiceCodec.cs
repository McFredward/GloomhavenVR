using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace GloomhavenVR.Net.TownServices;

/// <summary>One complete module revision, split into additive TLV78 records inside GVR1.</summary>
internal static class TownServiceCodec
{
    // Integrator owns allocation in NetProtocol. Keep these aliases until the integration commit.
    internal const byte MessageType = 19, FragmentType = 20, RecordId = 78;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static byte[] Write(TownServiceFrame frame)
    {
        Validate(frame);
        using var body = new MemoryStream();
        using (var w = new BinaryWriter(body, Utf8, true))
        {
            w.Write((byte)1); w.Write(frame.Service); w.Write(frame.Session); w.Write(frame.Sequence); w.Write(frame.BaseSequence);
            w.Write(frame.Module); w.Write(frame.Template); w.Write(frame.Structure); w.Write(frame.Visible);
            w.Write(frame.ParentModule); w.Write(frame.ParentBinding); w.Write(frame.ParentAlpha);
            w.Write(frame.SampleTime); w.Write(frame.SessionAge); w.Write((byte)frame.Modules.Length);
            foreach (ushort module in frame.Modules) w.Write(module);
            foreach (float value in frame.Pose) w.Write(value);
            var pool = new List<TownServiceValue>();
            var indices = new Dictionary<TownServiceValue, ushort>(TownServiceValueComparer.Instance);
            foreach (TownServiceNode node in frame.Nodes)
                for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                    if (node.Values.TryGetValue(key, out TownServiceValue? value) && !indices.ContainsKey(value))
                    { indices.Add(value, checked((ushort)pool.Count)); pool.Add(value); }
            w.Write((ushort)pool.Count);
            foreach (TownServiceValue value in pool)
            {
                w.Write((ushort)value.Numbers.Length);
                foreach (float number in value.Numbers) w.Write(number);
                w.Write((byte)value.Text.Length);
                foreach (string text in value.Text) WriteText(w, text);
            }
            w.Write((ushort)frame.Nodes.Length);
            foreach (TownServiceNode node in frame.Nodes)
            {
                w.Write(node.Binding); w.Write((byte)node.Values.Count);
                // Property order is canonical, independent of Dictionary implementation/runtime.
                for (ushort key = 1; key <= TownServiceProperty.Last; key++)
                {
                    if (!node.Values.TryGetValue(key, out TownServiceValue? value)) continue;
                    w.Write(key); w.Write(indices[value]);
                }
            }
        }
        byte[] raw = body.ToArray();
        int size = 6 + raw.Length + 2 * ((raw.Length + 254) / 255);
        if (size > TownServiceFrame.MaxBytes) throw new InvalidDataException("Town-service module exceeds the bounded snapshot size.");
        var packet = new byte[size];
        packet[0] = (byte)'G'; packet[1] = (byte)'V'; packet[2] = (byte)'R'; packet[3] = (byte)'1';
        packet[4] = 3; packet[5] = MessageType;
        for (int at = 6, offset = 0; offset < raw.Length;)
        {
            int count = Math.Min(255, raw.Length - offset);
            packet[at++] = RecordId; packet[at++] = (byte)count;
            Buffer.BlockCopy(raw, offset, packet, at, count); at += count; offset += count;
        }
        return packet;
    }

    internal static bool TryRead(byte[] packet, int length, out TownServiceFrame? frame)
    {
        frame = null;
        if (packet == null || length < 8 || length > packet.Length || length > TownServiceFrame.MaxBytes
            || packet[0] != 'G' || packet[1] != 'V' || packet[2] != 'R' || packet[3] != '1'
            || packet[4] != 3 || packet[5] != MessageType) return false;
        try
        {
            using var body = new MemoryStream();
            for (int at = 6; at < length;)
            {
                if (at + 2 > length) return false;
                byte record = packet[at++], count = packet[at++];
                if (at + count > length || (count == 0 && record == RecordId)) return false;
                if (record == RecordId) body.Write(packet, at, count);
                at += count;
            }
            body.Position = 0;
            using var r = new BinaryReader(body, Utf8, false);
            if (r.ReadByte() != 1) return false;
            var result = new TownServiceFrame { Service = r.ReadByte(), Session = r.ReadUInt32(),
                Sequence = r.ReadUInt64(), BaseSequence = r.ReadUInt64(), Module = r.ReadUInt16(), Template = r.ReadUInt16(),
                Structure = r.ReadUInt32() };
            byte shown = r.ReadByte(); if (shown > 1) return false;
            result.Visible = shown != 0; result.ParentModule = r.ReadUInt16(); result.ParentBinding = r.ReadUInt32();
            result.ParentAlpha = r.ReadSingle(); result.SampleTime = r.ReadSingle(); result.SessionAge = r.ReadSingle();
            int modules = r.ReadByte();
            if (modules > TownServiceFrame.MaxModules) return false;
            result.Modules = new ushort[modules];
            for (int i = 0; i < modules; i++) result.Modules[i] = r.ReadUInt16();
            for (int i = 0; i < result.Pose.Length; i++) result.Pose[i] = r.ReadSingle();
            int valueCount = r.ReadUInt16();
            if (valueCount > TownServiceFrame.MaxNodes * TownServiceProperty.Last) return false;
            var pool = new TownServiceValue[valueCount];
            for (int p = 0; p < valueCount; p++)
            {
                int n = r.ReadUInt16(); if (n > 1024) return false;
                var value = new TownServiceValue { Numbers = new float[n] };
                for (int v = 0; v < n; v++) value.Numbers[v] = r.ReadSingle();
                int t = r.ReadByte(); if (t > TownServiceFrame.MaxProperties) return false;
                value.Text = new string[t];
                for (int v = 0; v < t; v++) value.Text[v] = ReadText(r);
                pool[p] = value;
            }
            int nodes = r.ReadUInt16();
            if (nodes > TownServiceFrame.MaxNodes) return false;
            result.Nodes = new TownServiceNode[nodes];
            for (int i = 0; i < nodes; i++)
            {
                var node = new TownServiceNode { Binding = r.ReadUInt32() };
                int properties = r.ReadByte();
                if (properties > TownServiceProperty.Last) return false;
                ushort previous = 0;
                for (int p = 0; p < properties; p++)
                {
                    ushort key = r.ReadUInt16();
                    if (key <= previous || key > TownServiceProperty.Last) return false;
                    previous = key;
                    int index = r.ReadUInt16(); if (index >= pool.Length) return false;
                    node.Values.Add(key, pool[index]);
                }
                result.Nodes[i] = node;
            }
            if (body.Position != body.Length) return false;
            Validate(result); frame = result; return true;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static void WriteText(BinaryWriter writer, string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        if (bytes.Length > 16384) throw new InvalidDataException("Town-service text exceeds the bounded size.");
        writer.Write((ushort)bytes.Length); writer.Write(bytes);
    }
    private static string ReadText(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        if (count > 16384 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Truncated town-service text.");
        return Utf8.GetString(reader.ReadBytes(count));
    }
    internal static void Validate(TownServiceFrame frame)
    {
        if (frame.Service < 1 || frame.Service > 3 || frame.Session == 0 || frame.Sequence == 0
            || frame.BaseSequence >= frame.Sequence && frame.BaseSequence != 0
            || (frame.Template == 0 && frame.Module != TownServiceFrame.ManifestModule)
            || frame.Pose == null || frame.Pose.Length != 10 || frame.Nodes == null
            || frame.Nodes.Length > TownServiceFrame.MaxNodes
            || (frame.Visible && frame.Nodes.Length == 0 && frame.Module != TownServiceFrame.ManifestModule && frame.BaseSequence == 0)
            || frame.Modules == null || frame.Modules.Length > TownServiceFrame.MaxModules
            || (frame.Module != TownServiceFrame.ManifestModule && frame.Modules.Length != 0))
            throw new InvalidDataException("Invalid town-service module identity.");
        Finite(frame.SampleTime);
        Finite(frame.SessionAge);
        if (frame.SessionAge < 0) throw new InvalidDataException("Invalid town-service session age.");
        Finite(frame.ParentAlpha);
        if (frame.ParentAlpha < 0 || frame.ParentAlpha > 1 || frame.ParentModule == frame.Module
            && frame.Module != TownServiceFrame.ManifestModule
            || (frame.ParentModule == TownServiceFrame.ManifestModule ? frame.ParentBinding != 0
                : frame.ParentBinding == 0))
            throw new InvalidDataException("Invalid town-service parent.");
        if (frame.Module == TownServiceFrame.ManifestModule
            && (frame.Template != 0 || frame.Structure != 0 || frame.Nodes.Length != 0 || frame.BaseSequence != 0 || (!frame.Visible && frame.Modules.Length != 0)))
            throw new InvalidDataException("Malformed town-service manifest.");
        if (frame.SampleTime < 0) throw new InvalidDataException("Invalid town-service sample time.");
        for (int i = 0; i < frame.Modules.Length; i++)
            if (frame.Modules[i] == TownServiceFrame.ManifestModule || (i > 0 && frame.Modules[i] <= frame.Modules[i - 1]))
                throw new InvalidDataException("Invalid town-service manifest.");
        foreach (float value in frame.Pose) Finite(value);
        double norm = 0;
        for (int i = 3; i < 7; i++) norm += frame.Pose[i] * (double)frame.Pose[i];
        if (Math.Abs(norm - 1) > .01) throw new InvalidDataException("Invalid town-service rotation.");
        var bindings = new HashSet<uint>();
        foreach (TownServiceNode node in frame.Nodes)
        {
            if (node == null || node.Binding == 0 || !bindings.Add(node.Binding) || node.Values.Count > TownServiceProperty.Last)
                throw new InvalidDataException("Invalid town-service node.");
            foreach (var pair in node.Values)
            {
                TownServiceValue value = pair.Value;
                if (pair.Key == 0 || pair.Key > TownServiceProperty.Last || value == null
                    || value.Numbers == null || value.Numbers.Length > 1024 || value.Text == null
                    || value.Text.Length > TownServiceFrame.MaxProperties)
                    throw new InvalidDataException("Invalid town-service property.");
                foreach (float number in value.Numbers) Finite(number);
                foreach (string text in value.Text)
                    if (text == null || Utf8.GetByteCount(text) > 16384)
                        throw new InvalidDataException("Invalid town-service text.");
            }
        }
    }
    private static void Finite(float value)
    { if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite town-service geometry."); }
}

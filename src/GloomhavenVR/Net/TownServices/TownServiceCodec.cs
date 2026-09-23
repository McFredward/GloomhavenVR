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
            w.Write((byte)2); w.Write(frame.Service); w.Write(frame.Session); w.Write(frame.Sequence); w.Write(frame.BaseSequence);
            w.Write(frame.Module); w.Write(frame.Template); WriteText(w, frame.TemplateAddress); w.Write(frame.Structure); w.Write(frame.Visible);
            w.Write(frame.ParentModule); w.Write(frame.ParentBinding); w.Write(frame.ParentAlpha);
            w.Write(frame.SampleTime); w.Write(frame.SessionAge); w.Write((ushort)frame.Modules.Length);
            foreach (ushort module in frame.Modules) w.Write(module);
            foreach (float value in frame.Pose) w.Write(value);
            w.Write(frame.HasCanvasFrame);
            if (frame.HasCanvasFrame)
            {
                foreach (float value in frame.CanvasPose) w.Write(value);
                foreach (float value in frame.CanvasRect) w.Write(value);
                foreach (float value in frame.CanvasSettings) w.Write(value);
                w.Write(frame.CanvasSortingOrder); w.Write(frame.CanvasSortingLayer);
            }
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
        // NetProtocol.Magic (0x47565231) is written little endian by every existing lane.
        packet[0] = 0x31; packet[1] = 0x52; packet[2] = 0x56; packet[3] = 0x47;
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
            || packet[0] != 0x31 || packet[1] != 0x52 || packet[2] != 0x56 || packet[3] != 0x47
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
            byte version = r.ReadByte(); if (version != 1 && version != 2) return false;
            var result = new TownServiceFrame { Service = r.ReadByte(), Session = r.ReadUInt32(),
                Sequence = r.ReadUInt64(), BaseSequence = r.ReadUInt64(), Module = r.ReadUInt16(), Template = r.ReadUInt16(), TemplateAddress = ReadText(r),
                Structure = r.ReadUInt32() };
            byte shown = r.ReadByte(); if (shown > 1) return false;
            result.Visible = shown != 0; result.ParentModule = r.ReadUInt16(); result.ParentBinding = r.ReadUInt32();
            result.ParentAlpha = r.ReadSingle(); result.SampleTime = r.ReadSingle(); result.SessionAge = r.ReadSingle();
            int modules = version == 1 ? r.ReadByte() : r.ReadUInt16();
            if (modules > TownServiceFrame.MaxModules) return false;
            result.Modules = new ushort[modules];
            for (int i = 0; i < modules; i++) result.Modules[i] = r.ReadUInt16();
            for (int i = 0; i < result.Pose.Length; i++) result.Pose[i] = r.ReadSingle();
            byte canvas = r.ReadByte(); if (canvas > 1) return false; result.HasCanvasFrame = canvas != 0;
            if (result.HasCanvasFrame)
            {
                for (int i = 0; i < result.CanvasPose.Length; i++) result.CanvasPose[i] = r.ReadSingle();
                for (int i = 0; i < result.CanvasRect.Length; i++) result.CanvasRect[i] = r.ReadSingle();
                for (int i = 0; i < result.CanvasSettings.Length; i++) result.CanvasSettings[i] = r.ReadSingle();
                result.CanvasSortingOrder = r.ReadInt32(); result.CanvasSortingLayer = r.ReadInt32();
            }
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

    // The bundle is only a lossless transport container. Every child remains a complete
    // independently sequenced original module packet; no observer-template defaults apply.
    internal const int MaxBundleFrames = 32;
    internal static byte[] WriteBundle(IReadOnlyList<byte[]> packets)
    {
        if (packets.Count < 1 || packets.Count > MaxBundleFrames) throw new InvalidDataException("Invalid town bundle count.");
        using var body = new MemoryStream();
        body.WriteByte(3); body.WriteByte((byte)packets.Count);
        foreach (byte[] packet in packets)
        {
            if (!TryRead(packet, packet.Length, out TownServiceFrame? frame) || frame!.Module == TownServiceFrame.ManifestModule)
                throw new InvalidDataException("Invalid town bundle member.");
            body.WriteByte((byte)packet.Length); body.WriteByte((byte)(packet.Length >> 8));
            body.Write(packet, 0, packet.Length);
        }
        byte[] raw = body.ToArray();
        if (raw.Length + 8 > TownServiceFrame.MaxBytes) throw new InvalidDataException("Town bundle exceeds snapshot bound.");
        // Zero-sized record78 is reserved for the bounded v3 bundle body. Keeping its
        // bytes contiguous lets Deflate reuse repeated original material/property tables
        // across children; slicing every255 bytes would destroy those matching runs.
        var result = new byte[8+raw.Length]; result[0]=0x31; result[1]=0x52; result[2]=0x56; result[3]=0x47;
        result[4]=3; result[5]=MessageType; result[6]=RecordId; result[7]=0;
        Buffer.BlockCopy(raw,0,result,8,raw.Length);return result;
    }
    internal static bool TryReadBundle(byte[] packet, int length, out byte[][]? packets)
    {
        packets=null;
        if(packet==null||length<10||length>packet.Length||length>TownServiceFrame.MaxBytes
            ||packet[0]!=0x31||packet[1]!=0x52||packet[2]!=0x56||packet[3]!=0x47
            ||packet[4]!=3||packet[5]!=MessageType||packet[6]!=RecordId||packet[7]!=0)return false;
        var raw=new byte[length-8];Buffer.BlockCopy(packet,8,raw,0,raw.Length);
        if(raw.Length<2||raw[0]!=3||raw[1]<1||raw[1]>MaxBundleFrames)return false;
        var result=new byte[raw[1]][];int position=2;byte service=0;uint session=0;
        for(int i=0;i<result.Length;i++)
        {
            if(position+2>raw.Length)return false;int count=raw[position]|raw[position+1]<<8;position+=2;
            if(count<8||position+count>raw.Length)return false;
            var member=new byte[count];Buffer.BlockCopy(raw,position,member,0,count);position+=count;
            if(!TryRead(member,count,out TownServiceFrame? frame)||frame!.Module==TownServiceFrame.ManifestModule)return false;
            if(i==0){service=frame.Service;session=frame.Session;}
            else if(frame.Service!=service||frame.Session!=session)return false;
            result[i]=member;
        }
        if(position!=raw.Length)return false;packets=result;return true;
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
        if (frame.Module == TownServiceFrame.BundleStream || frame.Service < 1 || frame.Service > 3 || frame.Session == 0 || frame.Sequence == 0
            || frame.BaseSequence >= frame.Sequence && frame.BaseSequence != 0
            || (frame.Template == 0 && frame.Module != TownServiceFrame.ManifestModule)
            || frame.TemplateAddress == null || frame.TemplateAddress.Length > 1024
            || frame.Pose == null || frame.Pose.Length != 10 || frame.Nodes == null
            || frame.Nodes.Length > TownServiceFrame.MaxNodes
            || (frame.Visible && frame.Nodes.Length == 0 && frame.Module != TownServiceFrame.ManifestModule && frame.BaseSequence == 0)
            || frame.Modules == null || frame.Modules.Length > TownServiceFrame.MaxModules
            || (frame.Module != TownServiceFrame.ManifestModule && frame.Modules.Length != 0))
            throw new InvalidDataException("Invalid town-service module identity.");
        if (frame.CanvasPose == null || frame.CanvasPose.Length != 10 || frame.CanvasRect == null || frame.CanvasRect.Length != 4
            || frame.CanvasSettings == null || frame.CanvasSettings.Length != 5) throw new InvalidDataException("Invalid town canvas frame.");
        foreach (float value in frame.CanvasPose) Finite(value);
        foreach (float value in frame.CanvasRect) Finite(value);
        foreach (float value in frame.CanvasSettings) Finite(value);
        if (frame.HasCanvasFrame)
        {
            double canvasNorm = 0; for (int i = 3; i < 7; i++) canvasNorm += frame.CanvasPose[i] * (double)frame.CanvasPose[i];
            if (Math.Abs(canvasNorm - 1) > .01 || frame.CanvasRect[0] < 0 || frame.CanvasRect[1] < 0)
                throw new InvalidDataException("Invalid town canvas geometry.");
        }
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
            if (frame.Modules[i] >= TownServiceFrame.BundleStream || (i > 0 && frame.Modules[i] <= frame.Modules[i - 1]))
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

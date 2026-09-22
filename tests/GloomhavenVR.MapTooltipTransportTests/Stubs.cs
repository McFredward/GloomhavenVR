using System;
using System.Collections.Generic;

namespace GloomhavenVR.Net
{
    internal static class NetProtocol
    {
        internal const uint Magic = 0x47565231;
        internal const byte Version = 3, MsgRig = 0, MsgExtras = 1,
            MsgMapButtonTooltip = 23, ExtIdMapButtonTooltip = 82;
    }
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] buffer, ref int at, uint value)
        {
            for (int i = 0; i < 4; i++) buffer[at++] = (byte)(value >> (8 * i));
        }
    }
    internal sealed class Transport
    {
        internal readonly List<MapButtonTooltipSnapshot> Sent = new();
        internal bool Throw;
        internal void Send(byte[] bytes, int length, MapButtonTooltipSnapshot identity)
        {
            if (Throw) throw new InvalidOperationException("transport fixture failure");
            if (!MapButtonTooltipCodec.TryRead(bytes, length, out var decoded))
                throw new Exception("driver emitted an undecodable packet");
            if (identity.SampleTime != decoded!.SampleTime)
                throw new Exception("queued identity and serialized sample disagree");
            Sent.Add(decoded);
        }
    }
    internal sealed partial class NetAvatarDriver
    {
        private readonly Transport _transport = new();
        private readonly List<string> _errors = new();
        private void LogPhaseError(string phase, Exception error) => _errors.Add(phase + ": " + error.Message);
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapButtonTooltipPresentation
    {
        internal static byte[]? Current;
        internal static int Captures, Ticks, Resets, ThrowSender;
        internal static bool ThrowTick;
        internal static readonly List<int> Removed = new();
        internal static readonly List<(int Sender, byte[]? Payload, float Time)> Applied = new();
        internal static byte[]? Capture() { Captures++; return Current; }
        internal static void Receive(int sender, byte[]? payload, float time)
        {
            if (sender == ThrowSender) throw new InvalidOperationException("receive fixture failure");
            Applied.Add((sender, payload, time));
        }
        internal static void Tick()
        {
            Ticks++;
            if (ThrowTick) throw new InvalidOperationException("tick fixture failure");
        }
        internal static void Remove(int sender) => Removed.Add(sender);
        internal static void Reset() { Resets++; Applied.Clear(); Current = null; }
        internal static void ResetFixture()
        {
            Current = null; Captures = Ticks = Resets = ThrowSender = 0;
            ThrowTick = false; Removed.Clear(); Applied.Clear();
        }
    }
}

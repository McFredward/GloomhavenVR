using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Compact, allocation-free (de)serialization of an <see cref="AvatarState"/> to/from a
/// caller-provided byte buffer.
///
/// SHARED FRAME CONTRACT — poses are in GAME WORLD space (game units). Gloomhaven is
/// server-authoritative and top-down: the board, tiles and minis sit at identical world
/// coordinates on every client (that is exactly what lets flat multiplayer show everyone
/// the same board), so world space IS the shared reference frame. No per-player playspace
/// anchor is needed because — unlike LCVR — Gloomhaven has no world-locked player body;
/// each VR player is a free head above a shared table. If a future title build turns out to
/// offset the board per client, swap the identity conversion here for a board-anchor
/// transform (see <see cref="IBoardAnchor"/>) — the wire format does not change.
///
/// Layout (little-endian), wire v3:
///   [0..3]  uint32  magic  (NetProtocol.Magic)
///   [4]     byte    version
///   [5]     byte    type    (== NetProtocol.MsgRig for this serializer)
///   [6]     byte    flags   (head/left/right tracked, hasFingers, heldFigure, dominantRight)
///   [7]     byte    maskId  (chosen head mask 0..2)
///   [8..11] float32 worldScale
///   then, in order, for each present part (head, left, right):
///     pose = pos(3×float32=12) + rot(4×int16 quantized = 8)   → 20 bytes
///     if the part is a hand AND hasFingers: 5×byte curls        → 5 bytes
///   if FlagHeldFigure: actorId(int32=4) + pose(20)              → 24 bytes
/// Rotation quantization: each quaternion component q∈[-1,1] → round(q·32767) as int16;
/// reconstructed and re-normalized on read (≈ 0.006 rad worst case — imperceptible for a
/// floating hand).
/// </summary>
internal static unsafe class AvatarSerializer
{
    /// <summary>Upper bound on an encoded rig packet: header 12 + head 20 + 2 hands (20+5) +
    /// held-figure block (4+20) = 106, rounded up to 112 for headroom.</summary>
    public const int MaxSize = 112;

    private const float QuatScale = 32767f;

    // ---- write --------------------------------------------------------------------------

    /// <summary>
    /// Serialize <paramref name="state"/> into <paramref name="buffer"/> (must be &gt;=
    /// <see cref="MaxSize"/>). Returns the byte count written. No heap allocation.
    /// </summary>
    public static int Write(in AvatarState state, byte[] buffer)
    {
        int i = 0;
        WriteU32(buffer, ref i, NetProtocol.Magic);
        buffer[i++] = NetProtocol.Version;
        buffer[i++] = NetProtocol.MsgRig; // message type byte (v3)

        byte flags = 0;
        if (state.HeadValid) flags |= NetProtocol.FlagHeadValid;
        if (state.Left.Tracked) flags |= NetProtocol.FlagLeftTracked;
        if (state.Right.Tracked) flags |= NetProtocol.FlagRightTracked;
        if (state.HasFingers) flags |= NetProtocol.FlagHasFingers;
        if (state.HasHeldFigure) flags |= NetProtocol.FlagHeldFigure;
        if (state.DominantRight) flags |= NetProtocol.FlagDominantRight;
        buffer[i++] = flags;

        // Head mask id (0..2). Clamp defensively so a stray value never confuses the receiver.
        buffer[i++] = (byte)Mathf.Clamp(state.MaskId, 0, HeadMaskLibrary.MaskCount - 1);

        WriteF32(buffer, ref i, state.WorldScale <= 0f ? 1f : state.WorldScale);

        if (state.HeadValid)
            WritePose(buffer, ref i, in state.Head);

        if (state.Left.Tracked)
        {
            WritePose(buffer, ref i, in state.Left.Pose);
            if (state.HasFingers) WriteFingers(buffer, ref i, in state.Left);
        }
        if (state.Right.Tracked)
        {
            WritePose(buffer, ref i, in state.Right.Pose);
            if (state.HasFingers) WriteFingers(buffer, ref i, in state.Right);
        }

        if (state.HasHeldFigure)
        {
            WriteI32(buffer, ref i, state.HeldFigureActorId);
            WritePose(buffer, ref i, in state.HeldFigurePose);
        }
        return i;
    }

    private static void WritePose(byte[] b, ref int i, in RigPose p)
    {
        WriteF32(b, ref i, p.Position.x);
        WriteF32(b, ref i, p.Position.y);
        WriteF32(b, ref i, p.Position.z);
        Quaternion q = p.Rotation;
        // Guard against a zero/denormal quaternion.
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag < 1e-6f) { q = Quaternion.identity; mag = 1f; }
        float inv = 1f / mag;
        WriteI16(b, ref i, (short)Mathf.RoundToInt(Mathf.Clamp(q.x * inv, -1f, 1f) * QuatScale));
        WriteI16(b, ref i, (short)Mathf.RoundToInt(Mathf.Clamp(q.y * inv, -1f, 1f) * QuatScale));
        WriteI16(b, ref i, (short)Mathf.RoundToInt(Mathf.Clamp(q.z * inv, -1f, 1f) * QuatScale));
        WriteI16(b, ref i, (short)Mathf.RoundToInt(Mathf.Clamp(q.w * inv, -1f, 1f) * QuatScale));
    }

    private static void WriteFingers(byte[] b, ref int i, in HandStateSample h)
    {
        b[i++] = Curl(h.Curl0);
        b[i++] = Curl(h.Curl1);
        b[i++] = Curl(h.Curl2);
        b[i++] = Curl(h.Curl3);
        b[i++] = Curl(h.Curl4);
    }

    private static byte Curl(float c) => (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255);

    // ---- read ---------------------------------------------------------------------------

    /// <summary>
    /// Parse a packet. Returns false (and leaves <paramref name="state"/> defaulted) on any
    /// magic/version mismatch or truncation — never throws. <paramref name="length"/> is the
    /// valid byte count in <paramref name="buffer"/>.
    /// </summary>
    public static bool TryRead(byte[] buffer, int length, out AvatarState state)
    {
        state = default;
        if (buffer == null || length < 12)
            return false;

        int i = 0;
        if (ReadU32(buffer, ref i) != NetProtocol.Magic) return false;
        if (buffer[i++] != NetProtocol.Version) return false;
        if (buffer[i++] != NetProtocol.MsgRig) return false; // wrong message type for this serializer

        byte flags = buffer[i++];
        state.MaskId = (byte)Mathf.Clamp(buffer[i++], 0, HeadMaskLibrary.MaskCount - 1);
        state.WorldScale = ReadF32(buffer, ref i);
        if (!(state.WorldScale > 0f) || float.IsNaN(state.WorldScale) || float.IsInfinity(state.WorldScale))
            state.WorldScale = 1f;

        bool head = (flags & NetProtocol.FlagHeadValid) != 0;
        bool left = (flags & NetProtocol.FlagLeftTracked) != 0;
        bool right = (flags & NetProtocol.FlagRightTracked) != 0;
        bool fingers = (flags & NetProtocol.FlagHasFingers) != 0;
        bool held = (flags & NetProtocol.FlagHeldFigure) != 0;
        state.HasFingers = fingers;
        state.DominantRight = (flags & NetProtocol.FlagDominantRight) != 0;

        int need = (head ? 20 : 0) + (left ? 20 + (fingers ? 5 : 0) : 0) + (right ? 20 + (fingers ? 5 : 0) : 0)
                   + (held ? 24 : 0);
        if (length < i + need)
            return false;

        if (head)
        {
            state.HeadValid = true;
            ReadPose(buffer, ref i, out state.Head);
        }
        if (left)
        {
            state.Left.Tracked = true;
            ReadPose(buffer, ref i, out state.Left.Pose);
            if (fingers) ReadFingers(buffer, ref i, ref state.Left);
        }
        if (right)
        {
            state.Right.Tracked = true;
            ReadPose(buffer, ref i, out state.Right.Pose);
            if (fingers) ReadFingers(buffer, ref i, ref state.Right);
        }
        if (held)
        {
            state.HasHeldFigure = true;
            state.HeldFigureActorId = ReadI32(buffer, ref i);
            ReadPose(buffer, ref i, out state.HeldFigurePose);
        }
        return true;
    }

    private static void ReadPose(byte[] b, ref int i, out RigPose p)
    {
        p = default;
        p.Position = new Vector3(ReadF32(b, ref i), ReadF32(b, ref i), ReadF32(b, ref i));
        float x = ReadI16(b, ref i) / QuatScale;
        float y = ReadI16(b, ref i) / QuatScale;
        float z = ReadI16(b, ref i) / QuatScale;
        float w = ReadI16(b, ref i) / QuatScale;
        var q = new Quaternion(x, y, z, w);
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        p.Rotation = mag < 1e-6f ? Quaternion.identity : new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
    }

    private static void ReadFingers(byte[] b, ref int i, ref HandStateSample h)
    {
        h.Curl0 = b[i++] / 255f;
        h.Curl1 = b[i++] / 255f;
        h.Curl2 = b[i++] / 255f;
        h.Curl3 = b[i++] / 255f;
        h.Curl4 = b[i++] / 255f;
    }

    // ---- little-endian primitives (allocation-free) -------------------------------------
    // internal so PresenceSerializer (the type-1 extras packet) can reuse the exact same
    // encoding without duplicating the unsafe/quantization helpers.

    internal static void WriteU32(byte[] b, ref int i, uint v)
    {
        b[i++] = (byte)v; b[i++] = (byte)(v >> 8); b[i++] = (byte)(v >> 16); b[i++] = (byte)(v >> 24);
    }

    internal static uint ReadU32(byte[] b, ref int i)
    {
        uint v = (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
        i += 4;
        return v;
    }

    internal static void WriteI32(byte[] b, ref int i, int v) => WriteU32(b, ref i, (uint)v);

    internal static int ReadI32(byte[] b, ref int i) => (int)ReadU32(b, ref i);

    internal static void WriteI16(byte[] b, ref int i, short v)
    {
        b[i++] = (byte)v; b[i++] = (byte)(v >> 8);
    }

    internal static short ReadI16(byte[] b, ref int i)
    {
        short v = (short)(b[i] | (b[i + 1] << 8));
        i += 2;
        return v;
    }

    internal static void WriteF32(byte[] b, ref int i, float v)
    {
        uint bits = *(uint*)&v;
        WriteU32(b, ref i, bits);
    }

    internal static float ReadF32(byte[] b, ref int i)
    {
        uint bits = ReadU32(b, ref i);
        return *(float*)&bits;
    }

    // ---- pose primitives (internal for PresenceSerializer's board pose) ------------------

    /// <summary>Write a rigid pose (pos 12B + quantized rot 8B = 20B). Shared with the extras
    /// serializer so both packets encode poses identically.</summary>
    internal static void WritePoseShared(byte[] b, ref int i, in RigPose p) => WritePose(b, ref i, in p);

    /// <summary>Read a rigid pose written by <see cref="WritePoseShared"/> (20B).</summary>
    internal static void ReadPoseShared(byte[] b, ref int i, out RigPose p) => ReadPose(b, ref i, out p);
}

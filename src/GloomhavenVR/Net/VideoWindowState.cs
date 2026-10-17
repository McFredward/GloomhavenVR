using System;

namespace GloomhavenVR.Net;

/// <summary>Finite original subjects and explicit review aliases on unchanged72.</summary>
internal static class VideoMovieKeys
{
    internal const string ReviewPrefix = "Review/";
    private static readonly string[] Movies =
    {
        "CP_Intro/GH_CP_Intro.mov",
        "Heroes/Doomstalker.mov", "Heroes/Soothsinger.mov", "Heroes/Berserker.mov",
        "Heroes/Summoner.mov", "Heroes/BeastTyrant.mov", "Heroes/Quartermaster.mov",
        "Heroes/Nightshroud.mov", "Heroes/Elementalist.mov", "Heroes/Sunkeeper.mov",
        "Heroes/Sawbones.mov", "Heroes/Plagueherald.mov",
        "Ambient/Ambient_03_Forest_01.mov", "Ambient/Ambient_Crypt_01.mov",
        "Ambient/Amibient_07_Dungeon_01.mov", "Ambient/Ambient_05_Corpsewood_01.mov",
        "Ambient/Ambient_06_Town_01.mov", "Ambient/Ambient_02_Cave_01.mov",
        "Ambient/Ambient_04_Cave_02.mov",
        "Embedded/TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best.mov",
    };
    internal static int Count => Movies.Length;
    internal static string GetKey(int index) => Movies[index];
    internal static bool IsCanonical(string key) => Array.IndexOf(Movies, key) >= 0;
    internal static string StereoKey(string canonical) => canonical == Movies[0]
        ? "CP_Intro/GH_CP_Intro.sbs-v2.mov" : canonical.Substring(0, canonical.Length - 4) + ".sbs-v1.mov";
    internal static bool TryUnwrapReview(string? key, out string selected)
    {
        selected = string.Empty;
        if (string.IsNullOrEmpty(key) || key!.Length > 128 || !key.StartsWith(ReviewPrefix, StringComparison.Ordinal)) return false;
        string underlying = key.Substring(ReviewPrefix.Length);
        for (int i = 0; i < Movies.Length; i++)
        {
            if (underlying != Movies[i] && underlying != StereoKey(Movies[i])) continue;
            selected = underlying;
            return true;
        }
        return false;
    }
    internal static bool IsReview(string? key) => TryUnwrapReview(key, out _);
    internal static string ReviewKey(string selected) => ReviewPrefix + selected;
}

/// <summary>Presentation only. Source tokens distinguish repeated plays of the same movie;
/// deliberate review originals use that same source contract; mirrors only publish a pose.</summary>
internal struct VideoWindowState
{
    public bool Native;
    public bool Playing;
    public bool Closed;
    public uint Token;
    public uint Revision;
    public uint Millis;
    public string? Clip;
    public SharedWindowEntry Window;
}

/// <summary>One native source's ordering and retirement guard. A completed decoder cannot be
/// restarted by an old snapshot; a genuinely new playback token can still play the same clip.</summary>
internal sealed class VideoSourceClock
{
    internal VideoWindowState State;
    internal float At;
    internal uint ClosedToken;
    internal uint LastToken;
    internal uint LastRevision;
    internal bool HasSource;

    internal bool Observe(in VideoWindowState state, float now)
    {
        if (!state.Native || state.Token == 0 || state.Token < ClosedToken
            || (state.Token == ClosedToken && !state.Closed) || state.Token < LastToken
            || (state.Token == LastToken && state.Revision <= LastRevision)) return false;
        LastToken = state.Token;
        LastRevision = state.Revision;
        HasSource = !state.Closed;
        if (state.Closed) ClosedToken = state.Token;
        State = state;
        At = now;
        return true;
    }

    internal void Retire()
    {
        ClosedToken = LastToken;
        HasSource = false;
    }
}

/// <summary>Additive72: flags1, token4, revision4, time4, clipLength1, ASCII clip,
/// poseKey4, poseFlags1, optional stamp1/size1/frame1/pose20. No absolute paths.</summary>
internal static class VideoWindowCodec
{
    internal const int MaxClipBytes = 160;

    internal static bool ValidClip(string? clip)
    {
        if (string.IsNullOrEmpty(clip) || clip!.Length > MaxClipBytes) return false;
        if (clip.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
        if (VideoMovieKeys.IsReview(clip)) return true;
        if (!clip.StartsWith("Heroes/", StringComparison.Ordinal)
            && !clip.StartsWith("CP_Intro/", StringComparison.Ordinal)
            && !clip.StartsWith("Ambient/", StringComparison.Ordinal)
            && !clip.StartsWith("Embedded/", StringComparison.Ordinal)) return false;
        for (int j = 0; j < clip.Length; j++)
        {
            char c = clip[j];
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z')
                && !(c >= '0' && c <= '9') && c != '/' && c != '.' && c != '_' && c != '-') return false;
        }
        return true;
    }

    internal static bool Write(byte[] buffer, ref int i, in VideoWindowState video)
    {
        if (!ValidClip(video.Clip) || video.Token == 0) return false;
        bool pose = (video.Window.Flags & NetProtocol.SharedPoseBit) != 0;
        int length = 19 + video.Clip!.Length + (pose ? 23 : 0);
        if (i + 2 + length > buffer.Length) return false;
        buffer[i++] = NetProtocol.ExtIdVideoWindow;
        buffer[i++] = (byte)length;
        buffer[i++] = (byte)((video.Native ? 1 : 0) | (video.Playing ? 2 : 0) | (video.Closed ? 4 : 0));
        AvatarSerializer.WriteU32(buffer, ref i, video.Token);
        AvatarSerializer.WriteU32(buffer, ref i, video.Revision);
        AvatarSerializer.WriteU32(buffer, ref i, video.Millis);
        buffer[i++] = (byte)video.Clip.Length;
        foreach (char c in video.Clip) buffer[i++] = (byte)c;
        AvatarSerializer.WriteU32(buffer, ref i, video.Window.ContentKey);
        buffer[i++] = pose ? NetProtocol.SharedPoseBit : (byte)0;
        if (pose)
        {
            buffer[i++] = video.Window.PoseStamp;
            buffer[i++] = video.Window.SizeCode;
            buffer[i++] = video.Window.Frame;
            AvatarSerializer.WritePoseShared(buffer, ref i, in video.Window.Pose);
        }
        return true;
    }

    internal static bool TryRead(byte[] buffer, int i, int length, out VideoWindowState video)
    {
        video = default;
        if (length < 19 || i < 0 || i > buffer.Length - length) return false;
        int end = i + length;
        byte flags = buffer[i++];
        if ((flags & ~7) != 0 || (flags & 6) == 6) return false;
        video.Native = (flags & 1) != 0;
        video.Playing = (flags & 2) != 0;
        video.Closed = (flags & 4) != 0;
        video.Token = AvatarSerializer.ReadU32(buffer, ref i);
        video.Revision = AvatarSerializer.ReadU32(buffer, ref i);
        video.Millis = AvatarSerializer.ReadU32(buffer, ref i);
        int n = buffer[i++];
        if (n == 0 || n > MaxClipBytes || i + n + 5 > end || video.Token == 0) return false;
        video.Clip = System.Text.Encoding.ASCII.GetString(buffer, i, n);
        i += n;
        if (!ValidClip(video.Clip)) return false;
        video.Window.ContentKey = AvatarSerializer.ReadU32(buffer, ref i);
        video.Window.Flags = buffer[i++];
        if ((video.Window.Flags & ~NetProtocol.SharedPoseBit) != 0) return false;
        bool pose = (video.Window.Flags & NetProtocol.SharedPoseBit) != 0;
        if (i + (pose ? 23 : 0) != end) return false;
        if (pose)
        {
            video.Window.PoseStamp = buffer[i++];
            video.Window.SizeCode = buffer[i++];
            video.Window.Frame = buffer[i++];
            if (video.Window.Frame > NetProtocol.SharedFrameMax
                || video.Window.SizeCode < NetProtocol.StorySizeMinCode
                || video.Window.SizeCode > NetProtocol.StorySizeMaxCode) return false;
            AvatarSerializer.ReadPoseShared(buffer, ref i, out video.Window.Pose);
            var p = video.Window.Pose.Position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)
                || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z)) return false;
        }
        return true;
    }
}

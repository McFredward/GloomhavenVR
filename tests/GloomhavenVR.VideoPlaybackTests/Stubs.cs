using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value) => value.Destroyed = true;
        private static bool Null(Object? value) => ReferenceEquals(value, null) || value.Destroyed;
        public static bool operator ==(Object? a, Object? b) => Null(a) && Null(b) || ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component, new() => new() { gameObject = this };
    }
    public class Component : Object { public GameObject gameObject = null!; }
    public sealed class AudioSource : Object { public bool mute; }
    public struct Vector3 { public float x, y, z; }
    public struct Quaternion { }
    public static class Time { public static float unscaledTime; }
}

namespace UnityEngine.Video
{
    public enum VideoRenderMode { APIOnly }
    public enum VideoAudioOutputMode { None, Direct, AudioSource }
    public sealed class VideoPlayer : Component
    {
        public bool playOnAwake, isLooping, isPrepared, isPlaying, isPaused;
        public bool canSetTime = true;
        public string url = string.Empty;
        public double time, length = 100;
        public ushort audioTrackCount;
        public VideoRenderMode renderMode;
        public VideoAudioOutputMode audioOutputMode;
        public int PlayCalls, StopCalls;
        private readonly Dictionary<ushort, bool> _mute = new();
        private readonly Dictionary<ushort, AudioSource> _targets = new();
        public event Action<VideoPlayer>? prepareCompleted, loopPointReached;
        public event Action<VideoPlayer, string>? errorReceived;
        public void Prepare() { }
        public void Prepared() { isPrepared = true; prepareCompleted?.Invoke(this); }
        public void End() { isPlaying = false; loopPointReached?.Invoke(this); }
        public void Fail() => errorReceived?.Invoke(this, "injected decoder failure");
        public void Play() { PlayCalls++; isPlaying = true; isPaused = false; }
        public void Pause() { isPlaying = false; isPaused = true; }
        public void Stop() { StopCalls++; isPlaying = isPaused = false; }
        public bool GetDirectAudioMute(ushort track) => _mute.TryGetValue(track, out bool mute) && mute;
        public void SetDirectAudioMute(ushort track, bool mute) => _mute[track] = mute;
        public void SetDirectAudioVolume(ushort track, float volume) { }
        public AudioSource GetTargetAudioSource(ushort track) => _targets.TryGetValue(track, out AudioSource? source) ? source : null!;
        public void Bind(ushort track, AudioSource source) => _targets[track] = source;
    }
}

public static class AudioController { public static float GetGlobalVolume() => 0.8f; }

namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Note(string scope, string line) { } }
}

namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    using UnityEngine.Video;
    internal enum SharedWindowKind { Video }
    internal static class SharedWindows
    {
        internal static bool Online = true;
        internal static bool ParticipatesHere(SharedWindowKind kind) => Online;
    }
    internal static class NativeVideoWindow
    {
        internal static VideoPlayer? NativePlayer, Remote;
        internal static uint NativePlaybackGeneration;
        internal static bool Suppressed, AssetAvailable = true;
        internal static string PlaybackKey => NativePlayer != null ? "Heroes/A.mov" : string.Empty;
        internal static void SetRemoteSource(VideoPlayer? player) => Remote = player;
        internal static void SetNativePresentationSuppressed(bool suppressed) => Suppressed = suppressed;
        internal static void SetRemotePose(Vector3 pos, Quaternion rot, float size) { }
        internal static bool TryResolvePlaybackKey(string key, out string url) { url = key; return AssetAvailable; }
    }
}

namespace GloomhavenVR.Net
{
    using UnityEngine;
    internal struct RigPose { public Vector3 Position { get; set; } public Quaternion Rotation { get; set; } }
    internal struct SharedWindowEntry
    {
        public uint ContentKey;
        public byte Flags, PoseStamp, SizeCode, Frame;
        public RigPose Pose;
    }
    internal struct PresenceState { public bool HasVideoWindow; public VideoWindowState VideoWindow; }
    internal static class NetProtocol
    {
        public const byte ExtIdVideoWindow = 72, SharedPoseBit = 4, SharedFrameMax = 1,
            StorySizeMinCode = 1, StorySizeMaxCode = 255;
    }
    // Serialization is never used by this harness. Any accidental dependency fails loudly.
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] buffer, ref int i, uint value) => throw new NotSupportedException();
        internal static uint ReadU32(byte[] buffer, ref int i) => throw new NotSupportedException();
        internal static void WritePoseShared(byte[] buffer, ref int i, in RigPose pose) => throw new NotSupportedException();
        internal static void ReadPoseShared(byte[] buffer, ref int i, out RigPose pose) => throw new NotSupportedException();
    }
    internal static class RemoteMapStory
    {
        internal static void ResetVideoPose() { }
        internal static void SampleVideoPose(uint key, bool source, ref SharedWindowEntry entry) { }
        internal static void ObserveVideoPose(int sender, in SharedWindowEntry entry) { }
        internal static void ForgetVideoPose(int sender) { }
        internal static void ResolveVideoPose(uint key) { }
        internal static bool TryVideoInitialPose(int owner, uint key, out Vector3 pos, out Quaternion rot, out float size)
        { pos = default; rot = default; size = 1; return true; }
    }
}

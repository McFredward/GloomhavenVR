using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

/// <summary>Resolve explicit movie revisions without changing native continuation identity.</summary>
internal static class NativeVideoMovies
{
    private const string IntroName = "CP_Intro/GH_CP_Intro";
    internal const string IntroKey = IntroName + ".mov";
    internal const string StereoIntroKey = IntroName + ".sbs-v2.mov";
    internal const string LegacyStereoIntroKey = IntroName + ".sbs-v1.mov";
    internal const string NativeIntroKey = IntroName + ".native.mov";
    private sealed class Selection { internal string Url = string.Empty, Key = string.Empty; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VideoPlayer, Selection> Selections = new();
    private static bool _reportedReplacement;
    private static uint _introOpening, _opening;
    internal static int Count => GloomhavenVR.Net.VideoMovieKeys.Count;
    internal static string GetKey(int index) => GloomhavenVR.Net.VideoMovieKeys.GetKey(index);
    private static readonly List<VideoPlayer> CameraPlanePlayers = new(3);
    internal static void RegisterCameraPlane(VideoPlayer player)
    {
        if (!CameraPlanePlayers.Contains(player)) CameraPlanePlayers.Add(player);
    }
    internal static VideoPlayer? CameraPlaneSource
    {
        get
        {
            VideoPlayer? selected = null;
            float depth = float.MinValue;
            for (int i = CameraPlanePlayers.Count - 1; i >= 0; i--)
            {
                VideoPlayer player = CameraPlanePlayers[i];
                if (player == null) { CameraPlanePlayers.RemoveAt(i); continue; }
                Camera camera = player.targetCamera;
                if (!player.isActiveAndEnabled || camera == null || !camera.isActiveAndEnabled
                    || !IsStereoUrl(player.url)
                    || (player.renderMode != VideoRenderMode.CameraNearPlane && player.renderMode != VideoRenderMode.CameraFarPlane)) continue;
                if (selected == null || camera.depth > depth) { selected = player; depth = camera.depth; }
            }
            return selected;
        }
    }

    internal static string CanonicalKey(string? key)
    {
        if (GloomhavenVR.Net.VideoMovieKeys.TryUnwrapReview(key, out string selected)) key = selected;
        if (key == NativeIntroKey || key == LegacyStereoIntroKey) return IntroKey;
        if (key == null) return string.Empty;
        return GloomhavenVR.Net.VideoMovieKeys.CanonicalMovie(key);
    }
    internal static bool SamePresentation(string? first, string? second) =>
        GloomhavenVR.Net.VideoMovieKeys.IsReview(first) == GloomhavenVR.Net.VideoMovieKeys.IsReview(second)
        && CanonicalKey(first) == CanonicalKey(second);
    internal static string StereoKey(string canonical) => GloomhavenVR.Net.VideoMovieKeys.StereoKey(canonical);
    internal static bool IsCanonical(string key) => GloomhavenVR.Net.VideoMovieKeys.IsCanonical(key);
    internal static bool IsStereoKey(string? key)
    {
        if (GloomhavenVR.Net.VideoMovieKeys.TryUnwrapReview(key, out string selected)) key = selected;
        return key == LegacyStereoIntroKey || (!string.IsNullOrEmpty(key) && GloomhavenVR.Net.VideoMovieKeys.IsStereoMovie(key!));
    }

    internal static string NativePlaybackKey(VideoPlayer player)
    {
        string url = player.url;
        if (Selections.TryGetValue(player, out Selection? selected) && selected.Url == url) return selected.Key;
        string key = KeyFromUrl(url);
        // The native intro and approved replacement share a basename. Only the
        // intercepted assignment proves which content was selected.
        return key == IntroKey ? NativeIntroKey : key;
    }
    internal static string KeyFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        string path = url!.Replace('\\', '/');
        int last = path.LastIndexOf('/');
        if (last <= 0) return string.Empty;
        string key = path.Substring(path.LastIndexOf('/', last - 1) + 1);
        return IsPlaybackKey(key) ? key : string.Empty;
    }
    internal static bool IsStereoUrl(string? url) => IsStereoKey(KeyFromUrl(url));

    internal static bool IsPlaybackKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128) return false;
        if (GloomhavenVR.Net.VideoMovieKeys.IsReview(key)) return true;
        string extension = Application.platform == RuntimePlatform.Switch ? ".mp4" : ".mov";
        if (!key.EndsWith(extension, StringComparison.Ordinal)) return false;
        string canonical = CanonicalKey(key);
        if (IsCanonical(canonical)) return extension == ".mov" || key == canonical;
        // Retain the existing native hero grammar for game revisions/additional heroes.
        // Stereo revisions are admitted only by the exact catalogue above.
        string stem = key.Substring(0, key.Length - extension.Length);
        if (!stem.StartsWith("Heroes/", StringComparison.Ordinal) || stem.Length <= 7) return false;
        for (int i = 7; i < stem.Length; i++)
        {
            char c = stem[i];
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z')
                && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
        }
        return true;
    }

    private static bool TryModMovie(string key, out string url)
    {
        url = string.Empty;
        if (!IsPlaybackKey(key) || GloomhavenVR.Net.VideoMovieKeys.IsReview(key) || key == NativeIntroKey || Application.platform == RuntimePlatform.Switch) return false;
        string directory = Path.GetDirectoryName(typeof(NativeVideoMovies).Assembly.Location)!;
        string candidate = Path.Combine(directory, "Movies", key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(candidate)) return false;
        url = candidate;
        return true;
    }

    internal static bool TryResolvePlaybackKey(string key, out string url)
    {
        url = string.Empty;
        if (!IsPlaybackKey(key)) return false;
        if (GloomhavenVR.Net.VideoMovieKeys.TryUnwrapReview(key, out string selected)) key = selected;
        if (TryModMovie(key, out url)) return true;
        if (IsStereoKey(key))
        {
            key = CanonicalKey(key);
            if (TryModMovie(key, out url)) return true;
        }
        // The approved intro may only degrade to its approved original left picture.
        if (key == IntroKey) return TryModMovie(IntroKey, out url);
        if (key == NativeIntroKey) key = IntroKey;
        string root;
        if (Application.platform == RuntimePlatform.OSXPlayer)
            root = Path.Combine(Application.dataPath, "Resources", "Data", "StreamingAssets", "Movies");
        else
        {
            string folder = Application.platform == RuntimePlatform.Switch ? "Movies_MP4_30"
                : Application.platform == RuntimePlatform.GameCoreXboxOne ? "Movies_MOV_30" : "Movies";
            root = Path.Combine(Application.dataPath, "StreamingAssets", folder);
        }
        string candidate = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(candidate)) return false;
        url = candidate;
        return true;
    }

    internal static bool TrySelectReview(string canonical, out string selectedKey, out string url)
    {
        selectedKey = canonical;
        url = string.Empty;
        if (!IsCanonical(canonical)) return false;
        string stereoKey = StereoKey(canonical);
        bool hasStereo = TryModMovie(stereoKey, out string stereo);
        if (hasStereo && StereoVideoSurface.IsAvailable(out _))
        {
            selectedKey = stereoKey;
            url = stereo;
            return true;
        }
        if (!TryResolvePlaybackKey(canonical, out url)) return false;
        string reason = hasStereo ? "stereo renderer unavailable" : "selected stereo file unavailable";
        VRLog.Warn("WorldUI", $"NATIVE VIDEO REVIEW STEREO FALLBACK: key={stereoKey}, reason={reason}; using the exact original left companion in 2D.");
        return true;
    }

    internal static void SetAmbientUrl(VideoPlayer player, string originalUrl)
    {
        SetNativeUrl(player, originalUrl, KeyFromUrl(originalUrl).Replace(".mov", string.Empty));
        RegisterCameraPlane(player);
    }

    internal static void SetEmbeddedSource(VideoPlayer player)
    {
        // Both actual serialized clips are camera-plane sources. Keep that mode and
        // exact target camera: the game's normal callbacks and timing remain intact.
        string canonical = player.source == VideoSource.VideoClip && player.clip != null
            ? "Embedded/" + player.clip.name + ".mov" : CanonicalKey(KeyFromUrl(player.url));
        if (!canonical.StartsWith("Embedded/", StringComparison.Ordinal) || !IsCanonical(canonical)) return;
        NativeVideoReview.CancelForNativePlayback();
        bool vr = VRSession.IsRunning && WorldUIConfig.ConversionActive;
        string selected = StereoKey(canonical);
        if (vr && StereoVideoSurface.IsAvailable(out _) && TryModMovie(selected, out string stereo))
        {
            player.source = VideoSource.Url;
            SetNativeUrl(player, stereo, canonical.Substring(0, canonical.Length - 4));
            RegisterCameraPlane(player);
        }
        else if (TryModMovie(canonical, out string mono))
        {
            player.source = VideoSource.Url;
            // SetNativeUrl applies the same native mono/stereo preflight and provenance.
            SetNativeUrl(player, mono, canonical.Substring(0, canonical.Length - 4));
            RegisterCameraPlane(player);
        }
        else if (player.source == VideoSource.Url && IsStereoUrl(player.url))
        {
            // The original serialized clip remains bound even while source is Url.
            // If a mod companion disappears, reuse it instead of exposing packed SBS.
            if (player.clip != null) player.source = VideoSource.VideoClip;
            VRLog.Warn("WorldUI", $"NATIVE MOVIE STEREO FALLBACK: key={selected}, reason=embedded companion unavailable; original serialized clip retained.");
        }
    }

    internal static void SetNativeUrl(VideoPlayer player, string originalUrl, string requestedName)
    {
        // This is the original setter seam, before first Play/Prepare. Never restart a
        // native decoder or change renderMode/targetTexture after playback starts.
        NativeVideoReview.CancelForNativePlayback();
        string url = originalUrl;
        string key = KeyFromUrl(originalUrl);
        if (key == IntroKey) key = NativeIntroKey;
        string canonical = requestedName + ".mov";
        bool intro = canonical == IntroKey;
        if (IsCanonical(canonical) && Application.platform != RuntimePlatform.Switch)
        {
            unchecked { _opening++; if (intro) _introOpening++; }
            bool presentingVr = VRSession.IsRunning && WorldUIConfig.ConversionActive;
            string stereoKey = StereoKey(canonical);
            string reason = "versioned stereo asset is missing";
            bool hasStereo = TryModMovie(stereoKey, out string stereo);
            if (presentingVr && hasStereo && StereoVideoSurface.IsAvailable(out reason))
            {
                url = stereo;
                key = stereoKey;
            }
            else
            {
                if (TryModMovie(canonical, out string replacement)) { url = replacement; key = canonical; }
                if (presentingVr)
                {
                    if (intro)
                        VRLog.Warn("WorldUI", $"NATIVE INTRO STEREO FALLBACK: opening={_introOpening}, key={stereoKey}, resolved={key}, reason={reason}; native playback continues in 2D.");
                    else
                        VRLog.Warn("WorldUI", $"NATIVE MOVIE STEREO FALLBACK: opening={_opening}, key={stereoKey}, resolved={key}, reason={reason}; native playback continues in 2D.");
                }
            }
            if (intro)
            {
                VRLog.Note("WorldUI", $"NATIVE INTRO SELECTED: opening={_introOpening}, key={key}; native playback owns clock, audio and completion.");
                if (url != originalUrl && !_reportedReplacement)
                {
                    _reportedReplacement = true;
                    VRLog.Note("WorldUI", "NATIVE INTRO REPLACEMENT: using the approved mod-local campaign movie; native playback owns completion.");
                }
            }
            else VRLog.Note("WorldUI", $"NATIVE MOVIE SELECTED: opening={_opening}, key={key}; native playback owns clock, audio and completion.");
        }
        player.url = url;
        Selection selected = Selections.GetValue(player, _ => new Selection());
        selected.Url = url;
        selected.Key = key;
    }
}

/// <summary>Change the existing assignment before its first Play; leave native lifecycle intact.</summary>
[HarmonyPatch(typeof(VideoCamera), nameof(VideoCamera.PlayFullscreenVideo))]
internal static class NativeVideoMoviePatch
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        MethodInfo setter = AccessTools.PropertySetter(typeof(VideoPlayer), nameof(VideoPlayer.url));
        int count = 0;
        foreach (CodeInstruction instruction in code)
            if (instruction.Calls(setter)) count++;
        if (count != 1)
        {
            VRLog.Warn("WorldUI", "NATIVE INTRO REPLACEMENT: native URL assignment changed; keeping original movie playback.");
            return code;
        }
        MethodInfo replacement = AccessTools.Method(typeof(NativeVideoMovies), nameof(NativeVideoMovies.SetNativeUrl));
        var result = new List<CodeInstruction>(code.Count + 1);
        foreach (CodeInstruction instruction in code)
        {
            if (!instruction.Calls(setter)) { result.Add(instruction); continue; }
            // The native setter stack already contains (player, url). Append the original
            // requested name. Entry markers belong before the load; an exception-block end
            // must remain after the replacement call, keeping that call inside the native try.
            // No Stop/Play restart, prefix callback or postfix decoder mutation.
            var name = new CodeInstruction(OpCodes.Ldarg_1);
            name.labels.AddRange(instruction.labels);
            var call = new CodeInstruction(OpCodes.Call, replacement);
            foreach (ExceptionBlock block in instruction.blocks)
            {
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) call.blocks.Add(block);
                else name.blocks.Add(block);
            }
            result.Add(name);
            result.Add(call);
        }
        return result;
    }
}

/// <summary>Replace the existing ambient setter before its ordinary single Play.</summary>
[HarmonyPatch(typeof(BackgroundView), "Update")]
internal static class NativeAmbientMoviePatch
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        MethodInfo setter = AccessTools.PropertySetter(typeof(VideoPlayer), nameof(VideoPlayer.url));
        int count = 0;
        foreach (CodeInstruction instruction in code) if (instruction.Calls(setter)) count++;
        if (count != 1)
        {
            VRLog.Warn("WorldUI", "NATIVE MOVIE STEREO FALLBACK: ambient URL assignment changed; keeping original playback.");
            return code;
        }
        MethodInfo replacement = AccessTools.Method(typeof(NativeVideoMovies), nameof(NativeVideoMovies.SetAmbientUrl));
        for (int i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(setter)) continue;
            var changed = new CodeInstruction(code[i]);
            changed.opcode = OpCodes.Call;
            changed.operand = replacement;
            code[i] = changed;
        }
        return code;
    }
}

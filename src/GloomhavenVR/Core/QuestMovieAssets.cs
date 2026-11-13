using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.Core;

/// <summary>Small independently delivered Android movie shaders; original game banks remain unchanged.</summary>
public static class QuestMovieAssets
{
    private static AssetBundle? _bank;

    internal static Shader Shader(string name)
    {
        if (!QuestStandalonePlatform.Enabled) throw new InvalidOperationException("Android movie assets require Quest standalone.");
        if (_bank == null)
        {
            string path = Path.Combine(QuestStandalonePlatform.ResourceDirectory, "Movies", "ghvr-quest-movies.bundle");
            try { _bank = AssetBundle.LoadFromFile(path); }
            catch (Exception error) { throw Unavailable("Android movie shader bank load failed: " + error.GetType().Name); }
            if (_bank == null) throw Unavailable("Android stereo movie shader bank is unavailable.");
        }
        string asset = name == "GloomhavenVR/StereoVideoUI" ? "assets/stereovideoui.shader"
            : name == "Hidden/GloomhavenVR/QuestMovieEye" ? "assets/questmovieeye.shader"
            : throw new ArgumentException("Unknown Android movie shader.", nameof(name));
        Shader shader;
        try { shader = _bank.LoadAsset<Shader>(asset); }
        catch (Exception error) { throw Unavailable("Android movie shader load failed: " + error.GetType().Name); }
        if (shader == null || shader.name != name || !shader.isSupported)
            throw Unavailable("Android movie shader is unavailable: " + name);
        return shader;
    }

    private static InvalidDataException Unavailable(string reason)
    {
        // A same-size damaged local bank cannot be detected by the warm size
        // witness. Retire only its tiny receipt: the next launch repairs that
        // 24KB bank while all movie files remain retained. Current playback
        // follows the native mono fallback selected before Play.
        try
        {
            File.Delete(Path.Combine(QuestStandalonePlatform.ResourceDirectory, "Movies", "ghvr-quest-movies.bundle.quest-receipt"));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new InvalidDataException(reason);
    }

    /// <summary>The startup adapter clears embedded clip references, but preserves their canonical URL identity.</summary>
    public static bool TryEmbeddedCompanion(string name, out string path)
    {
        path = string.Empty;
        if (!QuestStandalonePlatform.Enabled || name != "TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best") return false;
        string candidate = Path.Combine(QuestStandalonePlatform.ResourceDirectory, "Movies", "Embedded", name + ".mov");
        if (!File.Exists(candidate)) return false;
        path = candidate;
        return true;
    }

    /// <summary>Only an explicitly versioned source with an owned stereo replacement may bypass the mono adapter.</summary>
    public static bool UsesStereoPlane(VideoPlayer player) => QuestStandalonePlatform.Enabled
        && player != null && WorldUI.NativeVideoMovies.IsStereoUrl(player.url);
}

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
            _bank = AssetBundle.LoadFromFile(path);
            if (_bank == null) throw new InvalidDataException("Android stereo movie shader bank is unavailable.");
        }
        string asset = name == "GloomhavenVR/StereoVideoUI" ? "assets/stereovideoui.shader"
            : name == "Hidden/GloomhavenVR/QuestMovieEye" ? "assets/questmovieeye.shader"
            : throw new ArgumentException("Unknown Android movie shader.", nameof(name));
        Shader shader = _bank.LoadAsset<Shader>(asset);
        if (shader == null || shader.name != name || !shader.isSupported)
            throw new InvalidDataException("Android movie shader is unavailable: " + name);
        return shader;
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

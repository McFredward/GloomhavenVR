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

/// <summary>Resolve the campaign's approved mod-local movie without changing its native identity.</summary>
internal static class NativeVideoMovies
{
    private const string IntroName = "CP_Intro/GH_CP_Intro";
    private static bool _reportedReplacement;

    // The maintainer approved the trailer's opening as the first-save intro on 2026-10-16.
    // Keep the game installation read-only: the replacement travels beside the plugin. Both
    // native playback and cosmetic peer decoders must resolve the same canonical movie key;
    // publishing this machine's mod path would break multiplayer and native skip matching.
    internal static bool IsPlaybackKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128) return false;
        string extension = Application.platform == RuntimePlatform.Switch ? ".mp4" : ".mov";
        if (!key.EndsWith(extension, StringComparison.Ordinal)) return false;
        string stem = key.Substring(0, key.Length - extension.Length);
        if (stem == IntroName) return true;
        if (!stem.StartsWith("Heroes/", StringComparison.Ordinal) || stem.Length <= 7) return false;
        for (int i = 7; i < stem.Length; i++)
        {
            char c = stem[i];
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z')
                && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
        }
        return true;
    }

    private static bool TryModIntro(string key, out string url)
    {
        url = string.Empty;
        // Only this MOV is shipped. Other assets and platform-specific MP4s stay native.
        if (key != IntroName + ".mov") return false;
        string directory = Path.GetDirectoryName(typeof(NativeVideoMovies).Assembly.Location)!;
        string candidate = Path.Combine(directory, "Movies", "CP_Intro", "GH_CP_Intro.mov");
        if (!File.Exists(candidate)) return false;
        url = candidate;
        return true;
    }

    internal static bool TryResolvePlaybackKey(string key, out string url)
    {
        url = string.Empty;
        if (!IsPlaybackKey(key)) return false;
        if (TryModIntro(key, out url)) return true;
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

    internal static void SetNativeUrl(VideoPlayer player, string originalUrl, string requestedName)
    {
        string url = originalUrl;
        if (requestedName == IntroName && Application.platform != RuntimePlatform.Switch
            && TryModIntro(IntroName + ".mov", out string replacement))
        {
            url = replacement;
            if (!_reportedReplacement)
            {
                _reportedReplacement = true;
                VRLog.Note("WorldUI", "NATIVE INTRO REPLACEMENT: using the approved mod-local campaign movie; native playback owns completion.");
            }
        }
        player.url = url;
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

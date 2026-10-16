using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

internal static class MovieResolutionTests
{
    internal static void Run(Action<bool, string> check)
    {
        string folder = Path.Combine(Path.GetDirectoryName(typeof(NativeVideoMovies).Assembly.Location)!, "Movies");
        string game = Path.Combine(Path.GetTempPath(), "ghvr-native-movie-" + Guid.NewGuid().ToString("N"));
        string mod = Path.Combine(folder, "CP_Intro", "GH_CP_Intro.mov");
        string native = Path.Combine(game, "StreamingAssets", "Movies", "CP_Intro", "GH_CP_Intro.mov");
        string hero = Path.Combine(game, "StreamingAssets", "Movies", "Heroes", "Brute.mov");
        string previousDataPath = Application.dataPath;
        RuntimePlatform previousPlatform = Application.platform;
        if (Directory.Exists(folder)) throw new Exception("Refusing to overwrite existing test movie output");
        var harmony = new Harmony("ghvr.test.native-intro-url");
        try
        {
            Application.dataPath = game;
            Application.platform = RuntimePlatform.WindowsPlayer;
            Directory.CreateDirectory(Path.GetDirectoryName(native)!);
            Directory.CreateDirectory(Path.GetDirectoryName(hero)!);
            File.WriteAllText(native, "original game movie");
            File.WriteAllText(hero, "original hero movie");
            check(NativeVideoWindow.TryResolvePlaybackKey("CP_Intro/GH_CP_Intro.mov", out string url) && url == native,
                "missing optional mod intro falls back to native campaign movie");
            harmony.CreateClassProcessor(typeof(NativeVideoMoviePatch)).Patch();
            var owner = new GameObject("Resolver native").AddComponent<VideoCamera>();
            var player = owner.m_VideoPlayer = owner.gameObject.AddComponent<VideoPlayer>();
            owner.m_Camera = owner.gameObject.AddComponent<Camera>();
            owner.OriginalUrl = native;
            int completed = 0;
            check(owner.PlayFullscreenVideo("CP_Intro/GH_CP_Intro", () => completed++, false)
                && player.PlayedUrl == native && player.PlayCalls == 1 && completed == 0,
                "native missing-mod fallback plays once without invoking completion early");

            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
            File.WriteAllText(mod, "approved mod-local movie");
            check(NativeVideoWindow.TryResolvePlaybackKey("CP_Intro/GH_CP_Intro.mov", out url) && url == mod,
                "remote canonical intro key resolves the same optional mod movie");
            int plays = player.PlayCalls;
            int setups = owner.NativeSetupCalls;
            check(owner.PlayFullscreenVideo("CP_Intro/GH_CP_Intro", () => completed++, true)
                && player.PlayedUrl == mod && player.PlayCalls == plays + 1
                && owner.NativeSetupCalls == setups + 1 && completed == 0 && owner.RewardInputLocked,
                "actual Harmony interception assigns mod intro before the single native Play and setup");
            VideoCamera.s_This = owner;
            check(NativeVideoWindow.PlaybackKey == "CP_Intro/GH_CP_Intro.mov",
                "mod-local native decoder retains the exact canonical playback identity");
            check(NativeVideoWindow.TrySkipNativeMovie() == false && completed == 0,
                "campaign resolver never invents an intro skip callback without the native step");
            owner.Stop();
            check(completed == 0 && !player.isPlaying, "ordinary native Stop retains native callback semantics");
            owner.OriginalUrl = hero;
            owner.PlayFullscreenVideo("Heroes/Brute", () => completed++, false);
            check(player.PlayedUrl == hero && NativeVideoWindow.PlaybackKey == "Heroes/Brute.mov",
                "actual Harmony interception leaves Hero playback and canonical identity unchanged");
            owner.OriginalUrl = native;
            foreach (string name in new[] { "CP_Intro/Other", "../CP_Intro/GH_CP_Intro", "CP_Intro/GH_CP_Intro.mov", "https://example.com/CP_Intro/GH_CP_Intro" })
            {
                owner.PlayFullscreenVideo(name, () => completed++, false);
                check(player.PlayedUrl == native, "non-exact native movie request cannot select replacement: " + name);
            }
            foreach (string key in new[] { "CP_Intro/../CP_Intro/GH_CP_Intro.mov", "CP_Intro/GH_CP_Intro.mov?x", "https://example.com/CP_Intro/GH_CP_Intro.mov", "CP_Intro/Other.mov" })
                check(!NativeVideoWindow.TryResolvePlaybackKey(key, out url) && url == string.Empty,
                    "resolver rejects malformed peer keys before probing local paths: " + key);
            check(NativeVideoWindow.TryResolvePlaybackKey("Heroes/Brute.mov", out url) && url == hero,
                "remote Hero key still resolves native game media");

            Application.platform = RuntimePlatform.Switch;
            owner.PlayFullscreenVideo("CP_Intro/GH_CP_Intro", () => completed++, false);
            check(player.PlayedUrl == native && !NativeVideoWindow.IsPlaybackKey("CP_Intro/GH_CP_Intro.mov"),
                "MOV replacement never overrides native Switch MP4 playback");
            Application.platform = RuntimePlatform.OSXPlayer;
            check(NativeVideoWindow.TryResolvePlaybackKey("CP_Intro/GH_CP_Intro.mov", out url) && url == mod,
                "canonical Mac intro uses the same optional mod-local MOV");
            Application.platform = RuntimePlatform.GameCoreXboxOne;
            check(NativeVideoWindow.TryResolvePlaybackKey("CP_Intro/GH_CP_Intro.mov", out url) && url == mod,
                "canonical Xbox MOV intro uses the same optional mod-local MOV");
            Application.platform = RuntimePlatform.WindowsPlayer;

            var setter = AccessTools.PropertySetter(typeof(VideoPlayer), nameof(VideoPlayer.url));
            var absent = new[] { new CodeInstruction(OpCodes.Nop) };
            check(NativeVideoMoviePatch.Transpiler(absent).Single() == absent[0],
                "changed native IL without a URL setter is preserved");
            var ambiguous = new[] { new CodeInstruction(OpCodes.Callvirt, setter), new CodeInstruction(OpCodes.Callvirt, setter) };
            check(NativeVideoMoviePatch.Transpiler(ambiguous).SequenceEqual(ambiguous),
                "ambiguous native URL setters fail closed without a partial patch");
            var original = new CodeInstruction(OpCodes.Callvirt, setter);
            var label = new System.Reflection.Emit.DynamicMethod("branch", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
            original.labels.Add(label);
            original.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            original.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            var changed = NativeVideoMoviePatch.Transpiler(new[] { original }).ToArray();
            check(changed.Length == 2 && changed[0].opcode == OpCodes.Ldarg_1
                && changed[0].labels.Single() == label
                && changed[0].blocks.Single().blockType == ExceptionBlockType.BeginExceptionBlock
                && changed[1].blocks.Single().blockType == ExceptionBlockType.EndExceptionBlock
                && changed[1].Calls(AccessTools.Method(typeof(NativeVideoMovies), nameof(NativeVideoMovies.SetNativeUrl)))
                && original.opcode == OpCodes.Callvirt,
                "transpiler preserves branch and exception markers without mutating original IL");
            File.Delete(mod);
            owner.PlayFullscreenVideo("CP_Intro/GH_CP_Intro", () => completed++, false);
            check(player.PlayedUrl == native
                && NativeVideoWindow.TryResolvePlaybackKey("CP_Intro/GH_CP_Intro.mov", out url) && url == native,
                "removing optional movie restores both native and remote original paths");
            check(File.ReadAllText(native) == "original game movie" && File.ReadAllText(hero) == "original hero movie",
                "native and Hero game references remain byte-for-byte untouched");
        }
        finally
        {
            harmony.Unpatch(AccessTools.Method(typeof(VideoCamera), nameof(VideoCamera.PlayFullscreenVideo)),
                HarmonyPatchType.All, harmony.Id);
            NativeVideoWindow.Shutdown();
            VideoCamera.s_This = null;
            Application.dataPath = previousDataPath;
            Application.platform = previousPlatform;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (Directory.Exists(game)) Directory.Delete(game, true);
        }
    }
}

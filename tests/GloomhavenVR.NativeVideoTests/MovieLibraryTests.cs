using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Video;

internal static class MovieLibraryTests
{
    internal static void Run(Action<bool, string> check)
    {
        NativeVideoWindow.Shutdown(); VideoCamera.s_This = null;
        string directory = Path.Combine(Path.GetDirectoryName(typeof(NativeVideoMovies).Assembly.Location)!, "Movies");
        string game = Path.Combine(Path.GetTempPath(), "ghvr-library-" + Guid.NewGuid().ToString("N"));
        string oldDataPath = Application.dataPath;
        if (Directory.Exists(directory)) throw new Exception("Library fixture refuses existing movie directory");
        var harmony = new Harmony("ghvr.test.movie-library");
        try
        {
            Application.dataPath = game;
            var owner = new GameObject("Native library owner").AddComponent<VideoCamera>();
            var player = owner.m_VideoPlayer = owner.gameObject.AddComponent<VideoPlayer>();
            owner.m_Camera = owner.gameObject.AddComponent<Camera>();
            harmony.CreateClassProcessor(typeof(NativeVideoMoviePatch)).Patch();
            harmony.CreateClassProcessor(typeof(NativeAmbientMoviePatch)).Patch();
            harmony.CreateClassProcessor(typeof(NativeStartupMoviePatch)).Patch();
            for (int i = 0; i < NativeVideoMovies.Count; i++)
            {
                string canonical = NativeVideoMovies.GetKey(i), stereoKey = NativeVideoMovies.StereoKey(canonical);
                string stereo = Path.Combine(directory, stereoKey);
                string original = canonical.StartsWith("Embedded/", StringComparison.Ordinal) || canonical == NativeVideoMovies.IntroKey
                    ? Path.Combine(directory, canonical) : Path.Combine(game, "StreamingAssets", "Movies", canonical);
                Directory.CreateDirectory(Path.GetDirectoryName(stereo)!);
                Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                File.WriteAllText(stereo, "opaque SBS witness " + i);
                File.WriteAllText(original, "unchanged original " + i);
                check(VideoWindowCodec.ValidClip(VideoMovieKeys.ReviewKey(canonical))
                    && VideoWindowCodec.ValidClip(VideoMovieKeys.ReviewKey(stereoKey)),
                    "wire and runtime share the same finite original catalogue: " + canonical);
                check(NativeVideoMovies.TryResolvePlaybackKey(VideoMovieKeys.ReviewKey(stereoKey), out string resolved) && resolved == stereo,
                    "review alias resolves exact selected stereo revision without a physical alias file: " + canonical);
                check(NativeVideoMovies.IsStereoKey(VideoMovieKeys.ReviewKey(stereoKey))
                    && !NativeVideoMovies.IsStereoKey(VideoMovieKeys.ReviewKey(canonical)),
                    "review alias retains exact stereo or mono interpretation: " + canonical);
                check(!NativeVideoMovies.SamePresentation(VideoMovieKeys.ReviewKey(stereoKey), stereoKey)
                    && NativeVideoMovies.SamePresentation(VideoMovieKeys.ReviewKey(stereoKey), VideoMovieKeys.ReviewKey(canonical)),
                    "review retirement is separate from genuine native playback of the same movie: " + canonical);
                if (!canonical.StartsWith("Embedded/", StringComparison.Ordinal))
                {
                    owner.OriginalUrl = original;
                    int calls = player.PlayCalls;
                    owner.PlayFullscreenVideo(canonical.Substring(0, canonical.Length - 4), () => throw new Exception("Native callback ran during selection"), false);
                    check(player.PlayedUrl == stereo && player.PlayCalls == calls + 1,
                        "every external exact original selects SBS before single native Play: " + canonical);
                }
                check(NativeVideoMovies.TryResolvePlaybackKey(canonical, out resolved) && resolved == original,
                    "owner-selected mono never upgrades to present stereo: " + canonical);
                File.Delete(stereo);
                check(NativeVideoMovies.TryResolvePlaybackKey(VideoMovieKeys.ReviewKey(stereoKey), out resolved) && resolved == original,
                    "missing stereo resolves exact left companion, including mod-local studio: " + canonical);
                check(File.ReadAllText(original) == "unchanged original " + i, "native source bytes stay read-only: " + canonical);
            }
            string[] aliases = Enumerable.Range(0, NativeVideoMovies.Count)
                .Select(index => VideoMovieKeys.ReviewKey(NativeVideoMovies.StereoKey(NativeVideoMovies.GetKey(index)))).ToArray();
            for (int warmup = 0; warmup < 100; warmup++)
                foreach (string alias in aliases) { _ = VideoMovieKeys.IsReview(alias); _ = NativeVideoMovies.IsStereoKey(alias); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int validAliases = 0;
            for (int repeat = 0; repeat < 1000; repeat++)
                foreach (string alias in aliases)
                    if (VideoMovieKeys.IsReview(alias) && NativeVideoMovies.IsStereoKey(alias)) validAliases++;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine("Warm movie-key witness: valid=" + validAliases + ", allocated=" + allocated + " bytes.");
            check(validAliases == 20000, "twenty thousand warm finite-key validations retain exact catalogue semantics");
            foreach (string bad in new[] { "Review/Review/Heroes/Summoner.mov", "Review/Heroes/Brute.mov", "Review/../Heroes/Summoner.mov",
                         "Review/Embedded/gameplay_trailer.mov", "Review/CP_Intro/GH_CP_Intro.native.mov", "Review/CP_Intro/GH_CP_Intro.sbs-v3.mov",
                         "Review/Ambient/Other.mov", "Review/" + new string('A', 150) + ".mov" })
                check(!NativeVideoMovies.IsPlaybackKey(bad) && !VideoWindowCodec.ValidClip(bad)
                    && !NativeVideoMovies.TryResolvePlaybackKey(bad, out _),
                    "review namespace refuses nested, foreign, retired and oversized aliases: " + bad);
            string ambient = "Ambient/Ambient_Crypt_01.mov";
            string ambientStereo = Path.Combine(directory, NativeVideoMovies.StereoKey(ambient));
            File.WriteAllText(ambientStereo, "ambient SBS");
            var ambientPlayer = new GameObject("Actual ambient caller").AddComponent<VideoPlayer>();
            ambientPlayer.targetCamera = ambientPlayer.gameObject.AddComponent<Camera>();
            ambientPlayer.renderMode = VideoRenderMode.CameraFarPlane;
            var background = new BackgroundView { Player = ambientPlayer, OriginalUrl = Path.Combine(game, "StreamingAssets", "Movies", ambient) };
            int questPrepares = FlatScreen.QuestPrepareCalls;
            background.Update();
            check(FlatScreen.QuestPrepareCalls == questPrepares + 1, "actual ambient setter reaches camera-plane preparation before first Play");
            check(ambientPlayer.PlayedUrl == ambientStereo && ambientPlayer.PlayCalls == 1
                && ambientPlayer.renderMode == VideoRenderMode.CameraFarPlane && NativeVideoMovies.CameraPlaneSource == ambientPlayer,
                "actual ambient Harmony seam retains camera mode and first-play selection");
            ambientPlayer.enabled = false;
            string studio = "Embedded/TWINSAILS_GFX_ANIMATION_LOGO_2K_30FPS_H264_best.mov";
            string studioStereo = Path.Combine(directory, NativeVideoMovies.StereoKey(studio));
            File.WriteAllText(studioStereo, "studio SBS");
            var startupPlayer = new GameObject("Actual startup caller").AddComponent<VideoPlayer>();
            startupPlayer.source = VideoSource.VideoClip;
            startupPlayer.clip = new VideoClip { name = Path.GetFileNameWithoutExtension(studio) };
            startupPlayer.targetCamera = startupPlayer.gameObject.AddComponent<Camera>();
            startupPlayer.renderMode = VideoRenderMode.CameraNearPlane;
            var startup = new IntroPlayer(startupPlayer);
            int prepares = FlatScreen.StartupPrepareCalls;
            startup.Show();
            check(startupPlayer.PlayedUrl == studioStereo && startupPlayer.PlayCalls == 1 && startup.TimerStarts == 1
                && startupPlayer.source == VideoSource.Url && startupPlayer.clip != null
                && startupPlayer.renderMode == VideoRenderMode.CameraNearPlane,
                "actual startup Harmony seam replaces bound clip before single native Play without changing timer or camera binding");
            check(FlatScreen.StartupPrepareCalls == prepares + 1,
                "actual startup Harmony seam asks the real presentation owner before original Play");
            FlatScreen.StartupSurfaceReady = false;
            startup.Show();
            check(startupPlayer.PlayedUrl.EndsWith(studio) && startupPlayer.PlayCalls == 2 && startup.TimerStarts == 2
                && !NativeVideoMovies.IsStereoUrl(startupPlayer.PlayedUrl) && startupPlayer.targetCameraAlpha == 1,
                "unavailable startup surface uses exact mono before single native Play and unchanged timer");
            File.Delete(Path.Combine(directory, studio));
            startup.Show();
            check(startupPlayer.source == VideoSource.VideoClip && startupPlayer.url == string.Empty
                && startupPlayer.PlayCalls == 3 && startup.TimerStarts == 3,
                "missing startup companion returns to serialized clip without retaining packed URL or skipping native timer");
            FlatScreen.StartupSurfaceReady = true;
            startupPlayer.enabled = false;
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            NativeVideoWindow.Shutdown(); VideoCamera.s_This = null;
            Application.dataPath = oldDataPath;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            if (Directory.Exists(game)) Directory.Delete(game, true);
        }
    }
}

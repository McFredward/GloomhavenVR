using GloomhavenVR.Quest;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

internal static class Program
{
    static int assertions;
    static string files = "";
    static void Check(bool condition, string label)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(label);
    }
    static void Reject(Action action, string label)
    {
        try { action(); } catch (InvalidDataException) { assertions++; return; }
        catch (InvalidOperationException) { assertions++; return; }
        throw new InvalidOperationException(label);
    }
    sealed class Case : IDisposable
    {
        public readonly string Root;
        public readonly QuestGameContentManifest Content;
        public readonly QuestGameMovieManifest Movies;
        public readonly GameObject Canvas;
        public readonly VideoPlayer Player;
        public readonly Scene Scene;
        public readonly QuestGameVideos Router = new();
        public Case()
        {
            Root = Path.Combine(files, Guid.NewGuid().ToString("N"));
            string relative = "StreamingAssets/QuestOriginalMovies/owned intro #1.mp4";
            string target = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            byte[] bytes = "Owned MP4 byte fixture. Codec decoding remains a player test."u8.ToArray();
            File.WriteAllBytes(target, bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Content = new() { schema = 1, inputKey = "fixture-original-input", archive = "quest-startup-content.zip", archiveSha256 = new string('a', 64),
                files = new[] { new QuestGameContentFile { path = relative, size = bytes.Length, sha256 = hash } } };
            Movies = new() { schema = 1, scope = "original-startup-menu-movies", clips = new[] { new QuestGameMovie {
                guid = "original-clip-guid", name = "Original Intro", path = relative, size = bytes.Length, sha256 = hash,
                bindings = new[] { new QuestGameMovieBinding { scene = "Intro", playerName = "Movie", playerPath = "Canvas/Inactive/Movie", playerFileId = "41", sourceScene = "Assets/Scenes/Intro.unity" } } } } };
            Canvas = new("Canvas"); Player = Canvas.Add("Inactive", false).Add("Movie", false).AddPlayer();
            Scene = new("Intro", Canvas);
        }
        public void Install() => Router.Install(Movies, Content, Root, Content.inputKey);
        public void Dispose() { Router.Dispose(); Directory.Delete(Root, true); }
    }
    static void InvalidManifests()
    {
        foreach (var mutation in new Action<Case>[] {
            c => c.Movies.schema = 0,
            c => c.Movies.scope = "unrelated-media",
            c => c.Movies.clips = Array.Empty<QuestGameMovie>(),
            c => c.Movies.clips = null!,
            c => c.Movies.clips = Enumerable.Repeat(c.Movies.clips[0], 33).ToArray(),
            c => c.Movies.clips[0] = null!,
            c => c.Movies.clips[0].guid = "",
            c => c.Movies.clips[0].name = "",
            c => c.Movies.clips[0].path = "StreamingAssets/QuestOriginalMovies/../outside.mp4",
            c => c.Movies.clips[0].path = "/tmp/outside.mp4",
            c => c.Movies.clips[0].path = "StreamingAssets/QuestOriginalMovies\\outside.mp4",
            c => c.Movies.clips[0].path = "StreamingAssets/QuestOriginalMovies/unmanifested.mp4",
            c => c.Movies.clips[0].size++,
            c => c.Movies.clips[0].size = 0,
            c => c.Movies.clips[0].sha256 = new string('b', 64),
            c => c.Movies.clips[0].bindings = Array.Empty<QuestGameMovieBinding>(),
            c => c.Movies.clips[0].bindings = Enumerable.Repeat(c.Movies.clips[0].bindings[0], 33).ToArray(),
            c => c.Movies.clips[0].bindings[0].scene = "",
            c => c.Movies.clips[0].bindings[0].playerName = "",
            c => c.Movies.clips[0].bindings[0].playerPath = "",
            c => c.Movies.clips[0].bindings[0] = null!,
            c => c.Content.inputKey = "mismatched-input",
            c => c.Content.schema = 0,
            c => c.Content.files = c.Content.files.Concat(c.Content.files).ToArray(),
            c => File.Delete(Path.Combine(c.Root, c.Movies.clips[0].path)),
            c => File.AppendAllText(Path.Combine(c.Root, c.Movies.clips[0].path), "extra") })
        {
            using var c = new Case(); mutation(c);
            Reject(() => c.Router.Install(c.Movies, c.Content, c.Root, "fixture-original-input"), "invalid-manifest");
            Check(c.Player.Writes.Count == 0, "invalid-manifest-does-not-touch-native-player");
        }
        using (var c = new Case())
        {
            var binding = c.Movies.clips[0].bindings[0];
            c.Movies.clips[0].bindings = new[] { binding, binding };
            Reject(c.Install, "duplicate-player-mapping");
        }
        using (var c = new Case())
        {
            c.Movies.clips = c.Movies.clips.Concat(c.Movies.clips).ToArray();
            Reject(c.Install, "duplicate-clip-guid");
        }
        using (var c = new Case())
        {
            var original = c.Movies.clips[0];
            c.Movies.clips = new[] { original, new QuestGameMovie { guid = "second-clip", name = "Conflicting Intro", path = original.path,
                size = original.size, sha256 = original.sha256, bindings = original.bindings } };
            Reject(c.Install, "conflicting-player-mapping");
        }
        using (var c = new Case())
        {
            string target = Path.Combine(c.Root, c.Movies.clips[0].path);
            string moved = target + ".owned"; File.Move(target, moved); File.CreateSymbolicLink(target, moved);
            Reject(c.Install, "symlink-movie-source");
        }
    }
    static void BindingAndNativeLifecycle()
    {
        using var c = new Case();
        Check(Task.Run(() => QuestGameContent.IsReady(c.Content, c.Root)).GetAwaiter().GetResult(), "worker-verification");
        Check(Fixture.WorkerApiCalls == 0, "worker-api");
        int originalPrepared = 0, originalStarted = 0, originalErrors = 0, originalCompleted = 0;
        c.Player.prepareCompleted += _ => originalPrepared++;
        c.Player.started += _ => originalStarted++;
        c.Player.errorReceived += (_, _) => originalErrors++;
        c.Player.loopPointReached += _ => originalCompleted++;
        var texture = c.Player.targetTexture; var camera = c.Player.targetCamera;
        var renderer = c.Player.targetMaterialRenderer; var audio = c.Player.targetAudioSource;
        // An exclusive managed file handle permits stat/size but forbids a duplicate
        // managed content read. Install must consume prior worker verification.
        using (var held = new FileStream(Path.Combine(c.Root, c.Movies.clips[0].path), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            try { c.Install(); } catch (IOException e) { throw new InvalidOperationException("duplicate-main-thread-hash", e); }
        c.Router.BindScene(new Scene("OtherScene", c.Canvas));
        Check(c.Player.Writes.Count == 0, "scene-match");
        c.Router.BindScene(c.Scene);
        Check(c.Player.clip == null && c.Player.source == VideoSource.Url, "file-backed-source");
        string expected = Path.GetFullPath(Path.Combine(c.Root, c.Movies.clips[0].path));
        Check(c.Player.url == expected && c.Player.url.Contains(" #1") && !c.Player.url.StartsWith("file:"), "exact-owned-url");
        Check(c.Player.Writes.SequenceEqual(new[] { "clip", "source", "url" }), "only-source-properties");
        Check(!c.Player.gameObject.activeSelf && !c.Player.gameObject.transform.parent.gameObject.activeSelf, "inactive-player-before-start");
        Check(c.Player.PlayCalls == 0 && c.Player.PrepareCalls == 0 && c.Player.StopCalls == 0, "native-lifecycle-no-play");
        Check(!c.Player.playOnAwake && c.Player.isLooping && !c.Player.waitForFirstFrame && !c.Player.skipOnDrop, "native-video-flags");
        Check(c.Player.renderMode == "CameraNearPlane" && ReferenceEquals(texture, c.Player.targetTexture)
            && ReferenceEquals(camera, c.Player.targetCamera) && ReferenceEquals(renderer, c.Player.targetMaterialRenderer)
            && c.Player.targetMaterialProperty == "_OriginalMovie" && c.Player.aspectRatio == "FitHorizontally", "native-render-routing");
        Check(c.Player.audioOutputMode == "AudioSource" && ReferenceEquals(audio, c.Player.targetAudioSource)
            && c.Player.controlledAudioTrackCount == 2, "native-audio-routing");
        Check(c.Player.time == 12.75 && c.Player.playbackSpeed == 0.75f && c.Player.timeReference == "ExternalTime", "native-timing");
        Check(c.Player.PreparedSubscribers == 2 && c.Player.StartedSubscribers == 2
            && c.Player.ErrorSubscribers == 2 && c.Player.CompletedSubscribers == 1, "native-callbacks-preserved");
        c.Router.BindScene(c.Scene);
        Check(c.Player.Writes.Count == 3 && c.Player.PreparedSubscribers == 2, "binding-idempotent");
        for (int i = 0; i < 25; i++) { c.Player.EmitPrepared(); c.Player.EmitStarted(); c.Player.EmitError("fixture codec error"); }
        c.Player.EmitCompleted();
        Check(originalPrepared == 25 && originalStarted == 25 && originalErrors == 25 && originalCompleted == 1, "native-callback-delivery");
        Check(Fixture.Logs.Count(x => x.Contains("original movie prepared")) == 1, "bounded-prepared");
        Check(Fixture.Logs.Count(x => x.Contains("original movie started")) == 1, "bounded-started");
        Check(Fixture.Logs.Count(x => x.Contains("original movie failed")) == 2, "bounded-errors");
        Check(Fixture.Logs.Where(x => x.Contains("original movie failed")).All(x => x.Contains("exists=true bytes=" + c.Movies.clips[0].size)
            && x.Contains("expectedBytes=" + c.Movies.clips[0].size)), "failure-file-facts");
        c.Router.Observe();
        Check(!Fixture.Logs.Any(x => x.Contains("decoded frame")), "no-false-frame-proof");
        c.Player.frame = 0; c.Router.Observe();
        Check(!Fixture.Logs.Any(x => x.Contains("decoded frame")), "no-frame-without-texture");
        c.Player.texture = new Texture();
        for (int i = 0; i < 25; i++) c.Router.Observe();
        Check(Fixture.Logs.Count(x => x.Contains("decoded frame")) == 1, "bounded-frame");
        int logCount = Fixture.Logs.Count;
        c.Router.Dispose(); c.Router.Dispose();
        Check(c.Player.PreparedSubscribers == 1 && c.Player.StartedSubscribers == 1
            && c.Player.ErrorSubscribers == 1 && c.Player.CompletedSubscribers == 1, "dispose-unhooks-only-router");
        c.Player.EmitPrepared(); c.Player.EmitStarted(); c.Player.EmitError("after disposal"); c.Player.EmitCompleted(); c.Router.Observe();
        Check(Fixture.Logs.Count == logCount, "disposed-no-log");
        Check(originalPrepared == 26 && originalStarted == 26 && originalErrors == 26 && originalCompleted == 2, "disposed-native-callbacks");
        Check(c.Player.StopCalls == 0, "dispose-does-not-stop-native-player");
        Reject(() => c.Router.BindScene(c.Scene), "disposed-no-bind");
        Reject(c.Install, "disposed-no-install");
    }
    static void DeliveryProvenance()
    {
        foreach (bool derived in new[] { false, true })
        {
            using var c = new Case(); var clip = c.Movies.clips[0];
            clip.delivery = derived ? "android-mp4-tmcd-remux-v1" : "original";
            clip.originalSha256 = derived ? new string('b', 64) : clip.sha256;
            clip.originalSize = clip.size + (derived ? 452 : 0);
            Check(Task.Run(() => QuestGameContent.IsReady(c.Content, c.Root)).GetAwaiter().GetResult(), "derived-content-worker-verification");
            c.Install(); c.Router.BindScene(c.Scene);
            Check(c.Player.url == Path.Combine(c.Root, clip.path), "derived-exact-content-url");
            Check(c.Player.PlayCalls == 0 && c.Player.PrepareCalls == 0 && c.Player.StopCalls == 0, "derived-native-lifecycle");
            Check(c.Player.Writes.SequenceEqual(new[] { "clip", "source", "url" }), "derived-only-source-properties");
            c.Player.EmitError("native codec failure");
            Check(Fixture.Logs.Any(x => x.Contains("delivery=" + clip.delivery) && x.Contains("native codec failure")), "delivery-failure-context");
            File.Delete(Path.Combine(c.Root, clip.path)); c.Player.EmitError("file removed");
            Check(Fixture.Logs.Any(x => x.Contains("exists=false") && x.Contains("file removed")), "failure-missing-file-facts");
        }
        foreach (var mutation in new Action<QuestGameMovie>[] {
            clip => clip.delivery = "unknown-recipe",
            clip => clip.originalSha256 = "invalid",
            clip => clip.originalSha256 = new string('B', 64),
            clip => clip.originalSize = 0,
            clip => clip.originalSize = clip.size,
            clip => clip.originalSha256 = clip.sha256,
            clip => clip.delivery = "original",
            clip => clip.delivery = null! })
        {
            using var c = new Case(); var clip = c.Movies.clips[0];
            clip.delivery = "android-mp4-tmcd-remux-v1"; clip.originalSha256 = new string('b', 64); clip.originalSize = clip.size + 452;
            mutation(clip); Reject(c.Install, "invalid-derived-provenance");
            Check(c.Player.Writes.Count == 0, "invalid-derived-provenance-does-not-bind");
        }
        foreach (var mutation in new Action<QuestGameMovie>[] {
            clip => clip.originalSha256 = new string('b', 64),
            clip => clip.originalSize++,
            clip => clip.delivery = "android-mp4-tmcd-remux-v1" })
        {
            using var c = new Case(); var clip = c.Movies.clips[0];
            clip.delivery = "original"; clip.originalSha256 = clip.sha256; clip.originalSize = clip.size;
            mutation(clip); Reject(c.Install, "inconsistent-original-provenance");
        }
    }
    static void MissingAndAmbiguousPlayers()
    {
        using (var c = new Case())
        {
            c.Install(); Reject(c.Install, "one-install");
            Reject(() => c.Router.BindScene(new Scene("Intro", new GameObject("Other"))), "missing-required-player");
        }
        using (var c = new Case())
        {
            c.Install(); var other = new GameObject("Canvas"); other.Add("Inactive").Add("Movie").AddPlayer();
            Reject(() => c.Router.BindScene(new Scene("Intro", c.Canvas, other)), "ambiguous-player");
            Check(c.Player.Writes.Count == 0, "ambiguous-does-not-bind");
        }
        using (var c = new Case())
        {
            c.Movies.clips[0].bindings[0].playerPath = "Canvas/Wrong/Movie"; c.Install();
            Reject(() => c.Router.BindScene(c.Scene), "exact-hierarchy");
        }
        using (var c = new Case())
        {
            c.Movies.clips[0].bindings[0].playerName = "WrongPlayer"; c.Install();
            Reject(() => c.Router.BindScene(c.Scene), "exact-player-name");
        }
        using (var c = new Case())
        {
            Reject(() => c.Router.BindScene(c.Scene), "uninstalled-no-bind");
        }
        using (var c = new Case())
        {
            c.Install(); c.Router.BindScene(c.Scene);
            var recreated = new GameObject("Canvas");
            var newPlayer = recreated.Add("Inactive", false).Add("Movie", false).AddPlayer();
            var unrelated = recreated.Add("OtherMovie").AddPlayer();
            c.Router.BindScene(new Scene("Intro", recreated));
            Check(newPlayer.source == VideoSource.Url && newPlayer.url == c.Player.url, "recreated-scene-binds-current-player");
            Check(newPlayer.PlayCalls == 0 && newPlayer.Writes.Count == 3, "recreated-scene-native-lifecycle");
            Check(unrelated.Writes.Count == 0 && unrelated.PreparedSubscribers == 0, "unmapped-player-untouched");
            c.Router.Dispose();
            Check(newPlayer.PreparedSubscribers == 0 && c.Player.PreparedSubscribers == 0, "recreated-scene-unhooks-both");
        }
    }
    static void ActualSourcesAndBoundedDiagnostics()
    {
        Fixture.Logs.Clear(); Time.realtimeSinceStartup = 100;
        using (var c = new Case())
        {
            c.Install(); c.Router.BindScene(c.Scene);
            string boundUrl = c.Player.url;
            Check(Fixture.Logs.Count(x => x.Contains("reason=bound-before-native-start")) == 1, "initial-target-snapshot");
            Check(Fixture.Logs.Any(x => x.Contains("active=False hierarchy=False") && x.Contains("targetCamera=FixtureVideoCamera")
                && x.Contains("targetTexture=FixtureVideoTarget 1920x1080") && x.Contains("actualUrlKind=raw-path")
                && x.Contains("matchesBoundSource=True")), "actual-native-targets-and-source");
            for (int i = 0; i < 1000; i++) c.Router.Observe();
            Check(Fixture.Logs.Count(x => x.Contains("original movie state")) == 1, "no-frame-driven-state-stream");
            for (int i = 0; i < 1000; i++) { Time.realtimeSinceStartup += 1; c.Router.Observe(); }
            Check(Fixture.Logs.Count(x => x.Contains("reason=observed-after-native-start")) == 3, "bounded-timed-state");
            string ambient = Path.Combine(c.Root, "StreamingAssets", "Movies", "Ambient", "OriginalScene.mov");
            Directory.CreateDirectory(Path.GetDirectoryName(ambient)!); File.WriteAllBytes(ambient, new byte[91]);
            c.Player.url = ambient; c.Player.EmitPrepared(); c.Player.EmitStarted();
            c.Player.frame = 23; c.Player.texture = new Texture(); c.Router.Observe();
            foreach (string token in new[] { "original movie prepared", "original movie started", "original movie decoded frame" })
                Check(Fixture.Logs.Any(x => x.Contains(token) && x.Contains("actualUrl=" + ambient)
                    && x.Contains("matchesBoundSource=False") && x.Contains("actualBytes=91")), "actual-opened-media-" + token);
            Check(c.Player.url == ambient && c.Player.PlayCalls == 0 && c.Player.StopCalls == 0, "native-source-change-never-rewritten");
            c.Player.url = new Uri(ambient).AbsoluteUri; c.Router.Observe();
            Check(Fixture.Logs.Any(x => x.Contains("reason=native-source-changed") && x.Contains("actualUrlKind=file-uri")
                && x.Contains("actualExists=true actualBytes=91")), "observe-native-file-uri-without-rewriting");
            c.Player.url = boundUrl; c.Router.Observe();
            c.Player.url = "https://example.invalid/original-native-source"; c.Router.Observe();
            Check(Fixture.Logs.Any(x => x.Contains("actualUrlKind=other actualUrl=https://example.invalid")), "non-file-native-source-safe-evidence");
            for (int i = 0; i < 1000; i++) { c.Player.url = ambient + i; c.Router.Observe(); }
            Check(Fixture.Logs.Count(x => x.Contains("reason=native-source-changed")) == 4, "bounded-native-source-changes");
            Check(c.Player.Writes.Count == 1007 && c.Player.PrepareCalls == 0, "evidence-does-not-write-video-settings");
        }
        Fixture.Logs.Clear();
        GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging = false;
        using (var c = new Case())
        {
            c.Install(); c.Router.BindScene(c.Scene);
            c.Player.EmitPrepared(); c.Player.EmitStarted(); c.Player.EmitError("retained normal failure");
            c.Player.texture = new Texture(); c.Player.frame = 1;
            for (int i = 0; i < 1000; i++) { Time.realtimeSinceStartup++; c.Player.url = "/missing-ambient/" + i; c.Router.Observe(); }
            Check(!Fixture.Logs.Any(x => x.Contains("original movie state")), "normal-level-no-native-state-stream");
            Check(Fixture.Logs.Any(x => x.Contains("original movie prepared")) && Fixture.Logs.Any(x => x.Contains("original movie failed"))
                && Fixture.Logs.Any(x => x.Contains("original movie decoded frame")), "normal-level-keeps-lifecycle-failure-context");
        }
        GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging = true;
    }
    static int Main(string[] args)
    {
        try
        {
            files = Path.GetFullPath(args[0]); Directory.CreateDirectory(files);
            InvalidManifests(); Fixture.Logs.Clear(); BindingAndNativeLifecycle(); MissingAndAmbiguousPlayers(); DeliveryProvenance(); ActualSourcesAndBoundedDiagnostics();
            Check(Fixture.WorkerApiCalls == 0, "worker-api");
            Console.WriteLine($"PASS Quest startup videos: {assertions} assertions; actual movie/content source, Unity video/scene seams; no codec/stereo proof");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

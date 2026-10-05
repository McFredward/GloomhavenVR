#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace GloomhavenVR.Quest
{
    [Serializable] public sealed class QuestGameMovieManifest
    {
        public int schema = 0;
        public string scope = null;
        public QuestGameMovie[] clips = null;
        public QuestGameMovieBinding[] dynamicPlayers = null;
        public int nativePlayerCount = 0;
    }
    [Serializable] public sealed class QuestGameMovie
    {
        public string guid = null, name = null, path = null, sha256 = null;
        public long size = 0;
        public string delivery = null, originalSha256 = null;
        public long originalSize = 0;
        public QuestGameMovieBinding[] bindings = null;
    }
    [Serializable] public sealed class QuestGameMovieBinding
    {
        public string scene = null, playerName = null, playerPath = null, sourceScene = null;
        public string playerFileId = null;
    }

    /// <summary>Restores owned file-backed video sources before original scene Start.</summary>
    public sealed class QuestGameVideos : IDisposable
    {
        sealed class BoundPlayer
        {
            public VideoPlayer Player;
            public string Context;
            public string Path;
            public long ExpectedSize;
            public bool Prepared, Started, Frame;
            public int Errors;
            public float BoundAt;
            public int StateSamples, SourceChanges;
            public string LastObservedUrl;
            public QuestCameraVideoOutput Output;
        }
        readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<BoundPlayer> players = new List<BoundPlayer>();
        QuestGameMovieManifest manifest;
        bool disposed;

        public void Install(QuestGameMovieManifest movies, QuestGameContentManifest content, string root, string inputKey)
        {
            if (manifest != null || disposed) throw new InvalidOperationException("Movie delivery was already initialized or disposed.");
            QuestGameContent.Validate(content, inputKey);
            if (movies == null || movies.schema != 1 || (movies.scope != "original-startup-menu-movies" && movies.scope != "original-campaign-movies")
                || movies.clips == null || movies.clips.Length == 0 || movies.clips.Length > 32)
                throw new InvalidDataException("Original startup movie manifest is missing or invalid.");
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in movies.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.guid) || string.IsNullOrEmpty(clip.name)
                    || clip.path == null || !clip.path.StartsWith("StreamingAssets/QuestOriginalMovies/", StringComparison.Ordinal)
                    || clip.size <= 0 || clip.bindings == null || clip.bindings.Length == 0 || clip.bindings.Length > 32)
                    throw new InvalidDataException("Original startup movie entry is invalid.");
                ValidateProvenance(clip);
                // Content was hashed on the delivery worker. Require the exact same
                // manifested bytes, without another main-thread movie hash. Linux
                // cannot import these MP4s as VideoClips. Delivered bytes may be
                // an explicitly recorded, lossless Android container adaptation.
                QuestGameContentFile file = null;
                foreach (var candidate in content.files)
                    if (candidate.path == clip.path) { if (file != null) throw new InvalidDataException("Duplicate movie content path."); file = candidate; }
                if (file == null || file.size != clip.size || file.sha256 != clip.sha256)
                    throw new InvalidDataException("Movie does not match verified original content: " + clip.name);
                string absolute = QuestGameContent.ResolvePath(root, clip.path);
                if (!File.Exists(absolute) || new FileInfo(absolute).Length != file.size)
                    throw new InvalidDataException("Verified movie file is missing or changed: " + clip.name);
                if (paths.ContainsKey(clip.guid)) throw new InvalidDataException("Duplicate original movie identity.");
                paths.Add(clip.guid, absolute);
                foreach (var binding in clip.bindings)
                {
                    if (binding == null || string.IsNullOrEmpty(binding.scene) || string.IsNullOrEmpty(binding.playerName)
                        || string.IsNullOrEmpty(binding.playerPath)) throw new InvalidDataException("Original movie binding is invalid.");
                    if (!bindings.Add(binding.scene + "/" + binding.playerPath))
                        throw new InvalidDataException("Duplicate or conflicting original movie player binding.");
                }
            }
            bool campaign = movies.scope == "original-campaign-movies";
            if (!campaign && movies.dynamicPlayers != null && movies.dynamicPlayers.Length != 0
                || campaign && (movies.dynamicPlayers == null || movies.dynamicPlayers.Length > 64))
                throw new InvalidDataException("Dynamic original movie players require a complete campaign census.");
            foreach (var binding in movies.dynamicPlayers ?? new QuestGameMovieBinding[0])
            {
                long identity;
                if (binding == null || string.IsNullOrEmpty(binding.scene) || string.IsNullOrEmpty(binding.playerName)
                    || string.IsNullOrEmpty(binding.playerPath) || string.IsNullOrEmpty(binding.sourceScene)
                    || !binding.sourceScene.StartsWith("Assets/", StringComparison.Ordinal)
                    || !binding.sourceScene.EndsWith(".unity", StringComparison.Ordinal)
                    || binding.sourceScene.Contains("\\") || Array.Exists(binding.sourceScene.Split('/'), part => part == "..")
                    || System.IO.Path.GetFileNameWithoutExtension(binding.sourceScene) != binding.scene
                    || !long.TryParse(binding.playerFileId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out identity) || identity == 0
                    || (binding.playerPath != binding.playerName && !binding.playerPath.EndsWith("/" + binding.playerName, StringComparison.Ordinal)))
                    throw new InvalidDataException("Dynamic original movie player identity is invalid.");
                string key = binding.scene + "/" + binding.playerPath;
                if (bindings.Contains(key))
                    throw new InvalidDataException("Duplicate or conflicting original movie player binding.");
                bindings.Add(key);
            }
            if ((campaign || movies.nativePlayerCount != 0) && movies.nativePlayerCount != bindings.Count)
                throw new InvalidDataException("Original movie player census differs from its declared bindings.");
            manifest = movies;
            Debug.Log("[Quest startup] verified original movie sources installed clips=" + movies.clips.Length
                + " cameraPlayers=" + bindings.Count + " dynamicPlayers=" + (movies.dynamicPlayers == null ? 0 : movies.dynamicPlayers.Length));
        }

        static bool Sha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char digit in value) if (!(digit >= '0' && digit <= '9') && !(digit >= 'a' && digit <= 'f')) return false;
            return true;
        }
        static void ValidateProvenance(QuestGameMovie clip)
        {
            // Legacy manifests delivered original files before provenance fields
            // existed. New manifests explicitly distinguish source and delivery.
            if (string.IsNullOrEmpty(clip.delivery) && string.IsNullOrEmpty(clip.originalSha256) && clip.originalSize == 0) return;
            if (!Sha256(clip.originalSha256) || clip.originalSize <= 0)
                throw new InvalidDataException("Movie original provenance is invalid: " + clip.name);
            if (clip.delivery == "original" && clip.originalSha256 == clip.sha256 && clip.originalSize == clip.size) return;
            if (clip.delivery == "android-mp4-tmcd-remux-v1" && clip.originalSha256 != clip.sha256 && clip.originalSize > clip.size) return;
            throw new InvalidDataException("Movie delivery recipe or provenance is inconsistent: " + clip.name);
        }

        public void BindScene(Scene scene)
        {
            if (manifest == null || disposed) throw new InvalidOperationException("Original movie delivery is unavailable.");
            PruneDestroyedPlayers();
            foreach (var clip in manifest.clips)
            foreach (var binding in clip.bindings)
            {
                if (binding.scene != scene.name) continue;
                VideoPlayer selected = SelectPlayer(scene, binding);
                bool known = false;
                foreach (var bound in players) if (bound.Player == selected) { known = true; break; }
                if (known) continue;
                // sceneLoaded precedes Start. Keep the native timed intro, Play call,
                // rendering target, audio routing and completion callbacks intact.
                selected.clip = null;
                selected.source = VideoSource.Url;
                // B618 confirms the tmcd-free Intro's expected bytes, but its
                // file:// route fails in Unity's Android extractor. The working
                // menu player does not establish that route's compatibility:
                // BackgroundView replaces its URL with a raw Movies/Ambient
                // path. Match that original absolute-path route. Native Play,
                // Prepare and the authored four-second Intro stay untouched.
                // This addresses the path hypothesis without transcoding media.
                selected.url = paths[clip.guid];
                var record = new BoundPlayer { Player = selected, Path = paths[clip.guid], ExpectedSize = clip.size,
                    BoundAt = Time.realtimeSinceStartup, LastObservedUrl = selected.url,
                    Context = scene.name + "/" + binding.playerPath + " source=" + clip.name + " delivery=" + (clip.delivery ?? "original-legacy") };
                players.Add(record);
                record.Output = new QuestCameraVideoOutput(selected, record.Context);
                selected.prepareCompleted += Prepared;
                selected.started += Started;
                selected.errorReceived += Error;
                Debug.Log("[Quest startup] original movie URL bound " + record.Context);
                if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) SampleState(record, "bound-before-native-start");
            }
            foreach (var binding in manifest.dynamicPlayers ?? new QuestGameMovieBinding[0])
            {
                if (binding.scene != scene.name) continue;
                VideoPlayer selected = SelectPlayer(scene, binding);
                bool known = false;
                foreach (var bound in players) if (bound.Player == selected) { known = true; break; }
                if (known) continue;
                if (selected.clip != null)
                    throw new InvalidDataException("Dynamic original movie player has an unexpected embedded clip: " + binding.playerPath);
                var record = new BoundPlayer { Player = selected, BoundAt = Time.realtimeSinceStartup,
                    LastObservedUrl = selected.url, Context = scene.name + "/" + binding.playerPath + " source=authored-dynamic" };
                players.Add(record);
                record.Output = new QuestCameraVideoOutput(selected, record.Context);
                selected.prepareCompleted += Prepared;
                selected.started += Started;
                selected.errorReceived += Error;
                Debug.Log("[Quest startup] original dynamic movie output bound " + record.Context);
                if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) SampleState(record, "bound-before-native-start");
            }
        }

        static VideoPlayer SelectPlayer(Scene scene, QuestGameMovieBinding binding)
        {
            VideoPlayer selected = null;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var player in root.GetComponentsInChildren<VideoPlayer>(true))
            {
                if (player.gameObject.name != binding.playerName || HierarchyPath(player.transform) != binding.playerPath) continue;
                if (selected != null) throw new InvalidDataException("Ambiguous original movie player: " + binding.playerPath);
                selected = player;
            }
            if (selected == null) throw new InvalidDataException("Required original movie player is missing: " + scene.name + "/" + binding.playerPath);
            return selected;
        }

        void PruneDestroyedPlayers()
        {
            for (int index = players.Count - 1; index >= 0; index--)
            {
                if (players[index].Player != null) continue;
                // Unity fake-null means native scene destruction has already
                // removed the player. Release its global camera/output hooks
                // without accessing destroyed native playback properties.
                if (players[index].Output != null) players[index].Output.Dispose();
                players.RemoveAt(index);
            }
        }

        static string HierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var node = transform; node != null; node = node.parent) names.Push(node.gameObject.name);
            return string.Join("/", names.ToArray());
        }
        BoundPlayer Find(VideoPlayer player) { foreach (var bound in players) if (bound.Player == player) return bound; return null; }
        void Prepared(VideoPlayer player)
        {
            var bound = Find(player); if (bound == null || bound.Prepared) return;
            bound.Prepared = true;
            Debug.Log("[Quest startup] original movie prepared " + bound.Context + " dimensions=" + player.width + "x" + player.height
                + " " + SourceFacts(bound));
            if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) SampleState(bound, "native-prepared");
        }
        void Started(VideoPlayer player)
        {
            var bound = Find(player); if (bound == null || bound.Started) return;
            bound.Started = true; Debug.Log("[Quest startup] original movie started " + bound.Context + " " + SourceFacts(bound));
            if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) SampleState(bound, "native-started");
        }
        void Error(VideoPlayer player, string error)
        {
            var bound = Find(player); if (bound == null || bound.Errors++ >= 2) return;
            string fileState;
            try { fileState = bound.Path == null ? "source=authored-dynamic"
                : File.Exists(bound.Path) ? "exists=true bytes=" + new FileInfo(bound.Path).Length : "exists=false"; }
            catch (IOException) { fileState = "stat=io-failed"; }
            catch (UnauthorizedAccessException) { fileState = "stat=access-denied"; }
            Debug.LogError("[Quest startup] original movie failed " + bound.Context + " " + fileState
                + (bound.Path == null ? "" : " expectedBytes=" + bound.ExpectedSize) + " " + SourceFacts(bound) + " detail=" + error);
            if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging) SampleState(bound, "native-open-failed");
        }

        static string SourceFacts(BoundPlayer bound)
        {
            // Native owners may replace the URL after binding. Report the media
            // actually opened, rather than retaining a stale trailer attribution.
            string url = bound.Player.url ?? string.Empty;
            string absolute = url;
            string kind = "raw-path";
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || !uri.IsFile)
                    return "actualUrlKind=invalid-file-uri actualUrl=" + url;
                absolute = uri.LocalPath; kind = "file-uri";
            }
            else if (!System.IO.Path.IsPathRooted(url))
                return "actualUrlKind=other actualUrl=" + url;
            string state;
            try { state = File.Exists(absolute) ? "actualExists=true actualBytes=" + new FileInfo(absolute).Length : "actualExists=false"; }
            catch (IOException) { state = "actualStat=io-failed"; }
            catch (UnauthorizedAccessException) { state = "actualStat=access-denied"; }
            return "actualUrlKind=" + kind + " matchesBoundSource=" + string.Equals(absolute, bound.Path, StringComparison.Ordinal)
                + " " + state + " actualUrl=" + url;
        }

        static string TextureFacts(Texture texture)
        {
            return texture != null ? texture.name + " " + texture.width + "x" + texture.height : "none";
        }
        static void SampleState(BoundPlayer bound, string reason)
        {
            var player = bound.Player;
            var camera = player.targetCamera;
            var target = player.targetTexture;
            Debug.Log("[Quest startup] original movie state " + bound.Context + " reason=" + reason
                + " active=" + player.isActiveAndEnabled + " hierarchy=" + player.gameObject.activeInHierarchy
                + " prepared=" + player.isPrepared + " playing=" + player.isPlaying + " paused=" + player.isPaused
                + " frame=" + player.frame + " time=" + player.time + " speed=" + player.playbackSpeed
                + " renderMode=" + player.renderMode + " decodedTexture=" + TextureFacts(player.texture)
                + " targetTexture=" + TextureFacts(target) + " targetCreated=" + (target != null && target.IsCreated())
                + " targetCamera=" + (camera != null ? camera.name + " active=" + camera.isActiveAndEnabled
                    + " mask=" + camera.cullingMask + " target=" + TextureFacts(camera.targetTexture) : "none")
                + " cameraAlpha=" + player.targetCameraAlpha + " audio=" + player.audioOutputMode
                + " tracks=" + player.controlledAudioTrackCount + " " + SourceFacts(bound));
        }
        public void Observe()
        {
            if (disposed) return;
            PruneDestroyedPlayers();
            foreach (var bound in players)
            {
                var player = bound.Player;
                if (player == null) continue;
                if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging)
                {
                    // Cheap gates precede native target sampling, file stats and
                    // formatting. Per player: three timed snapshots and at most
                    // four original-owner URL changes; no frame-driven stream.
                    float age = Time.realtimeSinceStartup - bound.BoundAt;
                    float next = bound.StateSamples == 0 ? 2f : bound.StateSamples == 1 ? 10f : 30f;
                    if (bound.StateSamples < 3 && age >= next)
                    { bound.StateSamples++; SampleState(bound, "observed-after-native-start"); }
                    string url = player.url;
                    if (bound.SourceChanges < 4 && url != bound.LastObservedUrl)
                    {
                        bound.LastObservedUrl = url; bound.SourceChanges++;
                        SampleState(bound, "native-source-changed");
                    }
                }
                if (bound.Frame || player.frame < 0 || player.texture == null) continue;
                bound.Frame = true;
                Debug.Log("[Quest startup] original movie decoded frame " + bound.Context + " frame=" + player.frame + " " + SourceFacts(bound));
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var bound in players)
            {
                if (bound.Output != null) bound.Output.Dispose();
                if (bound.Player != null)
                { bound.Player.prepareCompleted -= Prepared; bound.Player.started -= Started; bound.Player.errorReceived -= Error; }
            }
            players.Clear(); paths.Clear();
        }
    }
}
#endif

#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
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
    }
    [Serializable] public sealed class QuestGameMovie
    {
        public string guid = null, name = null, path = null, sha256 = null;
        public long size = 0;
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
            public bool Prepared, Started, Frame;
            public int Errors;
        }
        readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<BoundPlayer> players = new List<BoundPlayer>();
        QuestGameMovieManifest manifest;
        bool disposed;

        public void Install(QuestGameMovieManifest movies, QuestGameContentManifest content, string root, string inputKey)
        {
            if (manifest != null || disposed) throw new InvalidOperationException("Movie delivery was already initialized or disposed.");
            QuestGameContent.Validate(content, inputKey);
            if (movies == null || movies.schema != 1 || movies.scope != "original-startup-menu-movies"
                || movies.clips == null || movies.clips.Length == 0 || movies.clips.Length > 32)
                throw new InvalidDataException("Original startup movie manifest is missing or invalid.");
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clip in movies.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.guid) || string.IsNullOrEmpty(clip.name)
                    || clip.path == null || !clip.path.StartsWith("StreamingAssets/QuestOriginalMovies/", StringComparison.Ordinal)
                    || clip.size <= 0 || clip.bindings == null || clip.bindings.Length == 0 || clip.bindings.Length > 32)
                    throw new InvalidDataException("Original startup movie entry is invalid.");
                // Content was hashed on the delivery worker. Require the exact same
                // manifested bytes, without another main-thread movie hash. Linux
                // cannot import these MP4s as VideoClips; Android reads original files.
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
            manifest = movies;
            Debug.Log("[Quest startup] verified original movie sources installed clips=" + movies.clips.Length);
        }

        public void BindScene(Scene scene)
        {
            if (manifest == null || disposed) throw new InvalidOperationException("Original movie delivery is unavailable.");
            foreach (var clip in manifest.clips)
            foreach (var binding in clip.bindings)
            {
                if (binding.scene != scene.name) continue;
                VideoPlayer selected = null;
                foreach (var root in scene.GetRootGameObjects())
                foreach (var player in root.GetComponentsInChildren<VideoPlayer>(true))
                {
                    if (player.gameObject.name != binding.playerName || HierarchyPath(player.transform) != binding.playerPath) continue;
                    if (selected != null) throw new InvalidDataException("Ambiguous original movie player: " + binding.playerPath);
                    selected = player;
                }
                if (selected == null) throw new InvalidDataException("Required original movie player is missing: " + scene.name + "/" + binding.playerPath);
                bool known = false;
                foreach (var bound in players) if (bound.Player == selected) { known = true; break; }
                if (known) continue;
                // sceneLoaded precedes Start. Keep the native timed intro, Play call,
                // rendering target, audio routing and completion callbacks intact.
                selected.clip = null;
                selected.source = VideoSource.Url;
                selected.url = new Uri(paths[clip.guid]).AbsoluteUri;
                var record = new BoundPlayer { Player = selected, Context = scene.name + "/" + binding.playerPath + " source=" + clip.name };
                players.Add(record);
                selected.prepareCompleted += Prepared;
                selected.started += Started;
                selected.errorReceived += Error;
                Debug.Log("[Quest startup] original movie URL bound " + record.Context);
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
            Debug.Log("[Quest startup] original movie prepared " + bound.Context + " dimensions=" + player.width + "x" + player.height);
        }
        void Started(VideoPlayer player)
        {
            var bound = Find(player); if (bound == null || bound.Started) return;
            bound.Started = true; Debug.Log("[Quest startup] original movie started " + bound.Context);
        }
        void Error(VideoPlayer player, string error)
        {
            var bound = Find(player); if (bound == null || bound.Errors++ >= 2) return;
            Debug.LogError("[Quest startup] original movie failed " + bound.Context + " detail=" + error);
        }
        public void Observe()
        {
            if (disposed) return;
            foreach (var bound in players)
            {
                var player = bound.Player;
                if (player == null || bound.Frame || player.frame < 0 || player.texture == null) continue;
                bound.Frame = true;
                Debug.Log("[Quest startup] original movie decoded frame " + bound.Context + " frame=" + player.frame);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var bound in players)
                if (bound.Player != null)
                { bound.Player.prepareCompleted -= Prepared; bound.Player.started -= Started; bound.Player.errorReceived -= Error; }
            players.Clear(); paths.Clear();
        }
    }
}
#endif

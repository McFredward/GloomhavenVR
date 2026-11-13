#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>
    /// Host-only scheduling for the witnessed Unity 2021.3.5 Bee driver. Unity's
    /// job-worker-count controls JobQueue; IL2CPP --jobs controls conversion.
    /// Neither limits the native C++ backend. Its actual --threads option is
    /// forwarded through a private native apphost without editing installed tools.
    /// This generated project uses the adapter only for the builder entry point.
    /// </summary>
    public static class QuestBuildConcurrency
    {
        public static void Build()
        {
            using (Begin()) QuestBuild.Build();
        }

        internal static IDisposable Begin()
        {
            if (Application.unityVersion != "2021.3.5f1")
                throw new InvalidOperationException("Quest native scheduling requires Unity 2021.3.5f1.");
            string launcher = Environment.GetEnvironmentVariable("GHVRQ_BEE_LAUNCHER");
            string backend = Environment.GetEnvironmentVariable("GHVRQ_BEE_REAL_PATH");
            string value = Environment.GetEnvironmentVariable("GHVRQ_BEE_THREADS");
            int jobs;
            if (string.IsNullOrEmpty(launcher) || !Path.IsPathRooted(launcher) || !File.Exists(launcher)
                || string.IsNullOrEmpty(backend) || !Path.IsPathRooted(backend) || !File.Exists(backend)
                || !int.TryParse(value, out jobs) || jobs < 1 || jobs > 1024)
                throw new InvalidOperationException("Quest native scheduling is missing its verified host launcher/policy.");
            const string driverName = "UnityEditor.Scripting.ScriptCompilation.UnityBeeDriver";
            Type driver = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(driverName, false)).FirstOrDefault(t => t != null);
            FieldInfo field = driver == null ? null : driver.GetField("BeeBackendExecutable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(string) || !field.IsInitOnly)
                throw new InvalidOperationException("Unity Bee backend routing differs from the witnessed 2021.3.5 contract.");
            string original = field.GetValue(null) as string;
            string expected = Path.GetFullPath(Path.Combine(EditorApplication.applicationContentsPath,
                "bee_backend" + (Application.platform == RuntimePlatform.WindowsEditor ? ".exe" : "")));
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.IsNullOrEmpty(original) || !string.Equals(Path.GetFullPath(original), expected, comparison)
                || !string.Equals(Path.GetFullPath(backend), expected, comparison))
                throw new InvalidOperationException("Quest scheduling refuses an unexpected installed Bee backend path.");
            string savedHash = SessionState.GetString("BeeBackendHash", "");
            string originalHash;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(backend))
                originalHash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (!string.IsNullOrEmpty(savedHash) && !string.Equals(savedHash, originalHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Unity Bee backend changed after its cache identity was captured.");
            try
            {
                // Scheduling does not change backend bytes or DAG semantics. Keep
                // the real backend identity even if its first lookup is in Scope.
                SessionState.SetString("BeeBackendHash", originalHash);
                field.SetValue(null, launcher);
                if (!string.Equals(field.GetValue(null) as string, launcher, StringComparison.Ordinal))
                    throw new InvalidOperationException("Unity did not accept the private Bee launcher.");
            }
            catch (Exception error)
            {
                field.SetValue(null, original);
                SessionState.SetString("BeeBackendHash", savedHash);
                throw new InvalidOperationException("Cannot apply bounded native build scheduling in this Editor.", error);
            }
            Debug.Log("[QuestBuilder] Native Bee --threads=" + jobs + "; installed Editor remains unchanged.");
            return new Restore(field, original, savedHash);
        }

        private sealed class Restore : IDisposable
        {
            private readonly FieldInfo field;
            private readonly string original;
            private bool disposed;
            private readonly string savedHash;
            internal Restore(FieldInfo field, string original, string savedHash) { this.field = field; this.original = original; this.savedHash = savedHash; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                field.SetValue(null, original);
                SessionState.SetString("BeeBackendHash", savedHash);
                if (!string.Equals(field.GetValue(null) as string, original, StringComparison.Ordinal))
                    throw new InvalidOperationException("Cannot restore the original Unity Bee backend routing.");
            }
        }
    }
}
#endif

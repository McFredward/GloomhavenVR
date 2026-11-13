#nullable disable
#if GHVR_QUEST_STARTUP && GHVR_QUEST_GAME
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Configures the original engine's byte-preserving ARM64 process bridge.</summary>
    public static class QuestGameProcedural
    {
        [DllImport("QuestApparance", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        static extern int quest_apparance_configure(string nativeLibraryDirectory, string payloadDirectory, string writableRuntimeDirectory);
        public static bool Configured { get; private set; }
        public static string Failure { get; private set; }
        public static string Backend { get; private set; }
        [Serializable] sealed class BackendContract { public int schema = 0; public string backend = null; }

        public static void Configure(string ownedContentRoot)
        {
            if (Configured) throw new InvalidOperationException("The native procedural bridge was configured twice.");
            if (Application.platform != RuntimePlatform.Android || Application.isEditor)
                throw new InvalidOperationException("Campaign procedural bridge requires the actual Android player.");
            string payload = Path.Combine(ownedContentRoot, "StreamingAssets/ProceduralRuntime");
            if (!Directory.Exists(payload)) throw new DirectoryNotFoundException("The manifested original procedural runtime payload is absent.");
            try
            {
                var resource = Resources.Load<TextAsset>("quest-procedural-native");
                if (resource == null) throw new InvalidDataException("The signed procedural backend contract is unavailable.");
                var contract = JsonUtility.FromJson<BackendContract>(resource.text);
                if (contract == null || contract.schema != 1
                    || (contract.backend != "proton-arm64ec-fex" && contract.backend != "box64-wine9"))
                    throw new InvalidDataException("The procedural backend identity is invalid.");
                Backend = contract.backend;
                string native, writable;
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    using (var info = activity.Call<AndroidJavaObject>("getApplicationInfo"))
                        native = info.Get<string>("nativeLibraryDir");
                    using (var files = activity.Call<AndroidJavaObject>("getFilesDir"))
                        writable = Path.Combine(files.Call<string>("getAbsolutePath"), "quest-procedural-state");
                }
                if (string.IsNullOrWhiteSpace(native) || !Directory.Exists(native))
                    throw new DirectoryNotFoundException("Android's installed native executable directory is unavailable.");
                // Wine's private prefix requires ordinary Linux symlinks, which
                // Android's emulated external save/content filesystem rejects.
                // Keep process state in Context.filesDir; saves remain original.
                Directory.CreateDirectory(writable);
                // Android target30 permits packaged native executables here.
                // The copied original DLL and graphs remain data payloads.
                int result = quest_apparance_configure(native, payload, writable);
                if (result != 0) throw new InvalidOperationException("Native procedural bridge configuration failed: " + result);
                Configured = true;
                UnityEngine.Debug.Log("[Quest procedural] Original engine process paths configured; backend=" + Backend
                    + "; native generation readiness is established by the original engine, not this configuration step.");
            }
            catch (Exception failure)
            {
                Failure = failure.GetType().Name + ": " + failure.Message;
                throw;
            }
        }
    }
}
#endif

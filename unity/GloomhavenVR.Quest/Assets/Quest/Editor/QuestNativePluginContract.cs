#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Import the exact staged backend programs without sweeping old native binaries.</summary>
    public static class QuestNativePluginContract
    {
        public const string ResourcePath = "Assets/Quest/Resources/quest-procedural-native.json";
        public const string Prefix = "Assets/Quest/Plugins/Android/arm64-v8a/";
        [Serializable] public sealed class NativeFile { public string path, sha256; public long size; }
        [Serializable] public sealed class Contract { public int schema; public string backend; public NativeFile[] files; }

        public static Contract Configure(string expectedBackend)
        {
            if (!File.Exists(ResourcePath) || new FileInfo(ResourcePath).Length > 65536)
                throw new InvalidDataException("The procedural native program contract is missing or oversized.");
            var contract = UnityEngine.JsonUtility.FromJson<Contract>(File.ReadAllText(ResourcePath));
            if (contract == null || contract.schema != 1 || contract.backend != expectedBackend
                || (contract.backend != "proton-arm64ec-fex" && contract.backend != "box64-wine9")
                || contract.files == null || contract.files.Length < 3 || contract.files.Length > 64)
                throw new InvalidDataException("The native program inventory differs from the selected procedural backend.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (NativeFile file in contract.files)
            {
                if (file == null || file.path == null || !file.path.StartsWith(Prefix, StringComparison.Ordinal)
                    || !Regex.IsMatch(file.path.Substring(Prefix.Length), "^lib[A-Za-z0-9_]+\\.so$")
                    || !Regex.IsMatch(file.sha256 ?? "", "^[0-9a-f]{64}$") || file.size < 64
                    || !names.Add(file.path.Substring(Prefix.Length)))
                    throw new InvalidDataException("The procedural native program record is invalid or duplicated.");
                if (!File.Exists(file.path) || new FileInfo(file.path).Length != file.size || Hash(file.path) != file.sha256)
                    throw new InvalidDataException("The staged native program changed: " + file.path);
                using (var input = File.OpenRead(file.path))
                {
                    byte[] header = new byte[64];
                    if (input.Read(header, 0, header.Length) != 64 || header[0] != 127 || header[1] != 69
                        || header[2] != 76 || header[3] != 70 || header[4] != 2 || header[5] != 1 || header[6] != 1
                        || BitConverter.ToUInt16(header, 16) != 3 || BitConverter.ToUInt16(header, 18) != 183
                        || BitConverter.ToUInt32(header, 20) != 1)
                        throw new InvalidDataException("The native program is not an Android ARM64 shared/PIE ELF: " + file.path);
                }
            }
            if (!names.Contains("libQuestApparance.so") || !names.Contains("libopus_egpv.so")
                || (contract.backend == "box64-wine9" && (!names.Contains("libquest_box64.so") || !names.Contains("libquest_wineserver.so")))
                || (contract.backend == "proton-arm64ec-fex" && (names.Contains("libquest_box64.so") || names.Contains("libquest_wineserver.so"))))
                throw new InvalidDataException("The native program set does not preserve this backend's original game/voice ABI.");
            if (contract.backend == "proton-arm64ec-fex")
                foreach (string name in new[] { "libquest_proton.so", "libquest_proton_server.so", "libqn.so", "libqw.so", "libqs.so" })
                    if (!names.Contains(name)) throw new InvalidDataException("The Proton native loader/server/Unix module is missing: " + name);
            foreach (NativeFile file in contract.files)
            {
                var importer = AssetImporter.GetAtPath(file.path) as PluginImporter;
                if (importer == null) throw new InvalidOperationException("Required ARM64 native program importer is missing: " + file.path);
                importer.SetCompatibleWithAnyPlatform(false);
                importer.SetCompatibleWithEditor(false);
                importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
                importer.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                // ELF executables and Wine helpers belong to nativeLibraryDir.
                // They are launched explicitly and must never be preloaded by Unity.
                importer.isPreloaded = false;
                importer.SaveAndReimport();
            }
            return contract;
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif

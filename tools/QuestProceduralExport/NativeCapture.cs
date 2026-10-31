using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using UnityEngine;

namespace GloomhavenVR.Procedural
{
    /// <summary>Copies actual original native ABI traffic in the private conversion process.</summary>
    public static class NativeCapture
    {
        [Serializable] sealed class Record
        {
            public int schema = 1, index, entity, result, size, assetId, boundsAvailable, variantCount;
            public uint procedure;
            public string operation, descriptor, buffer, sha256;
            public bool dynamicDetail;
            public float seconds;
            public float[] frame, view;
        }
        static readonly object sync = new object();
        static string root;
        static StreamWriter writer;
        static int index;
        static long totalBytes;
        const long MaximumBytes = 512L * 1024 * 1024;
        const int MaximumRecords = 200000;

        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern int ApparanceCreateEntity(int old_handle);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern void ApparanceDestroyEntity(int entity_id);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern void ApparanceEntityBuild(int entity_handle, uint procedure_id, int data_size, IntPtr data_bytes, bool dynamic_detail);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern int ApparancePopEntityTask(out int data_size, out IntPtr data_bytes);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern int ApparancePopEngineTask(out int data_size, out IntPtr data_bytes);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern void ApparanceUpdateAsset(int entity_context, string asset_descriptor, int asset_id, int bounds_available, float[] asset_frame, int variant_count);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern string ApparanceGetNextAssetRequest(out int entity_context, out int asset_id);
        [DllImport("ApparanceEngine", CharSet = CharSet.Ansi)] static extern void ApparanceUpdate(float dt, Apparance.Net.Vector3 view_position);

        internal static void Configure(string output)
        {
            lock (sync)
            {
                if (writer != null) throw new InvalidOperationException("Native capture was initialized twice.");
                root = output;
                Directory.CreateDirectory(root);
                // A fresh configuration owns this run. Old payload files are
                // never referenced by the current overwritten ordered journal.
                writer = new StreamWriter(Path.Combine(root, "calls.jsonl"), false) { AutoFlush = true };
                File.WriteAllText(Path.Combine(root, "boundary.txt"), "Original Windows engine calls; no ARM64 or headset execution proof. Empty task polls are omitted.\n");
            }
        }

        static void Write(Record record, IntPtr data = default)
        {
            lock (sync)
            {
                if (writer == null) throw new InvalidOperationException("Native capture must precede original engine startup.");
                if (++index > MaximumRecords || record.size < 0 || record.size > MaximumBytes - totalBytes)
                    throw new InvalidDataException("Native capture exceeded its bounded private output budget.");
                record.index = index;
                if (record.size != 0)
                {
                    if (data == IntPtr.Zero) throw new InvalidDataException("Original native payload pointer is absent.");
                    byte[] bytes = new byte[record.size];
                    Marshal.Copy(data, bytes, 0, bytes.Length);
                    record.buffer = index.ToString("D8") + ".bin";
                    using (SHA256 hash = SHA256.Create()) record.sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                    File.WriteAllBytes(Path.Combine(root, record.buffer), bytes);
                    totalBytes += bytes.Length;
                }
                writer.WriteLine(JsonUtility.ToJson(record));
            }
        }

        public static int CreateEntity(int oldHandle)
        {
            int result = ApparanceCreateEntity(oldHandle);
            Write(new Record { operation = "create", entity = oldHandle, result = result });
            return result;
        }
        public static void DestroyEntity(int entity)
        {
            Write(new Record { operation = "destroy", entity = entity }); ApparanceDestroyEntity(entity);
        }
        public static void Build(int entity, uint procedure, int size, IntPtr bytes, bool dynamicDetail)
        {
            Write(new Record { operation = "build", entity = entity, procedure = procedure, size = size, dynamicDetail = dynamicDetail }, bytes);
            ApparanceEntityBuild(entity, procedure, size, bytes, dynamicDetail);
        }
        public static int PopEntityTask(out int size, out IntPtr bytes)
        {
            int result = ApparancePopEntityTask(out size, out bytes);
            if (result != 0 || size != 0) Write(new Record { operation = "entity-task", result = result, size = size }, bytes);
            return result;
        }
        public static int PopEngineTask(out int size, out IntPtr bytes)
        {
            int result = ApparancePopEngineTask(out size, out bytes);
            if (result != 0 || size != 0) Write(new Record { operation = "engine-task", result = result, size = size }, bytes);
            return result;
        }
        public static void UpdateAsset(int context, string descriptor, int assetId, int boundsAvailable, float[] frame, int variants)
        {
            Write(new Record { operation = "asset-response", entity = context, descriptor = descriptor, assetId = assetId,
                boundsAvailable = boundsAvailable, frame = frame, variantCount = variants });
            ApparanceUpdateAsset(context, descriptor, assetId, boundsAvailable, frame, variants);
        }
        public static string GetNextAssetRequest(out int context, out int assetId)
        {
            string result = ApparanceGetNextAssetRequest(out context, out assetId);
            if (!string.IsNullOrEmpty(result)) Write(new Record { operation = "asset-request", entity = context, assetId = assetId, descriptor = result });
            return result;
        }
        public static void Update(float seconds, Apparance.Net.Vector3 view)
        {
            Write(new Record { operation = "update", seconds = seconds, view = new[] { view.X, view.Y, view.Z } });
            ApparanceUpdate(seconds, view);
        }
        internal static void Flush() { lock (sync) writer?.Flush(); }
    }
}

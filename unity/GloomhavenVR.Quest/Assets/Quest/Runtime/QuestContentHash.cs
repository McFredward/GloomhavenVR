#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GloomhavenVR.Quest
{
    // Configure once on Unity's main thread. Workers never consult Unity platform
    // APIs. Android must use the independently optimized native implementation;
    // an unavailable ABI is a startup failure, not a silently slow fallback.
    internal static class QuestContentHash
    {
        internal const int BufferSize = 262144;
        static volatile bool native = false;
        static readonly object configuration = new object();

        internal static void Configure()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (configuration)
            {
                if (native) return;
                try
                {
                    if (Native.Abi() != 1) throw new InvalidDataException("Quest content hash ABI is incompatible.");
                    using (var sha = new Digest(true))
                    {
                        sha.Update(new byte[] { 97, 98, 99 }, 3);
                        if (sha.Finish() != "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
                            throw new InvalidDataException("Quest native content hash self-test failed.");
                    }
                    native = true;
                }
                catch (Exception error)
                {
                    throw new InvalidDataException("Quest native content verification is unavailable; startup is stopped.", error);
                }
            }
#endif
        }

        internal static Digest Create()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!native) throw new InvalidDataException("Quest native content verification was not configured before its worker.");
#endif
            return new Digest(native);
        }

        internal sealed class Digest : IDisposable
        {
            IntPtr context;
            SHA256 managed;
            bool finished, disposed;
            internal Digest(bool useNative)
            {
                if (useNative)
                {
                    context = Native.Create();
                    if (context == IntPtr.Zero) throw new OutOfMemoryException("Quest native SHA-256 context allocation failed.");
                }
                else managed = SHA256.Create();
            }
            internal void Update(byte[] buffer, int count)
            {
                if (disposed || finished) throw new InvalidOperationException("Content digest is no longer writable.");
                if (buffer == null || count < 0 || count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
                if (context != IntPtr.Zero)
                {
                    if (Native.Update(context, buffer, count) != 1) throw new InvalidDataException("Quest native SHA-256 update failed.");
                }
                else managed.TransformBlock(buffer, 0, count, buffer, 0);
            }
            internal string Finish()
            {
                if (disposed || finished) throw new InvalidOperationException("Content digest is already finalized.");
                finished = true;
                byte[] result;
                if (context != IntPtr.Zero)
                {
                    result = new byte[32];
                    if (Native.Final(context, result) != 1) throw new InvalidDataException("Quest native SHA-256 finalization failed.");
                }
                else { managed.TransformFinalBlock(Array.Empty<byte>(), 0, 0); result = managed.Hash; }
                return BitConverter.ToString(result).Replace("-", "").ToLowerInvariant();
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (context != IntPtr.Zero) { Native.Destroy(context); context = IntPtr.Zero; }
                if (managed != null) { managed.Dispose(); managed = null; }
            }
        }

        // These identities have authority only inside one delivery operation,
        // after a full byte hash. They are never serialized or trusted on a later
        // launch. Android's inode/ctime catches replacement and same-size writes,
        // including writes which restore mtime. Sandbox ownership is still assumed.
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdentity : IEquatable<FileIdentity>
        {
            public ulong Device, Inode;
            public long Size, ModifiedSeconds, ModifiedNanoseconds, ChangedSeconds, ChangedNanoseconds;
            public bool Equals(FileIdentity other) => Device == other.Device && Inode == other.Inode && Size == other.Size
                && ModifiedSeconds == other.ModifiedSeconds && ModifiedNanoseconds == other.ModifiedNanoseconds
                && ChangedSeconds == other.ChangedSeconds && ChangedNanoseconds == other.ChangedNanoseconds;
        }
        internal static FileIdentity Identity(string path)
        {
            if (native)
            {
                FileIdentity identity;
                if (Native.Identity(path, out identity) != 1) throw new InvalidDataException("Content identity is not a regular local file: " + path);
                return identity;
            }
            var file = new FileInfo(path);
            if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Content identity is not a regular local file: " + path);
            return new FileIdentity { Size = file.Length, ModifiedSeconds = file.LastWriteTimeUtc.Ticks, ChangedSeconds = file.CreationTimeUtc.Ticks };
        }

        static class Native
        {
            const string Library = "ghvr_quest_passthrough";
            [DllImport(Library, EntryPoint = "ghvr_content_hash_abi", CallingConvention = CallingConvention.Cdecl)] internal static extern int Abi();
            [DllImport(Library, EntryPoint = "ghvr_content_hash_create", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr Create();
            [DllImport(Library, EntryPoint = "ghvr_content_hash_update", CallingConvention = CallingConvention.Cdecl)] internal static extern int Update(IntPtr context, [In] byte[] bytes, int count);
            [DllImport(Library, EntryPoint = "ghvr_content_hash_final", CallingConvention = CallingConvention.Cdecl)] internal static extern int Final(IntPtr context, [Out] byte[] digest);
            [DllImport(Library, EntryPoint = "ghvr_content_hash_destroy", CallingConvention = CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr context);
            [DllImport(Library, EntryPoint = "ghvr_content_file_identity", CallingConvention = CallingConvention.Cdecl)] internal static extern int Identity([MarshalAs(UnmanagedType.LPStr)] string path, out FileIdentity identity);
        }
    }
}
#endif

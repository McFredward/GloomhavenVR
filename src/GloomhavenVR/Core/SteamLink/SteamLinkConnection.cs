using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GloomhavenVR.Core.SteamLink;

// Public OpenVR reads run in a short, isolated Background client, never in the
// Unity/OpenXR process. No receiver handshake, camera access, settings write or
// transport/network probe is implied by this hardware/driver identity.
internal static class SteamLinkConnection
{
    internal enum Reason
    {
        NoSession, Pending, UnsupportedPlatform, RuntimePath, HelperUnavailable,
        NativeUnavailable, WrongHeadset, WrongTransport, Changed, Ready
    }

    internal sealed class Snapshot
    {
        internal Snapshot(Reason reason, string tracking = "", string model = "", string registered = "", string detail = "")
        { State = reason; TrackingSystem = tracking; Model = model; RegisteredDeviceType = registered; Detail = detail; }
        internal Reason State { get; }
        internal bool Available => State == Reason.Ready;
        internal string TrackingSystem { get; }
        internal string Model { get; }
        internal string RegisteredDeviceType { get; }
        internal string Detail { get; }
        internal bool ReceiverVerified => false;
    }

    private static readonly Monitor State = new(token => Task.Run(() => ReadWindows(token), token));
    internal static bool Available => State.Current.Available; // Cached only: safe for UI/source gates.
    internal static Snapshot Current => State.Current;
    private static int _warnedReasons;

    // Caller must prove an ACTIVE running SteamVR OpenXR session, WindowsPlayer,
    // and !FrameNativePassthrough.Required. An installed/loaded DLL is not proof.
    internal static void Tick(bool eligibleRunningSteamVrSession, float nowSeconds)
    {
        Snapshot before = State.Current;
        State.Tick(eligibleRunningSteamVrSession, nowSeconds);
        Snapshot after = State.Current;
        if (!eligibleRunningSteamVrSession)
        {
            _warnedReasons = 0;
        }
        LogTransition(before, after);
    }

    internal static void LogTransition(Snapshot before, Snapshot after)
    {
        if (!before.Available && after.Available)
            VRLog.Info("Core", "Steam Frame connected through Steam Link; streaming mixed reality is available. Receiver installation is unverified.");
        int warning = after.State == Reason.RuntimePath ? 1 : after.State == Reason.HelperUnavailable ? 2
            : after.State == Reason.NativeUnavailable && after.Detail.Length != 0 ? 4 : 0;
        if (warning != 0 && (_warnedReasons & warning) == 0)
        {
            _warnedReasons |= warning;
            string message = after.State == Reason.RuntimePath
                ? "Steam Link connection could not be checked: the SteamVR runtime registry or API path is unavailable or invalid."
                : after.State == Reason.HelperUnavailable
                    ? "Steam Link connection could not be checked: its embedded identity reader is missing, changed or unavailable."
                    : "Steam Link identity reader could not query the connected headset.";
            VRLog.Warn("Core", message + (after.Detail.Length == 0 ? "" : " Detail: " + after.Detail));
        }
        if ((before.State != after.State || before.TrackingSystem != after.TrackingSystem
            || before.Model != after.Model || before.RegisteredDeviceType != after.RegisteredDeviceType
            || before.Detail != after.Detail) && VRLog.WantsDebug)
            VRLog.Debug("Core", "Steam Link connection identity: " + after.State
                + "; tracking=" + after.TrackingSystem + "; model=" + after.Model
                + "; registered=" + after.RegisteredDeviceType + "; detail=" + after.Detail + "; receiver=unverified.");
    }

    // Main-thread InputDevices.deviceConnected/disconnected/deviceConfigChanged.
    internal static void Invalidate() => State.Invalidate();
    internal static void Reset() { State.Reset(); _warnedReasons = 0; }

    internal sealed class Monitor
    {
        internal const int MaximumAttempts = 3;
        private readonly Func<CancellationToken, Task<Snapshot>> _reader;
        private Task<Snapshot>? _pending;
        private CancellationTokenSource? _cancel;
        private uint _epoch, _pendingEpoch;
        private bool _eligible;
        private int _attempts;
        private double _next;
        internal Snapshot Current { get; private set; } = new(Reason.NoSession);
        internal Monitor(Func<CancellationToken, Task<Snapshot>> reader) { _reader = reader; }
        internal void Reset()
        {
            _epoch++; _eligible = false; _attempts = 0; _next = 0;
            _cancel?.Cancel(); // Kills only our child; never waits on the Unity thread.
            if (Current.State != Reason.NoSession) Current = new Snapshot(Reason.NoSession);
        }
        internal void Invalidate()
        {
            _epoch++; _attempts = 0; _cancel?.Cancel();
            if (_eligible) Current = new Snapshot(Reason.Pending);
            // Retain the last rate limit, even during a flood of device events.
        }
        internal void Tick(bool eligible, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0) eligible = false;
            if (!eligible)
            {
                if (_eligible) Reset();
                Reap(false);
                return;
            }
            if (!_eligible)
            {
                _eligible = true; _epoch++; _attempts = 0; _next = now;
                Current = new Snapshot(Reason.Pending);
            }
            Reap(true);
            if (_pending != null || Current.Available || _attempts >= MaximumAttempts || now < _next) return;
            _attempts++; _next = now + 2; _pendingEpoch = _epoch;
            _cancel = new CancellationTokenSource();
            try { _pending = _reader(_cancel.Token); }
            catch (Exception) { _pending = Task.FromResult(new Snapshot(Reason.NativeUnavailable)); }
        }
        private void Reap(bool publish)
        {
            if (_pending == null || !_pending.IsCompleted) return;
            Snapshot value;
            try { value = _pending.GetAwaiter().GetResult(); }
            catch (Exception) { value = new Snapshot(Reason.NativeUnavailable); }
            _pending = null; _cancel?.Dispose(); _cancel = null;
            if (publish && _pendingEpoch == _epoch) Current = value;
        }
    }

    // The actual connected HMD0/class and stable strings originate in the public
    // versioned OpenVR API, not Unity's generic HMD name or DLL presence. Strings
    // identify ordinary cooperative drivers; they are not device attestation.
    internal static Snapshot Classify(bool connected, int deviceClass, string tracking, string model, string registered)
    {
        if (!connected || deviceClass != 1) return new Snapshot(Reason.NativeUnavailable);
        if (!Text(tracking) || !Text(model) || !Text(registered)) return new Snapshot(Reason.NativeUnavailable);
        if (!string.Equals(model, "Steam Frame", StringComparison.OrdinalIgnoreCase))
            return new Snapshot(Reason.WrongHeadset, tracking, model, registered);
        bool link = string.Equals(tracking, "vrlink", StringComparison.OrdinalIgnoreCase)
            || string.Equals(registered, "vrlink", StringComparison.OrdinalIgnoreCase)
            || registered.StartsWith("vrlink/", StringComparison.OrdinalIgnoreCase);
        return new Snapshot(link ? Reason.Ready : Reason.WrongTransport, tracking, model, registered);
    }
    private static bool Text(string value) => value != null && value.Length <= 96 && value.All(c => c >= 32 && c <= 126);

    [Serializable] private sealed class MetadataJson
    {
        public string schema = "", error = "", tracking_system = "", model = "", registered_type = "";
        public bool connected = false;
        public int device_class = 0;
    }

    internal static Snapshot Decode(string raw, int exitCode)
    {
        if (raw == null || raw.Length > MaximumOutput) return new Snapshot(Reason.NativeUnavailable);
        try
        {
            var keys = Regex.Matches(raw, "\"([^\"\\\\]+)\"\\s*:").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            string[] wanted = exitCode == 0
                ? new[] { "schema", "connected", "device_class", "tracking_system", "model", "registered_type" }
                : new[] { "schema", "error" };
            if (keys.Length != wanted.Length || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
                || keys.Except(wanted, StringComparer.Ordinal).Any()) return new Snapshot(Reason.NativeUnavailable);
            MetadataJson data = JsonUtility.FromJson<MetadataJson>(raw);
            if (data == null || data.schema != "ghvr-steamlink-connection-v1") return new Snapshot(Reason.NativeUnavailable);
            if (exitCode != 0)
            {
                // Report finite owned-reader stages, never arbitrary child output.
                string[] errors = { "arguments", "not_running", "library", "exports", "initialization", "interface", "disconnected", "properties", "changed" };
                return new Snapshot(data.error == "changed" ? Reason.Changed : Reason.NativeUnavailable,
                    detail: errors.Contains(data.error, StringComparer.Ordinal) ? "reader:" + data.error : "reader:invalid-error");
            }
            return Classify(data.connected, data.device_class, data.tracking_system, data.model, data.registered_type);
        }
        catch (Exception) { return new Snapshot(Reason.NativeUnavailable); }
    }

    internal const int MaximumOutput = 2048, HelperTimeoutMs = 5000;
    internal const string Resource = "GloomhavenVR.SteamLink.connection-reader.exe";
    internal const int HelperBytes = 42496;
    internal const string HelperSha256 = "cec82e68968441902e717c6bc595ff0ae0b0b03ffe72bc5eb26cbfe19c40a30f";
    internal const string ApiResource = "GloomhavenVR.SteamLink.openvr_api.dll";
    internal const int ApiBytes = 826688;
    internal const string ApiSha256 = "5e591aa3f4a991edcc8910921833edafa67a1b117849bbb4e235df9833bed7a3";

    private static Snapshot ReadWindows(CancellationToken cancellation)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
            return new Snapshot(Reason.UnsupportedPlatform);
        try
        {
            // Build687 hardware stopped at RuntimePath before HMD lookup despite
            // a valid single-entry registry and an existing runtime SDK copy.
            // That whole search was unnecessary: openvr_api.dll is an app SDK
            // loader, not the runtime's bin/vrclient_x64.dll. Ship the pinned
            // public redistributable and let Valve resolve its own registry and
            // overrides. Only our bounded child loads it, never Unity/OpenXR.
            string? library;
            string stage = "SDK preparation";
            try { library = PrepareResource(ApiResource, ApiBytes, ApiSha256, "openvr_api.dll", true, ref stage); }
            catch (Exception ex) { return new Snapshot(Reason.HelperUnavailable, detail: stage + ":" + ex.GetType().Name); }
            if (library == null) return new Snapshot(Reason.HelperUnavailable, detail: stage);
            string? helper;
            try { helper = PrepareResource(Resource, HelperBytes, HelperSha256, "connection-reader.exe", false, ref stage); }
            catch (Exception ex) { return new Snapshot(Reason.HelperUnavailable, detail: stage + ":" + ex.GetType().Name); }
            if (helper == null) return new Snapshot(Reason.HelperUnavailable, detail: stage);
            return RunChild(helper, library, cancellation);
        }
        catch (Exception ex) { return new Snapshot(Reason.NativeUnavailable, detail: "reader process:" + ex.GetType().Name); }
    }

    internal static bool LocalWindowsPath(string path) => path != null && path.Length >= 4 && path.Length < 1024
        && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
        && path[1] == ':' && path[2] == '\\'
        && !path.Skip(2).Any(c => c < 32 || c == '"' || c == '/' || c == ':')
        && !path.Substring(3).Split('\\').Any(part => part == "." || part == "..")
        ;

    private static bool PathSafe(string path)
    {
        // Windows-local only; do not cause UNC/network filesystem access.
        if (!LocalWindowsPath(path)) return false;
        if (!string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)) return false;
        string? current = path;
        while (!string.IsNullOrEmpty(current) && current!.Length > 3)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            current = Path.GetDirectoryName(current);
        }
        return true;
    }

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length <= 0 || file.Length > maximum) throw new InvalidDataException();
        byte[] data = new byte[checked((int)file.Length)];
        int used = 0;
        while (used < data.Length) { int read = file.Read(data, used, data.Length - used); if (read <= 0) throw new EndOfStreamException(); used += read; }
        if (file.ReadByte() != -1) throw new InvalidDataException();
        return data;
    }

    internal static bool Pe64(byte[] data, bool dll)
    {
        if (data == null || data.Length < 512 || data[0] != 'M' || data[1] != 'Z') return false;
        uint offset = BitConverter.ToUInt32(data, 60);
        if (offset < 64 || offset > data.Length - 264) return false;
        int at = checked((int)offset);
        return BitConverter.ToUInt32(data, at) == 0x4550 && BitConverter.ToUInt16(data, at + 4) == 0x8664
            && BitConverter.ToUInt16(data, at + 20) >= 240 && BitConverter.ToUInt16(data, at + 24) == 0x20b
            && ((BitConverter.ToUInt16(data, at + 22) & 0x2000) != 0) == dll;
    }

    internal static bool HelperPin(byte[] raw)
        => ArtifactPin(raw, HelperBytes, HelperSha256, false);

    internal static bool ArtifactPin(byte[] raw, int bytes, string hash, bool dll)
    {
        if (raw.Length != bytes || !Pe64(raw, dll)) return false;
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant() == hash;
    }

    internal static byte[]? ReadResource(string resourceName, int bytes, string hash, bool dll)
    {
        using Stream? resource = typeof(SteamLinkConnection).Assembly.GetManifestResourceStream(resourceName);
        if (resource == null || resource.Length != bytes) return null;
        byte[] raw = new byte[bytes]; int used = 0;
        while (used < raw.Length) { int read = resource.Read(raw, used, raw.Length - used); if (read <= 0) return null; used += read; }
        return ArtifactPin(raw, bytes, hash, dll) ? raw : null;
    }

    // Build688's actual Windows SDK resource has the exact expected name, size
    // and SHA256, but no cache directory was created. Do not rely solely on
    // Unity/Mono's SHGetFolderPath wrapper. Windows supplies LOCALAPPDATA directly;
    // retain the known-folder API only as a fallback. Normalize separators before
    // the local-drive guard, never turn a UNC/relative path into an accepted one.
    internal static string CacheFolder(string? environment, Func<string> knownFolder)
    {
        string folder = NormalizeFolder(environment);
        return folder.Length != 0 ? folder : NormalizeFolder(knownFolder());
    }

    private static string NormalizeFolder(string? value)
    {
        string normalized = (value ?? "").Replace('/', '\\').TrimEnd('\\');
        return LocalWindowsPath(normalized) ? normalized : "";
    }

    private static string? PrepareResource(string resourceName, int bytes, string hash, string fileName, bool dll, ref string stage)
    {
        stage = fileName + ":cache-folder";
        string local = CacheFolder(Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        if (local.Length == 0) return null;
        return ExtractResource(resourceName, bytes, hash, fileName, dll, local, PathSafe, ref stage);
    }

    // Keep the actual filesystem path covered by focused tests. The only
    // substituted boundary is the OS's Windows path/ancestor validator.
    internal static string? ExtractResource(string resourceName, int bytes, string hash, string fileName, bool dll,
        string local, Func<string, bool> pathSafe, ref string stage)
    {
        stage = fileName + ":embedded-resource-or-pin";
        byte[]? raw = ReadResource(resourceName, bytes, hash, dll);
        if (raw == null) return null;
        string directory = Path.Combine(local, "GloomhavenVR", "SteamLink", hash);
        stage = fileName + ":cache-directory-create";
        Directory.CreateDirectory(directory);
        stage = fileName + ":cache-directory-identity";
        if (!pathSafe(directory)) return null;
        string path = Path.Combine(directory, fileName);
        stage = fileName + ":cache-file-create";
        if (!File.Exists(path))
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(raw, 0, raw.Length); file.Flush();
        }
        stage = fileName + ":cache-file-identity";
        if (!pathSafe(path)) return null;
        stage = fileName + ":cache-file-pin";
        return ArtifactPin(ReadBounded(path, bytes), bytes, hash, dll) ? path : null;
    }

    internal static Snapshot RunChild(string helper, string library, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return new Snapshot(Reason.NativeUnavailable);
        var start = new ProcessStartInfo(helper, "\"" + library + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(helper)!
        };
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        start.EnvironmentVariables["PATH"] = system;
        // Preserve OpenVR's own path overrides so this client reaches the same
        // runtime/config as the user's live SteamVR session. Under Proton this
        // includes PROTON_VR_RUNTIME and the Wine prefix/DLL bridge environment:
        // the Windows SDK resolves Proton's C:\vrclient runtime path itself.
        // These inherited values are not an MR
        // permission signal; the connected HMD/driver metadata still decides.
        foreach (string name in new[] { "LD_PRELOAD", "LD_AUDIT" })
            start.EnvironmentVariables.Remove(name);
        using var child = new Process { StartInfo = start };
        if (!child.Start()) return new Snapshot(Reason.NativeUnavailable, detail: "reader:start");
        void Kill() { try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        using CancellationTokenRegistration cancel = cancellation.Register(Kill);
        try
        {
        Task<string?> stdout = BoundedOutput(child.StandardOutput, MaximumOutput, Kill);
        Task<string?> stderr = BoundedOutput(child.StandardError, 512, Kill);
        bool exited = child.WaitForExit(HelperTimeoutMs);
        if (!exited) { Kill(); exited = child.WaitForExit(1000); }
        if (!exited || !Task.WaitAll(new Task[] { stdout, stderr }, 1000))
            return new Snapshot(Reason.NativeUnavailable, detail: "reader:timeout");
        if (cancellation.IsCancellationRequested || stdout.Result == null || stderr.Result == null || !string.IsNullOrWhiteSpace(stderr.Result))
            return new Snapshot(Reason.NativeUnavailable, detail: "reader:cancelled-or-output");
        return Decode(stdout.Result, child.ExitCode);
        }
        finally
        {
            Kill();
            try { child.WaitForExit(1000); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
    private static async Task<string?> BoundedOutput(StreamReader reader, int maximum, Action overflow)
    {
        try
        {
            var text = new StringBuilder(); char[] chunk = new char[256]; int count;
            while ((count = await reader.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
            {
                if (text.Length > maximum - count) { overflow(); return null; }
                text.Append(chunk, 0, count);
            }
            return text.ToString();
        }
        catch (Exception) { overflow(); return null; }
    }
}

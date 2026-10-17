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
        internal Snapshot(Reason reason, string tracking = "", string model = "", string registered = "")
        { State = reason; TrackingSystem = tracking; Model = model; RegisteredDeviceType = registered; }
        internal Reason State { get; }
        internal bool Available => State == Reason.Ready;
        internal string TrackingSystem { get; }
        internal string Model { get; }
        internal string RegisteredDeviceType { get; }
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
        if (!eligibleRunningSteamVrSession) _warnedReasons = 0;
        LogTransition(before, after);
    }

    internal static void LogTransition(Snapshot before, Snapshot after)
    {
        if (!before.Available && after.Available)
            VRLog.Info("Core", "Steam Frame connected through Steam Link; streaming mixed reality is available. Receiver installation is unverified.");
        int warning = after.State == Reason.RuntimePath ? 1 : after.State == Reason.HelperUnavailable ? 2 : 0;
        if (warning != 0 && (_warnedReasons & warning) == 0)
        {
            _warnedReasons |= warning;
            VRLog.Warn("Core", after.State == Reason.RuntimePath
                ? "Steam Link connection could not be checked: the SteamVR runtime registry or API path is unavailable or invalid."
                : "Steam Link connection could not be checked: its embedded identity reader is missing, changed or unavailable.");
        }
        if ((before.State != after.State || before.TrackingSystem != after.TrackingSystem
            || before.Model != after.Model || before.RegisteredDeviceType != after.RegisteredDeviceType) && VRLog.WantsDebug)
            VRLog.Debug("Core", "Steam Link connection identity: " + after.State
                + "; tracking=" + after.TrackingSystem + "; model=" + after.Model
                + "; registered=" + after.RegisteredDeviceType + "; receiver=unverified.");
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
    [Serializable] private sealed class RuntimePaths { public string[] runtime = Array.Empty<string>(); }

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
            if (exitCode != 0) return new Snapshot(data.error == "changed" ? Reason.Changed : Reason.NativeUnavailable);
            return Classify(data.connected, data.device_class, data.tracking_system, data.model, data.registered_type);
        }
        catch (Exception) { return new Snapshot(Reason.NativeUnavailable); }
    }

    internal const int MaximumOutput = 2048, HelperTimeoutMs = 5000;
    internal const string Resource = "GloomhavenVR.SteamLink.connection-reader.exe";
    internal const int HelperBytes = 42496;
    internal const string HelperSha256 = "1c3a2c92e188d095cb80bc56b6452237b977571beac97da9159281491990284d";

    private static Snapshot ReadWindows(CancellationToken cancellation)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
            return new Snapshot(Reason.UnsupportedPlatform);
        try
        {
            string? library;
            try { library = RuntimeLibrary(); }
            catch (Exception) { return new Snapshot(Reason.RuntimePath); }
            if (library == null) return new Snapshot(Reason.RuntimePath);
            string? helper;
            try { helper = PrepareHelper(); }
            catch (Exception) { return new Snapshot(Reason.HelperUnavailable); }
            if (helper == null) return new Snapshot(Reason.HelperUnavailable);
            return RunChild(helper, library, cancellation);
        }
        catch (Exception) { return new Snapshot(Reason.NativeUnavailable); }
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

    private static string? RuntimeLibrary()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local) || !LocalWindowsPath(local)) return null;
        string registry = Path.Combine(local, "openvr", "openvrpaths.vrpath");
        if (!PathSafe(registry)) return null;
        byte[] raw = ReadBounded(registry, 32768);
        string json = new UTF8Encoding(false, true).GetString(raw);
        if (Regex.Matches(json, "\"runtime\"\\s*:").Count != 1) return null;
        RuntimePaths paths = JsonUtility.FromJson<RuntimePaths>(json);
        if (paths == null || paths.runtime == null || paths.runtime.Length == 0 || paths.runtime.Length > 4) return null;
        string[] values = paths.runtime.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (values.Length != 1) return null; // No guessed runtime in an ambiguous registry.
        string root = values[0].Replace('/', '\\').TrimEnd('\\');
        string library = Path.Combine(root, "bin", "win64", "openvr_api.dll");
        return PathSafe(library) && Pe64(ReadBounded(library, 8 * 1024 * 1024), true) ? library : null;
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
    {
        if (raw.Length != HelperBytes || !Pe64(raw, false)) return false;
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(raw)).Replace("-", "").ToLowerInvariant() == HelperSha256;
    }
    private static string? PrepareHelper()
    {
        using Stream? resource = typeof(SteamLinkConnection).Assembly.GetManifestResourceStream(Resource);
        if (resource == null || resource.Length != HelperBytes) return null;
        byte[] raw = new byte[HelperBytes]; int used = 0;
        while (used < raw.Length) { int read = resource.Read(raw, used, raw.Length - used); if (read <= 0) return null; used += read; }
        if (!HelperPin(raw)) return null;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local) || !LocalWindowsPath(local)) return null;
        string directory = Path.Combine(local, "GloomhavenVR", "SteamLink", HelperSha256);
        Directory.CreateDirectory(directory);
        if (!PathSafe(directory)) return null;
        string path = Path.Combine(directory, "connection-reader.exe");
        if (!File.Exists(path))
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(raw, 0, raw.Length); file.Flush();
        }
        return PathSafe(path) && HelperPin(ReadBounded(path, HelperBytes)) ? path : null;
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
        foreach (string name in new[] { "LD_PRELOAD", "LD_AUDIT", "VR_OVERRIDE", "VR_CONFIG_PATH", "VR_LOG_PATH" })
            start.EnvironmentVariables.Remove(name);
        using var child = new Process { StartInfo = start };
        if (!child.Start()) return new Snapshot(Reason.NativeUnavailable);
        void Kill() { try { if (!child.HasExited) child.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        using CancellationTokenRegistration cancel = cancellation.Register(Kill);
        try
        {
        Task<string?> stdout = BoundedOutput(child.StandardOutput, MaximumOutput, Kill);
        Task<string?> stderr = BoundedOutput(child.StandardError, 512, Kill);
        bool exited = child.WaitForExit(HelperTimeoutMs);
        if (!exited) { Kill(); exited = child.WaitForExit(1000); }
        if (!exited || !Task.WaitAll(new Task[] { stdout, stderr }, 1000)) return new Snapshot(Reason.NativeUnavailable);
        if (cancellation.IsCancellationRequested || stdout.Result == null || stderr.Result == null || !string.IsNullOrWhiteSpace(stderr.Result))
            return new Snapshot(Reason.NativeUnavailable);
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GloomhavenVR.Core.SteamLink;

// Public-driver settings live in one isolated Background client. Its stdin is
// the ownership lease, not a timeout. No image, camera or render callback is
// touched. All process/filesystem work, including cooperative EOF restoration,
// executes off the Unity thread. Scene/head/menu replacement is not a release.
internal static class SteamLinkStreamQuality
{
    internal const string Resource = "GloomhavenVR.SteamLink.stream-quality.exe";
    internal static readonly int HelperBytes = 46080;
    internal static readonly string HelperSha256 = "aae6bf6a0da3fd3a0f2e9e6b65ff6375786b5058fc4c8fc7c752c47298202ccd";
    internal const int RestoreGraceMs = 5000;
    private static readonly Monitor State = new(AcquireAsync, (warning, message) =>
    {
        if (warning) VRLog.Warn("Core", "Steam Link stream quality: " + message);
        else VRLog.Info("Core", "Steam Link stream quality: " + message);
    });

    internal static void Tick(bool enabled, int widthPixels, double nowSeconds)
    {
        State.Tick(Requested(enabled), NormalizeWidth(widthPixels), nowSeconds);
    }
    internal static bool Requested(bool enabled) => enabled && VRSession.IsRunning && !FrameNativePassthrough.Required
            && SteamLinkConnection.Available && MixedReality.Enabled != null && MixedReality.Enabled.Value
            && MixedReality.SteamLinkNative != null && MixedReality.SteamLinkNative.Value;
    internal static void Reset() => State.Reset();
    internal static bool ValidWidth(int value) => value >= 64 && value <= 16384 && value % 64 == 0;
    internal static int NormalizeWidth(int value)
    {
        return SteamLinkResolutionPolicy.TryWidth(value, 1, out int normalized) ? normalized : 0;
    }

    internal sealed class ReleaseResult
    {
        internal readonly bool Restored, Exited;
        internal readonly int Conflicts;
        internal readonly string Detail;
        internal ReleaseResult(bool restored, bool exited = true, int conflicts = 0, string detail = "")
        { Restored = restored; Exited = exited; Conflicts = conflicts; Detail = detail; }
    }
    internal interface ILease
    {
        bool Applied { get; }
        int AppliedWidth { get; }
        Task<ReleaseResult> Completion { get; }
        Task<ReleaseResult> ReleaseAsync();
    }
    internal enum Phase { Idle, Acquiring, Active, Releasing, Unavailable }

    // Tick only schedules a worker and changes finite state under this lock.
    // A stale acquisition must finish its owned restoration before the worker
    // completes; a newer generation cannot overlap that helper, even after Reset.
    internal sealed class Monitor
    {
        internal const int MaximumAttempts = 3;
        private readonly object _gate = new();
        private readonly Func<int, Task<ILease>> _acquire;
        private readonly Action<bool, string> _log;
        private Task? _worker;
        private TaskCompletionSource<bool>? _stop;
        private bool _wanted, _unsafe;
        private int _width, _attempts;
        private ulong _epoch;
        private double _next;
        private Phase _phase;
        internal Monitor(Func<int, Task<ILease>> acquire, Action<bool, string> log)
        { _acquire = acquire; _log = log; }
        internal Phase Current { get { lock (_gate) return _phase; } }
        internal int Attempts { get { lock (_gate) return _attempts; } }
        internal Task Settled { get { lock (_gate) return _worker ?? Task.CompletedTask; } }

        internal void Reset()
        {
            lock (_gate)
            {
                _wanted = false; _epoch++; _attempts = 0; _next = 0;
                _stop?.TrySetResult(true);
                _phase = _worker != null && !_worker.IsCompleted ? Phase.Releasing : Phase.Idle;
            }
        }
        internal void Tick(bool wanted, int width, double now)
        {
            lock (_gate)
            {
                // XR may briefly withhold its extent during a load. Keep only
                // this requested generation's known width; never invent an
                // initial size or retain one after Off/session end/Reset.
                if (wanted && width == 0 && _wanted && ValidWidth(_width)) width = _width;
                if (double.IsNaN(now) || double.IsInfinity(now) || now < 0 || !ValidWidth(width)) wanted = false;
                if (_wanted != wanted || wanted && _width != width)
                {
                    _epoch++; _wanted = wanted; _width = width; _attempts = 0; _next = now;
                    _stop?.TrySetResult(true);
                }
                if (_worker != null && !_worker.IsCompleted) return;
                // Observe a completed task only; never wait on the caller.
                if (_worker != null && _worker.IsFaulted) { _ = _worker.Exception; _unsafe = true; }
                _worker = null;
                if (!wanted) { _phase = Phase.Idle; return; }
                if (_unsafe || _attempts >= MaximumAttempts) { _phase = Phase.Unavailable; return; }
                if (now < _next) return;
                _attempts++; _next = now + 2;
                ulong epoch = _epoch;
                var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = stop; _phase = Phase.Acquiring;
                _worker = Task.Run(() => RunAsync(epoch, width, stop));
            }
        }
        private async Task RunAsync(ulong epoch, int width, TaskCompletionSource<bool> stop)
        {
            ILease? lease = null;
            try
            {
                lease = await _acquire(width).ConfigureAwait(false);
                bool current;
                lock (_gate)
                {
                    current = lease.Applied && _wanted && _epoch == epoch && !stop.Task.IsCompleted;
                    if (current) _phase = Phase.Active;
                }
                if (current)
                {
                    _log(false, "active; foveation and sharpening disabled; requested uniform width " + width
                        + "px; driver setting readback " + lease.AppliedWidth
                        + "px, following the current XR carrier. This is not proof of negotiated video resolution.");
                    await Task.WhenAny(stop.Task, lease.Completion).ConfigureAwait(false);
                }
            }
            catch (Exception ex) { _log(true, "could not acquire settings lease (" + ex.GetType().Name + ")."); }
            finally
            {
                if (lease != null)
                {
                    lock (_gate) _phase = Phase.Releasing;
                    ReleaseResult released;
                    try { released = await lease.ReleaseAsync().ConfigureAwait(false); }
                    catch (Exception ex) { released = new ReleaseResult(false, false, detail: ex.GetType().Name); }
                    // Process exit alone does not prove restoration. Retrying
                    // could adopt our own un-restored value as a new baseline.
                    lock (_gate) if (!released.Restored || !released.Exited) _unsafe = true;
                    _log(!released.Restored || !lease.Applied, released.Restored
                        ? lease.Applied
                            ? "released; original settings restored where still owned; external changes preserved=" + released.Conflicts + "."
                            : "requested settings lease was refused; no owned changes remain; stage=" + released.Detail + "."
                        : "restore unconfirmed; helper exited=" + released.Exited + "; stage=" + released.Detail + ".");
                }
                lock (_gate) _phase = _unsafe ? Phase.Unavailable : Phase.Idle;
            }
        }
    }

    private static async Task<ILease> AcquireAsync(int width)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8 || !ValidWidth(width))
            throw new PlatformNotSupportedException();
        if (HelperBytes <= 0 || HelperSha256.Length != 64) throw new InvalidDataException("Helper pin is not frozen.");
        string stage = "SDK";
        string? sdk = SteamLinkConnection.PrepareVerifiedResource(SteamLinkConnection.ApiResource,
            SteamLinkConnection.ApiBytes, SteamLinkConnection.ApiSha256, "openvr_api.dll", true, ref stage);
        string? helper = SteamLinkConnection.PrepareVerifiedResource(Resource, HelperBytes, HelperSha256,
            "stream-quality.exe", false, ref stage);
        if (sdk == null || helper == null) throw new InvalidDataException("Verified helper preparation failed at " + stage);
        var start = new ProcessStartInfo(helper, "\"" + sdk + "\" " + width)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(helper)!
        };
        start.EnvironmentVariables["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        // Keep Proton/Wine/OpenVR's inherited runtime bridge, but not loader injection.
        foreach (string name in new[] { "LD_PRELOAD", "LD_AUDIT" }) start.EnvironmentVariables.Remove(name);
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new IOException("Helper could not start.");
        }
        catch { process.Dispose(); throw; }
        ProcessLease? lease = null;
        return await GuardStartedAsync(async () =>
        {
            lease = new ProcessLease(process, width);
            if (await Task.WhenAny(lease.Ready, Task.Delay(5000)).ConfigureAwait(false) != lease.Ready
                || !await lease.Ready.ConfigureAwait(false))
                return new RefusedLease(await lease.ReleaseAsync().ConfigureAwait(false));
            return lease;
        }, () => lease != null ? lease.ReleaseAsync() : Task.Run(() =>
        {
            // Event/pipe setup failed after Start. Ask EOF before any wait/kill.
            // If monitoring itself fails, keep restoration explicitly unknown.
            bool exited = false;
            try
            {
                try { process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
                exited = process.WaitForExit(RestoreGraceMs);
                if (!exited)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    exited = process.WaitForExit(1000);
                }
                if (exited) process.Dispose();
            }
            catch { /* Never turn an unobserved post-Start failure into a retry. */ }
            return new ReleaseResult(false, exited, detail: "process-monitor-setup");
        })).ConfigureAwait(false);
    }

    internal static async Task<ILease> GuardStartedAsync(Func<Task<ILease>> monitorStarted,
        Func<Task<ReleaseResult>> restoreUnknown)
    {
        try { return await monitorStarted().ConfigureAwait(false); }
        catch
        {
            ReleaseResult result;
            try { result = await restoreUnknown().ConfigureAwait(false); }
            catch { result = new ReleaseResult(false, false, detail: "post-start-cleanup"); }
            return new RefusedLease(result);
        }
    }

    private sealed class RefusedLease : ILease
    {
        private readonly ReleaseResult _result;
        internal RefusedLease(ReleaseResult result) { _result = result; }
        public bool Applied => false;
        public int AppliedWidth => 0;
        public Task<ReleaseResult> Completion => Task.FromResult(_result);
        public Task<ReleaseResult> ReleaseAsync() => Task.FromResult(_result);
    }

    private sealed class ProcessLease : ILease
    {
        private readonly object _gate = new();
        private readonly Process _process;
        private readonly int _width;
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task<ReleaseResult> _watch;
        private Task<ReleaseResult>? _release;
        private int _closed;
        private int _appliedWidth;
        public bool Applied => _ready.Task.Status == TaskStatus.RanToCompletion && _ready.Task.Result;
        public int AppliedWidth => _appliedWidth;
        internal Task<bool> Ready => _ready.Task;
        internal ProcessLease(Process process, int width)
        {
            _process = process; _width = width;
            _process.Exited += OnExit; _process.EnableRaisingEvents = true;
            if (_process.HasExited) _exit.TrySetResult(true);
            _watch = WatchAsync();
        }
        private void OnExit(object? sender, EventArgs args) => _exit.TrySetResult(true);
        public Task<ReleaseResult> Completion => _watch;
        public Task<ReleaseResult> ReleaseAsync()
        {
            lock (_gate) return _release ??= Task.Run(ReleaseCoreAsync);
        }
        private void CloseInput()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
        }
        private async Task<ReleaseResult> ReleaseCoreAsync()
        {
            CloseInput(); // EOF is the first action; never kill instead of asking restoration.
            bool timedOut = await Task.WhenAny(_exit.Task, Task.Delay(RestoreGraceMs)).ConfigureAwait(false) != _exit.Task;
            if (timedOut)
            {
                try { if (!_process.HasExited) _process.Kill(); }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            bool exited = await Task.WhenAny(_exit.Task, Task.Delay(1000)).ConfigureAwait(false) == _exit.Task;
            if (!exited) return new ReleaseResult(false, false, detail: "EOF-grace-and-emergency-exit-unconfirmed");
            ReleaseResult observed;
            if (await Task.WhenAny(_watch, Task.Delay(1000)).ConfigureAwait(false) != _watch)
                observed = new ReleaseResult(false, detail: "exit-output-unconfirmed");
            else observed = await _watch.ConfigureAwait(false);
            _process.Exited -= OnExit; _process.Dispose();
            return timedOut ? new ReleaseResult(false, detail: "EOF-restore-grace-expired-emergency-kill") : observed;
        }
        private async Task<ReleaseResult> WatchAsync()
        {
            Task<string?> errors = ReadBoundedAsync(_process.StandardError, 512);
            bool acquired = false, valid = true, restored = false, terminal = false, errorReceipt = false;
            int conflicts = 0, total = 0;
            string detail = "helper-receipt";
            try
            {
                for (int lineCount = 0; ; lineCount++)
                {
                    string? line = await ReadLineAsync(_process.StandardOutput, 2048).ConfigureAwait(false);
                    if (line == null) break;
                    total += line.Length;
                    if (lineCount >= 4 || total > 4096) { valid = false; CloseInput(); break; }
                    if (!terminal && !acquired && DecodeAcquired(line, _width))
                    { acquired = true; _appliedWidth = int.Parse(Fields(line)!["applied_width"]); _ready.TrySetResult(true); }
                    else if (!terminal && acquired && DecodeRestored(line, out bool ok, out int count))
                    { restored = ok; conflicts = count; terminal = true; }
                    else if (!terminal && DecodeError(line, out bool rolledBack, out int preserved))
                    {
                        restored = rolledBack; conflicts = preserved; terminal = true; errorReceipt = true;
                        string code = Fields(line)!["error"];
                        detail = code.Length <= 48 ? "helper:" + code : "helper:invalid-error";
                        _ready.TrySetResult(false);
                    }
                    else { valid = false; _ready.TrySetResult(false); CloseInput(); }
                }
                _ready.TrySetResult(false);
                await _exit.Task.ConfigureAwait(false);
                string? stderr = await errors.ConfigureAwait(false);
                return new ReleaseResult(valid && terminal && restored && stderr != null && stderr.Length == 0
                    && (acquired && !errorReceipt && _process.ExitCode == 0 || errorReceipt && _process.ExitCode == 2),
                    conflicts: conflicts, detail: detail);
            }
            catch { _ready.TrySetResult(false); CloseInput(); return new ReleaseResult(false, _exit.Task.IsCompleted, detail: "helper-output"); }
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, int maximum)
    {
        var text = new StringBuilder(); char[] one = new char[1];
        while (await reader.ReadAsync(one, 0, 1).ConfigureAwait(false) != 0)
        {
            if (one[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length == maximum) throw new InvalidDataException("Bounded helper line exceeded.");
            text.Append(one[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }
    private static async Task<string?> ReadBoundedAsync(StreamReader reader, int maximum)
    {
        var text = new StringBuilder(); char[] buffer = new char[128]; int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
        { if (text.Length > maximum - count) return null; text.Append(buffer, 0, count); }
        return text.ToString();
    }
    private static Dictionary<string, string>? Fields(string raw)
    {
        if (raw == null || raw.Length > 2048) return null;
        const string field = "\"([a-z_]+)\"\\s*:\\s*(\"[a-z0-9_-]+\"|-?[0-9]{1,10}|true|false)";
        if (!Regex.IsMatch(raw, "^\\s*\\{\\s*" + field + "(?:\\s*,\\s*" + field + ")*\\s*\\}\\s*$", RegexOptions.CultureInvariant)) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(raw, field, RegexOptions.CultureInvariant))
        {
            string key = match.Groups[1].Value, token = match.Groups[2].Value;
            bool text = key == "schema" || key == "event" || key == "error";
            bool boolean = key == "applied" || key == "ok" || key == "restored"
                || key == "previous_sharpening" || key == "previous_automatic_width";
            if (values.ContainsKey(key) || text != (token[0] == '"')
                || boolean && token != "true" && token != "false"
                || !text && !boolean && (token == "true" || token == "false")) return null;
            values.Add(key, text ? token.Substring(1, token.Length - 2) : token);
        }
        return values.TryGetValue("schema", out string? schema) && schema == "ghvr-steamlink-quality-v1" ? values : null;
    }
    internal static bool DecodeAcquired(string raw, int requested)
    {
        Dictionary<string, string>? f = Fields(raw);
        string[] keys = { "schema", "event", "applied", "requested_width", "applied_width", "previous_foveation_mode", "previous_sharpening", "previous_automatic_width", "previous_uniform_width" };
        int absent = 0;
        return f != null && (f.Count == keys.Length || f.Count == keys.Length + 1 && f.ContainsKey("previous_absent_mask"))
            && (!f.ContainsKey("previous_absent_mask") || int.TryParse(f["previous_absent_mask"], out absent) && absent >= 0 && absent <= 15)
            && keys.All(f.ContainsKey) && f["event"] == "acquired" && f["applied"] == "true"
            && int.TryParse(f["requested_width"], out int width) && width == requested
            && int.TryParse(f["applied_width"], out int applied)
            && int.TryParse(f["previous_foveation_mode"], out int previousMode)
            && int.TryParse(f["previous_uniform_width"], out int previousWidth)
            && applied <= 16384 && previousWidth >= 0 && previousWidth <= 16384 && previousMode >= 0 && previousMode <= 3
            && ((absent & 1) == 0 || previousWidth == 0) && ((absent & 8) == 0 || previousMode == 0)
            && ((absent & 2) == 0 || f["previous_automatic_width"] == "false")
            && ((absent & 4) == 0 || f["previous_sharpening"] == "false")
            && (f["previous_sharpening"] == "true" || f["previous_sharpening"] == "false")
            && (f["previous_automatic_width"] == "true" || f["previous_automatic_width"] == "false")
            && (ValidWidth(applied) && applied == requested || applied > requested && applied == previousWidth && (absent & 11) == 0
                && f["previous_automatic_width"] == "false" && (previousMode == 1 || previousMode == 3));
    }
    internal static bool DecodeRestored(string raw, out bool restored, out int conflicts)
    {
        restored = false; conflicts = 0; Dictionary<string, string>? f = Fields(raw);
        string[] keys = { "schema", "event", "ok", "conflicts", "errors" };
        if (f == null || f.Count != keys.Length || !keys.All(f.ContainsKey) || f["event"] != "restored"
            || (f["ok"] != "true" && f["ok"] != "false") || !int.TryParse(f["conflicts"], out conflicts) || conflicts < 0 || conflicts > 4
            || !int.TryParse(f["errors"], out int errors) || errors < 0 || errors > 4) return false;
        restored = f["ok"] == "true" && errors == 0; return true;
    }
    internal static bool DecodeError(string raw, out bool restored, out int conflicts)
    {
        restored = false; conflicts = 0; Dictionary<string, string>? f = Fields(raw);
        string[] keys = { "schema", "event", "error", "restored", "conflicts", "errors" };
        if (f == null || f.Count != keys.Length || !keys.All(f.ContainsKey) || f["event"] != "error"
            || (f["restored"] != "true" && f["restored"] != "false") || !int.TryParse(f["conflicts"], out conflicts) || conflicts < 0 || conflicts > 4
            || !int.TryParse(f["errors"], out int errors) || errors < 0 || errors > 4) return false;
        restored = f["restored"] == "true" && errors == 0; return true;
    }
}

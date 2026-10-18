using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GloomhavenVR.Core.SteamLink;

// Optional Debug-only, read-only public compositor observations. This does NOT
// repair dashboard composition, identify a video frame, or change rendering,
// fade, settings or focus. Unity schedules the worker; no OpenVR API runs here.
internal static class SteamLinkCompositorMonitor
{
    internal const string Resource = "GloomhavenVR.SteamLink.compositor-state-probe.exe";
    internal static readonly int HelperBytes = 957952;
    internal static readonly string HelperSha256 = "fd5496814dbcc4a37de7d73e46fae155000197404adec353f29bdeb2e0009b38";
    internal const int MaximumSamples = 32, MaximumLine = 1024, MaximumBytes = 32768;
    internal const int StopGraceMs = 5000;
    private static readonly Monitor State = new(LaunchAsync, line =>
    {
        if (VRLog.WantsDebug) VRLog.Debug("Core", "Steam Link compositor observation (read-only/non-atomic): " + line);
    });
    internal static bool Eligible(bool debug, bool requested) => debug && requested;
    internal static void Tick(bool requested) => State.Tick(Eligible(VRLog.WantsDebug, requested));
    internal static void Reset() => State.Reset();

    internal interface IChild
    {
        TextReader Output { get; }
        Task Exited { get; }
        Task<bool> StopAsync(); // Cooperative stdin EOF first; bounded teardown only.
    }
    internal sealed class OwnershipUnknownException : IOException { }
    internal enum Phase { Idle, Starting, Observing, Spent, Stopping, Unsafe }
    internal sealed class Monitor
    {
        private readonly object _gate = new();
        private readonly Func<Task<IChild>> _launch;
        private readonly Action<string> _log;
        private Task? _worker;
        private TaskCompletionSource<bool>? _stop;
        private bool _wanted, _attempted, _unsafe;
        private ulong _epoch;
        private Phase _phase;
        internal Monitor(Func<Task<IChild>> launch, Action<string> log) { _launch = launch; _log = log; }
        internal Phase Current { get { lock (_gate) return _phase; } }
        internal Task Settled { get { lock (_gate) return _worker ?? Task.CompletedTask; } }
        internal void Reset()
        {
            lock (_gate)
            {
                _wanted = false; _attempted = false; _epoch++;
                _stop?.TrySetResult(true);
                _phase = _worker != null && !_worker.IsCompleted ? Phase.Stopping : Phase.Idle;
            }
        }
        internal void Tick(bool wanted)
        {
            lock (_gate)
            {
                if (_wanted != wanted)
                {
                    _wanted = wanted; _attempted = false; _epoch++;
                    _stop?.TrySetResult(true);
                }
                // A stale pending launch must finish its owned teardown before
                // any later generation may start, including after Reset.
                if (_worker != null && !_worker.IsCompleted) return;
                if (_worker != null && _worker.IsFaulted) { _ = _worker.Exception; _unsafe = true; }
                _worker = null;
                if (!wanted) { _phase = _unsafe ? Phase.Unsafe : Phase.Idle; return; }
                if (_unsafe) { _phase = Phase.Unsafe; return; }
                if (_attempted) { _phase = Phase.Spent; return; }
                _attempted = true;
                var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = stop; _phase = Phase.Starting;
                ulong epoch = _epoch;
                _worker = Task.Run(() => RunAsync(epoch, stop));
            }
        }
        private bool CurrentEpoch(ulong epoch) { lock (_gate) return _wanted && _epoch == epoch; }
        private void Log(ulong epoch, string text) { if (CurrentEpoch(epoch)) _log(text); }
        private async Task RunAsync(ulong epoch, TaskCompletionSource<bool> stop)
        {
            IChild? child = null; Task? reader = null;
            try
            {
                child = await _launch().ConfigureAwait(false);
                if (!CurrentEpoch(epoch) || stop.Task.IsCompleted) return;
                lock (_gate) _phase = Phase.Observing;
                reader = ReadAsync(child.Output, epoch);
                await Task.WhenAny(stop.Task, reader, child.Exited).ConfigureAwait(false);
                if (reader.IsCompleted) await reader.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OwnershipUnknownException) { lock (_gate) _unsafe = true; }
                Log(epoch, "observer unavailable; stage=" + ex.GetType().Name + "; no rendering or setting changed.");
            }
            finally
            {
                if (child != null)
                {
                    lock (_gate) _phase = Phase.Stopping;
                    bool stopped;
                    try { stopped = await child.StopAsync().ConfigureAwait(false); }
                    catch { stopped = false; }
                    if (reader != null)
                    {
                        // This grace retires CPU/pipe resources only. It never
                        // disables MR or restarts Steam/SteamVR/the observer.
                        if (await Task.WhenAny(reader, Task.Delay(1000)).ConfigureAwait(false) != reader) stopped = false;
                        else { try { await reader.ConfigureAwait(false); } catch { } }
                    }
                    lock (_gate) if (!stopped) _unsafe = true;
                    if (!stopped) Log(epoch, "observer teardown unconfirmed; further observation suppressed.");
                }
                lock (_gate) _phase = _unsafe ? Phase.Unsafe : _wanted ? Phase.Spent : Phase.Idle;
            }
        }
        private async Task ReadAsync(TextReader output, ulong epoch)
        {
            var protocol = new Protocol();
            var buffer = new char[128]; var line = new StringBuilder(MaximumLine);
            int total = 0;
            for (;;)
            {
                int count = await output.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0)
                {
                    if (line.Length != 0) throw new InvalidDataException("Incomplete bounded observation line.");
                    return;
                }
                for (int n = 0; n < count; n++)
                {
                    char value = buffer[n];
                    if (++total > MaximumBytes || value > 127 || value == '\0') throw new InvalidDataException("Observation budget/encoding.");
                    if (value == '\n')
                    {
                        string input = line.ToString(); line.Clear();
                        if (input.EndsWith("\r", StringComparison.Ordinal)) input = input.Substring(0, input.Length - 1);
                        if (!protocol.Accept(input, out string canonical, out string kind)) throw new InvalidDataException("Observation protocol.");
                        if (kind == "sample" || kind == "refused") Log(epoch, canonical);
                        if (kind == "budget") { lock (_gate) if (_wanted && _epoch == epoch) _phase = Phase.Spent; }
                    }
                    else
                    {
                        if (line.Length >= MaximumLine - 1) throw new InvalidDataException("Observation line budget.");
                        line.Append(value);
                    }
                }
            }
        }
    }

    internal sealed class Protocol
    {
        private bool _ready, _ended, _budget;
        private uint _samples;
        internal bool Accept(string line, out string canonical, out string kind)
        {
            if (!Decode(line, out canonical, out kind, out uint sample, out uint samples)) return false;
            if (_ended) return false;
            if (kind == "ready") { if (_ready || _samples != 0) return false; _ready = true; }
            else if (kind == "sample")
            {
                if (!_ready || _budget || sample != _samples + 1 || sample > MaximumSamples) return false;
                _samples = sample;
            }
            else if (kind == "budget") { if (!_ready || _budget || samples != _samples) return false; _budget = true; }
            else if (kind == "stopped") { if (!_ready || samples != _samples) return false; _ended = true; }
            else if (kind == "refused") { if (_ready || _samples != 0) return false; _ended = true; }
            return true;
        }
    }

    // Flat, closed protocol. Never deserialize arbitrary child objects, escaped
    // strings or messages. Missing/error-valued measurements stay unavailable.
    private static class JsonTokens
    {
    internal static readonly Regex Pair = new("\\G\\s*\"(?<key>[a-z_][a-z0-9_]{0,47})\"\\s*:\\s*(?<value>\"[A-Za-z0-9_.-]{1,64}\"|true|false|-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]{1,3})?)", RegexOptions.CultureInvariant);
    }
    internal static bool Decode(string line, out string canonical, out string kind, out uint sample, out uint samples)
    {
        canonical = kind = ""; sample = samples = 0;
        if (line.Length == 0 || line.Length >= MaximumLine || line.Any(c => c > 127 || c < 32)) return false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        int position = 0; while (position < line.Length && line[position] == ' ') position++;
        if (position >= line.Length || line[position++] != '{') return false;
        for (;;)
        {
            Match match = JsonTokens.Pair.Match(line, position);
            if (!match.Success || match.Index != position) return false;
            string key = match.Groups["key"].Value, value = match.Groups["value"].Value;
            if (fields.ContainsKey(key)) return false;
            fields.Add(key, value); position = match.Index + match.Length;
            while (position < line.Length && line[position] == ' ') position++;
            if (position >= line.Length) return false;
            char next = line[position++]; if (next == '}') break; if (next != ',') return false;
        }
        while (position < line.Length && line[position] == ' ') position++;
        if (position != line.Length || !fields.TryGetValue("schema", out string? schema) || schema != "\"sfnp-pc-compositor-state-v1\""
            || !fields.TryGetValue("event", out string? eventValue) || eventValue == null) return false;
        kind = eventValue.Trim('"');
        bool Keys(string names) => new HashSet<string>(("schema event " + names).Split(' '), StringComparer.Ordinal).SetEquals(fields.Keys);
        bool Bool(string key) => fields.TryGetValue(key, out string? v) && (v == "true" || v == "false");
        bool Int(string key) => fields.TryGetValue(key, out string? v) && int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        bool UInt(string key, out uint v) { v = 0; return fields.TryGetValue(key, out string? text) && uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out v); }
        bool ULong(string key, out ulong v) { v = 0; return fields.TryGetValue(key, out string? text) && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out v); }
        bool Float(string key) => fields.TryGetValue(key, out string? v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && !float.IsNaN(f) && !float.IsInfinity(f);
        if (kind == "ready") { if (!Keys("read_only non_atomic") || fields["read_only"] != "true" || fields["non_atomic"] != "true") return false; }
        else if (kind == "refused") { if (!Keys("stage code") || !Int("stage") || !Int("code")) return false; }
        else if (kind == "budget" || kind == "stopped")
        {
            if (!Keys(kind == "budget" ? "samples bytes" : "samples") || !UInt("samples", out samples) || samples > MaximumSamples) return false;
            if (kind == "budget" && (!UInt("bytes", out uint bytes) || bytes > MaximumBytes)) return false;
        }
        else if (kind == "sample")
        {
            const string floats = "fg_r fg_g fg_b fg_a bg_r bg_g bg_b bg_a grid_alpha scene_dimming";
            const string bools = "numeric_valid dashboard_visible can_render_scene do_not_fade_to_grid force_fade_tracking standalone_system_layer hmd_connected identity_stable identity_valid";
            const string ints = "do_not_fade_to_grid_error force_fade_tracking_error scene_dimming_error standalone_system_layer_error hmd_class frame_link_profile tracking_error model_error registration_error";
            if (!Keys("sample begin_ms end_ms scene_focus_pid last_frame_renderer_pid " + floats + " " + bools + " " + ints)
                || !UInt("sample", out sample) || sample == 0 || sample > MaximumSamples
                || !ULong("begin_ms", out ulong begin) || !ULong("end_ms", out ulong end) || end < begin
                || !UInt("scene_focus_pid", out _) || !UInt("last_frame_renderer_pid", out _)
                || floats.Split(' ').Any(key => !Float(key)) || bools.Split(' ').Any(key => !Bool(key))
                || ints.Split(' ').Any(key => !Int(key)) || !UInt("frame_link_profile", out uint profile) || profile > 2) return false;
            if (fields["numeric_valid"] == "false") foreach (string key in floats.Split(' ')) fields[key] = "null";
            foreach (string key in new[] { "do_not_fade_to_grid", "force_fade_tracking", "scene_dimming", "standalone_system_layer" })
                if (fields[key + "_error"] != "0") fields[key] = "null";
        }
        else return false;
        canonical = "{" + string.Join(",", fields.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => "\"" + pair.Key + "\":" + pair.Value)) + "}";
        return true;
    }

    private static Task<IChild> LaunchAsync()
    {
        // Called only by the off-Unity worker after the complete eligibility gate.
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) throw new PlatformNotSupportedException();
        if (HelperBytes <= 0 || HelperSha256.Length != 64) throw new InvalidDataException("Observation helper pin is not frozen.");
        string stage = "SDK";
        string? sdk = SteamLinkConnection.PrepareVerifiedResource(SteamLinkConnection.ApiResource,
            SteamLinkConnection.ApiBytes, SteamLinkConnection.ApiSha256, "openvr_api.dll", true, ref stage);
        string? helper = SteamLinkConnection.PrepareVerifiedResource(Resource, HelperBytes, HelperSha256,
            "compositor-state-probe.exe", false, ref stage);
        if (sdk == null || helper == null) throw new InvalidDataException("Verified observation helper unavailable.");
        var start = new ProcessStartInfo(helper, "--stdin-pipe --openvr \"" + sdk + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(helper)!
        };
        start.EnvironmentVariables["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (string name in new[] { "LD_PRELOAD", "LD_AUDIT" }) start.EnvironmentVariables.Remove(name);
        var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new IOException("Observation helper start refused."); }
        catch { process.Dispose(); throw; }
        try { return Task.FromResult<IChild>(new ProcessChild(process)); }
        catch
        {
            bool exited = false;
            try
            {
                try { process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
                exited = process.WaitForExit(StopGraceMs);
                if (!exited) { process.Kill(); exited = process.WaitForExit(1000); }
            }
            catch { /* Preserve unknown ownership; do not start another helper. */ }
            if (exited) process.Dispose();
            else throw new OwnershipUnknownException();
            throw;
        }
    }
    private sealed class ProcessChild : IChild
    {
        private readonly Process _process;
        private readonly TaskCompletionSource<bool> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<bool>? _stop; private readonly object _gate = new();
        private readonly Task _stderr;
        public TextReader Output => _process.StandardOutput;
        public Task Exited => _exit.Task;
        internal ProcessChild(Process process)
        {
            _process = process; _process.Exited += (_, _) => _exit.TrySetResult(true); _process.EnableRaisingEvents = true;
            if (_process.HasExited) _exit.TrySetResult(true);
            _stderr = Task.Run(() =>
            {
                try { var scratch = new char[128]; int total = 0, read; while ((read = _process.StandardError.Read(scratch, 0, scratch.Length)) != 0) if ((total += read) > 1024) { _process.StandardError.Close(); break; } }
                catch (IOException) { } catch (InvalidOperationException) { }
            });
        }
        public Task<bool> StopAsync() { lock (_gate) return _stop ??= Task.Run(() =>
        {
            bool exited = false;
            try
            {
                try { _process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
                exited = _process.WaitForExit(StopGraceMs);
                if (!exited) { try { _process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } exited = _process.WaitForExit(1000); }
            }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            finally
            {
                if (exited)
                {
                    try { _process.StandardOutput.Dispose(); _process.StandardError.Dispose(); if (!_stderr.Wait(1000)) exited = false; }
                    catch (IOException) { exited = false; } catch (InvalidOperationException) { exited = false; }
                    _process.Dispose();
                }
            }
            return exited;
        }); }
    }
}

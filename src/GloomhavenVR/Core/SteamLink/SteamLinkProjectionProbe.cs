using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GloomhavenVR.Core.SteamLink;

// Explicit, temporary projection-delivery preview, NOT a production quality fix.
// Public overlays have frozen poses: the player must keep their head still.
// API readiness is NOT proof of remote visibility. The preview never owns,
// clears, replaces or otherwise changes a camera, source image or MR carrier.
internal static class SteamLinkProjectionProbe
{
    internal const string Resource = "GloomhavenVR.SteamLink.projection-overlay-probe.exe";
    internal static readonly int HelperBytes = 210944;
    internal static readonly string HelperSha256 = "a70d83192d272ac63a3713b4f319a6e55b29b22e3b8a22cfce3533b845ba7feb";
    internal const string ReadyLine = "SFNP_PROJECTION_READY_V2", StoppedLine = "SFNP_PROJECTION_STOPPED_V2";
    internal const int MaximumBytes = 8192, MaximumLine = 1024;
    private static readonly Owner State = new(LaunchAsync, text =>
    {
        if (VRLog.WantsDebug) VRLog.Debug("Core", "Steam Link projection test: " + text);
    });
    internal static Phase Status => State.Current;
    internal static bool Eligible => VRLog.WantsDebug && VRSession.IsRunning
        && WorldUI.VROptionsTab.ProjectionProbeCheatsEnabled && MixedReality.Enabled.Value
        && SteamLinkSceneRuntime.Requested && SteamLinkSceneRuntime.ProjectionProbeSourceReady;

    internal static bool Start()
    {
        // Recheck the actual gate on stale UI callbacks. Starting schedules the
        // owned helper immediately: no future Tick/menu close is a launch gate.
        if (!Eligible) { Stop(); return false; }
        if (!WorldUI.VROptionsTab.IsOpen) return false;
        return State.Start(true, true);
    }

    internal static void Tick()
    {
        // Ordinary users do not even poll the options window for this preview.
        if (State.OwnsTest) State.Tick(Eligible, WorldUI.VROptionsTab.IsOpen);
    }
    internal static void Stop() => State.Stop();

    internal enum Phase { Idle, Starting, Active, Stopping, Failed, Unsafe }
    internal sealed class Owner
    {
        private readonly object _gate = new();
        private readonly Func<Task<SteamLinkCompositorMonitor.IChild>> _launch;
        private readonly Action<string> _log;
        private Task? _worker;
        private TaskCompletionSource<bool>? _stop;
        private bool _wanted, _unsafe, _menuHasClosed;
        private ulong _epoch;
        private Phase _phase;
        internal Owner(Func<Task<SteamLinkCompositorMonitor.IChild>> launch, Action<string> log) { _launch = launch; _log = log; }
        internal Phase Current { get { lock (_gate) return _phase; } }
        internal bool OwnsTest { get { lock (_gate) return _wanted; } }
        internal Task Settled { get { lock (_gate) return _worker ?? Task.CompletedTask; } }
        internal bool Start(bool eligible, bool menuOpen)
        {
            lock (_gate)
            {
                if (!eligible || !menuOpen || _unsafe || _wanted || (_worker != null && !_worker.IsCompleted)) return false;
                if (_worker != null && _worker.IsFaulted) { _ = _worker.Exception; _unsafe = true; _phase = Phase.Unsafe; return false; }
                _wanted = true; _epoch++; _phase = Phase.Starting;
                _menuHasClosed = false;
                var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = stop;
                ulong epoch = _epoch;
                _worker = Task.Run(() => RunAsync(epoch, stop));
                return true;
            }
        }
        internal void Stop()
        {
            lock (_gate)
            {
                _wanted = false; _epoch++; _stop?.TrySetResult(true);
                _phase = _unsafe ? Phase.Unsafe : _worker != null && !_worker.IsCompleted ? Phase.Stopping : Phase.Idle;
            }
        }
        internal void Tick(bool eligible, bool menuOpen)
        {
            lock (_gate)
            {
                if (!_wanted) return;
                // The initiating menu is tolerated only until its first actual
                // close. Closing is automatic in the UI, but even a failed close
                // never delays launch; its explicit Stop action remains usable.
                if (!menuOpen) _menuHasClosed = true;
                if (!eligible || (menuOpen && _menuHasClosed)) Stop();
            }
        }
        private bool CurrentEpoch(ulong epoch) { lock (_gate) return _wanted && _epoch == epoch; }
        private async Task RunAsync(ulong epoch, TaskCompletionSource<bool> stop)
        {
            SteamLinkCompositorMonitor.IChild? child = null; Task? reader = null;
            bool failed = false;
            try
            {
                child = await _launch().ConfigureAwait(false);
                if (!CurrentEpoch(epoch) || stop.Task.IsCompleted) return;
                reader = ReadAsync(child.Output, epoch);
                await Task.WhenAny(stop.Task, reader, child.Exited).ConfigureAwait(false);
                if (reader.IsCompleted) await reader.ConfigureAwait(false);
                if (CurrentEpoch(epoch) && !stop.Task.IsCompleted) throw new IOException("Projection helper ended.");
            }
            catch (Exception ex)
            {
                failed = true;
                if (ex is SteamLinkCompositorMonitor.OwnershipUnknownException) { lock (_gate) _unsafe = true; }
                if (CurrentEpoch(epoch)) _log("unavailable; stage=" + ex.GetType().Name + "; ordinary scene retained.");
            }
            finally
            {
                // Revoke preview ownership before any cooperative teardown wait.
                // Regardless of readiness or failure, the game image is untouched.
                lock (_gate) if (_epoch == epoch) { _wanted = false; _phase = Phase.Stopping; }
                if (child != null)
                {
                    bool stopped;
                    try { stopped = await child.StopAsync().ConfigureAwait(false); } catch { stopped = false; }
                    if (reader != null)
                    {
                        if (await Task.WhenAny(reader, Task.Delay(1000)).ConfigureAwait(false) != reader) stopped = false;
                        else { try { await reader.ConfigureAwait(false); } catch { } }
                    }
                    lock (_gate) if (!stopped) _unsafe = true;
                }
                lock (_gate)
                {
                    if (_epoch == epoch) { _wanted = false; _phase = _unsafe ? Phase.Unsafe : failed ? Phase.Failed : Phase.Idle; }
                    else _phase = _unsafe ? Phase.Unsafe : Phase.Idle;
                }
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
                if (count == 0) { if (line.Length != 0) throw new InvalidDataException("Incomplete projection line."); return; }
                for (int n = 0; n < count; n++)
                {
                    char value = buffer[n];
                    if (++total > MaximumBytes || value > 127 || value == '\0') throw new InvalidDataException("Projection output budget/encoding.");
                    if (value == '\n')
                    {
                        string input = line.ToString(); line.Clear();
                        if (input.EndsWith("\r", StringComparison.Ordinal)) input = input.Substring(0, input.Length - 1);
                        if (input.IndexOf('\r') >= 0) throw new InvalidDataException("Embedded projection carriage return.");
                        bool ready = protocol.Accept(input);
                        if (input.StartsWith("SFNP_PROJECTION_META_V2 ", StringComparison.Ordinal)
                            || input.StartsWith("SFNP_PROJECTION_GEOMETRY_V2 ", StringComparison.Ordinal)
                            || input.StartsWith("SFNP_PROJECTION_LOCAL_V2 ", StringComparison.Ordinal)
                            || input.StartsWith("SFNP_PROJECTION_REFUSED_V2 ", StringComparison.Ordinal))
                            if (CurrentEpoch(epoch)) _log(input);
                        if (input.StartsWith("SFNP_PROJECTION_REFUSED_V2 ", StringComparison.Ordinal))
                            throw new IOException("Projection helper refused.");
                        if (input == StoppedLine) lock (_gate) if (_epoch == epoch)
                        { _wanted = false; _phase = Phase.Stopping; _stop?.TrySetResult(true); }
                        if (ready) lock (_gate) if (_wanted && _epoch == epoch) { _phase = Phase.Active; _log("public preview locally ready; original game retained; remote visibility unverified."); }
                    }
                    else
                    {
                        if ((value < 32 && value != '\r') || line.Length >= MaximumLine - 1) throw new InvalidDataException("Projection line budget/encoding.");
                        line.Append(value);
                    }
                }
            }
        }
    }
    internal sealed class Protocol
    {
        private bool _ready, _stopped;
        internal bool Accept(string line)
        {
            if (_stopped) throw new InvalidDataException("Output after stop.");
            if (line == ReadyLine) { if (_ready) throw new InvalidDataException("Duplicate ready."); _ready = true; return true; }
            if (line == StoppedLine) { if (!_ready) throw new InvalidDataException("Stop before ready."); _stopped = true; }
            // Other bounded ASCII lines are trusted helper metadata, never a
            // readiness signal and never copied into ordinary player logs.
            return false;
        }
    }

    private static Task<SteamLinkCompositorMonitor.IChild> LaunchAsync()
    {
        // Called only by the off-Unity worker after the complete eligibility gate.
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) throw new PlatformNotSupportedException();
        if (HelperBytes <= 0 || HelperSha256.Length != 64) throw new InvalidDataException("Projection helper pin is not frozen.");
        string stage = "SDK";
        string? sdk = SteamLinkConnection.PrepareVerifiedResource(SteamLinkConnection.ApiResource,
            SteamLinkConnection.ApiBytes, SteamLinkConnection.ApiSha256, "openvr_api.dll", true, ref stage);
        string? helper = SteamLinkConnection.PrepareVerifiedResource(Resource, HelperBytes, HelperSha256,
            "projection-overlay-probe.exe", false, ref stage);
        if (sdk == null || helper == null) throw new InvalidDataException("Verified projection helper unavailable.");
        var start = new ProcessStartInfo(helper, "--stdin-pipe --openvr \"" + sdk + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(helper)!
        };
        start.EnvironmentVariables["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (string name in new[] { "LD_PRELOAD", "LD_AUDIT" }) start.EnvironmentVariables.Remove(name);
        var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new IOException("Projection helper start refused."); }
        catch { process.Dispose(); throw; }
        try { return Task.FromResult<SteamLinkCompositorMonitor.IChild>(new ProcessChild(process)); }
        catch
        {
            bool exited = false;
            try
            {
                try { process.StandardInput.Close(); } catch (IOException) { } catch (InvalidOperationException) { }
                exited = process.WaitForExit(SteamLinkCompositorMonitor.StopGraceMs);
                if (!exited) { process.Kill(); exited = process.WaitForExit(1000); }
            }
            catch { /* Preserve unknown ownership; do not start another helper. */ }
            if (exited) process.Dispose();
            else throw new SteamLinkCompositorMonitor.OwnershipUnknownException();
            throw;
        }
    }
    private sealed class ProcessChild : SteamLinkCompositorMonitor.IChild
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
                exited = _process.WaitForExit(SteamLinkCompositorMonitor.StopGraceMs);
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

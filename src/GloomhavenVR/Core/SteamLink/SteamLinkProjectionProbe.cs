using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink;

// Explicit, temporary transport diagnostic, NOT a production quality fix. Public
// projection overlays have frozen poses: the player must keep their head still.
// Normal MR never starts this helper or allocates this diagnostic's zero input.
internal static class SteamLinkProjectionProbe
{
    internal const string Resource = "GloomhavenVR.SteamLink.projection-overlay-probe.exe";
    internal static readonly int HelperBytes = 210944;
    internal static readonly string HelperSha256 = "e4adb0671abffddf73761a93d5f5dd864c7be1973262be43c82b1204c66ab1cb";
    internal const string ReadyLine = "SFNP_PROJECTION_READY_V2", StoppedLine = "SFNP_PROJECTION_STOPPED_V2";
    internal const int MaximumBytes = 8192, MaximumLine = 1024;
    private static readonly Owner State = new(LaunchAsync, text =>
    {
        if (VRLog.WantsDebug) VRLog.Debug("Core", "Steam Link projection test: " + text);
    });
    private static RenderTexture? _zero;
    private static readonly FrameLatch Frames = new();
    internal static Phase Status => State.Current;
    internal static bool Eligible => VRLog.WantsDebug && VRSession.IsRunning
        && WorldUI.VROptionsTab.ProjectionProbeCheatsEnabled && MixedReality.Enabled.Value
        && SteamLinkSceneRuntime.Requested;

    internal static bool Arm()
    {
        // Recheck the actual gate on stale UI callbacks, not only while building
        // the page. No persisted setting is created by this temporary action.
        if (!Eligible || !WorldUI.VROptionsTab.IsOpen) { Stop(); return false; }
        try
        {
            if (_zero == null)
            {
                var descriptor = new RenderTextureDescriptor(1, 1, GraphicsFormat.R8G8B8A8_UNorm, 0)
                {
                    dimension = TextureDimension.Tex2D, volumeDepth = 1, msaaSamples = 1,
                    useMipMap = false, autoGenerateMips = false, useDynamicScale = false
                };
                _zero = new RenderTexture(descriptor) { hideFlags = HideFlags.HideAndDontSave };
                if (!_zero.Create()) throw new InvalidOperationException("Zero input unavailable.");
                RenderTexture? active = RenderTexture.active;
                bool srgb = GL.sRGBWrite;
                try { RenderTexture.active = _zero; GL.sRGBWrite = false; GL.Clear(false, true, Color.clear); }
                finally { GL.sRGBWrite = srgb; RenderTexture.active = active; }
            }
            return State.Arm(true, true);
        }
        catch (Exception ex) { Stop(); if (VRLog.WantsDebug) VRLog.Debug("Core", "Steam Link projection test unavailable: " + ex.GetType().Name); return false; }
    }

    internal static void Tick()
    {
        State.Tick(Eligible, WorldUI.VROptionsTab.IsOpen);
        RetireZero();
    }
    internal static void Stop() { State.Stop(); RetireZero(); }
    internal static void PackFailed()
    {
        // This eye retries its untouched real source; the remaining eye of this
        // failed diagnostic frame must use that real source too.
        Frames.Reject();
        Stop();
    }
    private static void RetireZero()
    {
        // Never release the input between the two explicitly identified eyes.
        // Destroy is deferred by Unity; no borrowed source is retained/cleared.
        if (_zero != null && !State.OwnsTest && !Frames.IsCurrent(Time.frameCount))
        { UnityEngine.Object.Destroy(_zero); _zero = null; }
    }
    internal static RenderTexture InputForFrame(RenderTexture original, int frame)
    {
        Tick();
        return Frames.Select(original, _zero, frame, State.Current == Phase.Active);
    }

    internal sealed class FrameLatch
    {
        private int _frame = -1;
        private bool _zeroFrame;
        internal bool IsCurrent(int frame) => _frame == frame;
        internal void Reject() => _zeroFrame = false;
        internal RenderTexture Select(RenderTexture original, RenderTexture? zero, int frame, bool active)
        {
            // Worker readiness/permission/menu changes can happen between eyes.
            // Commit one decision per current XR frame; begin/end at its boundary.
            if (_frame != frame)
            {
                _frame = frame;
                _zeroFrame = active && zero != null && zero.IsCreated();
            }
            return _zeroFrame && zero != null ? zero : original;
        }
    }

    internal enum Phase { Idle, PendingClose, Starting, Active, Stopping, Failed, Unsafe }
    internal sealed class Owner
    {
        private readonly object _gate = new();
        private readonly Func<Task<SteamLinkCompositorMonitor.IChild>> _launch;
        private readonly Action<string> _log;
        private Task? _worker;
        private TaskCompletionSource<bool>? _stop;
        private bool _wanted, _unsafe;
        private ulong _epoch;
        private Phase _phase;
        internal Owner(Func<Task<SteamLinkCompositorMonitor.IChild>> launch, Action<string> log) { _launch = launch; _log = log; }
        internal Phase Current { get { lock (_gate) return _phase; } }
        internal bool OwnsTest { get { lock (_gate) return _wanted; } }
        internal Task Settled { get { lock (_gate) return _worker ?? Task.CompletedTask; } }
        internal bool Arm(bool eligible, bool menuOpen)
        {
            lock (_gate)
            {
                if (!eligible || !menuOpen || _unsafe || _wanted || (_worker != null && !_worker.IsCompleted)) return false;
                if (_worker != null && _worker.IsFaulted) { _ = _worker.Exception; _unsafe = true; _phase = Phase.Unsafe; return false; }
                _wanted = true; _epoch++; _phase = Phase.PendingClose;
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
                if (!eligible || (menuOpen && _phase != Phase.PendingClose)) { Stop(); return; }
                // The initiating options window must close ONCE before launch.
                // Its original open state is not a cancellation of the request.
                if (_phase != Phase.PendingClose || menuOpen) return;
                var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _stop = stop; _phase = Phase.Starting;
                ulong epoch = _epoch;
                _worker = Task.Run(() => RunAsync(epoch, stop));
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
                        bool ready = protocol.Accept(input);
                        if (ready) lock (_gate) if (_wanted && _epoch == epoch) { _phase = Phase.Active; _log("both public projections ready; zero base begins at the next complete-eye frame boundary."); }
                    }
                    else
                    {
                        if (value < 32 || line.Length >= MaximumLine - 1) throw new InvalidDataException("Projection line budget/encoding.");
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

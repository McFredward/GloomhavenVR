using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace GloomhavenVR.Core;

/// <summary>Which part of the install is running, for the progress line.</summary>
internal enum SelfUpdatePhase
{
    Idle,
    Preparing,
    Downloading,
    Verifying,
    Extracting,
    Handover,
    Failed,
    Cancelled,
}

/// <summary>
/// Download → verify → stage → hand over → quit.
///
/// <para>THE ORDER IS THE WHOLE DESIGN. A running process cannot overwrite the assemblies it has
/// loaded, so the mod never tries: it puts a fully unpacked, fully verified copy of the new release
/// in <c>BepInEx/GloomhavenVR-update/staged/</c>, writes a batch file that will do the replacement,
/// starts that batch file as a detached child, and quits. The child waits for this process to be
/// gone before it touches a single file, and starts the game again afterwards. See
/// <see cref="SelfUpdateApplyScript"/> for what the child does and why it cannot leave a broken
/// install.</para>
///
/// <para>EVERY BYTE IS WRITTEN INSIDE THE STAGING FOLDER. Nothing in this class writes to
/// <c>BepInEx/plugins</c> or <c>BepInEx/patchers</c>, so an install that is abandoned at any point
/// before the handover — cancelled, failed verification, the user walked out of the main menu — is
/// undone by deleting one folder, and the running install has not been touched at all.</para>
///
/// <para>THE PROGRESS BAR IS HONEST ABOUT ONE THING AND COARSE ABOUT THE REST. The release zip is
/// dominated by <c>gloomhavenvr.bundle</c>, 70,218,494 bytes that barely compress, so the download
/// is essentially the whole wall time; that stretch is byte-accurate against the size GitHub
/// publishes for the asset. Unpacking is counted in ENTRIES, not bytes, and the actual file
/// replacement happens after the game has exited and is not covered by the bar at all — that part
/// takes a second or two and is reported in <c>update.log</c>.</para>
/// </summary>
internal sealed class SelfUpdateInstaller
{
    /// <summary>Abort a download that has not moved for this long. Slow is fine; stopped is not.</summary>
    private const int StallSeconds = 45;

    /// <summary>How the bar is divided. Download dominates because the download dominates.</summary>
    private const float DownloadShare = 0.90f;

    /// <summary>See <see cref="DownloadShare"/>.</summary>
    private const float VerifyShare = 0.03f;

    /// <summary>See <see cref="DownloadShare"/>.</summary>
    private const float ExtractShare = 0.06f;

    /// <summary>Rate limit on the UPDATE INSTALL falsifier: one install attempt per session.</summary>
    private static int _attempts;

    private volatile bool _cancelled;

    /// <summary>Current phase; read by the driver to drive the window.</summary>
    internal SelfUpdatePhase Phase { get; private set; } = SelfUpdatePhase.Idle;

    /// <summary>0..1 across the whole staging job.</summary>
    internal float Progress { get; private set; }

    /// <summary>Bytes on disk so far, and the size the release publishes.</summary>
    internal long DownloadedBytes { get; private set; }

    /// <summary>See <see cref="DownloadedBytes"/>.</summary>
    internal long TotalBytes { get; private set; }

    /// <summary>Which term failed, in the words shown to the user. Empty while nothing has.</summary>
    internal string FailedTerm { get; private set; } = string.Empty;

    /// <summary>True once the applier has been started and the game has been asked to quit.</summary>
    internal bool HandedOver { get; private set; }

    /// <summary>Stop at the next opportunity and remove everything staged. Safe at any point.</summary>
    internal void Cancel() => _cancelled = true;

    /// <summary>
    /// Stage <paramref name="release"/> and hand over. Yields throughout; never throws out of the
    /// iterator; leaves the install untouched on every failure path.
    /// </summary>
    internal IEnumerator Run(SelfUpdateRelease release)
    {
        Phase = SelfUpdatePhase.Preparing;
        Progress = 0f;
        FailedTerm = string.Empty;
        TotalBytes = release.AssetSize;
        DownloadedBytes = 0;
        _attempts++;

        if (!SelfUpdatePaths.ResetStaging(out string stagingError))
        {
            Fail($"the update folder could not be prepared — {stagingError}", release, 0, 0);
            yield break;
        }

        // ---- download ------------------------------------------------------------------------
        Phase = SelfUpdatePhase.Downloading;
        UnityWebRequest? request = CreateDownload(release);
        if (request == null)
        {
            Fail("the download could not be started", release, 0, 0);
            yield break;
        }

        UnityWebRequestAsyncOperation? operation = Send(request);
        if (operation == null)
        {
            Dispose(request);
            Fail("the download would not start", release, 0, 0);
            yield break;
        }

        long lastSeen = -1;
        float lastMovedAt = Time.realtimeSinceStartup;
        while (!operation.isDone)
        {
            if (_cancelled)
            {
                Abort(request);
                Dispose(request);
                Cleanup();
                Phase = SelfUpdatePhase.Cancelled;
                yield break;
            }

            long got = Downloaded(request);
            DownloadedBytes = got;
            Progress = TotalBytes > 0
                ? Mathf.Clamp01((float)((double)got / TotalBytes)) * DownloadShare
                : 0f;

            if (got != lastSeen)
            {
                lastSeen = got;
                lastMovedAt = Time.realtimeSinceStartup;
            }
            else if (Time.realtimeSinceStartup - lastMovedAt > StallSeconds)
            {
                Abort(request);
                Dispose(request);
                Cleanup();
                Fail($"the download stopped moving for {StallSeconds} seconds", release, got, 0);
                yield break;
            }

            yield return null;
        }

        bool downloadOk = DownloadSucceeded(request, out string downloadError);
        DownloadedBytes = Downloaded(request);
        Dispose(request);
        if (!downloadOk)
        {
            Cleanup();
            Fail($"the download did not complete — {downloadError}", release, DownloadedBytes, 0);
            yield break;
        }

        if (!Promote(out string promoteError))
        {
            Cleanup();
            Fail($"the downloaded file could not be finalised — {promoteError}", release,
                DownloadedBytes, 0);
            yield break;
        }

        // ---- verify + unpack, on a worker so the frame keeps going ------------------------------
        Phase = SelfUpdatePhase.Verifying;
        Progress = DownloadShare;
        var worker = new StageWorker(release.AssetSize);
        if (!worker.Start())
        {
            Cleanup();
            Fail("the archive check could not be started", release, DownloadedBytes, 0);
            yield break;
        }

        while (!worker.Done)
        {
            if (_cancelled)
            {
                // The worker only ever writes inside the staging folder, so letting it finish and
                // then deleting that folder is both correct and simpler than interrupting a thread
                // in the middle of a file write.
                worker.RequestStop();
                while (!worker.Done)
                    yield return null;
                Cleanup();
                Phase = SelfUpdatePhase.Cancelled;
                yield break;
            }

            if (worker.Verified)
            {
                Phase = SelfUpdatePhase.Extracting;
                int total = Math.Max(1, worker.EntryCount);
                Progress = DownloadShare + VerifyShare
                    + Mathf.Clamp01(worker.ExtractedEntries / (float)total) * ExtractShare;
            }
            yield return null;
        }

        if (!worker.Ok)
        {
            Cleanup();
            Fail(worker.Error, release, DownloadedBytes, worker.EntryCount);
            yield break;
        }

        Progress = DownloadShare + VerifyShare + ExtractShare;

        // ---- hand over --------------------------------------------------------------------------
        Phase = SelfUpdatePhase.Handover;
        if (!WriteHandover(release, out string handoverError))
        {
            Cleanup();
            Fail(handoverError, release, DownloadedBytes, worker.EntryCount);
            yield break;
        }

        if (!StartApplier(out string startError))
        {
            Cleanup();
            Fail($"the installer could not be started — {startError}", release, DownloadedBytes,
                worker.EntryCount);
            yield break;
        }

        Progress = 1f;
        HandedOver = true;
        VRLog.Note("SelfUpdate",
            $"UPDATE INSTALL: staged={SelfUpdatePaths.StagedTree} entries={worker.EntryCount} "
            + $"bytes={DownloadedBytes.ToString(CultureInfo.InvariantCulture)} "
            + $"published={release.AssetSize.ToString(CultureInfo.InvariantCulture)} "
            + $"verification=ok copied=nothing yet — the applier replaces "
            + $"BepInEx/plugins/GloomhavenVR and BepInEx/patchers/GloomhavenVR once this process is "
            + $"gone, after backing both up to {SelfUpdatePaths.BackupRoot} "
            + $"relaunch=yes attempt={_attempts.ToString(CultureInfo.InvariantCulture)} "
            + $"target={release.Version}");

        Quit();
    }

    // ---- the pieces that may throw ---------------------------------------------------------------

    private static UnityWebRequest? CreateDownload(SelfUpdateRelease release)
    {
        try
        {
            var request = new UnityWebRequest(release.AssetUrl, UnityWebRequest.kHttpVerbGET);
            var handler = new DownloadHandlerFile(SelfUpdatePaths.PartialZip)
            {
                removeFileOnAbort = true,
            };
            request.downloadHandler = handler;
            // No overall timeout: a 70 MB download on a slow line is not a fault. The stall
            // detector in the loop is what ends a download that has actually stopped.
            request.timeout = 0;
            request.SetRequestHeader("User-Agent", SelfUpdateCheck.UserAgent());
            request.SetRequestHeader("Accept", "application/octet-stream");
            return request;
        }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"UPDATE INSTALL: download could not be built — {e.Message}");
            return null;
        }
    }

    private static UnityWebRequestAsyncOperation? Send(UnityWebRequest request)
    {
        try { return request.SendWebRequest(); }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"UPDATE INSTALL: download would not start — {e.Message}");
            return null;
        }
    }

    private static long Downloaded(UnityWebRequest request)
    {
        try { return (long)request.downloadedBytes; }
        catch (Exception) { return 0L; }
    }

    private static bool DownloadSucceeded(UnityWebRequest request, out string error)
    {
        try
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                error = string.Empty;
                return true;
            }
            error = request.responseCode > 0
                ? $"HTTP {request.responseCode.ToString(CultureInfo.InvariantCulture)}"
                : (request.error ?? "no answer");
            return false;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static void Abort(UnityWebRequest request)
    {
        try { request.Abort(); }
        catch (Exception) { /* aborting an already-dead request is not a fault */ }
    }

    private static void Dispose(UnityWebRequest request)
    {
        try { request.Dispose(); }
        catch (Exception) { /* see Abort */ }
    }

    /// <summary>Rename the .part file to its final name. Nothing is ever verified under .part.</summary>
    private static bool Promote(out string error)
    {
        error = string.Empty;
        try
        {
            if (File.Exists(SelfUpdatePaths.DownloadZip))
                File.Delete(SelfUpdatePaths.DownloadZip);
            File.Move(SelfUpdatePaths.PartialZip, SelfUpdatePaths.DownloadZip);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Write the marker the next start reads and the batch file that does the replacement. Both
    /// live in the staging folder; neither touches the install.
    /// </summary>
    private static bool WriteHandover(SelfUpdateRelease release, out string error)
    {
        error = string.Empty;
        try
        {
            int pid;
            string executableName;
            try
            {
                using Process self = Process.GetCurrentProcess();
                pid = self.Id;
                executableName = Path.GetFileName(self.MainModule?.FileName ?? string.Empty);
            }
            catch (Exception)
            {
                pid = 0;
                executableName = string.Empty;
            }
            if (executableName.Length == 0)
                executableName = BepInEx.Paths.ProcessName + ".exe";

            string? steamGameId = null;
            try { steamGameId = Environment.GetEnvironmentVariable("SteamGameId"); }
            catch (Exception) { /* a denied environment read is simply "not Steam" */ }

            string[] argv;
            try { argv = Environment.GetCommandLineArgs(); }
            catch (Exception) { argv = Array.Empty<string>(); }

            if (!SelfUpdateApplyScript.TryBuild(pid, executableName, steamGameId, argv,
                    release.Version, out string script, out string refusal,
                    out string? droppedArgument))
            {
                error = refusal;
                return false;
            }

            if (droppedArgument != null)
            {
                VRLog.Warn("SelfUpdate", "UPDATE INSTALL: a launch option contains a character the "
                    + $"relauncher cannot pass on safely — '{droppedArgument}'. The game will be "
                    + "restarted WITHOUT any launch options; add them again by hand if it needs them.");
            }

            var ascii = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(SelfUpdatePaths.ApplyScript, script, ascii);
            File.WriteAllText(SelfUpdatePaths.PendingMarker, release.Version, ascii);
            return true;
        }
        catch (Exception e)
        {
            error = $"the installer script could not be written — {e.Message}";
            return false;
        }
    }

    /// <summary>
    /// Start the applier as a detached child. It outlives this process by design — that is the
    /// only way a file this process holds open can ever be replaced.
    /// </summary>
    private static bool StartApplier(out string error)
    {
        error = string.Empty;
        try
        {
            var starter = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + SelfUpdatePaths.ApplyScript + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = SelfUpdatePaths.StagingRoot,
            };
            Process? child = Process.Start(starter);
            if (child == null)
            {
                error = "no process was created";
                return false;
            }
            child.Dispose();
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static void Quit()
    {
        try { Application.Quit(); }
        catch (Exception e)
        {
            VRLog.Warn("SelfUpdate", $"UPDATE INSTALL: Application.Quit threw — {e.Message}. The "
                + "installer is waiting; closing the game by hand finishes the update.");
        }
    }

    /// <summary>Remove everything staged. Called on every abandoned path, never after handover.</summary>
    private static void Cleanup()
    {
        try
        {
            if (Directory.Exists(SelfUpdatePaths.StagingRoot))
                Directory.Delete(SelfUpdatePaths.StagingRoot, recursive: true);
        }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"the staging folder could not be removed — {e.Message}");
        }
    }

    private void Fail(string term, SelfUpdateRelease release, long bytes, int entries)
    {
        Phase = SelfUpdatePhase.Failed;
        FailedTerm = term;
        VRLog.Warn("SelfUpdate",
            $"UPDATE INSTALL: staged={SelfUpdatePaths.StagedTree} entries={entries.ToString(CultureInfo.InvariantCulture)} "
            + $"bytes={bytes.ToString(CultureInfo.InvariantCulture)} "
            + $"published={release.AssetSize.ToString(CultureInfo.InvariantCulture)} "
            + $"verification=FAILED term={term} copied=nothing relaunch=no "
            + $"attempt={_attempts.ToString(CultureInfo.InvariantCulture)} target={release.Version} — "
            + "the installed version is untouched.");
    }

    /// <summary>
    /// Verification and unpacking on a background thread. Nothing in here touches a Unity API, so
    /// the thread is safe; the coroutine polls the volatile fields and drives the bar from them.
    /// </summary>
    private sealed class StageWorker
    {
        private readonly long _expectedBytes;
        private readonly int[] _progress = new int[1];
        private volatile bool _stop;

        internal StageWorker(long expectedBytes) => _expectedBytes = expectedBytes;

        internal volatile bool Done;
        internal volatile bool Ok;
        internal volatile bool Verified;
        internal string Error = string.Empty;
        internal int EntryCount;

        /// <summary>Entries written so far — read from the main thread while this runs.</summary>
        internal int ExtractedEntries => _progress[0];

        internal void RequestStop() => _stop = true;

        internal bool Start()
        {
            try
            {
                var thread = new Thread(Work) { IsBackground = true, Name = "GloomhavenVR.Stage" };
                thread.Start();
                return true;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Done = true;
                return false;
            }
        }

        private void Work()
        {
            try
            {
                SelfUpdateZip.Verdict verdict =
                    SelfUpdateZip.Verify(SelfUpdatePaths.DownloadZip, _expectedBytes);
                EntryCount = verdict.EntryCount;
                if (!verdict.Ok)
                {
                    Error = verdict.FailedTerm;
                    return;
                }
                Verified = true;
                if (_stop)
                {
                    Error = "cancelled";
                    return;
                }

                if (!SelfUpdateZip.Extract(SelfUpdatePaths.DownloadZip, SelfUpdatePaths.StagedTree,
                        _progress, out string extractError))
                {
                    Error = $"the archive could not be unpacked — {extractError}";
                    return;
                }

                // The unpack claimed success; prove it on disk before anything is handed over.
                foreach (string required in SelfUpdateZip.RequiredEntries)
                {
                    string path = Path.Combine(SelfUpdatePaths.StagedTree,
                        required.Replace('/', Path.DirectorySeparatorChar));
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length == 0)
                    {
                        Error = $"'{required}' is not on disk after unpacking";
                        return;
                    }
                }

                Ok = true;
            }
            catch (Exception e)
            {
                Error = $"the archive could not be processed — {e.Message}";
            }
            finally
            {
                Done = true;
            }
        }
    }
}

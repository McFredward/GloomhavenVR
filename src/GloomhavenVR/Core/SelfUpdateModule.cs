using System;
using System.Collections;
using System.Globalization;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// The self-update feature: one question to GitHub when the main menu comes up, one window if the
/// answer is "there is a newer release", and nothing whatsoever otherwise.
///
/// <para>REGISTRATION. This is an <see cref="IVRModule"/> like every other feature, but the list it
/// belongs in lives in <c>Plugin.RegisterModules</c>, which is not this lane's file. Until the
/// single line <c>_modules.Add(new Core.SelfUpdateModule());</c> is added there, nothing in this
/// feature runs — see <c>.planning/debug/laneO-out-of-lane.diff</c>.</para>
///
/// <para>NOT BLOCKING, ANYWHERE. The check is a coroutine over an asynchronous
/// <c>UnityWebRequest</c> with a ten-second timeout and no retry. Verification and unpacking happen
/// on a worker thread. There is no <c>.Result</c>, no <c>.Wait()</c>, and no path on which a frame
/// waits for the network. If GitHub is down, blocked, rate-limited, or the machine is simply
/// offline, the outcome is one line in the log and no window at all.</para>
///
/// <para>NO MULTIPLAYER SURFACE. Nothing here goes on the wire, nothing here is synchronised, and
/// the check only runs while the main menu exists — never in a lobby and never in a scenario. Two
/// clients on different builds are the business of <c>Net/VersionGuard</c>, which is untouched.</para>
/// </summary>
internal sealed class SelfUpdateModule : IVRModule
{
    public string Name => "SelfUpdate";

    private GameObject? _host;

    public void Init()
    {
        try
        {
            SelfUpdateConfig.Bind();

            // Deal with anything a previous attempt left behind, on a worker thread: a confirmed
            // update leaves roughly 140 MB to delete and the main thread is drawing frames.
            SelfUpdatePaths.SweepLeftoversAsync();

            _host = new GameObject("GloomhavenVR.SelfUpdate");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<SelfUpdateDriver>();
        }
        catch (Exception e)
        {
            // A courtesy feature must never be able to take the mod down with it.
            VRLog.Error("SelfUpdate", $"init failed, the update check is off for this session: {e}");
        }
    }

    public void Shutdown()
    {
        try
        {
            if (_host != null)
                UnityEngine.Object.Destroy(_host);
        }
        catch (Exception e)
        {
            VRLog.Error("SelfUpdate", $"shutdown threw: {e}");
        }
        finally
        {
            _host = null;
        }
    }
}

/// <summary>
/// The one per-frame entry point this feature has.
///
/// <para>ITS WHOLE BODY IS INSIDE A CATCH, and a repeated throw retires it permanently. An
/// unguarded <c>Update</c> that throws in this mod starves VR input — the exception unwinds before
/// the input pump downstream of it ever runs — so a feature that has nothing to do with input must
/// not be able to cause that. Retiring rather than throwing every frame also keeps it out of the
/// log at 90 Hz.</para>
/// </summary>
internal sealed class SelfUpdateDriver : MonoBehaviour
{
    /// <summary>How often the cheap "is the main menu up" poll runs. This is not a per-frame job.</summary>
    private const float PollSeconds = 0.5f;

    /// <summary>Consecutive throws after which this component takes itself out of the frame.</summary>
    private const int ThrowBudget = 5;

    /// <summary>Seconds to wait for the game to close itself before saying so in the window.</summary>
    private const float QuitPatienceSeconds = 20f;

    /// <summary>How often the progress LINE may be rewritten. See <c>DriveInstall</c>.</summary>
    private const float StatusSeconds = 0.25f;

    private enum State
    {
        /// <summary>Waiting for the main menu.</summary>
        Waiting,

        /// <summary>The GitHub question is in flight.</summary>
        Checking,

        /// <summary>The window is up with "Ignorieren" and "Updaten".</summary>
        Offering,

        /// <summary>Downloading and staging.</summary>
        Installing,

        /// <summary>The applier has been started and the game has been asked to quit.</summary>
        Quitting,

        /// <summary>Nothing more will happen this session unless the debug dial is flipped.</summary>
        Finished,
    }

    private readonly SelfUpdateDialog _dialog = new();
    private SelfUpdateCheck? _check;
    private SelfUpdateInstaller? _installer;

    private State _state = State.Waiting;
    private float _nextPoll;
    private int _throws;
    private bool _lastDialValue;
    private bool _lastVrRunning;
    private float _quitAskedAt;
    private float _nextStatusAt;
    private string _lastStatus = string.Empty;

    private void Awake()
    {
        _lastDialValue = SelfUpdateConfig.UpdateCheckOnDevBuilds?.Value
            ?? SelfUpdateConfig.UpdateCheckOnDevBuildsDefault;
        _lastVrRunning = VRSession.IsRunning;
    }

    private void OnDestroy()
    {
        try
        {
            _installer?.Cancel();
            _dialog.Close();
        }
        catch (Exception)
        {
            // Teardown. There is nobody left to tell.
        }
    }

    private void Update()
    {
        try
        {
            Step();
            _throws = 0;
        }
        catch (Exception e)
        {
            _throws++;
            VRLog.Error("SelfUpdate", $"update check driver threw ({_throws} in a row): {e}");
            if (_throws < ThrowBudget)
                return;
            VRLog.Error("SelfUpdate", $"retiring the update check after {ThrowBudget} throws in a "
                + "row. Nothing else in the mod is affected; restart the game to try again.");
            try { _dialog.Close(); }
            catch (Exception) { /* nothing left to do about it */ }
            enabled = false;
        }
    }

    private void Step()
    {
        // The window is the only thing that needs every frame: it keeps itself in the view cone.
        if (_dialog.IsShowing)
            _dialog.Tick();

        if (_state == State.Installing)
            DriveInstall();
        else if (_state == State.Quitting)
            DriveQuitting();

        if (Time.unscaledTime < _nextPoll)
            return;
        _nextPoll = Time.unscaledTime + PollSeconds;

        WatchReArmEdges();

        // Everything this feature does belongs to the main menu. If the menu is gone -- the player
        // started a scenario, opened the campaign -- the window goes with it, a running download is
        // abandoned and its staging folder removed. That is also what keeps the update out of a
        // multiplayer session: there is no path on which it can quit a game that has one.
        bool menu = MainMenuIsUp();
        if (!menu)
        {
            // State.Quitting is exempt: the applier has already been started and Application.Quit
            // is in flight, so the menu unloading there is the update WORKING, not being left.
            if (_state != State.Waiting && _state != State.Finished && _state != State.Quitting)
            {
                Abandon("the main menu was left");
                return;
            }
            // A finished window (a failure notice the user has not closed) does not follow the
            // player into a scenario.
            if (_state == State.Finished && _dialog.IsShowing)
                _dialog.Close();
            return;
        }

        if (_state == State.Waiting && menu)
            StartCheck();
    }

    // ---- states -----------------------------------------------------------------------------

    private void StartCheck()
    {
        _check = new SelfUpdateCheck();
        _state = State.Checking;
        StartCoroutine(RunCheck(_check));
    }

    private IEnumerator RunCheck(SelfUpdateCheck check)
    {
        yield return check.Run();

        if (!check.UpdateAvailable || check.Release == null)
        {
            _state = State.Finished;
            yield break;
        }

        if (!MainMenuIsUp())
        {
            _state = State.Finished;
            yield break;
        }

        SelfUpdateRelease release = check.Release;
        string body = string.Format(
            CultureInfo.InvariantCulture,
            SelfUpdateText.T("upd_body"),
            BuildInfo.Version,
            release.Version,
            Megabytes(release.AssetSize));

        _dialog.ShowChoice(
            SelfUpdateText.T("upd_title"),
            body,
            SelfUpdateText.T("upd_ignore"),
            SelfUpdateText.T("upd_update"),
            onIgnore: () =>
            {
                VRLog.Info("SelfUpdate", $"UPDATE CHECK: the user chose to ignore {release.Version} "
                    + "for this session.");
                _dialog.Close();
                _state = State.Finished;
            },
            onUpdate: () => BeginInstall(release));
        _state = State.Offering;
    }

    private void BeginInstall(SelfUpdateRelease release)
    {
        _installer = new SelfUpdateInstaller();
        _lastStatus = string.Empty;   // a second attempt must not inherit the first one's last line
        _nextStatusAt = 0f;
        _dialog.ShowProgress(
            string.Format(CultureInfo.InvariantCulture, SelfUpdateText.T("upd_working"),
                release.Version),
            SelfUpdateText.T("upd_cancel"),
            onCancel: () =>
            {
                _installer?.Cancel();
                _dialog.Close();
                _state = State.Finished;
            });
        _state = State.Installing;
        StartCoroutine(_installer.Run(release));
    }

    private void DriveInstall()
    {
        SelfUpdateInstaller? installer = _installer;
        if (installer == null)
        {
            _state = State.Finished;
            return;
        }

        _dialog.SetProgress(installer.Progress);

        // The bar is a rect write and can run every frame; the status LINE is TMP text, and
        // assigning it forces a mesh rebuild. At 90 Hz over a several-minute download that is
        // thousands of rebuilds and thousands of formatted strings for a readout the eye cannot
        // follow, so it moves at 4 Hz and only when it actually says something different.
        if (Time.unscaledTime >= _nextStatusAt)
        {
            _nextStatusAt = Time.unscaledTime + StatusSeconds;
            string status = StatusLine(installer);
            if (!string.Equals(status, _lastStatus, StringComparison.Ordinal))
            {
                _lastStatus = status;
                _dialog.SetStatus(status);
            }
        }

        switch (installer.Phase)
        {
            case SelfUpdatePhase.Failed:
                _dialog.ShowDone(
                    string.Format(CultureInfo.InvariantCulture, SelfUpdateText.T("upd_failed"),
                        installer.FailedTerm),
                    SelfUpdateText.T("upd_close"),
                    onClose: () =>
                    {
                        _dialog.Close();
                        _state = State.Finished;
                    });
                _state = State.Finished;
                break;

            case SelfUpdatePhase.Cancelled:
                _dialog.Close();
                _state = State.Finished;
                break;

            default:
                if (installer.HandedOver)
                {
                    _quitAskedAt = Time.unscaledTime;
                    _state = State.Quitting;
                }
                break;
        }
    }

    private void DriveQuitting()
    {
        _dialog.SetProgress(1f);
        string status = SelfUpdateText.T("upd_restarting");
        if (!string.Equals(status, _lastStatus, StringComparison.Ordinal))
        {
            _lastStatus = status;   // TMP rebuild guard — see DriveInstall
            _dialog.SetStatus(status);
        }
        if (Time.unscaledTime - _quitAskedAt < QuitPatienceSeconds)
            return;

        // The game is still here. The applier is waiting for this process and will do its work the
        // moment it is gone, so the honest thing is to say so rather than to keep a bar spinning.
        _dialog.ShowDone(SelfUpdateText.T("upd_still_running"), SelfUpdateText.T("upd_close"),
            onClose: () => _dialog.Close());
        _state = State.Finished;
    }

    private void Abandon(string why)
    {
        VRLog.Info("SelfUpdate", $"UPDATE INSTALL: abandoned — {why}. Nothing was installed and the "
            + "staging folder has been removed.");
        _installer?.Cancel();
        _installer = null;
        _dialog.Close();
        _state = State.Finished;
    }

    // ---- re-arming ---------------------------------------------------------------------------

    /// <summary>
    /// Two rising edges put the feature back in <see cref="State.Waiting"/> so it asks again:
    /// the debug dial being switched on (which is the whole point of the dial — he switches it on
    /// while standing in the main menu and wants the window, not a restart), and VR coming up after
    /// the menu did. Neither can start anything while a window is open or an install is running.
    /// </summary>
    private void WatchReArmEdges()
    {
        bool dial = SelfUpdateConfig.UpdateCheckOnDevBuilds?.Value
            ?? SelfUpdateConfig.UpdateCheckOnDevBuildsDefault;
        bool vr = VRSession.IsRunning;

        bool rearm = (dial && !_lastDialValue) || (vr && !_lastVrRunning);
        _lastDialValue = dial;
        _lastVrRunning = vr;

        if (rearm && _state == State.Finished && !_dialog.IsShowing)
            _state = State.Waiting;
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static bool MainMenuIsUp()
    {
        try
        {
            return GLOOM.MainMenu.MainMenuUIManager.Instance != null;
        }
        catch (Exception)
        {
            // The type is not loaded yet during boot; that simply means "no menu".
            return false;
        }
    }

    private static string StatusLine(SelfUpdateInstaller installer) => installer.Phase switch
    {
        SelfUpdatePhase.Downloading => string.Format(CultureInfo.InvariantCulture,
            SelfUpdateText.T("upd_downloading"),
            Megabytes(installer.DownloadedBytes), Megabytes(installer.TotalBytes)),
        SelfUpdatePhase.Verifying => SelfUpdateText.T("upd_verifying"),
        SelfUpdatePhase.Extracting => SelfUpdateText.T("upd_extracting"),
        SelfUpdatePhase.Handover => SelfUpdateText.T("upd_restarting"),
        _ => string.Empty,
    };

    private static string Megabytes(long bytes) =>
        (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
}

using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using UnityEngine.Networking;

namespace GloomhavenVR.Core;

/// <summary>How the one question to GitHub ended.</summary>
internal enum SelfUpdateOutcome
{
    /// <summary>Never asked — dev build without the debug dial.</summary>
    Disabled,

    /// <summary>No answer at all: offline, DNS, TLS, timeout. THE RESTING STATE, not a fault.</summary>
    Unreachable,

    /// <summary>GitHub answered 403/429. Also not a fault; the unauthenticated quota is shared.</summary>
    RateLimited,

    /// <summary>An HTTP status that is neither success nor rate limit.</summary>
    HttpError,

    /// <summary>Answered, but the body is not the shape a release answer has.</summary>
    Malformed,

    /// <summary>Answered and understood.</summary>
    Answered,
}

/// <summary>
/// The courtesy question: "is there a newer release than the one running?" — asked once, with a
/// short timeout, from a coroutine, and never asked again if it fails.
///
/// <para>NOTHING HERE BLOCKS. <see cref="UnityWebRequest"/> is asynchronous by construction: the
/// coroutine yields until the operation reports done and the main thread keeps rendering. There is
/// no <c>.Result</c>, no <c>.Wait()</c>, no synchronous <c>WebClient</c>, and no retry — a courtesy
/// check that retries is a service, and a service is something that can stall a main menu.</para>
///
/// <para>WHY <see cref="UnityWebRequest"/> AND NOT <c>HttpWebRequest</c>. Mono's TLS stack in a
/// Unity player has no usable root certificate store on many machines, and the usual mod workaround
/// — <c>ServicePointManager.ServerCertificateValidationCallback = accept everything</c> — would
/// mean this feature downloads and installs code over a connection it has stopped authenticating.
/// UnityWebRequest goes through the platform's own HTTP stack, which validates against the OS
/// certificate store. TLS to github.com is the ONLY trust anchor this feature has; it does not get
/// to be optional.</para>
///
/// <para>THE FALSIFIER. Exactly one <c>UPDATE CHECK:</c> line per attempt, and attempts are capped
/// at <see cref="MaxAttemptsPerSession"/> per process. It names this build and its dev flag,
/// whether the check was allowed to run and why, the endpoint, the outcome, the version found, the
/// verdict and the elapsed time — so "no window appeared" always has a written reason.</para>
/// </summary>
internal sealed class SelfUpdateCheck
{
    /// <summary>Repository the releases are published from.</summary>
    internal const string Owner = "McFredward";

    /// <summary>See <see cref="Owner"/>.</summary>
    internal const string Repository = "GloomhavenVR";

    /// <summary>The public, unauthenticated releases endpoint. No token is embedded anywhere.</summary>
    internal const string Endpoint =
        "https://api.github.com/repos/" + Owner + "/" + Repository + "/releases/latest";

    /// <summary>Human page the user is pointed at when an install fails.</summary>
    internal const string ReleasesPage =
        "https://github.com/" + Owner + "/" + Repository + "/releases";

    /// <summary>Seconds before the whole request is abandoned. A courtesy check, not a service.</summary>
    internal const int TimeoutSeconds = 10;

    /// <summary>Rate limit on the falsifier line AND on the work behind it.</summary>
    internal const int MaxAttemptsPerSession = 3;

    private static int _attempts;

    /// <summary>How the last attempt ended.</summary>
    internal SelfUpdateOutcome Outcome { get; private set; } = SelfUpdateOutcome.Disabled;

    /// <summary>The release, when <see cref="Outcome"/> is <see cref="SelfUpdateOutcome.Answered"/>.</summary>
    internal SelfUpdateRelease? Release { get; private set; }

    /// <summary>True only when a strictly newer version was published. The whole point.</summary>
    internal bool UpdateAvailable { get; private set; }

    /// <summary>True when the attempt was refused before any network access.</summary>
    internal bool WasCapped { get; private set; }

    /// <summary>
    /// Run the check. Yields; never throws out of the iterator; writes exactly one falsifier line.
    /// </summary>
    internal IEnumerator Run()
    {
        Outcome = SelfUpdateOutcome.Disabled;
        Release = null;
        UpdateAvailable = false;
        WasCapped = false;

        var clock = Stopwatch.StartNew();

        if (!SelfUpdateConfig.IsCheckEnabled(out string reason))
        {
            Report(clock, reason, enabled: false, latest: "-", verdict: "not asked");
            yield break;
        }

        if (_attempts >= MaxAttemptsPerSession)
        {
            WasCapped = true;
            VRLog.Info("SelfUpdate", $"UPDATE CHECK: build={BuildInfo.Display} dev={IsDev()} "
                + $"enabled=yes ({reason}) endpoint={Endpoint} outcome=capped "
                + $"latest=- verdict=already asked {MaxAttemptsPerSession} times this session "
                + "elapsed=0ms");
            yield break;
        }
        _attempts++;

        UnityWebRequest? request = Create();
        if (request == null)
        {
            Outcome = SelfUpdateOutcome.Unreachable;
            Report(clock, reason, enabled: true, latest: "-", verdict: "no request could be made");
            yield break;
        }

        // No try/catch may wrap a yield in a C# iterator, so every step that can throw is a helper
        // that returns a flag instead.
        UnityWebRequestAsyncOperation? operation = Send(request);
        if (operation == null)
        {
            Outcome = SelfUpdateOutcome.Unreachable;
            Dispose(request);
            Report(clock, reason, enabled: true, latest: "-", verdict: "the request would not start");
            yield break;
        }

        while (!operation.isDone)
            yield return null;

        string body = Harvest(request, out SelfUpdateOutcome outcome, out long status);
        Outcome = outcome;
        Dispose(request);

        if (outcome != SelfUpdateOutcome.Answered)
        {
            Report(clock, reason, enabled: true, latest: "-",
                verdict: outcome == SelfUpdateOutcome.HttpError
                    ? $"HTTP {status.ToString(CultureInfo.InvariantCulture)}"
                    : "no usable answer");
            yield break;
        }

        if (!SelfUpdateRelease.TryParse(body, out SelfUpdateRelease release, out string failedTerm))
        {
            Outcome = SelfUpdateOutcome.Malformed;
            Report(clock, reason, enabled: true, latest: "-", verdict: failedTerm);
            yield break;
        }

        Release = release;
        int comparison = SelfUpdateRelease.Compare(BuildInfo.Version, release.Version);
        string verdict;
        if (comparison == int.MinValue)
        {
            Outcome = SelfUpdateOutcome.Malformed;
            verdict = $"cannot compare '{BuildInfo.Version}' with '{release.Version}'";
        }
        else if (comparison < 0)
        {
            UpdateAvailable = true;
            verdict = "newer release available";
        }
        else if (comparison == 0)
        {
            verdict = "up to date";
        }
        else
        {
            verdict = "this build is ahead of the newest release";
        }

        Report(clock, reason, enabled: true, latest: release.Version, verdict: verdict);
    }

    // ---- the pieces that may throw, kept out of the iterator ----------------------------------

    private static UnityWebRequest? Create()
    {
        try
        {
            UnityWebRequest request = UnityWebRequest.Get(Endpoint);
            request.timeout = TimeoutSeconds;
            // GitHub requires a User-Agent and answers 403 without one. No token, ever: an
            // unauthenticated request cannot leak a credential, and the shared quota is plenty
            // for one question per game start.
            request.SetRequestHeader("User-Agent", UserAgent());
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            return request;
        }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"UPDATE CHECK: the request could not be built — {e.Message}");
            return null;
        }
    }

    /// <summary>Identifies the mod and its version, which is what a UA is for.</summary>
    internal static string UserAgent() =>
        $"GloomhavenVR/{BuildInfo.Version} (+https://github.com/{Owner}/{Repository})";

    private static UnityWebRequestAsyncOperation? Send(UnityWebRequest request)
    {
        try
        {
            return request.SendWebRequest();
        }
        catch (Exception e)
        {
            VRLog.Info("SelfUpdate", $"UPDATE CHECK: the request would not start — {e.Message}");
            return null;
        }
    }

    private static string Harvest(UnityWebRequest request, out SelfUpdateOutcome outcome,
        out long status)
    {
        outcome = SelfUpdateOutcome.Unreachable;
        status = 0;
        try
        {
            status = request.responseCode;
            if (status == 403 || status == 429)
            {
                outcome = SelfUpdateOutcome.RateLimited;
                return string.Empty;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                outcome = status >= 400
                    ? SelfUpdateOutcome.HttpError
                    : SelfUpdateOutcome.Unreachable;
                return string.Empty;
            }
            string text = request.downloadHandler?.text ?? string.Empty;
            if (text.Length == 0)
            {
                outcome = SelfUpdateOutcome.Malformed;
                return string.Empty;
            }
            outcome = SelfUpdateOutcome.Answered;
            return text;
        }
        catch (Exception)
        {
            outcome = SelfUpdateOutcome.Unreachable;
            return string.Empty;
        }
    }

    private static void Dispose(UnityWebRequest request)
    {
        try { request.Dispose(); }
        catch (Exception) { /* a disposed request that will not dispose is not a user-facing fault */ }
    }

    private static string IsDev() => SelfUpdateConfig.IsDevBuild ? "yes" : "no";

    /// <summary>
    /// The one falsifier line.
    ///
    /// <para>IT IS NEVER A WARNING. Being offline is the resting state of a machine playing a
    /// single-player game, and a warning for it would cry wolf on every start of every offline
    /// session. The whole line is Info; a genuine fault shows as the named term in <c>verdict=</c>,
    /// and the default log level in this mod is Trace, so it is in the log either way.</para>
    /// </summary>
    private void Report(Stopwatch clock, string reason, bool enabled, string latest, string verdict)
    {
        clock.Stop();
        VRLog.Info("SelfUpdate",
            $"UPDATE CHECK: build={BuildInfo.Display} version={BuildInfo.Version} dev={IsDev()} "
            + $"enabled={(enabled ? "yes" : "no")} ({reason}) endpoint={Endpoint} "
            + $"outcome={Outcome} latest={latest} verdict={verdict} "
            + $"elapsed={clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)}ms");
    }
}

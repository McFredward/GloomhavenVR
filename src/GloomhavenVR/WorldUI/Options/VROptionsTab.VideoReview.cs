using GloomhavenVR.Core;
using TMPro;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The maintainer approved the stereo intro on 2026-10-17 and requested individual movie
/// triggers usable inside a scenario. The runtime owns the decoder and the normal shared
/// video window; this page only requests a canonical movie and shows its current result.
/// A press never rebuilds the pane or steals another native menu entry's selection.
/// </summary>
internal static partial class VROptionsTab
{
    private static TMP_Text? _videoReviewStatus;
    private static string _videoReviewCaption = string.Empty;
    private static bool _videoReviewSnapshot;
    private static NativeVideoReviewStatus _videoReviewState;
    private static NativeVideoReviewFailure _videoReviewFailure;
    private static string _videoReviewKey = string.Empty, _videoReviewLanguage = string.Empty;

    private static int BuildVideoReviewTriggers()
    {
        if (ContentRoot == null)
            return 0;

        BuildHeader(ContentRoot, Loc.VideoReviewHeading, sub: true);
        BuildNote(ContentRoot, Loc.VideoReviewHint, multiline: true);
        // Reserve space for localized refusal feedback before the first press.
        // A longer asynchronous result must not move the next button under the pointer.
        var status = BuildNote(ContentRoot, VideoReviewCaption(), multiline: true, minLines: 3);
        _videoReviewStatus = status.GetComponentInChildren<TMP_Text>(true);
        _videoReviewCaption = string.Empty;
        _videoReviewSnapshot = false;

        int rows = 0;
        for (int i = 0; i < NativeVideoReview.Count; i++)
        {
            // Capture the exact key, not the loop index. Every button must request
            // its own film even after another action updates the live status.
            string key = NativeVideoReview.GetKey(i);
            BuildLinkRow(ContentRoot, Loc.VideoReviewPlay(key), () =>
            {
                NativeVideoReview.TryStart(key);
                RefreshVideoReviewRows();
            }, asAction: true);
            rows++;
        }

        BuildLinkRow(ContentRoot, Loc.VideoReviewClose, () =>
        {
            NativeVideoReview.Stop();
            RefreshVideoReviewRows();
        }, asAction: true);
        RefreshVideoReviewRows();
        return rows + 1;
    }

    /// <summary>Only repaint a changed status, retaining every button under the pointer.</summary>
    private static void RefreshVideoReviewRows()
    {
        if (_videoReviewStatus == null || !IsOpen)
            return;
        NativeVideoReviewStatus state = NativeVideoReview.Status;
        NativeVideoReviewFailure failure = NativeVideoReview.LastFailure;
        string key = NativeVideoReview.LastKey;
        string language = Loc.CurrentLanguage;
        if (_videoReviewSnapshot && state == _videoReviewState && failure == _videoReviewFailure
            && key == _videoReviewKey && language == _videoReviewLanguage)
            return;
        _videoReviewSnapshot = true;
        _videoReviewState = state;
        _videoReviewFailure = failure;
        _videoReviewKey = key;
        _videoReviewLanguage = language;
        string caption = VideoReviewCaption();
        if (caption == _videoReviewCaption)
            return;
        _videoReviewCaption = caption;
        _videoReviewStatus.text = caption;
    }

    private static string VideoReviewCaption()
    {
        // A refused second press leaves the current decoder playing/preparing.
        // Its refusal still needs to be visible without changing that lifecycle.
        if (NativeVideoReview.LastFailure != NativeVideoReviewFailure.None)
            return VideoReviewFailureCaption(NativeVideoReview.LastFailure);
        string name = Loc.VideoReviewName(NativeVideoReview.LastKey);
        return NativeVideoReview.Status switch
        {
            NativeVideoReviewStatus.Preparing => Loc.VideoReviewPreparing(name),
            NativeVideoReviewStatus.Playing => Loc.VideoReviewPlaying(name),
            NativeVideoReviewStatus.Closed => Loc.VideoReviewClosed,
            NativeVideoReviewStatus.Busy or NativeVideoReviewStatus.Failed =>
                VideoReviewFailureCaption(NativeVideoReview.LastFailure),
            _ => Loc.VideoReviewReady,
        };
    }

    private static string VideoReviewFailureCaption(NativeVideoReviewFailure failure) => failure switch
    {
        NativeVideoReviewFailure.InvalidContext => Loc.VideoReviewScenarioRequired,
        NativeVideoReviewFailure.NativeMovieActive or NativeVideoReviewFailure.RemoteMovieActive =>
            Loc.VideoReviewNativeBusy,
        NativeVideoReviewFailure.AlreadyActive => Loc.VideoReviewAlreadyPlaying,
        NativeVideoReviewFailure.MediaUnavailable => Loc.VideoReviewUnavailable,
        _ => Loc.VideoReviewFailed,
    };

    private static void ClearVideoReviewRows()
    {
        // Hiding/rebuilding options must not close the review film. Release only
        // the old label so an asynchronous prepare never writes a destroyed row.
        _videoReviewStatus = null;
        _videoReviewCaption = string.Empty;
        _videoReviewSnapshot = false;
        _videoReviewKey = _videoReviewLanguage = string.Empty;
    }
}

using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The maintainer approved the stereo intro on 2026-10-17 and requested individual movie
/// triggers usable inside a scenario. The runtime owns the decoder and the normal shared
/// video window; this page only requests a canonical movie and shows its current result.
/// A press never rebuilds the pane or steals another native menu entry's selection.
/// Since 2026-10-18 the spoiler catalogue belongs behind the existing cheats.cfg gate.
/// That permission is checked again on each press; closing remains available after revocation.
/// </summary>
internal static partial class VROptionsTab
{
    private static TMP_Text? _videoReviewStatus;
    private static string _videoReviewCaption = string.Empty;
    private static bool _videoReviewSnapshot;
    private static bool _videoReviewCheats;
    private static readonly List<GameObject> VideoReviewMovieRows = new();
    private static NativeVideoReviewStatus _videoReviewState;
    private static NativeVideoReviewFailure _videoReviewFailure;
    private static string _videoReviewKey = string.Empty, _videoReviewLanguage = string.Empty;

    private static int BuildVideoReviewTriggers()
    {
        if (ContentRoot == null || !CheatsAvailable)
            return 0;

        BuildHeader(ContentRoot, Loc.VideoReviewHeading, sub: true);
        BuildNote(ContentRoot, Loc.VideoReviewHint, multiline: true);
        // Reserve space for localized refusal feedback before the first press.
        // A longer asynchronous result must not move the next button under the pointer.
        var status = BuildNote(ContentRoot, VideoReviewCaption(), multiline: true, minLines: 3);
        _videoReviewStatus = FindPart<TMP_Text>(status.transform, "Title");
        _videoReviewCaption = string.Empty;
        _videoReviewSnapshot = false;

        int rows = 0;
        for (int i = 0; i < NativeVideoReview.Count; i++)
        {
            // Capture the exact key, not the loop index. Every button must request
            // its own film even after another action updates the live status.
            string key = NativeVideoReview.GetKey(i);
            VideoReviewMovieRows.Add(BuildLinkRow(ContentRoot, Loc.VideoReviewPlay(key), () =>
            {
                if (CheatsAvailable)
                    NativeVideoReview.TryStart(key);
                RefreshVideoReviewRows();
            }, asAction: true));
            rows++;
        }

        BuildLinkRow(ContentRoot, Loc.VideoReviewClose, () =>
        {
            NativeVideoReview.RequestClose();
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
        bool cheats = CheatsAvailable;
        if (_videoReviewSnapshot && state == _videoReviewState && failure == _videoReviewFailure
            && key == _videoReviewKey && language == _videoReviewLanguage && cheats == _videoReviewCheats)
            return;
        if (!_videoReviewSnapshot || cheats != _videoReviewCheats)
            foreach (GameObject row in VideoReviewMovieRows)
                if (row != null) row.SetActive(cheats);
        _videoReviewSnapshot = true;
        _videoReviewCheats = cheats;
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
        if (!CheatsAvailable)
            return Loc.VideoReviewCheatsRequired;
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
        VideoReviewMovieRows.Clear();
        _videoReviewCaption = string.Empty;
        _videoReviewSnapshot = false;
        _videoReviewKey = _videoReviewLanguage = string.Empty;
    }
}

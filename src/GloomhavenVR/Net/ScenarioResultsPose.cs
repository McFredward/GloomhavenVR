using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>The original Results window uses the same common world and gesture register as
/// existing shared windows. Its seventh identity never expands the frozen six-kind TLVs.</summary>
internal static class ScenarioResultsPose
{
    private const SharedWindowKind Kind = SharedWindowKind.Results;
    private static readonly SharedWindowPoseTrack Track = new();
    private static ScenarioResultsState? _target;
    private static uint _key, _nextKey;
    private static int _author, _mover;
    private static bool _nativeVisible, _nativeWasShown;
    private static uint _shownKey;
    private static int _shownAuthor, _pendingRetiredAuthor;
    private static uint _nativeShow, _authoredShow, _pendingRetiredKey;

    // Reuse the game's existing exact visibility-transition event. A show after
    // hide is observable even when both occur between our two Update calls.
    internal static void ObserveWindowVisibility(UnityEngine.UI.UIWindow window, bool shown)
    {
        if (!shown || window == null || !ReferenceEquals(window, ScenarioResultsNative.Window)) return;
        if (++_nativeShow == 0) ++_nativeShow;
        if (_shownKey != 0 && _nativeWasShown)
        { _pendingRetiredAuthor = _shownAuthor; _pendingRetiredKey = _shownKey; }
        _shownKey = 0; _shownAuthor = 0;
    }

    private static readonly Dictionary<int, uint> Retired = new();

    private static bool IsRetired(int author, uint key) => Retired.TryGetValue(author, out uint retired)
        && (key == retired || !SharedWindowMoveRegister.Newer(key, retired));

    // A native close retires the key that was visible IN THAT local episode,
    // never a genuinely early future packet waiting for its native template.
    internal static bool ObserveNativeBoundary(out int retiredAuthor, out uint retiredKey)
    {
        retiredAuthor = 0; retiredKey = 0;
        bool visible = NativeVisible;
        if (_pendingRetiredKey != 0 || _nativeWasShown && !visible && _shownKey != 0)
        {
            retiredAuthor = _pendingRetiredKey != 0 ? _pendingRetiredAuthor : _shownAuthor;
            retiredKey = _pendingRetiredKey != 0 ? _pendingRetiredKey : _shownKey;
            if (!Retired.TryGetValue(retiredAuthor, out uint watermark)
                || SharedWindowMoveRegister.Newer(retiredKey, watermark)) Retired[retiredAuthor] = retiredKey;
            if (_shownAuthor == retiredAuthor && _shownKey == retiredKey)
            { _shownAuthor = 0; _shownKey = 0; }
            if (_author == retiredAuthor && _key == retiredKey)
            {
                SharedWindowMoves.Close(Kind); _target = null; _key = 0; _mover = 0; Track.Reset();
            }
        }
        _pendingRetiredAuthor = 0; _pendingRetiredKey = 0;
        if (!visible || !_nativeWasShown) { _shownAuthor = 0; _shownKey = 0; }
        _nativeWasShown = visible;
        RememberShownKey();
        return retiredKey != 0;
    }

    private static void RememberShownKey()
    {
        if (NativeVisible && _shownKey == 0 && _key != 0)
        { _shownKey = _key; _shownAuthor = _author; }
    }

    internal static uint Key => _key;
    internal static int Author => _author;

    internal static bool NativeVisible => ScenarioResultsNative.Window != null
        && ScenarioResultsNative.Window.gameObject.activeInHierarchy
        && (ScenarioResultsNative.Window.IsOpen || ScenarioResultsNative.Window.IsVisible);

    internal static void BeginAuthorOpening()
    {
        int author = SharedWindowSpawnAuthority.AuthorId;
        bool visible = NativeVisible;
        if (author != _author) { Reset(); _author = author; }
        if (visible && (!_nativeVisible || _authoredShow != _nativeShow))
        {
            if (++_nextKey == 0) ++_nextKey;
            _key = _nextKey; _authoredShow = _nativeShow; _target = null; _mover = 0; Track.Reset();
            SharedWindowMoves.Open(Kind, _key);
            RememberShownKey();
        }
        _nativeVisible = visible;
    }

    internal static bool Capture(ScenarioResultsState state)
    {
        bool publisher = NetPlayerActors.LocalPlayerId() == _author;
        // Native IsOpen precedes the fitted shared reveal. Never lend that
        // provisional frame to observers while the actual source is still pending.
        if (publisher && !SharedWindowSpawnAuthority.MayPublishInitial(Kind)) return false;
        if (_key == 0 || _author <= 0 || !SharedWindows.TryGetGrab(Kind, out GrabbableModal? grab)
            || grab == null || !SharedWindowFrame.TryRead(grab, out Vector3 position,
                out Quaternion rotation, out float size, allowUnrevealed: publisher)
            || !PanelLayout.TryGetAnchor(out Vector3 origin, out Quaternion yaw)) return false;
        float scale = Mathf.Max(PanelLayout.WorldScale, .01f);
        Quaternion inverse = Quaternion.Inverse(yaw);
        state.Key = _key; state.Author = _author; state.Size = size;
        state.Reference = grab.SharedReferenceWorldScale;
        state.Origin = origin; state.Rotation = yaw; state.Scale = scale;
        state.Pose = new RigPose { Position = inverse * (position - origin) / scale,
            Rotation = inverse * rotation };
        if (SharedWindowMoves.SampleResults(_key, out SharedWindowMoveEntry move)) state.Move = move;
        return true;
    }

    internal static bool Observe(int sender, ScenarioResultsState state)
    {
        if (sender <= 0 || state.Author != SharedWindowSpawnAuthority.AuthorId
            || IsRetired(state.Author, state.Key)) return false;
        bool original = sender == state.Author && state.HasPresentation;
        if (original && (_key != state.Key || _author != state.Author))
        {
            SharedWindowMoves.Close(Kind); Track.Reset(); _target = null; _mover = 0;
            _key = state.Key; _author = state.Author;
            SharedWindowMoves.Open(Kind, _key);
        }
        if (state.Key != _key || state.Author != _author) return false;
        RememberShownKey();
        if (state.Move.HasValue)
        {
            SharedWindowMoveEntry move = state.Move.Value;
            if (!SharedWindowMoves.ObserveResults(sender, in move)) return original;
            if (!SharedWindowMoves.MayFollow(sender, Kind, _key)) return original;
        }
        else if (!original || SharedWindowMoves.HasLocalClaim(Kind)) return original;
        if (!state.Visible && original) { _target = null; return true; }
        _target = state;
        if (_mover != sender) { Track.Reset(); _mover = sender; }
        return true;
    }

    internal static void Tick()
    {
        if (_target == null || !NativeVisible || SharedWindowMoves.HasLocalClaim(Kind)
            || !SharedWindows.TryGetGrab(Kind, out GrabbableModal? grab) || grab == null
            || grab.IsGrabbed) return;
        ScenarioResultsState target = _target;
        if (target.Move.HasValue && !SharedWindowMoves.MayFollow(_mover, Kind, target.Key)) return;
        RigPose world = ToWorld(target);
        RigPose displayed = Track.Sample(world, target.Size, Time.unscaledTime, out float size);
        grab.YieldSharedControl();
        grab.ApplySharedReferenceWorldScale(target.Reference);
        Transform? root = ((IPanelGrabOwner)grab).GrabRoot;
        if (root == null) return;
        root.localScale = Vector3.one * SharedWindowSizeLaw.SharedGrabFactor(size);
        if (root.position != displayed.Position || root.rotation != displayed.Rotation)
            grab.PlaceFrameAt(displayed.Position, displayed.Rotation);
        SharedWindowIdentity.NotePoseApplied(Kind);
        ModalFallback.NoteSharedAnchorSpent(Kind, "current ordered results pose applied");
        SharedWindowSize.NoteRemotePuller(Kind, _mover);
    }

    internal static bool TryInitial(out Vector3 position, out Quaternion rotation, out float size)
    {
        position = Vector3.zero; rotation = Quaternion.identity; size = 1;
        if (_target == null || !_target.Visible || _target.Key != _key
            || IsRetired(_target.Author, _target.Key)) return false;
        RigPose world = ToWorld(_target); position = world.Position; rotation = world.Rotation;
        size = _target.Size;
        if (SharedWindows.TryGetGrab(Kind, out GrabbableModal? grab) && grab != null)
            grab.ApplySharedReferenceWorldScale(_target.Reference);
        return true;
    }

    private static RigPose ToWorld(ScenarioResultsState state) => new()
    {
        Position = state.Origin + state.Rotation * (state.Pose.Position * state.Scale),
        Rotation = state.Rotation * state.Pose.Rotation
    };

    internal static void Reset()
    {
        SharedWindowMoves.Close(Kind); _key = 0; _author = _mover = 0;
        _nativeVisible = _nativeWasShown = false; _shownKey = 0; _shownAuthor = 0;
        _nativeShow = _authoredShow = _pendingRetiredKey = 0; _pendingRetiredAuthor = 0;
        Retired.Clear(); _target = null; Track.Reset();
    }
}

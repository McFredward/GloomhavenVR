using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreenStereo
{
    private VideoPlayer? _stereoMovie;
    private VideoPlayer? _reportedMovie;
    private string _reportedMovieUrl = string.Empty;
    internal bool StereoMovieActive => _stereoMovie != null;
    internal bool OwnsMoviePlane(Camera camera) => _active && _stereoMovie != null
        && _stereoMovie.targetCamera == camera && _rtRight != null && _rtLeftShifted != null;

    private void SyncStereoMovie()
    {
        VideoPlayer? current = NativeVideoMovies.CameraPlaneSource;
        if (_stereoMovie == current) return;
        _stereoMovie = current;
        _shiftBlitFrame = -1;
        if (current != null)
            VRLog.Note("WorldUI", $"NATIVE MOVIE COMPOSITOR: key={NativeVideoMovies.NativePlaybackKey(current)}; actual headset eye selects its full SBS picture, live native UI stays on glass.");
    }

    /// <summary>
    /// Camera-plane originals keep their exact decoder/camera binding. Their packed
    /// native plane is replaced only on our background quad; the separate native UI
    /// camera stack still renders live onto glass. Never crop the whole composite,
    /// which would crop buttons/text and make each eye's menu different.
    /// </summary>
    private bool ShowStereoMovie(Camera head, Material material)
    {
        if (_stereoMovie == null || _rtRight == null || !EnsureShiftRt() || _rtLeftShifted == null) return false;
        VideoPlayer player = _stereoMovie;
        if (_shiftBlitFrame != Time.frameCount)
        {
            _shiftBlitFrame = Time.frameCount;
            Texture? texture = player.texture;
            bool valid = player.isActiveAndEnabled && player.isPrepared && (player.isPlaying || player.isPaused)
                && player.frame >= 0 && texture != null && texture.width == 3840 && texture.height == 1080;
            if (texture != null && player.isPrepared && (texture.width != 3840 || texture.height != 1080)
                && (_reportedMovie != player || _reportedMovieUrl != player.url))
            {
                _reportedMovie = player;
                _reportedMovieUrl = player.url;
                VRLog.Warn("WorldUI", $"NATIVE MOVIE STEREO FALLBACK: key={NativeVideoMovies.NativePlaybackKey(player)}, reason=versioned SBS frame dimensions are not 3840x1080; refusing the packed picture while native flow continues.");
            }
            RenderTexture? previous = RenderTexture.active;
            try
            {
                DrawMovieEye(valid ? texture : null, _rtLeftShifted, 0);
                DrawMovieEye(valid ? texture : null, _rtRight, 1);
            }
            finally { RenderTexture.active = previous; }
        }
        // Actual eyes only. A nested/auxiliary Mono callback can never advance parity
        // and swap the next head pass to the wrong recorded perspective.
        RenderTexture target = head.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? _rtRight : _rtLeftShifted;
        if (!ReferenceEquals(material.mainTexture, target)) material.mainTexture = target;
        return true;
    }

    internal static Rect MovieFrameRect(int width, int height, float perEyeAspect)
    {
        float fittedWidth = Mathf.Min(width, height * perEyeAspect);
        float fittedHeight = fittedWidth / perEyeAspect;
        return new Rect((width - fittedWidth) * .5f, (height - fittedHeight) * .5f, fittedWidth, fittedHeight);
    }

    private static void DrawMovieEye(Texture? texture, RenderTexture target, int eye)
    {
        RenderTexture.active = target;
        GL.Clear(true, true, Color.black);
        if (texture == null) return;
        Rect destination = MovieFrameRect(target.width, target.height,
            StereoVideoSurface.PerEyeAspect(texture.width, texture.height));
        Rect source = new(eye * .5f + .5f / texture.width, .5f / texture.height,
            .5f - 1f / texture.width, 1f - 1f / texture.height);
        GL.PushMatrix();
        try
        {
            GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
            Graphics.DrawTexture(destination, texture, source, 0, 0, 0, 0, Color.white);
        }
        finally { GL.PopMatrix(); }
    }
}

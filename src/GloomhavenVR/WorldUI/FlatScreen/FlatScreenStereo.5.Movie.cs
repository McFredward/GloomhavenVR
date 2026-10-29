using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreenStereo
{
    private VideoPlayer? _stereoMovie;
    private VideoPlayer? _reportedMovie;
    private string _reportedMovieUrl = string.Empty;
    private Material? _questMovieEye;
    internal bool StereoMovieActive => _stereoMovie != null;
    internal bool OwnsMoviePlane(Camera camera) => _active && _stereoMovie != null
        && _stereoMovie.targetCamera == camera && _rtRight != null && _rtLeftShifted != null;
    internal bool OwnsStartupMovie(VideoPlayer player, Material material) => _active && _stereoMovie == player
        && _quadMaterial == material && _rtRight != null && _rtRight.IsCreated()
        && _rtLeftShifted != null && _rtLeftShifted.IsCreated();

    // IntroPlayer.Start can select its camera-plane source after the last capture
    // census. Reconcile at that exact original camera's first render, including
    // its return to live logos; the packed plane must never enter a mono capture.
    internal bool NeedsMovieCaptureSync(Camera camera)
    {
        VideoPlayer? current = NativeVideoMovies.CameraPlaneSource;
        return current != _stereoMovie
            && ((current != null && current.targetCamera == camera)
                || (_stereoMovie != null && _stereoMovie.targetCamera == camera));
    }

    private void SyncStereoMovie()
    {
        VideoPlayer? current = NativeVideoMovies.CameraPlaneSource;
        if (_stereoMovie == current) { NativeMovieCameraPlane.RefreshStartup(); return; }
        _stereoMovie = current;
        NativeMovieCameraPlane.RefreshStartup();
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
        if (_stereoMovie == null) return false;
        // Quest has already completed all target-changing work at head pre-cull.
        // Its late headset callback may route textures only (B675 hardware boundary).
        if (!QuestStandalonePlatform.Enabled) PrepareStereoMovieEyes();
        if (_rtRight == null || _rtLeftShifted == null) return false;
        RenderTexture target = head.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? _rtRight : _rtLeftShifted;
        if (!ReferenceEquals(material.mainTexture, target)) material.mainTexture = target;
        if (QuestStandalonePlatform.SharedStereoEyeRouting)
            QuestStandalonePlatform.SetFlatScreenEyes(material, _rtLeftShifted, _rtRight);
        else QuestStandalonePlatform.SetFlatScreenMono(material, target);
        return true;
    }

    private void PrepareStereoMovieEyes()
    {
        if (_stereoMovie == null || _rtRight == null || !EnsureShiftRt() || _rtLeftShifted == null) return;
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
                float alpha = Mathf.Clamp01(NativeMovieCameraPlane.PresentationAlpha(player));
                // Startup's sole original camera is UI-classified and clears black.
                // No background stack authors _leftRt there; never expose fresh RT data.
                Texture? background = _introGuard ? null : _leftRt;
                if (QuestStandalonePlatform.Enabled)
                {
                    DrawQuestMovieEye(valid ? texture : null, _rtLeftShifted, 0, alpha, background);
                    DrawQuestMovieEye(valid ? texture : null, _rtRight, 1, alpha, background);
                }
                else
                {
                    DrawMovieEye(valid ? texture : null, _rtLeftShifted, 0, alpha, background);
                    DrawMovieEye(valid ? texture : null, _rtRight, 1, alpha, background);
                }
            }
            finally { RenderTexture.active = previous; }
        }
    }

    private void DrawQuestMovieEye(Texture? texture, RenderTexture target, int eye, float alpha, Texture? background)
    {
        if (_questMovieEye == null)
            _questMovieEye = new Material(QuestMovieAssets.Shader("Hidden/GloomhavenVR/QuestMovieEye"))
                { hideFlags = HideFlags.HideAndDontSave };
        Rect frame = MovieFrameRect(target.width, target.height, 1920f / 1080f);
        _questMovieEye.SetTexture("_Background", background != null ? background : Texture2D.blackTexture);
        _questMovieEye.SetVector("_FrameSize", new Vector4(frame.width / target.width, frame.height / target.height, 0f, 0f));
        _questMovieEye.SetFloat("_Eye", eye);
        _questMovieEye.SetFloat("_Alpha", texture == null ? 0f : alpha);
        bool srgb = GL.sRGBWrite;
        try
        {
            GL.sRGBWrite = target.sRGB;
            Graphics.Blit(texture != null ? texture : Texture2D.blackTexture, target, _questMovieEye, 0);
        }
        finally { GL.sRGBWrite = srgb; }
    }

    internal static Rect MovieFrameRect(int width, int height, float perEyeAspect)
    {
        float fittedWidth = Mathf.Min(width, height * perEyeAspect);
        float fittedHeight = fittedWidth / perEyeAspect;
        return new Rect((width - fittedWidth) * .5f, (height - fittedHeight) * .5f, fittedWidth, fittedHeight);
    }

    private static void DrawMovieEye(Texture? texture, RenderTexture target, int eye, float alpha = 1f, Texture? background = null)
    {
        if (background != null) Graphics.Blit(background, target);
        RenderTexture.active = target;
        if (background == null) GL.Clear(true, true, Color.black);
        if (texture == null) return;
        Rect destination = MovieFrameRect(target.width, target.height,
            StereoVideoSurface.PerEyeAspect(texture.width, texture.height));
        Rect source = new(eye * .5f + .5f / texture.width, .5f / texture.height,
            .5f - 1f / texture.width, 1f - 1f / texture.height);
        GL.PushMatrix();
        try
        {
            GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
            // Unity's built-in GUI texture pass doubles the supplied vertex tint;
            // its neutral value is 0.5, including alpha. White doubles movie RGB.
            Graphics.DrawTexture(destination, texture, source, 0, 0, 0, 0, new Color(.5f, .5f, .5f, .5f * alpha));
        }
        finally { GL.PopMatrix(); }
    }
}

using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    internal enum SceneEyeResult
    {
        Rendered, Disposed, UnknownEye, WorkingColorSpace,
        SourceUnavailable, SourceShape, SourceFormat, SourceSrgb, SourceDynamicScale,
        SourceMultisample, DestinationUnavailable, DestinationShape, DestinationFormat,
        DestinationSrgb, DestinationDynamicScale, SourceDestinationAlias, BlitFailed
    }

    // Caller owns actual head-camera/MultiPass/eye and current coverage admission.
    // An arbitrary OnRenderImage alpha is NOT admitted by this class. Retain
    // Unity's borrowed source only until this queued blit returns. No scene render, readback, upload, pool or prior frame.
    internal sealed class SceneEyeOutput : IDisposable
    {
        private readonly int _maximumSize = SystemInfo.maxTextureSize;
        private bool _disposed;
        public bool Ready => !_disposed;

        // The full-RGB bridge consumes coverage from this very RGBA texture in
        // the native DirectMode composition path. Do not pack a header, resize,
        // multiply RGB by A, threshold alpha or create a second render. The
        // ordinary Unity blit also performs the usual automatic MSAA resolve.
        // A null destination is Unity's current XR target, not a resource ID;
        // native submission/encoder association belongs to the native provider.
        public SceneEyeResult TryRenderOrdinary(RenderTexture source, RenderTexture? destination,
                                                 Camera.MonoOrStereoscopicEye eye)
            => Render(source, destination, eye);

        private SceneEyeResult Render(RenderTexture source, RenderTexture? destination,
                                      Camera.MonoOrStereoscopicEye eye)
        {
            if (_disposed) return SceneEyeResult.Disposed;
            if (eye != Camera.MonoOrStereoscopicEye.Left && eye != Camera.MonoOrStereoscopicEye.Right)
                return SceneEyeResult.UnknownEye;
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
                return SceneEyeResult.WorkingColorSpace;
            if (source == null || !source.IsCreated()) return SceneEyeResult.SourceUnavailable;
            if (!Shape(source)) return SceneEyeResult.SourceShape;
            if (!Format(source.graphicsFormat)) return SceneEyeResult.SourceFormat;
            if (source.sRGB) return SceneEyeResult.SourceSrgb;
            if (source.useDynamicScale) return SceneEyeResult.SourceDynamicScale;
            int samples = source.antiAliasing;
            if ((samples != 1 && samples != 2 && samples != 4 && samples != 8)
                || (samples > 1 && source.bindTextureMS)) return SceneEyeResult.SourceMultisample;
            if (destination != null)
            {
                if (source == destination) return SceneEyeResult.SourceDestinationAlias;
                if (!destination.IsCreated()) return SceneEyeResult.DestinationUnavailable;
                if (!Shape(destination)) return SceneEyeResult.DestinationShape;
                if (destination.width != source.width || destination.height != source.height)
                    return SceneEyeResult.DestinationShape;
                if (!Format(destination.graphicsFormat)) return SceneEyeResult.DestinationFormat;
                if (destination.sRGB) return SceneEyeResult.DestinationSrgb;
                if (destination.useDynamicScale) return SceneEyeResult.DestinationDynamicScale;
                int destinationSamples = destination.antiAliasing;
                if ((destinationSamples != 1 && destinationSamples != 2 && destinationSamples != 4 && destinationSamples != 8)
                    || (destinationSamples > 1 && destination.bindTextureMS)) return SceneEyeResult.DestinationShape;
            }
            RenderTexture? active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                // Preserve the original RGB dimensions and values. Unity resolves
                // bindMS=false sources as on its ordinary camera blit path.
                GL.sRGBWrite = false;
                Graphics.Blit(source, destination);
                return SceneEyeResult.Rendered;
            }
            catch (Exception) { return SceneEyeResult.BlitFailed; }
            finally
            {
                GL.sRGBWrite = srgb;
                RenderTexture.active = active;
            }
        }

        private bool Shape(RenderTexture value) => value.width > 0 && value.height > 0
            && value.width <= _maximumSize && value.height <= _maximumSize
            && value.dimension == TextureDimension.Tex2D && value.volumeDepth == 1 && !value.useMipMap;

        private static bool Format(GraphicsFormat value) => value == GraphicsFormat.R8G8B8A8_UNorm
            || value == GraphicsFormat.B8G8R8A8_UNorm;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}

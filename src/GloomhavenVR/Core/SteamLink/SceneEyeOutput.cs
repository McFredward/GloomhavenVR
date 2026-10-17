using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    internal enum SceneEyeResult
    {
        Rendered, Disposed, ShaderUnavailable, UnknownEye, WorkingColorSpace,
        SourceUnavailable, SourceShape, SourceFormat, SourceSrgb, SourceDynamicScale,
        SourceMultisample, DestinationUnavailable, DestinationShape, DestinationFormat,
        DestinationSrgb, DestinationDynamicScale, SourceDestinationAlias, BlitFailed
    }

    // Caller owns actual head-camera/MultiPass/eye and current coverage admission.
    // An arbitrary OnRenderImage alpha is NOT admitted by this class. Prepare the
    // material outside callbacks; retain Unity's borrowed source only until this
    // queued blit returns. No scene render, readback, upload, pool or prior frame.
    internal sealed class SceneEyeOutput : IDisposable
    {
        private readonly Material _material;
        private readonly int _maximumSize;
        private bool _disposed;
        public bool Ready => !_disposed && _material != null;

        public SceneEyeOutput(Shader shader)
        {
            if (shader == null || shader.name != SceneAtlasProfile.ShaderName || !shader.isSupported)
                throw new NotSupportedException("The frozen scene-atlas shader is unavailable.");
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _maximumSize = SystemInfo.maxTextureSize;
            if (_material.passCount != 1)
            {
                UnityEngine.Object.Destroy(_material);
                throw new NotSupportedException("The scene-atlas shader has an unexpected pass layout.");
            }
        }

        // Refusal records no draw. The caller must run its ordinary source blit
        // exactly once when the result is not Rendered. Never reuse an old atlas.
        public SceneEyeResult TryRender(RenderTexture source, RenderTexture? destination,
                                         Camera.MonoOrStereoscopicEye eye)
        {
            if (_disposed) return SceneEyeResult.Disposed;
            if (_material == null) return SceneEyeResult.ShaderUnavailable;
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
                if (!Format(destination.graphicsFormat)) return SceneEyeResult.DestinationFormat;
                if (destination.sRGB) return SceneEyeResult.DestinationSrgb;
                if (destination.useDynamicScale) return SceneEyeResult.DestinationDynamicScale;
                if (destination.antiAliasing != 1) return SceneEyeResult.DestinationShape;
            }
            RenderTexture? active = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                // Gamma SDR UNorm is the initial frozen transfer profile. The
                // shader has its own linear-clamp sampler and consumes the same
                // resolved source RGB/A. Unity auto-resolves ordinary bindMS=false
                // MSAA sources, just as the original Graphics.Blit path does.
                GL.sRGBWrite = false;
                Graphics.Blit(source, destination, _material, 0);
                return SceneEyeResult.Rendered;
            }
            catch (Exception) { return SceneEyeResult.BlitFailed; }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = active; }
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
            UnityEngine.Object.Destroy(_material); // Normal deferred destruction, after closing entry.
        }
    }
}

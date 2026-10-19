using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    // Owns an alpha-only depth prefix and optional exact screen-backdrop draw. This
    // is an opaque-depth prefix, NOT certification of later transparent draws.
    // The caller owns camera/enable-epoch admission and must Disarm before changing
    // its target, rendering path or clear policy. No camera, RT or depth is created.
    internal sealed class SceneEyeCoverage : IDisposable
    {
        internal enum Refusal
        {
            None, Disarmed, Disposed, Shader, Camera, Epoch, ColorSpace,
            RenderPipeline, ClearPolicy, Hdr, Target, Command
        }

        internal const string ShaderName = "Hidden/GloomhavenVR/SteamLinkSceneCoverage";
        private readonly Material? _material;
        private readonly CommandBuffer? _commands;
        private Camera? _camera;
        private Func<Renderer?>? _backdropSource;
        private Renderer? _backdrop;
        private ulong _epoch;
        private bool _attached, _disposed;

        internal SceneEyeCoverage(Shader shader)
        {
            Reason = Refusal.Shader;
            if (shader == null || shader.name != ShaderName || !shader.isSupported)
                return;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (_material.passCount != 3 || _material.FindPass("RESET") != 0
                || _material.FindPass("STAMP") != 1 || _material.FindPass("BACKDROP") != 2)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
                return;
            }
            _commands = new CommandBuffer { name = "GloomhavenVR.SameEyeOpaqueCoverage" };
            // Deliberately inherit the camera's current color AND depth/MSAA
            // attachments. Blit/SetRenderTarget/CameraDepthTexture would lose that
            // identity. Neither draw writes RGB, depth or stencil.
            RecordCommands(null);
            Reason = Refusal.Disarmed;
        }

        internal bool Ready => !_disposed && _attached && _camera != null;
        internal Refusal Reason { get; private set; }
        internal Camera? ArmedCamera => Ready ? _camera : null;
        internal ulong EnableEpoch => Ready ? _epoch : 0;

        internal bool IsArmedFor(Camera camera, ulong enableEpoch)
            => Ready && camera == _camera && enableEpoch != 0 && enableEpoch == _epoch;

        // Supply the existing screen owner's exact opaque backdrop, never a scene
        // search or a shader-name match. The getter is cold-created and allocation
        // free; it is read at the actual head render boundary so hide, replacement
        // and destruction cannot retain yesterday's rectangle until a cold scan.
        internal void SetOpaqueBackdropSource(Func<Renderer?>? source)
        {
            if (_disposed) return;
            _backdropSource = source;
            if (_commands != null) RecordCommands(null);
        }

        private void RecordCommands(Renderer? backdrop)
        {
            if (_disposed || _commands == null || _material == null) return;
            _commands.Clear();
            _commands.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 3);
            // The original depthless background RGB was already drawn before the
            // reset. Reassert only its exact geometry's A1, before transparent UI.
            // DrawRenderer follows the current renderer pose without copying it.
            if (backdrop != null) _commands.DrawRenderer(backdrop, _material, 0, 2);
            _backdrop = backdrop;
        }

        private void OnPreRender(Camera camera)
        {
            if (!Ready || camera != _camera) return;
            try
            {
                Renderer? backdrop = _backdropSource?.Invoke();
                if (backdrop == null || !backdrop.enabled || backdrop.forceRenderingOff
                    || !backdrop.gameObject.activeInHierarchy
                    || (camera.cullingMask & (1 << backdrop.gameObject.layer)) == 0)
                    backdrop = null;
                // Unity's destroyed-object equality would compare a stale managed
                // renderer equal to null and accidentally retain its old CB draw.
                if (!ReferenceEquals(backdrop, _backdrop)) RecordCommands(backdrop);
            }
            catch
            {
                // An owner getter is not permission to leak a callback exception
                // or leave the last successful mask attached after a failure.
                Disarm(); Reason = Refusal.Command;
            }
        }

        // Cold lifecycle operation; never call from an eye callback. Re-arming
        // another camera/epoch removes the old camera's commands first.
        internal bool Arm(Camera camera, ulong enableEpoch)
        {
            Disarm();
            if (_disposed) return Refuse(Refusal.Disposed);
            if (_material == null || _commands == null) return Refuse(Refusal.Shader);
            if (camera == null || camera.cameraType != CameraType.Game) return Refuse(Refusal.Camera);
            if (enableEpoch == 0) return Refuse(Refusal.Epoch);
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma) return Refuse(Refusal.ColorSpace);
            if (GraphicsSettings.currentRenderPipeline != null || camera.actualRenderingPath != RenderingPath.Forward)
                return Refuse(Refusal.RenderPipeline);
            if (camera.clearFlags != CameraClearFlags.SolidColor) return Refuse(Refusal.ClearPolicy);
            if (camera.allowHDR) return Refuse(Refusal.Hdr);
            RenderTexture target = camera.targetTexture;
            if (target != null && (!target.IsCreated() || target.depth < 16 || target.sRGB
                || target.dimension != TextureDimension.Tex2D || target.volumeDepth != 1
                || target.bindTextureMS || target.useDynamicScale
                || (target.antiAliasing != 1 && target.antiAliasing != 2 && target.antiAliasing != 4 && target.antiAliasing != 8)
                || (target.graphicsFormat != GraphicsFormat.R8G8B8A8_UNorm
                    && target.graphicsFormat != GraphicsFormat.B8G8R8A8_UNorm)))
                return Refuse(Refusal.Target);
            try
            {
                camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
                Camera.onPreRender += OnPreRender;
            }
            catch
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
                return Refuse(Refusal.Command);
            }
            _camera = camera; _epoch = enableEpoch; _attached = true;
            Reason = Refusal.None;
            return true;
        }

        internal void Disarm()
        {
            Camera.onPreRender -= OnPreRender;
            if (_attached && _camera != null && _commands != null)
                _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
            _attached = false; _camera = null; _epoch = 0;
            if (_commands != null) RecordCommands(null);
            Reason = _disposed ? Refusal.Disposed : Refusal.Disarmed;
        }

        private bool Refuse(Refusal reason) { Reason = reason; return false; }

        public void Dispose()
        {
            if (_disposed) return;
            Disarm(); _disposed = true; Reason = Refusal.Disposed;
            _backdropSource = null;
            _commands?.Release();
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}

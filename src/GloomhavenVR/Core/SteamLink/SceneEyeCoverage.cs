using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    // Owns two alpha-only commands on one explicitly supplied head camera. This
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
        private ulong _epoch;
        private bool _attached, _disposed;

        internal SceneEyeCoverage(Shader shader)
        {
            Reason = Refusal.Shader;
            if (shader == null || shader.name != ShaderName || !shader.isSupported)
                return;
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (_material.passCount != 2 || _material.FindPass("RESET") != 0 || _material.FindPass("STAMP") != 1)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
                return;
            }
            _commands = new CommandBuffer { name = "GloomhavenVR.SameEyeOpaqueCoverage" };
            // Deliberately inherit the camera's current color AND depth/MSAA
            // attachments. Blit/SetRenderTarget/CameraDepthTexture would lose that
            // identity. Neither draw writes RGB, depth or stencil.
            _commands.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            _commands.DrawProcedural(Matrix4x4.identity, _material, 1, MeshTopology.Triangles, 3);
            Reason = Refusal.Disarmed;
        }

        internal bool Ready => !_disposed && _attached && _camera != null;
        internal Refusal Reason { get; private set; }
        internal Camera? ArmedCamera => Ready ? _camera : null;
        internal ulong EnableEpoch => Ready ? _epoch : 0;

        internal bool IsArmedFor(Camera camera, ulong enableEpoch)
            => Ready && camera == _camera && enableEpoch != 0 && enableEpoch == _epoch;

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
            try { camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _commands); }
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
            if (_attached && _camera != null && _commands != null)
                _camera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _commands);
            _attached = false; _camera = null; _epoch = 0;
            Reason = _disposed ? Refusal.Disposed : Refusal.Disarmed;
        }

        private bool Refuse(Refusal reason) { Reason = reason; return false; }

        public void Dispose()
        {
            if (_disposed) return;
            Disarm(); _disposed = true; Reason = Refusal.Disposed;
            _commands?.Release();
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}

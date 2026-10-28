#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace GloomhavenVR.Quest
{
    /// <summary>
    /// Android camera-plane output adapter. B619 decodes the original Intro and
    /// plays its audio, but the redirected camera target remains grey. Copy the
    /// native decoder texture into that same capture, preserving the original
    /// player mode, audio, clock, callbacks and the mod's existing stereo depth.
    /// </summary>
    internal sealed class QuestCameraVideoOutput : IDisposable
    {
        internal const string ShaderName = "Hidden/GloomhavenVR/QuestCameraVideo";
        readonly VideoPlayer player;
        readonly string context;
        Camera camera;
        RenderTexture captured;
        CameraEvent drawEvent;
        CommandBuffer commands;
        Material material;
        Mesh quad;
        Texture texture;
        bool disposed, logged, failed;

        internal QuestCameraVideoOutput(VideoPlayer player, string context)
        {
            this.player = player;
            this.context = context;
            if (player != null) Camera.onPreRender += BeforeCamera;
        }

        internal static bool CapturedTarget(Camera candidate)
        {
            return GloomhavenVR.Core.QuestStandalonePlatform.IsFlatScreenVideoTarget(candidate);
        }

        void BeforeCamera(Camera rendering)
        {
            if (disposed) return;
            if (player == null) { Detach(); return; }
            // The head/other camera passes are irrelevant. Gate them before
            // sampling decoder state; this callback performs no scene sweeps.
            if (rendering != player.targetCamera)
            {
                if (rendering == camera) Detach();
                return;
            }
            if (!player.isActiveAndEnabled || !player.isPrepared
                || (!player.isPlaying && !player.isPaused) || player.frame < 0
                || (player.renderMode != VideoRenderMode.CameraNearPlane
                    && player.renderMode != VideoRenderMode.CameraFarPlane)
                || !CapturedTarget(player.targetCamera))
            { Detach(); return; }
            Texture decoded = player.texture;
            if (decoded == null) { Detach(); return; }
            try
            {
                Apply(rendering, decoded, player.renderMode, player.aspectRatio,
                    player.targetCameraAlpha, player.pixelAspectRatioNumerator,
                    player.pixelAspectRatioDenominator);
                if (!logged)
                {
                    logged = true;
                    Debug.Log("[Quest startup] original movie camera output " + context
                        + " mode=" + player.renderMode + " decoded=" + decoded.width + "x" + decoded.height
                        + " target=" + rendering.targetTexture.name + " " + rendering.targetTexture.width
                        + "x" + rendering.targetTexture.height + " frame=" + player.frame
                        + " actualUrl=" + player.url
                        + " nativePlaybackPreserved=true stereoCapturePreserved=true");
                }
            }
            catch (Exception error)
            {
                Detach();
                if (!failed)
                { failed = true; Debug.LogError("[Quest startup] original movie camera output failed " + context + " " + error.Message); }
            }
        }

        // Only presentation state is changed. This seam also permits a real GPU
        // fixture to supply asymmetric decoded pixels without a codec surrogate.
        internal void Apply(Camera destination, Texture decoded, VideoRenderMode mode,
            VideoAspectRatio aspect, float alpha, uint pixelNumerator, uint pixelDenominator)
        {
            if (disposed || !CapturedTarget(destination)) { Detach(); return; }
            if (mode != VideoRenderMode.CameraNearPlane && mode != VideoRenderMode.CameraFarPlane)
                throw new InvalidOperationException("Only original camera-plane movies may use the Quest output adapter.");
            if (decoded == null || decoded.width <= 0 || decoded.height <= 0
                || pixelNumerator == 0 || pixelDenominator == 0)
                throw new InvalidOperationException("Original decoded movie dimensions are invalid.");
            if (material == null)
            {
                var shader = Resources.Load<Shader>("QuestCameraVideo");
                if (shader == null || shader.name != ShaderName || !shader.isSupported || shader.passCount != 1)
                    throw new InvalidOperationException("Validated Quest camera movie shader is unavailable.");
                material = new Material(shader) { name = "GloomhavenVR.QuestCameraVideo", hideFlags = HideFlags.HideAndDontSave };
                quad = new Mesh { name = "GloomhavenVR.QuestCameraVideoQuad", hideFlags = HideFlags.HideAndDontSave };
                quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.UploadMeshData(true);
                commands = new CommandBuffer { name = "GloomhavenVR.QuestCameraVideo" };
            }
            if (texture != decoded)
            {
                texture = decoded;
                material.mainTexture = decoded;
            }
            RenderTexture target = destination.targetTexture;
            material.SetVector("_UvTransform", UvTransform(aspect, decoded.width, decoded.height,
                target.width, target.height, pixelNumerator, pixelDenominator));
            material.SetFloat("_Alpha", Mathf.Clamp01(alpha));
            bool far = mode == VideoRenderMode.CameraFarPlane;
            material.SetFloat("_Plane", far ? 1f : 0f);
            material.SetInt("_ZTest", (int)(far
                ? SystemInfo.usesReversedZBuffer ? CompareFunction.GreaterEqual : CompareFunction.LessEqual
                : CompareFunction.Always));
            CameraEvent desiredEvent = far ? CameraEvent.BeforeImageEffects : CameraEvent.AfterEverything;
            if (camera != destination || captured != target || drawEvent != desiredEvent)
            {
                Detach();
                camera = destination;
                captured = target;
                drawEvent = desiredEvent;
                // Inherit this camera's CURRENT color/depth attachments. Unity
                // may render into an intermediate buffer: explicitly binding the
                // final target before its resolve makes that later resolve erase
                // the movie. Far pixels compose before original image effects,
                // with native foreground depth; Near follows the native scene.
                commands.Clear();
                commands.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                camera.AddCommandBuffer(drawEvent, commands);
            }
        }

        internal static Vector4 UvTransform(VideoAspectRatio aspect, int width, int height,
            int targetWidth, int targetHeight, uint pixelNumerator, uint pixelDenominator)
        {
            float videoRatio = (float)((double)width * pixelNumerator / pixelDenominator / height);
            float targetRatio = (float)targetWidth / targetHeight;
            float x = 1f, y = 1f;
            if (aspect == VideoAspectRatio.NoScaling)
            { x = targetWidth / ((float)width * pixelNumerator / pixelDenominator); y = (float)targetHeight / height; }
            else if (aspect == VideoAspectRatio.FitHorizontally
                || (aspect == VideoAspectRatio.FitInside && videoRatio >= targetRatio)
                || (aspect == VideoAspectRatio.FitOutside && videoRatio < targetRatio)) y = videoRatio / targetRatio;
            else if (aspect == VideoAspectRatio.FitVertically
                || aspect == VideoAspectRatio.FitInside || aspect == VideoAspectRatio.FitOutside) x = targetRatio / videoRatio;
            else if (aspect != VideoAspectRatio.Stretch)
                throw new InvalidOperationException("Unknown native movie aspect mode.");
            return new Vector4(x, y, (1f - x) * .5f, (1f - y) * .5f);
        }

        void Detach()
        {
            if (camera != null && commands != null)
                camera.RemoveCommandBuffer(drawEvent, commands);
            camera = null; captured = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Camera.onPreRender -= BeforeCamera;
            Detach();
            if (commands != null) commands.Release();
            if (material != null) UnityEngine.Object.Destroy(material);
            if (quad != null) UnityEngine.Object.Destroy(quad);
            commands = null; material = null; quad = null; texture = null;
        }
    }
}
#endif

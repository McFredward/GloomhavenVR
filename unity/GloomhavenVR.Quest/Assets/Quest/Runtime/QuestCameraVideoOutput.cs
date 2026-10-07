#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace GloomhavenVR.Quest
{
    /// <summary>
    /// Android camera-plane output adapter. B620 proves native decoded frames
    /// and an attached output buffer, but the headset still shows grey. Complete
    /// the actual capture before its shared eye consumers and retain bounded
    /// decoded/capture/glass/consumer evidence. Preserve the original player mode,
    /// audio, clock, callbacks and the mod's existing stereo depth.
    /// </summary>
    internal sealed class QuestCameraVideoOutput : IDisposable
    {
        internal const string ShaderName = "Hidden/GloomhavenVR/QuestCameraVideo";
        readonly VideoPlayer player;
        readonly string context;
        Camera camera;
        Camera snapshotCamera;
        RenderTexture captured;
        Rect captureViewport;
        CommandBuffer completion;
        CommandBuffer farDraw, farSnapshot;
        Material ordinaryCopyMaterial;
        RenderTexture cameraPixels;
        bool farOutput;
        Material material;
        Mesh quad;
        Texture texture;
        bool disposed, logged, failed, consumerEvidencePending, probeFailed;
        int captureGeneration, completedGeneration = -1, snapshotGeneration = -1, evidenceSamples;
        float nextEvidence;

        internal QuestCameraVideoOutput(VideoPlayer player, string context)
        {
            this.player = player;
            this.context = context;
            Camera.onPostRender += AfterCamera;
            if (player != null)
            {
                Camera.onPreRender += BeforeCamera;
                GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoSampling += CompleteCapture;
                GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoSampled += SampleConsumer;
            }
        }

        void AfterCamera(Camera rendering)
        {
            if (!disposed && farOutput && rendering == snapshotCamera && cameraPixels != null
                && camera != null && CapturedTarget(camera))
                snapshotGeneration = captureGeneration;
        }

        internal static bool CapturedTarget(Camera candidate)
        {
            return GloomhavenVR.Core.QuestStandalonePlatform.IsFlatScreenVideoTarget(candidate);
        }

        void BeforeCamera(Camera rendering)
        {
            if (disposed) return;
            // Also serves the mono/suspended/split-fallback path, where the
            // stereo compositor has no active head hook. Its explicit hook runs
            // BEFORE the eye blits when stereo depth is active.
            GloomhavenVR.Core.QuestStandalonePlatform.PrepareFlatScreenVideoSample(rendering);
            if (consumerEvidencePending && rendering == GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera
                && camera != null)
            {
                Material consumer = GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoConsumer(camera);
                if (consumer != null && consumer.mainTexture == captured) SampleConsumer();
            }
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
                completion = new CommandBuffer { name = "GloomhavenVR.QuestCompletedCameraVideo" };
            }
            if (texture != decoded)
            {
                texture = decoded;
                material.mainTexture = decoded;
            }
            RenderTexture target = destination.targetTexture;
            Rect rect = destination.rect;
            Rect viewport = new Rect(rect.x * target.width, rect.y * target.height,
                rect.width * target.width, rect.height * target.height);
            Vector4 sampling = UvTransform(aspect, decoded.width, decoded.height,
                Mathf.Max(1, Mathf.RoundToInt(viewport.width)), Mathf.Max(1, Mathf.RoundToInt(viewport.height)),
                pixelNumerator, pixelDenominator);
            // B624's first Intro is a decoded camera-plane movie; the later
            // logos are ordinary Canvas sprites. The adapter draws raw clip
            // coordinates, bypassing the camera's RT projection adjustment.
            // On Vulkan this reverses the movie relative to those native UI
            // pixels. Derive the correction from this actual camera's GPU RT
            // projection, rather than flipping the screen or named clips.
            Matrix4x4 projection = destination.projectionMatrix;
            Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, true);
            if (gpuProjection.m11 * projection.m11 < 0f)
            { sampling.y = -sampling.y; sampling.w = 1f - sampling.w; }
            material.SetVector("_UvTransform", sampling);
            material.SetFloat("_Alpha", Mathf.Clamp01(alpha));
            bool far = mode == VideoRenderMode.CameraFarPlane;
            Camera finalCamera = far
                ? GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoFinalCamera(destination) : destination;
            material.SetFloat("_Plane", far ? 1f : 0f);
            material.SetInt("_ZTest", (int)(far
                ? SystemInfo.usesReversedZBuffer ? CompareFunction.GreaterEqual : CompareFunction.LessEqual
                : CompareFunction.Always));
            if (camera != destination || captured != target || captureViewport != viewport || farOutput != far
                || far && snapshotCamera != finalCamera)
            {
                Detach();
                camera = destination;
                captured = target;
                captureViewport = viewport;
                farOutput = far;
                snapshotGeneration = -1;
                completion.Clear();
                completion.SetRenderTarget(target,
                    RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                    target, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                completion.SetViewport(viewport);
                completion.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                if (target.useMipMap && !target.autoGenerateMips) completion.GenerateMips(target);
                if (far)
                {
                    if (cameraPixels != null) { cameraPixels.Release(); UnityEngine.Object.Destroy(cameraPixels); }
                    cameraPixels = new RenderTexture(target.width, target.height, 0, target.format,
                        target.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
                        { name = "GloomhavenVR.QuestCompletedMovieCamera", filterMode = FilterMode.Bilinear };
                    if (!cameraPixels.Create()) throw new InvalidOperationException("Completed movie camera capture could not be created.");
                    if (farDraw == null)
                    {
                        farDraw = new CommandBuffer { name = "GloomhavenVR.QuestFarCameraVideo" };
                        farSnapshot = new CommandBuffer { name = "GloomhavenVR.QuestCompletedFarCamera" };
                    }
                    // Far depth is valid inside this original camera's render,
                    // not guaranteed after its depth attachment is discarded.
                    // Keep foreground and image effects in the authored pass,
                    // then retain those actual completed pixels for consumption.
                    farDraw.Clear();
                    farDraw.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
                    farSnapshot.Clear();
                    if (ordinaryCopyMaterial == null) ordinaryCopyMaterial = GloomhavenVR.Core.QuestTextureCopy.CreateMaterial();
                    farSnapshot.Blit(BuiltinRenderTextureType.CurrentActive, cameraPixels, ordinaryCopyMaterial, 0);
                    camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, farDraw);
                    snapshotCamera = finalCamera;
                    snapshotCamera.AddCommandBuffer(CameraEvent.AfterEverything, farSnapshot);
                }
            }
            // Every producer render invalidates the completed sample. Near draws
            // once at consumption; Far retains its native-depth-composed pixels
            // and copies them. Neither path blends movie alpha a second time.
            unchecked { captureGeneration++; }
        }

        /// <summary>
        /// Camera events do not establish that Android's native camera-plane
        /// resolve has finished. B620 attached a valid buffer yet hardware still
        /// showed grey. Finish only this actual owned color/depth capture at its
        /// consumer boundary, before the existing eye shift/sampling. Late native
        /// output ordering remains a hypothesis until the bounded GPU evidence
        /// identifies it; no decoder, audio or original controller is replaced.
        /// </summary>
        internal void CompleteCapture()
        {
            try { CompleteCaptureCore(); }
            catch (Exception error)
            {
                Detach();
                if (!failed)
                { failed = true; Debug.LogError("[Quest startup] original movie completed output failed " + context + " " + error.Message); }
            }
        }

        void CompleteCaptureCore()
        {
            if (disposed || camera == null || !CapturedTarget(camera)
                || captured != camera.targetTexture || completion == null || texture == null)
                return;
            if (player != null && (!player.isActiveAndEnabled || !player.isPrepared
                || (!player.isPlaying && !player.isPaused) || player.frame < 0))
                return;
            if (farOutput && snapshotGeneration < 0) return; // never show an unrendered retained target
            if (completedGeneration == captureGeneration) return;
            completedGeneration = captureGeneration;
            bool evidence = player != null && GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging
                && evidenceSamples < 2 && Time.realtimeSinceStartup >= nextEvidence
                && player.frame >= Math.Max(2, (long)(player.frameRate * .75));
            if (evidence)
            {
                evidenceSamples++;
                // The original ambient movie begins with an authored dark fade.
                // Keep two sets, but sample its second one after that first fade
                // instead of spending both probes on its initial black frames.
                nextEvidence = Time.realtimeSinceStartup + (farOutput ? 4f : 1f);
                Probe(texture, "decoded");
                Probe(captured, "native-capture-before-composition");
                if (farOutput) Probe(cameraPixels, "native-far-composed-snapshot");
                Texture glass = GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoGlassCapture(camera);
                if (glass != null && glass != captured) Probe(glass, "independent-ui-glass-capture");
            }
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (farOutput)
                {
                    GloomhavenVR.Core.QuestTextureCopy.Copy(cameraPixels, captured);
                    // Unity already generates automatic mip chains on this
                    // blit's target. Its explicit GenerateMips API rejects
                    // autoGenerateMips targets (B624 logged this every frame).
                    if (captured.useMipMap && !captured.autoGenerateMips) captured.GenerateMips();
                }
                else Graphics.ExecuteCommandBuffer(completion);
            }
            finally { RenderTexture.active = previous; }
            if (evidence)
            {
                Probe(captured, "completed-capture");
                consumerEvidencePending = true;
            }
        }

        void SampleConsumer()
        {
            if (!consumerEvidencePending || disposed || camera == null || !CapturedTarget(camera)) return;
            consumerEvidencePending = false;
            Material consumer = GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoConsumer(camera);
            Debug.Log("[Quest startup] original movie consumer " + context
                    + " shader=" + (consumer == null ? "none" : consumer.shader.name)
                    + " texture=" + (consumer == null || consumer.mainTexture == null ? "none" : consumer.mainTexture.name)
                    + " headDepth=" + (GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera == null ? "none"
                        : GloomhavenVR.Core.QuestStandalonePlatform.HeadCamera.depth.ToString())
                    + " sourceDepth=" + camera.depth + " captureMips=" + captured.mipmapCount
                    + " captureFormat=" + captured.graphicsFormat + " captureDepth=" + captured.depth);
            if (consumer != null && consumer.mainTexture != null) Probe(consumer.mainTexture, "consumer-texture");
            if (consumer != null && consumer.HasProperty("_RightTex"))
            {
                Texture right = consumer.GetTexture("_RightTex");
                Debug.Log("[Quest startup] original movie consumer " + context
                    + " actualUrl=" + (player == null ? "none" : player.url)
                    + " stereoCapture=" + consumer.GetFloat("_StereoCapture")
                    + " rightTexture=" + (right == null ? "none" : right.name)
                    + " rightDimension=" + (right == null ? "none" : right.dimension.ToString()));
                if (right != null && right != consumer.mainTexture) Probe(right, "consumer-right-texture");
            }
        }

        void Probe(Texture source, string stage) => Probe(source, stage, !probeFailed && SystemInfo.supportsAsyncGPUReadback);

        // The explicit device decision permits the real GPU fixture to exercise
        // GLES devices without asynchronous readback. The caller bounds both
        // paths to two Debug evidence sets per movie; this is never a frame loop.
        internal void Probe(Texture source, string stage, bool asynchronous)
        {
            RenderTexture sample = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            string facts = context + " stage=" + stage + " frame=" + (player == null ? -1 : player.frame)
                + " mediaTime=" + (player == null ? -1 : player.time)
                + " actualUrl=" + (player == null ? "none" : player.url)
                + " source=" + source.name + " dimensions=" + source.width + "x" + source.height
                + " dimension=" + source.dimension + " format=" + source.graphicsFormat
                + " readbackMode=" + (asynchronous ? "async" : "bounded-sync");
            try
            {
                sample = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    { name = "GloomhavenVR.QuestMovieEvidence", filterMode = FilterMode.Point };
                if (!sample.Create()) throw new InvalidOperationException("Evidence texture could not be created.");
                RenderTexture previous = RenderTexture.active;
                try { GloomhavenVR.Core.QuestTextureCopy.Copy(source, sample); }
                finally { RenderTexture.active = previous; }
                if (!asynchronous)
                {
                    ReadSample(sample, facts, clock);
                    return;
                }
                RenderTexture requested = sample;
                AsyncGPUReadback.Request(sample, 0, TextureFormat.RGBA32, request =>
                {
                    try
                    {
                        if (request.hasError) throw new InvalidOperationException("GPU readback failed.");
                        ReportPixels(facts, request.GetData<Color32>().ToArray(), clock.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception error)
                    {
                        ProbeFailure(facts, error);
                        try
                        {
                            ReadSample(requested, facts.Replace("readbackMode=async", "readbackMode=bounded-sync-after-async-failure")
                                + " asyncElapsedMs=" + clock.Elapsed.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                                System.Diagnostics.Stopwatch.StartNew());
                        }
                        catch (Exception fallbackError) { ProbeFailure(facts + " fallback=failed", fallbackError); }
                    }
                    finally { requested.Release(); UnityEngine.Object.Destroy(requested); }
                });
                sample = null; // the scheduled callback now owns disposal
            }
            catch (Exception error) { ProbeFailure(facts, error); }
            finally
            {
                if (sample != null) { sample.Release(); UnityEngine.Object.Destroy(sample); }
            }
        }

        static void ReadSample(RenderTexture sample, string facts, System.Diagnostics.Stopwatch clock)
        {
            var cpu = new Texture2D(16, 8, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = sample;
                cpu.ReadPixels(new Rect(0, 0, 16, 8), 0, 0, false);
                cpu.Apply(false, false);
                ReportPixels(facts, cpu.GetPixels32(), clock.Elapsed.TotalMilliseconds);
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(cpu); }
        }

        static void ReportPixels(string facts, Color32[] pixels, double elapsedMs)
        {
            if (pixels.Length != 128) throw new InvalidOperationException("GPU readback pixel count differs.");
            int min = 255, max = 0, alphaMin = 255, alphaMax = 0;
            long sum = 0;
            uint hash = 2166136261;
            foreach (var pixel in pixels)
            {
                min = Math.Min(min, Math.Min(pixel.r, Math.Min(pixel.g, pixel.b)));
                max = Math.Max(max, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                alphaMin = Math.Min(alphaMin, pixel.a); alphaMax = Math.Max(alphaMax, pixel.a);
                sum += pixel.r + pixel.g + pixel.b;
                unchecked { hash = (hash ^ pixel.r) * 16777619; hash = (hash ^ pixel.g) * 16777619;
                    hash = (hash ^ pixel.b) * 16777619; hash = (hash ^ pixel.a) * 16777619; }
            }
            Debug.Log("[Quest startup] original movie pixels " + facts + " samples=" + pixels.Length
                + " rgbMin=" + min + " rgbMax=" + max + " rgbMean=" + sum / (pixels.Length * 3)
                + " alphaMin=" + alphaMin + " alphaMax=" + alphaMax + " hash=" + hash.ToString("x8")
                + " rightCenterRgba=" + pixels[76].r + "," + pixels[76].g + "," + pixels[76].b + "," + pixels[76].a
                + " readbackElapsedMs=" + elapsedMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }

        void ProbeFailure(string facts, Exception error)
        {
            if (probeFailed) return;
            probeFailed = true;
            Debug.LogWarning("[Quest startup] original movie pixels " + facts + " readback=failed detail=" + error.Message);
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
            if (camera != null)
            {
                if (farDraw != null) camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, farDraw);
            }
            if (snapshotCamera != null && farSnapshot != null)
                snapshotCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, farSnapshot);
            snapshotCamera = null;
            camera = null; captured = null;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Camera.onPreRender -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            Detach();
            if (completion != null) completion.Release();
            if (farDraw != null) farDraw.Release();
            if (farSnapshot != null) farSnapshot.Release();
            if (cameraPixels != null) { cameraPixels.Release(); UnityEngine.Object.Destroy(cameraPixels); }
            if (material != null) UnityEngine.Object.Destroy(material);
            if (ordinaryCopyMaterial != null) UnityEngine.Object.Destroy(ordinaryCopyMaterial);
            if (quad != null) UnityEngine.Object.Destroy(quad);
            GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoSampling -= CompleteCapture;
            GloomhavenVR.Core.QuestStandalonePlatform.FlatScreenVideoSampled -= SampleConsumer;
            completion = null; material = null; quad = null; texture = null;
            farDraw = null; farSnapshot = null; cameraPixels = null;
            ordinaryCopyMaterial = null;
        }
    }
}
#endif

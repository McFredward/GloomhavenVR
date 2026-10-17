using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core.SteamLink
{
    // Inert, externally supplied same-eye RT experiment. No runtime attachment,
    // game-alpha admission, stereo publication, video-frame or passthrough authority.
    // The caller owns every RT and its GPU lifetime; construct outside callbacks.
    internal sealed class ScenePayloadDraft : IDisposable
    {
        public const string Profile = "DRAFT-scene-contribution-atlas-v1";
        public const int CanvasWidth = 2112, CanvasHeight = 2752;
        public const int SceneSize = 2048, SceneX = 32, SceneY = 352;
        public const int CoverageSize = 1024, TileRows = 256, Halo = 16;
        public const int TileWidth = 1056, TileHeight = 288;
        public const int TopTileY = 32, BottomTileY = 2432, FooterY = 2720;
        private static readonly int CoverageTexture = Shader.PropertyToID("_CoverageTex");
        private readonly Material _pack, _decode;
        private bool _disposed;

        public ScenePayloadDraft(Shader pack, Shader decode)
        {
            if (pack == null || decode == null || !pack.isSupported || !decode.isSupported)
                throw new NotSupportedException("Draft packing shaders are unavailable.");
            _pack = new Material(pack);
            try { _decode = new Material(decode); }
            catch { UnityEngine.Object.Destroy(_pack); throw; }
        }

        // E.rgb is an externally admitted linear contribution, including emission
        // at A=0. Coverage is independent linear component G, never source alpha.
        // Both inputs are already resolved, immutable for these queued consumers.
        public void PackSameEye(RenderTexture contribution, RenderTexture coverage, RenderTexture atlas)
        {
            Running();
            Input(contribution, SceneSize, SceneSize);
            Input(coverage, SceneSize, SceneSize);
            Target(atlas, CanvasWidth, CanvasHeight);
            if (contribution == coverage || atlas == contribution || atlas == coverage)
                throw new ArgumentException("Draft source planes and atlas must be distinct.");
            _pack.SetTexture(CoverageTexture, coverage);
            Blit(contribution, atlas, _pack);
        }

        // Offline reconstruction for owned controls, not the native receiver.
        // The actual destination size is explicit; atlas allocation cannot raise
        // the resolution of an independently sized eye carrier.
        public void Decode(RenderTexture atlas, RenderTexture destination)
        {
            Running();
            Input(atlas, CanvasWidth, CanvasHeight);
            Target(destination, SceneSize, SceneSize);
            if (atlas == destination) throw new ArgumentException("Draft decode cannot alias.");
            Blit(atlas, destination, _decode);
        }

        private static void Target(RenderTexture target, int width, int height)
        {
            if (target == null || !target.IsCreated() || target.width != width || target.height != height
                || target.dimension != TextureDimension.Tex2D || target.antiAliasing != 1
                || target.format != RenderTextureFormat.ARGBFloat || target.sRGB
                || target.useMipMap || target.useDynamicScale)
                throw new ArgumentException("Draft RT requires exact linear single-sample RGBAFloat shape.");
        }

        private static void Input(RenderTexture source, int width, int height)
        {
            Target(source, width, height);
            if (source.filterMode != FilterMode.Bilinear || source.wrapMode != TextureWrapMode.Clamp)
                throw new ArgumentException("Draft input requires bilinear clamp without mipmaps.");
        }

        private static void Blit(Texture source, RenderTexture destination, Material material)
        {
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false; // This draft freezes a linear float-only storage experiment.
                Graphics.Blit(source, destination, material, 0);
            }
            finally { GL.sRGBWrite = srgb; RenderTexture.active = previous; }
        }

        private void Running()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ScenePayloadDraft));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // Normal Unity deferred destruction; caller closes entry and keeps
            // its queued RT consumers alive. No implicit GPU wait or RT release.
            UnityEngine.Object.Destroy(_pack);
            UnityEngine.Object.Destroy(_decode);
        }
    }
}

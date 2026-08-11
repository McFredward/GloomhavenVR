using UnityEditor;

namespace GloomhavenVR
{
    /// <summary>
    /// Import rules for the skybox-alternative panoramas (Assets/Bundle/Sky/*.jpg).
    /// Unity's default importer caps textures at 2048 — a 4096x2048 equirect panorama
    /// would ship half-resolution and visibly soft across a whole hemisphere. Panoramas
    /// keep their full 4096, mips on (the backdrop is minified at glance angles),
    /// clamped V (no pole wrap bleeding), trilinear.
    /// </summary>
    public sealed class SkyImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("Assets/Bundle/Sky/"))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.wrapModeU = UnityEngine.TextureWrapMode.Repeat;
            importer.wrapModeV = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.sRGBTexture = true;
        }
    }
}

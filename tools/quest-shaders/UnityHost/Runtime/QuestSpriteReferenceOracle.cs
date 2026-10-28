using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Read the packed SpriteAtlas through original Unity 2021.3.5 Windows assets.
/// The public sprite and its atlas relationship come from native PPtrs; no game
/// callbacks, guessed load names, or recovered texture rectangles run here.
/// </summary>
public sealed class QuestSpriteReferenceOracle : MonoBehaviour
{
    [Serializable] public sealed class Configuration
    {
        public int schema = 1;
        public string[] bundlePaths;
        public string spriteAssetName, atlasTag, spriteName, outputRoot;
        public string originalAtlasCab, originalSpriteCab;
        public long originalAtlasPathId, originalSpritePathId;
        public bool exportTextures = true;
    }
    [Serializable] public sealed class SpriteEvidence
    {
        public string name, textureName, packingMode, packingRotation, textureRectError;
        public int textureWidth, textureHeight, textureIndex;
        public bool packed;
        public float pixelsPerUnit;
        public Rect rect, textureRect;
        public Vector2 pivot, textureRectOffset;
        public Vector4 border;
        public Vector2[] vertices, uv;
        public ushort[] triangles;
    }
    [Serializable] public sealed class TextureEvidence
    {
        public string name, pngFile, pngSha256;
        public int width, height, instanceId;
    }
    [Serializable] public sealed class Receipt
    {
        public int schema = 1, spriteCount;
        public string unityVersion, graphicsDeviceType, sourceConfigurationSha256, atlasName, atlasTag;
        public bool originalWindowsAssetsRead, nativeCanBindObserved, headsetPictureVerified;
        public Configuration source;
        public SpriteEvidence publicSprite, requestedClone;
        public SpriteEvidence[] packedSprites;
        public TextureEvidence[] textures;
    }
    private Configuration _configuration;
    private readonly Dictionary<int, TextureEvidence> _textures = new Dictionary<int, TextureEvidence>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartOracle()
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "--quest-sprite-config");
        if (index < 0 || index + 1 >= args.Length) return;
        var oracle = new GameObject("OriginalSpriteAtlasOracle").AddComponent<QuestSpriteReferenceOracle>();
        oracle.StartCoroutine(oracle.Run(args[index + 1]));
    }
    private IEnumerator Run(string configPath)
    {
        try
        {
            _configuration = JsonUtility.FromJson<Configuration>(File.ReadAllText(configPath));
            if (_configuration == null || _configuration.schema != 1 || _configuration.bundlePaths == null ||
                _configuration.bundlePaths.Length == 0 || string.IsNullOrEmpty(_configuration.spriteAssetName) ||
                string.IsNullOrEmpty(_configuration.atlasTag) || string.IsNullOrEmpty(_configuration.spriteName))
                throw new InvalidOperationException("Native atlas oracle requires exact original identities and public asset route.");
            if (Application.unityVersion != "2021.3.5f1" || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
                throw new InvalidOperationException("Native atlas oracle requires original Unity 2021.3.5f1 and real D3D11 graphics.");
            Directory.CreateDirectory(_configuration.outputRoot);
            var bundles = _configuration.bundlePaths.Select(path => AssetBundle.LoadFromFile(path) ??
                throw new InvalidOperationException("Original asset bundle did not load: " + path)).ToArray();
            var sprite = bundles[bundles.Length - 1].LoadAsset<Sprite>(_configuration.spriteAssetName);
            if (sprite == null) throw new InvalidOperationException("Exact public original Sprite asset is missing.");
            var matches = Resources.FindObjectsOfTypeAll<SpriteAtlas>().Where(atlas => atlas.tag == _configuration.atlasTag && atlas.CanBindTo(sprite)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Native SpriteAtlas binding is missing or ambiguous: " + matches.Length);
            var atlas = matches[0];
            var clone = atlas.GetSprite(_configuration.spriteName);
            if (clone == null) throw new InvalidOperationException("Native atlas GetSprite returned no original clone.");
            var all = new Sprite[atlas.spriteCount];
            int count = atlas.GetSprites(all);
            if (count != all.Length || all.Any(item => item == null)) throw new InvalidOperationException("Native atlas packed sprite enumeration is incomplete.");
            var receipt = new Receipt {
                unityVersion = Application.unityVersion, graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                sourceConfigurationSha256 = Hash(File.ReadAllBytes(configPath)), source = _configuration,
                originalWindowsAssetsRead = true, nativeCanBindObserved = true, headsetPictureVerified = false,
                atlasName = atlas.name, atlasTag = atlas.tag, spriteCount = count,
                publicSprite = ReadSprite(sprite), requestedClone = ReadSprite(clone),
                packedSprites = all.Select(ReadSprite).ToArray(), textures = _textures.Values.ToArray()
            };
            if (receipt.requestedClone.textureWidth <= 0 || receipt.requestedClone.uv.Length == 0 ||
                receipt.requestedClone.uv.All(value => value == Vector2.zero))
                throw new InvalidOperationException("Original native atlas clone has no bound packed texture/UV evidence.");
            File.WriteAllText(Path.Combine(_configuration.outputRoot, "original-sprite-atlas.json"), JsonUtility.ToJson(receipt, true) + "\n");
            Debug.Log("PASS native SpriteAtlas oracle: " + atlas.name + ", sprites=" + count + ", textures=" + _textures.Count);
            Application.Quit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (_configuration != null && !string.IsNullOrEmpty(_configuration.outputRoot))
                File.WriteAllText(Path.Combine(_configuration.outputRoot, "original-sprite-atlas-error.txt"), error.ToString());
            Application.Quit(1);
        }
        yield break;
    }
    private SpriteEvidence ReadSprite(Sprite sprite)
    {
        var texture = sprite.texture;
        int id = texture == null ? 0 : texture.GetInstanceID();
        if (texture != null && !_textures.ContainsKey(id))
        {
            var evidence = new TextureEvidence { name = texture.name, width = texture.width, height = texture.height, instanceId = id };
            if (_configuration.exportTextures)
            {
                evidence.pngFile = "native-atlas-texture-" + _textures.Count + ".png";
                var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active;
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                var pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                byte[] png = pixels.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(_configuration.outputRoot, evidence.pngFile), png);
                evidence.pngSha256 = Hash(png);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(pixels);
            }
            _textures.Add(id, evidence);
        }
        var result = new SpriteEvidence {
            name = sprite.name, packed = sprite.packed, packingMode = sprite.packingMode.ToString(), packingRotation = sprite.packingRotation.ToString(),
            rect = sprite.rect, pivot = sprite.pivot, border = sprite.border, pixelsPerUnit = sprite.pixelsPerUnit,
            vertices = sprite.vertices, uv = sprite.uv, triangles = sprite.triangles, textureIndex = id,
            textureName = texture == null ? null : texture.name, textureWidth = texture == null ? 0 : texture.width,
            textureHeight = texture == null ? 0 : texture.height
        };
        try { result.textureRect = sprite.textureRect; result.textureRectOffset = sprite.textureRectOffset; }
        catch (Exception error) { result.textureRectError = error.Message; }
        return result;
    }
    private static string Hash(byte[] bytes)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}

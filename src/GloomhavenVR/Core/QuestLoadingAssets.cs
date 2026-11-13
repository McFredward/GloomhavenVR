using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// One small owned Android shader, carried by compact code updates. Keeping it
/// inside the plugin leaves existing main/town/voice/environment banks untouched.
/// It is resolved on the ordinary startup thread, never inside an eye callback.
/// </summary>
internal static class QuestLoadingAssets
{
    internal const string BundleResource = "GloomhavenVR.QuestLoading.spinner.bundle";
    internal const string ProvenanceResource = "GloomhavenVR.QuestLoading.provenance.json";
    private const string AssetPath = "assets/questloading/questloadingspinner.shader";
    private const string ShaderName = "GloomhavenVR/QuestLoadingSpinner";
    private const int MaximumBankBytes = 2 * 1024 * 1024;
    private static AssetBundle? _bank;
    private static Shader? _shader;
    private static string? _failure;

    internal static Shader? ResolveShader()
    {
        // The early startup HUD can precede platform adoption. That is a normal
        // retry boundary, not a permanently missing-resource failure. Desktop
        // never reads or loads the Android-only embedded bank.
        if (!QuestStandalonePlatform.Enabled) return null;
        if (_shader != null && _shader.isSupported) return _shader;
        if (_failure != null) return null;
        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan)
                throw new NotSupportedException("The Quest loading bank requires its Android Vulkan graphics API.");
            if (UnityEngine.XR.XRSettings.stereoRenderingMode != UnityEngine.XR.XRSettings.StereoRenderingMode.MultiPass)
                throw new NotSupportedException("The Quest loading bank is qualified for the current MultiPass eye routing only.");
            byte[] provenance = ReadResource(ProvenanceResource, 32768);
            var contract = QuestEnvironmentJson.Read(provenance);
            if (QuestEnvironmentJson.Integer(contract, "schema") != 1
                || QuestEnvironmentJson.String(contract, "unityVersion") != "2021.3.5f1"
                || QuestEnvironmentJson.String(contract, "target") != "Android"
                || QuestEnvironmentJson.String(contract, "graphicsApi") != "Vulkan"
                || QuestEnvironmentJson.String(contract, "colorSpace") != "Linear"
                || QuestEnvironmentJson.String(contract, "stereoRenderingPath") != "MultiPass"
                || QuestEnvironmentJson.String(contract, "shaderName") != ShaderName
                || QuestEnvironmentJson.String(contract, "assetPath") != AssetPath
                || QuestEnvironmentJson.String(contract, "bundleName") != "ghvr-quest-loading.bundle"
                || QuestEnvironmentJson.String(contract, "resource") != BundleResource
                || !QuestEnvironmentJson.Boolean(contract, "typeTreesEnabled")
                || !QuestEnvironmentJson.Boolean(contract, "chunkBasedCompression"))
                throw new InvalidDataException("The embedded Quest loading shader contract is invalid.");
            byte[] bytes = ReadResource(BundleResource, MaximumBankBytes);
            using var hash = SHA256.Create();
            string digest = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            if (QuestEnvironmentJson.Integer(contract, "bundleBytes") != bytes.Length
                || QuestEnvironmentJson.String(contract, "bundleSha256") != digest)
                throw new InvalidDataException("The embedded Quest loading bank differs from its compiled provenance.");
            _bank = AssetBundle.LoadFromMemory(bytes);
            if (_bank == null || _bank.name != "ghvr-quest-loading.bundle")
                throw new InvalidDataException("The embedded Quest loading bank is unavailable or has the wrong identity.");
            string[] names = _bank.GetAllAssetNames();
            if (names.Length != 1 || names[0] != AssetPath)
                throw new InvalidDataException("The embedded Quest loading bank has unexpected assets.");
            _shader = _bank.LoadAsset<Shader>(AssetPath);
            if (_shader == null || _shader.name != ShaderName || !_shader.isSupported)
                throw new NotSupportedException("The embedded Quest loading shader is unavailable or unsupported.");
            VRLog.Note("Compat", "Quest loading shader ready from its verified embedded Android Vulkan bank.");
            return _shader;
        }
        catch (Exception error)
        {
            _shader = null;
            if (_bank != null) _bank.Unload(true);
            _bank = null;
            _failure = error.GetType().Name + ": " + error.Message;
            VRLog.Note("Compat", "Quest loading shader unavailable; native loading artwork fallback retained (" + _failure + ").");
            return null;
        }
    }

    private static byte[] ReadResource(string name, int maximum)
    {
        using Stream? resource = typeof(QuestLoadingAssets).Assembly.GetManifestResourceStream(name);
        if (resource == null || resource.Length <= 0 || resource.Length > maximum)
            throw new InvalidDataException("The embedded Quest loading resource is missing or excessive: " + name);
        var bytes = new byte[(int)resource.Length];
        int copied = 0;
        while (copied < bytes.Length)
        {
            int count = resource.Read(bytes, copied, bytes.Length - copied);
            if (count == 0) throw new EndOfStreamException("The embedded Quest loading resource is truncated: " + name);
            copied += count;
        }
        return bytes;
    }
}

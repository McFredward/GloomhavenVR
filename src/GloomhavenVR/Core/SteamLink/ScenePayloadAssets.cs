using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core.SteamLink;

/// <summary>Small owned shader bank; loading never happens in an eye callback.</summary>
internal static class ScenePayloadAssets
{
    internal const string Resource = "GloomhavenVR.SteamLink.scene.bundle";
    private static AssetBundle? _bank;
    private static Shader? _pack, _coverage;
    private static string? _failure;
    private static readonly Dictionary<string, Shader> Variants = new(StringComparer.Ordinal);

    internal static bool TryGet(out Shader pack, out Shader coverage, out string reason)
    {
        if (_pack != null && _coverage != null && _pack.isSupported && _coverage.isSupported)
        {
            pack = _pack; coverage = _coverage; reason = string.Empty; return true;
        }
        if (_failure == null)
        {
            try
            {
                using Stream? resource = typeof(ScenePayloadAssets).Assembly.GetManifestResourceStream(Resource);
                if (resource == null || resource.Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("Steam Link scene shader bank is missing or oversized.");
                using var bytes = new MemoryStream();
                resource.CopyTo(bytes);
                _bank = AssetBundle.LoadFromMemory(bytes.ToArray());
                if (_bank == null) throw new InvalidDataException("Steam Link scene shader bank could not be loaded.");
                _pack = _bank.LoadAsset<Shader>("assets/steamlink/steamlinksceneatlas.shader");
                _coverage = _bank.LoadAsset<Shader>("assets/steamlink/steamlinkscenecoverage.shader");
                if (_pack == null || _coverage == null || !_pack.isSupported || !_coverage.isSupported)
                    throw new NotSupportedException("Steam Link scene shaders are unavailable on this graphics API.");
                foreach (Shader shader in _bank.LoadAllAssets<Shader>())
                {
                    const string suffix = ".SceneCoverage";
                    if (shader.isSupported && shader.name.EndsWith(suffix, StringComparison.Ordinal))
                        Variants.Add(shader.name.Substring(0, shader.name.Length - suffix.Length), shader);
                }
            }
            catch (Exception ex)
            {
                _pack = _coverage = null;
                if (_bank != null) _bank.Unload(true);
                _bank = null;
                Variants.Clear();
                _failure = ex.Message;
            }
        }
        pack = _pack!; coverage = _coverage!; reason = _failure ?? string.Empty;
        return _failure == null;
    }

    internal static Shader? FindVariant(string originalShader) =>
        Variants.TryGetValue(originalShader, out Shader shader) ? shader : null;
}

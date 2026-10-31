using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>Choose actual eye capacity once, before the player's display starts.</summary>
internal static class QuestEyeAllocation
{
    private const int MaximumConfigBytes = 256 * 1024;
    // Match the existing user-facing resolution range. B675's 1.0 ceiling was
    // conservative during the distorted-viewport investigation; it also silently
    // discarded every supersampling choice at the next launch. Only the stopped
    // display receives this value. The shared mobile default remains unchanged.
    internal const float MinimumScale = 0.5f;
    internal const float MaximumScale = 2f;

    internal static float ClampScale(float requested) =>
        float.IsNaN(requested) || float.IsInfinity(requested)
            ? QuestStandalonePlatform.StandaloneEyeResolutionDefault
            : Mathf.Clamp(requested, MinimumScale, MaximumScale);
    // The maintainer's Frame B661 capture (steam_frame/Player.log:362,432)
    // submits 2728x2728 at saved 0.8. Quest B678 actually submits 1344x1408 at 0.8
    // and 1680x1760 at 1.0: identical provider fractions are not comparable image
    // density. Make the shared row a Frame-calibrated density on Quest only.
    // Preserve the Quest runtime's aspect and match its shorter axis to the
    // captured Frame reference (3410 = 2728/.8), without changing saved values.
    // Meta documents 1680x1760 as Quest 3's provider default; the actual submitted
    // pixels remain observable separately. This is a raster-density reference,
    // never a promise of identical optics, FOV, texture quality or performance.
    internal const int ReferencePixelsAtOne = 3410;
    internal const int Quest3DefaultWidth = 1680;
    internal const int Quest3DefaultHeight = 1760;
    internal static float ProviderScale(float requested) =>
        ClampScale(requested) * (ReferencePixelsAtOne / (float)Quest3DefaultWidth);
    private static bool _attempted;

    internal static void Prepare(string configPath)
    {
        if (_attempted) return;
        _attempted = true;
        float requested = ReadSavedScale(configPath, out string source);
        float providerScale = ProviderScale(requested);
        var displays = new List<XRDisplaySubsystem>(1);
        int prepared = 0;
        string outcome = "no initialized display; retaining provider capacity";
        try
        {
            SubsystemManager.GetInstances(displays);
            // A live/reused session must never receive an allocation setter, even
            // if another stopped display is present. The normal Update controller
            // can restore a full viewport once; it cannot recreate live resources.
            foreach (XRDisplaySubsystem display in displays)
                if (display.running)
                {
                    outcome = "display already running; retaining provider capacity";
                    return;
                }
            foreach (XRDisplaySubsystem display in displays)
            {
                display.scaleOfAllRenderTargets = providerScale;
                if (Mathf.Abs(display.scaleOfAllViewports - 1f) > 0.0005f)
                    display.scaleOfAllViewports = 1f;
                prepared++;
                outcome = "allocation=" + display.scaleOfAllRenderTargets.ToString("F2", CultureInfo.InvariantCulture)
                    + " viewport=" + display.scaleOfAllViewports.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        catch (Exception error)
        {
            // Unsupported/missing provider APIs are a bounded compatibility
            // refusal, never a reason to try the dangerous live allocation path.
            outcome = "startup allocation unavailable (" + error.GetType().Name + "); retaining provider capacity";
        }
        finally
        {
            UnityEngine.Debug.Log("[Quest startup] eye allocation before display start: requested="
                + requested.ToString("F2", CultureInfo.InvariantCulture)
                + " providerRequested=" + providerScale.ToString("F4", CultureInfo.InvariantCulture)
                + " referenceShortAxis=" + ReferencePixelsAtOne
                + " questDefault=" + Quest3DefaultWidth + "x" + Quest3DefaultHeight
                + " source=" + source
                + " prepared=" + prepared + " " + outcome + ". Saved configuration is unchanged.");
        }
    }

    internal static float ReadSavedScale(string path, out string source)
    {
        float fallback = QuestStandalonePlatform.StandaloneEyeResolutionDefault;
        source = "shared-mobile-default";
        try
        {
            if (!File.Exists(path)) return fallback;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > MaximumConfigBytes)
            {
                source = "oversized-config-default";
                return fallback;
            }
            // A file may grow after its length was sampled. Read one bounded
            // snapshot rather than following a concurrently appended config.
            byte[] bytes = new byte[MaximumConfigBytes + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = file.Read(bytes, count, bytes.Length - count)) > 0)
                count += read;
            if (count > MaximumConfigBytes)
            {
                source = "oversized-config-default";
                return fallback;
            }
            using var snapshot = new MemoryStream(bytes, 0, count, false);
            using var reader = new StreamReader(snapshot);
            bool renderQuality = false;
            string? value = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string text = line.Trim();
                if (text.Length == 0 || text[0] == '#' || text[0] == ';') continue;
                if (text[0] == '[' && text[text.Length - 1] == ']')
                {
                    renderQuality = text == "[RenderQuality]";
                    continue;
                }
                if (!renderQuality) continue;
                int equals = text.IndexOf('=');
                if (equals >= 0 && text.Substring(0, equals).Trim() == "EyeResolutionScale")
                    value = text.Substring(equals + 1).Trim();
            }
            if (value == null) return fallback;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float saved)
                || float.IsNaN(saved) || float.IsInfinity(saved))
            {
                source = "invalid-config-default";
                return fallback;
            }
            source = "saved-rig-config";
            return ClampScale(saved);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            source = "unreadable-config-default";
            return fallback;
        }
    }
}

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
    private static bool _attempted;

    internal static void Prepare(string configPath)
    {
        if (_attempted) return;
        _attempted = true;
        float requested = ReadSavedScale(configPath, out string source);
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
                display.scaleOfAllRenderTargets = requested;
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
                + requested.ToString("F2", CultureInfo.InvariantCulture) + " source=" + source
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
            return Mathf.Clamp(saved, 0.5f, 1f);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            source = "unreadable-config-default";
            return fallback;
        }
    }
}

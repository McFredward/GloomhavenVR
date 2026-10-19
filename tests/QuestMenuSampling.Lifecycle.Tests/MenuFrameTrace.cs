using System;
using System.Collections;
using System.IO;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

// This oracle reads the finished head target, not the readiness predicate or a
// renderer callback. A valid state/texture reference is deliberately insufficient:
// either actual menu pixels or the actual original spinner must survive the draw.
internal static class MenuFrameTrace
{
    [Serializable]
    private sealed class Frame
    {
        public string phase = "";
        public int frame, illuminated, white, sourceIlluminated;
        public bool receipt, spinnerActive;
    }

    internal static int Samples { get; private set; }
    private static readonly string? Output = Environment.GetEnvironmentVariable("QUEST_LOADING_FRAME_REPORT");
    private static readonly string? Selected = Environment.GetEnvironmentVariable("QUEST_LOADING_HANDOFF_CASE");
    internal static bool Includes(string name) => string.IsNullOrEmpty(Selected) || Selected == name;

    internal static IEnumerator Covered(string phase, Camera ui, Camera head, RenderTexture source, RenderTexture target,
        GameObject spinner, Action<bool, string> check, Camera? overwriter = null)
    {
        int blankFrames = 0;
        bool receipt = false;
        for (int i = 0; i < 24; i++)
        {
            // Preserve the real producer -> optional later writer -> real consumer
            // order. The driver still runs production Tick in Unity's Update.
            ui.Render(); if (overwriter != null) overwriter.Render(); head.Render();
            Frame sample = Capture(phase, source, target, spinner);
            receipt |= sample.receipt;
            if (sample.illuminated < 3) blankFrames++;
            if (i == 0 || (sample.illuminated < 3 && blankFrames == 1)) Save(target, phase + "-frame-" + i);
            yield return new WaitForSecondsRealtime(.02f);
        }
        check(!receipt && blankFrames == 0,
            "Final composed " + phase + " cannot retire the spinner before real menu pixels; receipts=" + receipt + " blankFrames=" + blankFrames);
    }

    internal static void Visible(string phase, Camera ui, Camera head, RenderTexture source, RenderTexture target,
        GameObject spinner, Action<bool, string> check)
    {
        ui.Render(); head.Render();
        Frame sample = Capture(phase, source, target, spinner);
        Save(target, phase);
        // The native opaque white window occupies a large part of this final
        // head picture; a small white original spinner cannot pass this count.
        check(sample.sourceIlluminated > 256 && sample.white > 500,
            "The real original menu Graphic reaches the final composed head pixels");
    }

    private static Frame Capture(string phase, RenderTexture source, RenderTexture target, GameObject spinner)
    {
        var sample = new Frame { phase = phase, frame = Time.frameCount,
            receipt = FlatScreen.QuestMenuPresented, spinnerActive = spinner != null && spinner.activeInHierarchy };
        Read(source, out sample.sourceIlluminated, out _);
        Read(target, out sample.illuminated, out sample.white);
        Samples++;
        if (!string.IsNullOrEmpty(Output)) File.AppendAllText(Path.Combine(Output, "menu-frame-trace.jsonl"), JsonUtility.ToJson(sample) + "\n");
        return sample;
    }

    private static void Read(RenderTexture target, out int illuminated, out int white)
    {
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false);
        try
        {
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); copy.Apply(false);
            illuminated = white = 0;
            foreach (Color32 pixel in copy.GetPixels32())
            {
                if (pixel.r > 5 || pixel.g > 5 || pixel.b > 5) illuminated++;
                if (pixel.r > 220 && pixel.g > 220 && pixel.b > 220 && pixel.a > 127) white++;
            }
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }

    private static void Save(RenderTexture target, string role)
    {
        if (string.IsNullOrEmpty(Output)) return;
        RenderTexture previous = RenderTexture.active;
        var copy = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, false);
        try
        {
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); copy.Apply(false);
            File.WriteAllBytes(Path.Combine(Output, role + ".png"), copy.EncodeToPNG());
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(copy); }
    }
}

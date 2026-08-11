using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 14 — CARD BAND PIXEL CAPTURE: measure what the EYE sees, instead of listing candidates.
///
/// <para>WHY. Thirteen rounds of inventories judged the band by DEDUCTION — sprite pixels,
/// material colors, rect overlaps — and the ModBuild-117 run shows the limit of that method: the
/// hand-fan inventory acquitted everything ("no active dark band painter in THIS state on THIS
/// rig") while karten4.png plainly shows a black border around every fan card, and the tray
/// verdict convicted the rest-button 'Base' plates that sit entirely LEFT of the card. An
/// inventory can only name what its model of the renderer covers; a PIXEL READBACK measures the
/// composed result of everything — face layers, slab, liners, floors, backdrops, shaders,
/// lighting — with no model in between.</para>
///
/// <para>WHAT. At the same latch/re-arm moments as <see cref="CardBandPainter"/> (it calls in at
/// the end of each report — one fan card and one tray card, max a handful per session), a
/// temporary probe camera is placed on the card's face normal (viewer side), orthographic,
/// framing the face rect plus <see cref="Margin"/> (25 %) on every side, culling nothing the
/// player's camera would see (it copies the live head camera's cullingMask). It renders the
/// ACTUAL in-situ card to an offscreen ~256 px RenderTexture, reads the pixels back, and samples
/// the four band strips (top/bottom/left/right, mid-band — the band being outside the derived
/// art outline, the same definition the painter inventory uses) plus control points: the card
/// centre (should be bright art) and two points 15 % OUTSIDE the card (whatever backs it — the
/// liner on a tray, the backdrop in the fan). One <c>CARD BAND PIXELS</c> line logs the mean
/// RGBA per strip and per control plus a verdict clause comparing each strip against the known
/// signatures:</para>
/// <list type="bullet">
/// <item>recess floor ≈ (25,21,18) — near-black WARM (r−b positive);</item>
/// <item>liner/keycap wood — material luma ≈ 0.38, warm (and since round 14 the retinted slab
///   front/rim, <c>CardMesh.EdgeColor</c> warm umber, lands in this family too — so a NEAR-BLACK
///   strip can no longer be the slab);</item>
/// <item>printed game frame — dark COOL (&lt;40, r−b ≈ 0 or negative);</item>
/// <item>green-screen MR backdrop — saturated green;</item>
/// <item>the probe's own clear sentinel (magenta) — NOTHING rendered there. In MR passthrough
///   the compositor backs such pixels with the real room, which no scene camera can reproduce;
///   the line says so explicitly instead of guessing.</item>
/// </list>
///
/// <para>GEOMETRY NOTE, recorded here because round 14's brief asked for the reconciliation: the
/// backing slab does NOT exceed the face. <c>VRCard.SetCanvasSize</c> fits it to
/// facePixels × fit × VisibleFaceFraction — exactly the RENDERED art rect — and the inventory's
/// face-normalized space is the adopted game face root, itself drawn at the same 0.94 fit inside
/// the canvas. Slab and face rect are therefore the SAME rectangle, which is why the inventory
/// measures 'Backing' at exactly x 0.00..1.00: the "slab = 1.064× face" reading assumed a
/// full-canvas slab over a 0.94 art rect, which is not what SetCanvasSize builds.</para>
///
/// <para>COST DISCIPLINE. Runs only when the painter inventory itself runs (initial latch +
/// change-gated re-arms, ≤ 10 per session in total). Full try/catch/finally: the temporary RT is
/// released, the readback texture and camera destroyed, on every path. No live material, layer
/// or renderer is touched — the scene renders once more from one extra viewpoint, that is all.</para>
///
/// <para>ROUND 15 — THE INSTRUMENT WAS BLIND, AND SAID SO IN THE WRONG SENTENCE. User verdict on
/// ModBuild 118, verbatim: "Immer noch die schwarzen Kartenränder statt das outline der Karten
/// selber als mesh grenze. Logs liegen." In that log EVERY sample of EVERY capture — including
/// the card centre — hit the clear sentinel: the probe rendered nothing at all. Root cause, read
/// straight from the line itself: <c>cull = 'ScenarioCamera' mask 0x700FFF17</c>. The mask was
/// copied from <c>Camera.main</c> (the GAME's scenario camera), but the cards are mod-owned
/// visuals on the mod layer (that run: layer 27, mask 0x08000000 — "Mod layer resolved: 27"),
/// which is NOT in 0x700FFF17; the mod's own <c>GloomhavenVR.HeadCamera</c> is the camera that
/// actually renders them (it ORs <c>VRLayers.ModLayerMask</c> into its mask). So the probe
/// culled the card, the slab, the liner AND the backdrop, cleared to magenta, and then the
/// verdict path still printed "centre is bright art as expected" — a self-contradiction on a
/// sentinel centre. Three fixes, all here:</para>
/// <list type="number">
/// <item>MASK: taken from <c>Rig.VRRigDriver.HeadCamera</c> (the camera that provably renders
///   the cards), UNIONED with every layer actually present under the card's own subtree (walked,
///   not assumed) — so neither a future layer policy change nor an adopted game canvas on a
///   game layer can blind the probe again.</item>
/// <item>RETRY + LOUD FAILURE: if the centre still reads the sentinel after the first render,
///   one retry with <c>cullingMask = ~0</c>; if STILL sentinel, a Warn with the camera's
///   position/rotation/mask/planes and a census of every layer under the card root — and the
///   verdict states the instrument failed instead of acquitting the scene.</item>
/// <item>SCAN LINE: one horizontal scan across the full framed width at card mid-height,
///   run-length-encoded into quantized colour bands (max ~15 runs) — the exact analysis that
///   cracked the karten screenshots offline (colour runs with widths), now in every log.</item>
/// </list>
/// </summary>
internal static class CardBandPixelCapture
{
    /// <summary>Frame margin around the face rect, per side, as a fraction of the face.</summary>
    private const float Margin = 0.25f;

    /// <summary>Probe distance from the face along its normal (orthographic — arbitrary, but it
    /// must clear the card's own proud widgets; 25 cm does).</summary>
    private const float ProbeDistance = 0.25f;

    /// <summary>RT height in pixels (~256 px; width follows the face aspect).</summary>
    private const int RtHeight = 256;

    /// <summary>Samples per band strip.</summary>
    private const int StripSamples = 24;

    /// <summary>Clear sentinel: magenta, so "nothing rendered" is unmistakable in the readback
    /// (no card layer, floor or backdrop is magenta).</summary>
    private static readonly Color Sentinel = new(1f, 0f, 1f, 0f);

    private static bool s_errorLogged;

    /// <summary>
    /// Capture and log one <c>CARD BAND PIXELS</c> line for the given card face. Band fractions
    /// are the caller's (the derived-outline bands the painter inventory already resolved).
    /// Never throws.
    /// </summary>
    internal static void Capture(string context, string reason, RectTransform faceRoot,
                                 Rect faceRect, float bandL, float bandR, float bandB, float bandT)
    {
        GameObject? camGo = null;
        RenderTexture? rt = null;
        Texture2D? readback = null;
        RenderTexture? prevActive = null;
        try
        {
            // ---- frame the face in world space --------------------------------------------
            Vector3 bl = faceRoot.TransformPoint(new Vector3(faceRect.xMin, faceRect.yMin, 0f));
            Vector3 br = faceRoot.TransformPoint(new Vector3(faceRect.xMax, faceRect.yMin, 0f));
            Vector3 tl = faceRoot.TransformPoint(new Vector3(faceRect.xMin, faceRect.yMax, 0f));
            Vector3 center = faceRoot.TransformPoint(new Vector3(faceRect.center.x, faceRect.center.y, 0f));
            float faceWWorld = (br - bl).magnitude;
            float faceHWorld = (tl - bl).magnitude;
            if (faceWWorld < 1e-4f || faceHWorld < 1e-4f)
                return; // degenerate face — nothing measurable

            // The module convention is +Z away from the viewer, so the viewer side is −forward.
            Vector3 normal = faceRoot.forward;

            // ROUND 15 MASK FIX (see the class header): the mod's OWN head camera is the one that
            // renders the cards — Camera.main is the game's ScenarioCamera, whose mask excludes
            // the mod layer and blinded every 118 capture. Belt and braces: union the head mask
            // with every layer actually present under the card's own subtree, so the probe sees
            // whatever the card is made of even if a layer policy changes underneath it.
            Camera? eye = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
            Transform subtreeRoot = faceRoot;
            for (int up = 0; up < 3 && subtreeRoot.parent != null; up++)
                subtreeRoot = subtreeRoot.parent;
            var layerCensus = new Dictionary<int, int>();
            CountLayers(subtreeRoot, layerCensus);
            int subtreeMask = 0;
            foreach (KeyValuePair<int, int> kv in layerCensus)
                subtreeMask |= 1 << kv.Key;
            int cullingMask = (eye != null ? eye.cullingMask : ~0) | subtreeMask;

            camGo = new GameObject("GloomhavenVR.CardBandPixelProbe");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; // manual Render() only — never enters the normal camera loop
            cam.transform.SetPositionAndRotation(
                center - normal * ProbeDistance,
                Quaternion.LookRotation(normal, faceRoot.up));
            cam.orthographic = true;
            cam.orthographicSize = faceHWorld * 0.5f * (1f + 2f * Margin);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = ProbeDistance + 5f; // deep enough for floors/backdrops behind the card
            cam.cullingMask = cullingMask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sentinel;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;

            int rtWidth = Mathf.Clamp(Mathf.RoundToInt(RtHeight * (faceWWorld / faceHWorld)), 32, 512);
            rt = RenderTexture.GetTemporary(rtWidth, RtHeight, 24, RenderTextureFormat.ARGB32);
            // Target BEFORE Render() (XR discipline — stereoTargetEye is already None above, so
            // this manual Render() is a plain mono offscreen pass regardless of the HMD state).
            cam.targetTexture = rt;
            readback = new Texture2D(rtWidth, RtHeight, TextureFormat.RGBA32, mipChain: false);
            RenderTexture rtLocal = rt;       // non-nullable views for the local function below
            Texture2D readbackLocal = readback;

            Color32[] RenderAndRead()
            {
                cam.Render();
                prevActive = RenderTexture.active;
                RenderTexture.active = rtLocal;
                readbackLocal.ReadPixels(new Rect(0, 0, rtWidth, RtHeight), 0, 0, recalculateMipMaps: false);
                readbackLocal.Apply(updateMipmaps: false);
                RenderTexture.active = prevActive;
                prevActive = null;
                return readbackLocal.GetPixels32();
            }

            Color32[] pixels = RenderAndRead();

            // ---- sample the strips and controls -------------------------------------------
            // Face-normalized (nx, ny) → world → probe screen px, via the camera itself so no
            // hand-rolled projection can disagree with what was rendered.
            Vector4 MeanOf((float x, float y)[] points)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                int counted = 0;
                foreach ((float nx, float ny) in points)
                {
                    Vector3 world = faceRoot.TransformPoint(new Vector3(
                        faceRect.xMin + nx * faceRect.width,
                        faceRect.yMin + ny * faceRect.height, 0f));
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    int px = Mathf.Clamp(Mathf.RoundToInt(sp.x), 0, rtWidth - 1);
                    int py = Mathf.Clamp(Mathf.RoundToInt(sp.y), 0, RtHeight - 1);
                    Color32 c = pixels[py * rtWidth + px];
                    r += c.r; g += c.g; b += c.b; a += c.a;
                    counted++;
                }
                return counted > 0 ? new Vector4(r / counted, g / counted, b / counted, a / counted)
                                   : Vector4.zero;
            }

            (float, float)[] Strip(bool horizontal, float at)
            {
                var pts = new (float, float)[StripSamples];
                for (int i = 0; i < StripSamples; i++)
                {
                    // Sample between the side bands so a corner never contaminates a strip mean.
                    float t = Mathf.Lerp(horizontal ? bandL : bandB,
                                         horizontal ? 1f - bandR : 1f - bandT,
                                         (i + 0.5f) / StripSamples);
                    pts[i] = horizontal ? (t, at) : (at, t);
                }
                return pts;
            }

            // ROUND 15 HARDENING: the centre is the canary — a card face is ALWAYS rendered
            // geometry, so a sentinel centre means the INSTRUMENT failed, not the scene. Retry
            // once with an everything mask; if still blind, log the full camera state + layer
            // census loudly and let the verdict say "instrument", never "scene".
            Vector4 centre = MeanOf(new[] { (0.5f, 0.5f) });
            bool retriedWideOpen = false;
            bool centreSentinel = IsSentinel(centre);
            if (centreSentinel)
            {
                retriedWideOpen = true;
                cam.cullingMask = ~0;
                pixels = RenderAndRead();
                centre = MeanOf(new[] { (0.5f, 0.5f) });
                centreSentinel = IsSentinel(centre);
                if (centreSentinel)
                {
                    VRLog.Warn("Cards", $"CARD BAND PIXELS ({context}): PROBE BLIND — the card centre still " +
                                        "reads the clear sentinel even with cullingMask ~0 (retry). Camera state: " +
                                        $"pos {cam.transform.position}, rot {cam.transform.rotation.eulerAngles}, " +
                                        $"ortho size {cam.orthographicSize:F4}, near {cam.nearClipPlane:F3}, far " +
                                        $"{cam.farClipPlane:F3}, first-pass mask 0x{cullingMask:X8} (head " +
                                        $"'{(eye != null ? eye.name : "none")}'), RT {rtWidth}x{RtHeight}. Layers " +
                                        $"under card root '{subtreeRoot.name}': {CensusText(layerCensus)}. The " +
                                        "failure is in the instrument (camera pose/planes or an XR render-path " +
                                        "interaction), NOT evidence about the band.");
                }
            }

            Vector4 top = MeanOf(Strip(horizontal: true, at: 1f - bandT * 0.5f));
            Vector4 bottom = MeanOf(Strip(horizontal: true, at: bandB * 0.5f));
            Vector4 left = MeanOf(Strip(horizontal: false, at: bandL * 0.5f));
            Vector4 right = MeanOf(Strip(horizontal: false, at: 1f - bandR * 0.5f));
            Vector4 outsideLeft = MeanOf(new[] { (-0.15f, 0.5f) });
            Vector4 outsideBelow = MeanOf(new[] { (0.5f, -0.15f) });

            // ROUND 15 SCAN LINE: the full framed width at card mid-height, RLE'd into quantized
            // colour bands — the offline PIL analysis of the karten screenshots, reproduced in-log.
            Vector3 midWorld = faceRoot.TransformPoint(
                new Vector3(faceRect.center.x, faceRect.center.y, 0f));
            int scanRow = Mathf.Clamp(Mathf.RoundToInt(cam.WorldToScreenPoint(midWorld).y), 0, RtHeight - 1);
            string scan = ScanLine(pixels, scanRow, rtWidth);

            // ---- classify and log ----------------------------------------------------------
            var sb = new StringBuilder(768);
            bool sawSentinel = false;
            void Append(string name, Vector4 mean)
            {
                string cls = Classify(mean, ref sawSentinel);
                sb.Append(name).Append($" ({mean.x:F0},{mean.y:F0},{mean.z:F0},{mean.w:F0}) [{cls}]; ");
            }
            Append("top", top);
            Append("bottom", bottom);
            Append("left", left);
            Append("right", right);
            Append("centre", centre);
            Append("outside-left(+15 %)", outsideLeft);
            Append("outside-below(+15 %)", outsideBelow);

            string verdict = Verdict(top, bottom, left, right, centre, centreSentinel);
            string limitation = sawSentinel
                ? " LIMITATION: at least one sample hit the probe's clear sentinel — nothing in the "
                  + "scene renders there. Under MR passthrough the compositor backs such pixels with "
                  + "the real room, which no scene camera can reproduce; judge those samples by the "
                  + "neighbouring controls, not by the sentinel."
                : string.Empty;
            VRLog.Info("Cards", $"CARD BAND PIXELS ({context}, {reason}): in-situ readback from a probe " +
                                $"camera {ProbeDistance:F2} m out on the face normal (ortho, frame = face " +
                                $"+{Margin:P0}/side, RT {rtWidth}x{RtHeight}, cull = " +
                                (eye != null ? $"head '{eye.name}' mask ∪ card-subtree layers = 0x{cullingMask:X8}"
                                             : $"everything ∪ card-subtree layers = 0x{cullingMask:X8}") +
                                $", card-subtree layers {CensusText(layerCensus)}" +
                                (retriedWideOpen ? ", RETRIED wide-open ~0 after a sentinel centre" : string.Empty) +
                                "). Mean RGBA per mid-band strip and control — " + sb +
                                $"SCAN mid-height (row {scanRow}, {rtWidth} px, L→R across frame incl. " +
                                $"±{Margin:P0} margins): {scan}. VERDICT: {verdict}.{limitation}");
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("Cards", $"CARD BAND PIXELS capture failed ({ex.GetType().Name}: {ex.Message}) " +
                                    "— the band must be judged from the painter inventory this round.");
            }
        }
        finally
        {
            if (prevActive != null || RenderTexture.active == rt)
                RenderTexture.active = prevActive;
            if (camGo != null)
            {
                var cam = camGo.GetComponent<Camera>();
                if (cam != null)
                    cam.targetTexture = null;
                Object.Destroy(camGo);
            }
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (readback != null)
                Object.Destroy(readback);
        }
    }

    /// <summary>Is this mean the probe's own clear colour, i.e. "nothing rendered here"?</summary>
    private static bool IsSentinel(Vector4 mean) => mean.x > 235f && mean.z > 235f && mean.y < 24f;

    /// <summary>Count renderer layers in a subtree (transform count per layer, root included).</summary>
    private static void CountLayers(Transform root, Dictionary<int, int> census)
    {
        census.TryGetValue(root.gameObject.layer, out int n);
        census[root.gameObject.layer] = n + 1;
        for (int i = 0; i < root.childCount; i++)
            CountLayers(root.GetChild(i), census);
    }

    /// <summary>Human-readable layer census: "27 'ModLayer'×42, 5 'UI'×12".</summary>
    private static string CensusText(Dictionary<int, int> census)
    {
        var sb = new StringBuilder(96);
        foreach (KeyValuePair<int, int> kv in census)
        {
            if (sb.Length > 0)
                sb.Append(", ");
            string name = LayerMask.LayerToName(kv.Key);
            sb.Append(kv.Key).Append(string.IsNullOrEmpty(name) ? "" : $" '{name}'").Append('x').Append(kv.Value);
        }
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    /// <summary>
    /// ROUND 15: run-length-encode one RT row into quantized colour bands — "colour runs with
    /// widths", the offline screenshot analysis that actually cracked the band, reproduced
    /// in-log. Quantization starts at ±16 per channel and coarsens (32, 64, 128) until the row
    /// fits in <= 15 runs; a row that still exceeds that is truncated with an explicit "+N more".
    /// Each run logs its pixel width, its start position in FACE-normalized x (0 = card left
    /// edge, 1 = card right edge — the frame extends −0.25..1.25), and its mean RGB.
    /// </summary>
    private static string ScanLine(Color32[] pixels, int row, int rtWidth)
    {
        List<(int Start, int Len, long R, long G, long B)> runs = null!;
        for (int quant = 16; ; quant <<= 1)
        {
            runs = new List<(int, int, long, long, long)>();
            int keyR = -1, keyG = -1, keyB = -1;
            for (int x = 0; x < rtWidth; x++)
            {
                Color32 c = pixels[row * rtWidth + x];
                int qr = c.r / quant, qg = c.g / quant, qb = c.b / quant;
                if (runs.Count > 0 && qr == keyR && qg == keyG && qb == keyB)
                {
                    (int s, int len, long r, long g, long b) = runs[runs.Count - 1];
                    runs[runs.Count - 1] = (s, len + 1, r + c.r, g + c.g, b + c.b);
                }
                else
                {
                    runs.Add((x, 1, c.r, c.g, c.b));
                    keyR = qr; keyG = qg; keyB = qb;
                }
            }
            if (runs.Count <= 15 || quant >= 128)
                break;
        }
        var sb = new StringBuilder(512);
        int shown = Mathf.Min(runs.Count, 15);
        for (int i = 0; i < shown; i++)
        {
            (int start, int len, long r, long g, long b) = runs[i];
            // px → face-normalized x: the RT spans −Margin .. 1+Margin of the face.
            float nx = (float)start / rtWidth * (1f + 2f * Margin) - Margin;
            if (i > 0)
                sb.Append(" | ");
            sb.Append(len).Append("px@x").Append(nx.ToString("F2"))
              .Append('(').Append(r / len).Append(',').Append(g / len).Append(',').Append(b / len).Append(')');
        }
        if (runs.Count > shown)
            sb.Append(" | +").Append(runs.Count - shown).Append(" more runs");
        return sb.ToString();
    }

    /// <summary>One sample mean against the known band signatures (RGBA in 0..255).</summary>
    private static string Classify(Vector4 mean, ref bool sawSentinel)
    {
        float r = mean.x, g = mean.y, b = mean.z;
        if (r > 235f && b > 235f && g < 24f)
        {
            sawSentinel = true;
            return "CLEAR sentinel — nothing rendered";
        }
        if (g >= 140f && g > r * 1.8f && g > b * 1.8f)
            return "GREEN-SCREEN MR backdrop";
        float luma = r * 0.299f + g * 0.587f + b * 0.114f;
        float warm = r - b;
        if (luma <= 48f)
            return warm >= 6f
                ? "near-black WARM — recess-floor family ≈(25,21,18)"
                : "near-black COOL — printed game frame (<40 cool) family";
        if (luma <= 145f)
            return warm >= 15f
                ? "mid WARM — wood family (liner/keycap grain, retinted slab rim)"
                : "mid COOL";
        return "BRIGHT — card art";
    }

    /// <summary>
    /// The verdict clause: which strips are dark, and what family that convicts. ROUND 15: a
    /// sentinel centre now voids the whole verdict — the 118 log shipped "centre is bright art
    /// as expected" over a magenta centre, which is the one sentence an instrument must never
    /// say about its own failure.
    /// </summary>
    private static string Verdict(Vector4 top, Vector4 bottom, Vector4 left, Vector4 right,
                                  Vector4 centre, bool centreSentinel)
    {
        if (centreSentinel)
        {
            return "INSTRUMENT FAILURE — the card centre hit the clear sentinel even after the " +
                   "wide-open retry, so this capture rendered nothing and proves nothing about " +
                   "the band; see the PROBE BLIND warning above for the camera state";
        }
        var dark = new StringBuilder(128);
        int sentinelStrips = 0;
        void Check(string name, Vector4 mean)
        {
            if (IsSentinel(mean))
            {
                sentinelStrips++;
                return; // a sentinel strip is "nothing rendered", not a dark painter
            }
            float luma = mean.x * 0.299f + mean.y * 0.587f + mean.z * 0.114f;
            if (luma > 48f)
                return;
            if (dark.Length > 0)
                dark.Append(", ");
            dark.Append(name).Append(mean.x - mean.z >= 6f ? " (warm)" : " (cool)");
        }
        Check("top", top);
        Check("bottom", bottom);
        Check("left", left);
        Check("right", right);
        float centreLuma = centre.x * 0.299f + centre.y * 0.587f + centre.z * 0.114f;
        string centreNote = centreLuma > 96f
            ? "centre is bright art as expected"
            : $"centre is DARK too (luma {centreLuma:F0}) — the whole face is dark, judge the art first";
        if (sentinelStrips == 4)
            return $"all four band strips hit the clear sentinel — nothing renders in the band " +
                   $"region from this viewpoint (MR passthrough shows the room there); {centreNote}";
        if (dark.Length == 0)
            return $"no near-black band strip in the probe's view" +
                   (sentinelStrips > 0 ? $" ({sentinelStrips} strip(s) sentinel — unrendered)" : string.Empty) +
                   $"; {centreNote}";
        return $"NEAR-BLACK band strip(s): {dark} — a COOL strip is the PRINT (an unpunched or " +
               "incompletely punched face layer; since round 14 the slab rim is warm umber and can " +
               "no longer read near-black), a WARM strip is the recess floor showing through " +
               $"(liner missing, undersized or behind the floor there); {centreNote}";
    }
}

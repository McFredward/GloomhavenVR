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
///
/// <para>ROUND 16 — THE DIFFERENTIAL AND THE CROSS-CHECK. USER VERDICT on ModBuild 119, verbatim:
/// "Ein erster teilerfolg: Die Ränder sind jetzt nun nicht mehr schwarz sondern bech (siehe
/// karten5.png) aber immer noch nicht transparent wie sie sein sollten. Logs liegen ab". The 119
/// log carries a genuine PARADOX: the painter inventory reads the Backing slab as 'Standard'
/// q2450 CUTOUT-clipped with the baked Edge footprint (whose cached mask is 85.1 % opaque —
/// arithmetically consistent with TRANSPARENT bands), yet the band strips measure OPAQUE umber
/// (125,97,68,≈255) — which is EdgeColor × (light + emission floor) to within 1/255. Texture says
/// clipped; render says painted. Deduction is done; two new instruments end the paradox class:</para>
/// <list type="number">
/// <item>THE DIFFERENTIAL (<c>CARD BAND DIFF</c>): after the baseline render, the SAME framing is
///   re-rendered once per candidate with exactly ONE candidate suppressed for that render only —
///   the card's own Backing renderer disabled; only the edge/rim SUBMESH silenced (slot 0 swapped
///   to an invisible material via <c>sharedMaterials</c> array assignment — never <c>.materials</c>,
///   which would clone); the tray's SlotSeatLiner disabled; the adopted face canvas disabled; and
///   an everything-but-the-card cull mask (run only when the card subtree owns no shared builtin
///   layer — otherwise skipped, and the line says so). Each pass logs its four band-strip means
///   beside the baseline; the candidate whose suppression turns a strip to the sentinel/backdrop
///   IS that strip's painter, per strip, per placement. Every suppression restores in a
///   <c>finally</c> inside the same frame and verifies the restored state.</item>
/// <item>THE CROSS-CHECK (<c>CARD BAND TEX-VS-RENDER</c>): the LIVE Edge texture is read back off
///   the GPU (Blit — the baked texture is makeNoLongerReadable, so the CPU mask cannot vouch for
///   the GPU copy) and its ALPHA is RLE-logged along the same mid-height scan row and the u=0.5
///   column (the top/bottom bands are where the 119 strips measured wood). The mapping is stated,
///   not assumed: <c>CardMesh.Build</c> gives the slab's front face planar card-space UVs
///   (u = x/width + 0.5), so texture u IS the scan's face-normalized x; only the thin rim samples
///   2 % inset (RimUvInset). Beside it, the AT-CAPTURE-TIME material state of both submesh slots
///   (keywords, queue, _Cutoff, mainTexture, _EMISSION, shared-pair identity) — a mismatch against
///   the inventory's latch-time reading is a state-drift conviction — plus the
///   <see cref="CardShaderProbe.DescribeCutoutClip"/> verdict: whether a fragment sampling an
///   alpha-0 texel of this very material's OWN texture is actually discarded on this GPU/build
///   (a keyword is a request; a stripped Standard cutout VARIANT silently never clips, and the
///   slab then paints its full envelope in EdgeColor — exactly the measured band).</item>
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
    /// Capture and log one <c>CARD BAND PIXELS</c> line for the given card face, then the round-16
    /// <c>CARD BAND TEX-VS-RENDER</c> cross-check and <c>CARD BAND DIFF</c> differential passes
    /// (see the class header). Band fractions are the caller's (the derived-outline bands the
    /// painter inventory already resolved); <paramref name="card"/> is the owning card root (for
    /// the Backing renderer and the canvas), <paramref name="contextRoot"/> the owning slot/fan
    /// subtree (for the SlotSeatLiner). Never throws.
    /// </summary>
    internal static void Capture(string context, string reason, RectTransform faceRoot,
                                 Rect faceRect, float bandL, float bandR, float bandB, float bandT,
                                 VRCard? card, Transform? contextRoot)
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
            // hand-rolled projection can disagree with what was rendered. Takes the pixel array
            // explicitly (round 16) so the differential passes can measure their own renders
            // through the exact same sampler.
            Vector4 MeanOf(Color32[] px, (float x, float y)[] points)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                int counted = 0;
                foreach ((float nx, float ny) in points)
                {
                    Vector3 world = faceRoot.TransformPoint(new Vector3(
                        faceRect.xMin + nx * faceRect.width,
                        faceRect.yMin + ny * faceRect.height, 0f));
                    Vector3 sp = cam.WorldToScreenPoint(world);
                    int ppx = Mathf.Clamp(Mathf.RoundToInt(sp.x), 0, rtWidth - 1);
                    int ppy = Mathf.Clamp(Mathf.RoundToInt(sp.y), 0, RtHeight - 1);
                    Color32 c = px[ppy * rtWidth + ppx];
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
            Vector4 centre = MeanOf(pixels, new[] { (0.5f, 0.5f) });
            bool retriedWideOpen = false;
            bool centreSentinel = IsSentinel(centre);
            if (centreSentinel)
            {
                retriedWideOpen = true;
                cam.cullingMask = ~0;
                pixels = RenderAndRead();
                centre = MeanOf(pixels, new[] { (0.5f, 0.5f) });
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

            // Round 16: one strip-measuring function shared by the baseline and every
            // differential pass — same sample points, same sampler, different pixel arrays.
            Vector4[] MeasureStrips(Color32[] px) => new[]
            {
                MeanOf(px, Strip(horizontal: true, at: 1f - bandT * 0.5f)),   // top
                MeanOf(px, Strip(horizontal: true, at: bandB * 0.5f)),        // bottom
                MeanOf(px, Strip(horizontal: false, at: bandL * 0.5f)),       // left
                MeanOf(px, Strip(horizontal: false, at: 1f - bandR * 0.5f)),  // right
            };
            Vector4[] baseStrips = MeasureStrips(pixels);
            Vector4 top = baseStrips[0];
            Vector4 bottom = baseStrips[1];
            Vector4 left = baseStrips[2];
            Vector4 right = baseStrips[3];
            Vector4 outsideLeft = MeanOf(pixels, new[] { (-0.15f, 0.5f) });
            Vector4 outsideBelow = MeanOf(pixels, new[] { (0.5f, -0.15f) });

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

            // ================= ROUND 16 (see the class header) ==============================
            // Two further instruments, same frame, same framing. Each individually guarded so a
            // failure in one can never cost the baseline line above or the other instrument.
            Renderer? backing = FindBackingRenderer(card);

            // ---- Deliverable 2: texture-vs-render cross-check ------------------------------
            try
            {
                LogTexVsRender(context, reason, backing, baseStrips);
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("Cards", $"CARD BAND TEX-VS-RENDER ({context}) failed " +
                                    $"({ex.GetType().Name}: {ex.Message}) — the cross-check is missing " +
                                    "this capture; the DIFF line below still decides.");
            }

            // ---- Deliverable 1: the differential passes ------------------------------------
            // Re-render the SAME framing once per candidate, each with exactly ONE candidate
            // suppressed for that render only. Suppress → render → measure → restore, all inside
            // this same frame; every restore lives in a finally and is verified afterwards. The
            // candidate whose suppression turns a strip to the sentinel/backdrop IS that strip's
            // painter — per strip, per placement.
            try
            {
                string[] stripNames = { "top", "bottom", "left", "right" };
                var flippedBy = new List<string>[] { new(), new(), new(), new() };
                var alteredBy = new List<string>[] { new(), new(), new(), new() };
                var diff = new StringBuilder(768);
                void AppendStrips(Vector4[] s)
                {
                    for (int i = 0; i < 4; i++)
                        diff.Append(i > 0 ? " " : string.Empty).Append(stripNames[i])
                            .Append($"({s[i].x:F0},{s[i].y:F0},{s[i].z:F0},{s[i].w:F0})");
                }
                diff.Append("baseline ");
                AppendStrips(baseStrips);

                void RunPass(string label,
                             System.Func<(System.Action? Restore, System.Func<bool>? Verify, string? Skip)> arm)
                {
                    diff.Append(" | ").Append(label).Append(' ');
                    System.Action? restore = null;
                    System.Func<bool>? verifyRestored = null;
                    try
                    {
                        (System.Action? armedRestore, System.Func<bool>? armedVerify, string? skip) = arm();
                        restore = armedRestore;
                        verifyRestored = armedVerify;
                        if (skip != null)
                        {
                            diff.Append("SKIPPED (").Append(skip).Append(')');
                            return;
                        }
                        Vector4[] s = MeasureStrips(RenderAndRead());
                        AppendStrips(s);
                        for (int i = 0; i < 4; i++)
                        {
                            if (IsSentinel(baseStrips[i]))
                                continue; // nothing rendered there at baseline — nothing to convict
                            if (IsSentinel(s[i]))
                            {
                                flippedBy[i].Add(label);
                            }
                            else if (Mathf.Abs(s[i].x - baseStrips[i].x) + Mathf.Abs(s[i].y - baseStrips[i].y)
                                     + Mathf.Abs(s[i].z - baseStrips[i].z) > 96f)
                            {
                                alteredBy[i].Add(label);
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        diff.Append("FAILED (").Append(ex.GetType().Name).Append(')');
                    }
                    finally
                    {
                        restore?.Invoke();
                        if (verifyRestored != null && !verifyRestored())
                        {
                            diff.Append(" [RESTORE FAILED]");
                            VRLog.Warn("Cards", $"CARD BAND DIFF ({context}): pass '{label}' could not " +
                                                "verify its restored state — inspect the card visuals.");
                        }
                    }
                }

                // 1. The card's own Backing MeshRenderer, whole.
                RunPass("-Backing", () =>
                {
                    if (backing == null)
                        return (null, null, "no Backing renderer resolved under the card root");
                    bool was = backing.enabled;
                    backing.enabled = false;
                    return (() => backing.enabled = was, () => backing.enabled == was, null);
                });

                // 2. Only the slab's EDGE/RIM submesh (slot 0 — CardMesh.Build's front+rim),
                // silenced by swapping slot 0 to a fully transparent Sprites/Default stand-in via
                // sharedMaterials ARRAY assignment. Never `.materials` (that clones instances and
                // would detach this renderer from the shared pair — the very defect class under
                // investigation); restoring reassigns the original array.
                RunPass("-EdgeSubmesh", () =>
                {
                    if (backing == null)
                        return (null, null, "no Backing renderer resolved under the card root");
                    Material[] orig = backing.sharedMaterials;
                    if (orig.Length < 2)
                        return (null, null, $"backing has {orig.Length} material slot(s) — no separate edge submesh");
                    Material? invisible = InvisibleMaterial();
                    if (invisible == null)
                        return (null, null, "no alpha-blended shader available for the invisible stand-in");
                    var alt = (Material[])orig.Clone();
                    alt[0] = invisible;
                    backing.sharedMaterials = alt;
                    return (() => backing.sharedMaterials = orig,
                            () => backing.sharedMaterials.Length > 0
                                  && ReferenceEquals(backing.sharedMaterials[0], orig[0]), null);
                });

                // 3. The tray's SlotSeatLiner(s) (tray context only — the fan has none).
                RunPass("-Liner", () =>
                {
                    var liners = new List<Renderer>(2);
                    if (contextRoot != null)
                    {
                        s_diffScratch.Clear();
                        contextRoot.GetComponentsInChildren(includeInactive: false, s_diffScratch);
                        foreach (Renderer r in s_diffScratch)
                        {
                            if (r != null && r.enabled && r.name == "SlotSeatLiner")
                                liners.Add(r);
                        }
                        s_diffScratch.Clear();
                    }
                    if (liners.Count == 0)
                    {
                        return (null, null, contextRoot == null
                            ? "no owning slot/fan root resolved"
                            : $"no active SlotSeatLiner under '{contextRoot.name}' — fan context");
                    }
                    foreach (Renderer r in liners)
                        r.enabled = false;
                    return ((System.Action)(() =>
                            {
                                foreach (Renderer r in liners)
                                {
                                    if (r != null)
                                        r.enabled = true;
                                }
                            }),
                            (System.Func<bool>)(() =>
                            {
                                foreach (Renderer r in liners)
                                {
                                    if (r == null || !r.enabled)
                                        return false;
                                }
                                return true;
                            }), null);
                });

                // 4. The adopted game widget's face canvas (its root Canvas component — every
                // uGUI graphic of the face stops rendering; the component is re-enabled before
                // the player's camera renders this frame).
                RunPass("-FaceCanvas", () =>
                {
                    Canvas? cv = faceRoot.GetComponentInParent<Canvas>();
                    if (cv == null)
                        return (null, null, "no Canvas above the face root");
                    Canvas rootCv = cv.rootCanvas != null ? cv.rootCanvas : cv;
                    bool was = rootCv.enabled;
                    rootCv.enabled = false;
                    return (() => rootCv.enabled = was, () => rootCv.enabled == was, null);
                });

                // 5. Everything EXCEPT the card subtree culled. Only honest when the card's
                // subtree owns no shared builtin layer — a mask on layers 0..7 would still render
                // foreign geometry and the pass would convict/acquit nothing; skipped loudly then.
                RunPass("-AllButCard", () =>
                {
                    if (card == null)
                        return (null, null, "no card root");
                    var cardCensus = new Dictionary<int, int>();
                    CountLayers(card.transform, cardCensus);
                    int cardMask = 0;
                    bool sharedBuiltin = false;
                    foreach (KeyValuePair<int, int> kv in cardCensus)
                    {
                        cardMask |= 1 << kv.Key;
                        if (kv.Key < 8)
                            sharedBuiltin = true;
                    }
                    if (sharedBuiltin)
                    {
                        return (null, null, "card subtree uses shared builtin layer(s) (" +
                                            CensusText(cardCensus) + ") — an only-card mask would still " +
                                            "render foreign geometry there");
                    }
                    int prevMask = cam.cullingMask;
                    cam.cullingMask = cardMask;
                    return (() => cam.cullingMask = prevMask, () => cam.cullingMask == prevMask, null);
                });

                // ---- the differential verdict, per strip -----------------------------------
                var dv = new StringBuilder(320);
                for (int i = 0; i < 4; i++)
                {
                    if (dv.Length > 0)
                        dv.Append("; ");
                    dv.Append(stripNames[i]).Append(": ");
                    if (IsSentinel(baseStrips[i]))
                    {
                        dv.Append("baseline already sentinel — nothing renders there");
                        continue;
                    }
                    if (flippedBy[i].Count > 0)
                    {
                        bool byBacking = flippedBy[i].Contains("-Backing");
                        bool byEdge = flippedBy[i].Contains("-EdgeSubmesh");
                        if (byBacking && byEdge)
                        {
                            dv.Append("PAINTED BY the Backing slab's EDGE/RIM submesh (slot 0 — " +
                                      "both -Backing and -EdgeSubmesh clear the strip)");
                        }
                        else if (byBacking)
                        {
                            dv.Append("PAINTED BY the Backing slab but NOT its edge/rim slot " +
                                      "(only -Backing clears it — the BACK submesh, slot 1)");
                        }
                        else
                        {
                            dv.Append("PAINTED BY ").Append(string.Join(" and ", flippedBy[i]))
                              .Append(" (its suppression clears the strip to the sentinel)");
                        }
                        if (alteredBy[i].Count > 0)
                            dv.Append("; also altered by ").Append(string.Join(", ", alteredBy[i]));
                    }
                    else if (alteredBy[i].Count > 0)
                    {
                        dv.Append("no suppression cleared it, but ").Append(string.Join(", ", alteredBy[i]))
                          .Append(" changed it materially (>32/channel mean) — the strip is a composite " +
                                  "and that candidate paints part of it (or backs it)");
                    }
                    else
                    {
                        dv.Append("NO candidate changed this strip — its painter is outside the " +
                                  "candidate set (game scene geometry, or the compositor)");
                    }
                }
                VRLog.Info("Cards", $"CARD BAND DIFF ({context}, {reason}): the SAME framing re-rendered " +
                                    "once per candidate with exactly ONE candidate suppressed for that " +
                                    "render only (restored in a finally inside this same frame, " +
                                    $"restoration verified). Band-strip means per pass — {diff}. " +
                                    $"VERDICT: {dv}.");
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("Cards", $"CARD BAND DIFF ({context}) failed ({ex.GetType().Name}: " +
                                    $"{ex.Message}) — the differential is missing this capture; the " +
                                    "baseline and TEX-VS-RENDER lines above still stand.");
            }
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

    // ================================================= round 16: helpers (see class header) --

    /// <summary>Reused scratch for the liner sweep (no per-capture allocation growth).</summary>
    private static readonly List<Renderer> s_diffScratch = new(32);

    /// <summary>Cutout clip-probe verdicts per material name — the GPU experiment runs once per
    /// material per session; later captures print the cached sentence.</summary>
    private static readonly Dictionary<string, string> s_clipVerdictByMat = new(4);

    private static Material? s_invisibleMat;

    /// <summary>
    /// The card's own Backing renderer under <paramref name="card"/>: named 'Backing' (both the
    /// procedural body and the bundle-prefab branch name it that — <c>VRCard</c>), preferring the
    /// one whose slot 0 is a shared card-edge material when several exist.
    /// </summary>
    private static Renderer? FindBackingRenderer(VRCard? card)
    {
        if (card == null)
            return null;
        Renderer[] rs = card.GetComponentsInChildren<Renderer>(includeInactive: false);
        Renderer? named = null;
        foreach (Renderer r in rs)
        {
            if (r == null || r.name != "Backing")
                continue;
            Material[] mats = r.sharedMaterials;
            if (mats.Length > 0 && mats[0] != null && IsSharedEdge(mats[0]))
                return r;
            named ??= r;
        }
        return named;
    }

    private static bool IsSharedEdge(Material m)
        => ReferenceEquals(m, CardMesh.CreateEdgeMaterial(CardBodyKind.Ability))
           || ReferenceEquals(m, CardMesh.CreateEdgeMaterial(CardBodyKind.Item))
           || ReferenceEquals(m, CardMesh.CreateEdgeMaterial(CardBodyKind.Neutral));

    /// <summary>Fully transparent alpha-blended stand-in for the -EdgeSubmesh pass (built once).
    /// Sprites/Default is guaranteed present (uGUI and <c>Net/RemoteBoardCard</c> ride it) and
    /// alpha-blends by construction — with color (0,0,0,0) it contributes no fragment.</summary>
    private static Material? InvisibleMaterial()
    {
        if (s_invisibleMat != null)
            return s_invisibleMat;
        Shader? sh = Shader.Find("Sprites/Default");
        if (sh == null)
            return null;
        s_invisibleMat = new Material(sh)
        {
            name = "GloomhavenVR.CardBandDiff.Invisible",
            color = new Color(0f, 0f, 0f, 0f),
        };
        return s_invisibleMat;
    }

    /// <summary>Which shared card-body material asset this is, if any — same identity test the
    /// painter inventory prints at latch time, re-read here AT CAPTURE TIME so drift is visible.</summary>
    private static string PairIdentity(Material mat)
    {
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Ability))) return "SHARED Ability edge";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Ability))) return "SHARED Ability back";
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Item))) return "SHARED Item edge";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Item))) return "SHARED Item back";
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Neutral))) return "SHARED Neutral edge";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Neutral))) return "SHARED Neutral back";
        return "NOT a shared-pair member — a per-renderer instance or foreign material (STATE DRIFT " +
               "against the inventory's latch-time reading)";
    }

    /// <summary>
    /// Deliverable 2 (round 16): the live material state of both Backing submesh slots AT CAPTURE
    /// TIME, the LIVE Edge texture's GPU-side alpha RLE'd along the scan row (v = 0.5) and the
    /// u = 0.5 column, and the cutout clip-execution probe. The texture is read back off the GPU
    /// via Blit — the baked cutout texture is <c>makeNoLongerReadable</c>, so the CPU footprint
    /// cannot vouch for what the GPU actually samples; this line can. Mapping: the slab's front
    /// face carries planar card-space UVs (<c>CardMesh.Build</c>: u = x/width + 0.5 over the face
    /// rect), so texture u IS the scan's face-normalized x and texture v the face-normalized y;
    /// only the thin rim samples 2 % inset (<c>RimUvInset</c>).
    /// </summary>
    private static void LogTexVsRender(string context, string reason, Renderer? backing,
                                       Vector4[] baseStrips)
    {
        if (backing == null)
        {
            VRLog.Info("Cards", $"CARD BAND TEX-VS-RENDER ({context}, {reason}): no Backing renderer " +
                                "resolved under the card root — cross-check skipped.");
            return;
        }
        Material[] mats = backing.sharedMaterials;
        var sb = new StringBuilder(640);
        for (int m = 0; m < mats.Length && m < 2; m++)
        {
            Material? mat = mats[m];
            if (mat == null)
            {
                sb.Append("slot").Append(m).Append(" null; ");
                continue;
            }
            string kws = string.Join(" ", mat.shaderKeywords);
            string cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff").ToString("F2") : "n/a";
            Texture? tex = mat.HasProperty("_MainTex") ? mat.mainTexture : null;
            sb.Append("slot").Append(m).Append(" '").Append(mat.name).Append("' [")
              .Append(PairIdentity(mat)).Append("] shader '")
              .Append(mat.shader != null ? mat.shader.name : "<none>").Append("' keywords [")
              .Append(kws).Append("] q").Append(mat.renderQueue).Append(" _Cutoff ").Append(cutoff)
              .Append(" mainTex ").Append(tex != null ? $"'{tex.name}' {tex.width}x{tex.height}" : "none")
              .Append(" _EMISSION ").Append(mat.IsKeywordEnabled("_EMISSION") ? "on" : "off")
              .Append("; ");
        }

        // ---- GPU-side alpha of the LIVE edge texture -------------------------------------
        string rowRle = "n/a", colRle = "n/a";
        int texW = 0, texH = 0;
        bool gpuTopHole = false, gpuBottomHole = false, gpuLeftHole = false, gpuRightHole = false;
        Material? edgeMat = mats.Length > 0 ? mats[0] : null;
        Texture? edgeTex = edgeMat != null && edgeMat.HasProperty("_MainTex") ? edgeMat.mainTexture : null;
        if (edgeTex != null && edgeTex.width >= 8 && edgeTex.height >= 8)
        {
            RenderTexture? trt = null;
            Texture2D? tread = null;
            RenderTexture? prev = RenderTexture.active;
            try
            {
                texW = edgeTex.width;
                texH = edgeTex.height;
                trt = RenderTexture.GetTemporary(texW, texH, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(edgeTex, trt);
                RenderTexture.active = trt;
                tread = new Texture2D(texW, texH, TextureFormat.RGBA32, mipChain: false);
                tread.ReadPixels(new Rect(0, 0, texW, texH), 0, 0, recalculateMipMaps: false);
                tread.Apply(updateMipmaps: false);
                Color32[] tp = tread.GetPixels32();
                var rowA = new byte[texW];
                var colA = new byte[texH];
                int midRow = texH / 2, midCol = texW / 2;
                for (int x = 0; x < texW; x++)
                    rowA[x] = tp[midRow * texW + x].a;
                for (int y = 0; y < texH; y++)
                    colA[y] = tp[y * texW + midCol].a;
                rowRle = AlphaRle(rowA);
                colRle = AlphaRle(colA);
                gpuLeftHole = EndHole(rowA, fromStart: true);
                gpuRightHole = EndHole(rowA, fromStart: false);
                gpuBottomHole = EndHole(colA, fromStart: true);
                gpuTopHole = EndHole(colA, fromStart: false);
            }
            finally
            {
                RenderTexture.active = prev;
                if (trt != null)
                    RenderTexture.ReleaseTemporary(trt);
                if (tread != null)
                    Object.Destroy(tread);
            }
        }

        // ---- cutout clip-execution probe (once per material per session) -----------------
        string clip;
        if (edgeMat == null)
        {
            clip = "skipped — no slot-0 material";
        }
        else if (s_clipVerdictByMat.TryGetValue(edgeMat.name, out string? cached))
        {
            clip = "(cached) " + cached;
        }
        else
        {
            CardBodyKind kind =
                ReferenceEquals(edgeMat, CardMesh.CreateEdgeMaterial(CardBodyKind.Ability)) ? CardBodyKind.Ability
                : ReferenceEquals(edgeMat, CardMesh.CreateEdgeMaterial(CardBodyKind.Item)) ? CardBodyKind.Item
                : CardBodyKind.Neutral;
            byte[]? mask = kind == CardBodyKind.Neutral ? null : CardMesh.Footprint(kind, out _, out _);
            if (mask == null)
            {
                clip = "skipped — no footprint applied for this material's kind, so there is no " +
                       "known alpha-0 texel to probe";
            }
            else
            {
                CardMesh.Footprint(kind, out int fw, out int fh);
                int cx = fw / 2;
                int holeY = -1;
                for (int y = fh - 1; y > fh / 2; y--)
                {
                    if (mask[y * fw + cx] < 16)
                    {
                        holeY = y;
                        break;
                    }
                }
                if (holeY < 0)
                {
                    clip = "skipped — the CPU footprint has no alpha-0 texel on the centre column's " +
                           "upper half (no top band in the mask)";
                }
                else
                {
                    var uvA = new Vector2((cx + 0.5f) / fw, (holeY + 0.5f) / fh);
                    var uvO = new Vector2(0.5f, 0.5f);
                    clip = CardShaderProbe.DescribeCutoutClip(edgeMat, uvA, uvO);
                }
            }
            s_clipVerdictByMat[edgeMat.name] = clip;
        }

        // ---- the cross-check clause ------------------------------------------------------
        var mismatch = new List<string>(4);
        void Cross(string name, bool gpuHole, Vector4 strip)
        {
            if (gpuHole && !IsSentinel(strip) && strip.w >= 128f)
                mismatch.Add(name);
        }
        Cross("top", gpuTopHole, baseStrips[0]);
        Cross("bottom", gpuBottomHole, baseStrips[1]);
        Cross("left", gpuLeftHole, baseStrips[2]);
        Cross("right", gpuRightHole, baseStrips[3]);
        string crossVerdict = mismatch.Count > 0
            ? $"MISMATCH on {string.Join("/", mismatch)} — the GPU-side texture ends in alpha 0 exactly " +
              "where the rendered strip measured opaque: the alpha EXISTS on the GPU but is not honored " +
              "at raster time; the CUTOUT CLIP PROBE clause decides whether the clip executes at all"
            : "no texture-vs-render contradiction in this capture (either the GPU texture carries no " +
              "alpha-0 band or the rendered strips are already clear)";

        VRLog.Info("Cards", $"CARD BAND TEX-VS-RENDER ({context}, {reason}): live material state AT " +
                            "CAPTURE TIME (same frame as the strips above — any difference against the " +
                            $"inventory's latch-time reading is a state-drift conviction): {sb}" +
                            $"EDGE ALPHA row v=0.50 ({texW} tx, L→R; texture u IS face-normalized scan x — " +
                            "CardMesh.Build gives the slab front planar card-space UVs u = x/width + 0.5, " +
                            $"rim 2 % inset): {rowRle}. EDGE ALPHA col u=0.50 ({texH} tx, bottom→top; a " +
                            "platform Blit flip would only swap the end labels — both ends are logged): " +
                            $"{colRle}. CUTOUT CLIP PROBE (slot 0): {clip}. CROSS-CHECK: {crossVerdict}.");
    }

    /// <summary>RLE of an alpha line into ≤ 12 runs (bucketed by a/32): "Ntx a=mean".</summary>
    private static string AlphaRle(byte[] line)
    {
        var runs = new List<(int Len, long Sum)>(16);
        int key = -1;
        foreach (byte a in line)
        {
            int k = a / 32;
            if (runs.Count > 0 && k == key)
            {
                (int len, long sum) = runs[runs.Count - 1];
                runs[runs.Count - 1] = (len + 1, sum + a);
            }
            else
            {
                runs.Add((1, a));
                key = k;
            }
        }
        var sb = new StringBuilder(160);
        int shown = Mathf.Min(runs.Count, 12);
        for (int i = 0; i < shown; i++)
        {
            (int len, long sum) = runs[i];
            if (i > 0)
                sb.Append(" | ");
            sb.Append(len).Append("tx a=").Append(sum / len);
        }
        if (runs.Count > shown)
            sb.Append(" | +").Append(runs.Count - shown).Append(" more runs");
        return sb.Length > 0 ? sb.ToString() : "empty";
    }

    /// <summary>Does this alpha line END in a run of near-zero alpha at least ~1 % long — i.e.
    /// does the GPU texture carry a transparent band at that edge?</summary>
    private static bool EndHole(byte[] line, bool fromStart)
    {
        int n = 0;
        if (fromStart)
        {
            for (int i = 0; i < line.Length && line[i] < 16; i++)
                n++;
        }
        else
        {
            for (int i = line.Length - 1; i >= 0 && line[i] < 16; i--)
                n++;
        }
        return n >= Mathf.Max(2, line.Length / 100);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// Reporting half of <see cref="GlowCardCensus"/> — the <c>[Perf] GLOW CARDS</c> line itself.
///
/// <para>THE RULE THIS FILE EXISTS TO ENFORCE. ModBuild 251's verdict sentence named a cause that its
/// own population could not carry: the three cards it counted as depth-fade dependants were offscreen,
/// not submitted, zero-sized and on monsters, while every card near the gate printed
/// <c>depth-fade props: NONE</c>. That cost a hardware round. So <see cref="AppendVerdict"/> takes its
/// arithmetic ONLY from the SUBJECT-ELIGIBLE set — submitted, on screen, at least
/// <see cref="GlowCardCensus.SubjectMinPx"/> pixels across — and when that set is empty it says the
/// line has no subject and stops, rather than reasoning about whatever it happened to find.</para>
/// </summary>
internal static partial class GlowCardCensus
{
    /// <summary>Screenshot the user's photographs are taken at, so a screen rect can be read straight
    /// off the log and compared to a pixel position in the JPEG without arithmetic.</summary>
    private const float PhotoW = 3840f;
    private const float PhotoH = 2160f;

    private static readonly ShaderTagId LightModeTag = new("LightMode");

    /// <summary>
    /// Emit the census and drop every reference. It ALWAYS prints, including when it found nothing,
    /// and when it found nothing it says exactly which tests it applied — a silent instrument and an
    /// instrument that never ran must never look the same, which is this repo's standing rule and was
    /// learned the expensive way.
    /// </summary>
    internal static void Log()
    {
        Stopwatch clock = Stopwatch.StartNew();
        if (!_sampled)
        {
            var miss = new StringBuilder(768);
            miss.Append("GLOW CARDS — NOT SAMPLED this window. The SCENE walk rationed itself "
                        + "(see the SCENE line above), so this census was never armed and the "
                        + "absence of records below carries NO information about the scene. "
                        + "This sentence exists because an instrument that found nothing and "
                        + "an instrument that never ran must never look the same.");
            if (_ownWalkNote.Length > 0)
                miss.Append(" | ").Append(_ownWalkNote);
            AppendBestSample(miss);
            _ownWalkNote = string.Empty;
            VRLog.Info(Scope, miss.ToString());
            return;
        }

        StringBuilder sb = new(8192);
        try
        {
            sb.Append("GLOW CARDS — what the pale hard-edged rectangles on the gate ARE, ranked by "
                      + "what the player can actually see. Report (2026-08-24): 'Diese schwebenden "
                      + "viereckigen Lichter an dem Tor erscheinen mir komisch … Das ist dauerhaft so "
                      + "egal was ein oder ausgeblendet wird - ich kann mich nicht erinnern, dass es "
                      + "flat sowas gab' (schwebende_lichter.jpg / walls_gone.jpg)");
            if (_ownWalkNote.Length > 0)
                sb.Append(" | ").Append(_ownWalkNote);
            AppendGlobals(sb);
            AppendPathContrast(sb);
            AppendPopulation(sb);
            List<Candidate> ranked = Rank();
            AppendCards(sb, ranked);
            AppendTail(sb, ranked);
            AppendVerdict(sb, ranked);
            LatchBestSample(sb, ranked);
            AppendBestSample(sb);
        }
        catch (Exception e)
        {
            sb.Append(" | census threw ").Append(e.GetType().Name).Append(": ").Append(e.Message);
        }
        finally
        {
            clock.Stop();
            _costMs = clock.Elapsed.TotalMilliseconds;
            sb.Append(" | INSTRUMENT COST, measured not asserted: ")
              .Append(_costMs.ToString("F2"))
              .Append("ms to build this line");
            if (_ranOwnWalk)
                sb.Append(" plus ").Append(_lastOwnWalkMs.ToString("F1")).Append("ms for the standalone walk above");
            else
                sb.Append("; collection rode the SCENE walk at one dictionary lookup per material "
                          + "slot plus one Renderer.bounds read per SUBMITTED renderer, and added no "
                          + "scene walk of its own");
            _ownWalkNote = string.Empty;
            Reset();
        }
        VRLog.Info(Scope, sb.ToString());
    }

    // ==========================================================================================
    //  globals
    // ==========================================================================================

    /// <summary>The depth contract. Kept because it is cheap and because a reader coming from
    /// ModBuild 251 will look for it — but it is now stated as WHAT IT IS: a hypothesis that was
    /// tested on hardware and failed, so its presence here is context, not a lead.</summary>
    private static void AppendGlobals(StringBuilder sb)
    {
        bool depthOn = _headKnown && (_headDepth & DepthTextureMode.Depth) != 0;
        sb.Append(" | DEPTH CONTRACT (CLOSED BY MEASUREMENT — do not re-litigate): head camera "
                  + "depthTextureMode=")
          .Append(_headKnown ? _headDepth.ToString() : "UNKNOWN (no head camera this window)")
          .Append(", [Optimize] HeadDepthPrepass=").Append(_depthDialOn ? "true" : "false")
          .Append(", QualitySettings.softParticles=").Append(_softParticles ? "true" : "false")
          .Append(". ModBuild 251 proposed that an unwritten _CameraDepthTexture makes soft-particle "
                  + "fades saturate to full opacity. The user ran the A/B with the prepass ON "
                  + "(second_logs/Player.log records depthTextureMode=Depth) and reported the "
                  + "rectangles UNCHANGED: 'Auch mit HeadDepthPrepass=true sind die schwebenden "
                  + "Lichter an der Tür noch da'. The depth texture is NOT the cause")
          .Append(depthOn
                      ? "; depth IS written in this window, so nothing below can blame it either"
                      : "; depth is not written in this window, which is the state the symptom was "
                        + "ALSO present in with depth on");
    }

    /// <summary>
    /// THE TWO CAMERAS, side by side. The user's sentence 'ich kann mich nicht erinnern, dass es flat
    /// sowas gab' is a claim about the difference between the camera the game draws this scene with
    /// and the camera the mod draws it with, and until this block existed no line in this codebase
    /// printed both. Three differences can each make the same renderer look different in VR:
    /// the RENDERING PATH (a shader whose subshader set differs between Forward and Deferred draws
    /// through a different pass, or through its Fallback), the CULLING MASK (a layer we render and
    /// the game does not is drawn only in VR), and the COMMAND BUFFERS (the game's
    /// TilesOcclusionGenerator hangs its occlusion-map buffer on its own camera at BeforeGBuffer,
    /// which a forward head camera never reaches).
    /// </summary>
    private static void AppendPathContrast(StringBuilder sb)
    {
        sb.Append(" | PATH CONTRAST — the mod's camera vs the game's: head '").Append(_headName)
          .Append("' path=").Append(_headKnown ? _headPath.ToString() : "UNKNOWN")
          .Append(" mask=0x").Append(_headMask.ToString("X8"));
        if (!_scenarioKnown)
        {
            sb.Append("; the game's 'ScenarioCamera' was NOT FOUND this window, so the whole "
                      + "flat-vs-VR comparison below is unavailable and no card can be called "
                      + "VR-only. Absence of the camera is not absence of the difference");
            return;
        }
        sb.Append("; game 'ScenarioCamera' path=").Append(_scenarioPath)
          .Append(" mask=0x").Append(_scenarioMask.ToString("X8"))
          .Append(" commandBuffers=").Append(_scenarioBuffers);

        if (_headKnown && _headPath != _scenarioPath)
        {
            sb.Append(" ⇒ THE TWO CAMERAS RENDER THIS SCENE THROUGH DIFFERENT PATHS. Any shader whose "
                      + "subshader/pass set differs between them — a Deferred-only pass, a Fallback "
                      + "that kicks in on Forward, an emissive surface that is composited by the "
                      + "lighting pass in one and drawn flat in the other — renders DIFFERENTLY in VR "
                      + "than on the flat screen, and that is a per-shader fact printed in the "
                      + "'passes' field of each full dump below");
        }
        else if (_headKnown)
        {
            sb.Append(" ⇒ same rendering path on both, so the path cannot be why a renderer looks "
                      + "different in VR");
        }

        AppendCommandBuffers(sb);

        int vrOnly = _headMask & ~_scenarioMask;
        sb.Append(" | LAYERS THE HEAD RENDERS AND THE GAME DOES NOT (the mod's OWN layer ")
          .Append(VRLayers.ModLayer)
          .Append(" is excluded from the candidate pool entirely and is not listed here): ");
        if (vrOnly == 0)
        {
            sb.Append("none — the head camera's mask is no wider than the ScenarioCamera's, so "
                      + "nothing on screen in VR is hidden on the flat screen by masking");
            return;
        }
        bool any = false;
        int populated = 0, populatedRenderers = 0;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((vrOnly & (1 << layer)) == 0)
                continue;
            int pop = _layerPopKnown ? LayerPop[layer] : -1;
            if (pop == 0 || layer == VRLayers.ModLayer)
                continue;   // an empty layer is not a finding, and our own layer is not a suspect
            string name = LayerMask.LayerToName(layer);
            sb.Append(any ? ", " : "").Append(layer).Append('=')
              .Append(string.IsNullOrEmpty(name) ? "<unnamed>" : name);
            if (pop >= 0)
            {
                sb.Append(' ').Append(pop).Append(" renderer(s), ").Append(LayerVis[layer]).Append(" visible");
                populated++;
                populatedRenderers += pop;
            }
            any = true;
        }
        if (!any)
        {
            sb.Append("the mask is wider by 0x").Append(vrOnly.ToString("X8"))
              .Append(" but EVERY layer in that difference is EMPTY in this scene ⇒ narrowing the "
                      + "head mask would change nothing that is visible, and no card below can be "
                      + "explained by 'the flat game never draws this layer'");
            return;
        }
        sb.Append(" (").Append(populated).Append(" populated layer(s), ").Append(populatedRenderers)
          .Append(" renderer(s) total). Anything on those layers is drawn in VR and never on the flat "
                  + "screen; cards below carry the VrOnly mark when they are");
    }

    /// <summary>Every CameraEvent a built-in-pipeline camera can carry a command buffer at. Written
    /// out rather than taken from <c>Enum.GetValues</c> (which allocates and boxes) and ordered the
    /// way the frame runs, so a reader can see WHERE in the frame each buffer sits.</summary>
    private static readonly CameraEvent[] AllCameraEvents =
    {
        CameraEvent.BeforeDepthTexture, CameraEvent.AfterDepthTexture,
        CameraEvent.BeforeDepthNormalsTexture, CameraEvent.AfterDepthNormalsTexture,
        CameraEvent.BeforeGBuffer, CameraEvent.AfterGBuffer,
        CameraEvent.BeforeReflections, CameraEvent.AfterReflections,
        CameraEvent.BeforeLighting, CameraEvent.AfterLighting,
        CameraEvent.BeforeFinalPass, CameraEvent.AfterFinalPass,
        CameraEvent.BeforeForwardOpaque, CameraEvent.AfterForwardOpaque,
        CameraEvent.BeforeImageEffectsOpaque, CameraEvent.AfterImageEffectsOpaque,
        CameraEvent.BeforeSkybox, CameraEvent.AfterSkybox,
        CameraEvent.BeforeForwardAlpha, CameraEvent.AfterForwardAlpha,
        CameraEvent.BeforeHaloAndLensFlares, CameraEvent.AfterHaloAndLensFlares,
        CameraEvent.BeforeImageEffects, CameraEvent.AfterImageEffects,
        CameraEvent.AfterEverything,
    };

    /// <summary>
    /// NAME the command buffers on both cameras, and say which of them the head camera cannot run.
    ///
    /// <para>WHY THIS IS EVIDENCE AND NOT TRIVIA. The head camera reports
    /// <c>commandBuffers: 0 total</c>; the game's ScenarioCamera reported <b>2</b> in the ModBuild 253
    /// hardware log, and the mod knew the name of only one of them (<c>TilesOcclusionGenerator</c>'s
    /// "Tile Occlusion Map Generation" at <c>BeforeGBuffer</c>, from the decompiled source). A buffer
    /// attached at a DEFERRED-only event — BeforeGBuffer, AfterGBuffer, Before/AfterLighting,
    /// Before/AfterReflections, Before/AfterFinalPass — is never reached on a FORWARD camera. So any
    /// compositing the game does there simply does not happen in VR, on the same geometry, and that is
    /// a mechanism for "it looks different in the headset" that is entirely independent of shaders,
    /// layers and the wall fade. If the second buffer composites decals, light pools or emissive
    /// cards, it is the answer. This block names it so the next reader does not have to guess.</para>
    /// </summary>
    private static void AppendCommandBuffers(StringBuilder sb)
    {
        sb.Append(" | COMMAND BUFFERS: ");
        AppendBuffersFor(sb, _head, "head");
        sb.Append("; ");
        AppendBuffersFor(sb, _scenarioCam, "ScenarioCamera");
    }

    private static void AppendBuffersFor(StringBuilder sb, Camera? cam, string label)
    {
        if (cam == null)
        {
            sb.Append(label).Append(" n/a (camera not resolved)");
            return;
        }
        try
        {
            int total = cam.commandBufferCount;
            sb.Append(label).Append(' ').Append(total);
            if (total == 0)
            {
                sb.Append(" (none — nothing outside the ordinary render loop is attached)");
                return;
            }
            bool forward = cam.actualRenderingPath == RenderingPath.Forward;
            sb.Append(':');
            for (int e = 0; e < AllCameraEvents.Length; e++)
            {
                CameraEvent evt = AllCameraEvents[e];
                CommandBuffer[] bufs = cam.GetCommandBuffers(evt);
                if (bufs.Length == 0)
                    continue;
                for (int i = 0; i < bufs.Length; i++)
                {
                    sb.Append(" '").Append(bufs[i].name).Append("' at ").Append(evt)
                      .Append(" (").Append(bufs[i].sizeInBytes).Append("B)");
                }
                if (forward && IsDeferredOnlyEvent(evt))
                    sb.Append(" [DEFERRED-ONLY EVENT — INERT on this forward camera]");
            }
        }
        catch (Exception e)
        {
            sb.Append(label).Append(" n/a (").Append(e.GetType().Name).Append(')');
        }
    }

    private static bool IsDeferredOnlyEvent(CameraEvent evt) => evt
        is CameraEvent.BeforeGBuffer or CameraEvent.AfterGBuffer
        or CameraEvent.BeforeReflections or CameraEvent.AfterReflections
        or CameraEvent.BeforeLighting or CameraEvent.AfterLighting
        or CameraEvent.BeforeFinalPass or CameraEvent.AfterFinalPass;

    private static void AppendPopulation(StringBuilder sb)
    {
        sb.Append(" | POPULATION: ").Append(_renderersOffered).Append(" renderer(s) and ")
          .Append(_slotsOffered).Append(" material slot(s) offered; ").Append(_matched)
          .Append(" slot(s) matched a material test on ").Append(_distinctGlowShaders)
          .Append(" distinct glow/light/decal shader(s); ").Append(Pool.Count)
          .Append(" candidate(s) pooled (cap ").Append(MaxCandidates).Append("), ")
          .Append(_dropped).Append(" dropped");
        if (_dropped > 0)
            sb.Append(" — the largest dropped one spanned ").Append(_droppedMaxSpan.ToString("F0")).Append("px");
        sb.Append("; ").Append(_modLayerSkipped)
          .Append(" renderer(s) were REFUSED for being on the mod's own layer ")
          .Append(VRLayers.ModLayer)
          .Append(" — in ModBuild 253 those filled all 14 detailed records and 41 of 96 candidates "
                  + "with the mod's environment room, its hand meshes and its pointer laser, because "
                  + "they are large, flat, transparent-queued and on a layer the ScenarioCamera does "
                  + "not render, which lights four marks at once. The subject is GAME geometry")
          .Append("; the collection half cost ").Append(_boundsReads)
          .Append(" Renderer.bounds read(s) and ").Append(_slotsOffered)
          .Append(" dictionary lookup(s), and nothing else per renderer");
        sb.Append(" | SELECTOR — a candidate needs ANY ONE of four independent tests, and every record "
                  + "prints which ones it hit: [Shader] the shader name contains one of ")
          .Append(string.Join(", ", ShaderMarks))
          .Append("; [Queue] the material's render queue is >= 2900 (Transparent or later — 2450 "
                  + "alpha-test foliage is deliberately excluded, it would drown the ranking); "
                  + "[Layer] the object sits on 1/18/28/29/30/31 (TransparentFX, Particle, and this "
                  + "game's three render-target layers) or on a layer only the head camera renders; "
                  + "[Plate] the object is SUBMITTED, at least ").Append(SubjectMinPx.ToString("F0"))
          .Append("px across and its thinnest AABB axis is <= ").Append((PlateRatio * 100f).ToString("F0"))
          .Append("% of its longest — i.e. it is a QUAD, which is the only test that does not need to "
                  + "know what the subject is called. RANKING has a SUBJECT BAND: submitted plate-"
                  + "shaped candidates between ").Append(SubjectMinPx.ToString("F0")).Append(" and ")
          .Append(SubjectMaxPx.ToString("F0"))
          .Append("px outrank everything else by construction and are ordered among themselves by how "
                  + "close their span is to ").Append(SubjectTargetPx.ToString("F0"))
          .Append("px ON A LOG SCALE (so 45px and 180px rank equally, and an estimate that is out by "
                  + "a factor of two still keeps the subject near the top); everything outside the "
                  + "band is ordered by span x8 if VrOnly x3 if Plate x0.05 if not submitted. The "
                  + "band exists because ModBuild 253 ranked by raw span "
                  + "and filled every detailed record with the biggest things on screen — a 71,202px "
                  + "ground plane, a 17,983px star dome — while the photographed rectangles are of "
                  + "order SEVENTY pixels across. Bigger is not more likely to be the subject; being "
                  + "the shape and size in the photograph is. The score is printed so this can be "
                  + "checked rather than trusted");
        sb.Append(" | READ 'submitted' WITH CARE: it is enabled AND Renderer.isVisible AND in the head "
                  + "culling mask, and Unity's isVisible is true when ANY camera can see the renderer "
                  + "— the ScenarioCamera, a preview station, an RT capture. The screen rect on each "
                  + "record is the field that says whether the HEAD camera can see it, and a rect "
                  + "marked OFF SCREEN on a 'submitted' card is that gotcha, not a contradiction");
        if (!_projectionKnown)
        {
            sb.Append(" | WARNING: the head projection could not be read this window, so EVERY span "
                      + "below is 0 and the ranking is meaningless. Treat this sample as unranked");
        }
    }

    // ==========================================================================================
    //  the records
    // ==========================================================================================

    private static List<Candidate> Rank()
    {
        var ranked = new List<Candidate>(Pool.Count);
        for (int i = 0; i < Pool.Count; i++)
        {
            if (Pool[i].R != null)
                ranked.Add(Pool[i]);
        }
        ranked.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return ranked;
    }

    private static void AppendCards(StringBuilder sb, List<Candidate> ranked)
    {
        if (ranked.Count == 0)
        {
            sb.Append(" | NOTHING MATCHED — not one renderer in the sample hit any of the four tests. "
                      + "That is a strong statement and it should be checked before it is believed: "
                      + "the Plate test alone matches every submitted flat quad over "
                      + SubjectMinPx.ToString("F0") + "px, so an empty population here means either "
                      + "the walk sampled a frame with no scenario content in it (check the renderer "
                      + "count above) or the head projection was unreadable (see the WARNING, if any)");
            return;
        }

        int n = Mathf.Min(MaxCards, ranked.Count);
        for (int i = 0; i < n; i++)
        {
            Candidate c = ranked[i];
            Renderer? r = c.R;
            if (r == null)
                continue;
            sb.Append(" | [").Append(i).Append("] '").Append(PathOf(r.transform)).Append("' ");
            AppendIdentity(sb, c, r);
            AppendScreen(sb, c);
            AppendBlend(sb, c);
            AppendTint(sb, c, r);
            if (i < FullDumpCards)
            {
                AppendProps(sb, c);
                AppendPasses(sb, c);
            }
            else
            {
                sb.Append(" | (full property/pass dump withheld — only the top ").Append(FullDumpCards)
                  .Append(" by score get one, to bound the line)");
            }
        }
    }

    private static void AppendIdentity(StringBuilder sb, Candidate c, Renderer r)
    {
        Material? mat = c.Mat;
        Shader? sh = c.Sh;
        sb.Append(r.GetType().Name).Append(" shader '")
          .Append(sh != null ? SafeName(sh) : "<no material slot offered>")
          .Append("' material '").Append(mat != null ? SafeName(mat) : "n/a").Append('\'');
        if (mat != null)
        {
            int q;
            try { q = mat.renderQueue; } catch (Exception) { q = -1; }
            sb.Append(" q").Append(q);
        }
        if (sh != null)
        {
            bool supported;
            try { supported = sh.isSupported; } catch (Exception) { supported = true; }
            if (!supported)
            {
                sb.Append(" SHADER NOT SUPPORTED ON THIS GPU/PATH ⇒ Unity is drawing this with the "
                          + "shader's Fallback, which is a different pass with different blending "
                          + "and is on its own enough to turn a soft card into a flat quad");
            }
        }
        int layer = r.gameObject.layer;
        string layerName = LayerMask.LayerToName(layer);
        sb.Append(c.Submitted ? " enabled+visible+submitted" : " not-submitted(")
          .Append(c.Submitted ? "" : (r.enabled ? "enabled" : "DISABLED"))
          .Append(c.Submitted ? "" : (r.isVisible ? "+visible" : "+offscreen"))
          .Append(c.Submitted ? "" : ")")
          .Append(" layer ").Append(layer).Append('=')
          .Append(string.IsNullOrEmpty(layerName) ? "<unnamed>" : layerName)
          .Append(" marks[").Append(c.Marks).Append("] span ").Append(c.Span.ToString("F0"))
          .Append("px score ").Append(c.Score.ToString("F0"))
          .Append(" AABB c(").Append(c.B.center.x.ToString("F2")).Append(',')
          .Append(c.B.center.y.ToString("F2")).Append(',').Append(c.B.center.z.ToString("F2"))
          .Append(") s(").Append(c.B.size.x.ToString("F3")).Append(',')
          .Append(c.B.size.y.ToString("F3")).Append(',').Append(c.B.size.z.ToString("F3")).Append(')');
    }

    /// <summary>The AABB's 12 edges, as index pairs into the 8 corners built with bit0=x, bit1=y,
    /// bit2=z. Used to clip the box against the near plane instead of projecting bare corners.</summary>
    private static readonly int[] BoxEdges =
    {
        0, 1, 0, 2, 0, 4, 1, 3, 1, 5, 2, 3,
        2, 6, 3, 7, 4, 5, 4, 6, 5, 7, 6, 7
    };

    private static readonly Vector3[] CornerScratch = new Vector3[8];

    /// <summary>
    /// WHERE THIS IS ON THE SCREEN, computed a way that survives an object straddling the near plane.
    ///
    /// <para>WHAT WAS WRONG IN ModBuild 253, in full. The first version projected the eight AABB
    /// corners with <c>Camera.WorldToViewportPoint</c>, skipped the ones with <c>z &lt;= 0</c> and took
    /// the min/max of the rest. That is broken for any object the camera is inside or beside — which,
    /// in a room-scale rig standing in a diorama, is most of them. A corner just BEHIND the eye is not
    /// merely skipped: the corners just in FRONT of it project to enormous coordinates, because their
    /// perspective divisor goes to zero. Every single record in the 253 hardware log read
    /// <c>PARTIALLY BEHIND the camera</c> and printed rectangles like <c>px -152401,-9423 ..
    /// 97640,11227</c>. Half a million pixels in a 3840-wide frame is not a conservative bound, it is
    /// noise, and the caveat attached to it did not rescue it: the rect was supposed to be the
    /// identification key against the photograph and it identified nothing.</para>
    ///
    /// <para>WHAT IT DOES NOW. The box is clipped against the near plane in VIEW space, edge by edge,
    /// before anything is projected: for each of the twelve edges, an endpoint in front of the near
    /// plane contributes itself, and an edge that straddles the plane contributes the exact
    /// intersection point. Only points at or in front of the near plane are ever divided by, so no
    /// coordinate can blow up. The result is the true screen-space bound of the box's visible part.
    /// The printed rectangle is then CLAMPED to the frame, and when the unclamped bound ran outside it
    /// the record says so in words rather than printing a number nobody can use.</para>
    ///
    /// <para>Coordinates are TOP-LEFT origin, the way an image viewer reports them, and scaled to a
    /// 3840x2160 screenshot so a reader can type them straight into the photograph.</para>
    /// </summary>
    private static void AppendScreen(StringBuilder sb, Candidate c)
    {
        sb.Append(" | screen: ");
        Camera? head = _head;
        if (head == null || !_projectionKnown || c.B.size == Vector3.zero)
        {
            sb.Append("n/a (no head camera, unreadable projection, or empty bounds)");
            return;
        }
        try
        {
            Matrix4x4 toView = head.worldToCameraMatrix;
            Matrix4x4 proj = head.projectionMatrix;
            float near = Mathf.Max(head.nearClipPlane, 1e-4f);

            Vector3 ctr = c.B.center, ext = c.B.extents;
            for (int i = 0; i < 8; i++)
            {
                CornerScratch[i] = toView.MultiplyPoint3x4(new Vector3(
                    ctr.x + ((i & 1) != 0 ? ext.x : -ext.x),
                    ctr.y + ((i & 2) != 0 ? ext.y : -ext.y),
                    ctr.z + ((i & 4) != 0 ? ext.z : -ext.z)));
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool clipped = false;
            int accepted = 0;

            for (int e = 0; e < BoxEdges.Length; e += 2)
            {
                Vector3 a = CornerScratch[BoxEdges[e]];
                Vector3 b = CornerScratch[BoxEdges[e + 1]];
                // Unity's view space looks down -Z, so distance in front of the eye is -z.
                float da = -a.z, db = -b.z;
                bool inA = da >= near, inB = db >= near;
                if (!inA && !inB)
                {
                    clipped = true;
                    continue;
                }
                if (inA)
                    Accumulate(proj, a, ref minX, ref minY, ref maxX, ref maxY, ref accepted);
                if (inB)
                    Accumulate(proj, b, ref minX, ref minY, ref maxX, ref maxY, ref accepted);
                if (inA == inB)
                    continue;
                clipped = true;
                float denom = da - db;
                if (Mathf.Abs(denom) < 1e-6f)
                    continue;
                float t = (near - db) / denom;
                Accumulate(proj, b + (a - b) * t, ref minX, ref minY, ref maxX, ref maxY, ref accepted);
            }

            if (accepted == 0)
            {
                sb.Append("ENTIRELY BEHIND the near plane this frame — the head camera cannot see any "
                          + "part of this renderer, so it is not what the photograph shows");
                return;
            }

            // Viewport y is bottom-up; an image viewer is top-down.
            float top = 1f - maxY, bottom = 1f - minY;
            bool outside = minX < 0f || maxX > 1f || top < 0f || bottom > 1f;
            float cx = Mathf.Clamp01(0.5f * (minX + maxX));
            float cy = Mathf.Clamp01(0.5f * (top + bottom));
            float x0 = Mathf.Clamp01(minX), x1 = Mathf.Clamp01(maxX);
            float y0 = Mathf.Clamp01(top), y1 = Mathf.Clamp01(bottom);

            sb.Append("px ").Append((x0 * PhotoW).ToString("F0")).Append(',')
              .Append((y0 * PhotoH).ToString("F0")).Append(" .. ")
              .Append((x1 * PhotoW).ToString("F0")).Append(',').Append((y1 * PhotoH).ToString("F0"))
              .Append(" (").Append(((x1 - x0) * PhotoW).ToString("F0")).Append('x')
              .Append(((y1 - y0) * PhotoH).ToString("F0")).Append("), centre ")
              .Append((cx * PhotoW).ToString("F0")).Append(',').Append((cy * PhotoH).ToString("F0"))
              .Append(" — top-left origin, scaled to a 3840x2160 screenshot and CLAMPED to the frame");
            if (x1 <= 0f || x0 >= 1f || y1 <= 0f || y0 >= 1f)
                sb.Append("; OFF SCREEN (the unclamped bound lies entirely outside the viewport)");
            else if (outside)
                sb.Append("; the unclamped bound extends past the frame edge, so this renderer is "
                          + "larger than the rectangle printed");
            if (clipped)
                sb.Append("; near-plane clipped (the box straddles the eye), exact for the visible part");
        }
        catch (Exception e)
        {
            sb.Append("n/a (").Append(e.GetType().Name).Append(')');
        }
    }

    /// <summary>Project one VIEW-space point that is already known to be at or in front of the near
    /// plane, and fold it into the running viewport bound. <c>MultiplyPoint</c> does the perspective
    /// divide; the divisor cannot approach zero here because the caller clipped first.</summary>
    private static void Accumulate(in Matrix4x4 proj, Vector3 viewPos,
                                   ref float minX, ref float minY,
                                   ref float maxX, ref float maxY, ref int accepted)
    {
        Vector3 ndc = proj.MultiplyPoint(viewPos);
        float vx = (ndc.x + 1f) * 0.5f;
        float vy = (ndc.y + 1f) * 0.5f;
        if (float.IsNaN(vx) || float.IsNaN(vy) || float.IsInfinity(vx) || float.IsInfinity(vy))
            return;
        if (vx < minX) minX = vx;
        if (vx > maxX) maxX = vx;
        if (vy < minY) minY = vy;
        if (vy > maxY) maxY = vy;
        accepted++;
    }

    /// <summary>
    /// THE FIELD THAT DECIDES WHETHER AN ALPHA WRITE COULD EVER DIM THIS CARD — and the field
    /// ModBuild 251 printed as <c>src=n/a dst=n/a zwrite=n/a</c> for every single record, which is
    /// not a measurement, it is a shrug. It read three material properties and printed n/a when they
    /// were absent, never saying that absence is itself the answer: Unity exposes NO runtime query
    /// for a pass's fixed-function blend state, so a shader that hardcodes <c>Blend One One</c> in its
    /// source has nothing to read. This version says which of the six state properties the material
    /// actually declares, and when it declares none it says what that means and falls back to the
    /// tags, which are readable.
    /// </summary>
    private static void AppendBlend(StringBuilder sb, Candidate c)
    {
        Material? mat = c.Mat;
        sb.Append(" | blend: ");
        if (mat == null)
        {
            sb.Append("no material slot was offered for this renderer (it was pooled on geometry "
                      + "and layer alone), so nothing about its blending is known");
            return;
        }
        Scratch.Clear();
        for (int i = 0; i < StateProps.Length; i++)
        {
            try
            {
                if (!mat.HasProperty(StateProps[i]))
                    continue;
                float v = mat.GetFloat(StateProps[i]);
                Scratch.Add(StatePropNames[i] + "=" + (i < 2 ? BlendName(Mathf.RoundToInt(v)) : v.ToString("0.##")));
            }
            catch (Exception)
            {
                // an unreadable state property is simply not reported
            }
        }
        if (Scratch.Count > 0)
        {
            sb.Append(string.Join(" ", Scratch.ToArray()));
        }
        else
        {
            sb.Append("this material declares NONE of _SrcBlend/_DstBlend/_ZWrite/_ZTest/_Cull/_BlendOp. "
                      + "That is not a missing reading — it means the blend is written literally into "
                      + "the shader's pass and Unity offers no runtime API to read it back. The tags "
                      + "below are the only readable proxy");
        }
        Scratch.Clear();
        for (int i = 0; i < TagNames.Length; i++)
        {
            try
            {
                string v = mat.GetTag(TagNames[i], false, string.Empty);
                if (!string.IsNullOrEmpty(v))
                    Scratch.Add(TagNames[i] + "=" + v);
            }
            catch (Exception)
            {
                // skip
            }
        }
        sb.Append(" | tags: ").Append(Scratch.Count == 0 ? "(none declared)" : string.Join(" ", Scratch.ToArray()));
    }

    /// <summary>Whether the wall fade is driving this card, and whether it could. A card with no block
    /// is not being driven (a CLAIMING answer); a card with a block whose alpha is near zero while it
    /// is still bright in the headset is being driven and ignoring it (a RENDERING answer).</summary>
    private static void AppendTint(StringBuilder sb, Candidate c, Renderer r)
    {
        Material? mat = c.Mat;
        if (mat == null)
            return;
        int tintIndex = -1;
        for (int i = 0; i < TintProps.Length; i++)
        {
            try
            {
                if (!mat.HasProperty(TintProps[i]))
                    continue;
            }
            catch (Exception)
            {
                continue;
            }
            tintIndex = i;
            break;
        }
        sb.Append(" | fade: ");
        if (tintIndex < 0)
        {
            sb.Append("no _TintColor/_Color/_BaseColor on this material ⇒ the wall fade cannot "
                      + "classify it as an alpha prop at all, and no alpha ramp can ever reach it");
            return;
        }
        Color authored = Color.clear;
        try { authored = mat.GetColor(TintProps[tintIndex]); } catch (Exception) { }
        sb.Append(TintPropNames[tintIndex]).Append(" authored a=").Append(authored.a.ToString("F3"));
        try
        {
            if (!r.HasPropertyBlock())
            {
                sb.Append(", NO property block on the renderer ⇒ nothing is driving this card right "
                          + "now (a CLAIMING answer, not a rendering one)");
                return;
            }
            _blockScratch ??= new MaterialPropertyBlock();
            _blockScratch.Clear();
            r.GetPropertyBlock(_blockScratch);
            if (!_blockScratch.HasColor(TintProps[tintIndex]))
            {
                sb.Append(", a property block IS attached but carries no ")
                  .Append(TintPropNames[tintIndex]).Append(" ⇒ something else owns this block");
                return;
            }
            float a = _blockScratch.GetColor(TintProps[tintIndex]).a;
            sb.Append(", LIVE block a=").Append(a.ToString("F3"));
            if (a < 0.05f)
            {
                sb.Append(" ⇒ a fade IS writing this card to invisible. If it is still bright in the "
                          + "headset the write is being ignored, and the blend/tags above say whether "
                          + "it ever could have worked");
            }
        }
        catch (Exception)
        {
            sb.Append(", property block unreadable");
        }
    }

    /// <summary>
    /// EVERY property the material's shader declares, with its live value — not a hard-coded list of
    /// nine names. ModBuild 251 printed <c>depth-fade props: NONE</c> for every card near the gate,
    /// and the honest reading of that was never "these cards have no fade": it was "these cards do not
    /// use any of the nine names I guessed". <c>LightShaftShd</c> may well call its fade something
    /// else entirely, and this is the dump that would show it.
    /// </summary>
    private static void AppendProps(StringBuilder sb, Candidate c)
    {
        Material? mat = c.Mat;
        Shader? sh = c.Sh;
        sb.Append(" | props: ");
        if (mat == null || sh == null)
        {
            sb.Append("n/a (no material slot)");
            return;
        }
        int n;
        try { n = sh.GetPropertyCount(); }
        catch (Exception e) { sb.Append("n/a (").Append(e.GetType().Name).Append(')'); return; }

        sb.Append(n).Append(" declared:");
        int printed = 0;
        for (int i = 0; i < n; i++)
        {
            if (printed >= MaxProps)
            {
                sb.Append(" +").Append(n - i).Append(" more");
                break;
            }
            string name;
            ShaderPropertyType type;
            try
            {
                name = sh.GetPropertyName(i);
                type = sh.GetPropertyType(i);
            }
            catch (Exception)
            {
                continue;
            }
            sb.Append(' ').Append(name).Append('=');
            try
            {
                switch (type)
                {
                    case ShaderPropertyType.Color:
                    {
                        Color col = mat.GetColor(name);
                        sb.Append('(').Append(col.r.ToString("F3")).Append(',').Append(col.g.ToString("F3"))
                          .Append(',').Append(col.b.ToString("F3")).Append(",a=").Append(col.a.ToString("F3")).Append(')');
                        break;
                    }
                    case ShaderPropertyType.Vector:
                    {
                        Vector4 v = mat.GetVector(name);
                        sb.Append('(').Append(v.x.ToString("F2")).Append(',').Append(v.y.ToString("F2"))
                          .Append(',').Append(v.z.ToString("F2")).Append(',').Append(v.w.ToString("F2")).Append(')');
                        break;
                    }
                    case ShaderPropertyType.Texture:
                        AppendTexture(sb, mat.GetTexture(name));
                        break;
                    default:
                        sb.Append(mat.GetFloat(name).ToString("0.###"));
                        break;
                }
            }
            catch (Exception)
            {
                sb.Append("<unreadable>");
            }
            printed++;
        }

        try
        {
            string[] kw = mat.shaderKeywords;
            sb.Append(" | keywords: ").Append(kw.Length == 0 ? "(none)" : string.Join(",", kw));
        }
        catch (Exception)
        {
            sb.Append(" | keywords: (unreadable)");
        }
    }

    /// <summary>A texture slot: name, size, format and mip count. A card whose _MainTex is null draws
    /// Unity's implicit WHITE, which is a flat opaque rectangle by itself; a card whose texture has no
    /// alpha channel (a DXT1/BC1 format) cannot carry a soft edge at all however it is blended. Both
    /// are one-line answers to 'why is this a hard rectangle' and neither was printed before.</summary>
    private static void AppendTexture(StringBuilder sb, Texture? tex)
    {
        if (tex == null)
        {
            sb.Append("<NULL — Unity substitutes white, which draws as a flat opaque rectangle>");
            return;
        }
        sb.Append('<').Append(SafeName(tex)).Append(' ').Append(tex.width).Append('x').Append(tex.height);
        if (tex is Texture2D t2)
        {
            sb.Append(' ').Append(t2.format);
            bool hasAlpha = t2.format is TextureFormat.DXT5 or TextureFormat.DXT5Crunched
                or TextureFormat.RGBA32 or TextureFormat.ARGB32 or TextureFormat.BGRA32
                or TextureFormat.RGBA4444 or TextureFormat.ARGB4444 or TextureFormat.Alpha8
                or TextureFormat.RGBAHalf or TextureFormat.RGBAFloat or TextureFormat.BC7
                or TextureFormat.RGBA64;
            if (!hasAlpha)
                sb.Append(" NO-ALPHA-CHANNEL");
        }
        sb.Append(" mips=").Append(tex.mipmapCount).Append('>');
    }

    /// <summary>
    /// THE PASSES, and the LightMode tag of each. This is the field the PATH CONTRAST block above
    /// points at: the game draws this scene through one rendering path and the mod through another,
    /// and a material whose subshaders offer a Deferred pass but no ForwardBase is drawn on the head
    /// camera through the shader's FALLBACK — a different program, with different blending, on the
    /// same geometry. That would look like exactly what was photographed, and nothing in this
    /// codebase has ever printed it.
    /// </summary>
    private static void AppendPasses(StringBuilder sb, Candidate c)
    {
        Material? mat = c.Mat;
        Shader? sh = c.Sh;
        sb.Append(" | passes: ");
        if (mat == null || sh == null)
        {
            sb.Append("n/a (no material slot)");
            return;
        }
        try
        {
            int pc = mat.passCount;
            sb.Append("active subshader has ").Append(pc).Append(':');
            for (int i = 0; i < pc && i < 8; i++)
            {
                string pn = mat.GetPassName(i);
                sb.Append(" [").Append(i).Append(']').Append(string.IsNullOrEmpty(pn) ? "<unnamed>" : pn);
            }
        }
        catch (Exception e)
        {
            sb.Append("mat.passCount threw ").Append(e.GetType().Name);
        }
        bool hasDeferred = false, hasForwardBase = false, hasUntagged = false, tagsRead = false;
        try
        {
            bool supported;
            try { supported = sh.isSupported; } catch (Exception) { supported = true; }
            int subs = sh.subshaderCount;
            sb.Append(" | isSupported=").Append(supported ? "yes" : "NO")
              .Append(", shader declares ").Append(subs).Append(" subshader(s), LightMode per pass:");
            for (int s = 0; s < subs && s < 4; s++)
            {
                int pc = sh.GetPassCountInSubshader(s);
                sb.Append(" sub").Append(s).Append('{');
                for (int p = 0; p < pc && p < 8; p++)
                {
                    ShaderTagId tag = sh.FindPassTagValue(s, p, LightModeTag);
                    string name = tag.name;
                    sb.Append(p == 0 ? "" : ",").Append(string.IsNullOrEmpty(name) ? "<none>" : name);
                    tagsRead = true;
                    if (string.IsNullOrEmpty(name))
                        hasUntagged = true;
                    else if (name.Equals("Deferred", StringComparison.OrdinalIgnoreCase))
                        hasDeferred = true;
                    else if (name.Equals("ForwardBase", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("ForwardAdd", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("Always", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("Vertex", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("VertexLM", StringComparison.OrdinalIgnoreCase)
                             || name.Equals("VertexLMRGBM", StringComparison.OrdinalIgnoreCase))
                        hasForwardBase = true;
                }
                sb.Append('}');
            }
        }
        catch (Exception e)
        {
            sb.Append(" | subshader tags n/a (").Append(e.GetType().Name).Append(')');
            return;
        }

        // THE PROOF SENTENCE, or its absence. Stated in exactly these words so it can be grepped, and
        // stated ONLY when the tags were actually read — an unreadable tag set must not look like a
        // clean forward shader.
        if (!tagsRead)
        {
            sb.Append(" ⇒ no LightMode tag could be read, so this card says NOTHING about the "
                      + "forward/deferred difference either way");
        }
        else if (hasDeferred && !hasForwardBase && !hasUntagged)
        {
            sb.Append(" ⇒ DEFERRED PASS BUT NO FORWARDBASE. This material cannot be drawn as authored "
                      + "by the mod's FORWARD head camera: Unity falls through to the shader's "
                      + "Fallback, a different program with different blending, on the same geometry. "
                      + "The game's DeferredShading ScenarioCamera draws it correctly. THAT IS THE "
                      + "MECHANISM, and it is proven for this card, not inferred");
        }
        else if (hasDeferred)
        {
            sb.Append(" ⇒ has a Deferred pass AND a forward-drawable pass, so the path difference "
                      + "changes how it is LIT but cannot make it fall back to another program");
        }
        else
        {
            sb.Append(" ⇒ no Deferred pass at all: this card is drawn by the same program on both "
                      + "cameras, so the forward/deferred difference is NOT why it looks like this");
        }
    }

    /// <summary>
    /// The tail, and it now carries the MATCHING KEY. ModBuild 253's tail printed only a name, a span
    /// and the marks, which is not enough to recognise anything: the whole point of this line is that
    /// a reader with the photograph open can find the record that sits where a rectangle sits. So every
    /// tail entry gets its shader, its material, its queue and its screen-centre pixel — about 140
    /// characters each, which is a cheap price for the only field that can close this.
    /// </summary>
    private static void AppendTail(StringBuilder sb, List<Candidate> ranked)
    {
        if (ranked.Count <= MaxCards)
            return;
        int shown = Mathf.Min(MaxTail, ranked.Count - MaxCards);
        sb.Append(" | RANKED BUT NOT DETAILED (next ").Append(shown).Append(" of ")
          .Append(ranked.Count - MaxCards)
          .Append("; centre px is top-left origin on a 3840x2160 screenshot — in Wandproblem.jpg the "
                  + "two rectangles sit at roughly 670,1120 and 750,800):");
        for (int i = MaxCards; i < MaxCards + shown; i++)
        {
            Candidate c = ranked[i];
            if (c.R == null)
                continue;
            sb.Append(" [").Append(i).Append("] '").Append(c.R.name).Append('\'');
            if (c.Sh != null)
                sb.Append(" sh'").Append(SafeName(c.Sh)).Append('\'');
            if (c.Mat != null)
            {
                sb.Append(" mat'").Append(SafeName(c.Mat)).Append('\'');
                try { sb.Append(" q").Append(c.Mat.renderQueue); } catch (Exception) { }
            }
            sb.Append(' ').Append(c.Span.ToString("F0")).Append("px ").Append(c.Marks)
              .Append(c.InBand ? " IN-BAND" : "")
              .Append(c.Submitted ? "" : " not-submitted");
            AppendCentre(sb, c);
        }
    }

    /// <summary>Just the clamped screen centre, for the compact tail entries.</summary>
    private static void AppendCentre(StringBuilder sb, Candidate c)
    {
        Camera? head = _head;
        if (head == null || !_projectionKnown || c.B.size == Vector3.zero)
            return;
        try
        {
            Vector3 v = head.WorldToViewportPoint(c.B.center);
            if (v.z <= head.nearClipPlane)
            {
                sb.Append(" centre BEHIND-EYE");
                return;
            }
            sb.Append(" centre ").Append((Mathf.Clamp01(v.x) * PhotoW).ToString("F0")).Append(',')
              .Append((Mathf.Clamp01(1f - v.y) * PhotoH).ToString("F0"));
            if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f)
                sb.Append("(off-screen)");
        }
        catch (Exception)
        {
            // a centre that cannot be projected simply is not printed
        }
    }

    // ==========================================================================================
    //  the verdict — derived ONLY from cards that could actually be the subject
    // ==========================================================================================

    private static void AppendVerdict(StringBuilder sb, List<Candidate> ranked)
    {
        int eligible = 0, plates = 0, vrOnly = 0, additive = 0, noTint = 0, nullMainTex = 0;
        int inBand = 0, deferredOnly = 0;
        float biggest = 0f;
        for (int i = 0; i < ranked.Count; i++)
        {
            Candidate c = ranked[i];
            if (!c.Submitted || c.Span < SubjectMinPx)
                continue;
            eligible++;
            if (c.InBand)
                inBand++;
            if (IsDeferredOnly(c.Sh))
                deferredOnly++;
            if (c.Span > biggest)
                biggest = c.Span;
            if ((c.Marks & Mark.Plate) != 0)
                plates++;
            if ((c.Marks & Mark.VrOnly) != 0)
                vrOnly++;
            Material? mat = c.Mat;
            if (mat == null)
                continue;
            try
            {
                if (mat.HasProperty(StateProps[1]) && Mathf.RoundToInt(mat.GetFloat(StateProps[1])) == 1)
                    additive++;
                bool hasTint = false;
                for (int t = 0; t < TintProps.Length; t++)
                {
                    if (!mat.HasProperty(TintProps[t]))
                        continue;
                    hasTint = true;
                    break;
                }
                if (!hasTint)
                    noTint++;
                if (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == null && mat.mainTexture == null)
                    nullMainTex++;
            }
            catch (Exception)
            {
                // a card whose material cannot be probed simply does not contribute to the counts
            }
        }

        sb.Append(" | VERDICT — derived ONLY from cards that are SUBMITTED and at least ")
          .Append(SubjectMinPx.ToString("F0"))
          .Append("px across, because a card nobody can see cannot be the one in the photograph "
                  + "(ModBuild 251's verdict was built from three offscreen zero-sized monster "
                  + "effects and was wrong): ");
        if (eligible == 0)
        {
            sb.Append("NO CARD IN THIS SAMPLE COULD BE THE SUBJECT. Every candidate is either not "
                      + "submitted or smaller than the floor, so this window has nothing to say about "
                      + "the gate rectangles and no conclusion is drawn. Read the best-latched sample "
                      + "below instead, or take a sample while looking at the gate");
            return;
        }
        sb.Append(eligible).Append(" eligible card(s), the largest ").Append(biggest.ToString("F0"))
          .Append("px; ").Append(inBand).Append(" are IN THE SUBJECT BAND (submitted flat quads of ")
          .Append(SubjectMinPx.ToString("F0")).Append('-').Append(SubjectMaxPx.ToString("F0"))
          .Append("px — the class the photograph shows), ").Append(deferredOnly)
          .Append(" of the eligible cards have a DEFERRED PASS AND NO FORWARD-DRAWABLE PASS (those "
                  + "cannot be drawn as authored by this forward head camera and ARE the mechanism), ")
          .Append(plates).Append(" of them are flat QUADS (the shape in the photo), ")
          .Append(vrOnly).Append(" are on a layer the flat game never draws, ").Append(additive)
          .Append(" declare _DstBlend=One (ADDITIVE — an alpha write can never dim those, whatever is "
                  + "claiming them), ").Append(noTint)
          .Append(" expose no colour property the wall fade could write at all, and ").Append(nullMainTex)
          .Append(" have a NULL main texture (Unity draws white, i.e. a flat opaque rectangle). ");
        if (deferredOnly > 0)
        {
            sb.Append("START WITH THE DEFERRED-ONLY ONES: the game draws this scene DeferredShading "
                      + "and the mod draws it Forward, so a material whose only lighting pass is "
                      + "Deferred is drawn here through its Fallback — a different program with "
                      + "different blending, which is exactly how a soft card becomes a flat opaque "
                      + "rectangle. That is proven per card in the 'passes' field, not inferred");
        }
        else if (vrOnly > 0)
        {
            sb.Append("START WITH THE VrOnly ONES: they are drawn by the head camera and by nothing "
                      + "else, which matches 'ich kann mich nicht erinnern, dass es flat sowas gab' "
                      + "exactly, and the remedy for that class is a culling mask, not a shader");
        }
        else if (plates > 0)
        {
            sb.Append("No card is VR-only, so masking is NOT the explanation. Compare the screen rects "
                      + "of the flat quads above against the rectangles in schwebende_lichter.jpg — "
                      + "the one whose rect lands on a rectangle IS the subject, and its props, "
                      + "keywords, tags and pass list are the next thing to read");
        }
        else
        {
            sb.Append("No flat quad is eligible in this sample, so the subject was probably not on "
                      + "screen when the walk fired; do not conclude from these records");
        }
    }

    // ==========================================================================================
    //  best-sample latch — so one good window keeps answering
    // ==========================================================================================

    /// <summary>
    /// Keep the richest sample this session. The ModBuild 251 A/B produced ONE armed window and it
    /// landed on a five-renderer frame, so the whole session said <c>NOTHING MATCHED</c> — an
    /// artefact of sampling that reads exactly like a finding. Latching the best sample means a
    /// single window that caught the gate keeps answering on every window afterwards, clearly marked
    /// with its age so nobody mistakes it for live state.
    /// </summary>
    private static void LatchBestSample(StringBuilder sb, List<Candidate> ranked)
    {
        float best = 0f;
        for (int i = 0; i < ranked.Count; i++)
        {
            Candidate c = ranked[i];
            if (!c.Submitted || (c.Marks & Mark.Plate) == 0)
                continue;
            if (c.Span > best)
                best = c.Span;
        }
        if (best <= _bestSampleScore)
            return;
        _bestSampleScore = best;
        _bestSampleAt = Time.unscaledTime;
        try { _bestSampleScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name; }
        catch (Exception) { _bestSampleScene = "?"; }
        // Store the CARD block of this line only — the globals are re-printed live every window.
        int cut = sb.ToString().IndexOf(" | [0] ", StringComparison.Ordinal);
        _bestSample = cut < 0 ? string.Empty : sb.ToString(cut, sb.Length - cut);
        if (_bestSample.Length > 6000)
            _bestSample = _bestSample.Substring(0, 6000) + " …(truncated)";
    }

    private static void AppendBestSample(StringBuilder sb)
    {
        if (_bestSample.Length == 0 || _bestSampleAt < 0f)
            return;
        float age = Time.unscaledTime - _bestSampleAt;
        if (age < 1f)
            return;   // it IS this window's sample; do not print it twice
        sb.Append(" | BEST SAMPLE SO FAR — captured ").Append(age.ToString("F0"))
          .Append("s ago in scene '").Append(_bestSampleScene).Append("', largest flat submitted quad ")
          .Append(_bestSampleScore.ToString("F0"))
          .Append("px. THIS IS STALE STATE, kept because a census that only answers on the window "
                  + "somebody else chose to walk is how ModBuild 251's whole A/B session produced one "
                  + "useless sample. Positions and block values below were true THEN:")
          .Append(_bestSample);
    }

    // ==========================================================================================
    //  helpers
    // ==========================================================================================

    /// <summary>Hierarchy path, deepest-last, capped at six levels — enough to tell 'Wall 3' from
    /// 'ThickDoor : (guid)' without printing an Apparance path that fills the log line.</summary>
    private static string PathOf(Transform t)
    {
        var parts = new List<string>(6);
        Transform? cur = t;
        for (int i = 0; i < 6 && cur != null; i++)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }

    /// <summary>
    /// TRUE when this shader declares a <c>Deferred</c> lighting pass and NO pass the forward path can
    /// draw — the one configuration the mod's forward head camera cannot render as authored, because
    /// Unity then falls through to the shader's Fallback. Deliberately conservative: an untagged pass
    /// counts as forward-drawable (an unlit shader's single pass usually carries no LightMode), and a
    /// shader whose tags cannot be read returns FALSE. Over-reporting this would be exactly the
    /// ModBuild 251 mistake in a new coat.
    /// </summary>
    private static bool IsDeferredOnly(Shader? sh)
    {
        if (sh == null)
            return false;
        try
        {
            bool hasDeferred = false;
            int subs = sh.subshaderCount;
            for (int s = 0; s < subs && s < 4; s++)
            {
                int pc = sh.GetPassCountInSubshader(s);
                for (int p = 0; p < pc && p < 8; p++)
                {
                    string name = sh.FindPassTagValue(s, p, LightModeTag).name;
                    if (string.IsNullOrEmpty(name))
                        return false;   // untagged ⇒ forward can draw it
                    if (name.Equals("Deferred", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDeferred = true;
                        continue;
                    }
                    if (name.Equals("ForwardBase", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("ForwardAdd", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Always", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Vertex", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("VertexLM", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("VertexLMRGBM", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return hasDeferred;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string SafeName(UnityEngine.Object o)
    {
        try { return o.name; }
        catch (Exception) { return "<unreadable>"; }
    }

    private static string BlendName(int v) => v switch
    {
        -1 => "n/a",
        0 => "Zero",
        1 => "One",
        2 => "DstColor",
        3 => "SrcColor",
        4 => "OneMinusDstColor",
        5 => "SrcAlpha",
        6 => "OneMinusSrcColor",
        7 => "DstAlpha",
        8 => "OneMinusDstAlpha",
        9 => "SrcAlphaSaturate",
        10 => "OneMinusSrcAlpha",
        _ => v.ToString()
    };
}

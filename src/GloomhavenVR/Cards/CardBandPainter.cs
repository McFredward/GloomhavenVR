using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 12 — THE PAINTER INVENTORY: name the black band's painter by EVIDENCE, not deduction.
///
/// <para>WHY THIS EXISTS. The round-11 GPU experiment (<see cref="CardShaderProbe"/>) returned
/// verdict (a) on the ModBuild-115 hardware run: the card-FX shader HONORS alpha at rest — the
/// alpha-0 half showed the magenta sentinel at <c>_Dissolve=0</c>. That verdict exonerates the
/// face Images eleven rounds edited: their punched alpha-0 pixels are genuinely invisible, so the
/// band the user still sees ("der schwarze Rand … immer noch vollständig da") has ANOTHER
/// painter — a different layer, a different material, or geometry behind the face. Every
/// inventory so far sampled IMAGES only and judged them by sprite pixels or <c>Image.color</c>;
/// none of them could see the painter classes the probe now points at.</para>
///
/// <para>THE USER'S OWN HYPOTHESIS THIS ROUND (verbatim): "Ich habe eine Vermutung: Die krtae
/// besteht aus Vorder UND Rückseite - wenn du die ganze Zeit nur die Vorderseite bearbeitest aber
/// die Rückseite der Karte nicht in dem selben outline schneidest sehe ich bei mir immer noch ein
/// Rechteck. Prüfe das." — Checked against the decompiled sources: the game's own ability-card
/// widget carries NO card-back Image (no flip animation exists — <c>FullAbilityCard</c> /
/// <c>AbilityCardUI</c>, decompiled/GH.Runtime), but the hypothesis is RIGHT in spirit twice
/// over. (1) <c>FullAbilityCard.ShowCard</c> loads the SAME background sprite into TWO Images —
/// <c>headerImage</c> AND <c>unfocusedMask</c> (FullAbilityCard.cs:442-443), a full-card dimming
/// copy drawn OVER the front at a slightly different rect (the ModBuild-115 two-placement warning:
/// y offset 0.02, height 0.97) and toggled by <c>SetUnfocused</c> (active in multiplayer on a
/// character not under the player's control — CardsHandUI.cs:1615). Left uncut it re-paints the
/// printed frame the eleven rounds erased from the header. (2) The MOD's own procedural card body
/// (<see cref="CardMesh"/>) has a genuine back face, rim and dark front behind the art — clipped
/// only while the silhouette footprint is applied to its SHARED material pair, and clipped NOT AT
/// ALL when a body rides the <see cref="CardBodyKind.Neutral"/> legacy pair.</para>
///
/// <para>WHAT THIS CLASS DOES: once per session per context (a TRAY-SLOT card and a HAND-FAN
/// card), enumerate EVERYTHING that renders under one adopted ability card root — every uGUI
/// Graphic with its material, shader, face-normalized rect, color and active/cull state
/// (inactive-but-present ones included, flagged); every MeshRenderer/renderer under the owning
/// <see cref="VRCard"/> with its materials, shaders, render queues, alpha-clip state, shared-pair
/// identity and face-space bounds — and flag which of them geometrically covers the frame band
/// while being able to paint dark opaque pixels there. For custom shaders the color inventory
/// cannot judge (the sprite-less 'UIFX_Overlay' spans 120 % of the face through its OWN material,
/// <c>CardEffects.fgFx</c> — a shader the round-11 probe never exercised), the round-12 rest
/// probe (<see cref="CardShaderProbe.DescribeRestOutput"/>) renders each one offscreen over a
/// white input and reports what it actually outputs. The one latched CARD BAND PAINTER line per
/// context names the painter(s) found — designed so the USER's next hardware log names his
/// machine's painter even where this developer rig shows none.</para>
///
/// <para>COST: a static two-flag check per frame once latched (nothing runs); the inventory
/// itself allocates once per context at latch time, like every other latched diagnostic in this
/// module. No per-frame allocation, no FindObjectsOfType — everything is reached from the card's
/// own hierarchy.</para>
/// </summary>
internal static class CardBandPainter
{
    /// <summary>Latch per context: 0 = tray slot, 1 = hand fan / free.</summary>
    private static readonly bool[] s_done = new bool[2];

    /// <summary>Shaders already rest-probed (results are per shader, not per card).</summary>
    private static readonly Dictionary<string, string> s_probeByShader = new(4);

    private static readonly List<(string Field, Image? Img)> s_namedScratch = new(8);
    private static readonly Vector3[] s_corners = new Vector3[4];

    private static bool s_errorLogged;

    /// <summary>Anything left to report? One static field read — the per-frame cost.</summary>
    internal static bool Pending => !s_done[0] || !s_done[1];

    /// <summary>Fallback band depth when no validated outline exists (the inventory's 8 %).</summary>
    private const float FallbackBand = 0.08f;

    /// <summary>Near-black luma gate, same scale as the band inventory (48/255).</summary>
    private const float DarkLuma = 48f / 255f;

    /// <summary>
    /// Report once per context when the face is ready (background art loaded). Called from
    /// <c>CardFace.Maintain</c> behind the <see cref="Pending"/> gate. Never throws.
    /// </summary>
    internal static void MaybeReport(RectTransform? faceRoot, RectTransform? host, AbilityCardUI? owner)
    {
        if (faceRoot == null || host == null || owner == null)
            return;
        try
        {
            FullAbilityCard? ability = owner.fullAbilityCard;
            if (ability == null)
                return;
            int context = ContextOf(host);
            if (s_done[context])
                return;

            // Readiness: the background art must be loaded, or every verdict below would judge
            // placeholder state. Resolved through the same named-plate identity the punch uses.
            s_namedScratch.Clear();
            CardFaceCrop.ResolveNamedPlates(ability, s_namedScratch);
            Image? header = null, mask = null;
            foreach ((string field, Image? img) in s_namedScratch)
            {
                if (field == "_headerImage") header = img;
                else if (field == "unfocusedMask") mask = img;
            }
            if (header == null || header.sprite == null)
                return; // art not arrived — retry on a later Maintain tick

            VRCard? card = host.GetComponentInParent<VRCard>();
            if (card == null)
                return;

            s_done[context] = true; // latch first — a throwing inventory must not retry forever
            Report(context == 0 ? "tray slot" : "hand fan", faceRoot, card, ability, header, mask);
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("Cards", $"CARD BAND PAINTER inventory failed ({ex.GetType().Name}: " +
                                    $"{ex.Message}) — the painter must be read from the other " +
                                    "diagnostics this round.");
            }
        }
    }

    /// <summary>0 = tray slot (an ancestor named Slot1/Slot2 — both the procedural and the
    /// bundled board name their slot anchors exactly that), 1 = anything else (hand fan, held,
    /// free float).</summary>
    private static int ContextOf(Transform host)
    {
        for (Transform? t = host; t != null; t = t.parent)
        {
            if (t.name == "Slot1" || t.name == "Slot2")
                return 0;
        }
        return 1;
    }

    private static void Report(string context, RectTransform faceRoot, VRCard card,
                               FullAbilityCard ability, Image header, Image? mask)
    {
        Rect faceRect = faceRoot.rect;
        if (faceRect.width < 1f || faceRect.height < 1f)
            return;
        CardOutline? outline = CardOutline.ForKind(CardBodyKind.Ability);
        float bandL = outline != null ? Mathf.Max(outline.BandLeft, 0.02f) : FallbackBand;
        float bandR = outline != null ? Mathf.Max(outline.BandRight, 0.02f) : FallbackBand;
        float bandB = outline != null ? Mathf.Max(outline.BandBottom, 0.02f) : FallbackBand;
        float bandT = outline != null ? Mathf.Max(outline.BandTop, 0.02f) : FallbackBand;

        var sb = new StringBuilder(1024);
        var painters = new List<string>(4);

        // ---- (1) the back-side answer, by name -----------------------------------------
        sb.Append("BACK-SIDE CHECK (user: 'Vorder UND Rückseite'): the game widget has NO ")
          .Append("card-back Image (decompiled FullAbilityCard/AbilityCardUI — no flip exists); ")
          .Append("its second full-card layer is 'unfocusedMask' (ShowCard loads the SAME ")
          .Append("background sprite into headerImage AND unfocusedMask): ");
        if (mask == null)
        {
            sb.Append("UNRESOLVED on this build. ");
        }
        else
        {
            bool maskActive = mask.gameObject.activeInHierarchy && mask.enabled;
            Sprite? maskSprite = mask.sprite;
            bool maskPunched = maskSprite != null && CardFaceMipBake.OriginalOf(maskSprite) != null;
            Rect maskRect = CardFace.FaceNormRectOf(mask.rectTransform, mask.rectTransform.rect,
                                                    faceRoot, faceRect, s_corners);
            sb.Append('\'').Append(mask.name).Append("' ")
              .Append(maskActive ? "ACTIVE" : "inactive (SetUnfocused(false) — paints nothing now)")
              .Append(", sprite ").Append(maskSprite != null ? $"'{maskSprite.name}'" : "none")
              .Append(maskSprite == null ? string.Empty
                  : maskPunched ? " (a mod copy — punched/cropped)" : " (the ORIGINAL — frame intact)")
              .Append($", rect x {maskRect.xMin:F3}..{maskRect.xMax:F3} y {maskRect.yMin:F3}..{maskRect.yMax:F3}")
              .Append(" of the face. ");
            if (maskActive && maskSprite != null && !maskPunched)
                painters.Add($"'{mask.name}' (the widget's own second background copy, drawn with the " +
                             "ORIGINAL sprite — printed frame and all)");
        }

        // ---- (2) canvas graphics that reach the frame band ------------------------------
        Graphic[] graphics = faceRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        int silent = 0;
        var probeWanted = new List<(string Shader, Material Mat)>(3);
        sb.Append("CANVAS GRAPHICS reaching the band (of ").Append(graphics.Length)
          .Append(" total; canvas draws at z -0.0012 IN FRONT of the body, in listed = paint order): ");
        int listed = 0;
        foreach (Graphic g in graphics)
        {
            if (g == null || g.rectTransform == null)
                continue;
            Rect norm = CardFace.FaceNormRectOf(g.rectTransform, g.rectTransform.rect,
                                                faceRoot, faceRect, s_corners);
            bool reachesBand = norm.xMin < bandL || norm.xMax > 1f - bandR
                            || norm.yMin < bandB || norm.yMax > 1f - bandT;
            bool active = g.gameObject.activeInHierarchy && g.enabled;
            if (!reachesBand || (!active && !(g is Image)))
            {
                silent++;
                continue;
            }
            var img = g as Image;
            Sprite? sprite = img != null ? img.sprite : null;
            if (!active && sprite == null)
            {
                silent++; // an inactive sprite-less quad renders nothing and never will as-is
                continue;
            }
            Material? mat = g.material;
            string shaderName = mat != null && mat.shader != null ? mat.shader.name : "<none>";
            Color col = g.color;
            float luma = col.r * 0.299f + col.g * 0.587f + col.b * 0.114f;
            bool culled = g.canvasRenderer != null && g.canvasRenderer.cull;
            if (listed++ > 0)
                sb.Append("; ");
            sb.Append('\'').Append(g.name).Append('\'');
            if (sprite != null)
                sb.Append("/'").Append(sprite.name).Append('\'');
            else if (img != null)
                sb.Append(" [no sprite → uGUI WHITE texture]");
            else
                sb.Append(" [").Append(g.GetType().Name).Append(']');
            sb.Append(" shader '").Append(shaderName).Append('\'')
              .Append($" rgba({col.r:F2},{col.g:F2},{col.b:F2},{col.a:F2}) luma {luma:F2}")
              .Append($" rect x {norm.xMin:F2}..{norm.xMax:F2} y {norm.yMin:F2}..{norm.yMax:F2}");
            if (!active)
                sb.Append(" [INACTIVE now]");
            if (culled)
                sb.Append(" [culled]");
            bool colorDark = col.a >= 0.5f && luma <= DarkLuma;
            if (active && !culled && colorDark)
            {
                sb.Append(" [DARK BY COLOR — paints the band]");
                painters.Add($"'{g.name}' (dark color-only graphic over the band)");
            }
            // Custom shaders get the rest probe — the color inventory cannot judge them.
            if (active && mat != null && mat.shader != null
                && shaderName != "UI/Default" && !shaderName.StartsWith("TextMeshPro")
                && !s_probeByShader.ContainsKey(shaderName) && probeWanted.Count < 3)
            {
                probeWanted.Add((shaderName, mat));
                s_probeByShader[shaderName] = "pending";
            }
        }
        if (listed == 0)
            sb.Append("NONE");
        sb.Append(". ").Append(silent).Append(" graphic(s) never reach the band or render nothing. ");

        // ---- (3) renderers under the VRCard root (the body BEHIND the face) --------------
        Renderer[] renderers = card.GetComponentsInChildren<Renderer>(includeInactive: true);
        sb.Append("RENDERERS under the card root (behind/around the face, face-local z in canvas ")
          .Append("units, +z = behind): ");
        int rlisted = 0;
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;
            Bounds b = r.bounds;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = faceRoot.InverseTransformPoint(corner);
                float nx = (local.x - faceRect.xMin) / faceRect.width;
                float ny = (local.y - faceRect.yMin) / faceRect.height;
                if (nx < minX) minX = nx;
                if (nx > maxX) maxX = nx;
                if (ny < minY) minY = ny;
                if (ny > maxY) maxY = ny;
                if (local.z < minZ) minZ = local.z;
                if (local.z > maxZ) maxZ = local.z;
            }
            bool coversBand = minX < bandL || maxX > 1f - bandR || minY < bandB || maxY > 1f - bandT;
            bool active = r.enabled && r.gameObject.activeInHierarchy;
            if (rlisted++ > 0)
                sb.Append("; ");
            sb.Append('\'').Append(r.name).Append("' ")
              .Append(active ? "on" : "OFF")
              .Append($" x {minX:F2}..{maxX:F2} y {minY:F2}..{maxY:F2} z {minZ:F1}..{maxZ:F1}");
            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material? mat = mats[m];
                if (mat == null)
                {
                    sb.Append(" [slot ").Append(m).Append(": null]");
                    continue;
                }
                string identity = IdentityOf(mat);
                bool clipped = mat.IsKeywordEnabled("_ALPHATEST_ON");
                float mluma = 1f;
                if (mat.HasProperty("_Color"))
                {
                    Color mc = mat.color;
                    mluma = mc.r * 0.299f + mc.g * 0.587f + mc.b * 0.114f;
                }
                sb.Append(" [").Append(identity)
                  .Append(" '").Append(mat.shader != null ? mat.shader.name : "<none>").Append('\'')
                  .Append(" q").Append(mat.renderQueue)
                  .Append(clipped ? " CUTOUT-clipped" : " NOT clipped")
                  .Append($" luma {mluma:F2}]");
                if (active && coversBand && !clipped && mluma <= 0.25f)
                {
                    sb.Append(" [DARK UNCLIPPED BODY over the band]");
                    painters.Add($"renderer '{r.name}' slot {m} ({identity}, dark, NOT alpha-clipped)");
                }
            }
        }
        if (rlisted == 0)
            sb.Append("NONE");
        sb.Append(". Silhouette clip applied this session: Ability=")
          .Append(CardMesh.SilhouetteApplied(CardBodyKind.Ability))
          .Append(", Item=").Append(CardMesh.SilhouetteApplied(CardBodyKind.Item)).Append(". ");

        // ---- (4) rest-output probes for the custom shaders -------------------------------
        foreach ((string shaderName, Material mat) in probeWanted)
        {
            string verdict = CardShaderProbe.DescribeRestOutput(mat);
            s_probeByShader[shaderName] = verdict;
            sb.Append("REST PROBE shader '").Append(shaderName).Append("': ").Append(verdict).Append(". ");
            if (verdict.Contains("DARK OPAQUE"))
                painters.Add($"every graphic on shader '{shaderName}' (paints dark on a white input)");
        }

        // ---- (5) the verdict --------------------------------------------------------------
        string bandNote = outline != null
            ? $"band = outside the derived outline (bands {outline.BandSummary})"
            : $"band = the outer {FallbackBand:P0} (no validated outline)";
        string verdictLine = painters.Count > 0
            ? "PAINTER(S) FOUND ON THIS RIG: " + string.Join(", ", painters) + " — cut/route these first"
            : "no active dark band painter in THIS state on THIS rig — if the user's log still shows " +
              "the band, HIS line's inventory names what differs (an ACTIVE unfocusedMask, an OFF " +
              "silhouette clip, a dark-probing shader)";
        var chain = new StringBuilder(64);
        int depth = 0;
        for (Transform? t = card.transform.parent; t != null && depth < 4; t = t.parent, depth++)
        {
            if (chain.Length > 0)
                chain.Append(" < ");
            chain.Append(t.name);
        }
        VRLog.Info("Cards", $"CARD BAND PAINTER ({context}): full render inventory of one adopted " +
                            $"ability card ('{card.name}' under {chain}, face " +
                            $"{faceRect.width:F0}x{faceRect.height:F0} px, {bandNote}). " +
                            $"{sb}VERDICT: {verdictLine}.");
    }

    /// <summary>Which material asset is this — one of the shared card-body pairs (and which), or
    /// something else? Reference identity against the cached shared materials (creation is cached,
    /// so probing does not build anything twice).</summary>
    private static string IdentityOf(Material mat)
    {
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Ability)))
            return "SHARED Ability edge/rim";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Ability)))
            return "SHARED Ability back";
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Item)))
            return "SHARED Item edge/rim";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Item)))
            return "SHARED Item back";
        if (ReferenceEquals(mat, CardMesh.CreateEdgeMaterial(CardBodyKind.Neutral)))
            return "SHARED NEUTRAL edge (NEVER clipped by design — CardBodyKind)";
        if (ReferenceEquals(mat, CardMesh.CreateBackMaterial(CardBodyKind.Neutral)))
            return "SHARED NEUTRAL back (NEVER clipped by design — CardBodyKind)";
        return $"'{mat.name}'";
    }
}

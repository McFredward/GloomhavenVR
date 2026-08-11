using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 10 — THE GEOMETRIC RECT CROP: the band-painting face layers stop RASTERIZING the frame
/// band, instead of merely painting it transparent.
///
/// <para>WHY ALPHA WAS NOT ENOUGH (the ModBuild-113 evidence). The geometric punch verifiably
/// erased every outside-outline pixel of the background to alpha 0 — its own log line carries the
/// count (45145 px = 1.88 %) — and the user reported an IDENTICAL band, including at the top,
/// where ONLY the punched background paints (the action halves sit lower). The one explanation
/// class left is that erasing to transparent black does not change what his renderer draws there.
/// The prime suspect is in <c>decompiled/GH.Runtime/CardEffects.cs</c>: <c>Awake</c> assigns
/// every card-face Image a CUSTOM material (<c>image2.material = new Material(image2.material)</c>)
/// and animates <c>_Dissolve</c>/<c>_Burn</c>/<c>_GreyOut</c>/<c>_PosAndBounds</c> on it. That
/// shader is an asset we cannot read offline. If it outputs opaque (e.g. a dissolve-clip shader
/// whose clip term ignores texture alpha at <c>_Dissolve</c> = 0), then alpha-0 pixels whose RGB
/// is black render... black. Identical. That reading would also retroactively explain why NINE
/// texture-side rounds produced literally zero visible change while the band visibly vanishes
/// during the game's own dissolve ANIMATION (the clip path finally runs) — which the user reports
/// every round. The punch sweep's <c>CARD SHADER IDENTITY</c> line settles the theory factually;
/// THIS class ships the fix that is correct under every answer.</para>
///
/// <para>THE MECHANISM. For each band-painting layer — resolved BY IDENTITY from the game's own
/// components, never by heuristic (see <see cref="ResolveNamedPlates"/>) — the punched pixel
/// slice is CROPPED to the card outline's extent bounding box
/// (<c>CardFaceMipBake.OutlineCroppedReplacementFor</c>, <c>CardOutline.ExtentFace</c>) and the
/// layer's RectTransform is shrunk/repositioned so its drawn rect equals EXACTLY the face rect
/// the kept pixels cover. Uniform layers render pixel-identically (a uniform map restricted to a
/// sub-block IS the sub-block's uniform map); sliced layers keep every kept corner pixel in place
/// because the sprite borders shrink by exactly the crop. Then no rasterized geometry exists in
/// the frame band and the shader cannot matter.</para>
///
/// <para>SCOPE — the locally adopted ABILITY face only, on purpose. The crop edits live game
/// RectTransforms, so it needs (a) a per-frame maintain seam (the game rewrites face state on
/// every hand refresh) and (b) a guaranteed restore. Only <see cref="CardFace"/> has both; it
/// drives <see cref="Maintain"/> from its own per-frame pass and its art-arrival LateUpdate seam,
/// and calls <see cref="Restore"/> from every release path (Restore, Yield, teardown). Peer
/// clones and item faces keep the alpha punch — exactly ModBuild 113, never worse — because no
/// per-frame seam owns their rects; if the shader-identity line proves alpha is mishandled, THAT
/// is the follow-up for those surfaces.</para>
///
/// <para>PER-LAYER SAFETY GATES, each a stated fallback to the alpha punch (today's look):
/// a layer rotated relative to the face (rect math would shear), a layer under an enabled
/// LayoutGroup or wearing a ContentSizeFitter (a rect edit would be fought every layout pass —
/// detected up front), an unmappable Image type, a crop the bake refuses (its line names the
/// numbers), and a LIVE fight (the game rewriting the rect every frame for
/// <see cref="FightFrameLimit"/> consecutive frames — detected, restored, logged, disabled).
/// A hovered plate hands its rect back for the duration of the game's
/// <c>Image.overrideSprite</c> state and re-crops when the hover clears — the hover state sprite
/// was never punched in any round (pre-existing residual, STATE §5e), so the game's own geometry
/// is the only correct rendering of it.</para>
///
/// <para>RESTORE CONTRACT. Every entry records the exact original <c>sizeDelta</c>,
/// <c>localPosition</c> and <c>preserveAspect</c> before the first edit and hands precisely those
/// back; the worn sprite goes back to the exact ORIGINAL game sprite (the crop sprite also lives
/// in the shared restore map, so <c>CardFaceMipBake.RestoreSprites</c> covers any walk that gets
/// there first). The crop sprite is NEVER registered as a generic replacement — no Rescan, no
/// arrival watch, no peer clone can ever serve it without the rect that belongs to it.</para>
/// </summary>
internal sealed class CardFaceCrop
{
    // ----------------------------------------------------------- named identity (static) --

    // CardEffects' serialized Image fields — the exact objects the game's card-FX material rides
    // on, and (per the ModBuild-113 band inventory) the exact three that paint the band:
    // 'Header' (_headerImage), 'Default action top' (_topDefAction), 'Default action button'
    // (_botDefAction). The two big action plates (_topButton/_bottomAction) are included because
    // they are face layers by construction, whatever they contribute on a given class.
    private static readonly FieldInfo? s_feHeader = AccessTools.Field(typeof(CardEffects), "_headerImage");
    private static readonly FieldInfo? s_feTopButton = AccessTools.Field(typeof(CardEffects), "_topButton");
    private static readonly FieldInfo? s_feBottomAction = AccessTools.Field(typeof(CardEffects), "_bottomAction");
    private static readonly FieldInfo? s_feTopDefAction = AccessTools.Field(typeof(CardEffects), "_topDefAction");
    private static readonly FieldInfo? s_feBotDefAction = AccessTools.Field(typeof(CardEffects), "_botDefAction");

    /// <summary>Fallback identity for the plates when a CardEffects field is null on this build:
    /// <c>FullAbilityCardAction.actionButton</c> is public, <c>defaultActionButton</c> is not.</summary>
    private static readonly FieldInfo? s_feDefaultActionButton =
        AccessTools.Field(typeof(FullAbilityCardAction), "defaultActionButton");

    /// <summary>
    /// Resolve the named plate Images of one <paramref name="ability"/> face. Appends one entry
    /// per field, null Image included — the punch sweep's named-identity log states which fields
    /// resolved and which did not, so a game update renaming a field is one log line, not a
    /// silent regression. Never throws.
    /// </summary>
    internal static void ResolveNamedPlates(FullAbilityCard ability, List<(string Field, Image? Img)> into)
    {
        try
        {
            CardEffects? fx = ability.cardEffects;
            Image? Get(FieldInfo? f) => fx != null && f != null ? f.GetValue(fx) as Image : null;
            Image? header = Get(s_feHeader);
            Image? topButton = Get(s_feTopButton);
            Image? bottomAction = Get(s_feBottomAction);
            Image? topDef = Get(s_feTopDefAction);
            Image? botDef = Get(s_feBotDefAction);

            FullAbilityCardAction? topHalf = ability.topActionButton;
            FullAbilityCardAction? bottomHalf = ability.bottomActionButton;
            if (topButton == null && topHalf != null && topHalf.actionButton != null)
                topButton = topHalf.actionButton.image;
            if (bottomAction == null && bottomHalf != null && bottomHalf.actionButton != null)
                bottomAction = bottomHalf.actionButton.image;
            Button? DefaultButtonOf(FullAbilityCardAction? half) =>
                half != null && s_feDefaultActionButton != null
                    ? s_feDefaultActionButton.GetValue(half) as Button
                    : null;
            if (topDef == null)
            {
                Button? b = DefaultButtonOf(topHalf);
                topDef = b != null ? b.image : null;
            }
            if (botDef == null)
            {
                Button? b = DefaultButtonOf(bottomHalf);
                botDef = b != null ? b.image : null;
            }

            into.Add(("_headerImage", header));
            into.Add(("_topButton", topButton));
            into.Add(("_bottomAction", bottomAction));
            into.Add(("_topDefAction", topDef));
            into.Add(("_botDefAction", botDef));
        }
        catch (System.Exception ex)
        {
            if (s_latchLogged.Add("resolve|error"))
                VRLog.Warn("Cards", $"CARD FRAME CROP: named-plate resolution failed ({ex.GetType().Name}: " +
                                    $"{ex.Message}) — plates ride the span gate only this session.");
        }
    }

    // ---------------------------------------------------------------- instance state --

    /// <summary>One cropped layer under maintenance.</summary>
    private sealed class Entry
    {
        internal Image Img = null!;
        internal string Field = string.Empty;
        internal Sprite CropSprite = null!;
        internal Sprite GameSource = null!; // the exact ORIGINAL game sprite (restore target)
        internal Vector2 OrigSizeDelta;
        internal Vector3 OrigLocalPos;
        internal bool OrigPreserveAspect;
        internal Vector2 TargetSize;
        internal Vector3 TargetLocalPos;
        internal bool Applied;
        internal int FightFrames;
        internal bool Disabled;
    }

    /// <summary>Consecutive frames the game may rewrite a cropped rect before the crop concedes
    /// a layout fight and falls back to the alpha punch for that layer. Sporadic state-change
    /// rewrites reset the counter; only a genuine every-frame fight reaches it (~1.3 s at 90 Hz).</summary>
    private const int FightFrameLimit = 120;

    /// <summary>Seconds between attempts to BUILD entries for layers not yet cropped (art loads
    /// async). Maintaining existing entries runs every frame regardless.</summary>
    private const float BuildInterval = 0.5f;

    /// <summary>Latched one-time log lines (per field+reason / per field application).</summary>
    private static readonly HashSet<string> s_latchLogged = new(8);

    private static readonly List<(string Field, Image? Img)> s_namedScratch = new(6);
    private static readonly Vector3[] s_corners = new Vector3[4];

    private readonly List<Entry> _entries = new(6);

    /// <summary>image id → source-sprite id a refusal was latched against; retried only when the
    /// layer's art actually changes.</summary>
    private readonly Dictionary<int, int> _settled = new(8);

    private RectTransform? _faceRoot;
    private float _nextBuild;
    private bool _errorLogged;

    // -------------------------------------------------------------------- maintain --

    /// <summary>
    /// Per-frame seam (driven by <c>CardFace.Maintain</c> and the art-arrival LateUpdate):
    /// re-assert every cropped sprite+rect pair over whatever the game rewrote, park while a
    /// hover overrideSprite owns a plate, and — rate-limited — build entries for layers whose
    /// art has arrived since. Cheap in the steady state. Never throws.
    /// </summary>
    internal void Maintain(FullAbilityCard? ability, RectTransform? faceRoot)
    {
        if (ability == null || faceRoot == null)
            return;
        if (CardsConfig.FaceMipBake == null || !CardsConfig.FaceMipBake.Value)
        {
            // The crop lives on the punched pixel copies; no bake, no crop — and if the dial was
            // turned off mid-session, hand back anything still cropped instead of abandoning it.
            if (_entries.Count > 0)
                Restore();
            return;
        }
        try
        {
            if (!ReferenceEquals(_faceRoot, faceRoot))
            {
                // A different face adopted into this slot — never carry rect bookkeeping across.
                RestoreEntries();
                _faceRoot = faceRoot;
                _settled.Clear();
                _nextBuild = 0f;
            }
            MaintainEntries();
            if (Time.unscaledTime >= _nextBuild)
            {
                _nextBuild = Time.unscaledTime + BuildInterval;
                BuildEntries(ability, faceRoot);
            }
        }
        catch (System.Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                VRLog.Warn("Cards", $"CARD FRAME CROP maintain failed ({ex.GetType().Name}: {ex.Message}) — " +
                                    "this face keeps the alpha punch (ModBuild-113 look).");
            }
        }
    }

    /// <summary>Hand every cropped layer back exactly as found — rect, preserveAspect and the
    /// original game sprite — and drop all bookkeeping. Unity-null guarded; idempotent.</summary>
    internal void Restore()
    {
        try
        {
            RestoreEntries();
        }
        catch (System.Exception)
        {
            _entries.Clear(); // teardown race — nothing left alive to restore
        }
        _settled.Clear();
        _faceRoot = null;
    }

    private void RestoreEntries()
    {
        foreach (Entry e in _entries)
        {
            Image img = e.Img;
            if (img == null)
                continue;
            if (ReferenceEquals(img.sprite, e.CropSprite))
                img.sprite = e.GameSource;
            if (e.Applied)
                RestoreRect(e);
        }
        _entries.Clear();
    }

    private static void RestoreRect(Entry e)
    {
        RectTransform irt = e.Img.rectTransform;
        irt.sizeDelta = e.OrigSizeDelta;
        irt.localPosition = e.OrigLocalPos;
        e.Img.preserveAspect = e.OrigPreserveAspect;
        e.Applied = false;
    }

    private static void Apply(Entry e)
    {
        Image img = e.Img;
        if (!ReferenceEquals(img.sprite, e.CropSprite))
            img.sprite = e.CropSprite;
        if (img.preserveAspect)
            img.preserveAspect = false; // the crop rect IS the drawn rect; letterboxing it again would double-inset
        RectTransform irt = img.rectTransform;
        irt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, e.TargetSize.x);
        irt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, e.TargetSize.y);
        irt.localPosition = e.TargetLocalPos;
        e.Applied = true;
    }

    private void MaintainEntries()
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            Image img = e.Img;
            if (img == null)
            {
                _entries.RemoveAt(i); // died with the hierarchy — nothing to restore
                continue;
            }
            if (e.Disabled)
                continue;

            // HOVER PARK: the game's SpriteSwap transition sets overrideSprite, which uGUI
            // renders INSTEAD of img.sprite. That state sprite was never punched in any round
            // (pre-existing hover residual) — the only correct rendering of it is the game's own
            // geometry, so the rect steps aside for the duration and returns when hover clears.
            Sprite? over = img.overrideSprite;
            if (over != null && !ReferenceEquals(over, img.sprite))
            {
                if (e.Applied)
                {
                    if (ReferenceEquals(img.sprite, e.CropSprite))
                        img.sprite = e.GameSource;
                    RestoreRect(e);
                }
                continue;
            }

            Sprite? worn = img.sprite;
            if (!ReferenceEquals(worn, e.CropSprite))
            {
                Sprite? source = worn != null ? (CardFaceMipBake.OriginalOf(worn) ?? worn) : null;
                if (source == null || !ReferenceEquals(source, e.GameSource))
                {
                    // Genuinely NEW art (character switch / loader churn onto different content):
                    // this entry's crop no longer describes what is drawn. Hand the rect back and
                    // let the build pass mint a crop for the new art.
                    if (e.Applied)
                        RestoreRect(e);
                    _entries.RemoveAt(i);
                    continue;
                }
                // Same art re-assigned (the loader re-served it, or a sweep re-pointed it at the
                // full punched copy before this pass) — the crop pair goes back on together below.
            }

            RectTransform irt = img.rectTransform;
            Vector2 size = irt.rect.size;
            bool drifted = !e.Applied
                || Mathf.Abs(size.x - e.TargetSize.x) > 0.5f
                || Mathf.Abs(size.y - e.TargetSize.y) > 0.5f
                || (irt.localPosition - e.TargetLocalPos).sqrMagnitude > 0.25f
                || !ReferenceEquals(img.sprite, e.CropSprite)
                || img.preserveAspect;
            if (!drifted)
            {
                e.FightFrames = 0;
                continue;
            }
            if (e.Applied)
            {
                // It WAS ours last frame and something rewrote it — count consecutive fights.
                e.FightFrames++;
                if (e.FightFrames >= FightFrameLimit)
                {
                    if (ReferenceEquals(img.sprite, e.CropSprite))
                        img.sprite = e.GameSource;
                    RestoreRect(e);
                    e.Disabled = true;
                    _settled[img.GetInstanceID()] = e.GameSource.GetInstanceID();
                    VRLog.Warn("Cards", $"CARD FRAME CROP disabled for '{e.Field}' ('{img.name}'): the game " +
                                        $"rewrote its rect {FightFrameLimit} consecutive frames — a live " +
                                        "layout fight, not state-change churn. This layer falls back to the " +
                                        "alpha punch (ModBuild-113 look) and its exact original rect is " +
                                        "restored; if its band survives, this line is the reason.");
                    continue;
                }
            }
            Apply(e);
        }
    }

    // ---------------------------------------------------------------------- build --

    private void BuildEntries(FullAbilityCard ability, RectTransform faceRoot)
    {
        Rect faceRect = faceRoot.rect;
        if (faceRect.width < 1f || faceRect.height < 1f)
            return;

        s_namedScratch.Clear();
        ResolveNamedPlates(ability, s_namedScratch);

        // THE OUTLINE MUST BE THIS FACE'S OWN — resolved through the named background exactly
        // like the punch sweep resolves it, never through the per-kind slot: in a multi-class
        // party that slot holds whichever face was swept last, and cropping class B against
        // class A's outline would be a wrong shape (forbidden). ForSource is cache-first (keyed
        // on content), so this is a dictionary probe in the steady state; a crop-maintained
        // background resolves through OriginalOf to the same content.
        CardOutline? outline = null;
        foreach ((string field, Image? img) in s_namedScratch)
        {
            if (field != "_headerImage" || img == null || img.sprite == null)
                continue;
            if (CardFace.TryBuildPunchMapping(img, img.sprite, faceRoot, faceRect, s_corners,
                    out CardFaceMipBake.PunchMapping headerMap, out bool uniformSimple)
                && uniformSimple)
            {
                Sprite headerSource = CardFaceMipBake.OriginalOf(img.sprite) ?? img.sprite;
                outline = CardOutline.ForSource(CardBodyKind.Ability, headerSource, headerMap.FaceRect);
            }
            break;
        }
        if (outline == null)
            return; // background not resolvable/validated yet — the CARD OUTLINE lines carry why

        foreach ((string field, Image? img) in s_namedScratch)
        {
            if (img == null || !img.enabled || !img.gameObject.activeSelf)
                continue;
            int imgId = img.GetInstanceID();
            if (HasEntry(imgId))
                continue;
            Sprite? worn = img.sprite;
            if (worn == null || CardFaceMipBake.IsCropSprite(worn))
                continue;
            Sprite source = CardFaceMipBake.OriginalOf(worn) ?? worn;
            int sourceId = source.GetInstanceID();
            if (_settled.TryGetValue(imgId, out int settledSource) && settledSource == sourceId)
                continue; // latched refusal for exactly this art — retry only on new art

            if (Quaternion.Angle(img.rectTransform.rotation, faceRoot.rotation) > 0.5f)
            {
                Latch(imgId, sourceId, field, img.name,
                      "the layer is rotated relative to the face — axis-aligned rect math would shear it");
                continue;
            }
            if (IsLayoutDriven(img))
            {
                Latch(imgId, sourceId, field, img.name,
                      "a LayoutGroup/ContentSizeFitter drives this rect — an edit would be fought " +
                      "every layout pass");
                continue;
            }
            if (!CardFace.TryBuildPunchMapping(img, worn, faceRoot, faceRect, s_corners,
                    out CardFaceMipBake.PunchMapping map, out _))
            {
                Latch(imgId, sourceId, field, img.name, $"unmappable Image.Type {img.type}");
                continue;
            }

            Sprite? crop = CardFaceMipBake.OutlineCroppedReplacementFor(source, map, outline,
                out Rect targetFace, out string? refusal);
            if (crop == null)
            {
                // "Clean" (draws nothing outside the extent bbox) is the good refusal: the alpha
                // punch plus today's rect already never rasterize the band for this layer.
                Latch(imgId, sourceId, field, img.name, refusal ?? "crop refused");
                continue;
            }

            // Rect math: the face-normalized target rect → this image's own local space (own
            // rect units), assuming the axis alignment verified above. rect.min in own local is
            // always -pivot*size, so the residual translation goes through localPosition, scaled
            // into parent units by the image's own localScale.
            RectTransform irt = img.rectTransform;
            Vector3 wMin = faceRoot.TransformPoint(new Vector3(
                faceRect.xMin + targetFace.xMin * faceRect.width,
                faceRect.yMin + targetFace.yMin * faceRect.height, 0f));
            Vector3 wMax = faceRoot.TransformPoint(new Vector3(
                faceRect.xMin + targetFace.xMax * faceRect.width,
                faceRect.yMin + targetFace.yMax * faceRect.height, 0f));
            Vector3 lMin = irt.InverseTransformPoint(wMin);
            Vector3 lMax = irt.InverseTransformPoint(wMax);
            var size = new Vector2(lMax.x - lMin.x, lMax.y - lMin.y);
            if (float.IsNaN(size.x) || float.IsInfinity(size.x)
                || float.IsNaN(size.y) || float.IsInfinity(size.y)
                || size.x <= 1f || size.y <= 1f || size.x > 16384f || size.y > 16384f)
            {
                // Not latched permanently against this art on purpose when the transform is
                // degenerate mid-animation — but Latch keys on (image, art), so a later re-adopt
                // or art change retries. A card scaled to zero this frame simply skips.
                Latch(imgId, sourceId, field, img.name,
                      $"degenerate target rect {size.x:F1}x{size.y:F1} px in layer space " +
                      "(zero/deforming scale at build time)");
                continue;
            }
            Vector2 pivot = irt.pivot;
            var newRectMin = new Vector2(-pivot.x * size.x, -pivot.y * size.y);
            Vector3 ls = irt.localScale;
            Vector3 targetLocalPos = irt.localPosition + new Vector3(
                (lMin.x - newRectMin.x) * ls.x, (lMin.y - newRectMin.y) * ls.y, 0f);

            var e = new Entry
            {
                Img = img,
                Field = field,
                CropSprite = crop,
                GameSource = source,
                OrigSizeDelta = irt.sizeDelta,
                OrigLocalPos = irt.localPosition,
                OrigPreserveAspect = img.preserveAspect,
                TargetSize = size,
                TargetLocalPos = targetLocalPos,
            };
            _entries.Add(e);
            Apply(e);
            if (s_latchLogged.Add($"applied|{field}"))
            {
                Rect old = irt.rect; // post-apply: equals the target by construction
                VRLog.Info("Cards", $"CARD FRAME CROP (Ability): '{field}' ('{img.name}') now draws " +
                                    $"'{crop.name}' in x {targetFace.xMin:F3}..{targetFace.xMax:F3}, " +
                                    $"y {targetFace.yMin:F3}..{targetFace.yMax:F3} of the face " +
                                    $"(rect {e.OrigSizeDelta.x:F0}x{e.OrigSizeDelta.y:F0} sizeDelta → " +
                                    $"{old.width:F0}x{old.height:F0} px). The frame band under this layer " +
                                    "is now GEOMETRICALLY unpainted — no rasterized pixel exists there, so " +
                                    "the card shader's alpha semantics cannot matter. Maintained every " +
                                    "frame from CardFace.Maintain; the exact original sizeDelta/" +
                                    "localPosition/preserveAspect and the original game sprite return on " +
                                    "release. Every card of this class shares this one crop bake.");
            }
        }
    }

    private bool HasEntry(int imgId)
    {
        foreach (Entry e in _entries)
        {
            if (e.Img != null && e.Img.GetInstanceID() == imgId)
                return true;
        }
        return false;
    }

    /// <summary>Is this rect owned by the layout system? (An edit would be re-written every
    /// canvas rebuild — a per-frame fight the crop refuses up front instead of losing slowly.)</summary>
    private static bool IsLayoutDriven(Image img)
    {
        if (img.GetComponent<ContentSizeFitter>() != null)
            return true;
        Transform? parent = img.transform.parent;
        if (parent == null)
            return false;
        LayoutGroup? group = parent.GetComponent<LayoutGroup>();
        if (group == null || !group.enabled)
            return false;
        LayoutElement? element = img.GetComponent<LayoutElement>();
        return element == null || !element.ignoreLayout;
    }

    /// <summary>Latch a per-layer fallback (this face, this art) and say so once per
    /// field+reason: the layer keeps the alpha punch, which is exactly the ModBuild-113 look.</summary>
    private void Latch(int imgId, int sourceId, string field, string imgName, string reason)
    {
        _settled[imgId] = sourceId;
        if (s_latchLogged.Add($"{field}|{reason}"))
        {
            VRLog.Info("Cards", $"CARD FRAME CROP fallback for '{field}' ('{imgName}'): {reason}. This " +
                                "layer stays ALPHA-PUNCH only (ModBuild-113 behaviour); if the frame band " +
                                "survives under it while the cropped layers are clean, the shader-identity " +
                                "line names why alpha alone was not enough.");
        }
    }
}

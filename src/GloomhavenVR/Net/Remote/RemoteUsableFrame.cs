using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// THE PEER HALF OF THE ITEM FAN'S "you can play this NOW" FRAME — the receiver of extension record
/// <see cref="NetProtocol.ExtIdItemUsable"/>.
///
/// <para>WHAT IT CLOSES (2026-09-02 multiplayer report, item 5, verbatim): "Die Gegenstände, die
/// benutzbar sind, haben eine highlighting Animation, diese ist aber nicht beim remote board beim
/// Mitspieler sichtbar bei deren Gegenstandsfächer (verletzt 1:1 Regel)." The owner's chips wear a
/// soft gold breathing outline (<c>ItemsPile.ItemChip.BuildUsableFrame</c>); the mirrored arc wore
/// nothing at all, because the carrier is 100 % mod-owned and lives only on the owner's own chips.
/// The CLOSED stack's cue was already mirrored — one boolean, board-UI byte 2 bit 7 — but that says
/// nothing about WHICH card, and the fan is where the user was looking.</para>
///
/// <para>WHY THE ANSWER TRAVELS INSTEAD OF BEING RE-DERIVED HERE: both arms of the owner's
/// predicate end in the VIEWER's own state — <c>CardsGameApi.IsActionTurn</c> finishes on
/// <c>cur.IsUnderMyControl</c>, and the active-bonus arm reads a LOCAL UI singleton — so a
/// re-derivation is identically 0 on exactly the board that needs it. The full argument is on
/// <see cref="NetProtocol.ExtIdItemUsable"/>; the short version is that re-deriving would AND the
/// owner's state with the viewer's copy of the same key, which is the mirrored card-dust defect.
/// </para>
///
/// <para>IT IS THE SAME MACHINERY, NOT A LOOK-ALIKE: the sprite is
/// <see cref="WorldUI.SoftCueArt.FrameSprite"/> and the motion is
/// <see cref="WorldUI.SoftFramePulse"/>, the two pieces the owner's own chip frame is built from,
/// driven off <c>Time.unscaledTime</c>. The pixel constants below are the owner's, restated with
/// their source named, and they are pinned against <c>Cards/Piles/ItemsPile.cs</c> by
/// <c>ItemUsableVectors</c> in the wire tests so the two frames cannot drift apart unnoticed.</para>
///
/// <para>THE PERIOD IS THE BOARD OWNER'S, off extension record 28 (id 161) — see
/// <see cref="s_ownerBeatSeconds"/> for the ruling it obeys and for the two-rhythms-on-one-board
/// picture that reading this viewer's own dial produced. What that costs is the sentence this
/// paragraph used to end with: framed chips on a peer's board no longer beat in phase with the
/// viewer's own item cues when the two players have tuned the dial differently. That is the ruling,
/// not an oversight — a mirror wears the owner's dial, and 1:1 outranks local legibility.</para>
/// </summary>
internal static class RemoteUsableFrame
{
    /// <summary>Pixel width the card is mapped to for the frame canvas — <c>ItemsPile</c>'s
    /// <c>FrameReferencePixels</c>. Working in a fixed pixel reference is what lets the outset and
    /// the corner radius be stated in the same uGUI units the initiative ring uses.</summary>
    internal const float FrameReferencePixels = 300f;

    /// <summary>How far outside the card silhouette the frame floats — <c>ItemsPile</c>'s
    /// <c>FrameOutsetPixels</c>.</summary>
    internal const float FrameOutsetPixels = 8f;

    /// <summary>Corner radius of the 9-sliced outline sprite — <c>ItemsPile</c>'s
    /// <c>FrameCornerRadiusPx</c>.</summary>
    internal const int FrameCornerRadiusPx = 14;

    /// <summary>Local Z of the frame canvas: the viewer side of the card face — <c>ItemsPile</c>'s
    /// <c>FrameZ</c>.</summary>
    internal const float FrameZ = -0.0022f;

    /// <summary>The mod's telegraph gold, warmed toward the initiative ring's amber. Alpha is the
    /// CEILING the breath multiplies.</summary>
    internal static readonly Color FrameColor = new(1f, 0.80f, 0.32f, 1f);

    /// <summary>Breath floor / ceiling / silhouette swing — <c>ItemsPile</c>'s own arguments to
    /// <see cref="WorldUI.SoftFramePulse.Init(Graphic, Color, float, float, float, float)"/>.
    /// A SILHOUETTE change is the half of this cue a bright passthrough room cannot swallow.</summary>
    internal const float FrameMinAlpha = 0.45f;
    internal const float FrameMaxAlpha = 1f;
    internal const float FrameScalePulse = 0.07f;

    /// <summary>
    /// THE BOARD OWNER'S OWN <c>[Cards] ItemCueBeatSeconds</c>, latched by
    /// <see cref="ResolveSlots"/> for the <see cref="Build"/> calls that follow it in the same tick.
    /// Seeded with the SHIPPED constant, which is what every untuned client resolves to anyway.
    ///
    /// <para><b>WHY THE VALUE ARRIVES THROUGH A LATCH RATHER THAN AN ARGUMENT.</b> The only caller
    /// of both methods is <c>RemoteItemFan.TickUsableFrames</c>, which calls
    /// <see cref="ResolveSlots"/> and then, in the loop directly beneath it and behind that call's
    /// own <c>true</c>, <see cref="Build"/> — so the owner whose beat this is, is the owner whose
    /// slots were just resolved, in the same statement block, for the same fan.
    /// <see cref="Build"/> itself is handed a chip transform and two floats and has no route to a
    /// <see cref="RemoteAvatar"/>; widening its signature is a one-line change in a file this lane
    /// was not given, and it is named in the round report. The optional parameter on
    /// <see cref="Build"/> is that seam, already open: the day the caller passes the owner's number
    /// explicitly, this field stops being read and can go.</para>
    ///
    /// <para>THE PREVIOUS VALUE HERE WAS THIS VIEWER'S OWN DIAL, and that was a ruling breach
    /// (R2 finding F2). <c>NetProtocol.cs:647</c>, in the ModBuild 478/479 RULINGS block:
    /// "ItemCueBeatSeconds follows the OWNER on both surfaces. <c>[Voice] BadgeScale</c> is an
    /// approved viewer-local exception." The OTHER of those two surfaces already obeys it —
    /// <c>RemoteControlBoard.cs:3443</c> takes <c>tuning.ItemCueBeatSeconds</c> off record 28 id
    /// 161 for the closed pile-stack's rings — so a maintainer at 3.0 s watching a peer's open item
    /// fan saw the closed stack breathing at 3.0 s (the owner's) and the chip frames at 1.25 s
    /// (his own), TWO RHYTHMS ON ONE BOARD, and neither of them the owner's. The defending comment
    /// at <c>RemoteItemFan.cs:1910-1918</c> argues that "how fast the room breathes is the room's"
    /// and cites no ruling; it argues against a decision already taken, and it is corrected there.
    /// </para>
    ///
    /// <para>RESIDUE, STATED: a frame is built once and its <see cref="WorldUI.SoftFramePulse"/> is
    /// initialised then, so an owner who moves this dial WHILE a peer is watching their open fan
    /// keeps the old period on the already-built frames until they are rebuilt.
    /// <c>RemoteItemFan.SyncTuning</c> explicitly does not rebuild for animation dials ("No rebuild
    /// is ever needed"), so that window can outlive the fan. <c>SoftFramePulse.Init</c> is
    /// re-callable, so closing it is one more line on the revision edge in the same caller — also in
    /// the round report. It costs nothing at the shipped defaults and cannot be closed from
    /// here.</para>
    /// </summary>
    private static float s_ownerBeatSeconds = Defaults.ItemCueBeatSeconds;

    /// <summary>
    /// Build the frame as a child of <paramref name="parent"/>, INACTIVE — the caller switches it on
    /// for the positions record 35 names. Structure and numbers are
    /// <c>ItemsPile.ItemChip.BuildUsableFrame</c>'s: a world-space canvas at
    /// <c>sortingOrder</c> 1 (above the hosted face canvas at 0, decided rather than distance-luck)
    /// carrying one stretched child Image with the hollow 9-sliced outline, so
    /// <see cref="WorldUI.SoftFramePulse"/> can breathe the CHILD's localScale without rescaling the
    /// canvas' pixel reference under it.
    ///
    /// <para><c>CardGlow.RankWithPanels</c> is called for the reason the owner's frame calls it: this
    /// outline floats just OUTSIDE the card silhouette, which is exactly where the card's
    /// depth-writing slab did NOT stamp depth, so an MR backing plate on the panel ladder would
    /// simply paint over it. Ranking it by its own eye distance is what keeps the reported "Die
    /// mixed reality hintergründe schieben sich vor den outlines von karten" from coming back on the
    /// mirrored arc.</para>
    /// </summary>
    /// <param name="ownerBeatSeconds">The BOARD OWNER's <c>[Cards] ItemCueBeatSeconds</c> off
    /// extension record 28 (id 161). Zero or less means "the caller did not name it", and the beat
    /// then comes from <see cref="s_ownerBeatSeconds"/> — the same owner's value, latched by
    /// <see cref="ResolveSlots"/> a few lines earlier in the caller. It is NEVER this viewer's live
    /// dial; see <see cref="s_ownerBeatSeconds"/> for the ruling and for what reading the viewer's
    /// copy was costing.</param>
    internal static GameObject Build(Transform parent, float cardW, float cardH,
                                     float ownerBeatSeconds = 0f)
    {
        var canvasGo = new GameObject("RemoteUsableFrame", typeof(RectTransform), typeof(Canvas));
        var rt = (RectTransform)canvasGo.transform;
        rt.SetParent(parent, worldPositionStays: false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1;

        float scale = cardW / FrameReferencePixels;
        rt.sizeDelta = new Vector2(FrameReferencePixels + 2f * FrameOutsetPixels,
                                   (scale > 0f ? cardH / scale : cardH) + 2f * FrameOutsetPixels);
        rt.localScale = new Vector3(scale, scale, scale);
        rt.localPosition = new Vector3(0f, 0f, FrameZ);
        rt.localRotation = Quaternion.identity;

        var ringGo = new GameObject("Ring", typeof(RectTransform), typeof(Image));
        var ringRt = (RectTransform)ringGo.transform;
        ringRt.SetParent(rt, worldPositionStays: false);
        ringRt.anchorMin = Vector2.zero;
        ringRt.anchorMax = Vector2.one;
        ringRt.offsetMin = Vector2.zero;
        ringRt.offsetMax = Vector2.zero;
        ringRt.localPosition = Vector3.zero;
        ringRt.localRotation = Quaternion.identity;

        var img = ringGo.GetComponent<Image>();
        img.sprite = WorldUI.SoftCueArt.FrameSprite(FrameCornerRadiusPx);
        img.type = Image.Type.Sliced;
        img.fillCenter = false;    // hollow — a frame, never a wash over the art
        img.raycastTarget = false; // nothing may raycast a peer's mirror
        img.color = FrameColor;
        // ONE RHYTHM FOR THE WHOLE ITEM CUE, AND IT IS THE BOARD OWNER'S. The same
        // [Cards] ItemCueBeatSeconds that drives the owner's own chip frame and the closed stack's
        // rings, taken off THEIR record 28 (id 161) — not this viewer's copy of the dial, which is
        // what stood here and which put two rhythms on one mirrored board. The ruling is quoted on
        // s_ownerBeatSeconds; the sibling consumer is RemoteControlBoard.cs:3443. Guarded the same
        // way that one is, because a wire value is never trusted and a zero beat divides.
        ringGo.AddComponent<WorldUI.SoftFramePulse>().Init(
            img, FrameColor,
            beatSeconds: Mathf.Max(0.2f, ownerBeatSeconds > 0f ? ownerBeatSeconds : s_ownerBeatSeconds),
            minAlpha: FrameMinAlpha, maxAlpha: FrameMaxAlpha, scalePulse: FrameScalePulse);

        VRLayers.Apply(canvasGo);
        CardGlow.RankWithPanels(canvasGo);
        canvasGo.SetActive(false);
        return canvasGo;
    }

    /// <summary>
    /// Re-seed an ALREADY BUILT frame's period from the owner's dial. Closes the residue this file
    /// recorded when the beat moved onto the wire: <see cref="Build"/> runs once per frame object
    /// and <c>SoftFramePulse.Init</c> is the only writer of the period, so an owner who moved
    /// <c>[Cards] ItemCueBeatSeconds</c> WHILE a peer had their fan open kept the old rhythm until
    /// something forced a rebuild — and <c>RemoteItemFan</c> deliberately does not rebuild for an
    /// animation dial.
    ///
    /// <para>Called only from that class's tuning-revision edge, and only when the value actually
    /// changed, so the steady state costs nothing. Silently does nothing for a frame that has no
    /// pulse component — a frame mid-teardown is not an error worth a line.</para>
    /// </summary>
    /// <param name="frame">A frame object returned by <see cref="Build"/>.</param>
    /// <param name="ownerBeatSeconds">The BOARD OWNER's dial off record 28, guarded by the caller
    /// and guarded again here for the same reason <see cref="Build"/> guards it: a wire value is
    /// never trusted and a zero beat divides.</param>
    internal static void Retune(GameObject? frame, float ownerBeatSeconds)
    {
        if (frame == null)
            return;
        var pulse = frame.GetComponentInChildren<WorldUI.SoftFramePulse>(includeInactive: true);
        if (pulse == null)
            return;
        var img = pulse.GetComponent<Image>();
        if (img == null)
            return;
        pulse.Init(
            img, FrameColor,
            beatSeconds: Mathf.Max(0.2f, ownerBeatSeconds),
            minAlpha: FrameMinAlpha, maxAlpha: FrameMaxAlpha, scalePulse: FrameScalePulse);
    }

    /// <summary>
    /// Map a record-35 mask over <c>Inventory.AllItems</c> RAW index onto ARC SLOTS of a mirrored
    /// item fan, filling <paramref name="into"/> with one flag per slot. Returns false — leaving the
    /// list empty, i.e. "no frames" — when the peer's inventory cannot be read at all, which is the
    /// safe direction because it is the state a fresh arc is in.
    ///
    /// <para>THE TWO INDEX SPACES ARE NOT THE SAME, and that is the entire reason this method
    /// exists rather than a direct <c>mask &gt;&gt; i</c> in the caller. Both fan builders SKIP null
    /// inventory entries (<c>ItemsPile.Populate</c> on the owner's side,
    /// <c>RemotePileFronts.Resolve</c> on this one) while <c>ItemsPile.UsableMask</c> sets bit i
    /// from the RAW index — so one null anywhere in the list puts every later chip one arc position
    /// below its mask bit, and the frame would land on the neighbouring card. Counting non-nulls
    /// here is what makes the mask address the same chip the owner is looking at.</para>
    /// </summary>
    internal static bool ResolveSlots(RemoteAvatar owner, ushort mask, List<bool> into)
    {
        into.Clear();
        if (owner == null)
            return false;
        // THE OWNER'S BEAT, TAKEN ON THE SAME SEAM AS THEIR SLOTS. Written before any path that can
        // reach a Build: the caller only builds behind this method's `true`, so a frame minted in
        // this tick always breathes on the tuning of the board it is being minted onto. Read every
        // call rather than latched on the revision edge, for RemoteMapRoom.ScaleFactorFor's reason:
        // a peer whose record 28 has not landed resolves to this client's shipped constant (the
        // RemoteAvatar constructor seeds BoardTuning from an EMPTY payload) and corrects itself on
        // the first tick after it does, instead of staying wrong for the session.
        s_ownerBeatSeconds = Mathf.Max(0.2f, owner.BoardTuning.ItemCueBeatSeconds);
        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(owner, out _);
        CInventory? inv = actor != null ? actor.Inventory : null;
        List<CItem>? all = inv != null ? inv.AllItems : null;
        if (all == null)
            return false;
        for (int raw = 0; raw < all.Count; raw++)
        {
            if (all[raw] == null)
                continue;   // no chip is built for a null entry — on either machine
            bool on = raw < NetProtocol.ItemUsableMaskBits && (mask & (1 << raw)) != 0;
            into.Add(on);
        }
        return true;
    }
}

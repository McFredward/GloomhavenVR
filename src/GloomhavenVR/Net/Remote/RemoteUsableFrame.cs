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
/// driven off <c>Time.unscaledTime</c> — so every framed card on every board in the room breathes
/// in PHASE and reads as one cue rather than several competing flickers. The pixel constants below
/// are the owner's, restated with their source named, and they are pinned against
/// <c>Cards/Piles/ItemsPile.cs</c> by <c>ItemUsableVectors</c> in the wire tests so the two frames
/// cannot drift apart unnoticed.</para>
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
    internal static GameObject Build(Transform parent, float cardW, float cardH)
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
        // ONE RHYTHM FOR THE WHOLE ITEM CUE: the same [Cards] ItemCueBeatSeconds the owner's frame
        // and the closed stack's rings beat on. This is a DIAL, and it is deliberately the VIEWER's
        // own copy of it rather than a wire field — see the note in RemoteItemFan.TickUsableFrames.
        ringGo.AddComponent<WorldUI.SoftFramePulse>().Init(
            img, FrameColor,
            beatSeconds: Mathf.Max(0.2f, CardsConfig.ItemCueBeatSeconds.Value),
            minAlpha: FrameMinAlpha, maxAlpha: FrameMaxAlpha, scalePulse: FrameScalePulse);

        VRLayers.Apply(canvasGo);
        CardGlow.RankWithPanels(canvasGo);
        canvasGo.SetActive(false);
        return canvasGo;
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

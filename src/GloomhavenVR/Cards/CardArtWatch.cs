using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// ZERO-ALIASED-FRAME card art: the seam that makes a mip-baked sprite the FIRST thing a card
/// ever renders, instead of the second.
///
/// <para>THE REPORT (user, verbatim): "Man sieht wenn man neue Karten auflegt (zB beim Wechsel des
/// Characters) immer für ca. 1 Sekunde die Variante mit aliasing und dann hört es auf und es
/// deutlich besser. Ich will die Karte ohne aliasing direkt sehen ohne dass es erst nachgeladen
/// werden muss."</para>
///
/// <para>THE MEASURED CHAIN — every step read from source, not inferred:</para>
/// <list type="number">
/// <item><description><c>FullAbilityCard.OnEnable → ShowCard()</c> starts the addressable card-art
/// loads (decompiled/GH.Runtime/FullAbilityCard.cs:430-447). Until a load lands,
/// <c>ImageLoadingContext.LoadAsync</c> holds that Image <c>enabled = false</c>
/// (ImageLoadingContext.cs:31), so its art is not on screen at all.</description></item>
/// <item><description>The load lands across TWO Unity sync-context continuations, i.e. two
/// different frames: the inner one assigns <c>image.sprite</c>
/// (UIExtensions.cs:23), the outer one then runs <c>image.enabled = true</c>
/// (ImageLoadingContext.cs:37). There is therefore a whole frame in which the mipless sprite is
/// ON the Image but nothing of it is visible — the exact window this watch aims at.</description></item>
/// <item><description>Nothing on the mod side looked at the face between those events. The only
/// re-scan was a fixed <c>1 s</c> cadence (<c>CardFace.MipRescanInterval</c>, and its twins in
/// <c>Net.RemoteCardArt</c> / <c>ItemsPile</c>), so the game's mipless art rendered for anything
/// up to a full second before the swap. That constant IS the reported second.</description></item>
/// </list>
///
/// <para>WHY IT WAS "IMMER" AND NOT ONLY THE FIRST TIME: the bake caches
/// (<c>CardFaceMipBake.s_bakedByIdentity</c> / <c>s_regionTextureByKey</c>) are keyed by CONTENT
/// identity and are never evicted, so the second switch to a class the session already baked is a
/// pure dictionary hit — microseconds. The aliasing still lasted a second, because what was late
/// was the SWAP, not the bake. Making the bake faster would have fixed nothing.</para>
///
/// <para>WHAT THIS DOES: hold the card's <c>Image</c> list and the sprite each one carried last
/// frame, and once per frame (from a LateUpdate, so it is after the loader's continuations and
/// before uGUI builds the canvas) swap any Image whose sprite REFERENCE changed. On the async
/// path that fires in the frame described in step 2 — while the Image is still disabled — so the
/// bake itself also runs where the player cannot see it, and the art's first rendered frame is
/// already the mipmapped copy. On the loader's warm fast path (a synchronous re-assign inside
/// <c>ShowCard</c>) it fires in the same frame as the assignment, still ahead of rendering.</para>
///
/// <para>COST, and why it swaps per-Image instead of re-running
/// <see cref="CardFaceMipBake.Rescan"/>: the steady state is one reference compare per Image per
/// frame and NO allocation. A card face carries <c>CardEffects</c> animations that rewrite a
/// sprite every frame while a card burns; a "something changed → full rescan" shape would have
/// turned that into a per-frame <c>GetComponentsInChildren</c> walk plus an array allocation on
/// every card in the fan. Here a churning Image costs one dictionary lookup.</para>
///
/// <para>NOTHING POPS: a sprite the bake refuses (rotated / tight-packed / inexact trim geometry,
/// or the VRAM ceiling) resolves to null ONCE, is written back into the snapshot as the original,
/// and is never asked about again — the Image simply keeps the game's own texture, exactly as
/// before. There is no low-res placeholder and no second swap to notice.</para>
/// </summary>
internal sealed class CardArtWatch
{
    /// <summary>Every <c>Image</c> under the watched root, captured on <see cref="Capture"/>.</summary>
    private Image[]? _images;

    /// <summary>The sprite each watched Image carried at the last <see cref="Poll"/>. Parallel to
    /// <see cref="_images"/>.</summary>
    private Sprite?[]? _sprites;

    /// <summary>
    /// ROUND 11 — the DISSOLVE EPSILON FLOOR rides this watch, and the placement is the point:
    /// this class is the ONE per-frame, per-face seam that BOTH the locally adopted ability faces
    /// (<c>CardFace.MaintainArtArrival</c> polls from <c>VRCard.LateUpdate</c>) and every remote
    /// peer front (<c>Net.RemoteCardArt.MaintainMipBake</c> polls each frame a front is shown —
    /// hand fan, board slots, active column, pile/item fans) already run, with the face's
    /// <c>Image</c> array in hand. Riding it covers the user's full scope ("… UND auch alle
    /// Karten genauso die remote angezeigt werden im Multiplayer") without a single Net/** edit;
    /// the hosted ITEM cards, which have no art watch, carry their own instance in
    /// <c>ItemsPile.ItemChip</c>. Captured with the watch arrays, ticked at the TOP of
    /// <see cref="Poll"/> — deliberately BEFORE the [Cards] FaceMipBake gate, because the floor
    /// is a shader-state fix and must not vanish with a texture-quality dial — and released in
    /// <see cref="Clear"/> (the same full-restore moment the sprites use).
    /// </summary>
    private readonly CardDissolveFloor _dissolveFloor = new();

    /// <summary>Running VRAM total at the last logged line — see <see cref="LogStepBytes"/>.</summary>
    private static long s_lastBudgetLoggedBytes = -1;

    /// <summary>Budget growth that earns a new log line (1 MB). A whole class' ability-card art is
    /// ~10-20 MB, so every first-sight class switch logs exactly once, while the per-sprite chrome
    /// (tens of KB each) never spams. The line carries the RESOLVED budget so a hardware log can
    /// still answer "did the ceiling bind?" — the question the 2026-08 regression turned on.</summary>
    private const long LogStepBytes = 1024L * 1024L;

    /// <summary>True once a root has been captured and the per-frame poll can do anything.</summary>
    internal bool IsArmed => _images != null && _sprites != null;

    /// <summary>
    /// (Re)capture the watch arrays from <paramref name="root"/>'s live hierarchy. Cheap enough
    /// to run on adoption and on the owner's slow backstop cadence (the allocating
    /// <c>GetComponentsInChildren</c> lives here and nowhere else), which is also what picks up
    /// Images the game created after the last capture.
    /// </summary>
    internal void Capture(Component? root)
    {
        _images = null;
        _sprites = null;
        if (root == null)
            return;
        try
        {
            // includeInactive: the loader disables the very Images whose art we are waiting for,
            // and the game toggles face sub-widgets (enhancement slots, XP orbs) at will.
            Image[] images = root.GetComponentsInChildren<Image>(includeInactive: true);
            var sprites = new Sprite?[images.Length];
            for (int i = 0; i < images.Length; i++)
                sprites[i] = images[i] != null ? images[i].sprite : null;
            _images = images;
            _sprites = sprites;
            // Round 11: (re)collect the card-FX material clones from the same array — the floor's
            // only allocating step shares this walk instead of adding one.
            _dissolveFloor.Capture(images);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"Card art watch capture failed ({ex.GetType().Name}: {ex.Message}) — " +
                                "the owner's cadenced mip-bake backstop still runs.");
        }
    }

    /// <summary>Forget the watched hierarchy (the face went back to the game / the clone died).</summary>
    internal void Clear()
    {
        _images = null;
        _sprites = null;
        // Full-restore contract: hand the game back its own rest-state dissolve.
        _dissolveFloor.Release();
    }

    /// <summary>
    /// One frame's worth of arrival handling: swap every Image whose sprite reference changed for
    /// its mip-baked equivalent. Returns the number swapped (0 = nothing changed, the common
    /// case). Fully guarded — a bake surprise degrades to the game's own mipless sprite, never to
    /// a broken face. <paramref name="what"/> names the surface in the log line.
    /// </summary>
    internal int Poll(string what)
    {
        // Round 11: the dissolve floor ticks FIRST, before the mip-bake gate below — it is a
        // shader-state fix (the black frame band), not a texture-quality one, and must keep
        // running when [Cards] FaceMipBake is off.
        _dissolveFloor.Tick();
        if (CardsConfig.FaceMipBake == null || !CardsConfig.FaceMipBake.Value)
            return 0;
        Image[]? images = _images;
        Sprite?[]? seen = _sprites;
        if (images == null || seen == null || images.Length != seen.Length)
            return 0;

        long before = CardFaceMipBake.BakedVramBytes;
        int swapped = 0;
        try
        {
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null)
                    continue;
                Sprite? now = img.sprite;
                if (ReferenceEquals(now, seen[i]))
                    continue; // unchanged — the overwhelmingly common case, one compare
                if (now != null && !CardFaceMipBake.IsBakedSprite(now))
                {
                    Sprite? replacement = CardFaceMipBake.ReplacementFor(now);
                    if (replacement != null)
                    {
                        img.sprite = replacement;
                        swapped++;
                    }
                }
                // Snapshot what the Image ACTUALLY carries now — our replacement, or the original
                // when the bake refused this sprite. Either way it is asked about exactly once.
                seen[i] = img.sprite;
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"Card art arrival swap failed on {what} " +
                                $"({ex.GetType().Name}: {ex.Message}) — the card keeps the game's " +
                                "sprites and the cadenced backstop takes over.");
            Clear();
            return 0;
        }

        if (swapped > 0)
            LogBudget(what, swapped, before);
        return swapped;
    }

    /// <summary>Budget bookkeeping line, throttled to <see cref="LogStepBytes"/> of growth.</summary>
    private static void LogBudget(string what, int swapped, long before)
    {
        long bytes = CardFaceMipBake.BakedVramBytes;
        if (s_lastBudgetLoggedBytes >= 0 && bytes - s_lastBudgetLoggedBytes < LogStepBytes)
            return;
        s_lastBudgetLoggedBytes = bytes;
        VRLog.Info("Cards", $"MIP BAKE on arrival ({what}): {swapped} sprite(s) swapped in the SAME frame " +
                            "the game assigned the art — i.e. while the loader still had the Image " +
                            "disabled, so the card's FIRST rendered frame is already the mipmapped copy " +
                            "(the ~1 s aliased window is gone). Budget now " +
                            $"{CardFaceMipBake.BudgetSummary}" +
                            (bytes > before
                                ? $"; this arrival baked ~{(bytes - before) / (1024f * 1024f):F1} MB."
                                : "."));
    }
}

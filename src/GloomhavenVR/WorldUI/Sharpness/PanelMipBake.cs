using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Runtime MIP BAKE for the WorldUI panel surfaces -- the initiative track and the
/// mouseover tooltip box (user report: "Aliasing ist extrem stark bei den Raendern der
/// Mouse-Overlay-Hints und auch den Linien und Raendern der Bilder in der
/// Initiativreihenfolge ... bei den Karten hast du das bereits geschafft").
///
/// ROOT CAUSE -- the SAME data defect the card faces had (Cards/CardFaceMipBake.cs, the
/// proven prior art this class deliberately mirrors). The INITIATIVE TEXTURE DIAG
/// hardware log line already proved it for the track: every texture the adopted track
/// canvas samples ships with mipmapCount == 1, aniso 1, Bilinear. The tooltip canvas
/// draws from the same family of mipless UI atlases. A mipless bilinear texture under
/// minification (a portrait or a tooltip frame on a world-space quad at board distance
/// covers far fewer pixels than its texels) aliases in TEXTURE space: MSAA resolves
/// geometry edges only and supersampling merely shrinks the footprint further, so the
/// shimmer survives both -- exactly as it did on the cards until the mip bake.
///
/// MECHANISM -- one shared cache, two graphic kinds:
/// <list type="bullet">
/// <item><description><c>Image</c> (frame / selection / line sprites): swapped through
/// <see cref="CardFaceMipBake.ReplacementFor"/>, the proven sprite-equivalence path
/// (same rect / pivot / PPU / border on a mipmapped trilinear/aniso copy; rotated,
/// tight-packed and inexact-trim sprites are SKIPPED with a log line, never guessed --
/// the v3 card-corruption hard rule). Sharing the Cards cache is deliberate: a tooltip
/// icon on an atlas a card face already baked reuses that copy instead of holding a
/// second ~85 MB one.</description></item>
/// <item><description><c>RawImage</c> (the track's <c>m_AvatarImage</c> portraits):
/// there is NO sprite to swap -- the game's <c>CharacterPortraitsProvider</c> assigns a
/// raw <c>Texture</c> plus a <c>uvRect</c> (decompiled InitiativeTrackActorAvatar.cs:
/// <c>UpdateTexture(texture, coords)</c>). The whole texture is baked via
/// <see cref="CardFaceMipBake.BakedTextureFor(Texture2D)"/> (identical dimensions, so
/// the uvRect stays valid untouched) and <c>RawImage.texture</c> is pointed at the
/// copy. Only <c>Texture2D</c> sources bake; anything else (a RenderTexture, null
/// after the provider's <c>UnloadedTexture</c>) is left alone.</description></item>
/// </list>
///
/// RE-ASSERT, CHANGE-GATED -- both surfaces are POOLED and REBUILT by the game: the
/// track re-registers every avatar per round (<c>SetAttributesDirect</c> -&gt;
/// <c>RegisterNewUser</c> -&gt; <c>UpdateTexture</c> puts the ORIGINAL mipless texture
/// back, and portraits arrive async from the misc_characterportraits bundle), and the
/// tooltip's line objects are pooled per hover. So the owners re-run
/// <see cref="Rescan"/> (the track on a 1 s cadence while converted, mirroring
/// CardFace.MipRescanInterval; the tooltip on its already-gated visible frames) and the
/// scan itself is idempotent-cheap: an Image already wearing a baked sprite and a
/// RawImage already pointing at a baked copy resolve via dictionary hits, and a write
/// happens ONLY when the game genuinely swapped a graphic back (the CardParticlesOff
/// re-assert spirit -- never per-frame churn). The scans use the List-based
/// GetComponentsInChildren overloads into reused scratch lists, so the steady state
/// allocates nothing.
///
/// RESTORE -- mutate-and-restore house style: <see cref="Restore"/> hands every live
/// graphic its original sprite/texture back (sprites via
/// <see cref="CardFaceMipBake.RestoreSprites"/>, raw textures via the baked-to-original
/// map here; an original the game has meanwhile Destroyed -- e.g. its rebuilt
/// portrait mega-texture -- restores to null, the game's own <c>UnloadedTexture</c>
/// contract, and the provider re-assigns on its next refresh). Called when the track
/// surface releases/shuts down and when the tooltip presentation restores to screen
/// space. The baked textures themselves stay in the session-lifetime shared cache --
/// the CardFaceMipBake precedent (bake once per content identity, budgeted and logged,
/// never destroyed mid-session) -- because a card face may still be sampling the very
/// atlas copy a tooltip icon shares.
///
/// Config gate: [WorldUI] PanelMipBake (mirrors [Cards] FaceMipBake, read live).
/// Every actual bake logs name/size/mips/VRAM through the shared MIP BAKE log lines,
/// so the hardware log carries the coverage evidence per texture; this class adds one
/// line per SURFACE naming how many graphics were swapped.
///
/// <para>=====================================================================</para>
/// <para>THE ARRIVAL WATCH (ModBuild 192) -- WHY THE SHOP ITEM CARD SHOWED ALIASED FOR
/// A SECOND AND THEN SNAPPED CLEAN.</para>
///
/// <para>THE REPORT (user, verbatim): "Shop mouseovers sind da, aber die Itemkarten dort
/// werden 1 Sekunde mit dem starken aliasing angezeigt dann sieht man wie es
/// verschwindet. Ich will das direkt die Variante ohne aliasing angezeigt wird und es
/// nicht erst nachgeladen wird."</para>
///
/// <para>MEASURED FROM THE ModBuild 191 HARDWARE LOG (.planning/debug/Player.log), not
/// inferred. The surface is the merchant's item hint -- log line 5683,
/// <c>'UIPartyItemInventoryTooltip (item card hint)'</c>, hanging inside
/// <c>GloomhavenVR.Panel_Modal_UI Shop Item Window</c>. Its art is one standalone
/// 512x497 texture per item, addressable-loaded on hover ('second skin',
/// 'cloak of pockets', 'splintmail', 'Iron_Helmet', 'Minor_Healing_Potion' ...). The
/// window floated at frame 27811 and the hint was first SHOWN around frame 27862-27892
/// (log lines 5684 / 5735). The FIRST item art was not baked until log line 5944,
/// between the SPIKE frames 28474 and 28503 -- i.e. ~600 frames later, after roughly
/// sixty show/hide cycles of the very same hint. Once the queue had drained, each newly
/// hovered item's art landed one to two scans after it appeared: 'cloak of pockets'
/// between frames 28563 and 28591, 'splintmail' 28622-28650, 'flea bitten shawl'
/// ~28709. Frames in that scene cost 24-27 ms (the SPIKE lines), so one scan period is
/// ~0.73 s and one-to-two scans is ~0.7-1.5 s. THAT IS THE REPORTED SECOND, to the
/// frame.</para>
///
/// <para>WHY: on a FLOATED window nothing in this file was ever called. Every
/// <see cref="Rescan"/> caller is a scenario surface (the initiative track, the stat
/// panels, the prop-info and enemy-reveal cards, the scenario tooltip canvas); the map
/// room's shop window has no such owner. The only thing that mip-baked it was
/// <c>PanelSamplingProbe</c> -- a MEASURING instrument that also treats what it
/// measures -- and it is structurally incapable of a zero-aliased-frame swap for two
/// independent reasons, both read from its source: it scans every
/// <c>ScanIntervalFrames = 30</c> frames, and it walks the panel with
/// <c>GetComponentsInChildren(includeInactive: false)</c>, so a graphic the loader has
/// not enabled yet is INVISIBLE TO IT. By the time the probe can see the item art, the
/// art is already on screen. Its <c>MaxBakeAttemptsPerScan = 4</c> is what stretched the
/// first item's wait to ~600 frames. None of that is a fault in the probe: measuring is
/// its job, and it must not measure a graphic that has no rendered size.</para>
///
/// <para>WHAT THE ARRIVAL WATCH DOES -- the ZERO-FRAME seam, and it is the same seam
/// <c>Cards/CardArtWatch</c> already won this exact argument on for the ability cards
/// ("MIP BAKE on arrival (remote card): ... swapped in the SAME frame the game assigned
/// the art"). It holds, per floated panel, the panel's <c>Image</c> array captured with
/// <c>includeInactive: TRUE</c> plus the sprite each one carried last frame, and once
/// per frame from a LateUpdate it swaps every Image whose sprite REFERENCE changed. The
/// game's <c>ImageLoadingContext</c> keeps the Image <c>enabled = false</c> until the
/// OUTER continuation of the load (ImageLoadingContext.cs:31/37 -- assignment and enable
/// land in two different frames), so on the async path the swap happens while the
/// graphic is still hidden and the item card's FIRST rendered frame is already the
/// mipmapped copy. There is no low-res placeholder and no second swap to notice: a
/// sprite the bake refuses resolves to null once, is written back into the snapshot as
/// the original, and is never asked about again.</para>
///
/// <para>COST, BOUNDED AND STATED. Steady state per frame is ONE reference compare per
/// watched Image and NO allocation -- the shop window carries ~475 graphics, so ~1000
/// compares across every floated panel, single-digit microseconds. The allocating
/// <c>GetComponentsInChildren</c> re-capture (which is what picks up Images the game
/// pooled into the hint after the last capture) runs for at most ONE panel per frame and
/// at most once per <see cref="RecaptureIntervalFrames"/> per panel -- the probe's own
/// cadence, and strictly less walking than the probe already does every 30 frames.
/// NEW bakes are rate-capped at <see cref="MaxArrivalBakesPerFrame"/> per frame, which
/// is HALF the probe's existing <c>MaxBakeAttemptsPerScan = 4</c>: this seam can never
/// do more bake work in one frame than the code already shipping may. Over the cap a
/// graphic is DEFERRED, not dropped -- its snapshot entry is left stale so the next
/// frame retries it -- and the deferral count is on the log line. VRAM policy is
/// UNCHANGED: every bake still goes through the one shared cache, the one
/// <c>CardFaceMipBake.MaxBakedVramBytes</c> ceiling and the same skip verdicts; this
/// class changes WHEN a swap happens, never WHETHER one is affordable.</para>
///
/// <para>WHY IT WATCHES ARRIVALS AND NOT "EVERYTHING ON THE PANEL": a blanket per-frame
/// bake of every graphic on a floated window is exactly the pressure the probe's 1.35
/// minification gate exists to prevent (and the 2026-08 count-budget regression is what
/// happens when bake budget goes to chrome). An arrival watch fires ONLY when the game
/// assigns new art, so the steady state bakes nothing at all. In particular a panel's
/// FIRST capture RECORDS and does not swap -- the initial population is the probe's job,
/// it already does it, and front-loading ~475 pieces of window chrome at two bakes a
/// frame would put the one graphic the user complained about at the BACK of a six-second
/// queue. See <c>ArrivalWatch.FirstCaptureDone</c>.</para>
///
/// <para>DRIVEN BY ITS OWN PUMP, because there is no seam to borrow: the map room's
/// floated windows have no per-frame owner in a file this lane owns, and the WorldUI
/// module's tick list is not ours to extend. <see cref="EnsureArrivalWatch"/> installs a
/// single hidden DontDestroyOnLoad GameObject whose LateUpdate calls
/// <see cref="TickArrivals"/>, and it is called from <see cref="Rescan"/> (scenario) and
/// from <c>CardFaceMipBake.ReplacementFor</c> / <c>BakedTextureFor</c> (which the probe
/// calls on the shop window, so the pump is guaranteed alive in the map room too). If
/// the GameObject cannot be created the class stands down completely with ONE Warn
/// naming the consequence -- the probe's 30-frame scan remains the only swap, i.e.
/// exactly today's behaviour, never worse.</para>
///
/// <para>MULTIPLAYER: unaffected. Every write here is a local <c>Image.sprite</c>
/// assignment to a mipmapped copy of a texture the game already shipped -- no game
/// state, no Bolt/FFSNet traffic, nothing on the wire, and no ScenarioRuleLibrary or
/// Bolt patch. A peer's client renders its own panels from its own cache.</para>
///
/// <para>OFF STATE: with [WorldUI] PanelMipBake false the tick returns before it looks at
/// anything, every watch is dropped and every panel it had swapped is restored -- which
/// is the same state the config-off build is in today, because the probe's own bake is
/// gated on the identical entry.</para>
/// </summary>
internal static class PanelMipBake
{
    /// <summary>Reused Image scan buffer (List overload of GetComponentsInChildren -- no
    /// steady-state allocation; the tooltip rescans on every visible frame).</summary>
    private static readonly List<Image> ImageScratch = new(64);

    /// <summary>Reused RawImage scan buffer (same discipline).</summary>
    private static readonly List<RawImage> RawScratch = new(16);

    /// <summary>baked raw-texture instance id -&gt; the game texture it replaced. First
    /// original wins (a second Texture2D instance deduped onto the same baked copy keeps
    /// the first mapping -- both are game-owned content-identical textures, and restore
    /// must be deterministic).</summary>
    private static readonly Dictionary<int, Texture> s_rawOriginalByBaked = new(16);

    /// <summary>Surfaces that already logged their first successful swap (one live line
    /// per surface, matching CardFaceMipBake's one-shot swap log).</summary>
    private static readonly HashSet<string> s_swapLogged = new(4);

    /// <summary>One warning per session on a throwing scan/restore -- a bake surprise must
    /// never spam nor break the owning surface (same latch as CardFaceMipBake).</summary>
    private static bool s_errorLogged;

    /// <summary>
    /// Swap every mipless-texture graphic under <paramref name="root"/> for its mip-baked
    /// equivalent (see the class doc). Idempotent and cheap once warm; guarded so a bake
    /// surprise can never break the owning surface's tick. Safe to call on any cadence --
    /// all writes are change-gated by construction (an already-swapped graphic is
    /// recognized and skipped).
    /// </summary>
    internal static void Rescan(Component? root, string surface)
    {
        if (root == null || WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
            return;
        // The arrival watch is a SEPARATE, panel-wide mechanism (see the class doc); this is
        // simply the earliest guaranteed call site in scenario mode. It is a managed bool test
        // once the pump exists.
        EnsureArrivalWatch();
        try
        {
            int swapped = 0;

            // Images: the frame / selection / digit / tooltip-line sprites. includeInactive
            // because the game toggles sub-widgets (selection frames, dead-image overlays,
            // pooled tooltip lines) -- swap them while hidden so re-activation shows the
            // baked copy at once.
            ImageScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, ImageScratch);
            for (int i = 0; i < ImageScratch.Count; i++)
            {
                Image img = ImageScratch[i];
                if (img == null)
                    continue;
                Sprite? sprite = img.sprite;
                if (sprite == null || CardFaceMipBake.IsBakedSprite(sprite))
                    continue; // no sprite / already sampling a baked copy
                Sprite? replacement = CardFaceMipBake.ReplacementFor(sprite);
                if (replacement != null)
                {
                    img.sprite = replacement;
                    swapped++;
                }
            }
            ImageScratch.Clear();

            // RawImages: the initiative portraits (see the class doc's RawImage note).
            RawScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, RawScratch);
            for (int i = 0; i < RawScratch.Count; i++)
            {
                RawImage raw = RawScratch[i];
                // The Unity-null check on tex matters: a Destroyed Texture2D still passes the
                // C# type pattern, and touching its width/height would throw out of the scan.
                if (raw == null || raw.texture is not Texture2D tex || tex == null)
                    continue; // null/destroyed (provider unloaded) or not a bakeable plain texture
                if (s_rawOriginalByBaked.ContainsKey(tex.GetInstanceID()))
                    continue; // already pointing at one of our baked copies
                if (tex.mipmapCount > 1)
                    continue; // the source already has mips -- nothing to fix
                Texture2D? baked = CardFaceMipBake.BakedTextureFor(tex);
                if (baked == null)
                    continue; // budget/size/failed -- keeps the original, never worse
                int bakedId = baked.GetInstanceID();
                if (!s_rawOriginalByBaked.ContainsKey(bakedId))
                    s_rawOriginalByBaked[bakedId] = tex;
                raw.texture = baked; // same dimensions -> the game's uvRect stays valid
                swapped++;
            }
            RawScratch.Clear();

            if (swapped > 0 && s_swapLogged.Add(surface))
            {
                VRLog.Info("WorldUI", $"MIP BAKE live on '{surface}': {swapped} graphic(s) now sample " +
                                      "mipmapped trilinear/aniso copies of the game's mipless textures " +
                                      "(shared cache with the card faces) -- texture-space shimmer " +
                                      "addressed at the data; see the MIP BAKE lines for each texture.");
            }
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("WorldUI", $"Panel mip-bake rescan failed on '{surface}' " +
                                      $"({ex.GetType().Name}: {ex.Message}) -- the surface keeps the " +
                                      "original mipless graphics.");
            }
        }
    }

    /// <summary>Read-only native identity for public raw-image presentation; never changes the
    /// owner's mipmapped output or the cache's lifetime.</summary>
    internal static Texture OriginalFor(Texture texture) =>
        s_rawOriginalByBaked.TryGetValue(texture.GetInstanceID(), out Texture original) && original != null
            ? original : texture;

    /// <summary>
    /// Hand every graphic under <paramref name="root"/> its ORIGINAL sprite/texture back --
    /// called before a surface returns its canvas to the game (track release/shutdown,
    /// tooltip restore to screen space), so the 2D UI is left exactly as authored. The
    /// full-restore counterpart of <see cref="Rescan"/>; guarded the same way.
    /// </summary>
    internal static void Restore(Component? root)
    {
        if (root == null)
            return;
        try
        {
            // Sprites: the shared cache knows every replacement it minted.
            CardFaceMipBake.RestoreSprites(root);

            // Raw textures: baked copy back to the recorded original. An original the game
            // has Destroyed since (its portrait mega-texture is rebuilt on demand) restores
            // to null -- the game's own UnloadedTexture state, re-filled by the provider.
            if (s_rawOriginalByBaked.Count == 0)
                return;
            RawScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, RawScratch);
            for (int i = 0; i < RawScratch.Count; i++)
            {
                RawImage raw = RawScratch[i];
                if (raw == null || raw.texture == null)
                    continue;
                if (s_rawOriginalByBaked.TryGetValue(raw.texture.GetInstanceID(), out Texture original))
                    raw.texture = original != null ? original : null;
            }
            RawScratch.Clear();
        }
        catch (System.Exception ex)
        {
            if (!s_errorLogged)
            {
                s_errorLogged = true;
                VRLog.Warn("WorldUI", $"Panel mip-bake restore failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }

    // ================================================================================
    // ARRIVAL WATCH -- see the class doc. Everything below exists to make the mipmapped
    // copy the FIRST thing a floated panel's freshly loaded art ever renders.
    // ================================================================================

    /// <summary>
    /// How often ONE watched panel re-runs the allocating <c>GetComponentsInChildren</c> walk that
    /// discovers Images the game pooled in since the last capture (the merchant hint's line objects
    /// are created per hover -- the ModBuild 191 log shows "MODAL LAYER: moved 27/29/44/55
    /// transform(s)" arriving batch by batch). 30 frames is the SAME cadence
    /// <c>PanelSamplingProbe.ScanIntervalFrames</c> already runs a heavier walk on, and only one
    /// panel re-captures per frame, so this adds strictly less work than what already ships.
    /// A newly discovered Image is entered with a NULL snapshot on purpose, so the very next poll
    /// treats whatever art it is carrying as an arrival and swaps it.
    /// </summary>
    private const int RecaptureIntervalFrames = 30;

    /// <summary>
    /// NEW bakes this seam may START in one frame. HALF of the probe's shipping
    /// <c>MaxBakeAttemptsPerScan = 4</c>, deliberately: a first-sight sprite off an atlas this
    /// session has not read back yet costs a full-atlas GPU readback (a 512x497 item card is ~1 MB
    /// and a few ms; a 4096&#178; atlas is ~64 MB and is the one genuine hitch risk in the whole
    /// cache), and in VR a stutter is worse than the aliasing it removes. Over the cap a graphic is
    /// DEFERRED -- its snapshot entry is left stale so the next frame retries it -- never dropped
    /// and never left permanently mipless. Already-asked sprites cost nothing and are not counted,
    /// so a card-burn animation that rewrites a sprite every frame can never exhaust it.
    /// </summary>
    private const int MaxArrivalBakesPerFrame = 2;

    /// <summary>Runaway guard on the snapshot arrays -- NOT a tuning knob. The shop window, the
    /// largest floated panel in the hardware log, carries ~475 graphics; 4096 is an order of
    /// magnitude above anything observed. Graphics past it keep the probe's 30-frame scan as their
    /// only swap, and that is said out loud once per panel.</summary>
    private const int MaxWatchedImagesPerPanel = 4096;

    /// <summary>Runaway guard on how many panels are watched at once (the log's busiest moment had
    /// three floated at once). Same contract as above: over the cap, the probe remains the swap.</summary>
    private const int MaxWatchedPanels = 12;

    /// <summary>Seconds between ARRIVAL log lines per panel. Counters accumulate across the
    /// interval, so throttling loses no evidence -- the line always carries resolved totals.</summary>
    private const float ArrivalLogIntervalSeconds = 5f;

    /// <summary>Snapshot slot state: this Image has been dealt with and must not be asked about
    /// again until its sprite reference CHANGES (it is wearing a baked copy, or the cache refused
    /// it). Deliberately a named constant and never an arithmetic operand -- the sentinel-overflow
    /// lesson (<c>now - int.MinValue</c> silently wrapping) applies to any magic sentinel, so the
    /// only place this value is ever read is an equality test.</summary>
    private const int SlotSettled = int.MinValue;

    /// <summary>Snapshot slot state: not yet evaluated since the last sprite change.</summary>
    private const int SlotUnknown = -1;

    /// <summary>One floated panel's arrival snapshot.</summary>
    private sealed class ArrivalWatch
    {
        internal ArrivalWatch(GameObject host, RectTransform target, string name)
        {
            Host = host;
            HostId = host.GetInstanceID();
            Target = target;
            Name = name;
        }

        internal readonly GameObject Host;
        internal readonly int HostId;

        /// <summary>The game's own window RectTransform. It SURVIVES the host's release (the host
        /// is ours and is destroyed; the target is merely reparented home), which is why restore
        /// goes through it and not through <see cref="Host"/> -- the same choice the probe makes.</summary>
        internal readonly RectTransform Target;

        internal readonly string Name;

        internal Image[] Images = System.Array.Empty<Image>();

        /// <summary>The sprite each watched Image carried at the last poll. Parallel to
        /// <see cref="Images"/>.</summary>
        internal Sprite?[] Seen = System.Array.Empty<Sprite?>();

        /// <summary><see cref="SlotSettled"/>, <see cref="SlotUnknown"/>, or the frame at which
        /// this Image was first seen RENDERABLE while carrying art we have not baked -- i.e. the
        /// start of an aliased window, which is the number the user is complaining about.</summary>
        internal int[] State = System.Array.Empty<int>();

        internal int NextRecaptureFrame;
        internal bool CapLogged;

        /// <summary>False until the panel's very first capture has run. THE FIRST CAPTURE RECORDS,
        /// IT DOES NOT SWAP: a floated window arrives with its whole population already assigned
        /// (the shop window carries ~475 graphics in the hardware log), and treating all of them as
        /// arrivals would (a) blanket-bake a panel the probe's 1.35 minification gate deliberately
        /// filters — the pressure that caused the 2026-08 count-budget regression — and, worse,
        /// (b) queue the ONE graphic the user is complaining about behind ~475 pieces of window
        /// chrome at <see cref="MaxArrivalBakesPerFrame"/> per frame, i.e. re-create the very delay
        /// this seam exists to remove. The initial population is the probe's job and it already does
        /// it. This watch owns ARRIVALS: art the game assigns AFTER the panel was first seen, and
        /// Images the game pools in later (the merchant hint's per-hover line objects).</summary>
        internal bool FirstCaptureDone;

        // Accumulated between log lines; reset when the line is emitted.
        internal int HiddenSwaps;
        internal int LiveSwaps;
        internal int WorstLiveFrames;
        internal int Deferred;
        internal float NextLogTime;
        internal bool EverLogged;
    }

    private static readonly List<ArrivalWatch> Watches = new(4);
    private static readonly Dictionary<int, ArrivalWatch> WatchByHost = new(4);
    private static readonly HashSet<int> LiveHosts = new(8);

    /// <summary>Source sprite ids this watch has already handed to the bake cache. A FIRST ask
    /// costs one of the per-frame bake slots because it may trigger an atlas readback; every later
    /// ask is a dictionary hit in the shared cache and is free. Mirrors
    /// <c>PanelSamplingProbe.Attempted</c> exactly, including its unbounded growth: the set holds
    /// ints for sprites the session has actually met, which the same probe already proves is a few
    /// hundred.</summary>
    private static readonly HashSet<int> Asked = new(128);

    /// <summary>Carry-over buffers used ONLY inside a re-capture, so an Image that survives the
    /// walk keeps its snapshot instead of being re-offered as a fresh arrival. Reused and cleared;
    /// no steady-state allocation.</summary>
    private static readonly Dictionary<int, Sprite?> CarrySeen = new(256);
    private static readonly Dictionary<int, int> CarryState = new(256);

    private static GameObject? s_pumpGo;
    private static bool s_pumpInstalled;
    private static bool s_pumpFailed;
    private static bool s_arrivalErrorLogged;
    private static bool s_panelCapLogged;
    private static bool s_rateCapLogged;
    private static int s_lastTickFrame = -1;

    /// <summary>
    /// Install the LateUpdate pump that drives <see cref="TickArrivals"/>, once per session.
    /// Idempotent and free after the first call (a managed bool test). Called from
    /// <see cref="Rescan"/> and from the shared bake cache's entry points, because the floated
    /// windows this seam exists for have NO per-frame owner in a file this lane owns.
    /// <para>DEGRADES SAFELY: if the GameObject cannot be created the class stands down for the
    /// whole session with ONE Warn naming the consequence -- <c>PanelSamplingProbe</c>'s 30-frame
    /// scan stays the only swap, which is exactly the behaviour of the build that shipped before
    /// this seam existed. Never throws.</para>
    /// </summary>
    internal static void EnsureArrivalWatch()
    {
        if (s_pumpInstalled || s_pumpFailed)
            return;
        // OFF means OFF: with the gate false this build must be indistinguishable from the one
        // before the seam existed. Nothing is installed and no arm line is printed. The check is
        // re-evaluated on every call, so turning the dial on mid-session still installs it — and
        // note the asymmetry, which is deliberate: turning it OFF again does NOT destroy the pump
        // object. TickArrivals' own gate stands every watch down and hands back every swapped
        // sprite on that edge, so the surviving GameObject is inert, and destroying it would give
        // up the free re-install that ArrivalPump.OnDestroy exists to keep.
        if (WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
            return;
        try
        {
            s_pumpGo = new GameObject("GloomhavenVR.PanelMipBakeArrivals")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            Object.DontDestroyOnLoad(s_pumpGo);
            s_pumpGo.AddComponent<ArrivalPump>();
            s_pumpInstalled = true;
            VRLog.Info("WorldUI", "MIP BAKE ARRIVAL watch armed (ModBuild 192). From now on a floated " +
                                  "panel's freshly assigned art is swapped for its mipmapped copy in the " +
                                  "SAME LateUpdate the game assigns it -- while the game's own image " +
                                  "loader still has the graphic disabled -- so the shop item card's FIRST " +
                                  "rendered frame is already the clean one. Read the MIP BAKE ARRIVAL " +
                                  "lines below for the per-panel evidence; 'hidden' swaps are the " +
                                  "zero-aliased-frame case and are the target.");
        }
        catch (System.Exception ex)
        {
            s_pumpFailed = true;
            s_pumpGo = null;
            // HW-VERIFY (2026-09 refactor, F-64) — the seam is off for the whole session
            // (s_pumpFailed), and the line's own text names the consequence the user sees.
            VRLog.Alert("WorldUI", $"MIP BAKE ARRIVAL watch could not be installed ({ex.GetType().Name}: " +
                                  $"{ex.Message}). THE CONSEQUENCE: floated panels keep the " +
                                  "PanelSamplingProbe's 30-frame scan as their only mip swap, so freshly " +
                                  "loaded art (the shop item card) is shown mipless for up to ~1 s before " +
                                  "it snaps clean -- exactly the pre-192 behaviour, never worse. Nothing " +
                                  "else changes and no further attempt is made this session.");
        }
    }

    /// <summary>
    /// The pump. A single hidden DontDestroyOnLoad component; LateUpdate because the game's image
    /// loader assigns sprites from Unity sync-context continuations that run in Update, and
    /// LateUpdate is after them and still before uGUI builds the canvas for this frame's eye passes.
    /// </summary>
    private sealed class ArrivalPump : MonoBehaviour
    {
        private void LateUpdate() => TickArrivals();

        private void OnDestroy()
        {
            // A ScriptEngine reload or a scene teardown can take the object; allow a re-install
            // rather than silently never running again.
            s_pumpInstalled = false;
            s_pumpGo = null;
        }
    }

    /// <summary>
    /// One frame of arrival handling across every floated panel. Fully guarded: an unguarded throw
    /// in a per-frame pump starves VR input, so every path here degrades to "the panel keeps the
    /// game's own sprite" and says so once.
    /// </summary>
    // INTERNAL since ModBuild 192: WorldUIModule's LateTick list drives this directly as the step
    // "PanelMipBake.Arrivals", which is the per-frame owner the floated windows never had. The
    // self-installed pump below stays for the scenario path; s_lastTickFrame makes a second call in
    // the same frame a no-op, so the two owners cannot double-swap.
    internal static void TickArrivals()
    {
        int frame = Time.frameCount;
        if (s_lastTickFrame == frame)
            return; // belt: two pumps can never double-charge the per-frame bake budget
        s_lastTickFrame = frame;

        if (WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
        {
            // OFF is exactly today's behaviour: stop watching and hand back everything this seam
            // swapped. The probe's own bake is gated on the identical entry, so nothing else is
            // holding a baked sprite on these panels.
            if (Watches.Count > 0)
                StandDownArrivals("[WorldUI] PanelMipBake turned off");
            return;
        }

        try
        {
            SyncWatches();
            PollWatches(frame);
        }
        catch (System.Exception ex)
        {
            if (!s_arrivalErrorLogged)
            {
                s_arrivalErrorLogged = true;
                // HW-VERIFY (2026-09 refactor, F-64) — latched by s_arrivalErrorLogged, once.
                VRLog.Alert("WorldUI", $"MIP BAKE ARRIVAL tick failed ({ex.GetType().Name}: {ex.Message}). " +
                                      "THE CONSEQUENCE: floated panels fall back to the " +
                                      "PanelSamplingProbe's 30-frame scan for their mip swaps, so freshly " +
                                      "loaded art is aliased for up to ~1 s again. Nothing was left " +
                                      "half-written -- every swap is a single sprite assignment -- and the " +
                                      "watch keeps trying on later frames.");
            }
        }
    }

    /// <summary>
    /// Reconcile the watch list against the live floated panels. New panels are registered with an
    /// immediate first capture; departed ones are RESTORED (originals back on the game's own window,
    /// which survives the host) and dropped. Cheap: the panel registry is a handful of entries.
    /// </summary>
    private static void SyncWatches()
    {
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        LiveHosts.Clear();
        for (int p = 0; p < panels.Count; p++)
        {
            ConvertedPanel panel = panels[p];
            if (panel == null || !panel.IsAlive || panel.HostGo == null || panel.Target == null)
                continue;
            int hostId = panel.HostGo.GetInstanceID();
            LiveHosts.Add(hostId);
            if (WatchByHost.ContainsKey(hostId))
                continue;
            if (Watches.Count >= MaxWatchedPanels)
            {
                if (!s_panelCapLogged)
                {
                    s_panelCapLogged = true;
                    // HW-VERIFY (2026-09 refactor, F-64) — latched by s_panelCapLogged, once.
                    VRLog.Note("WorldUI", $"MIP BAKE ARRIVAL: more than {MaxWatchedPanels} floated panel(s) " +
                                          $"at once -- '{panel.Target.name}' and any further one are NOT " +
                                          "arrival-watched. THE CONSEQUENCE for them is the pre-192 " +
                                          "behaviour: the PanelSamplingProbe's 30-frame scan swaps their " +
                                          "art, so freshly loaded art is aliased for up to ~1 s. The " +
                                          "already-watched panels are unaffected.");
                }
                continue;
            }
            var watch = new ArrivalWatch(panel.HostGo, panel.Target, panel.Target.name)
            {
                NextRecaptureFrame = Time.frameCount, // capture on this very poll
            };
            Watches.Add(watch);
            WatchByHost[hostId] = watch;
        }

        for (int i = Watches.Count - 1; i >= 0; i--)
        {
            ArrivalWatch watch = Watches[i];
            if (watch.Host != null && LiveHosts.Contains(watch.HostId))
                continue;
            // Gone home: full-restore contract. Idempotent with the probe's own RestoreDeparted --
            // whichever runs second finds the originals already in place and writes nothing.
            Restore(watch.Target);
            Watches.RemoveAt(i);
            WatchByHost.Remove(watch.HostId);
        }
    }

    /// <summary>Drop every watch and restore what it swapped (config off / hard stand-down).</summary>
    private static void StandDownArrivals(string why)
    {
        for (int i = 0; i < Watches.Count; i++)
            Restore(Watches[i].Target);
        VRLog.Info("WorldUI", $"MIP BAKE ARRIVAL stand-down ({why}): {Watches.Count} watched panel(s) " +
                              "restored to the game's own mipless graphics. This is the pre-192 state " +
                              "exactly -- freshly loaded art on a floated panel will again be aliased for " +
                              "up to ~1 s before the PanelSamplingProbe's 30-frame scan swaps it.");
        Watches.Clear();
        WatchByHost.Clear();
    }

    /// <summary>
    /// The per-frame body: at most ONE allocating re-capture, then an allocation-free reference
    /// compare over every watched Image, swapping the ones whose art the game just changed.
    /// </summary>
    private static void PollWatches(int frame)
    {
        int bakeBudget = MaxArrivalBakesPerFrame;
        bool recaptured = false;

        for (int w = 0; w < Watches.Count; w++)
        {
            ArrivalWatch watch = Watches[w];
            if (watch.Host == null)
                continue; // dropped on the next SyncWatches

            if (!recaptured && frame >= watch.NextRecaptureFrame)
            {
                recaptured = true;
                Recapture(watch);
                // Stagger the panels so their walks never collide on one frame.
                watch.NextRecaptureFrame = frame + RecaptureIntervalFrames + w;
            }

            Image[] images = watch.Images;
            Sprite?[] seen = watch.Seen;
            int[] state = watch.State;
            if (images.Length != seen.Length || images.Length != state.Length)
                continue; // a re-capture is mid-flight; next frame is consistent

            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img == null)
                    continue;

                Sprite? now = img.sprite;
                if (!ReferenceEquals(now, seen[i]))
                {
                    // ARRIVAL: the game assigned different art to this Image since the last frame.
                    if (now != null && !CardFaceMipBake.IsBakedSprite(now))
                    {
                        int spriteId = now.GetInstanceID();
                        bool firstAsk = !Asked.Contains(spriteId);
                        if (firstAsk && bakeBudget <= 0)
                        {
                            // Rate cap: leave seen[i] STALE so the next frame retries this exact
                            // graphic. Deferred, never dropped.
                            watch.Deferred++;
                            if (!s_rateCapLogged)
                            {
                                s_rateCapLogged = true;
                                VRLog.Info("WorldUI", "MIP BAKE ARRIVAL: the " +
                                                      $"{MaxArrivalBakesPerFrame}-bake-per-frame rate cap " +
                                                      "bound for the first time. Graphics over the cap are " +
                                                      "RETRIED on the next frame, so they are late by " +
                                                      "frames, not by the ~1 s scan period, and none is " +
                                                      "left mipless. The cap exists so a burst of first- " +
                                                      "sight atlases cannot stutter a VR frame; if the " +
                                                      "'deferred' count on the ARRIVAL lines below stays " +
                                                      "high, the cap -- not the seam -- is the bottleneck.");
                            }
                            continue;
                        }
                        if (firstAsk)
                        {
                            Asked.Add(spriteId);
                            bakeBudget--;
                        }

                        // Read BEFORE the swap: a graphic the loader has not enabled yet cannot have
                        // rendered, so this swap costs the player ZERO aliased frames. That is the
                        // whole point of the seam and it is what the log line reports.
                        bool renderable = img.isActiveAndEnabled;
                        Sprite? replacement = CardFaceMipBake.ReplacementFor(now);
                        if (replacement != null)
                        {
                            img.sprite = replacement;
                            if (renderable)
                            {
                                watch.LiveSwaps++;
                                // state[i] >= 0 is the frame this Image was first seen on screen
                                // carrying art we had not baked -- the aliased window, in frames.
                                int since = state[i];
                                int aliased = since >= 0 ? frame - since : 0;
                                if (aliased > watch.WorstLiveFrames)
                                    watch.WorstLiveFrames = aliased;
                            }
                            else
                            {
                                watch.HiddenSwaps++;
                            }
                        }
                    }
                    // Snapshot what the Image ACTUALLY carries now -- our replacement, or the
                    // original when the cache refused it. Either way it is asked about exactly once.
                    seen[i] = img.sprite;
                    state[i] = SlotSettled;
                    continue;
                }

                // Unchanged. Start (or keep) the aliased-window clock only while the slot has not
                // been settled -- that keeps the steady state to one reference compare plus one
                // integer test per Image.
                if (state[i] == SlotUnknown)
                {
                    if (now == null || CardFaceMipBake.IsBakedSprite(now))
                        state[i] = SlotSettled;
                    else if (img.isActiveAndEnabled)
                        state[i] = frame;
                }
            }

            ReportArrivals(watch, frame);
        }
    }

    /// <summary>
    /// Re-run the allocating hierarchy walk for ONE panel, carrying every surviving Image's
    /// snapshot across. An Image the walk meets for the FIRST time is entered with a null snapshot
    /// on purpose: the next poll then treats whatever art it is already carrying as an arrival and
    /// swaps it, which is what covers the hint's per-hover pooled line objects.
    /// </summary>
    private static void Recapture(ArrivalWatch watch)
    {
        CarrySeen.Clear();
        CarryState.Clear();
        Image[] old = watch.Images;
        Sprite?[] oldSeen = watch.Seen;
        int[] oldState = watch.State;
        if (old.Length == oldSeen.Length && old.Length == oldState.Length)
        {
            for (int i = 0; i < old.Length; i++)
            {
                Image img = old[i];
                if (img == null)
                    continue;
                int id = img.GetInstanceID();
                CarrySeen[id] = oldSeen[i];
                CarryState[id] = oldState[i];
            }
        }

        ImageScratch.Clear();
        watch.Host.GetComponentsInChildren(includeInactive: true, ImageScratch);
        int count = ImageScratch.Count;
        if (count > MaxWatchedImagesPerPanel)
        {
            count = MaxWatchedImagesPerPanel;
            if (!watch.CapLogged)
            {
                watch.CapLogged = true;
                // HW-VERIFY (2026-09 refactor, F-64) — latched per panel by watch.CapLogged.
                VRLog.Note("WorldUI", $"MIP BAKE ARRIVAL on '{watch.Name}': {ImageScratch.Count} Image(s) " +
                                      $"exceeds the {MaxWatchedImagesPerPanel} arrival-watch cap. THE " +
                                      "CONSEQUENCE: the graphics past the cap are not arrival-swapped and " +
                                      "fall back to the PanelSamplingProbe's 30-frame scan (aliased for up " +
                                      "to ~1 s after their art loads). Everything under the cap is " +
                                      "unaffected. The cap is a runaway guard, not a budget -- a panel this " +
                                      "large means the hierarchy assumption is wrong, not that VRAM is short.");
            }
        }

        if (watch.Images.Length != count)
        {
            watch.Images = new Image[count];
            watch.Seen = new Sprite?[count];
            watch.State = new int[count];
        }
        Image[] images = watch.Images;
        Sprite?[] seen = watch.Seen;
        int[] state = watch.State;
        for (int i = 0; i < count; i++)
        {
            Image img = ImageScratch[i];
            images[i] = img;
            if (img != null && CarrySeen.TryGetValue(img.GetInstanceID(), out Sprite? prev))
            {
                seen[i] = prev;
                state[i] = CarryState.TryGetValue(img.GetInstanceID(), out int prevState)
                    ? prevState
                    : SlotUnknown;
            }
            else if (watch.FirstCaptureDone)
            {
                // The game POOLED this Image in since the last capture (the merchant hint builds
                // its line objects per hover). A null snapshot means "its current art is an
                // arrival", so the next poll swaps it.
                seen[i] = null;
                state[i] = SlotUnknown;
            }
            else
            {
                // Panel's FIRST capture: record only — see ArrivalWatch.FirstCaptureDone for why
                // this must not swap. The aliased-window clock still starts, so the log line below
                // still reports it if the probe turns out to be late on this population.
                seen[i] = img != null ? img.sprite : null;
                state[i] = SlotUnknown;
            }
        }
        watch.FirstCaptureDone = true;
        ImageScratch.Clear();
        CarrySeen.Clear();
        CarryState.Clear();
    }

    /// <summary>
    /// THE LINE THAT LETS THE NEXT ROUND BE JUDGED. It answers, for the surfaces the user reported,
    /// how many frames passed between a graphic being on screen and it sampling a MIPPED texture.
    /// ZERO IS THE TARGET and the line says so. Throttled per panel, but the counters accumulate
    /// across the interval, so no evidence is thrown away.
    /// </summary>
    private static void ReportArrivals(ArrivalWatch watch, int frame)
    {
        int total = watch.HiddenSwaps + watch.LiveSwaps;
        if (total == 0 && watch.Deferred == 0)
            return;
        float now = Time.unscaledTime;
        if (watch.EverLogged && now < watch.NextLogTime)
            return;
        watch.EverLogged = true;
        watch.NextLogTime = now + ArrivalLogIntervalSeconds;

        float ms = watch.WorstLiveFrames * Time.unscaledDeltaTime * 1000f;
        VRLog.Info("WorldUI",
            $"MIP BAKE ARRIVAL on '{watch.Name}' (frame {frame}): {total} graphic(s) swapped to a mipmapped " +
            $"copy — {watch.HiddenSwaps} while the graphic was still HIDDEN (the game's image loader had " +
            $"not enabled it yet) and {watch.LiveSwaps} while it was already on screen; worst on-screen wait " +
            $"{watch.WorstLiveFrames} frame(s) (~{ms:F0} ms); {watch.Deferred} deferred by the " +
            $"{MaxArrivalBakesPerFrame}-bake/frame rate cap. Budget now {CardFaceMipBake.BudgetSummary}. " +
            "READ IT LIKE THIS. A HIDDEN swap costs the player ZERO aliased frames — the art's FIRST " +
            "rendered frame is already the mipmapped copy — and that is the target for the merchant's item " +
            "card, whose art arrives async with the Image disabled. 'worst on-screen wait 0' means every " +
            "live swap also landed before the graphic could render with mipless art. A worst of 1-2 frames " +
            "means the game assigned that art AFTER this LateUpdate (a coroutine or end-of-frame writer) — " +
            "annoying but invisible. A worst near 30 frames, or a HIDDEN count of 0 with a large LIVE count, " +
            "means the arrival was MISSED and PanelSamplingProbe's 30-frame scan did the swap instead: that " +
            "is the ~1 s aliased window the user reported, and the cause is either a graphic created after " +
            "the last re-capture or a panel that is not arrival-watched (look for the cap warnings above). " +
            "A persistently large 'deferred' count means the rate cap, not the seam, is the bottleneck — " +
            "raise MaxArrivalBakesPerFrame only if the Perf SPIKE lines show no new hitch. AND IF IT NOW " +
            "HITCHES: a new SPIKE frame whose worst step is WorldUI and which lands on the same frame as an " +
            "ARRIVAL line with a first-sight 4096-square atlas in the MIP BAKE lines above is this seam's " +
            "readback — lower MaxArrivalBakesPerFrame to 1.");

        watch.HiddenSwaps = 0;
        watch.LiveSwaps = 0;
        watch.WorstLiveFrames = 0;
        watch.Deferred = 0;
    }
}

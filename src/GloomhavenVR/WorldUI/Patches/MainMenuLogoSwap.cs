using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// The main menu wears the mod's own wordmark.
///
/// <para><b>USER REQUEST (2026-08-22, verbatim):</b> "5) Ich hab dir ein 'GloomhavenVR' Logo
/// abgelegt (.debug/ressources/GloomhavenVR_logo.png) - ich möchte von dir, dass du im Hauptmenu
/// das original Logo damit ersetzt (so das es genauso aussieht wie jetzt nur mit dem neuen
/// Logo)."</para>
///
/// <para><b>WHERE THE OBJECT IS.</b> The main menu is the <c>Gloomhaven_unified</c> scene
/// (<c>Bootstrap.cs:59</c> — <c>string sceneName = "Gloomhaven_unified";</c>). Its screen owner is
/// <c>GLOOM.MainMenu.MainMenuUIManager</c> (<c>MainMenuUIManager.cs:20</c>), and the title
/// wordmark hangs off exactly one field: <c>[SerializeField] private GameObject _logo</c>
/// (<c>MainMenuUIManager.cs:61-62</c>), exposed as <c>public GameObject Logo => _logo</c>
/// (<c>:78</c>). It is a plain <c>GameObject</c> — there is NO C# handle on the sprite anywhere in
/// GH.Runtime, so the <see cref="Image"/> has to be resolved off that object at runtime. The only
/// consumer in the whole assembly is <c>CompendiumWindow.OnShown/OnHidden</c>
/// (<c>CompendiumWindow.cs:155-166</c> and <c>:192-202</c>), which does nothing but
/// <c>SetActive(false/true)</c> — no tween, no fade, no material swap. A second, ambiguous copy
/// exists on <c>BackgroundView.m_Logo</c> (<c>BackgroundView.cs:12</c>, reachable through
/// <c>LogoSetActive</c> at <c>:23-26</c>, which has zero callers in GH.Runtime and is therefore
/// either scene-wired via a UnityEvent or dead); this patch swaps it too when it resolves to a
/// DIFFERENT object, because two copies of the same wordmark on one screen must not disagree.
///
/// <para><b>WHAT IS NOT THE TARGET.</b> <c>IntroPlayer</c> (<c>IntroPlayer.cs:18-49</c>, the
/// <c>Image _logo</c> / <c>List&lt;Sprite&gt; _logos</c> / <c>Sprite _unityLogo</c> with the
/// LeanTween alpha+scale <c>ShowLogos</c> coroutine) is the publisher/engine splash in the
/// <c>Intro</c> scene. Those are other companies' marks; replacing them was neither asked for nor
/// defensible. Untouched.</para>
///
/// <para><b>HOW "GENAUSO WIE JETZT" IS HELD.</b> This is a <i>sprite assignment on the existing
/// graphic</i>, never a new GameObject: the RectTransform, its anchors, its parent canvas and
/// sorting order, the <c>Image.color</c> (so any CanvasGroup / colour fade still drives it), the
/// material and every animator on the object all stay exactly as the scene authored them.</para>
///
/// <para><b>THE RECT IS RESIZED, AND ModBuild 220 GOT THIS WRONG.</b> 220 kept the authored rect
/// and set <c>preserveAspect</c>, which fits the sprite INSIDE it — so the whole wordmark shrank to
/// make room for the VR. USER CORRECTION (2026-08-22, verbatim): <i>"Das Logo ist ja das originale
/// Logo nur mit der 'VR' ergänzung - Es sollte daher die selben Seitenvehältnisse haben bzw nur
/// leicht breiter da das VR breiter ist. Aber der 'Gloomhaven' Schriftzug sollte 1:1 genau an der
/// Stelle sein, an dem das originale Logo war."</i> The artwork is the game's own wordmark with VR
/// appended, so GLOOMHAVEN must keep the original's size and position and the VR must hang past it.
/// <see cref="MainMenuLogoPlacement.PlaceOnBand"/> therefore GROWS the rect so that the sprite's
/// GLOOMHAVEN band comes out at the WIDTH the old wordmark's own letters had, and offsets it so the
/// band's left edge and vertical centre land on theirs; the sprite is never stretched, because the
/// rect is made exactly the sprite's own aspect.</para>
///
/// <para><b>AND IT TOOK THREE ROUNDS, EACH FAILING THE SAME WAY.</b> 220 fitted the sprite inside
/// the rect (wordmark too small); 221 matched the rect's height (2.35x too wide); 222 matched the
/// old sprite's INK height — but the game's asset carries a broad soft outer glow that the artist's
/// file, flattened onto black, does not, so its ink box is ~28 % taller than its letters while ours
/// is ~3 % taller than ours. On identical artwork every one of those looks to the eye like "nothing
/// changed", which is exactly what the user reported three times. The placement now matches on
/// WIDTH, where the glow is a ~3 % effect instead of a ~28 % one, and
/// <see cref="MainMenuLogoPlacement.MeasureInk"/> no longer guesses the contour at all: it SWEEPS
/// the old asset's alpha contours and keeps the one whose box has our band's aspect. Every number is
/// logged, so the result is judged from a Player.log rather than only from a photograph.</para>
///
/// <para><b>ROUND FIVE, AND THE PREMISE OF THE FIRST FOUR IS NOW EXHAUSTED.</b> USER REPORT
/// (2026-08-22, verbatim, the fourth time): <i>"Das Logo ist immer noch das selbe, Problem nicht
/// behoben."</i> — with <c>.planning/debug/logo.jpg</c>, which shows GLOOMHAVEN and no VR anywhere.
/// But ModBuild 223's own log line says the swap SUCCEEDED and every number in it re-derives:
/// <c>old INK box 903.3x123.9 → rect 1109.0x193.9 … Shape check old/band 7.293/7.008 = 1.041:
/// CONSISTENT … No RectMask2D/Mask anywhere above it</c>. So the placement arithmetic is no longer
/// a plausible cause and IT IS NOT RE-TUNED HERE. Two other things can be true, and this round
/// exists to separate them <i>from a log line rather than from a photograph</i>:
/// <list type="bullet">
///   <item><description><b>(A) the wordmark the player sees is a DIFFERENT graphic than the one we
///   swapped.</b> Nothing had ever enumerated the scene — we only ever looked under
///   <c>MainMenuUIManager._logo</c> (null in that log) and under the FIRST <c>BackgroundView</c>
///   whose <c>m_Logo</c> answered. <see cref="MainMenuLogoPlacement.Census"/> now lists every
///   <see cref="Graphic"/> in the scene, and <see cref="SweepScene"/> swaps every one of them that
///   carries the original artwork.</description></item>
///   <item><description><b>(B) our sprite is assigned but not drawn</b> — clipped, occluded,
///   re-laid-out or reverted. <see cref="MainMenuLogoPlacement.MeasureDrawnInk"/> reads the
///   rendered picture back and reports the ink in the GLOOMHAVEN band and in the VR box
///   separately, and <see cref="MainMenuLogoPlacement.WatchRoutine"/> re-asserts the swap for a
///   bounded window.</description></item>
/// </list></para>
///
/// <para><b>WHAT THE DECOMPILED SOURCE ALREADY SETTLES ABOUT (B).</b> There is exactly one
/// <c>.sprite =</c> assignment to any logo in the whole of GH.Runtime and it is
/// <c>IntroPlayer.cs:88</c>, in the <c>Intro</c> scene, on the publisher splash. <c>BackgroundView</c>
/// touches <c>m_Logo</c> only through <c>SetActive</c> (<c>BackgroundView.cs:23-26</c>) and
/// <c>CompendiumWindow</c> only through <c>SetActive</c> as well. <b>No game code re-assigns the
/// main-menu wordmark's sprite.</b> The re-assert watch below therefore expects to find nothing —
/// and it is shipped anyway, because it also catches the OTHER half of (B) that source cannot rule
/// out: a layout component rewriting the rect we sized.</para>
///
/// <para><b>WHERE THE BYTES LIVE — and why not the bundle.</b> The PNG ships as an
/// <c>EmbeddedResource</c> inside the plugin DLL (<c>GloomhavenVR.Assets.GloomhavenVR_logo.png</c>,
/// declared in <c>GloomhavenVR.csproj</c>) and is turned into a texture at runtime with
/// <c>Texture2D.LoadImage</c>. The asset bundle would have been the natural home for art, and was
/// REJECTED: it has been byte-identical at 70,218,494 bytes since ModBuild 172 and every install
/// since has been plugin-DLL-only, so rebuilding it would cost the user a full reinstall for a
/// logo. This is the first <c>EmbeddedResource</c> in the project; the route was already weighed
/// once and turned down for AUDIO in <c>Core/EnvSound.Bank.cs:79</c>, for reasons that do not
/// apply here — a PNG needs no hand-written decoder, because Unity decodes a <c>byte[]</c> into a
/// <c>Texture2D</c> directly.</para>
///
/// <para><b>ALSO REJECTED:</b> (a) re-skinning by hunting the sprite by NAME through
/// <c>Resources.FindObjectsOfTypeAll&lt;Sprite&gt;()</c> — the same art may be atlased and shared,
/// and a name match is not identity; (b) instantiating our own Image next to the original and
/// disabling it — a new object inherits none of the scene's layout or fade wiring, which is
/// exactly what "genauso wie jetzt" forbids; (c) fitting the sprite inside the authored rect
/// (what 220 shipped) — it shrinks the wordmark, which is the correction above.</para>
///
/// <para><b>MULTIPLAYER:</b> presentation only. No wire traffic, no session state read or written,
/// and the swap is identical for host and client. Desktop-flat play with the mod installed simply
/// also shows the VR wordmark, which is the point of the request.</para>
/// </para></summary>
[HarmonyPatch]
internal static class MainMenuLogoSwap
{
    private const string Scope = "MenuLogo";

    /// <summary>Logical name set explicitly in the csproj, so a folder rename cannot silently
    /// break the lookup. <see cref="LoadSprite"/> still falls back to a suffix scan.</summary>
    private const string ResourceName = "GloomhavenVR.Assets.GloomhavenVR_logo.png";

    /// <summary>Built once per process and kept alive by this reference alone — the texture is
    /// <c>HideAndDontSave</c> so the scene unload behind every return-to-menu cannot collect it.
    /// </summary>
    private static Sprite? s_sprite;

    /// <summary>Set when the resource could not be decoded, so the warning is emitted once and
    /// the failed decode is not retried on every menu load.</summary>
    private static bool s_loadFailed;

    /// <summary>Every graphic this menu load swapped, with the rect it had BEFORE we touched it.
    /// Cleared at the top of every <see cref="Apply"/> because a fresh load of
    /// <c>Gloomhaven_unified</c> re-instantiates every one of them, so the old entries are dead
    /// references. The snapshot is what makes the re-assert idempotent: a revert is repaired by
    /// restoring the authored rect first and then running the identical placement, never by
    /// re-applying the growth on top of an already-grown rect.</summary>
    private static readonly List<MainMenuLogoPlacement.SwapRecord> s_records = new();

    /// <summary>The artwork we replaced, learned from the swap itself rather than guessed. This is
    /// what <see cref="SweepScene"/> matches against, so "every instance of the original logo" is
    /// decided by asset identity and not by a name that could belong to anything.</summary>
    private static readonly List<Sprite> s_originalSprites = new();

    /// <summary>Same, for the <see cref="RawImage"/> case where there is no sprite at all.</summary>
    private static readonly List<Texture> s_originalTextures = new();

    /// <summary>What this menu load swapped, for the instruments and the watch to read.</summary>
    internal static IReadOnlyList<MainMenuLogoPlacement.SwapRecord> Records => s_records;

    // =============================================================================================
    //  THE SEAM
    // =============================================================================================

    /// <summary>
    /// Postfix on <c>MainMenuUIManager.Awake</c> (<c>MainMenuUIManager.cs:79-86</c>, private —
    /// hence the string target). <c>_logo</c> is a serialized scene reference, so it is already
    /// wired by the time Awake runs, and Awake runs again on every fresh load of
    /// <c>Gloomhaven_unified</c> (returning to the menu from a scenario), which is exactly the
    /// cadence the swap needs. Postfix and not prefix: <c>Awake</c> sets the static
    /// <c>Instance</c>, and running after it keeps the game's own bookkeeping first.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainMenuUIManager), "Awake")]
    private static void MainMenuUIManager_Awake_Postfix(MainMenuUIManager __instance)
    {
        try
        {
            Apply(__instance);
        }
        catch (Exception ex)
        {
            // A cosmetic swap must never take the main menu down with it.
            VRLog.Error(Scope, $"Main-menu logo swap threw and was swallowed: {ex}");
        }
    }

    private static void Apply(MainMenuUIManager manager)
    {
        Sprite? sprite = LoadSprite();
        if (sprite == null)
            return; // LoadSprite already said why, loudly

        // A fresh load of Gloomhaven_unified re-instantiates every graphic in it, so last visit's
        // records and originals are dead references. Start clean or the sweep will match nothing
        // and the watch will chase destroyed objects.
        s_records.Clear();
        s_originalSprites.Clear();
        s_originalTextures.Clear();

        int swapped = 0;
        GameObject? primary = manager != null ? manager.Logo : null;
        swapped += SwapUnder(primary, "MainMenuUIManager._logo", sprite);

        // EVERY BackgroundView, not the first one that answered. Until ModBuild 226 this took the
        // first component whose m_Logo was non-null and stopped — which is a silent bet that the
        // scene contains exactly one, and nothing had ever checked that bet.
        var hosts = new List<GameObject>();
        int views = CollectBackgroundViewLogos(hosts);
        int secondaries = 0;
        for (int i = 0; i < hosts.Count; i++)
        {
            if (hosts[i] == primary)
                continue;
            secondaries++;
            swapped += SwapUnder(hosts[i], $"BackgroundView#{i}.m_Logo", sprite);
        }
        VRLog.Info(Scope,
            $"BackgroundView census: {views} component(s) live in a real scene, {hosts.Count} of them " +
            $"carry a non-null m_Logo, {secondaries} were swapped as secondaries. Before ModBuild 226 " +
            "only the FIRST of these was ever looked at.");

        // FIX 1 — every instance of the original artwork, not just the one the field pointed at.
        swapped += SweepScene(sprite, "at Awake");

        if (swapped == 0)
        {
            VRLog.Warn(Scope,
                "MAIN-MENU LOGO NOT SWAPPED — no Image/RawImage carrying artwork was found under " +
                $"MainMenuUIManager._logo (host={Describe(primary)}) or under any of the {hosts.Count} " +
                "BackgroundView.m_Logo host(s), and the scene sweep had no original artwork to match " +
                "against. The menu still shows the GAME's wordmark. The subtree dump above says what " +
                "is actually on those objects; the resolver takes the largest sprite-bearing graphic, " +
                "so a host built from a Text/TMP object or a spriteless Image will land here. THE " +
                "SCENE CENSUS BELOW IS THE EVIDENCE — it names every graphic in the scene.");
        }
        else
        {
            VRLog.Note(Scope, $"Main-menu logo replaced with the GloomhavenVR wordmark ({swapped} graphic(s)).");
        }

        // Instruments and the watch. Everything past this point needs a MonoBehaviour to run a
        // coroutine on and a rendered frame to look at; the manager is the natural host because its
        // lifetime is exactly the main menu's.
        MainMenuLogoPlacement.ArmWatch(manager, sprite, s_records.Count);
    }

    /// <summary>Called by the watch coroutine. Kept here because it owns <see cref="s_records"/>.
    /// </summary>
    internal static void RunWatchTick(Sprite ours, bool deepSweep, out int repaired, out int swept)
    {
        repaired = 0;
        swept = 0;

        foreach (MainMenuLogoPlacement.SwapRecord rec in s_records)
        {
            if (rec.Graphic == null)
                continue;
            string? why = rec.WhatChanged(ours);
            if (why == null)
                continue;

            // Log ONCE per record, however many times it has to be repaired: a graphic that is
            // rewritten every frame would otherwise bury the log, and the first line already says
            // everything the next round needs.
            if (!rec.RepairLogged)
            {
                rec.RepairLogged = true;
                VRLog.Warn(Scope,
                    $"RE-ASSERT FIRED on '{rec.Origin}' ({rec.Path}): {why}. Something outside this " +
                    "patch is rewriting the logo after our Awake postfix ran — that is hypothesis (B), " +
                    "and this line is the proof. The authored rect is restored and the identical " +
                    "placement re-applied; further repairs on this graphic are silent.");
            }

            rec.RestoreRect();
            if (ApplyTo(rec.Graphic, ours, rec.Origin + " (re-assert)", rec) != null)
                repaired++;
        }

        // Quiet on purpose: the watch runs this fifteen times per menu visit and an unconditional
        // line each time would bury the four that matter. It is NOT a silent scan — the watch's
        // closing line reports how many of these ran and what they found, which is the same
        // guarantee for a fifteenth of the log.
        if (deepSweep)
            swept = SweepScene(ours, "watch tick", verbose: false);
    }

    // =============================================================================================
    //  RESOLUTION — by structure, never by a hard-coded child path
    // =============================================================================================

    /// <summary>
    /// Swaps the artwork on the single largest sprite-bearing graphic under <paramref name="host"/>
    /// (inactive children included — <c>CompendiumWindow</c> may have the whole object switched
    /// off when we run). "Largest" is the RectTransform's own area: a wordmark host that also
    /// carries a small flourish or a drop-shadow copy must not have the flourish overwritten with
    /// a second wordmark. Every candidate is logged either way, so a wrong pick is visible in the
    /// log rather than only in the headset.
    /// </summary>
    /// <returns>1 if a graphic was swapped, 0 otherwise.</returns>
    private static int SwapUnder(GameObject? host, string origin, Sprite sprite)
    {
        if (host == null)
        {
            VRLog.Warn(Scope, $"{origin} is null — nothing to swap there.");
            return 0;
        }

        Graphic? best = null;
        float bestArea = -1f;
        int candidates = 0;
        var seen = new StringBuilder(256);

        foreach (Image img in host.GetComponentsInChildren<Image>(includeInactive: true))
        {
            float area = Area(img.rectTransform);
            Sprite? art = img.sprite;
            bool carries = art != null;
            Append(seen, img.name, "Image", area, art != null ? art.name : "<no sprite>");
            if (!carries)
                continue;
            candidates++;
            if (area > bestArea) { bestArea = area; best = img; }
        }

        foreach (RawImage raw in host.GetComponentsInChildren<RawImage>(includeInactive: true))
        {
            float area = Area(raw.rectTransform);
            Texture? art = raw.texture;
            bool carries = art != null;
            Append(seen, raw.name, "RawImage", area, art != null ? art.name : "<no texture>");
            if (!carries)
                continue;
            candidates++;
            if (area > bestArea) { bestArea = area; best = raw; }
        }

        VRLog.Info(Scope, $"{origin} → '{host.name}' graphics: {(seen.Length == 0 ? "<none>" : seen.ToString())}");

        if (best == null)
            return 0;

        if (candidates > 1)
        {
            VRLog.Info(Scope,
                $"{origin} carries {candidates} art-bearing graphics; swapping only the largest " +
                $"('{best.name}', {bestArea:F0} px²) and leaving the rest as authored. If the menu " +
                "ends up showing a leftover piece of the OLD wordmark, that list is where it is.");
        }

        return ApplyTo(best, sprite, origin, null) != null ? 1 : 0;
    }

    /// <summary>
    /// Put our artwork on one graphic and place it.
    ///
    /// <para><paramref name="reuse"/> is null for a first swap — a <see cref="MainMenuLogoPlacement.SwapRecord"/>
    /// is then created, which snapshots the authored rect, and it is filed in <see cref="s_records"/>.
    /// It is non-null for a RE-ASSERT, where the caller has already restored the authored rect from
    /// that same snapshot: measuring the old artwork against a rect we had already grown would
    /// compound the growth on every repair, which is the one way this fix could make things worse.</para>
    /// </summary>
    /// <returns>The record for the swapped graphic, or null if nothing was swapped.</returns>
    internal static MainMenuLogoPlacement.SwapRecord? ApplyTo(
        Graphic graphic, Sprite sprite, string origin, MainMenuLogoPlacement.SwapRecord? reuse)
    {
        MainMenuLogoPlacement.SwapRecord record = reuse ?? new MainMenuLogoPlacement.SwapRecord(graphic, origin);
        var rect = graphic.rectTransform.rect;
        float hostAspect = rect.height > 0.001f ? rect.width / rect.height : 0f;
        float newAspect = sprite.rect.height > 0.001f ? sprite.rect.width / sprite.rect.height : 0f;

        if (graphic is Image image)
        {
            if (ReferenceEquals(image.sprite, sprite))
            {
                // Already ours — never grow the rect twice. Not an error and not a swap.
                VRLog.Info(Scope, $"{origin}: '{image.name}' already carries our wordmark; left alone.");
                return null;
            }

            RememberOriginal(image.sprite, null);
            string wasSprite = image.sprite != null ? image.sprite.name : "<none>";
            Rect wasRect = image.sprite != null ? image.sprite.rect : default;
            Image.Type wasType = image.type;
            bool wasPreserve = image.preserveAspect;

            // WHERE THE OLD WORDMARK ACTUALLY DREW, in this RectTransform's own local space, and
            // then WHERE THE INK INSIDE IT WAS. Both steps are needed and ModBuild 221 shipped only
            // the first: the game's sprite is 2048x582 at aspect 3.519 while the wordmark drawn on
            // it is roughly aspect 7.4, i.e. the asset is mostly transparent margin. Treating the
            // drawn box as the wordmark scaled our band to the MARGIN's height and produced a logo
            // 2.35x too wide — which is exactly what the hardware photograph shows.
            Rect oldBox = MainMenuLogoPlacement.DrawnBox(rect, wasRect, wasPreserve, wasType);
            Rect inkFrac = MainMenuLogoPlacement.MeasureInk(image.sprite, out string inkNote);
            Rect oldInk = MainMenuLogoPlacement.SubBox(oldBox, inkFrac);

            image.sprite = sprite;
            // Unity's overrideSprite GETTER falls back to `sprite`, so an active override is
            // invisible to a null test and would silently keep drawing the old art. Clearing it is
            // safe here: every overrideSprite writer in GH.Runtime is an interactive-widget
            // transition helper (UITab.cs:272, UISelectField.cs:529, UISelectField_Option.cs:189,
            // UISelectField_Arrow.cs:123, UIButtonExtended_Target.cs:270,
            // UIInputFieldLayoutExtension.cs:239, UIHighlightTransition.cs:239) — none of them is
            // a static logo.
            image.overrideSprite = null;
            // Simple + preserveAspect OFF: the rect is about to be made exactly the sprite's own
            // aspect, so the sprite fills it with no letterboxing and rect == drawn box. Letting
            // preserveAspect fit inside the rect is what shrank the wordmark in ModBuild 220.
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.SetAllDirty();

            string placement = MainMenuLogoPlacement.PlaceOnBand(image, oldInk, newAspect, origin);

            VRLog.Info(Scope,
                $"{origin}: Image '{image.name}' sprite '{wasSprite}' " +
                $"({wasRect.width:F0}x{wasRect.height:F0}, aspect {(wasRect.height > 0.001f ? wasRect.width / wasRect.height : 0f):F3}) " +
                $"→ '{sprite.name}' ({sprite.rect.width:F0}x{sprite.rect.height:F0}, aspect {newAspect:F3}); " +
                $"old drawn box {oldBox.width:F1}x{oldBox.height:F1} at ({oldBox.xMin:F1},{oldBox.yMin:F1}) " +
                $"(preserveAspect was {wasPreserve}, type {wasType}); {inkNote} → old INK box " +
                $"{oldInk.width:F1}x{oldInk.height:F1} at ({oldInk.xMin:F1},{oldInk.yMin:F1}), aspect " +
                $"{(oldInk.height > 0.001f ? oldInk.width / oldInk.height : 0f):F3}. {placement} " +
                $"Colour, material, parent, canvas order and every sibling untouched. Path: {record.Path}");
            record.NoteApplied();
            if (reuse == null)
                s_records.Add(record);
            return record;
        }

        if (graphic is RawImage rawImage)
        {
            if (ReferenceEquals(rawImage.texture, sprite.texture))
            {
                VRLog.Info(Scope, $"{origin}: '{rawImage.name}' already carries our wordmark; left alone.");
                return null;
            }

            RememberOriginal(null, rawImage.texture);
            string wasTex = rawImage.texture != null ? rawImage.texture.name : "<none>";
            rawImage.texture = sprite.texture;
            rawImage.SetAllDirty();
            // RawImage has no preserveAspect. Shrink the rect to our aspect ONLY when the anchors
            // are not stretched (sizeDelta is then the real size and the pivot keeps it centred);
            // a stretched rect is driven by its parent and must not be fought.
            RectTransform rt = rawImage.rectTransform;
            bool stretched = rt.anchorMin != rt.anchorMax;
            if (!stretched && newAspect > 0.001f && hostAspect > 0.001f && rect.width > 0.001f)
            {
                float w = Mathf.Min(rect.width, rect.height * newAspect);
                rt.sizeDelta = new Vector2(w, w / newAspect);
            }
            VRLog.Warn(Scope,
                $"{origin}: the logo graphic is a RawImage ('{rawImage.name}'), texture '{wasTex}' → " +
                $"'{sprite.texture.name}'. RawImage cannot preserve aspect on its own, so the rect was " +
                $"{(stretched ? "LEFT ALONE (stretched anchors) and the artwork may be distorted" : "fitted to aspect " + newAspect.ToString("F3"))}. " +
                $"Worth a look in the headset. Path: {record.Path}");
            record.NoteApplied();
            if (reuse == null)
                s_records.Add(record);
            return record;
        }

        VRLog.Warn(Scope, $"{origin}: '{graphic.name}' is a {graphic.GetType().Name}, which this patch cannot skin.");
        return null;
    }

    // =============================================================================================
    //  FIX 1 — SWAP EVERY INSTANCE, NOT ONE
    // =============================================================================================

    /// <summary>
    /// Remember the artwork we are replacing, so the sweep can recognise other copies of it. Learned
    /// from the swap rather than hard-coded: a name is not identity, and the point of the sweep is
    /// to find the SAME asset elsewhere, not anything called "logo".
    /// </summary>
    private static void RememberOriginal(Sprite? sprite, Texture? texture)
    {
        if (sprite != null && !s_originalSprites.Contains(sprite))
            s_originalSprites.Add(sprite);
        if (texture != null && !s_originalTextures.Contains(texture))
            s_originalTextures.Add(texture);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is the artwork we replaced.
    ///
    /// <para>Three ways in, in decreasing strength. Reference identity is the only one that is
    /// certain. Same texture object plus same sprite name catches an atlas region referenced through
    /// a second <see cref="Sprite"/> instance. Same sprite name plus same texture NAME catches the
    /// case where a second copy of the asset was loaded into a second texture — which is why the
    /// name is never used on its own: "Logo" on an unrelated texture is not our asset.</para>
    /// </summary>
    private static bool IsOriginalArt(Sprite? candidate)
    {
        if (candidate == null)
            return false;
        foreach (Sprite original in s_originalSprites)
        {
            if (ReferenceEquals(candidate, original))
                return true;
            if (original == null || candidate.name != original.name)
                continue;
            if (ReferenceEquals(candidate.texture, original.texture))
                return true;
            if (candidate.texture != null && original.texture != null
                && candidate.texture.name == original.texture.name)
                return true;
        }
        return false;
    }

    private static bool IsOriginalArt(Texture? candidate)
    {
        if (candidate == null)
            return false;
        foreach (Texture original in s_originalTextures)
        {
            if (ReferenceEquals(candidate, original))
                return true;
            if (original != null && candidate.name == original.name)
                return true;
        }
        // A RawImage may legitimately be showing the texture behind the Image's sprite.
        foreach (Sprite original in s_originalSprites)
        {
            if (original != null && ReferenceEquals(candidate, original.texture))
                return true;
        }
        return false;
    }

    private static bool AlreadySwapped(Graphic graphic)
    {
        foreach (MainMenuLogoPlacement.SwapRecord rec in s_records)
        {
            if (ReferenceEquals(rec.Graphic, graphic))
                return true;
        }
        return false;
    }

    /// <summary>
    /// THE FIX FOR HYPOTHESIS (A). Every <see cref="Image"/>/<see cref="RawImage"/> anywhere in a
    /// real scene that still carries the artwork we replaced gets the same treatment and the same
    /// placement arithmetic, each logged by its full hierarchy path.
    ///
    /// <para>The <c>BackgroundView.m_Logo</c> resolution above stays the PRIMARY — it is the one
    /// with a named field behind it and the one whose subtree is dumped — and this sweep reports
    /// separately whatever the primary did not reach. Rounds one to four could only ever have
    /// swapped one graphic, and nothing had ever established that there is only one; if the menu
    /// draws the wordmark from a second Image, or a loading screen draws it again, that is the
    /// user's branding too and swapping it is correct rather than a side effect.</para>
    ///
    /// <para><c>Resources.FindObjectsOfTypeAll</c> and not <c>FindObjectsOfType</c>: an inactive
    /// object is exactly the case the latter cannot see, and a menu that is switched off when
    /// <c>Awake</c> runs is the normal state here. Prefab and asset instances are filtered out by
    /// requiring a real scene, because writing to a loaded prefab would leak across scene loads.</para>
    /// </summary>
    /// <returns>How many graphics this sweep swapped that were not swapped already.</returns>
    internal static int SweepScene(Sprite ours, string when, bool verbose = true)
    {
        if (s_originalSprites.Count == 0 && s_originalTextures.Count == 0)
        {
            if (verbose)
                VRLog.Warn(Scope,
                    $"SCENE SWEEP ({when}) DID NOT RUN — nothing was swapped by the field resolution, " +
                    "so there is no original artwork to match other copies against. This is not the " +
                    "sweep failing; it is the primary resolution having found no logo at all. The " +
                    "scene census is the evidence for what is actually in the scene.");
            return 0;
        }

        int swapped = 0;
        int inspected = 0;
        var hits = new StringBuilder(256);

        foreach (Image img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || !img.gameObject.scene.IsValid())
                continue;
            inspected++;
            if (!IsOriginalArt(img.sprite) || AlreadySwapped(img))
                continue;
            string path = MainMenuLogoPlacement.PathOf(img.transform);
            if (hits.Length > 0) hits.Append(" | ");
            hits.Append(path);
            if (ApplyTo(img, ours, $"scene sweep {when} → {path}", null) != null)
                swapped++;
        }

        foreach (RawImage raw in Resources.FindObjectsOfTypeAll<RawImage>())
        {
            if (raw == null || !raw.gameObject.scene.IsValid())
                continue;
            inspected++;
            if (!IsOriginalArt(raw.texture) || AlreadySwapped(raw))
                continue;
            string path = MainMenuLogoPlacement.PathOf(raw.transform);
            if (hits.Length > 0) hits.Append(" | ");
            hits.Append(path);
            if (ApplyTo(raw, ours, $"scene sweep {when} → {path}", null) != null)
                swapped++;
        }

        // Unconditional at verbose. A sweep that only speaks when it finds something hides the fact
        // that it ran at all, and this project has paid for that twice.
        if (!verbose && swapped == 0)
            return 0;

        VRLog.Info(Scope,
            $"SCENE SWEEP ({when}): {inspected} scene Image/RawImage inspected against " +
            $"{s_originalSprites.Count} original sprite(s) and {s_originalTextures.Count} original " +
            $"texture(s); {swapped} FURTHER graphic(s) carried the old wordmark and were swapped" +
            (swapped == 0
                 ? ". The field resolution had already reached every copy of it — so hypothesis (A), a "
                   + "second wordmark graphic drawn from the SAME asset, is ruled out for this menu."
                 : $": {hits}. These are copies rounds one to four never touched."));
        return swapped;
    }


    /// <summary>
    /// EVERY secondary candidate, resolved without a per-frame cost (this runs once per menu load).
    /// <c>Resources.FindObjectsOfTypeAll</c> and not <c>FindObjectOfType</c>: the background view
    /// may legitimately be disabled when the menu manager wakes, and an inactive object is exactly
    /// the case <c>FindObjectOfType</c> cannot see. Prefab assets are filtered out by requiring a
    /// real scene.
    ///
    /// <para><b>ModBuild 226 CHANGED THIS FROM "the first one" TO "all of them".</b> The old version
    /// returned as soon as one <c>BackgroundView</c> had a non-null <c>m_Logo</c>, which silently
    /// assumed the scene contains exactly one — an assumption nothing had ever checked, and one of
    /// the two ways the user could keep seeing the game's wordmark while our log reported a
    /// successful swap. <c>BackgroundView</c> has no callers anywhere in GH.Runtime, so how many
    /// instances the scene carries cannot be read off the source at all; it has to be counted at
    /// runtime, and now it is.</para>
    /// </summary>
    /// <returns>How many BackgroundView components live in a real scene (whatever their m_Logo).</returns>
    private static int CollectBackgroundViewLogos(List<GameObject> into)
    {
        int live = 0;
        foreach (BackgroundView view in Resources.FindObjectsOfTypeAll<BackgroundView>())
        {
            if (view == null || !view.gameObject.scene.IsValid())
                continue; // prefab / asset instance, not the live menu
            live++;
            if (view.m_Logo != null && !into.Contains(view.m_Logo))
                into.Add(view.m_Logo);
        }
        return live;
    }

    // =============================================================================================
    //  THE ASSET
    // =============================================================================================

    /// <summary>
    /// Decodes the embedded PNG once. The texture and the sprite are <c>HideAndDontSave</c> so the
    /// <c>Resources.UnloadUnusedAssets</c> that rides along with every scene load cannot reclaim
    /// them between two visits to the menu.
    /// </summary>
    private static Sprite? LoadSprite()
    {
        if (s_sprite != null)
            return s_sprite;
        if (s_loadFailed)
            return null;

        byte[]? bytes = ReadResource();
        if (bytes == null || bytes.Length < 8)
        {
            s_loadFailed = true;
            VRLog.Warn(Scope,
                $"MAIN-MENU LOGO NOT SWAPPED — embedded resource '{ResourceName}' is missing or empty " +
                $"(read {bytes?.Length ?? -1} bytes). Check the <EmbeddedResource> item in " +
                "GloomhavenVR.csproj; the menu keeps the game's own wordmark.");
            return null;
        }

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true, linear: false)
        {
            name = "GloomhavenVR.MainMenuLogo",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };
        if (!tex.LoadImage(bytes, markNonReadable: true))
        {
            UnityEngine.Object.Destroy(tex);
            s_loadFailed = true;
            VRLog.Warn(Scope,
                $"MAIN-MENU LOGO NOT SWAPPED — Texture2D.LoadImage rejected the {bytes.Length} bytes of " +
                $"'{ResourceName}'. The menu keeps the game's own wordmark.");
            return null;
        }

        var sprite = Sprite.Create(
            tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);
        sprite.name = "GloomhavenVR.MainMenuLogo";
        sprite.hideFlags = HideFlags.HideAndDontSave;

        VRLog.Info(Scope,
            $"Embedded wordmark decoded: {tex.width}x{tex.height} ({bytes.Length} bytes, " +
            $"aspect {(float)tex.width / tex.height:F3}).");

        s_sprite = sprite;
        return sprite;
    }

    private static byte[]? ReadResource()
    {
        Assembly asm = typeof(MainMenuLogoSwap).Assembly;
        Stream? stream = asm.GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            // Fall back to a suffix scan so a rename of the Assets folder degrades to a log line
            // rather than to a silent no-op.
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith("GloomhavenVR_logo.png", StringComparison.OrdinalIgnoreCase))
                    continue;
                VRLog.Warn(Scope,
                    $"Embedded resource '{ResourceName}' not found; using '{name}' instead — the " +
                    "LogicalName in GloomhavenVR.csproj and ResourceName here have drifted apart.");
                stream = asm.GetManifestResourceStream(name);
                break;
            }
        }
        if (stream == null)
            return null;

        using (stream)
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    // =============================================================================================
    //  SMALL HELPERS
    // =============================================================================================

    private static float Area(RectTransform rt)
    {
        Rect r = rt.rect;
        return Mathf.Abs(r.width * r.height);
    }

    private static void Append(StringBuilder sb, string name, string kind, float area, string art)
    {
        if (sb.Length > 0)
            sb.Append(", ");
        sb.Append($"{name}[{kind} {area:F0}px² art='{art}']");
    }

    private static string Describe(GameObject? go) => go == null ? "<null>" : $"'{go.name}'";
}

/// <summary>
/// Where the new wordmark is put, so that GLOOMHAVEN keeps the size and position the game's own
/// logo had and only the VR extension hangs past it. Split out of <see cref="MainMenuLogoSwap"/>
/// for a mechanical reason: that class carries <c>[HarmonyPatch]</c>, and the Harmony analyzer
/// reads every struct-typed parameter of every method in such a class as a patch argument
/// (Harmony003, "non-ref patch parameter modified"). These helpers take <c>Rect</c>s, so they live
/// next door instead of being contorted to please the analyzer.
/// </summary>
internal static class MainMenuLogoPlacement
{
    private const string Scope = "MenuLogo";

    // ---- constants of the shipped artwork -------------------------------------------------------
    //
    // USER CORRECTION (2026-08-22, verbatim), which is what this whole section exists for: "Das Logo
    // ist ja das originale Logo nur mit der 'VR' ergänzung - Es sollte daher die selben
    // Seitenvehältnisse haben bzw nur leicht breiter da das VR breiter ist. Aber der 'Gloomhaven'
    // Schriftzug sollte 1:1 genau an der Stelle sein, an dem das originale Logo war."
    //
    // AND THEN TWICE MORE: "Das Logo ist immer noch das selbe" (ModBuild 220's fit-inside) and "Logo
    // ist immer noch nicht sichtbar, bzw. ich sehe noch das originale logo" (221/222). Both times
    // the swap HAD happened — the log said so — and both times the wordmark came out at the wrong
    // SIZE, which on identical artwork is indistinguishable from "nothing changed".
    //
    // ================================ WHAT THE THIRD LOG SETTLED ==================================
    // ModBuild 222 measured the game asset instead of assuming, and the number it printed refutes
    // the assumption underneath BOTH earlier attempts:
    //
    //     GH_Logo 2048x582; ink 0.0469..0.9629 x, 0.1096..0.7808 y  =>  ink aspect 4.802
    //
    // The shipped wordmark's own band is aspect 6.7-7.0 (below). Those cannot both be the same
    // artwork — so THE TWO BOXES ARE NOT MEASURING THE SAME THING. The game's asset carries a broad
    // soft OUTER GLOW; the artist's file was flattened onto black, so its glow blended into the
    // background and the alpha crop trimmed it away. Solving for the glow width g from
    //     (1875 - 2g) / (390 - 2g) = 6.9   gives g ~ 55 px on a 2048 px asset,
    // i.e. the game's ink box is ~28 % taller than its letters while ours is ~3 % taller than ours.
    //
    // ================================ WHAT FOLLOWS, AND IT IS THE FIX =============================
    // MATCH BY WIDTH, NOT BY HEIGHT. Horizontally a 55 px glow is 3 % of a 1875 px wordmark;
    // vertically it is 28 % of a 390 px one. The width is an order of magnitude more robust to the
    // exact glow, which is the one quantity neither side can measure about the other.
    // Sanity check on the arithmetic, and the reason this is not a fourth guess: matching widths
    // predicts a letter height of 151.6 local units, while the game's ink box scaled by its own
    // letters-to-ink ratio gives 151.7 — agreement to 0.1 %, from two independent routes.
    //
    // Anchors: the band's LEFT edge and its VERTICAL CENTRE. Not the top and not the bottom — a
    // symmetric glow moves both of those and leaves the centre where it is.
    //
    // ---- and the contour is SEARCHED FOR, not chosen ---------------------------------------------
    // We know one thing for certain and it is enough: the GLOOMHAVEN part of the new artwork IS the
    // game's own wordmark. So at the contour where both are cut the same way, the two boxes must
    // have the SAME ASPECT — and <see cref="MeasureInk"/> therefore sweeps the old asset's alpha
    // contours and keeps the one whose aspect matches <see cref="BandAspect"/>. That turns the one
    // quantity nobody can know about somebody else's asset (how far its soft glow reaches) into a
    // measurement, and it fails loudly with a number when no contour matches at all.
    //
    // MEASURED off Assets/GloomhavenVR_logo.png (1024x179 RGBA). The band's right edge is defined
    // reproducibly rather than by eye: it is the column at which the silhouette's bottom drops more
    // than 12 px below the wordmark's own baseline, which is where the VR's descending flourish
    // begins (column 838-840, stable across every threshold from 0.078 to 0.85). At the core
    // threshold the band is x 5..838, y 5..123.
    //
    // THEY ARE FRACTIONS OF THE SPRITE, NOT PIXELS, on purpose: a future re-export at another
    // resolution keeps working as long as the composition is unchanged, and a re-export that MOVES
    // the wordmark inside the canvas is a change to these numbers and to nothing else.

    /// <summary>Left edge of the GLOOMHAVEN band, as a fraction of sprite width.</summary>
    private const float BandLeft = 5f / 1024f;

    /// <summary>Right edge of the GLOOMHAVEN band (exclusive), as a fraction of sprite width — the
    /// column where the VR's flourish takes over. THE PLACEMENT TURNS ON THIS NUMBER together with
    /// <see cref="BandLeft"/>: the new rect is sized so that this span covers the width of the old
    /// wordmark's letters exactly.</summary>
    private const float BandRight = 839f / 1024f;

    /// <summary>Top of the GLOOMHAVEN band, as a fraction of sprite height from the TOP edge.
    /// Diagnostic only since ModBuild 223 — the placement anchors on the centre, not the top.</summary>
    private const float BandTop = 5f / 179f;

    /// <summary>Bottom of the GLOOMHAVEN band (exclusive), fraction of sprite height from the top.
    /// Everything below is the VR's flourish and is allowed to hang past where the old logo ended.</summary>
    private const float BandBottom = 124f / 179f;

    /// <summary>Width of the GLOOMHAVEN band as a fraction of the sprite's width — the number the
    /// whole placement turns on since ModBuild 223.</summary>
    private const float BandWidth = BandRight - BandLeft;

    /// <summary>Height of the GLOOMHAVEN band as a fraction of the sprite's height. Diagnostic only
    /// — it is what the placement used to scale by, and reporting it is how the next log can be
    /// compared against the two rounds that got this wrong.</summary>
    private const float BandHeight = BandBottom - BandTop;

    /// <summary>Vertical centre of the GLOOMHAVEN band, as a fraction of sprite height from the top
    /// — the anchor that a symmetric glow cannot move.</summary>
    private const float BandCentreY = (BandTop + BandBottom) * 0.5f;

    /// <summary>Aspect of the GLOOMHAVEN band itself. Used ONLY for the consistency check against
    /// the old wordmark's core box; nothing is scaled by it.</summary>
    private const float BandAspect = 7.008f;

    /// <summary>Widest readback used to find the ink. 512 columns across a 2048 px asset resolves
    /// the box to 0.2 % of its width, which is far below anything the eye can judge against a
    /// hand-authored menu, and it keeps the one-off readback trivial.</summary>
    private const int InkSampleWidth = 512;

    /// <summary>
    /// WHERE THE INK IS INSIDE A SPRITE, as a fraction of its rect, measured with y running DOWN
    /// from the top so it can be compared with the band constants directly. Returns the whole
    /// sprite (and says why in <paramref name="note"/>) when it cannot measure.
    ///
    /// <para><b>WHY THIS EXISTS.</b> ModBuild 221 aligned the new wordmark to the box the old
    /// sprite DREW in, on the unstated assumption that a logo asset is cropped to its logo. It is
    /// not: the game's <c>GH_Logo</c> is 2048x582 (aspect 3.519) and the wordmark on it is about
    /// aspect 7.4, so most of the asset is transparent margin. Scaling to the margin made the
    /// replacement 2.35x too wide, which is what the hardware photograph shows and what 221's own
    /// consistency check reported as "implied band right edge 0.426 — OUT OF RANGE". The check was
    /// right; this is the measurement it was asking for.</para>
    ///
    /// <para><b>WHY A BLIT AND NOT <c>GetPixels</c>.</b> A shipped game texture is almost never
    /// <c>isReadable</c>, so the CPU-side call would throw. Blitting into a temporary
    /// <see cref="RenderTexture"/> and reading THAT back works regardless of the source's read flag
    /// and regardless of its compression, because the GPU does the decode. It costs one small
    /// readback per menu load and nothing per frame.</para>
    /// </summary>
    internal static Rect MeasureInk(Sprite? sprite, out string note)
    {
        Rect whole = new Rect(0f, 0f, 1f, 1f);
        Texture2D? tex = sprite != null ? sprite.texture : null;
        if (sprite == null || tex == null || tex.width < 2 || tex.height < 2)
        {
            note = "ink NOT measured (no source texture), so the old sprite's whole rect is used as "
                   + "its wordmark — if the asset has a transparent margin the replacement will be "
                   + "too large by exactly that margin";
            return whole;
        }

        // The sprite's own window into its texture — non-trivial only if the art is atlased.
        Rect tr = sprite.textureRect;
        if (tr.width < 1f || tr.height < 1f)
            tr = new Rect(0f, 0f, tex.width, tex.height);

        int sw = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(InkSampleWidth, tr.width)), 2, 2048);
        int sh = Mathf.Clamp(Mathf.RoundToInt(sw * (tr.height / tr.width)), 2, 2048);

        RenderTexture? rt = null;
        RenderTexture? prev = RenderTexture.active;
        Texture2D? shot = null;
        try
        {
            rt = RenderTexture.GetTemporary(sw, sh, 0, RenderTextureFormat.ARGB32,
                                            RenderTextureReadWrite.Linear);
            var scale = new Vector2(tr.width / tex.width, tr.height / tex.height);
            var offset = new Vector2(tr.x / tex.width, tr.y / tex.height);
            Graphics.Blit(tex, rt, scale, offset);

            RenderTexture.active = rt;
            shot = new Texture2D(sw, sh, TextureFormat.RGBA32, mipChain: false);
            shot.ReadPixels(new Rect(0f, 0f, sw, sh), 0, 0, recalculateMipMaps: false);
            shot.Apply(false, false);

            Color32[] px = shot.GetPixels32();
            float pixelAspect = sw / (float)sh;

            // ---- SELF-CALIBRATION, and it is what ends three rounds of threshold guessing ---------
            //
            // We know one thing for certain and it is enough: the GLOOMHAVEN part of the new artwork
            // IS the game's own wordmark, so at the contour where the two are cut the same way, the
            // two boxes MUST have the same aspect. So do not pick a threshold — SEARCH for the one
            // whose box on the old art matches our band's aspect, and use that box.
            //
            // This turns the one quantity nobody can know about someone else's asset (how far its
            // soft glow reaches) into a measurement. It also fails LOUDLY and informatively: if no
            // contour anywhere in 8..248 produces our aspect, then the artwork is not what it was
            // said to be, and the log says so with the closest it could get.
            Rect bestFrac = whole;
            float bestAspect = 0f, bestErr = float.MaxValue;
            int bestAlpha = -1;
            Rect outerFrac = whole;
            float outerAspect = 0f;
            bool anyLit = false;

            for (int alpha = 8; alpha <= 248; alpha += 8)
            {
                int x0 = sw, x1 = -1, y0 = sh, y1 = -1;
                for (int y = 0; y < sh; y++)
                {
                    int row = y * sw;
                    for (int x = 0; x < sw; x++)
                    {
                        if (px[row + x].a <= alpha)
                            continue;
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                }
                if (x1 < x0 || y1 < y0)
                    break;   // nothing left at this contour; every higher one is emptier still

                float fx = x0 / (float)sw;
                float fw = (x1 - x0 + 1) / (float)sw;
                float fyTop = 1f - (y1 + 1) / (float)sh;
                float fh = (y1 - y0 + 1) / (float)sh;
                float aspect = fh > 0.0001f ? fw / fh * pixelAspect : 0f;

                if (!anyLit)
                {
                    anyLit = true;
                    outerFrac = new Rect(fx, fyTop, fw, fh);
                    outerAspect = aspect;
                }

                float err = Mathf.Abs(aspect - BandAspect);
                if (err < bestErr)
                {
                    bestErr = err;
                    bestAspect = aspect;
                    bestAlpha = alpha;
                    bestFrac = new Rect(fx, fyTop, fw, fh);
                }
            }

            if (!anyLit)
            {
                note = $"ink NOT found — every one of the {sw}x{sh} sampled pixels is at or below "
                       + "alpha 8. Either the source has no alpha channel at all (then the whole rect "
                       + "IS the artwork and this is correct) or the readback failed silently; the "
                       + "whole rect is used";
                return whole;
            }

            bool matched = bestErr / BandAspect < 0.08f;
            note = $"old art measured on a {sw}x{sh} readback. OUTER contour (alpha>8): "
                   + $"x {outerFrac.xMin:F4}..{outerFrac.xMax:F4} y {outerFrac.yMin:F4}..{outerFrac.yMax:F4}, "
                   + $"aspect {outerAspect:F3}. CALIBRATED contour: alpha>{bestAlpha}, "
                   + $"x {bestFrac.xMin:F4}..{bestFrac.xMax:F4} y {bestFrac.yMin:F4}..{bestFrac.yMax:F4}, "
                   + $"aspect {bestAspect:F3} against our band's {BandAspect:F3}"
                   + (matched
                          ? $" — MATCHED to {bestErr / BandAspect * 100f:F1}%. The gap between the two "
                            + "contours is the old asset's soft outer glow, which our own artwork does "
                            + "not carry; the calibrated box is the one the placement uses"
                          : $" — NO CONTOUR MATCHES (closest is {bestErr / BandAspect * 100f:F1}% off). "
                            + "The new artwork is then NOT the game's wordmark plus VR, or the sprite "
                            + "is atlased and the readback caught a neighbour. The closest box is used "
                            + "anyway, and it will be wrong by about that much");

            if (!matched)
                VRLog.Warn(Scope,
                    $"{note}. This is the check that caught ModBuild 221 and 222; treat the number "
                    + "above as the size error to expect in the headset.");

            return bestFrac;
        }
        catch (Exception ex)
        {
            note = $"ink NOT measured ({ex.GetType().Name}: {ex.Message}) — the whole rect is used, "
                   + "so a padded source asset will make the replacement too large";
            return whole;
        }
        finally
        {
            RenderTexture.active = prev;
            if (shot != null)
                UnityEngine.Object.Destroy(shot);
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>A sub-box of <paramref name="outer"/> given as fractions with y running DOWN from
    /// the top, returned in the same local space as <paramref name="outer"/> (y up).</summary>
    internal static Rect SubBox(Rect outer, Rect frac)
    {
        float w = outer.width * frac.width;
        float h = outer.height * frac.height;
        float x = outer.xMin + outer.width * frac.xMin;
        float yMax = outer.yMax - outer.height * frac.yMin;
        return new Rect(x, yMax - h, w, h);
    }

    /// <summary>
    /// The box a sprite's artwork actually drew in, inside <paramref name="rect"/>, in the
    /// RectTransform's own local space.
    ///
    /// <para>With <c>preserveAspect</c> off (or on a tiled/sliced Image, where it does nothing) the
    /// sprite is stretched across the whole rect and the answer is the rect. With it on, uGUI fits
    /// the sprite inside the rect and CENTRES it — that fitted box, not the rect, is where the
    /// player saw the logo, and it is what the new placement has to reproduce.</para>
    /// </summary>
    internal static Rect DrawnBox(Rect rect, Rect spriteRect, bool preserveAspect, Image.Type type)
    {
        bool fits = preserveAspect && (type == Image.Type.Simple || type == Image.Type.Filled);
        if (!fits || spriteRect.width <= 0.001f || spriteRect.height <= 0.001f
            || rect.width <= 0.001f || rect.height <= 0.001f)
            return rect;

        float aspect = spriteRect.width / spriteRect.height;
        float w = rect.width;
        float h = w / aspect;
        if (h > rect.height)
        {
            h = rect.height;
            w = h * aspect;
        }
        return new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h);
    }

    /// <summary>
    /// Resize and move the RectTransform so that the sprite's GLOOMHAVEN band covers
    /// <paramref name="oldInk"/> exactly, letting the VR extension hang to the right and a little
    /// below — which is precisely what the user asked for.
    ///
    /// <para><paramref name="oldInk"/> is the box the old sprite's INK occupied, not the box it
    /// was drawn into — see <see cref="MeasureInk"/> for why the difference cost ModBuild 221.</para>
    ///
    /// <para>THE ARITHMETIC. The band is <see cref="BandWidth"/> of the sprite's width, so the new
    /// rect is <c>oldInk.width / BandWidth</c> wide for the band inside it to come out at the old
    /// wordmark's own width; the height follows from the sprite's aspect, so nothing is ever
    /// stretched. The rect is then offset so the band's LEFT edge and VERTICAL CENTRE sit on the old
    /// letters'. Net effect: the wordmark is the size and position it always was and the rect around
    /// it is a little bigger — the opposite of ModBuild 220, which kept the rect and shrank the
    /// wordmark into it, and of 221/222, which matched the wrong axis.</para>
    ///
    /// <para>A CONSISTENCY CHECK THAT COSTS NOTHING AND HAS ALREADY EARNED ITS KEEP TWICE — it is
    /// what caught ModBuild 221 (0.426 against a plausible 0.90) and 222 (0.582), both times in the
    /// very log the user sent with the photograph. It reports the SHAPE agreement between the old
    /// letters and our band, i.e. the axis the placement does not scale by, so it is genuinely
    /// independent of the fit.</para>
    ///
    /// <para>WHY THE RECT MAY BE TOUCHED AT ALL, when the ModBuild 220 note said it must not be:
    /// that rule was mine, not the user's, and his correction overrides it. It is still refused in
    /// the one case where it would be destructive — a rect driven by a layout system, where the
    /// size we write is not the size that survives the next layout pass. There the old fit-inside
    /// behaviour is kept and the refusal is logged.</para>
    /// </summary>
    /// <returns>One sentence for the swap's log line, stating what was done or why it was not.</returns>
    internal static string PlaceOnBand(Image image, Rect oldInk, float newAspect, string origin)
    {
        RectTransform rt = image.rectTransform;

        if (newAspect <= 0.001f || oldInk.height <= 0.001f || oldInk.width <= 0.001f)
        {
            image.preserveAspect = true;
            return "PLACEMENT SKIPPED — the old drawn box or the new aspect is degenerate; fell back "
                   + "to fitting inside the authored rect, so the wordmark will be smaller than the "
                   + "original.";
        }

        if (image.GetComponent<UnityEngine.UI.ContentSizeFitter>() != null
            || image.GetComponent<UnityEngine.UI.LayoutElement>() != null
            || (rt.parent != null && rt.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null))
        {
            image.preserveAspect = true;
            VRLog.Warn(Scope,
                $"{origin}: the logo's rect is driven by a layout component, so it was NOT resized — "
                + "a size written here would be overwritten on the next layout pass and the result "
                + "would flicker between two placements. The wordmark is fitted inside the authored "
                + "rect instead and will therefore be SMALLER than the game's own logo. To fix it "
                + "properly the placement has to move to the layout element that owns the size.");
            return "PLACEMENT REFUSED (layout-driven rect) — fitted inside instead.";
        }

        // ---- THE MATCH IS ON WIDTH. See the constants block for why height is the wrong axis: the
        //      game's asset carries a broad soft glow our artwork does not, and vertically that
        //      glow is ~28% of the wordmark while horizontally it is ~3%.
        float w2 = oldInk.width / BandWidth;
        float h2 = w2 / newAspect;

        // Band LEFT onto the old letters' left; band VERTICAL CENTRE onto theirs. Not the top and
        // not the bottom — a symmetric glow moves both of those and leaves the centre alone.
        float targetXMin = oldInk.xMin - BandLeft * w2;
        float targetYMax = oldInk.center.y + BandCentreY * h2;
        float targetYMin = targetYMax - h2;

        Vector2 pivot = rt.pivot;
        string anchors = CollapseAnchors(rt);

        Vector2 delta = new Vector2(targetXMin + pivot.x * w2, targetYMin + pivot.y * h2);
        rt.sizeDelta = new Vector2(w2, h2);
        rt.anchoredPosition += delta;

        // ---- the consistency check, now on the axis the placement does NOT use, so it is genuinely
        //      independent of it. If the artwork really is the same wordmark, the old LETTER box and
        //      our band have the same aspect. 1.00 is perfect; the tolerance is wide because the two
        //      alpha thresholds do not cut the letterforms at exactly the same contour.
        float oldAspect = oldInk.width / oldInk.height;
        float ratio = oldAspect / BandAspect;
        string verdict = ratio is > 0.85f and < 1.18f
            ? $"CONSISTENT — the old letters and our band agree on shape to {Mathf.Abs(1f - ratio) * 100f:F0}%"
            : "OUT OF RANGE — the old LETTER box is not the same shape as our GLOOMHAVEN band, so "
              + "either the artwork is not the game's wordmark plus VR, or the contour sweep found "
              + "no matching cut. The wordmark will be the wrong size by about that much, and "
              + "MeasureInk's own line above says which of the two it is — NOT another guess";

        return $"letters {oldInk.width:F1}x{oldInk.height:F1} at ({oldInk.xMin:F1},{oldInk.yMin:F1}) → "
               + $"rect {w2:F1}x{h2:F1}, moved by ({delta.x:F1},{delta.y:F1}), {anchors} "
               + $"The GLOOMHAVEN band is now {oldInk.width:F1} wide — the old wordmark's own width — "
               + $"and {BandHeight * h2:F1} tall; the VR hangs {(1f - BandRight) * w2:F1} past its right "
               + $"edge and {(1f - BandBottom) * h2:F1} below. "
               + $"Shape check old/band {oldAspect:F3}/{BandAspect:F3} = {ratio:F3}: {verdict}. "
               + $"{DescribeClipping(rt)}";
    }

    /// <summary>
    /// Whether anything above this graphic will CUT the widened rect, and how much room it has.
    ///
    /// <para>This exists because "I still see the original logo" is what an oversized wordmark whose
    /// VR has been clipped away looks like — the two are indistinguishable in a photograph, since
    /// the GLOOMHAVEN part is the same artwork either way. A mask is the one thing that could hide
    /// the change while every number in the log reads correct, so the log states it rather than
    /// leaving the next round to wonder.</para>
    /// </summary>
    private static string DescribeClipping(RectTransform rt)
    {
        var sb = new StringBuilder(160);
        Transform? t = rt.parent;
        int masks = 0;
        while (t != null)
        {
            if (t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null)
            {
                masks++;
                if (masks == 1)
                    sb.Append("CLIPPED BY '").Append(t.name).Append('\'');
            }
            t = t.parent;
        }

        if (masks == 0)
            return "No RectMask2D/Mask anywhere above it, so nothing clips the widened rect.";

        if (rt.parent is RectTransform p)
            sb.Append(" — its rect is ").Append(p.rect.width.ToString("F0")).Append('x')
              .Append(p.rect.height.ToString("F0")).Append(", ours is ")
              .Append(rt.rect.width.ToString("F0")).Append('x').Append(rt.rect.height.ToString("F0"));
        sb.Append(". IF THE VR IS MISSING IN THE HEADSET, THIS IS WHY, and the fix is to place the "
                  + "wordmark inside the mask rather than to grow past it.");
        return sb.ToString();
    }

    /// <summary>
    /// Turn stretched anchors into point anchors WITHOUT moving the rect, so that
    /// <c>sizeDelta</c> means "size" and <c>anchoredPosition</c> means "position".
    ///
    /// <para>A stretched rect takes its size from the parent, so writing a size into
    /// <c>sizeDelta</c> there would mean "parent size PLUS this" and would move with every parent
    /// resize. Collapsing first is the standard fix and is exactly reversible on the next scene
    /// load, because the scene re-instantiates the object.</para>
    /// </summary>
    /// <returns>A clause for the log naming what was done to the anchors.</returns>
    private static string CollapseAnchors(RectTransform rt)
    {
        if (rt.anchorMin == rt.anchorMax)
            return "anchors were already a point and are unchanged;";

        if (rt.parent is not RectTransform parent)
            return "anchors are stretched but the parent is not a RectTransform, so they were left "
                   + "alone — the placement may drift if the parent resizes;";

        Vector2 size = rt.rect.size;
        Vector3 lp = rt.localPosition;
        Rect pr = parent.rect;
        var anchor = new Vector2(0.5f, 0.5f);
        var anchorPoint = new Vector2(pr.xMin + anchor.x * pr.width, pr.yMin + anchor.y * pr.height);

        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(lp.x - anchorPoint.x, lp.y - anchorPoint.y);
        return "stretched anchors were collapsed to the parent's centre at the same rect;";
    }

    // =============================================================================================
    //  WHERE THE VR IS, so the readback can ask about it separately
    // =============================================================================================
    //
    // MEASURED off Assets/GloomhavenVR_logo.png (1024x179 RGBA), the same file the band constants
    // above were measured from, with the same method: the alpha silhouette. The VR glyphs and their
    // descending flourish occupy columns 840..1023 and rows 9..170. The boxes below are pulled in by
    // a few pixels on every side so that a small projection error cannot make the box straddle the
    // artwork's edge and dilute the reading.
    //
    // WHAT THE ARTWORK PUTS INSIDE THEM, so the next log has something to compare against:
    //     band box  x 5..839, y 5..124   -> 85.7 % of its pixels at alpha>128, 58.1 % of them bright
    //     VR box    x 845..1020, y 10..170 -> 68.9 % at alpha>128, 40.7 % bright
    // "Bright" is luminance above 60/255, which is the letters and their metal fill rather than the
    // dark outline. So a VR that IS drawn reads around 0.4, and one that is not reads near the
    // background. That is a factor the eye cannot argue with.

    /// <summary>Left edge of the VR box, as a fraction of sprite width.</summary>
    private const float VrLeft = 845f / 1024f;

    /// <summary>Right edge of the VR box, as a fraction of sprite width.</summary>
    private const float VrRight = 1020f / 1024f;

    /// <summary>Top of the VR box, as a fraction of sprite height from the TOP edge.</summary>
    private const float VrTop = 10f / 179f;

    /// <summary>Bottom of the VR box, fraction of sprite height from the top — it hangs well below
    /// the GLOOMHAVEN band, which is the whole point of the user's geometric contract.</summary>
    private const float VrBottom = 170f / 179f;

    /// <summary>Fraction of bright pixels at which a region counts as "the artwork is drawn here".
    /// The artwork itself delivers 0.58 in the band and 0.41 in the VR box, and a menu background
    /// that happens to be bright would have to fill a tenth of the box to reach this.</summary>
    private const float InkVerdictThreshold = 0.10f;

    // ---- the watch's cadence, all bounded ---------------------------------------------------
    private const float WatchTickSeconds = 0.5f;      // 2 Hz, as specified
    private const int WatchTicks = 60;                // 30 seconds, then it stops for good
    private const int WatchDeepSweepEvery = 4;        // a full scene sweep every 2 s, not every tick
    private const float ShownPollSeconds = 0.25f;
    private const int ShownPollLimit = 80;            // 20 s to wait for the menu to be shown

    // =============================================================================================
    //  SMALL SHARED HELPERS
    // =============================================================================================

    /// <summary>Full hierarchy path of a transform, for a log line that can be acted on.</summary>
    internal static string PathOf(Transform? t)
    {
        if (t == null)
            return "<null>";
        var sb = new StringBuilder(96);
        while (t != null)
        {
            if (sb.Length > 0)
                sb.Insert(0, '/');
            sb.Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    /// <summary>
    /// The nearest <see cref="Canvas"/> above <paramref name="t"/>, INCLUDING inactive ones.
    /// <c>Graphic.canvas</c> cannot be used for this: it caches through
    /// <c>GetComponentsInParent(false, …)</c>, so on an inactive object — which is half of what the
    /// census exists to look at — it answers null and the census would report "no canvas" for
    /// something that plainly has one.
    /// </summary>
    private static Canvas? FindCanvas(Transform? t)
    {
        while (t != null)
        {
            var c = t.GetComponent<Canvas>();
            if (c != null)
                return c;
            t = t.parent;
        }
        return null;
    }

    // =============================================================================================
    //  WHAT WE DID TO ONE GRAPHIC — the snapshot that makes a repair idempotent
    // =============================================================================================

    /// <summary>
    /// One swapped graphic, the rect it had BEFORE we touched it, and the rect we left on it.
    ///
    /// <para>The authored snapshot is not bookkeeping, it is a correctness requirement. The
    /// placement measures the OLD sprite inside the CURRENT rect and then grows that rect; running
    /// it a second time on an already-grown rect would grow it again, and a re-assert that made the
    /// wordmark bigger on every repair would be worse than no re-assert at all. So a repair restores
    /// the authored rect first and then runs the identical arithmetic — same inputs, same output.</para>
    ///
    /// <para>The applied snapshot answers the other half of hypothesis (B), the half that no
    /// decompiled source can rule out: a <c>LayoutGroup</c>, <c>ContentSizeFitter</c> or
    /// <c>AspectRatioFitter</c> that rewrites <c>sizeDelta</c> on the layout pass AFTER our Awake
    /// postfix. <see cref="PlaceOnBand"/> refuses to resize when it can SEE such a component, but it
    /// can only see the ones it tests for, and a driver on a grandparent or a custom script is
    /// invisible to it. Comparing what we wrote against what is there two seconds later catches all
    /// of them at once, without having to enumerate them.</para>
    /// </summary>
    internal sealed class SwapRecord
    {
        internal readonly Graphic Graphic;
        internal readonly string Origin;
        internal readonly string Path;

        private readonly Vector2 _authoredSize;
        private readonly Vector2 _authoredPos;
        private readonly Vector2 _authoredAnchorMin;
        private readonly Vector2 _authoredAnchorMax;

        private Vector2 _appliedSize;
        private Vector2 _appliedPos;

        /// <summary>Set the first time this graphic had to be repaired, so the warning is emitted
        /// once however often it fires. A per-frame writer would otherwise flood the log and the
        /// first line already carries everything the next round needs.</summary>
        internal bool RepairLogged;

        internal SwapRecord(Graphic graphic, string origin)
        {
            Graphic = graphic;
            Origin = origin;
            // No null guard on purpose: every caller resolves a live graphic first, and a record
            // built from a destroyed one would be a lie the watch then acted on.
            Path = PathOf(graphic.transform);
            RectTransform rt = graphic.rectTransform;
            _authoredSize = rt.sizeDelta;
            _authoredPos = rt.anchoredPosition;
            _authoredAnchorMin = rt.anchorMin;
            _authoredAnchorMax = rt.anchorMax;
            _appliedSize = _authoredSize;
            _appliedPos = _authoredPos;
        }

        /// <summary>Called once the placement has run, to record what we left behind.</summary>
        internal void NoteApplied()
        {
            if (Graphic == null)
                return;
            RectTransform rt = Graphic.rectTransform;
            _appliedSize = rt.sizeDelta;
            _appliedPos = rt.anchoredPosition;
        }

        /// <summary>Put the rect back exactly as the scene authored it, so the placement can be
        /// re-run from the same inputs it saw the first time.</summary>
        internal void RestoreRect()
        {
            if (Graphic == null)
                return;
            RectTransform rt = Graphic.rectTransform;
            rt.anchorMin = _authoredAnchorMin;
            rt.anchorMax = _authoredAnchorMax;
            rt.sizeDelta = _authoredSize;
            rt.anchoredPosition = _authoredPos;
        }

        /// <summary>
        /// What has changed under us since the swap, or null if nothing has.
        ///
        /// <para>The sprite test goes through <c>overrideSprite</c> on purpose. Its GETTER falls back
        /// to <c>sprite</c>, so one comparison covers both "the sprite was re-assigned" and "an
        /// override was pushed on top of ours" — and an override is the failure mode that a test on
        /// <c>sprite</c> alone cannot see at all.</para>
        /// </summary>
        internal string? WhatChanged(Sprite ours)
        {
            if (Graphic == null)
                return null;

            if (Graphic is Image image && !ReferenceEquals(image.overrideSprite, ours))
            {
                string now = image.overrideSprite != null ? image.overrideSprite.name : "<none>";
                return $"the drawn sprite is '{now}' again, not ours "
                       + $"(sprite='{(image.sprite != null ? image.sprite.name : "<none>")}')";
            }

            if (Graphic is RawImage raw && !ReferenceEquals(raw.texture, ours.texture))
            {
                return $"the texture is '{(raw.texture != null ? raw.texture.name : "<none>")}' again, not ours";
            }

            RectTransform rt = Graphic.rectTransform;
            Vector2 size = rt.sizeDelta;
            Vector2 pos = rt.anchoredPosition;
            if ((size - _appliedSize).sqrMagnitude > 1f || (pos - _appliedPos).sqrMagnitude > 1f)
            {
                return $"the rect was rewritten from the {_appliedSize.x:F1}x{_appliedSize.y:F1} at "
                       + $"({_appliedPos.x:F1},{_appliedPos.y:F1}) we wrote to {size.x:F1}x{size.y:F1} at "
                       + $"({pos.x:F1},{pos.y:F1}) — that is a layout driver this patch does not test "
                       + "for, and it is the half of hypothesis (B) that no decompiled source could "
                       + "have ruled out";
            }

            return null;
        }
    }

    // =============================================================================================
    //  INSTRUMENT 1 — THE SCENE CENSUS
    // =============================================================================================

    private sealed class CensusRow
    {
        internal float Area;
        internal bool Interesting;
        internal string Text = string.Empty;
    }

    /// <summary>
    /// EVERY <see cref="Graphic"/> in the scene, listed once, unconditionally.
    ///
    /// <para><b>WHY THIS EXISTS.</b> Four rounds resolved the wordmark through
    /// <c>MainMenuUIManager._logo</c> (null) and one <c>BackgroundView.m_Logo</c>, and never once
    /// asked what else is in the scene. If the wordmark the player sees is a different graphic —
    /// a second Image, a TMP object, a copy in a loading view — every number those four rounds
    /// printed can be exactly right and the menu still unchanged. This line is what makes that
    /// decidable: <b>if a second logo exists, it is named here.</b></para>
    ///
    /// <para>Listed are the graphics whose artwork is named like a logo or a title, the graphics
    /// whose OBJECT is named that way (a wordmark built out of TMP has no sprite to match on), and
    /// the fifteen largest by screen area, which is where a full-screen impostor would hide.
    /// <c>Resources.FindObjectsOfTypeAll</c> so inactive objects are included; a real scene is
    /// required so loaded prefabs are not.</para>
    ///
    /// <para>It prints whether it found anything or not. A diagnostic that only speaks on success
    /// has cost this project several rounds of "the fix produced no improvement" that turned out to
    /// mean "the fix never ran".</para>
    /// </summary>
    internal static void Census(string reason, Sprite? ours)
    {
        var rows = new List<CensusRow>(64);
        int inScene = 0;
        var world = new Vector3[4];

        foreach (Graphic g in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (g == null || !g.gameObject.scene.IsValid())
                continue;
            inScene++;

            string kind = g.GetType().Name;
            string art = "<none>";
            string artSize = string.Empty;
            if (g is Image img)
            {
                Sprite? s = img.overrideSprite != null ? img.overrideSprite : img.sprite;
                if (s != null)
                {
                    art = s.name;
                    artSize = $" {s.rect.width:F0}x{s.rect.height:F0}";
                }
            }
            else if (g is RawImage raw && raw.texture != null)
            {
                art = raw.texture.name;
                artSize = $" {raw.texture.width}x{raw.texture.height}";
            }

            RectTransform rt = g.rectTransform;
            rt.GetWorldCorners(world);

            Canvas? canvas = FindCanvas(g.transform);
            Camera? cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : (canvas.worldCamera != null ? canvas.worldCamera : Camera.main);

            float sx0 = float.MaxValue, sy0 = float.MaxValue, sx1 = float.MinValue, sy1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(cam, world[i]);
                if (sp.x < sx0) sx0 = sp.x;
                if (sp.y < sy0) sy0 = sp.y;
                if (sp.x > sx1) sx1 = sp.x;
                if (sp.y > sy1) sy1 = sp.y;
            }
            float area = Mathf.Max(0f, sx1 - sx0) * Mathf.Max(0f, sy1 - sy0);

            bool named = Mentions(art) || Mentions(g.name);
            bool mine = ours != null
                        && ((g is Image i2 && ReferenceEquals(i2.sprite, ours))
                            || (g is RawImage r2 && ReferenceEquals(r2.texture, ours.texture)));

            float alpha;
            try { alpha = g.canvasRenderer != null ? g.canvasRenderer.GetInheritedAlpha() : -1f; }
            catch (Exception) { alpha = -1f; }

            rows.Add(new CensusRow
            {
                Area = area,
                Interesting = named || mine,
                Text = $"{PathOf(g.transform)} [{kind}] art='{art}'{artSize}"
                       + (mine ? " <-- OURS" : string.Empty)
                       + $" enabled={g.enabled} active={g.IsActive()} inheritedAlpha={alpha:F3}"
                       + $" colorA={g.color.a:F3} layer={LayerMask.LayerToName(g.gameObject.layer)}"
                       + $" canvas='{(canvas != null ? canvas.name : "<none>")}'"
                       + (canvas != null
                              ? $" mode={canvas.renderMode} order={canvas.sortingOrder}"
                                + $" override={canvas.overrideSorting} canvasOn={canvas.isActiveAndEnabled}"
                              : string.Empty)
                       + $" world=({world[0].x:F2},{world[0].y:F2},{world[0].z:F2})..({world[2].x:F2},{world[2].y:F2},{world[2].z:F2})"
                       + $" screen=({sx0:F0},{sy0:F0} {sx1 - sx0:F0}x{sy1 - sy0:F0}) area={area:F0}",
            });
        }

        rows.Sort((a, b) => b.Area.CompareTo(a.Area));

        int selected = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Interesting || i < 15)
                selected++;
        }

        // The header is a Note and the rows are Info on purpose: the header states how many rows
        // follow, so a log whose level is below Verbose shows a header with no rows under it —
        // visibly truncated rather than silently empty. Rounds three and four both lost time to
        // instruments whose absence looked exactly like a clean result.
        VRLog.Note(Scope,
            $"SCENE CENSUS ({reason}): {inScene} Graphic(s) live in a real scene; {selected} row(s) "
            + "follow at Info level, listing every one whose artwork OR object is named like a "
            + "logo/title plus the 15 largest by screen area. IF A SECOND WORDMARK EXISTS IT IS IN "
            + "THIS LIST — that is the whole reason the list is here, because rounds one to four only "
            + "ever looked at one named field. If no census# lines follow this one, the log level is "
            + "below Verbose and the census was NOT empty.");

        int listed = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Interesting && i >= 15)
                continue;
            listed++;
            VRLog.Info(Scope, $"census#{listed:D2} {rows[i].Text}");
        }

        if (selected == 0)
            VRLog.Warn(Scope,
                "SCENE CENSUS listed NOTHING, which means the scene contains no Graphic at all. "
                + "Either this ran before the menu was built or the menu is not a uGUI scene — "
                + "both make every other line in this block meaningless.");
    }

    private static bool Mentions(string? name) =>
        name != null
        && (name.IndexOf("logo", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0);

    // =============================================================================================
    //  INSTRUMENT 2 — READ BACK WHAT WAS ACTUALLY DRAWN
    // =============================================================================================

    /// <summary>
    /// The decisive measurement, and the one every previous round lacked: after a frame has been
    /// rendered with the swap in place, how much ink is in the GLOOMHAVEN band and how much is in
    /// the VR box.
    ///
    /// <para><b>WHY A PHOTOGRAPH CANNOT ANSWER THIS.</b> The GLOOMHAVEN part of our artwork IS the
    /// game's own wordmark, drawn at the game's own width by construction. So a correct swap and no
    /// swap at all look identical in that region — which is exactly why the user has now reported
    /// "still the same logo" four times against four different builds, three of which really were
    /// wrong and one of which may not be. The VR box is the only region where the two differ, so
    /// that is the region to measure, and it has to be measured on PIXELS.</para>
    ///
    /// <para><b>HOW THE PICTURE IS OBTAINED.</b> A throwaway camera cloned from the one that owns
    /// the canvas, rendering into a temporary <see cref="RenderTexture"/>. A clone rather than the
    /// game's own camera because assigning a target texture to a live XR camera for one frame is a
    /// risk taken on the user's headset for a diagnostic; a clone carries none of it. The same clone
    /// then does the world-to-pixel projection, so the mapping and the picture cannot disagree — a
    /// self-consistency this project has been bitten by before, when a falsifier blitted from the
    /// active target and became a copy of its own subject.</para>
    ///
    /// <para><b>THE VERTICAL FLIP IS NOT GUESSED AT.</b> Whether a readback lands top-down or
    /// bottom-up depends on the path and the platform, and a wrong guess would mis-sample BOTH
    /// regions and read as "the logo is not there". So both orientations are computed and both are
    /// printed, and the verdict is taken from whichever one finds the band — which is the region we
    /// know has artwork in it under every hypothesis.</para>
    ///
    /// <para><b>AND IT CAN SAY THAT IT SAW NOTHING.</b> A control strip of the same size directly
    /// above the logo is measured too. If band, VR, whole rect and control all read empty, the
    /// capture did not see this canvas at all — a panel drawn through the mod's supersampler onto an
    /// isolated layer would do exactly that — and the line says the measurement is VOID rather than
    /// letting the next round read it as "the logo is absent".</para>
    /// </summary>
    internal static void MeasureDrawnInk(SwapRecord rec)
    {
        Graphic? g = rec.Graphic;
        if (g == null)
        {
            VRLog.Warn(Scope, $"READBACK SKIPPED for '{rec.Origin}' — the graphic was destroyed before the frame.");
            return;
        }

        Camera? probe = null;
        GameObject? probeGo = null;
        RenderTexture? rt = null;
        RenderTexture? prevActive = RenderTexture.active;
        Texture2D? shot = null;

        try
        {
            RectTransform rtf = g.rectTransform;
            var quad = new Vector3[4];   // 0 bottom-left, 1 top-left, 2 top-right, 3 bottom-right
            rtf.GetWorldCorners(quad);

            Canvas? canvas = FindCanvas(g.transform);
            Camera? source = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            bool worldSpace = canvas != null && canvas.renderMode == RenderMode.WorldSpace;
            bool overlay = canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay;
            // An overlay canvas is composited in screen space with no camera at all, and
            // RectTransformUtility wants a NULL camera for exactly that case — handing it
            // Camera.main would project through a frustum the canvas never went through.
            Camera? mapCam = overlay ? null : source;
            string how;

            int w, h;
            if (worldSpace && source != null)
            {
                w = 1024;
                h = Mathf.Clamp(
                    Mathf.RoundToInt(1024f * source.pixelHeight / Mathf.Max(1, source.pixelWidth)), 64, 2048);
                probeGo = new GameObject("GloomhavenVR.LogoReadbackCam") { hideFlags = HideFlags.HideAndDontSave };
                probeGo.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                probe = probeGo.AddComponent<Camera>();
                // Off before anything else: an enabled camera would join the render loop on its own,
                // and this one exists only to be told to Render() once, into our own target.
                probe.enabled = false;
                probe.CopyFrom(source);
                probe.stereoTargetEye = StereoTargetEyeMask.None;
                probe.aspect = w / (float)h;
                // CopyFrom brings the source's projection matrix along, and in XR that is a
                // per-eye asymmetric one. Resetting it makes the clone use an ordinary symmetric
                // frustum built from the fov and the aspect we just set — which is both what the
                // readback wants and, crucially, the same matrix WorldToViewportPoint will use.
                probe.ResetProjectionMatrix();
                probe.enabled = false;
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                probe.targetTexture = rt;
                probe.Render();
                probe.targetTexture = null;
                how = $"a throwaway clone of '{source.name}' rendered into a {w}x{h} target";
            }
            else
            {
                w = Mathf.Clamp(Screen.width, 64, 4096);
                h = Mathf.Clamp(Screen.height, 64, 4096);
                rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                how = $"the composited {w}x{h} frame (the canvas is {(canvas == null ? "<none>" : canvas.renderMode.ToString())}, "
                      + "so there is no single camera that owns it)";
            }

            RenderTexture.active = rt;
            shot = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false);
            shot.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, recalculateMipMaps: false);
            shot.Apply(false, false);
            Color32[] px = shot.GetPixels32();

            float bandH = BandBottom - BandTop;

            string band = Region(px, w, h, quad, probe, mapCam, worldSpace,
                                 "BAND (GLOOMHAVEN)", BandLeft, BandRight, BandTop, BandBottom,
                                 out float bandUp, out float bandDown);
            string vr = Region(px, w, h, quad, probe, mapCam, worldSpace,
                               "VR", VrLeft, VrRight, VrTop, VrBottom, out float vrUp, out float vrDown);
            string whole = Region(px, w, h, quad, probe, mapCam, worldSpace,
                                  "WHOLE RECT", 0f, 1f, 0f, 1f, out float rectUp, out float rectDown);
            string ctrl = Region(px, w, h, quad, probe, mapCam, worldSpace,
                                 "CONTROL (background above the logo)",
                                 BandLeft, BandRight, BandTop - bandH, BandTop,
                                 out float ctrlUp, out float ctrlDown);

            // Take the orientation that finds the band: the band has artwork in it under EVERY
            // hypothesis (ours or the game's — it is the same wordmark), so it is the one region
            // that can be used to decide which way up the readback landed.
            bool flipped = bandDown > bandUp;
            float bandInk = flipped ? bandDown : bandUp;
            float vrInk = flipped ? vrDown : vrUp;
            float ctrlInk = flipped ? ctrlDown : ctrlUp;
            float rectInk = flipped ? rectDown : rectUp;

            string verdict;
            if (bandInk < InkVerdictThreshold && vrInk < InkVerdictThreshold
                && rectInk < InkVerdictThreshold && ctrlInk < InkVerdictThreshold)
            {
                verdict = "VOID — band, VR, whole rect and the control strip are ALL empty, so this "
                          + "capture never saw the canvas rather than the canvas being empty. A panel "
                          + "drawn through the supersampler's isolated capture layer reads exactly "
                          + "like this. DO NOT read this as 'the logo is absent'; read it as 'this "
                          + "readback path cannot see this canvas' and point the next one at the "
                          + "camera named in the census line for this graphic.";
            }
            else if (bandInk >= InkVerdictThreshold && vrInk >= InkVerdictThreshold)
            {
                verdict = $"BOTH REGIONS HAVE INK (band {bandInk:F3}, VR {vrInk:F3} against the "
                          + "artwork's own 0.58 and 0.41). The wordmark AND the VR are being drawn "
                          + "where the placement put them, so the swap is visually correct and the "
                          + "complaint is about something else — size, position or a second copy of "
                          + "the logo elsewhere on the screen. The census above lists every graphic; "
                          + "that is where to look next, NOT at the placement constants.";
            }
            else if (bandInk >= InkVerdictThreshold)
            {
                verdict = $"HYPOTHESIS (B): the band has ink ({bandInk:F3}) and the VR box does not "
                          + $"({vrInk:F3} against the artwork's 0.41). Something is drawing the "
                          + "GLOOMHAVEN letters and not the VR — either the old sprite is still on "
                          + "this graphic (the re-assert watch below says whether it was put back), "
                          + "or our sprite is drawn and its right-hand end is CLIPPED OR OCCLUDED. "
                          + "The census line for this graphic gives the canvas, its sorting order and "
                          + "the screen box; a sibling drawn after it and covering that box is the "
                          + "next thing to check.";
            }
            else
            {
                verdict = $"NEITHER REGION HAS INK (band {bandInk:F3}, VR {vrInk:F3}) while the "
                          + $"surrounding frame does (whole rect {rectInk:F3}, control {ctrlInk:F3}). "
                          + "The graphic is NOT where this patch thinks it is: the rect we sized and "
                          + "moved does not project onto the wordmark the player sees. That makes the "
                          + "visible wordmark a DIFFERENT graphic — hypothesis (A) — and the census "
                          + "above names every candidate.";
            }

            VRLog.Note(Scope,
                $"READBACK on '{rec.Origin}' ({rec.Path}) from {how}; orientation used: "
                + $"{(flipped ? "TOP-DOWN (the readback landed flipped)" : "BOTTOM-UP (as read)")}. "
                + $"{band} {vr} {whole} {ctrl} VERDICT: {verdict}");
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope,
                $"READBACK FAILED for '{rec.Origin}' ({ex.GetType().Name}: {ex.Message}). Nothing was "
                + "measured, so this round has the census and the re-assert watch and nothing else. "
                + "The swap itself is unaffected — this is a diagnostic and it is fully swallowed.");
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (probe != null)
                probe.targetTexture = null;
            if (shot != null)
                UnityEngine.Object.Destroy(shot);
            if (rt != null)
                RenderTexture.ReleaseTemporary(rt);
            if (probeGo != null)
                UnityEngine.Object.DestroyImmediate(probeGo);
        }
    }

    /// <summary>
    /// One region of the sprite, projected onto the captured picture and measured both ways up.
    /// <paramref name="t0"/>/<paramref name="t1"/> run DOWN from the top of the rect, like every other
    /// fraction in this file, so they can be read straight off the artwork constants.
    /// </summary>
    private static string Region(
        Color32[] px, int w, int h, Vector3[] quad, Camera? probe, Camera? mapCam, bool viewport,
        string label, float u0, float u1, float t0, float t1, out float inkUp, out float inkDown)
    {
        inkUp = 0f;
        inkDown = 0f;

        // The rect is planar and its local coordinates are affine, so bilinear interpolation between
        // the four world corners is exact, perspective or not.
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        bool behind = false;
        for (int c = 0; c < 4; c++)
        {
            float u = (c == 0 || c == 1) ? u0 : u1;
            float t = (c == 0 || c == 3) ? t1 : t0;
            Vector3 world = quad[0] + (quad[3] - quad[0]) * u + (quad[1] - quad[0]) * (1f - t);

            float sx, sy;
            if (viewport && probe != null)
            {
                Vector3 v = probe.WorldToViewportPoint(world);
                if (v.z <= 0f) behind = true;
                sx = v.x * w;
                sy = v.y * h;
            }
            else
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(mapCam, world);
                sx = sp.x * (w / (float)Mathf.Max(1, Screen.width));
                sy = sp.y * (h / (float)Mathf.Max(1, Screen.height));
            }

            if (sx < x0) x0 = sx;
            if (sy < y0) y0 = sy;
            if (sx > x1) x1 = sx;
            if (sy > y1) y1 = sy;
        }

        if (behind)
            return $"{label}: BEHIND THE CAMERA, not measured.";

        // Clamped to [0, size] on BOTH ends, not to [0, size-1] on the low one: a region that lies
        // entirely off the right-hand edge would otherwise collapse onto the last column and be
        // reported as a one-pixel-wide measurement of the picture's border instead of as "outside".
        int px0 = Mathf.Clamp(Mathf.FloorToInt(x0), 0, w);
        int px1 = Mathf.Clamp(Mathf.CeilToInt(x1), 0, w);
        int py0 = Mathf.Clamp(Mathf.FloorToInt(y0), 0, h);
        int py1 = Mathf.Clamp(Mathf.CeilToInt(y1), 0, h);
        int count = Mathf.Max(0, px1 - px0) * Mathf.Max(0, py1 - py0);
        if (count <= 0)
            return $"{label}: projects OUTSIDE the captured picture "
                   + $"({x0:F0},{y0:F0})..({x1:F0},{y1:F0}) of {w}x{h}, not measured.";

        string up = Stats(px, w, h, px0, py0, px1, py1, flip: false, out inkUp);
        string down = Stats(px, w, h, px0, py0, px1, py1, flip: true, out inkDown);
        return $"{label}: box ({px0},{py0})..({px1},{py1}) of {w}x{h}, {count} px; bottom-up {up}; top-down {down}.";
    }

    /// <summary>Ink statistics over one pixel box. <paramref name="flip"/> mirrors the row index, so
    /// the caller gets both orientations from one readback and never has to guess.</summary>
    private static string Stats(
        Color32[] px, int w, int h, int x0, int y0, int x1, int y1, bool flip, out float bright)
    {
        long n = 0, nBright = 0, nLit = 0;
        double sr = 0, sg = 0, sb = 0;
        float maxLuma = 0f;

        for (int y = y0; y < y1; y++)
        {
            int row = (flip ? (h - 1 - y) : y) * w;
            if (row < 0 || row > (h - 1) * w)
                continue;
            for (int x = x0; x < x1; x++)
            {
                Color32 c = px[row + x];
                float luma = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                n++;
                sr += c.r; sg += c.g; sb += c.b;
                if (luma > maxLuma) maxLuma = luma;
                if (luma > 0.235f) nBright++;   // 60/255, the artwork's own letter fill
                if (luma > 0.100f) nLit++;
            }
        }

        bright = n > 0 ? nBright / (float)n : 0f;
        float lit = n > 0 ? nLit / (float)n : 0f;
        return $"bright={bright:F3} lit={lit:F3} mean=({sr / Math.Max(1, n) / 255.0:F3},"
               + $"{sg / Math.Max(1, n) / 255.0:F3},{sb / Math.Max(1, n) / 255.0:F3}) max={maxLuma:F3}";
    }

    // =============================================================================================
    //  FIX 2 — SURVIVE A RE-ASSIGNMENT, and say so even when nothing happens
    // =============================================================================================

    /// <summary>
    /// Arm the one coroutine that carries both instruments and the re-assert watch.
    ///
    /// <para>Bounded on purpose and in three ways: it waits at most
    /// <see cref="ShownPollLimit"/> * <see cref="ShownPollSeconds"/> seconds for the menu to be
    /// shown, it polls at 2 Hz for <see cref="WatchTicks"/> ticks and then stops for good, and the
    /// only per-tick work on a quiet menu is a field comparison per swapped graphic. The full scene
    /// sweep runs every <see cref="WatchDeepSweepEvery"/> ticks, not every tick — a scene-wide
    /// FindObjectsOfTypeAll at 2 Hz is precisely the kind of thing that once owned 12.6 ms of an
    /// 11.11 ms frame in this project.</para>
    ///
    /// <para>The manager is the host because its lifetime IS the main menu's: leave the menu and the
    /// coroutine dies with the scene, which is exactly when the watch should stop.</para>
    /// </summary>
    internal static void ArmWatch(MainMenuUIManager? manager, Sprite ours, int records)
    {
        if (manager == null || !manager.isActiveAndEnabled)
        {
            VRLog.Warn(Scope,
                "RE-ASSERT WATCH NOT ARMED AND THE READBACK DID NOT RUN — there is no live "
                + "MainMenuUIManager to host the coroutine. The census below is taken immediately "
                + "instead, at Awake, before anything has been drawn; treat its 'active' and "
                + "'inheritedAlpha' columns as provisional.");
            try { Census("no coroutine host, taken at Awake", ours); }
            catch (Exception ex) { VRLog.Warn(Scope, $"Census threw and was swallowed: {ex.Message}"); }
            return;
        }

        VRLog.Info(Scope,
            $"RE-ASSERT WATCH ARMED on {records} swapped graphic(s): 2 Hz for "
            + $"{WatchTicks * WatchTickSeconds:F0} s, with a full scene sweep every "
            + $"{WatchDeepSweepEvery * WatchTickSeconds:F0} s. A CLOSING LINE IS PRINTED EITHER WAY — "
            + "if nothing ever reverted it will say so, because a guard that only speaks when it "
            + "fires is indistinguishable from a guard that never ran.");

        try
        {
            manager.StartCoroutine(WatchRoutine(ours));
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope, $"RE-ASSERT WATCH FAILED TO START ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    /// <summary>The one coroutine: wait for the menu to actually be on screen, take both
    /// instruments, then watch.</summary>
    internal static IEnumerator WatchRoutine(Sprite ours)
    {
        // ---- wait for the menu to be SHOWN. MainMenuUIManager.Awake fires while the scene is still
        //      loading and InitialiseMainMenu only runs once PersistentData has loaded, so a readback
        //      taken at the first end-of-frame would measure a menu that is not up yet and would
        //      report an empty band with total confidence.
        bool nothingSwapped = MainMenuLogoSwap.Records.Count == 0;
        int waited = 0;
        while (waited < ShownPollLimit && !AnyRecordVisible())
        {
            waited++;
            yield return new WaitForSeconds(ShownPollSeconds);
        }
        bool shown = AnyRecordVisible();
        yield return new WaitForEndOfFrame();

        VRLog.Info(Scope,
            (nothingSwapped
                 ? $"NOTHING WAS SWAPPED, so there is no graphic to wait for; the full "
                   + $"{waited * ShownPollSeconds:F1} s were waited out so that the census below is "
                   + "taken with the menu up rather than mid-load"
                 : $"MENU {(shown ? "IS SHOWN" : "NEVER BECAME VISIBLE")} after "
                   + $"{waited * ShownPollSeconds:F1} s")
            + "; taking the census and the readback now"
            + (shown || nothingSwapped
                   ? "."
                   : " ANYWAY — every measurement below is of a swapped graphic that never became "
                     + "active, which is itself the finding: the object we swapped is not the one "
                     + "the menu shows."));

        try { Census(shown ? "after the menu was shown" : "menu never became visible", ours); }
        catch (Exception ex) { VRLog.Warn(Scope, $"Census threw and was swallowed: {ex.Message}"); }

        // Graphics that were created after our Awake postfix ran are only reachable from here.
        try { MainMenuLogoSwap.SweepScene(ours, "after the menu was shown"); }
        catch (Exception ex) { VRLog.Warn(Scope, $"Late sweep threw and was swallowed: {ex.Message}"); }

        IReadOnlyList<SwapRecord> records = MainMenuLogoSwap.Records;
        if (records.Count == 0)
        {
            VRLog.Warn(Scope,
                "READBACK SKIPPED — nothing was swapped, so there is no rect to measure. The census "
                + "above is this round's only evidence and it is the one that matters: it names every "
                + "graphic in the scene.");
        }
        else
        {
            for (int i = 0; i < records.Count; i++)
            {
                try { MeasureDrawnInk(records[i]); }
                catch (Exception ex) { VRLog.Warn(Scope, $"Readback threw and was swallowed: {ex.Message}"); }
            }
        }

        // ---- the watch itself
        int repairs = 0, lateSwaps = 0, ticks = 0, sweeps = 0;
        for (int i = 0; i < WatchTicks; i++)
        {
            yield return new WaitForSeconds(WatchTickSeconds);
            ticks++;
            bool deep = (i % WatchDeepSweepEvery) == WatchDeepSweepEvery - 1;
            if (deep)
                sweeps++;
            try
            {
                MainMenuLogoSwap.RunWatchTick(ours, deep, out int repaired, out int swept);
                repairs += repaired;
                lateSwaps += swept;
            }
            catch (Exception ex)
            {
                VRLog.Warn(Scope, $"Re-assert tick threw and was swallowed: {ex.Message}");
            }
        }

        VRLog.Note(Scope,
            $"RE-ASSERT WATCH CLOSED after {ticks} tick(s) over {ticks * WatchTickSeconds:F0} s and "
            + $"{sweeps} scene sweep(s): {repairs} repair(s), {lateSwaps} late graphic(s) swapped, across "
            + $"{MainMenuLogoSwap.Records.Count} watched graphic(s). "
            + (repairs == 0 && lateSwaps == 0
                   ? "NOTHING EVER REVERTED AND NOTHING NEW APPEARED — the watch ran and found nothing, "
                     + "which agrees with the decompiled sources: no code in GH.Runtime assigns a "
                     + "sprite to the main-menu wordmark. Re-assignment is therefore ruled out, and "
                     + "whatever is wrong is in the READBACK line above, not here."
                   : "The lines above name what changed and when."));
    }

    /// <summary>
    /// Whether at least one swapped graphic is switched on.
    ///
    /// <para>Deliberately NOT gated on the inherited alpha, even though a zero there would mean the
    /// graphic draws nothing. A CanvasRenderer's inherited alpha is only refreshed on a layout pass,
    /// so a gate on it can sit closed while the menu is plainly up — and this project has already
    /// spent builds on a stillness gate that never opened. The alpha is REPORTED by the census and
    /// judged by the readback instead, where being wrong about it costs a log line rather than the
    /// whole measurement.</para>
    /// </summary>
    private static bool AnyRecordVisible()
    {
        IReadOnlyList<SwapRecord> records = MainMenuLogoSwap.Records;
        for (int i = 0; i < records.Count; i++)
        {
            Graphic? g = records[i].Graphic;
            if (g != null && g.IsActive() && g.enabled)
                return true;
        }
        return false;
    }
}

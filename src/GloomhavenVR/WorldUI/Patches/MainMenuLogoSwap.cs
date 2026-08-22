using System;
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
/// <see cref="MainMenuLogoPlacement.PlaceOnBand"/> therefore GROWS the rect by the ratio of the sprite's height to its
/// GLOOMHAVEN band and offsets it so the band lands on the box the old sprite drew in; the sprite
/// is never stretched, because the new rect is made exactly the sprite's own aspect. The measured
/// band constants and the arithmetic are documented on <see cref="MainMenuLogoPlacement"/>,
/// and every number is logged so the result can be judged from a
/// Player.log rather than only from a photograph.</para>
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
/// </summary>
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

        int swapped = 0;
        GameObject? primary = manager != null ? manager.Logo : null;
        swapped += SwapUnder(primary, "MainMenuUIManager._logo", sprite);

        GameObject? secondary = FindBackgroundViewLogo();
        if (secondary != null && secondary != primary)
            swapped += SwapUnder(secondary, "BackgroundView.m_Logo", sprite);

        if (swapped == 0)
        {
            VRLog.Warn(Scope,
                "MAIN-MENU LOGO NOT SWAPPED — no Image/RawImage carrying artwork was found under " +
                $"MainMenuUIManager._logo (host={Describe(primary)}) or BackgroundView.m_Logo " +
                $"(host={Describe(secondary)}). The menu still shows the GAME's wordmark. The " +
                "subtree dump above says what is actually on those objects; the resolver takes " +
                "the largest sprite-bearing graphic, so a host built from a Text/TMP object or a " +
                "spriteless Image will land here.");
        }
        else
        {
            VRLog.Note(Scope, $"Main-menu logo replaced with the GloomhavenVR wordmark ({swapped} graphic(s)).");
        }
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

        return ApplyTo(best, sprite, origin) ? 1 : 0;
    }

    private static bool ApplyTo(Graphic graphic, Sprite sprite, string origin)
    {
        var rect = graphic.rectTransform.rect;
        float hostAspect = rect.height > 0.001f ? rect.width / rect.height : 0f;
        float newAspect = sprite.rect.height > 0.001f ? sprite.rect.width / sprite.rect.height : 0f;

        if (graphic is Image image)
        {
            if (ReferenceEquals(image.sprite, sprite))
                return true; // already ours — never grow the rect twice

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
                "Colour, material, parent, canvas order and every sibling untouched.");
            return true;
        }

        if (graphic is RawImage rawImage)
        {
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
                "Worth a look in the headset.");
            return true;
        }

        VRLog.Warn(Scope, $"{origin}: '{graphic.name}' is a {graphic.GetType().Name}, which this patch cannot skin.");
        return false;
    }


    /// <summary>
    /// The second candidate, resolved without a per-frame cost (this runs once per menu load).
    /// <c>Resources.FindObjectsOfTypeAll</c> and not <c>FindObjectOfType</c>: the background view
    /// may legitimately be disabled when the menu manager wakes, and an inactive object is exactly
    /// the case <c>FindObjectOfType</c> cannot see. Prefab assets are filtered out by requiring a
    /// real scene.
    /// </summary>
    private static GameObject? FindBackgroundViewLogo()
    {
        foreach (BackgroundView view in Resources.FindObjectsOfTypeAll<BackgroundView>())
        {
            if (view == null || !view.gameObject.scene.IsValid())
                continue; // prefab / asset instance, not the live menu
            if (view.m_Logo != null)
                return view.m_Logo;
        }
        return null;
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
    // MEASURED off Assets/GloomhavenVR_logo.png (1024x179 RGBA) at alpha > 0.08, by taking each
    // column's first and last covered row. The wordmark's own band is rows 4..127 and holds flat
    // across the whole GLOOMHAVEN run; from column 838 the bottom drops away to row 168 — that is
    // the VR's descending flourish, and it is the ONLY reason the shipped PNG is taller than the
    // original artwork. Measuring at 0.02 / 0.25 / 0.50 instead moves these by at most one pixel
    // (0.6 % of the band), so the numbers are not threshold-sensitive.
    //
    // THEY ARE FRACTIONS OF THE SPRITE, NOT PIXELS, on purpose: a future re-export at another
    // resolution keeps working as long as the composition is unchanged, and a re-export that MOVES
    // the wordmark inside the canvas is a change to these four numbers and to nothing else.

    /// <summary>Top of the GLOOMHAVEN band, as a fraction of sprite height from the TOP edge.</summary>
    private const float BandTop = 4f / 179f;

    /// <summary>Bottom of the GLOOMHAVEN band (exclusive), as a fraction of sprite height from the
    /// top edge. Everything below this is the VR's flourish and is allowed to hang past where the
    /// original logo ended.</summary>
    private const float BandBottom = 128f / 179f;

    /// <summary>Left edge of the wordmark, as a fraction of sprite width. Non-zero only because the
    /// crop keeps an 8 px margin for the rim glow.</summary>
    private const float BandLeft = 4f / 1024f;

    /// <summary>Height of the GLOOMHAVEN band as a fraction of the sprite's height — the number the
    /// whole placement turns on: the new rect is this much TALLER than the box the old wordmark
    /// drew in, so that the band inside it comes out the original size.</summary>
    private const float BandHeight = BandBottom - BandTop;

    /// <summary>Alpha at or below which a pixel counts as empty margin, 0..255. Deliberately low:
    /// this hunts the outer edge of a glow, and a high threshold would crop into it and make the
    /// replacement wordmark slightly too large.</summary>
    private const byte InkAlpha = 20;

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
            int x0 = sw, x1 = -1, y0 = sh, y1 = -1, hits = 0;
            for (int y = 0; y < sh; y++)
            {
                int row = y * sw;
                for (int x = 0; x < sw; x++)
                {
                    if (px[row + x].a <= InkAlpha)
                        continue;
                    hits++;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }

            if (hits == 0 || x1 < x0 || y1 < y0)
            {
                note = $"ink NOT found — every one of the {sw}x{sh} sampled pixels is at or below "
                       + $"alpha {InkAlpha}. Either the source has no alpha channel at all (then the "
                       + "whole rect IS the artwork and this is correct) or the readback failed "
                       + "silently; the whole rect is used";
                return whole;
            }

            // ReadPixels' origin is BOTTOM-left; the band constants run from the TOP. Flip here so
            // everything downstream shares one convention.
            float fx = x0 / (float)sw;
            float fw = (x1 - x0 + 1) / (float)sw;
            float fyTop = 1f - (y1 + 1) / (float)sh;
            float fh = (y1 - y0 + 1) / (float)sh;

            note = $"ink measured on a {sw}x{sh} readback: x {fx:F4}..{fx + fw:F4}, y (from top) "
                   + $"{fyTop:F4}..{fyTop + fh:F4}, {hits} lit pixel(s), so {(1f - fw * fh) * 100f:F1}% "
                   + "of the old asset is transparent margin";
            return new Rect(fx, fyTop, fw, fh);
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
    /// <para>THE ARITHMETIC. The band is <see cref="BandHeight"/> of the sprite's height, so the
    /// new rect must be <c>oldInk.height / BandHeight</c> tall (≈ 1.44×) for the band inside it to
    /// come out at the original height; the width follows from the sprite's own aspect, so nothing
    /// is ever stretched. The rect is then offset so the band's top-left corner sits on
    /// <c>oldInk</c>'s top-left. Net effect: the wordmark is the size and position it always was
    /// and the rect around it is bigger — the opposite of ModBuild 220, which kept the rect and
    /// shrank the wordmark into it.</para>
    ///
    /// <para>A CONSISTENCY CHECK THAT COSTS NOTHING AND HAS ALREADY EARNED ITS KEEP — it is what
    /// caught ModBuild 221, reporting 0.426 against a plausible 0.90 in the very log the user sent
    /// with the photograph. If the new artwork really is the game's own wordmark plus VR, then the
    /// old ink box's aspect IS the band's aspect, which puts the band's right edge at <c>oldAspect · BandHeight / spriteAspect</c> of
    /// the sprite width. Independently measured, the VR's first column is at 0.818 and the N runs
    /// on underneath it, so anything in roughly 0.82..1.00 corroborates the assumption. A value
    /// outside 0.70..1.02 does not, and says so in the log rather than quietly mis-scaling.</para>
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

        float oldAspect = oldInk.width / oldInk.height;
        float impliedBandRight = oldAspect * BandHeight / newAspect;

        float h2 = oldInk.height / BandHeight;
        float w2 = h2 * newAspect;

        // The band's top-left inside the new rect, and the offset that puts it on the old box's.
        float targetXMin = oldInk.xMin - BandLeft * w2;
        float targetYMin = (oldInk.yMax + BandTop * h2) - h2;

        Vector2 pivot = rt.pivot;
        string anchors = CollapseAnchors(rt);

        Vector2 delta = new Vector2(targetXMin + pivot.x * w2, targetYMin + pivot.y * h2);
        rt.sizeDelta = new Vector2(w2, h2);
        rt.anchoredPosition += delta;

        string verdict = impliedBandRight is > 0.70f and < 1.02f
            ? $"consistent with the artwork (VR starts at 0.818 of the width and the N runs on under it)"
            : "OUT OF RANGE — the new artwork is probably NOT the game's wordmark plus VR, or the "
              + "original logo asset carries padding this patch cannot see. The wordmark will be "
              + "the wrong size; the fix is the four Band* constants";

        return $"rect {oldInk.width:F1}x{oldInk.height:F1} → {w2:F1}x{h2:F1} (×{h2 / oldInk.height:F3}), "
               + $"moved by ({delta.x:F1},{delta.y:F1}), {anchors} The GLOOMHAVEN band now covers the "
               + $"old drawn box exactly and the VR hangs past it. Implied band right edge "
               + $"{impliedBandRight:F3} of sprite width: {verdict}.";
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
}

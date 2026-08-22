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
/// material and every animator on the object all stay exactly as the scene authored them. The one
/// property this patch does change beyond the sprite is <c>preserveAspect = true</c> (with
/// <c>type = Simple</c>, which is what <c>preserveAspect</c> requires): the new wordmark reads
/// "GLOOMHAVEN VR" on one line and is therefore WIDER per unit of height than the original, and
/// letterboxing it inside the authored rect is the only way to keep it in the same screen box
/// without stretching the letters. Both aspects are logged so a follow-up round can judge the
/// result from a Player.log instead of a screenshot.</para>
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
/// exactly what "genauso wie jetzt" forbids; (c) resizing the RectTransform to our aspect —
/// it is hand-authored screen furniture and a mod has no business moving it.</para>
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
            string wasSprite = image.sprite != null ? image.sprite.name : "<none>";
            Rect wasRect = image.sprite != null ? image.sprite.rect : default;
            Image.Type wasType = image.type;

            image.sprite = sprite;
            // Unity's overrideSprite GETTER falls back to `sprite`, so an active override is
            // invisible to a null test and would silently keep drawing the old art. Clearing it is
            // safe here: every overrideSprite writer in GH.Runtime is an interactive-widget
            // transition helper (UITab.cs:272, UISelectField.cs:529, UISelectField_Option.cs:189,
            // UISelectField_Arrow.cs:123, UIButtonExtended_Target.cs:270,
            // UIInputFieldLayoutExtension.cs:239, UIHighlightTransition.cs:239) — none of them is
            // a static logo.
            image.overrideSprite = null;
            // preserveAspect only applies to Simple and Filled; the wordmark is never 9-sliced.
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.SetAllDirty();

            VRLog.Info(Scope,
                $"{origin}: Image '{image.name}' sprite '{wasSprite}' " +
                $"({wasRect.width:F0}x{wasRect.height:F0}, aspect {(wasRect.height > 0.001f ? wasRect.width / wasRect.height : 0f):F3}) " +
                $"→ '{sprite.name}' ({sprite.rect.width:F0}x{sprite.rect.height:F0}, aspect {newAspect:F3}); " +
                $"rect {rect.width:F0}x{rect.height:F0} (aspect {hostAspect:F3}) UNCHANGED, type {wasType}→Simple, " +
                "preserveAspect on. Colour, material, anchors and canvas order untouched.");
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

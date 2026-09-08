using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// MP BUG #7 (name label): the flat game shows the pinging PLAYER'S NAME next to every hex
/// ping — a <c>UIPingTooltip</c> on a SCREEN-SPACE canvas positioned by projecting the hex
/// through <c>WorldspaceUITools.WorldspaceCamera</c> (UIPingTooltip.cs:113-121). In VR that
/// projection uses the flat scenario camera, so the label is invisible/misplaced for a VR
/// player; only the world-space 3D highlight survives.
///
/// FIX: postfix on the game's single ping-display chokepoint,
/// <c>PingManager.Ping3DElement(GameObject element, NetworkPlayer player)</c> (private; BOTH
/// public entries funnel into it, and the network receive path
/// <c>ProxyPingHex → Ping3DElementMultiPlayer</c> does too — PingManager.cs:63-88,135). So the
/// world-space VR name tag appears for our own pings AND for every ping received from any
/// peer, flat or VR, with the exact name the flat game would show
/// (<c>player?.Username ?? PlatformLayer.UserData.UserName</c>, PingManager.cs:81).
///
/// The <c>player</c> argument is declared as <c>object</c>: <c>FFSNet.NetworkPlayer</c> is
/// Bolt-derived and this mod deliberately has no bolt.dll reference (see
/// <see cref="Net.NetPlayerActors"/>); Username/PlayerID are read via cached reflection.
///
/// Vanilla-mirroring rules (PingManager.cs:73-93): one live ping per (element, player) — a
/// repeat while shown is a no-op; a NEW ping removes the same player's previous ping and any
/// other ping on the same element (<c>HidePing(player, element)</c>). Lifetime mirrors the
/// manager's own <c>lifetimePing</c> (2 s default), with a short fade tail.
///
/// COSMETIC ONLY: renders a mod-owned world-space copy of the game's own tooltip; never
/// touches game state, no wire bytes of ours (the name arrives via the game's own PingHex
/// payload/registry — second-source-of-truth rule, INVARIANTS-Net-Rig.md). Only active while
/// VR runs; the flat tooltip keeps doing its job on desktop. Degrades to a silent no-op if
/// reflection fails.
/// </summary>
[HarmonyPatch]
internal static class PingNameTag_Patch
{
    /// <summary>Private chokepoint — resolved by name (single method, no overloads), so this
    /// class never types the Bolt-derived NetworkPlayer parameter. Degrades by design:
    /// TargetMethod may return null on a changed game build (patch then no-ops).</summary>
    private static MethodBase? TargetMethod()
    {
        MethodBase? m = AccessTools.Method(typeof(PingManager), "Ping3DElement");
        if (m == null)
            // HW-VERIFY (2026-09 refactor, F-37) — a feature self-disarm: name tags are off for
            // the session. Once.
            VRLog.Alert("Board", "[Ping] PingManager.Ping3DElement not found — ping name tags disabled " +
                "(game's own ping visuals unaffected).");
        return m;
    }

    private static void Postfix(GameObject element, object? player, PingManager __instance)
    {
        try
        {
            if (!VRSession.IsRunning || element == null)
                return;
            PingNameTag.OnPingShown(element, player, __instance);
        }
        catch (Exception e)
        {
            // A SWALLOWED THROW MUST NEVER BE SILENT (2026-09 refactor, F-37) — this catch keeps
            // the game's own method alive and hides the failure completely at Warn.
            VRLog.Error("Board", $"[Ping] name-tag postfix failed (suppressed): {e.Message}");
        }
    }
}

/// <summary>
/// The world-space name tag itself, floating above the pinged hex and billboarded to the
/// local head every frame.
///
/// ORIGINAL-DESIGN REBUILD (user request 2026-08: "Ich moechte, dass das Original-Design vom
/// Spiel genutzt wird beim Namenstag"): the tag used to be a bare mod-drawn TMP line, which
/// looked nothing like the game's ping tooltip. It is now a CLONE of the game's own tooltip
/// PREFAB — <c>PingManager.pingTooltipPrefab</c> (publicized [SerializeField],
/// PingManager.cs:29-30) — parented under a mod-owned WORLD-SPACE canvas at the marker, so
/// the background sprite, font, material, colors, layout AND the platform-icon TMP sprite
/// come from the game's own assets, pixel-identical by construction. Route chosen: CLONE
/// (over a pixel-for-pixel rebuild) per the <see cref="Net.RemoteWidgetMirror"/> precedent —
/// its Neutralize pipeline is reused here in miniature; see <see cref="Neutralize"/> for the
/// one deliberate difference.
///
/// The prefab is cloned PRISTINE (authored serialized state) rather than duplicating the
/// LIVE screen-space instance: the live tooltip is cloned mid-show-animation (GUIAnimator
/// runtime state does not survive Instantiate — non-serialized fields reset), so a live copy
/// can freeze at alpha/scale 0; the prefab's authored state is the fully-visible design.
/// The TEXT however is read from the live instance the game just configured
/// (<see cref="FindVanillaTooltipText"/> matches <c>UIPingTooltip.m_ObjectToTrack</c> to the
/// pinged element), so the string — platform icon sprite tag + masked username, composed by
/// PingManager.cs:81-82 — is the exact one the flat game shows, with a reflection-free
/// fallback to the mod-resolved name.
///
/// FALLBACK: if the prefab is missing, the clone comes out degenerate (e.g. a game update
/// re-anchors it stretch-style so it collapses against our zero-size frame) or anything
/// throws, the old TMP label is built instead — the tag NEVER silently disappears.
/// Self-expires after the manager's ping lifetime with an alpha fade (CanvasGroup on the
/// clone; TMP color on the fallback). Billboard convention as before (<see cref="Net.OwnerTag"/>:
/// text reads from -Z, so aim +Z away from the head).
/// </summary>
internal sealed class PingNameTag : MonoBehaviour
{
    // World-unit metrics (game tile ~= 1.72 world units across).
    private const float Rise = 2.4f;        // label height above the hex center
    private const float FadeTail = 0.35f;   // alpha fade-out at end of life (seconds)
    private const float DefaultLifetime = 2f; // PingManager.lifetimePing fallback

    /// <summary>Target world height of the cloned tooltip (backdrop included) — reads like a
    /// small caption over the ~1.72-unit hex, matching the old tag's presence.</summary>
    private const float TooltipWorldHeight = 0.9f;

    /// <summary>Width clamp (world units) so an extreme name cannot span the diorama — the old
    /// tag's text-box width, kept as the ceiling.</summary>
    private const float TooltipMaxWorldWidth = 4.6f;

    /// <summary>Below this measured pixel size the clone is treated as degenerate (collapsed
    /// stretch anchors / empty prefab) and the fallback label is used instead.</summary>
    private const float MinPrefabPixels = 4f;

    // Fallback-label metrics (the pre-clone design, kept as the degrade path).
    private const float FallbackWidth = 4.6f;
    private const float FallbackHeight = 0.85f;

    /// <summary>Live tags, for the vanilla one-per-(element,player) / replacement rules.</summary>
    private static readonly List<PingNameTag> Live = new();

    // Cached reflection into FFSNet.NetworkPlayer (Bolt-derived — never typed here) and the
    // local-name fallback chain PlatformLayer.UserData.UserName (platform assembly).
    private static bool _reflected;
    private static PropertyInfo? _playerId;   // int NetworkPlayer.PlayerID
    private static PropertyInfo? _username;   // string NetworkPlayer.Username (already bad-word-masked)
    private static PropertyInfo? _userData;   // static PlatformLayer.UserData
    private static PropertyInfo? _userName;   // PlatformUserData.UserName

    private int _elementId;
    private int _playerKey;
    private float _dieAt;                    // unscaled time
    private CanvasGroup? _group;             // clone route: fade carrier
    private Canvas? _canvas;                 // clone route: the world-space canvas that draws it
    private TextMeshPro? _label;             // fallback route: mod-drawn TMP line

    // The tag's own renderers for the panel-ladder compositing (BoardVisual.OrderWithPanels — the
    // "the menu behind it mixes with the label" fix); same cache contract as OwnerTag/RemoteNameTag.
    private Renderer[] _tagRenderers = Array.Empty<Renderer>();
    private int _tagRenderersRefreshAt;
    private const int TagRenderersRefreshFrames = 90; // ~1 s at 90 Hz, see RemoteNameTag
    private Color _baseColor;
    private Action? _tickCached;             // [Optimize] CacheTickDelegates — see BoardPing.Update

    /// <summary>Show (or vanilla-dedupe) the tag for one <c>Ping3DElement</c> call.</summary>
    internal static void OnPingShown(GameObject element, object? player, PingManager manager)
    {
        EnsureReflected();

        int playerKey = 0;
        string? name = null;
        if (player != null)
        {
            try
            {
                playerKey = _playerId?.GetValue(player) is int id ? id : player.GetHashCode();
                name = _username?.GetValue(player) as string;
            }
            catch { /* identity unreadable → local fallback below */ }
        }
        if (string.IsNullOrEmpty(name))
            name = LocalUserName();
        if (string.IsNullOrEmpty(name))
            name = playerKey != 0 ? $"Player {playerKey}" : null; // same fallback OwnerTag ships

        // The exact string the game's own tooltip shows for THIS ping (platform icon + name).
        string? vanillaText = FindVanillaTooltipText(manager, element);
        if (vanillaText == null && name == null)
            return; // nothing presentable — cosmetic feature, skip silently

        int elementId = element.GetInstanceID();

        // Vanilla IsPingShown mirror: same element + same player while alive → no-op.
        for (int i = 0; i < Live.Count; i++)
        {
            PingNameTag t = Live[i];
            if (t != null && t._elementId == elementId && t._playerKey == playerKey)
                return;
        }

        // Vanilla HidePing(player, element) mirror: a new ping replaces the same player's
        // previous tag and any tag already sitting on this element.
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PingNameTag t = Live[i];
            if (t == null)
            {
                Live.RemoveAt(i);
                continue;
            }
            if (t._playerKey == playerKey || t._elementId == elementId)
            {
                // Eager removal: Unity's Destroy is end-of-frame, so a same-frame follow-up
                // ping must not dedupe against this dying tag (vanilla removes instantly too).
                Live.RemoveAt(i);
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        float lifetime = DefaultLifetime;
        try
        {
            // Publicized private serialized field — the game's own ping lifetime.
            lifetime = manager.lifetimePing > 0f ? manager.lifetimePing : DefaultLifetime;
        }
        catch { /* keep default */ }

        var go = new GameObject($"GloomhavenVR.PingNameTag[{playerKey}]");
        go.transform.position = element.transform.position + Vector3.up * Rise;
        PingNameTag tag = go.AddComponent<PingNameTag>();
        tag._elementId = elementId;
        tag._playerKey = playerKey;
        tag._dieAt = Time.unscaledTime + lifetime;
        tag.Build(manager, vanillaText, name);
        Live.Add(tag);
    }

    /// <summary>Clone route first, mod label second (see the class remarks for why either).</summary>
    private void Build(PingManager manager, string? vanillaText, string? fallbackName)
    {
        if (!TryBuildVanillaClone(manager, vanillaText ?? fallbackName))
        {
            // Prefer the PLAIN name here: a vanilla string carries a <sprite name="..."> tag
            // that only resolves against the game TMP component's sprite asset — on the bare
            // mod label it would render as a missing-glyph box.
            BuildFallbackLabel(fallbackName ?? vanillaText ?? "?");
        }
        VRLayers.Apply(gameObject);
    }

    /// <summary>
    /// The game-design route: world-space canvas on this GameObject, pristine clone of
    /// <c>PingManager.pingTooltipPrefab</c> under it, neutralized to pure presentation +
    /// layout, text injected, then measured and scaled to <see cref="TooltipWorldHeight"/>.
    /// Built under the INACTIVE root so no cloned game script ever reaches Awake
    /// (RemoteWidgetMirror/RemoteCardArt's proven ordering) — which also guarantees
    /// DestroyImmediate below never triggers a game OnDestroy.
    /// </summary>
    private bool TryBuildVanillaClone(PingManager manager, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        UIPingTooltip? prefab;
        try
        {
            prefab = manager.pingTooltipPrefab; // publicized [SerializeField], PingManager.cs:29-30
        }
        catch
        {
            prefab = null;
        }
        if (prefab == null)
        {
            WarnCloneUnavailableOnce("PingManager.pingTooltipPrefab is null/renamed");
            return false;
        }

        gameObject.SetActive(false);
        try
        {
            var canvas = gameObject.AddComponent<Canvas>(); // auto-upgrades our Transform to RectTransform
            _canvas = canvas;                               // ranked on the panel ladder every Tick
            canvas.renderMode = RenderMode.WorldSpace;
            Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
            if (head != null)
                canvas.worldCamera = head;
            var rootRect = (RectTransform)transform;
            rootRect.sizeDelta = Vector2.zero; // the clone is centered, never stretch-resolved

            GameObject clone = Instantiate(prefab.gameObject, transform, false);
            clone.name = "VanillaPingTooltipClone";

            // Grab the authored text target BEFORE the component carrying the reference dies.
            UIPingTooltip? ui = clone.GetComponent<UIPingTooltip>();
            TextMeshProUGUI? label = ui != null ? ui.descriptionText : null;
            if (label == null)
            {
                WarnCloneUnavailableOnce("UIPingTooltip.descriptionText not resolvable on the clone");
                AbortClone(clone, canvas);
                return false;
            }

            Neutralize(clone);

            var cloneRect = clone.transform as RectTransform;
            if (cloneRect == null || label == null)
            {
                WarnCloneUnavailableOnce("clone root is not a RectTransform / text died in Neutralize");
                AbortClone(clone, canvas);
                return false;
            }

            // Center on the marker regardless of how the prefab is anchored on its flat canvas.
            cloneRect.anchorMin = cloneRect.anchorMax = cloneRect.pivot = new Vector2(0.5f, 0.5f);
            cloneRect.anchoredPosition = Vector2.zero;
            cloneRect.localRotation = Quaternion.identity;
            cloneRect.localScale = Vector3.one;

            label.text = text;
            clone.SetActive(true); // pool pattern: the prefab may be authored inactive (GetPingTooltip → Show)
            gameObject.SetActive(true);

            // The game's own layout sizes the backdrop to the name — force it now so the
            // measurement below sees final geometry (cold-open lesson from CanvasConversion).
            LayoutRebuilder.ForceRebuildLayoutImmediate(cloneRect);
            Vector2 sizePx = cloneRect.rect.size;
            if (sizePx.x < MinPrefabPixels || sizePx.y < MinPrefabPixels)
            {
                WarnCloneUnavailableOnce($"clone measures degenerate ({sizePx.x:F0}x{sizePx.y:F0} px)");
                AbortClone(clone, canvas);
                return false;
            }

            float scale = Mathf.Min(TooltipWorldHeight / sizePx.y, TooltipMaxWorldWidth / sizePx.x);
            transform.localScale = new Vector3(scale, scale, scale);

            _group = clone.GetComponent<CanvasGroup>(); // Neutralize guarantees one at the root
            VRLog.Debug("Board", $"[Ping] name tag: game tooltip clone ({sizePx.x:F0}x{sizePx.y:F0} px, " +
                                 $"{scale * 1000f:F2} mm/px) at the marker.");
            return true;
        }
        catch (Exception e)
        {
            VRLog.Warn("Board", $"[Ping] vanilla tooltip clone failed — mod label fallback: {e.Message}");
            // Tear down whatever half-built children exist; the fallback label rebuilds on the root.
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
            Canvas? c = GetComponent<Canvas>();
            if (c != null)
                DestroyImmediate(c);
            _canvas = null;
            _group = null;
            return false;
        }
        finally
        {
            gameObject.SetActive(true); // never leave the tag parked inactive (Update = lifetime)
        }
    }

    /// <summary>Failure unwinding for the clone route: drop the clone and the canvas so the
    /// fallback TMP label builds on a plain (Rect)Transform root.</summary>
    private void AbortClone(GameObject clone, Canvas canvas)
    {
        DestroyImmediate(clone);
        DestroyImmediate(canvas);
        _canvas = null;
        _group = null;
    }

    /// <summary>
    /// RemoteWidgetMirror's Neutralize in miniature (see there for the pass/ordering
    /// rationale): destroy everything that is not presentation, reverse component order,
    /// repeated until a pass frees nothing, Canvas held to the last pass, colliders swept.
    /// DELIBERATE DELTA: layout components stay (this clone is not rect-puppeted — the game's
    /// own layout must size the backdrop to the injected name), and every CanvasGroup is
    /// forced to alpha 1 because show/hide GUIAnimators may author their rest state at 0.
    /// </summary>
    private static void Neutralize(GameObject clone)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Component[] components = clone.GetComponentsInChildren<Component>(includeInactive: true);
            bool freed = false;
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component c = components[i];
                if (c == null || c is Transform || IsPresentation(c))
                    continue;
                if (c is Canvas && pass == 0)
                    continue;
                DestroyImmediate(c);
                if (c == null)
                    freed = true;
            }
            if (!freed && pass > 0)
                break;
        }

        foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(col);

        foreach (CanvasGroup g in clone.GetComponentsInChildren<CanvasGroup>(true))
        {
            g.alpha = 1f;
            g.interactable = false;
            g.blocksRaycasts = false;
        }
        if (clone.GetComponent<CanvasGroup>() == null)
        {
            CanvasGroup cg = clone.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
    }

    /// <summary>The presentation whitelist — RemoteWidgetMirror's plus the layout family
    /// (kept here on purpose, see <see cref="Neutralize"/>).</summary>
    private static bool IsPresentation(Component c) =>
        c is Graphic || c is CanvasRenderer || c is Mask || c is RectMask2D
        || c is BaseMeshEffect || c is CanvasGroup
        || c is LayoutGroup || c is ContentSizeFitter || c is LayoutElement;

    /// <summary>
    /// The exact description string the game composed for THIS ping: Ping3DElement has just
    /// called <c>tooltip.Show(description, element, …)</c>, tooltips live as children of the
    /// PingManager (<c>Instantiate(pingTooltipPrefab, base.transform)</c>, PingManager.cs:53),
    /// and vanilla guarantees at most one ACTIVE tooltip per element (<c>HidePing(player,
    /// element, instant: true)</c> right before showing) — so matching the publicized
    /// <c>m_ObjectToTrack</c> against the pinged element is unambiguous.
    /// </summary>
    private static string? FindVanillaTooltipText(PingManager manager, GameObject element)
    {
        try
        {
            foreach (UIPingTooltip t in manager.GetComponentsInChildren<UIPingTooltip>(includeInactive: false))
            {
                if (t == null || t.m_ObjectToTrack != element)
                    continue;
                string? s = t.descriptionText != null ? t.descriptionText.text : null;
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }
        catch (Exception e)
        {
            VRLog.Debug("Board", $"[Ping] live tooltip text lookup failed (mod name fallback): {e.Message}");
        }
        return null;
    }

    private static bool _warnedCloneUnavailable;

    /// <summary>One warning per session when the clone route is unavailable — the tag still
    /// works via the fallback label, but the design regression should be visible in logs.</summary>
    private static void WarnCloneUnavailableOnce(string reason)
    {
        if (_warnedCloneUnavailable)
            return;
        _warnedCloneUnavailable = true;
        // HW-VERIFY (2026-09 refactor, F-37) — the doc beside this line says "the design
        // regression should be visible in logs", and at Warn it was visible in none. Once.
        VRLog.Note("Board", $"[Ping] game tooltip clone unavailable ({reason}) — ping name tags " +
            "use the mod-drawn label this session.");
    }

    /// <summary>The pre-clone mod label, kept verbatim as the degrade path.</summary>
    private void BuildFallbackLabel(string name)
    {
        _label = gameObject.AddComponent<TextMeshPro>();
        _label.text = name;
        _label.alignment = TextAlignmentOptions.Center;
        _baseColor = new Color(1f, 0.95f, 0.85f);   // OwnerTag's warm off-white
        _label.color = _baseColor;
        _label.fontStyle = FontStyles.Bold;
        TmpFit.Fit(_label, FallbackWidth, FallbackHeight, wrap: false);
        // Free-floating over the room in MR (plate dies with the tag). fades: TRUE because this tag
        // expires by FADING its colour alpha (Tick's FadeTail), not by being switched off: an MR
        // plate is opaque by construction, so without the opt-in it stood at full opacity behind an
        // already-invisible name for the whole tail and then vanished in one frame (same residue
        // class as the tooltip streak, user report 2026-08-09). MrBacking's "THE PLATE MUST DIE
        // WITH ITS CONTENT" says why the fade is opt-in rather than read off every label's alpha.
        WorldUI.MrBacking.Label(_label, fades: true);
        // (The clone route needs no MR plate — the game tooltip brings its own backdrop.)
    }

    private void Update()
    {
        TickGuard.Run("Board.PingTag", PerfConfig.CacheDelegates ? _tickCached ??= Tick : Tick);
    }

    private void Tick()
    {
        float now = Time.unscaledTime;
        if (now >= _dieAt)
        {
            Destroy(gameObject);
            return;
        }

        // Fade tail (alpha only; vanilla hides via a GUI animator we deliberately stripped).
        float remain = _dieAt - now;
        float a = remain < FadeTail ? remain / FadeTail : 1f;
        if (_group != null)
        {
            if (!Mathf.Approximately(_group.alpha, a))
                _group.alpha = a;
        }
        else if (_label != null && !Mathf.Approximately(_label.color.a, a))
        {
            _label.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, a);
        }

        // Billboard toward the local head (aim +Z AWAY — TMP and uGUI both read from −Z).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = transform.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

            // PANEL COMPOSITING — the third free-floating identity tag, and the one that was never
            // in the 2026-08-04 sweep. Net.OwnerTag and Net.RemoteNameTag both rank their renderers
            // on the converted-panel distance ladder every frame through the SAME seam; this one
            // ranked nothing and sat at the default sortingOrder 0, which loses to a converted
            // panel's >= 100 at every distance and every angle. Ping a hex behind the initiative
            // row and the pinging player's name was painted over — while that same player's head
            // tag composited correctly a metre away. The tag lives ~2 s, so it read as a flicker.
            //
            // BOTH ROUTES, because this tag has two and they draw through different systems: the
            // vanilla-tooltip CLONE is uGUI under a mod-owned world-space Canvas (a CanvasRenderer
            // is NOT a Renderer, so the renderer array never sees it), the FALLBACK is a mod-drawn
            // TextMeshPro, i.e. a real Renderer. The exemption list on BoardVisual.AdoptBoardOrder
            // names the owner tag as already driven "by somebody else" — this one was driven by
            // nobody.
            float eyeDistance = away.magnitude;
            RefreshTagRenderers();
            Net.BoardVisual.OrderWithPanels(_tagRenderers, eyeDistance);
            Net.BoardVisual.OrderWithPanels(_canvas, eyeDistance);
        }
    }

    /// <summary>Lazy-stale cache of the tag's own renderers, the same cache contract
    /// <c>Net.OwnerTag</c> and <c>Net.RemoteNameTag</c> keep: refetch when empty, when an entry
    /// died, or on the periodic pickup that catches MrBacking's lazily ADDED backing plate (which
    /// is added without any existing entry dying, so a died-entry test alone cannot see it).</summary>
    private void RefreshTagRenderers()
    {
        bool stale = _tagRenderers.Length == 0 || Time.frameCount >= _tagRenderersRefreshAt;
        for (int i = 0; !stale && i < _tagRenderers.Length; i++)
        {
            if (_tagRenderers[i] == null)
                stale = true;
        }
        if (!stale)
            return;
        _tagRenderers = GetComponentsInChildren<Renderer>(includeInactive: false);
        _tagRenderersRefreshAt = Time.frameCount + TagRenderersRefreshFrames;
    }

    private void OnDestroy()
    {
        Live.Remove(this);
    }

    /// <summary>Hot-reload hygiene (BoardModule.Shutdown): destroy live tags, clear statics.</summary>
    internal static void Reset()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PingNameTag t = Live[i];
            if (t != null)
                UnityEngine.Object.Destroy(t.gameObject);
        }
        Live.Clear();
        _warnedCloneUnavailable = false;
    }

    /// <summary>The flat game's own local-name source: <c>PlatformLayer.UserData.UserName</c>
    /// (PingManager.cs:81), via reflection — PlatformUserData lives in a platform assembly the
    /// mod does not reference.</summary>
    private static string? LocalUserName()
    {
        try
        {
            object? userData = _userData?.GetValue(null);
            return userData == null ? null : _userName?.GetValue(userData) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureReflected()
    {
        if (_reflected)
            return;
        _reflected = true;
        try
        {
            Type? networkPlayer = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            _playerId = networkPlayer?.GetProperty("PlayerID");
            _username = networkPlayer?.GetProperty("Username");

            Type? platformLayer = AccessTools.TypeByName("PlatformLayer");
            _userData = platformLayer?.GetProperty("UserData", BindingFlags.Public | BindingFlags.Static);
            Type? userDataType = _userData?.PropertyType;
            _userName = userDataType?.GetProperty("UserName");

            if (_username == null)
                VRLog.Warn("Board", "[Ping] NetworkPlayer.Username not resolvable — ping name tags " +
                    "fall back to 'Player <id>'.");
        }
        catch (Exception e)
        {
            VRLog.Warn("Board", $"[Ping] name-tag reflection resolution threw: {e.Message}");
        }
    }
}

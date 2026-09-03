using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>A FLOATING WINDOW BREAKS INTO REAL DEBRIS THAT FLIES THROUGH THE ROOM, AND FORMS OUT OF
/// IT.</b>
/// User, 2026-08-24: <i>"Ich möchte nicht mehr, dass die Fenster einfach aufploppen und urplötzlich
/// wieder von einem Frame auf den anderen verschwinden. ... Ich stelle mir ein verschwindendes
/// Fenster vor, das in Partikel von Wind verweht. Und Auftauchen eventuell andersrum ... Aber
/// wichtig: Das Ganze soll 1s höchstens 2s gehen, es soll niemanden aufhalten, nur cool
/// aussehen."</i>
///
/// <para>In English: <i>"I no longer want the windows to just pop up and then vanish from one
/// frame to the next. ... I picture a vanishing window blowing away into particles of wind. And
/// appearing perhaps the other way round ... But important: the whole thing should take one
/// second, at most two, it must not hold anyone up, just look cool."</i></para>
///
/// <para><b>"ES SOLL NIEMANDEN AUFHALTEN" — "IT MUST NOT HOLD ANYONE UP" — IS THE ACCEPTANCE
/// CRITERION, NOT A PREFERENCE</b>, and it sits directly on top of a standing ruling this project
/// has already lost a build to: <i>"es MUSS immer möglich sein das Optionsmenu zu öffnen"</i> —
/// "it MUST always be possible to open the options menu" (see
/// .planning/OPTIONS-MENU-NEVER-BLOCKED.md).
/// So the whole class is built around four properties, each of which is reached BY CONSTRUCTION
/// rather than by a catch:</para>
/// <list type="number">
/// <item><b>Nothing sits between a keypress and a window opening.</b> <see cref="PlayIn"/> is
///   called AFTER the window has been made visible and interactive; it never gates, never delays
///   and never returns a "not yet". The window's collider, its <c>GraphicRaycaster</c> and its
///   laser target are up before this class hears about it, and it does not touch any of them.
///   ONE READ-ONLY QUALIFICATION (2026-09-03): the far-ray and poke drivers ASK this class
///   whether a pane's elements are still missing (<see cref="IsPointerBlind"/>) and skip its
///   canvas plane while they are — for an appear that is the element sweep only, about 0.2 s
///   of the 0.35 s, after which the window is whole and the last dust settles on a clickable
///   pane. Nothing on the window is switched; the drivers merely decline a plane that shows
///   nothing, so the beam runs through the dust instead of stopping on an invisible pane.</item>
/// <item><b>A dismissed window detaches from input in the same statement it starts fading.</b>
///   <see cref="PlayOut"/> disables the host's <c>GraphicRaycaster</c> as its first act. Only the
///   pixels linger; a player cannot click a ghost.</item>
/// <item><b><see cref="PlayOut"/> is a TOTAL function.</b> Its callback runs exactly once on every
///   path — effect switched off, shader unresolved, panel already dead, an exception while
///   building, the host deactivated under it, the host destroyed under it, or the hard ceiling
///   expiring. When the effect cannot run, the callback runs SYNCHRONOUSLY inside the call, which
///   makes the fallback literally today's code path with a function call in front of it.</item>
/// <item><b>No path ends with a window invisible-but-alive.</b> Every element alpha this class
///   writes is recorded before it is written and restored from that record on completion,
///   cancellation, disable, destroy and watchdog. And the field is EXACT at both ends
///   (<see cref="WindowMaterialiseField.Presence"/>), so "restored" means literally the number that
///   was there, not 0.98. The debris is exact the same way and needs no restore at all: it is
///   mod-owned geometry that is destroyed with the carrier, and its size is exactly zero at the end
///   of both directions, so a completed appear leaves nothing sitting on the finished
///   window.</item>
/// </list>
///
/// <para><b>HOW IT LOOKS, IN TWO HALVES THAT SHARE ONE FIELD.</b> The window's own uGUI elements
/// are removed at ELEMENT granularity — each <c>CanvasRenderer</c> under the host gets its
/// <c>SetAlpha</c> driven by <see cref="WindowMaterialiseField.PresenceOf"/> over its own extent — so
/// the window disintegrates in a wave along the wind instead of dimming uniformly. At the same wave
/// front, a few hundred <b>real tetrahedral shards</b> are torn out of the elements that are going
/// dark and fly off through the room. Both halves evaluate the SAME field, which is why a shard
/// leaves in the frame its own patch of window disappears; see <see cref="WindowMaterialiseField"/>
/// for the arithmetic, the two fronts, and the stereo argument.</para>
///
/// <para><b>WHAT THE 2026-08-26 REDESIGN CHANGED, AND WHY.</b> User, on 292/293: <i>"Ich mag die
/// Fenster ein- und ausblend-Animation nicht. Ich will eher, dass es wirkliche Partikeleffekte in
/// der 3D-Umgebung auslöst, aktuell ist es eher ein 2D-Effekt."</i> He was describing the mechanism
/// correctly. The debris used to be painted on ONE quad parented to the window's host rect, in the
/// window's own plane, with <c>ZWrite Off</c> — nothing it drew could ever be nearer or further than
/// the window or be hidden by a table leg. It is now world geometry with an out-of-plane launch
/// velocity, depth writes, and a split across two renderers that bracket the window in the panel
/// draw ladder. The whole argument is in the class doc on the partial in
/// <c>WindowMaterialiseDebris.cs</c>.</para>
///
/// <para><b>AND NO HEAD POSE.</b> User, same message: <i>"Der Effekt soll nicht an den
/// Kopfbewegungen gebunden sein"</i>. Camera-facing billboards are head-bound by definition, so the
/// debris is solids with their own tumble instead. Nothing in this feature reads a camera position,
/// a view matrix, a screen position or a depth texture, and that is checked mechanically by the
/// preview's identifier audit rather than asserted.</para>
///
/// <para><b>WHY ELEMENT GRANULARITY AND NOT A PER-PIXEL DISSOLVE.</b> A per-pixel dissolve of a
/// canvas means putting a material on every <c>Graphic</c>. That is invasive, it is silently wrong
/// for any graphic carrying a non-stock material (the <c>Custom/SimpleGrabPassBlur</c> that cost
/// PanelSupersample sixteen builds is exactly such a graphic), and it would have to be undone
/// perfectly on every interruption. <c>CanvasRenderer.SetAlpha</c> is a single float per element
/// that uGUI itself does not write, needs no rebuild, no layout and no material, and is restored by
/// writing the recorded number back.</para>
///
/// <para><b>REAL PARTICLES, BUT NO <c>ParticleSystem</c> COMPONENT.</b> The rig runs at roughly
/// 9.57 world units per metre (198 on the map-room table) and 37 of 43 of the game's own particle
/// systems use <c>ParticleSystemScalingMode.Local</c>, which ignores hierarchy scale by design — a
/// burst sized in local units is invisible or absurd. Every size in this feature is authored in
/// APPARENT METRES and converted once, from the panel's own measured <c>lossyScale</c> and the live
/// rig scale, with every link of that chain logged. There is also no per-frame CPU simulation to
/// pay for: a shard's whole trajectory is a closed function of one uniform, so N flying shards cost
/// two <c>SetFloat</c>s. The four reasons in full are in the <c>WindowMaterialiseDebris.cs</c> class
/// doc.</para>
///
/// <para><b>LOCAL PRESENTATION ONLY.</b> Nothing here touches the wire. No field is added to any
/// packet, no <c>NetProtocol.ModBuild</c> change is implied, and a window's pose sync is untouched
/// — this class writes element alphas and two mod-owned mesh renderers, both of which are
/// per-client decoration. A peer sees their own animation, on their own client, at their own
/// configured duration.</para>
/// </summary>
internal static partial class WindowMaterialise
{
    private const string Scope = "WorldUI";

    internal const string ShaderName = "GloomhavenVR/WindowMaterialise";

    /// <summary>
    /// <b>THE CODE CEILING NO CONFIG VALUE CAN CROSS.</b> The user's own bound: <i>"Das Ganze soll
    /// 1s höchstens 2s gehen"</i>. A dial is a number a person can type into a .cfg by hand, and a
    /// 30-second appear animation would make a window slow to appear — which is the one thing this
    /// feature is not allowed to do. Both durations are clamped to this on every read, so the
    /// ceiling holds against a hand-edited file, a corrupt file and a future rebase alike.
    /// </summary>
    internal const float HardCeilingSeconds = 2.0f;

    /// <summary>Below this a duration is not an animation, it is today's hard cut — and taking the
    /// hard cut is exactly what we want for such a value, so it is the OFF branch rather than a
    /// clamp.</summary>
    internal const float MinSeconds = 0.05f;

    /// <summary>Slack on top of the configured duration before the watchdog force-finishes a
    /// runner. Generous on purpose: it is a backstop for "LateUpdate stopped being called", not a
    /// timing mechanism.</summary>
    internal const float WatchdogSlackSeconds = 0.5f;

    // ---- config -------------------------------------------------------------------------------
    // Bound against WorldUIConfig's OWN ConfigFile rather than a new one, the same arrangement
    // ModalFallback.9.Spawn.cs:1532 uses for WindowLegibility: the dials belong in the [WorldUI]
    // section of dev.gloomhavenvr.worldui.cfg, and binding a second file for four entries would put
    // them in a file the VR options browser lists separately.

    private static ConfigEntry<bool>? _enabled;
    private static ConfigEntry<float>? _appearSeconds;
    private static ConfigEntry<float>? _vanishSeconds;
    private static ConfigEntry<float>? _intensity;
    private static bool _bound;

    /// <summary>Bind on first use. WorldUIConfig.Bind() has always run by the time any window is
    /// converted, but a null <c>FileHandle</c> is still handled — an unbound dial reads its
    /// pre-bind fallback and the feature works, rather than throwing on a null.</summary>
    private static void EnsureBound()
    {
        if (_bound)
            return;
        ConfigFile? file = WorldUIConfig.FileHandle;
        if (file == null)
            return;
        _bound = true;

        _enabled = file.Bind("WorldUI", "WindowMaterialise", Defaults.WindowMaterialise,
            "Floating windows MATERIALISE out of flying debris when they appear and break apart into "
            + "it when they close, instead of popping in and out from one frame to the next. The "
            + "debris is real geometry in the room: it passes in front of and behind the window, it "
            + "is hidden by furniture it goes behind, and it separates from the window with real "
            + "parallax when you lean. Nothing about it is attached to your head. "
            + "OFF restores exactly today's behaviour: the window is shown in one frame and released "
            + "in one frame, and not a single alpha is written by this feature. The animation NEVER "
            + "delays a window: it is fully interactive from its first frame, and a closing window "
            + "stops accepting input the instant it starts to fade.");

        _appearSeconds = file.Bind("WorldUI", "WindowMaterialiseAppearSeconds",
            Defaults.WindowMaterialiseAppearSeconds,
            new ConfigDescription(
                "How long a window takes to materialise. Deliberately SHORTER than the vanish — the "
                + "user asked for it ('Die Auftauch-Animation eventuell etwas schneller ... da man "
                + "hier schnell interagieren können soll'). The window is live and clickable from "
                + $"frame one regardless. Hard-capped at {HardCeilingSeconds:F1}s in code, so no cfg "
                + $"value can make a window slow to appear; below {MinSeconds:F2}s the effect is off "
                + "and the window is simply shown. Range 0.05-2.",
                new AcceptableValueRange<float>(0.05f, HardCeilingSeconds)));

        _vanishSeconds = file.Bind("WorldUI", "WindowMaterialiseVanishSeconds",
            Defaults.WindowMaterialiseVanishSeconds,
            new ConfigDescription(
                "How long a closing window takes to blow away. Longer than the appear, because "
                + "nothing is waiting on it: input is detached before the first frame of it, so the "
                + $"lingering pixels cannot hold anyone up. Hard-capped at {HardCeilingSeconds:F1}s "
                + $"in code; below {MinSeconds:F2}s the window is released in one frame as it is "
                + "today. Range 0.05-2.",
                new AcceptableValueRange<float>(0.05f, HardCeilingSeconds)));

        _intensity = file.Bind("WorldUI", "WindowMaterialiseIntensity",
            Defaults.WindowMaterialiseIntensity,
            new ConfigDescription(
                "How large the flying dust is. 0 leaves the element-by-element dissolve with no "
                + "dust at all (a clean directional wipe, and no extra geometry is built at all); "
                + "1 is the shipped look — motes of 2.2-6.0 apparent mm, which is about two to five "
                + "headset pixels at the distance a window sits; 2 doubles that into coarse grit. "
                + "This scales an AMPLITUDE only — it "
                + "cannot change how "
                + "fast anything moves, because the effect has no clock: every position in it is a "
                + "function of progress. Range 0-2.",
                new AcceptableValueRange<float>(0f, 2f)));

        VRLog.Info(Scope, "WINDOW MATERIALISE dials bound: "
                          + $"WindowMaterialise={Enabled}, appear={AppearSeconds:F2}s, "
                          + $"vanish={VanishSeconds:F2}s, intensity={Intensity:F2}. "
                          + $"Both durations are clamped to {HardCeilingSeconds:F1}s IN CODE.");
    }

    /// <summary>Master switch. OFF is not a fast animation, it is no animation: every entry point
    /// returns without writing anything and <see cref="PlayOut"/> runs its callback inline.</summary>
    internal static bool Enabled
    {
        get
        {
            EnsureBound();
            return _enabled?.Value ?? Defaults.WindowMaterialise;
        }
    }

    /// <summary>THE NUMBER INSIDE Clamped() IS THE PRE-BIND FALLBACK, NOT THE SHIPPED DEFAULT —
    /// that lives on one annotated line in <c>Defaults/Defaults.WorldUI.cs</c>. Reading it from
    /// <c>Defaults</c> here means the two can never drift.</summary>
    internal static float AppearSeconds
    {
        get
        {
            EnsureBound();
            return Clamp(_appearSeconds?.Value ?? Defaults.WindowMaterialiseAppearSeconds);
        }
    }

    internal static float VanishSeconds
    {
        get
        {
            EnsureBound();
            return Clamp(_vanishSeconds?.Value ?? Defaults.WindowMaterialiseVanishSeconds);
        }
    }

    internal static float Intensity
    {
        get
        {
            EnsureBound();
            return Mathf.Clamp(_intensity?.Value ?? Defaults.WindowMaterialiseIntensity, 0f, 2f);
        }
    }

    /// <summary>The ceiling, applied on every read rather than at bind time — a dial the player
    /// drags in the VR menu writes a new value into a live entry, and a clamp that only ran at bind
    /// would not see it.</summary>
    private static float Clamp(float seconds) =>
        seconds < MinSeconds ? 0f : Mathf.Min(seconds, HardCeilingSeconds);

    // ---- the shader ---------------------------------------------------------------------------

    private static Material? _material;

    /// <summary>
    /// The one shared material every effect draws with; per-effect values ride a
    /// <c>MaterialPropertyBlock</c>, so N animating windows still share one material and one shader
    /// variant. (A MaterialPropertyBlock cannot set shader KEYWORDS — this shader declares none, on
    /// purpose.)
    ///
    /// <para>Resolved through <see cref="BundleShaders"/> and never through a bare
    /// <c>Shader.Find</c>: a shader referenced only by runtime C# is never loaded, so
    /// <c>Shader.Find</c> returns null for it forever. That has cost this project two builds. Only
    /// successes are cached, so a lookup before the bundle lands is retried on the next window.</para>
    /// </summary>
    private static Material? SharedMaterial()
    {
        if (_material != null)
            return _material;
        Shader? sh = BundleShaders.Resolve(
            ShaderName, Scope,
            "windows materialise out of flying debris and break apart into it.",
            "Windows will still appear and close ELEMENT BY ELEMENT along the wind (that half is "
            + "pure C# and needs no shader) but with no debris drawn. Nothing is delayed or lost.");
        if (sh == null)
            return null;
        _material = new Material(sh) { name = "GloomhavenVR.WindowMaterialise" };
        return _material;
    }

    // ---- live runners -------------------------------------------------------------------------

    /// <summary>Every effect currently playing. Bounded by the number of floated windows (the map
    /// room's arc holds five, the supersampler's effective cap is seven), walked linearly — a
    /// dictionary for a list this short would cost more than it saves and would need pruning of its
    /// own.</summary>
    private static readonly List<WindowMaterialiseRunner> Live = new(8);

    internal static void Register(WindowMaterialiseRunner r)
    {
        if (!Live.Contains(r))
            Live.Add(r);
    }

    internal static void Unregister(WindowMaterialiseRunner r) => Live.Remove(r);

    // ---- the pane is not a pointer surface while its elements are missing --------------------
    //
    // USER, 2026-09-03: "Die Partikel der Materialisieren- bzw. Verpuffen-Animation von Fenstern
    // colliden aktuell mit dem Laser; das soll nicht der Fall sein." — the laser collides with the
    // dust of a window appearing or dissolving.
    //
    // WHAT THE BEAM ACTUALLY STOPPED AT. Not a shard: the debris is two MeshRenderers with no
    // Collider and no Graphic (WindowMaterialiseDebris.BuildHalf), on the mod layer, and
    // PanelInkBounds skips any subtree carrying a Renderer, so no pointer path — the physics pick
    // (RayInteractor), the canvas-plane clamp (RayUguiDriver), the GraphicRaycaster (UguiPointer),
    // the hit-rect walk — ever resolved a hit on the dust. What every one of them DID resolve was
    // the window's own canvas plane: RayUguiDriver clamps the beam to every canvas in
    // UguiPokeSurfaces that is isActiveAndEnabled on mere hover of its HIT RECT, with no term for
    // the GraphicRaycaster (PlayOut disables it — clicks were already dead), the CanvasGroup, or
    // this effect. A vanishing window stays registered until CanvasConversion.Release runs at the
    // END of the dissolve, and its Canvas is held ON by WindowVisibilityHold for the whole 0.9 s;
    // an appearing window is registered at conversion and its elements are at alpha 0 for the
    // first ElementSpan of the appear. Either way the beam ended on an invisible pane in the
    // middle of a dust cloud, which is indistinguishable from "the particles collide with the
    // laser". This predicate is the one term those drivers were missing.

    /// <summary>
    /// <b>Is this canvas the pane of a window whose elements are currently missing?</b> True for
    /// the whole of a vanish and for the element sweep of an appear
    /// (<see cref="WindowMaterialiseRunner.PointerBlind"/>). The far-ray driver and the poke
    /// driver skip such a canvas in their surface loops, so the beam runs through the dust to
    /// whatever stands behind the window and a fingertip cannot press a pane that is not there.
    /// Costs a walk of <see cref="Live"/>, which is empty almost always and never longer than the
    /// number of windows in flight.
    /// </summary>
    internal static bool IsPointerBlind(Canvas? canvas)
    {
        if (canvas == null || Live.Count == 0)
            return false;
        for (int i = 0; i < Live.Count; i++)
        {
            WindowMaterialiseRunner r = Live[i];
            if (r != null && r.PointerBlind && r.Panel != null
                && ReferenceEquals(r.Panel.HostCanvas, canvas))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Is <paramref name="t"/> one of the effect's own objects — under a live runner's carrier
    /// (the debris hangs off it), or anything wearing a <c>ParticleSystemRenderer</c>? A particle
    /// cloud is never a click target, whoever spawned it.
    /// </summary>
    private static bool IsEffectObject(Transform t, out string why)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            WindowMaterialiseRunner r = Live[i];
            if (r != null && t.IsChildOf(r.transform))
            {
                why = "under a live materialise carrier";
                return true;
            }
        }
        if (t.GetComponent<ParticleSystemRenderer>() != null)
        {
            why = "it carries a ParticleSystemRenderer";
            return true;
        }
        why = string.Empty;
        return false;
    }

    private static int _pointerHitPrinted;
    private static int _pointerHitLastId;
    private static string _pointerHitLastPath = string.Empty;

    /// <summary>
    /// <b>A pointer path resolved a hit — was it on the effect?</b> Called by the physics pick on
    /// every change of hit collider and by the far-ray driver on every canvas it clamps to; both
    /// are edge-triggered by their callers, so this costs nothing while nothing changes. Prints
    /// the <c>LASER HIT PARTICLE</c> line, change-gated on (path, object) and capped at six per
    /// session. THE LINE MUST BE ABSENT from the next log: the fix above closes the pane and the
    /// dust never had a surface, so a hit here names a path this fix did not close.
    /// </summary>
    internal static void ProbePointerHit(string side, string path, Transform? hit)
    {
        if (hit == null || _pointerHitPrinted >= 6)
            return;
        string why;
        bool blind = false;
        if (Live.Count > 0)
        {
            Canvas? canvas = hit.GetComponent<Canvas>();
            blind = canvas != null && IsPointerBlind(canvas);
        }
        if (blind)
            why = "the pane of a window whose elements are missing";
        else if (!IsEffectObject(hit, out why))
            return;
        int id = hit.GetInstanceID();
        if (id == _pointerHitLastId && path == _pointerHitLastPath)
            return;
        _pointerHitLastId = id;
        _pointerHitLastPath = path;
        _pointerHitPrinted++;
        // HW-VERIFY
        VRLog.Note(Scope, $"LASER HIT PARTICLE: the {side} hand's {path} resolved a hit on "
                          + $"'{hit.name}' (layer {hit.gameObject.layer}) — {why}. A pointer path is "
                          + "treating the materialise effect as a surface. USER REPORT (2026-09-03): "
                          + "the dust collides with the laser. THIS LINE MUST BE ABSENT: every "
                          + "pointer path skips a pane whose elements are missing and the dust "
                          + "carries no collider, so a hit here names a path the fix did not close. "
                          + $"{_pointerHitPrinted} of 6 for this session.");
    }

    // ---- the close edge -----------------------------------------------------------------------

    /// <summary>Close-edge holds waiting to be claimed by a vanish. Bounded by the number of
    /// floated windows and by <see cref="WindowMaterialisePreRoll.GraceSeconds"/>, which is why a
    /// list and a linear walk are the right shape here too.</summary>
    private static readonly List<WindowMaterialisePreRoll> PreRolls = new(4);

    private static bool _subscribed;

    /// <summary>
    /// <b>Listen for the close edge.</b> Subscribed lazily from <see cref="PlayIn"/> and
    /// <see cref="PlayOut"/> rather than from a module boot, because a window has to be FLOATED
    /// before it can be released and both of those run first — so there is no window whose close
    /// edge can be missed, and no ordering to get wrong against <c>VREventsModule</c>'s own patch
    /// installation.
    ///
    /// <para>The event itself is not this class's to raise: it is
    /// <c>VREvents.WindowVisibility</c>, published from the Harmony postfix on
    /// <c>UIWindow.EvaluateAndTransitionToVisualState</c> — the single choke point every visibility
    /// change funnels through (<c>Core/Events/GameEventPatches.cs</c>). Subscribing rather than
    /// adding a second patch keeps the mod to one patch on that method, and
    /// <c>VREvents.Invoke</c> already guards every handler so a throw here cannot reach the game's
    /// message pump.</para>
    /// </summary>
    private static void EnsureSubscribed()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        VREvents.WindowVisibility += OnWindowVisibility;
    }

    /// <summary>
    /// A window changed visual state. On the FALLING edge of one this mod is currently floating,
    /// take the visibility hold immediately — see <see cref="WindowMaterialisePreRoll"/> for why the
    /// hold moves to the edge and the ANIMATION does not.
    /// </summary>
    private static void OnWindowVisibility(WindowVisibilityEvent e)
    {
        // THIS HANDLER MUST NOT THROW, and not for the usual reason. VREvents.Invoke wraps the
        // whole multicast delegate in ONE try, so a throw here does not merely lose this
        // subscriber's work — UnityEvent-style, it amputates every subscriber registered after it
        // on the same event. A decoration is never allowed to take another feature's notification
        // away, so the body is wrapped and a failure costs the animation and nothing else.
        try
        {
            if (e.Shown)
                return;
            if (!Enabled || VanishSeconds <= 0f)
                return;
            UIWindow w = e.Window;
            if (w == null)
                return;
            // NO CLOSE-EDGE HOLD FOR A HOVER CARD (ModBuild 367). This hold exists to keep a
            // window's pixels on screen for the one tick between the game hiding it and the mod
            // starting the dissolve. With no dissolve to wait for, holding a hover card's pixels
            // open would keep the card visible AFTER the pointer left it — the effect's gate has
            // to sit on every one of its three entry points, not only on the two that animate.
            if (ModalFallback.IsMapRoomHoverCard(w))
                return;

            ConvertedPanel? panel = PanelUnder(w.transform);
            if (panel == null)
                return;
            // A vanish that is already playing owns the window's visibility itself; an appear that
            // is playing is about to be cancelled by the vanish that claims this hold. Neither
            // wants a second owner.
            if (IsAnimating(panel) || FindPreRoll(panel) != null)
                return;

            WindowMaterialisePreRoll? pre = WindowMaterialisePreRoll.Begin(panel);
            if (pre != null)
                PreRolls.Add(pre);
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, "WINDOW MATERIALISE: taking the close-edge visibility hold threw "
                               + $"({ex.GetType().Name}: {ex.Message}). The dissolve still starts "
                               + "from the release tick one tick later, which is where it started "
                               + "before this hold existed.");
        }
    }

    /// <summary>
    /// The converted panel whose host <paramref name="t"/> currently lives under, or null.
    ///
    /// <para>Matched by ANCESTRY against <c>HostRect</c> and not by reference against
    /// <c>Target</c>: the rect a window is converted THROUGH is not required to be the
    /// <c>UIWindow</c>'s own transform (<c>ModalFallback.8.Convert.cs:345</c>), and containment is
    /// not identity — but "is inside this panel's host" is precisely a containment question, so
    /// containment is the right test here.</para>
    /// </summary>
    private static ConvertedPanel? PanelUnder(Transform? t)
    {
        if (t == null)
            return null;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel p = panels[i];
            RectTransform? host = p?.HostRect;
            if (host == null)
                continue;
            if (ReferenceEquals(t, host) || t.IsChildOf(host))
                return p;
        }
        return null;
    }

    private static WindowMaterialisePreRoll? FindPreRoll(ConvertedPanel panel)
    {
        for (int i = PreRolls.Count - 1; i >= 0; i--)
        {
            WindowMaterialisePreRoll pre = PreRolls[i];
            if (pre == null)
            {
                PreRolls.RemoveAt(i);
                continue;
            }
            if (ReferenceEquals(pre.Panel, panel))
                return pre;
        }
        return null;
    }

    internal static void UnregisterPreRoll(WindowMaterialisePreRoll pre) => PreRolls.Remove(pre);

    /// <summary>
    /// <b>Claim the close-edge hold for this panel's dissolve, or return null if there is none.</b>
    /// The hold is handed over LIVE — the pre-roll never releases it — so the window's visibility
    /// passes from one owner to the next inside a single statement and is not unheld for a frame.
    /// </summary>
    internal static WindowVisibilityHold? ClaimPreRoll(ConvertedPanel panel)
    {
        WindowMaterialisePreRoll? pre = FindPreRoll(panel);
        return pre?.Adopt();
    }

    /// <summary>
    /// Drop a close-edge hold without a dissolve claiming it — the window goes exactly where the
    /// game was sending it. For the paths that deliberately do NOT animate: a re-opened window, and
    /// the empty-release rule (a window that is drawing nothing has nothing to dissolve, and
    /// holding its pixels would be holding no pixels).
    /// </summary>
    internal static void DropPreRoll(ConvertedPanel? panel, string reason)
    {
        if (panel == null)
            return;
        FindPreRoll(panel)?.End(reason);
    }

    /// <summary>
    /// <b>Is this panel mid-effect?</b> Offered so the liveness rule ("no empty windows", 2.0 s of
    /// nothing drawing) can tell a window that is deliberately dissolving from one that has died.
    ///
    /// <para>THE ASSUMPTION THIS RELIES ON IS STATED RATHER THAN CODED AGAINST, because that rule is
    /// being rewritten in a parallel lane: a materialising window is below the rule's drawing
    /// threshold for at most <see cref="AppearSeconds"/>, which is hard-capped at
    /// <see cref="HardCeilingSeconds"/> = 2.0 s — i.e. never longer than the rule's own 2.0 s dwell,
    /// so an APPEAR cannot complete an empty-window timer even if nothing consults this. A VANISH
    /// leaves <c>Converted</c> in the same frame it starts (see the integration hunk in
    /// .planning/WINDOW-MATERIALISE.md), so the rule never sees it at all. This predicate is
    /// therefore belt-and-braces, not load-bearing.</para>
    /// </summary>
    internal static bool IsAnimating(ConvertedPanel? panel)
    {
        if (panel == null)
            return false;
        for (int i = 0; i < Live.Count; i++)
            if (Live[i] != null && ReferenceEquals(Live[i].Panel, panel))
                return true;
        return false;
    }

    /// <summary>
    /// <b>Is this game window's float still dissolving?</b> The one question the convert loop has to
    /// be able to ask, and the reason is a hazard rather than a nicety.
    ///
    /// <para>A vanishing float leaves <c>Converted</c> in the frame the teardown starts, but its
    /// <c>CanvasConversion.Release</c> — the call that re-parents the GAME's window back to its 2D
    /// home — is what gets deferred behind the animation. For up to
    /// <see cref="VanishSeconds"/> the game's window is therefore still parented under a host that
    /// nothing lists any more. If the convert loop re-floated it in that gap it would record the
    /// DYING HOST as the window's original parent, and the pending release would then re-parent the
    /// window into a destroyed object. One <c>continue</c> in the convert loop closes it; the gap
    /// is bounded by the hard code ceiling, so the window is never held out for more than 2 s even
    /// if something goes wrong.</para>
    ///
    /// <para>Matched by ANCESTRY rather than by reference, because the rect a window is converted
    /// through (<c>ModalFallback.8.Convert.cs:345</c>) is not required to be the <c>UIWindow</c>'s
    /// own transform — containment is not identity, and a reference test that happens to hold today
    /// would fail silently the day a window is converted through a child.</para>
    /// </summary>
    internal static bool IsVanishing(Transform? windowRoot)
    {
        if (windowRoot == null)
            return false;
        for (int i = 0; i < Live.Count; i++)
        {
            WindowMaterialiseRunner r = Live[i];
            if (r == null || !r.Vanishing)
                continue;
            RectTransform? target = r.Panel?.Target;
            if (target == null)
                continue;
            if (ReferenceEquals(target, windowRoot) || windowRoot.IsChildOf(target)
                || target.IsChildOf(windowRoot))
                return true;
        }
        return false;
    }

    /// <summary>
    /// <b>The window is up, visible and interactive — now make it look like it arrived.</b> Call
    /// this AFTER the reveal, in the same LateUpdate, so the first frame the eye sees is already
    /// the first frame of the animation rather than a full-alpha pop.
    ///
    /// <para>This call cannot fail in a way the caller has to handle. If the effect is off, the
    /// panel is dead, or anything throws, the window simply stays exactly as the caller left it:
    /// fully visible. There is no state to unwind because nothing has been written yet.</para>
    /// </summary>
    internal static void PlayIn(ConvertedPanel? panel)
    {
        EnsureSubscribed();
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
            return;
        if (!Enabled)
            return;
        // A HOVER CARD ARRIVES INSTANTLY — see IsHoverCard (ModBuild 367).
        if (IsHoverCard(panel))
            return;
        float seconds = AppearSeconds;
        if (seconds <= 0f)
            return;

        try
        {
            Cancel(panel, "a new appear started");
            // A window that is APPEARING is not one whose pixels need holding: the game is on its
            // way to showing it, and a stale close-edge hold from a previous close would be a
            // second owner of the same three switches.
            DropPreRoll(panel, "the window re-opened");
            WindowMaterialiseRunner.Begin(panel, seconds, materialising: true, onDone: null);
        }
        catch (Exception ex)
        {
            // The window is already visible and interactive; a failure here costs the decoration
            // and nothing else. Restore anything a half-built runner may have written.
            Cancel(panel, "PlayIn threw");
            VRLog.Error(Scope, $"WINDOW MATERIALISE: appear on '{Name(panel)}' FAILED "
                               + $"({ex.GetType().Name}: {ex.Message}). The window is shown "
                               + "immediately, exactly as it is with the effect switched off.");
        }
    }

    /// <summary>
    /// <b>The window is being dismissed. Detach it from input NOW, let the pixels blow away, then
    /// run <paramref name="onDone"/>.</b>
    ///
    /// <para><b><paramref name="onDone"/> RUNS EXACTLY ONCE, ON EVERY PATH.</b> That is the whole
    /// contract, and the caller may treat this call as "the release happens, possibly later". When
    /// the effect is off / unavailable / impossible, it runs SYNCHRONOUSLY before this method
    /// returns, which makes the fallback today's code with a call in front of it. When the effect
    /// runs, it fires at the end of the dissolve — or earlier, if the host is deactivated or
    /// destroyed under us, or if the watchdog fires.</para>
    ///
    /// <para>The FIRST thing that happens, before any decision about whether the effect can run at
    /// all, is that the host's <c>GraphicRaycaster</c> is switched off. A player must never be able
    /// to click a ghost, and that must not depend on the effect working.</para>
    /// </summary>
    internal static void PlayOut(ConvertedPanel? panel, Action onDone)
    {
        if (onDone == null)
            throw new ArgumentNullException(nameof(onDone),
                "PlayOut's whole contract is that the callback runs; a null one is a caller bug, "
                + "and swallowing it would defer a window's release forever.");

        EnsureSubscribed();
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
        {
            NotePlayOutOutcome(panel, ref _outDeadPanel,
                               "THE PANEL WAS ALREADY GONE — dead, or its world host had been "
                               + "destroyed — so there was never anything to dissolve");
            onDone();
            return;
        }

        DetachInput(panel);

        // A SECOND RELEASE OF A FLOAT THAT IS ALREADY DISSOLVING MUST NOT PLAY A SECOND CLOUD.
        // The first runner's callback is the release; this call's callback is chained behind it,
        // so both run exactly once (the contract) and the eye sees one dissolve, at the pose the
        // first one started from. Before this clause, Cancel below would have cut the first
        // dissolve short (its callback releasing the host) and Begin would then have run on a
        // dead panel — no second cloud either, but a truncated first one.
        WindowMaterialiseRunner? running = FindVanishing(panel);
        if (running != null)
        {
            NoteStraySuppressed(panel, "a VANISH is already running for this float (a double "
                                       + "release) — the new release is chained behind the running "
                                       + "dissolve instead of starting a second one");
            NotePlayOutOutcome(panel, ref _outStray,
                               "SUPPRESSED AS A STRAY DISSOLVE — a vanish was already running for "
                               + "this float, so the release was chained behind it");
            running.ChainOnDone(onDone);
            return;
        }

        if (!Enabled)
        {
            NotePlayOutOutcome(panel, ref _outEffectOff,
                               "THE EFFECT IS SWITCHED OFF at its own dial, so the release ran "
                               + "synchronously, exactly as before the effect existed");
            onDone();
            return;
        }
        // A HOVER CARD LEAVES INSTANTLY — see IsHoverCard (ModBuild 367). The callback runs
        // synchronously here, which is PlayOut's documented "effect off" path: the card is
        // released in this frame, exactly as it was before the effect existed. Deferring a hover
        // card's release behind a 0.9 s dissolve would also keep it alive across the next hover.
        if (IsHoverCard(panel))
        {
            NotePlayOutOutcome(panel, ref _outHoverCard,
                               "IT IS A MAP-ROOM HOVER CARD (ModBuild 367), which arrives and "
                               + "leaves instantly by design — no dust either way");
            onDone();
            return;
        }
        // NOTHING ON THE SCREEN TO BLOW AWAY — the other half of the 2026-09-03 report. The dust
        // must not announce a window that is not there, and it must not FAREWELL one either: in
        // that same log 'GloomhavenVR.Panel_Modal_New Party display' played a full 0.90 s VANISH
        // (:5012) over a window whose content the game had taken away eight hundred lines earlier
        // and which the player therefore never saw standing.
        //
        // THE TERMS ARE LIFETIME PROPERTIES OF THE FLOAT, NOT A MEASUREMENT TAKEN HERE, and that
        // distinction is load-bearing: this class's OWN close-edge hold disables the window's
        // CanvasGroup component (WindowVisibilityHold concedes the alpha and takes the group out of
        // the multiply instead), and a drawability test would read the game's tweened-to-zero alpha
        // off that disabled group and call EVERY ordinary closing window empty — deleting the
        // vanish animation the user asked for. ModalFallback.HasNothingToDissolve carries the two
        // safe terms and the argument in full.
        //
        // Skipping is PlayOut's documented "effect off" path: the callback runs synchronously, the
        // release happens in this frame, and any close-edge hold this window took is released
        // first so nothing is left holding switches on a window that is being torn down.
        if (ModalFallback.HasNothingToDissolve(panel, out string nothingWhy))
        {
            DropPreRoll(panel, "the window has nothing on the screen to dissolve");
            // HW-VERIFY
            VRLog.Note(Scope, $"WINDOW MATERIALISE VANISH SKIPPED on '{Name(panel)}': {nothingWhy}. "
                              + "The float is released in this frame, exactly as it is with the "
                              + "effect switched off — no shards, no 0.9 s wait. USER REPORT "
                              + "(2026-09-03): dust for a window that was not there. IF THIS LINE "
                              + "APPEARS FOR A WINDOW THE PLAYER COULD SEE, this gate is wrong and "
                              + "the two terms it names are the place to look.");
            NotePlayOutOutcome(panel, ref _outSkipped,
                               "SKIPPED, because the float had nothing on the screen left to take "
                               + $"away: {nothingWhy}");
            onDone();
            return;
        }
        // A VANISH MAY ONLY PLAY WHERE A WINDOW WAS DRAWN ON THE PREVIOUS FRAME (user report,
        // 2026-09-03: at scenario start the dissolve appeared somewhere else entirely, where no
        // window was any more). The two lifetime terms above are the rule's first line; this is
        // the measured one: the order pass stamps every panel it found render-visible, with the
        // host's pose, in the last LateUpdate — so a panel with no stamp from the previous frame
        // was not on the screen for the eye to lose, and dust for it announces a window that is
        // not there. A host that moved since it was last drawn is refused for the same reason:
        // the pose the eye knows and the pose the cloud would spawn at are different places.
        if (!DrawnOnPreviousFrame(panel, out string strayWhy))
        {
            DropPreRoll(panel, "the window was not drawn on the previous frame");
            NoteStraySuppressed(panel, strayWhy);
            NotePlayOutOutcome(panel, ref _outStray,
                               $"SUPPRESSED AS A STRAY DISSOLVE — {strayWhy}");
            onDone();
            return;
        }
        float seconds = VanishSeconds;
        if (seconds <= 0f)
        {
            NotePlayOutOutcome(panel, ref _outZeroLength,
                               "THE VANISH LENGTH IS ZERO at its own dial, so the release ran "
                               + "synchronously with no animation");
            onDone();
            return;
        }

        try
        {
            Cancel(panel, "a vanish started");
            NotePlayOutOutcome(panel, ref _outStarted,
                               "STARTED — the dissolve and its shards are running now, and the "
                               + "release happens at the end of it",
                               SpawnClause(panel));
            if (!WindowMaterialiseRunner.Begin(panel, seconds, materialising: false, onDone))
                onDone();
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, $"WINDOW MATERIALISE: vanish on '{Name(panel)}' FAILED "
                               + $"({ex.GetType().Name}: {ex.Message}). The window is released in "
                               + "this frame, exactly as it is with the effect switched off.");
            Cancel(panel, "PlayOut threw");
            onDone();
        }
    }

    // ---- WHAT HAPPENED THE LAST TIME A WINDOW WAS ASKED TO DISSOLVE -----------------------------
    //
    // ModBuild 386 — A ZERO THAT MEANT TWO DIFFERENT THINGS. In the 2026-09-03 hardware log the
    // vanish-skip line had ZERO hits, and that reading was ambiguous between "no window was ever
    // skipped" and "PlayOut was never REACHED for the window in the report" — which is what it
    // actually was: 'UI Event Window' is sticky in the map room, so the release loop kept it and
    // the one call site never ran. A skip line can only ever say something about a call that
    // happened, so the missing half is an ENTRY line, and this is it.
    //
    // HOW TO READ THE NEXT LOG. Every one of these lines names the window and the outcome and
    // carries the running tally of all six outcomes, so:
    //   * a window that left the screen and has NO line here at all  ⇒ PlayOut was never reached
    //     (look at the release loop in ModalFallback.4.Tick.cs, not at the effect);
    //   * a line saying SKIPPED                                      ⇒ it was reached and refused
    //     by the ModBuild 374 rule, and the skip line beside it names which of its two terms;
    //   * a line saying STARTED                                      ⇒ the dissolve ran, and the
    //     "ended (completed)" report that follows carries the element and shard counts.
    //
    // CHANGE-GATED ON (outcome, window), NOT ON THE COUNTS, because the counts change on every
    // call and a gate that reads them is no gate at all. A window that opens and closes fifty
    // times with the same outcome therefore prints once and the fifty are still in the tally the
    // next DIFFERENT line prints. The counters are bumped before the gate, so nothing is lost.
    private static int _outDeadPanel;

    private static int _outEffectOff;

    private static int _outHoverCard;

    private static int _outSkipped;

    private static int _outZeroLength;

    private static int _outStarted;

    /// <summary>The seventh outcome (2026-09-03, "dust where no window was"): refused by the
    /// previous-frame rule or folded into a vanish already running. See
    /// <see cref="DrawnOnPreviousFrame"/>.</summary>
    private static int _outStray;

    private static string _lastOutKey = string.Empty;

    /// <param name="tail">An extra clause appended AFTER the change gate — it may vary per call
    /// (a pose does) without defeating the gate, which keys on the window and the outcome only.</param>
    private static void NotePlayOutOutcome(ConvertedPanel? panel, ref int counter, string outcome,
                                           string tail = "")
    {
        counter++;
        string who = panel != null ? Name(panel) : "<no panel>";
        string key = who + "\0" + outcome;
        if (key == _lastOutKey)
            return;
        _lastOutKey = key;
        int total = _outDeadPanel + _outEffectOff + _outHoverCard + _outSkipped
                    + _outZeroLength + _outStarted + _outStray;
        // HW-VERIFY
        VRLog.Note(Scope, $"WINDOW MATERIALISE PLAYOUT on '{who}': {outcome}. TALLY for this "
                          + $"session, {total} call(s) in total: {_outStarted} started, "
                          + $"{_outSkipped} skipped for having nothing to dissolve, "
                          + $"{_outHoverCard} hover card(s), {_outEffectOff} with the effect off, "
                          + $"{_outZeroLength} with a zero-length vanish, {_outDeadPanel} on a "
                          + "panel that was already gone. THIS LINE IS THE FALSIFIER: it is written "
                          + "on EVERY entry to PlayOut, so a floated window that left the screen "
                          + "and is named by no line here was never handed to the effect at all — "
                          + "that is a release-path question and not an effect question."
                          + $" STRAY RULE: {_outStray} suppressed for not having been drawn on the "
                          + "previous frame or for being a double release (grep STRAY DISSOLVE "
                          + "SUPPRESSED for the reason)."
                          + tail);
    }

    // ---- THE STRAY-DISSOLVE RULE ---------------------------------------------------------------
    //
    // USER REPORT (2026-09-03, translated): "At scenario start in the map room, when the story
    // opened for the first time, the DISSOLVE animation sometimes appears at a completely
    // different place, although there was no window there (any more)."
    //
    // THE ModBuild 410 LOG, READ END TO END FOR THAT MOMENT: 'New Party display' went DORMANT at
    // :4257 (EMPTY WINDOW HIDDEN — the game had emptied it; the liveness rule switched every Canvas
    // and Renderer off and kept the seat), and the PANEL DRAW ORDER census kept reporting it
    // "hidden: DORMANT" (:4270, :4297, :4385, :4418). The story curtain rose at :4420, released it
    // ON REFUSAL at :4423, and PlayOut STARTED a 0.90 s vanish at :4424 — 900 shards over a 2.09 x
    // 1.13 m frame at host pose (63.83, 225.24, -290.15), the seat it had claimed at world yaw 94
    // degrees (:4145), 35 degrees to the right of the story window at yaw 59 (:4406). The vanish
    // ended at :4459 having "OWNED 18 UIWindow(s), claimed at the release tick". The eye saw dust
    // over an empty seat, next to the window it was reading.
    //
    // WHY THE ModBuild 374 GATE DID NOT CATCH IT: ModalFallback.HasNothingToDissolve searched
    // `Converted` for the float, and the release loop had ALREADY removed it. The gate was inert on
    // its only call site for 36 builds (0 VANISH SKIPPED lines in any log). Fixed at the source
    // (StampReleaseVerdict, taken before the removal), and backed by the rule below, which does not
    // depend on which list the float is in.
    //
    // THE RULE: a vanish may only play where a window was DRAWN ON THE PREVIOUS FRAME, and it spawns
    // from the pose of THAT frame. "Drawn" is the mod's own visibility state as stamped by the order
    // pass (CanvasConversion.StampShownPose, the last WorldUI LateUpdate step): the reveal gate open,
    // no render hide, host active, host canvas on. It is deliberately NOT the game's alpha, which
    // the close-edge hold confounds and which would refuse every ordinary close.

    /// <summary>A host that moved more than this, in real metres, between the frame it was last
    /// drawn and the release is refused: the eye knows one place and the cloud would appear in
    /// another. One frame of an ordinary grab-follow moves millimetres; a re-seat moves decimetres.</summary>
    private const float StrayMoveMetres = 0.10f;

    private static int _strayPrinted;

    private static string _strayLastKey = string.Empty;

    /// <summary>
    /// <b>Was this panel render-visible in the last order pass, and is its host still where it
    /// was?</b> Returns true when the instrument itself has not run since the previous frame —
    /// no stamp is no verdict, and a rule that refused every vanish because its instrument was
    /// asleep would delete the effect the user asked for.
    /// </summary>
    internal static bool DrawnOnPreviousFrame(ConvertedPanel panel, out string why)
    {
        int now = Time.frameCount;
        int stampRan = CanvasConversion.ShownStampFrame;
        if (stampRan < now - 1)
        {
            why = $"the shown-pose stamp has not run since frame {stampRan} (now {now}) — no "
                  + "verdict, the vanish plays";
            return true;
        }
        if (panel.LastShownFrame < now - 1)
        {
            why = panel.LastShownFrame == 0
                ? "it was NEVER render-visible in any order pass since it floated (reveal gate, "
                  + "render hide or an inactive host on every frame), so the eye never saw it "
                  + "standing"
                : $"it was last render-visible on frame {panel.LastShownFrame}, "
                  + $"{now - panel.LastShownFrame} frame(s) before this release — hidden since "
                  + "(dormant, render-hidden or inactive), so there was no window on the screen "
                  + "to lose";
            return false;
        }
        RectTransform? host = panel.HostRect;
        if (host != null)
        {
            float scale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
            float movedM = Vector3.Distance(host.position, panel.LastShownPosition) / scale;
            if (movedM > StrayMoveMetres)
            {
                why = $"its host moved {movedM:F2} m since the frame it was last drawn (more than "
                      + $"{StrayMoveMetres:F2} m), so the cloud would spawn somewhere the eye never "
                      + "saw the window";
                return false;
            }
        }
        why = $"render-visible on frame {panel.LastShownFrame} (now {now})";
        return true;
    }

    /// <summary>
    /// The clause appended to the vanish's START and END lines: where the cloud spawns (world, and
    /// real metres from the head), whether the window was drawn on the previous frame, how far the
    /// host has moved since, and what released it. Computed once at the start of the effect.
    /// </summary>
    internal static string SpawnClause(ConvertedPanel panel)
    {
        RectTransform? host = panel.HostRect;
        bool stamped = panel.LastShownFrame > 0;
        Vector3 pos = stamped ? panel.LastShownPosition : host != null ? host.position : Vector3.zero;
        float scale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
        Camera? cam = CanvasConversion.WorldCamera;
        string head = cam != null
            ? $"{Vector3.Distance(cam.transform.position, pos) / scale:F2} m from the head"
            : "head distance unknown (no world camera)";
        int now = Time.frameCount;
        bool drawnPrev = stamped && panel.LastShownFrame >= now - 1;
        string moved = stamped && host != null
            ? $"{Vector3.Distance(host.position, panel.LastShownPosition) / scale * 1000f:F0} mm"
            : "n/a (never stamped)";
        string trigger = panel.ReleaseTrigger.Length > 0 ? panel.ReleaseTrigger : "<not stamped>";
        return $" SPAWN POSE: world ({pos.x:F2}, {pos.y:F2}, {pos.z:F2}), {head}, taken from "
               + (stamped ? $"the last drawn frame {panel.LastShownFrame}" : "the live host (no stamp)")
               + $"; DRAWN ON THE PREVIOUS FRAME: {(drawnPrev ? "YES" : "NO")} (now {now}); host "
               + $"moved {moved} since it was last drawn; TRIGGER: {trigger}.";
    }

    /// <summary>The runner already dissolving this panel, if any — for the double-release fold.</summary>
    private static WindowMaterialiseRunner? FindVanishing(ConvertedPanel panel)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            WindowMaterialiseRunner r = Live[i];
            if (r != null && r.Vanishing && ReferenceEquals(r.Panel, panel))
                return r;
        }
        return null;
    }

    /// <summary>The <c>STRAY DISSOLVE SUPPRESSED</c> line: change-gated on (window, reason) and
    /// capped at six per session. Every hit names a vanish the eye would have seen over nothing.</summary>
    private static void NoteStraySuppressed(ConvertedPanel panel, string reason)
    {
        if (_strayPrinted >= 6)
            return;
        string key = Name(panel) + "\0" + reason;
        if (key == _strayLastKey)
            return;
        _strayLastKey = key;
        _strayPrinted++;
        // HW-VERIFY
        VRLog.Note(Scope, $"STRAY DISSOLVE SUPPRESSED on '{Name(panel)}' ({_strayPrinted}/6 this "
                          + $"session): {reason}. The float is released in this frame with no shards "
                          + "and no 0.9 s wait, exactly as with the effect switched off. USER REPORT "
                          + "(2026-09-03): the dissolve appeared where no window was (any more) — in "
                          + "the ModBuild 410 log that was a DORMANT 'New Party display' released by "
                          + "the story curtain and dissolved over its empty seat. IF THIS LINE NAMES A "
                          + "WINDOW THE PLAYER COULD SEE ON THE FRAME BEFORE, the previous-frame "
                          + "stamp (CanvasConversion.StampShownPose) is the place to look."
                          + SpawnClause(panel));
    }

    /// <summary>
    /// Stop any effect on this panel and put every alpha it wrote back. Safe to call on a panel
    /// with no effect, on a dead panel and from inside a runner's own teardown.
    ///
    /// <para>A cancelled VANISH still runs its callback — cancelling the ANIMATION must never
    /// cancel the RELEASE, or the window is left alive with no owner.</para>
    /// </summary>
    internal static void Cancel(ConvertedPanel? panel, string reason)
    {
        if (panel == null)
            return;
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            WindowMaterialiseRunner r = Live[i];
            if (r == null)
            {
                Live.RemoveAt(i);
                continue;
            }
            if (ReferenceEquals(r.Panel, panel))
                r.Finish(reason, restore: true);
        }
    }

    /// <summary>Stop everything and put every window back the way it was. For scenario exit, VR
    /// off and module shutdown — a bulk release must not leave five windows dissolving into a scene
    /// that is being torn down, and it must not defer five releases behind an animation.</summary>
    internal static void CancelAll(string reason)
    {
        for (int i = Live.Count - 1; i >= 0; i--)
            Live[i]?.Finish(reason, restore: true);
        Live.Clear();
        // The close-edge holds go with them, and for the same reason: a bulk release must not leave
        // a window's visibility owned by a decoration in a scene that is being torn down.
        for (int i = PreRolls.Count - 1; i >= 0; i--)
            PreRolls[i]?.End(reason);
        PreRolls.Clear();
    }

    // ---- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// Input off, in one place, before anything else. The host's <c>GraphicRaycaster</c> is the
    /// window's uGUI hit surface and the laser's only way in; disabling it is immediate and total.
    ///
    /// <para>What is deliberately NOT done here: the poke registration and the grab bar's
    /// <c>BoxCollider</c>. Both are the caller's, both are already dropped by the teardown the
    /// caller runs around this call (<c>wp.Grab?.Destroy()</c> and
    /// <c>UguiPokeSurfaces.Unregister</c> inside <c>CanvasConversion.Release</c>), and reaching
    /// into another subsystem's registry from a decoration would be a second owner for state that
    /// already has one.</para>
    /// </summary>
    private static void DetachInput(ConvertedPanel panel)
    {
        try
        {
            if (panel.HostRaycaster != null)
                panel.HostRaycaster.enabled = false;
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope, $"WINDOW MATERIALISE: could not disable the raycaster on "
                              + $"'{Name(panel)}' ({ex.GetType().Name}). The caller's own teardown "
                              + "destroys the host and with it the raycaster, so the ghost cannot "
                              + "outlive the release either way.");
        }
    }

    internal static string Name(ConvertedPanel? panel) =>
        panel == null ? "<null>"
        : panel.HostGo != null ? panel.HostGo.name
        : panel.Target != null ? panel.Target.name
        : "<released>";

    /// <summary>
    /// <b>A CARD THE POINTER MERELY TOUCHES IS NOT A WINDOW THAT ARRIVES</b> (ModBuild 367).
    /// User, 2026-09-03, for the second time: <i>"Dieselbe Animation die bei den Fenstern kommt
    /// (auflösen und materialisieren mit dem Staub), kommt immer noch auch bei dem Mouseover von
    /// dem Kartensymbolen. Hier soll es nicht kommen, es handelt sich hier nicht um ein Fenster in
    /// dem Sinne. Diesen request hatte ich schon gestellt, ist aber nach wie vor da."</i>
    ///
    /// <para><b>THE FIRST ATTEMPT MISSED BECAUSE IT NAMED THE WRONG ANIMATION.</b> ModBuild 365
    /// took the map symbol's own hover animation off — the 20 % <c>MeshParent</c> scale pop and
    /// the <c>NodeHoverIndicator</c> particles, patched on <c>MapLocation.Highlight</c>. That is a
    /// DIFFERENT animation on a DIFFERENT object, and switching it off could not touch this one:
    /// the dust he is describing is the mod's own window decoration, played on the card the hover
    /// raises. The 2026-09-03 log settles it in one read — 6 <c>WINDOW MATERIALISE APPEAR</c> and
    /// 13 <c>WINDOW MATERIALISE VANISH</c> lines, every one of them on
    /// <c>'GloomhavenVR.Panel_Modal_UI Quest Preview Popup'</c>, which is the map room's hover
    /// card and nothing else. Nineteen ceremonies for nineteen pointer sweeps.</para>
    ///
    /// <para><b>THE CLASS, NOT THE ONE WINDOW.</b> The distinction the effect needs is exactly the
    /// one <see cref="ModalFallback.IsMapRoomHoverCard"/> already draws and already justifies at
    /// length: a window the player OPENS is a thing that arrives and may take a moment doing it; a
    /// card that opens and closes WITH THE POINTER is a readout, and a readout that has to
    /// assemble itself before it can be read is worse than one that is simply there. So the
    /// tooltips go with the quest preview — the same rule, matched by COMPONENT and never by
    /// name.</para>
    ///
    /// <para><b>THIS IS A DECORATION GATE AND NOTHING ELSE.</b> The card still floats, still gets
    /// its seat, still follows the head, still closes with the hover. All that changes is that it
    /// appears and disappears in one frame, which is what it did before the effect existed and
    /// what every tooltip in every other part of the game does. Nothing here writes to the game.
    /// </para>
    ///
    /// <para>Resolved from the panel's own conversion target: <c>ModalFallback</c>'s test wants the
    /// <c>UIWindow</c>, and the rect a window is converted through is not required to BE that
    /// window's transform (containment is not identity). So the window is looked up on the target
    /// and, failing that, the two hover-card components are asked for directly — a target that
    /// carries the preview popup is the preview popup whether or not the <c>UIWindow</c> sits on
    /// the same GameObject.</para>
    /// </summary>
    internal static bool IsHoverCard(ConvertedPanel? panel)
    {
        RectTransform? target = panel?.Target;
        if (target == null)
            return false;
        if (!MapRoom.MapRoomDriver.Active)
            return false;
        var window = target.GetComponent<UIWindow>();
        if (window != null)
            return ModalFallback.IsMapRoomHoverCard(window);
        return target.GetComponent<UIQuestPreviewPopup>() != null
               || target.GetComponent<UILocalTooltip>() != null;
    }
}

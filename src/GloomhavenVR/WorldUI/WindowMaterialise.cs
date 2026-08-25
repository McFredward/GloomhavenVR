using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
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
/// <para><b>"ES SOLL NIEMANDEN AUFHALTEN" IS THE ACCEPTANCE CRITERION, NOT A PREFERENCE</b>, and it
/// sits directly on top of a standing ruling this project has already lost a build to: <i>"es MUSS
/// immer möglich sein das Optionsmenu zu öffnen"</i> (see .planning/OPTIONS-MENU-NEVER-BLOCKED.md).
/// So the whole class is built around four properties, each of which is reached BY CONSTRUCTION
/// rather than by a catch:</para>
/// <list type="number">
/// <item><b>Nothing sits between a keypress and a window opening.</b> <see cref="PlayIn"/> is
///   called AFTER the window has been made visible and interactive; it never gates, never delays
///   and never returns a "not yet". The window's collider, its <c>GraphicRaycaster</c> and its
///   laser target are up before this class hears about it, and it does not touch any of them.</item>
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
                "How large the flying debris is. 0 leaves the element-by-element dissolve with no "
                + "debris at all (a clean directional wipe, and no extra geometry is built at all); "
                + "1 is the shipped look; 2 is heavy rubble. This scales an AMPLITUDE only — it "
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
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
            return;
        if (!Enabled)
            return;
        float seconds = AppearSeconds;
        if (seconds <= 0f)
            return;

        try
        {
            Cancel(panel, "a new appear started");
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

        if (panel == null || !panel.IsAlive || panel.HostGo == null)
        {
            onDone();
            return;
        }

        DetachInput(panel);

        if (!Enabled)
        {
            onDone();
            return;
        }
        float seconds = VanishSeconds;
        if (seconds <= 0f)
        {
            onDone();
            return;
        }

        try
        {
            Cancel(panel, "a vanish started");
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
}

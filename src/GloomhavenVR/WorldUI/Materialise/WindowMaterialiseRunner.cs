using System;
using System.Collections.Generic;
using System.Diagnostics;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>ONE PLAYING EFFECT.</b> A component on a mod-owned child of the window's host rect, which is
/// what makes the dangerous cases safe: it dies with the host, so "the window was destroyed under
/// us" is <see cref="OnDestroy"/>, and "the window was deactivated under us" is
/// <see cref="OnDisable"/>. Both funnel into <see cref="Finish"/>, which puts every alpha back and
/// runs the pending release exactly once.
///
/// <para><b>THE ONE FAILURE THIS CLASS EXISTS TO MAKE IMPOSSIBLE</b> is an animation state that can
/// be entered and not left, because leaving one un-left means a window that is alive, listed,
/// clickable and completely invisible. There is no "playing" flag anywhere that is not owned by an
/// object whose destruction is itself an exit. Enumerate the ways out: the ramp completes; the host
/// is deactivated; the host is destroyed; the scene is torn down; <c>LateUpdate</c> throws; the
/// window is closed while materialising; the window is re-opened while dematerialising; the effect
/// is switched off mid-flight; the watchdog expires. Every one of them lands in
/// <see cref="Finish"/>, and <see cref="Finish"/> is idempotent.</para>
///
/// <para><b>WHY LateUpdate.</b> Two reasons, both learned here. It is the last writer, so anything
/// that also writes these alphas during the frame has already had its say (this project's ruling
/// after a four-way write war: own the FINAL value in LateUpdate, do not fight for the flag). And
/// MultiPass renders both eyes after LateUpdate, so a value written here is the same value in both
/// eyes by construction — the same reason <c>CanvasConversion.CompleteReveal</c> insists on being
/// there.</para>
///
/// <para><b>WHAT A FRAME COSTS, AND WHY IT IS ALL ON THE ELEMENT HALF.</b> The debris is real
/// world-space geometry whose every trajectory is a closed function of one uniform, so driving a few
/// hundred flying shards costs <b>two <c>SetFloat</c>s and two <c>SetPropertyBlock</c>s</b>, one per
/// half — the same per frame whether there are ninety shards or four hundred and twenty, which the
/// cost harness confirmed by measuring 0.4 µs at both. The measurable per-frame
/// cost of this effect is entirely the element half: five <c>smoothstep</c>s and one native
/// <c>SetAlpha</c> per <c>CanvasRenderer</c>. That is what <see cref="Report"/> prints, and it is
/// why the shard count does not appear in the per-frame figure.</para>
/// </summary>
internal sealed class WindowMaterialiseRunner : MonoBehaviour
{
    private const string Scope = "WorldUI";

    // Per-frame (two of them).
    private static readonly int FrontId = Shader.PropertyToID("_Front");
    private static readonly int SizeScaleId = Shader.PropertyToID("_SizeScale");
    // Per-effect constants, pushed once in Begin.
    private static readonly int LifeSpanId = Shader.PropertyToID("_LifeSpan");
    private static readonly int WindId = Shader.PropertyToID("_Wind");
    private static readonly int DriftId = Shader.PropertyToID("_Drift");
    private static readonly int FallId = Shader.PropertyToID("_Fall");
    private static readonly int SpinTurnsId = Shader.PropertyToID("_SpinTurns");

    /// <summary>Reused element lists. A big window is several hundred CanvasRenderers and this
    /// effect runs on every window open and close, so the walk must not allocate a fresh array each
    /// time.</summary>
    private static readonly Stack<List<CanvasRenderer>> RendererPool = new(8);
    private static readonly Stack<List<float>> FloatPool = new(16);

    internal ConvertedPanel Panel = null!;

    private float _seconds;
    private bool _materialising;

    /// <summary>True while this effect is a VANISH — the direction whose float has already left
    /// <c>Converted</c> and whose release is still pending.</summary>
    internal bool Vanishing => !_materialising;

    /// <summary>Seconds this effect has been running. Read-only, and offered for ONE caller:
    /// <see cref="WindowMaterialise.EndVanishNow"/>, whose log line has to say how much of the
    /// dissolve was still owed when a re-open cut it short. Nothing decides on it.</summary>
    internal float Elapsed => _elapsed;

    /// <summary>The duration this effect was started with (after the code clamp). Same single
    /// reader as <see cref="Elapsed"/>, same reason.</summary>
    internal float Total => _seconds;

    private Action? _onDone;
    private float _elapsed;
    private bool _finished;
    private bool _reported;

    private List<CanvasRenderer>? _renderers;
    private List<float>? _origAlpha;
    private List<float>? _threshold;

    private WindowMaterialise.DebrisCloud? _debris;
    private MaterialPropertyBlock? _mpb;

    /// <summary>
    /// <b>Who owns the window's visibility while a VANISH plays.</b> Null on an appear and on a
    /// vanish that found no <c>UIWindow</c> to hold.
    ///
    /// <para>Without it the dissolve draws on a window the game has already emptied: the game's own
    /// <c>UIWindow</c> fades its <c>CanvasGroup</c> to 0 in 0.1 s, deactivates the GameObject and
    /// switches the <c>Canvas</c> off, all of it inside the first ninth of a 0.9 s vanish. That is
    /// the 2026-09-02 report in one sentence — <i>"ploppt das Fenster erst weg, danach kommt die
    /// Animation"</i>. See <see cref="WindowVisibilityHold"/> for the decompiled line numbers and
    /// for which of the three channels is conceded and which two are owned in LateUpdate.</para>
    /// </summary>
    private WindowVisibilityHold? _hold;

    /// <summary>How many <c>UIWindow</c>s the hold covers, kept separately because the hold is
    /// released before <see cref="Report"/> runs — and this is the number a hardware log is read
    /// against to answer "did the ownership engage at all, or did the dissolve draw on a window the
    /// game had already emptied?".</summary>
    private int _held;

    /// <summary>Whether the hold came from the CLOSE EDGE rather than from this call. The
    /// difference is one <c>ModalFallback.Tick</c>, and it is the difference that matters on an
    /// INSTANT hide, where the game empties the window inside its own call.</summary>
    private bool _heldFromEdge;

    /// <summary>ModBuild 405: of <see cref="_held"/>, how many the hold actually drives and how
    /// many were nested windows the game held hidden and were left alone
    /// (<c>WindowVisibilityHold.Rec.Held</c>). Copied at claim time because the hold is released
    /// before <see cref="Report"/> runs.</summary>
    private int _heldGroups, _leftToGame;

    // Measured, not estimated. Printed once per effect at its end.
    private readonly Stopwatch _watch = new();
    private double _worstMs;
    private double _totalMs;
    private int _frames;
    private double _buildMs;

    /// <summary>
    /// <b>The pane is not a pointer surface while its elements are missing.</b> True for the whole
    /// of a VANISH and for an APPEAR until the element front has swept the window
    /// (<c>k &gt;= WindowMaterialiseField.ElementSpan</c>); false once the window is whole and only
    /// dust is still settling on it.
    ///
    /// <para><b>THE 2026-09-03 REPORT, IN ONE SENTENCE:</b> <i>"Die Partikel der Materialisieren-
    /// bzw. Verpuffen-Animation von Fenstern colliden aktuell mit dem Laser."</i> The dust carries
    /// no collider and no graphic, so no pointer path ever hit a shard — what the beam stopped at
    /// was the WINDOW'S OWN CANVAS PLANE. <c>RayUguiDriver</c> clamps the beam to every registered
    /// canvas that is <c>isActiveAndEnabled</c> on mere hover of its plane, with no term for the
    /// raycaster, the group or this effect; a vanishing window stays registered until
    /// <c>CanvasConversion.Release</c> runs at the END of the dissolve and its canvas is HELD ON by
    /// <see cref="WindowVisibilityHold"/> for the whole 0.9 s, so the beam ended on an invisible
    /// pane in the middle of the dust cloud — which is exactly what "collides with the particles"
    /// looks like. <see cref="WindowMaterialise.IsPointerBlind"/> is what the far-ray and poke
    /// drivers ask, and this is the bit they read.</para>
    /// </summary>
    internal bool PointerBlind { get; private set; }

    /// <summary>How long the pane was pointer-blind, accumulated in unscaled seconds; printed by
    /// <see cref="Report"/> beside the duration so a log shows the blind window was the element
    /// sweep and not the whole appear.</summary>
    private float _blindSeconds;

    // Censused ONCE in Begin, straight after the debris is built, and printed by Report: what the
    // effect's own objects are to a pointer. A physics ray needs a Collider; a bounds walk would
    // see a Renderer; neither should find anything here, and the line says so in numbers.
    private string _effectRootName = string.Empty;
    private int _effectLayer = -1;
    private int _effectColliders;
    private string _effectRendererTypes = string.Empty;

    /// <summary>
    /// Start an effect on <paramref name="panel"/>. Returns false when nothing could be started, in
    /// which case NOTHING has been written and the caller is exactly where it was — for
    /// <c>PlayOut</c> that means running the release inline, which is today's behaviour.
    /// </summary>
    internal static bool Begin(ConvertedPanel panel, float seconds, bool materialising, Action? onDone)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null || panel.HostRect == null)
            return false;

        RectTransform host = panel.HostRect;
        Rect r = host.rect;
        if (r.width <= 0.01f || r.height <= 0.01f)
            return false;

        // THE CARRIER IS CREATED FIRST AND UNCONDITIONALLY, before any decision about debris. It is
        // what owns the effect's lifetime: it is parented to the host, so every "the window died
        // under us" case is one of this component's own Unity messages. The debris, if there is any,
        // hangs off it and is destroyed with it.
        var carrier = new GameObject(WindowMaterialise.DebrisName + ".Runner")
        {
            layer = panel.HostGo.layer,
        };
        Transform ct = carrier.transform;
        ct.SetParent(host, worldPositionStays: false);
        ct.localPosition = Vector3.zero;
        ct.localRotation = Quaternion.identity;
        ct.localScale = Vector3.one;
        // A VANISH SPAWNS FROM THE POSE OF THE LAST DRAWN FRAME, never from the live host: the
        // carrier is still parented to the host (its lifetime is the host's), but its world pose is
        // the one the order pass stamped in the previous LateUpdate, and its scale undoes any host
        // scale change made since — so a re-seat between the last drawn frame and the release
        // cannot move the cloud to a place the eye never saw the window. The previous-frame rule in
        // PlayOut has already refused anything that moved more than StrayMoveMetres; what is left
        // is at most one frame of motion. An APPEAR is left at the host: it is being revealed at
        // its final pose in this very frame and has no drawn frame behind it yet.
        if (!materialising && panel.LastShownFrame > 0)
        {
            ct.SetPositionAndRotation(panel.LastShownPosition, panel.LastShownRotation);
            Vector3 cur = host.lossyScale;
            Vector3 was = panel.LastShownLossyScale;
            ct.localScale = new Vector3(SafeRatio(was.x, cur.x), SafeRatio(was.y, cur.y),
                                        SafeRatio(was.z, cur.z));
        }

        var runner = carrier.AddComponent<WindowMaterialiseRunner>();
        runner.Panel = panel;
        runner._seconds = Mathf.Max(seconds, WindowMaterialise.MinSeconds);
        runner._materialising = materialising;
        runner._onDone = onDone;
        runner._spawnClause = materialising ? string.Empty : WindowMaterialise.SpawnClause(panel);

        // THE VISIBILITY HOLD IS TAKEN BEFORE ANYTHING IS DRAWN, and on a VANISH ONLY. On a vanish
        // the game has already started emptying the window from under us (its own 0.1 s alpha
        // tween, its ChangeActive and its _disableCanvas switch — all three verified in
        // WindowVisibilityHold's doc), so this is what makes the dissolve a dissolve instead of an
        // animation played over a window that has already gone.
        //
        // AN APPEAR IS DELIBERATELY LEFT ALONE. The user's report on 334 was explicit that the
        // fade-in already works ("Das Einfaden funktioniert schon gut"), the game is on its way to
        // SHOWING that window rather than emptying it, and a live window whose focus, re-show and
        // parallel-window behaviour the game still manages is not one a decoration should be
        // holding switches on. One direction, one reason.
        //
        // The hold is CLAIMED from the close edge when there is one — handed over live, so the
        // window's visibility passes from one owner to the next without a frame in between — and
        // taken fresh otherwise, which is the path an interrupted or re-entered vanish takes.
        if (!materialising)
        {
            runner._hold = WindowMaterialise.ClaimPreRoll(panel);
            runner._heldFromEdge = runner._hold != null;
            if (runner._hold == null)
            {
                var hold = new WindowVisibilityHold();
                hold.Capture(host, panel.Target);
                runner._hold = hold.Count > 0 ? hold : null;
            }
            runner._held = runner._hold?.Count ?? 0;
            runner._heldGroups = runner._hold?.HeldCount ?? 0;
            runner._leftToGame = runner._hold?.LeftToGameCount ?? 0;
        }

        var build = Stopwatch.StartNew();
        try
        {
            runner.CollectElements(host);
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, $"WINDOW MATERIALISE: collecting the elements of "
                               + $"'{WindowMaterialise.Name(panel)}' threw "
                               + $"({ex.GetType().Name}: {ex.Message}). Nothing has been written to "
                               + "the window; it behaves exactly as with the effect off.");
            // The caller runs onDone itself when Begin returns false, so this runner must NOT also
            // run it — a release that happens twice is as bad as one that never happens.
            runner._onDone = null;
            runner.Finish("element collection threw", restore: true);
            return false;
        }

        // THE DEBRIS IS SEEDED FROM THE ELEMENTS, which is why it is built after them and takes them
        // as an argument. A failure here is not an error: the element-by-element dissolve is the half
        // that actually removes the window, and it runs perfectly well with no shards.
        try
        {
            if (runner._renderers != null && runner._origAlpha != null
                && WindowMaterialise.TryBuildDebris(panel, runner._renderers, runner._origAlpha,
                                                    out WindowMaterialise.DebrisCloud? cloud)
                && cloud != null)
            {
                cloud.Go.transform.SetParent(ct, worldPositionStays: false);
                runner._debris = cloud;
                runner._mpb = new MaterialPropertyBlock();
                // Every shape constant is pushed from WindowMaterialiseField so the shader and the
                // CPU field cannot drift apart. The shader's Properties block holds the same numbers,
                // but only as the value an editor preview would show.
                float canvasPerMetre = 1f / Mathf.Max(cloud.ApparentMetresPerCanvasUnit, 1e-7f);
                runner._mpb.SetVector(WindId,
                    new Vector4(cloud.WindCanvas.x, cloud.WindCanvas.y, 0f, 0f));
                runner._mpb.SetFloat(LifeSpanId, WindowMaterialiseField.DebrisLifeSpan);
                runner._mpb.SetFloat(DriftId,
                    WindowMaterialiseField.DebrisDriftMetres * canvasPerMetre);
                runner._mpb.SetFloat(FallId,
                    WindowMaterialiseField.DebrisFallMetres * canvasPerMetre);
                runner._mpb.SetFloat(SpinTurnsId, 1f);
            }
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, $"WINDOW MATERIALISE: building the debris for "
                               + $"'{WindowMaterialise.Name(panel)}' threw "
                               + $"({ex.GetType().Name}: {ex.Message}). The window still dissolves "
                               + "element by element, with no shards.");
            runner._debris = null;
            runner._mpb = null;
        }
        build.Stop();
        runner._buildMs = build.Elapsed.TotalMilliseconds;
        runner.CensusPointerSurface(carrier);

        WindowMaterialise.Register(runner);

        // WRITE FRAME ZERO NOW, NOT NEXT LateUpdate. PlayIn is called from inside the reveal's own
        // LateUpdate, and rendering happens after LateUpdate — so if the first write waited for the
        // NEXT frame's LateUpdate, the eye would see one frame of a fully opaque window before the
        // materialise started. That single frame is the "aufploppen" the user asked to be rid of.
        runner.Apply(0f);
        return true;
    }

    /// <summary>
    /// Snapshot every <c>CanvasRenderer</c> under the host: what its alpha is now (so it can be put
    /// back exactly), and where it sits in panel UV (so the front reaches it at the right moment).
    ///
    /// <para>The thresholds are computed ONCE, here, because they do not change over the life of
    /// the effect — only the front moves. That is what makes the per-frame cost five
    /// <c>smoothstep</c>s and one native <c>SetAlpha</c> per element instead of five noise
    /// evaluations per element per frame. FIVE points per element rather than one, because the
    /// biggest CanvasRenderer in a window is its background plate and a plate judged by its
    /// centre cross-fades as one block in the middle of the sweep — see
    /// <see cref="WindowMaterialiseField.PresenceOf"/>.</para>
    ///
    /// <para><c>includeInactive: true</c> on purpose: an element that is inactive now and drawn later
    /// would otherwise come back at full alpha in the middle of a dissolve. Writing an alpha on an
    /// inactive renderer costs nothing and draws nothing.</para>
    /// </summary>
    private void CollectElements(RectTransform host)
    {
        _renderers = RendererPool.Count > 0 ? RendererPool.Pop() : new List<CanvasRenderer>(256);
        _origAlpha = FloatPool.Count > 0 ? FloatPool.Pop() : new List<float>(256);
        _threshold = FloatPool.Count > 0 ? FloatPool.Pop() : new List<float>(1280);
        _renderers.Clear();
        _origAlpha.Clear();
        _threshold.Clear();

        host.GetComponentsInChildren(true, _renderers);

        Rect r = host.rect;
        float aspect = r.width / Mathf.Max(r.height, 0.01f);
        for (int i = 0; i < _renderers.Count; i++)
        {
            CanvasRenderer cr = _renderers[i];
            if (cr == null)
            {
                _origAlpha.Add(1f);
                for (int k = 0; k < WindowMaterialiseField.Samples; k++)
                    _threshold.Add(1f);
                continue;
            }
            // ModBuild 405: THROUGH THE VEIL. A renderer the hidden-window veil is holding reads
            // 0 here, and capturing that zero as its "own" alpha restored it as zero in Finish —
            // one half of the black character column in the 404 log. The veil keeps the pre-veil
            // value; this asks for it and falls back to the channel itself when nothing holds it.
            _origAlpha.Add(CanvasConversion.PreVeilAlpha(cr, cr.GetAlpha()));

            var rt = cr.transform as RectTransform;
            for (int k = 0; k < WindowMaterialiseField.Samples; k++)
            {
                Vector2 uv = new Vector2(0.5f, 0.5f);
                if (rt != null)
                    uv = UvOf(host, r, rt, SampleU[k], SampleV[k]);
                _threshold.Add(WindowMaterialiseField.Threshold(uv, aspect));
            }
        }
    }

    /// <summary>
    /// What the effect's own objects are to a pointer, censused once and printed at the end. The
    /// root is the debris carrier when there is one (it is the object <c>VRLayers.Apply</c> moved
    /// to the mod layer) and this runner's own carrier otherwise. Colliders and renderers are
    /// counted over the WHOLE subtree, because a pointer path would find one anywhere in it.
    /// </summary>
    private void CensusPointerSurface(GameObject carrier)
    {
        try
        {
            GameObject root = _debris?.Go != null ? _debris.Go : carrier;
            _effectRootName = root.name;
            _effectLayer = root.layer;
            _effectColliders = carrier.GetComponentsInChildren<Collider>(true).Length;
            Renderer[] rends = carrier.GetComponentsInChildren<Renderer>(true);
            var types = new List<string>(2);
            for (int i = 0; i < rends.Length; i++)
            {
                string tn = rends[i].GetType().Name;
                if (!types.Contains(tn))
                    types.Add(tn);
            }
            _effectRendererTypes = types.Count > 0
                ? $"{string.Join("+", types)} x{rends.Length}"
                : "none";
        }
        catch (Exception ex)
        {
            _effectRendererTypes = $"census threw {ex.GetType().Name}";
        }
    }

    /// <summary>Where the four corners and the centre of an element are sampled, in its own
    /// normalised rect.</summary>
    private static readonly float[] SampleU = { 0f, 1f, 0f, 1f, 0.5f };
    private static readonly float[] SampleV = { 0f, 0f, 1f, 1f, 0.5f };

    /// <summary>One point of an element, in PANEL UV.</summary>
    private static Vector2 UvOf(RectTransform host, Rect hostRect, RectTransform rt, float u, float v)
    {
        Rect er = rt.rect;
        Vector3 world = rt.TransformPoint(new Vector3(er.xMin + u * er.width,
                                                      er.yMin + v * er.height, 0f));
        Vector3 local = host.InverseTransformPoint(world);
        // CLAMPED, and this is load-bearing rather than defensive. Elements really do sit outside
        // the host rect - PanelInkBounds found a quest window drawing its reward row 373 px BELOW
        // its own rect's bottom edge. An unclamped UV puts the threshold outside 0..1, and the
        // field is only exact at its endpoints while the threshold is inside them. Clamping is what
        // keeps "restored" meaning literally the alpha that was there.
        return new Vector2(
            Mathf.Clamp01((local.x - hostRect.xMin) / hostRect.width),
            Mathf.Clamp01((local.y - hostRect.yMin) / hostRect.height));
    }

    private void LateUpdate()
    {
        // UNGUARDED Update BODIES STARVE INPUT IN THIS PROJECT. A single NRE out of a per-frame
        // method has taken the whole WorldUI driver down before; a decoration is never allowed to
        // do that, so the body is wrapped and a throw ENDS the effect cleanly rather than repeating
        // 19,853 times.
        try
        {
            if (_finished)
                return;

            if (Panel == null || !Panel.IsAlive || Panel.HostGo == null)
            {
                Finish("the panel died under the effect", restore: true);
                return;
            }
            if (!WindowMaterialise.Enabled)
            {
                Finish("the effect was switched off mid-flight", restore: true);
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            if (PointerBlind)
                _blindSeconds += Time.unscaledDeltaTime;

            // THE WATCHDOG. Not a timing mechanism — the ramp below finishes on its own — but the
            // answer to "what if _seconds was somehow zero, or unscaledDeltaTime is pathological".
            // A window may not be held by this effect for longer than the code ceiling plus slack,
            // full stop.
            if (_elapsed > WindowMaterialise.HardCeilingSeconds + WindowMaterialise.WatchdogSlackSeconds)
            {
                Finish($"WATCHDOG at {_elapsed:F2}s", restore: true);
                return;
            }

            _watch.Restart();
            float k = _elapsed / _seconds;
            Apply(k);
            _watch.Stop();
            double ms = _watch.Elapsed.TotalMilliseconds;
            _totalMs += ms;
            _frames++;
            if (ms > _worstMs)
                _worstMs = ms;

            if (k >= 1f)
                Finish("completed", restore: true);
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, $"WINDOW MATERIALISE: LateUpdate on "
                               + $"'{WindowMaterialise.Name(Panel)}' threw "
                               + $"({ex.GetType().Name}: {ex.Message}). The effect is ended, every "
                               + "alpha it wrote is put back, and any pending release runs now.");
            try
            {
                Finish("LateUpdate threw", restore: true);
            }
            catch (Exception inner)
            {
                VRLog.Error(Scope, "WINDOW MATERIALISE: the recovery ALSO threw "
                                   + $"({inner.GetType().Name}: {inner.Message}). Disabling this "
                                   + "component so it cannot repeat.");
                enabled = false;
            }
        }
    }

    /// <summary>
    /// Write one frame at normalised time <paramref name="k"/>: every element's alpha, and the two
    /// uniforms the whole debris cloud rides on.
    ///
    /// <para>The two channels run on DIFFERENT ramps and that is the point — see
    /// <see cref="WindowMaterialiseField.Progresses"/>. The window finishes its half in the first
    /// <c>ElementSpan</c> of the duration; the debris keeps flying for the rest of it. On a vanish
    /// the two fronts are the same function until the element one saturates, so a shard still leaves
    /// in the frame its own patch of window goes dark.</para>
    /// </summary>
    private void Apply(float k)
    {
        // FIRST, AND IN LateUpdate, WHICH IS WHY IT WINS. The game's tween runner writes its two
        // booleans from the Update phase; this is the last writer in the frame, so the value the
        // renderer sees is this one. The same argument the element alphas below rest on.
        _hold?.Assert();

        WindowMaterialiseField.Progresses(k, _materialising,
                                          out float elementProgress, out float debrisFront);

        // THE PANE IS A POINTER SURFACE ONLY WHILE IT IS WHOLE. A vanish is blind from its first
        // frame to its last; an appear is blind exactly while the element front is still moving
        // (elementProgress is 1 at k = 0 and exactly 0 from k = ElementSpan on, see Progresses),
        // so the last shards settle onto a window the laser can already click. Read by
        // RayUguiDriver and PokeInteractor through WindowMaterialise.IsPointerBlind.
        PointerBlind = !_materialising || elementProgress > 0f;

        List<CanvasRenderer>? rs = _renderers;
        List<float>? th = _threshold;
        List<float>? orig = _origAlpha;
        if (rs != null && th != null && orig != null)
        {
            int n = rs.Count;
            for (int i = 0; i < n; i++)
            {
                CanvasRenderer cr = rs[i];
                if (cr == null)
                    continue;
                // TIMES THE ELEMENT'S OWN ALPHA, not instead of it. An element the game had put at
                // 0.3 must not jump to 1.0 for the duration of the effect; and at progress 0 this
                // writes back exactly the number that was there, so the effect's first frame is a
                // no-op and its restore is exact rather than approximately exact.
                cr.SetAlpha(orig[i] * WindowMaterialiseField.PresenceOf(
                    th, i * WindowMaterialiseField.Samples, elementProgress));
            }
        }

        WindowMaterialise.DebrisCloud? d = _debris;
        if (d != null && _mpb != null && d.Front != null && d.Behind != null)
        {
            _mpb.SetFloat(FrontId, debrisFront);
            _mpb.SetFloat(SizeScaleId,
                WindowMaterialiseField.DebrisSizeScale(k, _materialising, WindowMaterialise.Intensity));
            d.Front.SetPropertyBlock(_mpb);
            d.Behind.SetPropertyBlock(_mpb);
        }
    }

    /// <summary>
    /// <b>THE ONLY EXIT, AND IT IS IDEMPOTENT.</b> Puts every alpha back, hands the pending release
    /// on, returns the pooled buffers and destroys the carrier object.
    ///
    /// <para><b>Restoring happens BEFORE the callback, always.</b> The callback is the window's real
    /// teardown, which puts the game's window back into its 2D home — and the alphas this effect
    /// wrote live on the GAME's CanvasRenderers, not on anything the mod owns. Handing back a
    /// window whose four hundred elements are still at alpha 0.03 would make it invisible in the
    /// flat game, forever, with nothing left alive to fix it.</para>
    ///
    /// <para><b>Cancelling the ANIMATION never cancels the RELEASE.</b> A vanish that is interrupted
    /// still runs its callback: the caller has already removed the window from every list it was
    /// in, so a swallowed callback would leave it alive with no owner.</para>
    /// </summary>
    /// <summary>The SPAWN POSE / DRAWN ON THE PREVIOUS FRAME / TRIGGER clause, computed once in
    /// <see cref="Begin"/> for a vanish and appended to the end line. Empty for an appear.</summary>
    private string _spawnClause = string.Empty;

    /// <summary>A per-axis scale ratio that survives a zero: a host scaled to nothing on one axis
    /// has no cloud worth correcting, and a division by zero would give the carrier a NaN scale.</summary>
    private static float SafeRatio(float was, float now)
        => Mathf.Abs(now) > 1e-7f && Mathf.Abs(was) > 1e-7f ? was / now : 1f;

    /// <summary>
    /// <b>Run one more callback when this vanish ends</b> — the double-release fold: a second
    /// <c>PlayOut</c> for a float that is already dissolving does not start a second cloud, it
    /// chains its release behind the running one. Both callbacks still run exactly once, in order,
    /// on every path <see cref="Finish"/> takes.
    /// </summary>
    internal void ChainOnDone(Action more)
    {
        Action? prev = _onDone;
        _onDone = prev == null ? more : () => { prev(); more(); };
    }

    internal void Finish(string reason, bool restore)
    {
        if (_finished)
            return;
        _finished = true;

        if (restore)
            RestoreAll();

        // THE VISIBILITY HOLD GOES BACK BEFORE THE CALLBACK, for exactly the reason the element
        // alphas do: the callback is the window's real teardown, and handing a window back with a
        // CanvasGroup this effect had switched off would make it invisible in the flat game with
        // nothing left alive to fix it. Released unconditionally — a hold is given back on a
        // cancel, a watchdog, a throw and a destroy alike, not only on a clean finish, because the
        // three switches it holds are the game's and not ours.
        _hold?.Release(reason);
        _hold = null;

        Report(reason);
        WindowMaterialise.Unregister(this);

        Action? done = _onDone;
        _onDone = null;

        ReturnBuffers();
        if (_debris != null)
        {
            // The two halves share one vertex buffer but are two native Mesh objects, and neither is
            // owned by the GameObject that references it — destroying the carrier does not free
            // them. Both go back to the pool here, before the destroy.
            WindowMaterialise.ReleaseMesh(_debris.MeshFront);
            WindowMaterialise.ReleaseMesh(_debris.MeshBehind);
            _debris = null;
        }
        _mpb = null;

        if (gameObject != null)
            Destroy(gameObject);

        // LAST, so that whatever the release does to the hierarchy cannot interleave with this
        // object's own teardown.
        done?.Invoke();
    }

    private void RestoreAll()
    {
        List<CanvasRenderer>? rs = _renderers;
        List<float>? orig = _origAlpha;
        if (rs == null || orig == null)
            return;
        int n = Mathf.Min(rs.Count, orig.Count);
        for (int i = 0; i < n; i++)
        {
            CanvasRenderer cr = rs[i];
            if (cr != null)
                cr.SetAlpha(orig[i]);
        }
    }

    private void ReturnBuffers()
    {
        if (_renderers != null)
        {
            _renderers.Clear();
            if (RendererPool.Count < 8)
                RendererPool.Push(_renderers);
            _renderers = null;
        }
        if (_origAlpha != null)
        {
            _origAlpha.Clear();
            if (FloatPool.Count < 16)
                FloatPool.Push(_origAlpha);
            _origAlpha = null;
        }
        if (_threshold != null)
        {
            _threshold.Clear();
            if (FloatPool.Count < 16)
                FloatPool.Push(_threshold);
            _threshold = null;
        }
    }

    /// <summary>
    /// The cost, MEASURED. Worst frame and mean, with the element count and the shard count that
    /// produced them, so a reader can tell "this is what a 700-element window costs" from "this is
    /// what the effect costs" without re-deriving anything. One line per effect; there are a handful
    /// of window opens in a minute, not a handful per frame.
    ///
    /// <para>The BUILD cost is printed separately and on purpose. It is the one-off price of seeding
    /// a few hundred shards out of the window's own elements, paid in the frame the window opens —
    /// a frame that is already doing a full canvas conversion — and it is not part of the per-frame
    /// figure. Reporting one number for both would hide whichever of them mattered.</para>
    /// </summary>
    private void Report(string reason)
    {
        if (_reported)
            return;
        _reported = true;
        int n = _renderers?.Count ?? 0;
        int shards = _debris?.Shards ?? 0;
        double mean = _frames > 0 ? _totalMs / _frames : 0d;
        VRLog.Info(Scope, $"WINDOW MATERIALISE {(_materialising ? "APPEAR" : "VANISH")} on "
                          + $"'{WindowMaterialise.Name(Panel)}' ended ({reason}) after "
                          + $"{_elapsed:F2}s of {_seconds:F2}s over {_frames} frame(s). "
                          + $"{n} CanvasRenderer(s) driven, {shards} shard(s) in the air; per-frame "
                          + $"cost MEASURED at {mean:F3} ms mean, {_worstMs:F3} ms worst. That is the "
                          + $"alpha write for every element ({WindowMaterialiseField.Samples} "
                          + "smoothsteps and one SetAlpha each; the noise behind them is evaluated "
                          + "once per element at the start, never per frame) PLUS two SetFloats and "
                          + "two SetPropertyBlocks for the WHOLE debris cloud — the shard count does "
                          + "not enter the per-frame cost, because every shard's trajectory is a "
                          + "closed function of one uniform evaluated on the GPU. One-off build cost "
                          + $"{_buildMs:F3} ms (element walk + shard seeding), paid in the frame the "
                          + "window opens. "
                          + (_materialising
                              ? "VISIBILITY: not held — an appear leaves the game's own show "
                                + "transition alone, by design."
                              : _held > 0
                                  ? $"VISIBILITY: OWNED for {_held} UIWindow(s), claimed "
                                    + (_heldFromEdge
                                        ? "ON THE CLOSE EDGE (one ModalFallback.Tick before this "
                                          + "runner existed)"
                                        : "at the release tick (no close-edge hold was standing)")
                                    + " — the game's 0.1 s alpha tween, its ChangeActive and its "
                                    + "_disableCanvas switch could not empty the window under this "
                                    + "animation."
                                    + $" ModBuild 405: {_heldGroups} of them had their channels "
                                    + $"driven and {_leftToGame} nested window(s) the game was "
                                    + "holding hidden were LEFT TO THE GAME (their CanvasGroup "
                                    + "stayed on, so their sub-screens did not become drawable "
                                    + "under the dissolve)."
                                  : "VISIBILITY: NOT HELD — no UIWindow was found under the host, so "
                                    + "the game's own 0.1 s hide fade is still the thing that "
                                    + "removes this window and the dissolve is drawn over it.")
                          + " Debris: "
                          + (shards > 0
                              ? "drawn"
                              : "NOT drawn (no shader / degenerate rect / no visible element / "
                                + "intensity 0)")
                          + ". Budget is 11.11 ms."
                          // 2026-09-03, "the dust collides with the laser": what a pointer could
                          // find under the effect (nothing), and how long the PANE was withheld
                          // from the far-ray and poke drivers — the thing the beam really stopped
                          // at. A vanish is blind for its whole duration; an appear for its
                          // element sweep (ElementSpan x duration) only.
                          + $" POINTER: effect root '{_effectRootName}' on layer {_effectLayer} "
                          + $"('{LayerMask.LayerToName(_effectLayer)}'), {_effectColliders} "
                          + $"Collider(s) and renderer(s) {_effectRendererTypes} under it, so no "
                          + "physics ray or bounds walk has anything of the dust to hit; the pane's "
                          + $"canvas plane was POINTER-BLIND for {_blindSeconds:F2}s of the "
                          + $"{_elapsed:F2}s (RayUguiDriver and PokeInteractor skip it while "
                          + "elements are missing — WindowMaterialise.IsPointerBlind), so the beam "
                          + "runs through the dust to whatever stands behind the window."
                          // 2026-09-03, "the dissolve appeared where no window was": the cloud's
                          // spawn pose, its distance from the head, whether the window was drawn
                          // on the frame before the release, and what released it. Empty for an
                          // appear.
                          + _spawnClause);
    }

    /// <summary>The host was deactivated under us. <c>LateUpdate</c> will not run again, so the
    /// effect must end HERE or the window is frozen at whatever alpha it had reached.</summary>
    private void OnDisable() => Finish("the carrier was disabled (host deactivated)", restore: true);

    /// <summary>The host was destroyed under us — the game tore the UI down, the scene changed, or
    /// the release ran from somewhere that did not know about the effect. Restoring alphas on
    /// objects that are being destroyed is harmless; NOT running the pending release would leak a
    /// window.</summary>
    private void OnDestroy() => Finish("the carrier was destroyed", restore: true);
}

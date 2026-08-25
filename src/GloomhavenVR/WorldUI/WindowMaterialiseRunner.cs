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
/// </summary>
internal sealed class WindowMaterialiseRunner : MonoBehaviour
{
    private const string Scope = "WorldUI";

    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private static readonly int WindId = Shader.PropertyToID("_Wind");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int RaggedId = Shader.PropertyToID("_Ragged");
    private static readonly int FrontScaleId = Shader.PropertyToID("_FrontScale");
    private static readonly int AgeSpanId = Shader.PropertyToID("_AgeSpan");
    private static readonly int DriftId = Shader.PropertyToID("_Drift");
    private static readonly int PlumeSpanId = Shader.PropertyToID("_PlumeSpan");
    private static readonly int SpreadId = Shader.PropertyToID("_Spread");
    private static readonly int StreakId = Shader.PropertyToID("_Streak");
    private static readonly int ThinId = Shader.PropertyToID("_Thin");
    private static readonly int TailFadeId = Shader.PropertyToID("_TailFade");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

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

    private Action? _onDone;
    private float _elapsed;
    private bool _finished;
    private bool _reported;

    private List<CanvasRenderer>? _renderers;
    private List<float>? _origAlpha;
    private List<float>? _threshold;

    private MeshRenderer? _quad;
    private Mesh? _mesh;
    private MaterialPropertyBlock? _mpb;

    // Measured, not estimated. Printed once per effect at its end.
    private readonly Stopwatch _watch = new();
    private double _worstMs;
    private double _totalMs;
    private int _frames;

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

        bool haveQuad = WindowMaterialise.TryBuildQuad(panel, out GameObject? go, out MeshRenderer? mr,
                                                       out Mesh? mesh, out float aspect);
        if (!haveQuad)
        {
            // No flakes (no shader yet, or a rect with no inside). The element-by-element dissolve
            // is pure C# and is the half that actually removes the window, so it still runs; the
            // carrier object is still mod-owned and still parented to the host, so every
            // interruption path below is unchanged.
            aspect = r.width / Mathf.Max(r.height, 0.01f);
            go = new GameObject(WindowMaterialise.QuadName) { layer = panel.HostGo.layer };
            go.transform.SetParent(host, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }
        if (go == null)
            return false;
        GameObject carrier = go;

        var runner = carrier.AddComponent<WindowMaterialiseRunner>();
        runner.Panel = panel;
        runner._seconds = Mathf.Max(seconds, WindowMaterialise.MinSeconds);
        runner._materialising = materialising;
        runner._onDone = onDone;
        runner._quad = mr;
        runner._mesh = mesh;

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

        if (mr != null)
        {
            runner._mpb = new MaterialPropertyBlock();
            // Every shape constant is pushed from WindowMaterialiseField so the shader and the CPU
            // field cannot drift apart. The shader's Properties block holds the same numbers, but
            // only as the value an editor preview would show.
            runner._mpb.SetVector(WindId, new Vector4(WindowMaterialiseField.Wind.x,
                                                      WindowMaterialiseField.Wind.y, 0f, 0f));
            runner._mpb.SetFloat(AspectId, aspect);
            runner._mpb.SetFloat(SoftnessId, WindowMaterialiseField.Softness);
            runner._mpb.SetFloat(RaggedId, WindowMaterialiseField.Ragged);
            runner._mpb.SetFloat(FrontScaleId, WindowMaterialiseField.FrontScale);
            runner._mpb.SetFloat(AgeSpanId, WindowMaterialiseField.AgeSpan);
            runner._mpb.SetFloat(DriftId, WindowMaterialiseField.Drift);
            runner._mpb.SetFloat(PlumeSpanId, WindowMaterialiseField.PlumeSpan);
            runner._mpb.SetFloat(SpreadId, WindowMaterialiseField.Spread);
            runner._mpb.SetFloat(StreakId, WindowMaterialiseField.Streak);
            runner._mpb.SetFloat(ThinId, WindowMaterialiseField.Thin);
            // THE ONE ASYMMETRY BETWEEN THE TWO DIRECTIONS. A vanish must end at nothing, so its
            // plume is faded out over the last fifth of progress. An appear STARTS at progress 1,
            // where that same fade would leave the first frames completely empty — a window that is
            // live and clickable while showing the player nothing. See the shader's _TailFade.
            runner._mpb.SetFloat(TailFadeId, materialising ? 0f : 1f);
            runner._mpb.SetFloat(IntensityId, WindowMaterialise.Intensity);
        }

        WindowMaterialise.Register(runner);

        // WRITE FRAME ZERO NOW, NOT NEXT LateUpdate. PlayIn is called from inside the reveal's own
        // LateUpdate, and rendering happens after LateUpdate — so if the first write waited for the
        // NEXT frame's LateUpdate, the eye would see one frame of a fully opaque window before the
        // materialise started. That single frame is the "aufploppen" the user asked to be rid of.
        runner.Apply(runner.ProgressAt(0f));
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
            _origAlpha.Add(cr.GetAlpha());

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

    /// <summary>Dissolve progress at a normalised time: 0 = the window is all there, 1 = it is all
    /// gone. An appear is a vanish played backwards, which is exactly what the user described
    /// ("Auftauchen eventuell andersrum").</summary>
    private float ProgressAt(float k)
    {
        // LINEAR, and the first preview strip is why. An eased k compounds with the plume's own
        // pow(p, 1.5) travel and pushes all the visible action into the middle of the duration:
        // the 1.00 s vanish was over by 0.63 s and the 0.50 s appear showed literally nothing for
        // its first 0.13 s. Both ends of a linear ramp are still exact, and the effect now fills
        // the time the user asked it to take instead of finishing early and then waiting.
        float e = Mathf.Clamp01(k);
        return _materialising ? 1f - e : e;
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
            Apply(ProgressAt(k));
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

    /// <summary>Write one frame: every element's alpha, and the shader's one animated uniform.</summary>
    private void Apply(float progress)
    {
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
                    th, i * WindowMaterialiseField.Samples, progress));
            }
        }

        if (_quad != null && _mpb != null)
        {
            _mpb.SetFloat(ProgressId, progress);
            _quad.SetPropertyBlock(_mpb);
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
    internal void Finish(string reason, bool restore)
    {
        if (_finished)
            return;
        _finished = true;

        if (restore)
            RestoreAll();

        Report(reason);
        WindowMaterialise.Unregister(this);

        Action? done = _onDone;
        _onDone = null;

        ReturnBuffers();
        WindowMaterialise.ReleaseMesh(_mesh);
        _mesh = null;
        _quad = null;
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
    /// The cost, MEASURED. Worst frame and mean, with the element count that produced them and the
    /// dial state, so a reader can tell "this is what a 700-element window costs" from "this is what
    /// the effect costs" without re-deriving anything. One line per effect; there are a handful of
    /// window opens in a minute, not a handful per frame.
    /// </summary>
    private void Report(string reason)
    {
        if (_reported)
            return;
        _reported = true;
        int n = _renderers?.Count ?? 0;
        double mean = _frames > 0 ? _totalMs / _frames : 0d;
        VRLog.Info(Scope, $"WINDOW MATERIALISE {(_materialising ? "APPEAR" : "VANISH")} on "
                          + $"'{WindowMaterialise.Name(Panel)}' ended ({reason}) after "
                          + $"{_elapsed:F2}s of {_seconds:F2}s over {_frames} frame(s). "
                          + $"{n} CanvasRenderer(s) driven; per-frame cost MEASURED at "
                          + $"{mean:F3} ms mean, {_worstMs:F3} ms worst — that is the alpha write "
                          + "for every element plus one MaterialPropertyBlock set, and it is the "
                          + $"whole CPU cost of this effect ({WindowMaterialiseField.Samples} "
                          + "smoothsteps and one SetAlpha per element; the noise behind them is "
                          + "evaluated once per element at the start, never per frame). "
                          + $"Flakes: {(_quad != null ? "drawn" : "NOT drawn (no shader / degenerate rect)")}. "
                          + $"Budget is 11.11 ms.");
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

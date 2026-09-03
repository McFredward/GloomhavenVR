using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// =================================================================================================
// THE HIDDEN-WINDOW VEIL (ModBuild 401) — the game's verdict, enforced on the renderer.
//
// THE DEFECT, in the user's words across four rounds: on the campaign map, clicking a personal
// quest and the first press of "Reisen" each flash something for EXACTLY ONE FRAME — his own
// frame-step of the video shows a "Character löschen" dialog that must not appear at all, and a
// label reading "New Text". Rulings: prevent it if possible, hide it if that is better — what
// matters is that the player never sees it — and never an empty window.
//
// WHAT FOUR ROUNDS ESTABLISHED, each by its own instrument:
//
//   * ModBuild 392 (pre-Start veil): 24 pre-Start entries in 591,324 examined, 0 accepted,
//     PREVENTED 0. Whatever flashes has already run Start().
//   * ModBuild 395 (reveal restore withheld): "0 canvas(es) of 0" three times. The mechanism it
//     was built for does not occur.
//   * ModBuild 397/399 (COLUMN OVERSPILL): the flash finally has NAMES. On frame 4776 the
//     character column measured 1964x1080 px against its 328x1080 pin — six times its width, 96
//     graphics — and the three extremes of that union all read the same verdict:
//
//         under UIWindow 'Hero Level Up Panel' (ID HeroLevelUpPanel):
//           OPEN=False, STARTED=True, VISIBLE=False, activeSelf=True, canvas=none
//         under UIWindow 'Party Display UI ' (ID None):
//           OPEN=False, STARTED=True, VISIBLE=False, activeSelf=True, canvas=none
//
// READ AGAINST THE GAME'S OWN CODE (decompiled UIWindow.cs): a hidden UIWindow is CanvasGroup
// alpha 0 on an object that STAYS ACTIVE unless m_DisableOnZeroAlpha is set (ChangeActive,
// :742-746), and IsVisible is exactly `m_CanvasGroup.alpha > 0` (:309). So OPEN=False,
// VISIBLE=False, activeSelf=True is the flat game's NORMAL resting state for a nested sibling
// screen — and canvas=none means there is no per-window Canvas to switch off, which is why both
// earlier rules, each of which reached for the _disableCanvas lever, were inert by construction.
//
// Those graphics were nevertheless COUNTED by the column measurement, and the measurement's
// admission test is `g.color.a * g.canvasRenderer.GetInheritedAlpha() >= 0.05`. So on that frame
// the RENDERER'S inherited alpha said "drawable" while the WINDOW'S CanvasGroup said "alpha 0".
// That disagreement is the flash: uGUI propagates CanvasGroup alpha into CanvasRenderers lazily,
// on the canvas update that follows a change, and a subtree whose canvas the conversion has just
// re-enabled (the reveal, CanvasConversion.6.Hide.cs) or whose parent the game has just activated
// renders ONCE with whatever inherited alpha its renderers last held before the propagation
// catches up. One frame. Exactly what he filmed.
//
// THE REMEDY, and why it is safe to ship under the "never an empty window" ruling:
//
//   Every frame, in LateUpdate (after every Update, before anything renders), every NESTED
//   UIWindow under a converted panel that the game reports NOT OPEN and NOT VISIBLE has every
//   Graphic beneath it culled at the CanvasRenderer (cull = true, alpha 0) — the same lever a
//   RectMask2D pulls for clipped content, applied to content the game says is hidden. The veil
//   lifts the moment the game's own alpha rises above zero or the window opens, i.e. on the FIRST
//   frame of its show tween, so nothing the game wants shown is ever held back and no animation
//   loses a frame. It cannot produce an empty window because the panel's own window and every
//   ancestor of the conversion target are never candidates, and a child the game deliberately
//   draws through a hidden parent (a CanvasGroup with ignoreParentGroups) is skipped by the same
//   chain walk uGUI itself performs.
//
//   It is a HIDE, not a prevention, and it is chosen deliberately: the state that flashes is a
//   correct state (the game keeps closed screens active at alpha 0 on purpose), so there is
//   nothing to prevent — only a one-frame propagation gap to bridge, and bridging it at the
//   renderer is what the engine's own masks do.
//
// WHAT IT MEASURES, so the next log settles it instead of the next round: at each veil it counts
// how many graphics under the window had a renderer alpha that would have drawn against the
// chain's verdict (the DISAGREEMENT — the flash population itself), prints whether the panel was
// revealed on that very frame, and summarises per panel on release. A summary of ZERO events is
// printed in words: no hidden nested window was ever active under this panel.
// =================================================================================================
internal static partial class CanvasConversion
{
    /// <summary>One nested window under veil, with everything needed to lift it exactly.</summary>
    private sealed class VeiledWindow
    {
        public UIWindow Window = null!;
        public readonly List<CanvasRenderer> Renderers = new(32);
        public readonly List<float> Alphas = new(32);
        public int VeiledAtFrame;
    }

    private sealed class HiddenWindowVeilState
    {
        public readonly List<VeiledWindow> Veiled = new(4);
        public int VeilEvents;
        public int WindowsSeen;
        public int GraphicsVeiled;
        /// <summary>Graphics whose CanvasRenderer inherited alpha said DRAWABLE while the
        /// CanvasGroup chain said hidden — the population that paints for one frame.</summary>
        public int Disagreements;
        public float WorstDisagreement;
        public int VeilsOnRevealFrame;
        public int LinesLeft = HiddenWindowVeilLinesPerPanel;
    }

    /// <summary>Named veil lines per panel per life. Three names a defect; more is a flood.</summary>
    private const int HiddenWindowVeilLinesPerPanel = 3;

    private static readonly Dictionary<ConvertedPanel, HiddenWindowVeilState> HiddenWindowVeils = new(8);
    private static readonly List<UIWindow> VeilWindowScratch = new(16);
    private static readonly List<Graphic> VeilGraphicScratch = new(128);

    /// <summary>Per-frame, per-panel, from <see cref="LateTick"/> BEFORE the reveal flip, so the
    /// frame a panel becomes visible in is already veiled where the game says it should be.</summary>
    private static void TickHiddenWindowVeil(ConvertedPanel panel)
    {
        Transform? target = panel.Target;
        if (target == null)
            return;
        if (!HiddenWindowVeils.TryGetValue(panel, out HiddenWindowVeilState? st))
        {
            st = new HiddenWindowVeilState();
            HiddenWindowVeils[panel] = st;
        }

        // 1. LIFT — the game shows it, or it is gone. IsVisible is `alpha > 0`, so the lift lands
        //    on the first frame of a show tween and no animation loses a frame.
        for (int i = st.Veiled.Count - 1; i >= 0; i--)
        {
            VeiledWindow v = st.Veiled[i];
            if (v.Window == null || v.Window.IsOpen || v.Window.IsVisible
                || !v.Window.gameObject.activeInHierarchy)
            {
                Unveil(v);
                st.Veiled.RemoveAt(i);
            }
        }

        // 2. VEIL — every nested window the game reports hidden, on an active object.
        VeilWindowScratch.Clear();
        target.GetComponentsInChildren(includeInactive: false, VeilWindowScratch);
        for (int i = 0; i < VeilWindowScratch.Count; i++)
        {
            UIWindow w = VeilWindowScratch[i];
            if (w == null)
                continue;
            Transform wt = w.transform;
            // The panel's own window and anything above the conversion target are never
            // candidates: veiling them is the one way to make an empty window.
            if (ReferenceEquals(wt, target) || target.IsChildOf(wt))
                continue;
            if (w.IsOpen || w.IsVisible)
                continue;
            // A window with no CanvasGroup reports IsVisible=false FOREVER (it reads a null
            // group) — that is a window driven by something else, not a hidden one. Skip.
            if (w.GetComponent<CanvasGroup>() == null)
                continue;
            if (IsVeiled(st, w))
                continue;
            Veil(panel, st, w);
        }
        VeilWindowScratch.Clear();
    }

    private static bool IsVeiled(HiddenWindowVeilState st, UIWindow w)
    {
        for (int i = 0; i < st.Veiled.Count; i++)
        {
            if (ReferenceEquals(st.Veiled[i].Window, w))
                return true;
        }
        return false;
    }

    private static void Veil(ConvertedPanel panel, HiddenWindowVeilState st, UIWindow w)
    {
        var v = new VeiledWindow { Window = w, VeiledAtFrame = Time.frameCount };
        int disagreements = 0, skippedOwnGroup = 0;
        float worst = 0f;
        VeilGraphicScratch.Clear();
        w.GetComponentsInChildren(includeInactive: false, VeilGraphicScratch);
        for (int i = 0; i < VeilGraphicScratch.Count; i++)
        {
            Graphic g = VeilGraphicScratch[i];
            if (g == null)
                continue;
            CanvasRenderer? cr = g.canvasRenderer;
            if (cr == null)
                continue;
            // The chain walk uGUI itself performs: a group below the window that ignores its
            // parents is drawn by the game even while the window is hidden, and stays drawn here.
            if (GroupChainAlphaBelow(g.transform, w.transform) >= FitMinAlpha)
            {
                skippedOwnGroup++;
                continue;
            }
            float inherited = cr.GetInheritedAlpha();
            if (g.color.a * inherited >= FitMinAlpha)
            {
                disagreements++;
                if (inherited > worst)
                    worst = inherited;
            }
            v.Renderers.Add(cr);
            v.Alphas.Add(cr.GetAlpha());
            cr.SetAlpha(0f);
            cr.cull = true;
        }
        VeilGraphicScratch.Clear();
        st.Veiled.Add(v);
        st.VeilEvents++;
        st.WindowsSeen++;
        st.GraphicsVeiled += v.Renderers.Count;
        st.Disagreements += disagreements;
        if (worst > st.WorstDisagreement)
            st.WorstDisagreement = worst;
        bool onRevealFrame = panel.LastRevealFrame == Time.frameCount;
        if (onRevealFrame)
            st.VeilsOnRevealFrame++;

        if (st.LinesLeft <= 0)
            return;
        st.LinesLeft--;
        // HW-VERIFY: the line that names a hidden window the game left active under a converted
        // panel and says whether its renderers would have drawn. "N of them DISAGREE" > 0 is the
        // flash population caught before the frame rendered; 0 means the veil was a no-op there.
        VRLog.Note("WorldUI",
            $"HIDDEN-WINDOW VEIL on '{(panel.HostGo != null ? panel.HostGo.name : "?")}' at frame "
            + $"{Time.frameCount} (#{HiddenWindowVeilLinesPerPanel - st.LinesLeft} of at most "
            + $"{HiddenWindowVeilLinesPerPanel} named): '{w.gameObject.name}' (ID {w.ID}) reports "
            + $"OPEN={w.IsOpen}, VISIBLE={w.IsVisible}, STARTED={w.HasGoneToStartingState}, "
            + $"activeSelf={w.gameObject.activeSelf} — {v.Renderers.Count} graphic(s) under it "
            + $"culled at the CanvasRenderer, {disagreements} of them DISAGREE (their inherited "
            + $"alpha said drawable, worst {worst:0.00}, while the CanvasGroup chain says < "
            + $"{FitMinAlpha:0.00}); {skippedOwnGroup} skipped because a group of their own ignores "
            + $"the hidden parent and the game draws them anyway. Panel revealed this same frame: "
            + $"{(onRevealFrame ? "YES" : "no")} (last reveal frame {panel.LastRevealFrame}). "
            + "HOW TO READ IT: a non-zero DISAGREE count is the one-frame flash — renderers that "
            + "would have painted against the game's own verdict before uGUI propagated the "
            + "window's alpha 0 into them; the veil lifts on the first frame the game's alpha "
            + "rises above zero or the window opens, so nothing wanted is withheld. This is a HIDE "
            + "and not a prevention, by the user's 2026-09-03 ruling that either is acceptable.");
    }

    private static void Unveil(VeiledWindow v)
    {
        for (int i = 0; i < v.Renderers.Count; i++)
        {
            CanvasRenderer cr = v.Renderers[i];
            if (cr == null)
                continue;
            cr.cull = false;
            cr.SetAlpha(i < v.Alphas.Count ? v.Alphas[i] : 1f);
        }
        v.Renderers.Clear();
        v.Alphas.Clear();
    }

    /// <summary>Product of every <c>CanvasGroup.alpha</c> from <paramref name="from"/> up to but
    /// EXCLUDING <paramref name="window"/>, honouring <c>ignoreParentGroups</c> as uGUI does. A
    /// value at or above <see cref="FitMinAlpha"/> means a group below the window is drawn on its
    /// own authority; the window's own alpha 0 is deliberately not in the product.</summary>
    private static float GroupChainAlphaBelow(Transform from, Transform window)
    {
        float alpha = 1f;
        bool ignoresParents = false;
        for (Transform? t = from; t != null && !ReferenceEquals(t, window); t = t.parent)
        {
            var group = t.GetComponent<CanvasGroup>();
            if (group == null)
                continue;
            alpha *= group.alpha;
            if (group.ignoreParentGroups)
            {
                ignoresParents = true;
                break;
            }
        }
        // Without an ignoreParentGroups group in between, the window's own alpha 0 governs and the
        // graphic is hidden by the game's verdict — report 0 so it is veiled.
        return ignoresParents ? alpha : 0f;
    }

    /// <summary>The clause the FIXED FIT line carries so the veil's state is readable beside the
    /// column it protects, including the zero.</summary>
    private static string DescribeHiddenWindowVeil(ConvertedPanel panel)
    {
        if (!HiddenWindowVeils.TryGetValue(panel, out HiddenWindowVeilState? st))
            return "HIDDEN-WINDOW VEIL: not yet ticked for this panel";
        return $"HIDDEN-WINDOW VEIL: {st.Veiled.Count} nested window(s) veiled right now, "
               + $"{st.VeilEvents} veil event(s) over {st.GraphicsVeiled} graphic(s) since this panel "
               + $"opened, {st.Disagreements} disagreement(s) (worst inherited alpha "
               + $"{st.WorstDisagreement:0.00}), {st.VeilsOnRevealFrame} on a reveal frame";
    }

    /// <summary>Panel release: lift every veil and print the per-panel summary, zero included.</summary>
    private static void ReleaseHiddenWindowVeil(ConvertedPanel panel)
    {
        if (!HiddenWindowVeils.TryGetValue(panel, out HiddenWindowVeilState? st))
            return;
        HiddenWindowVeils.Remove(panel);
        for (int i = 0; i < st.Veiled.Count; i++)
            Unveil(st.Veiled[i]);
        st.Veiled.Clear();
        // HW-VERIFY: the readable zero. A panel that never had a hidden nested window active under
        // it prints "0 veil event(s)" in words — a different reading from this line being absent.
        VRLog.Note("WorldUI",
            $"HIDDEN-WINDOW VEIL summary for '{(panel.HostGo != null ? panel.HostGo.name : "?")}': "
            + $"{st.VeilEvents} veil event(s) over {st.WindowsSeen} window(s) and {st.GraphicsVeiled} "
            + $"graphic(s); {st.Disagreements} graphic(s) would have DRAWN against the game's verdict "
            + $"(worst inherited alpha {st.WorstDisagreement:0.00}); {st.VeilsOnRevealFrame} veil(s) "
            + "landed on the frame this panel was revealed. ZERO events means no hidden nested "
            + "window was ever active under this panel while it was up, so the veil had nothing to "
            + "do here — which is what a panel without sibling screens looks like, and is not the "
            + "same as the veil not running.");
    }
}

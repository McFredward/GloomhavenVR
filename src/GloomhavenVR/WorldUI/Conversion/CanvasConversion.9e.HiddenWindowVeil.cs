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
//
// =================================================================================================
// ModBuild 405 — WHAT THE 404 LOG SAID, AND THE TWO MECHANISMS IT NAMES.
//
// The 404 round returned "Ich sehe kein Aufblitzen mehr" and a regression: the whole CHARACTER
// COLUMN of 'New Party display' drawn as a black rectangle under the battle-goal picker. The log
// has both halves of it, and neither is a hidden window the game draws:
//
//   1. THE BLACK COLUMN IS THE VEIL'S OWN RESTORE VALUE. The veil saved `cr.GetAlpha()` at veil
//      time and wrote it back on lift. `CanvasRenderer.SetAlpha` is a channel the mod's OWN
//      WindowMaterialise runner also writes: it captures `orig = cr.GetAlpha()` when a dissolve
//      starts, writes `orig * presence(t)` to all 2071 renderers every LateUpdate for 0.9 s, and
//      restores `orig` in Finish — BEFORE the release callback that runs the veil's own release
//      (WindowMaterialiseRunner.Finish: RestoreAll, then hold.Release, then done()). In the 404
//      log the story curtain closed 'New Party display' (line 1990) and the VANISH started (1992);
//      DURING that dissolve the game hid 'Party Display UI ' — the column's own window — and the
//      veil took it (2 late events, 356 graphics, the FIXED FIT at 2047 counting 2 column graphics
//      because the fit rejects culled renderers). Its saved alpha was a MID-DISSOLVE value. The
//      release then restored that value over the runner's own restore, the window went back to its
//      2D home at a fraction of its alpha, the next float re-veiled it with THAT fraction saved
//      (frame 2386, 178 graphics) and lifted it to the same fraction when the game opened it. The
//      fit measured 84-86 graphics at 2534/2583 — it reads INHERITED alpha, not the renderer's
//      own, so it could not see the column it was measuring was dim to black.
//
//      Remedy: the alpha channel is no longer captured once and restored blindly. Every veiled
//      renderer is re-asserted each tick; a NON-ZERO value found on it between two asserts is a
//      foreign write (the runner, or a game CrossFadeAlpha) and becomes the value that lift will
//      restore; the lift itself reads the channel first and, if someone wrote it since our last
//      assert, leaves THEIR value standing. And the runner now captures `orig` THROUGH the veil
//      (PreVeilAlpha), so a dissolve that starts over a veiled renderer neither captures our zero
//      nor restores it as the window's own. Two mod writers on one channel now have an order.
//
//   2. THE 181 DISAGREE GRAPHICS AT INHERITED ALPHA 1.00 ARE THE MATERIALISE HOLD, NOT THE GAME.
//      WindowVisibilityHold.Assert switched `CanvasGroup.enabled = false` on EVERY UIWindow under
//      the host for the duration of a dissolve — including nested windows the game holds hidden
//      at alpha 0. A disabled CanvasGroup contributes nothing to inherited alpha, so every hidden
//      sub-screen (the level-up panel, the equipment panel, the delete-character dialog) became
//      DRAWABLE at inherited alpha 1.00 while its UIWindow still reported OPEN=False,
//      VISIBLE=False. Both late veils in the 404 log landed inside the vanish, and both read
//      exactly that. The hold now leaves a nested window's group alone unless that window was OPEN
//      or VISIBLE when the hold was taken (WindowMaterialiseVisibility.cs, Held), so a window the
//      game keeps hidden stays hidden under the dissolve — which is also the most economical
//      account of a one-frame "Character löschen": a close-edge hold taken and given back within
//      a frame or two makes every hidden dialog under the window drawable for exactly that long.
//
// WHAT THE VEIL DOES NOW, AND WHY IT STILL CATCHES THE FLASH:
//
//   Every frame, in LateUpdate (after every Update, before anything renders), every NESTED
//   UIWindow under a converted panel that the game reports NOT OPEN and NOT VISIBLE has every
//   Graphic beneath it culled at the CanvasRenderer (cull = true, alpha 0) — the same lever a
//   RectMask2D pulls for clipped content, applied to content the game says is hidden. The veil
//   lifts the moment the game's own alpha rises above zero or the window opens, i.e. on the FIRST
//   frame of its show tween. It cannot produce an empty window because the panel's own window and
//   every ancestor of the conversion target are never candidates, and a child the game
//   deliberately draws through a hidden parent (a CanvasGroup with ignoreParentGroups) is skipped.
//
//   NEW: THE SETTLED RE-READ. A stale inherited alpha lasts ONE serviced frame — uGUI recomputes
//   it on the canvas update that follows. So once the panel has RENDERED at least twice since a
//   window was veiled, the engine's own `GetInheritedAlpha()` is the truth about what the game
//   draws, and a veiled graphic it still reports drawable is one the game is drawing on purpose.
//   The veil LIFTS FOR THAT GRAPHIC — restores it, un-culls it, and says so, with the window's own
//   CanvasGroup.enabled beside it (False there means a hold, not the game). The user's ruling is
//   that every element must render correctly AND the flash must stay prevented; the first frame
//   is still caught, and nothing the engine keeps drawing stays hidden past the second.
//
//   NEW: CULL IS RE-ASSERTED. `Graphic.Rebuild` skips a culled renderer and leaves its dirty
//   flags set, so a lift calls `Graphic.OnCullingChanged()` — the engine's own re-registration
//   after a cull — rather than handing back stale geometry ("New Text" is exactly what a label
//   rebuilt-while-culled looks like).
//
// WHAT IT MEASURES: at each veil it counts how many graphics under the window had a renderer
// alpha that would have drawn against the chain's verdict (the DISAGREEMENT), prints whether the
// panel was revealed on that very frame, names EVERY window that disagrees (the 3-per-panel cap no
// longer hides them), counts the lifts the settled re-read made, and summarises per panel on
// release with the worst three disagreeing windows and one path each.
// =================================================================================================
internal static partial class CanvasConversion
{
    /// <summary>One nested window under veil, with everything needed to lift it exactly.</summary>
    private sealed class VeiledWindow
    {
        public UIWindow Window = null!;
        /// <summary>The window's own CanvasGroup — the thing whose alpha 0 this veil enforces.
        /// While it is DISABLED (a materialise hold) it governs nothing, and the settled re-read
        /// must not read the engine's answer as the game's.</summary>
        public CanvasGroup? Group;
        public readonly List<CanvasRenderer> Renderers = new(32);
        public readonly List<Graphic> Graphics = new(32);
        public int VeiledAtFrame;
        /// <summary>Ticks since the veil on which the panel was actually rendering. The settled
        /// re-read needs two: one for uGUI to service the canvas, one to read the result.</summary>
        public int RenderTicks;
        /// <summary>Graphics the settled re-read handed back to the game from this window.</summary>
        public int LiftedForGame;
    }

    /// <summary>The alpha a veiled renderer goes back to, and how many veiled windows hold it (a
    /// window nested in a hidden window is veiled twice; the renderer stays culled until the last
    /// hold is gone).</summary>
    private struct VeilHold
    {
        public float Alpha;
        public int Holds;
    }

    private sealed class DisagreeingWindow
    {
        public string Name = "";
        public string Id = "";
        public int Count;
        public string Path = "";
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
        /// <summary>Lines named PAST the cap because they carried DISAGREE graphics.</summary>
        public int DisagreeLinesLeft = HiddenWindowVeilDisagreeLinesPerPanel;
        public int LiftLinesLeft = HiddenWindowVeilLiftLinesPerPanel;
        /// <summary>Per-window DISAGREE tallies for the summary's worst-three clause.</summary>
        public readonly List<DisagreeingWindow> Disagreeing = new(4);
        /// <summary>Settled re-read lifts: how many graphics, over how many re-read passes.</summary>
        public int LiftedForGame;
        public int LiftPasses;
        /// <summary>Foreign writes to the alpha channel observed on veiled renderers (the
        /// materialise runner, a game CrossFadeAlpha) — the population the old restore lost.</summary>
        public int ForeignAlphaWrites;
    }

    /// <summary>Named veil lines per panel per life. Three names a defect; more is a flood.</summary>
    private const int HiddenWindowVeilLinesPerPanel = 3;

    /// <summary>Extra named lines per panel for windows that DISAGREE — the 404 log had 181
    /// disagreeing graphics in windows the cap did not name, so the cap could not say which.</summary>
    private const int HiddenWindowVeilDisagreeLinesPerPanel = 6;

    /// <summary>Settled-re-read lift lines per panel per life.</summary>
    private const int HiddenWindowVeilLiftLinesPerPanel = 3;

    /// <summary>Rendering ticks a veiled window must have seen before the engine's inherited alpha
    /// is trusted over the veil: one to service the canvas, one to read it.</summary>
    private const int HiddenWindowVeilSettleTicks = 2;

    /// <summary>After the first settled re-read, how often (in rendering ticks) it repeats. A hold
    /// can switch a group off at any later moment; eight frames bounds the cost to a 1/8 share of
    /// one GetInheritedAlpha per veiled renderer per frame.</summary>
    private const int HiddenWindowVeilRereadEvery = 8;

    private static readonly Dictionary<ConvertedPanel, HiddenWindowVeilState> HiddenWindowVeils = new(8);
    private static readonly Dictionary<CanvasRenderer, VeilHold> VeilHolds = new(1024);
    private static readonly List<UIWindow> VeilWindowScratch = new(16);
    private static readonly List<Graphic> VeilGraphicScratch = new(128);

    /// <summary>
    /// THE ALPHA A RENDERER HAD BEFORE THE VEIL TOOK IT, or <paramref name="own"/> when it is not
    /// veiled. For the one other mod writer of <c>CanvasRenderer.SetAlpha</c> — the materialise
    /// runner captures its <c>orig</c> through this, so a dissolve that starts over a veiled
    /// renderer neither captures the veil's zero nor restores it as the window's own value.
    /// </summary>
    internal static float PreVeilAlpha(CanvasRenderer cr, float own)
    {
        if (cr != null && VeilHolds.TryGetValue(cr, out VeilHold h))
            return h.Alpha;
        return own;
    }

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

        bool rendering = !panel.RenderHidden;

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
                continue;
            }
            // 1b. RE-ASSERT, and learn every foreign write. The runner writes this channel every
            //     LateUpdate of a dissolve; the value it left is the one a lift must hand back.
            ReassertVeil(st, v);
            // 1c. THE SETTLED RE-READ — the engine's verdict once it has had a serviced frame.
            //     Not while the window's own group is DISABLED: then the engine's inherited alpha
            //     reflects a materialise hold, not the game, and a hidden sub-screen must stay
            //     hidden under the dissolve (the "Character löschen" dialog of the 397 video).
            if (rendering)
            {
                v.RenderTicks++;
                int since = v.RenderTicks - HiddenWindowVeilSettleTicks;
                bool governs = v.Group == null || v.Group.enabled;
                if (governs && since >= 0 && since % HiddenWindowVeilRereadEvery == 0)
                    LiftWhatTheGameDraws(panel, st, v);
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
            CanvasGroup? group = w.GetComponent<CanvasGroup>();
            if (group == null)
                continue;
            // ModBuild 405, THE STRUCTURAL TEST: the verdict this veil enforces is the window's
            // CanvasGroup at alpha 0. A group that is DISABLED governs nothing — its alpha is out
            // of the inherited multiply (that is what WindowVisibilityHold does on purpose for a
            // window the dissolve is drawing), so IsVisible=false is a number nobody is applying,
            // and enforcing it here would cull content the effect is deliberately showing. Left
            // to whoever switched the group off; it becomes a candidate again when the group is.
            if (!group.enabled)
                continue;
            if (IsVeiled(st, w))
                continue;
            Veil(panel, st, w, group);
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

    /// <summary>Re-write the two levers on every renderer of one veiled window. A non-zero alpha
    /// found here was written by someone else since our last assert: it becomes the restore value.
    /// Cost: one GetAlpha and one cull read per renderer, writes only on change.</summary>
    private static void ReassertVeil(HiddenWindowVeilState st, VeiledWindow v)
    {
        for (int i = 0; i < v.Renderers.Count; i++)
        {
            CanvasRenderer cr = v.Renderers[i];
            if (cr == null)
                continue;
            float a = cr.GetAlpha();
            if (a != 0f)
            {
                if (VeilHolds.TryGetValue(cr, out VeilHold h))
                {
                    h.Alpha = a;
                    VeilHolds[cr] = h;
                }
                st.ForeignAlphaWrites++;
                cr.SetAlpha(0f);
            }
            if (!cr.cull)
                cr.cull = true;
        }
    }

    private static void Veil(ConvertedPanel panel, HiddenWindowVeilState st, UIWindow w, CanvasGroup group)
    {
        var v = new VeiledWindow { Window = w, Group = group, VeiledAtFrame = Time.frameCount };
        int disagreements = 0, skippedOwnGroup = 0;
        float worst = 0f;
        string disagreePath = "";
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
                {
                    worst = inherited;
                    disagreePath = HierarchyPathBelow(g.transform, panel.Target);
                }
            }
            v.Renderers.Add(cr);
            v.Graphics.Add(g);
            if (VeilHolds.TryGetValue(cr, out VeilHold h))
            {
                // Already veiled under an enclosing hidden window: its channel is ours already
                // (reads 0), so the pre-veil value on record is the one to keep.
                h.Holds++;
                VeilHolds[cr] = h;
            }
            else
            {
                // ModBuild 434 — THROUGH THE SEAT VEIL, for exactly the reason this whole file
                // exists for the materialise runner. A renderer the sub-view SEAT veil (part 9g)
                // is holding reads 0 here, and capturing that zero as its pre-veil value would
                // restore it as zero on this veil's own lift — the black-column failure with the
                // two writers swapped. Falls back to the channel itself when nothing holds it.
                VeilHolds[cr] = new VeilHold
                {
                    Alpha = PreSeatVeilAlpha(cr, cr.GetAlpha()),
                    Holds = 1,
                };
            }
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
        if (disagreements > 0)
            RecordDisagreement(st, w, disagreements, disagreePath);
        bool onRevealFrame = panel.LastRevealFrame == Time.frameCount;
        if (onRevealFrame)
            st.VeilsOnRevealFrame++;

        // The cap never hides a window that DISAGREES: those are the ones the 404 round could not
        // name. A disagreeing window past the cap takes one of its own six lines instead.
        bool pastCap = st.LinesLeft <= 0;
        if (pastCap)
        {
            if (disagreements == 0 || st.DisagreeLinesLeft <= 0)
                return;
            st.DisagreeLinesLeft--;
        }
        else
        {
            st.LinesLeft--;
        }
        bool animating = WindowMaterialise.IsAnimating(panel);
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
            + "and not a prevention, by the user's 2026-09-03 ruling that either is acceptable. "
            + $"ModBuild 405: the window's own CanvasGroup reads enabled={(group != null ? group.enabled.ToString() : "none")} "
            + $"alpha={(group != null ? group.alpha : -1f):0.00}; a materialise is "
            + $"{(animating ? "RUNNING" : "not running")} on this panel"
            + (disagreements > 0
                ? $"; worst DISAGREE graphic: '{disagreePath}'. A DISAGREE at inherited 1.00 while "
                  + "enabled=False or a materialise is RUNNING is the mod's own visibility hold "
                  + "(WindowVisibilityHold), not the game drawing a hidden window."
                : ".")
            + (pastCap ? " NAMED PAST THE CAP because it DISAGREES: the cap must never hide the "
                         + "window that carries the flash population." : ""));
    }

    private static void RecordDisagreement(HiddenWindowVeilState st, UIWindow w, int count, string path)
    {
        string name = w.gameObject.name;
        for (int i = 0; i < st.Disagreeing.Count; i++)
        {
            DisagreeingWindow d = st.Disagreeing[i];
            if (d.Name == name)
            {
                d.Count += count;
                if (string.IsNullOrEmpty(d.Path))
                    d.Path = path;
                return;
            }
        }
        st.Disagreeing.Add(new DisagreeingWindow
        {
            Name = name, Id = w.ID.ToString(), Count = count, Path = path,
        });
    }

    /// <summary>
    /// THE SETTLED RE-READ. The panel has rendered at least twice since this window was veiled, so
    /// uGUI has serviced its canvas and <c>GetInheritedAlpha()</c> is no longer the stale value the
    /// veil exists for — it is what the engine multiplies in NOW. A veiled graphic the engine still
    /// reports drawable is one the game draws on purpose (or one a mod-side hold made drawable —
    /// the line says which, by printing the window's own <c>CanvasGroup.enabled</c>). Either way
    /// the user's ruling is that every element renders correctly, so it is handed back.
    /// </summary>
    private static void LiftWhatTheGameDraws(ConvertedPanel panel, HiddenWindowVeilState st, VeiledWindow v)
    {
        st.LiftPasses++;
        int lifted = 0;
        float worst = 0f;
        string firstPath = "";
        for (int i = v.Renderers.Count - 1; i >= 0; i--)
        {
            CanvasRenderer cr = v.Renderers[i];
            Graphic? g = i < v.Graphics.Count ? v.Graphics[i] : null;
            if (cr == null || g == null)
                continue;
            if (!g.isActiveAndEnabled)
                continue;
            // A disabled canvas is not serviced, so its renderers' inherited alpha is exactly the
            // stale reading this pass must not trust.
            Canvas? canvas = g.canvas;
            if (canvas == null || !canvas.enabled || !canvas.isActiveAndEnabled)
                continue;
            float inherited = cr.GetInheritedAlpha();
            if (g.color.a * inherited < FitMinAlpha)
                continue;
            if (lifted == 0)
                firstPath = HierarchyPathBelow(g.transform, panel.Target);
            if (inherited > worst)
                worst = inherited;
            ReleaseRenderer(cr, g);
            v.Renderers.RemoveAt(i);
            if (i < v.Graphics.Count)
                v.Graphics.RemoveAt(i);
            lifted++;
        }
        if (lifted == 0)
            return;
        v.LiftedForGame += lifted;
        st.LiftedForGame += lifted;
        if (st.LiftLinesLeft <= 0)
            return;
        st.LiftLinesLeft--;
        UIWindow w = v.Window;
        CanvasGroup? group = w != null ? w.GetComponent<CanvasGroup>() : null;
        // HW-VERIFY: the line that says the veil GAVE BACK graphics the engine kept drawing under
        // a window the game reports hidden. enabled=False on the window's own CanvasGroup names
        // the mod's materialise hold as the reason; enabled=True with a materialise not running is
        // the game genuinely drawing through a hidden window, and the veil must not cover it.
        VRLog.Note("WorldUI",
            $"HIDDEN-WINDOW VEIL LIFTED FOR THE GAME on '{(panel.HostGo != null ? panel.HostGo.name : "?")}' "
            + $"at frame {Time.frameCount}: '{(w != null ? w.gameObject.name : "?")}' (ID "
            + $"{(w != null ? w.ID.ToString() : "?")}) still reports OPEN={(w != null && w.IsOpen)}, "
            + $"VISIBLE={(w != null && w.IsVisible)}, yet {lifted} of its veiled graphic(s) read "
            + $"inherited alpha >= {FitMinAlpha:0.00} (worst {worst:0.00}) on a SETTLED frame — "
            + $"{v.RenderTicks} rendering tick(s) after the veil at frame {v.VeiledAtFrame}, so uGUI "
            + "has serviced the canvas and this is what it multiplies in, not a stale value. Those "
            + $"{lifted} are handed back (alpha restored, cull off, geometry re-registered); "
            + $"{v.Renderers.Count} stay veiled. First lifted: '{firstPath}'. The window's own "
            + $"CanvasGroup reads enabled={(group != null ? group.enabled.ToString() : "none")} "
            + $"alpha={(group != null ? group.alpha : -1f):0.00}; a materialise is "
            + $"{(WindowMaterialise.IsAnimating(panel) ? "RUNNING" : "not running")} on this panel. "
            + "HOW TO READ IT: enabled=False is WindowVisibilityHold switching the group off — the "
            + "mod made this window drawable, and ModBuild 405 stops it doing that to a window "
            + "the game holds hidden; enabled=True with no materialise running means the game "
            + "draws this content through a hidden window and the veil must leave it alone. A "
            + "session with NO such line means the engine agreed with the game on every settled "
            + "frame, which is the expected reading.");
    }

    private static void Unveil(VeiledWindow v)
    {
        for (int i = 0; i < v.Renderers.Count; i++)
        {
            CanvasRenderer cr = v.Renderers[i];
            if (cr == null)
            {
                // Destroyed (scene change) but still a managed key: drop it or the table grows
                // by one dead renderer per veiled graphic per scene load.
                if (!ReferenceEquals(cr, null))
                    VeilHolds.Remove(cr);
                continue;
            }
            ReleaseRenderer(cr, i < v.Graphics.Count ? v.Graphics[i] : null);
        }
        v.Renderers.Clear();
        v.Graphics.Clear();
    }

    /// <summary>
    /// Hand one renderer back. The alpha it gets is, in order: whatever a foreign writer put on
    /// the channel since our last assert (a non-zero reading — the runner's restore lands one call
    /// before this on the release path), else the pre-veil value on record. A renderer still held
    /// by an enclosing hidden window stays culled. <c>OnCullingChanged</c> is the engine's own
    /// re-registration for the rebuilds <c>Graphic.Rebuild</c> skipped while culled.
    /// </summary>
    private static void ReleaseRenderer(CanvasRenderer cr, Graphic? g)
    {
        if (!VeilHolds.TryGetValue(cr, out VeilHold h))
        {
            cr.cull = false;
            return;
        }
        h.Holds--;
        if (h.Holds > 0)
        {
            VeilHolds[cr] = h;
            return;
        }
        VeilHolds.Remove(cr);
        float foreign = cr.GetAlpha();
        cr.cull = false;
        cr.SetAlpha(foreign != 0f ? foreign : h.Alpha);
        if (g != null)
            g.OnCullingChanged();
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

    /// <summary>The graphic's path from the conversion target down, at most eight levels, for a
    /// line that has to name WHICH graphic rather than how many.</summary>
    private static string HierarchyPathBelow(Transform t, Transform? stop)
    {
        if (t == null)
            return "?";
        string path = t.name;
        Transform? node = t.parent;
        for (int depth = 0; node != null && depth < 8; depth++, node = node.parent)
        {
            if (stop != null && ReferenceEquals(node, stop))
                break;
            path = node.name + "/" + path;
        }
        return path;
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
               + $"{st.WorstDisagreement:0.00}), {st.VeilsOnRevealFrame} on a reveal frame, "
               + $"{st.LiftedForGame} graphic(s) LIFTED for the game by the settled re-read, "
               + $"{st.ForeignAlphaWrites} foreign alpha write(s) learned";
    }

    /// <summary>The summary's worst-three clause: which windows carried the DISAGREE graphics.</summary>
    private static string DescribeDisagreeingWindows(HiddenWindowVeilState st)
    {
        if (st.Disagreeing.Count == 0)
            return "DISAGREE BY WINDOW: none — no veiled window had a drawable renderer";
        st.Disagreeing.Sort((a, b) => b.Count.CompareTo(a.Count));
        var sb = new System.Text.StringBuilder(256);
        sb.Append("DISAGREE BY WINDOW (worst three of ").Append(st.Disagreeing.Count).Append("): ");
        int n = Mathf.Min(3, st.Disagreeing.Count);
        for (int i = 0; i < n; i++)
        {
            DisagreeingWindow d = st.Disagreeing[i];
            if (i > 0)
                sb.Append(", ");
            sb.Append('\'').Append(d.Name).Append("' (ID ").Append(d.Id).Append(") ")
              .Append(d.Count).Append(" graphic(s)");
            if (i == 0 && !string.IsNullOrEmpty(d.Path))
                sb.Append(" e.g. '").Append(d.Path).Append('\'');
        }
        return sb.ToString();
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
            + "same as the veil not running. "
            + $"ModBuild 405: {DescribeDisagreeingWindows(st)}; {st.LiftedForGame} graphic(s) LIFTED "
            + $"for the game over {st.LiftPasses} settled re-read pass(es) because uGUI kept "
            + "reporting them drawable under a window the game calls hidden (zero means the engine "
            + $"agreed with the game on every settled frame); {st.ForeignAlphaWrites} foreign write(s) "
            + "to a veiled renderer's alpha were learned and handed back instead of being overwritten "
            + "(the 404 regression: a mid-dissolve alpha restored over the column, and the column "
            + "drawn black).");
    }
}

using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE DISSOLVE OWNS THE WINDOW'S VISIBILITY WHILE IT PLAYS — BECAUSE THE GAME EMPTIES THE
/// WINDOW IN 0.1 s AND OUR ANIMATION TAKES 0.9.</b>
///
/// <para>User, 2026-09-02, on ModBuild 334: <i>"Das Einfaden funktioniert schon gut, aber ausfaden
/// also der animation wenn ein Fenster verschwindet ploppt das Fenster erst weg, danach kommt die
/// Animation. Ich will eher den Eindruck das Fenster würde sich 'auflösen' statt vorher sichtbar
/// wegzuploppen."</i> — "the fade-in works well, but on the fade-out the window pops away FIRST and
/// the animation comes afterwards. I want the impression that the window DISSOLVES rather than
/// visibly popping away first."</para>
///
/// <para><b>THE MECHANISM, VERIFIED IN THE DECOMPILE (GH.Runtime/UnityEngine.UI/UIWindow.cs).</b>
/// A close runs <c>Hide()</c> → <c>EvaluateAndTransitionToVisualState(Hidden, instant)</c> (:524,
/// :539). That method sets <c>m_CurrentVisualState = state</c> immediately — so
/// <c>IsOpen => m_CurrentVisualState == VisualState.Shown</c> (:317) is already false — and then
/// empties the window through <b>three separate channels</b>, every one of which is enough on its
/// own to make it invisible:</para>
/// <list type="number">
/// <item><b>The alpha tween.</b> <c>StartAlphaTween(0, m_TransitionDuration)</c> with
///   <c>m_TransitionDuration = 0.1f</c> (:100, :552). Its per-step callback is
///   <c>SetCanvasAlpha</c> (:611, :618), which writes <c>m_CanvasGroup.alpha</c>. A
///   <c>CanvasGroup</c> at alpha 0 makes every <c>CanvasRenderer</c> below it draw nothing whatever
///   this effect writes with <c>SetAlpha</c> — inherited alpha is not the group. <b>0.1 s out of a
///   0.9 s vanish is the "wegploppen".</b></item>
/// <item><b>The GameObject.</b> <c>SetCanvasAlpha</c> calls <c>ChangeActive()</c> (:623, :742),
///   which is <c>gameObject.SetActive(m_CanvasGroup.alpha &gt; Mathf.Epsilon)</c> for a window with
///   <c>m_DisableOnZeroAlpha</c> (:113) set. The last tween step therefore DEACTIVATES the window
///   outright.</item>
/// <item><b>The Canvas.</b> <c>OnTransitionCompleted()</c> (:590) does
///   <c>if (_disableCanvas &amp;&amp; m_CurrentVisualState == Hidden) _canvas.enabled = false</c>,
///   and an <i>instant</i> hide does it in <c>OnTransitionStarted</c> (:584) before anything else
///   runs at all. This is the "empty shell" <c>ReassertStickyVisible</c> already fights for the
///   sticky case (ModalFallback.7.Close.cs:277-284).</item>
/// </list>
///
/// <para><b>WHO OWNS WHAT, PER FRAME, WHILE A VANISH PLAYS.</b> The rule this project has already
/// paid for is <i>"don't win a write war — concede the flag, own the number"</i>, and it is applied
/// literally, once per channel:</para>
/// <list type="bullet">
/// <item><b>The alpha: CONCEDED, and taken out of the multiply instead.</b> This class does NOT
///   write <c>m_CanvasGroup.alpha</c>. It sets <c>CanvasGroup.enabled = false</c>, so the group
///   stops contributing to inherited alpha and the game's tween goes on writing a number that
///   nothing reads. THE GAME REMAINS THE ONLY WRITER OF THAT NUMBER, which is what makes the
///   restore exact and terminal-correct: re-enabling the component at the end applies whatever the
///   game last decided — 0 for a window that closed, 1 for one that was re-opened underneath us —
///   with no captured value to guess from and no epsilon anywhere. During the hold the ONLY writer
///   of <c>enabled</c> is this class, so there is no war to lose.</item>
/// <item><b>The GameObject and the Canvas: OWNED IN LateUpdate, which is the last writer.</b> Both
///   are booleans the game flips at most a handful of times during a 0.1 s tween, from Update-phase
///   code (the tween runner) — so re-asserting them in <c>LateUpdate</c> wins the frame by
///   construction, exactly as <c>WindowMaterialiseRunner</c>'s own alphas do. Neither is written
///   unless it is found wrong, and each one that had to be forced is REMEMBERED, because the flag
///   is also how the end state is restored.</item>
/// </list>
///
/// <para><b>AND THE RESTORE PUTS THE GAME'S OWN RULE BACK, NOT A SNAPSHOT.</b> This is the one
/// place a captured value would be actively wrong: at capture the window was open and the game
/// wanted it drawn, and by the end of the dissolve the game wants it gone. So a channel that had to
/// be forced is released by re-deriving the game's own predicate from the game's own live numbers —
/// <c>SetActive(alpha &gt; Mathf.Epsilon)</c> is literally <c>ChangeActive</c>, and
/// <c>canvas.enabled = window.IsOpen</c> is literally <c>OnTransitionCompleted</c> /
/// <c>OnTransitionStarted</c>. A window that was re-opened mid-dissolve therefore comes back ON,
/// and one that stayed closed comes back OFF, from the same two lines. A channel that never had to
/// be forced is not touched at all.</para>
///
/// <para><b>NOTHING HERE IS GAME STATE.</b> Three presentation switches, held for at most the
/// duration of one dissolve and released to the game's own current intent. No <c>Show</c>, no
/// <c>Hide</c>, no <c>Escape</c>, no visual-state write, nothing on the wire — the game's idea of
/// what is open is untouched, and <c>IsOpen</c> is false throughout because the game made it false
/// before this class ever heard about the close. The same three switches, in the same direction,
/// are already written by <c>ReassertStickyVisible</c> on a floated window; the difference is that
/// this hold is bounded by an animation and puts them back.</para>
///
/// <para><b>INPUT.</b> Disabling a <c>CanvasGroup</c> also stops its <c>blocksRaycasts</c> and
/// <c>interactable</c> applying. That is harmless HERE and only here: <c>PlayOut</c>'s first act is
/// to disable the host's <c>GraphicRaycaster</c>, which is the window's only hit surface and the
/// laser's only way in, so nothing under the group can be reached by anything for the whole life of
/// the hold. A player cannot click a ghost.</para>
/// </summary>
internal sealed class WindowVisibilityHold
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// One <c>UIWindow</c>'s three visibility channels. The <c>CanvasGroup</c> and the
    /// <c>Canvas</c> are looked up BY IDENTITY on the window's own GameObject, which is not a
    /// convenience — it is the same lookup the game itself does
    /// (<c>m_CanvasGroup = base.gameObject.GetComponent&lt;CanvasGroup&gt;()</c> at UIWindow.cs:354,
    /// <c>_canvas = GetComponent&lt;Canvas&gt;()</c> at :350), so this is exactly the set of
    /// components that window's own transition can drive. Containment is not identity, and a
    /// <c>GetComponentInParent</c> here would have picked up a neighbour's group.
    /// </summary>
    private struct Rec
    {
        internal UIWindow Window;
        internal CanvasGroup? Group;
        internal Canvas? Canvas;
        internal GameObject Go;
        internal bool GroupWasEnabled;
        internal bool WasActive;
        internal bool ForcedCanvas;
        internal bool ForcedActive;
        /// <summary>ModBuild 405: whether this hold may touch the window at all. TRUE for the
        /// panel's own window and for every nested window that was OPEN or VISIBLE when the hold
        /// was taken; FALSE for a nested window the game was holding hidden at alpha 0. Switching
        /// a hidden window's CanvasGroup off made its whole sub-screen DRAWABLE at inherited alpha
        /// 1.00 for the length of the dissolve (the 404 log: 181 graphics under windows reporting
        /// OPEN=False VISIBLE=False, every one of them inside a vanish) — the level-up panel, the
        /// equipment panel and the delete-character dialog all became visible under the effect.
        /// A window the game keeps hidden draws nothing today and must draw nothing under the
        /// dissolve, so its three channels are left exactly where the game put them.</summary>
        internal bool Held;
    }

    private readonly List<Rec> _recs = new(2);
    private bool _released;

    /// <summary>Windows whose channels this hold actually drives (see <see cref="Rec.Held"/>).</summary>
    internal int HeldCount { get; private set; }

    /// <summary>Nested windows the game was holding hidden when the hold was taken, left alone.</summary>
    internal int LeftToGameCount { get; private set; }

    /// <summary>Reused between captures. A window subtree holds one <c>UIWindow</c> in the ordinary
    /// case and a handful in the nested ones, and a vanish happens on every window close.</summary>
    private static readonly List<UIWindow> Scratch = new(8);

    /// <summary>Change-gated: the two "the game emptied the window under us" channels are the ones
    /// a hardware log needs to name, and they are per-WINDOW facts (a serialized
    /// <c>m_DisableOnZeroAlpha</c> / <c>_disableCanvas</c>), not per-effect ones. Logged the first
    /// time each is actually observed, so a silent log means neither happened rather than that the
    /// line was never reached.</summary>
    private static bool _loggedForcedActive;

    private static bool _loggedForcedCanvas;

    internal int Count => _recs.Count;

    /// <summary>
    /// Snapshot the <c>UIWindow</c>s under <paramref name="host"/> and take the hold immediately —
    /// in the same statement, because the caller's next act is to draw a frame.
    ///
    /// <para><c>includeInactive: true</c> is load-bearing rather than defensive: on an INSTANT hide
    /// the window is already deactivated by the time anything of ours runs (UIWindow.cs:584 fires
    /// inside <c>OnTransitionStarted</c>, before the visual state is even assigned), and an
    /// inactive-only search would find nothing to hold and the dissolve would play on an empty
    /// window — the exact defect this class exists for.</para>
    ///
    /// <para><b><paramref name="primary"/> IS WHICH WINDOW THIS PANEL IS FOR, AND IT IS THE ONE
    /// WINDOW THIS CLASS MAY SWITCH BACK ON.</b> Pass the panel's <c>Target</c> — the rect the
    /// float was converted through. Every OTHER <c>UIWindow</c> in the subtree is held only if it
    /// was ACTIVE when the hold was taken, because a window the game is deliberately keeping off
    /// (an unselected tab, a submenu of a floated menu) is not this effect's to reveal: it draws
    /// nothing today and it must draw nothing during the dissolve. The panel's own window is the
    /// exception because it is the one whose deactivation we are here to undo, and on an instant
    /// hide that deactivation has ALREADY happened by the time this runs — so reading
    /// <c>activeSelf</c> for it would read the defect and call it the intent.</para>
    /// </summary>
    internal void Capture(RectTransform host, Transform? primary)
    {
        if (host == null)
            return;
        Scratch.Clear();
        host.GetComponentsInChildren(true, Scratch);
        for (int i = 0; i < Scratch.Count; i++)
        {
            UIWindow w = Scratch[i];
            if (w == null)
                continue;
            GameObject go = w.gameObject;
            Transform wt = w.transform;
            // Ancestry, not reference: a window is NOT required to be converted through its own
            // transform (ModalFallback.8.Convert.cs:345), so "the panel's own window" is "the one
            // the converted rect sits at or inside".
            bool isPrimary = primary != null
                             && (ReferenceEquals(primary, wt) || primary.IsChildOf(wt));
            var rec = new Rec
            {
                Window = w,
                Group = w.GetComponent<CanvasGroup>(),
                Canvas = w.GetComponent<Canvas>(),
                Go = go,
                GroupWasEnabled = false,
                WasActive = go.activeSelf || isPrimary,
                // ModBuild 405: a nested window the game holds hidden (not open, alpha 0) is not
                // this effect's to make drawable — see Rec.Held. IsVisible is `alpha > 0`, so a
                // nested window mid-way through its own hide tween is still held and dissolves
                // along with the panel, exactly as before.
                Held = isPrimary || w.IsOpen || w.IsVisible,
            };
            rec.GroupWasEnabled = rec.Group != null && rec.Group.enabled;
            if (rec.Held)
                HeldCount++;
            else
                LeftToGameCount++;
            _recs.Add(rec);
        }
        Scratch.Clear();
        Assert();
    }

    /// <summary>
    /// Hold the three channels for one frame. Called from the runner's <c>LateUpdate</c> — the last
    /// writer in the frame, and the same phase every other number this effect owns is written in.
    /// </summary>
    internal void Assert()
    {
        if (_released)
            return;
        for (int i = 0; i < _recs.Count; i++)
        {
            Rec r = _recs[i];
            if (r.Window == null)
                continue;
            // ModBuild 405: a window the game holds hidden keeps all three channels as the game
            // left them (Rec.Held).
            if (!r.Held)
                continue;

            // (1) THE ALPHA, CONCEDED. Take the group out of the inherited-alpha multiply instead
            //     of fighting the tween for the number inside it. The game keeps writing that
            //     number; nothing reads it while this is off; re-enabling at the end applies
            //     whatever the game last wrote, which is the correct end state by construction.
            if (r.Group != null && r.Group.enabled)
                r.Group.enabled = false;

            // (2) THE CANVAS. UIWindow.cs:584/:592 — a `_disableCanvas` window switches its own
            //     Canvas off, and the whole subtree stops rendering. This is the "empty shell".
            if (r.Canvas != null && !r.Canvas.enabled)
            {
                r.Canvas.enabled = true;
                if (!r.ForcedCanvas)
                {
                    r.ForcedCanvas = true;
                    if (!_loggedForcedCanvas)
                    {
                        _loggedForcedCanvas = true;
                        VRLog.Info(Scope, $"WINDOW MATERIALISE: '{r.Window.name}' is a "
                                          + "`_disableCanvas` UIWindow — it switched its own Canvas "
                                          + "OFF on the hide transition (UIWindow.cs:584/592), which "
                                          + "would have stopped the whole subtree rendering in the "
                                          + "first frames of the dissolve. Held on for the duration "
                                          + "and put back to the window's own IsOpen at the end. "
                                          + "This line prints once per process.");
                    }
                }
            }

            // (3) THE GAMEOBJECT. UIWindow.cs:742 ChangeActive — a `m_DisableOnZeroAlpha` window
            //     deactivates itself on the tween step that reaches alpha 0. Only re-activated if
            //     it was active when the hold was taken: a window that was already off is not this
            //     effect's to switch on.
            if (r.Go != null && r.WasActive && !r.Go.activeSelf)
            {
                r.Go.SetActive(true);
                if (!r.ForcedActive)
                {
                    r.ForcedActive = true;
                    if (!_loggedForcedActive)
                    {
                        _loggedForcedActive = true;
                        VRLog.Info(Scope, $"WINDOW MATERIALISE: '{r.Window.name}' is a "
                                          + "`m_DisableOnZeroAlpha` UIWindow — its own alpha tween "
                                          + "DEACTIVATED it (UIWindow.cs:742 ChangeActive) while the "
                                          + "dissolve was still playing. Held active for the "
                                          + "duration and put back through ChangeActive's own rule "
                                          + "at the end. This line prints once per process.");
                    }
                }
            }

            _recs[i] = r;
        }
    }

    /// <summary>
    /// <b>Give every channel back, and give it back to the GAME'S CURRENT INTENT rather than to the
    /// snapshot.</b> Idempotent, and safe on destroyed objects.
    ///
    /// <para>The <c>CanvasGroup</c> is the easy one and deliberately so: its <c>enabled</c> is
    /// restored to what it was, and because this class never wrote the alpha, the alpha standing
    /// underneath it is the game's own — 0 for a window that closed, 1 for one re-opened
    /// mid-dissolve. No branch, no epsilon, no guess.</para>
    ///
    /// <para>The other two are restored by RE-DERIVING the game's own predicates from the game's own
    /// live numbers, which is the only correct thing to do: they were forced precisely because the
    /// game had decided against them, and by now that decision is the final one.</para>
    /// </summary>
    internal void Release(string reason)
    {
        if (_released)
            return;
        _released = true;
        int restoredCanvas = 0, restoredActive = 0;
        for (int i = 0; i < _recs.Count; i++)
        {
            Rec r = _recs[i];
            if (!r.Held)
                continue;
            try
            {
                if (r.Group != null)
                    r.Group.enabled = r.GroupWasEnabled;

                bool open = r.Window != null && r.Window.IsOpen;

                // OnTransitionCompleted / OnTransitionStarted, re-derived: a `_disableCanvas`
                // window's Canvas is on exactly while the window is Shown.
                if (r.ForcedCanvas && r.Canvas != null)
                {
                    r.Canvas.enabled = open;
                    restoredCanvas++;
                }

                // ChangeActive, re-derived, from the game's own alpha — which this class never
                // wrote, so it is the game's number and not ours.
                if (r.ForcedActive && r.Go != null)
                {
                    float alpha = r.Group != null ? r.Group.alpha : (open ? 1f : 0f);
                    r.Go.SetActive(alpha > Mathf.Epsilon);
                    restoredActive++;
                }
            }
            catch (Exception ex)
            {
                // A release that throws halfway is the one way this could leave a window standing
                // in a state nobody owns, so every record is released independently.
                VRLog.Warn(Scope, "WINDOW MATERIALISE: releasing the visibility hold on a window "
                                  + $"threw ({ex.GetType().Name}: {ex.Message}). The remaining "
                                  + "windows of this hold are still released; the window's own "
                                  + "teardown owns whatever is left.");
            }
        }
        if (restoredCanvas > 0 || restoredActive > 0)
            VRLog.Info(Scope, $"WINDOW MATERIALISE: visibility hold released ({reason}) — "
                              + $"{restoredCanvas} Canvas(es) and {restoredActive} GameObject(s) "
                              + "handed back to the game's OWN rule (canvas.enabled = IsOpen, "
                              + "SetActive(alpha > Epsilon)), evaluated on the game's own live "
                              + "numbers rather than on a snapshot taken before the close.");
        _recs.Clear();
    }
}

/// <summary>
/// <b>THE HOLD, TAKEN ON THE CLOSE EDGE — ONE TICK BEFORE THE DISSOLVE ITSELF CAN START.</b>
///
/// <para>The dissolve is started from <c>ModalFallback.4.Tick.cs</c>'s RELEASE phase, i.e. from the
/// tick on which the mod NOTICES that a window has left the open set. That is one
/// <c>ModalFallback.Tick</c> behind the close, no more — measured against the phase order, the
/// window leaves <c>OpenWindows</c> in <c>PhasePolls</c> and is released in <c>PhaseRelease</c> of
/// the very next tick — but "one frame" is not "no frames", and on an INSTANT hide one frame is the
/// whole difference between a dissolve and a pop: <c>SetCanvasAlpha(0)</c> and
/// <c>_canvas.enabled = false</c> both run inside the game's own call, with no tween in front of
/// them.</para>
///
/// <para>So the ownership starts at the true edge. <c>UIWindow.EvaluateAndTransitionToVisualState</c>
/// is the single choke point every visibility change funnels through — it is already patched, and
/// already published as <c>VREvents.WindowVisibility</c> — and on the falling edge of a window that
/// is currently FLOATED this carrier takes the same <see cref="WindowVisibilityHold"/> the runner
/// will use, and holds it until the runner adopts it.</para>
///
/// <para><b>AND IT DETACHES INPUT IN THE SAME STATEMENT, because the hold would otherwise HAND
/// INPUT BACK for a frame.</b> This is the one thing the hold does that is not purely additive, and
/// it has to be answered rather than noticed later. The game's own <c>StartAlphaTween</c> sets
/// <c>m_CanvasGroup.blocksRaycasts = false</c> (UIWindow.cs:605) as its first act, which is what
/// makes a closing window stop taking clicks TODAY — and switching that <c>CanvasGroup</c> off
/// stops <c>blocksRaycasts</c> applying at all, i.e. it would make the window clickable again for
/// the tick between the close edge and <c>PlayOut</c>. So this carrier disables the host's
/// <c>GraphicRaycaster</c> exactly as <see cref="WindowMaterialise.PlayOut"/> does, one tick
/// earlier: that raycaster is the window's only uGUI hit surface, the laser's only way in, and the
/// poke's only source of hits (<c>PokeInteractor</c>: "hits come from the canvas's own (enabled)
/// GraphicRaycaster only"). A player cannot click a ghost at any point on this path. If the hold
/// lapses unclaimed the raycaster is put back exactly as it was found.</para>
///
/// <para><b>IT CANNOT OUTLIVE ITS PURPOSE, AND THAT IS THE WHOLE DESIGN.</b> A hold nobody adopts
/// is a window frozen visible and briefly unclickable, and the second half of that is the standing
/// ruling's own failure ("es MUSS immer möglich sein das Optionsmenü zu öffnen"). So:</para>
/// <list type="bullet">
/// <item>It is a component on a mod-owned child of the panel's host, so "the window died under us"
///   is <c>OnDestroy</c> and "the host was deactivated" is <c>OnDisable</c>. Both release.</item>
/// <item>It expires by itself after <see cref="MaxGraceFrames"/> frames OR
///   <see cref="GraceSeconds"/>, WHICHEVER COMES FIRST. Two bounds and not one, because either
///   alone has a hole: a frame count alone is unbounded in time across a hitch, and a time alone
///   is unbounded in frames — one 200 ms hitch frame would expire the hold before the release tick
///   it is waiting for ever ran. The release tick is exactly ONE <c>ModalFallback.Tick</c> behind
///   the close edge, so four frames is three frames of slack and 0.15 s is the wall-clock cap on
///   the same thing.</item>
/// <item>When it expires it releases everything and the window goes exactly where the game was
///   sending it — i.e. to the behaviour that shipped in ModBuild 334, at most four frames late.
///   A window that STAYS floated (a <c>Sticky</c> menu the game's single-window toggle hid, a live
///   level-message chain) therefore loses at most those four frames of clickability, and gets them
///   back without anything else having happened to it.</item>
/// </list>
///
/// <para><b>WHY IT DOES NOT START THE ANIMATION ITSELF.</b> A closed window is not necessarily a
/// dying float: <c>ModalFallback</c> keeps <c>Sticky</c> menus and live level-message chains floated
/// through a game-side <c>Hide</c> (<c>ReassertStickyVisible</c> exists precisely to force those
/// back on), and starting a vanish on their close edge would dissolve a window that is staying. The
/// decision "this float is over" belongs to the release phase and stays there. What moves to the
/// edge is only the part that MUST be there: not losing the pixels.</para>
/// </summary>
internal sealed class WindowMaterialisePreRoll : MonoBehaviour
{
    private const string Scope = "WorldUI";

    /// <summary>Wall-clock cap on an unclaimed hold. Paired with
    /// <see cref="MaxGraceFrames"/>; see the class doc for why one bound is not enough.</summary>
    internal const float GraceSeconds = 0.15f;

    /// <summary>Frame cap on an unclaimed hold. The release tick is ONE
    /// <c>ModalFallback.Tick</c> behind the close edge, so this is three frames of slack.</summary>
    internal const int MaxGraceFrames = 4;

    internal ConvertedPanel Panel = null!;

    private WindowVisibilityHold? _hold;
    private float _elapsed;
    private int _frames;
    private bool _done;

    /// <summary>The host raycaster this carrier switched off, and whether it really was on when it
    /// did. Null when there was nothing to detach.</summary>
    private GraphicRaycaster? _raycaster;

    private bool _raycasterWasEnabled;

    internal static WindowMaterialisePreRoll? Begin(ConvertedPanel panel)
    {
        RectTransform host = panel.HostRect;
        if (host == null || panel.HostGo == null)
            return null;

        var go = new GameObject(WindowMaterialise.DebrisName + ".PreRoll")
        {
            layer = panel.HostGo.layer,
        };
        Transform t = go.transform;
        t.SetParent(host, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        var pre = go.AddComponent<WindowMaterialisePreRoll>();
        pre.Panel = panel;
        var hold = new WindowVisibilityHold();
        hold.Capture(host, panel.Target);
        if (hold.Count == 0)
        {
            // No UIWindow under this host — nothing this class can hold, and holding nothing is a
            // carrier with a lifetime and no purpose.
            hold.Release("nothing to hold");
            Destroy(go);
            return null;
        }
        pre._hold = hold;

        // INPUT OFF, ONE TICK EARLY. See the class doc: switching the CanvasGroup off stops the
        // game's own `blocksRaycasts = false` applying, so without this the window would take
        // clicks again for the tick between the close edge and PlayOut. PlayOut does exactly this
        // as its own first act, so on the ordinary path this is that same write, earlier.
        try
        {
            GraphicRaycaster gr = panel.HostRaycaster;
            if (gr != null)
            {
                pre._raycaster = gr;
                pre._raycasterWasEnabled = gr.enabled;
                gr.enabled = false;
            }
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope, "WINDOW MATERIALISE: could not detach input for the close-edge hold "
                              + $"({ex.GetType().Name}). The hold is dropped rather than kept, "
                              + "because a held window that can still be clicked is the one thing "
                              + "this path may not produce.");
            hold.Release("input could not be detached");
            Destroy(go);
            return null;
        }
        return pre;
    }

    /// <summary>
    /// Hand the live hold to the runner and disappear WITHOUT releasing it — the runner takes over
    /// ownership in the same statement, so the window is never unheld for a single frame.
    /// </summary>
    internal WindowVisibilityHold? Adopt()
    {
        WindowVisibilityHold? h = _hold;
        _hold = null;
        _done = true;
        WindowMaterialise.UnregisterPreRoll(this);
        if (gameObject != null)
            Destroy(gameObject);
        return h;
    }

    private void LateUpdate()
    {
        // An unguarded per-frame body has taken this project's whole WorldUI driver down before, and
        // that is the one thing a decoration may never do.
        try
        {
            if (_done)
                return;
            if (Panel == null || !Panel.IsAlive || Panel.HostGo == null)
            {
                End("the panel died under the hold");
                return;
            }
            _elapsed += Time.unscaledDeltaTime;
            _frames++;
            if (_frames >= MaxGraceFrames || _elapsed > GraceSeconds)
            {
                End($"no dissolve claimed it within {_frames} frame(s) / {_elapsed:F3}s "
                    + $"(caps: {MaxGraceFrames} frames, {GraceSeconds:F2}s)");
                return;
            }
            _hold?.Assert();
        }
        catch (Exception ex)
        {
            VRLog.Error(Scope, $"WINDOW MATERIALISE: the close-edge hold threw "
                               + $"({ex.GetType().Name}: {ex.Message}). It is released now, which "
                               + "puts the window exactly where the game was sending it.");
            try
            {
                End("the hold threw");
            }
            catch (Exception inner)
            {
                VRLog.Error(Scope, "WINDOW MATERIALISE: releasing the close-edge hold ALSO threw "
                                   + $"({inner.GetType().Name}: {inner.Message}). Disabling this "
                                   + "component so it cannot repeat.");
                enabled = false;
            }
        }
    }

    /// <summary>The only exit, and it is idempotent.</summary>
    internal void End(string reason)
    {
        if (_done)
            return;
        _done = true;
        _hold?.Release(reason);
        _hold = null;
        // INPUT BACK, because this window was never taken over by a dissolve: nothing here has
        // decided that it is dying, and an unclaimed hold must leave no trace at all. The ADOPTED
        // path deliberately does NOT come through here — PlayOut owns the raycaster from there on
        // and the window is being released.
        try
        {
            if (_raycaster != null && _raycasterWasEnabled && !_raycaster.enabled)
                _raycaster.enabled = true;
        }
        catch (Exception ex)
        {
            VRLog.Warn(Scope, "WINDOW MATERIALISE: re-attaching input after an unclaimed "
                              + $"close-edge hold threw ({ex.GetType().Name}: {ex.Message}). The "
                              + "host is normally being torn down when that happens, which drops "
                              + "the raycaster anyway.");
        }
        _raycaster = null;
        WindowMaterialise.UnregisterPreRoll(this);
        if (gameObject != null)
            Destroy(gameObject);
    }

    private void OnDisable() => End("the carrier was disabled (host deactivated)");

    private void OnDestroy() => End("the carrier was destroyed");
}

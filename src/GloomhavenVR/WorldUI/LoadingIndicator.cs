using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// VR loading indicator (user request: "loading screens read as a total VR freeze").
///
/// WHY: the game has NO synchronous scene loads — <c>SceneController.LoadSceneCoroutine</c>
/// polls <c>LoadSceneAsync</c>/<c>UnloadSceneAsync</c> per frame and the user's Player.log
/// proves dozens of frames render during a scenario load. VR still LOOKS frozen because
/// nothing visible changes: the flat loading UI lives on Screen-Space-CAMERA canvases whose
/// UICamera is destroyed mid-transition (CanvasManager rebinds on sceneLoaded), so the
/// loading screen reaches neither the HMD eye textures nor the FlatScreen capture RT — the
/// HMD shows only the menu rig's black void + hands, motionless.
///
/// This component makes the wait READ as live, in the game's own visual language:
/// <list type="bullet">
/// <item>While <c>SceneController.Instance.IsLoading || ScenarioIsLoading</c> (public
///   properties, plain per-tick poll — zero Harmony patches), two stacked mod-layer quads
///   float ~1.5 m ahead of the CURRENT rig head camera showing the flat loading screen's
///   REAL spinner art: the <c>LoadingScreen.m_LoadingIconBase</c>/<c>m_LoadingIconOverlay</c>
///   sprites read once via AccessTools, animated exactly like the game's
///   <c>AnimateIcon()</c> — a discrete <c>m_IconSpinSpeed</c>-degree Z step every
///   <c>m_IconUpdateSpeed</c> seconds (the stepped look IS the visual identity) with the
///   overlay alpha-pulsed by <c>m_IconGlowSpeed</c> between <c>m_IconOverlayMinAlpha</c>
///   and 1. Reflection/sprite failure falls back to a procedural ring + arc-gradient
///   overlay — never a crash, logged once.</item>
/// <item>"Only the spinner on black": while active, <see cref="FlatScreenSuppressed"/>
///   gates <c>FlatScreen.WantVisible</c> to false, so the 2D quad (hints/progress/half-dead
///   menu composite) hides and the Menu2D head camera's black void clear carries the
///   backdrop. The gate is recomputed every tick, so the screen returns through its normal
///   Show() path the moment loading ends. Hands stay visible (mod layer).</item>
/// <item>Smaller hitches (part 2): while loading, <c>Application.backgroundLoadingPriority</c>
///   is dropped to <c>Low</c> — Unity's async-integration time slice per frame shrinks, so
///   the many 25–100 ms integration hitches get smaller at the cost of a somewhat longer
///   load. The game never sets the value and only polls <c>isDone</c>, so nothing depends
///   on load timing. The EXACT prior value is captured before the flip and restored when
///   loading ends (and defensively on <see cref="Shutdown"/>).</item>
/// </list>
///
/// The residual big single frames (per-transition GC.Collect spike, procgen
/// WaitForCompletion frames, the one-time boot activation frame) are in-process
/// unsplittable — the spinner briefly freezes there too; accepted (see commit).
///
/// <para><b>BOOT CLAUSE (ModBuild 107 report, verbatim: "Am Anfang wenn man das Spiel lädt
/// gibt es immer noch eine kurze Phase in der das ganze Spiel hängt nach der Intro. Dann nach
/// einer kurzen Freeze-phase in dem auch die VR hände hängen sieht man das Ladesymbol und dann
/// das Hauptmenu.").</b> The poll above cannot see that window, and the game shows nothing in
/// it either. Both facts come out of the same Player.log (ModBuild 107 / b765a5b6e) read
/// against the decompiled sources:</para>
/// <list type="number">
/// <item>The window runs from <c>[Bootstrap] Finished showing splash screen.</c> (the intro is
///   over) to the game's first <c>Showing loading screen.</c> — about eleven seconds, of which
///   the last ~2.7 s are the hard freeze: three single frames of 569 ms, 1090 ms and 933 ms
///   (<c>[Perf] SPIKE</c> frames 759, 840, 849). 62 % of that is the game parsing its
///   Guildmaster and Campaign YML rulesets synchronously on the main thread (the game's own
///   <c>[YML] … Duration:</c> lines); the mod is 21.3 ms of the 2.7 s, i.e. 0.8 %.</item>
/// <item><c>SceneController.IsLoading</c> is written in exactly ONE place —
///   <c>SceneController.ShowLoadingScreen</c>, which also prints <c>Showing loading screen.</c>
///   — so it is false for the whole window by construction, and the flat loading screen the
///   user finally sees is that very call. Nothing is on screen before it.</item>
/// </list>
/// <para>So the indicator ARMS for the boot window too. The user picked this ("option A":
/// arm at the END OF THE INTRO, not before it — the intro itself keeps playing in VR).</para>
///
/// <para>THE ARMING SEAM is the game's own boot driver, not a guess: <c>Bootstrap.ShowSplash</c>
/// waits for <c>IntroPlayer.EventCompleted</c>, logs <c>Finished showing splash screen.</c> and
/// in the very next statement assigns <c>_loadScene = SceneManager.LoadSceneAsync(
/// "Gloomhaven_unified", Single)</c>. That field going non-null therefore IS "the intro has
/// ended and the load has begun", one field read per tick. The alternatives were rejected on
/// evidence: the active scene name still reads "Intro" throughout (the game's
/// <c>UnloadSceneAsync(Intro)</c> is refused — "Unloading the last loaded scene … is not
/// supported" is in the log), and no scene event fires at this edge.</para>
///
/// <para>THE LATCH is a strictly one-way three-state machine (<see cref="BootCoverage"/>):
/// BeforeIntro → Covering → Done, with Done absorbing. Covering can only be ENTERED from
/// BeforeIntro, and BeforeIntro is left forever on the first tick that sees a live
/// <c>SceneController</c> (a hot reload mid-session, or simply starting after boot, retires the
/// clause without ever arming it). A late <c>SceneController.OnDestroy</c> — scene teardown,
/// return to the main menu, quit to desktop — sets <c>Instance = null</c> again, which is
/// exactly the trap a bare "Instance is null" gate would fall into: it can never resurrect this
/// clause, because Done is terminal and Covering has no entry from it.</para>
///
/// <para>WHY THE RETIREMENT EDGE IS NOT "SceneController appeared". It was specified that way
/// and the log refutes it: <c>Start SceneController</c> stands at frame ~758, BEFORE all three
/// stall frames (759, 840, 849) and ~2.7 s before the game's first loading screen. Retiring
/// there would drop the spinner precisely at the freeze it exists for. The clause is retired
/// instead on the first of: (a) the ordinary poll above going true — a seamless handover, the
/// expected path, and the frame where the game's own loading screen takes the picture;
/// (b) <c>MainMenuUIManager.Instance</c> existing (the menu is up); (c) a
/// <see cref="BootCoverCapSeconds"/> runaway cap. (b) and (c) are pure safety: a clause that
/// stuck ON would also hold <see cref="FlatScreenSuppressed"/> and hide the game forever, so it
/// is given two independent ways to die that do not depend on the game's loading state.</para>
///
/// <para>WHAT IT DOES NOT FIX, and this was stated to the user and accepted before it was
/// built: nothing here shortens the freeze. Unity polls the XR poses, runs the player loop and
/// submits to the compositor on the one thread the YML parse is blocking, so the hands still
/// freeze — and the spinner, which lives on that same thread, freezes with them for the 1090 ms
/// and the 933 ms frame. This changes WHAT THE PLAYER LOOKS AT during the wait (a spinner that
/// is running before and after the two stalls instead of an empty void), not whether the main
/// thread stalls. The mod's 0.8 % share is not where the time goes and cannot be made to be.</para>
///
/// <para><b>THE BOOT SPINNER IS THE GAME'S OWN ART (user, 2026-08-11, verbatim: "Statt eine
/// eigene Ladeanimation am Anfang die du eingefügt hast, bitte nutze auch an dieser Stelle (wie
/// an jeder anderen auch) das Ladesymbol vom Spiel das sich dreht, dass du anderer Stelle schon
/// implementiert hast").</b> ModBuild 108 shipped a procedural ring here and called it
/// unavoidable, reasoning that <c>LoadingScreen</c> is a serialized reference INSIDE
/// <c>Gloomhaven_unified</c> — the scene still loading. That is true of <c>LoadingScreen</c> and
/// false as a conclusion, because the game puts a SECOND copy of the same spinner in the scene
/// that IS loaded:</para>
/// <list type="number">
/// <item><c>IntroPlayer</c> (Intro scene) holds a serialized <c>GameObject _loading</c>, and the
///   last two statements of its <c>ShowLogos</c> coroutine are
///   <c>_loading.SetActive(true); onCompleted?.Invoke();</c>. That callback IS
///   <c>IntroPlayer.EventCompleted</c> — the event <c>Bootstrap.ShowSplash</c> is waiting on
///   before it logs "Finished showing splash screen." and assigns <c>_loadScene</c>. Because
///   <c>ShowSplash</c> then has to spin one more <c>yield return null</c> before it sees
///   <c>introCompleted</c>, the game's own loading widget is switched on a FULL FRAME BEFORE
///   this indicator's arming edge — by construction, not by luck.</item>
/// <item>The Intro scene is still loaded for the whole window: the game's
///   <c>UnloadSceneAsync("Intro")</c> is refused, and the refusal stands in the user's
///   Player.log ("Unloading the last loaded scene Assets/Scenes/Intro.unity … is not supported")
///   two lines above "Finished showing splash screen.".</item>
/// <item>The widget is driven by <c>AnimateLoadingIcon</c>: same four serialized numbers as
///   <c>LoadingScreen</c> (<c>m_IconSpinSpeed</c>/<c>m_IconGlowSpeed</c>/<c>m_IconUpdateSpeed</c>/
///   <c>m_IconOverlayMinAlpha</c>), same <c>m_LoadingIconBase</c>/<c>m_LoadingIconOverlay</c>
///   pair, same AnimateIcon loop — differing ONLY in that it waits on
///   <c>WaitForSecondsRealtime</c> instead of Chronos' <c>Timekeeper</c>. No code anywhere
///   references that class, so it can only be attached in a scene, and a Chronos-free duplicate
///   exists precisely because the scene it lives in has no Timekeeper. At boot the Intro scene
///   is the only such scene.</item>
/// </list>
/// <para>HOW FAR THAT IS PROVEN: (1) and (2) are read straight out of the decompiled sources and
/// the log. (3) is an INFERENCE — serialized scene data is not readable from <c>decompiled/</c>,
/// so WHICH component sits on <c>_loading</c> cannot be settled here. The code therefore does not
/// depend on it: it looks for <c>AnimateLoadingIcon</c> first (best case — art AND the exact
/// tuning), and otherwise takes the sprite <c>Image</c>s under <c>IntroPlayer._loading</c>
/// whatever drives them. The "Loading indicator boot art:" line names the source that actually
/// won, so one hardware run settles it.</para>
///
/// <para>AND A PERSISTED COPY, because that inference may be wrong. The first time an ORDINARY
/// load resolves the real <c>LoadingScreen</c> sprites they are written to
/// <c>BepInEx/config/dev.gloomhavenvr.loadingicon.{base,overlay}.png</c> plus a small
/// <c>.txt</c> carrying the tints, the size ratio and the four animation numbers. Later boots
/// PREFER that copy over the live Intro art, because it is the very symbol every other load
/// shows — which is what was asked for. Cost: one <c>ReadPixels</c> per layer of a sub-rect
/// capped at <see cref="CopyMaxPixels"/> on its long edge, once per installation, on an ordinary
/// load frame that is already behind a spinner — never on the boot path. Re-taken only when the
/// live sprites' names or pixel sizes stop matching the stored identity (i.e. a game update
/// changed the art).</para>
///
/// <para>ORDER: persisted copy → live Intro widget → procedural ring. With the Intro path
/// working the game's symbol shows from the very first launch; if it does not, the first launch
/// after installing still shows the ring and every launch after it shows the game's own.
/// Showing NOTHING during the boot window was rejected: those eleven seconds are exactly the
/// picture that must not read as a crash, which is why this clause exists at all. Whichever
/// source wins, the art is marked PROVISIONAL and dropped on the next hide, so every later load
/// re-reads the live <c>LoadingScreen</c> exactly as it did before — the swap never happens
/// while the spinner is on screen.</para>
///
/// <para>THE BOOT COPY IS MOD-OWNED, and has to be: the Intro scene's textures die when
/// <c>Gloomhaven_unified</c> activates (Unity unloads the now-unused serialized files), which is
/// ~2.7 s BEFORE the game's own loading screen takes over — holding the raw sprite reference
/// would put blank white quads on screen for exactly that stretch. Both layers are therefore
/// blitted into mod-owned <c>RenderTexture</c>s on the arming frame: GPU-side, no readback, no
/// pipeline stall. (A GPU device reset inside the window would blank them; the ordinary path
/// re-reads the live art on the next load either way.)</para>
///
/// <para>APPEARING AND DISAPPEARING: one <see cref="FadeSeconds"/> unscaled alpha ramp both
/// ways (hard rule — nothing in this project pops), applied as a FACTOR on both layers so the
/// game's own glow pulse keeps running straight through it. This is new for every load, not
/// only for boot: the arming edge is now sometimes a LIVE picture (the intro's last frame)
/// rather than the black void it used to be, and punching a spinner onto that would read as a
/// glitch. The flat-screen gate keeps its edge-exact semantics — the screen returns the instant
/// loading ends and the spinner cross-fades out over it, rather than the ramp holding the
/// game's own picture back by a third of a second on every single transition.</para>
///
/// LIFETIME: the quads parent under the head camera (identity local rotation = always
/// facing; a per-tick world-pose copy would lag the tracked head by a frame and swim).
/// The rig can be torn down/rebuilt MID-transition, which destroys the parented quads —
/// every tick re-resolves <c>VRRigDriver.HeadCamera</c> and rebuilds on Unity-null, so the
/// indicator survives any number of rig cycles. Purely local presentation (MP: nothing on
/// the wire); every game-object mutation is mod-owned; [WorldUI] LoadingIndicator=false
/// disables the whole feature (visuals, gate and priority flip).
/// </summary>
internal sealed class LoadingIndicator
{
    // ---- placement (head-local) --------------------------------------------------------
    private const float DistanceMeters = 1.5f;
    private const float BelowEyeMeters = 0.18f;
    private const float SizeMeters = 0.25f;
    /// <summary>Overlay sits this far in FRONT of the base quad (toward the head) so the
    /// transparent queue's distance sort always draws it over the base.</summary>
    private const float OverlayLiftMeters = 0.005f;

    // ---- animation fallbacks (used only when the LoadingScreen tuning fields are
    // unreadable/degenerate; chosen to read like the game's stepped spinner) -------------
    private const float FallbackSpinDegrees = 6f;
    private const float FallbackStepSeconds = 0.025f;
    private const float FallbackGlowStep = 0.02f;
    private const float FallbackMinAlpha = 0.25f;
    /// <summary>Max animation steps consumed per frame — a residual spike frame drops its
    /// backlog instead of fast-forwarding the spinner in one visible jump.</summary>
    private const int MaxStepsPerFrame = 8;

    /// <summary>Appear/disappear ramp (hard rule: nothing in this project pops). Unscaled —
    /// the whole point is that it runs while the game's time is stopped or stalling.</summary>
    private const float FadeSeconds = 0.30f;

    // ---- boot clause (see the class comment) --------------------------------------------
    /// <summary>How often the game's boot driver is looked for while the intro plays. The
    /// search only runs in the BeforeIntro state and stops on the first hit, so this is a
    /// handful of calls in a scene with five renderers — never a steady-state cost.</summary>
    private const float BootSearchIntervalSeconds = 0.25f;
    /// <summary>Longest edge of a mod-owned copy of one spinner layer, in pixels — it caps both
    /// the boot-frame blit and the once-per-installation <c>ReadPixels</c>. Sized off the HMD,
    /// not off a round number: the icon spans <see cref="SizeMeters"/> = 0.25 m at
    /// <see cref="DistanceMeters"/> = 1.5 m, i.e. 9.5°, and the eye texture measured on the user's
    /// rig (3072 px over ~105°) resolves ~29 px per degree — about 280 px across. 512 therefore
    /// stays above native for the copy, so the boot spinner is not visibly softer than the raw
    /// sprite every later load uses. Smaller sources are never upscaled.</summary>
    private const int CopyMaxPixels = 512;

    /// <summary>Runaway cap on the boot clause. The measured window is ~11 s; this is a
    /// safety net, not a schedule — if it ever fires, the indicator simply reverts to its
    /// pre-boot-clause behaviour instead of hiding the game behind a stuck spinner.</summary>
    private const float BootCoverCapSeconds = 90f;

    /// <summary>One-way boot-window latch. Done is absorbing — see the class comment for why
    /// this shape, and not a null check on <c>SceneController.Instance</c>, is what makes a
    /// mid-game resurrection structurally impossible.</summary>
    private enum BootCoverage
    {
        BeforeIntro,
        Covering,
        Done,
    }

    /// <summary>One spinner layer's acquired art + geometry (base or overlay).</summary>
    private sealed class LayerArt
    {
        public Texture? Tex;              // null = procedural fallback texture in FallbackTex
        public Rect Uv = new(0f, 0f, 1f, 1f); // normalized sub-rect (sprite atlas support)
        public Color Tint = Color.white;
        public float Width = 1f;          // authored aspect (uGUI rect), normalized below
        public float Height = 1f;
        public float SizeRatio = 1f;      // this layer's size relative to the BASE layer
    }

    /// <summary>
    /// True while the loading indicator owns the HMD picture — <c>FlatScreen.WantVisible</c>
    /// returns false so ONLY spinner + hands show on the void. Recomputed every tick;
    /// false the instant loading ends or the feature is toggled off (gate self-restores).
    /// </summary>
    internal static bool FlatScreenSuppressed { get; private set; }

    private GameObject? _root;
    private Transform? _baseQuad;
    private Transform? _overlayQuad;
    private Material? _baseMaterial;
    private Material? _overlayMaterial;
    private Mesh? _baseMesh;
    private Mesh? _overlayMesh;
    private Texture2D? _fallbackBaseTex;
    private Texture2D? _fallbackOverlayTex;

    // Art + tuning, read ONCE from the live LoadingScreen (cached across rig rebuilds).
    private LayerArt? _baseArt;
    private LayerArt? _overlayArt;
    private float _spinDegrees = FallbackSpinDegrees;
    private float _stepSeconds = FallbackStepSeconds;
    private float _glowStep = FallbackGlowStep;
    private float _minAlpha = FallbackMinAlpha;
    private bool _artFailedLogged;
    /// <summary>The acquired art is a BOOT-WINDOW stand-in (persisted copy, live Intro widget or
    /// the procedural ring) rather than the live <c>LoadingScreen</c> — dropped on the next HIDE
    /// so the real sprites are picked up once they exist.</summary>
    private bool _artIsProvisional;
    private bool _bootArtLogged;
    /// <summary>Textures this class created and must destroy (boot-window copies: the two
    /// <c>RenderTexture</c> blits, or the two decoded cache PNGs). The procedural ring keeps its
    /// own two fields because those are rebuilt on a different path.</summary>
    private readonly List<Texture> _ownedArtTextures = new(2);
    /// <summary>One cache-write attempt per session — success or failure, we never retry in the
    /// same run (a read-only config directory would otherwise cost a readback on every load).</summary>
    private bool _cacheWriteAttempted;

    // AnimateIcon replica state.
    private float _accum;
    private float _overlayAlpha;
    private bool _increaseAlpha;

    // Background-loading-priority flip (part 2).
    private bool _prioritySet;
    private UnityEngine.ThreadPriority _priorPriority;

    // Appear/disappear ramp: 0 = fully gone (root deactivated), 1 = fully shown.
    private float _fade;

    private bool _shownLogged;

    // Boot clause state.
    private BootCoverage _boot;
    private Bootstrap? _bootstrap;
    private System.Reflection.FieldInfo? _bootLoadSceneField;
    private float _bootSearchCooldown;
    private float _bootCoverSeconds;

    public void Tick()
    {
        // Short circuit, in this order on purpose: with VR off, or the feature off, NOTHING
        // below runs — not the poll, not the boot latch, not its one-off object search.
        bool want = VRSession.IsRunning && WorldUIConfig.LoadingIndicator.Value && IsGameLoading();

        // Part 2 runs whenever the feature is on and a load is in flight — flipped and
        // restored on the edges only, one log line each (existing VRLog style).
        //
        // NOT during the boot window, and that exclusion is deliberate: shrinking Unity's
        // async-integration slice buys smoother frames by making the load LONGER, and the boot
        // load is the one the user is complaining about the length of. It would also buy
        // nothing there — the frames that stall are the game's own synchronous YML parse on
        // the main thread, not async integration, so a smaller slice cannot touch them. The
        // boot clause is a display decision and stays one; it must not sit on the load path.
        TickLoadPriority(want && _boot != BootCoverage.Covering);

        // The flat-screen gate keeps its shipped edge-exact semantics (the screen returns the
        // instant loading ends). The spinner's own fade-out then cross-fades over the screen
        // that just came back, rather than delaying it by a ramp on every load.
        FlatScreenSuppressed = want;

        // Appear/disappear WITH the animation (hard rule): one unscaled ramp both ways. It is
        // also what makes the boot arming bearable — the spinner rises over the intro's last
        // frame instead of being punched onto it.
        _fade = Mathf.MoveTowards(_fade, want ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);

        if (!want && _fade <= 0f)
        {
            HideNow();
            return;
        }

        // Re-resolve the head EVERY tick: the menu/scenario rig is torn down and rebuilt
        // mid-transition, killing both the old camera and (via parenting) our quads.
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (_root != null)
                _root.SetActive(false); // no camera = nothing renders anyway; wait for the rebuild
            // Nothing was on screen, so there is nothing to fade FROM: the rebuild fades in
            // from zero rather than popping back at whatever alpha the ramp had reached.
            _fade = 0f;
            return;
        }

        if (_root == null)
            BuildVisual();
        if (_root == null)
            return; // hard build failure already logged (once)

        if (_root.transform.parent != head.transform)
        {
            _root.transform.SetParent(head.transform, worldPositionStays: false);
            _root.transform.localPosition = new Vector3(0f, -BelowEyeMeters, DistanceMeters);
            _root.transform.localRotation = Quaternion.identity; // child of the head = always facing
            _root.transform.localScale = Vector3.one;
        }
        if (!_root.activeSelf)
            _root.SetActive(true);
        if (!_shownLogged)
        {
            _shownLogged = true;
            VRLog.Info("WorldUI", $"Loading indicator shown: {(_boot == BootCoverage.Covering ? "BOOT window (intro over, " +
                                      "Gloomhaven_unified loading — the game shows nothing here)" : "game spinner")} " +
                                  $"{DistanceMeters:0.0} m ahead of head '{head.name}' (step {_spinDegrees:0.#}° / " +
                                  $"{_stepSeconds:0.###}s, glow {_glowStep:0.###}, min alpha {_minAlpha:0.##}, " +
                                  $"fade {FadeSeconds:0.00}s) — flat screen suppressed for the load.");
        }

        Animate();
    }

    /// <summary>Fully faded out: drop the visual and, if the art is the boot window's
    /// provisional ring, drop that too so the next show re-reads the game's own sprites.</summary>
    private void HideNow()
    {
        if (_root != null && _root.activeSelf)
        {
            _root.SetActive(false);
            VRLog.Info("WorldUI", "Loading indicator hidden (loading ended, faded out) — flat screen released.");
        }
        _shownLogged = false;
        if (_artIsProvisional && SceneController.Instance != null)
            DropProvisionalArt();
    }

    public void Shutdown()
    {
        // Defensive restore: never leave the process with a lowered loading priority.
        if (_prioritySet)
        {
            Application.backgroundLoadingPriority = _priorPriority;
            _prioritySet = false;
            VRLog.Info("WorldUI", $"Loading indicator shutdown: backgroundLoadingPriority restored to {_priorPriority}.");
        }
        FlatScreenSuppressed = false;
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
        _baseQuad = null;
        _overlayQuad = null;
        DestroyObj(ref _baseMaterial);
        DestroyObj(ref _overlayMaterial);
        DestroyObj(ref _baseMesh);
        DestroyObj(ref _overlayMesh);
        DestroyObj(ref _fallbackBaseTex);
        DestroyObj(ref _fallbackOverlayTex);
        DestroyOwnedArtTextures();
        _baseArt = null;
        _overlayArt = null;
        _artIsProvisional = false;
        _shownLogged = false;
        _fade = 0f;
        // _boot is deliberately NOT reset: the latch is one-way for the whole process. A VR
        // stop/start or a hot reload happens long after boot, and re-arming there is precisely
        // the resurrection this design forbids (BeforeIntro would also retire on the live
        // SceneController anyway — this is belt and braces).
    }

    /// <summary>
    /// Throw away the boot window's provisional art while the indicator is INVISIBLE, so the
    /// next show re-resolves the game's own live spinner sprites. Deliberately not done at the
    /// moment the real art becomes available (that moment is mid-boot-window, with the spinner on
    /// screen — swapping the art under the player is exactly the pop this project forbids).
    /// </summary>
    private void DropProvisionalArt()
    {
        _artIsProvisional = false;
        _baseArt = null;
        _overlayArt = null;
        _baseQuad = null;
        _overlayQuad = null;
        if (_root != null)
        {
            UnityEngine.Object.Destroy(_root);
            _root = null;
        }
        DestroyObj(ref _baseMaterial);
        DestroyObj(ref _overlayMaterial);
        DestroyObj(ref _baseMesh);
        DestroyObj(ref _overlayMesh);
        DestroyObj(ref _fallbackBaseTex);
        DestroyObj(ref _fallbackOverlayTex);
        DestroyOwnedArtTextures();
        VRLog.Info("WorldUI", "Loading indicator: provisional boot-window art dropped while hidden — the next load " +
                              "re-reads the game's own live spinner art.");
    }

    /// <summary>Destroy the boot-window copies (RenderTexture blits / decoded cache PNGs). They
    /// are not scene objects, so nothing else ever collects them.</summary>
    private void DestroyOwnedArtTextures()
    {
        for (int i = 0; i < _ownedArtTextures.Count; i++)
        {
            Texture t = _ownedArtTextures[i];
            if (t != null)
                UnityEngine.Object.Destroy(t);
        }
        _ownedArtTextures.Clear();
    }

    private static void DestroyObj<T>(ref T? obj) where T : UnityEngine.Object
    {
        if (obj != null)
        {
            UnityEngine.Object.Destroy(obj);
            obj = null;
        }
    }

    /// <summary>
    /// The game is in a loading transition (public state, no patches needed) — OR it is in the
    /// boot window, which that public state cannot express (class comment, BOOT CLAUSE).
    /// Called ONLY from the <c>VRSession.IsRunning</c> short circuit in <see cref="Tick"/>, so
    /// with VR off nothing below ever runs.
    /// </summary>
    private bool IsGameLoading()
    {
        SceneController sc = SceneController.Instance;
        bool ordinary = sc != null && (sc.IsLoading || sc.ScenarioIsLoading);
        // Evaluated FIRST and unconditionally: the latch has to advance on the very tick the
        // ordinary poll goes true, which is the tick it retires on.
        bool boot = TickBootCoverage(sc, ordinary);
        return ordinary || boot;
    }

    /// <summary>
    /// Advance the one-way boot latch and answer whether the boot clause currently holds.
    /// Pure display bookkeeping: it reads state, it never waits on or drives a load.
    /// </summary>
    private bool TickBootCoverage(SceneController? sc, bool ordinary)
    {
        switch (_boot)
        {
            case BootCoverage.Done:
                return false;

            case BootCoverage.BeforeIntro:
                if (sc != null)
                {
                    // The game is already up: booted long ago, or the mod (re)started
                    // mid-session. The clause retires WITHOUT ever arming — this is the one
                    // check that keeps a hot reload from arming on a boot seam that has been
                    // sitting non-null since startup.
                    _boot = BootCoverage.Done;
                    return false;
                }
                if (!IntroHasEnded())
                    return false;
                _boot = BootCoverage.Covering;
                _bootCoverSeconds = 0f;
                VRLog.Info("WorldUI", "Loading indicator ARMED for the boot window: the intro has ended and " +
                                      "'Gloomhaven_unified' is loading (Bootstrap._loadScene assigned). The game puts " +
                                      "nothing on screen until its own loading screen, ~11 s later; the main thread " +
                                      "still stalls at the YML parse frames and the spinner stops with it.");
                return true;

            default: // Covering
                _bootCoverSeconds += Time.unscaledDeltaTime;
                if (ordinary)
                {
                    // The handover, and the expected exit: the game's own loading state is now
                    // true, so the ordinary poll carries the very same picture from this frame
                    // on — the spinner never blinks. Returning false here is correct, the
                    // caller ORs it with `ordinary`.
                    RetireBootCoverage("the game's own loading state took over (seamless handover)");
                    return false;
                }
                if (GLOOM.MainMenu.MainMenuUIManager.Instance != null)
                {
                    RetireBootCoverage("the main menu exists");
                    return false;
                }
                if (_bootCoverSeconds > BootCoverCapSeconds)
                {
                    RetireBootCoverage($"runaway cap {BootCoverCapSeconds:0}s reached — falling back to the " +
                                       "pre-boot-clause behaviour rather than holding the picture");
                    return false;
                }
                return true;
        }
    }

    /// <summary>Terminal, by construction: nothing ever assigns anything but Done again.</summary>
    private void RetireBootCoverage(string why)
    {
        _boot = BootCoverage.Done;
        _bootstrap = null;
        _bootLoadSceneField = null;
        VRLog.Info("WorldUI", $"Loading indicator boot window closed after {_bootCoverSeconds:0.0}s — {why}. " +
                              "The clause cannot re-arm for the rest of the session.");
    }

    /// <summary>
    /// The intro-end seam, read off the game's own boot driver: <c>Bootstrap.ShowSplash</c>
    /// assigns <c>_loadScene</c> in the statement directly after it logs
    /// <c>Finished showing splash screen.</c>, so non-null == "the intro is over and
    /// Gloomhaven_unified has started loading". Any failure (renamed field after a game
    /// update, missing driver) retires the clause — the indicator then behaves exactly as it
    /// did before this existed.
    /// </summary>
    private bool IntroHasEnded()
    {
        try
        {
            if (_bootstrap == null)
            {
                _bootSearchCooldown -= Time.unscaledDeltaTime;
                if (_bootSearchCooldown > 0f)
                    return false;
                _bootSearchCooldown = BootSearchIntervalSeconds;
                _bootstrap = UnityEngine.Object.FindObjectOfType<Bootstrap>();
                if (_bootstrap == null)
                    return false;
                _bootLoadSceneField = AccessTools.Field(typeof(Bootstrap), "_loadScene");
                if (_bootLoadSceneField == null)
                {
                    _boot = BootCoverage.Done;
                    VRLog.Warn("WorldUI", "Loading indicator: Bootstrap._loadScene not found — the boot window " +
                                          "cannot be recognised and stays uncovered (everything else is unaffected).");
                    return false;
                }
            }
            return _bootLoadSceneField!.GetValue(_bootstrap) != null;
        }
        catch (Exception ex)
        {
            _boot = BootCoverage.Done;
            VRLog.Warn("WorldUI", $"Loading indicator: reading the game's boot seam failed ({ex.GetType().Name}: " +
                                  $"{ex.Message}) — the boot window stays uncovered.");
            return false;
        }
    }

    // ---- part 2: async-integration slice shrink ------------------------------------------

    private void TickLoadPriority(bool loading)
    {
        if (loading && !_prioritySet)
        {
            _priorPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;
            _prioritySet = true;
            VRLog.Info("WorldUI", $"Loading started: backgroundLoadingPriority {_priorPriority} → Low " +
                                  "(smaller per-frame async-integration slices — smaller hitches, slightly longer load).");
        }
        else if (!loading && _prioritySet)
        {
            Application.backgroundLoadingPriority = _priorPriority;
            _prioritySet = false;
            VRLog.Info("WorldUI", $"Loading ended: backgroundLoadingPriority restored to {_priorPriority}.");
        }
    }

    // ---- visuals -------------------------------------------------------------------------

    private void BuildVisual()
    {
        ResolveArt();
        if (_baseArt == null || _overlayArt == null)
            return; // ResolveArt always produces SOMETHING; null only on a logged hard failure

        _root = new GameObject("GloomhavenVR.LoadingIndicator");
        // Parented under the (DontDestroyOnLoad) rig, but the flag also covers the brief
        // unparented window during a rebuild — Single-mode scene loads must never take it.
        UnityEngine.Object.DontDestroyOnLoad(_root);

        // A mid-transition rig teardown destroys the parented quads but NOT these materials
        // or mesh clones — drop the orphans before creating the rebuild's set (no per-rebuild
        // leak).
        DestroyObj(ref _baseMaterial);
        DestroyObj(ref _overlayMaterial);
        DestroyObj(ref _baseMesh);
        DestroyObj(ref _overlayMesh);
        _baseMaterial = CreateLayerMaterial(_baseArt, _fallbackBaseTex);
        _overlayMaterial = CreateLayerMaterial(_overlayArt, _fallbackOverlayTex);
        _baseQuad = CreateQuad("Base", _baseMaterial, _baseArt, 0f, ref _baseMesh);
        _overlayQuad = CreateQuad("Overlay", _overlayMaterial, _overlayArt, -OverlayLiftMeters, ref _overlayMesh);

        // Match LoadingScreen.OnEnable: overlay starts at min alpha, rising.
        _overlayAlpha = _minAlpha;
        _increaseAlpha = true;
        _accum = 0f;
        ApplyOverlayAlpha();

        VRLayers.Apply(_root); // mod layer: only the rig head camera renders it (CAMERA-POLICY §2)
    }

    private Material CreateLayerMaterial(LayerArt art, Texture2D? fallbackTex)
    {
        // Same shader family as WorldUIAssets.CreateFlatMaterial, but alpha-blended on
        // purpose: the spinner art lives in the sprite's alpha channel, and Sprites/Default
        // ships shipped-verified (FlatScreen glass uses it) with a _Color tint the glow
        // pulse can drive.
        Shader? shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default")
                         ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader)
        {
            mainTexture = art.Tex != null ? art.Tex : fallbackTex,
            color = art.Tint,
            // No mainTextureScale/Offset here: Sprites/Default ignores _MainTex_ST (no
            // TRANSFORM_TEX in its vertex shader) — the quad's mesh UVs carry the atlas
            // sub-rect instead (see CreateQuad).
        };
        return material;
    }

    private Transform CreateQuad(string name, Material material, LayerArt art, float zOffset, ref Mesh? meshClone)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "GloomhavenVR.LoadingIndicator." + name;
        UnityEngine.Object.Destroy(quad.GetComponent<Collider>()); // visual only — never a ray/poke target
        quad.GetComponent<Renderer>().sharedMaterial = material;
        // Atlas sub-rect via mesh UVs (shader-independent). Identity rect (procedural
        // fallback textures) keeps the shared primitive mesh — nothing to remap.
        Rect uv = art.Uv;
        if (uv.x != 0f || uv.y != 0f || uv.width != 1f || uv.height != 1f)
        {
            MeshFilter meshFilter = quad.GetComponent<MeshFilter>();
            // Clone — NEVER mutate the shared primitive mesh (every Quad in the process,
            // including the mod's own, would be corrupted).
            Mesh clone = UnityEngine.Object.Instantiate(meshFilter.sharedMesh);
            clone.name = "GloomhavenVR.LoadingIndicator." + name + ".Mesh";
            Vector2[] uvs = clone.uv;
            for (int i = 0; i < uvs.Length; i++)
                uvs[i] = new Vector2(uv.x + uvs[i].x * uv.width, uv.y + uvs[i].y * uv.height);
            clone.uv = uvs;
            meshFilter.mesh = clone;
            // The quad dies with _root, but the clone is an asset-like orphan — tracked so
            // Shutdown/BuildVisual destroy it explicitly (mirrors the material bookkeeping).
            meshClone = clone;
        }
        Transform t = quad.transform;
        t.SetParent(_root!.transform, worldPositionStays: false);
        t.localPosition = new Vector3(0f, 0f, zOffset); // -z = toward the head (quad front faces the camera)
        t.localRotation = Quaternion.identity;
        // Normalize the authored aspect so the larger side spans SizeMeters × SizeRatio.
        float max = Mathf.Max(art.Width, art.Height);
        float size = SizeMeters * art.SizeRatio;
        t.localScale = new Vector3(size * art.Width / max, size * art.Height / max, 1f);
        return t;
    }

    /// <summary>
    /// Read the REAL spinner art + tuning off the live <c>LoadingScreen</c> once
    /// (AccessTools field access on the private serialized fields). Any failure — missing
    /// instance, tight-packed sprite (textureRect throws), renamed field after a game
    /// update — falls back to a procedural ring/arc so the indicator NEVER crashes or
    /// goes blank; logged once at Warn.
    /// </summary>
    private void ResolveArt()
    {
        if (_baseArt != null && _overlayArt != null)
            return;
        if (SceneController.Instance == null)
        {
            // BOOT WINDOW: the live LoadingScreen is a serialized reference INSIDE
            // Gloomhaven_unified, the scene still loading — but the game's own spinner art is
            // reachable anyway (class comment). Never a failure path, hence Info and
            // _artFailedLogged untouched so a genuine later failure can still say so once.
            ResolveBootArt();
            return;
        }
        try
        {
            LoadingScreen? ls = SceneController.Instance != null ? SceneController.Instance.LoadingScreenInstance : null;
            if (ls != null)
            {
                var baseGo = AccessTools.Field(typeof(LoadingScreen), "m_LoadingIconBase")?.GetValue(ls) as GameObject;
                var overlayGo = AccessTools.Field(typeof(LoadingScreen), "m_LoadingIconOverlay")?.GetValue(ls) as GameObject;
                LayerArt? baseArt = ReadLayer(baseGo, baseGo);
                LayerArt? overlayArt = ReadLayer(overlayGo, baseGo);
                if (baseArt != null && overlayArt != null)
                {
                    _baseArt = baseArt;
                    _overlayArt = overlayArt;
                    _artIsProvisional = false;
                    _spinDegrees = ReadTuning(ls, "m_IconSpinSpeed", FallbackSpinDegrees);
                    _stepSeconds = ReadTuning(ls, "m_IconUpdateSpeed", FallbackStepSeconds);
                    _glowStep = ReadTuning(ls, "m_IconGlowSpeed", FallbackGlowStep);
                    // Min alpha may legitimately be 0 — only NaN/negative/degenerate falls back.
                    float minAlpha = ReadTuningRaw(ls, "m_IconOverlayMinAlpha");
                    _minAlpha = (minAlpha >= 0f && minAlpha < 1f) ? minAlpha : FallbackMinAlpha;
                    VRLog.Info("WorldUI", "Loading indicator art: game spinner sprites acquired " +
                                          $"(base '{DescribeTex(_baseArt)}', overlay '{DescribeTex(_overlayArt)}').");
                    // Off the boot path by construction (this branch needs a live SceneController):
                    // persist a copy so the NEXT boot can show this exact symbol even if the Intro
                    // source above ever fails. Once per installation; see the class comment.
                    TryWriteIconCache(baseGo, overlayGo, _baseArt, _overlayArt);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_artFailedLogged)
            {
                _artFailedLogged = true;
                VRLog.Warn("WorldUI", $"Loading indicator: reading the game's spinner art failed ({ex.GetType().Name}: " +
                                      $"{ex.Message}) — procedural ring fallback active.");
            }
        }
        if (!_artFailedLogged)
        {
            _artFailedLogged = true;
            VRLog.Warn("WorldUI", "Loading indicator: LoadingScreen spinner sprites unavailable — " +
                                  "procedural ring fallback active.");
        }
        BuildFallbackArt();
    }

    // ---- boot-window art: the game's own symbol, not the mod's ring -----------------------

    /// <summary>
    /// Acquire the boot window's art in strict preference order (class comment):
    /// <list type="number">
    /// <item>the PERSISTED copy of the game's own loading screen, written by an earlier session —
    ///   byte-identical to the symbol every ordinary load shows, which is what the user asked
    ///   for;</item>
    /// <item>the LIVE widget the game itself switched on one frame ago in the Intro scene;</item>
    /// <item>the procedural ring — now a last resort, not the answer.</item>
    /// </list>
    /// Always produces something, never throws: every step is individually guarded and simply
    /// falls through to the next. Whatever wins is PROVISIONAL and dropped on the next hide.
    /// </summary>
    private void ResolveBootArt()
    {
        string source;
        if (TryCachedIconArt())
            source = "the persisted copy of the game's own loading screen (written by an earlier session)";
        else if (TryIntroIconArt(out string introHow))
            source = introHow;
        else
        {
            BuildFallbackArt();
            source = "the procedural ring — neither a persisted copy nor the Intro scene's own loading widget " +
                     "could be read; the game's symbol will be persisted on the first ordinary load and used " +
                     "from the next launch on";
        }
        if (!_bootArtLogged)
        {
            _bootArtLogged = true;
            VRLog.Info("WorldUI", $"Loading indicator boot art: {source} " +
                                  $"(base '{DescribeTex(_baseArt!)}', overlay '{DescribeTex(_overlayArt!)}', " +
                                  $"step {_spinDegrees:0.#}° / {_stepSeconds:0.###}s, glow {_glowStep:0.###}, " +
                                  $"min alpha {_minAlpha:0.##}).");
        }
    }

    /// <summary>
    /// The live loading widget in the Intro scene — the one the game's own boot code switched on
    /// in the statement before the edge this indicator arms at. Preferred shape is
    /// <c>AnimateLoadingIcon</c>, which carries both layers AND the four animation numbers, so
    /// the spin direction, the step size and the glow pulse are the game's own rather than this
    /// file's constants. If that component is not there, the game's serialized
    /// <c>IntroPlayer._loading</c> object is walked for sprite <c>Image</c>s instead — that path
    /// keeps the fallback tuning, which is stated in the log line so a hardware run can tell the
    /// two apart.
    /// </summary>
    private bool TryIntroIconArt(out string how)
    {
        how = string.Empty;
        try
        {
            GameObject? baseGo = null;
            GameObject? overlayGo = null;
            AnimateLoadingIcon? anim = FindLiveInstance<AnimateLoadingIcon>();
            if (anim != null)
            {
                baseGo = AccessTools.Field(typeof(AnimateLoadingIcon), "m_LoadingIconBase")?.GetValue(anim) as GameObject;
                overlayGo = AccessTools.Field(typeof(AnimateLoadingIcon), "m_LoadingIconOverlay")?.GetValue(anim) as GameObject;
                how = "the live 'AnimateLoadingIcon' widget in the Intro scene (art AND the game's own animation numbers)";
            }
            if (baseGo == null || overlayGo == null)
            {
                // Second shape: whatever the game switches on, read as plain uGUI. Hierarchy
                // order decides base/overlay — that is how both known animators declare them.
                IntroPlayer? intro = FindLiveInstance<IntroPlayer>();
                var loading = intro != null
                    ? AccessTools.Field(typeof(IntroPlayer), "_loading")?.GetValue(intro) as GameObject
                    : null;
                if (loading == null)
                    return false;
                Image[] images = loading.GetComponentsInChildren<Image>(includeInactive: true);
                var icons = new List<Image>(2);
                for (int i = 0; i < images.Length; i++)
                {
                    Image img = images[i];
                    if (img == null || img.sprite == null)
                        continue;
                    // Keep only square-ish rects. A "loading" object plausibly also carries a
                    // full-width backdrop or a text bar, and blowing one of those up into the
                    // spinner quad would look far worse than the ring this replaces. Every icon
                    // the game animates (LoadingScreen's and AnimateLoadingIcon's alike) is a
                    // rotating disc, so squareness is the one property that must hold.
                    Rect r = img.rectTransform.rect;
                    float aspect = r.height > 0.01f ? r.width / r.height : 0f;
                    if (aspect < 0.5f || aspect > 2f)
                        continue;
                    icons.Add(img);
                }
                if (icons.Count == 0)
                    return false;
                baseGo = icons[0].gameObject;
                overlayGo = icons[icons.Count > 1 ? 1 : 0].gameObject;
                anim = null;
                how = $"the spinner sprites under the game's own 'IntroPlayer._loading' object " +
                      $"({icons.Count} of {images.Length} image(s) kept; no AnimateLoadingIcon found, so the " +
                      "animation numbers stay at this file's fallbacks)";
            }

            LayerArt? baseArt = ReadLayer(baseGo, baseGo);
            LayerArt? overlayArt = ReadLayer(overlayGo, baseGo);
            if (baseArt == null || overlayArt == null)
                return false;
            // The Intro scene's textures die when Gloomhaven_unified activates — take mod-owned
            // copies NOW or the last ~2.7 s of the window renders blank white quads.
            if (!AdoptOwnedCopy(baseArt) || !AdoptOwnedCopy(overlayArt))
            {
                DestroyOwnedArtTextures();
                return false;
            }
            _baseArt = baseArt;
            _overlayArt = overlayArt;
            _artIsProvisional = true;
            if (anim != null)
            {
                _spinDegrees = ReadAnimTuning(anim, "m_IconSpinSpeed", FallbackSpinDegrees);
                _stepSeconds = ReadAnimTuning(anim, "m_IconUpdateSpeed", FallbackStepSeconds);
                _glowStep = ReadAnimTuning(anim, "m_IconGlowSpeed", FallbackGlowStep);
                float minAlpha = ReadFloatField(anim, typeof(AnimateLoadingIcon), "m_IconOverlayMinAlpha");
                _minAlpha = (minAlpha >= 0f && minAlpha < 1f) ? minAlpha : FallbackMinAlpha;
            }
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Info("WorldUI", $"Loading indicator: the Intro scene's loading widget could not be read " +
                                  $"({ex.GetType().Name}: {ex.Message}) — falling through to the next source.");
            return false;
        }
    }

    /// <summary>First live (scene-resident) instance of a game component, inactive ones included.
    /// <c>FindObjectOfType</c> skips inactive objects and would miss a widget whose parent canvas
    /// is off; the scene check rejects the prefab/asset copies <c>FindObjectsOfTypeAll</c> also
    /// returns. Runs once, in a boot window where the only loaded scene is the Intro.</summary>
    private static T? FindLiveInstance<T>() where T : Component
    {
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.scene.IsValid())
                return all[i];
        }
        return null;
    }

    /// <summary>
    /// Replace a layer's borrowed texture with a mod-owned copy of just its sprite sub-rect.
    /// GPU-side <c>Graphics.Blit</c>: no <c>ReadPixels</c>, no readback, no pipeline stall — this
    /// runs on the arming frame, which is on the boot path. Capped at
    /// <see cref="CopyMaxPixels"/> on the long edge, aspect preserved.
    /// </summary>
    private bool AdoptOwnedCopy(LayerArt art)
    {
        Texture? src = art.Tex;
        if (src == null || src.width <= 0 || src.height <= 0)
            return false;
        Rect uv = art.Uv;
        float pw = Mathf.Max(1f, uv.width * src.width);
        float ph = Mathf.Max(1f, uv.height * src.height);
        float shrink = Mathf.Min(1f, CopyMaxPixels / Mathf.Max(pw, ph));
        int w = Mathf.Max(1, Mathf.RoundToInt(pw * shrink));
        int h = Mathf.Max(1, Mathf.RoundToInt(ph * shrink));
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            name = "GloomhavenVR.LoadingIconCopy",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        if (!rt.Create())
        {
            UnityEngine.Object.Destroy(rt);
            return false;
        }
        RenderTexture prev = RenderTexture.active;
        try
        {
            // Blit leaves the sub-rect filling the whole target, so the layer's UV rect becomes
            // the identity and CreateQuad keeps the shared primitive mesh.
            Graphics.Blit(src, rt, new Vector2(uv.width, uv.height), new Vector2(uv.x, uv.y));
        }
        finally
        {
            RenderTexture.active = prev;
        }
        art.Tex = rt;
        art.Uv = new Rect(0f, 0f, 1f, 1f);
        _ownedArtTextures.Add(rt);
        return true;
    }

    private static float ReadAnimTuning(AnimateLoadingIcon anim, string field, float fallback)
    {
        float v = ReadFloatField(anim, typeof(AnimateLoadingIcon), field);
        return (v > 0f && !float.IsNaN(v) && !float.IsInfinity(v)) ? v : fallback;
    }

    private static float ReadFloatField(object target, Type type, string field)
    {
        object? v = AccessTools.Field(type, field)?.GetValue(target);
        return v is float f ? f : float.NaN;
    }

    // ---- persisted copy of the game's own loading symbol ----------------------------------

    /// <summary>Per-user data lives where every other mod-owned file lives — the BepInEx config
    /// directory, under the same <c>dev.gloomhavenvr.*</c> naming the module configs use. Never
    /// the game install.</summary>
    private static string IconCachePath(string suffix) =>
        Path.Combine(Paths.ConfigPath, MyPluginInfo.PLUGIN_GUID + ".loadingicon" + suffix);

    /// <summary>What the stored copy is a copy OF. Compared against the live sprites on every
    /// ordinary load so a game update that changes the art re-takes the capture exactly once.</summary>
    private static string IconCacheIdentity(Sprite b, Sprite o) =>
        string.Format(CultureInfo.InvariantCulture, "{0}:{1}x{2}|{3}:{4}x{5}",
            b.name, (int)b.textureRect.width, (int)b.textureRect.height,
            o.name, (int)o.textureRect.width, (int)o.textureRect.height);

    /// <summary>
    /// Load the persisted spinner. Any problem at all — files missing, unreadable, truncated,
    /// not a PNG, a manifest from a future version — returns false with the partial work cleaned
    /// up, so the caller simply falls through to the next source. Corrupt input therefore
    /// degrades to today's behaviour and never to an exception on the boot path.
    /// </summary>
    private bool TryCachedIconArt()
    {
        Texture2D? baseTex = null;
        Texture2D? overlayTex = null;
        try
        {
            string manifest = IconCachePath(".txt");
            if (!File.Exists(manifest) || !File.Exists(IconCachePath(".base.png")) || !File.Exists(IconCachePath(".overlay.png")))
                return false;
            Dictionary<string, string> kv = ParseManifest(File.ReadAllLines(manifest));
            if (!kv.TryGetValue("v", out string? version) || version != "1")
                return false;
            baseTex = LoadPngTexture(IconCachePath(".base.png"), "GloomhavenVR.LoadingIconCached.Base");
            overlayTex = baseTex != null ? LoadPngTexture(IconCachePath(".overlay.png"), "GloomhavenVR.LoadingIconCached.Overlay") : null;
            if (baseTex == null || overlayTex == null)
            {
                DestroyObj(ref baseTex);
                DestroyObj(ref overlayTex);
                return false;
            }
            // The PNGs are already cropped to the sprite rect, so the UV rect is the identity and
            // the pixel dimensions carry the authored aspect.
            _baseArt = new LayerArt
            {
                Tex = baseTex,
                Tint = ParseColor(kv, "basetint", Color.white),
                Width = baseTex.width,
                Height = baseTex.height,
            };
            _overlayArt = new LayerArt
            {
                Tex = overlayTex,
                Tint = ParseColor(kv, "overlaytint", Color.white),
                Width = overlayTex.width,
                Height = overlayTex.height,
                SizeRatio = Mathf.Clamp(ParseFloat(kv, "overlayratio", 1f), 0.25f, 4f),
            };
            _ownedArtTextures.Add(baseTex);
            _ownedArtTextures.Add(overlayTex);
            _artIsProvisional = true;
            _spinDegrees = PositiveOr(ParseFloat(kv, "spin", float.NaN), FallbackSpinDegrees);
            _stepSeconds = PositiveOr(ParseFloat(kv, "step", float.NaN), FallbackStepSeconds);
            _glowStep = PositiveOr(ParseFloat(kv, "glow", float.NaN), FallbackGlowStep);
            float minAlpha = ParseFloat(kv, "minalpha", float.NaN);
            _minAlpha = (minAlpha >= 0f && minAlpha < 1f) ? minAlpha : FallbackMinAlpha;
            return true;
        }
        catch (Exception ex)
        {
            DestroyObj(ref baseTex);
            DestroyObj(ref overlayTex);
            VRLog.Info("WorldUI", $"Loading indicator: the persisted loading symbol could not be read " +
                                  $"({ex.GetType().Name}: {ex.Message}) — falling through to the next source; " +
                                  "it is rewritten on the next ordinary load.");
            return false;
        }
    }

    /// <summary>
    /// Persist the game's own spinner so the NEXT boot can show it. Called only from the
    /// ordinary path (a live <c>SceneController</c> is required to get here), at most once per
    /// session and only when there is no current copy on disk.
    ///
    /// <para>COST, stated plainly: two <c>Graphics.Blit</c>es plus two
    /// <c>Texture2D.ReadPixels</c> of at most <see cref="CopyMaxPixels"/>² pixels. A ReadPixels
    /// is a full pipeline stall — at the 512×512 cap that is 1 MiB per layer, order a few
    /// milliseconds for the pair. It happens on ONE frame, once per installation, during an
    /// ordinary load that is already covered by this very spinner, and never during the boot
    /// window. If the game's sprites are smaller than the cap, so is the readback.</para>
    /// </summary>
    private void TryWriteIconCache(GameObject? baseGo, GameObject? overlayGo, LayerArt baseArt, LayerArt overlayArt)
    {
        if (_cacheWriteAttempted || baseGo == null || overlayGo == null)
            return;
        _cacheWriteAttempted = true;
        Texture2D? basePixels = null;
        Texture2D? overlayPixels = null;
        try
        {
            Image? baseImage = baseGo.GetComponent<Image>();
            Image? overlayImage = overlayGo.GetComponent<Image>();
            Sprite? baseSprite = baseImage != null ? baseImage.sprite : null;
            Sprite? overlaySprite = overlayImage != null ? overlayImage.sprite : null;
            if (baseSprite == null || overlaySprite == null)
                return;
            string identity = IconCacheIdentity(baseSprite, overlaySprite);
            string manifest = IconCachePath(".txt");
            if (File.Exists(manifest) && File.Exists(IconCachePath(".base.png")) && File.Exists(IconCachePath(".overlay.png")))
            {
                Dictionary<string, string> existing = ParseManifest(File.ReadAllLines(manifest));
                if (existing.TryGetValue("v", out string? v) && v == "1" &&
                    existing.TryGetValue("id", out string? id) && id == identity)
                    return; // already the current art — no readback at all
            }
            basePixels = CaptureSpritePixels(baseSprite);
            overlayPixels = basePixels != null ? CaptureSpritePixels(overlaySprite) : null;
            if (basePixels == null || overlayPixels == null)
                return;
            File.WriteAllBytes(IconCachePath(".base.png"), basePixels.EncodeToPNG());
            File.WriteAllBytes(IconCachePath(".overlay.png"), overlayPixels.EncodeToPNG());
            File.WriteAllText(IconCachePath(".txt"), BuildIconManifest(identity, baseArt, overlayArt));
            VRLog.Info("WorldUI", $"Loading indicator: the game's loading symbol was persisted to " +
                                  $"'{IconCachePath(".*")}' ({basePixels.width}x{basePixels.height} + " +
                                  $"{overlayPixels.width}x{overlayPixels.height}) — from the next launch on, the " +
                                  "boot window shows this exact symbol instead of a stand-in.");
        }
        catch (Exception ex)
        {
            VRLog.Info("WorldUI", $"Loading indicator: persisting the game's loading symbol failed " +
                                  $"({ex.GetType().Name}: {ex.Message}) — harmless, the boot window keeps using the " +
                                  "Intro scene's live widget (or the ring).");
        }
        finally
        {
            DestroyObj(ref basePixels);
            DestroyObj(ref overlayPixels);
        }
    }

    /// <summary>Sprite sub-rect → a readable RGBA32 <see cref="Texture2D"/> via a temporary render
    /// target. The blit is what makes this work on compressed/atlased/non-readable game textures;
    /// the readback is the cost documented on <see cref="TryWriteIconCache"/>.</summary>
    private static Texture2D? CaptureSpritePixels(Sprite sprite)
    {
        Texture2D src = sprite.texture;
        if (src == null || src.width <= 0 || src.height <= 0)
            return null;
        Rect tr = sprite.textureRect;
        float shrink = Mathf.Min(1f, CopyMaxPixels / Mathf.Max(Mathf.Max(1f, tr.width), Mathf.Max(1f, tr.height)));
        int w = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, tr.width) * shrink));
        int h = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1f, tr.height) * shrink));
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt,
                new Vector2(tr.width / src.width, tr.height / src.height),
                new Vector2(tr.x / src.width, tr.y / src.height));
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false, linear: false);
            tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, recalculateMipMaps: false);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static Texture2D? LoadPngTexture(string path, string name)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8)
            return null;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        if (!tex.LoadImage(bytes, markNonReadable: true))
        {
            UnityEngine.Object.Destroy(tex);
            return null;
        }
        return tex;
    }

    /// <summary>The manifest carries the tuning that the boot window otherwise has no way to
    /// know: this session's numbers have just been read off the live <c>LoadingScreen</c>, so the
    /// persisted spinner rotates in the same direction, at the same step, with the same glow.</summary>
    private string BuildIconManifest(string identity, LayerArt baseArt, LayerArt overlayArt)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("# GloomhavenVR — a copy of the GAME's own loading symbol, taken once so the boot window\n");
        sb.Append("# (intro over, Gloomhaven_unified still loading) can show it. Delete these three files to\n");
        sb.Append("# have the mod re-take them on the next load. Nothing here is user tuning.\n");
        sb.Append("v=1\n");
        sb.Append("id=").Append(identity).Append('\n');
        Append(sb, "spin", _spinDegrees);
        Append(sb, "step", _stepSeconds);
        Append(sb, "glow", _glowStep);
        Append(sb, "minalpha", _minAlpha);
        sb.Append("basetint=").Append(FormatColor(baseArt.Tint)).Append('\n');
        sb.Append("overlaytint=").Append(FormatColor(overlayArt.Tint)).Append('\n');
        Append(sb, "overlayratio", overlayArt.SizeRatio);
        return sb.ToString();

        static void Append(System.Text.StringBuilder b, string key, float value) =>
            b.Append(key).Append('=').Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string FormatColor(Color c) => string.Format(CultureInfo.InvariantCulture,
        "{0:R},{1:R},{2:R},{3:R}", c.r, c.g, c.b, c.a);

    private static Dictionary<string, string> ParseManifest(string[] lines)
    {
        var kv = new Dictionary<string, string>(12, StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrEmpty(line) || line[0] == '#')
                continue;
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return kv;
    }

    private static float ParseFloat(Dictionary<string, string> kv, string key, float fallback) =>
        kv.TryGetValue(key, out string? s) &&
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) &&
        !float.IsNaN(v) && !float.IsInfinity(v)
            ? v
            : fallback;

    private static Color ParseColor(Dictionary<string, string> kv, string key, Color fallback)
    {
        if (!kv.TryGetValue(key, out string? s))
            return fallback;
        string[] parts = s.Split(',');
        if (parts.Length != 4)
            return fallback;
        var c = new Color();
        for (int i = 0; i < 4; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ||
                float.IsNaN(v) || float.IsInfinity(v))
                return fallback;
            c[i] = Mathf.Clamp01(v);
        }
        return c;
    }

    private static float PositiveOr(float value, float fallback) =>
        (value > 0f && !float.IsNaN(value) && !float.IsInfinity(value)) ? value : fallback;

    private static string DescribeTex(LayerArt art) =>
        art.Tex != null ? $"{art.Tex.name} {art.Uv}" : "procedural";

    /// <summary>One icon layer from its uGUI object: sprite texture + atlas sub-rect + tint +
    /// authored rect size (aspect and size relative to the base icon).</summary>
    private static LayerArt? ReadLayer(GameObject? iconGo, GameObject? baseGo)
    {
        if (iconGo == null || baseGo == null)
            return null;
        Image? image = iconGo.GetComponent<Image>();
        Sprite? sprite = image != null ? image.sprite : null;
        Texture2D? tex = sprite != null ? sprite.texture : null;
        if (image == null || sprite == null || tex == null)
            return null;

        Rect tr = sprite.textureRect; // throws for tight-packed sprites → caller's catch → fallback
        var art = new LayerArt
        {
            Tex = tex,
            Uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height),
            Tint = image.color,
            Width = Mathf.Max(1f, tr.width),
            Height = Mathf.Max(1f, tr.height),
        };
        // Relative size from the authored uGUI rects (overlay may be larger/smaller than base).
        var iconRect = iconGo.GetComponent<RectTransform>();
        var baseRect = baseGo.GetComponent<RectTransform>();
        if (iconRect != null && baseRect != null && baseRect.rect.width > 1f)
            art.SizeRatio = Mathf.Clamp(iconRect.rect.width / baseRect.rect.width, 0.25f, 4f);
        return art;
    }

    private static float ReadTuning(LoadingScreen ls, string field, float fallback)
    {
        float v = ReadTuningRaw(ls, field);
        return (v > 0f && !float.IsNaN(v) && !float.IsInfinity(v)) ? v : fallback;
    }

    private static float ReadTuningRaw(LoadingScreen ls, string field)
    {
        object? v = AccessTools.Field(typeof(LoadingScreen), field)?.GetValue(ls);
        return v is float f ? f : float.NaN;
    }

    /// <summary>Procedural stand-in: base = faint full ring, overlay = angular-gradient arc
    /// (so the stepped rotation stays visible and the glow pulse has something to pulse).</summary>
    private void BuildFallbackArt()
    {
        _artIsProvisional = true;
        DestroyObj(ref _fallbackBaseTex);
        DestroyObj(ref _fallbackOverlayTex);
        _fallbackBaseTex = BuildRingTexture(arcGradient: false);
        _fallbackOverlayTex = BuildRingTexture(arcGradient: true);
        var tint = new Color(0.85f, 0.80f, 0.70f, 1f); // parchment-ish, matches the mod's neutral art
        _baseArt = new LayerArt { Tex = _fallbackBaseTex, Tint = new Color(tint.r, tint.g, tint.b, 0.45f) };
        _overlayArt = new LayerArt { Tex = _fallbackOverlayTex, Tint = tint };
    }

    private static Texture2D BuildRingTexture(bool arcGradient)
    {
        const int size = 128;
        const float outer = 60f, inner = 44f, soft = 2.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = arcGradient ? "GloomhavenVR.LoadingRingArc" : "GloomhavenVR.LoadingRing",
        };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Soft-edged annulus.
                float a = Mathf.Clamp01((outer - r) / soft) * Mathf.Clamp01((r - inner) / soft);
                if (arcGradient && a > 0f)
                {
                    // Alpha ramps around the circle → a comet-like arc whose stepped
                    // rotation is unmistakable even at a glance.
                    float t = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI)) + 0.5f; // 0..1 around
                    a *= t * t;
                }
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return tex;
    }

    // ---- AnimateIcon replica ---------------------------------------------------------------

    /// <summary>
    /// Replicates <c>LoadingScreen.AnimateIcon()</c> on unscaled time: one discrete step —
    /// rotate BOTH quads by <c>m_IconSpinSpeed</c> about local Z and pulse the overlay alpha
    /// by <c>m_IconGlowSpeed</c> — every <c>m_IconUpdateSpeed</c> seconds (accumulated; the
    /// game's Chronos wait produces the same discrete-step look). A spike frame consumes at
    /// most <see cref="MaxStepsPerFrame"/> steps and drops the rest of the backlog.
    /// </summary>
    private void Animate()
    {
        if (_baseQuad == null || _overlayQuad == null)
            return;
        _accum += Time.unscaledDeltaTime;
        int steps = 0;
        while (_accum >= _stepSeconds && steps++ < MaxStepsPerFrame)
        {
            _accum -= _stepSeconds;
            _baseQuad.Rotate(0f, 0f, _spinDegrees, Space.Self);
            _overlayQuad.Rotate(0f, 0f, _spinDegrees, Space.Self);
            if (_overlayAlpha >= 1f)
                _increaseAlpha = false;
            else if (_overlayAlpha <= _minAlpha)
                _increaseAlpha = true;
            _overlayAlpha = _increaseAlpha
                ? Mathf.Min(1f, _overlayAlpha + _glowStep)
                : Mathf.Max(_minAlpha, _overlayAlpha - _glowStep);
        }
        if (_accum >= _stepSeconds)
            _accum %= _stepSeconds; // spike frame: drop the backlog, keep the phase
        ApplyOverlayAlpha();
    }

    /// <summary>Writes both layers' alpha: the authored tint × (overlay only) the glow pulse ×
    /// the appear/disappear ramp. The ramp is a factor, never a replacement — the game's own
    /// pulse keeps running while the spinner fades in or out.</summary>
    private void ApplyOverlayAlpha()
    {
        if (_baseMaterial != null && _baseArt != null)
        {
            Color baseTint = _baseArt.Tint;
            _baseMaterial.color = new Color(baseTint.r, baseTint.g, baseTint.b, baseTint.a * _fade);
        }
        if (_overlayMaterial != null && _overlayArt != null)
        {
            Color tint = _overlayArt.Tint;
            _overlayMaterial.color = new Color(tint.r, tint.g, tint.b, tint.a * _overlayAlpha * _fade);
        }
    }
}

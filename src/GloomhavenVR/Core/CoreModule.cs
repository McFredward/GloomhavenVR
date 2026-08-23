using System.Runtime.CompilerServices;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// XR bootstrap: runtime-dependency loading, OpenXR init (pre-flight, runtime
/// failover, diagnostics) and clean teardown. Sets <see cref="VRSession.IsRunning"/>
/// which every other module keys off. While VR runs it also hosts the
/// <see cref="VRHeartbeat"/> on the mod's own hardened root GO (freeze diagnosis
/// net — hardware test #4).
/// </summary>
internal sealed class CoreModule : IVRModule
{
    public string Name => "Core";

    /// <summary>True once RuntimeDeps are loaded — gate for JITing XR-typed methods.</summary>
    private static bool _depsLoaded;

    private GameObject? _hostGo;

    public void Init()
    {
        // Localization helper: subscribe to the engine's language-change event and fan out
        // to Loc.OnChanged so all mod-authored VR text follows the game's language live.
        // Runs regardless of VR availability (safe before I2 is loaded) so it is always
        // torn down cleanly on hot-reload.
        Loc.Init();

        // Give logged exceptions their stack back. FIRST, before any other module can throw:
        // the game switches every stack trace off at boot, which is why a mod throw and a game
        // throw have been indistinguishable single anonymous lines in Player.log. See
        // ExceptionTraces for the full evidence chain. Runs regardless of VR availability.
        ExceptionTraces.Install();

        // Order matters: nothing referencing Unity.XR.* types may be JIT-compiled
        // before LoadAll() has put those assemblies into the AppDomain. Init() itself
        // only *calls* the (non-inlined) methods that use them.
        if (!RuntimeDepsLoader.LoadAll())
        {
            VRLog.Warn(Name, "VR unavailable this session (RuntimeDeps missing) — game continues flat.");
            return;
        }

        _depsLoaded = true;
        StartVR();

        // Own hardened root (same pattern as every other driver host): the heartbeat and the
        // performance monitor must outlive scene sweeps and never ride a game-visible GO.
        // Created UNCONDITIONALLY (it used to be gated on VRSession.IsRunning) because the
        // performance instrumentation is worth having on the flat screen too — a session that
        // never reached VR is exactly the one whose frame times we want in the log.
        _hostGo = new GameObject("GloomhavenVR.Core");
        Object.DontDestroyOnLoad(_hostGo);
        _hostGo.hideFlags = HideFlags.HideAndDontSave;

        // Frame-pacing instrumentation (2026-07 perf pass). Cheap and self-disabling: with
        // [Perf] Enabled = false its host costs one bool test per frame and nothing else.
        PerfMonitor.Install(_hostGo);

        // ModBuild 228. Installed on the SAME unconditional host and for the same reason: the
        // flat-screen session is the reference reading the VR one has to be judged against, and its
        // two behaviour dials gate themselves on VRSession.IsRunning rather than on this call site,
        // so a session that never reached VR stays the vanilla game.
        AutoLod.Install(_hostGo);
        // The grep list is exhaustive on purpose. Every hardware round of this project starts with
        // somebody being told which string to search a 9 MB Player.log for, and a line nobody knows
        // the name of is a line nobody reads — [Perf] SCENE spent five ModBuilds switched off while
        // three separate analyses cited what it "would" have said.
        VRLog.Info(Name, "Performance monitor installed — grep the log for '[Perf] FRAME' "
                         + "(pacing/GC/XR summary), '[Perf] STEPS' (mod subsystems ranked by cost), "
                         + "'[Perf] SPIKE' (individual over-budget frames), '[Perf] SPLIT' (which "
                         + "LAYER owns the frame: logic vs render loop vs blocked, per camera, now "
                         + "with each camera's figure broken into CULL and SUBMIT), '[Perf] SIM' "
                         + "(what the MAIN-THREAD LOOP iterates over: the length of Unity's "
                         + "Update/LateUpdate lists, the heaviest ticking behaviour TYPES by "
                         + "instance count, and the Animator/ParticleSystem census by cullingMode), "
                         + "'[Perf] SCENE' (what the RENDER LOOP is asked to submit, by root, "
                         + "layer, renderer kind, shader and material), '[Perf] TEX' (why a surface "
                         + "looks soft up close: masterTextureLimit, anisotropic filtering, texture "
                         + "streaming, and the source texels behind every renderer that fills the "
                         + "view, as texels per rendered pixel) and '[Perf] GFX' (the render "
                         + "state that multiplies it, plus the command buffers attached to the head "
                         + "camera). The last four are ON by default from ModBuild 227 and each "
                         + "prints its own measured cost. Configure in dev.gloomhavenvr.perf.cfg or "
                         + "under Einstellungen › Grafik.");
        VRLog.Info(Name, "LOD instrument installed — grep the log for '[Perf] LOD'. One line per "
                         + "scene answering WHAT DECIDES THE LEVEL OF DETAIL: how many AutomaticLOD "
                         + "behaviours exist (2560 of the 2986 entries on Unity's Update list in the "
                         + "ModBuild 227 reading), how many of them take Update's first-line "
                         + "early-out and therefore never read a camera at all, which camera each "
                         + "one WOULD resolve against, how many LODGroups exist and — the part worth "
                         + "the most — the distribution of levels the scene is actually running at "
                         + "for the VR head camera, with the head camera's FOV printed twice (the "
                         + "property and the value derived from the projection matrix, which for an "
                         + "XR camera routinely disagree) beside the flat camera's, and the lodBias "
                         + "that would cancel the difference. It also prints "
                         + "QualitySettings.masterTextureLimit, the COMPETING mechanism for blurry "
                         + "surfaces, which no line in this log has ever carried. Two dials sit "
                         + "beside it in dev.gloomhavenvr.perf.cfg: [Optimize] AutomaticLodIdleSkip "
                         + "(ON — takes the provably-inert behaviours off the Update list) and "
                         + "[Optimize] LodBias (0 = change nothing).");

        if (VRSession.IsRunning)
        {
            _hostGo.AddComponent<VRHeartbeat>();
            _hostGo.AddComponent<VRPresenceWatch>();
            VRLog.Info(Name, "Heartbeat installed — one [Core] status line every 10 s " +
                             "(frames, rig driver, head pose, display/input subsystems, tracking). " +
                             "Presence watch installed — HMD doff/don raises the session-resume " +
                             "recovery sweep (test #17).");
        }
    }

    public void Shutdown()
    {
        Loc.Dispose(); // detach the engine localization event (hot-reload teardown)
        ExceptionTraces.Shutdown(); // puts the game's own StackTraceLogType.None straight back
        PerfMonitor.Shutdown(); // drop the sampling host + every step record before the GO dies
        AutoLod.Shutdown();     // re-enable every AutomaticLOD it disabled, restore lodBias

        if (_hostGo != null)
        {
            Object.Destroy(_hostGo);
            _hostGo = null;
        }
        if (_depsLoaded)
            StopVR();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void StartVR()
    {
        string? runtimeOverride = Plugin.RuntimeOverride.Value;
        if (string.IsNullOrWhiteSpace(runtimeOverride))
            runtimeOverride = null;

        if (!OpenXRBootstrap.Start(runtimeOverride, Plugin.RuntimePriority.Value, Plugin.SkipRuntimeCandidates.Value))
            VRLog.Warn(Name, "VR unavailable this session — game continues flat.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StopVR() => OpenXRBootstrap.Stop();
}

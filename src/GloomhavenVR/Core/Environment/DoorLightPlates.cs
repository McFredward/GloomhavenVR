using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE "SCHWEBENDE LEUCHTENDE VIERECKE" (user, 2026-08-25: <i>"Ich möchte, dass du die
/// fliegenden leuchtenden Vierecke im gesamten Spiel (nicht nur in den Szenario) unsichtbar
/// machst (aber ohne ihr Licht zu entfernen)"</i>; photographs
/// <c>.planning/debug/schwebende_lichter.jpg</c>, <c>walls_gone.jpg</c>).
///
/// <para><b>WHAT THEY ARE — identified by geometry, from two independent viewpoints.</b> They
/// are the drawn quads of the game's own <c>Door_Light_Front_Mesh</c> /
/// <c>Door_Light_Back_Mesh</c> renderers, which hang under every
/// <see cref="UnityGameEditorDoorProp"/> (the crypt variant keeps the bare name, the outpost
/// variant is <c>CR_OS_Door_Light_*_Mesh</c>). The shipped GATE DUMP measured that renderer's
/// bounds on hardware as a paper-thin vertical plane <b>2.069 wu wide x 0.257 wu tall x 0.008
/// wu thick</b>, centred at world y = 1.67 — one plane on the front face of the door frame and
/// one on the back (LogOutput.log, <c>GATE DUMP A[0.3]/A[0.4]/A[2.3]/A[2.4]</c>; the same box
/// appears rotated 60 degrees as <c>s(1.041,0.257,1.796)</c>, which reconstructs to the same
/// 2.069 x 0.257 x 0.008 exactly).</para>
///
/// <para>THE MEASUREMENT THAT NAMES THEM. In <c>schwebende_lichter.jpg</c> the three pale
/// rectangles have connected-component boxes 29x75, 90x104 and 53x119 px and the trio spans
/// x 1626..2493 = 867 px; the door leaf <c>CR_ST_Door_01_Left</c> is 2.708 wu tall in the same
/// frame. Pinning the plate HEIGHT to the measured 0.257 wu gives 292-463 px/wu across the
/// trio's depth range, so the trio's world span is between 1.87 and 2.97 wu. In
/// <c>walls_gone.jpg</c> — a different session, a different viewpoint, the masonry dissolved —
/// the same trio measures ~28 px tall and ~257 px across at ~109 px/wu, i.e. 0.257 wu tall and
/// 2.36 wu across. <b>Both photographs put a row of small coplanar quads, each ~0.25 wu wide
/// and 0.257 wu tall, spanning ~2.1 wu at door-light height.</b> That is the
/// <c>Door_Light_*_Mesh</c> bounding box, filled the way a row of separate little plates fills
/// it. Nothing else in the 5803-renderer census has that footprint.</para>
///
/// <para>THIS CORRECTS ModBuild 259, which dismissed the same suspect for being "an 8:1 strip
/// 0.257 wu tall" while "the photographed regions are taller than wide". Both halves are true
/// and the conclusion does not follow: 8:1 is the <b>union</b> of the drawn quads, and the
/// individual quads inside it are taller than wide. The height — the one dimension a union of
/// coplanar quads in a row reports faithfully — matched all along.</para>
///
/// <para><b>WHY THEY LOOK LIKE THAT, AND WHY THE MOD IS NOT THE CAUSE.</b> The plates draw with
/// <c>Amp_Basic_N_MRAO</c> / <c>PR_CR_Door_Double_04_Mat</c> at queue 2000 — an ordinary
/// OPAQUE LIT material, not an additive glow. Three consequences, each of which the
/// photographs show:</para>
/// <list type="bullet">
/// <item>They sit centimetres from the door's own point light, so at that distance the falloff
///   blows a lit surface to near-white with a hotspot inside a hard mesh edge. Pale, flat,
///   hard-edged — the photograph.</item>
/// <item>The shader has no WallFade variant (the frame beside them uses
///   <c>Amp_Basic_WallFade</c>), so the wall system cannot reach them: <i>"dauerhaft so egal
///   was ein oder ausgeblendet wird"</i> is predicted, not mysterious.</item>
/// <item><i>"Ich kann mich nicht erinnern, dass es flat sowas gab"</i> needs no mod defect: a
///   0.25 wu plate is a couple of pixels under the flat game's top-down camera and a
///   hand's-breadth away in VR.</item>
/// </list>
/// <para>The census never found them across fifteen rounds because its own subject band
/// REMOVED them: they are lit, and the band admits only cards that "do not owe their
/// brightness to the room's lights". They are printed in every gate dump, marked
/// <c>out-of-band</c>. <see cref="MaterialLoaderHeal"/>'s write ledger independently acquits
/// the mod — 596 renderers written that session, 0 of them named <c>*door_light*</c>, with all
/// 12 that exist under the 6 registered door props reached by the scan.</para>
///
/// <para><b>THE RULE.</b> A renderer that sits under a <see cref="UnityGameEditorDoorProp"/>
/// and whose name contains <c>door_light</c> (ordinal, case-insensitive) has
/// <c>Renderer.enabled = false</c>. Nothing else is written — never a <see cref="Light"/>
/// (standing project ruling, and literally the user's condition), never a material, never a
/// property block, never the door leaves, never a ParticleSystem. An opaque lit plate emits no
/// photons in this renderer, so hiding it removes the rectangle and changes no illumination at
/// all: every light in the door prop keeps shining and the pool of light it casts on the door
/// is untouched.</para>
///
/// <para><b>WHAT IT MIGHT OVER-CATCH.</b> Exactly the door lights, and only them: the whole
/// 5803-renderer hardware census contains four names matching the fragment —
/// <c>Door_Light_Front_Mesh</c>, <c>Door_Light_Back_Mesh</c> and their <c>CR_OS_</c> outpost
/// twins. The candle glow the user has NOT complained about
/// (<c>CR_GE_Candle_V1/CandlePivot/CandleFlame/Glow</c>), the wall torches and every particle
/// effect are outside the door-prop subtree and outside the name, so none of them can be
/// reached. The one real cost is intentional: if the game meant these plates as a visual cue
/// that a doorway is lit or passable, that cue goes with them. The user asked for the squares
/// to disappear game-wide and this is the smallest write that does it.</para>
///
/// <para><b>COVERAGE.</b> <see cref="SceneRegistry.DoorProps"/> is filled by a Harmony postfix
/// on <c>UnityGameEditorDoorProp</c>'s own lifecycle method plus a
/// <c>FindObjectsOfType(includeInactive: true)</c> seed at install, so it holds every door prop
/// in every loaded scene — scenario, outpost, map room — not just scenarios. Installed from
/// <see cref="MaterialLoaderHeal.Install"/>, which <c>CompatModule.Init</c> runs once per VR
/// session and which survives scene loads (<c>DontDestroyOnLoad</c>). What it cannot reach: a
/// door-light renderer that is NOT under a <c>UnityGameEditorDoorProp</c> (the ledger's
/// independent population count says there are none: 12 of 12), and anything drawn while the
/// mod is not running.</para>
///
/// <para><b>COST.</b> Per tick (2 Hz): one <c>Collect</c> over a ~10-entry registry — never a
/// scene sweep, this repo's default suspect — plus, only for door props not yet resolved or
/// due a 5 s re-resolve, one <c>GetComponentsInChildren</c> over that prop's ~20 renderers with
/// one <c>string.IndexOf</c> each. Steady state is a dozen <c>Renderer.enabled</c> reads. The
/// re-resolve exists because a door prop's "Generated Content" is built late.</para>
///
/// <para><b>MP.</b> Local presentation only. No wire field, no networked state, no game state:
/// every client hides its own plates and a client without the mod is unaffected. REVERSIBLE:
/// every renderer this file disabled is re-enabled by <see cref="Uninstall"/>.</para>
/// </summary>
internal static class DoorLightPlates
{
    private const string Name = "Core";
    private const string DriverName = "GloomhavenVR.DoorLightPlates";

    /// <summary>Renderer-name fragment that identifies a door-light plate (ordinal,
    /// case-insensitive). Matches both <c>Door_Light_Front_Mesh</c> and
    /// <c>CR_OS_Door_Light_Back_Mesh</c>. Deliberately the SAME literal
    /// <see cref="MaterialLoaderHeal"/>'s ledger watched, so the file that acquitted the mod
    /// and the file that hides the plates cannot disagree about what a door light is.</summary>
    internal const string PlateNameFragment = "door_light";

    private static PlateDriver? _driver;

    /// <summary>Arm the suppression (idempotent). No-op when VR isn't running — the flat game
    /// draws these plates a couple of pixels wide and nobody has complained about them.</summary>
    internal static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<PlateDriver>();
        VRLog.Info(Name,
            "DoorLightPlates installed — the door props' own 'Door_Light_*_Mesh' plates "
            + "(2.069 x 0.257 wu of opaque LIT geometry a few cm from the door's light, which "
            + "is why they read as flat pale rectangles) have their RENDERER switched off "
            + "game-wide. No Light, material or property block is touched, so the door keeps "
            + "every photon it casts.");
    }

    /// <summary>Drop the driver and put back every renderer this file switched off.</summary>
    internal static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.RestoreAll(); }
        catch (System.Exception e) { VRLog.Warn(Name, $"DoorLightPlates: restore threw: {e.Message}"); }
        try { Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private sealed class PlateDriver : MonoBehaviour
    {
        /// <summary>How often the registry is re-read. Fast enough that a door prop which
        /// activates mid-frame is dark within half a second, cheap enough that the steady
        /// state is a dozen bool reads.</summary>
        private const float TickSeconds = 0.5f;

        /// <summary>How often a door prop's subtree is re-walked. A prop's "Generated Content"
        /// is instantiated after the prop itself, so a single resolve at first sight would
        /// miss the plates on a room that opens later.</summary>
        private const float ReresolveSeconds = 5f;

        /// <summary>Re-disables of ONE renderer before the log says so. A door light that keeps
        /// coming back means something in the game re-enables it every frame, and this file
        /// must not get into a write war with it (project ruling: concede the flag).</summary>
        private const int WriteWarThreshold = 40;

        private readonly List<UnityGameEditorDoorProp> _props = new(16);
        private readonly List<Renderer> _subtree = new(64);

        /// <summary>Plates found per door prop, by the prop's instance id.</summary>
        private readonly Dictionary<int, List<Renderer>> _plates = new();

        /// <summary>Every renderer this driver switched off — the restore list.</summary>
        private readonly List<Renderer> _hidden = new(32);
        private readonly HashSet<int> _hiddenIds = new();

        /// <summary>How often each renderer had to be switched off again.</summary>
        private readonly Dictionary<int, int> _rewrites = new();

        private float _nextTick;
        private float _nextResolve;
        private int _reported;
        private bool _warnedWriteWar;
        private System.Action? _tick;

        private void Awake() => _tick = Tick; // cached delegate — TickGuard hot-path contract

        /// <summary>Routed through <see cref="TickGuard"/> like every other module driver: an
        /// unguarded throw in Update would starve the components after it on Unity's list, and
        /// this way the step is also named and timed in the <c>[Perf] STEPS</c> line.</summary>
        private void Update() => TickGuard.Run("Core.DoorLightPlates", _tick!, Name);

        private void Tick()
        {
            if (!VRSession.IsRunning)
                return;
            float now = Time.unscaledTime;
            if (now < _nextTick)
                return;
            _nextTick = now + TickSeconds;

            bool resolve = now >= _nextResolve;
            if (resolve)
                _nextResolve = now + ReresolveSeconds;

            Sweep(resolve);
        }

        private void Sweep(bool resolve)
        {
            SceneRegistry.DoorProps.Collect(_props);
            int hiddenThisTick = 0;
            foreach (UnityGameEditorDoorProp prop in _props)
            {
                if (prop == null)
                    continue;
                int propId = prop.GetInstanceID();
                bool resolveThis = resolve;
                if (!_plates.TryGetValue(propId, out List<Renderer> plates))
                {
                    plates = new List<Renderer>(4);
                    _plates[propId] = plates;
                    resolveThis = true; // first sight of this prop
                }
                if (resolveThis)
                    Resolve(prop, plates);

                for (int i = plates.Count - 1; i >= 0; i--)
                {
                    Renderer r = plates[i];
                    if (r == null)
                    {
                        plates.RemoveAt(i);
                        continue;
                    }
                    if (!r.enabled)
                        continue;
                    r.enabled = false;
                    hiddenThisTick++;
                    NoteHidden(r);
                }
            }
            if (hiddenThisTick > 0 && _reported < 3)
            {
                _reported++;
                VRLog.Info(Name,
                    $"DoorLightPlates: hid {hiddenThisTick} door-light plate renderer(s) across "
                    + $"{_props.Count} door prop(s) — Renderer.enabled only; no Light, material or "
                    + "property block was written.");
            }
        }

        /// <summary>Walk one door prop's subtree (includeInactive: the plates of an unrevealed
        /// room are inactive now and drawn later) and cache the renderers that match.</summary>
        private void Resolve(UnityGameEditorDoorProp prop, List<Renderer> plates)
        {
            plates.Clear();
            _subtree.Clear();
            prop.transform.GetComponentsInChildren(includeInactive: true, _subtree);
            foreach (Renderer r in _subtree)
            {
                if (r == null)
                    continue;
                if (r.name.IndexOf(PlateNameFragment, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                plates.Add(r);
            }
        }

        private void NoteHidden(Renderer r)
        {
            int id = r.GetInstanceID();
            if (_hiddenIds.Add(id))
            {
                _hidden.Add(r);
                return;
            }
            // Already hidden once and enabled again by somebody else.
            _rewrites.TryGetValue(id, out int n);
            n++;
            _rewrites[id] = n;
            if (n >= WriteWarThreshold && !_warnedWriteWar)
            {
                _warnedWriteWar = true;
                VRLog.Warn(Name,
                    $"DoorLightPlates: renderer '{r.name}' has been re-enabled by the game "
                    + $"{n} times, so this file is in a write war with a live writer rather "
                    + "than making a one-off correction. The plate will flicker; the fix is to "
                    + "find that writer, not to tick faster.");
            }
        }

        internal void RestoreAll()
        {
            foreach (Renderer r in _hidden)
            {
                if (r != null)
                    r.enabled = true;
            }
            _hidden.Clear();
            _hiddenIds.Clear();
            _plates.Clear();
            _rewrites.Clear();
        }
    }
}

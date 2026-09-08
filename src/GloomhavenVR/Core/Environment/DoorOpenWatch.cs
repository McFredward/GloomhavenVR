using System.Collections.Generic;
using System.Text;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE OPENED DOOR WHOSE LEAF STAYS — the instrument and the two remedies (user report
/// 2026-09-03, runde_tür_problem.jpg: <i>"Die runde Tür verschwindet nicht mehr wenn man die Tür
/// regulär öffnet. Gameplay-technisch funktioniert alles und das Hex wird begehbar, aber das
/// Asset verschwindet nicht mehr"</i>).
///
/// <para><b>WHAT THE FLAT GAME DOES (decompiled, 2026-09-03).</b> <c>Choreographer.OpenDoor</c>
/// (Choreographer.cs:13305) plays the state <c>"Open"</c> on the door's animator —
/// <c>MF.GetGameObjectAnimator(doorGO)</c>, the first <c>Animator</c> with a controller under the
/// door prop root <c>'ThickDoor : (guid)'</c>. That animator sits on the Apparance-PLACED leaf
/// assembly under <c>HexDoor(Clone)/Generated Content/</c> (the ModBuild 396-405 dumps in
/// Board/FigureGrab/ActorPropBody.cs). If there is no animator yet, the play is deferred ONCE
/// through <c>ProceduralProp.PlacementCompleteAction</c>; nothing in the game ever re-plays it.
/// The rules side is <c>CObjectDoor.DoorIsOpen</c> (the path node's <c>IsBridgeOpen</c>) — that is
/// why the hex is walkable while the picture is wrong: the rules moved on, the picture did not.</para>
///
/// <para><b>WHAT THE 21:58 LOG (ModBuild 413) PROVES AND DOES NOT PROVE.</b> No mod writer names a
/// door-leaf renderer anywhere in 21 679 lines: every wall-fade lane refuses the whole scenario-door
/// assembly (the DOORWAYS census, "SCENARIO DOOR (never fades)", <c>blk 0/0</c>), the FIGURE-GUARD
/// drops renderers under an Animator before adoption, and <c>MaterialLoaderHeal</c> skips the
/// <c>UnityGameEditorDoorProp</c> subtree on every path (its "re-enabled foreign-disabled" lines
/// name only tables, candles and floor halves). So the log cannot say WHICH of the two remaining
/// mechanisms took the leaf back, and both are consistent with the photograph (the disc stands at
/// its CLOSED pose, not at any end pose of a clip):</para>
/// <list type="number">
///   <item><b>The content was rebuilt after the open.</b> <c>ApparanceEntity</c> content is
///   destroyed and re-instantiated on every refresh (bounds/transform changes via
///   <c>MonitorMovement</c>, a subtree going inactive→active, the engine's detail focus — see
///   Board/FigureGrab/GrabbableProp.FreezeApparance and Core/Water/WaterTerrainVR.cs). A fresh
///   leaf carries a fresh animator in its DEFAULT (closed) state, and the game's one deferred
///   replay was spent long ago. The door then reads open in the rules and closed on screen.</item>
///   <item><b>The animator never advanced.</b> The prop SMBs latch <c>animator.speed =
///   Timekeeper.instance.m_GlobalClock.timeScale</c> ONCE in <c>OnStateEnter</c>
///   (DelayedDestroySMB.cs:63, DelayedDeactivatePropAnimSMB.cs:58) and never refresh it; a state
///   entered while the clock is paused keeps speed 0 forever (Board/FigureGrab/PropAnimWatch.cs
///   documents the same latch on the chest).</item>
/// </list>
///
/// <para><b>THE REMEDIES, each gated on its own observed condition and each logged when it fires,
/// so the next log names the mechanism instead of guessing it.</b></para>
/// <list type="bullet">
///   <item>RE-ASSERT: a door the rules say is open whose current animator instance has never been
///   seen in <c>"Open"</c> — a replaced instance (rebuild) or a lost deferral — gets the game's own
///   call, <c>MF.GameObjectAnimatorPlay(root, "Open")</c>, exactly what <c>OpenDoor</c>'s deferred
///   <c>PlacementCompleteAction</c> would have done. Never while the door is in the
///   Choreographer's <c>DoorsUnlocking</c> list, never mid-transition, at most once per 2 s and
///   <see cref="ReassertCap"/> times per door (the cap ALERTS: hitting it means the door's content
///   is being rebuilt over and over, which is its own finding).</item>
///   <item>UNLATCH: an animator in <c>"Open"</c> with <c>speed == 0</c> while the global clock runs
///   gets <c>speed = TimeManager.TimeScale</c> — the value the SMB would have latched had it fired
///   one frame later. The flat game has the same latch; a door in that state never opens there
///   either, so this restores the authored intent rather than inventing behaviour.</item>
/// </list>
///
/// <para><b>THE INSTRUMENT.</b> <c>DOOR OPENED</c> fires when a scenario door's RULES state flips
/// to open (read-only, from <c>CObjectDoor.DoorIsOpen</c>, so a cheat's ForceActivate and a regular
/// "Tür öffnen" both count), naming the root, the animator and its state, and the leaf renderers
/// under the game's own animator handle: how many the game has disabled, how many the wall system
/// holds a rule on (<see cref="WallSegmentFade.DescribeHold"/>), how many are still enabled.
/// Three samples follow (+1 s, +3 s, +8 s); the +8 s line's "still enabled" must read 0 once the
/// game's hide has run. A sample that shows <c>"Open"</c> at normalizedTime ≥ 1 with every leaf
/// renderer still enabled and unmoved says the clip hides nothing and the hide lives elsewhere; a
/// sample that shows the animator REPLACED names the rebuild.</para>
///
/// <para><b>THE CAUSE, AS FAR AS IT IS MEASURED (ModBuild 429).</b> The game's ENTIRE
/// door-open behaviour is one call — <c>Choreographer.OpenDoor</c> plays the state "Open" on the
/// door's animator and does nothing else. Two families were open after ModBuild 426: (A) the
/// clip's curve paths do not resolve on this placement, or (B) the write is suppressed or undone.
/// <c>DOOR OPEN CLIP SAMPLE</c> (ModBuild 427) closed (A): on all six doors of the ModBuild 428
/// hardware log the clip <c>Door_02_Open</c> moves the same four transforms — <c>Door_Left</c>,
/// <c>Door_Right</c>, <c>Door_Light_Back</c>, <c>Door_Light_Front</c> — so the paths resolve on
/// every kit that ships. What is left is WHEN, and the ModBuild 428 log answers that too, against
/// the instrument's own verdict: the four doors whose <c>DOOR OPEN PHASES</c> line read
/// "THE ANIMATOR NEVER WRITES" were in state <b>Idle</b> (clip <c>Door_02_Idle</c>, 1.00 s) at the
/// rules flip AND still in Idle at the +1 s sample, and only reached "Open" between +1 s and +3 s.
/// The probe window was 1.5 s. It measured the IDLE clip and reported an animator that never
/// writes — a flawless measurement of the wrong state. Hence <see cref="Watch.ProbeMinSeconds"/>
/// and <see cref="Watch.ProbeMaxSeconds"/>: the window now stays open until the "Open" state has
/// actually run, and both lines print WHICH STATES the window covered, so the verdict can never
/// again be read off a state nobody asked about.</para>
///
/// <para><b>NO PICTURE IS STOOD IN FOR (user ruling, 2026-09-05): <i>"Bitte entferne jegliche
/// workarounds die du eingebaut hattest mit dem deaktivieren und setze wieder voll auf die Logik
/// des Spiels bei allen Türen."</i></b> ModBuild 416-428 carried a stand-in that switched off the
/// door leaf when it had not moved a second after the open. It is gone: nothing in this file
/// writes <c>Renderer.enabled</c> or <c>SetActive</c> on door content, ever. What remains is the
/// game's own machinery (the two remedies above, which only replay calls the game itself makes)
/// plus the belt below, which restores an assumption the flat game was written under. The door's
/// picture is the GAME's from here on, right or wrong, and the instrument says which.</para>
///
/// <para><b>THE BELT — <c>[Compat] DoorAnimateOffscreen</c>, ON by default.</b> It adds no
/// behaviour; it puts back an assumption the flat game was written under. That game's camera looks
/// down on the whole room, so a door it opens is on screen while its clip plays. In VR the camera
/// stands IN the room and the door you just opened is very often behind you — a condition the
/// authors never had to consider, and precisely the one Unity's default cullingMode withholds the
/// transform write for. So the scenario door animators this watch already resolves get
/// <c>AlwaysAnimate</c>, the replaced value is remembered per door and restored on a scenario
/// change, on uninstall, when the animator instance is replaced and the moment the dial goes off.
/// It is belted onto EVERY animator with a controller under the door prop, not only the one
/// <c>MF.GetGameObjectAnimator</c> returns: a door prop root carries two animated subtrees (the
/// Apparance-generated one under <c>Generated Content</c>, and the prefab clone
/// <c>ApparanceLayer.Create</c> parents in at decompiled ApparanceLayer.cs:72) and MF searches
/// with the <c>GetComponentsInChildren</c> overload that SKIPS INACTIVE objects, so which of the
/// two answers moves with the room's visibility state. The game writes <c>cullingMode</c> in
/// exactly zero places (decompiled tree, 0 hits), so there is nobody to fight for it.
/// It demonstrably worked: the user saw a door's open animation for the first time on ModBuild
/// 428 (<i>"Außerdem haben die Türen eine Animation beim aufgehen, das konnte ich jetzt sehen.
/// Das will ich auch sehen!"</i>), so the belt stays ON by default. It is not a workaround — it
/// puts back the vanilla precondition, it writes one enum per door animator, and every replaced
/// value is given back.</para>
///
/// <para><b>NO GAME STATE IS WRITTEN.</b> <c>Animator.Play</c> and <c>Animator.speed</c> are
/// presentation on this client's own copy of the door; the rules state is only read. MULTIPLAYER:
/// every peer runs its own choreography against its own scene — nothing goes on the wire, and a
/// peer without the mod sees exactly what its own game shows it.</para>
///
/// <para><b>COST.</b> One pass over the door registry (≈10 entries) every quarter second; the
/// animator is cached per door and re-resolved (one <c>GetComponentsInChildren</c>) only while it
/// is missing. The leaf walk runs at the open and at the three samples, never per tick.</para>
/// </summary>
internal static class DoorOpenWatch
{
    private const string Name = "Core";
    private const string DriverName = "GloomhavenVR.DoorOpenWatch";

    /// <summary>Re-asserts per door before the watch stops and ALERTS instead. Twelve is far
    /// beyond any legitimate count (one rebuild per open would be one) and small enough that a
    /// rebuild loop cannot turn into a per-second replay for the rest of the scenario.</summary>
    private const int ReassertCap = 12;

    private static Watch? _driver;

    internal static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        var go = new GameObject(DriverName);
        Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<Watch>();
        VRLog.Info(Name,
            "DoorOpenWatch installed — every scenario door's rules state (CObjectDoor.DoorIsOpen) "
            + "is watched; a door that opens logs DOOR OPENED with its leaf renderers and three "
            + "follow-up samples, an open door whose animator instance was never seen in 'Open' "
            + "gets the game's own MF.GameObjectAnimatorPlay(root, \"Open\") replayed, and an 'Open' "
            + "state latched at speed 0 while the clock runs is unlatched.");
    }

    internal static void Uninstall()
    {
        if (_driver == null)
            return;
        try { Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private sealed class Entry
    {
        public UnityGameEditorDoorProp Prop = null!;
        public string RootName = string.Empty;
        public CObjectDoor? Door;
        public float NextDoorResolve;
        public bool SeenOnce;
        public bool WasOpen;
        /// <summary>True when the door was already open the first time the watch saw it (a save,
        /// a hot reload) — no DOOR OPENED line, a longer grace before any re-assert.</summary>
        public bool OpenAtFirstSight;
        public float OpenedAt;
        public Animator? Animator;
        public int AnimatorId;
        public float InstanceSeenAt;
        public float NextAnimatorResolve;
        /// <summary>The CURRENT animator instance has been observed in the 'Open' state.</summary>
        public bool ObservedOpen;
        /// <summary>How many times the animator instance under this door has been REPLACED since
        /// the watch first saw it. This was a write-once <c>bool Rebuilt</c>, which meant every
        /// line printed after the first rebuild read "REPLACED since the open" for the rest of the
        /// scenario whether or not the CURRENT instance was the one from the flip — a field that
        /// tells the truth once and then lies.</summary>
        public int Rebuilds;
        /// <summary>...and how many of those replacements happened since THIS open. Reset at the
        /// flip, so the number beside a DOOR OPENED sample is about the open it is sampling.</summary>
        public int RebuildsSinceOpen;
        /// <summary>The animator instance id as it stood at the flip, so a line can say whether the
        /// instance it is describing IS that one rather than only that something changed once.</summary>
        public int AnimatorIdAtFlip;
        public int Reasserts;
        public float LastReassert;
        public bool NoOpenState;
        public bool CapAlerted;
        public int Unlatched;
        /// <summary>REMEDY C fired on the current animator instance (once per instance).</summary>
        public bool DissolveReasserted;
        public int SampleStage;
        public float NextSample;
        /// <summary>The renderer instance ids under the animator handle at the flip, with their
        /// count and centroid — read by <see cref="Watch.AppendLeafCensus"/> to say how many of
        /// them the game has destroyed since. NOTHING is written to any of them: this watch owns
        /// no renderer's <c>enabled</c> bit at all (ModBuild 429, user ruling 2026-09-05).</summary>
        public readonly List<int> LeafIds = new(16);
        public int LeafCount;
        public Vector3 LeafCenter;
        /// <summary>The clip sample (<see cref="Watch.SampleOpenClip"/>) has run against the
        /// CURRENT animator instance. Once per instance, and it is a measurement, not a remedy.</summary>
        public bool ClipSampled;
        /// <summary>The animator instance id the clip sample ran on, so the CLIP SAMPLE line can
        /// say whether the transforms it names are the ones the frame probe measured.</summary>
        public int ClipSampleAnimatorId;
        // ---- the per-frame probe over the first 0.6 s after the flip (ModBuild 416; widened in
        // ModBuild 427 from ONE renderer's transform to EVERY transform under the animator)
        public bool ProbeArmed;
        public float ProbeUntil;
        public int ProbeFrames;
        public bool ProbeFirst;
        public float DeltaPosMin, DeltaPosMax, DeltaRotMax;
        public Vector3 RootPos0, RootScale0;
        public Quaternion RootRot0;
        public float RootPosDevMax, RootRotDevMax, RootScaleDevMax;
        /// <summary>EVERY transform under the animator, with its local TRS on the probe's first
        /// frame. ModBuild 416 watched <c>LeafRefs[0]</c> alone while the READING sentence claimed
        /// "no transform under the animator changed by a ten-thousandth" — on the CR_ST_Door_02
        /// kit that one renderer was the FRAME, so the two door leaves were never measured at all.</summary>
        public readonly List<Transform> ProbeT = new(32);
        public readonly List<Vector3> ProbePos0 = new(32);
        public readonly List<Quaternion> ProbeRot0 = new(32);
        public readonly List<Vector3> ProbeScale0 = new(32);
        /// <summary>How many transforms the probe actually covered — the number the READING quotes.</summary>
        public int ProbeCovered;
        public float AnyPosDevMax, AnyRotDevMax, AnyScaleDevMax;
        public int WorstPosIdx = -1, WorstRotIdx = -1, WorstScaleIdx = -1;
        public int Transitions;
        public float NtFirst, NtLast;
        public int ClipCountMin, ClipCountMax;
        public bool Bound0, BoundLast, GraphValid0, GraphValidLast;
        // ---- THE THREE-PHASE WRITE-WAR PROBE (ModBuild 428). The ModBuild 416/427 probe read
        // every transform ONCE PER FRAME, from Update — i.e. BEFORE Unity's animation phase. A
        // pose the animator writes after Update and a second writer puts back before the next
        // Update is bit-identical in every one of those samples, so "not one transform changed"
        // was never able to tell "the animator writes nothing" from "the animator writes and
        // someone overwrites it". Three samples in the SAME frame separate them by phase:
        // Update -> [Unity animation] -> LateUpdate -> [render] -> end of frame -> next Update.
        /// <summary>Local TRS of every probe transform as read in <c>Update</c> this frame.</summary>
        public readonly List<Vector3> PhaseUPos = new(32);
        public readonly List<Quaternion> PhaseURot = new(32);
        public readonly List<Vector3> PhaseUScale = new(32);
        /// <summary>...as read in <c>LateUpdate</c>, i.e. AFTER the animation phase wrote.</summary>
        public readonly List<Vector3> PhaseLPos = new(32);
        public readonly List<Quaternion> PhaseLRot = new(32);
        public readonly List<Vector3> PhaseLScale = new(32);
        /// <summary>...as read at <c>WaitForEndOfFrame</c>, after rendering.</summary>
        public readonly List<Vector3> PhaseEPos = new(32);
        public readonly List<Quaternion> PhaseERot = new(32);
        public readonly List<Vector3> PhaseEScale = new(32);
        public bool HaveU, HaveL, HaveE;
        /// <summary>Update -> LateUpdate: what the ANIMATION PHASE wrote.</summary>
        public readonly PhaseDelta UL = new();
        /// <summary>LateUpdate -> end of frame: what ran after every LateUpdate.</summary>
        public readonly PhaseDelta LE = new();
        /// <summary>End of frame -> the NEXT Update: what put the pose back between frames.</summary>
        public readonly PhaseDelta EU = new();
        public int PhaseFramesU, PhaseFramesL, PhaseFramesE;
        // ---- the per-frame VISIBILITY census (ModBuild 428). cullingMode CullUpdateTransforms
        // withholds the transform write while NONE of the animator's renderers is visible, and
        // the state machine keeps advancing — the exact shape of this defect. ModBuild 426 read
        // ONE renderer's isVisible at ONE instant, which settles nothing about the 0.87 s the
        // clip runs.
        public readonly List<Renderer> ProbeR = new(16);
        public int VisMin, VisMax, VisZeroFrames, VisFrames;
        // ---- WHICH STATE THE WINDOW ACTUALLY COVERED (ModBuild 429). The ModBuild 428 window was
        // a flat 1.5 s from the rules flip, and on four of the six hardware doors the animator was
        // still in 'Idle' (clip Door_02_Idle, 1.00 s) for the whole of it — 'Open' was entered
        // between the +1 s and the +3 s sample. The probe therefore measured the IDLE clip and its
        // READING said "THE ANIMATOR NEVER WRITES": a flawless measurement of the wrong stage. So
        // the window now waits for 'Open' (see Watch.ProbeMaxSeconds) and every line prints which
        // states it covered — for the two working doors as much as the four failing ones. A term
        // that reads the same on both sides is not the cause, and saying so in the line is worth
        // as much as finding the one that differs.
        public int StateHash0, StateHashLast, StateChanges;
        public bool SawOpen;
        /// <summary>Seconds after the flip that 'Open' was first seen, or -1 while never seen.</summary>
        public float OpenSeenAfter;
        public int OpenFrames;
        public float OpenNtFirst, OpenNtLast;
        public int ActiveFrames, EnabledFrames;
        public float SpeedMin, SpeedMax;
        public float Weight0Min, Weight0Max;
        public int LayerCount;
        public AnimatorUpdateMode UpdateModeLast;
        public bool RootMotionLast;
        /// <summary>The clip on layer 0 at the first and the last sampled frame, and the weight
        /// band it played at. A clip playing at weight 0 writes nothing and advances normally,
        /// which from the outside looks exactly like a clip that binds nothing.</summary>
        public AnimationClip? Clip0, ClipLast;
        public float ClipWeightMin, ClipWeightMax;
        /// <summary>The animator instance the window ran on, and how long the window actually
        /// lasted — no longer the constant the line used to print.</summary>
        public int ProbeAnimatorId;
        public float ProbeSpan;
        /// <summary>The hard deadline. <see cref="ProbeUntil"/> is now only the MINIMUM.</summary>
        public float ProbeHardUntil;
        // ---- THE BELT (ModBuild 428): AlwaysAnimate on the game's own door animator, and the
        // value it replaced. Restored on a scenario change, on uninstall, when the animator
        // instance is replaced and the moment the dial goes off.
        /// <summary>EVERY animator the belt was written to under this door prop, with the
        /// cullingMode each one had. Not just the one <c>MF.GetGameObjectAnimator</c> resolves: a
        /// door prop root carries TWO animated subtrees — the Apparance-generated one under
        /// <c>Generated Content</c> and the plain prefab clone <c>ApparanceLayer.Create</c>
        /// parents in (decompiled ApparanceLayer.cs:72) — and <c>MF.GetGameObjectAnimator</c>
        /// searches with <c>GetComponentsInChildren&lt;Animator&gt;()</c>, the overload that SKIPS
        /// INACTIVE objects (decompiled MF.cs:135-146). Which of the two it returns therefore
        /// changes with the room's visibility state, so belting only the one resolved right now
        /// can arm the wrong subtree. Belting both costs one enum write each.</summary>
        public readonly List<Animator> BeltOn = new(4);
        public readonly List<AnimatorCullingMode> BeltMode0 = new(4);
        /// <summary>Next time the prop is re-walked for animators (a subtree that was inactive at
        /// the last walk can be active now — <c>ProceduralMapTile.ShowContent</c> deactivates the
        /// whole <c>Generated Content</c> subtree while a room is in Preview visibility).</summary>
        public float NextBeltScan;
        /// <summary>How often the belt has been (re-)asserted on this door. A door whose content is
        /// rebuilt in a loop would otherwise print a line per rebuild; only the first two print.</summary>
        public int BeltWrites;
    }

    /// <summary>The largest per-frame move seen between two PHASES of the same frame, by metric,
    /// with the index of the transform that produced it. Held per entry and reused, so the
    /// per-frame path allocates nothing and the NAME is read once, at log time.</summary>
    private sealed class PhaseDelta
    {
        public float PosMax, RotMax, ScaleMax;
        public int PosIdx = -1, RotIdx = -1, ScaleIdx = -1;

        public void Reset()
        {
            PosMax = RotMax = ScaleMax = 0f;
            PosIdx = RotIdx = ScaleIdx = -1;
        }

        public void Feed(int i, float dpos, float drot, float dscale)
        {
            if (dpos > PosMax) { PosMax = dpos; PosIdx = i; }
            if (drot > RotMax) { RotMax = drot; RotIdx = i; }
            if (dscale > ScaleMax) { ScaleMax = dscale; ScaleIdx = i; }
        }

        /// <summary>The same thresholds the DOOR OPEN FRAMES reading has used since ModBuild 416:
        /// a ten-thousandth of a world unit, or a hundredth of a degree.</summary>
        public bool Moved => PosMax > 1e-4f || RotMax > 0.01f || ScaleMax > 1e-4f;
    }

    private sealed class Watch : MonoBehaviour
    {
        private const float TickSeconds = 0.25f;
        private const float GraceSeconds = 1.5f;
        private const float FirstSightGraceSeconds = 5f;
        private const float ReassertSpacingSeconds = 2f;
        private static readonly float[] SampleAt = { 1f, 3f, 8f };
        /// <summary>Seconds between two belt scans of one door prop. It is not once-and-done
        /// because a door's animated subtree can be INACTIVE when the room is in Preview
        /// visibility (decompiled ProceduralMapTile.ShowContent) and because Apparance destroys
        /// and re-instantiates placed content; two seconds is far inside the reaction time of a
        /// door the player has to walk to, and ten doors cost ten GetComponentsInChildren every
        /// two seconds.</summary>
        private const float BeltScanSeconds = 2f;
        private static readonly int OpenHash = Animator.StringToHash("Open");

        private readonly List<UnityGameEditorDoorProp> _props = new(16);
        private readonly Dictionary<int, Entry> _entries = new(16);
        private readonly List<Renderer> _rendererScratch = new(32);
        private readonly List<int> _deadScratch = new(4);
        private readonly StringBuilder _sb = new(1024);
        // ---- the movable-set measurement's own scratch (ModBuild 427). Separate from
        // _rendererScratch/_sb on purpose: the measurement runs while the log builders are live.
        private readonly List<Renderer> _measureRenderers = new(32);
        private readonly List<bool> _measureEnabled0 = new(32);
        private readonly List<bool> _measureEnabledA = new(32);
        private readonly List<bool> _measureFlipped = new(32);
        private readonly List<Transform> _measureT = new(64);
        private readonly List<Vector3> _measurePos0 = new(64);
        private readonly List<Quaternion> _measureRot0 = new(64);
        private readonly List<Vector3> _measureScale0 = new(64);
        private readonly List<bool> _measureActive0 = new(64);
        private readonly List<Vector3> _measurePosA = new(64);
        private readonly List<Quaternion> _measureRotA = new(64);
        private readonly List<Vector3> _measureScaleA = new(64);
        private readonly List<bool> _measureActiveA = new(64);
        private readonly List<bool> _measureMoved = new(64);
        private readonly List<AnimationClip> _clipScratch = new(8);
        /// <summary>The List overload of <c>Animator.GetCurrentAnimatorClipInfo</c> fills this
        /// one; the array overload allocates a fresh array on every call and the probe reads it
        /// once per frame per open door.</summary>
        private readonly List<AnimatorClipInfo> _clipInfoScratch = new(4);
        /// <summary>The belt's own animator scratch — the walk runs while nothing else is live,
        /// but a shared list would still be a trap the day it does not.</summary>
        private readonly List<Animator> _beltScratch = new(8);
        private readonly StringBuilder _clipSb = new(768);
        private ScenarioState? _state;
        private float _nextTick;
        private System.Action? _tick;
        private System.Action? _late;
        private System.Action? _eof;

        private void Awake()
        {
            // Cached delegates — TickGuard hot-path contract (a method group converted at the call
            // site allocates a delegate every frame).
            _tick = Tick;
            _late = SampleLatePhase;
            _eof = SampleEndOfFramePhase;
        }

        private void OnDestroy()
        {
            // NOTHING is owed on the renderer side any more: this watch switched off its last
            // renderer in ModBuild 428 and owns no `enabled` bit from ModBuild 429 on (user
            // ruling 2026-09-05). The belt IS owed back — one cullingMode enum per door animator.
            try { RestoreAllBelts("watch destroyed"); }
            catch { /* the animators went with the scene */ }
        }

        private int _probing;
        /// <summary>The end-of-frame pump is running (started when the first probe window opens,
        /// stopped by itself when the last one closes).</summary>
        private bool _pumping;
        /// <summary>One instance for the life of the watch — <c>new WaitForEndOfFrame()</c> in a
        /// yield allocates a managed object per frame otherwise.</summary>
        private readonly WaitForEndOfFrame _endOfFrame = new();

        private void Update() => TickGuard.Run("Core.DoorOpenWatch", _tick!, Name);

        /// <summary>The SECOND of the three phase samples: after Unity's animation phase, so this
        /// is where an animator's transform write is visible for the first time. Runs only while a
        /// probe window is open, which is ~1.5 s per door open and never otherwise.</summary>
        private void LateUpdate()
        {
            if (_probing > 0)
                TickGuard.Run("Core.DoorOpenWatch.Late", _late!, Name);
        }

        /// <summary>The THIRD phase sample, after the frame has rendered — the codebase's own
        /// end-of-frame idiom (WorldUIModule.cs, InitiativeTrack.cs). It catches a writer that runs
        /// after every LateUpdate (an onBeforeRender handler, another end-of-frame coroutine).
        /// It stops itself the moment the last window closes, so nothing yields per frame while no
        /// door is opening.</summary>
        private System.Collections.IEnumerator PumpEndOfFrame()
        {
            while (_probing > 0)
            {
                yield return _endOfFrame;
                if (_probing > 0)
                    TickGuard.Run("Core.DoorOpenWatch.EndOfFrame", _eof!, Name);
            }
            _pumping = false;
        }

        private void Tick()
        {
            if (!VRSession.IsRunning)
            {
                // VR went down mid-window. Nothing further will decrement _probing from here, and
                // that counter is what holds the LateUpdate branch and the end-of-frame coroutine
                // open — so close every window rather than leave a pump yielding for the rest of
                // the process.
                if (_probing > 0)
                    CloseAllProbes();
                return;
            }
            float now = Time.unscaledTime;
            if (_probing > 0)
                SampleFrameProbes(now); // every frame while a window is open, ProbeMaxSeconds at worst
            if (now < _nextTick)
                return;
            _nextTick = now + TickSeconds;

            ScenarioState? state = null;
            try { state = ScenarioManager.CurrentScenarioState; }
            catch { /* rule library not ready */ }
            if (!ReferenceEquals(state, _state))
            {
                RestoreAllBelts("scenario changed"); // nothing we wrote on an animator may outlive its scenario
                foreach (KeyValuePair<int, Entry> kv in _entries)
                    CloseProbeSilently(kv.Value); // ...and no probe window may outlive its entry
                _entries.Clear(); // a new scenario: every door is a new door
                _state = state;
            }
            if (state == null)
                return;

            SceneRegistry.DoorProps.Collect(_props);
            _deadScratch.Clear();
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                if (kv.Value.Prop == null)
                    _deadScratch.Add(kv.Key);
            }
            foreach (int dead in _deadScratch)
            {
                if (_entries.TryGetValue(dead, out Entry gone))
                {
                    RestoreBelt(gone, "the door prop is gone"); // no-op once the animator died with it
                    CloseProbeSilently(gone);
                }
                _entries.Remove(dead);
            }

            foreach (UnityGameEditorDoorProp prop in _props)
            {
                if (prop == null)
                    continue;
                int id = prop.GetInstanceID();
                if (!_entries.TryGetValue(id, out Entry e))
                {
                    e = new Entry { Prop = prop, RootName = prop.gameObject.name };
                    _entries[id] = e;
                }
                Step(e, state, now);
            }
        }

        private void Step(Entry e, ScenarioState state, float now)
        {
            if (e.Door == null && now >= e.NextDoorResolve)
            {
                e.NextDoorResolve = now + 5f;
                e.Door = ResolveDoor(e.Prop, state);
            }
            if (e.Door == null)
                return;

            // THE BELT MUST BE ON BEFORE THE OPEN, NOT AFTER IT, so it runs here — ahead of the
            // rules read, for a CLOSED door as much as an open one. Unity decides whether to write
            // an animator's transforms at the moment the clip runs, and a door is closed right up
            // until the frame it is asked to open; arming only doors that are already open would
            // arm every one of them exactly one clip too late.
            ApplyBelt(e, now, force: false);

            bool open;
            try { open = e.Door.DoorIsOpen; }
            catch { open = false; } // PathFinder not built yet — read as closed

            if (!e.SeenOnce)
            {
                e.SeenOnce = true;
                e.WasOpen = open;
                if (open)
                {
                    e.OpenAtFirstSight = true;
                    e.OpenedAt = now;
                    ResolveAnimator(e, now, force: true);
                    e.AnimatorIdAtFlip = e.AnimatorId; // the instance this open is about
                    e.RebuildsSinceOpen = 0;
                    CaptureLeaf(e);
                }
                return;
            }

            if (open && !e.WasOpen)
            {
                e.WasOpen = true;
                e.OpenAtFirstSight = false;
                e.OpenedAt = now;
                e.Reasserts = 0;
                e.Unlatched = 0;
                e.CapAlerted = false;
                e.SampleStage = 0;
                e.NextSample = now + SampleAt[0];
                ResolveAnimator(e, now, force: true);
                e.AnimatorIdAtFlip = e.AnimatorId; // the instance this open is about
                e.RebuildsSinceOpen = 0;
                ApplyBelt(e, now, force: true); // a rebuilt subtree must not wait out the cadence
                CaptureLeaf(e);
                ArmFrameProbe(e, now);
                LogOpened(e, now);
                return;
            }
            if (!open)
            {
                // A scenario restart closes doors again — start over on the next open. Nothing is
                // owed here any more: until ModBuild 428 this branch also restored the renderers
                // the mod had switched off, and the mod switches nothing off from ModBuild 429.
                e.WasOpen = false;
                return; // the belt was already asserted above — it does not need an open door
            }

            // Open in the rules. Keep the picture in step with that.
            ResolveAnimator(e, now, force: false);
            Animator? a = e.Animator;
            if (a != null && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
            {
                AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
                bool isOpen = st.IsName("Open");
                if (isOpen)
                {
                    e.ObservedOpen = true;
                    Unlatch(e, a, st, now);
                    ReassertDissolve(e, a, st, now);
                }
                else
                {
                    Reassert(e, a, st, now);
                }
            }

            // The leaf baseline the census reports against. It is captured at the flip, but a
            // door whose content had not been placed yet has an empty one — HideStuckLeaf used to
            // re-capture it here on its way past, and that write outlives the method.
            if (e.LeafCount == 0 && a != null)
                CaptureLeaf(e);

            // THE CLIP SAMPLE — once per animator instance, and only ONCE THE PHASE WINDOW HAS
            // CLOSED. SampleAnimation WRITES the live hierarchy and writes the recorded original
            // back field by field; running it while the three-phase probe is open would have the
            // instrument measuring its own writes. It answered its question (the clip binds on
            // every kit that ships) but it is the only reading that names the clip and its curve
            // paths against a REBUILT placement, so it still runs once per instance.
            if (!e.ClipSampled && !e.ProbeArmed && a != null && now - e.OpenedAt >= ClipSampleAfterSeconds)
            {
                e.ClipSampled = true;
                e.ClipSampleAnimatorId = e.AnimatorId;
                SampleOpenClip(e, a);
            }

            if (!e.OpenAtFirstSight && e.SampleStage < SampleAt.Length && now >= e.NextSample)
            {
                int stage = e.SampleStage;
                e.SampleStage++;
                if (e.SampleStage < SampleAt.Length)
                    e.NextSample = e.OpenedAt + SampleAt[e.SampleStage];
                LogOpenedSample(e, stage, now);
            }
        }

        // ---------------------------------------------------------------- remedies

        /// <summary>REMEDY A — the game's own deferred replay, generalised to every placement.</summary>
        private void Reassert(Entry e, Animator a, AnimatorStateInfo st, float now)
        {
            if (e.ObservedOpen)
                return; // this instance opened already and moved on — not ours to replay
            float grace = e.OpenAtFirstSight ? FirstSightGraceSeconds : GraceSeconds;
            float since = now - Mathf.Max(e.OpenedAt, e.InstanceSeenAt);
            if (since < grace)
                return;
            if (e.NoOpenState || e.Reasserts >= ReassertCap)
            {
                if (e.Reasserts >= ReassertCap && !e.CapAlerted)
                {
                    e.CapAlerted = true;
                    LogReassertCap(e, st);
                }
                return;
            }
            if (now - e.LastReassert < ReassertSpacingSeconds)
                return;
            if (a.IsInTransition(0))
                return;
            if (IsUnlocking(e.Door))
                return;
            if (!a.HasState(0, OpenHash))
            {
                e.NoOpenState = true;
                LogNoOpenState(e, a, st);
                return;
            }
            e.LastReassert = now;
            e.Reasserts++;
            bool played = false;
            try { played = MF.GameObjectAnimatorPlay(e.Prop.gameObject, "Open"); }
            catch (System.Exception ex)
            {
                VRLog.Warn(Name, $"DoorOpenWatch: replaying 'Open' on '{e.RootName}' threw {ex.GetType().Name}: {ex.Message}");
            }
            LogReasserted(e, a, st, played, now);
        }

        /// <summary>REMEDY B — an 'Open' state latched at speed 0 by an SMB while the clock was paused.</summary>
        private void Unlatch(Entry e, Animator a, AnimatorStateInfo st, float now)
        {
            if (a.speed != 0f)
                return;
            bool paused;
            float scale;
            try { paused = TimeManager.IsPaused; scale = TimeManager.TimeScale; }
            catch { return; }
            if (paused || scale <= 0f)
                return; // the clock IS stopped — speed 0 is the game's intent right now
            if (st.normalizedTime >= 1f)
                return; // the clip finished; a zero speed on a finished clip changes nothing
            a.speed = scale;
            e.Unlatched++;
            LogUnlatched(e, a, st, scale, now);
        }

        private static bool IsUnlocking(CObjectDoor? door)
        {
            if (door == null)
                return false;
            try
            {
                Choreographer? c = Choreographer.s_Choreographer;
                List<CObjectProp>? list = c != null ? c.DoorsUnlocking : null;
                return list != null && list.Contains(door);
            }
            catch { return false; }
        }

        /// <summary>
        /// REMEDY C — the ModBuild 414 reading (LogOutput.log 23:43, lines 5275/5296/5325/5447). The
        /// 'Open' state of 'CV_Door_Destructable_Controller' plays and keeps playing (normalizedTime
        /// 0.29 → 16.84 over 8 s, speed 1, clock running), the mod holds no rule on the one leaf
        /// renderer 'CV_Door_01', nothing disables it and it never moves. So the clip is not the
        /// hide. The hide the game ships for a DESTRUCTIBLE door is the dissolve:
        /// <c>DeathDissolveSMB.OnStateUpdate</c> counts <c>m_InstanceDeathDelay</c> down on the
        /// Chronos clock and calls <c>Die</c> → <c>DeathDissolve.ExternalPlay()</c>
        /// (DeathDissolveSMB.cs:35-46), whose coroutine sets <c>_Toggle_Dissolve = 1</c> and ramps
        /// <c>_Cutout</c> on <c>renderer.materials</c> (DeathDissolve.cs:135-165) — the same
        /// machinery that dissolves a dead figure. That coroutine logs every renderer it takes
        /// (<c>Debug.Log(obj.name)</c>, DeathDissolve.cs:138) and Player.log carries NO 'CV_Door_01'
        /// line after the open at 12943: the dissolve never STARTED. Which term stopped it is what
        /// <see cref="AppendLeafForensics"/> prints; this remedy runs the game's own next step once
        /// the SMB's window has provably passed — the state has looped at least twice or three
        /// seconds went by — and only if a DeathDissolve exists under the animator, has not been
        /// played, and is not in progress. It is the exact call <c>DeathDissolveSMB.Die</c> makes.
        /// Once per animator instance.
        /// </summary>
        private void ReassertDissolve(Entry e, Animator a, AnimatorStateInfo st, float now)
        {
            if (e.DissolveReasserted)
                return;
            if (st.normalizedTime < 2f && now - Mathf.Max(e.OpenedAt, e.InstanceSeenAt) < 3f)
                return;
            if (e.OpenAtFirstSight && now - e.InstanceSeenAt < FirstSightGraceSeconds)
                return;
            DeathDissolve? dd = null;
            try { dd = a.gameObject.GetComponentInChildren<DeathDissolve>(true); }
            catch { /* subtree mid-rebuild */ }
            if (dd == null)
            {
                e.DissolveReasserted = true; // nothing to replay on this door kit — say so once
                LogNoDissolve(e, a, st);
                return;
            }
            bool inProgress = false;
            try { inProgress = DeathDissolve.s_DeathDissolvesInProgress.Contains(dd); }
            catch { /* static list unreadable */ }
            if (dd.PlayedExternally() || inProgress)
            {
                e.DissolveReasserted = true; // the game's own dissolve is running — leave it
                return;
            }
            if (CoroutineHelper.instance == null)
                return; // DeathDissolve.Play needs it on its first line; wait for it
            e.DissolveReasserted = true;
            bool ok = false;
            try { dd.ExternalPlay(); ok = true; }
            catch (System.Exception ex)
            {
                VRLog.Warn(Name, $"DoorOpenWatch: DeathDissolve.ExternalPlay on '{e.RootName}' threw {ex.GetType().Name}: {ex.Message}");
            }
            LogDissolveReasserted(e, a, st, dd, ok, now);
        }

        private void LogDissolveReasserted(Entry e, Animator a, AnimatorStateInfo st, DeathDissolve dd, bool ok, float now)
        {
            _sb.Clear();
            _sb.Append("DOOR DISSOLVE RE-ASSERTED '").Append(e.RootName).Append("': the 'Open' state has run ")
               .Append(st.normalizedTime.ToString("0.00")).Append(" loops (")
               .Append((now - e.OpenedAt).ToString("0.0")).Append(" s since the open) and the door's own "
                     + "DeathDissolve '").Append(dd.gameObject.name)
               .Append("' had never been played (PlayedExternally false, not in s_DeathDissolvesInProgress) — "
                     + "DeathDissolveSMB.Die never reached it. The mod called dd.ExternalPlay() itself, the "
                     + "exact call Die makes; it ")
               .Append(ok ? "returned" : "THREW")
               .Append(". Player.log must now carry a bare '").Append(LeafNameOf(a)).Append("' line "
                     + "(DeathDissolve.Play's Debug.Log of every renderer it takes) and the next sample must "
                     + "show _Toggle_Dissolve 1 and _Cutout ramping. ");
            AppendLeafForensics(e, a, st);
            // HW-VERIFY: fires only when the remedy runs; the next sample's material dump is the reading.
            VRLog.Note(Name, _sb.ToString());
        }

        private void LogNoDissolve(Entry e, Animator a, AnimatorStateInfo st)
        {
            _sb.Clear();
            _sb.Append("DOOR DISSOLVE ABSENT '").Append(e.RootName).Append("': the 'Open' state has run ")
               .Append(st.normalizedTime.ToString("0.00")).Append(" loops and there is NO DeathDissolve "
                     + "component anywhere under the animator '").Append(a.gameObject.name)
               .Append("' — this door kit cannot be hidden by the dissolve path; whatever hides it in the "
                     + "flat game is in the forensics that follow. ");
            AppendLeafForensics(e, a, st);
            // HW-VERIFY: names the door kit that has no dissolve to replay.
            VRLog.Note(Name, _sb.ToString());
        }

        private static string LeafNameOf(Animator a)
        {
            try
            {
                Renderer? r = a.GetComponentInChildren<Renderer>(true);
                return r != null ? r.name : "?";
            }
            catch { return "?"; }
        }

        /// <summary>
        /// EVERY TERM THAT CAN STOP THE HIDE, read at each sample — the ModBuild 414 round showed the
        /// state runs and the leaf stays, so the next log has to name WHICH of these is off:
        /// the clip (name/length/loop/events), the SMBs on the current state and the DeathDissolveSMB's
        /// own countdown fields, the DeathDissolve component and its inputs, the coroutine host, the
        /// leaf's path, visibility, property block and every Float/Range material property (a
        /// <c>_Cutout</c> that never leaves 0 with <c>_Toggle_Dissolve</c> 0 is a dissolve that never
        /// started; one that ramps under a property block carrying the same key is an override).
        /// </summary>
        private void AppendLeafForensics(Entry e, Animator a, AnimatorStateInfo st)
        {
            _sb.Append("FORENSICS: ");
            try
            {
                AnimatorClipInfo[] clips = a.GetCurrentAnimatorClipInfo(0);
                _sb.Append("clips ").Append(clips.Length);
                for (int i = 0; i < clips.Length && i < 2; i++)
                {
                    AnimationClip c = clips[i].clip;
                    if (c == null) { _sb.Append(" [null]"); continue; }
                    _sb.Append(" ['").Append(c.name).Append("' len ").Append(c.length.ToString("0.00"))
                       .Append(" s loop ").Append(c.isLooping).Append(" wrap ").Append(c.wrapMode)
                       .Append(" events ").Append(c.events.Length);
                    for (int k = 0; k < c.events.Length && k < 4; k++)
                        _sb.Append(k == 0 ? ": " : ", ").Append(c.events[k].functionName).Append('@')
                           .Append(c.events[k].time.ToString("0.00"));
                    _sb.Append(']');
                }
            }
            catch (System.Exception ex) { _sb.Append("clips unreadable (").Append(ex.GetType().Name).Append(')'); }
            try
            {
                StateMachineBehaviour[] smbs = a.GetBehaviours(st.fullPathHash, 0);
                _sb.Append("; SMBs on this state ").Append(smbs.Length);
                for (int i = 0; i < smbs.Length && i < 4; i++)
                {
                    StateMachineBehaviour b = smbs[i];
                    if (b == null) { _sb.Append(" [null]"); continue; }
                    _sb.Append(" [").Append(b.GetType().Name);
                    if (b is DeathDissolveSMB dds)
                    {
                        _sb.Append(": onAnimationEnd ").Append(dds.m_OnAnimationEnd)
                           .Append(" deathDelay ").Append(dds.m_DeathDelay.ToString("0.00"))
                           .Append(" instanceDelay ").Append(dds.m_InstanceDeathDelay.ToString("0.00"))
                           .Append(" notDead ").Append(dds.m_NotDead);
                    }
                    _sb.Append(']');
                }
                StateMachineBehaviour[] all = a.GetBehaviours<StateMachineBehaviour>();
                _sb.Append("; SMBs on the whole controller ").Append(all.Length);
                for (int i = 0; i < all.Length && i < 6; i++)
                    _sb.Append(i == 0 ? ": " : ", ").Append(all[i] != null ? all[i].GetType().Name : "null");
            }
            catch (System.Exception ex) { _sb.Append("; SMBs unreadable (").Append(ex.GetType().Name).Append(')'); }
            try
            {
                DeathDissolve? dd = a.gameObject.GetComponentInChildren<DeathDissolve>(true);
                if (dd == null)
                    _sb.Append("; DeathDissolve NONE under the animator");
                else
                {
                    bool inProgress = DeathDissolve.s_DeathDissolvesInProgress.Contains(dd);
                    _sb.Append("; DeathDissolve on '").Append(dd.gameObject.name).Append("' (enabled ")
                       .Append(dd.isActiveAndEnabled).Append(", playedExternally ").Append(dd.PlayedExternally())
                       .Append(", inProgress ").Append(inProgress)
                       .Append(", animTimeTaken ").Append(dd.m_animTimeTaken.ToString("0.00"))
                       .Append(", showParticles ").Append(dd.ShowParticleEffect)
                       .Append(", particles ").Append(dd.Particles != null ? dd.Particles.name : "null")
                       .Append(", characterMesh ").Append(dd.CharacterMesh != null ? dd.CharacterMesh.name : "null")
                       .Append(", curve ").Append(dd.Curve != null ? dd.Curve.length + " key(s)" : "null")
                       .Append(", lifeTime ").Append(dd.LifeTime.ToString("0.0"))
                       .Append(", animTime ").Append(dd.AnimTime.ToString("0.00"))
                       .Append(", initialDelay ").Append(dd.initialDelay.ToString("0.00")).Append(')');
                }
                _sb.Append("; CoroutineHelper.instance ").Append(CoroutineHelper.instance != null ? "present" : "NULL");
            }
            catch (System.Exception ex) { _sb.Append("; DeathDissolve unreadable (").Append(ex.GetType().Name).Append(')'); }
            try
            {
                Chronos.Timekeeper keeper = Chronos.Timekeeper.instance;
                Chronos.GlobalClock? clock = keeper != null ? keeper.m_GlobalClock : null;
                _sb.Append("; Unity Time.timeScale ").Append(Time.timeScale.ToString("0.00"))
                   .Append(", Chronos clock ").Append(clock != null
                       ? $"time {clock.time:0.00} deltaTime {clock.deltaTime:0.0000} paused {clock.paused}"
                       : "NULL")
                   .Append("; animator updateMode ").Append(a.updateMode)
                   .Append(" rootMotion ").Append(a.applyRootMotion);
            }
            catch (System.Exception ex) { _sb.Append("; clocks unreadable (").Append(ex.GetType().Name).Append(')'); }
            try
            {
                _rendererScratch.Clear();
                a.GetComponentsInChildren(includeInactive: true, _rendererScratch);
                int shown = 0;
                foreach (Renderer r in _rendererScratch)
                {
                    if (r == null || shown >= 2)
                        continue;
                    shown++;
                    _sb.Append("; LEAF '").Append(PathUnder(r.transform, e.Prop.transform))
                       .Append("' isVisible ").Append(r.isVisible)
                       .Append(" enabled ").Append(r.enabled)
                       .Append(" active ").Append(r.gameObject.activeInHierarchy)
                       .Append(" layer ").Append(r.gameObject.layer)
                       .Append(" queue ").Append(r.sharedMaterial != null ? r.sharedMaterial.renderQueue : -1);
                    bool hasBlock = r.HasPropertyBlock();
                    _sb.Append(" propertyBlock ").Append(hasBlock);
                    if (hasBlock)
                    {
                        var mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb);
                        _sb.Append(" (carries:");
                        AppendBlockKey(mpb, "_Cutout");
                        AppendBlockKey(mpb, "_Toggle_Dissolve");
                        AppendBlockKey(mpb, "_Cutoff");
                        AppendBlockKey(mpb, "_WallFade");
                        AppendBlockKey(mpb, "_InvisibilityControl");
                        AppendBlockKey(mpb, "_Alpha");
                        _sb.Append(')');
                    }
                    Material[] mats = r.sharedMaterials;
                    _sb.Append(" materials ").Append(mats.Length);
                    for (int m = 0; m < mats.Length && m < 2; m++)
                    {
                        Material mat = mats[m];
                        if (mat == null) { _sb.Append(" [null]"); continue; }
                        Shader sh = mat.shader;
                        _sb.Append(" ['").Append(mat.name).Append("' shader '").Append(sh != null ? sh.name : "null").Append('\'');
                        if (sh != null)
                        {
                            int n = sh.GetPropertyCount();
                            int printed = 0;
                            for (int pi = 0; pi < n && printed < 28; pi++)
                            {
                                UnityEngine.Rendering.ShaderPropertyType t = sh.GetPropertyType(pi);
                                if (t != UnityEngine.Rendering.ShaderPropertyType.Float
                                    && t != UnityEngine.Rendering.ShaderPropertyType.Range)
                                    continue;
                                string pn = sh.GetPropertyName(pi);
                                printed++;
                                _sb.Append(printed == 1 ? " " : ",").Append(pn).Append('=')
                                   .Append(mat.GetFloat(pn).ToString("0.###"));
                            }
                            if (mat.HasProperty("_Color"))
                                _sb.Append(" _Color.a=").Append(mat.GetColor("_Color").a.ToString("0.##"));
                        }
                        _sb.Append(']');
                    }
                }
                _rendererScratch.Clear();
            }
            catch (System.Exception ex) { _sb.Append("; leaf unreadable (").Append(ex.GetType().Name).Append(')'); }
        }

        private void AppendBlockKey(MaterialPropertyBlock mpb, string key)
        {
            int id = Shader.PropertyToID(key);
            if (mpb.HasProperty(id))
                _sb.Append(' ').Append(key).Append('=').Append(mpb.GetFloat(id).ToString("0.###"));
        }

        private static string PathUnder(Transform t, Transform root)
        {
            var parts = new List<string>(8);
            for (Transform? n = t; n != null && n != root && parts.Count < 8; n = n.parent)
                parts.Add(n.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ---------------------------------------------------------------- the outcome

        // ------------------------------------------------- what the clip moves, MEASURED

        /// <summary>
        /// ROUTE (a) — ASK THE CLIP WHAT IT MOVES.
        ///
        /// <para><c>AnimationClip.SampleAnimation(GameObject, float)</c> is a RUNTIME API: it
        /// evaluates a clip's curves directly against a hierarchy BY CURVE PATH, with no playable
        /// graph in between. Sampling at t=0 and at t=length and diffing the two therefore answers
        /// two questions at once — which transforms and renderers this clip is entitled to write
        /// (the movable set this hide needs), and whether the clip's paths resolve on this
        /// placement AT ALL (the ModBuild 413-415 question the DOOR OPEN FRAMES line could not
        /// close, because a graph that applies nothing and a clip that binds nothing look
        /// identical from the outside).</para>
        ///
        /// <para>The recorded ORIGINAL state is written back field by field afterwards — never by
        /// re-sampling at 0, which is a different value from whatever the graph had put there.</para>
        ///
        /// <para>IT IS A MEASUREMENT AND NOTHING ELSE (ModBuild 429). Until ModBuild 428 its
        /// result also chose which renderers the mod's own hide stand-in was allowed to switch
        /// off; that stand-in is gone and nothing consumes the movable SET any more. The line it
        /// prints is the whole product.</para>
        /// </summary>
        private void SampleOpenClip(Entry e, Animator a)
        {
            RuntimeAnimatorController? rac = null;
            try { rac = a.runtimeAnimatorController; }
            catch { /* mid-swap */ }
            if (rac == null)
            {
                LogClipSample(e, a, "none", "none", 0, 0, 0, 0f, 0f, false, string.Empty,
                              "there is no runtime animator controller on the handle", 0);
                return;
            }

            // ---- pick the clips: the live 'Open' state's own clip, else every 'Open'-named clip
            // on the controller, else every clip. Capped so a fat controller cannot cost a frame.
            _clipScratch.Clear();
            string how;
            try
            {
                AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Open"))
                {
                    AnimatorClipInfo[] cur = a.GetCurrentAnimatorClipInfo(0);
                    foreach (AnimatorClipInfo ci in cur)
                    {
                        if (ci.clip != null)
                            _clipScratch.Add(ci.clip);
                    }
                }
            }
            catch { _clipScratch.Clear(); }
            AnimationClip[] all;
            try { all = rac.animationClips; }
            catch { all = System.Array.Empty<AnimationClip>(); }
            if (_clipScratch.Count > 0)
            {
                how = "the clip bound to the LIVE 'Open' state";
            }
            else
            {
                foreach (AnimationClip c in all)
                {
                    if (c != null && c.name.IndexOf("Open", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        _clipScratch.Add(c);
                }
                if (_clipScratch.Count > 0)
                {
                    how = "every clip on the controller whose name contains 'Open' (the 'Open' state's "
                        + "own clip could not be identified)";
                }
                else
                {
                    foreach (AnimationClip c in all)
                    {
                        if (c != null)
                            _clipScratch.Add(c);
                    }
                    how = "EVERY clip on the controller, union of their results — not one of them is named 'Open'";
                }
            }
            if (_clipScratch.Count > 8)
                _clipScratch.RemoveRange(8, _clipScratch.Count - 8);
            if (_clipScratch.Count == 0)
            {
                LogClipSample(e, a, "none", "none", 0, 0, 0, 0f, 0f, false, string.Empty,
                              "the controller carries no clips at all", 0);
                return;
            }

            // ---- snapshot the ORIGINAL state of everything under the handle
            _measureT.Clear();
            _measureRenderers.Clear();
            try
            {
                a.GetComponentsInChildren(includeInactive: true, _measureT);
                a.GetComponentsInChildren(includeInactive: true, _measureRenderers);
            }
            catch
            {
                _measureT.Clear();
                _measureRenderers.Clear();
            }
            if (_measureT.Count == 0)
            {
                _clipScratch.Clear();
                LogClipSample(e, a, "none", "none", 0, 0, 0, 0f, 0f, false, string.Empty,
                              "no transform under the handle could be read", 0);
                return;
            }
            _measurePos0.Clear(); _measureRot0.Clear(); _measureScale0.Clear(); _measureActive0.Clear();
            _measurePosA.Clear(); _measureRotA.Clear(); _measureScaleA.Clear(); _measureActiveA.Clear();
            _measureMoved.Clear();
            foreach (Transform t in _measureT)
            {
                bool live = t != null;
                _measurePos0.Add(live ? t!.localPosition : Vector3.zero);
                _measureRot0.Add(live ? t!.localRotation : Quaternion.identity);
                _measureScale0.Add(live ? t!.localScale : Vector3.one);
                _measureActive0.Add(live && t!.gameObject.activeSelf);
                _measurePosA.Add(Vector3.zero);
                _measureRotA.Add(Quaternion.identity);
                _measureScaleA.Add(Vector3.one);
                _measureActiveA.Add(false);
                _measureMoved.Add(false);
            }
            _measureEnabled0.Clear(); _measureEnabledA.Clear(); _measureFlipped.Clear();
            foreach (Renderer r in _measureRenderers)
            {
                _measureEnabled0.Add(r != null && r.enabled);
                _measureEnabledA.Add(false);
                _measureFlipped.Add(false);
            }

            // ---- sample each clip at 0 and at its length, diffing snapshot B against snapshot A
            _clipSb.Clear();
            int movedCount = 0, flippedCount = 0;
            float maxPos = 0f, maxRot = 0f;
            bool sampled = false;
            GameObject host = a.gameObject;
            foreach (AnimationClip clip in _clipScratch)
            {
                if (clip == null)
                    continue;
                _clipSb.Append(_clipSb.Length == 0 ? "" : ", ").Append('\'').Append(clip.name)
                       .Append("' (").Append(clip.length.ToString("0.00")).Append(" s)");
                try
                {
                    clip.SampleAnimation(host, 0f);
                    for (int i = 0; i < _measureT.Count; i++)
                    {
                        Transform t = _measureT[i];
                        if (t == null)
                            continue;
                        _measurePosA[i] = t.localPosition;
                        _measureRotA[i] = t.localRotation;
                        _measureScaleA[i] = t.localScale;
                        _measureActiveA[i] = t.gameObject.activeSelf;
                    }
                    for (int j = 0; j < _measureRenderers.Count; j++)
                    {
                        Renderer r = _measureRenderers[j];
                        _measureEnabledA[j] = r != null && r.enabled;
                    }
                    clip.SampleAnimation(host, clip.length);
                    sampled = true;
                    for (int i = 0; i < _measureT.Count; i++)
                    {
                        Transform t = _measureT[i];
                        if (t == null)
                            continue;
                        float dp = (t.localPosition - _measurePosA[i]).magnitude;
                        float dr = Quaternion.Angle(t.localRotation, _measureRotA[i]);
                        float ds = (t.localScale - _measureScaleA[i]).magnitude;
                        if (dp > maxPos) maxPos = dp;
                        if (dr > maxRot) maxRot = dr;
                        if (dp > 1e-4f || dr > 0.01f || ds > 1e-4f
                            || t.gameObject.activeSelf != _measureActiveA[i])
                            _measureMoved[i] = true;
                    }
                    for (int j = 0; j < _measureRenderers.Count; j++)
                    {
                        Renderer r = _measureRenderers[j];
                        if (r != null && r.enabled != _measureEnabledA[j])
                            _measureFlipped[j] = true;
                    }
                    // A BELT for what this probe does not snapshot. The write-back below is exact
                    // for transforms, activeSelf and Renderer.enabled; a clip may also carry curves
                    // on material or component properties, and leaving one of those at its END value
                    // is how a "measurement" turns into a change. Parking the clip at t=0 puts them
                    // back at the start of the OPEN animation, which is the closed door, and the
                    // recorded originals are then written over the top of it.
                    clip.SampleAnimation(host, 0f);
                }
                catch (System.Exception ex)
                {
                    VRLog.Warn(Name, $"DoorOpenWatch: sampling '{clip.name}' on '{e.RootName}' threw "
                                   + $"{ex.GetType().Name}: {ex.Message}");
                }
            }
            string clipNames = _clipSb.ToString();
            _clipSb.Clear();

            // ---- WRITE THE RECORDED ORIGINAL BACK, field by field. Never by re-sampling at 0.
            RestoreSampledState();

            int covered = _measureT.Count;
            int rendererCount = _measureRenderers.Count;
            if (!sampled)
            {
                _clipScratch.Clear();
                _measureT.Clear();
                _measureRenderers.Clear();
                LogClipSample(e, a, clipNames, "none", covered, 0, 0, 0f, 0f, false, string.Empty,
                              "every SampleAnimation call threw — " + how, rendererCount);
                return;
            }

            // ---- WHAT THE CLIP MOVES, named. Until ModBuild 428 this loop also built the set
            // of renderers the hide stand-in was allowed to switch off; the stand-in is gone, so
            // the names and the counts ARE the product.
            int named = 0;
            for (int i = 0; i < _measureT.Count; i++)
            {
                if (!_measureMoved[i])
                    continue;
                Transform t = _measureT[i];
                if (t == null)
                    continue;
                movedCount++;
                if (named < 6)
                {
                    named++;
                    _clipSb.Append(named == 1 ? "" : ", ").Append('\'').Append(t.name).Append('\'');
                }
            }
            string movedNames = _clipSb.Length == 0 ? "none" : _clipSb.ToString();
            _clipSb.Clear();
            int namedFlip = 0;
            for (int j = 0; j < _measureRenderers.Count; j++)
            {
                Renderer r = _measureRenderers[j];
                if (r == null)
                    continue;
                bool flipped = _measureFlipped[j];
                if (flipped)
                {
                    flippedCount++;
                    if (namedFlip < 4)
                    {
                        namedFlip++;
                        _clipSb.Append(namedFlip == 1 ? "" : ", ").Append('\'').Append(r.name).Append('\'');
                    }
                }
            }
            string flipNames = _clipSb.Length == 0 ? "none" : _clipSb.ToString();
            _clipSb.Clear();
            _clipScratch.Clear();
            _measureT.Clear();
            _measureRenderers.Clear();

            LogClipSample(e, a, clipNames, movedNames, covered, movedCount, flippedCount,
                          maxPos, maxRot, true, flipNames, how, rendererCount);
        }

        /// <summary>Write the recorded original TRS / activeSelf / renderer-enabled back. Only
        /// where it actually differs, so a hierarchy the sample never touched takes no writes.</summary>
        private void RestoreSampledState()
        {
            for (int i = 0; i < _measureT.Count && i < _measurePos0.Count; i++)
            {
                Transform t = _measureT[i];
                if (t == null)
                    continue;
                try
                {
                    if (t.localPosition != _measurePos0[i])
                        t.localPosition = _measurePos0[i];
                    if (t.localRotation != _measureRot0[i])
                        t.localRotation = _measureRot0[i];
                    if (t.localScale != _measureScale0[i])
                        t.localScale = _measureScale0[i];
                    if (t.gameObject.activeSelf != _measureActive0[i])
                        t.gameObject.SetActive(_measureActive0[i]);
                }
                catch { /* a transform destroyed under us mid-restore */ }
            }
            for (int j = 0; j < _measureRenderers.Count && j < _measureEnabled0.Count; j++)
            {
                Renderer r = _measureRenderers[j];
                if (r == null)
                    continue;
                try
                {
                    if (r.enabled != _measureEnabled0[j])
                        r.enabled = _measureEnabled0[j];
                }
                catch { /* ditto */ }
            }
        }

        /// <summary>The clip instrument. See <see cref="SampleOpenClip"/>.</summary>
        private void LogClipSample(Entry e, Animator a, string clipNames, string movedNames, int covered,
                                   int movedCount, int flippedCount, float maxPos, float maxRot,
                                   bool ran, string flipNames, string how, int rendererCount)
        {
            _sb.Clear();
            _sb.Append("DOOR OPEN CLIP SAMPLE '").Append(e.RootName).Append("': animator '")
               .Append(a.gameObject.name).Append("' (controller '").Append(ControllerName(a))
               .Append("'), clip(s) ").Append(clipNames).Append(" chosen as ").Append(how)
               .Append(" — AnimationClip.SampleAnimation was run against the live hierarchy at t=0 and at "
                     + "t=length, and the recorded original state written back field by field. COVERED ")
               .Append(covered).Append(" transform(s) and ").Append(rendererCount)
               .Append(" renderer(s) under the handle. MOVED ").Append(movedCount)
               .Append(" transform(s): ").Append(movedNames)
               .Append("; largest local delta between the two samples: pos ").Append(maxPos.ToString("0.0000"))
               .Append(" wu, rot ").Append(maxRot.ToString("0.00")).Append(" deg. RENDERER FLAGS FLIPPED BY THE CLIP: ")
               .Append(flippedCount).Append(flippedCount > 0 ? " — " + flipNames : "")
               // APPENDED, ModBuild 429: are the transforms named above the ones the frame probe
               // measured, or a different placement with the same names? Both walks come off the
               // animator, so equal instance ids settle it and unequal ids name a rebuild between
               // the two readings.
               .Append(". ANIMATOR INSTANCE ").Append(e.ClipSampleAnimatorId)
               .Append("; the DOOR OPEN FRAMES window on this door ran on instance ")
               .Append(e.ProbeAnimatorId).Append(" over ").Append(e.ProbeCovered)
               .Append(" transform(s) — ")
               .Append(e.ProbeAnimatorId == 0
                       ? "no window ran on this door (it was already open at first sight), so the two "
                         + "readings cannot be compared"
                       : e.ProbeAnimatorId == e.ClipSampleAnimatorId
                         ? "the SAME instance, so the transforms named here ARE the ones the probe "
                           + "measured and 'the clip moves them' and 'the probe saw them stand still' "
                           + "are statements about the same objects"
                         : "a DIFFERENT instance: the placement was rebuilt between the window and "
                           + "this sample, and the two readings are about different objects")
               .Append(". READING: ");
            if (!ran)
                _sb.Append("the sample DID NOT RUN, so it proves neither side of the ModBuild 413-415 question — ")
                   .Append(how).Append(". Nothing is stood in for either way: since ModBuild 429 this "
                                     + "file never writes Renderer.enabled or SetActive on door content, so "
                                     + "the door keeps whatever picture the game gives it and this sample "
                                     + "simply says nothing this time.");
            else if (movedCount > 0 || flippedCount > 0)
                _sb.Append("the clip DOES bind this hierarchy — evaluating its curves directly by path, with no "
                         + "playable graph in between, writes ").Append(movedCount)
                   .Append(" transform(s) and ").Append(flippedCount)
                   .Append(" renderer flag(s). So the ModBuild 413-415 question is answered on the side of the "
                         + "PLAYABLE GRAPH: the curve paths resolve on this placement and it is the graph's "
                         + "APPLICATION of them at runtime that writes nothing (DOOR OPEN FRAMES reads all zeroes "
                         + "while the state runs). Look for what suppresses the graph's write, not for a missing path.");
            else
                _sb.Append("the clip binds NOTHING on this hierarchy — SampleAnimation evaluates the curves "
                         + "directly by path with no playable graph in between, and it moves not one of the ")
                   .Append(covered).Append(" transform(s) under the handle and flips not one renderer flag. So the "
                         + "ModBuild 413-415 question is answered on the side of the CLIP AND ITS PATHS: the curve "
                         + "paths this clip carries do not exist on this placement, and no playable graph could ever "
                         + "have applied them. The graph is innocent, and the door still gets no picture from this "
                         + "mod — the ModBuild 416-428 stand-in was removed on the user's ruling, so what this line "
                         + "names is a fault in the GAME's own content that the two replay remedies above are the "
                         + "only sanctioned answer to.");
            // HW-VERIFY: the one line that decides 'the graph does not apply the clip' vs 'the clip
            // binds nothing on this placement' — open since ModBuild 413. Once per animator instance.
            VRLog.Note(Name, _sb.ToString());
        }

        // ---------------------------------------------------------------- the per-frame probe

        /// <summary>MODBUILD 428: 0.6 s covered two thirds of the 0.87 s <c>Door_02_Open</c> clip,
        /// so the window could close while the clip was still running and the phase deltas would
        /// have been read off a partial open. 1.5 s covers the clip with room either side, and the
        /// window is still one and a half seconds per door open in a whole scenario.
        /// <para>MODBUILD 429 — AND 1.5 s FROM THE FLIP WAS THE WRONG WINDOW ANYWAY. A door's
        /// rules state flips before its animator enters "Open": on four of the six doors in the
        /// ModBuild 428 hardware log the animator was in <c>Idle</c> (clip <c>Door_02_Idle</c>,
        /// 1.00 s) at the flip AND at the +1 s sample, and reached "Open" between +1 s and +3 s —
        /// after this window had closed. Those four printed "THE ANIMATOR NEVER WRITES", which was
        /// a correct measurement of the idle clip and said nothing whatever about the open clip.
        /// So this is now only the MINIMUM: the window stays open until the "Open" state has been
        /// seen and its clip has run past the end, or until <see cref="ProbeMaxSeconds"/>.</para></summary>
        private const float ProbeMinSeconds = 1.5f;

        /// <summary>The hard deadline, measured from the flip. A door that never enters "Open"
        /// must not hold a per-frame window and an end-of-frame coroutine open for the rest of the
        /// scenario, and a window that ran this long is itself the finding — the line says so.
        /// Twelve seconds is past the +8 s sample, so the window and the samples cover the same
        /// story.</summary>
        private const float ProbeMaxSeconds = 12f;

        /// <summary>How far past the end of the "Open" clip the window keeps sampling before it
        /// closes early. A fifth of a clip beyond the end is enough to catch a writer that puts
        /// the pose back once the clip has finished.</summary>
        private const float OpenDoneNormalizedTime = 1.2f;

        /// <summary>How long after the flip the clip sample may run. It WRITES the hierarchy and
        /// writes the recorded original back, so it must never overlap the phase window; the gate
        /// in <see cref="Step"/> also waits for <c>ProbeArmed</c> to clear.</summary>
        private const float ClipSampleAfterSeconds = 1.0f;

        /// <summary>Open a 0.6 s per-frame window on the door's animator: the root-motion deltas it
        /// PRODUCES each frame against what EVERY transform under the animator actually DOES. The two
        /// readings it separates: delta != 0 with an unchanged root = the motion is produced and
        /// discarded (a write war on the root transform — nothing in the game implements
        /// OnAnimatorMove, grep 2026-09-04); delta == 0 and nothing changes = the clip binds nothing
        /// on this hierarchy (a path the clip expects that this placement does not have).
        /// <para>MODBUILD 427: the population is the WHOLE subtree. ModBuild 416 watched
        /// <c>LeafRefs[0]</c> — one renderer's transform — while its READING sentence asserted "no
        /// transform under the animator changed by a ten-thousandth". On the CR_ST_Door_02 kit that
        /// one renderer was the FRAME, so the two door leaves the sentence spoke for were never
        /// measured at all. The worst mover is kept by INDEX so the per-frame walk allocates nothing
        /// and the name is read once, at log time.</para></summary>
        private void ArmFrameProbe(Entry e, float now)
        {
            if (e.ProbeArmed)
                return;
            Animator? a = e.Animator;
            if (a == null)
                return;
            e.ProbeArmed = true;
            e.ProbeFirst = true;
            e.ProbeUntil = now + ProbeMinSeconds;
            e.ProbeHardUntil = now + ProbeMaxSeconds;
            e.ProbeAnimatorId = e.AnimatorId;
            e.ProbeSpan = 0f;
            e.ProbeFrames = 0;
            // The state census (ModBuild 429). A window that never covers the 'Open' state cannot
            // say anything about the open clip, and until this build it said the opposite.
            e.StateHash0 = e.StateHashLast = 0;
            e.StateChanges = 0;
            e.SawOpen = false;
            e.OpenSeenAfter = -1f;
            e.OpenFrames = 0;
            e.OpenNtFirst = e.OpenNtLast = -1f;
            e.ActiveFrames = e.EnabledFrames = 0;
            e.SpeedMin = float.MaxValue;
            e.SpeedMax = float.MinValue;
            e.Weight0Min = float.MaxValue;
            e.Weight0Max = float.MinValue;
            e.LayerCount = 0;
            e.UpdateModeLast = AnimatorUpdateMode.Normal;
            e.RootMotionLast = false;
            e.Clip0 = e.ClipLast = null;
            e.ClipWeightMin = float.MaxValue;
            e.ClipWeightMax = float.MinValue;
            e.DeltaPosMin = float.MaxValue;
            e.DeltaPosMax = 0f;
            e.DeltaRotMax = 0f;
            e.RootPosDevMax = e.RootRotDevMax = e.RootScaleDevMax = 0f;
            e.AnyPosDevMax = e.AnyRotDevMax = e.AnyScaleDevMax = 0f;
            e.WorstPosIdx = e.WorstRotIdx = e.WorstScaleIdx = -1;
            e.Transitions = 0;
            e.NtFirst = -1f;
            e.NtLast = -1f;
            e.ClipCountMin = int.MaxValue;
            e.ClipCountMax = 0;
            // EVERY transform under the animator, not one renderer's. ModBuild 416 watched
            // LeafRefs[0] and the READING claimed it had watched them all; on CR_ST_Door_02 that one
            // renderer was the FRAME and the two door leaves were never measured. Filled once per
            // arm (the List overload reuses the capacity); the per-frame walk allocates nothing.
            e.ProbeT.Clear();
            try { a.GetComponentsInChildren(includeInactive: true, e.ProbeT); }
            catch { e.ProbeT.Clear(); }
            e.ProbeCovered = e.ProbeT.Count;
            e.ProbePos0.Clear();
            e.ProbeRot0.Clear();
            e.ProbeScale0.Clear();
            // The three-phase state (ModBuild 428). Nothing is captured until the first Update
            // sample; HaveU/HaveL/HaveE say which snapshots the comparison may read.
            e.HaveU = e.HaveL = e.HaveE = false;
            e.UL.Reset();
            e.LE.Reset();
            e.EU.Reset();
            e.PhaseFramesU = e.PhaseFramesL = e.PhaseFramesE = 0;
            e.PhaseUPos.Clear(); e.PhaseURot.Clear(); e.PhaseUScale.Clear();
            e.PhaseLPos.Clear(); e.PhaseLRot.Clear(); e.PhaseLScale.Clear();
            e.PhaseEPos.Clear(); e.PhaseERot.Clear(); e.PhaseEScale.Clear();
            // The visibility census population — every renderer under the animator handle, the
            // exact population Unity's CullUpdateTransforms asks about.
            e.ProbeR.Clear();
            try { a.GetComponentsInChildren(includeInactive: true, e.ProbeR); }
            catch { e.ProbeR.Clear(); }
            e.VisMin = int.MaxValue;
            e.VisMax = 0;
            e.VisZeroFrames = 0;
            e.VisFrames = 0;
            _probing++;
            if (!_pumping)
            {
                _pumping = true;
                try { StartCoroutine(PumpEndOfFrame()); }
                catch { _pumping = false; /* the watch is being torn down — two phases still read */ }
            }
        }

        /// <summary>
        /// Close an open probe window WITHOUT logging, because the entry it belongs to is going
        /// away (a scenario change, a destroyed door prop).
        /// <para>ModBuild 428, and it is a correctness fix rather than a tidy-up: <c>_probing</c>
        /// was incremented by <see cref="ArmFrameProbe"/> and decremented ONLY by
        /// <see cref="SampleFrameProbes"/>, so an entry dropped while its window was open leaked the
        /// count for the rest of the session and the watch then ran its per-frame path over an empty
        /// armed set forever. That was cheap and invisible when the counter only gated one loop; it
        /// is neither now, because the same counter holds a <c>LateUpdate</c> and a
        /// <c>WaitForEndOfFrame</c> coroutine open.</para>
        /// </summary>
        private void CloseAllProbes()
        {
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                try { CloseProbeSilently(kv.Value); }
                catch { /* one entry's teardown may not stop the rest */ }
            }
            _probing = 0;
        }

        private void CloseProbeSilently(Entry e)
        {
            if (!e.ProbeArmed)
                return;
            e.ProbeArmed = false;
            _probing--;
            if (_probing < 0)
                _probing = 0; // belt and braces: the pump must be able to stop
            e.ProbeT.Clear();
            e.ProbePos0.Clear();
            e.ProbeRot0.Clear();
            e.ProbeScale0.Clear();
            e.ProbeR.Clear();
            e.PhaseUPos.Clear(); e.PhaseURot.Clear(); e.PhaseUScale.Clear();
            e.PhaseLPos.Clear(); e.PhaseLRot.Clear(); e.PhaseLScale.Clear();
            e.PhaseEPos.Clear(); e.PhaseERot.Clear(); e.PhaseEScale.Clear();
            e.HaveU = e.HaveL = e.HaveE = false;
        }

        private void SampleFrameProbes(float now)
        {
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.ProbeArmed)
                    continue;
                Animator? a = e.Animator;
                // MODBUILD 429: the window closes on the STATE, not on a stopwatch alone. It runs
                // at least ProbeMinSeconds, then until the 'Open' state has run past the end of
                // its clip, and never past ProbeMaxSeconds. A door that never enters 'Open' now
                // says so on its own line instead of having its idle clip reported as an animator
                // that never writes.
                bool openDone = e.SawOpen && e.OpenNtLast >= OpenDoneNormalizedTime;
                if (a == null || now >= e.ProbeHardUntil || (now > e.ProbeUntil && openDone))
                {
                    e.ProbeArmed = false;
                    e.ProbeSpan = Mathf.Max(0f, now - e.OpenedAt);
                    _probing--;
                    LogOpenFrames(e, a == null);
                    LogOpenPhases(e, a == null);
                    // Drop the subtree references: the window is closed and a door's content is
                    // destroyed and re-instantiated freely by Apparance.
                    e.ProbeT.Clear();
                    e.ProbePos0.Clear();
                    e.ProbeRot0.Clear();
                    e.ProbeScale0.Clear();
                    e.ProbeR.Clear();
                    e.PhaseUPos.Clear(); e.PhaseURot.Clear(); e.PhaseUScale.Clear();
                    e.PhaseLPos.Clear(); e.PhaseLRot.Clear(); e.PhaseLScale.Clear();
                    e.PhaseEPos.Clear(); e.PhaseERot.Clear(); e.PhaseEScale.Clear();
                    e.HaveU = e.HaveL = e.HaveE = false;
                    continue;
                }
                try
                {
                    Transform root = a.transform;
                    if (e.ProbeFirst)
                    {
                        e.ProbeFirst = false;
                        e.RootPos0 = root.localPosition;
                        e.RootRot0 = root.localRotation;
                        e.RootScale0 = root.localScale;
                        e.ProbePos0.Clear();
                        e.ProbeRot0.Clear();
                        e.ProbeScale0.Clear();
                        for (int i = 0; i < e.ProbeT.Count; i++)
                        {
                            Transform t = e.ProbeT[i];
                            bool live = t != null;
                            e.ProbePos0.Add(live ? t!.localPosition : Vector3.zero);
                            e.ProbeRot0.Add(live ? t!.localRotation : Quaternion.identity);
                            e.ProbeScale0.Add(live ? t!.localScale : Vector3.one);
                        }
                        e.Bound0 = a.hasBoundPlayables;
                        e.GraphValid0 = a.playableGraph.IsValid();
                    }
                    e.ProbeFrames++;
                    float dp = a.deltaPosition.magnitude;
                    if (dp < e.DeltaPosMin) e.DeltaPosMin = dp;
                    if (dp > e.DeltaPosMax) e.DeltaPosMax = dp;
                    float dr = Quaternion.Angle(Quaternion.identity, a.deltaRotation);
                    if (dr > e.DeltaRotMax) e.DeltaRotMax = dr;
                    e.RootPosDevMax = Mathf.Max(e.RootPosDevMax, (root.localPosition - e.RootPos0).magnitude);
                    e.RootRotDevMax = Mathf.Max(e.RootRotDevMax, Quaternion.Angle(root.localRotation, e.RootRot0));
                    e.RootScaleDevMax = Mathf.Max(e.RootScaleDevMax, (root.localScale - e.RootScale0).magnitude);
                    // EVERY transform under the animator, worst mover kept BY INDEX so the name
                    // is only read at log time. No allocation on this path.
                    for (int i = 0; i < e.ProbeT.Count && i < e.ProbePos0.Count; i++)
                    {
                        Transform t = e.ProbeT[i];
                        if (t == null)
                            continue;
                        float dpos = (t.localPosition - e.ProbePos0[i]).magnitude;
                        if (dpos > e.AnyPosDevMax) { e.AnyPosDevMax = dpos; e.WorstPosIdx = i; }
                        float drot = Quaternion.Angle(t.localRotation, e.ProbeRot0[i]);
                        if (drot > e.AnyRotDevMax) { e.AnyRotDevMax = drot; e.WorstRotIdx = i; }
                        float dsc = (t.localScale - e.ProbeScale0[i]).magnitude;
                        if (dsc > e.AnyScaleDevMax) { e.AnyScaleDevMax = dsc; e.WorstScaleIdx = i; }
                    }
                    if (a.IsInTransition(0))
                        e.Transitions++;
                    AnimatorStateInfo pst = a.GetCurrentAnimatorStateInfo(0);
                    float nt = pst.normalizedTime;
                    if (e.NtFirst < 0f) e.NtFirst = nt;
                    e.NtLast = nt;
                    // ---- THE STATE CENSUS (ModBuild 429). Every field is recorded for EVERY
                    // door, working and failing alike: a term that reads the same on both sides is
                    // not the cause, and the line has to be able to say that.
                    int hash = pst.shortNameHash;
                    if (e.StateHash0 == 0) e.StateHash0 = hash;
                    if (e.StateHashLast != 0 && hash != e.StateHashLast) e.StateChanges++;
                    e.StateHashLast = hash;
                    if (pst.IsName("Open"))
                    {
                        if (!e.SawOpen)
                        {
                            e.SawOpen = true;
                            e.OpenSeenAfter = Mathf.Max(0f, now - e.OpenedAt);
                            e.OpenNtFirst = nt;
                        }
                        e.OpenFrames++;
                        e.OpenNtLast = nt;
                    }
                    if (a.gameObject.activeInHierarchy) e.ActiveFrames++;
                    if (a.enabled) e.EnabledFrames++;
                    float sp = a.speed;
                    if (sp < e.SpeedMin) e.SpeedMin = sp;
                    if (sp > e.SpeedMax) e.SpeedMax = sp;
                    float w0 = a.GetLayerWeight(0);
                    if (w0 < e.Weight0Min) e.Weight0Min = w0;
                    if (w0 > e.Weight0Max) e.Weight0Max = w0;
                    e.LayerCount = a.layerCount;
                    e.UpdateModeLast = a.updateMode;
                    e.RootMotionLast = a.applyRootMotion;
                    // The List overload — the array overload allocates once per call, and this
                    // runs every frame per open door. A clip playing at WEIGHT 0 advances
                    // normally and writes nothing, which is indistinguishable from the outside
                    // from a clip that binds nothing.
                    _clipInfoScratch.Clear();
                    a.GetCurrentAnimatorClipInfo(0, _clipInfoScratch);
                    if (_clipInfoScratch.Count > 0)
                    {
                        AnimationClip? pc = _clipInfoScratch[0].clip;
                        float pw = _clipInfoScratch[0].weight;
                        if (e.Clip0 == null) e.Clip0 = pc;
                        e.ClipLast = pc;
                        if (pw < e.ClipWeightMin) e.ClipWeightMin = pw;
                        if (pw > e.ClipWeightMax) e.ClipWeightMax = pw;
                    }
                    _clipInfoScratch.Clear();
                    int cc = a.GetCurrentAnimatorClipInfoCount(0);
                    if (cc < e.ClipCountMin) e.ClipCountMin = cc;
                    if (cc > e.ClipCountMax) e.ClipCountMax = cc;
                    e.BoundLast = a.hasBoundPlayables;
                    e.GraphValidLast = a.playableGraph.IsValid();
                    // ---- PHASE 1 of 3 (ModBuild 428): Update, BEFORE Unity's animation phase.
                    // First close the previous frame's end-of-frame -> Update pair: that interval
                    // contains no animation write at all, so anything moving across it is a
                    // between-frames writer putting the pose back.
                    if (e.HaveE)
                        FeedPhase(e, e.EU, e.PhaseEPos, e.PhaseERot, e.PhaseEScale);
                    CapturePhase(e, e.PhaseUPos, e.PhaseURot, e.PhaseUScale);
                    e.HaveU = true;
                    e.HaveL = false;
                    e.HaveE = false;
                    e.PhaseFramesU++;
                    // The visibility census, once per frame. isVisible reports the LAST completed
                    // culling pass, so this reads the frame before — which is exactly the value
                    // Unity's own CullUpdateTransforms decision was taken on.
                    CountVisible(e);
                }
                catch { /* a frame the animator was mid-rebuild — the window keeps sampling */ }
            }
        }

        /// <summary>PHASE 2 of 3: after Unity's animation phase. Everything the animator wrote
        /// this frame is visible here for the first time, and nothing has yet had a chance to put
        /// it back — so <c>Entry.UL</c> IS the animator's write, measured directly.</summary>
        private void SampleLatePhase()
        {
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.ProbeArmed || !e.HaveU)
                    continue;
                try
                {
                    FeedPhase(e, e.UL, e.PhaseUPos, e.PhaseURot, e.PhaseUScale);
                    CapturePhase(e, e.PhaseLPos, e.PhaseLRot, e.PhaseLScale);
                    e.HaveL = true;
                    e.PhaseFramesL++;
                }
                catch { /* mid-rebuild subtree — the next frame samples again */ }
            }
        }

        /// <summary>PHASE 3 of 3: after the frame rendered. What moved between LateUpdate and here
        /// ran after every LateUpdate in the process — an onBeforeRender handler or another
        /// end-of-frame coroutine.</summary>
        private void SampleEndOfFramePhase()
        {
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.ProbeArmed || !e.HaveL)
                    continue;
                try
                {
                    FeedPhase(e, e.LE, e.PhaseLPos, e.PhaseLRot, e.PhaseLScale);
                    CapturePhase(e, e.PhaseEPos, e.PhaseERot, e.PhaseEScale);
                    e.HaveE = true;
                    e.PhaseFramesE++;
                }
                catch { /* as above */ }
            }
        }

        /// <summary>Every probe transform's CURRENT local TRS against the snapshot in
        /// <paramref name="pos"/>/<paramref name="rot"/>/<paramref name="scale"/>, folded into
        /// <paramref name="d"/>. Allocation-free: the worst mover is kept by INDEX and its name is
        /// read once, at log time.</summary>
        private static void FeedPhase(Entry e, PhaseDelta d,
                                      List<Vector3> pos, List<Quaternion> rot, List<Vector3> scale)
        {
            int n = e.ProbeT.Count;
            if (pos.Count < n) n = pos.Count;
            if (rot.Count < n) n = rot.Count;
            if (scale.Count < n) n = scale.Count;
            for (int i = 0; i < n; i++)
            {
                Transform t = e.ProbeT[i];
                if (t == null)
                    continue;
                d.Feed(i,
                       (t.localPosition - pos[i]).magnitude,
                       Quaternion.Angle(t.localRotation, rot[i]),
                       (t.localScale - scale[i]).magnitude);
            }
        }

        /// <summary>Snapshot every probe transform's local TRS into the three lists. Clear+Add
        /// keeps the capacity the arm reserved, so this allocates nothing after the first frame.</summary>
        private static void CapturePhase(Entry e,
                                         List<Vector3> pos, List<Quaternion> rot, List<Vector3> scale)
        {
            pos.Clear();
            rot.Clear();
            scale.Clear();
            for (int i = 0; i < e.ProbeT.Count; i++)
            {
                Transform t = e.ProbeT[i];
                bool live = t != null;
                pos.Add(live ? t!.localPosition : Vector3.zero);
                rot.Add(live ? t!.localRotation : Quaternion.identity);
                scale.Add(live ? t!.localScale : Vector3.one);
            }
        }

        /// <summary>How many renderers under the animator handle report <c>isVisible</c> this
        /// frame. Unity withholds an animator's TRANSFORM WRITE while that count is zero and its
        /// cullingMode is CullUpdateTransforms — the state machine keeps advancing either way,
        /// which is exactly what the ModBuild 426 log shows.</summary>
        private static void CountVisible(Entry e)
        {
            int visible = 0;
            for (int i = 0; i < e.ProbeR.Count; i++)
            {
                Renderer r = e.ProbeR[i];
                if (r != null && r.isVisible)
                    visible++;
            }
            e.VisFrames++;
            if (visible < e.VisMin) e.VisMin = visible;
            if (visible > e.VisMax) e.VisMax = visible;
            if (visible == 0) e.VisZeroFrames++;
        }

        private void LogOpenFrames(Entry e, bool animatorLost)
        {
            _sb.Clear();
            _sb.Append("DOOR OPEN FRAMES '").Append(e.RootName).Append("': ").Append(e.ProbeFrames)
               .Append(" frame(s) sampled over the first ").Append(e.ProbeSpan.ToString("0.0"))
               .Append(" s after the flip").Append(animatorLost ? " (animator LOST mid-window)" : "")
               .Append(" — root motion PRODUCED per frame: deltaPosition min ")
               .Append(e.DeltaPosMin == float.MaxValue ? "n/a" : e.DeltaPosMin.ToString("0.0000"))
               .Append(" max ").Append(e.DeltaPosMax.ToString("0.0000")).Append(" wu, deltaRotation max ")
               .Append(e.DeltaRotMax.ToString("0.00")).Append(" deg; root '")
               .Append(e.Animator != null ? e.Animator.gameObject.name : "?")
               .Append("' local TRS deviation max: pos ").Append(e.RootPosDevMax.ToString("0.0000"))
               .Append(" wu, rot ").Append(e.RootRotDevMax.ToString("0.00")).Append(" deg, scale ")
               .Append(e.RootScaleDevMax.ToString("0.0000"))
               .Append("; EVERY transform under the animator was measured — ").Append(e.ProbeCovered)
               .Append(" of them; the WORST mover: pos ").Append(e.AnyPosDevMax.ToString("0.0000"))
               .Append(" wu on '").Append(ProbeNameAt(e, e.WorstPosIdx)).Append("', rot ")
               .Append(e.AnyRotDevMax.ToString("0.00")).Append(" deg on '").Append(ProbeNameAt(e, e.WorstRotIdx))
               .Append("', scale ").Append(e.AnyScaleDevMax.ToString("0.0000")).Append(" on '")
               .Append(ProbeNameAt(e, e.WorstScaleIdx)).Append('\'')
               .Append("; inTransition frames ").Append(e.Transitions)
               .Append("; normalizedTime ").Append(e.NtFirst.ToString("0.00")).Append(" -> ").Append(e.NtLast.ToString("0.00"))
               .Append("; hasBoundPlayables ").Append(e.Bound0).Append(" -> ").Append(e.BoundLast)
               .Append("; playableGraph valid ").Append(e.GraphValid0).Append(" -> ").Append(e.GraphValidLast)
               .Append("; clipInfoCount ").Append(e.ClipCountMin == int.MaxValue ? "n/a" : e.ClipCountMin.ToString())
               .Append("..").Append(e.ClipCountMax)
               // ModBuild 428, APPENDED (the token above is unchanged): Unity's ONLY built-in
               // transform-write suppression is cullingMode CullUpdateTransforms while NONE of the
               // animator's renderers is visible. ModBuild 426 read one renderer at one instant.
               .Append("; renderers under the handle ").Append(e.ProbeR.Count)
               .Append(", isVisible per frame min ")
               .Append(e.VisMin == int.MaxValue ? "n/a" : e.VisMin.ToString())
               .Append(" max ").Append(e.VisMax).Append(", frames with ZERO visible ")
               .Append(e.VisZeroFrames).Append(" of ").Append(e.VisFrames)
               .Append("; cullingMode ").Append(CullingModeOf(e))
               .Append(", belt ").Append(BeltEnabled ? "ON" : "OFF");
            AppendStateCoverage(e);
            _sb.Append(". READING: ");
            bool produced = e.DeltaPosMax > 1e-4f || e.DeltaRotMax > 0.01f;
            bool rootMoved = e.RootPosDevMax > 1e-4f || e.RootRotDevMax > 0.01f || e.RootScaleDevMax > 1e-4f;
            bool leafMoved = e.AnyPosDevMax > 1e-4f || e.AnyRotDevMax > 0.01f || e.AnyScaleDevMax > 1e-4f;
            if (produced && !rootMoved)
                _sb.Append("root motion is PRODUCED and DISCARDED — the animator hands a delta out every frame and the "
                         + "root transform never takes it: a write war on the root transform (nothing in the game "
                         + "implements OnAnimatorMove; look for a per-frame transform writer on the placement).");
            else if (produced && rootMoved)
                _sb.Append("root motion is produced AND applied to the root — the leaf under it should have moved with "
                         + "it; if the leaf centre still reads unmoved at the samples, the root is put back afterwards.");
            else if (!produced && (rootMoved || leafMoved))
                _sb.Append("no root motion, but a transform under the animator DID move during the clip — the clip binds "
                         + "a child path; the samples after the clip say whether it stays there. The mover is named "
                         + "above, out of all ").Append(e.ProbeCovered).Append(" transform(s) in the subtree.");
            else
                _sb.Append("no root motion and NOT ONE of the ").Append(e.ProbeCovered)
                   .Append(" transform(s) under the animator changed by a ten-thousandth — this covers the WHOLE "
                         + "subtree, every leaf and the frame, not the single renderer transform ModBuild 416 "
                         + "watched while claiming the same thing. So the clip binds NOTHING on this hierarchy (a "
                         + "path it expects that this placement does not have, or curves on properties this probe "
                         + "does not read: material or enabled curves were already ruled out by the sample's "
                         + "material dump). DOOR OPEN CLIP SAMPLE decides that last ambiguity directly.")
                   // ModBuild 428, APPENDED to the sentence above: this whole line is sampled from
                   // Update, i.e. BEFORE Unity's animation phase, so a pose the animator writes and
                   // a second writer puts back before the next Update is bit-identical in every
                   // sample it takes. That is now measured separately — see DOOR OPEN PHASES.
                   .Append(" NOTE: every sample above is taken in Update, BEFORE the animation "
                         + "phase, so this cannot see a write that is undone before the next frame. "
                         + "DOOR OPEN PHASES measures that, and the isVisible census on this line "
                         + "says whether cullingMode CullUpdateTransforms could have withheld the "
                         + "write at all.");
            AppendStateCaveat(e);
            // HW-VERIFY: the one line that decides 'produced and discarded' vs 'binds nothing'.
            VRLog.Note(Name, _sb.ToString());
        }

        /// <summary>
        /// THE THREE-PHASE WRITE-WAR LINE (ModBuild 428) — the one instrument that can tell
        /// "the animator writes nothing" from "the animator writes and someone overwrites it",
        /// which every probe before it read from a single Update-phase sample and therefore could
        /// not. The frame is Update -> [Unity animation] -> LateUpdate -> [render] -> end of frame,
        /// so <c>Update -> LateUpdate</c> IS the animation phase's own write, in isolation.
        /// </summary>
        private void LogOpenPhases(Entry e, bool animatorLost)
        {
            _sb.Clear();
            _sb.Append("DOOR OPEN PHASES '").Append(e.RootName).Append("': ").Append(e.ProbeCovered)
               .Append(" transform(s) under the animator read THREE times in the SAME frame over "
                     + "the first ").Append(e.ProbeSpan.ToString("0.0")).Append(" s after the flip")
               .Append(animatorLost ? " (animator LOST mid-window)" : "")
               .Append(" — Update (before Unity's animation phase) / LateUpdate (after it) / end of "
                     + "frame (after rendering); frames sampled U ").Append(e.PhaseFramesU)
               .Append(", L ").Append(e.PhaseFramesL).Append(", E ").Append(e.PhaseFramesE)
               .Append(". Update->LateUpdate (THE ANIMATION PHASE'S OWN WRITE): ");
            AppendPhase(e, e.UL);
            _sb.Append("; LateUpdate->end of frame (a writer after every LateUpdate): ");
            AppendPhase(e, e.LE);
            _sb.Append("; end of frame->next Update (a writer between frames): ");
            AppendPhase(e, e.EU);
            _sb.Append("; and the Update-phase drift across the WHOLE window (does it STAY): pos ")
               .Append(e.AnyPosDevMax.ToString("0.0000")).Append(" wu, rot ")
               .Append(e.AnyRotDevMax.ToString("0.00")).Append(" deg, scale ")
               .Append(e.AnyScaleDevMax.ToString("0.0000"));
            AppendStateCoverage(e);
            _sb.Append(". READING: ");

            bool stays = e.AnyPosDevMax > 1e-4f || e.AnyRotDevMax > 0.01f || e.AnyScaleDevMax > 1e-4f;
            if (e.PhaseFramesL == 0 || e.PhaseFramesE == 0)
            {
                _sb.Append("INCONCLUSIVE — one of the three phase samples never ran (L ")
                   .Append(e.PhaseFramesL).Append(", E ").Append(e.PhaseFramesE)
                   .Append("). LateUpdate is a Unity message on this watch and end of frame is a "
                         + "WaitForEndOfFrame coroutine on it; a zero for either means the watch was "
                         + "disabled or the coroutine never resumed, NOT that the door held still. "
                         + "Nothing below this point may be read off this window.");
            }
            else if (!e.SawOpen)
            {
                // MODBUILD 429. This branch is NEW and it is the one four of the six ModBuild 428
                // doors should have printed. They printed "THE ANIMATOR NEVER WRITES" instead,
                // which was a true statement about the state the window covered (Idle) and a false
                // one about the question being asked.
                _sb.Append("THE WINDOW NEVER SAW THE 'Open' STATE — the animator stayed out of "
                         + "'Open' for the whole window (states covered are named above; ")
                   .Append(e.StateChanges).Append(" state change(s) over ")
                   .Append(e.ProbeSpan.ToString("0.0")).Append(" s, hard deadline ")
                   .Append(ProbeMaxSeconds.ToString("0")).Append(" s). NOTHING here is a statement "
                         + "about the open clip: every delta above was measured against whatever "
                         + "state WAS running, and a state whose clip holds the closed pose writes "
                         + "zeroes exactly like an animator that binds nothing. This is the shape "
                         + "the ModBuild 428 log had on four of six doors — Idle at the flip, Idle "
                         + "at +1 s, 'Open' only between +1 s and +3 s — and the window was 1.5 s. "
                         + "If this line appears with the window at its hard deadline, the door's "
                         + "rules state opened and the game never played 'Open' on THIS animator "
                         + "within twelve seconds: hunt the gap between Choreographer.OpenDoor and "
                         + "the animator, not the animator itself.");
            }
            else if (e.UL.Moved && !stays)
            {
                _sb.Append("THE ANIMATOR WRITES AND SOMEONE OVERWRITES IT — the animation phase moved '")
                   .Append(ProbeNameAt(e, e.UL.PosIdx >= 0 ? e.UL.PosIdx : e.UL.RotIdx))
                   .Append("' by ").Append(e.UL.PosMax.ToString("0.0000")).Append(" wu / ")
                   .Append(e.UL.RotMax.ToString("0.00"))
                   .Append(" deg every frame and the pose was back at the Update value by the next "
                         + "frame, so the clip binds fine and the door is losing a WRITE WAR. The "
                         + "interval that put it back is named above: a bigger LateUpdate->end-of-frame "
                         + "number means an onBeforeRender or end-of-frame writer, a bigger "
                         + "end-of-frame->next-Update number means a writer between frames "
                         + "(a coroutine, a FixedUpdate, or an early Update). Hunt the writer on '")
                   .Append(ProbeNameAt(e, e.UL.PosIdx >= 0 ? e.UL.PosIdx : e.UL.RotIdx))
                   .Append("' — the mod's own audit found no per-frame writer that can reach a "
                         + "scenario door, so this would name the GAME's.");
            }
            else if (e.UL.Moved)
            {
                _sb.Append("THE DOOR IS ANIMATING AND THE MOVE STAYS — the animation phase writes and "
                         + "nothing takes it back. The clip binds and the vanilla open is working on "
                         + "this door. From ModBuild 429 there is no mod stand-in behind it: this "
                         + "watch switches nothing off, so what is on screen here is the GAME's own "
                         + "picture. If the leaf still LOOKS wrong from here it is a pose question, "
                         + "not a binding question.");
            }
            else if (e.LE.Moved || e.EU.Moved)
            {
                _sb.Append("THE ANIMATION PHASE WROTE NOTHING, BUT SOMETHING ELSE MOVED THE DOOR — "
                         + "the animator is not the mover this window saw. Name the phase above and "
                         + "the transform with it; the animator itself is still unexplained and "
                         + "DOOR OPEN CLIP SAMPLE decides whether its clip binds at all.");
            }
            else
            {
                _sb.Append("THE ANIMATOR NEVER WRITES — nothing moved in ANY of the three phases, so "
                         + "there is no write for anyone to undo and this is NOT a write war. That "
                         + "leaves two causes and this build separates them: either the clip binds "
                         + "nothing on this hierarchy (DOOR OPEN CLIP SAMPLE runs the clip against "
                         + "the live objects and says so directly), or Unity withheld the write — "
                         + "which requires cullingMode CullUpdateTransforms AND zero visible "
                         + "renderers, both printed on the DOOR OPEN FRAMES line beside this one. "
                         + "If that line says 'frames with ZERO visible' is 0 and cullingMode is "
                         + "AlwaysAnimate, the culling explanation is dead and only the binding is "
                         + "left.");
            }
            AppendStateCaveat(e);
            // HW-VERIFY: this is the line that decides family (A) 'binds nothing' against family
            // (B) 'the write is undone' — the whole point of the round.
            VRLog.Note(Name, _sb.ToString());
        }

        /// <summary>
        /// WHICH STATE THE WINDOW COVERED, and every animator term that can make a running state
        /// write nothing (ModBuild 429). Appended to BOTH probe lines and printed for every door,
        /// the two that animated as much as the four that did not: the comparison that decides
        /// this is working-two against failing-four, so a field that reads the same on both sides
        /// has to be visible on both sides to be ruled out.
        /// <para>The terms, and what each one would prove: <c>speed</c> 0 = a latched SMB (the
        /// existing UNLATCH remedy); layer 0 <c>weight</c> 0 or clip <c>weight</c> 0 = a clip that
        /// advances normally and writes nothing, which is bit-identical from the outside to a clip
        /// that binds nothing; <c>active</c>/<c>enabled</c> frames short of the frame count = a
        /// subtree that was switched off for part of the window (the ModBuild 428 belt lines read
        /// <c>active False</c> at belt time and the census read <c>active True</c> a moment later,
        /// and this resolves that disagreement per frame); <c>updateMode</c> and
        /// <c>applyRootMotion</c> for completeness, both expected identical on all six.</para>
        /// </summary>
        private void AppendStateCoverage(Entry e)
        {
            _sb.Append("; STATE COVERAGE: ").Append(e.StateChanges)
               .Append(" state change(s), first hash ").Append(e.StateHash0)
               .Append(", last hash ").Append(e.StateHashLast).Append("; 'Open' ")
               .Append(e.SawOpen
                       ? "first seen " + e.OpenSeenAfter.ToString("0.00") + " s after the flip"
                       : "NEVER SEEN in this window")
               .Append(", frames in 'Open' ").Append(e.OpenFrames).Append(" of ").Append(e.ProbeFrames)
               .Append(", its normalizedTime ")
               .Append(e.OpenNtFirst < 0f ? "n/a" : e.OpenNtFirst.ToString("0.00")).Append(" -> ")
               .Append(e.OpenNtLast < 0f ? "n/a" : e.OpenNtLast.ToString("0.00"))
               .Append("; animator active ").Append(e.ActiveFrames).Append(" of ").Append(e.ProbeFrames)
               .Append(" frame(s), enabled ").Append(e.EnabledFrames)
               .Append("; speed ").Append(e.SpeedMin == float.MaxValue ? "n/a" : e.SpeedMin.ToString("0.00"))
               .Append("..").Append(e.SpeedMax == float.MinValue ? "n/a" : e.SpeedMax.ToString("0.00"))
               .Append("; layerCount ").Append(e.LayerCount).Append(", layer 0 weight ")
               .Append(e.Weight0Min == float.MaxValue ? "n/a" : e.Weight0Min.ToString("0.00"))
               .Append("..").Append(e.Weight0Max == float.MinValue ? "n/a" : e.Weight0Max.ToString("0.00"))
               .Append("; updateMode ").Append(e.UpdateModeLast)
               .Append(", applyRootMotion ").Append(e.RootMotionLast)
               .Append("; layer 0 clip '").Append(e.Clip0 != null ? e.Clip0.name : "none")
               .Append("' -> '").Append(e.ClipLast != null ? e.ClipLast.name : "none")
               .Append("', its weight ")
               .Append(e.ClipWeightMin == float.MaxValue ? "n/a" : e.ClipWeightMin.ToString("0.00"))
               .Append("..").Append(e.ClipWeightMax == float.MinValue ? "n/a" : e.ClipWeightMax.ToString("0.00"))
               .Append("; window ran on animator instance ").Append(e.ProbeAnimatorId)
               .Append(" (the clip sample that follows runs on the instance it names, so the two "
                     + "lines describe the same objects only when those ids match)");
        }

        /// <summary>The one sentence that stops every verdict on these two lines from being read
        /// as a statement about the OPEN clip when the window never covered it. Appended last, so
        /// it is the final clause of the line whatever the verdict above it was.</summary>
        private void AppendStateCaveat(Entry e)
        {
            if (e.SawOpen)
                return;
            _sb.Append(" CAVEAT: THE 'Open' STATE WAS NEVER COVERED by this window, so nothing on "
                     + "this line — not the deltas, not the phase pairs, not the isVisible census "
                     + "and not the verdict above — says anything about the door's open clip. It "
                     + "describes the state that WAS running. This is exactly how ModBuild 428 "
                     + "reported four doors as animators that never write: they were in Idle for "
                     + "the whole 1.5 s window and entered 'Open' after it closed.");
        }

        /// <summary>One phase pair's three maxima and the transform that produced each.</summary>
        private void AppendPhase(Entry e, PhaseDelta d)
        {
            _sb.Append("pos ").Append(d.PosMax.ToString("0.0000")).Append(" wu on '")
               .Append(ProbeNameAt(e, d.PosIdx)).Append("', rot ").Append(d.RotMax.ToString("0.00"))
               .Append(" deg on '").Append(ProbeNameAt(e, d.RotIdx)).Append("', scale ")
               .Append(d.ScaleMax.ToString("0.0000")).Append(" on '")
               .Append(ProbeNameAt(e, d.ScaleIdx)).Append('\'');
        }

        /// <summary>The name of the probe transform at <paramref name="idx"/>, read only when the
        /// line is built — the per-frame path keeps the INDEX so it allocates nothing.</summary>
        private static string ProbeNameAt(Entry e, int idx)
        {
            if (idx < 0 || idx >= e.ProbeT.Count)
                return "none";
            Transform t = e.ProbeT[idx];
            return t != null ? t.name : "destroyed";
        }

        // ---------------------------------------------------------------- resolution

        private static CObjectDoor? ResolveDoor(UnityGameEditorDoorProp prop, ScenarioState state)
        {
            try
            {
                UnityGameEditorObject? obj = prop.GetComponent<UnityGameEditorObject>();
                if (obj != null && obj.PropObject is CObjectDoor byObject)
                    return byObject;
                string name = prop.gameObject.name;
                List<CObjectProp>? doors = state.DoorProps;
                if (doors != null)
                {
                    foreach (CObjectProp p in doors)
                    {
                        if (p is CObjectDoor d && p.InstanceName == name)
                            return d;
                    }
                }
            }
            catch { /* rule library mid-load — retried in 5 s */ }
            return null;
        }

        private void ResolveAnimator(Entry e, float now, bool force)
        {
            if (e.Animator != null && !force)
                return;
            if (!force && now < e.NextAnimatorResolve)
                return;
            e.NextAnimatorResolve = now + 1f;
            Animator? a = null;
            try { a = MF.GetGameObjectAnimator(e.Prop.gameObject); }
            catch { /* a dying subtree — retried in 1 s */ }
            if (a == null)
            {
                e.Animator = null;
                return;
            }
            int id = a.GetInstanceID();
            if (id != e.AnimatorId)
            {
                if (e.AnimatorId != 0)
                {
                    e.Rebuilds++;          // the leaf assembly was re-instantiated under us
                    e.RebuildsSinceOpen++;
                }
                e.AnimatorId = id;
                e.InstanceSeenAt = now;
                e.ObservedOpen = false;
                e.DissolveReasserted = false;
                // A new leaf assembly is a new hierarchy, with its own curve paths: the clip
                // sample is asked again. Nothing is written to it and nothing is decided by it.
                e.ClipSampled = false;
            }
            e.Animator = a;
        }

        // ---------------------------------------------------------------- THE BELT (ModBuild 428)

        /// <summary>
        /// <c>[Compat] DoorAnimateOffscreen</c> — read live, so the belt can be A/B'd inside one
        /// session. The entry is bound in Plugin.cs; a null read (a config file that has not been
        /// created yet) falls back to the SHIPPED default rather than to <c>false</c>, because the
        /// default IS the fix.
        /// </summary>
        private static bool BeltEnabled
        {
            get
            {
                try { return Plugin.DoorAnimateOffscreen?.Value ?? Defaults.DoorAnimateOffscreen; }
                catch { return Defaults.DoorAnimateOffscreen; }
            }
        }

        /// <summary>
        /// PUT THE VANILLA ASSUMPTION BACK. The game's whole door-open behaviour is one call —
        /// <c>Choreographer.OpenDoor</c> plays the state "Open" on the door's animator and does
        /// nothing else (decompiled Choreographer.cs:13305). Unity's default for a placed prop
        /// animator is <c>AnimatorCullingMode.CullUpdateTransforms</c>: the state machine keeps
        /// advancing while none of that animator's renderers is visible, and the TRANSFORM WRITE is
        /// withheld. That is the only transform-write suppression Unity has, and it is the exact
        /// shape of this defect (state "Open" at normalizedTime 9.28 with every transform unmoved,
        /// ModBuild 426 log). The flat game never met it: a top-down camera holding the whole room
        /// has the door on screen whenever it opens. A first-person camera standing IN the room
        /// does not, so this is a VR-only condition the authors never had to consider.
        /// <para>What is written and what is owed back: one enum per door animator, every replaced
        /// value remembered on the entry, and every one of them put back when the dial goes off, on
        /// a scenario change, when the door prop dies and on uninstall. A row whose animator has
        /// been destroyed under us (Apparance re-instantiates placed content freely) is pruned —
        /// nothing is owed to an object that is gone. Nothing else is touched. MP-safe: every peer
        /// animates its own copy of its own scene, nothing goes on the wire.</para>
        /// </summary>
        private void ApplyBelt(Entry e, float now, bool force)
        {
            if (!BeltEnabled)
            {
                if (e.BeltOn.Count > 0)
                    RestoreBelt(e, "the belt was switched off");
                return;
            }
            if (!force && now < e.NextBeltScan)
                return;
            e.NextBeltScan = now + BeltScanSeconds;
            if (e.Prop == null)
                return;
            _beltScratch.Clear();
            try { e.Prop.GetComponentsInChildren(includeInactive: true, _beltScratch); }
            catch { return; } // a subtree mid-teardown — the next scan tries again
            for (int i = 0; i < _beltScratch.Count; i++)
            {
                Animator a = _beltScratch[i];
                if (a == null || a.runtimeAnimatorController == null)
                    continue; // MF.GetGameObjectAnimator's own test: an animator with no controller
                if (IndexOfBelt(e, a) >= 0)
                    continue; // already asserted on THIS instance
                AnimatorCullingMode before;
                try { before = a.cullingMode; }
                catch { continue; } // a dying animator — the next scan tries again
                if (before != AnimatorCullingMode.AlwaysAnimate)
                {
                    try { a.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
                    catch { continue; }
                }
                e.BeltOn.Add(a);
                e.BeltMode0.Add(before);
                e.BeltWrites++;
                // A door whose content is rebuilt in a loop would otherwise print a line per
                // rebuild. The first two print; the rest are counted on the line's assert number.
                if (e.BeltWrites <= 2)
                    LogBelt(e, a, before);
            }
            _beltScratch.Clear();
            PruneDeadBelts(e);
        }

        /// <summary>Where <paramref name="a"/> sits in the belt ledger, or -1. A linear walk over
        /// at most a handful of animators per door, and it runs at the scan cadence, not per frame.</summary>
        private static int IndexOfBelt(Entry e, Animator a)
        {
            for (int i = 0; i < e.BeltOn.Count; i++)
            {
                if (ReferenceEquals(e.BeltOn[i], a))
                    return i;
            }
            return -1;
        }

        /// <summary>Drop ledger rows whose animator has been destroyed (Apparance destroys and
        /// re-instantiates a door's content freely). Nothing is owed to an object that is gone, and
        /// the row would otherwise keep the entry growing for the life of the scenario.</summary>
        private static void PruneDeadBelts(Entry e)
        {
            for (int i = e.BeltOn.Count - 1; i >= 0; i--)
            {
                if (e.BeltOn[i] == null)
                {
                    e.BeltOn.RemoveAt(i);
                    e.BeltMode0.RemoveAt(i);
                }
            }
        }

        /// <summary>Give every belted animator back the cullingMode it had. Safe on a destroyed
        /// animator, on a door that never got the belt, and called twice.</summary>
        private void RestoreBelt(Entry e, string why)
        {
            int restored = 0;
            for (int i = 0; i < e.BeltOn.Count; i++)
            {
                Animator a = e.BeltOn[i];
                if (a == null)
                    continue;
                try { a.cullingMode = e.BeltMode0[i]; restored++; }
                catch { /* the animator went with its scene — nothing is owed */ }
            }
            e.BeltOn.Clear();
            e.BeltMode0.Clear();
            if (restored > 0)
            {
                VRLog.Info(Name, $"DoorOpenWatch: gave {restored} animator(s) under '{e.RootName}' "
                                 + $"their authored cullingMode back ({why}).");
            }
        }

        private void RestoreAllBelts(string why)
        {
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                try { RestoreBelt(kv.Value, why); }
                catch { /* one door's teardown may not stop the rest */ }
            }
        }

        /// <summary>The live cullingMode of the door's animator, for the DOOR OPEN FRAMES line —
        /// the value that decides whether Unity's suppression could have applied at all.</summary>
        private static string CullingModeOf(Entry e)
        {
            Animator? a = e.Animator;
            if (a == null)
                return "no animator";
            try { return a.cullingMode.ToString(); }
            catch { return "?"; }
        }

        private void LogBelt(Entry e, Animator a, AnimatorCullingMode before)
        {
            _sb.Clear();
            _sb.Append("DOOR ANIMATE OFFSCREEN '").Append(e.RootName).Append("': animator '")
               .Append(PathUnder(a.transform, e.Prop != null ? e.Prop.transform : a.transform))
               .Append("' (controller '").Append(ControllerName(a)).Append("', active ")
               .Append(a.gameObject.activeInHierarchy).Append(", resolved-by-MF ")
               .Append(ReferenceEquals(a, e.Animator))
               .Append(") cullingMode ").Append(before).Append(" -> ")
               .Append(a.cullingMode).Append(" — assert #").Append(e.BeltWrites)
               .Append(" on this door, ").Append(e.BeltOn.Count)
               .Append(" animator(s) belted here so far. WHY: the game opens a door with ONE call, "
                     + "MF.GameObjectAnimatorPlay(door, \"Open\"), and Unity's "
                     + "CullUpdateTransforms withholds an animator's TRANSFORM WRITE while none of "
                     + "its renderers is visible while still advancing the state machine — which is "
                     + "the picture the ModBuild 426 log shows (state 'Open' at normalizedTime 9.28, "
                     + "not one transform moved). The game itself manufactures the precondition: it "
                     + "disables every renderer under a door prop until the room is revealed "
                     + "(decompiled UnityGameEditorRuntime.cs:811-818 and ApparanceLayer.cs:74-81) "
                     + "and it never writes cullingMode anywhere, so whatever each door prefab was "
                     + "serialised with is what runs. The flat game's top-down camera never produced "
                     + "the off-camera half of that condition; a first-person VR camera produces it "
                     + "constantly. EVERY animator with a controller under the prop is belted, not "
                     + "just the one MF resolves: a door prop carries two animated subtrees and MF "
                     + "searches with the overload that skips INACTIVE ones, so which one it returns "
                     + "moves with the room's visibility. This IS at least part of the cause: on "
                     + "ModBuild 428, with this belt on, the user saw a door's open animation for "
                     + "the first time. Every replaced value is restored on a scenario change and "
                     + "on uninstall; [Compat] DoorAnimateOffscreen turns it off live.");
            // HW-VERIFY: the candidate FIX's own line — a build where this never prints has not
            // tested the belt, whatever the door did.
            VRLog.Note(Name, _sb.ToString());
        }

        private void CaptureLeaf(Entry e)
        {
            e.LeafIds.Clear();
            e.LeafCount = 0;
            e.LeafCenter = Vector3.zero;
            Animator? a = e.Animator;
            if (a == null)
                return;
            _rendererScratch.Clear();
            a.GetComponentsInChildren(includeInactive: true, _rendererScratch);
            Vector3 sum = Vector3.zero;
            foreach (Renderer r in _rendererScratch)
            {
                if (r == null)
                    continue;
                e.LeafIds.Add(r.GetInstanceID());
                sum += r.transform.position;
            }
            e.LeafCount = e.LeafIds.Count;
            if (e.LeafCount > 0)
                e.LeafCenter = sum / e.LeafCount;
            _rendererScratch.Clear();
        }

        // ---------------------------------------------------------------- the lines

        private void LogOpened(Entry e, float now)
        {
            _sb.Clear();
            _sb.Append("DOOR OPENED '").Append(e.RootName).Append("' (")
               .Append(DescribeDoor(e.Door)).Append("): the rules state flipped to open "
               + "(CObjectDoor.DoorIsOpen, read-only — a regular 'Tür öffnen' and a cheat's "
               + "ForceActivate both land here). ");
            AppendAnimator(e, now);
            AppendAnimatorCensus(e);
            AppendLeafCensus(e, "leaf renderers under the game's own animator handle at the flip");
            _sb.Append(" Samples follow at +1 s, +3 s and +8 s; the +8 s line's 'still enabled' must "
                     + "read 0 once the game's 'Open' has hidden the leaf. If it reads the full count "
                     + "with the animator in 'Open' at normalizedTime >= 1 and the leaf unmoved, the clip "
                     + "hides nothing and the hide lives elsewhere (an SMB destroy, or a rebuild that "
                     + "re-placed a closed leaf — the sample says REPLACED when that happened). ");
            AppendForensicsIfAnimator(e);
            // HW-VERIFY: the door-leaf regression (2026-09-03, runde_tür_problem.jpg) is decided by this
            // line and its samples — the tier must survive the default log level.
            VRLog.Note(Name, _sb.ToString());
        }

        private void LogOpenedSample(Entry e, int stage, float now)
        {
            _sb.Clear();
            _sb.Append("DOOR OPENED +").Append(SampleAt[stage].ToString("0")).Append(" s '")
               .Append(e.RootName).Append("': ");
            AppendAnimator(e, now);
            AppendLeafCensus(e, "leaf renderers captured at the flip");
            _sb.Append(" Wall-fade restores that DECLINED a foreign enable so far this session: ")
               .Append(WallSegmentFade.DeclinedForeignEnables)
               .Append(" (each one is a renderer the GAME switched off while the mod held a rule on "
                     + "it — before ModBuild 414 the restore would have switched it back on).");
            if (stage == SampleAt.Length - 1)
                _sb.Append(" LAST SAMPLE — 'still enabled' must read 0 here.");
            _sb.Append(' ');
            AppendForensicsIfAnimator(e);
            // HW-VERIFY: the +8 s sample is the reading the next hardware round is waiting on.
            VRLog.Note(Name, _sb.ToString());
        }

        private void LogReasserted(Entry e, Animator a, AnimatorStateInfo st, bool played, float now)
        {
            if (e.Reasserts > 3)
                return; // the cap line reports the rest
            _sb.Clear();
            _sb.Append("DOOR OPEN RE-ASSERTED '").Append(e.RootName).Append("' (#").Append(e.Reasserts)
               .Append("): the rules say open but the door's animator '").Append(a.gameObject.name)
               .Append("' (controller '").Append(ControllerName(a)).Append("') was in state hash ")
               .Append(st.shortNameHash).Append(" at normalizedTime ").Append(st.normalizedTime.ToString("0.00"))
               .Append(", never seen in 'Open' on this instance — instance ")
               .Append(e.AnimatorIdAtFlip != 0 && e.AnimatorId != e.AnimatorIdAtFlip
                       ? "REPLACED since the open (the leaf assembly was re-instantiated: an Apparance rebuild of the door's HexDoor content)"
                       : "unchanged since the open (the game's one deferred replay never landed)")
               .Append(" [replaced ").Append(e.RebuildsSinceOpen).Append(" time(s) since this open, ")
               .Append(e.Rebuilds).Append(" since first sight]")
               .Append(". The mod replayed the game's own call, MF.GameObjectAnimatorPlay(root, \"Open\") — "
                     + "Choreographer.OpenDoor's PlacementCompleteAction path — and it returned ")
               .Append(played ? "true" : "FALSE (no 'Open' state on layer 0, or no controller)")
               .Append(". ").Append((now - e.OpenedAt).ToString("0.0")).Append(" s after the open.");
            // HW-VERIFY: fires only when the remedy runs; names the mechanism (REPLACED vs unchanged).
            VRLog.Note(Name, _sb.ToString());
        }

        private void LogReassertCap(Entry e, AnimatorStateInfo st)
        {
            _sb.Clear();
            _sb.Append("DOOR OPEN RE-ASSERT CAP '").Append(e.RootName).Append("': replayed 'Open' ")
               .Append(e.Reasserts).Append(" times and the animator is STILL not in 'Open' (state hash ")
               .Append(st.shortNameHash).Append("). Either the door's content is rebuilt over and over "
                     + "(each rebuild is a fresh closed leaf — look for the churn in the healer's "
                     + "'completed N stalled renderer(s)' cadence) or 'Open' transitions straight out "
                     + "again on this controller. The watch stops replaying this door.");
            // HW-VERIFY: a cap hit is a finding in itself — a rebuild loop on a door prop.
            VRLog.Alert(Name, _sb.ToString());
        }

        private void LogNoOpenState(Entry e, Animator a, AnimatorStateInfo st)
        {
            _sb.Clear();
            _sb.Append("DOOR OPEN NOT REPLAYABLE '").Append(e.RootName).Append("': the animator '")
               .Append(a.gameObject.name).Append("' (controller '").Append(ControllerName(a))
               .Append("') has no 'Open' state on layer 0 (state hash now ").Append(st.shortNameHash)
               .Append("), so MF.GameObjectAnimatorPlay would refuse it too — this door is not "
                     + "animated by the 'Open' state and the watch leaves it alone.");
            // HW-VERIFY: tells the next round the door kit does not use the 'Open' state at all.
            VRLog.Note(Name, _sb.ToString());
        }

        private void LogUnlatched(Entry e, Animator a, AnimatorStateInfo st, float scale, float now)
        {
            if (e.Unlatched > 2)
                return;
            _sb.Clear();
            _sb.Append("DOOR ANIMATOR UNLATCHED '").Append(e.RootName).Append("': animator '")
               .Append(a.gameObject.name).Append("' sat in 'Open' at normalizedTime ")
               .Append(st.normalizedTime.ToString("0.00")).Append(" with speed 0 while the global clock "
                     + "runs at ").Append(scale.ToString("0.00"))
               .Append(" — an SMB latched a paused clock in OnStateEnter (DelayedDestroySMB.cs:63 / "
                     + "DelayedDeactivatePropAnimSMB.cs:58 latch once and never refresh). Speed set to the "
                     + "clock's value; the clip can finish now. ")
               .Append((now - e.OpenedAt).ToString("0.0")).Append(" s after the open.");
            // HW-VERIFY: fires only when the latch was real — the second mechanism, caught by name.
            VRLog.Note(Name, _sb.ToString());
        }

        private void AppendAnimator(Entry e, float now)
        {
            Animator? a = e.Animator;
            if (a == null)
            {
                _sb.Append("Animator: NONE under the root right now (MF.GetGameObjectAnimator found no "
                         + "controller — the leaf content is not placed; the game would defer its 'Open' "
                         + "to PlacementCompleteAction). ");
                return;
            }
            string stateName = "?";
            float nt = 0f;
            int hash = 0;
            bool inTransition = false;
            try
            {
                AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
                hash = st.shortNameHash;
                nt = st.normalizedTime;
                stateName = st.IsName("Open") ? "Open" : st.IsName("Unlock") ? "Unlock"
                    : st.IsName("Unlocked_Idle") ? "Unlocked_Idle" : st.IsName("Closed") ? "Closed"
                    : st.IsName("Idle") ? "Idle" : "other";
                inTransition = a.IsInTransition(0);
            }
            catch { /* controller mid-swap */ }
            bool paused = false;
            float scale = -1f;
            try { paused = TimeManager.IsPaused; scale = TimeManager.TimeScale; }
            catch { /* TimeManager not up */ }
            _sb.Append("Animator '").Append(a.gameObject.name).Append("' (controller '")
               .Append(ControllerName(a)).Append("', instance ").Append(e.AnimatorId)
               // APPENDED, ModBuild 428: this clause was `Rebuilt ? " REPLACED since the open" :
               // " unchanged since the open"` off a write-once bool, so from the first rebuild on
               // it read REPLACED forever — including on an instance that WAS the one from the
               // flip. It now compares the ids and carries both counts.
               .Append(e.AnimatorIdAtFlip == 0 ? ", no flip instance recorded"
                     : e.AnimatorId == e.AnimatorIdAtFlip ? ", the SAME instance as at the flip"
                     : ", NOT the instance from the flip")
               .Append("; replaced ").Append(e.RebuildsSinceOpen).Append(" time(s) since this open, ")
               .Append(e.Rebuilds).Append(" time(s) since the watch first saw this door")
               .Append("): state ").Append(stateName).Append(" (hash ").Append(hash)
               .Append(") normalizedTime ").Append(nt.ToString("0.00"))
               .Append(inTransition ? " in transition" : "")
               .Append(", speed ").Append(a.speed.ToString("0.00"))
               .Append(", enabled ").Append(a.isActiveAndEnabled)
               .Append(", cullingMode ").Append(a.cullingMode)
               .Append("; global clock timeScale ").Append(scale.ToString("0.00"))
               .Append(paused ? " PAUSED" : " running")
               .Append("; 'Open' observed on this instance: ").Append(e.ObservedOpen)
               .Append("; re-asserts ").Append(e.Reasserts).Append(", unlatches ").Append(e.Unlatched)
               .Append("; ").Append((now - e.OpenedAt).ToString("0.0")).Append(" s since the open. ");
        }

        /// <summary>
        /// EVERY animator with a controller under the door prop, not just the one the game's own
        /// resolver returns (ModBuild 428). The prop root carries TWO animated subtrees — the
        /// Apparance-generated content under <c>Generated Content</c>, and the door prefab clone
        /// <c>ApparanceLayer.Create</c> parents into it (decompiled ApparanceLayer.cs:72) — and
        /// <c>MF.GetGameObjectAnimator</c> takes the FIRST hit from
        /// <c>GetComponentsInChildren&lt;Animator&gt;()</c>, the overload that SKIPS INACTIVE
        /// objects (decompiled MF.cs:135-146). The whole <c>Generated Content</c> subtree is
        /// deactivated while a room is in Preview visibility, so which animator the game plays
        /// "Open" on can differ between two calls in the same scenario. Every line this watch has
        /// printed so far named ONE animator and never said which of the two it was.
        /// </summary>
        private void AppendAnimatorCensus(Entry e)
        {
            if (e.Prop == null)
            {
                _sb.Append("Animator census: the door prop is gone. ");
                return;
            }
            _beltScratch.Clear();
            try { e.Prop.GetComponentsInChildren(includeInactive: true, _beltScratch); }
            catch { _beltScratch.Clear(); }
            int withController = 0;
            for (int i = 0; i < _beltScratch.Count; i++)
            {
                Animator x = _beltScratch[i];
                if (x != null && x.runtimeAnimatorController != null)
                    withController++;
            }
            _sb.Append("Animator census under the prop: ").Append(_beltScratch.Count)
               .Append(" animator(s), ").Append(withController).Append(" with a controller");
            for (int i = 0; i < _beltScratch.Count; i++)
            {
                Animator x = _beltScratch[i];
                if (x == null || x.runtimeAnimatorController == null)
                    continue;
                _sb.Append(" | '").Append(PathUnder(x.transform, e.Prop.transform))
                   .Append("' controller '").Append(ControllerName(x)).Append("', active ")
                   .Append(x.gameObject.activeInHierarchy).Append(", enabled ").Append(x.enabled)
                   .Append(", cullingMode ").Append(x.cullingMode)
                   .Append(ReferenceEquals(x, e.Animator) ? ", THE ONE MF RESOLVES" : "")
                   .Append(IndexOfBelt(e, x) >= 0 ? ", belted" : ", NOT belted");
            }
            _beltScratch.Clear();
            _sb.Append(". READING: more than one 'with a controller' means the game's own resolver "
                     + "is choosing between two door subtrees, and it uses the search that skips "
                     + "INACTIVE objects — so an entry here reading active False is an animator the "
                     + "game cannot see right now and could start playing 'Open' on later. ");
        }

        private void AppendLeafCensus(Entry e, string what)
        {
            Animator? a = e.Animator;
            int total = 0, gameDisabled = 0, modHeld = 0, stillEnabled = 0, destroyed = 0;
            float moved = 0f;
            int named = 0, namedOff = 0;
            var names = new StringBuilder(160);
            var offNames = new StringBuilder(160);
            if (a != null)
            {
                _rendererScratch.Clear();
                a.GetComponentsInChildren(includeInactive: true, _rendererScratch);
                Vector3 sum = Vector3.zero;
                var liveIds = new HashSet<int>();
                foreach (Renderer r in _rendererScratch)
                {
                    if (r == null)
                        continue;
                    total++;
                    liveIds.Add(r.GetInstanceID());
                    sum += r.transform.position;
                    bool enabledNow = r.enabled && r.gameObject.activeInHierarchy;
                    string? hold = null;
                    try { hold = WallSegmentFade.DescribeHold(r); }
                    catch { /* wall system mid-teardown */ }
                    if (hold != null)
                        modHeld++;
                    if (!enabledNow)
                    {
                        gameDisabled++;
                        // APPENDED, ModBuild 429. Until this build the census named the holds only
                        // on renderers that were still ON, so a door whose leaf read "game-disabled
                        // 4, mod holds a rule on 4" never said WHICH lane held the four that were
                        // off — and that count is the one question the user's ruling turns on.
                        if (hold != null && namedOff < 4)
                        {
                            namedOff++;
                            offNames.Append(offNames.Length == 0 ? "" : ", ").Append('\'')
                                    .Append(r.name).Append("' [mod: ").Append(hold).Append(']');
                        }
                    }
                    else
                    {
                        stillEnabled++;
                        if (named < 3)
                        {
                            named++;
                            names.Append(names.Length == 0 ? "" : ", ").Append('\'').Append(r.name).Append('\'')
                                 .Append(hold != null ? " [mod: " + hold + "]" : " [no mod rule]");
                        }
                    }
                }
                foreach (int id in e.LeafIds)
                {
                    if (!liveIds.Contains(id))
                        destroyed++;
                }
                if (total > 0 && e.LeafCount > 0)
                    moved = ((sum / total) - e.LeafCenter).magnitude;
                _rendererScratch.Clear();
            }
            _sb.Append(what).Append(": ").Append(total).Append(" now (")
               .Append(e.LeafCount).Append(" at the flip, ").Append(destroyed)
               .Append(" of those DESTROYED since); game-disabled or inactive ").Append(gameDisabled)
               .Append(", mod holds a rule on ").Append(modHeld)
               .Append(", still enabled ").Append(stillEnabled)
               .Append(stillEnabled > 0 ? " — " + names : "")
               // APPENDED, ModBuild 429: WHICH mod lane holds a renderer that is currently OFF.
               // From this build this watch holds NONE of them (it switches nothing off any more),
               // so every tag printed here names a DIFFERENT subsystem writing door content — the
               // exact thing the user's ruling of 2026-09-05 says must not happen.
               .Append("; of the ").Append(gameDisabled)
               .Append(" that are off or inactive, the mod holds a rule on ").Append(namedOff == 0 ? "none" : offNames.ToString())
               .Append(" (this watch holds none of them from ModBuild 429 on, so a tag here names "
                     + "another subsystem)")
               .Append("; leaf centre moved ").Append(moved.ToString("0.00")).Append(" wu since the flip.");
        }

        private void AppendForensicsIfAnimator(Entry e)
        {
            Animator? a = e.Animator;
            if (a == null || !a.isActiveAndEnabled)
                return;
            AnimatorStateInfo st;
            try { st = a.GetCurrentAnimatorStateInfo(0); }
            catch { return; }
            AppendLeafForensics(e, a, st);
        }

        private static string ControllerName(Animator a)
        {
            try
            {
                RuntimeAnimatorController? c = a.runtimeAnimatorController;
                return c != null ? c.name : "<none>";
            }
            catch { return "<?>"; }
        }

        private static string DescribeDoor(CObjectDoor? d)
        {
            if (d == null)
                return "door state unresolved";
            try
            {
                return $"type {d.DoorType}, lock {d.LockType}, entrance {d.IsDungeonEntrance}, exit {d.IsDungeonExit}";
            }
            catch { return "door state unreadable"; }
        }
    }
}

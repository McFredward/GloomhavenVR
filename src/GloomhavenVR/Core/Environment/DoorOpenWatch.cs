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
        public bool Rebuilt;
        public int Reasserts;
        public float LastReassert;
        public bool NoOpenState;
        public bool CapAlerted;
        public int Unlatched;
        /// <summary>REMEDY C fired on the current animator instance (once per instance).</summary>
        public bool DissolveReasserted;
        public int SampleStage;
        public float NextSample;
        public readonly List<int> LeafIds = new(16);
        public int LeafCount;
        public Vector3 LeafCenter;
    }

    private sealed class Watch : MonoBehaviour
    {
        private const float TickSeconds = 0.25f;
        private const float GraceSeconds = 1.5f;
        private const float FirstSightGraceSeconds = 5f;
        private const float ReassertSpacingSeconds = 2f;
        private static readonly float[] SampleAt = { 1f, 3f, 8f };
        private static readonly int OpenHash = Animator.StringToHash("Open");

        private readonly List<UnityGameEditorDoorProp> _props = new(16);
        private readonly Dictionary<int, Entry> _entries = new(16);
        private readonly List<Renderer> _rendererScratch = new(32);
        private readonly List<int> _deadScratch = new(4);
        private readonly StringBuilder _sb = new(1024);
        private ScenarioState? _state;
        private float _nextTick;
        private System.Action? _tick;

        private void Awake() => _tick = Tick; // cached delegate — TickGuard hot-path contract

        private void Update() => TickGuard.Run("Core.DoorOpenWatch", _tick!, Name);

        private void Tick()
        {
            if (!VRSession.IsRunning)
                return;
            float now = Time.unscaledTime;
            if (now < _nextTick)
                return;
            _nextTick = now + TickSeconds;

            ScenarioState? state = null;
            try { state = ScenarioManager.CurrentScenarioState; }
            catch { /* rule library not ready */ }
            if (!ReferenceEquals(state, _state))
            {
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
                _entries.Remove(dead);

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
                CaptureLeaf(e);
                LogOpened(e, now);
                return;
            }
            if (!open)
            {
                e.WasOpen = false; // a scenario restart closes doors again — start over on the next open
                return;
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
                    e.Rebuilt = true; // the leaf assembly was re-instantiated under us
                e.AnimatorId = id;
                e.InstanceSeenAt = now;
                e.ObservedOpen = false;
                e.DissolveReasserted = false;
            }
            e.Animator = a;
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
               .Append(e.Rebuilt ? "REPLACED since the open (the leaf assembly was re-instantiated: an Apparance rebuild of the door's HexDoor content)"
                                 : "unchanged since the open (the game's one deferred replay never landed)")
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
               .Append(e.Rebuilt ? " REPLACED since the open" : " unchanged since the open")
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

        private void AppendLeafCensus(Entry e, string what)
        {
            Animator? a = e.Animator;
            int total = 0, gameDisabled = 0, modHeld = 0, stillEnabled = 0, destroyed = 0;
            float moved = 0f;
            int named = 0;
            var names = new StringBuilder(160);
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
                        gameDisabled++;
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

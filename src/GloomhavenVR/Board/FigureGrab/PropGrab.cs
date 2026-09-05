using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using Script.Controller;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// PROP DISCOVERY — the registry that finds chests, gold piles, traps, obstacles, quest items and
/// resources and hands each one to the hands as a <see cref="GrabbableProp"/>.
///
/// <para><b>WHY A SECOND REGISTRY EXISTS AT ALL, stated once so nobody re-derives it.</b> The
/// figure registry (<c>FigureGrabDriver.RefreshRegistry</c>) walks
/// <c>WorldspaceUITools._panelUIControllers</c> and demands a <c>CInteractableActor</c>, which
/// resolves its actor from <c>GetComponentInParent&lt;CharacterManager&gt;()</c> — characters and
/// monsters only. Its prop annex walked <c>Choreographer.m_ClientObjects</c> instead. The
/// ModBuild 335 hardware census settled what that annex could ever reach, verbatim:</para>
/// <code>
/// [Props] census: Choreographer.m_ClientObjects held 0 entr(y/ies), 0 with an ActorBehaviour,
/// 0 liftable by import type, 0 of those drawing anything, 0 adopted.
/// ScenarioState.Props held 15 prop(s), 14 liftable (4 of the 4 sampled resolved to a GameObject).
/// </code>
/// <para>Zero, against fourteen. A prop reaches <c>m_ClientObjects</c> only by having a
/// <c>CObjectActor</c>, and a prop is given one only when it is configured for HEALTH
/// (CMap.cs:502-518; <c>CObjectActor.SetAttachedToProp</c> refuses independently at
/// CObjectActor.cs:99-104). Chests, gold piles, quest items, resources and non-destructible
/// obstacles have none — and the entry a prop WITH health does get is an invisible
/// <c>PropDummyObject</c> standing on the hex, not the mesh. So that annex is gone and this is
/// what replaced it: <c>ScenarioManager.CurrentScenarioState.Props</c> is the population, and
/// <c>ObjectCacheService.GetPropObject(prop)</c> is the visual
/// (<c>_propsCache</c>, a <c>Dictionary&lt;CObjectProp, GameObject&gt;</c> written by every
/// prop-instantiating site: Choreographer.SpawnProp:13159, PlaceRandomProps:15459,
/// DelayedDropSMB.cs:181, UnityGameEditorRuntime.cs:784/:832, ClientScenarioManager.cs:220).</para>
///
/// <para><b>THE VISUALS ARRIVE LATE, and the same log proved it.</b> At the first census
/// <c>0 of the 4 sampled resolved to a GameObject</c> and <c>collider=no</c>; seconds later
/// <c>4 of 4</c> and <c>collider=yes</c>. Discovery therefore may not decide once. It keeps
/// looking — but on a CHANGE GATE and a bounded settle budget, never as a per-frame scene sweep:
/// <c>FindObjectsOfType</c> is a recorded trap in this project ("near-free" has shipped as a
/// per-frame cost twice) and nothing here does one. Once every liftable prop has resolved, a
/// scan costs one <c>List.Count</c> comparison and returns.</para>
///
/// <para><b>NO ELECTION LOOP.</b> A prop's pick-radius gate lives in
/// <see cref="GrabbableProp.AllowsHand"/>, evaluated by the <c>ProximityGrabber</c> inside the
/// loop it already runs. There is no per-hand pass here, and props and figures therefore compete
/// in ONE election rather than two that would have to be arbitrated against each other.</para>
///
/// <para><b>MULTIPLAYER.</b> Local-only and additive: nothing is sent, nothing is expected, an
/// unmodded peer sees a chest that never moves. The seam is documented in
/// <see cref="HeldProps"/>.</para>
/// </summary>
internal static class PropGrab
{
    /// <summary>Seconds between discovery scans while the population is still settling. Two
    /// seconds is the cadence the prop census already uses, and the census is what measured the
    /// late arrival this scan exists to survive.</summary>
    private const float ScanIntervalSeconds = 2f;

    /// <summary>
    /// <b>THIS BUDGET IS GONE (ModBuild 367), AND ITS REMOVAL IS HALF THE 2026-09-03 FIX.</b>
    ///
    /// <para>It used to read: <i>"Scans allowed after the prop list last CHANGED SIZE, while
    /// liftable props are still unresolved. Twelve at the interval above covers ~24 s — the whole
    /// of the load and reveal — and then costs nothing. A hard budget rather than a cadence that
    /// runs forever, because ObjectCacheService.GetPropObject logs a warning of the game's own on
    /// every miss and a discovery pass must not be the loudest thing in the log."</i></para>
    ///
    /// <para>Every sentence of that was true and the conclusion was still wrong, because the
    /// premise it rests on — that a miss is loud — was a property of HOW we asked, not of the
    /// question. <see cref="PropVisualLookup"/> reads the cache dictionary directly and says
    /// nothing on a miss, so the reason to stop looking is gone. What the budget cost is the
    /// 2026-09-03 report in full: once it expired, <c>resolve</c> was false for the rest of the
    /// scenario, every unresolved prop was counted as pending WITHOUT a lookup, and no prop could
    /// ever become grabbable again — no glow, no collider, no ghost, no card, silently, with the
    /// census budget also spent so the log said nothing either. A prop whose room is revealed in
    /// minute ten is the ordinary case, not the edge case.</para>
    ///
    /// <para>The floor that replaces it: the walk runs while ANY liftable prop is unresolved, at
    /// the same 2 s cadence, and the walk is a <c>PropLift</c> test per prop plus one dictionary
    /// probe per missing one. No scene query, ever. Once everything has resolved, a scan is one
    /// <c>List.Count</c> comparison and a return, exactly as before.</para>
    /// </summary>

    /// <summary>Cadence ticks between the slow floor sweeps — one full (but resolution-free) walk
    /// every <c>IdleSweepScans * ScanIntervalSeconds</c> seconds, ~10 s. See the change gate in
    /// <see cref="Scan"/> for the case the size comparison alone cannot see.</summary>
    private const int IdleSweepScans = 5;

    private static readonly Dictionary<CObjectProp, GrabbableProp> Registry = new();
    private static readonly HashSet<CObjectProp> Seen = new();
    private static readonly List<CObjectProp> Scratch = new(8);

    private static float _nextScan;
    private static int _lastPropCount = -1;
    private static int _pendingResolve;
    private static int _idleScansLeft = IdleSweepScans;
    private static object? _lastState;
    private static bool _loggedRegistration;

    /// <summary>How many props the last scan accepted by IMPORT TYPE and then refused as
    /// unliftable (<see cref="PropLift"/>). Read by the <c>[Props]</c> census, so one line says
    /// whether the new gate is doing anything at all on this board.</summary>
    private static int _refusedThisScan;

    /// <summary>Refusal lines this scenario. Two: the hardware question is "does the pit stop
    /// offering itself", which the first line answers completely, and a board can hold several
    /// pits — a line per pit per ten-second sweep would drown the log.</summary>
    private const int RefusalLogBudget = 2;

    private static int _refusalLogsLeft = RefusalLogBudget;

    /// <summary>How many liftable-by-import-type props the last scan refused as unliftable.</summary>
    internal static int Refused => _refusedThisScan;

    /// <summary>How many props are currently registered as grabbable — read by the
    /// <c>[Props]</c> census so one line says whether discovery actually landed.</summary>
    internal static int Registered => Registry.Count;

    /// <summary>How many liftable props the last scan could NOT resolve to a GameObject (or to a
    /// collider). Also read by the census: a positive number with a spent settle budget is the
    /// signature of a prop whose visual never arrived.</summary>
    internal static int Pending => _pendingResolve;

    /// <summary>Is this exact prop instance registered as grabbable? The per-sample column of the
    /// <c>[Props]</c> census, so the line names WHICH prop discovery missed instead of only how
    /// many.</summary>
    internal static bool IsRegistered(CObjectProp? prop) => prop != null && Registry.ContainsKey(prop);

    /// <summary>
    /// The collider this prop actually REGISTERED against, or null if it is not registered — read
    /// by the <c>[Props]</c> census's REACHHEXES column so the line measures the shape the hands
    /// are really being tested against, not the shape this file believes it built.
    ///
    /// <para>Read-only: it hands back a reference and nothing else, so the census cannot become
    /// load-bearing through it. A destroyed collider comes back as a Unity-null the caller's own
    /// <c>== null</c> catches, which is the same contract <see cref="Prune"/> relies on.</para>
    /// </summary>
    /// <summary>The nearest registered prop whose pick volume contains <paramref name="hand"/>'s
    /// pinch point, or null. Pure; the registry is small (single digits to a few dozen), so this is
    /// one ClosestPoint per prop per call and it is called only from the capture test's edge.</summary>
    internal static GrabbableProp? NearestInReach(VRHand hand, out float realMetres)
    {
        GrabbableProp? best = null;
        realMetres = float.PositiveInfinity;
        foreach (KeyValuePair<CObjectProp, GrabbableProp> kv in Registry)
        {
            GrabbableProp g = kv.Value;
            if (g == null || !g.InReachOf(hand, out float d))
                continue;
            if (d < realMetres)
            {
                realMetres = d;
                best = g;
            }
        }
        return best;
    }

    internal static Collider? PickColliderOf(CObjectProp? prop)
    {
        if (prop == null)
            return null;
        return Registry.TryGetValue(prop, out GrabbableProp grabbable) ? grabbable.PickCollider : null;
    }

    /// <summary>
    /// One call per frame, from <c>FigureGrabDriver.RefreshRegistry</c> — the step that already
    /// owns "keep the adoption set current". Ordered: glides and the held re-assert first (both
    /// must run whatever the dial says), then the prune, then the ghost reconcile, then the gate,
    /// then the scan.
    /// </summary>
    internal static void Tick()
    {
        // A release glide already in the air lands regardless of the feature dial — the same rule
        // that keeps FigureGrab.Glide above the config gate in FigureGrabDriver.Update.
        GrabbableProp.TickGlides();

        // The Apparance thaw sits above the gate for the same reason: a prop whose rebuild-on-move
        // was frozen for a hold must get MonitorMovement back whatever the dial does next, or it
        // could never re-synthesize again for the rest of the session. One List.Count compare when
        // nothing is pending. See GrabbableProp.FreezeApparance.
        GrabbableProp.TickThaws();

        // The held re-assert sits above the gate for the same reason: a prop still in a hand when
        // GrabProps is toggled off is released by the gate's ReleaseAll on THIS frame, and must not
        // be rendered at a drifted pose for the frame in between. Iterates at most two props and
        // is a single Count compare when nothing is held.
        GrabbableProp.TickHeld();

        // The animation A/B sits above the gate for the same reason the thaw does: its HOME window
        // opens AFTER the prop has landed, so a dial turned off mid-flight would otherwise strand a
        // half-taken measurement and cost the round its only reading. One field test in the steady
        // state, and nothing at all once its two-verdict budget is spent. See PropAnimWatch.
        PropAnimWatch.Tick();

        // The belt sits above the gate for the same reason, and for one more: a prop still in a
        // hand when GrabProps is toggled off is released by the gate's ReleaseAll on THIS frame,
        // and the culling values it replaced must be handed back on that same frame or they are
        // stranded for the rest of the session. One Count compare when nothing is held. See
        // PropAnimBelt.
        PropAnimBelt.Tick();

        Prune();
        PropGhosts.Tick();

        if (!FigureGrabConfig.GrabPropsEnabled)
        {
            if (Registry.Count > 0)
                ReleaseAll();
            return;
        }

        Scan();
    }

    /// <summary>Drop any prop whose visual died under us (looted, broken, scenario teardown). Cheap
    /// — a walk over at most a scenario's worth of liftable props, no scene query.</summary>
    private static void Prune()
    {
        if (Registry.Count == 0)
            return;
        Scratch.Clear();
        foreach (KeyValuePair<CObjectProp, GrabbableProp> kv in Registry)
        {
            if (kv.Value.Visual == null || kv.Value.PickCollider == null)
                Scratch.Add(kv.Key);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Drop(Scratch[i]);
    }

    private static void Scan()
    {
        ScenarioRuleLibrary.ScenarioState? state = ScenarioManager.CurrentScenarioState;
        List<CObjectProp>? props = state != null ? state.Props : null;

        // A NEW scenario state object is a new board: everything registered against the old one is
        // stale by identity, whatever its GameObjects still look like.
        if (!ReferenceEquals(state, _lastState))
        {
            _lastState = state;
            if (Registry.Count > 0)
                ReleaseAll();
            _lastPropCount = -1;
            _pendingResolve = 0;
            _unusableOwnShapes = 0;
            _unusableLogsLeft = UnusableLogBudget;
            // A NEW board is a new cache population: the InstanceName index and the re-key counter
            // are per scenario, and a stale index would answer for a prop that no longer exists.
            PropVisualLookup.Reset();
        }

        if (props == null)
            return;

        float now = Time.unscaledTime;
        if (now < _nextScan)
            return;
        _nextScan = now + ScanIntervalSeconds;

        // ---- THE CHANGE GATE, three conditions and a floor ------------------------------------
        //
        //   CHANGED   the prop list is a different size than last time: a prop spawned or was
        //             removed, so walk it and re-arm the settle budget.
        //   SETTLING  liftable props are still unresolved and the budget holds: the visuals are
        //             arriving, which the ModBuild 335 census watched happen over seconds.
        //   SWEEP     a slow floor, one walk per IdleSweepScans cadence ticks (~10 s). It exists
        //             for the one case the size comparison cannot see: a prop removed and another
        //             added inside the same tick leaves the count identical. The sweep is nearly
        //             free — every prop already in the registry is a dictionary hit and nothing
        //             else — and it deliberately does NOT re-resolve once the settle budget is
        //             spent (see `resolve` below), so it can never turn into a warning generator.
        //
        // Steady state is therefore one int comparison per tick, and one dictionary walk per ten
        // seconds. Nothing here queries the scene.
        bool countChanged = props.Count != _lastPropCount;
        // SETTLING NO LONGER EXPIRES (ModBuild 367) — see SettleScanBudget's note for what its
        // expiry cost. While a liftable prop has no visual, the walk keeps looking, silently.
        bool settling = !countChanged && _pendingResolve > 0;
        bool sweep = --_idleScansLeft <= 0;
        if (sweep)
            _idleScansLeft = IdleSweepScans;
        if (!countChanged && !settling && !sweep)
            return;
        if (countChanged)
            _lastPropCount = props.Count;

        // EVERY WALK MAY ASK NOW (ModBuild 367). The clause that used to stand here withheld the
        // lookup once a settle budget expired, because GetPropObject logs a warning of the GAME's
        // own on every miss (ObjectCacheService.cs:104) and a discovery pass must not be the
        // loudest thing in the log it is meant to make readable. That reasoning was sound and the
        // conclusion was still the 2026-09-03 defect: a withheld lookup is a prop that can never
        // come back. PropVisualLookup reads the cache dictionary directly and is silent on a miss,
        // so the trade-off it was balancing no longer exists — see SettleScanBudget's note.

        // PHASE 1 — membership. Who does the scenario state say a hand may lift, right now.
        //
        // ONE GATE, and it is PropLift.MayBeLifted. Membership is where "erst gar nicht
        // aufnehmbar" has to be decided, because a prop that never enters this registry never
        // gets a GrabbableProp, and the hover glow, the pick collider, the home ghost, the info
        // panel and the grab itself all hang off that object and off nothing else. Refusing
        // later — in CanGrab, say — would leave the glow promising a pickup that cannot happen,
        // which is the half of the report that is not about the grab.
        Seen.Clear();
        _refusedThisScan = 0;
        for (int i = 0; i < props.Count; i++)
        {
            CObjectProp prop = props[i];
            if (prop == null)
                continue;
            if (PropLift.MayBeLifted(prop, out string why))
            {
                Seen.Add(prop);
                continue;
            }
            // Only count the ones the IMPORT-TYPE whitelist already accepted: everything else
            // (doors, pressure plates, terrain) was never a candidate and is not news.
            if (!FigureGrabDriver.IsLiftableProp(prop))
                continue;
            _refusedThisScan++;
            if (_refusalLogsLeft <= 0)
                continue;
            _refusalLogsLeft--;
            // HW-VERIFY: this line is the ONLY place the shipped log says WHICH term refused a prop —
            // the game's authored OverrideDisallowDestroyAndMove flag, or the solid-obstacle family
            // test. It must stay at a tier the DEFAULT log level prints (Note/Alert/Error).
            VRLog.Note("FigureGrab",
                $"[Props] NOT LIFTABLE: '{prop.PrefabName}' {prop.ObjectType} — {why}. It is not "
                + "registered as grabbable at all, so it gets no hover glow, no pick collider, no "
                + "ghost and no info panel; the hand passes straight over it. (User, ModBuild 350: "
                + "\"Bitte exkludiere solche Obstacles die man nicht zerstören kann bei dem Greifen "
                + "wie zB die 'DarkPitObstacles' diese soll erst garnicht aufnehmbar sein.\") "
                + $"({_refusalLogsLeft} more refusal lines this scenario.)");
        }

        // PHASE 2 — DROP THE STALE, and it runs BEFORE the add on purpose. A state sync can hand
        // back a Props list of the same length whose entries are fresh CObjectProp instances
        // describing the same board. Reference identity then makes every one of them "new" and
        // every registered one "gone" — a full re-key over UNCHANGED GameObjects. Adding first
        // would have the incoming entry and the outgoing entry share a visual for the length of
        // this method, which is exactly the window in which one of them tears down state the other
        // is about to rely on. Dropping first makes the re-key a clean hand-over.
        if (Registry.Count > 0)
        {
            Scratch.Clear();
            foreach (KeyValuePair<CObjectProp, GrabbableProp> kv in Registry)
            {
                if (!Seen.Contains(kv.Key))
                    Scratch.Add(kv.Key);
            }
            for (int i = 0; i < Scratch.Count; i++)
                Drop(Scratch[i]);
        }

        // PHASE 3 — register what is not registered yet.
        _pendingResolve = 0;
        PropVisualLookup.Route route = PropVisualLookup.Route.None;

        foreach (CObjectProp prop in Seen)
        {
            if (Registry.ContainsKey(prop))
                continue;

            // THE LOOKUP IS NO LONGER GetPropObject (ModBuild 367). It is the same dictionary,
            // asked so that a RE-KEYED cache still answers and a miss stays silent —
            // PropVisualLookup carries the whole diagnosis and the three routes.
            GameObject? visual = PropVisualLookup.Resolve(prop, out route);
            // NOT YET DRAWING is the same answer as NOT YET SPAWNED and takes the same retry: the
            // census watched both flip from "no" to "yes" within the first seconds of a scenario.
            if (visual == null || !FigureGrabDriver.DrawsSomething(visual))
            {
                _pendingResolve++;
                continue;
            }

            // A prop may have no collider at all — it was never interactable through its own mesh
            // (the game reaches a chest through its HEX). BuildPropCollider makes a TRIGGER box
            // from the renderer bounds on Ignore Raycast, so it can never enter the game's physics
            // or its picking; it exists only for the proximity election to measure against.
            //
            // THE BOX, ONCE BUILT, BELONGS TO THE PROP AND NOT TO THIS ENTRY. It is a child of the
            // prop visual, so Unity destroys it with the prop and it cannot leak past the scenario.
            // Tearing it down when a registry ENTRY is dropped would be worse in the one case that
            // matters: a state sync re-keys every prop (see phase 2) and the rebuilt entry would
            // then have to re-create the box in the same frame the old one was destroyed —
            // Object.Destroy is deferred to end of frame, so the lookup below would hand the new
            // entry the doomed one. Reused instead: this lookup finds it exactly like an authored
            // collider, and `built` is false the second time round.
            //
            // THE CHOICE OF SHAPE MOVED OUT (ModBuild 371), AND THAT IS THE 2026-09-03 MULTI-HEX
            // FIX. The two lines that used to stand here were `own = GetComponentInChildren
            // <Collider>()` and `built ? BuildPropCollider(visual) : own` — i.e. the FIRST
            // collider in the prop's subtree, falling back to the renderer-bounds box only when
            // there was none at all. For a TwoHexObstacle that first collider is the authored
            // one-hex shape on the prop root (the game picks props by raycast on the "Hovering"
            // layer, which UnityGameEditorObject.Start sets on the ROOT only), and since the ONE
            // registered collider is the only thing every reach test measures against, one hex of
            // that prop answered the hand and the other did not: "das highlighting erscheint
            // allerdings nur wenn ich beim prop über ein einziges feld mit der hand bin". The
            // renderer-bounds fallback that would have spanned the whole prop was exactly the
            // branch these props never took — the ModBuild 367 log says so in one clause,
            // "Collider: the prop's own." PropReach carries the diagnosis and the sources.
            //
            // `own` is still resolved here, and still by the same expression, because it is what
            // the single-hex answer IS: PropReach.Resolve hands it straight back for every prop
            // that stands on one hex, so those props keep the shape their pick radius was tuned
            // against, unchanged and unwidened.
            // ---- THE PROP'S OWN PICK SHAPE, AND THE MODBUILD 445 CORRECTION -------------------
            //
            // This line used to be `visual.GetComponentInChildren<Collider>()` — the first collider
            // in the subtree, whatever state it was in. That is the gold-pile defect in one
            // expression: an enemy-drop MoneyToken carries an authored collider that is present and
            // SWITCHED OFF, `GetComponentInChildren` happily returned it, and the registry then
            // measured every hand against a shape whose `ClosestPoint` hands back the query point.
            // The pile therefore read as 0 mm from both hands everywhere on the board, forever.
            //
            // WHAT THAT COST, because it was two user-visible features and not one:
            //   * the pile could never be highlighted or picked up — ProximityGrabber.UpdateHighlight
            //     skips a disabled collider BEFORE any distance test (it always knew this), so the
            //     entry existed, was counted as grabbable by our own census, and was invisible to
            //     the election that actually elects;
            //   * figure resizing died board-wide — FigureStretch's "a prop under the hand beats the
            //     resize shell" veto consumed that same zero through PropGrab.NearestInReach and
            //     refused every capture, on both machines, for a whole session.
            // Both halves are one fact, and the fact is that PropGrab did not ask the question
            // ProximityGrabber has been asking all along. It asks it now, in one shared place:
            // VRInteractables.IsUsablePickShape.
            //
            // `own` is null when the prop has no BELIEVABLE shape of its own, which is exactly the
            // condition BuildPropCollider was always the answer to — so the fix needs no new
            // machinery, only the right question. PropReach.SingleHex builds (or reuses) the box.
            Collider? own = PropReach.OwnPickShape(visual, out int ownPresent, out int ownUsable);
            bool built = own == null;
            Collider? collider = PropReach.Resolve(visual, prop, own,
                out PropReach.Route reachRoute, out int reachHexes, out string reachHexSource);
            if (collider == null)
            {
                _pendingResolve++;
                continue;
            }

            if (ownPresent > 0 && ownUsable == 0)
                NoteUnusableOwnShape(visual, prop, reachRoute, ownPresent);

            var grabbable = new GrabbableProp(prop, visual, collider);
            VRInteractables.RegisterGrabbable(grabbable, collider);
            Registry[prop] = grabbable;

            if (_loggedRegistration)
                continue;
            _loggedRegistration = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("FigureGrab",
                $"[Props] registry OPEN: first grabbable prop is {grabbable.Label}, visual "
                + $"'{visual.name}' resolved through ObjectCacheService (NOT through an actor — it "
                + "has none, which is exactly what the ModBuild 335 census measured). RESOLVED "
                + $"THROUGH {PropVisualLookup.Describe(route)} (ModBuild 367). Collider: "
                + (built ? "built from its renderer bounds (the prop had none)." : "the prop's own.")
                + " It now carries the figure hover glow, the figure pick radius, the trigger-only "
                + "grab and a home ghost. Logged once per scenario; the [Props] census line counts "
                + "the rest."
                + $" REACH VOLUME (ModBuild 371): {PropReach.Describe(reachRoute)}; this prop "
                + $"stands on {reachHexes} hex(es) according to {reachHexSource}. A prop on ONE hex "
                + "keeps exactly the collider it had before this build; a prop on SEVERAL gets one "
                + "trigger box spanning all of them, because the single registered collider is the "
                + "only shape every hover and grab test measures against. The census line's "
                + "REACHHEXES column says whether that landed, per prop.");
        }

    }

    /// <summary>How many props this scenario registered a stand-in box because every collider
    /// they own is unusable — read by the <c>[Props]</c> census so one number says whether the
    /// 2026-09-05 gold-pile class is present on this board at all.</summary>
    internal static int UnusableOwnShapes => _unusableOwnShapes;

    private static int _unusableOwnShapes;

    /// <summary>Lines per scenario for the report below. Two: a board can hold a dozen gold piles
    /// and they all fail for the same reason, so the first line answers the question completely
    /// and the census counter carries the rest.</summary>
    private const int UnusableLogBudget = 2;

    private static int _unusableLogsLeft = UnusableLogBudget;

    /// <summary>
    /// A PROP WHOSE EVERY OWN COLLIDER IS A DEAD PICK SHAPE — named, once, with its cause.
    ///
    /// <para>This is the instrument the 2026-09-05 round did not have. Both hardware logs carried
    /// the SYMPTOM in abundance (<c>'GoldPile' MoneyToken at 0 mm</c>, hundreds of times, beside a
    /// second distance on the same line that moved continuously) and nothing anywhere said WHY a
    /// surface distance would be pinned at zero. It reads the state at the moment of registration,
    /// which is the moment the decision is made, and it names the clause that decided it.</para>
    /// </summary>
    private static void NoteUnusableOwnShape(GameObject visual, CObjectProp prop,
                                             PropReach.Route route, int ownPresent)
    {
        _unusableOwnShapes++;
        if (_unusableLogsLeft <= 0)
            return;
        _unusableLogsLeft--;
        string label = $"'{(prop != null ? prop.PrefabName : "?")}' {(prop != null ? prop.ObjectType.ToString() : "?")}";
        // HW-VERIFY: this line is the falsifier for the 2026-09-05 gold-pile diagnosis — it prints
        // only when a prop's own colliders are ALL unusable, so its presence proves the class
        // exists on this board and its absence proves the cause was something else. It must stay
        // at a tier the DEFAULT log level prints (Note/Alert/Error); scripts/check-hw-verify.py
        // enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] DEAD PICK SHAPE: {label} (visual '{visual.name}') carries {ownPresent} "
            + "collider(s) of its own and NOT ONE of them is a shape a reach test may believe — "
            + $"{PropReach.DescribeOwnRefusal(visual)}. That matters because every proximity test "
            + "in this mod is Vector3.Distance(point, collider.ClosestPoint(point)), and Unity's "
            + "ClosestPoint returns THE QUERY POINT for a collider like this: the prop would read "
            + "as 0 mm from both hands at every position on the board. Registering it that way is "
            + "what killed gold-pile highlighting AND the figure-resize gesture on 2026-09-05 — "
            + "the resize veto asks 'is a prop nearer than the shell?' and a permanent zero "
            + $"answers yes forever. It is therefore NOT registered: {PropReach.Describe(route)}. "
            + $"({_unusableLogsLeft} more of these lines this scenario; the [Props] census keeps "
            + "counting them after that.)");
    }

    private static void Drop(CObjectProp prop)
    {
        if (Registry.TryGetValue(prop, out GrabbableProp grabbable))
        {
            grabbable.Restore();
            VRInteractables.UnregisterGrabbable(grabbable);
        }
        Registry.Remove(prop);
    }

    /// <summary>Put every prop back and empty the registry — the config gate going off, a scenario
    /// teardown, or the driver being destroyed. Restores home poses and layers first, so no prop is
    /// ever left riding a hand that no longer exists.</summary>
    internal static void ReleaseAll()
    {
        foreach (KeyValuePair<CObjectProp, GrabbableProp> kv in Registry)
        {
            kv.Value.Restore();
            VRInteractables.UnregisterGrabbable(kv.Value);
        }
        Registry.Clear();
        // Every Restore above scheduled a thaw a few frames out; there may be no more frames.
        GrabbableProp.FlushThaws();
        HeldProps.Clear();
        PropGhosts.Clear();
        _lastPropCount = -1;
        _pendingResolve = 0;
        _idleScansLeft = IdleSweepScans;
        // The InstanceName index and the re-key counter belong to the board that is going away.
        PropVisualLookup.Reset();
        _nextScan = 0f;
        _loggedRegistration = false;
        _refusedThisScan = 0;
        _refusalLogsLeft = RefusalLogBudget;
        GrabbableProp.ResetLogBudgets();
    }

    /// <summary>Module shutdown / scene change: <see cref="ReleaseAll"/> plus the scenario-state
    /// identity, so the next board re-discovers from scratch.</summary>
    internal static void Clear()
    {
        ReleaseAll();
        PropLift.ClearCache();
        _lastState = null;
    }
}

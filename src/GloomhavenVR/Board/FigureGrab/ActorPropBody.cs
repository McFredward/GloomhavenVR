using System.Collections.Generic;
using Apparance.Unity;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE BODY OF AN ACTOR THAT HAS NONE — the link from a health-bearing prop's invisible
/// <c>PropDummyObject</c> actor back to the prop the player can actually see.
///
/// <para><b>THE REPORT (2026-09-03 hardware round), and the ruling that followed it.</b> A scenario
/// gave a door health, so the player found a health bar on it and could pick it up with the
/// trigger — "<i>Allerdings hat man nichts in der Hand</i>". The first classification was that the
/// grab was the defect. The user overruled it: "<i>wenn das keine normale Tür ist sondern wirklich
/// etwas das man angreifen kann mit entsprechender Info, dann macht es Sinn das a) eine healtbar da
/// ist und b) auch dass man sie nehmen kann und die infos sieht. In dem Fall mach es nicht
/// rückgängig, aber fix die kleinen Probleme</i>" — the bar must float ABOVE the door, and a held
/// door must behave like a held prop (visible in the hand, a ghost at home, a glow on hover).
/// "<i>WICHTIG: Das ganze gilt hier nur für solche besondere Außnahmeprops/Türen, normale Türen
/// sollen von all dem nicht betroffen sein.</i>"</para>
///
/// <para><b>ONE MISSING THING, FOUR SYMPTOMS.</b> Everything the ModBuild 396 log measured about
/// <c>PropDummyObject</c> agrees: one <c>MeshRenderer</c> named <c>Base</c>, <c>NO MESH</c>,
/// <c>&lt;null material&gt;</c>, <c>box size (0.00, 0.00, 0.00)</c>. So there is nothing to clone
/// (no glow — "NOTHING TO GLOW"), nothing to clone (no ghost — "NOT ONE of them is drawable … they
/// have no box"), nothing to move into the hand (nothing in the hand), and nothing to measure
/// (the bar's anchor collapses to the actor origin, i.e. inside the door). The body the actor lacks
/// is its HOST PROP's visual. Hand it that, and all four follow from the code that is already
/// there.</para>
///
/// <para><b>THE LINK, AND IT IS THE GAME'S OWN FIELD.</b>
/// <c>CObjectActor.m_PropAttachedTo</c>, exposed as <c>CObjectActor.AttachedProp</c>
/// (decompiled CObjectActor.cs:9-13). It is written by <c>CObjectActor.SetAttachedToProp</c>
/// (cs:99-104), which REFUSES outright unless
/// <c>propAttachedTo.PropHealthDetails.HasHealth</c> — it logs "Unable to create dummy ObjectActor
/// for … The prop is not configured for health Correctly" and returns without assigning. So an
/// attached prop is, by the game's own construction, a prop configured for health. The same field
/// is what <c>CObjectActor.ActorLocKey</c> and <c>OnDeath</c> read to give the door its info panel
/// and its destruct/unlock behaviour, so it is the identity link the rules engine itself trusts.
/// From the prop, the visual comes from <see cref="PropVisualLookup"/> — the mod's ONE prop-visual
/// lookup, already used by <see cref="PropGrab"/> and silent on a miss. There is deliberately no
/// second lookup and no nearest-prop guess: a proximity match would bind the wrong door the first
/// time two health props stand adjacent.</para>
///
/// <para><b>THIS IS WHY A NORMAL DOOR IS UNTOUCHED, BY CONSTRUCTION.</b> Not by a name test and not
/// by a type test. A door without <c>PropHealthDetails.HasHealth</c> is never given a
/// <c>CObjectActor</c> at all (<c>CMap</c> only builds one for a prop configured for health), so it
/// has no <c>ActorBehaviour</c>, no <c>WorldspacePanelUIController</c>, no health bar, and it never
/// reaches any caller of this class. The middle case is the proof that this is not a door special
/// case: a DESTRUCTIBLE OBSTACLE takes exactly the same path and gets exactly the same treatment,
/// and it is not a door.</para>
///
/// <para><b>IDENTITY STAYS WITH THE ACTOR.</b> The actor is WHO it is — health, guid, wire slot,
/// info panel, the cell the rules engine moves. The prop is WHAT you see. This class only ever
/// answers "which GameObject is this actor's visible body"; nothing here writes game state, and the
/// two are never merged.</para>
///
/// <para><b>MULTIPLAYER.</b> Every term is read from replicated scenario state (the prop list and
/// the actor's own attached-prop field) and from the scene the game builds from it. No per-peer
/// input, no hand pose, no local tie-break, no wire field: two modded peers resolve the same body
/// for the same actor, and an unmodded peer sees none of it.</para>
/// </summary>
internal static class ActorPropBody
{
    /// <summary>How long a failed resolve is left alone before it is tried again. The visual
    /// arrives asynchronously (the ModBuild 335 census watched props go from nothing to drawing
    /// over seconds), so a miss must never latch; a walk per second per unresolved actor is the
    /// price of that, and "near-free" scene work has shipped as a per-frame cost here twice.</summary>
    private const float RetrySeconds = 1f;

    /// <summary>Census lines per scenario, change-gated and at most one per
    /// <see cref="CensusIntervalSeconds"/>. The LAST one printed is the settled state.</summary>
    private const int CensusLogBudget = 12;

    private const float CensusIntervalSeconds = 2f;

    /// <summary>How many actors the census NAMES. A count alone says a door and a destructible
    /// boulder were resolved in exactly the same way.</summary>
    private const int NamedSamples = 3;

    /// <summary>One resolved (or not-yet-resolved) attached-prop actor.</summary>
    private struct Entry
    {
        public CObjectProp? Prop;
        public GameObject? Visual;
        public float RetryAt;

        /// <summary>Has this actor already been counted as RESOLVED by the census? A visual can be
        /// destroyed and rebuilt (Apparance churn does exactly that), and re-resolving it must not
        /// make the census report more resolved actors than there are actors.</summary>
        public bool Counted;
    }

    /// <summary>Actor-root instance id -&gt; its resolution. Bounded by the number of health props
    /// a scenario holds (single digits in every log so far); dropped with the scenario.</summary>
    private static readonly Dictionary<int, Entry> Cache = new(8);

    private static readonly List<ApparanceEntity> EntityScratch = new(4);
    private static readonly List<string> ResolvedNames = new(NamedSamples);
    private static readonly List<string> UnresolvedNames = new(NamedSamples);

    private static int _attachedActors;
    private static int _resolved;
    private static int _censusLogsLeft = CensusLogBudget;
    private static float _nextCensus;
    private static long _lastCensusSignature = -1;

    /// <summary>
    /// The visible body of <paramref name="actor"/>, or null when this actor is not an
    /// attached-prop actor (every ordinary miniature, and every normal door — which has no actor at
    /// all) or when its prop's visual has not been built yet.
    ///
    /// <para>Never returns the actor's own root: the whole point is that the actor's subtree is
    /// empty. A caller may therefore treat a non-null answer as "use this instead".</para>
    /// </summary>
    internal static GameObject? BodyFor(ActorBehaviour? actor)
    {
        GameObject? root = actor != null ? actor.m_RootGameObject : null;
        if (root == null)
            return null;
        return Resolve(actor!, root);
    }

    /// <summary>Overload for callers that hold the tracked GameObject rather than the behaviour —
    /// <c>ActorBars</c> has a <c>WorldspacePanelUIController.m_ObjectToTrack</c> and nothing
    /// else.</summary>
    internal static GameObject? BodyFor(GameObject? trackedRoot)
    {
        if (trackedRoot == null)
            return null;
        ActorBehaviour behaviour = ActorBehaviour.GetActorBehaviour(trackedRoot);
        return behaviour == null ? null : Resolve(behaviour, trackedRoot);
    }

    /// <summary>The scenario-state prop this actor is attached to, or null. Read by the census and
    /// by the anchor line so both can name the prop rather than only the actor.</summary>
    internal static CObjectProp? PropFor(ActorBehaviour? actor)
    {
        GameObject? root = actor != null ? actor.m_RootGameObject : null;
        if (root == null)
            return null;
        Resolve(actor!, root);
        return Cache.TryGetValue(root.GetInstanceID(), out Entry e) ? e.Prop : null;
    }

    private static GameObject? Resolve(ActorBehaviour behaviour, GameObject root)
    {
        int id = root.GetInstanceID();
        float now = Time.unscaledTime;
        if (Cache.TryGetValue(id, out Entry entry))
        {
            if (entry.Visual != null)
                return entry.Visual;
            if (entry.Prop == null || now < entry.RetryAt)
                return null;                 // not an attached-prop actor, or on the retry cooldown
        }
        else
        {
            // THE ONE TEST THAT DECIDES MEMBERSHIP, and it is the game's own field. Anything that
            // is not a CObjectActor with an attached prop is cached as "never" (Prop == null) and
            // costs one dictionary hit for the rest of the scenario.
            CActor? actor = behaviour.Actor;
            CObjectProp? prop = (actor as CObjectActor)?.AttachedProp;
            entry = new Entry { Prop = prop, Visual = null, RetryAt = 0f };
            if (prop == null)
            {
                Cache[id] = entry;
                return null;
            }
            _attachedActors++;
        }

        GameObject? visual = PropVisualLookup.Resolve(entry.Prop, out PropVisualLookup.Route route);
        // A prop that resolves to the ACTOR'S OWN ROOT would be a body that is not there; refuse it
        // rather than hand a caller the empty subtree it was trying to get away from.
        if (visual == root)
            visual = null;
        entry.Visual = visual;
        entry.RetryAt = visual == null ? now + RetrySeconds : 0f;
        bool firstResolve = visual != null && !entry.Counted;
        if (firstResolve)
            entry.Counted = true;
        Cache[id] = entry;

        if (visual != null)
        {
            if (firstResolve)
            {
                _resolved++;
                if (ResolvedNames.Count < NamedSamples)
                    ResolvedNames.Add($"'{entry.Prop!.InstanceName}' {entry.Prop.ObjectType} -> visual "
                                      + $"'{visual.name}' via {PropVisualLookup.Describe(route)}");
            }
        }
        else if (UnresolvedNames.Count < NamedSamples)
        {
            UnresolvedNames.Add($"'{entry.Prop!.InstanceName}' {entry.Prop.ObjectType} -> "
                                + $"{PropVisualLookup.Describe(route)}");
        }
        return visual;
    }

    // ---- THE HOLD: the body rides into the hand and comes back --------------------------------

    /// <summary>One suspended prop body: where it came from, and the Apparance flags we turned
    /// off for the length of the hold.</summary>
    private sealed class Held
    {
        public GameObject Visual = null!;
        public Transform? OrigParent;
        public Vector3 OrigLocalPos;
        public Quaternion OrigLocalRot;
        public Vector3 OrigLocalScale;
        public ApparanceEntity[] Frozen = System.Array.Empty<ApparanceEntity>();
        public bool[] FrozenMonitor = System.Array.Empty<bool>();
    }

    private static readonly Dictionary<ActorBehaviour, Held> HeldBodies = new(2);

    private static bool _loggedAttach;

    /// <summary>
    /// Bring this actor's prop body into the actor's own visual subtree for the length of a hold,
    /// so the mini machinery that is already there — the ghost clone, the held reparent, the size
    /// latch, the stat panel dock — reaches the door without knowing a door exists.
    ///
    /// <para><b>WHY REPARENT RATHER THAN DRIVE A SECOND TRANSFORM.</b> Every one of those consumers
    /// walks ONE subtree. A second transform driven alongside would be a second writer of the same
    /// pose, and this project's standing lesson about that ("a remedy knows one writer") is that the
    /// two drift and the log cannot say which one the eye saw. The reparent is scoped to the hold
    /// and undone by <see cref="Release"/>, so nothing about the board's hierarchy changes while
    /// the door is standing in its frame.</para>
    ///
    /// <para><b>APPARANCE IS FROZEN FOR THE HOLD</b>, exactly as <see cref="GrabbableProp"/> does
    /// and for the reason documented in full there: an <c>ApparanceEntity</c> re-builds its
    /// generated content whenever it is TRANSFORMED (the plugin's own tooltip on
    /// <c>MonitorMovement</c> says so in those words), so a prop in a moving hand destroys and
    /// re-instantiates its own meshes every frame, each one born <c>Renderer.enabled = false</c>.
    /// That is the ModBuild 349 finding and it applies unchanged to a door.</para>
    ///
    /// <para>Idempotent. A no-op for an actor with no prop body, which is every ordinary
    /// miniature.</para>
    /// </summary>
    /// <param name="actor">The actor being grabbed.</param>
    /// <param name="into">The transform the body should ride — the actor's
    /// <c>m_AnimatedGameObject</c> when it has one, so the ghost clone (built from exactly that
    /// object) picks the body up too.</param>
    internal static void Hold(ActorBehaviour? actor, Transform? into)
    {
        if (actor == null || into == null || HeldBodies.ContainsKey(actor))
            return;
        GameObject? visual = BodyFor(actor);
        if (visual == null || visual.transform == into || into.IsChildOf(visual.transform))
            return;

        var held = new Held
        {
            Visual = visual,
            OrigParent = visual.transform.parent,
            OrigLocalPos = visual.transform.localPosition,
            OrigLocalRot = visual.transform.localRotation,
            OrigLocalScale = visual.transform.localScale,
        };

        EntityScratch.Clear();
        visual.GetComponentsInChildren(includeInactive: true, EntityScratch);
        held.Frozen = EntityScratch.Count > 0 ? EntityScratch.ToArray()
                                              : System.Array.Empty<ApparanceEntity>();
        held.FrozenMonitor = new bool[held.Frozen.Length];
        int froze = 0;
        for (int i = 0; i < held.Frozen.Length; i++)
        {
            ApparanceEntity e = held.Frozen[i];
            if (e == null)
                continue;
            held.FrozenMonitor[i] = e.MonitorMovement;
            e.MonitorMovement = false;
            froze++;
        }
        EntityScratch.Clear();

        // worldPositionStays: the door does not move when it becomes a child; it is exactly where
        // the player last saw it standing, and the hand's own reparent (one line later in
        // FigureGrabbable.OnGrab) is what lifts it.
        visual.transform.SetParent(into, worldPositionStays: true);
        HeldBodies[actor] = held;

        if (_loggedAttach)
            return;
        _loggedAttach = true;
        // HW-VERIFY: the line that says the held door has a body at all. Its ABSENCE with a grab in
        // the log means the body was never attached and every one of the three hold symptoms
        // (nothing in the hand, no ghost, no glow) is still the ORIGINAL defect, not a new one.
        VRLog.Note("FigureGrab",
            $"HEALTH-PROP BODY attached for the hold of '{visual.name}': the actor's own subtree is "
            + "empty (that is what a PropDummyObject is), so its host prop's visual rides the hold "
            + "inside the actor's animated object — which is the SAME object the home ghost is "
            + "cloned from and the same one the hand reparents, so 'visible in the hand' and 'leaves "
            + $"a ghost' are one attachment and not two. {froze} ApparanceEntity(s) in that visual "
            + "had MonitorMovement turned off for the length of the hold (restored on release): an "
            + "Apparance entity re-builds its generated content whenever it is TRANSFORMED, which is "
            + "the ModBuild 349 finding — see GrabbableProp.FreezeApparance for the whole chain. A "
            + "COUNT OF 0 means this prop is not Apparance content and its flicker, if any, has "
            + "another cause. KNOWN SIDE EFFECT, stated rather than hidden: this prop root is also "
            + "what WallSegmentFade.SeedGateColumns re-seeds a DOORWAY's arch rect from, so while "
            + "the door is in a hand that rect follows the hand and the real doorway loses its arch "
            + "exclusion until release, when the next rescan re-seeds it. Transient and "
            + "self-correcting; if the masonry above the arch is seen flickering DURING a hold, "
            + "that is the mechanism and the remedy belongs in the fade subsystem, not here. "
            + "Logged once per scenario.");
    }

    /// <summary>Put the body back where the board had it and hand every <c>MonitorMovement</c>
    /// back. Idempotent, and safe against a parent destroyed during the hold (scene teardown):
    /// a dead original parent unparents to the scene root rather than throwing.</summary>
    internal static void Release(ActorBehaviour? actor)
    {
        if (actor == null || !HeldBodies.TryGetValue(actor, out Held held))
            return;
        HeldBodies.Remove(actor);

        for (int i = 0; i < held.Frozen.Length; i++)
        {
            ApparanceEntity e = held.Frozen[i];
            if (e != null)
                e.MonitorMovement = held.FrozenMonitor[i];
        }

        if (held.Visual == null)
            return;
        Transform t = held.Visual.transform;
        t.SetParent(held.OrigParent != null ? held.OrigParent : null, worldPositionStays: false);
        t.localPosition = held.OrigLocalPos;
        t.localRotation = held.OrigLocalRot;
        t.localScale = held.OrigLocalScale;
    }

    /// <summary>Is this actor's body currently riding a hold? Read by the anchor measurement, which
    /// must not re-measure a door that is in somebody's hand.</summary>
    internal static bool IsHeld(ActorBehaviour? actor) => actor != null && HeldBodies.ContainsKey(actor);

    // ---- THE READABLE ZERO --------------------------------------------------------------------

    /// <summary>
    /// One line saying what this rule did on this board — INCLUDING when it did nothing, because
    /// "no line" reads identically for "this scenario has no destructible props" and "the rule
    /// never ran", and this project has lost hardware rounds to exactly that pair.
    ///
    /// <para>It also prints the population the ruling is about: how many props the scenario holds
    /// that are configured for health (the ones this whole feature applies to) against how many
    /// DOORS it holds that are NOT (the ones that must be untouched). Those two numbers are the
    /// proof of "normale Türen sollen von all dem nicht betroffen sein" — a reader can count them
    /// instead of taking it on trust.</para>
    /// </summary>
    internal static void LogCensus()
    {
        if (_censusLogsLeft <= 0)
            return;
        float now = Time.unscaledTime;
        if (now < _nextCensus)
            return;

        int healthProps = 0, plainDoors = 0, healthDoors = 0;
        List<CObjectProp>? props = ScenarioManager.CurrentScenarioState?.Props;
        if (props != null)
        {
            for (int i = 0; i < props.Count; i++)
            {
                CObjectProp p = props[i];
                if (p == null)
                    continue;
                bool health = p.PropHealthDetails != null && p.PropHealthDetails.HasHealth;
                bool door = p.ObjectType == ScenarioManager.ObjectImportType.Door;
                if (health)
                    healthProps++;
                if (door && health)
                    healthDoors++;
                else if (door)
                    plainDoors++;
            }
        }

        long signature = ((long)healthProps << 40) ^ ((long)plainDoors << 28)
                         ^ ((long)_attachedActors << 16) ^ ((long)_resolved << 4);
        if (signature == _lastCensusSignature)
            return;
        _nextCensus = now + CensusIntervalSeconds;
        _lastCensusSignature = signature;
        _censusLogsLeft--;

        string verdict = _attachedActors == 0
            ? "NO actor on this board is attached to a prop, so this rule changed NOTHING here — "
              + "which is what a scenario with no destructible props looks like, and is a different "
              + "reading from this line being absent (that would mean the rule never ran)."
            : $"{_resolved} of them resolved to a visible body"
              + (ResolvedNames.Count > 0 ? $" ({string.Join(" | ", ResolvedNames)})" : string.Empty)
              + (_resolved < _attachedActors
                  ? $"; {_attachedActors - _resolved} did NOT, and are retried every "
                    + $"{RetrySeconds:F1} s"
                    + (UnresolvedNames.Count > 0
                        ? $" ({string.Join(" | ", UnresolvedNames)})" : string.Empty)
                  : "; none is still waiting for a visual") + ".";

        // HW-VERIFY: the membership proof for the 2026-09-03 ruling — which props took the
        // exceptional path and which doors did not. Grep "HEALTH-PROP BODY census".
        VRLog.Note("FigureGrab",
            $"HEALTH-PROP BODY census: the scenario holds {healthProps} prop(s) configured for "
            + $"health ({healthDoors} of them door(s)) and {plainDoors} ORDINARY door(s) with no "
            + "health at all. An ordinary door is given no CObjectActor by the rule library "
            + "(CObjectActor.SetAttachedToProp refuses a prop that is not configured for health), so "
            + "it has no bar, no grab and no body lookup — it is untouched by construction and not "
            + $"by a name test. {_attachedActors} attached-prop actor(s) reached this rule; {verdict} "
            + $"({_censusLogsLeft} more census lines this scenario; capped and change-gated.)");
    }

    /// <summary>Scenario teardown. Every hold is released first — a body left parented under a
    /// destroyed actor is a door that never comes back.</summary>
    internal static void Clear()
    {
        if (HeldBodies.Count > 0)
        {
            var actors = new List<ActorBehaviour>(HeldBodies.Keys);
            for (int i = 0; i < actors.Count; i++)
                Release(actors[i]);
            HeldBodies.Clear();
        }
        Cache.Clear();
        ResolvedNames.Clear();
        UnresolvedNames.Clear();
        _attachedActors = 0;
        _resolved = 0;
        _censusLogsLeft = CensusLogBudget;
        _nextCensus = 0f;
        _lastCensusSignature = -1;
        _loggedAttach = false;
    }
}

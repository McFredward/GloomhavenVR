using System;
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
        /// <summary>The prop visual the body was resolved from (the whole door prop). Never
        /// moved since ModBuild 400 — see <see cref="Moved"/>.</summary>
        public GameObject Visual = null!;
        /// <summary>The subtree that actually rides the hand: the door LEAF, never the arch.</summary>
        public GameObject Moved = null!;
        public Transform? OrigParent;
        public Vector3 OrigLocalPos;
        public Quaternion OrigLocalRot;
        public Vector3 OrigLocalScale;
        /// <summary>The leaf's WORLD pose at the grab — what the restore is measured against.</summary>
        public Vector3 HomeWorldPos;
        public Quaternion HomeWorldRot;
        public ApparanceEntity[] Frozen = System.Array.Empty<ApparanceEntity>();
        public bool[] FrozenMonitor = System.Array.Empty<bool>();
        /// <summary>The leaf's and the whole body's world boxes at the grab, for the picture
        /// line: a leaf box that reaches the arch top is the frame in the hand; one that is a
        /// 0.3 wu slab at floor level is the fog tile.</summary>
        public Bounds LeafBox, BodyBox;
        public int LeafRenderers, BodyRenderers;
        public string Route = string.Empty;
        /// <summary>The leaf's own up axis expressed in the actor root's frame at the grab. The
        /// held pose is applied to the ROOT (FigureGrabbable.ApplyHeldPose), so the leaf stands
        /// upright in the hand exactly when this is (0, 1, 0).</summary>
        public Vector3 LeafUpInRoot;
        public bool PictureLogged;
    }

    /// <summary>One leaf decision for a prop visual. A refusal is NOT permanent (ModBuild 405):
    /// the door's animated content arrives asynchronously — <c>Choreographer.OpenDoor</c> itself
    /// defers "Open" through <c>ProceduralProp.PlacementCompleteAction</c> when
    /// <c>MF.GetGameObjectAnimator</c> finds nothing yet — so a leaf that is not there at the
    /// first hover may be there a second later. A leaf, once found, is memoised for the
    /// scenario.</summary>
    private struct LeafEntry
    {
        public GameObject? Leaf;
        public string Why;
        public string Route;
        public int PreviewExcluded;
        public float RetryAt;
    }

    /// <summary>Prop-visual instance id -> the leaf chosen for it. Dropped with the scenario.</summary>
    private static readonly Dictionary<int, LeafEntry> LeafCache = new(4);
    private static readonly List<Renderer> RendererScratch = new(64);
    private static readonly List<Transform> ChainScratch = new(16);
    private static readonly List<Animator> AnimatorScratch = new(4);
    private static bool _loggedRefusal;
    private static bool _loggedRelease;
    private static int _releases;
    private static float _worstReleaseDeltaWU;
    private static float _worstReleaseDeltaDeg;
    private static int _holdPictureLogsLeft = HoldPictureLogBudget;

    /// <summary>How many HEALTH-PROP GHOST lines a scenario may print: the first hold, and then only
    /// a hold whose picture is WRONG (the real leaf wearing the mod's overlay shader, no ghost
    /// clone, or a glow that covered nothing).</summary>
    private const int HoldPictureLogBudget = 4;

    /// <summary>The game's own prefix for PROCEDURALLY PLACED content under a door's
    /// <c>Generated Content</c>: the doorway arch (<c>PCG_CV_Doorway_01_PR</c>), the floor tile
    /// (<c>PCG_CV_Floor_Basic_07_PR</c>). Everything the 396 and 399 logs record under
    /// <c>ThickDoor/HexDoor(Clone)/Generated Content/</c> that is NOT the door leaf carries it,
    /// and the leaf assembly (<c>CR_ST_Door_02/CR_ST_Door_01/…</c>) does not.
    /// <b>ModBuild 405: that was true of ONE door kit.</b> The ModBuild 404 door
    /// (<c>CV_StoneDoorFrame_Split</c> / <c>CV_DoorSign</c>, 39 renderers, y −0.32..3.81) keeps
    /// its whole door assembly under a <c>PCG_</c> placement, so this partition is the FALLBACK
    /// now; the game's own animator handle comes first — see <see cref="LeafOf"/>.</summary>
    private const string ProceduralPlacementPrefix = "PCG_";

    /// <summary>
    /// The game's own name for the FOG-OF-WAR PREVIEW under a prop's <c>Generated Content</c>
    /// (decompiled ProceduralMapTile.ShowContent, cs:154-171: <c>FindInChildren("Generated
    /// Content")</c>, then <c>FindInChildren("Preview")</c>, and the preview is switched on for
    /// <c>Visibility.Preview</c> / <c>PreviewWithDoors</c> while every sibling is the full content).
    /// A door standing on the edge of an unrevealed room carries one: the hex kit 'Simple Tile' +
    /// 'EN_Unseen_FloorHex_Edge_Damage_03_PR' + 'EN_CR_FloorTiles_Damaged_03' on the
    /// <c>Amp_Basic_Unseen</c> shader, a 1.74 × 0.29 × 2.01 slab at y −0.37..−0.07.
    ///
    /// <para><b>THE MODBUILD 404 DEFECT, in one sentence:</b> those three were the ONLY renderers
    /// under the door with no <c>PCG_</c> ancestor, so the ModBuild 400 partition chose the fog
    /// tile as "the door leaf" — the log's <c>'Simple Tile' … shader 'Amp_Basic_Unseen' …
    /// queue 2050</c> — and the hand took a translucent floor hex with the game's own fog material
    /// on it. That is the "GHOST of the door" the user photographed: not the mod's ghost material
    /// on the real leaf, but the real fog tile wearing its own. Nothing under this node is ever a
    /// candidate again, on either route.</para>
    /// </summary>
    private const string PreviewNode = "Preview";

    /// <summary>
    /// THE PART OF A HEALTH PROP THAT MAY RIDE A HAND — for a door, the LEAF and nothing else
    /// (user ruling 2026-09-03, testing ModBuild 399: <i>"der Türrahmen kommt mit und verdreht
    /// sich … wenn ich loslasse ist der Türrahmen/Torbogen dann dauerhaft an einer anderen Stelle!
    /// Exkludiere den Torbogen komplett — es reicht wenn wirklich nur das runde Türelement in der
    /// Hand ist"</i>).
    ///
    /// <para><b>WHY THE WHOLE VISUAL WAS WRONG, AND WHY IT BROKE THE BOARD.</b> ModBuild 399
    /// reparented the prop ROOT. That root is <c>ThickDoor : (guid)</c> → <c>HexDoor(Clone)</c>,
    /// and <c>HexDoor(Clone)</c> is a <c>ProceduralDoorway</c> carrying an <c>ApparanceEntity</c>
    /// whose <c>Generated Content</c> IS the arch and the floor. Moving that root moved the entity;
    /// an Apparance entity re-generates its content when it is transformed, and the regeneration is
    /// asynchronous — so the arch was re-baked around the HAND's pose and stayed there after the
    /// transform itself had been put back. The frame that "kommt mit und verdreht sich" and the
    /// frame that is "dauerhaft an einer anderen Stelle" are the same mistake seen twice.</para>
    ///
    /// <para><b>THE PARTITION IS STRUCTURAL, NOT A NAME LIST.</b> Under the door's generated
    /// content the game places two kinds of thing: PROCEDURAL PLACEMENTS, whose roots carry the
    /// <c>PCG_</c> prefix (the arch, the floor), and the door leaf assembly, which does not. The
    /// movable set is every renderer with no <c>PCG_</c>-prefixed ancestor below the visual, and
    /// the leaf root is the lowest common ancestor of that set. Three refusals, each of which
    /// leaves the hold EMPTY rather than partial (the user's stated preference over a
    /// board-damaging hold): the set is empty; its common ancestor is the visual itself (the
    /// partition did not separate anything); or the candidate subtree contains an
    /// <c>ApparanceEntity</c> — an entity is never transformed by this class again.</para>
    ///
    /// <para><b>MODBUILD 405: THE PARTITION ABOVE PICKED THE FOG TILE.</b> On the 404 door every
    /// door renderer sat under a <c>PCG_</c> placement and the only renderers that did not were
    /// the game's fog-of-war preview hex ('Simple Tile' on <c>Amp_Basic_Unseen</c>, y −0.37..−0.07)
    /// — so the "leaf" was a translucent floor slab, which the user photographed as "a GHOST of
    /// the door", with no highlight (the glow's container was built under it and measured
    /// inactive) and a home ghost cloned from the same slab. The rule is now the game's own door
    /// handle first — the animator <c>Choreographer.OpenDoor</c> plays "Open" on — with the
    /// partition as a preview-excluded fallback. See <see cref="LeafOf"/>.</para>
    /// </summary>
    internal static GameObject? HeldPartFor(ActorBehaviour? actor)
    {
        GameObject? visual = BodyFor(actor);
        return visual == null ? null : LeafOf(visual, out _, out _, out _);
    }

    /// <summary>
    /// THE DOOR IS WHAT THE GAME SWINGS OPEN (ModBuild 405). The primary route is the game's own
    /// handle on the door: <c>Choreographer.OpenDoor</c> (decompiled cs:13305-13343) resolves the
    /// prop's GameObject and plays the "Open" state on <c>MF.GetGameObjectAnimator(doorGO)</c> —
    /// the FIRST <c>Animator</c> with a <c>runtimeAnimatorController</c> below the visual (MF.cs:
    /// 135-146) — and, when there is none yet, defers the whole call to
    /// <c>ProceduralProp.PlacementCompleteAction</c>, which says the animator lives INSIDE the
    /// asynchronously placed content. That animator's subtree is the door: the frame-and-wings
    /// assembly the game animates, whatever kit it came from and whatever its placement node is
    /// called. It is refused when it IS the visual root (nothing separated), when it contains an
    /// <c>ApparanceEntity</c> (never transformed by this class), when it sits under or contains
    /// the game's fog-of-war <see cref="PreviewNode"/>, or when it holds no mesh renderer at all.
    ///
    /// <para>The ModBuild 400 <c>PCG_</c> partition stays as the FALLBACK for a kit whose door has
    /// no animator, now with the preview subtree excluded — which, on the ModBuild 404 kit, turns
    /// the fog-tile hold into a clean refusal (an empty hand, the user's stated preference over a
    /// board-damaging one) rather than into the fog tile.</para>
    ///
    /// <para>The three <c>out</c> strings feed the log: <paramref name="why"/> is the verdict,
    /// <paramref name="route"/> names which of the two rules decided it, and
    /// <paramref name="previewExcluded"/> counts what the preview exclusion removed — a readable
    /// zero on a door with no preview.</para>
    /// </summary>
    private static GameObject? LeafOf(GameObject visual, out string why, out string route,
                                      out int previewExcluded)
    {
        int id = visual.GetInstanceID();
        float now = Time.unscaledTime;
        if (LeafCache.TryGetValue(id, out LeafEntry memo))
        {
            if (memo.Leaf != null || now < memo.RetryAt)
            {
                why = memo.Why;
                route = memo.Route;
                previewExcluded = memo.PreviewExcluded;
                return memo.Leaf;
            }
        }

        Transform top = visual.transform;
        previewExcluded = 0;

        // ROUTE 1 — the game's own door handle: the first animator with a controller, exactly the
        // walk MF.GetGameObjectAnimator does (depth-first, active objects only).
        GameObject? leaf = null;
        route = "animator";
        why = string.Empty;
        AnimatorScratch.Clear();
        visual.GetComponentsInChildren(includeInactive: false, AnimatorScratch);
        Animator? animator = null;
        for (int i = 0; i < AnimatorScratch.Count; i++)
        {
            Animator a = AnimatorScratch[i];
            if (a != null && a.runtimeAnimatorController != null)
            {
                animator = a;
                break;
            }
        }
        AnimatorScratch.Clear();

        if (animator == null)
            why = "no Animator with a controller below the visual yet (the game's OpenDoor would "
                  + "defer to PlacementCompleteAction here too)";
        else
        {
            Transform at = animator.transform;
            int meshes = CountMeshRenderers(at, out int underPreview);
            if (at == top)
                why = $"the animator sits on the visual root '{top.name}' itself, which separates nothing";
            else if (IsUnderNode(at, top, PreviewNode))
                why = $"the animator '{at.name}' sits under the game's '{PreviewNode}' fog-of-war node";
            else if (underPreview > 0)
                why = $"the animator '{at.name}' contains the game's '{PreviewNode}' node ({underPreview} "
                      + "renderer(s) under it)";
            else if (at.GetComponentInChildren<ApparanceEntity>(includeInactive: true) != null)
                why = $"the animator '{at.name}' contains an ApparanceEntity, which is never transformed";
            else if (meshes == 0)
                why = $"the animator '{at.name}' holds no mesh renderer";
            else
            {
                leaf = at.gameObject;
                why = $"'{at.name}' is the object the game's OpenDoor animates ('{animator.runtimeAnimatorController.name}'), "
                      + $"carrying {meshes} mesh renderer(s)";
            }
        }

        // ROUTE 2 — the ModBuild 400 structural partition, preview-excluded.
        if (leaf == null)
        {
            string animatorWhy = why;
            route = "pcg-partition";
            RendererScratch.Clear();
            visual.GetComponentsInChildren(includeInactive: true, RendererScratch);
            int total = RendererScratch.Count, movable = 0, withMesh = 0;
            Transform? lca = null;
            for (int i = 0; i < RendererScratch.Count; i++)
            {
                Renderer r = RendererScratch[i];
                if (r == null)
                    continue;
                if (IsUnderNode(r.transform, top, PreviewNode))
                {
                    previewExcluded++;
                    continue;
                }
                bool procedural = false;
                for (Transform? t = r.transform; t != null && t != top; t = t.parent)
                {
                    if (t.name.StartsWith(ProceduralPlacementPrefix, StringComparison.Ordinal))
                    {
                        procedural = true;
                        break;
                    }
                }
                if (procedural)
                    continue;
                movable++;
                if (HasMesh(r))
                    withMesh++;
                lca = lca == null ? r.transform : CommonAncestor(lca, r.transform, top);
            }
            RendererScratch.Clear();

            if (movable == 0)
                why = $"no movable renderer — all {total - previewExcluded} sit under a "
                      + $"'{ProceduralPlacementPrefix}' placement";
            else if (withMesh == 0)
                why = $"{movable} movable renderer(s) but none has a mesh";
            else if (lca == null || lca == top)
                why = $"the {movable} movable renderer(s) have no common ancestor below the visual — the "
                      + "partition separated nothing";
            else if (lca.GetComponentInChildren<ApparanceEntity>(includeInactive: true) != null)
                why = $"the candidate '{lca.name}' contains an ApparanceEntity, which is never transformed";
            else
            {
                leaf = lca.gameObject;
                why = $"'{lca.name}' carries {movable} of {total} renderer(s) ({withMesh} with a mesh); the "
                      + $"other {total - movable - previewExcluded} stay under their "
                      + $"'{ProceduralPlacementPrefix}' placements";
            }
            why = $"animator route refused ({animatorWhy}); PCG partition: {why}; {previewExcluded} "
                  + $"renderer(s) under the game's '{PreviewNode}' fog-of-war node were excluded from "
                  + "both routes";
        }
        else
        {
            why = $"{why}; {previewExcluded} renderer(s) under the game's '{PreviewNode}' node "
                  + "excluded";
        }

        LeafCache[id] = new LeafEntry
        {
            Leaf = leaf, Why = why, Route = route, PreviewExcluded = previewExcluded,
            RetryAt = leaf == null ? now + RetrySeconds : 0f,
        };
        return leaf;
    }

    /// <summary>True when a transform named <paramref name="node"/> lies on the chain from
    /// <paramref name="t"/> (inclusive) up to <paramref name="top"/> (exclusive).</summary>
    private static bool IsUnderNode(Transform t, Transform top, string node)
    {
        for (Transform? c = t; c != null && c != top; c = c.parent)
        {
            if (string.Equals(c.name, node, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Mesh-bearing renderers under <paramref name="root"/> (inclusive, inactive
    /// included), and how many of ALL its renderers sit under a <see cref="PreviewNode"/>.</summary>
    private static int CountMeshRenderers(Transform root, out int underPreview)
    {
        underPreview = 0;
        int meshes = 0;
        RendererScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, RendererScratch);
        for (int i = 0; i < RendererScratch.Count; i++)
        {
            Renderer r = RendererScratch[i];
            if (r == null)
                continue;
            if (IsUnderNode(r.transform, root, PreviewNode))
                underPreview++;
            else if (HasMesh(r))
                meshes++;
        }
        RendererScratch.Clear();
        return meshes;
    }

    /// <summary>World-space union of every enabled mesh renderer under <paramref name="root"/>,
    /// with the count; a zero-size box with count 0 when there is none.</summary>
    private static Bounds RendererUnion(Transform root, out int count)
    {
        count = 0;
        Bounds union = default;
        RendererScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, RendererScratch);
        for (int i = 0; i < RendererScratch.Count; i++)
        {
            Renderer r = RendererScratch[i];
            if (r == null || !HasMesh(r))
                continue;
            Bounds b = r.bounds;
            if (count == 0) union = b; else union.Encapsulate(b);
            count++;
        }
        RendererScratch.Clear();
        return union;
    }

    private static string Box(Bounds b, int count) => count == 0
        ? "no mesh renderer"
        : $"size ({b.size.x:0.00}, {b.size.y:0.00}, {b.size.z:0.00}) y {b.min.y:0.00}..{b.max.y:0.00} "
          + $"from {count} mesh renderer(s)";

    private static bool HasMesh(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr)
            return smr.sharedMesh != null;
        MeshFilter? mf = r.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null;
    }

    /// <summary>Lowest common ancestor of two transforms, not climbing above <paramref name="top"/>
    /// (returns <paramref name="top"/> itself when nothing below it is shared).</summary>
    private static Transform CommonAncestor(Transform a, Transform b, Transform top)
    {
        ChainScratch.Clear();
        for (Transform? t = a; t != null; t = t.parent)
        {
            ChainScratch.Add(t);
            if (t == top)
                break;
        }
        for (Transform? t = b; t != null; t = t.parent)
        {
            if (ChainScratch.Contains(t))
            {
                ChainScratch.Clear();
                return t;
            }
            if (t == top)
                break;
        }
        ChainScratch.Clear();
        return top;
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

        // ONLY THE LEAF RIDES THE HAND (ModBuild 400). See HeldPartFor for the partition and for
        // the ModBuild 399 defect it ends. A refusal leaves the hold EMPTY — the hand takes the
        // bodiless actor exactly as it did before 399 — because an empty hand is harmless and a
        // half-door in the hand moved the arch for the rest of the scenario.
        GameObject? leaf = LeafOf(visual, out string leafWhy, out string leafRoute, out int previewExcluded);
        if (leaf == null)
        {
            if (_loggedRefusal)
                return;
            _loggedRefusal = true;
            // HW-VERIFY: the line that says a health prop was NOT lent to the hand and why. Its
            // presence beside a 'grabbed figure (PropDummyObject)' line means the empty hand is
            // deliberate, not the ModBuild 399 defect returning.
            VRLog.Note("FigureGrab",
                $"HEALTH-PROP BODY REFUSED for the hold of '{visual.name}': {leafWhy}. Nothing is "
                + "attached and the hand takes the empty actor, which is the pre-ModBuild-399 state "
                + "and damages nothing. A partial hold is never shipped: the 399 hardware round "
                + "moved the whole prop root, and that root carries the ApparanceEntity whose "
                + "generated content IS the doorway arch — it was re-baked around the hand's pose "
                + "and stayed there after release. Logged once per scenario.");
            return;
        }
        Transform leafT = leaf.transform;

        var held = new Held
        {
            Visual = visual,
            Moved = leaf,
            OrigParent = leafT.parent,
            OrigLocalPos = leafT.localPosition,
            OrigLocalRot = leafT.localRotation,
            OrigLocalScale = leafT.localScale,
            HomeWorldPos = leafT.position,
            HomeWorldRot = leafT.rotation,
            Route = leafRoute,
            LeafUpInRoot = into.InverseTransformDirection(leafT.up),
        };
        held.LeafBox = RendererUnion(leafT, out held.LeafRenderers);
        held.BodyBox = RendererUnion(visual.transform, out held.BodyRenderers);

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
        leafT.SetParent(into, worldPositionStays: true);
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
            + "Logged once per scenario. "
            + $"ModBuild 400 CORRECTION to the sentences above: only the LEAF rides the hand now — "
            + $"'{leaf.name}' ({leafWhy}) — and the prop root with its ApparanceEntity never moves, "
            + "so the KNOWN SIDE EFFECT can no longer arise (the arch rect is seeded from renderers "
            + "that stay exactly where they were) and the frozen entities are frozen as a "
            + "precaution against a rebuild that would destroy the held leaf, not because they are "
            + "moved. The restore is MEASURED on release — grep HEALTH-PROP BODY RELEASED. "
            + $"ModBuild 405 CORRECTION: the leaf was chosen by the '{leafRoute}' route — 'animator' "
            + "is the object the game's own OpenDoor plays 'Open' on (MF.GetGameObjectAnimator), "
            + "'pcg-partition' the ModBuild 400 fallback — and the game's 'Preview' fog-of-war node "
            + $"({previewExcluded} renderer(s), the 'Simple Tile' hex kit on Amp_Basic_Unseen that the "
            + "404 hand took as the door) is excluded on both. LEAF BOX at the grab "
            + $"{Box(held.LeafBox, held.LeafRenderers)} against the WHOLE BODY "
            + $"{Box(held.BodyBox, held.BodyRenderers)}: a leaf reaching the body's top y is the frame "
            + "in the hand, a 0.3 wu slab at floor level is the fog tile again, and a door reads "
            + "roughly y 0..3.3 under a 4.6 arch. Leaf up axis in the actor root's frame "
            + $"({held.LeafUpInRoot.x:0.00}, {held.LeafUpInRoot.y:0.00}, {held.LeafUpInRoot.z:0.00}) — "
            + "the held pose is applied to the ROOT, so y=1.00 means the door stands upright in the "
            + "hand exactly as a figure does. The picture of the hold (glow, ghost, held material) "
            + "is on the HEALTH-PROP GHOST line one frame later.");
    }

    /// <summary>
    /// THE PICTURE OF THE HOLD, one frame in (ModBuild 405) — the three things the 404 round could
    /// not tell apart: what the hover glow covered, whether the home ghost is a CLONE of the leaf or
    /// the leaf itself, and what material the REAL leaf wears in the hand. Called by
    /// <c>FigureGrabbable.TickHeldScale</c> on the first held frame of a hold that has a prop body;
    /// a no-op for every ordinary miniature. Prints on the first hold of a scenario, and again only
    /// while the picture is WRONG (budgeted).
    /// </summary>
    internal static void LogHoldPicture(ActorBehaviour? actor, int glowClones, bool glowContainerActive,
                                        GameObject? ghost)
    {
        if (actor == null || !HeldBodies.TryGetValue(actor, out Held held) || held.PictureLogged)
            return;
        held.PictureLogged = true;
        if (_holdPictureLogsLeft <= 0)
            return;

        GameObject? leaf = held.Moved;
        string leafName = leaf != null ? leaf.name : "<destroyed>";
        string heldShader = "<no renderer>", heldMaterial = "<no renderer>";
        int leafRenderers = 0;
        bool leafWearsOverlay = false;
        if (leaf != null)
        {
            RendererScratch.Clear();
            leaf.GetComponentsInChildren(includeInactive: false, RendererScratch);
            for (int i = 0; i < RendererScratch.Count; i++)
            {
                Renderer r = RendererScratch[i];
                if (r == null || !HasMesh(r))
                    continue;
                if (leafRenderers == 0)
                {
                    Material? m = r.sharedMaterial;
                    heldMaterial = m != null ? m.name : "<null material>";
                    heldShader = m != null && m.shader != null ? m.shader.name : "<null shader>";
                }
                leafRenderers++;
            }
            RendererScratch.Clear();
            leafWearsOverlay = heldShader.StartsWith("GloomhavenVR/", StringComparison.Ordinal);
        }

        bool ghostIsClone = ghost != null && leaf != null
                            && ghost.GetInstanceID() != leaf.GetInstanceID()
                            && !leaf.transform.IsChildOf(ghost.transform)
                            && !ghost.transform.IsChildOf(leaf.transform);
        int ghostRenderers = 0;
        string ghostShader = "<no ghost>";
        if (ghost != null)
        {
            RendererScratch.Clear();
            ghost.GetComponentsInChildren(includeInactive: false, RendererScratch);
            for (int i = 0; i < RendererScratch.Count; i++)
            {
                Renderer r = RendererScratch[i];
                if (r == null || !HasMesh(r))
                    continue;
                if (ghostRenderers == 0)
                {
                    Material? m = r.sharedMaterial;
                    ghostShader = m != null && m.shader != null ? m.shader.name : "<null shader>";
                }
                ghostRenderers++;
            }
            RendererScratch.Clear();
        }

        bool wrong = leafWearsOverlay || !ghostIsClone || glowClones == 0 || !glowContainerActive;
        // The first hold always prints; later holds only while something above reads wrong.
        if (_holdPictureLogsLeft < HoldPictureLogBudget && !wrong)
            return;
        _holdPictureLogsLeft--;

        // HW-VERIFY: the line that separates the three ModBuild 404 symptoms. GLOW covered N
        // renderer(s) (0 = the hover lit nothing; container INACTIVE = it was built under a switched-
        // off object). GHOST CLONE=True with its own renderer count = a translucent copy stands at
        // home. HELD LEAF shader = what the hand holds; the game's door material means the SOLID
        // door, 'GloomhavenVR/Overlay' means the ghost material landed on the real leaf, and
        // 'Amp_Basic_Unseen' means the fog tile is in the hand again.
        VRLog.Note("FigureGrab",
            $"HEALTH-PROP GHOST for the hold of '{(held.Visual != null ? held.Visual.name : "<destroyed>")}': "
            + $"GLOW at the last hover covered {glowClones} renderer(s) (container "
            + $"{(glowContainerActive ? "active" : "INACTIVE")}); HOME GHOST is a CLONE: {ghostIsClone} "
            + $"({(ghost != null ? $"ghost instance {ghost.GetInstanceID()} vs leaf {(leaf != null ? leaf.GetInstanceID() : 0)}" : "no ghost object")}, "
            + $"{ghostRenderers} mesh renderer(s) on the ghost, first shader '{ghostShader}'); HELD LEAF "
            + $"'{leafName}' via the '{held.Route}' route has {leafRenderers} active mesh renderer(s), first "
            + $"material '{heldMaterial}' on shader '{heldShader}' at the first held frame — "
            + $"{(leafWearsOverlay ? "THE MOD'S OVERLAY SHADER IS ON THE REAL LEAF, which is the ghost-in-the-hand defect" : "the game's own material, so the hand holds the SOLID leaf")}. "
            + $"Verdict {(wrong ? "WRONG" : "as designed")}; {_holdPictureLogsLeft} more of these this scenario.");
    }

    /// <summary>Put the body back where the board had it and hand every <c>MonitorMovement</c>
    /// back. Idempotent, and safe against a parent destroyed during the hold (scene teardown):
    /// a dead original parent unparents to the scene root rather than throwing.</summary>
    internal static void Release(ActorBehaviour? actor)
    {
        if (actor == null || !HeldBodies.TryGetValue(actor, out Held held))
            return;
        HeldBodies.Remove(actor);

        // ORDER (ModBuild 400): the transform goes back BEFORE any MonitorMovement is handed back.
        // The 399 code did it the other way round, and an entity that is re-armed while its
        // content is still at the hand's pose is exactly what re-bakes the arch there. The leaf is
        // not an entity, so the order is a belt here — but it is the right belt.
        float deltaWU = -1f, deltaDeg = -1f;
        if (held.Moved != null)
        {
            Transform t = held.Moved.transform;
            t.SetParent(held.OrigParent != null ? held.OrigParent : null, worldPositionStays: false);
            t.localPosition = held.OrigLocalPos;
            t.localRotation = held.OrigLocalRot;
            t.localScale = held.OrigLocalScale;
            deltaWU = Vector3.Distance(t.position, held.HomeWorldPos);
            deltaDeg = Quaternion.Angle(t.rotation, held.HomeWorldRot);
        }

        for (int i = 0; i < held.Frozen.Length; i++)
        {
            ApparanceEntity e = held.Frozen[i];
            if (e != null)
                e.MonitorMovement = held.FrozenMonitor[i];
        }

        _releases++;
        if (deltaWU > _worstReleaseDeltaWU) _worstReleaseDeltaWU = deltaWU;
        if (deltaDeg > _worstReleaseDeltaDeg) _worstReleaseDeltaDeg = deltaDeg;
        // The line prints on the FIRST release and again whenever a release is worse than every
        // earlier one, so a silently failing restore cannot hide behind a clean first reading.
        bool worse = deltaWU >= 0.001f || deltaDeg >= 0.05f;
        if (_loggedRelease && !worse)
            return;
        _loggedRelease = true;
        string moved = held.Moved != null ? held.Moved.name : "<destroyed during the hold>";
        // HW-VERIFY: the PROOF that a released door is back where the board had it. 'delta 0.000 wu
        // / 0.00 deg' is a measured zero, not a missing line; anything else names a restore that
        // did not land and is the 399 'dauerhaft an einer anderen Stelle' defect in a new coat.
        VRLog.Note("FigureGrab",
            $"HEALTH-PROP BODY RELEASED '{moved}' of '{(held.Visual != null ? held.Visual.name : "<destroyed>")}': "
            + $"world delta after the restore {(deltaWU < 0 ? "UNMEASURABLE (leaf destroyed)" : $"{deltaWU:0.000} wu / {deltaDeg:0.00} deg")} "
            + $"against the pose captured at the grab; worst this scenario {_worstReleaseDeltaWU:0.000} wu / "
            + $"{_worstReleaseDeltaDeg:0.00} deg over {_releases} release(s). The prop root and its "
            + "ApparanceEntity were never transformed, so the arch had nothing to re-bake from; "
            + "MonitorMovement was handed back only AFTER the leaf was home. A non-zero delta here "
            + "means the leaf's original parent moved or was rebuilt during the hold.");
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
        LeafCache.Clear();
        _loggedRefusal = false;
        _loggedRelease = false;
        _releases = 0;
        _worstReleaseDeltaWU = 0f;
        _worstReleaseDeltaDeg = 0f;
        _holdPictureLogsLeft = HoldPictureLogBudget;
    }
}

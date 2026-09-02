using System.Collections.Generic;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// MAY A HAND TAKE THIS PROP OFF THE BOARD? The single predicate every prop-grab surface asks,
/// so the grab, the hover glow, the pick collider, the home ghost and the info panel cannot
/// disagree with each other.
///
/// <para><b>THE REPORT (ModBuild 350 hardware round), verbatim.</b> "Bitte exkludiere solche
/// Obstacles die man nicht zerstören kann bei dem Greifen wie zB die 'DarkPitObstacles' diese
/// soll erst garnicht aufnehmbar sein." A dark pit is a HOLE IN THE FLOOR. You cannot destroy it
/// and you must not be able to lift it — and "erst gar nicht aufnehmbar" is a stronger request
/// than "the grab fails": a hover glow that promises a pickup which never comes is worse than no
/// glow at all. Note the "wie zB": he named ONE example of a CLASS, so what follows is a
/// predicate, never a name blacklist.</para>
///
/// <para><b>WHERE THE ANSWER IS ENFORCED, and why that is one place.</b> A prop that fails this
/// test is never handed to <see cref="PropGrab"/>'s registry, so no
/// <see cref="GrabbableProp"/> is ever constructed for it. Everything the player can feel hangs
/// off that construction and off nothing else:</para>
/// <list type="bullet">
///   <item><b>the grab</b> — <c>VRInteractables.RegisterGrabbable</c> is the only way into
///   <c>VRInteractables.Grabbables</c>, which is the list <c>ProximityGrabber</c> walks;</item>
///   <item><b>the hover glow</b> — <c>IGrabHighlight.OnGrabHighlight</c> is called by the
///   grabber on the candidate it elected out of that same list (ProximityGrabber.cs:795-799),
///   so an unregistered prop is never a candidate and never glows;</item>
///   <item><b>the pick collider</b> — <c>FigureGrabDriver.BuildPropCollider</c> is only ever
///   called on the registration path, so a refused prop that had no collider of its own is
///   never given the mod-owned <c>VR_PropReach</c> trigger box either;</item>
///   <item><b>the home ghost</b> — <c>PropGhosts.NotifyHeld</c> only ever runs from
///   <c>GrabbableProp.OnGrab</c>;</item>
///   <item><b>the info panel</b> — <c>GrabbableProp.ShowInfo</c>, likewise.</item>
/// </list>
/// <para>So there is exactly one gate and no second opinion to keep in sync.</para>
///
/// <para><b>NO GAME STATE IS WRITTEN, and none is read that could change any.</b> Refusing to
/// lift a pit tells the game nothing about that pit: this class reads four serialized fields of
/// <c>CObjectProp</c> and returns a bool. It never calls <c>Activate</c>, <c>DestroyProp</c>,
/// <c>SetOverrideDisallowMoveOrDestroy</c> or anything else that mutates.</para>
///
/// <para><b>MULTIPLAYER.</b> Prop holds are local-only in this build BY DESIGN (see
/// <see cref="HeldProps"/>), and this predicate keeps that: it is evaluated per machine from the
/// SCENARIO STATE, which is the replicated object every peer already agrees on, so two modded
/// peers reach the same verdict without a byte on the wire and an unmodded peer is unaffected.
/// It is a refusal, additive and inert.</para>
/// </summary>
internal static class PropLift
{
    /// <summary>
    /// THE OBSTACLE FAMILIES THE GAME MODELS AS A SOLID OBJECT STANDING ON A HEX.
    ///
    /// <para>This is a POSITIVE list of the game's own <see cref="EPropType"/>, not a blacklist of
    /// prefab names, and it is deliberately a whitelist so that anything new the game ever routes
    /// into the <c>Obstacle</c> import bucket is refused until somebody looks at it — the safe
    /// direction for a request whose whole point is "do not offer me a pickup that cannot
    /// happen".</para>
    ///
    /// <para><c>GlobalSettings.GetApparancePropType</c> (GlobalSettings.cs:452-465) is what maps
    /// <see cref="EPropType"/> to <c>ScenarioManager.ObjectImportType</c>, and it routes exactly
    /// SIX values into <c>Obstacle</c>: the five below plus
    /// <see cref="EPropType.DarkPitObstacle"/>. The five below are meshes dropped onto a hex —
    /// rocks, plinths, ice walls, boulders. The sixth is a hole cut into the floor. That is the
    /// whole of the difference, and it is the difference the user named.</para>
    /// </summary>
    private static readonly EPropType[] SolidObstacleTypes =
    {
        EPropType.OneHexObstacle,
        EPropType.TwoHexObstacle,
        EPropType.ThreeHexObstacle,
        EPropType.ThreeHexCurvedObstacle,
        EPropType.ThreeHexStraightObstacle,
    };

    /// <summary>
    /// <c>CObjectProp.PropType</c> memoized by prefab name.
    ///
    /// <para><b>WHY MEMOIZED AT ALL.</b> The game's property is not a field read: it runs two
    /// LINQ <c>SingleOrDefault</c> passes over <c>Enum.GetValues</c> arrays comparing
    /// <c>x.ToString()</c> to the prefab name (CObjectProp.cs:48-59), so every call allocates a
    /// delegate and ~45 strings. That is nothing at the handful-per-scenario rate this predicate
    /// actually runs at, but the prop census asks the same question about the same props on every
    /// one of its twelve walks, and "near-free" scene work has shipped as a per-frame cost on
    /// this project twice. A prefab name maps to one type forever, so one dictionary hit is the
    /// honest steady state. Bounded by the number of DISTINCT prop prefab names the game has —
    /// about twenty — and cleared with the registry.</para>
    /// </summary>
    private static readonly Dictionary<string, EPropType> PropTypeByPrefab = new(16);

    /// <summary>Human-readable verdict for one prop, for the census and for the one-shot refusal
    /// line. Kept SHORT: it is printed once per named sample inside a line that already carries
    /// five other columns.</summary>
    internal const string ReasonLiftable = "yes";

    /// <summary>
    /// May a hand lift this prop off the board?
    ///
    /// <para><b>TERM 1 — the import-type whitelist</b>
    /// (<see cref="FigureGrabDriver.IsLiftableProp"/>), unchanged: chests, goal chests, gold,
    /// traps, obstacles, carryable quest items and loose resources. Terrain you merely walk
    /// through more slowly was never in it.</para>
    ///
    /// <para><b>TERM 2 — the game's own veto flag.</b>
    /// <c>CObjectProp.OverrideDisallowDestroyAndMove</c> (CObjectProp.cs:112) is the ONLY
    /// per-prop bit in the entire rule library that means "this prop may neither be moved nor
    /// destroyed", and every destroy and every move ability the game has consults it FIRST and
    /// UNCONDITIONALLY: <c>CAbilityDestroyObstacle</c> (cs:130 and :519),
    /// <c>CAbilityMoveObstacle</c> (cs:316 and :491), <c>CAbilityMoveTrap</c> (cs:391) and
    /// <c>CAttackEffect</c> (cs:218). The game surfaces it to the player itself, as the hex
    /// tooltip's IMMOVABLE_OBSTACLE_DESCR_TOOLTIP (WorldspaceStarHexDisplay.cs:3435). Lifting a
    /// prop out of the world IS a move, so this flag is the exact question, asked in the game's
    /// own words.</para>
    ///
    /// <para><b>TERM 3 — an obstacle must be a solid object, not a hole.</b> See
    /// <see cref="SolidObstacleTypes"/>. It applies to the <c>Obstacle</c> import type only:
    /// chests, gold, quest items and resources are LOOTED and traps are SPRUNG — none of them is
    /// an "obstacle you cannot destroy" and the report did not ask about them.</para>
    ///
    /// <para><b>WHY HEALTH IS NOT A TERM, and this is the trap worth naming.</b>
    /// <c>PropHealthDetails.HasHealth</c> looks like the obvious discriminator — a prop is given
    /// a <c>CObjectActor</c> only when it is configured for health (CMap.cs:502-518,
    /// CObjectActor.cs:99-104) — and it is the WRONG one, because this project's own hardware log
    /// already falsified it. The ModBuild 350 census read
    /// <c>Choreographer.m_ClientObjects held 0 entr(y/ies)</c> in the very scenario that held the
    /// dark pit AND the four <c>OneHexObstacle</c>s the user successfully grabbed. No prop on that
    /// board had health — not the pit, and not the rocks. A "destructible == has health" rule
    /// would therefore have refused every rock he is happy with and would have looked, in the
    /// log, exactly like a rule that worked. The rocks are destructible for a different reason:
    /// an obstacle with NO health and no veto flag is exactly what a Destroy Obstacle ability
    /// targets (CAbilityDestroyObstacle.cs:130 removes the ones with health, not the ones
    /// without) and what an attack destroys outright (CAttackEffect.cs:218).</para>
    /// </summary>
    /// <param name="prop">The scenario-state prop. Null is not liftable.</param>
    /// <param name="why">Short verdict for the log: <see cref="ReasonLiftable"/>, or the term that
    /// refused it. Always set.</param>
    internal static bool MayBeLifted(CObjectProp? prop, out string why)
    {
        if (prop == null)
        {
            why = "no (null prop)";
            return false;
        }

        if (!FigureGrabDriver.IsLiftableProp(prop))
        {
            why = "no (import type not liftable)";
            return false;
        }

        if (prop.OverrideDisallowDestroyAndMove)
        {
            why = "no (game flags it OverrideDisallowDestroyAndMove — immovable/indestructible)";
            return false;
        }

        if (prop.ObjectType != ScenarioManager.ObjectImportType.Obstacle)
        {
            why = ReasonLiftable;
            return true;
        }

        EPropType type = ResolvePropType(prop);

        // FAIL OPEN on an unrecognised prefab name, and only on that. EPropType.None means the
        // prefab name is in NEITHER of the game's two prop enums, i.e. we know nothing — and
        // inventing a refusal out of ignorance would silently break a pickup that works today.
        // A prefab name the game DOES know but this list does not is a different case and is
        // refused above: that is a new obstacle family, and it should be looked at before it is
        // offered to a hand.
        if (type == EPropType.None)
        {
            why = ReasonLiftable;
            return true;
        }

        for (int i = 0; i < SolidObstacleTypes.Length; i++)
        {
            if (SolidObstacleTypes[i] == type)
            {
                why = ReasonLiftable;
                return true;
            }
        }

        why = $"no ({type} is not a solid obstacle standing on a hex — it cannot be destroyed or lifted)";
        return false;
    }

    /// <summary>Convenience overload for callers that only want the verdict.</summary>
    internal static bool MayBeLifted(CObjectProp? prop) => MayBeLifted(prop, out _);

    /// <summary>
    /// <c>prop.PropType</c> through <see cref="PropTypeByPrefab"/>. Wrapped because the property
    /// is game code running LINQ over reflection-produced enum arrays inside a discovery scan: a
    /// throw here would take the whole prop registry pass with it, and a scan that dies leaves the
    /// board with no grabbable props at all.
    /// </summary>
    private static EPropType ResolvePropType(CObjectProp prop)
    {
        string name = prop.PrefabName ?? string.Empty;
        if (PropTypeByPrefab.TryGetValue(name, out EPropType cached))
            return cached;

        EPropType type;
        try
        {
            type = prop.PropType;
        }
        catch
        {
            type = EPropType.None;
        }
        PropTypeByPrefab[name] = type;
        return type;
    }

    /// <summary>Drop the memo — scenario teardown, from <see cref="PropGrab.Clear"/>. Not
    /// required for correctness (a prefab name means the same thing in every scenario); it keeps
    /// a long session from holding names no board will ask about again.</summary>
    internal static void ClearCache() => PropTypeByPrefab.Clear();
}

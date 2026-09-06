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
/// <para><b>AND THE ModBuild 450 CORRECTION, which RETIRES one of the two terms that report grew.</b>
/// "In dem Test-Szenario gibt es einen 'Brunnen' - das ist ein nicht zerstörbares Hindernis. Ich
/// will trotzdem das man es normal in die Hand nehmen kann (inkl. Highlighting, Multiplayer sync
/// etc.). Das geht aktuell nicht." The well is an indestructible obstacle and he wants it lifted
/// like any other prop, glow and mirror included. That is a DIRECT reversal of the criterion the
/// 350 gate was written against — "Obstacles die man nicht zerstören kann" — and the newer ruling
/// wins. The 2026-09-06 log is unambiguous about which term was refusing it: on a board of 22
/// props, <c>REFUSED as unliftable (1 in all)</c>, and the one is
/// <c>'OneHexObstacle' Obstacle hasHealth=NO disallowMoveOrDestroy=YES</c>. It resolved, it drew,
/// it stood on one hex — the destructibility flag was the whole of the refusal.</para>
///
/// <para><b>WHAT SURVIVES OF 350 IS THE EXAMPLE, NOT THE CRITERION, AND THAT COSTS NOTHING.</b> The
/// only object he ever named is the dark pit, and the dark pit is refused by the SOLID-OBSTACLE
/// term on its own, with no reference to destructibility at all:
/// <c>GlobalSettings.GetApparancePropType</c> (GlobalSettings.cs:446-460) routes exactly six
/// <see cref="EPropType"/> values into the <c>Obstacle</c> import bucket and
/// <see cref="SolidObstacleTypes"/> whitelists five of them — the sixth is
/// <see cref="EPropType.DarkPitObstacle"/>. So dropping the destructibility term leaves the pit
/// exactly as unliftable as it was, by a term that was already carrying it, and this file gets
/// SMALLER rather than growing a well-shaped exception beside a pit-shaped one.</para>
///
/// <para><b>AND THAT FLAG WAS NEVER THE RIGHT QUESTION FOR A VR GESTURE.</b>
/// <c>OverrideDisallowDestroyAndMove</c> is a RULES bit: it is what
/// <c>CAbilityDestroyObstacle</c> (cs:130, :519), <c>CAbilityMoveObstacle</c> (cs:316, :491),
/// <c>CAbilityMoveTrap</c> (cs:391) and <c>CAttackEffect</c> (cs:218) consult before a card MOVES
/// or DESTROYS a prop in the game's model. Picking a prop up in VR does neither:
/// <see cref="GrabbableProp"/> rides the VISUAL on the hand and glides it home on release, and no
/// rule-library call is made at any point — the prop's tile, its pathing blockers, its line of
/// sight and its activation state are exactly what they were before the hand touched it. So the
/// well stays immovable to every ability in the game while being liftable by a hand, and that is
/// not a contradiction: they are answers to two different questions, and this predicate had been
/// answering the game's.</para>
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
/// lift a pit — or accepting a well — tells the game nothing about either: this class reads three
/// serialized fields of <c>CObjectProp</c> and returns a bool. It never calls <c>Activate</c>,
/// <c>DestroyProp</c>, <c>SetOverrideDisallowMoveOrDestroy</c> or anything else that mutates.</para>
///
/// <para><b>MULTIPLAYER — and this paragraph was STALE, so it is re-derived rather than edited.</b>
/// It used to read "prop holds are local-only in this build BY DESIGN". They are not, and have not
/// been since the held-prop mirror landed: a held prop is sent to every modded peer over extension
/// record 37 (<c>NetProtocol.ExtIdHeldProp</c>, <c>Net.NetProps</c> / <see cref="NetHeldProps"/>),
/// two slots per player, one per hand, exactly as a held figure is. So "inkl. Multiplayer sync"
/// needs NO new wire surface for the well: this predicate decides only whether a
/// <see cref="GrabbableProp"/> is constructed, and the mirror, the glow, the ghost and the card all
/// hang off the grab that follows.</para>
///
/// <para>The predicate itself still sends nothing. It is evaluated per machine from the SCENARIO
/// STATE — <c>OverrideDisallowDestroyAndMove</c> and <c>PrefabName</c> are both serialized on
/// <c>CObjectProp</c> (CObjectProp.cs:233/:290) and both are compared by the game's own MP state
/// diff (CObjectProp.cs:799) — so every peer already agrees on the inputs and two modded peers
/// reach the same verdict without a byte on the wire. WIDENING it therefore widens both sides
/// identically, which is what makes the well's mirror work with no protocol change at all; an
/// unmodded peer is unaffected either way.</para>
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
    /// <para><b>TERM 2 — an obstacle must be a solid object, not a hole.</b> See
    /// <see cref="SolidObstacleTypes"/>. It applies to the <c>Obstacle</c> import type only:
    /// chests, gold, quest items and resources are LOOTED and traps are SPRUNG — none of them is
    /// a "hole in the floor" and the report did not ask about them.</para>
    ///
    /// <para><b>THE TERM THAT USED TO STAND BETWEEN THEM IS GONE (ModBuild 450), and its removal
    /// IS the well fix.</b> It read <c>if (prop.OverrideDisallowDestroyAndMove) return false;</c>
    /// and it was the single term the 2026-09-06 log caught refusing the well. The class note
    /// carries the whole argument; the short form is that the flag is a RULES bit about cards
    /// moving and destroying props, a VR lift moves and destroys nothing, and the one object the
    /// 350 report actually named — the dark pit — is refused by TERM 2 above without it. Do not
    /// re-add it as a special case for some future prop: if a prop must not be liftable, the
    /// honest place is a family this whitelist does not contain.</para>
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

        // NO DESTRUCTIBILITY TERM HERE, DELIBERATELY (ModBuild 450) — see the method note. The
        // census still prints `disallowMoveOrDestroy=` on every prop line, and it is now the column
        // that PROVES this: a `disallowMoveOrDestroy=YES ... MAYLIFT=yes GRABBABLE=yes` row is the
        // well, lifted.
        if (prop.ObjectType != ScenarioManager.ObjectImportType.Obstacle)
        {
            why = ReasonLiftable;
            return true;
        }

        EPropType type = ResolvePropType(prop);

        // FAIL OPEN on an unrecognised prefab name, and only on that. EPropType.None means the
        // prefab name is in NEITHER of the game's two prop enums, i.e. we know nothing — and
        // inventing a refusal out of ignorance would silently break a pickup that works today.
        // A prefab name the game DOES know but this list does not is a different case and falls
        // through to the refusal BELOW: that is a new obstacle family, and it should be looked at
        // before it is offered to a hand.
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

        why = $"no ({type} is not a solid obstacle standing on a hex — a hole in the floor, or an "
            + "obstacle family nobody has looked at yet; there is no mesh here to lift)";
        return false;
    }

    /// <summary>Convenience overload for callers that only want the verdict.</summary>
    internal static bool MayBeLifted(CObjectProp? prop) => MayBeLifted(prop, out _);

    /// <summary>
    /// <c>prop.PropType</c> through <see cref="PropTypeByPrefab"/>. Wrapped because the property
    /// is game code running LINQ over reflection-produced enum arrays inside a discovery scan: a
    /// throw here would take the whole prop registry pass with it, and a scan that dies leaves the
    /// board with no grabbable props at all.
    ///
    /// <para>INTERNAL rather than private since ModBuild 371 so <see cref="PropReach"/> can ask the
    /// same question through the same memo. It must not re-derive it: <c>CObjectProp.PropType</c>
    /// runs two LINQ passes over <c>Enum.GetValues</c> comparing <c>x.ToString()</c> to the prefab
    /// name (CObjectProp.cs:48-59), and the prop census asks about the same props on every walk —
    /// a second uncached caller would re-introduce exactly the allocation this memo exists to
    /// remove.</para>
    /// </summary>
    internal static EPropType ResolvePropType(CObjectProp prop)
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

using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// IS THIS OBJECT PART OF A FIGURE? — the one question the Hands scenery classes have to get
/// right, and the one ModBuild 431 got wrong on every single object it asked about.
///
/// <para><b>THE DEFECT.</b> <see cref="SceneClothHands"/> excluded actor cloth with
/// <c>cloth.GetComponentInParent&lt;ActorBehaviour&gt;()</c>. The ModBuild-431 hardware log
/// reports <c>6 simulating scenery cloth(s) of 6 found (0 skipped as actor cloth)</c> while every
/// one of those six sits at
/// <c>Board/&lt;guid&gt;/HE_Mindthief_PR(Clone)/HE_Mindthief/Mindthief_Cloth_Front</c> and friends
/// — i.e. all six ARE actor cloth and none was excluded. The test did not return a wrong answer
/// for a subtle reason; it asked a question whose answer is structurally always no.</para>
///
/// <para><b>WHY IT IS ALWAYS NO.</b> <c>Choreographer</c> builds a figure like this
/// (decompiled GH.Runtime/Choreographer.cs:1065-1082, and the same shape again at :880-897):</para>
/// <code>
/// GameObject gameObject = Object.Instantiate(m_ActorPrefab);      // ← carries ActorBehaviour
/// Animator animator = characterInstance.GetComponentsInChildren&lt;Animator&gt;()
///                        .FirstOrDefault(x =&gt; layer is "Hero" or "Monster");
/// gameObject.transform.SetParent(animator.transform);             // ← UNDER the animated object
/// animator.gameObject.AddComponent&lt;ActorEvents&gt;();
/// </code>
/// <para>The <c>ActorBehaviour</c> lives on an instance of <c>m_ActorPrefab</c> that is parented
/// UNDER the animated object — so it is the cloth's SIBLING SUBTREE, never its ancestor. The game
/// itself never looks up for it: <c>ActorBehaviour.SetActor</c> collects the actor's cloth with
/// <c>m_Animator.gameObject.GetComponentsInChildren&lt;Cloth&gt;()</c> (ActorBehaviour.cs:122),
/// <c>ActorBehaviour.GetActorBehaviour</c> falls back to <c>GetComponentInChildren</c>
/// (:221-233), and <c>IdleSMB</c> reads
/// <c>m_Animator.gameObject.GetComponentInChildren&lt;ActorBehaviour&gt;()</c> (IdleSMB.cs:33).
/// Every one of them looks DOWN from the animated object. A parent walk was looking the wrong
/// way.</para>
///
/// <para><b>THE MARKER THAT DOES WORK.</b> <c>ActorEvents</c> is added at runtime, by those two
/// Choreographer sites and nowhere else in the whole decompiled game — every other mention of the
/// type is a lookup. It is added to <c>animator.gameObject</c>, which is exactly the object
/// <c>ActorBehaviour</c> then sweeps for cloth. So "an <c>ActorEvents</c> on me or above me" IS
/// the game's own definition of actor-owned, expressed as a component test with no name
/// matching in it. That is TERM 2 below, and it is the term that fires.</para>
///
/// <para><b>WHY THREE TERMS AND NOT ONE.</b> Term 1 (an <c>ActorBehaviour</c> above) is kept
/// because it is correct wherever it fires and costs one call; a summon or an editor-placed actor
/// that does carry it on a real ancestor is still caught. Term 3 (an <c>ActorBehaviour</c> under
/// the nearest driving <c>Animator</c> ancestor) is the game's own <c>GetActorBehaviour</c> shape
/// and catches an actor built by a path neither of the other two sees. Each term reports WHICH
/// one fired, because a boolean with no evidence is how the previous version stayed wrong for
/// four builds while printing a number that looked like an answer.</para>
///
/// <para><b>COST.</b> Never called per frame. <see cref="SceneClothHands"/> asks once per cloth
/// per registry sweep (a handful of objects, every 3 s); <see cref="SceneHangingHands"/> asks once
/// per renderer, ever, and memoizes the verdict. Term 3's subtree walk is bounded to the FIRST
/// ancestor <c>Animator</c> that actually drives something (a controller-less Animator is not an
/// actor's) so it cannot degrade into a scene sweep from a high-up ancestor.</para>
/// </summary>
internal static class SceneryActors
{
    /// <summary>How far up a hierarchy the walk goes. A figure's cloth sits 2 levels under the
    /// animated object and the animated object 2 under the board; 12 is far past any of that and
    /// bounds the cost against a deeply nested prop.</summary>
    internal const int MaxDepth = 12;

    /// <summary>Is this transform part of a figure? The allocation-free form — no evidence string
    /// is built, so this is the one a sweep may call.</summary>
    internal static bool IsActorOwned(Transform? t)
    {
        if (t == null)
            return false;
        return AboveActorBehaviour(t) != null
               || AboveActorEvents(t, out _) != null
               || UnderAnimatorActorBehaviour(t, out _) != null;
    }

    /// <summary>
    /// Is this transform part of a figure, and BY WHICH TERM? The evidence string is built only
    /// here, so a caller that wants a census line pays for it and a caller in a loop does not.
    ///
    /// <para>The string always names the term and the object it fired on, in both directions:
    /// a scenery verdict says which terms were tried and how far, so "0 skipped as actor cloth"
    /// can never again mean "the test was blind" and look like "there are no actors".</para>
    /// </summary>
    internal static bool IsActorOwned(Transform? t, out string evidence)
    {
        if (t == null)
        {
            evidence = "no transform (destroyed mid-sweep)";
            return false;
        }

        ActorBehaviour? above = AboveActorBehaviour(t);
        if (above != null)
        {
            evidence = $"ACTOR by term 1: ActorBehaviour on ancestor '{above.gameObject.name}'";
            return true;
        }

        ActorEvents? events = AboveActorEvents(t, out int eventsDepth);
        if (events != null)
        {
            evidence = $"ACTOR by term 2: ActorEvents on '{events.gameObject.name}' "
                       + $"{eventsDepth} level(s) up — Choreographer adds that component to the "
                       + "actor's animated object, which is the exact object ActorBehaviour "
                       + "sweeps for cloth (ActorBehaviour.cs:122)";
            return true;
        }

        ActorBehaviour? under = UnderAnimatorActorBehaviour(t, out string animatorName);
        if (under != null)
        {
            evidence = $"ACTOR by term 3: ActorBehaviour '{under.gameObject.name}' sits UNDER the "
                       + $"driving Animator on ancestor '{animatorName}' — the same shape "
                       + "ActorBehaviour.GetActorBehaviour uses";
            return true;
        }

        evidence = $"SCENERY: no ActorBehaviour above, no ActorEvents within {MaxDepth} level(s), "
                   + "and no ActorBehaviour under the nearest driving Animator ancestor";
        return false;
    }

    /// <summary>TERM 1 — an <c>ActorBehaviour</c> on a real ancestor. <c>includeInactive: true</c>
    /// on purpose: the no-argument overload silently skips ancestors whose GameObject is inactive,
    /// and a figure being spawned or a figure inside a closed room has them.</summary>
    private static ActorBehaviour? AboveActorBehaviour(Transform t)
    {
        try
        {
            return t.GetComponentInParent<ActorBehaviour>(true);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>TERM 2 — the marker Choreographer stamps on the actor's animated object. This is
    /// the term that fires for every figure in the game.</summary>
    private static ActorEvents? AboveActorEvents(Transform t, out int depth)
    {
        depth = 0;
        Transform? p = t;
        for (int i = 0; p != null && i < MaxDepth; i++, p = p.parent)
        {
            ActorEvents? found;
            try
            {
                found = p.GetComponent<ActorEvents>();
            }
            catch
            {
                found = null;
            }
            if (found != null)
            {
                depth = i;
                return found;
            }
        }
        return null;
    }

    /// <summary>TERM 3 — <c>ActorBehaviour.GetActorBehaviour</c>'s own shape: look DOWN from the
    /// animated object. Bounded to the FIRST ancestor Animator that actually drives something,
    /// because an Animator with no <c>runtimeAnimatorController</c> is not the one
    /// <c>MF.GetGameObjectAnimator</c> would have picked, and walking every Animator ancestor's
    /// subtree is how a fallback turns into a per-sweep scene sweep.</summary>
    private static ActorBehaviour? UnderAnimatorActorBehaviour(Transform t, out string animatorName)
    {
        animatorName = "";
        Transform? p = t.parent;
        for (int i = 0; p != null && i < MaxDepth; i++, p = p.parent)
        {
            Animator? a;
            try
            {
                a = p.GetComponent<Animator>();
            }
            catch
            {
                a = null;
            }
            if (a == null || a.runtimeAnimatorController == null)
                continue;
            animatorName = p.name;
            try
            {
                return p.GetComponentInChildren<ActorBehaviour>(true);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }
}

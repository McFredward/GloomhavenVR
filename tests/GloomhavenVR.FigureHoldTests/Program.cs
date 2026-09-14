using System;
using System.Reflection;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Net;
using UnityEngine;
using ScenarioRuleLibrary;
static class Program
{
    private static int assertions;
    private static void Check(bool pass, string label)
    {
        assertions++;
        if (!pass)
            throw new InvalidOperationException(label);
    }
    private static ActorBehaviour Actor(string id)
    {
        var a = new ActorBehaviour();
        a.Actor.ActorGuid = id;
        a.m_RootGameObject.Actor = a;
        a.m_AnimatedGameObject.transform.parent = a.m_RootGameObject.transform;
        WorldspaceUITools.Instance._panelUIControllers.Add(new()
        {
            m_ObjectToTrack = a.m_RootGameObject
        });
        return a;
    }
    private static void Hold(int peer, int slot, ActorBehaviour a) => NetFigures.ApplyRemoteHeld(peer, slot, NetFigures.StableActorId(a.Actor), new(0, 8, 0), Quaternion.identity);
    private static object? Patch(string name, ActorBehaviour a) => typeof(ActorBehaviour_HeldTransform_Patch).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { a });
    private static void Main()
    {
        for (int round = 0; round < 32; round++)
        {
            var a = Actor("moving" + round);
            var b = Actor("idle" + round);
            a.m_RootGameObject.transform.localPosition = new(3, 0, 2);
            Hold(1, 0, a);
            Hold(1, 1, b);
            NetFigures.Tick();
            Check(NetHeldFigures.Owns(a) && NetHeldFigures.Owns(b), "Both hands must hold independent figures");
            Check(a.m_RootGameObject.transform.position.y > 7, "Harness must actually move remote mini into the hand");
            var ghost = FigureGhosts.GhostFor(a)!;
            FigureRingSuppressor.RecordGameIntent(a, true);
            a.m_Hilight.SetActive(false);
            Patch("SetLocoTarget_Prefix", a);
            Check(a.m_RootGameObject.transform.position == new Vector3(3, 0, 2), "Native movement must sample board origin, not the hand pose");
            Check(!ghost.activeSelf && FigureGhosts.GhostFor(a) == null, "Home ghost must stop drawing in the native action frame");
            Check(a.m_Hilight.activeSelf, "Native selection ring must restore with its released actor");
            Check(!NetHeldFigures.Owns(a) && NetHeldFigures.Owns(b), "Action must release only its own actor");
            a.m_RootGameObject.transform.position = new(4, 0, 2);
            // native locomotion after the prefix
            Hold(1, 0, a);
            NetFigures.Tick();
            Check(!NetHeldFigures.Owns(a) && a.m_RootGameObject.transform.position == new Vector3(4, 0, 2), "Stale hold must not resume after native action returns idle");
            Hold(1, 1, a);
            NetFigures.ReleaseRemoteSlot(1, 0);
            Hold(1, 1, a);
            NetFigures.Tick();
            Check(!NetHeldFigures.Owns(a), "Shifted held slot must retain interrupted actor quarantine");
            NetFigures.ReleaseRemoteSlot(1, 1);
            Hold(1, 0, a);
            NetFigures.Tick();
            Check(NetHeldFigures.Owns(a), "Owner release must permit a genuine new grab");
            NetFigures.ReleaseRemote(1);
            foreach (string seam in new[] { "PushPullToLocation_Prefix", "TeleportToLocation_Prefix", "TeleportToCurrentLocoTarget_Prefix", "ForceSetLocoIntermediateTarget_Prefix" })
            {
                a.m_RootGameObject.transform.position = new(3, 0, 2);
                Hold(1, 0, a);
                NetFigures.Tick();
                Patch(seam, a);
                Check(!NetHeldFigures.Owns(a) && a.m_RootGameObject.transform.position == new Vector3(3, 0, 2), "Native motion seam must release at home: " + seam);
                NetFigures.ReleaseRemote(1);
            }
            a.m_RootGameObject.transform.position = new(3, 0, 2);
            Hold(1, 0, a);
            NetFigures.Tick();
            a.Busy = true;
            Check((bool)Patch("Update_Prefix", a)!, "Non-idle native Update must resume immediately");
            Check(a.m_RootGameObject.transform.position == new Vector3(3, 0, 2), "Attack frame must start at board home");
            a.Busy = false;
            NetFigures.ReleaseRemote(1);
            foreach (string flag in new[] { "IsMoving", "m_NewLocoTarget", "m_Jump", "m_IsPushPullInProgress", "m_Teleport" })
            {
                Hold(1, 0, a);
                NetFigures.Tick();
                typeof(ActorBehaviour).GetField(flag)!.SetValue(a, true);
                NetFigures.Tick();
                Check(!NetHeldFigures.Owns(a), "Native locomotion flag must release even within Idle-Run: " + flag);
                typeof(ActorBehaviour).GetField(flag)!.SetValue(a, false);
                NetFigures.ReleaseRemote(1);
            }
            Hold(1, 0, a);
            NetFigures.Tick();
            a.Busy = true;
            NetFigures.Tick();
            Check(!NetHeldFigures.Owns(a), "Interpolation must independently relinquish non-idle actors");
            a.Busy = false;
            NetFigures.ReleaseRemote(1);
            a.Busy = true;
            Hold(1, 0, a);
            Check(!NetHeldFigures.Owns(a), "Late first packet must not adopt a busy figure");
            a.Busy = false;
            Hold(1, 0, a);
            Check(!NetHeldFigures.Owns(a), "Rejected busy lease must remain rejected after idle");
            NetFigures.ReleaseRemote(1);
            Hold(1, 0, a);
            NetFigures.Tick();
            Hold(2, 0, a);
            NetFigures.Tick();
            Patch("SetLocoTarget_Prefix", a);
            Check(!NetHeldFigures.Owns(a), "All peers must relinquish the same native actor together");
            NetFigures.ReleaseRemote(1);
            Hold(2, 0, a);
            Check(!NetHeldFigures.Owns(a), "One peer release must not clear another interrupted lease");
            NetFigures.ReleaseRemote(2);
            a.m_RootGameObject.transform.localScale = Vector3.one;
            Hold(1, 0, a);
            NetFigures.Tick();
            NetFigures.ApplyHeldStretch(2, 3, 1);
            Hold(2, 0, a);
            NetFigures.Tick();
            Check(a.m_RootGameObject.transform.localScale.x > 2.9f, "Harness must render peer stretch");
            NetFigures.ReleaseRemoteSlot(1, 0);
            Check(a.m_RootGameObject.transform.localScale.x > 2.9f, "One alias release must not reset another active holder's scale");
            Patch("SetLocoTarget_Prefix", a);
            Check(a.m_RootGameObject.transform.localScale == Vector3.one, "Forced action must restore the shared native home scale");
            NetFigures.ReleaseRemote(1);
            NetFigures.ReleaseRemote(2);
            Hold(1, 0, a);
            NetFigures.Tick();
            Patch("SetLocoTarget_Prefix", a);
            a.m_RootGameObject = null!;
            NetFigures.Tick();
            Hold(1, 0, a);
            var replacement = Actor(a.Actor.ActorGuid);
            replacement.m_RootGameObject.transform.position = new(5, 0, 1);
            // Make the old cache entry genuinely unresolved, then publish its replacement.
            a.Actor = null!;
            Hold(1, 1, replacement);
            NetFigures.ReleaseRemoteSlot(1, 0);
            NetFigures.Tick();
            Check(!NetHeldFigures.Owns(replacement), "Actor replacement and shifted alias must retain the interrupted stable-id lease");
            NetFigures.ReleaseRemote(1);
            a = replacement;
            Hold(1, 0, a);
            Hold(1, 1, b);
            NetFigures.Tick();
            var choreo = new Choreographer();
            Choreographer_HeldFigureAction_Patch.ReleaseParticipants(choreo, new CActorIsAttacking_MessageData { m_AttackingActor = a.Actor, m_ActorsAttacking = new() { b.Actor } });
            Check(!NetHeldFigures.Owns(a) && !NetHeldFigures.Owns(b), "Attack must restore attacker and actual target before native facing reads");
            NetFigures.ReleaseRemote(1);
            Hold(1, 0, a);
            Hold(1, 1, b);
            Choreographer_HeldFigureAction_Patch.ReleaseParticipants(choreo, new CActorBeenDamaged_MessageData { m_ActorBeingDamaged = a.Actor, m_ActorSpawningMessage = b.Actor });
            Check(!NetHeldFigures.Owns(a) && NetHeldFigures.Owns(b), "Damage must release its named target, not an unrelated spawning actor");
            NetFigures.ReleaseRemote(1);
            Hold(1, 0, a);
            Hold(1, 1, b);
            Choreographer_HeldFigureAction_Patch.ReleaseParticipants(choreo, new CActorDead_MessageData { m_Actor = a.Actor, m_ActorSpawningMessage = b.Actor });
            Check(!NetHeldFigures.Owns(a) && NetHeldFigures.Owns(b), "Death must release the actual dying actor");
            NetFigures.ReleaseRemote(1);
            Hold(1, 0, a);
            Choreographer_HeldFigureAction_Patch.ReleaseParticipants(choreo, new CMessageData { m_Type = CMessageData.MessageType.PlayerSelectingToAvoidDamageOrNot, m_ActorSpawningMessage = a.Actor });
            Check(NetHeldFigures.Owns(a), "Human decision must preserve an idle held actor");
            var animator = new Animator(a.m_AnimatedGameObject.transform);
            var animationPrefix = typeof(MF_HeldFigureAnimation_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
            animationPrefix.Invoke(null, new object[] { animator, "SleepIdle" });
            Check(NetHeldFigures.Owns(a), "Native idle clip must not release an inspected figure");
            animator.ValidState = false;
            animationPrefix.Invoke(null, new object[] { animator, "Attack" });
            Check(NetHeldFigures.Owns(a), "Missing native animation must not release a figure");
            animator.ValidState = true;
            animator.ThrowOnState = true;
            animationPrefix.Invoke(null, new object[] { animator, "Attack" });
            Check(NetHeldFigures.Owns(a), "Cosmetic animator probe failure must not escape the native animation prefix");
            animator.ThrowOnState = false;
            choreo.ThrowOnLookup = true;
            typeof(Choreographer_HeldFigureAction_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { choreo, new CActorDead_MessageData { m_Actor = a.Actor } });
            Check(NetHeldFigures.Owns(a), "Cosmetic participant lookup failure must not escape native message dispatch");
            choreo.ThrowOnLookup = false;
            animationPrefix.Invoke(null, new object[] { animator, "Attack" });
            Check(!NetHeldFigures.Owns(a), "Actual non-idle animation must relinquish its exact held actor before Play");
            NetFigures.ReleaseRemote(1);
            var hand = new GloomhavenVR.Hands.VRHand();
            var failedLocal = FigureGrabbable.Create(a, hand);
            failedLocal.ThrowOnRestore = true;
            Patch("SetLocoTarget_Prefix", a);
            Check(HeldFigures.Owns(a), "Cosmetic restore failure must not escape the native movement prefix");
            failedLocal.ThrowOnRestore = false;
            failedLocal.Restore();
            var local = FigureGrabbable.Create(a, hand);
            a.Busy = true;
            local.OnRelease(hand, Vector3.zero);
            Check(local.RestoreCount == 1 && !local.GlidingNow && !HeldFigures.Owns(a), "Local forced release must be immediate, never a release glide");
            a.Busy = false;
            local = FigureGrabbable.Create(a, hand);
            local.OnRelease(hand, Vector3.zero);
            Check(local.GlidingNow && HeldFigures.Owns(a), "Voluntary idle release must retain its original transmitted glide");
            FigureGrabbable.ReleaseForNativeAction(a);
            Check(!local.GlidingNow && !HeldFigures.Owns(a), "Native action must interrupt a local release glide immediately");
            local = FigureGrabbable.Create(a, hand);
            local.OnRelease(hand, Vector3.zero);
            a.Busy = true;
            local.TickLocalGlide();
            Check(!local.GlidingNow && !HeldFigures.Owns(a), "Glide tick must independently yield to native non-idle animation");
            a.Busy = false;
            HeldFigures.Actors.Add(a);
            Hold(1, 0, a);
            NetFigures.Tick();
            Check(HeldFigures.Owns(a) && !NetHeldFigures.Owns(a), "Remote samples must not steal local pose ownership");
            HeldFigures.Actors.Clear();
            NetFigures.ReleaseRemote(1);
            Hold(1, 0, a);
            a.Busy = true;
            Patch("LateUpdate_Prefix", a);
            Check(!NetHeldFigures.Owns(a), "Late native action must not remain pinned");
            a.Busy = false;
            NetFigures.ReleaseRemote(1);
        }
        Console.WriteLine($"Figure hold production tests: {assertions} assertions passed.");
    }
}

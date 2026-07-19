using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Registers a <see cref="FigureGrabbable"/> against every live board figure and drives
/// the far laser point-and-grab (P8). Enumeration mirrors <c>WorldUI/ActorBars.Tick</c>:
/// it walks the game's own <c>WorldspaceUITools.Instance._panelUIControllers</c> registry,
/// takes each controller's tracked figure GameObject (<c>m_ObjectToTrack</c>), resolves the
/// figure's <c>CInteractableActor</c> collider + <c>ActorBehaviour</c>, and adopts/prunes
/// one grabbable per actor.
///
/// Near reach-and-close grabs are handled entirely by <see cref="ProximityGrabber"/>
/// (grip button, since the grabbable is <c>GrabWithGrip</c>). This driver only adds the
/// FAR path: on grip-down, if the hand's ray points at a figure and the grabber is idle,
/// <c>ForceGrab</c> plucks it — the same laser grab the card fan uses.
/// </summary>
internal sealed class FigureGrabDriver : MonoBehaviour
{
    private sealed class Adopted
    {
        public FigureGrabbable Grabbable = null!;
        public Collider Collider = null!;
    }

    // Keyed by the figure's interactable collider component (Unity-nullable key, like
    // ActorBars' controller map — a destroyed key stays a valid CLR dictionary key).
    private readonly Dictionary<CInteractableActor, Adopted> _adoptions = new();
    private readonly List<CInteractableActor> _scratch = new(32);

    private void OnDestroy() => ReleaseAll();

    private void Update()
    {
        if (!FigureGrabConfig.GrabFigures.Value)
        {
            if (_adoptions.Count > 0)
                ReleaseAll();
            return;
        }

        TickGuard.Run("FigureGrab.Registry", RefreshRegistry);
        TickGuard.Run("FigureGrab.LaserGrab", TickLaserGrab);

        // Keep the held figure's stat panel locked to that figure even if the laser sweeps
        // another figure on the board (risk #5 — the game's hover would otherwise re-target).
        if (HeldFigures.Count > 0)
            StatPanelSurface.ReassertHeld();
    }

    private void RefreshRegistry()
    {
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null || Choreographer.s_Choreographer == null)
            return;

        // Adopt new figures.
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController controller = controllers[i];
            if (controller == null)
                continue;
            GameObject figure = controller.m_ObjectToTrack;
            if (figure == null)
                continue;

            CInteractableActor interactable = figure.GetComponentInChildren<CInteractableActor>(includeInactive: true);
            if (interactable == null || _adoptions.ContainsKey(interactable))
                continue;

            Collider? collider = interactable.GetComponent<Collider>();
            if (collider == null)
                collider = interactable.GetComponentInChildren<Collider>();
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(figure);
            if (collider == null || actor == null)
                continue;

            var grabbable = new FigureGrabbable(actor);
            VRInteractables.RegisterGrabbable(grabbable, collider);
            _adoptions[interactable] = new Adopted { Grabbable = grabbable, Collider = collider };
        }

        // Prune figures whose collider/actor died (actor removed / scene unloading).
        if (_adoptions.Count == 0)
            return;
        _scratch.Clear();
        foreach (KeyValuePair<CInteractableActor, Adopted> pair in _adoptions)
        {
            if (pair.Key == null || pair.Value.Collider == null)
                _scratch.Add(pair.Key!);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Drop(_scratch[i]);
    }

    private void TickLaserGrab()
    {
        TryLaserGrab(VRHands.Left);
        TryLaserGrab(VRHands.Right);
    }

    private void TryLaserGrab(VRHand? hand)
    {
        if (hand == null || !hand.HasPose || !hand.GripDown)
            return;
        // Near grip-grab (a highlighted figure in reach) belongs to the ProximityGrabber;
        // the far pluck only fires when the grabber is idle this frame.
        if (hand.Grabber.Held != null || hand.Grabber.Highlighted != null)
            return;
        if (!hand.Ray.TryGetPick(out PickPose pick) || !pick.HasHit || pick.HitCollider == null)
            return;

        CInteractableActor interactable = pick.HitCollider.GetComponentInParent<CInteractableActor>();
        if (interactable == null || !_adoptions.TryGetValue(interactable, out Adopted adopted))
            return;

        hand.Grabber.ForceGrab(adopted.Grabbable, releaseOnTriggerUp: false);
    }

    private void Drop(CInteractableActor key)
    {
        if (_adoptions.TryGetValue(key, out Adopted adopted))
        {
            adopted.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(adopted.Grabbable);
        }
        _adoptions.Remove(key);
    }

    private void ReleaseAll()
    {
        foreach (KeyValuePair<CInteractableActor, Adopted> pair in _adoptions)
        {
            pair.Value.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(pair.Value.Grabbable);
        }
        _adoptions.Clear();
        HeldFigures.Clear();
    }
}

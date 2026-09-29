// Production PokeInteractor with explicit tracking and pointer-dispatch boundaries.
// The target is still the original Unity Image/Button, not a fixture replacement.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact
{
    internal interface IPokeable
    {
        void OnPokeEnter(Hands.VRHand hand);
        void OnPokeExit(Hands.VRHand hand);
        void OnPoke(Hands.VRHand hand);
    }
    internal interface IPokeCandidateFilter { bool AcceptsPokePoint(Vector3 point); }
    internal static class VRInteractables
    {
        internal readonly struct Entry
        {
            internal readonly Collider Collider;
            internal readonly IPokeable Target;
            internal Entry(Collider collider, IPokeable target) { Collider = collider; Target = target; }
        }
        internal static readonly List<Entry> Pokeables = new();
        internal static void Prune() { }
    }
    internal readonly struct PokeSurfaceTuning
    {
        internal float PressThrough => .05f;
        internal float HoverRange => .035f;
        internal float ReleaseDepth => .02f;
    }
    internal static class UguiPokeSurfaces
    {
        internal static readonly List<Canvas> Surfaces = new();
        internal static PokeSurfaceTuning TuningFor(Canvas canvas) => default;
        internal static void Prune() { }
    }
    internal sealed class UguiPointer
    {
        private GameObject? _target;
        private GameObject? _pressed;
        private readonly PointerEventData _event;
        internal GameObject? Hovered => _target;
        internal UguiPointer(string side) => _event = new PointerEventData(EventSystem.current);
        internal bool TryRaycast(Canvas canvas, Vector2 position, out RaycastResult result)
        {
            result = default;
            // The -nographics Unity runner has no GraphicRaycaster output. Keep only
            // that engine raycast boundary explicit; production PokeInteractor performs
            // the real fingertip plane, grip, depth and press-state transitions.
            foreach (Image graphic in canvas.GetComponentsInChildren<Image>(false))
            {
                if (!graphic.raycastTarget || !graphic.IsActive() || !graphic.Raycast(position, canvas.worldCamera))
                    continue;
                result.gameObject = graphic.gameObject;
                return true;
            }
            return false;
        }
        internal void SetHovered(GameObject? target) => _target = target;
        internal void Press(Vector2 position)
        {
            _pressed = _target;
            if (_pressed != null)
                ExecuteEvents.ExecuteHierarchy(_pressed, _event, ExecuteEvents.pointerDownHandler);
        }
        internal void Release(Vector2 position)
        {
            if (_pressed != null)
            {
                ExecuteEvents.ExecuteHierarchy(_pressed, _event, ExecuteEvents.pointerUpHandler);
                if (ReferenceEquals(_pressed, _target))
                    ExecuteEvents.ExecuteHierarchy(_pressed, _event, ExecuteEvents.pointerClickHandler);
            }
            _pressed = null;
        }
        internal void Cancel()
        {
            if (_pressed != null)
                ExecuteEvents.ExecuteHierarchy(_pressed, _event, ExecuteEvents.pointerUpHandler);
            _pressed = null;
            _target = null;
        }
    }
}

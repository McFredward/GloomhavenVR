using System;
using UnityEngine;
namespace GloomhavenVR.Hands.Interact;
internal class NativeGrabMethods
{
    private readonly VRHand _hand = new();
    private bool _releaseOnTriggerUp;
    private string _grabLabel = "";
    private float _highlightDropAt;
    private bool _highlightGrabbable;
    public IGrabbable? Held;
    public IGrabbable? Highlighted;
    public event Action<VRHand, IGrabbable?>? HighlightChanged;
    private void LogGrab(string message) { }
    internal void Take(IGrabbable target)=>BeginGrab(target,true,"trigger","fixture");
    internal void Hover(IGrabbable? target)=>SetHighlighted(target);
    internal bool Eligible(IGrabbable target)=>CanGrabNow(target);
    internal bool Allowed(IGrabbableHandFilter filter)=>AllowsHandNow(filter,_hand);
    internal static float Distance(in VRInteractables.GrabbableEntry e,Vector3 p)=>ReachDistance(in e,p);
    // ORIGINAL_METHODS
}

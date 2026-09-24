using UnityEngine;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;

// Only tracked input and the final adoption sinks are fixtures. Reach, modal/UI arbitration,
// hysteresis, hover feedback and the receiving trigger gesture compile from CardsDriver.
namespace GloomhavenVR.Hands.Interact { internal interface IGrabbable { } }
namespace GloomhavenVR.Hands
{
    internal enum HapticPreset { HoverTick }
    internal sealed class VRHand
    {
        internal bool HasPose=true, TriggerDown;
        internal float WorldScale=1f;
        internal string Side="fixture";
        internal readonly Grabber Grabber=new(); internal readonly Ray Ray=new();
        internal readonly UiRay RayUgui=new(); internal readonly HandRig Rig=new();
        internal int Haptics;
        internal void SendHaptic(HapticPreset _) => Haptics++;
    }
    internal sealed class Grabber { internal IGrabbable? Held; }
    internal sealed class Ray { internal bool Enabled=true; internal int Claims; internal void SuppressFarClick()=>Claims++; }
    internal sealed class UiRay { internal bool HasHit; }
    internal sealed class HandRig { internal Transform IndexTip=null!, PalmCenter=null!; }
    internal static class VRHands { internal static VRHand? Left,Right; }
}
namespace GloomhavenVR.Cards
{
    internal static class CardsConfig { internal sealed class Setting { internal float Value=.14f; } internal static readonly Setting CardWidth=new(); }
    internal class TransferProbe : MonoBehaviour, IGrabbable, IFanSweepTarget
    {
        internal float Width=.14f;
        internal int Adoptions;
        public bool SweepEligible=>true;
        public float SweepFaceWidthWorld=>Width;
        public string SweepName=>name;
        public bool TrySweepDistance(Vector3 point,out float distance) {distance=point.magnitude;return true;}
    }
    internal sealed class VRCard : TransferProbe { }
    internal sealed class MerchantProbe : TransferProbe,IItemCardHold
    {
        public bool IsItemCard=>true; public Transform HeldRoot=>transform;
        public bool TryTouch(Vector3 p,out float d)=>TrySweepDistance(p,out d);
        public bool Transfer(VRHand from,VRHand to) {Adoptions++;from.Grabber.Held=null;to.Grabber.Held=this;return true;}
    }
    internal sealed partial class ItemsPile
    {
        internal void TransferHeldChip(ItemChip chip,VRHand from,VRHand to)
        {chip.Adoptions++;from.Grabber.Held=null;to.Grabber.Held=chip;}
        internal sealed partial class ItemChip {internal ItemsPile Owner=new();}
    }
    internal sealed partial class CardsDriver
    {
        internal static GameObject? CardBackingPrefab;
        private VRHand? _transferHoverHand;
        private float _nextTransferLogAt;
        private bool _modalInputBlocked;
        internal void Step(bool modal=false) {_modalInputBlocked=modal;UpdateHeldCardTransfer();}
        private void TransferHeldCard(VRCard card,VRHand from,VRHand to)
        {card.Adoptions++;from.Grabber.Held=null;to.Grabber.Held=card;}
    }
}
public static partial class MirrorProgram
{
    private static void ItemTransferDetector()
    {
        foreach(float scale in new[]{.05f,1f,2f,198.12f})
        foreach(int kind in new[]{0,1,2})
        {
            var left=new VRHand {WorldScale=scale};var right=new VRHand {WorldScale=scale};
            left.Rig.IndexTip=Go("left tip").transform;left.Rig.PalmCenter=Go("left palm").transform;
            right.Rig.IndexTip=Go("right tip").transform;right.Rig.PalmCenter=Go("right palm").transform;
            VRHands.Left=left;VRHands.Right=right;
            GameObject go=Go("transfer source");
            TransferProbe probe=kind==0?go.AddComponent<VRCard>():kind==1?go.AddComponent<ItemsPile.ItemChip>():go.AddComponent<MerchantProbe>();
            probe.Width=.14f*scale;left.Grabber.Held=probe;
            var driver=new CardsDriver();
            right.Rig.IndexTip.position=Vector3.one*scale;right.Rig.PalmCenter.position=Vector3.one*scale;
            right.TriggerDown=true;driver.Step();
            Check(probe.Adoptions==0&&right.Haptics==0&&right.Ray.Claims==0,"far hands cannot transfer or claim a trigger");
            right.Rig.IndexTip.position=Vector3.right*.01f*scale;right.TriggerDown=false;driver.Step();
            Check(right.Haptics==1&&right.Ray.Claims==1,"either fingertip or palm contact admits transfer");
            driver.Step();Check(right.Haptics==1,"continued shared hover emits no repeated haptic");
            right.RayUgui.HasHit=true;right.TriggerDown=true;driver.Step();
            Check(probe.Adoptions==0,"nearer native UI retains the trigger");
            right.RayUgui.HasHit=false;driver.Step(modal:true);
            Check(probe.Adoptions==0,"modal decision keeps transfer input blocked");
            driver.Step();Check(probe.Adoptions==1&&ReferenceEquals(right.Grabber.Held,probe)&&left.Grabber.Held==null,
                "all three card kinds transfer through the same detector");
            // Reverse transfer can be palm-only, with the fingertip beyond contact range.
            left.Rig.IndexTip.position=Vector3.one*scale;left.Rig.PalmCenter.position=Vector3.right*.1f*scale;
            left.TriggerDown=true;right.TriggerDown=false;driver.Step();
            Check(probe.Adoptions==2&&ReferenceEquals(left.Grabber.Held,probe),"reverse hand transfer uses the existing palm contact reach");
        }
        VRHands.Left=VRHands.Right=null;
    }
}

using System;
using System.Reflection;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using UnityEngine;
using UnityEngine.UI;

public static partial class InteractionProgram
{
    private static void StockCardReturns626()
    {
        foreach(float scale in new[]{.62f,1f,2.6f})foreach(bool parked in new[]{false,true})
        {
            Transform shared=Probe.Go("Shared original map").transform;
            shared.SetPositionAndRotation(new Vector3(.6f,0f,-.8f),Quaternion.Euler(0f,31f,0f));
            shared.localScale=Vector3.one*scale;
            Transform cabinet=Probe.Go("Original stock slot",shared).transform;
            cabinet.localPosition=new Vector3(-.4f,1.1f,.2f);cabinet.localRotation=Quaternion.Euler(0f,-19f,0f);
            Transform physical=Probe.Go("Actual stock physical root",cabinet).transform;
            physical.localPosition=new Vector3(.01f,.07f,.03f);physical.localRotation=Quaternion.Euler(70f,0f,0f);
            var card=(RectTransform)Probe.Go("Original ItemCardUI",physical).transform;
            card.gameObject.AddComponent<ItemCardUI>();card.gameObject.AddComponent<Image>();
            card.localPosition=new Vector3(.017f,-.03f,.01f);card.localRotation=Quaternion.Euler(11f,-6f,13f);
            card.sizeDelta=new Vector2(180f,145f);card.localScale=Vector3.one*.001f;
            var button=Probe.Go("Original buy selection").AddComponent<Button>();
            object identity=new();int payments=0;
            var hand=new VRHand {Side=HandSide.Right,WorldScale=scale,TriggerUp=true};
            hand.Rig.Root.SetPositionAndRotation(new Vector3(.9f,1.2f,.2f)*scale,Quaternion.Euler(17f,63f,-31f));
            using var token=new TownServiceToken(card,button,()=>identity,()=>identity,()=>true,shared,physical,
                drop:()=>{payments++;return true;},eligible:()=>false,inspect:()=>true);
            token.Tick(scale);Check(hand.Grabber.ForceGrab(token,true),"actual cabinet card can be picked up without purchase");
            token.Tick(scale);hand.Grabber.ReleaseTick();
            if(parked)
            {
                Transform palm=Probe.Go("Actual merchant palm",shared).transform;
                palm.localPosition=new Vector3(.5f,1.3f,-.2f);palm.localRotation=Quaternion.Euler(-17f,27f,11f);
                token.ParkOffering(palm,()=>{});token.Tick(scale);token.ReturnOffering();
            }
            Check(!token.IsHeld&&token.IsMoving&&payments==0,
                "outside release and canceled merchant palm both keep the original card in a return flight without gameplay");
            var started=typeof(TownServiceToken).GetField("_returnStarted",BindingFlags.NonPublic|BindingFlags.Instance)!;
            started.SetValue(token,Time.unscaledTime-.07f);
            Check(token.TryCardReturnMotion(card,shared,null,out uint revision,out float[] data)&&revision!=0&&data.Length==38,
                "actual original stock flight exports exact root and original face provenance before it completes");
            Transform observer=Probe.Go("Original observer shared frame").transform;
            observer.SetPositionAndRotation(shared.position+Vector3.right*7f,shared.rotation);observer.localScale=shared.localScale;
            Transform copy=Probe.Go("Exact original card face",observer).transform;
            Vector3 previous=physical.position;
            foreach(float age in new[]{.07f,.13f,.20f,.28f,.35f})
            {
                started.SetValue(token,Time.unscaledTime-age);token.Tick(scale);
                TownCardReturnMotion.Apply(copy,observer,observer,0,data,age);
                Vector3 expected=observer.TransformPoint(shared.InverseTransformPoint(card.position));
                Check(Vector3.Distance(copy.position,expected)<.0002f*scale,
                    "real native release and cancellation match observer card face at each intermediate animation frame");
                Check(Quaternion.Angle(copy.rotation,card.rotation)<.02f,
                    "rotating native card retains exact face orientation instead of interpolating flattened endpoints");
                Check(Vector3.Distance(copy.lossyScale,card.lossyScale)<.0002f*scale,
                    "original face scale is identical through cabinet return and palm cancellation");
                if(age>.07f)Check(Vector3.Distance(previous,physical.position)>.00001f*scale,
                    "outside release has a visible local card flight rather than an instant snap");
                previous=physical.position;
            }
            Check(token.HasReturnMotion,"completed card return retains its endpoint briefly for late peers");
            started.SetValue(token,Time.unscaledTime-1f);token.Tick(scale);
            Check(!token.TryCardReturnMotion(card,shared,null,out _,out _),"expired card flight cannot override a new cabinet epoch");
            Check(hand.Grabber.ForceGrab(token,true),"settled stock original is immediately inspectable again");
            Check(!token.TryCardReturnMotion(card,shared,null,out _,out _),"new pickup invalidates old return provenance immediately");
            Clean();
        }
    }
    private static void CardOwnerReturns626()
    {
        foreach(float scale in new[]{.62f,1f,2.6f})
        {
            Transform shared=Probe.Go("Actual owner frame").transform;
            shared.SetPositionAndRotation(new Vector3(.3f,0f,-.4f),Quaternion.Euler(0f,31f,0f)); shared.localScale=Vector3.one*scale;
            Transform parent=Probe.Go("Nonunit native card parent",shared).transform; parent.localScale=Vector3.one*1.6f;
            Transform observer=Probe.Go("Same observer frame").transform;
            observer.SetPositionAndRotation(shared.position+Vector3.right*7f,shared.rotation);observer.localScale=shared.localScale;
            Transform copy=Probe.Go("Observer original face",observer).transform;
            var ability=Probe.Go("Actual native ability phase",parent).AddComponent<TownAbilityReturnOwner626>();
            ability._flyFromPos=shared.TransformPoint(new Vector3(.6f,.8f,.1f));ability._flyToPos=shared.TransformPoint(new Vector3(-.3f,.7f,-.1f));
            ability._flyFromScale=Vector3.one*.18f;ability._flyToScale=Vector3.one*.11f;
            ability._flyRot=Quaternion.Euler(17f,81f,-33f);ability._flyArcUp=Vector3.up;ability._flyArcHeight=.2f*scale;
            Transform face=Probe.Go("Original adopted ability face",ability.transform).transform;
            face.localPosition=new Vector3(.017f,-.021f,.006f);face.localRotation=Quaternion.Euler(8f,-13f,7f);face.localScale=Vector3.one*.002f;
            foreach(float age in new[]{.03f,.11f,.22f,.35f,.43f})
            {
                ability.Advance(age);
                Check(ability.TryTownReturnMotion(face,shared,null,out _,out float[] data),"actual VRCard phase exports its own returning ability tween");
                TownCardReturnMotion.Apply(copy,observer,observer,0,data,age);
                Check(Vector3.Distance(copy.position,observer.TransformPoint(shared.InverseTransformPoint(face.position)))<.0002f*scale,
                    "actual VRCard owner phase and remote return agree through native SmootherStep and arc");
                Check(Quaternion.Angle(copy.rotation,face.rotation)<.02f&&Vector3.Distance(copy.lossyScale,face.lossyScale)<.0002f*scale,
                    "ability return retains the adopted original orientation and nonunit parent scale");
            }
            ability.Holder=new VRHand();Check(!ability.TryTownReturnMotion(face,shared,null,out _,out _),"new actual ability pickup invalidates its old return clock");
            var chip=Probe.Go("Actual ItemChip collapse phase",parent).AddComponent<TownItemReturnOwner626>();
            chip._collapsing=true;chip._collapseFrom=shared.TransformPoint(new Vector3(.7f,.9f,.2f));
            chip._collapseWorld=shared.TransformPoint(new Vector3(-.5f,.5f,-.2f));chip._collapseFromRot=Quaternion.Euler(-19f,47f,33f);
            chip._collapseSpin=Quaternion.Euler(0f,0f,-70f);chip._collapseFromScale=.18f;
            Transform itemFace=Probe.Go("Actual chip original face",chip.transform).transform;
            itemFace.localRotation=Quaternion.Euler(7f,-9f,11f);itemFace.localScale=Vector3.one*.002f;
            foreach(float age in new[]{.02f,.07f,.13f,.19f,.235f})
            {
                chip.AdvanceCollapse(age);
                Check(chip.TryTownReturnMotion(itemFace,shared,null,out _,out float[] data),"actual ItemChip closed-fan phase exports its own collapse");
                TownCardReturnMotion.Apply(copy,observer,observer,0,data,age);
                Check(Vector3.Distance(copy.position,observer.TransformPoint(shared.InverseTransformPoint(itemFace.position)))<.0002f*scale,
                    "actual ItemChip collapse and observer agree during back-ease anticipation and arrival");
                Check(Quaternion.Angle(copy.rotation,itemFace.rotation)<.02f&&Vector3.Distance(copy.lossyScale,itemFace.lossyScale)<.0002f*scale,
                    "closed-fan return retains the original face scale under its actual nonunit parent");
            }
            chip._collapsing=false;chip._releaseGlide=.35f;chip._homePos=new Vector3(-.2f,.15f,.1f);chip._homeRot=Quaternion.Euler(-15f,39f,21f);
            chip.transform.localPosition=new Vector3(.4f,.5f,-.3f);chip.transform.localRotation=Quaternion.Euler(29f,78f,-17f);chip.transform.localScale=Vector3.one*.25f;
            var hand=new VRHand {Side=HandSide.Left,WorldScale=scale};hand.Rig.Root.SetPositionAndRotation(shared.position+new Vector3(.6f,.9f,.2f),Quaternion.Euler(-27f,123f,49f));
            Transform remoteHand=Probe.Go("Approved observer holder").transform; remoteHand.SetPositionAndRotation(hand.Rig.Root.position+Vector3.right*7f,hand.Rig.Root.rotation);remoteHand.localScale=Vector3.one*scale;
            Check(chip.TryTownReturnMotion(itemFace,shared,hand,out _,out float[] open),"actual open ItemChip return exports approved holder-relative exact endpoints");
            foreach(float dt in new[]{.03f,.06f,.09f,.08f})
            {
                chip.AdvanceOpen(dt);float age=.35f-chip._releaseGlide;
                TownCardReturnMotion.Apply(copy,remoteHand,observer,3,open,age);
                Check(Vector3.Distance(copy.position,itemFace.position+Vector3.right*7f)<.0002f*scale,
                    "actual owned ItemChip return follows its native exponential glide under a rotated hand and map");
                Check(Quaternion.Angle(copy.rotation,itemFace.rotation)<.02f,"open native item return keeps its original upright frame instead of inheriting pitched holder rotation");
            }
            chip.TownOffering=true;Check(!chip.TryTownReturnMotion(itemFace,shared,hand,out _,out _),"reoffering an owned card cannot replay a stale fan return");
            Clean();
        }
    }

}

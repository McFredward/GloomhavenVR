using System;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool pass,string message) { checks++; if(!pass)throw new InvalidOperationException(message); }
    private static GameObject Child(string name,Transform parent)
    { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go; }
    private static void Field(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.SetValue(owner,value);
    private static void Advance(VRHand hand,float now) { VRHand.Now=now;VRHand.Active=hand; }
    private static void Pulses(VRHand hand,int wanted,string reason)
    { Check(hand.Pulses.Count==wanted,reason+" (got "+hand.Pulses.Count+", expected "+wanted+")"); }
    public static int Run()
    {
        checks=0;
        var root=new GameObject("fixture-card",typeof(RectTransform));root.SetActive(false);
        var left=new VRHand();var right=new VRHand();
        try {
            // Real original native components and metadata, no substitute UI object model.
            var full=root.AddComponent<FullAbilityCard>();full.enabled=false;
            var top=Child("top",root.transform).AddComponent<FullAbilityCardAction>();top.enabled=false;
            var bottom=Child("bottom",root.transform).AddComponent<FullAbilityCardAction>();bottom.enabled=false;
            full.topActionButton=top;full.bottomActionButton=bottom;
            var standard=Child("default",top.transform).AddComponent<Button>();Field(top,"defaultActionButton",standard);
            var glyph=Child("glyph",top.transform);glyph.AddComponent<Image>();
            var other=Child("other-glyph",top.transform);other.AddComponent<Image>();
            var defaultGlyph=Child("default-text",standard.transform);defaultGlyph.AddComponent<Image>();
            var bottomGlyph=Child("bottom-text",bottom.transform);bottomGlyph.AddComponent<Image>();
            var layout=Child("generated-top-layout",root.transform);
            var pusher=layout.AddComponent<FullCardEventPusher>();Field(pusher,"_eventType",FullCardEventPusher.EventType.Top);
            var generated=Child("generated-icon",layout.transform);
            var defaultPusher=Child("default-pusher-outside-button",root.transform).AddComponent<FullCardEventPusher>();
            Field(defaultPusher,"_eventType",FullCardEventPusher.EventType.Top|FullCardEventPusher.EventType.Default);
            root.SetActive(true);
            Check(ReferenceEquals(HoverHapticTargets.Ui(glyph),top)&&ReferenceEquals(HoverHapticTargets.Ui(other),top),"native icon children resolve one original top action");
            Check(ReferenceEquals(HoverHapticTargets.Ui(generated),top),"generated layout pusher resolves original top half outside button subtree");
            Check(ReferenceEquals(HoverHapticTargets.Ui(defaultGlyph),standard)&&ReferenceEquals(HoverHapticTargets.Ui(defaultPusher.gameObject),standard),"original default pusher metadata retains standard action identity");
            for(int i=0;i<90;i++) { Advance(left,.001f+i/90f);DriverFrames.Ray(left,glyph);left.Hover(bottom); }
            Pulses(left,2,"simultaneous distinct action producers tick each region once without ping-pong");left.ClearPulses();
            Advance(right,1);DriverFrames.Ray(right,glyph);Pulses(right,1,"entering top action ticks once");
            for(int i=1;i<100;i++) { Advance(right,1+i/90f);DriverFrames.Ray(right,i%2==0?glyph:other); }
            Pulses(right,1,"tooltip child churn remains silent inside one action");
            Advance(right,2.11f);DriverFrames.Arm(right,glyph);Pulses(right,1,"arming same card action retains its single hover encounter");
            Advance(right,2.12f);DriverFrames.Ray(right,defaultGlyph);Pulses(right,2,"top to standard action ticks exactly once");
            right.RayUgui.Hovered=defaultGlyph;DriverFrames.Half(right,full,true);Pulses(right,2,"default plate refresh never enters surrounding half");
            for(int i=1;i<=20;i++) { Advance(right,2.12f+i/90f);DriverFrames.Ray(right,defaultGlyph);DriverFrames.Half(right,full,true); }
            Advance(right,2.4f);DriverFrames.Ray(right,glyph);DriverFrames.Half(right,full,true);Pulses(right,3,"standard to top ticks after genuine surrounding-half absence");
            Advance(right,2.41f);DriverFrames.Ray(right,bottomGlyph);DriverFrames.Half(right,full,false);Pulses(right,4,"top to bottom ticks once across pointer and geometric half driver");
            Advance(right,2.42f);DriverFrames.Ray(right,glyph);Pulses(right,4,"quick region return inside loss grace does not rattle at the boundary");
            Advance(right,2.6f);DriverFrames.Ray(right,glyph);Pulses(right,5,"region reentry after loss grace ticks again");
            var tooltip=new GameObject("passive-tooltip");try { Advance(right,2.7f);DriverFrames.Ray(right,tooltip);Pulses(right,5,"passive tooltip has no haptic target"); } finally { Object.DestroyImmediate(tooltip); }
            var button=Child("ordinary-button",root.transform.parent??root.transform).AddComponent<Button>();
            button.transform.SetParent(null);try {
                var label=Child("button-label",button.transform);Check(ReferenceEquals(HoverHapticTargets.Ui(label),button),"ordinary actionable UI resolves its parent Selectable");
                button.interactable=false;Check(HoverHapticTargets.Ui(label)==null,"disabled ordinary UI makes no new hover promise");
            } finally { Object.DestroyImmediate(button.gameObject); }
            var fan=new CardFan();var cardsRoot=new GameObject("fan");try {
                for(int i=0;i<4;i++) { var card=new GameObject("card-"+i).AddComponent<VRCard>();card.transform.SetParent(cardsRoot.transform);card.transform.position=new Vector3(i*.1f,0,0);card.gameObject.AddComponent<BoxCollider>().size=new Vector3(.05f,.1f,.001f);fan._cards.Add(card); }
                Physics.SyncTransforms();VRHands.Primary=left;
                // Actual fan method continuously refreshes the same physical card identity.
                for(int cardIndex=0;cardIndex<4;cardIndex++) {
                    var card=fan._cards[cardIndex];left.Rig.IndexTip.position=card.transform.position;
                    for(int frame=0;frame<30;frame++) {
                        Advance(left,4+cardIndex*.5f+frame/90f);fan.Frame();
                        if(frame==9)card.OnPokeEnter(left); // fingertip edge delayed beyond old50ms throttle
                        if(frame==25) { left.Grabber.Highlighted=card;DriverFrames.Grabber(left); }
                    }
                    Pulses(left,cardIndex+1,"one physical pulse per card despite staggered fingertip, poke and palm edges");
                }
                Advance(left,5.84f);left.Hover(fan._cards[3]);Pulses(left,4,"continuing same card remains one encounter");
                Advance(right,5.84f);right.Hover(fan._cards[3]);Pulses(right,6,"other hand gets independent encounter on same card");
                Advance(left,7);left.Hover(fan._cards[3]);Pulses(left,5,"genuine later reentry ticks again");
                left.ClearPulses();
                Advance(left,8);left.Legacy(HapticPreset.HoverTick);left.Hover(fan._cards[0]);Pulses(left,1,"legacy callback then semantic interactor shares same-frame hover receipt");
                Advance(left,8.01f);left.Hover(fan._cards[1]);left.Legacy(HapticPreset.HoverTick);Pulses(left,2,"semantic interactor then legacy callback shares receipt while rapid next card ticks");
                left.Legacy(HapticPreset.ClickPulse);left.Legacy(HapticPreset.GrabPulse);Pulses(left,4,"click and grab confirmation remain independent of hover suppression");
                Check(left.Pulses[2]==HapticPreset.ClickPulse&&left.Pulses[3]==HapticPreset.GrabPulse,"exact confirmation presets are preserved");
                left.ClearPulses();Advance(left,8.5f);left.Hover(fan._cards[2]);left.Hover(fan._cards[3]);
                Advance(left,8.51f);left.Hover(fan._cards[3]);Pulses(left,2,"two distinct semantic cards in one frame both receive their encounter pulse");
            } finally { Object.DestroyImmediate(cardsRoot); }
            checks+=DriverFrames.BoardCases(left);
        } finally { VRHands.Primary=null;left.Destroy();right.Destroy();Object.DestroyImmediate(root); }
        return checks;
    }
}

using System.Collections;
using Chronos;
using GloomhavenVR.Cards;
using UnityEngine;
static class Program {
    static int count;
    static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    static void Main(){
        var owner=new ItemsPile();var chip=new ItemsPile.ItemChip();var sibling=new ItemsPile.ItemChip();owner.Add(chip);owner.Add(sibling);owner.Begin(chip);
        Check(owner.Pending&&owner.Count==2&&owner.ClippedChipIndex==0&&chip.PendingUse&&chip.InputBlocked,"Used chip must remain registered in its original recess");
        var effect=chip._cardUI!.cardEffects;var native=effect.BurnCardTimeline(true);var tracked=ItemBurnPlayback.Track(effect,native);
        Check(!ItemBurnPlayback.Playing(effect),"Creating an iterator does not start native playback");
        Check(tracked.MoveNext()&&ReferenceEquals(tracked.Current,native.Current),"Native yielded object must be passed through unchanged");
        for(int i=0;i<100;i++){chip.Tick();Check(tracked.MoveNext()&&ItemBurnPlayback.Playing(effect)&&chip.Collapses==0&&owner.Pending&&owner.Count==2&&owner.ClippedChipIndex==0,"Paused native burn must retain card and slot beyond unscaled flourish");}
        Check(chip._cardUI.StateReads==100,"Original item state refresh must precede each completion check");
        Timekeeper.instance.m_GlobalClock.time=1;Timekeeper.instance.m_GlobalClock.deltaTime=1;
        Check(!tracked.MoveNext()&&!ItemBurnPlayback.Playing(effect),"Native completion must clear exact playback lifetime");chip.Tick();
        Check(GloomhavenVR.Net.ItemAppearanceSampler.Completions==1&&GloomhavenVR.Net.ItemAppearanceSampler.CapturedBeforeRetirement,"Native terminal capture must precede chip retirement and collapse");
        Check(chip.Collapses==1&&!owner.Pending&&owner.Count==1&&!chip.PendingUse&&owner.ClippedChipIndex==-1,"Only native completion may remove the source before collapse");chip.Tick();Check(chip.Collapses==1,"Flourish completion must be once-only");
        var missing=new ItemCardEffects{fgFx=null};var empty=ItemBurnPlayback.Track(missing,missing.BurnCardTimeline(true));Check(!empty.MoveNext()&&!ItemBurnPlayback.Playing(missing),"Native no-art exit must not create a permanent hold");
        var fault=new Probe{Throw=true};var wrap=ItemBurnPlayback.Track(effect,fault);try{wrap.MoveNext();throw new Exception("exception swallowed");}catch(InvalidOperationException e){Check(ReferenceEquals(e,fault.Error),"Native exception identity must survive");}Check(!ItemBurnPlayback.Playing(effect),"Native failure must release playback ownership");
        var first=new Probe();var second=new Probe();var a=ItemBurnPlayback.Track(effect,first);var b=ItemBurnPlayback.Track(effect,second);a.MoveNext();b.MoveNext();((IDisposable)a).Dispose();Check(ItemBurnPlayback.Playing(effect)&&first.Disposes==1,"Overlapping native burns must retain remaining ownership");((IDisposable)b).Dispose();((IDisposable)b).Dispose();Check(!ItemBurnPlayback.Playing(effect)&&second.Disposes==1,"Disposal must release exact ownership once");
        var done=new Probe{Finish=true};var completed=ItemBurnPlayback.Track(effect,done);Check(!completed.MoveNext(),"Native immediate completion is preserved");((IDisposable)completed).Dispose();Check(done.Disposes==1,"Dispose after normal completion must still reach native iterator");
        var ui=new ItemCardUI();var once=ui.cardEffects;Timekeeper.instance.m_GlobalClock.time=0;Timekeeper.instance.m_GlobalClock.deltaTime=0;
        once.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);var initial=once.Playing;
        for(int i=0;i<100;i++){once.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);once.ToggleAdditiveEffect(true,ItemCardEffects.FXTask.Consumed);once.RestoreCard();Check(once.Starts==1&&once.Resets==1&&ReferenceEquals(initial,once.Playing),"Forced item state refresh must keep one original consumed timeline");}
        Timekeeper.instance.m_GlobalClock.time=1;Timekeeper.instance.m_GlobalClock.deltaTime=1;Check(!once.Playing!.MoveNext(),"Original item timeline must complete normally");once.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);once.RestoreCard();Check(once.Starts==1&&once.Resets==1,"Completed consumed items must not restart from forced state refresh");
        ui.item.SlotState=ScenarioRuleLibrary.CItem.EItemSlotState.Ready;once.RestoreCard();Check(once.Resets==2,"Recovered item state must permit normal native reset");ui.item.SlotState=ScenarioRuleLibrary.CItem.EItemSlotState.Consumed;once.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);Check(once.Starts==2,"Recovered items may burn once again");
        var obsoleteItem=once.Playing;ui.OnReturnedToPool();Check(once.Resets==4&&!ItemBurnPlayback.Playing(once),"Item pool retirement must precede native material reset");Check(!obsoleteItem!.MoveNext(),"Retired item iterators must not write into recycled artwork");
        var replacement=new ItemCardUI();replacement.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);replacement.item=new();replacement.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);Check(replacement.cardEffects.Starts==2,"Changed item identity cannot inherit a previous consumed episode");
        var historical=new ItemCardUI();historical.cardEffects._greyOut=777;ItemBurnPlayback.ObserveInitialState(historical);historical.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);historical.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);
        Check(historical.cardEffects.NativeAnimated==0&&historical.cardEffects.NativeSettled==1&&historical.cardEffects.imgComp[0].material.GetFloat(777)==1,"An initially consumed item host must paint native settled artwork exactly once without replay");
        historical.item.SlotState=ScenarioRuleLibrary.CItem.EItemSlotState.Ready;historical.cardEffects.RestoreCard();historical.item.SlotState=ScenarioRuleLibrary.CItem.EItemSlotState.Consumed;historical.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);Check(historical.cardEffects.NativeAnimated==1,"A recovered historical host must animate its next actual use");
        var lateArt=new ItemCardUI();lateArt.cardEffects.fgFx=null;lateArt.cardEffects._greyOut=777;ItemBurnPlayback.ObserveInitialState(lateArt);lateArt.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);lateArt.cardEffects.fgFx=new();lateArt.cardEffects.ToggleEffect(true,ItemCardEffects.FXTask.Consumed);Check(lateArt.cardEffects.NativeSettled==2&&lateArt.cardEffects.imgComp[0].material.GetFloat(777)==1,"Missing historical item artwork must allow its first valid settle later");
        Console.WriteLine($"Item burn: {count} assertions passed.");
    }
    sealed class Probe:IEnumerator,IDisposable {internal bool Throw,Finish;internal int Disposes;internal readonly Exception Error=new InvalidOperationException();public object Current=>this;public bool MoveNext(){if(Throw)throw Error;return !Finish;}public void Reset(){}public void Dispose()=>Disposes++;}
}

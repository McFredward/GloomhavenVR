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
        Check(chip.Collapses==1&&!owner.Pending&&owner.Count==1&&!chip.PendingUse&&owner.ClippedChipIndex==-1,"Only native completion may remove the source before collapse");chip.Tick();Check(chip.Collapses==1,"Flourish completion must be once-only");
        var missing=new ItemCardEffects{fgFx=null};var empty=ItemBurnPlayback.Track(missing,missing.BurnCardTimeline(true));Check(!empty.MoveNext()&&!ItemBurnPlayback.Playing(missing),"Native no-art exit must not create a permanent hold");
        var fault=new Probe{Throw=true};var wrap=ItemBurnPlayback.Track(effect,fault);try{wrap.MoveNext();throw new Exception("exception swallowed");}catch(InvalidOperationException e){Check(ReferenceEquals(e,fault.Error),"Native exception identity must survive");}Check(!ItemBurnPlayback.Playing(effect),"Native failure must release playback ownership");
        var first=new Probe();var second=new Probe();var a=ItemBurnPlayback.Track(effect,first);var b=ItemBurnPlayback.Track(effect,second);a.MoveNext();b.MoveNext();((IDisposable)a).Dispose();Check(ItemBurnPlayback.Playing(effect)&&first.Disposes==1,"Overlapping native burns must retain remaining ownership");((IDisposable)b).Dispose();((IDisposable)b).Dispose();Check(!ItemBurnPlayback.Playing(effect)&&second.Disposes==1,"Disposal must release exact ownership once");
        var done=new Probe{Finish=true};var completed=ItemBurnPlayback.Track(effect,done);Check(!completed.MoveNext(),"Native immediate completion is preserved");((IDisposable)completed).Dispose();Check(done.Disposes==1,"Dispose after normal completion must still reach native iterator");
        Console.WriteLine($"Item burn: {count} assertions passed.");
    }
    sealed class Probe:IEnumerator,IDisposable {internal bool Throw,Finish;internal int Disposes;internal readonly Exception Error=new InvalidOperationException();public object Current=>this;public bool MoveNext(){if(Throw)throw Error;return !Finish;}public void Reset(){}public void Dispose()=>Disposes++;}
}

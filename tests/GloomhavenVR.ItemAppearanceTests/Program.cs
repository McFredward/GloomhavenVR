using GloomhavenVR.Net;
using GloomhavenVR.Cards;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

static class Program
{
    static int assertions;
    static void Check(bool value,string message){assertions++;if(!value)throw new InvalidOperationException(message);}
    static (ItemCardEffects fx,Image face,Image overlay,CanvasGroup group) Art(string rootName)
    {
        var root=new Transform(rootName);
        var face=new Image(new Transform("Face",root)){enabled=true,color=new(1,.6f,.3f,1)};
        var overlay=new Image(new Transform("Flame",root)){enabled=true,color=new(1,.5f,.2f,1)};
        var fallback=new TMPro.TMP_SubMeshUI(new Transform("Unpopulated fallback font",root));
        var group=new CanvasGroup(new Transform("Original fade",root)){enabled=true,alpha=.4f};
        face.material.shader.Properties.UnionWith(CardAppearanceBindings.FloatIds.Take(7));
        overlay.material.shader.Properties.UnionWith(CardAppearanceBindings.FloatIds.Skip(7));
        overlay.material.shader.Properties.Add(CardAppearanceBindings.Particle);
        face.material.SetFloat(CardAppearanceBindings.FloatIds[0],.7f);
        face.material.SetFloat(CardAppearanceBindings.FloatIds[1],.4f);
        overlay.material.SetFloat(CardAppearanceBindings.FloatIds[7],.8f);
        var fx=new ItemCardEffects(root,overlay);
        overlay.material.SetTexture(CardAppearanceBindings.Particle,fx.overlayFrameBurn);
        return(fx,face,overlay,group);
    }
    static ItemAppearanceSnapshot Frame(float time,ItemAppearanceState state)=>new(time,new[]{state});
    static ItemAppearanceState With(ItemAppearanceState state,byte flags){var copy=state.Copy();copy.Flags=flags;return copy;}
    static void Main()
    {
        var item=new CItem{SlotState=CItem.EItemSlotState.Consumed};
        var actor=new CPlayerActor{Id=17};actor.Inventory!.AllItems.Add(item);RemoteBoardFocus.Actors[17]=actor;
        var owner=new ItemsPile{OwnerActor=actor}; var native=Art("native");
        var chip=new ItemsPile.ItemChip{Owner=owner,Item=item,NativeItemCard=new(native.fx),BurnPresentationPending=true,PendingUse=true};
        ItemsPile.ItemChip.Registered.Add(chip);
        var pending=ItemAppearanceSampler.Sample();Check(pending.Length==1&&pending[0].Flags==5,"Only the actual pending use card arms a clipped burn");
        Check(ReferenceEquals(pending,ItemAppearanceSampler.Sample()),"Unchanged native sampling must reuse the published frame");
        var snapshot=Frame(10,pending[0]);
        var bytes=new byte[ItemAppearanceCodec.MaxSize];int size=ItemAppearanceCodec.Write(snapshot,bytes);
        Check(ItemAppearanceCodec.TryRead(bytes,size,out var decoded)&&ItemAppearanceState.Same(snapshot.States[0],decoded!.States[0]),"Original item fields must survive bounded wire roundtrip");
        for(int end=0;end<size;end++)Check(!ItemAppearanceCodec.TryRead(bytes,end,out _),"Truncated native item frames must be rejected atomically");
        var invalid=(byte[])bytes.Clone(); invalid[24]=3;Check(!ItemAppearanceCodec.TryRead(invalid,size,out _),"Invalid item population must be rejected");
        var immutable=Frame(10,pending[0]);float frozen=immutable.States[0].Nodes[0].Value.Values[8];pending[0].Nodes[0].Value.Values[8]=.2f;Check(immutable.States[0].Nodes[0].Value.Values[8]==frozen,"Queued native frames must own immutable node values");
        pending[0].Nodes[0].Value.Values[8]=frozen;
        Time.unscaledTime=0;ItemAppearanceMirror.Set(3,snapshot);
        Check(ItemAppearanceMirror.HoldsClip(3,17,0,1)&&ItemAppearanceMirror.HoldsAnyClip(3,actor),"Valid clipped burn must hold before its first native paint");
        var clone=Art("unrelated clone root name");var bind=new ItemAppearanceBindings(clone.fx);var originalMaterial=clone.face.material;
        Check(bind.Apply(snapshot.States[0],snapshot.States[0],1,new(1,2,3,4)),"Original path-bound item hierarchy must accept native output");
        Check(clone.face.material.GetFloat(CardAppearanceBindings.FloatIds[0])==.7f&&clone.face.material.GetFloat(CardAppearanceBindings.FloatIds[1])==.4f,"Original native grey and burn values must be copied without settled endpoints");
        Check(!ReferenceEquals(clone.face.material,originalMaterial)&&clone.group.alpha==.4f,"Playback must own material copies and original group alpha");
        clone.face.material.SetFloat(CardAppearanceBindings.FloatIds[0],.9f);bind.Apply(snapshot.States[0],snapshot.States[0],1,new());
        Check(clone.face.material.GetFloat(CardAppearanceBindings.FloatIds[0])==.7f,"Repeated samples must repair actual target interference");
        var active=With(snapshot.States[0],1);ItemAppearanceMirror.Set(4,Frame(10,active));Check(!ItemAppearanceMirror.HoldsAnyClip(4,actor),"Unclipped fan effects cannot create an empty recess hold");
        ItemAppearanceSampler.RetainCompletion(chip);native.face.material.SetFloat(CardAppearanceBindings.FloatIds[1],0);native.group.alpha=0;
        chip.BurnPresentationPending=false;chip.PendingUse=false;
        var terminal=ItemAppearanceSampler.Sample();Check(terminal[0].Flags==6&&terminal[0].Nodes[0].Value.Values[9]==.4f,"Terminal output must be retained before collapse mutates original graphics");
        ItemAppearanceMirror.Set(3,Frame(10.1f,terminal[0]));
        Check(ItemAppearanceMirror.HoldsClip(3,17,0,1),"Terminal arrival alone cannot release an unpainted original burn");
        ItemAppearanceMirror.MarkPresented(3,terminal[0],.5f);Check(ItemAppearanceMirror.HoldsClip(3,17,0,1),"Partial interpolation before terminal source time must retain clip");
        ItemAppearanceMirror.Set(3,Frame(10.2f,terminal[0]));ItemAppearanceMirror.MarkPresented(3,terminal[0],.5f);
        Check(!ItemAppearanceMirror.HoldsClip(3,17,0,1),"Later heartbeat interpolation through terminal time must release the clip");
        ItemAppearanceMirror.Set(5,Frame(10,snapshot.States[0]));ItemAppearanceMirror.RejectPresentation(5,snapshot.States[0]);
        Check(!ItemAppearanceMirror.HoldsAnyClip(5,actor),"A proven native binding failure must not hold an invisible recess forever");
        ItemAppearanceMirror.MarkPresented(5,snapshot.States[0],1);Check(ItemAppearanceMirror.HoldsAnyClip(5,actor),"A repaired native binding must resume normal burn retention");
        actor.Inventory.AllItems.Clear();ItemAppearanceMirror.Set(6,Frame(10,snapshot.States[0]));Check(!ItemAppearanceMirror.HoldsAnyClip(6,actor),"An unresolved opening cannot hold an unrelated item");
        actor.Inventory.AllItems.Add(item);ItemAppearanceMirror.Set(6,Frame(10.1f,snapshot.States[0]));Check(ItemAppearanceMirror.HoldsAnyClip(6,actor),"Model arrival must retry unresolved item binding");
        actor.Inventory.AllItems.Clear();ItemAppearanceMirror.Set(6,Frame(10.2f,terminal[0]));Check(ReferenceEquals(ItemAppearanceMirror.ItemAt(6,17,0,1),item),"Already-bound terminal output must retain its original item after model removal");
        ItemAppearanceMirror.Set(6,new(10.3f,Array.Empty<ItemAppearanceState>()));Check(!ItemAppearanceMirror.HoldsAnyClip(6,actor),"Explicit generation retirement must cancel pending layout holds");
        var forfeit=With(snapshot.States[0],5);forfeit.Population=1;CardsGameApi.Rewards=new(){item};ItemAppearanceMirror.Set(7,Frame(10,forfeit));CardsGameApi.Rewards.Clear();ItemAppearanceMirror.Set(7,Frame(10.1f,With(forfeit,6)));Check(ReferenceEquals(ItemAppearanceMirror.ItemAt(7,17,0,1),item),"Forfeit burn must keep its original reward after removal");
        ItemsPile.ItemChip.Registered.Clear();item.SlotState=CItem.EItemSlotState.Ready;
        Check(ItemAppearanceSampler.Sample().Length==0,"Recovery with a closed fan must retire the retained terminal generation");
        actor.Inventory.AllItems.Add(item);ItemsPile.ItemChip.Registered.Add(chip);var recovered=ItemAppearanceSampler.Sample();Check(recovered.Length==1&&recovered[0].Flags==0&&recovered[0].Generation!=terminal[0].Generation,"Recovered item must publish a fresh generation without a burn overlay flag");
        ItemAppearanceSampler.RetainCompletion(chip);chip.NativeItemCard=new(Art("rebuilt original widget").fx);var rebuilt=ItemAppearanceSampler.Sample();
        Check(rebuilt.Length==1&&rebuilt[0].Generation!=recovered[0].Generation&&rebuilt[0].Flags==0,"Rebuilt original item widgets must not inherit a prior source terminal frame");
        ItemAppearanceSampler.RetainCompletion(chip);ItemsPile.ItemChip.Registered.Clear();RemoteBoardFocus.Actors.Clear();Check(ItemAppearanceSampler.Sample().Length==0,"Actor retirement must remove retained native sources");
        var largest=new ItemAppearanceState[ItemAppearanceSnapshot.MaxCards];
        for(int c=0;c<largest.Length;c++)
        {
            largest[c]=snapshot.States[0].Copy();largest[c].Generation=(uint)c+1;largest[c].Count=32;largest[c].Seat=(byte)c;
            largest[c].Nodes=Enumerable.Range(0,12).Select(i=>new ItemAppearanceNode{Binding=(uint)i+1,Value=new CardAppearanceNode{Role=0,Flags=3}}).ToArray();
        }
        var maximum=new ItemAppearanceSnapshot(20,largest);int worst=ItemAppearanceCodec.Write(maximum,bytes);
        Check(worst==56654&&ItemAppearanceCodec.TryRead(bytes,worst,out var maxRead)&&maxRead!.States.Length==32,"Maximum native item population must fit the advertised atomic payload budget");
        bind.Destroy();Check(UnityEngine.Object.Destroyed>=2,"Native playback teardown must dispose owned materials");
        Console.WriteLine($"Item appearance: {assertions} assertions passed.");
    }
}

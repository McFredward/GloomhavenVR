using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
internal static class Program
{
    private static int _assertions;
    private static readonly Rect Narrow = new(-982,-540,328,1080);
    private static readonly Rect Huge = new(-994,-540,1988,1080);
    private static void Check(bool value,string message) { _assertions++; if(!value) throw new Exception(message); }
    private static void Near(float value,float expected,string message) => Check(Math.Abs(value-expected)<0.02f,message);
    private static void Equal(Rect a,Rect b,string message) { Near(a.x,b.x,message);Near(a.y,b.y,message);Near(a.width,b.width,message);Near(a.height,b.height,message); }
    private static Rect Tick(MrBackingLayout state,Rect r,int sample,float time,float duration=0.15f)
    { state.Present(r,true,sample,time,duration,out Rect shown);return shown; }
    private static MrBackingLayout Seed(Rect r)
    { var state=new MrBackingLayout(); Tick(state,r,1,0);Tick(state,r,2,0.05f);Tick(state,r,2,0.3f);return state; }
    private static void Main()
    {
        Check(!MrBackingLayout.ReadyForSample(false,Huge),"unfitted clone cannot invent MR geometry");
        Check(!MrBackingLayout.ReadyForSample(true,default),"empty fit is not a native window");
        Check(MrBackingLayout.ReadyForSample(true,Narrow),"fitted clone admits owner geometry");
        Check(!MrBackingLayout.ReadyForSample(false,Narrow),"rebuild retires previous fitted geometry");
        Rect fitted=MrBackingLayout.WindowRect(Huge,Narrow,false,float.PositiveInfinity);
        Near(fitted.width,344,"transparent host must not inflate the narrow character window");
        Near(fitted.center.x,Narrow.center.x,"backing remains centered on the column");
        Near(fitted.height,1096,"small margin around real window");
        Rect shop=MrBackingLayout.WindowRect(new(-960,-540,1920,1080),new(460,-300,517,800),true,-911);
        Near(shop.xMin,-968,"painted merchant keeps left artwork");Near(shop.xMax,985,"ink outside artwork retained");
        Near(shop.yMin,-919,"ultrawide artwork below frame retained");Near(shop.yMax,548,"painted frame top retained");
        Rect overflow=MrBackingLayout.WindowRect(Huge,new(-1100,-650,2400,1300),false,-900);
        Near(overflow.width,2416,"real out-of-frame ink is not cropped");
        Near(overflow.yMin,-658,"unpainted frame does not borrow an unrelated plate floor");

        // Hardware regression: a row-sized portrait must not back the parent's 1080 px screen
        // canvas, even when a native portrait background is classified as painted artwork.
        Rect row = new(-90, -95, 180, 190);
        Rect scoped = MrBackingLayout.WindowRect(Huge, row, true, -1080, fitScoped:true);
        Near(scoped.width,196,"scoped initiative backing must fit original portrait width");
        Near(scoped.height,206,"scoped initiative backing must not inherit tall parent artwork");
        Near(scoped.center.y,row.center.y,"scoped backing stays centred on the original row");
        var source = new Transform();
        var mask = new Transform { parent=source };
        var rowHolder = new Transform { parent=mask };
        var portrait = new Transform { parent=rowHolder };
        var sibling = new Transform { parent=source };
        var siblingText = new Transform { parent=sibling };
        Check(MrBackingScope.Valid(source,rowHolder),"original rowHolder is a valid scope");
        Check(!MrBackingScope.Valid(source,new Transform()),"unrelated rowHolder cannot measure this panel");
        Check(MrBackingScope.Visit(source,rowHolder)&&MrBackingScope.Visit(mask,rowHolder),
            "scope ancestors must still be visited for native clipping");
        Check(!MrBackingScope.Paint(source,rowHolder)&&!MrBackingScope.Paint(mask,rowHolder),
            "parent layout graphics must not paint the row backing");
        Check(MrBackingScope.Visit(rowHolder,rowHolder)&&MrBackingScope.Paint(rowHolder,rowHolder),
            "the original rowHolder's own real artwork remains content");
        Check(MrBackingScope.Visit(portrait,rowHolder)&&MrBackingScope.Paint(portrait,rowHolder),
            "native portrait graphics remain measurable");
        Check(!MrBackingScope.Visit(sibling,rowHolder)&&!MrBackingScope.Paint(siblingText,rowHolder),
            "fullscreen sibling graphics and text cannot inflate the backing");
        Check(MrBackingScope.Valid(source,null)&&MrBackingScope.Visit(sibling,null)
            &&MrBackingScope.Paint(siblingText,null),"ordinary windows retain their complete artwork");
        var clone = new Transform();
        var cloneMask = new Transform { parent=clone };
        var cloneHolder = new Transform { parent=cloneMask };
        var clonePortrait = new Transform { parent=cloneHolder };
        var cloneSibling = new Transform { parent=clone };
        Check(MrBackingScope.Valid(clone,cloneHolder)&&MrBackingScope.Visit(cloneMask,cloneHolder)
            &&MrBackingScope.Paint(clonePortrait,cloneHolder)&&!MrBackingScope.Paint(cloneSibling,cloneHolder),
            "remote inert hierarchy uses the same original rowHolder boundary");
        var shrinkScope = Seed(Huge);
        Tick(shrinkScope,scoped,50,1);Tick(shrinkScope,scoped,51,1.05f);
        Rect shrinkingScope=Tick(shrinkScope,scoped,51,1.1f);
        Check(shrinkingScope.height>scoped.height&&shrinkingScope.height<Huge.height,
            "corrected backing shrinks visibly instead of popping");
        Equal(Tick(shrinkScope,scoped,51,1.3f),scoped,"corrected backing settles on the original row");

        var first=new MrBackingLayout();
        Check(!first.Present(Huge,true,1,0,0.15f,out _),"first sample must not reveal a giant initial frame");
        Check(!first.Present(Huge,true,1,0.01f,0.15f,out _),"same sample is not independent confirmation");
        Check(!first.Present(Narrow,true,2,0.04f,0.15f,out _),"one oversized sample must not seed presentation");
        Check(first.Present(Narrow,true,3,0.08f,0.15f,out Rect start),"two real samples admit current content");
        Near(start.width,0,"first plate grows rather than popping in");
        Near(start.center.x,Narrow.center.x,"first growth starts at the visible column");
        Rect middle=Tick(first,Narrow,3,0.13f);
        Check(middle.width>0&&middle.width<Narrow.width,"opening visibly interpolates");
        Equal(Tick(first,Narrow,3,0.3f),Narrow,"opening reaches precise measured bounds");

        var change=Seed(Narrow);
        Rect grow=new(-982,-540,650,1080);
        Equal(Tick(change,grow,4,1),Narrow,"unconfirmed growth stays put");
        Equal(Tick(change,grow,5,1.05f),Narrow,"retarget starts from the displayed shape");
        Rect growing=Tick(change,grow,5,1.1f);
        Check(growing.width>Narrow.width&&growing.width<grow.width,"growth is animated");
        Equal(Tick(change,grow,5,1.3f),grow,"growth completes");
        Tick(change,Narrow,6,2);Tick(change,Narrow,7,2.05f);
        Rect shrinking=Tick(change,Narrow,7,2.1f);
        Check(shrinking.width>Narrow.width&&shrinking.width<grow.width,"shrink is animated");
        Equal(Tick(change,Narrow,7,2.3f),Narrow,"shrink completes");
        Tick(change,Huge,8,3);
        Equal(Tick(change,Narrow,9,3.01f),Narrow,"single reveal spike never changes the plate");
        Equal(Tick(change,Narrow,9,3.3f),Narrow,"spike cannot return later");
        Check(!change.Present(Narrow,false,10,4,0.15f,out _),"invisible content cannot leave an empty plate");
        Check(!change.Present(Huge,true,11,4.01f,0.15f,out _),"reopening does not inherit old confirmation");
        Tick(change,Narrow,12,4.02f);Tick(change,Narrow,13,4.03f);
        Near(Tick(change,Narrow,13,4.03f).width,0,"reopened window grows from its new content");

        var moving=Seed(Narrow);
        for(int i=0;i<8;i++) Tick(moving,new(-982,-540,400+i*25,1080),10+i,1+i*0.04f);
        Check(Tick(moving,new(-982,-540,575,1080),17,1.5f).width>Narrow.width,"continuously changing native layout cannot starve");
        var instant=new MrBackingLayout();Tick(instant,Narrow,1,0,0);
        Equal(Tick(instant,Narrow,2,0.01f,0),Narrow,"zero configured duration retains instantaneous mode");
        foreach(Rect bad in new[]{new Rect(0,0,0,1),new Rect(float.NaN,0,1,1),new Rect(0,0,float.PositiveInfinity,1),new Rect(0,float.NegativeInfinity,1,1)})
            Check(!instant.Present(bad,true,3,1,0.15f,out _),"invalid geometry never produces an opaque artifact");

        var local=Seed(Narrow);var remote=Seed(Narrow);
        for(int i=0;i<40;i++)
        {
            Rect sample=i<15?grow:Narrow;
            Equal(Tick(local,sample,30+i,i*0.02f+1),Tick(remote,sample,30+i,i*0.02f+1),"same owner samples yield identical remote animation");
        }
        var steady=Seed(Narrow);Tick(steady,Narrow,40,1);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++) steady.Present(Narrow,true,40,1+i*0.01f,0.15f,out _);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"steady presentation allocates nothing");

        var panel=new ConvertedPanel();GrabbableModal.LiveHolders.Clear();
        Check(!GrabbableModal.TryGetMrBackingRect(panel,out _,out _,out _),"unowned surface is distinguishable from empty modal");
        var holder=new GrabbableModal{_panel=panel,_mrInkRect=Narrow,_mrInkSampleFrame=42,_mrInkValid=true};
        GrabbableModal.LiveHolders.Add(holder);
        Check(GrabbableModal.TryGetMrBackingRect(panel,out Rect raw,out bool visible,out int revision)&&visible&&revision==42,"visible owner reuses its original sample generation");
        Equal(raw,Narrow,"modal accessor returns raw measured content");
        holder._mrInkValid=false;
        Check(GrabbableModal.TryGetMrBackingRect(panel,out _,out visible,out _)&&!visible,"known empty modal may not fall back to its full host");
        holder._mrInkValid=true;panel.Owed=true;
        Check(GrabbableModal.TryGetMrBackingRect(panel,out _,out visible,out _)&&!visible,"never-drawn map window stays withheld");
        panel.Owed=false;holder._barHiddenForEmpty=true;
        Check(GrabbableModal.TryGetMrBackingRect(panel,out _,out visible,out _)&&!visible,"empty owner chrome cannot leave an MR plate");
        Check(!GrabbableModal.TryGetMrBackingRect(new ConvertedPanel(),out _,out _,out _),"unrelated window cannot borrow owner bounds");
        Console.WriteLine($"MR backing production tests: {_assertions} assertions passed.");
    }
}

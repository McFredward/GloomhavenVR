int assertions=0;
void Check(bool value,string message) { assertions++; if(!value) throw new InvalidOperationException(message); }
var a=new CPlayerActor(1); var b=new CPlayerActor(2); var first=new CAbilityCard(11); var next=new CAbilityCard(12);
foreach (bool front in new[]{false,true})
foreach (int seat in new[]{0,1})
{
    var cards=new[]{new CardFixture(),new CardFixture()};
    cards[0].Set(first,front,a); cards[1].Set(next,front,a);
    var source=cards[seat]; var identity=seat==0 ? first : next;
    source.TransferToFlight(identity.CardInstanceID,a);
    Check(!source.Visible && !source.Vanishing,"Flight must immediately replace its source without dock crumble");
    Check(cards[1-seat].Visible,"Unrelated recess must retain its card");
    for(int frame=0;frame<180;frame++)
    {
        source.Set(identity,front,a);
        Check(!source.Visible,"Stale seating must not repaint a departed source, even after the arc duration");
    }
    source.Set(null,front,a);
    Check(!source.Vanishing,"Empty acknowledgement must not create a second departure animation");
    source.Set(identity,front,a);
    Check(source.Visible,"Same card must be reusable after empty acknowledgement");
    source.TransferToFlight(identity.CardInstanceID,a,2);
    source.Set(seat==0 ? next : first,front,a);
    Check(source.Visible,"A new card in the same occupied seat must clear the departure fence");
    source.TransferToFlight((seat==0 ? next : first).CardInstanceID,a,3);
    source.Set(seat==0 ? next : first,front,b);
    Check(source.Visible,"Different actor must not inherit the prior source fence");
}
var unknown=new CardFixture(); unknown.SetAnonymousBack(); unknown.TransferToFlight(int.MinValue,a);
Check(!unknown.Visible,"Covered unknown source must transfer without exposing an identity");
unknown.SetAnonymousBack(); Check(!unknown.Visible,"Stale anonymous snapshot must remain fenced");
unknown.Set(null,false,a); unknown.SetAnonymousBack(); Check(unknown.Visible,"Empty acknowledgement must rearm anonymous seating");
unknown.TransferToFlight(int.MinValue,a,2); unknown.ClearFlightTransfer(); unknown.SetAnonymousBack();
Check(unknown.Visible,"Focus change must clear anonymous departure fence");
var replacement=new CardFixture(); replacement.Set(next,true,a); replacement.TransferToFlight(first.CardInstanceID,a);
Check(replacement.Visible,"Delayed old-card flight must not hide a replacement card");
var fading=new CardFixture(); fading.Set(first,true,a); fading.Set(null,true,a);
Check(fading.Visible && fading.Vanishing,"Non-flight departure must retain native dock crumble");
fading.TransferToFlight(first.CardInstanceID,a);
Check(!fading.Visible && !fading.Vanishing,"Late flight must cancel a source crumble already in progress");
var fx=new FxFixture(); RemoteBoardFocus.Current=a;
var outgoing=new Flight { SourceActor=a, FaceCard=first, Endpoints=(byte)((int)CardFxAnchor.Slot0 | ((int)CardFxAnchor.Discard << 4)) };
fx._owner.Board._cards[0].Set(first,true,a); fx.Apply(outgoing);
Check(!fx._owner.Board._cards[0].Visible,"Production flight orchestration must transfer the source");
RemoteBoardFocus.Current=b; fx._owner.Board._cards[0].ClearFlightTransfer(); fx._owner.Board._cards[0].Set(next,true,b); fx.Apply(outgoing);
Check(fx._owner.Board._cards[0].Visible,"Running A flight must not hide character B");
RemoteBoardFocus.Current=a; fx._owner.Board._cards[0].ClearFlightTransfer(); fx._owner.Board._cards[0].Set(first,true,a); fx.Apply(outgoing);
Check(!fx._owner.Board._cards[0].Visible,"Focus away and back must reassert the retained source identity");
var incoming=new Flight { SourceActor=a,FaceCard=first,Generation=2, Endpoints=(byte)((int)CardFxAnchor.Discard | ((int)CardFxAnchor.Slot0 << 4)) };
fx.Apply(incoming); fx.Apply(outgoing); fx._owner.Board.Landing=first; fx._owner.Board.CompleteFlightLanding(CardFxAnchor.Slot0,a,2);
Check(fx._owner.Board._cards[0].Visible,"Incoming same-card flight must rearm without an empty snapshot");
fx.Apply(outgoing);
Check(fx._owner.Board._cards[0].Visible,"Older outgoing generation must not reclaim a landed newer card");
fx._owner.Board._cards[0].Blank(); RemoteBoardFocus.Current=b;
fx._owner.Board.CompleteFlightLanding(CardFxAnchor.Slot0,a,2);
Check(!fx._owner.Board._cards[0].Visible,"Late landing must not recreate source actor on another character");
RemoteBoardFocus.Current=a; RemoteBoardFocus.Exhausted=true;
fx._owner.Board.CompleteFlightLanding(CardFxAnchor.Slot0,a,2);
Check(!fx._owner.Board._cards[0].Visible,"Late landing must not recreate exhausted actor cards");
RemoteBoardFocus.Exhausted=false;
var acknowledged=new CardFixture(); acknowledged.Set(first,true,a);
acknowledged.TransferToFlight(first.CardInstanceID,a,10); acknowledged.Set(null,true,a);
acknowledged.TransferToFlight(first.CardInstanceID,a,10); acknowledged.Set(first,true,a);
Check(acknowledged.Visible,"Old flight must not recreate its fence after native empty acknowledgement");
acknowledged.TransferToFlight(first.CardInstanceID,a,11); acknowledged.Set(next,true,a);
acknowledged.TransferToFlight(first.CardInstanceID,a,11); acknowledged.Set(null,true,a);
acknowledged.TransferToFlight(first.CardInstanceID,a,11); acknowledged.Set(first,true,a);
Check(acknowledged.Visible,"Old flight must not undo a replacement acknowledgement after that replacement also leaves");
Check(CompactFixture.Advance(true,2,0,0)==0,"Incoming empty Slot0 must not consume existing Slot1 model card");
Check(CompactFixture.Advance(true,3,0,0)==1,"Suppressed occupied Slot0 must consume its own compact model seat");
Console.WriteLine($"Flight timing production policies: {assertions} assertions.");

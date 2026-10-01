using System;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string description) { _assertions++;if(!value)throw new Exception(description); }
    private static void Near(float value,float wanted,string description)=>Check(Math.Abs(value-wanted)<.001f,description+$" {value} != {wanted}");
    private static void Reset()
    {
        TownServicePopulation.Reset();RemoteTownResidents.Reset();TownServiceMirror.RemoteSessions.Clear();
        TownServiceMerchantHandoff.WantsOffering=false;TownServiceMirror.RemoteMerchantOffering=false;TownServiceStation.NearVisitor=false;
        TownServiceMirror.TempleReceived=false;TownServiceMirror.TempleKnown=false;TownServiceMirror.TempleAvailable=true;
        TownServiceMirror.TempleOwner=0;TownServiceMirror.TempleSession=TownServiceMirror.TempleRevision=0;
        TownServiceMirror.TempleTransitionAge=0;
        TownServiceMirror.TempleStates.Clear();TownServiceEnhancementHandoff.HasVisibleCue=false;
        TownServiceMirror.RemoteEnhancementCue=false;
        TownServiceAvailability.Locked.Clear();
        WorldUIConfig.ImmersiveTownServices.Value=true;MapRoomDriver.Active=true;MapRoomDriver.FrameReady=true;
        StoryComposite.PointOfNoReturn=false;TownServiceVoice.Requests=0;
        MapRoomDriver.Center=new(10,20,30);MapRoomDriver.Scale=2;NetPlayerActors.Local=10;
        TownServicePresentation.Active=false;TownServicePresentation.Service=0;TownServicePresentation.SessionAge=0;
        TownServiceStation.Ready=true;TownServiceStation.Missing.Clear();TownServiceStation.Creates=0;TownServiceStation.Disposals=0;
        Time.unscaledTime=100;Time.unscaledDeltaTime=.3f;TownServiceStation.Floor=4;
    }
    private static void Tick(float dt=.3f) { Time.unscaledDeltaTime=dt;Time.unscaledTime+=dt;TownServicePopulation.Tick(); }
    private static TownResidentsState State(float tag=7)
    {
        var state=new TownResidentsState{Active=true};
        for(int n=0;n<3;n++)state.Set(n,new TownResidentPose{Pose=new RigPose{Position=new Vector3(n+1,tag,n+2),Rotation=Quaternion.identity},Scale=1,Age=tag,Visibility=255,ActorFloorOffset=-.03f*(n+1),FurnitureBottom=-.06f*(n+1)});
        return state;
    }
    private static void Observe(int peer,TownResidentsState state)=>RemoteTownResidents.Observe(peer,new PresenceState{HasTownResidents=true,TownResidents=state});
    private static void LockedResidentLifetime()
    {
        Reset(); Tick(); AllVisible();
        NativeTemplates.Invalidated.Clear();
        TownServiceAvailability.Locked.Add(2); Tick();
        Check(TownServiceStation.Live.Count == 2 && !TownServiceStation.Live.ContainsKey(2)
            && !TownServiceVisitTarget.Live.ContainsKey(2),
            "a native-locked temple retires its complete resident and interaction target");
        Check(TownServiceStation.Live.ContainsKey(1) && TownServiceStation.Live.ContainsKey(3)
            && TownServicePopulation.Published.Active,
            "locking one service leaves independently unlocked town residents published");
        var published = TownServicePopulation.Published;
        Check(TownResidentsCodec.Valid(in published),
            "locked resident cannot invalidate the complete global authority record");
        Check(NativeTemplates.Invalidated.Contains(2),
            "locked resident invalidates its native furniture template");
        TownServiceAvailability.Locked.Clear(); Tick(); AllVisible();
        Reset();
    }
    private static void AllVisible()
    {
        Check(TownServiceStation.Live.Count==3,"all three permanent residents");
        for(byte s=1;s<=3;s++)
        {Check(TownServicePopulation.Available(s),"resident ready affordance");Near(TownServiceStation.Live[s].Visibility,1,"resident fully visible");Check(TownServiceVisitTarget.Live[s].Enabled,"ready resident input");}
    }
    private static void MerchantOffering()
    {
        Reset();TownServiceStation.NearVisitor=true;Tick();
        Check(TownServicePopulation.PublishedActivities.Merchant.Engaged,
            "nearby visitor may receive the merchant's gaze without offering a card");
        Check(TownServiceStation.Live[1].AttentionQueries>0,"merchant gaze remains independent of offered cards");
        Check(TownServicePopulation.PublishedActivities.Temple.Engaged,"other residents retain ordinary visitor attention");
        TownServiceStation.NearVisitor=false;Tick();
        Check(!TownServicePopulation.PublishedActivities.Merchant.Engaged,
            "merchant gaze ends outside visitor range before offer tests");
        TownServiceMerchantHandoff.WantsOffering=true;Tick();
        Check(TownServicePopulation.PublishedActivities.Merchant.Engaged,"local eligible card requests authoritative palm");
        TownServiceMerchantHandoff.WantsOffering=false;TownServiceMirror.RemoteMerchantOffering=true;Tick();
        Check(TownServicePopulation.PublishedActivities.Merchant.Engaged,"remote owner offering requests the same authoritative palm");
        TownServiceMirror.RemoteMerchantOffering=false;Tick();
        Check(!TownServicePopulation.PublishedActivities.Merchant.Engaged,"last offer withdrawal releases merchant arm");
    }
    private static void EnchantressApproach()
    {
        Reset(); TownServiceStation.NearVisitor=true; Tick(.7f);
        Check(TownServicePopulation.PublishedActivities.Enchantress.Engaged,
            "approach extends the enchantress hand before any native window cue");
        TownServiceMirror.RemoteEnhancementCue=true; Tick(.7f);
        Check(TownServicePopulation.PublishedActivities.Enchantress.Engaged,
            "a subsequent remote native cue cannot withdraw the offering pose");
    }
    private static void TemplePresentation()
    {
        // Learn an available baseline while nobody is visiting, then approach after
        // availability has changed. The first attentive frame must already be on the
        // direct prayer-to-cover path; it may never expose the available hands-down pose.
        Reset();TownServiceStation.NearVisitor=false;
        TownServiceMirror.TempleReceived=true;TownServiceMirror.TempleOwner=10;
        TownServiceMirror.TempleSession=6;TownServiceMirror.TempleKnown=true;
        TownServiceMirror.TempleAvailable=true;TownServiceMirror.TempleRevision=8;
        Tick(.7f);TownServiceMirror.TempleAvailable=false;TownServiceStation.NearVisitor=true;Tick(.01f);
        TownActivityPose directState=TownServicePopulation.PublishedActivities.Temple;
        var direct=TownServiceActivityMotion.Visual(2,in directState);
        TownServiceActivityMotion.ApplyTempleAvailability(ref direct,false,1f);
        Check(Vector3.Distance(TownServiceStation.Live[2].LastActivity.Left,direct.Left)<.0001f,
            "known unavailable approach transitions directly from prayer to covered bowl");

        // A late private-window hydration is different: attention has already moved
        // the arms. The newly learned unavailable baseline must blend from that
        // visible pose, not snap the cover weight to one on its first packet.
        Reset();TownServiceStation.NearVisitor=true;
        Tick(.7f);
        Vector3 beforeHydration=TownServiceStation.Live[2].LastActivity.Left;
        TownServiceMirror.TempleReceived=true;TownServiceMirror.TempleOwner=10;
        TownServiceMirror.TempleSession=8;TownServiceMirror.TempleKnown=true;
        TownServiceMirror.TempleAvailable=false;TownServiceMirror.TempleRevision=2;
        Tick(.01f);
        Check(Vector3.Distance(beforeHydration,TownServiceStation.Live[2].LastActivity.Left)<.03f,
            "late unavailable hydration retains a continuous arm pose");

        Reset();TownServiceStation.NearVisitor=true;
        TownServiceMirror.TempleReceived=true;TownServiceMirror.TempleOwner=10;
        TownServiceMirror.TempleSession=7;TownServiceMirror.TempleKnown=true;
        TownServiceMirror.TempleAvailable=true;Tick(.7f);
        TownServiceMirror.TempleAvailable=false;TownServiceMirror.TempleRevision=1;
        TownServiceMirror.TempleTransitionAge=.14f;Tick(.01f);
        TownActivityPose donationState=TownServicePopulation.PublishedActivities.Temple;
        var donationExpected=TownServiceActivityMotion.Visual(2,in donationState);
        TownServiceActivityMotion.ApplyTempleBlessing(ref donationExpected,.14f);
        TownServiceActivityMotion.ApplyTempleBreath(ref donationExpected,
            TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(TownServiceStation.Live[2].LastActivity.Left,donationExpected.Left)<.0001f,
            "live donation plays the replicated blessing before the unavailable cover");

        Reset();TownServiceStation.NearVisitor=true;
        TownServiceMirror.TempleReceived=true;TownServiceMirror.TempleOwner=10;
        TownServiceMirror.TempleSession=7;TownServiceMirror.TempleKnown=true;
        TownServiceMirror.TempleAvailable=false;TownServiceMirror.TempleRevision=9;
        Tick(.01f);Tick(TownServiceActivityMotion.TransitionSeconds);
        TownServiceStation temple=TownServiceStation.Live[2];
        Check(temple.Blessings==0,"unavailable hydration establishes a blessing baseline");

        // Closing and rebuilding the native ritual briefly removes the manifest. Rehydrating
        // the same unavailable revision is presentation continuity, never a new donation.
        TownServiceMirror.TempleReceived=false;Tick(.01f);
        Vector3 before=temple.LastActivity.Left;
        TownServiceMirror.TempleReceived=true;Tick(.01f);
        Check(temple.Blessings==0,"unchanged unavailable revision does not replay after reopen");
        Check(Vector3.Distance(before,temple.LastActivity.Left)<.003f,
            "one-frame temple manifest gap preserves the running cover pose");

        TownServiceMirror.TempleRevision=10;TownServiceMirror.TempleTransitionAge=.12f;Tick(.01f);
        Check(temple.Blessings==1,"live revision in the same session plays one blessing");
        Near(temple.LastBlessingAge,.12f,"blessing resumes at replicated age");
        Tick(.01f);Check(temple.Blessings==1,"unchanged revision cannot replay per frame");
        TownServiceMirror.TempleReceived=false;Tick(.01f);
        TownServiceMirror.TempleReceived=true;Tick(.01f);
        Check(temple.Blessings==1,"unchanged committed revision cannot replay after hydration");

        TownServiceMirror.TempleSession=8;TownServiceMirror.TempleRevision=40;Tick(.01f);
        Check(temple.Blessings==1,"new owner session establishes a baseline even at higher revision");

        // Departure used to assign cover blend zero before the analytic attention transition.
        // At 90 Hz both the palm target and cover contribution must begin their return gradually.
        // The anatomical bowl path moves roughly 4 mm in its first departure frame.
        TownServiceMirror.TempleReceived=false;TownServiceStation.NearVisitor=false;
        before=temple.LastActivity.Left;float beforeRoll=temple.LastActivity.LeftRoll;Tick(1f/90f);
        Check(Vector3.Distance(before,temple.LastActivity.Left)<.005f,
            "temple cover and visitor attention release continuously on departure: "
            + Vector3.Distance(before,temple.LastActivity.Left) + " m");
        // An already playing blessing has its own smooth wrist motion. The
        // combined departure must remain below two degrees per 90 Hz frame;
        // the old half-degree cover-only bound incorrectly rejected that motion.
        Check(Math.Abs(beforeRoll-temple.LastActivity.LeftRoll)<2f,
            "temple cover wrist rotation releases continuously on departure: "
            + beforeRoll + " -> " + temple.LastActivity.LeftRoll);
    }
    private static void TempleConcurrentVisitors()
    {
        Reset();TownServiceStation.NearVisitor=true;
        TownServiceMirror.TempleStates.Add(new TownTempleDonationState(10,1,true,true,4,0f));
        Tick(.7f);
        TownServiceStation single=TownServiceStation.Live[2];
        Vector3 beforeEligibilityChange=single.LastActivity.Left;
        TownServiceMirror.TempleStates[0]=new TownTempleDonationState(10,1,true,false,4,0f);
        Tick(.01f);
        Check(single.Blessings==0 && Vector3.Distance(beforeEligibilityChange,single.LastActivity.Left)<.02f,
            "non-donation eligibility loss eases the bowl cover instead of snapping the arm");

        Reset();TownServiceStation.NearVisitor=true;
        TownServiceMirror.TempleStates.Add(new TownTempleDonationState(10,1,true,true,4,0f));
        TownServiceMirror.TempleStates.Add(new TownTempleDonationState(11,2,true,true,7,0f));
        Tick(.7f);
        TownServiceStation temple=TownServiceStation.Live[2];
        TownActivityPose pose=TownServicePopulation.PublishedActivities.Temple;
        var open=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleBreath(ref open,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(temple.LastActivity.Left,open.Left)<.0001f,
            "two eligible visitors retain the shared open bowl");

        TownServiceMirror.TempleStates[0]=new TownTempleDonationState(10,1,true,false,4,0f);
        Tick(.01f);
        Check(temple.Blessings==0,"eligibility change without a native commit cannot play a blessing");
        pose=TownServicePopulation.PublishedActivities.Temple;
        open=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleBreath(ref open,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(temple.LastActivity.Left,open.Left)<.0001f,
            "one ineligible visitor cannot cover the bowl while another can donate");

        TownServiceMirror.TempleStates[1]=new TownTempleDonationState(11,2,true,true,8,.12f);
        Tick(.01f);
        Check(temple.Blessings==1,"non-elected eligible visitor's explicit donation plays one blessing");
        Tick(.01f);Check(temple.Blessings==1,"unchanged revision cannot replay for either visitor");
        TownServiceMirror.TempleStates.RemoveAt(1);Tick(.01f);
        TownServiceMirror.TempleStates.Add(new TownTempleDonationState(11,2,true,true,8,.14f));Tick(.01f);
        Check(temple.Blessings==1,"transient visitor manifest gap preserves per-session donation baseline");

        TownServiceMirror.TempleStates[1]=new TownTempleDonationState(11,2,true,false,8,.14f);
        Tick(3f);
        pose=TownServicePopulation.PublishedActivities.Temple;
        var stillBlessing=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleBreath(ref stillBlessing,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(temple.LastActivity.Left,stillBlessing.Left)<.001f,
            "remaining blessing motes do not freeze the hand on the unavailable cover");
        Tick(1.1f);Tick(TownServiceActivityMotion.TransitionSeconds);
        pose=TownServicePopulation.PublishedActivities.Temple;
        var closed=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleAvailability(ref closed,false,1f);
        TownServiceActivityMotion.ApplyTempleBreath(ref closed,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(temple.LastActivity.Left,closed.Left)<.001f,
            "bowl cover follows the complete blessing tail for the unavailable visitor");
        TownServiceMirror.TempleStates.Add(new TownTempleDonationState(12,3,false,false,0,0f));
        Tick(TownServiceActivityMotion.TransitionSeconds);
        pose=TownServicePopulation.PublishedActivities.Temple;
        open=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleBreath(ref open,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(temple.LastActivity.Left,open.Left)<.001f,
            "new unknown visitor opens bowl until native eligibility is known");
    }
    private static void StoryCommitment()
    {
        Reset();TownServiceStation.NearVisitor=true;
        TownServiceMirror.RemoteSessions[2]=new TownServiceSessionInfo { Active=true,Service=2,Peer=2,
            ReceivedTime=Time.unscaledTime,LastSeenTime=Time.unscaledTime,SessionAge=.1f };
        TownServiceMirror.TempleReceived=true;TownServiceMirror.TempleOwner=10;
        TownServiceMirror.TempleSession=7;TownServiceMirror.TempleKnown=true;
        TownServiceMirror.TempleAvailable=false;TownServiceMirror.TempleRevision=9;
        StoryComposite.PointOfNoReturn=true;Tick(.7f);
        Check(!TownServicePopulation.PublishedActivities.Merchant.Engaged
            && !TownServicePopulation.PublishedActivities.Temple.Engaged
            && !TownServicePopulation.PublishedActivities.Enchantress.Engaged,
            "point of no return withdraws all player-facing resident attention");
        Check(TownServiceVisitTarget.Live.Values.All(target=>!target.Visible),
            "point of no return withdraws every resident visit");
        Check(TownServiceStation.Live.Count==3,
            "point of no return preserves shared resident presentation for a remote visitor");
        Check(TownServiceStation.Live.Values.All(station=>!station.LastActivityAudioVisible),
            "point of no return silences local and remote resident work audio");
        Check(TownServiceVoice.Requests==0,
            "point of no return suppresses contextual resident speech");
        TownServiceMirror.TempleRevision=10;Tick(.01f);
        Check(TownServiceStation.Live[2].Blessings==0,
            "point of no return consumes a remote temple revision without replaying its blessing");
        StoryComposite.PointOfNoReturn=false;Tick(.01f);
        Check(TownServiceStation.Live.Values.All(station=>station.LastActivityAudioVisible),
            "leaving story commitment restores resident audio without rebuilding the stations");
        Check(TownServicePopulation.PublishedActivities.Merchant.TransitionAge>0f,
            "leaving story commitment resumes the existing smooth attention transition");
    }
    private static void WireAndFollowerParity()
    {
        var commitGate=new TownServicePopulation.TempleBlessingGate();
        Check(commitGate.Observe(true,2,4,true,false,1,.25f,true),
            "first manifest with explicit fresh commit clock retains the blessing");
        Check(!commitGate.Observe(true,2,4,true,false,1,.3f,true),
            "the same first committed manifest does not replay twice");
        Check(!commitGate.Observe(true,3,5,true,false,1,8f,true),
            "old committed event does not restart for a late observer");
        Check(!commitGate.Observe(true,4,6,true,false,1,.25f,false),
            "legacy unavailable baseline without a commit clock cannot invent a donation");
        Reset(); Tick(.7f);
        var state=TownServicePopulation.PublishedActivities;
        var face=TownServicePopulation.PublishedFaces;
        Check(state.Merchant.TransitionAge==TownActivityPose.TransitionSeconds
            && TownActivityCodec.Valid(in state),
            "production settled .95-second activity remains serializable");
        byte[] bytes=new byte[TownActivityCodec.PacketBytes];
        int count=TownActivityCodec.WritePacket(bytes,in state,in face);
        Check(count==TownActivityCodec.PacketBytes
            && TownActivityCodec.ReadPacket(bytes,count,out var replay,out var replayFace),
            "actual producer writes and reads the extended atomic performance packet");
        Check(TownActivityCodec.ReadPacket(bytes,count,out replay,out replayFace)
            && replay.HasSharedPerformance && replay.Interactive,
            "new performance tail retains the author interaction state");
        Check(replay.HasEnvironmentLight && replay.EnvironmentLightIntensity==.4f
            && replay.EnvironmentLightColour.y==.8f && replay.EnvironmentLightDirection.z==1f,
            "author fill sampler survives the actual atomic packet");
        var invalid=state;invalid.TempleUnavailableBlend=1.01f;
        Check(!TownActivityCodec.Valid(in invalid),"out-of-range author cover is rejected");
        invalid=state;invalid.TempleBlessingGeneration=1;invalid.TempleBlessingStartedClock=state.Clock+.01f;
        Check(!TownActivityCodec.Valid(in invalid),"future blessing timestamps cannot invent a gesture");
        invalid=state;invalid.EnvironmentLightDirection=new(0f,0f,2f);
        Check(!TownActivityCodec.Valid(in invalid),"non-unit environment light direction is rejected");
        invalid=state;invalid.EnvironmentLightColour.x=float.NaN;
        Check(!TownActivityCodec.Valid(in invalid),"non-finite author light colour is rejected");
        Check(replay.HasAuthoredFoley,"author contact records survive the actual performance packet");
        invalid=state;invalid.MerchantFoley=new(){Cue=6,Generation=1,StartedClock=state.Clock};
        Check(!TownActivityCodec.Valid(in invalid),"merchant cannot author enchantress spell foley");
        var light=state;light.HasAuthoredFoley=false;
        int lightCount=TownActivityCodec.WritePacket(bytes,in light,in face);
        Check(lightCount==198 && TownActivityCodec.ReadPacket(bytes,lightCount,out replay,out _)
            && replay.HasEnvironmentLight && !replay.HasAuthoredFoley,
            "light performance without contact records remains readable");
        var contacts=state;contacts.MerchantFoley=new(){Cue=1,Generation=4,StartedClock=state.Clock*.5f};
        contacts.EnchantressFoley=new(){Cue=6,Generation=9,StartedClock=state.Clock*.75f};
        int contactCount=TownActivityCodec.WritePacket(bytes,in contacts,in face);
        Check(contactCount==216 && TownActivityCodec.ReadPacket(bytes,contactCount,out replay,out _)
            && replay.MerchantFoley.Generation==4 && replay.EnchantressFoley.Cue==6
            && replay.MerchantFoley.StartedClock==contacts.MerchantFoley.StartedClock,
            "authored sound cue, revision and onset survive wire round trip");
        var shared=state;shared.HasAuthoredFoley=false;shared.HasEnvironmentLight=false;
        int sharedCount=TownActivityCodec.WritePacket(bytes,in shared,in face);
        Check(sharedCount==170 && TownActivityCodec.ReadPacket(bytes,sharedCount,out replay,out _)
            && replay.HasSharedPerformance && !replay.HasEnvironmentLight,
            "shared performance without a lighting tail remains readable");
        var legacy=state;legacy.HasAuthoredFoley=false;legacy.HasEnvironmentLight=false;legacy.HasSharedPerformance=false;
        int legacyCount=TownActivityCodec.WritePacket(bytes,in legacy,in face);
        Check(legacyCount==157 && TownActivityCodec.ReadPacket(bytes,legacyCount,out replay,out _)
            && !replay.HasSharedPerformance,"historical 53-byte activity packet remains readable");
        Reset();
        state=new TownActivityState{Active=true,Epoch=11,Sequence=1,Clock=20,
            HasSharedPerformance=true,Interactive=true,TempleUnavailableBlend=.73f,
            HasEnvironmentLight=true,EnvironmentLightDirection=new(0,1,0),
            EnvironmentLightColour=new(.2f,.3f,.4f),EnvironmentLightIntensity=.25f};
        for(int n=0;n<3;n++)state.Set(n,new TownActivityPose{WorkClock=9f,TransitionAge=TownActivityPose.TransitionSeconds,Engaged=true});
        face=new TownFaceState{Active=true,Epoch=11,Sequence=1,Clock=20};
        var presence=new PresenceState{HasTownResidents=true,TownResidents=State(),
            HasTownActivity=true,TownActivity=state,HasTownFace=true,TownFace=face};
        RemoteTownResidents.Observe(2,in presence);Tick(.01f);
        Check(!TownServicePopulation.IsFaceAuthor,"the observer follows one lower-id resident author");
        Check(TownServiceLighting.Replays>0 && TownServiceLighting.LastReplay.EnvironmentLightIntensity==.25f
            && TownServiceLighting.LastReplay.EnvironmentLightDirection.y==1f,
            "follower applies the published light instead of resampling its own room");
        var pose=TownServicePopulation.PublishedActivities.Temple;
        var expected=TownServiceActivityMotion.Visual(2,in pose);
        TownServiceActivityMotion.ApplyTempleAvailability(ref expected,false,.73f);
        TownServiceActivityMotion.ApplyTempleBreath(ref expected,TownServicePopulation.PublishedActivities.Clock);
        Check(Vector3.Distance(TownServiceStation.Live[2].LastActivity.Left,expected.Left)<.0001f,
            "follower uses author cover even with no local temple manifest");
        state.Sequence=2;face.Sequence=2;state.Clock=face.Clock=20.01f;
        state.TempleBlessingGeneration=1;state.TempleBlessingStartedClock=19.81f;
        presence.TownActivity=state;presence.TownFace=face;
        RemoteTownResidents.Observe(2,in presence);Tick(.01f);
        TownServiceStation temple=TownServiceStation.Live[2];
        Check(temple.Blessings==1 && temple.LastBlessingAge>=.20f,
            "observer replays the durable author blessing without its local donation revision");
        Tick(.01f);Check(temple.Blessings==1,
            "repeated snapshots do not restart the shared blessing event");
        state.Sequence=3;face.Sequence=3;state.Clock=face.Clock=20.03f;state.Interactive=false;
        presence.TownActivity=state;presence.TownFace=face;
        RemoteTownResidents.Observe(2,in presence);Tick(.01f);
        Check(!temple.LastActivityAudioVisible,
            "author story commitment silences observers before their private UI catches up");
    }
    private static void Main()
    {
        WireAndFollowerParity();
        MerchantOffering();
        EnchantressApproach();
        TemplePresentation();
        TempleConcurrentVisitors();
        StoryCommitment();
        LockedResidentLifetime();
        Reset();Tick();AllVisible();Check(TownServicePopulation.Published.Active,"ready population advertised");
        Check(!TownServicePopulation.Published.HasCloth,
            "resident authors no retired stand-cloth controls");
        for(int i=0;i<8;i++)Tick(1);AllVisible();Check(TownServiceStation.Creates==3,"no visitor-dependent respawn or repeated create");
        Check(TownServiceStation.Live.Values.All(s=>s.Clip=="Idle"),"no greeting without a visit");
        TownServicePresentation.Active=true;TownServicePresentation.Service=2;TownServicePresentation.SessionAge=.4f;Tick(.1f);
        Check(TownServiceStation.Live[2].Clip=="Idle","native visit preserves continuous occupation body clock");Check(TownServiceStation.Live[1].Clip=="Idle","unvisited resident remains idle");
        TownServicePresentation.Active=false;Tick();AllVisible();
        NativeTemplates.Invalidated.Clear();WorldUIConfig.ImmersiveTownServices.Value=false;Tick();Check(TownServiceStation.Live.Count==0,"opt out removes unvisited residents");Check(NativeTemplates.Invalidated.Count==3,"retired residents invalidate all native decoration templates");Check(!TownServicePopulation.Published.Active,"opt out withdraws authority");
        WorldUIConfig.ImmersiveTownServices.Value=true;Tick();AllVisible();Check(TownServiceStation.Creates==6,"re-enable re-creates all residents");
        MapRoomDriver.Active=false;Tick();Check(TownServiceStation.Live.Count==0&&TownServiceVisitTarget.Live.Count==0,"leaving map destroys residents and input");Check(TownServicePopulation.Frame==null,"leaving map clears common frame");

        Reset();TownServiceStation.Ready=false;Tick();Check(TownServiceStation.Live.Count==3,"assets preparing residents allocated");
        Check(!TownServicePopulation.Published.Active,"not ready never advertised as authority");
        for(byte s=1;s<=3;s++) {Check(!TownServicePopulation.Available(s),"not ready never replaces native affordance");Near(TownServiceStation.Live[s].Visibility,0,"counter hidden until original dressing ready");Check(!TownServiceVisitTarget.Live[s].Enabled,"no input before assets");}
        TownServiceStation.Ready=true;Tick(.05f);Check(TownServiceStation.Live[1].Visibility<1,"ready entrance retains animated fade");Check(!TownServiceVisitTarget.Live[1].Enabled,"no input during entrance");Tick(.3f);AllVisible();

        Reset();TownServiceStation.Missing.Add(2);Tick();Check(!TownServicePopulation.Available(2),"missing bundle no fake resident");Check(!TownServicePopulation.Published.Active,"partial population cannot author");int attempts=TownServiceStation.Creates;Tick(.2f);Check(TownServiceStation.Creates==attempts,"asset retry bounded");TownServiceStation.Missing.Clear();Tick(2.1f);AllVisible();
        Reset();MapRoomDriver.FrameReady=false;Tick();Check(TownServiceStation.Live.Count==0,"unavailable map frame cannot spawn");MapRoomDriver.FrameReady=true;Tick();AllVisible();

        Reset();Observe(5,State(5));
        var formerClothOwner = State(2); formerClothOwner.HasCloth = true;
        formerClothOwner.MerchantCloth = new TownClothRunnerState { Left = new Vector2(-.018f, .012f) };
        formerClothOwner.TempleLeft = new TownClothRunnerState { Left = new Vector2(.02f, -.01f) };
        Observe(2,formerClothOwner);
        Check(RemoteTownResidents.TryAuthor(out var chosen,out var elapsed),"remote author elected");Near(chosen.Merchant.Age,2,"lowest fresh player wins");Near(elapsed,0,"new packet elapsed zero");
        Tick(.5f);Near(TownServiceStation.Live[1].Root.position.y,24,"author pose mapped through shared frame");Near(TownServiceStation.Live[1].Age,2.5f,"author animation extrapolated");
        Check(!TownServicePopulation.Published.HasCloth,
            "legacy cloth tail is ignored when a remote resident pose is republished");
        for(byte s=1;s<=3;s++)
        {
            Near(TownServiceStation.Live[s].ActorFloorOffset,-.03f*s,"observer applies author's sole height");
            Near(TownServiceStation.Live[s].FurnitureBottom,-.06f*s,"observer applies author's furniture contact");
            Near(TownServicePopulation.Published.At(s-1).ActorFloorOffset,-.03f*s,"republished sole height stays authored");
            Near(TownServicePopulation.Published.At(s-1).FurnitureBottom,-.06f*s,"republished support height stays authored");
        }
        Check(TownServiceStation.Live.Values.All(s=>s.LastAuthor==false),"followers never choose their local floor");TownServiceStation.Floor=-100;Tick(.2f);Near(TownServiceStation.Live[1].Root.position.y,24,"viewer environment floor cannot overwrite author");
        RemoteTownResidents.Forget(2);Check(RemoteTownResidents.TryAuthor(out chosen,out _),"next peer after departure");Near(chosen.Merchant.Age,5,"next lowest wins");
        Observe(5,new TownResidentsState{Active=false});Check(!RemoteTownResidents.TryAuthor(out _,out _),"opt-out withdraws immediately");Tick();Check(TownServiceStation.Live.Values.All(s=>s.LastAuthor==true),"authority handover restores local placement");Near(TownServiceStation.Live[1].Root.position.y,-100,"new author applies current environment floor");
        Observe(2,State());Time.unscaledTime+=NetProtocol.StaleTimeoutSeconds+.01f;Check(!RemoteTownResidents.TryAuthor(out _,out _),"stale author expires");
        Observe(2,State());RemoteTownResidents.Observe(2,default);Check(!RemoteTownResidents.TryAuthor(out _,out _),"missing resident presence withdraws prior author");
        Observe(2,State());NetPlayerActors.Local=1;Check(!RemoteTownResidents.TryAuthor(out _,out _),"lower local player retains authority");WorldUIConfig.ImmersiveTownServices.Value=false;Check(RemoteTownResidents.TryAuthor(out _,out _),"opted-out local cannot claim authority");

        Reset();WorldUIConfig.ImmersiveTownServices.Value=false;Observe(2,State());Tick();Check(TownServiceStation.Live.Count==0,"remote permanent residents alone do not override opted-out viewer");
        TownServiceMirror.RemoteSessions[2]=new TownServiceSessionInfo{Peer=2,Active=true,Service=1,ReceivedTime=Time.unscaledTime,LastSeenTime=Time.unscaledTime,SessionAge=.3f};Tick(.1f);
        Check(TownServiceStation.Live.Count==1&&TownServiceStation.Live.ContainsKey(1),"actual remote visitor only opens its station for opted-out viewer");Check(!TownServiceVisitTarget.Live[1].Enabled,"opted-out viewer has no immersive visit input");Check(TownServiceVisitTarget.Live[1].Visible,"visible remote resident still occludes behind UI for opted-out viewer");Check(!TownServicePopulation.Published.Active,"observer of remote visit never advertises enabled population");
        Time.unscaledTime+=NetProtocol.StaleTimeoutSeconds+.1f;Tick();Check(TownServiceStation.Live.Count==0,"stale visitor station retires");Check(!TownServicePopulation.HasRemoteVisitors,"stale visitor does not keep remote presence alive");
        Reset();var presence=default(PresenceState);presence.TownActivityRecordSeen=false;MapRoomDriver.Active=false;RemoteTownResidents.Sample(ref presence);Check(!presence.HasTownResidents,"no resident presence outside map");MapRoomDriver.Active=true;Tick();RemoteTownResidents.Sample(ref presence);Check(presence.HasTownResidents&&presence.TownResidents.Active,"map presence publishes all prepared residents");
        int oldClears=TownServiceConfirmationMask.Clears;TownServicePopulation.Reset();Check(!TownServicePopulation.Published.Active,"reset withdraws published population");Check(TownServiceConfirmationMask.Clears==oldClears+1,"reset releases confirmation presentation ownership");
        Reset();NetPlayerActors.Local=10;
        Tick(500f);
        var face=new TownFaceState{Active=true,Epoch=42,Sequence=1,Clock=20};face.Set(0,new TownFacePose{HeadYaw=22});
        var authority=new PresenceState{HasTownResidents=true,TownResidents=State(),HasTownFace=true,TownFace=face};
        authority.TownActivityRecordSeen=true;
        RemoteTownResidents.Observe(2,in authority);
        Check(!RemoteTownFaces.Sample(2,out _,out _),"malformed81 cannot accept otherwise valid face half through legacy fallback");
        authority.TownActivityRecordSeen=false;
        float authorReceived=Time.unscaledTime;
        RemoteTownResidents.Observe(2,in authority);Tick();
        Check(!TownServiceStation.Live[1].FaceAuthor&&TownServiceStation.Live[1].FaceReceived,"follower applies received face without local attention election");
        Near(TownServiceStation.Live[1].FacePose.HeadYaw,0,"new authority starts from actual previously displayed gaze");
        Tick(.35f);
        Near(TownServiceStation.Live[1].FacePose.HeadYaw,22,"follower face reaches authority angles after shared recovery");
        Near(TownServicePopulation.PublishedFaces.Clock,20+(Time.unscaledTime-authorReceived),"follower clock uses author even when local history is far ahead");
        NetPlayerActors.Local=1;Tick();
        Check(TownServiceStation.Live[1].FaceAuthor&&TownServiceStation.Live[1].FaceSeeds==1,"new lower ID seeds existing authority before authoring");
        Near(TownServiceStation.Live[1].FacePose.HeadYaw,22,"handover retains existing head angle");
        Check(TownServicePopulation.PublishedFaces.Clock>=20 && TownServicePopulation.PublishedFaces.Clock<21,"handover face clock never regresses");
        WorldUIConfig.ImmersiveTownServices.Value=false;
        TownServiceMirror.RemoteSessions[3]=new TownServiceSessionInfo{Peer=3,Active=true,Service=1,ReceivedTime=Time.unscaledTime,LastSeenTime=Time.unscaledTime};
        RemoteTownResidents.Forget(2);RemoteTownFaces.Forget(2);Tick();
        Check(!TownServiceStation.Live[1].FaceAuthor&&!TownServicePopulation.IsFaceAuthor,"opted out observer never authors even without eligible peer");
        Check(!TownServicePopulation.PublishedFaces.Active,"opted out observer never publishes face ownership");
        Console.WriteLine($"Town residents: {_assertions} production lifecycle/authority assertions passed");
    }
}

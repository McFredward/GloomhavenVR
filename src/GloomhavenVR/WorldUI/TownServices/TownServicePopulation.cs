using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>One cosmetic NPC per service, independent of the number of browsing players.
/// The canonical parchment frame never uses a visitor's zoom, head pose or environment choice.</summary>
internal static class TownServicePopulation
{
    /// <summary>Track explicit committed donation revisions per visitor session. A character
    /// change can alter eligibility without a donation, and the elected interaction owner
    /// need not be the visitor who donated. Reopening or a one-frame manifest gap retains
    /// each baseline. Old sessions are bounded because their manifests cannot return after
    /// dozens of later openings in the same map.</summary>
    internal sealed class TempleBlessingGate
    {
        private readonly Dictionary<(int Owner, uint Session), uint> _seen = new();
        private readonly Queue<(int Owner, uint Session)> _order = new();

        internal bool Observe(bool received, int owner, uint session, bool known,
            bool available, uint revision, float commitAge = float.PositiveInfinity,
            bool hasCommitAge = false)
        {
            if (!received || owner <= 0 || session == 0) return false;
            var key = (owner, session);
            if (!_seen.TryGetValue(key, out uint previous))
            {
                _seen.Add(key, revision);
                _order.Enqueue(key);
                if (_order.Count > 64) _seen.Remove(_order.Dequeue());
                // A first manifest can already contain the donation: the native
                // callback may win the race with its initial availability packet.
                // Only the explicit committed event clock permits replay here;
                // an unavailable character or a saved revision is not an event.
                return known && revision != 0 && hasCommitAge && !float.IsNaN(commitAge)
                    && !float.IsInfinity(commitAge) && commitAge >= 0f
                    && commitAge < TownServiceActivityMotion.TempleBlessingVisualSeconds;
            }
            bool committed = known && unchecked((int)(revision - previous)) > 0;
            if (committed) _seen[key] = revision;
            return committed;
        }
    }

    private sealed class Resident
    {
        internal TownServiceStation Station = null!;
        internal float Visibility, Age;
        internal byte Clip;
        internal TownActivityPose Activity = new TownActivityPose { TransitionAge = TownServiceActivityMotion.TransitionSeconds };
        internal float MerchantOfferingBlend;
        internal readonly TempleBlessingGate TempleBlessing = new();
        internal float TempleUnavailableBlend;
        internal bool TempleDirectCover;
        internal bool TempleHydratingCover;
        internal bool TempleUnavailableSpoken;
        internal bool TempleAvailabilityObserved;
        internal float TempleBlessingStartedAt = float.NegativeInfinity;
        internal float TempleBlessingStartedClock;
        internal uint TempleBlessingGeneration;
        internal bool ObservedActivity;
        internal readonly TownServiceActivityHandover Handover = new();
        internal TownServiceVisitTarget Visit = null!;
        internal TownServiceOccupationBadge Occupation = null!;
    }
    private static readonly Dictionary<byte, Resident> Residents = new();
    private static readonly List<TownTempleDonationState> TempleDonationStates = new(4);
    private static GameObject? _frame;
    internal static Transform? Frame => _frame != null ? _frame.transform : null;
    internal static TownResidentsState Published { get; private set; }
    internal static TownFaceState PublishedFaces { get; private set; }
    internal static TownActivityState PublishedActivities { get; private set; }
    internal static bool IsFaceAuthor { get; private set; }
    internal static uint PerformanceEpoch { get; private set; }
    private static uint _faceSequence, _faceEpoch;
    private static float _faceClock, _lastRemoteFaceTime = float.NegativeInfinity;
    internal static bool Available(byte service) => TownServiceAvailability.NativeUnlocked(service)
        && Residents.TryGetValue(service, out Resident? resident)
        && resident.Station.IsReady;
    private static float _started, _retryAt;
    internal static bool HasRemoteVisitors
    {
        get
        {
            float now = Time.unscaledTime;
            foreach (TownServiceSessionInfo remote in TownServiceMirror.RemoteSessions.Values)
                if (remote.Active && now - remote.LastSeenTime <= NetProtocol.StaleTimeoutSeconds) return true;
            return false;
        }
    }

    internal static bool Prepare()
    {
        if (!MapRoomDriver.Active || !MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale)) return false;
        if (_frame == null)
        {
            _frame = new GameObject("GloomhavenVR.TownService.SharedFrame"); _started = Time.unscaledTime;
            _faceEpoch = unchecked((uint)System.Guid.NewGuid().GetHashCode());
            if (_faceEpoch == 0) _faceEpoch = 1;
        }
        // Acquire can call Prepare several times in one presentation frame. Writing an
        // unchanged parent transform dirties every resident and cabinet descendant.
        Transform frame = _frame.transform;
        if (!frame.position.Equals(center) || !frame.rotation.Equals(Quaternion.identity))
            frame.SetPositionAndRotation(center, Quaternion.identity);
        Vector3 frameScale = Vector3.one * scale;
        if (!frame.localScale.Equals(frameScale)) frame.localScale = frameScale;
        TownServiceMirror.SharedFrameForRemote = RemoteFrame;
        return true;
    }

    private static Transform? RemoteFrame(int peer) => MapRoomDriver.Active ? Frame : null;

    internal static TownServiceStation? Acquire(byte service)
    {
        if (!TownServiceAvailability.NativeUnlocked(service)) return null;
        if (!Prepare()) return null;
        if (Residents.TryGetValue(service, out Resident? current)) return current.Station;
        TownServiceStation? station = TownServiceStation.Create(service, _frame!.transform.position, _frame.transform.localScale.x);
        if (station == null) return null;
        station.SetVisibility(0f);
        Residents.Add(service, new Resident { Station = station,
            Visit = new TownServiceVisitTarget(service, station.Root),
            Occupation = new TownServiceOccupationBadge(service, station.Root) });
        return station;
    }

    internal static void Tick()
    {
        bool enabled = WorldUIConfig.ImmersiveTownServices.Value;
        // Native quest commitment is the single boundary for every resident-facing
        // affordance. Keep the residents allocated for visual continuity, while the
        // elected author eases and publishes attention back to neutral for all peers.
        bool interactive = enabled && !StoryComposite.PointOfNoReturn;
        if (!MapRoomDriver.Active) { Reset(); return; }
        TownServiceTutorialPatches.Tick();
        if (_frame == null && !enabled && !HasRemoteVisitors
            && TownServiceEnhancementHandoff.Returning.Count == 0) return;
        if (!Prepare()) { Reset(); return; }
        float now = Time.unscaledTime;
        bool follows = RemoteTownResidents.TryAuthor(out TownResidentsState authored, out float elapsed);
        int faceAuthor = RemoteTownResidents.AuthorPlayer;
        TownFaceState remoteFace = default;
        float faceElapsed = 0f;
        bool hasFace = follows && RemoteTownFaces.Sample(faceAuthor, out remoteFace, out faceElapsed);
        TownActivityState remoteActivity = default;
        float activityElapsed = 0f;
        bool hasActivity = follows && RemoteTownActivities.Sample(faceAuthor, out remoteActivity, out activityElapsed);
        bool wasFaceAuthor = IsFaceAuthor;
        IsFaceAuthor = !follows && enabled;
        if (IsFaceAuthor && !wasFaceAuthor)
        {
            // A returning authority may inherit a different source clock. Give this ownership
            // lifetime its own epoch so its earlier fast packets cannot overwrite the handover.
            _faceEpoch = unchecked((uint)System.Guid.NewGuid().GetHashCode());
            if (_faceEpoch == 0) _faceEpoch = 1;
        }
        PerformanceEpoch = hasActivity ? remoteActivity.Epoch : IsFaceAuthor ? _faceEpoch : PerformanceEpoch;
        TownFaceState seed = default;
        int seedAuthor = 0;
        float seedElapsed = 0f;
        bool seedAuthority = IsFaceAuthor && (!wasFaceAuthor || !PublishedFaces.Active) && RemoteTownFaces.TrySeed(out seed, out seedAuthor, out seedElapsed);
        _faceClock += Mathf.Max(0f, Time.unscaledDeltaTime);
        if (hasFace) { _faceClock = remoteFace.Clock; _lastRemoteFaceTime = now; }
        else if (seedAuthority && now - _lastRemoteFaceTime > NetProtocol.StaleTimeoutSeconds + .25f)
            _faceClock = seed.Clock;
        var faces = new TownFaceState { Active = enabled, Epoch = _faceEpoch,
            Sequence = unchecked(++_faceSequence), Clock = _faceClock };
        var activities = new TownActivityState { Active = enabled, Epoch = _faceEpoch, Sequence = _faceSequence, Clock = _faceClock,
            HasSharedPerformance = true, Interactive = interactive };
        // Only the elected author transmits physical contacts and environment
        // tails. Keep a follower's diagnostic snapshot valid without inventing
        // a second source of shared lighting or sound.
        activities.HasAuthoredFoley = IsFaceAuthor;
        bool sharedInteractive = hasActivity && remoteActivity.HasSharedPerformance
            ? remoteActivity.Interactive : interactive;
        TownActivityState activitySeed = default;
        bool seedActivity = IsFaceAuthor && !wasFaceAuthor && RemoteTownActivities.TrySeed(out activitySeed, out _, out _);
        var published = new TownResidentsState { Active = enabled };
        // A locked resident is an invisible entry, not an invalid quaternion/scale.
        // TLV79 validates all three entries; default zero geometry silently dropped
        // the whole authority record in campaigns with an unopened service.
        for (int index = 0; index < 3; index++)
            published.Set(index, new TownResidentPose
            { Pose = new RigPose { Rotation = Quaternion.identity }, Scale = 1f });
        bool retry = now >= _retryAt;
        bool missing = false;
        for (byte service = 1; service <= 3; service++)
        {
            bool lookingAtVisitor = false;
            bool visiting = TownServiceMirror.TryInteractionOwner(service, out _, out _, out float ownerAge);
            float visitAge = visiting ? ownerAge : float.PositiveInfinity;
            bool unlocked = TownServiceAvailability.NativeUnlocked(service);
            bool used = TownServiceAvailability.ShouldPublish(unlocked, enabled, visiting);
            // An opt-in setting is not an unlock. The original modes read these saved
            // headquarters flags (and their FTUE gate) before exposing their buttons. Keeping
            // even an invisible locked station allocated left its table/props and ray surfaces
            // in the room. Retire the whole resident at the same native availability boundary.
            if (!unlocked)
            {
                if (Residents.TryGetValue(service, out Resident? locked))
                {
                    locked.Occupation.Dispose(); locked.Visit.Dispose(); locked.Station.Dispose(); Residents.Remove(service);
                    NativeTemplates.InvalidateResident(service);
                }
                continue;
            }
            // Prepare already sampled the shared frame above. Acquire is needed only
            // for a missing station; calling it for each existing resident repeats
            // the frame lookup and transform writes on every tick.
            if (!Residents.TryGetValue(service, out Resident? resident) && used && retry)
            {
                Acquire(service);
                Residents.TryGetValue(service, out resident);
            }
            if (resident == null)
            { if (used) missing = true; published.Active = false; continue; }
            resident.Station.RefreshEnvironment(!follows);
            bool ready = resident.Station.IsReady;
            if (!ready) published.Active = false;
            float greeting = resident.Station.GreetingDuration;
            if (used && ready && follows)
            {
                TownResidentPose pose = authored.At(service - 1);
                Transform root = resident.Station.Root;
                root.SetPositionAndRotation(_frame!.transform.TransformPoint(pose.Pose.Position),
                    _frame.transform.rotation * pose.Pose.Rotation);
                root.localScale = Vector3.one * (_frame.transform.lossyScale.x * pose.Scale);
                resident.Station.SetGrounding(pose.ActorFloorOffset, pose.FurnitureBottom);
                resident.Visibility = pose.Visibility / 255f;
                resident.Age = pose.Age + elapsed;
                resident.Clip = pose.Clip;
                if (resident.Clip == 1 && resident.Age >= greeting)
                { resident.Clip = 0; resident.Age -= greeting; }
            }
            else
            {
                resident.Visibility = Mathf.MoveTowards(resident.Visibility, used && ready ? 1f : 0f,
                    Time.unscaledDeltaTime / (used ? .22f : .18f));
                // Occupations replace the discrete greeting restart with a continuous body
                // sample. Hand settling and attention transitions own the visible welcome.
                resident.Clip = 0;
                resident.Age += Mathf.Max(0f, Time.unscaledDeltaTime);
            }
            resident.Station.SetVisibility(resident.Visibility);
            resident.Station.Sample(resident.Clip == 1 ? "Greeting" : "Idle", resident.Age);
            if (hasActivity)
            { resident.Activity = remoteActivity.At(service - 1); resident.ObservedActivity = true; }
            else
            {
                if (seedActivity && !resident.ObservedActivity) resident.Activity = activitySeed.At(service - 1);
                if (IsFaceAuthor)
                {
                    bool engaged = resident.Station.PrepareActivityAttention(resident.Activity.Engaged);
                    // The face starts looking at this visitor immediately. The merchant's
                    // coin hand may finish a transfer before body attention begins.
                    lookingAtVisitor = engaged;
                    // The same elected face attention owns the enchantress's offered hand.
                    // It sees local and remote visitors in her actual approach volume;
                    // a native-window cue can arrive later, or belong to a different
                    // resident while both attention volumes overlap. Requiring that cue
                    // left her looking at a visitor with no hand to accept a card.
                    // Finish the current coin contact before greeting. Immediate
                    // attention could strand a gripped coin in midair; this authored
                    // decision is carried in the ordinary occupation stream.
                    if (service == 1 && interactive) engaged |= TownServiceMerchantHandoff.WantsOffering
                        || TownServiceMirror.RemoteMerchantOffering;
                    if (service == 3 && interactive) engaged |= TownServiceSharedCue.LocalReady
                        || TownServiceSharedCue.HasReadyVisitor(3) || TownServiceMirror.TransactionOwner(3) != 0;
                    if (!interactive) engaged = false;
                    if (service == 1 && engaged && !resident.Activity.Engaged
                        && !TownServiceActivityMotion.MerchantCanAttend(resident.Activity.WorkClock))
                        engaged = false;
                    TownServiceActivityMotion.Engage(ref resident.Activity, engaged);
                }
                // A follower never decides which player deserves attention, including when
                // packets temporarily stop. Its last analytic transition simply completes.
                resident.Activity = TownServiceActivityMotion.Advance(resident.Activity, Time.unscaledDeltaTime);
            }
            TownActivityVisual visual = TownServiceActivityMotion.Visual(service, in resident.Activity);
            TownFacePose remotePose = remoteFace.At(service - 1);
            uint sourceEpoch = hasActivity ? remoteActivity.Epoch : IsFaceAuthor ? _faceEpoch : 0;
            resident.Handover.Sample(faceAuthor, sourceEpoch, Time.unscaledDeltaTime, in visual, in remotePose,
                out TownActivityVisual displayedActivity, out TownFacePose displayedFace);
            if (service == 1)
            {
                if (IsFaceAuthor)
                {
                    bool offering = interactive && (TownServiceMerchantHandoff.WantsOffering
                        || TownServiceMirror.RemoteMerchantOffering);
                    resident.MerchantOfferingBlend = Mathf.MoveTowards(resident.MerchantOfferingBlend,
                        offering ? 1f : 0f, Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds);
                }
                else if (hasActivity)
                    resident.MerchantOfferingBlend = remoteActivity.MerchantOfferingBlend;
                // A follower with no fresh activity packet holds the last authored
                // pose. It must never invent a new transition from local offer state.
                TownServiceActivityMotion.ApplyMerchantOffering(ref displayedActivity, resident.MerchantOfferingBlend);
                activities.MerchantOfferingBlend = resident.MerchantOfferingBlend;
            }
            else if (service == 2)
            {
                if (seedActivity)
                {
                    resident.TempleUnavailableBlend = activitySeed.TempleUnavailableBlend;
                    resident.TempleBlessingGeneration = activitySeed.TempleBlessingGeneration;
                    resident.TempleBlessingStartedClock = activitySeed.TempleBlessingStartedClock;
                }
                if (IsFaceAuthor)
                {
                    TownServiceMirror.TryTemplePresentationState(out bool received, out bool anyCanDonate);
                    TownServiceMirror.CollectTempleDonationStates(TempleDonationStates);
                    bool donationCommitted = false;
                    float transitionAge = float.PositiveInfinity;
                    int blessedVisitor = 0;
                    foreach (TownTempleDonationState state in TempleDonationStates)
                        if (resident.TempleBlessing.Observe(true, state.Peer, state.Session,
                            state.Known, state.Available, state.Revision, state.TransitionAge, state.HasCommitAge))
                        {
                            donationCommitted = true;
                            if (state.TransitionAge < transitionAge
                                || state.TransitionAge == transitionAge && (blessedVisitor == 0 || state.Peer < blessedVisitor))
                            { transitionAge = state.TransitionAge; blessedVisitor = state.Peer; }
                        }
                    if (donationCommitted)
                    {
                        // The native donation callback requests her gratitude. Availability also
                        // flips to false on the same update; treating that edge as a new refusal
                        // used to replace the gratitude with "you cannot donate again".
                        // Reserve the refusal for a later visit after she has returned to prayer.
                        resident.TempleUnavailableSpoken = true;
                        resident.TempleBlessingStartedAt = now - transitionAge;
                        resident.TempleBlessingStartedClock = _faceClock - transitionAge;
                        TownServiceFaceAttention.BlessVisitor(blessedVisitor, transitionAge);
                        // Face preparation happened before this commit was discovered.
                        // Replace that target now, so the first blessed frame already
                        // looks at the donor instead of an unrelated nearby visitor.
                        resident.Station.PrepareActivityAttention(resident.Activity.Engaged);
                        if (resident.TempleBlessingGeneration != uint.MaxValue) resident.TempleBlessingGeneration++;
                        if (VRLog.WantsDebug)
                            VRLog.Debug("TownServices", "Shared blessing accepted: visitor=" + blessedVisitor
                                + ", epoch=" + _faceEpoch + ", generation=" + resident.TempleBlessingGeneration
                                + ", age=" + transitionAge.ToString("F3"));
                        if (interactive) TownServiceVoice.RequestReaction(2, TownVoiceReaction.PriestessDonate);
                    }
                    // The private window may already be unavailable when first hydrated. Its
                    // revision is a baseline, not evidence that this viewer witnessed a donation.
                    // The unavailable pose belongs to the permanent resident, so removal of the
                    // temporary interaction record must release it over the same analytic transition
                    // as attention. Resetting this value to zero produced the recorded one-frame
                    // bowl-cover -> prayer snap every time the visitor walked away.
                    // All visitors see one bowl. It stays open while any visitor's character
                    // can donate; a different local character's purse remains individually
                    // disabled by the original native TempleEligible check in TownServiceRitual.
                    // Unknown fresh eligibility is treated as open until its owner publishes.
                    bool unavailable = interactive && received && !anyCanDonate;
                    float blessingAge = now - resident.TempleBlessingStartedAt;
                    // Build 596 Frame: the native donation completed and raised Prosperity,
                    // but the priestess's hand appeared to freeze over the bowl. The revision
                    // starts a 2.45 s gesture with motes lasting up to 4.20 s; availability
                    // flips on the same callback. Previously that flip moved the underlying
                    // pose to its permanent bowl cover *during* the gesture, so the gesture
                    // ended directly on a still hand and its fading motes. Finish the full
                    // authored visual first, then ease into the existing unavailable pose.
                    // Story/reward windows do not reset this owner clock or the shared pose.
                    bool blessingVisible = blessingAge >= 0f
                        && blessingAge < TownServiceActivityMotion.TempleBlessingVisualSeconds;
                    bool coverUnavailable = unavailable && !blessingVisible;
                    // If the first state seen on approach is already unavailable, attention must
                    // travel directly from prayer to the covered bowl. Ramping a second blend from
                    // zero made the first half of the entrance visibly pass through the available
                    // hands-down pose. A live availability change while she is already attending
                    // still uses the ordinary smooth transition, as does every departure.
                    // A private window can hydrate after attention has already become visible.
                    // Jumping its cover weight to one at that point produces a one-frame arm snap.
                    // Only choose the direct prayer-to-cover path before attention starts; a late
                    // baseline blends from the pose already on screen.
                    if (coverUnavailable && !resident.TempleAvailabilityObserved)
                    {
                        resident.TempleDirectCover = displayedActivity.Attention <= .05f;
                        resident.TempleHydratingCover = !resident.TempleDirectCover;
                    }
                    if (received) resident.TempleAvailabilityObserved = true;
                    if (coverUnavailable && resident.TempleDirectCover)
                        resident.TempleUnavailableBlend = 1f;
                    else if (coverUnavailable && resident.TempleHydratingCover)
                        resident.TempleUnavailableBlend = Mathf.MoveTowards(resident.TempleUnavailableBlend,
                            1f, Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds);
                    else if (coverUnavailable)
                    {
                        // Only a native committed donation has a shared author timestamp.
                        // Character selection, affordability and saved state can change
                        // availability without a donation revision. Those changes must
                        // ease from the visible pose rather than treating an absent age
                        // as infinity and snapping the arm onto the bowl in one frame.
                        float committedAge = now - resident.TempleBlessingStartedAt;
                        resident.TempleUnavailableBlend = committedAge >= 0f
                            && committedAge <= TownServiceActivityMotion.TransitionSeconds + .15f
                                ? Mathf.Clamp01(committedAge / TownServiceActivityMotion.TransitionSeconds)
                                : Mathf.MoveTowards(resident.TempleUnavailableBlend, 1f,
                                    Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds);
                    }
                    else if (received || displayedActivity.Attention <= .001f)
                    {
                        resident.TempleDirectCover = false;
                        resident.TempleHydratingCover = false;
                        resident.TempleUnavailableBlend = Mathf.MoveTowards(resident.TempleUnavailableBlend,
                            0f,
                            Time.unscaledDeltaTime / TownServiceActivityMotion.TransitionSeconds);
                    }
                    // A disappearing private manifest is ambiguous while attention is still
                    // returning: it can be an ordinary departure or a one-frame native window
                    // rebuild. Keep the last cover contribution and let the already analytic
                    // attention fade carry it to prayer. At neutral, the branch above clears it
                    // before any later available visit can start.
                    if (displayedActivity.Attention < .10f || !interactive)
                        resident.TempleUnavailableSpoken = false;
                    else if (unavailable && IsFaceAuthor && displayedActivity.Attention >= .35f
                        && !resident.TempleUnavailableSpoken)
                    {
                        resident.TempleUnavailableSpoken = true;
                        TownServiceVoice.RequestReaction(2, TownVoiceReaction.PriestessUnavailable);
                    }
                    if (displayedActivity.Attention <= .001f)
                        resident.TempleAvailabilityObserved = false;
                }
                else if (hasActivity && remoteActivity.HasSharedPerformance)
                {
                    resident.TempleUnavailableBlend = remoteActivity.TempleUnavailableBlend;
                    resident.TempleBlessingGeneration = remoteActivity.TempleBlessingGeneration;
                    resident.TempleBlessingStartedClock = remoteActivity.TempleBlessingStartedClock;
                }
                float sharedBlessingAge = resident.TempleBlessingGeneration == 0
                    ? float.PositiveInfinity : _faceClock - resident.TempleBlessingStartedClock;
                resident.TempleBlessingStartedAt = now - sharedBlessingAge;
                TownServiceActivityMotion.ApplyTempleAvailability(ref displayedActivity,
                    resident.TempleUnavailableBlend <= 0f, resident.TempleUnavailableBlend);
                if (sharedInteractive)
                    TownServiceActivityMotion.ApplyTempleBlessing(ref displayedActivity, sharedBlessingAge);
                TownServiceActivityMotion.ApplyTempleBreath(ref displayedActivity, _faceClock);
                resident.Station.SampleTempleBlessing(sourceEpoch, resident.TempleBlessingGeneration,
                    sharedBlessingAge, sharedInteractive);
                activities.TempleUnavailableBlend = resident.TempleUnavailableBlend;
                activities.TempleBlessingGeneration = resident.TempleBlessingGeneration;
                activities.TempleBlessingStartedClock = resident.TempleBlessingStartedClock;
            }
            resident.Station.SampleActivity(in displayedActivity);
            resident.Station.SampleActivityAudio(faceAuthor, sourceEpoch, resident.Activity.WorkClock,
                sharedInteractive && used && ready && resident.Visibility >= .99f, in displayedActivity,
                lookingAtVisitor, IsFaceAuthor, _faceClock,
                service == 1 ? remoteActivity.MerchantFoley
                    : service == 3 ? remoteActivity.EnchantressFoley : default);
            if (service == 1) activities.MerchantFoley = resident.Station.PublishedFoley;
            else if (service == 3) activities.EnchantressFoley = resident.Station.PublishedFoley;
            activities.Set(service - 1, resident.Activity);
            remotePose = displayedFace;
            if (seedAuthority) resident.Station.SeedFace(seed.At(service - 1), seedAuthor, seedElapsed);
            TownFacePose shownFace = resident.Station.SampleFace(IsFaceAuthor, hasFace, faceAuthor, in remotePose, faceElapsed, _faceClock);
            resident.Handover.RecordFace(in shownFace);
            faces.Set(service - 1, shownFace);
            // An opted-out observer still needs the compatibility occlusion while
            // watching a remote visit. An enabled client at story commitment does not:
            // its resident visit is deliberately withdrawn with every real offering.
            resident.Visit.Tick((interactive || !enabled) && used && ready && resident.Visibility >= .99f);
            resident.Occupation.Tick(!StoryComposite.PointOfNoReturn && used && ready && resident.Visibility >= .99f);
            Transform station = resident.Station.Root;
            published.Set(service - 1, new TownResidentPose {
                Pose = new RigPose { Position = _frame!.transform.InverseTransformPoint(station.position),
                    Rotation = Quaternion.Inverse(_frame.transform.rotation) * station.rotation },
                Scale = station.lossyScale.x / _frame.transform.lossyScale.x,
                Age = resident.Age, Clip = resident.Clip,
                ActorFloorOffset = resident.Station.ActorFloorOffset, FurnitureBottom = resident.Station.FurnitureBottom,
                Visibility = (byte)Mathf.RoundToInt(resident.Visibility * 255f) });
            if (!used && resident.Visibility <= 0f)
            { NativeTemplates.InvalidateResident(service); resident.Occupation.Dispose(); resident.Visit.Dispose(); resident.Station.Dispose(); Residents.Remove(service); }
        }
        if (missing && retry) _retryAt = now + 2f;
        if (IsFaceAuthor) TownServiceLighting.SampleEnvironment(_frame!.transform, ref activities);
        else if (hasActivity) TownServiceLighting.ApplyEnvironment(_frame!.transform, in remoteActivity);
        TownActivityState surfaceLighting = IsFaceAuthor ? activities : remoteActivity;
        foreach (Resident resident in Residents.Values) resident.Station.BindEnvironment(_frame!.transform, in surfaceLighting);
        // Keep TLV79's historical cloth tail readable, but no longer author it.
        published.HasCloth = false;
        Published = published;
        faces.Active = published.Active;
        PublishedFaces = faces; activities.Active = published.Active; PublishedActivities = activities;
        TownServiceSharedCue.Tick();
        TownServiceVisitTarget.TickLaser();
    }

    internal static void Reset()
    {
        Published = default; PublishedFaces = default; PublishedActivities = default; IsFaceAuthor = false;
        TownServiceSharedCue.Reset();
        TownServiceFaceAttention.ResetBlessingFocus();
        PerformanceEpoch = 0;
        _faceClock = 0f; _lastRemoteFaceTime = float.NegativeInfinity;
        if (_frame == null && Residents.Count == 0) return;
        TownServiceFaceSpeech.ResetObserver?.Invoke();
        RemoteTownFaces.Reset(); RemoteTownActivities.Reset();
        TownServiceConfirmationMask.Clear();
        TownServiceSync.Shutdown();
        foreach (Resident resident in Residents.Values)
        { resident.Occupation.Dispose(); resident.Visit.Dispose(); resident.Station.Dispose(); }
        Residents.Clear();
        if (_frame != null) Object.Destroy(_frame);
        _frame = null; _retryAt = 0f;
    }
}

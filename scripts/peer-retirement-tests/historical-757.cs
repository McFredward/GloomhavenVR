// Source-bound old behavior at dev cd827d688 (ModBuild757).
// Compiling defect controls only; never linked by the production project.
    private void ForgetPeer(int playerId)
    {
        if (_transport is FfsNetTransport ffs)
            ffs.ForgetPeer(playerId);
        if (_avatars.TryGetValue(playerId, out RemoteAvatar avatar))
        {
            WorldUI.Surfaces.DamageDecisionPreview.ResetRemote(avatar);
            CharacterDecisionPresentation.Remove(avatar);
            avatar.Destroy();
            _avatars.Remove(playerId);
        }
        _pending.Remove(playerId);             // nothing queued for a peer we no longer hold
        _pendingExtras.Remove(playerId);
        _pendingAnimations.Remove(playerId);
        ForgetNativePresentation(playerId);
        _createRetryAt.Remove(playerId);       // a rejoin gets a fresh construction budget
        NetFigures.ReleaseRemote(playerId);    // drop any figure this peer was holding
        NetProps.ReleaseRemote(playerId);      // …and put any map item they carried back on its hex
        NetPlayerActors.ForgetAvatarFetch(playerId); // …and a rejoin gets a fresh fetch budget
        Board.CharacterFocus.ForgetPeer(playerId);   // …and their focus outline goes with them
        _peerEnv.Remove(playerId);                   // …and they stop being a clock/host candidate
        RemoteTestTriggers.ForgetPeer(playerId);     // …and any debug override they owned is released
        PeerCardFaceCensus.ReportPeerGone(playerId); // …and their card-face rows stop being reported
    }
    private void DestroyAllAvatars()
    {
        _decisionWait.Tick(0, 0, 0f);
        _delayedDecisionPlayer = 0;
        _nextDecisionWait = 0f;
        _pendingAnimations.Clear();
        ResetNativePresentation();
        _lastAnimationSnapshot = null;
        _lastSentAnimations = null;
        _nextAnimationRefresh = 0;
        _lastAnimationSourceFrameTime = 0;
        if (_transport is FfsNetTransport ffs)
            ffs.ResetFragments();
        // THE IDS FIRST, because ForgetPeer removes from _avatars and a dictionary cannot be
        // written while it is being enumerated. _scratchIds is free here: the only other user is
        // TickAvatars, which has finished with it before any teardown path can run.
        _scratchIds.Clear();
        foreach (KeyValuePair<int, RemoteAvatar> kv in _avatars)
            _scratchIds.Add(kv.Key);
        for (int i = 0; i < _scratchIds.Count; i++)
            ForgetPeer(_scratchIds[i]);
        _avatars.Clear();   // belt: ForgetPeer emptied it entry by entry
    }
    private void OnDisable()
    {
        Unsubscribe();
        _pending.Clear();
        _pendingExtras.Clear();
        _pendingAnimations.Clear();
        ResetNativePresentation();
        _lastAnimationSnapshot = null;
        _lastSentAnimations = null;
        _nextAnimationRefresh = 0;
        _lastAnimationSourceFrameTime = 0;
        _createRetryAt.Clear();
        _rxRejectLogged.Clear();   // a new session re-reports a peer it cannot parse
        _rxRejected = 0;
        _sendGateState = -1; // next session logs its join window from scratch
        NetCardFx.Reset(); // never carry a queued card animation into the next session
        _hasFx = false;
        _lastSentBoardUi = -1;      // next session re-states the board UI from scratch
        _lastSentPileCounts = int.MinValue;   // and re-states the pile counts…
        _lastSentHalfHover = int.MinValue;    // …the half hover…
        _lastSentHalfSelect = int.MinValue;   // …the clicked halves…
        _lastSentSlotOrder = int.MinValue;    // …the round-card slot order (record 18)…
        _lastSentEmptyFanHint = false;        // …the empty-fan placard (record 14 bit 4)…
        _tuningPager.Reset();                 // …and our own board tuning (record 28)…
        _lastSentTrackHoverActor = int.MinValue; // …and the track hover from scratch
        _lastSentWallFadeCount = -1;             // …and the synced wall-fade set
        _lastSentFocusActor = int.MinValue;       // …and the character focus (record 22)
        _lastSentFocusAttentionActor = 0;         // …including who the game was waiting on
        _lastSentTrackSelectionCount = -1;        // …and the track's selection frames (record 23)
        _lastSentTrackOrderCount = -1;            // …and the track's per-viewer player order (27)
        _lastSentTrackOrderOwned = 0;
        Board.CharacterFocus.Reset();             // …including every peer's synced focus
        _lastSentDecisionLines = null; // next session re-states the docked decision row afresh
        _lastSentDecisionState = -1;   // …including its option states + prompt-text variant
        _lastSentDecisionWidgets = -1; // …and the widget roles + damage numbers (record 29)
        _lastSentUseBarMask = -1;      // …and the use-bar drawer below it (record 25)
        _lastSentConfirmLabel = null;  // and the live cap labels
        _lastSentSkipLabel = null;
        _lastSentUndoLabel = null;
        _lastSentItemUseLabel = null;
        RemoteStorySync.Reset();       // …and never carries a story page/pose into a new session
        RemoteMapRoom.Reset();         // …nor a peer's map-room placard (which owns a GameObject)
        RemoteMapStory.Reset();
        MapStoryLifecycle.Reset();        // …nor a map story page or a shared window pose
        RemoteVideoPlayback.Reset();  // …nor a cosmetic movie decoder or prior playback identity
        _lastSentCapPress = -1;        // …and never replays a stale keycap press into a new session
        Cards.BoardCapPress.Clear();   // …including the latch it is diffed against
        _fanPresentationSent.Reset();  // new peers need a complete fan presentation sample
        _sentSecondFigureValid = false; // nor a new session's second held figure
        _lastSentSecondActorId = 0;
        _sentSecondCardValid = false;   // nor its second held card
        _lastSentCardGripMask = 0;      // nor which of them was held rigidly (record 34)
        _loggedCardGrip = false;
        _lastSentStretchPrimary = NetProtocol.HeldStretchCodeNeutral;   // nor a stale stretch
        _lastSentStretchSecondary = NetProtocol.HeldStretchCodeNeutral; // (record 30)
        _loggedStretch = false;
        _sentHeldPropValid = false;     // nor a new session's held map items (record 37)
        _sentSecondPropValid = false;
        _lastSentHeldPropId = 0;
        _lastSentSecondPropId = 0;
        _lastSentHeldPropSize = NetProtocol.HeldStretchCodeNeutral;
        _lastSentSecondPropSize = NetProtocol.HeldStretchCodeNeutral;
        _loggedHeldProp = false;
        // …and every prop a peer was carrying goes back on its hex. NOT optional and NOT symmetric
        // with the figure teardown one line up: a released figure is re-authored by the game's own
        // Update, a released prop is re-authored by NOTHING, so a driver teardown mid-hold would
        // strand a chest in mid-air for the rest of the scenario.
        NetProps.Clear();
        // Version handshake is session state; badges are reversible game-UI decoration — both
        // must not survive a driver teardown (hot reload / module shutdown).
        VersionGuard.Reset();
        PlayerBadges.RestoreAll();
        _flatApplied = false;
        DestroyAllAvatars();
    }

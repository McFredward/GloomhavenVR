using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// SENDER-side sample of the LOCAL initiative-track hover — the source of extras extension
/// record <see cref="NetProtocol.ExtIdTrackHover"/> (user defect 2026-08-04: "Die Mouseover
/// der Initiativreihenfolge sind nicht synchronisiert").
///
/// WHAT COUNTS AS "HOVERED": exactly what the game itself considers hovered. Every pointer
/// path — the VR laser delivering uGUI events to the converted track surface, a fingertip
/// poke, a desktop mouse — funnels through <c>InitiativeTrackActorAvatar.OnPointerEnter/Exit
/// → SetHilighted</c>, which latches the avatar's own <c>highlighted</c> field. That field is
/// therefore polled here (publicized assembly — no reflection), so the wire state IS the
/// game's hover state by construction and no input path can be missed.
///
/// WHY THE STABLE ACTOR ID and not the entry's display index: the track's on-screen order is
/// PER-CLIENT during the online selection phase (vanilla's
/// <c>InitiativeTrackActorBehaviour.CompareTo</c> sorts by <c>IsUnderMyControl</c> there), so
/// an index would lift the wrong portrait on the other side. The id is the shared ActorGuid
/// hash (<c>NetFigures.StableActorId</c>) — the same cross-client id space the held-figure
/// records ride; the per-class <c>CActor.ID</c> would collide across enemy classes.
///
/// THE POPUP FLAG: for an ENEMY entry, hovering opens the round-action preview
/// (<c>MonsterBaseUI.TogglePreview</c> — the "info popup" of the user report). Its OPEN state
/// is read off the live widget (<c>monsterBaseUI.gameObject.activeSelf</c>), never inferred,
/// so phase gates the game applies (no preview before the monster cards phase) are honoured
/// for free. Player entries have no popup in VR (the full-card preview is deliberately
/// blocked — <c>WorldUI.Patches.InitiativeHoverCardBlock</c>), so the flag is simply false
/// for them. PUBLIC INFO throughout: the track and the monster preview are scenario-wide
/// widgets every client already renders identically; no card identity is read or sent.
///
/// <para>SECOND SAMPLE, SAME WIDGET: <see cref="SampleSelectedActorIds"/> reads the track's
/// vanilla SELECTION FRAMES (extras record <see cref="NetProtocol.ExtIdTrackSelection"/>). It
/// lives here because it is the other half of one job — "what is per-VIEWER about this client's
/// initiative track", the state a mirrored track must never copy off the observer's own widget.
/// Everything else the track shows (the reorder slide, the portraits, the discs, grayscale, the
/// extra-turn and damage-warning animations, the at-turn ordering) is GLOBAL or host-replicated
/// and needs no wire at all.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs wire bytes (extras extension records 16, 5 B while
/// hovering, and 23, 1 + 4·n B while a selection frame stands). Sends PUBLIC track entries' stable
/// ids and one boolean; no popup CONTENT and no card identity ever ride the wire (receivers show
/// their own copy of the public widget). See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal static class InitiativeHoverSampler
{
    /// <summary>
    /// The initiative-track entry the local pointer is on: its stable actor id
    /// (<c>NetFigures.StableActorId</c> — the ActorGuid hash) and
    /// whether its info popup is open. False while the track does not exist, is hidden, or no
    /// entry is highlighted. Wrapped whole — a half-built track (scene teardown, mid-round
    /// rebuild) must read as "no hover", never throw inside the extras sender.
    /// </summary>
    internal static bool TrySample(out int actorId, out bool popupOpen)
    {
        actorId = 0;
        popupOpen = false;
        try
        {
            InitiativeTrack track = InitiativeTrack.Instance;
            if (track == null || !track.gameObject.activeInHierarchy)
                return false;
            System.Collections.Generic.List<InitiativeTrackActorBehaviour> ui = track.actorsUI;
            if (ui == null)
                return false;

            for (int i = 0; i < ui.Count; i++)
            {
                InitiativeTrackActorBehaviour beh = ui[i];
                if (beh == null || !beh.gameObject.activeSelf || beh.Actor == null)
                    continue;
                InitiativeTrackActorAvatar avatar = beh.Avatar;
                // `highlighted` is the exact latch SetHilighted writes on pointer enter/exit —
                // the game's own answer to "is this entry hovered" (publicized private).
                if (avatar == null || !avatar.highlighted)
                    continue;
                int id = NetFigures.StableActorId(beh.Actor);
                if (id == 0)
                    continue; // 0 is "none" everywhere in this system — not expressible
                actorId = id;
                // The popup: the live enemy preview widget's own active flag. An entry whose
                // preview the game refused to open (wrong phase, no card yet) reads closed.
                popupOpen = beh is InitiativeTrackEnemyBehaviour enemy
                            && enemy.monsterBaseUI != null
                            && enemy.monsterBaseUI.gameObject.activeSelf;
                return true; // one pointer, one hover — first highlighted entry wins
            }
        }
        catch
        {
            // Degrade to "no hover": a torn-down singleton mid-read is a frame-order artefact,
            // not an error worth a log line at 15 Hz.
        }
        return false;
    }

    /// <summary>
    /// THE SELECTION FRAMES the local track is showing right now — extras record
    /// <see cref="NetProtocol.ExtIdTrackSelection"/>. Fills <paramref name="into"/> with the stable
    /// actor ids of every entry whose vanilla <c>selectionObject</c> is genuinely ON SCREEN, and
    /// returns how many were written (0 = no frame anywhere, ⇒ no record).
    ///
    /// <para>WHY THE WIDGET AND NOT THE MODEL. Every alternative source re-DERIVES the frame:
    /// <c>InitiativeTrack.SelectedActor()</c> is the track's own bookkeeping and is not the same
    /// thing as a visible frame (<c>ToggleSelection</c> refuses to show one for an actor
    /// <c>IsTakingExtraTurn</c>, InitiativeTrackActorAvatar.cs:128-135), and
    /// <c>Choreographer.CurrentActor</c> is only the auto-select half of the story. Reading
    /// <c>selectionObject.activeInHierarchy</c> is the pixel itself: whatever vanilla decided, for
    /// whatever reason, present or future, is what goes on the wire — which is exactly the user's
    /// ruling ("die highlights … so wie der Spieler sie sieht").</para>
    ///
    /// <para>WHY A LIST: <c>InitiativeTrack.Select</c> skips the deselect while the incoming actor
    /// is taking an extra turn (InitiativeTrack.cs:340), so two frames can stand at once. Capped at
    /// the caller's buffer AND <see cref="NetProtocol.TrackSelectionMaxIds"/>.</para>
    ///
    /// <para>PLAYERS, ENEMIES AND OBJECTS ALIKE — the frame is a TRACK fact and the ids ride the
    /// same <c>NetFigures.StableActorId</c> space record 16 already uses, which is what lets a
    /// peer's mirrored track finally frame a selected ENEMY (the gap record 22 structurally could
    /// not express, because a character focus cannot name a monster).</para>
    ///
    /// <para>Wrapped whole: a half-built track must read as "no frame", never throw inside the
    /// extras sender.</para>
    /// </summary>
    internal static int SampleSelectedActorIds(int[] into)
    {
        if (into == null || into.Length == 0)
            return 0;
        int n = 0;
        try
        {
            InitiativeTrack track = InitiativeTrack.Instance;
            if (track == null || !track.gameObject.activeInHierarchy)
                return 0;
            System.Collections.Generic.List<InitiativeTrackActorBehaviour> ui = track.actorsUI;
            if (ui == null)
                return 0;

            int cap = into.Length < NetProtocol.TrackSelectionMaxIds
                ? into.Length
                : NetProtocol.TrackSelectionMaxIds;
            for (int i = 0; i < ui.Count && n < cap; i++)
            {
                InitiativeTrackActorBehaviour beh = ui[i];
                if (beh == null || !beh.gameObject.activeSelf || beh.Actor == null)
                    continue;
                InitiativeTrackActorAvatar avatar = beh.Avatar;
                UnityEngine.GameObject? frame = avatar != null ? avatar.selectionObject : null;
                if (frame == null || !frame.activeInHierarchy)
                    continue;
                int id = NetFigures.StableActorId(beh.Actor);
                if (id == 0)
                    continue; // 0 is "none" everywhere in this system — not expressible
                into[n++] = id;
            }
        }
        catch
        {
            // Degrade to "no frame": a torn-down singleton mid-read is a frame-order artefact.
            return 0;
        }
        return n;
    }

    /// <summary>
    /// THE ON-SCREEN ORDER OF THIS CLIENT'S PLAYER TRACK ENTRIES, plus which of them this client
    /// controls — extras record <see cref="NetProtocol.ExtIdTrackOrder"/>. Fills
    /// <paramref name="into"/> with the stable actor ids of the PLAYER entries in display order and
    /// returns how many were written; <paramref name="ownedMask"/> gets bit k set when
    /// <c>into[k]</c> is a character this client controls. Returns 0 (⇒ no record) outside the
    /// window in which the order is per-viewer at all.
    ///
    /// <para>THE WINDOW IS VANILLA'S OWN, not a guess: <c>FFSNetwork.IsOnline</c> and
    /// <c>PhaseManager.PhaseType == SelectAbilityCardsOrLongRest</c> are literally the first two
    /// conjuncts of the branch in <c>InitiativeTrackActorBehaviour.CompareTo</c> (decompiled
    /// GH.Runtime/InitiativeTrackActorBehaviour.cs:160) that makes two player entries sort by
    /// <c>IsUnderMyControl</c>. Outside it every client's <c>CompareTo</c> reduces to the same
    /// <c>GetOrderPriority</c>/<c>SubInitiative</c> comparison over the same replicated model, so
    /// the order is global and there is nothing to send — which is what keeps an idle packet
    /// byte-identical to the previous build's.</para>
    ///
    /// <para>PLAYER ENTRIES ONLY. The branch requires <c>actor.IsPlayerByDefault() &amp;&amp;
    /// other.IsPlayerByDefault()</c>, so an ENEMY entry is never compared by ownership: the enemy
    /// block is identical on every client and paying four bytes per monster row to say so would be
    /// waste. <c>InitiativeTrackPlayerBehaviour</c> is the type test, so an exhausted hero — which
    /// vanilla appends to the very same track — is included exactly as vanilla includes it.</para>
    ///
    /// <para>DISPLAY ORDER = ASCENDING SIBLING INDEX under the track holder, which is where
    /// vanilla's <c>UpdateSortingOrder</c> writes it (<c>actorsUI.Sort()</c> then
    /// <c>SetAsFirstSibling()</c> per entry). It is the same convention
    /// <c>RemoteInitiativeTrack.CollectFromGameTrack</c> already reads the track in, and the
    /// receiver re-reads its own row the same way — so the two sides are comparing the same thing
    /// and the record is a pure permutation, not a coordinate.</para>
    ///
    /// <para>Wrapped whole: a half-built track must read as "no order", never throw inside the
    /// extras sender.</para>
    /// </summary>
    internal static int SampleTrackOrder(int[] into, out byte ownedMask)
    {
        ownedMask = 0;
        if (into == null || into.Length == 0)
            return 0;
        int n = 0;
        try
        {
            // Vanilla's own divergence window, verbatim (CompareTo:160). Outside it the order is
            // global on every client and the record must not exist.
            if (!FFSNetwork.IsOnline
                || PhaseManager.PhaseType != CPhase.PhaseType.SelectAbilityCardsOrLongRest)
                return 0;

            InitiativeTrack track = InitiativeTrack.Instance;
            if (track == null || !track.gameObject.activeInHierarchy)
                return 0;
            System.Collections.Generic.List<InitiativeTrackActorBehaviour> ui = track.actorsUI;
            if (ui == null)
                return 0;

            int cap = into.Length < NetProtocol.TrackOrderMaxIds
                ? into.Length
                : NetProtocol.TrackOrderMaxIds;

            // Selection sort over the live list by sibling index — the display order — without
            // allocating a sorted copy: this runs on the extras cadence with at most a handful of
            // player rows, and the extras sender allocates nothing anywhere else either.
            int taken = 0;
            while (n < cap)
            {
                int bestIdx = -1;
                int bestSibling = int.MaxValue;
                for (int i = 0; i < ui.Count; i++)
                {
                    InitiativeTrackActorBehaviour beh = ui[i];
                    if (beh == null || !beh.gameObject.activeSelf || beh.Actor == null)
                        continue;
                    if (beh is not InitiativeTrackPlayerBehaviour)
                        continue; // the enemy block never permutes — see the doc
                    if (i >= 32 || (taken & (1 << i)) != 0)
                        continue; // the visited set is an int mask — 32 rows is far past any track
                    int sibling = beh.transform.GetSiblingIndex();
                    if (sibling < bestSibling)
                    {
                        bestSibling = sibling;
                        bestIdx = i;
                    }
                }
                if (bestIdx < 0)
                    break;
                taken |= 1 << bestIdx;
                CActor actor = ui[bestIdx].Actor;
                int id = NetFigures.StableActorId(actor);
                if (id == 0)
                    continue; // 0 is "none" everywhere in this system — not expressible
                if (actor.IsUnderMyControl)
                    ownedMask |= (byte)(1 << n);
                into[n++] = id;
            }
        }
        catch
        {
            // Degrade to "no order": a torn-down singleton mid-read is a frame-order artefact,
            // and the receiver's fallback for "no record" is the mirrored arrangement it already
            // shows — never a half-applied permutation.
            ownedMask = 0;
            return 0;
        }
        return n;
    }
}

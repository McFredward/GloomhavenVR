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
/// an index would lift the wrong portrait on the other side. <c>CActor.ID</c> is the same
/// cross-client id space the held-figure records already ride.
///
/// THE POPUP FLAG: for an ENEMY entry, hovering opens the round-action preview
/// (<c>MonsterBaseUI.TogglePreview</c> — the "info popup" of the user report). Its OPEN state
/// is read off the live widget (<c>monsterBaseUI.gameObject.activeSelf</c>), never inferred,
/// so phase gates the game applies (no preview before the monster cards phase) are honoured
/// for free. Player entries have no popup in VR (the full-card preview is deliberately
/// blocked — <c>WorldUI.Patches.InitiativeHoverCardBlock</c>), so the flag is simply false
/// for them. PUBLIC INFO throughout: the track and the monster preview are scenario-wide
/// widgets every client already renders identically; no card identity is read or sent.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs wire bytes (extras extension record 16, 5 B,
/// only while hovering). Sends a public track entry's stable id + one boolean; the popup
/// CONTENT never rides the wire (receivers show their own copy of the public widget). See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class InitiativeHoverSampler
{
    /// <summary>
    /// The initiative-track entry the local pointer is on: its stable <c>CActor.ID</c> and
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
                int id = beh.Actor.ID;
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
}

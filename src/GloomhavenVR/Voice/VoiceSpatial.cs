using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using VoiceChat;

namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE SPATIAL — the teammate's voice comes out of the teammate's mask. The game already carries
//  the voice; this file moves the sound to the body, in PERCEIVED metres, and tells the name tag
//  who is talking.
// =================================================================================================

/// <summary>
/// Binds each remote voice user to that player's <see cref="RemoteAvatar"/> and drives the peer's
/// <c>AudioSource</c> — its position, its spatial blend and its rolloff — so the voice arrives from
/// the mask instead of from the world origin.
///
/// <para><b>USER REQUEST (2026-08-24, verbatim).</b> "Bitte gehe den ingame Voice-Chat als erstes
/// an. Wie du bereits erwähnt hast soll er räumlich sein, also die Stimme kommt von der jeweiligen
/// Maske. Optional möchte ich auch, dass wenn jemand spricht das entsprechend im Stem-Logo sichtbar
/// ist (deaktivierbar). zB mit einem Lautsprechersymbol in einer Ecke das ausschlägt bei Ton."</para>
///
/// <para><b>AND HIS OWN CORRECTION, same round:</b> "Steam-Logo über dem Kopf; sorry typo". So the
/// indicator is not on the mask and not in a corner of the view — it is a corner of the STEAM
/// AVATAR QUAD the mod already floats above every peer's head
/// (<c>Net/RemoteNameTag.cs</c>). It is drawn by <see cref="VoiceBadge"/>; this file supplies the
/// state and nothing else.</para>
///
/// =============================================================================================
/// <para><b>THE MOD DOES NOT SHIP A VOICE CHAT. THE GAME HAS ONE.</b> Everything about the
/// transport, the connection, the privilege check, the per-user mute and the options page belongs
/// to <c>GH.Runtime/VoiceChat/</c> over Photon, and none of it is touched. See
/// <see cref="VoiceChatBridge"/> for the seam, for why NO Harmony patch was needed, and for where
/// the "may not patch" line was drawn.</para>
///
/// <para><b>WHAT THIS FILE WRITES, EXHAUSTIVELY.</b> Per remote speaker: <c>transform.position</c>,
/// <c>spatialBlend</c>, <c>rolloffMode</c> + the custom curve, <c>minDistance</c>,
/// <c>maxDistance</c>, <c>spread</c>, <c>dopplerLevel</c>, <c>bypassReverbZones</c>,
/// <c>priority</c>. Every one of them is saved before the first write and restored on stand-down.
/// <b>It never writes <c>AudioSource.volume</c>, never calls <c>Mute</c>/<c>UnMute</c>, never
/// touches <c>Speaker.StartPlayback</c>/<c>StopPlayback</c>, and never writes any game state.</b>
/// That is not squeamishness, it is the reason the flat game keeps working: <c>volume</c> IS the
/// game's per-user volume slider (<c>ConnectedUserVoice.Volume</c>), and playback state IS the
/// game's per-user mute, re-asserted every update by
/// <c>VoiceChatUserBlockerController.UpdateMuteStates</c>. A player who mutes someone, or turns
/// voice off in the game's own menu, or presses <c>ToggleVoiceChatControl</c>, gets exactly what
/// they got before this feature existed — because this feature does not participate in any of it.
/// </para>
///
/// =============================================================================================
/// <para><b>THE MAPPING, WHICH DECIDES WHETHER THE FEATURE CAN BE CORRECT AT ALL.</b> The voice
/// side hands us <c>(voiceChatUserId, userName, platformAccountID, platformName, isHost)</c>. The
/// mod's avatars are keyed by <c>NetworkPlayer.PlayerID</c>. The join is NOT guessed and it is NOT
/// "the only other player in the room" (right for two players, silently wrong for three):</para>
/// <code>
///     ConnectedUserVoice.PlatformAccountID
///        == NetworkPlayer.PlatformNetworkAccountPlayerID     &lt;-- the game's own join, verbatim
///        -&gt; NetworkPlayer.PlayerID
///        -&gt; NetAvatarDriver._avatars[playerId] -&gt; RemoteAvatar.HeadHolder
/// </code>
/// <para>That string equality is what the SHIPPED GAME does, in two places, to decide which
/// portrait and which name belong to a voice row —
/// <c>PlayerPortraitVoiceComponent.cs:41-45</c> and <c>PlayerNameVoiceComponent.cs:146-150</c> are
/// both literally <c>PlayerRegistry.AllPlayers.FirstOrDefault(x =&gt;
/// x.PlatformNetworkAccountPlayerID == accountId)</c>. Both sides are filled from the same
/// <c>PlatformLayer.UserData.PlatformNetworkAccountPlayerID</c> (voice: BoltVoiceBridge.cs:172;
/// player: NetworkPlayer.cs:146). Implementation and the SteamID64 trap next door:
/// <see cref="NetPlayerActors.PlayerIdForNetworkAccount"/>.</para>
///
/// <para><b>WHAT A MISMATCH LOOKS LIKE AND WHAT THE LOG SAYS.</b> A voice user we cannot resolve
/// keeps <c>spatialBlend = 0</c> — it stays audible exactly as it is today, from everywhere, and is
/// never silenced. One line per unmatched user names the account id, the display name, and every
/// candidate it compared against:</para>
/// <code>
///     VOICE SPATIAL: no network player for voice user 'Bob' (account 76561…, id 3) —
///     roster was [1: 4711 'Alice', 2: 815 'Carol']. Their voice stays NON-spatial (2D),
///     which is exactly vanilla behaviour; it is not muted.
/// </code>
/// <para>A voice bound to the WRONG player would instead announce itself on the BOUND line — "voice
/// user 'Bob' bound to player 2" — beside a name tag that says Carol. That is the one failure this
/// design cannot detect by itself, because the game's own UI would be wrong in exactly the same way
/// for exactly the same reason.</para>
///
/// =============================================================================================
/// <para><b>DISTANCES ARE PERCEIVED METRES, SCALED BY rigScale — the convention
/// <c>Core/EnvSound.cs:128-155</c> established and this file follows rather than reinventing.</b>
/// <c>rigScale = RigRoot.lossyScale.x</c> is world units per perceived metre and runs ~13 to ~26 in
/// a scenario and higher on the map table, so a curve authored in metres and typed straight into
/// <c>AudioSource.maxDistance</c> would be wrong by that factor and a teammate an arm's length away
/// would be silent. The conversion happens in exactly one place (<see cref="ApplyScale"/>) and is
/// re-applied whenever the player zooms, on the same 0.1 % tolerance EnvSound uses. The curve
/// itself is <see cref="VoiceCurve"/>, which states its decibels and is gated by the wire tests.
/// </para>
///
/// <para><b>dopplerLevel IS ZERO</b>, for EnvSound's reason exactly: the sources are static but the
/// LISTENER is not, and at rigScale ~22 a comfortable head movement is ~22 world units per second,
/// which Unity would hear as a supersonic listener and pitch-shift a teammate's voice accordingly.
/// This is not a stylistic choice.</para>
///
/// <para><b>NOTHING HERE READS THE HEAD POSE TO PLACE THE SOUND.</b> The source is anchored at the
/// peer's mask in world space and the <c>AudioListener</c> does the panning — the same ruling the
/// window debris was held to. The head camera is touched for exactly one thing, in
/// <see cref="Core.HeadEar"/>: it is where the ear is MOUNTED. Placement never consults it.</para>
///
/// <para><b>AND THE EAR HAD TO BE HOISTED OUT OF EnvSound TO MAKE THIS TRUE.</b> Until now the only
/// code that put an <c>AudioListener</c> on the head was the environment ambience, which owns it
/// only while an environment is standing with environment sounds ON. With them off the enabled
/// listener is the game's PARKED camera, many world units away, and every number above would be
/// computed against the wrong point. See <see cref="Core.HeadEar"/> — that file is the fix and the
/// defect report.</para>
///
/// =============================================================================================
/// <para><b>THE FOUR LIFECYCLE CASES THE BRIEF ASKED ABOUT, and what each does.</b></para>
/// <list type="bullet">
/// <item><b>A peer joins mid-session.</b> Nothing special: the voice list is POLLED, so a new entry
/// binds on the next tick. Events were rejected on purpose — see
/// <see cref="VoiceChatBridge.TryGetVoices"/> for the trap in <c>EventUserDisconnected</c>.</item>
/// <item><b>A peer's voice arrives before their avatar exists.</b> Normal, and it is the common
/// case: the voice room is joined long before the peer sends their first VR rig packet, and a
/// non-VR peer never sends one at all. The binding retries on a cadence and the voice stays 2D in
/// the meantime — audible, just not placed. THE FEATURE NEVER TRADES AUDIBILITY FOR
/// POSITION.</item>
/// <item><b>A peer leaves while talking.</b> Their entry disappears from <c>PlayerVoices</c>, we
/// restore their <c>AudioSource</c> and forget them. Note the game POOLS and RECYCLES speaker
/// GameObjects across users (BoltVoiceBridge.cs:199-208), which is exactly why the restore has to
/// happen and why the per-user cache is keyed on the <c>ConnectedUserVoice</c> INSTANCE.</item>
/// <item><b>The local player's own voice.</b> <c>PlayerVoices</c> only ever holds INCOMING users so
/// it should never contain us; we compare against <c>SelfUserVoice.PlatformAccountID</c> anyway and
/// skip with a loud line if it ever does. A self-voice glued to one's own head would be a feedback
/// loop nobody could diagnose from a screenshot.</item>
/// </list>
///
/// =============================================================================================
/// <para><b>THE LEVEL, AND A CORRECTION TO THE BRIEF.</b> The brief said to read the level from the
/// game's own detector, citing <c>SelfUserVoice.IsSpeaking =&gt; _recorder.VoiceDetector.Detected</c>.
/// That detector exists for the LOCAL microphone only. For a remote peer the game's flag is
/// <c>ConnectedUserVoice.IsSpeaking =&gt; _speaker.IsPlaying</c> — a BOOLEAN playback-active flag, not
/// an amplitude — and there is no per-remote level anywhere in the game or on Photon's remote
/// <c>Speaker</c>. So the request "ein Lautsprechersymbol das ausschlägt bei Ton" cannot be served
/// by the game's detector alone. The split is therefore:</para>
/// <list type="number">
/// <item><b>WHETHER someone is speaking is the game's flag, unmodified.</b> The badge is gated on
/// <c>IsSpeaking</c> — the identical expression the flat game's own roster uses to light its talk
/// icon (<c>PlayerTalkVoiceComponent.cs:20</c>). The two can therefore never disagree about who is
/// talking, which is the disagreement that would have been undebuggable.</item>
/// <item><b>HOW FAR the icon deflects is the RMS of that peer's own <c>AudioSource</c></b>
/// (<c>GetOutputData</c>), smoothed by <see cref="VoiceCurve.Smooth"/> and quantised with
/// hysteresis by <see cref="VoiceCurve.StepFor"/>. That is not a second opinion about who is
/// speaking — it is a measurement of THE EXACT AUDIO THE PLAYER IS HEARING, taken from the game's
/// own source object. It cannot contradict the game's UI, because it is never asked whether
/// somebody is talking.</item>
/// </list>
///
/// =============================================================================================
/// <para><b>NOT VERIFIED ON HARDWARE OR WITH A SECOND CLIENT.</b> Every claim above is read off
/// source or measured offline. What a desk cannot reach — that the account ids actually match in a
/// live Steam lobby, that Photon's pooled speaker behaves as the decompile says, that the curve is
/// comfortable to a human ear — is listed, item by item, in
/// <c>.planning/VOICE-SPATIAL.md</c> under "WHAT I COULD NOT VERIFY". Read that before believing
/// this file.</para>
/// </summary>
internal static class VoiceSpatial
{
    private const string Scope = VoiceChatBridge.Scope;

    /// <summary>This feature's name in <see cref="Core.HeadEar"/>'s claim set.</summary>
    private const string EarClaim = "VoiceSpatial";

    /// <summary>Relative rig-scale change that forces every source's rolloff to be recomputed.
    /// The same 0.1 % EnvSound uses, for the same reason: zoom is exactly such a change.</summary>
    private const float ScaleRefreshTolerance = 0.001f;

    /// <summary>Seconds between attempts to bind a voice user that has no avatar yet. A peer who
    /// never spawns one (a flat-screen player) would otherwise walk the roster every frame.</summary>
    private const float RebindInterval = 1.0f;

    /// <summary>Samples read per peer per frame for the level. 256 at 48 kHz is 5.3 ms of audio —
    /// long enough to be a stable RMS for speech, short enough to be free.</summary>
    private const int LevelSamples = 256;

    /// <summary>Reference RMS that maps to a full-scale icon deflection.</summary>
    private const float LevelReference = 0.35f;

    // ---- state -----------------------------------------------------------------------------

    /// <summary>What we saved before touching a peer's <c>AudioSource</c>, so stand-down can put it
    /// back exactly. A restore that guesses is a restore that silently changes the game.</summary>
    private struct Saved
    {
        public float SpatialBlend;
        public AudioRolloffMode Rolloff;
        public float MinDistance;
        public float MaxDistance;
        public float Spread;
        public float Doppler;
        public bool BypassReverb;
        public int Priority;
        public Vector3 Position;
    }

    private sealed class Bound
    {
        public ConnectedUserVoice Voice = null!;
        public int VoiceUserId;
        public string? Account;
        public string? Name;

        public GameObject? SpeakerGo;
        public AudioSource? Source;
        public Saved Original;
        public bool Applied;              // Original is valid and our settings are written

        public int PlayerId;              // 0 = not resolved to a network player yet
        public float NextRebindAt;
        public bool LoggedUnmatched;
        public bool LoggedBound;
        public bool LoggedNoSpeaker;

        public bool Speaking;
        public float Level;               // smoothed 0..1
        public int Step;                  // 0..VoiceCurve.Steps-1
        public bool Spatialised;          // the last spatialBlend we wrote was the 3D one
    }

    /// <summary>Keyed by the game's <c>ConnectedUserVoice</c> instance — see the pooling note in
    /// <see cref="VoiceChatBridge.TryGetSpeaker"/> for why the instance and not the id.</summary>
    private static readonly Dictionary<ConnectedUserVoice, Bound> Bindings = new();

    /// <summary>Speaking state by PlayerID, for <see cref="VoiceBadge"/>. Rebuilt each tick.</summary>
    private static readonly Dictionary<int, (bool Speaking, int Step)> ByPlayer = new();

    private static readonly List<ConnectedUserVoice> LiveVoices = new();
    private static readonly List<ConnectedUserVoice> Dead = new();
    private static readonly float[] LevelBuffer = new float[LevelSamples];
    private static readonly List<(int Id, string? Account, string? Name)> Roster = new();

    private static float _builtScale = -1f;
    private static float _curveFull = float.NaN;
    private static float _curveSilence = float.NaN;
    private static float _curveShape = float.NaN;
    private static AnimationCurve? _curve;
    private static bool _running;
    private static int _boundCount;

    /// <summary>How many voice users are currently spatialised onto a mask. Diagnostic.</summary>
    internal static int BoundCount => _boundCount;

    // =============================================================================================
    //  THE TICK
    // =============================================================================================

    /// <summary>
    /// One frame. Driven by <see cref="VoiceDriver"/>, guarded by its caller, and cheap when there
    /// is nothing to do: with voice chat down this is one static property read and a return.
    /// </summary>
    internal static void Tick(float dt)
    {
        if (VoiceModule.Enabled == null || !VoiceModule.Enabled.Value)
        {
            if (_running)
                StandDown("the [Voice] Enabled dial was switched off");
            return;
        }

        BoltVoiceChatService? service = VoiceChatBridge.Service();
        if (service == null)
        {
            if (_running)
                StandDown("the game's voice chat service went away (session ended, or voice was shut down)");
            return;
        }

        LiveVoices.Clear();
        if (!VoiceChatBridge.TryGetVoices(service, LiveVoices))
            return;

        if (LiveVoices.Count == 0)
        {
            if (_running)
                StandDown("no remote voice users are connected any more");
            return;
        }

        // THE EAR. Claimed only while somebody is actually in the voice room, so a solo player's
        // audio setup is never disturbed by a feature that has nothing to do.
        if (!_running)
        {
            _running = true;
            Core.HeadEar.Claim(EarClaim);
            VRLog.Info(Scope, $"VOICE SPATIAL up — {LiveVoices.Count} remote voice user(s) in the room. " +
                              "The game's voice chat is untouched (no patch, no mute, no volume write); " +
                              "this feature only moves each peer's AudioSource onto that peer's mask and " +
                              "reports who is talking to the name tag.");
        }
        else
        {
            // Cheap and idempotent: covers the rig being rebuilt under us (a recenter destroys and
            // recreates the head camera, taking the listener with it).
            Core.HeadEar.Claim(EarClaim);
        }

        string? selfAccount = VoiceChatBridge.SelfAccount(service);
        float rigScale = RigScale();
        EnsureCurve();

        if (_builtScale <= 0f || Mathf.Abs(rigScale - _builtScale) > ScaleRefreshTolerance * _builtScale)
            ApplyScale(rigScale);

        // ---- reap bindings whose voice user has gone ------------------------------------------
        Dead.Clear();
        foreach (KeyValuePair<ConnectedUserVoice, Bound> kv in Bindings)
        {
            if (!LiveVoices.Contains(kv.Key))
                Dead.Add(kv.Key);
        }
        for (int i = 0; i < Dead.Count; i++)
        {
            if (Bindings.TryGetValue(Dead[i], out Bound? gone))
            {
                Restore(gone);
                VRLog.Info(Scope, $"VOICE SPATIAL: voice user '{gone.Name ?? "?"}' (player {gone.PlayerId}) left " +
                                  "the voice room — their AudioSource is restored to the settings it had before " +
                                  "the mod touched it. The game pools and reuses these speaker objects, so this " +
                                  "restore is what stops our rolloff leaking onto the next person to join.");
            }
            Bindings.Remove(Dead[i]);
        }

        // ---- drive every live voice user ------------------------------------------------------
        float now = Time.unscaledTime;
        ByPlayer.Clear();
        _boundCount = 0;

        for (int i = 0; i < LiveVoices.Count; i++)
        {
            ConnectedUserVoice voice = LiveVoices[i];
            if (!Bindings.TryGetValue(voice, out Bound? b))
            {
                b = new Bound
                {
                    Voice = voice,
                    VoiceUserId = VoiceChatBridge.UserIdOf(voice),
                    Account = VoiceChatBridge.AccountOf(voice),
                    Name = VoiceChatBridge.NameOf(voice),
                };

                // OUR OWN VOICE MUST NEVER BE SPATIALISED ONTO OUR OWN HEAD. PlayerVoices is filled
                // only from EventAddIncomingVoiceUser so this should be unreachable; if it ever
                // fires, the log line is the whole diagnosis.
                if (!string.IsNullOrEmpty(selfAccount) && b.Account == selfAccount)
                {
                    VRLog.Warn(Scope, $"VOICE SPATIAL: the local player's OWN voice (account {b.Account}) appeared " +
                                      "in PlayerVoices, which is supposed to hold incoming users only. It is being " +
                                      "SKIPPED — spatialising it would place the player's own microphone on the " +
                                      "player's own head. Nothing is broken; this line means the assumption in " +
                                      "VoiceChatBridge.SelfAccount's doc is wrong and should be revisited.");
                    continue;
                }

                Bindings[voice] = b;
            }

            TickOne(b, now, dt, rigScale);

            if (b.PlayerId != 0)
            {
                _boundCount += b.Spatialised ? 1 : 0;
                ByPlayer[b.PlayerId] = (b.Speaking, b.Step);
            }
        }
    }

    /// <summary>One voice user: resolve, place, level. Never throws for a reason the caller can act
    /// on; a peer with nothing resolved yet simply stays 2D.</summary>
    private static void TickOne(Bound b, float now, float dt, float rigScale)
    {
        // ---- the AudioSource ------------------------------------------------------------------
        if (b.Source == null)
        {
            b.SpeakerGo = VoiceChatBridge.TryGetSpeaker(b.VoiceUserId);
            b.Source = b.SpeakerGo != null ? b.SpeakerGo.GetComponent<AudioSource>() : null;
            if (b.Source == null)
            {
                if (!b.LoggedNoSpeaker)
                {
                    b.LoggedNoSpeaker = true;
                    VRLog.Info(Scope, $"VOICE SPATIAL: voice user '{b.Name ?? "?"}' (id {b.VoiceUserId}) has no " +
                                      "speaker object yet — BoltVoiceBridge.GetSpeaker found nothing. Their voice " +
                                      "plays exactly as vanilla until it appears; we keep asking.");
                }
                return;
            }
            Save(b);
        }

        // ---- the network player, and through them the avatar ---------------------------------
        if (b.PlayerId == 0 && now >= b.NextRebindAt)
        {
            b.NextRebindAt = now + RebindInterval;
            b.PlayerId = NetPlayerActors.PlayerIdForNetworkAccount(b.Account);

            if (b.PlayerId == 0 && !b.LoggedUnmatched)
            {
                b.LoggedUnmatched = true;
                LogUnmatched(b);
            }
            else if (b.PlayerId != 0 && !b.LoggedBound)
            {
                b.LoggedBound = true;
                VRLog.Info(Scope, $"VOICE SPATIAL: voice user '{b.Name ?? "?"}' (voice id {b.VoiceUserId}, account " +
                                  $"{b.Account}) bound to network player {b.PlayerId} by the game's own join " +
                                  "(ConnectedUserVoice.PlatformAccountID == NetworkPlayer." +
                                  "PlatformNetworkAccountPlayerID — the same comparison PlayerPortraitVoiceComponent " +
                                  "uses to pick their portrait). IF THE VOICE COMES OUT OF THE WRONG MASK, THIS LINE " +
                                  "NAMES THE BINDING THAT WAS WRONG.");
            }
        }

        // ---- place it -------------------------------------------------------------------------
        bool placed = false;
        if (b.PlayerId != 0
            && NetAvatarDriver.TryGetPeerHeadHolder(b.PlayerId, out Transform holder)
            && holder != null)
        {
            // World-anchored at the mask. The AudioListener does the panning; the head pose is
            // never read here.
            b.SpeakerGo!.transform.position = holder.position;
            placed = true;
        }

        float wantBlend = placed ? Mathf.Clamp01(VoiceModule.SpatialBlend!.Value) : 0f;
        if (b.Spatialised != placed || !b.Applied)
        {
            b.Spatialised = placed;
            b.Applied = true;
            Apply(b, wantBlend, rigScale);

            VRLog.Info(Scope, placed
                ? $"VOICE SPATIAL: player {b.PlayerId}'s voice is now 3D at their mask " +
                  $"(spatialBlend {wantBlend:F2}, full level to {VoiceModule.FullLevelMeters!.Value:F1} m, " +
                  $"silent at {VoiceModule.SilenceMeters!.Value:F1} m PERCEIVED, rigScale {rigScale:F2} " +
                  "world units per perceived metre)."
                : $"VOICE SPATIAL: player {b.PlayerId} ('{b.Name ?? "?"}') has no VR avatar to speak from right " +
                  "now, so their voice is left NON-spatial (2D) — audible from everywhere, exactly as it is " +
                  "without the mod. This is the intended fallback for a flat-screen teammate and for the " +
                  "seconds before a peer's first rig packet arrives; it is never silence.");
        }
        else if (placed)
        {
            // Steady state: the position write above is the only per-frame work. spatialBlend and
            // the rolloff are change-gated, so this branch writes nothing at all.
        }

        // ---- the level ------------------------------------------------------------------------
        b.Speaking = VoiceChatBridge.IsSpeaking(b.Voice) && !VoiceChatBridge.IsMuted(b.Voice);

        float raw = 0f;
        if (b.Speaking && b.Source != null)
        {
            b.Source.GetOutputData(LevelBuffer, 0);
            double sum = 0d;
            for (int i = 0; i < LevelBuffer.Length; i++)
                sum += (double)LevelBuffer[i] * LevelBuffer[i];
            raw = Mathf.Clamp01((float)System.Math.Sqrt(sum / LevelBuffer.Length) / LevelReference);
        }

        b.Level = VoiceCurve.Smooth(b.Level, raw, dt);
        b.Step = VoiceCurve.StepFor(b.Level, b.Step, b.Speaking);
    }

    // =============================================================================================
    //  THE AUDIO SOURCE
    // =============================================================================================

    private static void Save(Bound b)
    {
        AudioSource s = b.Source!;
        b.Original = new Saved
        {
            SpatialBlend = s.spatialBlend,
            Rolloff = s.rolloffMode,
            MinDistance = s.minDistance,
            MaxDistance = s.maxDistance,
            Spread = s.spread,
            Doppler = s.dopplerLevel,
            BypassReverb = s.bypassReverbZones,
            Priority = s.priority,
            Position = b.SpeakerGo != null ? b.SpeakerGo.transform.position : Vector3.zero,
        };
        VRLog.Info(Scope, $"VOICE SPATIAL: took over the AudioSource of voice user '{b.Name ?? "?"}' " +
                          $"(id {b.VoiceUserId}). It was spatialBlend {b.Original.SpatialBlend:F2}, " +
                          $"{b.Original.Rolloff} rolloff {b.Original.MinDistance:F2}..{b.Original.MaxDistance:F2} " +
                          $"world units, spread {b.Original.Spread:F0}, doppler {b.Original.Doppler:F2}, at " +
                          $"{b.Original.Position}. Volume and playback state are NOT read and NOT written — those " +
                          "belong to the game's own per-user volume slider and mute, and they keep working.");
    }

    private static void Apply(Bound b, float blend, float rigScale)
    {
        AudioSource s = b.Source!;
        if (s == null)
            return;

        s.spatialBlend = blend;
        s.dopplerLevel = 0f;               // mandatory in a scaled world; see the class doc
        s.spread = VoiceModule.Spread!.Value;
        s.bypassReverbZones = true;        // the game's reverb zones are sized for the game's world
        // ABOVE the game's default 128 (EnvSound sits at 200, well below it). Speech is the one
        // sound in the session that must not be the voice Unity culls when the pool is full.
        s.priority = 32;

        if (blend > 0f)
        {
            s.rolloffMode = AudioRolloffMode.Custom;
            if (_curve != null)
                s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _curve);
            s.minDistance = VoiceModule.FullLevelMeters!.Value * rigScale;
            s.maxDistance = VoiceModule.SilenceMeters!.Value * rigScale;
        }
        else
        {
            // 2D: the rolloff is not consulted, but leave the numbers sane so a mid-session
            // transition to 3D does not spend a frame at whatever the prefab had.
            s.minDistance = VoiceModule.FullLevelMeters!.Value * rigScale;
            s.maxDistance = VoiceModule.SilenceMeters!.Value * rigScale;
        }
    }

    private static void Restore(Bound b)
    {
        AudioSource? s = b.Source;
        if (s == null || !b.Applied)
            return;
        s.spatialBlend = b.Original.SpatialBlend;
        s.rolloffMode = b.Original.Rolloff;
        s.minDistance = b.Original.MinDistance;
        s.maxDistance = b.Original.MaxDistance;
        s.spread = b.Original.Spread;
        s.dopplerLevel = b.Original.Doppler;
        s.bypassReverbZones = b.Original.BypassReverb;
        s.priority = b.Original.Priority;
        if (b.SpeakerGo != null)
            b.SpeakerGo.transform.position = b.Original.Position;
        b.Applied = false;
        b.Spatialised = false;
    }

    /// <summary>
    /// Turn every bound source's PERCEIVED distances into world units. The whole of the scale fix,
    /// deliberately one function so there is exactly one place where metres become world units —
    /// the shape <c>EnvSound.ApplyScale</c> established.
    /// </summary>
    private static void ApplyScale(float rigScale)
    {
        float before = _builtScale;
        _builtScale = rigScale;
        int n = 0;
        foreach (KeyValuePair<ConnectedUserVoice, Bound> kv in Bindings)
        {
            Bound b = kv.Value;
            if (b.Source == null || !b.Applied)
                continue;
            b.Source.minDistance = VoiceModule.FullLevelMeters!.Value * rigScale;
            b.Source.maxDistance = VoiceModule.SilenceMeters!.Value * rigScale;
            n++;
        }
        if (n > 0 || before > 0f)
        {
            VRLog.Info(Scope, $"VOICE SPATIAL scale: rigScale {before:F2} -> {rigScale:F2} world units per " +
                              $"perceived metre; {n} voice source(s) re-scaled. Full level out to " +
                              $"{VoiceModule.FullLevelMeters!.Value:F1} perceived m " +
                              $"(= {VoiceModule.FullLevelMeters.Value * rigScale:F1} world), silent at " +
                              $"{VoiceModule.SilenceMeters!.Value:F1} perceived m " +
                              $"(= {VoiceModule.SilenceMeters.Value * rigScale:F1} world).");
        }
    }

    /// <summary>Rebuild the shared rolloff curve when one of its three dials moves.</summary>
    private static void EnsureCurve()
    {
        float full = VoiceModule.FullLevelMeters!.Value;
        float silence = VoiceModule.SilenceMeters!.Value;
        float shape = VoiceModule.RolloffShape!.Value;
        if (_curve != null && full == _curveFull && silence == _curveSilence && shape == _curveShape)
            return;

        _curveFull = full;
        _curveSilence = silence;
        _curveShape = shape;

        var times = new float[VoiceCurve.KeyCount];
        var values = new float[VoiceCurve.KeyCount];
        VoiceCurve.SampleKeys(times, values, full, silence, shape);
        var keys = new Keyframe[VoiceCurve.KeyCount];
        for (int i = 0; i < VoiceCurve.KeyCount; i++)
            keys[i] = new Keyframe(times[i], values[i]);
        _curve = new AnimationCurve(keys);
        for (int i = 0; i < VoiceCurve.KeyCount; i++)
        {
            _curve.SmoothTangents(i, 0f);
        }

        // Force a re-apply on the next tick so live dial edits land without a restart.
        foreach (KeyValuePair<ConnectedUserVoice, Bound> kv in Bindings)
            kv.Value.Applied = false;

        VRLog.Info(Scope, $"VOICE ROLLOFF rebuilt — full level to {full:F1} perceived m, silent at {silence:F1} m, " +
                          $"shape {shape:F2}. Reads {VoiceCurve.GainDb(4f, full, silence, shape):F1} dB at 4 m, " +
                          $"{VoiceCurve.GainDb(8f, full, silence, shape):F1} dB at 8 m, " +
                          $"{VoiceCurve.GainDb(14f, full, silence, shape):F1} dB at 14 m. THESE ARE PERCEIVED " +
                          "METRES; the world-unit numbers are on the VOICE SPATIAL scale line.");
    }

    private static float RigScale()
    {
        Transform? rig = Rig.VRRigDriver.RigRoot;
        float s = rig != null ? rig.lossyScale.x : 1f;
        return !(s > 0f) || float.IsInfinity(s) ? 1f : s;
    }

    // =============================================================================================
    //  WHAT THE NAME TAG ASKS
    // =============================================================================================

    /// <summary>
    /// Whether <paramref name="playerId"/> is talking and how hard the icon should deflect (0 =
    /// silent, <see cref="VoiceCurve.Steps"/>-1 = loudest). False when that player has no voice
    /// user bound — which is also the answer for the local player, who has no name tag at all.
    /// </summary>
    internal static bool TryGetVoice(int playerId, out bool speaking, out int step)
    {
        if (ByPlayer.TryGetValue(playerId, out (bool Speaking, int Step) v))
        {
            speaking = v.Speaking;
            step = v.Step;
            return true;
        }
        speaking = false;
        step = 0;
        return false;
    }

    // =============================================================================================
    //  TEARDOWN
    // =============================================================================================

    /// <summary>
    /// Put every AudioSource back, release the ear, forget everything. Idempotent; the guard is what
    /// makes the off path free and what stops a per-frame log line.
    /// </summary>
    internal static void StandDown(string why)
    {
        if (!_running && Bindings.Count == 0)
            return;

        int n = Bindings.Count;
        foreach (KeyValuePair<ConnectedUserVoice, Bound> kv in Bindings)
            Restore(kv.Value);
        Bindings.Clear();
        ByPlayer.Clear();
        _boundCount = 0;
        _builtScale = -1f;
        _running = false;
        Core.HeadEar.Release(EarClaim);

        VRLog.Info(Scope, $"VOICE SPATIAL off — {why}. {n} voice source(s) restored to the settings they had " +
                          "before the mod touched them, and the head ear released (it survives if the environment " +
                          "ambience still wants it). The game's voice chat is unaffected and keeps playing.");
    }

    private static void LogUnmatched(Bound b)
    {
        Roster.Clear();
        NetPlayerActors.CollectRoster(Roster);
        var sb = new StringBuilder(256);
        sb.Append("VOICE SPATIAL: no network player for voice user '").Append(b.Name ?? "?")
          .Append("' (account ").Append(string.IsNullOrEmpty(b.Account) ? "<empty>" : b.Account)
          .Append(", voice id ").Append(b.VoiceUserId).Append(") — roster was [");
        for (int i = 0; i < Roster.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(Roster[i].Id).Append(": ").Append(Roster[i].Account ?? "<null>")
              .Append(" '").Append(Roster[i].Name ?? "?").Append('\'');
        }
        sb.Append("]. Their voice stays NON-SPATIAL (2D), which is exactly vanilla behaviour — it is NOT muted and "
                + "NOT quieter. The join is ConnectedUserVoice.PlatformAccountID == "
                + "NetworkPlayer.PlatformNetworkAccountPlayerID, the game's own comparison; if the roster above "
                + "shows the right person under a DIFFERENT id string, that is the bug and this line is the "
                + "evidence. An empty account or \"0\" means the peer is signed out of their platform, which the "
                + "game itself cannot map either.");
        VRLog.Warn(Scope, sb.ToString());
    }
}

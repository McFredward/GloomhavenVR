using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using VoiceChat;

namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE CHAT BRIDGE — read-only access to the voice chat the GAME ALREADY HAS. It reads; it never
//  mutes, never sets a volume, never starts or stops a stream, and it patches nothing.
// =================================================================================================

/// <summary>
/// Everything this mod needs to know about the game's Photon-backed voice chat.
///
/// <para><b>THE MOD DOES NOT IMPLEMENT VOICE CHAT AND MUST NOT.</b> Gloomhaven ships one:
/// <c>GH.Runtime/VoiceChat/</c> plus <c>PhotonVoice.dll</c> / <c>PhotonVoice.API.dll</c> in the
/// game's Managed folder, connected over the same Photon backend the game already uses, with its
/// own options page, its own per-user mute, its own ESC-menu roster and its own privilege check. The
/// VR job is to make that voice SPATIAL and to show who is talking — not to build a transport, not
/// to add a dependency and not to ask anybody for an account.</para>
///
/// =============================================================================================
/// <para><b>NO HARMONY PATCH. NOT ONE. This is a correction to the brief this work was dispatched
/// with, which named a private method as "the seam".</b> That method —
/// <c>BoltVoiceChatService.VoiceBridgeOnEventAddIncomingVoiceUser</c> (BoltVoiceChatService.cs:143)
/// — is where the game builds a <c>ConnectedUserVoice</c> from a Photon <c>Speaker</c>, and it
/// looked like the only way in. It is not. Everything the feature needs is already PUBLIC:</para>
/// <code>
///     Singleton&lt;BoltVoiceChatService&gt;.Instance          // BoltVoiceChatService.cs:14, Singleton.cs:7
///       .PlayerVoices        IReadOnlyList&lt;ConnectedUserVoice&gt;   // :28
///       .SelfUserVoice       SelfUserVoice                       // :26
///       .IsVoiceChatConnected                                    // :30
///     ConnectedUserVoice.PlatformAccountID / .VoiceChatUserId / .IsSpeaking / .Name  // :33,:37,:41,:31
///     BoltVoiceBridge.Instance                                   // BoltVoiceBridge.cs:25 (public static FIELD)
///       .GetSpeaker(int playerID, out GameObject speaker)        // :269
/// </code>
/// <para>So the whole feature is a READER. That matters for three separate reasons, each worth the
/// search on its own: the patch inventory stays at 80 classes / 132 methods; there is no question to
/// answer about whether patching a voice service counts as patching Photon Bolt (see WHERE THE LINE
/// WAS DRAWN); and a pure reader cannot break the flat game's voice UI, because it never writes
/// anything the flat game's voice UI reads.</para>
///
/// <para><b>WHERE THE LINE WAS DRAWN, and why it turned out not to matter.</b> The standing ruling
/// is: never patch <c>ScenarioRuleLibrary</c>, Photon Bolt, or <c>FFSNet.NetworkManager</c>.
/// <c>VoiceChat.BoltVoiceChatService</c> is GAME code living in <c>GH.Runtime</c> — it is not
/// <c>bolt.dll</c> — so a Harmony patch on it would have been within existing convention, the way
/// the mod already patches <c>FFSNet.ActionProcessor</c> (<c>Net/FfsNetTransport.cs</c>). The line
/// that is NOT crossed under any circumstance is <c>bolt.dll</c>, <c>PhotonVoice.dll</c> and
/// <c>FFSNet</c> internals, and this file goes near none of them: it names no Photon type, adds no
/// assembly reference, and reaches the Speaker's <c>GameObject</c> through the game's own public
/// accessor. In the end no patch was needed at all, so the question is moot in fact as well as in
/// principle.</para>
///
/// =============================================================================================
/// <para><b>WHY MOST OF THIS IS DIRECTLY TYPED AND EXACTLY ONE MEMBER IS REFLECTED — and the split
/// was MEASURED, not guessed.</b> The first version of this file reflected everything, on the
/// assumption that these types were unreachable because their neighbours drag in Bolt and Photon.
/// That assumption was tested by writing the direct calls and compiling them, and it was half
/// wrong:</para>
/// <list type="bullet">
/// <item><c>Singleton&lt;BoltVoiceChatService&gt;.Instance</c> / <c>.IsInitialized</c>,
/// <c>BoltVoiceChatService.PlayerVoices</c> / <c>.SelfUserVoice</c> / <c>.IsVoiceChatConnected</c>,
/// every <c>ConnectedUserVoice</c> member and <c>SelfUserVoice.PlatformAccountID</c>
/// <b>COMPILE</b> against the shipped <c>GH.Runtime.dll</c> with no extra reference. Its base is
/// <c>Singleton&lt;T&gt; : MonoBehaviour</c>, and its Photon-typed fields are never touched.</item>
/// <item><c>BoltVoiceBridge</c> does NOT: <c>error CS0012: the type 'GlobalEventListener' is
/// defined in an assembly that is not referenced… 'bolt.user'</c>. It derives from a Bolt type, so
/// naming it in C# would mean adding <c>bolt.user</c> to the compile line — a dependency the brief
/// rules out and one that would put the mod at the mercy of the game's Photon version.</item>
/// </list>
/// <para>So the twelve members above are now <b>verified by the compiler against the real DLL</b>
/// rather than being strings that might be wrong, and the per-frame <see cref="IsSpeaking"/> read
/// is a direct property call instead of a <c>PropertyInfo.GetValue</c>. Reflection survives for
/// <see cref="TryGetSpeaker"/> alone, which is the one door that is genuinely locked.</para>
///
/// <para><b>WHAT THE DIRECT REFERENCE COSTS, stated rather than hidden.</b> If a future patch of
/// the game removed one of those members, the mod would throw where it touches it instead of
/// degrading quietly. That is contained: every call sits under <c>VoiceDriver</c>'s
/// <c>TickGuard.Run</c>, which catches, logs once with a stack and never rethrows — so the blast
/// radius is spatial voice and nothing else. It is also the convention this mod already lives by
/// everywhere it touches <c>GH.Runtime</c> directly.</para>
/// </summary>
internal static class VoiceChatBridge
{
    internal const string Scope = "Voice";

    private static bool _init;
    private static bool _bridgeDisabled;
    private static string _why = "not resolved yet";

    // The ONE reflected door: BoltVoiceBridge derives from a Bolt type (see the class doc).
    private static FieldInfo? _bridgeInstance;   // public static BoltVoiceBridge Instance
    private static MethodInfo? _getSpeaker;      // bool GetSpeaker(int, out GameObject)

    /// <summary>Args buffer for <see cref="TryGetSpeaker"/> — one allocation, reused.</summary>
    private static readonly object?[] SpeakerArgs = new object?[2];

    /// <summary>Why the speaker lookup is unavailable, for the one log line the driver emits.</summary>
    internal static string Why => _why;

    private static void EnsureInit()
    {
        if (_init)
            return;
        _init = true;
        try
        {
            Type? bridge = AccessTools.TypeByName("VoiceChat.BoltVoiceBridge");
            _bridgeInstance = bridge == null ? null : AccessTools.Field(bridge, "Instance");
            _getSpeaker = bridge == null ? null : AccessTools.Method(bridge, "GetSpeaker");

            if (bridge == null || _bridgeInstance == null || _getSpeaker == null)
            {
                Disable($"BoltVoiceBridge reflection incomplete (type={bridge != null}, " +
                        $"Instance={_bridgeInstance != null}, GetSpeaker={_getSpeaker != null})");
                return;
            }

            _why = "available";
            VRLog.Info(Scope, "VOICE BRIDGE resolved — the game's own voice chat is reachable read-only. " +
                              "Twelve of its members are compile-checked against GH.Runtime.dll directly; only " +
                              "BoltVoiceBridge.GetSpeaker is reflected, because that type derives from Bolt and " +
                              "naming it would add bolt.user to the compile line. No Harmony patch is installed " +
                              "for this feature and none is needed — every member it uses is public.");
        }
        catch (Exception e)
        {
            Disable($"reflection threw: {e.GetType().Name}: {e.Message}");
        }
    }

    private static void Disable(string why)
    {
        _bridgeDisabled = true;
        _why = why;
        VRLog.Warn(Scope, $"VOICE BRIDGE unavailable — {why}. Spatial voice is OFF for this session; the game's " +
                          "own voice chat is untouched and behaves exactly as it does without the mod.");
    }

    /// <summary>
    /// The live <c>BoltVoiceChatService</c>, or null when voice chat is not up yet. Cheap enough to
    /// call every frame: one static property read behind an <c>IsInitialized</c> guard.
    ///
    /// <para>Deliberately re-read rather than cached. <c>BoltVoiceChatService.OnDestroy</c> never
    /// calls <c>base.OnDestroy()</c> (BoltVoiceChatService.cs:81-90), so the game's own
    /// <c>Singleton._instance</c> is never cleared and CAN outlive the object — a cached reference
    /// here would be Unity-fake-null and a stale one would be a lie. Reading through the property
    /// and null-checking with Unity's operator catches the destroyed case.</para>
    /// </summary>
    internal static BoltVoiceChatService? Service()
    {
        EnsureInit();
        if (!global::Singleton<BoltVoiceChatService>.IsInitialized)
            return null;
        BoltVoiceChatService svc = global::Singleton<BoltVoiceChatService>.Instance;
        return svc == null ? null : svc;
    }

    /// <summary>Whether the game reports an established voice connection. Diagnostic only — the
    /// driver works off the voice LIST, because that is what actually has speakers in it.</summary>
    internal static bool IsConnected(BoltVoiceChatService service) => service.IsVoiceChatConnected;

    /// <summary>
    /// Append every currently connected REMOTE voice user to <paramref name="into"/>.
    ///
    /// <para><b>POLLED, NOT EVENT-DRIVEN, AND THAT IS A DELIBERATE CHOICE AGAINST A TRAP.</b> The
    /// service publishes <c>EventNewUserConnected</c> / <c>EventUserDisconnected</c> and subscribing
    /// to them is the obvious design. It is wrong. <c>OnEventStateUpdate</c>
    /// (BoltVoiceChatService.cs:118-130) raises <c>EventUserDisconnected</c> for EVERY entry on
    /// every Bolt state update — the <c>Invoke</c> at :127 sits OUTSIDE the <c>if (!IsLinked)</c>
    /// block at :123-126 — so a peer merely connecting somewhere in the session would fire a
    /// "disconnected" for every voice including the ones still talking. The game's own UI works
    /// around this by responding with a blunt unbind-and-rebind (VoceChatOptions.cs:144-148,
    /// EscMenuVoiceChatController.cs:132-135). Reading the list IS the unbind-and-rebind, without
    /// the intermediate wrong state.</para>
    /// </summary>
    internal static bool TryGetVoices(BoltVoiceChatService service, List<ConnectedUserVoice> into)
    {
        if (into == null)
            return false;
        IReadOnlyList<ConnectedUserVoice>? list = service.PlayerVoices;
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            ConnectedUserVoice v = list[i];
            if (v != null)
                into.Add(v);
        }
        return true;
    }

    /// <summary>The platform NETWORK account id of a voice user — the join column onto
    /// <c>FFSNet.NetworkPlayer</c>. See <c>NetPlayerActors.PlayerIdForNetworkAccount</c>.</summary>
    internal static string? AccountOf(ConnectedUserVoice voice) => voice.PlatformAccountID;

    /// <summary>The Photon actor number, which is also the key of the game's speaker registry
    /// (BoltVoiceBridge.cs:197-217 keys the dictionary on the same int it passes to the event).</summary>
    internal static int UserIdOf(ConnectedUserVoice voice) => voice.VoiceChatUserId;

    /// <summary>The game's censored display name for a voice user. Diagnostic only.</summary>
    internal static string? NameOf(ConnectedUserVoice voice) => voice.Name;

    /// <summary>
    /// THE GAME'S OWN "is this person talking" flag, and the only thing the indicator is allowed to
    /// gate on.
    ///
    /// <para>For a remote user this is <c>ConnectedUserVoice.IsSpeaking =&gt; _speaker.IsPlaying</c>
    /// (ConnectedUserVoice.cs:41) — Photon's playback-active flag. It is the exact expression the
    /// flat game's own roster uses to light its talk icon
    /// (<c>PlayerTalkVoiceComponent.cs:20</c>: <c>_talkIcon.enabled = UserVoice.IsSpeaking</c>) and
    /// to fade its row (<c>EscMenuVoiceChatRow.cs:74-83</c>). Reading the same flag is what
    /// guarantees the VR badge and the flat menu can never disagree about WHO is speaking.</para>
    ///
    /// <para><b>AND IT IS A FLAG, NOT A LEVEL — a correction to the brief.</b> The brief pointed at
    /// <c>SelfUserVoice.IsSpeaking =&gt; _recorder.VoiceDetector.Detected</c>, which is a genuine
    /// voice-activity detector, and assumed the remote path had one too. It does not: self and
    /// remote implement the same interface with different semantics, and there is no per-remote
    /// amplitude anywhere in the game. Photon's <c>ILevelMeter</c> exists in
    /// <c>PhotonVoice.API.dll</c> but hangs off the LOCAL voice only and is not exposed on a remote
    /// <c>Speaker</c>. So "ein Lautsprechersymbol das ausschlägt bei Ton" cannot be driven from the
    /// game's detector alone; see <c>VoiceSpatial</c>'s THE LEVEL for what drives the deflection and
    /// why that is still not a second opinion about who is talking.</para>
    /// </summary>
    internal static bool IsSpeaking(ConnectedUserVoice voice) => voice.IsSpeaking;

    /// <summary>Whether the player has muted this peer through the game's own roster. It is read
    /// for the LEVEL and never for the badge's on/off: a muted peer who is talking still shows a
    /// badge, because whether they are speaking is a fact about their board and muting is this
    /// listener's own choice. What the mute does is hold the deflection at step 1 —
    /// <c>GetOutputData</c> on a muted source reads silence and would otherwise drive the badge to
    /// a level that contradicts the state it is showing. See <c>VoiceSpatial.cs:458-473</c>.</summary>
    internal static bool IsMuted(ConnectedUserVoice voice) => voice.IsMuted;

    /// <summary>
    /// The LOCAL player's own voice account id, used to prove a voice user is not us.
    ///
    /// <para><c>PlayerVoices</c> only ever receives INCOMING users (it is filled from
    /// <c>EventAddIncomingVoiceUser</c>), so our own voice should never appear in it and should
    /// never be spatialised onto our own mask. "Should" is not "is", and a self-voice glued to the
    /// player's own head would be a feedback loop nobody could diagnose from a screenshot — so the
    /// driver checks anyway, and logs loudly if it ever fires.</para>
    /// </summary>
    internal static string? SelfAccount(BoltVoiceChatService service)
    {
        SelfUserVoice? self = service.SelfUserVoice;
        return self?.PlatformAccountID;
    }

    /// <summary>
    /// The <c>GameObject</c> carrying the Photon <c>Speaker</c> and its <c>AudioSource</c> for
    /// <paramref name="voiceUserId"/>, or null.
    ///
    /// <para><b>THIS IS WHY NO PATCH IS NEEDED.</b> <c>BoltVoiceBridge.GetSpeaker</c>
    /// (BoltVoiceBridge.cs:269) is public and its dictionary is keyed by the same int that
    /// <c>ConnectedUserVoice.VoiceChatUserId</c> holds, so the AudioSource is reachable without
    /// touching the private <c>_audioSource</c> / <c>_speaker</c> fields and without naming a Photon
    /// type. Note the <c>out</c> parameter is a <c>GameObject</c>, not a <c>Speaker</c> — the game's
    /// own signature, which is what makes it usable from here at all.</para>
    ///
    /// <para><b>THE ONE REFLECTED CALL IN THE FEATURE</b>, because <c>BoltVoiceBridge</c> derives
    /// from Bolt's <c>GlobalEventListener</c> and naming it would add <c>bolt.user</c> to the
    /// compile line. Once per binding, not per frame.</para>
    ///
    /// <para><b>SPEAKERS ARE POOLED AND RECYCLED ACROSS USERS</b> (BoltVoiceBridge.cs:199-208
    /// re-keys the registry and flips <c>IsBusy</c>), so this must be re-asked rather than cached
    /// across users. The driver caches it per <c>ConnectedUserVoice</c> INSTANCE, which the game
    /// discards when the remote voice is removed, so the cache cannot outlive the binding.</para>
    /// </summary>
    internal static GameObject? TryGetSpeaker(int voiceUserId)
    {
        EnsureInit();
        if (_bridgeDisabled)
            return null;
        try
        {
            object? bridge = _bridgeInstance!.GetValue(null);
            if (bridge is not UnityEngine.Object bo || bo == null)
                return null;

            SpeakerArgs[0] = voiceUserId;
            SpeakerArgs[1] = null;
            bool ok = _getSpeaker!.Invoke(bridge, SpeakerArgs) is bool b && b;
            return ok ? SpeakerArgs[1] as GameObject : null;
        }
        catch { return null; }
    }
}
